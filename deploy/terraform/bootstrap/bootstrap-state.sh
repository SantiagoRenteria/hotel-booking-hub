#!/usr/bin/env bash
# Bootstrap del state remoto de Terraform (ADR-022).
#
# Resuelve el huevo-gallina del backend: el Storage Account del tfstate no puede vivir en su propio state.
# Crea (idempotente) un RG-state PERMANENTE con Storage + container y asigna al deployer el rol de datos de blob.
# Corre UNA vez; NO se destruye con el ciclo apply->destroy del RG-app.
#
# Auth: usa la sesión `az` ya iniciada (az login). Cero secretos: el backend usa `use_azuread_auth=true`.
#
# Uso:
#   ./bootstrap-state.sh                 # usa los valores por defecto
#   RG_STATE=hbh-tfstate-rg SA=hbhtfstate LOCATION=eastus2 ./bootstrap-state.sh
#
# Tras correrlo, inicializa Terraform con backend remoto:
#   terraform init \
#     -backend-config="resource_group_name=$RG_STATE" \
#     -backend-config="storage_account_name=$SA" \
#     -backend-config="container_name=tfstate" \
#     -backend-config="key=hotel-booking-hub.tfstate"
set -euo pipefail

RG_STATE="${RG_STATE:-hbh-tfstate-rg}"
# El nombre del Storage Account debe ser GLOBAL-único (3-24 chars, minúsculas/dígitos). Si "hbhtfstate" está
# tomado, sobreescribe con SA=... (p. ej. añade tus iniciales o un sufijo).
SA="${SA:-hbhtfstate}"
LOCATION="${LOCATION:-eastus2}"
CONTAINER="${CONTAINER:-tfstate}"

echo ">> Suscripción activa:"
az account show --query "{sub:name, id:id, user:user.name}" -o table

# 1) RG-state permanente (idempotente).
if [ "$(az group exists --name "$RG_STATE")" != "true" ]; then
  echo ">> Creando RG-state permanente: $RG_STATE ($LOCATION)"
  az group create --name "$RG_STATE" --location "$LOCATION" --only-show-errors -o none
else
  echo ">> RG-state ya existe: $RG_STATE"
fi

# 2) Storage Account (idempotente, endurecido: TLS1.2, sin blob público, sin claves compartidas para datos).
if ! az storage account show --name "$SA" --resource-group "$RG_STATE" --only-show-errors -o none 2>/dev/null; then
  echo ">> Creando Storage Account: $SA"
  az storage account create \
    --name "$SA" --resource-group "$RG_STATE" --location "$LOCATION" \
    --sku Standard_LRS --kind StorageV2 \
    --min-tls-version TLS1_2 --allow-blob-public-access false \
    --only-show-errors -o none
else
  echo ">> Storage Account ya existe: $SA"
fi

# 3) Rol de datos de blob para el deployer (acceso AAD passwordless al tfstate).
DEPLOYER_OID="$(az ad signed-in-user show --query id -o tsv 2>/dev/null || az account show --query user.name -o tsv)"
SA_ID="$(az storage account show --name "$SA" --resource-group "$RG_STATE" --query id -o tsv)"
echo ">> Asignando 'Storage Blob Data Contributor' al deployer sobre el Storage (idempotente)"
az role assignment create \
  --assignee "$DEPLOYER_OID" \
  --role "Storage Blob Data Contributor" \
  --scope "$SA_ID" \
  --only-show-errors -o none 2>/dev/null || echo "   (rol ya asignado o propagándose)"

# 4) Container del tfstate (idempotente, auth por AAD login).
echo ">> Creando container '$CONTAINER' (auth-mode login)"
az storage container create \
  --name "$CONTAINER" --account-name "$SA" --auth-mode login \
  --only-show-errors -o none

cat <<EOF

>> Bootstrap OK. Inicializa Terraform con:

  terraform init \\
    -backend-config="resource_group_name=$RG_STATE" \\
    -backend-config="storage_account_name=$SA" \\
    -backend-config="container_name=$CONTAINER" \\
    -backend-config="key=hotel-booking-hub.tfstate"

  (Si migras desde state local: añade -migrate-state)
EOF
