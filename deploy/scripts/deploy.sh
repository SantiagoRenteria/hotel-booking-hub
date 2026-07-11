#!/usr/bin/env bash
# Task 6 (Story 8.2) — Orquestador del ciclo de despliegue real de bajo costo (ADR-021/022/023).
#
#   preflight -> bootstrap state -> init(backend) -> plan -> [COMPUERTA CONFIRM] apply
#             -> build/push imágenes (ACR) -> apply(con imágenes+IP) -> migraciones -> smoke
#
# COMPUERTA: `terraform apply` crea recursos FACTURABLES en Azure. Este script NO aplica sin CONFIRM=yes.
# Tras probar, ejecutar deploy/scripts/destroy.sh para no incurrir en costos (apply->probar->destroy).
#
# Requisitos: `az login` activo; Terraform + dotnet ef + sqlcmd + openssl disponibles.
# Uso:  CONFIRM=yes ./deploy/scripts/deploy.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TF="$ROOT/deploy/terraform"
SCRIPTS="$ROOT/deploy/scripts"

PREFIJO="${PREFIJO:-hbh}"; ENTORNO="${ENTORNO:-dev}"; LOCATION="${LOCATION:-eastus2}"
NOMBRE="$PREFIJO-$ENTORNO"
RG_APP="$NOMBRE-rg"
ACR="$(echo "${NOMBRE}acr" | tr -d '-')"
KV="$NOMBRE-kv"
SQL_SERVER="$NOMBRE-sql.database.windows.net"
GATEWAY_APP="$NOMBRE-gateway"

RG_STATE="${RG_STATE:-hbh-tfstate-rg}"
SA="${SA:-hbhtfstate}"
CONTAINER="${CONTAINER:-tfstate}"

say() { echo -e "\n>> $*"; }

# Auth de Terraform por Azure CLI (cero secretos). ARM_SUBSCRIPTION_ID es OBLIGATORIO en azurerm v4;
# ARM_USE_CLI=true fuerza la auth por `az` y evita el sondeo de IMDS/MSI (que cuelga varios minutos fuera de Azure).
export ARM_SUBSCRIPTION_ID="$(az account show --query id -o tsv)"
export ARM_TENANT_ID="$(az account show --query tenantId -o tsv)"
export ARM_USE_CLI="true"
export ARM_USE_MSI="false"

# 0) Preflight
say "Preflight: suscripción y providers"
az account show --query "{sub:name, id:id}" -o table
for p in Microsoft.App Microsoft.ServiceBus Microsoft.Cache Microsoft.Sql Microsoft.ContainerRegistry Microsoft.KeyVault Microsoft.OperationalInsights Microsoft.Insights; do
  az provider register --namespace "$p" --only-show-errors -o none || true
done
IP_DEPLOYER="$(curl -fsS https://api.ipify.org || echo '')"
say "IP del deployer (firewall SQL): ${IP_DEPLOYER:-<no detectada>}"

# 1) Bootstrap del state remoto (idempotente, RG-state permanente)
say "Bootstrap del state remoto"
RG_STATE="$RG_STATE" SA="$SA" LOCATION="$LOCATION" CONTAINER="$CONTAINER" bash "$TF/bootstrap/bootstrap-state.sh"

# 2) init con backend remoto
say "terraform init (backend azurerm, auth AAD)"
terraform -chdir="$TF" init -reconfigure \
  -backend-config="resource_group_name=$RG_STATE" \
  -backend-config="storage_account_name=$SA" \
  -backend-config="container_name=$CONTAINER" \
  -backend-config="key=hotel-booking-hub.tfstate"

# 3) Primer apply SIN imágenes reales (crea ACR/infra); usa placeholders para poder construir en ACR.
say "terraform plan (infra base)"
terraform -chdir="$TF" plan -var="ip_deployer=$IP_DEPLOYER" -out=tfplan

if [ "${CONFIRM:-no}" != "yes" ]; then
  cat <<EOF

*** COMPUERTA ***
El plan anterior CREA recursos facturables en Azure ($RG_APP, región $LOCATION).
Revisa el plan. Para aplicar, re-ejecuta con:  CONFIRM=yes ./deploy/scripts/deploy.sh
(Y recuerda: al terminar, ./deploy/scripts/destroy.sh)
EOF
  exit 0
fi

say "terraform apply (infra base)"
terraform -chdir="$TF" apply tfplan

# 4) Build/push de imágenes a ACR (tag = git sha) y captura de las refs
say "Construyendo imágenes en ACR ($ACR)"
IMG_VARS="$(ACR="$ACR" bash "$SCRIPTS/build-push.sh")"

# 5) apply con las imágenes reales + IP del deployer
say "terraform apply (con imágenes reales)"
# shellcheck disable=SC2086
terraform -chdir="$TF" apply -auto-approve -var="ip_deployer=$IP_DEPLOYER" $IMG_VARS

# 6) Migraciones EF (idempotentes, por AAD)
say "Aplicando migraciones EF Core"
SQL_SERVER="$SQL_SERVER" bash "$SCRIPTS/migrate.sh"

# 7) Smoke end-to-end
GATEWAY_FQDN="$(terraform -chdir="$TF" output -raw gateway_fqdn)"
say "Smoke contra https://$GATEWAY_FQDN"
GATEWAY="https://$GATEWAY_FQDN" KV="$KV" RG="$RG_APP" APP_NOTIF="$NOMBRE-notificaciones" bash "$SCRIPTS/smoke.sh"

cat <<EOF

>> Despliegue + smoke OK. Recursos VIVOS en $RG_APP (facturando).
>> Para no incurrir en costos:  ./deploy/scripts/destroy.sh
EOF
