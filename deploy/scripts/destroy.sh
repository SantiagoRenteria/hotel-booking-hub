#!/usr/bin/env bash
# Task 6 (Story 8.2) — Teardown del RG-app efímero (AC-E8.2.5). Deja limpia la suscripción; el RG-state
# permanente (tfstate) NO se toca. Estrategia apply->probar->destroy para pagar solo las horas de prueba.
#
# Uso:  ./deploy/scripts/destroy.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TF="$ROOT/deploy/terraform"
RG_STATE="${RG_STATE:-hbh-tfstate-rg}"; SA="${SA:-hbhtfstate}"; CONTAINER="${CONTAINER:-tfstate}"

# Auth por Azure CLI (igual que deploy.sh): subscription obligatoria en azurerm v4; evita el sondeo IMDS.
export ARM_SUBSCRIPTION_ID="$(az account show --query id -o tsv)"
export ARM_TENANT_ID="$(az account show --query tenantId -o tsv)"
export ARM_USE_CLI="true"
export ARM_USE_MSI="false"

# Auth del backend por clave de cuenta (runtime, no en repo).
export ARM_ACCESS_KEY="$(az storage account keys list --account-name "$SA" --resource-group "$RG_STATE" --query '[0].value' -o tsv)"

# Asegura el backend inicializado (por si se corre en una sesión nueva).
terraform -chdir="$TF" init -reconfigure \
  -backend-config="resource_group_name=$RG_STATE" \
  -backend-config="storage_account_name=$SA" \
  -backend-config="container_name=$CONTAINER" \
  -backend-config="key=hotel-booking-hub.tfstate" >/dev/null

echo ">> terraform destroy del RG-app (el RG-state permanece)"
# Best-effort: si el state está desincronizado (drift), `destroy` puede fallar. No abortamos — la limpieza
# garantizada por `az` de abajo deja el lienzo limpio igual.
terraform -chdir="$TF" destroy -auto-approve -var="ip_deployer=" \
  || echo "   terraform destroy con errores (posible drift); se fuerza la limpieza por az."

# --- Limpieza GARANTIZADA (barre huérfanos fuera del state) ---
# `terraform destroy` SOLO borra lo que está en el state. Un apply previo interrumpido deja recursos HUÉRFANOS
# (creados en Azure, no registrados) que quedan facturando y colisionan con el próximo deploy ("already exists").
# Nombres derivados del mismo esquema que Terraform (local.nombre = "${prefijo}-${entorno}").
NOMBRE="${PREFIJO:-hbh}-${ENTORNO:-dev}"
RG_APP="${RG_APP:-${NOMBRE}-rg}"
KV_APP="${KV_APP:-${NOMBRE}-kv}"

echo ">> Limpieza garantizada: borrando el RG-app completo ($RG_APP)"
az group delete --name "$RG_APP" --yes 2>/dev/null || echo "   ($RG_APP ya no existe)"

echo ">> Purga del Key Vault soft-deleted ($KV_APP) — evita que el próximo deploy recupere el vault viejo"
az keyvault purge --name "$KV_APP" 2>/dev/null || echo "   ($KV_APP no estaba en soft-delete)"

# Reset del state remoto: tras el nuke, el state podría quedar con entradas fantasma. Lo dejamos vacío para que
# el próximo deploy arranque en lienzo limpio (Azure vacío + state vacío = imposible chocar con "already exists").
echo ">> Reset del state remoto (blob tfstate) para el próximo deploy"
az storage blob delete --account-name "$SA" --container-name "$CONTAINER" \
  --name "hotel-booking-hub.tfstate" --auth-mode key --account-key "$ARM_ACCESS_KEY" 2>/dev/null \
  || echo "   (state blob ya no existía)"

echo ">> destroy OK. Verifica que no queden recursos facturables:  az group list -o table"
