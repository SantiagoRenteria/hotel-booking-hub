#!/usr/bin/env bash
# Task 3 (Story 8.2) — Construye y sube las 4 imágenes a ACR con `az acr build` (ADR-021).
#
# `az acr build` construye en el propio ACR (no necesita Docker daemon local ni `docker login`; usa la sesión az
# y respeta admin_enabled=false). Tag = git sha corto. Emite en stdout las refs de imagen para pasarlas a
# Terraform como -var imagen_*=... (ver deploy.sh).
#
# Requiere: el ACR ya creado (terraform apply lo provisiona) y rol AcrPush para el deployer sobre el ACR.
#
# Uso:  ACR=hbhdevacr ./build-push.sh
set -euo pipefail

ACR="${ACR:?Define ACR=<nombre-del-registry> (sin .azurecr.io)}"
TAG="${TAG:-$(git rev-parse --short HEAD)}"
DOCKERFILE="${DOCKERFILE:-Dockerfile}"
CONTEXT="${CONTEXT:-.}"

# El Dockerfile multi-stage se parametriza por servicio con PROJECT_PATH + APP_DLL (igual que deploy/docker-compose.yml).
# repo -> "PROJECT_PATH|APP_DLL"
declare -A APPS=(
  [gateway]="src/ApiGateway/ApiGateway.csproj|ApiGateway.dll"
  [hoteles]="src/Servicios/Hoteles/Hoteles.Api/Hoteles.Api.csproj|Hoteles.Api.dll"
  [reservas]="src/Servicios/Reservas/Reservas.Api/Reservas.Api.csproj|Reservas.Api.dll"
  [notificaciones]="src/Servicios/Notificaciones/Notificaciones.Worker/Notificaciones.Worker.csproj|Notificaciones.Worker.dll"
)

for repo in "${!APPS[@]}"; do
  IFS='|' read -r project_path app_dll <<<"${APPS[$repo]}"
  echo ">> az acr build: $repo:$TAG ($project_path)" >&2
  az acr build \
    --registry "$ACR" \
    --image "hotel-booking-hub/$repo:$TAG" \
    --file "$DOCKERFILE" \
    --build-arg "PROJECT_PATH=$project_path" \
    --build-arg "APP_DLL=$app_dll" \
    "$CONTEXT" >&2
done

LOGIN_SERVER="$(az acr show --name "$ACR" --query loginServer -o tsv)"
echo ">> Imágenes construidas. Refs para Terraform:" >&2
echo "-var imagen_gateway=$LOGIN_SERVER/hotel-booking-hub/gateway:$TAG"
echo "-var imagen_hoteles=$LOGIN_SERVER/hotel-booking-hub/hoteles:$TAG"
echo "-var imagen_reservas=$LOGIN_SERVER/hotel-booking-hub/reservas:$TAG"
echo "-var imagen_notificaciones=$LOGIN_SERVER/hotel-booking-hub/notificaciones:$TAG"
