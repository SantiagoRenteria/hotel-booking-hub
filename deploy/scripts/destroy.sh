#!/usr/bin/env bash
# Task 6 (Story 8.2) — Teardown del RG-app efímero (AC-E8.2.5). Deja limpia la suscripción; el RG-state
# permanente (tfstate) NO se toca. Estrategia apply->probar->destroy para pagar solo las horas de prueba.
#
# Uso:  ./deploy/scripts/destroy.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TF="$ROOT/deploy/terraform"
RG_STATE="${RG_STATE:-hbh-tfstate-rg}"; SA="${SA:-hbhtfstate}"; CONTAINER="${CONTAINER:-tfstate}"

# Asegura el backend inicializado (por si se corre en una sesión nueva).
terraform -chdir="$TF" init -reconfigure \
  -backend-config="resource_group_name=$RG_STATE" \
  -backend-config="storage_account_name=$SA" \
  -backend-config="container_name=$CONTAINER" \
  -backend-config="key=hotel-booking-hub.tfstate" >/dev/null

echo ">> terraform destroy del RG-app (el RG-state permanece)"
terraform -chdir="$TF" destroy -auto-approve -var="ip_deployer="

echo ">> destroy OK. Verifica que no queden recursos facturables:  az group list -o table"
