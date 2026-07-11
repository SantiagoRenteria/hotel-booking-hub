#!/usr/bin/env bash
# Task 5 (Story 8.2) — Smoke end-to-end contra el despliegue real (AC-E8.2.4).
#   1) GET /health con retry (cold start de ACA scale-from-zero + auto-resume de SQL).
#   2) Mint de JWT (rol Agente) con la clave de Key Vault.
#   3) Flujo de negocio real por el Gateway: crear hotel -> crear habitación -> crear reserva -> cancelar (atajo).
#   4) Verificar la propagación del evento al worker de Notificaciones (logs de la Container App).
#
# Uso:  GATEWAY=https://<gateway-fqdn> KV=<keyvault> RG=<rg-app> ./smoke.sh
set -euo pipefail

GATEWAY="${GATEWAY:?Define GATEWAY=https://<gateway-fqdn>}"
KV="${KV:?Define KV=<nombre-del-key-vault>}"
RG="${RG:?Define RG=<resource-group-app>}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_NOTIF="${APP_NOTIF:-hbh-dev-notificaciones}"

say() { echo ">> $*"; }
req() { # metodo ruta [json]  -> imprime el body en 2xx; reintenta el cold start (scale-to-zero) de servicios internos
  local m="$1" path="$2" body="${3:-}" attempt=1 out code resp
  local args=(-sS -X "$m" "$GATEWAY$path" -H "Authorization: Bearer $TOKEN" -w $'\n%{http_code}')
  [ -n "$body" ] && args+=(-H "Content-Type: application/json" -d "$body")
  while :; do
    out="$(curl "${args[@]}" 2>/dev/null)"
    code="${out##*$'\n'}"
    resp="${out%$'\n'*}"
    if [ "$code" -ge 200 ] && [ "$code" -lt 300 ]; then
      printf '%s' "$resp"
      return 0
    fi
    if [ "$attempt" -ge 8 ]; then
      echo "   $m $path -> HTTP $code tras $attempt intentos: $resp" >&2
      return 1
    fi
    echo "   $m $path -> HTTP $code (cold start de servicio interno); reintento $attempt/8 en 15s..." >&2
    sleep 15
    attempt=$((attempt + 1))
  done
}

# 1) Health con retry (hasta ~2 min por cold start).
say "Esperando /health en $GATEWAY ..."
for i in $(seq 1 24); do
  if curl -fsS "$GATEWAY/health" -o /dev/null; then say "health OK (intento $i)"; break; fi
  [ "$i" -eq 24 ] && { echo "health NO respondió 200" >&2; exit 1; }
  sleep 5
done

# 2) Token de prueba (clave desde Key Vault, cero secretos en repo).
say "Recuperando clave de firma de Key Vault y emitiendo JWT (Agente)"
KEY="$(az keyvault secret show --vault-name "$KV" --name jwt-signing-key --query value -o tsv)"
TOKEN="$(bash "$HERE/mint-jwt.sh" "$KEY" Agente)"

# 3) Flujo de negocio.
say "Crear hotel"
# estado como entero (EstadoHotel.Habilitado=1): la entrada del API bindea el enum numérico (sin JsonStringEnumConverter).
HOTEL=$(req POST /api/v1/hoteles '{"nombre":"Hotel Smoke","ciudad":"Bogota","direccion":"Cll 1 #2-3","descripcion":"smoke","estado":1}')
HOTEL_ID=$(printf '%s' "$HOTEL" | sed -nE 's/.*"id"[: ]*"([0-9a-fA-F-]+)".*/\1/p' | head -1)
say "hotelId=$HOTEL_ID"

say "Crear habitación"
HAB=$(req POST "/api/v1/hoteles/$HOTEL_ID/habitaciones" '{"tipo":"Doble","costoBase":100.0,"impuestos":19.0,"ubicacion":"Piso 2","estado":1,"capacidad":2}')
HAB_ID=$(printf '%s' "$HAB" | sed -nE 's/.*"id"[: ]*"([0-9a-fA-F-]+)".*/\1/p' | head -1)
say "habitacionId=$HAB_ID"

say "Crear reserva"
# Cuerpo en UNA sola línea: `curl -d` elimina los saltos de línea y un JSON multilínea acababa corrupto → 400.
RESERVA=$(req POST /api/v1/reservas "{\"habitacionId\":\"$HAB_ID\",\"entrada\":\"2026-08-01\",\"salida\":\"2026-08-03\",\"huespedes\":[{\"nombres\":\"Ana\",\"apellidos\":\"Lopez\",\"fechaNacimiento\":\"1990-01-01\",\"genero\":\"F\",\"tipoDocumento\":\"CC\",\"numeroDocumento\":\"CC1234567\",\"email\":\"ana@x.com\",\"telefono\":\"3001234567\"}],\"contactoEmergencia\":{\"nombreCompleto\":\"Juan\",\"telefono\":\"3007654321\"},\"agenteEmail\":\"agente-smoke@ejemplo.com\"}")
RESERVA_ID=$(printf '%s' "$RESERVA" | sed -nE 's/.*"id"[: ]*"([0-9a-fA-F-]+)".*/\1/p' | head -1)
say "reservaId=$RESERVA_ID (evento ReservaConfirmada debería propagarse al worker)"

say "Cancelar (atajo de un paso)"
# iniciador=2 (Agente), decision=1 (AprobarAplicandoPenalidad); enums numéricos.
req POST "/api/v1/reservas/$RESERVA_ID/cancelaciones/atajo" '{"categoriaMotivo":"Cliente","detalleMotivo":"smoke","iniciador":2,"decision":1}' >/dev/null
say "cancelación OK"

# 4) Verificar propagación del evento (el worker consume del Service Bus y notifica).
say "Revisando logs del worker de Notificaciones ($APP_NOTIF) por evidencia del evento"
az containerapp logs show -n "$APP_NOTIF" -g "$RG" --tail 100 --type console 2>/dev/null \
  | grep -iE "reserva|notific|cancel" | tail -20 || say "(no se pudieron leer logs; revisar en App Insights)"

say "SMOKE OK — /health 200 + flujo de negocio 2xx. Verifica la evidencia del evento arriba."
