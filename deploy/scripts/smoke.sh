#!/usr/bin/env bash
# Smoke end-to-end por el Gateway (Story 8.2 AC-E8.2.4, ampliado en T.2 AC-ET.2.2).
# Ejerce todos los endpoints de negocio del Gateway, con ambos roles y casos negativos:
#   1) GET /health + /alive con retry (cold start de ACA scale-from-zero + auto-resume de SQL).
#   2) Mint de JWT para Agente y Viajero (clave de Key Vault en nube, o JWT_SIGNING_KEY en local).
#   3) Catálogo (Agente): crear hotel + habitación, y su ciclo de vida (editar/deshabilitar/habilitar
#      hotel y habitación; eliminar un hotel desechable -> 204).
#   4) Reserva (Agente): crear -> cancelar (atajo).
#   5) Lecturas: disponibilidad (Viajero+Agente), listado (con la reserva creada) y detalle.
#   6) Idempotencia: replay del Idempotency-Key -> 200 con la MISMA reserva.
#   7) Cancelación en DOS pasos: solicitud -> cancelaciones-pendientes -> resolución.
#   8) Negativos: 401 (sin token), 403 (Viajero en SoloAgente), 404 (reserva inexistente),
#      409 (editar hotel con rowVersion stale).
#   9) Verificar la propagación del evento al worker de Notificaciones (logs: az en nube / docker en local).
#
# Uso (nube):   GATEWAY=https://<fqdn> KV=<keyvault> RG=<rg-app> ./smoke.sh
# Uso (local):  GATEWAY=http://localhost:8080 JWT_SIGNING_KEY=<clave> ./smoke.sh
set -euo pipefail

GATEWAY="${GATEWAY:?Define GATEWAY=https://<gateway-fqdn> (o http://localhost:8080 en local)}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_NOTIF="${APP_NOTIF:-hbh-dev-notificaciones}"
RG="${RG:-}"
KV="${KV:-}"

say() { echo ">> $*"; }
die() { echo "   FALLO: $*" >&2; exit 1; }
id_of() { printf '%s' "$1" | sed -nE 's/.*"id"[: ]*"([0-9a-fA-F-]+)".*/\1/p' | head -1; }
# rowVersion es base64 (puede llevar +/=): captura cualquier carácter que no sea comilla.
rv_of() { printf '%s' "$1" | sed -nE 's/.*"rowVersion"[: ]*"([^"]+)".*/\1/p' | head -1; }

# Cuerpo de reserva parametrizado por habitación + fechas (una sola línea: `curl -d` elimina saltos → 400).
reserva_body() {
  printf '{"habitacionId":"%s","entrada":"%s","salida":"%s","huespedes":[{"nombres":"Ana","apellidos":"Lopez","fechaNacimiento":"1990-01-01","genero":"F","tipoDocumento":"CC","numeroDocumento":"CC1234567","email":"ana@x.com","telefono":"3001234567"}],"contactoEmergencia":{"nombreCompleto":"Juan","telefono":"3007654321"},"agenteEmail":"agente-smoke@ejemplo.com"}' "$1" "$2" "$3"
}

# _curl <token> <metodo> <path> <body> <header-extra> -> imprime "code\nbody"; reintenta SOLO transitorios
# (000/408/502/503/504 = cold start de servicio interno tras el gateway). Los 4xx reales NO se reintentan.
_curl() {
  local tok="$1" m="$2" path="$3" body="$4" xh="$5" attempt=1 out code resp args
  while :; do
    args=(-sS -X "$m" "$GATEWAY$path" -w $'\n%{http_code}')
    [ -n "$tok" ] && args+=(-H "Authorization: Bearer $tok")
    [ -n "$xh" ] && args+=(-H "$xh")
    [ -n "$body" ] && args+=(-H "Content-Type: application/json" -d "$body")
    out="$(curl "${args[@]}" 2>/dev/null || printf '\n000')"
    code="${out##*$'\n'}"
    resp="${out%$'\n'*}"
    case "$code" in
      000 | 408 | 502 | 503 | 504)
        [ "$attempt" -ge 8 ] && break
        echo "   $m $path -> HTTP $code (cold start); reintento $attempt/8 en 15s..." >&2
        sleep 15
        attempt=$((attempt + 1))
        ;;
      *) break ;;
    esac
  done
  printf '%s\n%s' "$code" "$resp"
}

tok_de() { case "$1" in A) printf '%s' "$TOKEN" ;; V) printf '%s' "$TOKEN_VIAJERO" ;; -) printf '' ;; esac; }

# ok <rol> <metodo> <path> [body] [header] -> exige 2xx; imprime el body. (rol: A=Agente, V=Viajero)
ok() {
  local r code body
  r="$(_curl "$(tok_de "$1")" "$2" "$3" "${4:-}" "${5:-}")"
  code="${r%%$'\n'*}"
  body="${r#*$'\n'}"
  { [ "$code" -ge 200 ] && [ "$code" -lt 300 ]; } || die "$2 $3 -> HTTP $code: $body"
  printf '%s' "$body"
}

# expect <code> <rol> <metodo> <path> [body] [header] -> exige código EXACTO
expect() {
  local want="$1" r code
  r="$(_curl "$(tok_de "$2")" "$3" "$4" "${5:-}" "${6:-}")"
  code="${r%%$'\n'*}"
  [ "$code" = "$want" ] || die "$3 $4 -> HTTP $code (esperaba $want)"
  say "negativo OK: $3 $4 -> $code (esperado)"
}

# ---- 1) Health con retry (hasta ~2 min por cold start) ----
say "Esperando /health en $GATEWAY ..."
for i in $(seq 1 24); do
  if curl -fsS "$GATEWAY/health" -o /dev/null; then
    say "health OK (intento $i)"
    break
  fi
  [ "$i" -eq 24 ] && die "health NO respondió 200"
  sleep 5
done
curl -fsS "$GATEWAY/alive" -o /dev/null && say "alive OK" || say "(alive no disponible; continúo)"

# ---- 2) Tokens (Agente + Viajero). Clave: JWT_SIGNING_KEY en local, o Key Vault en nube ----
if [ -n "${JWT_SIGNING_KEY:-}" ]; then
  say "Usando JWT_SIGNING_KEY del entorno (modo local)"
  KEY="$JWT_SIGNING_KEY"
else
  [ -n "$KV" ] || die "Define KV=<key-vault> (nube) o JWT_SIGNING_KEY=<clave> (local)"
  say "Recuperando clave de firma de Key Vault ($KV)"
  KEY="$(az keyvault secret show --vault-name "$KV" --name jwt-signing-key --query value -o tsv)"
fi
TOKEN="$(bash "$HERE/mint-jwt.sh" "$KEY" Agente hotel-booking-hub hotel-booking-hub-api agente-smoke@ejemplo.com)"
TOKEN_VIAJERO="$(bash "$HERE/mint-jwt.sh" "$KEY" Viajero hotel-booking-hub hotel-booking-hub-api viajero-smoke@ejemplo.com)"

# ---- 3) Catálogo: crear + ciclo de vida (Agente) ----
say "Crear hotel"
HOTEL=$(ok A POST /api/v1/hoteles '{"nombre":"Hotel Smoke","ciudad":"Bogota","direccion":"Cll 1 #2-3","descripcion":"smoke","estado":1}')
HOTEL_ID=$(id_of "$HOTEL")
HOTEL_RV=$(rv_of "$HOTEL")
HOTEL_RV0="$HOTEL_RV" # rowVersion original: quedará stale tras la 1ra edición (para el negativo 409)
say "hotelId=$HOTEL_ID"

say "Crear habitación"
HAB=$(ok A POST "/api/v1/hoteles/$HOTEL_ID/habitaciones" '{"tipo":"Doble","costoBase":100.0,"impuestos":19.0,"ubicacion":"Piso 2","estado":1,"capacidad":2}')
HAB_ID=$(id_of "$HAB")
HAB_RV=$(rv_of "$HAB")
say "habitacionId=$HAB_ID"

say "Ciclo de vida del hotel: editar -> deshabilitar -> habilitar"
R=$(ok A PUT "/api/v1/hoteles/$HOTEL_ID" "{\"rowVersion\":\"$HOTEL_RV\",\"nombre\":\"Hotel Smoke (editado)\",\"ciudad\":\"Bogota\",\"direccion\":\"Cll 9\",\"descripcion\":\"editado\"}"); HOTEL_RV=$(rv_of "$R")
R=$(ok A POST "/api/v1/hoteles/$HOTEL_ID/deshabilitar" "{\"rowVersion\":\"$HOTEL_RV\"}"); HOTEL_RV=$(rv_of "$R")
R=$(ok A POST "/api/v1/hoteles/$HOTEL_ID/habilitar" "{\"rowVersion\":\"$HOTEL_RV\"}"); HOTEL_RV=$(rv_of "$R")
say "hotel lifecycle OK"

say "Ciclo de vida de la habitación: editar -> deshabilitar -> habilitar"
R=$(ok A PUT "/api/v1/habitaciones/$HAB_ID" "{\"rowVersion\":\"$HAB_RV\",\"tipo\":\"Suite\",\"costoBase\":150.0,\"impuestos\":28.5,\"ubicacion\":\"Piso 3\",\"capacidad\":3}"); HAB_RV=$(rv_of "$R")
R=$(ok A POST "/api/v1/habitaciones/$HAB_ID/deshabilitar" "{\"rowVersion\":\"$HAB_RV\"}"); HAB_RV=$(rv_of "$R")
R=$(ok A POST "/api/v1/habitaciones/$HAB_ID/habilitar" "{\"rowVersion\":\"$HAB_RV\"}"); HAB_RV=$(rv_of "$R")
say "habitacion lifecycle OK"

say "Eliminar (baja lógica) un hotel desechable -> 204"
DES=$(ok A POST /api/v1/hoteles '{"nombre":"Desechable","ciudad":"Cali","direccion":"Cll 0","descripcion":"del","estado":1}')
DES_ID=$(id_of "$DES"); DES_RV=$(rv_of "$DES")
expect 204 A DELETE "/api/v1/hoteles/$DES_ID" "{\"rowVersion\":\"$DES_RV\"}"

# ---- 4) Reserva (Agente) ----
say "Crear reserva (fechas ago) + cancelar por atajo"
RESERVA=$(ok A POST /api/v1/reservas "$(reserva_body "$HAB_ID" 2026-08-01 2026-08-03)")
RESERVA_ID=$(id_of "$RESERVA")
say "reservaId=$RESERVA_ID (evento ReservaConfirmada debería propagarse al worker)"
ok A POST "/api/v1/reservas/$RESERVA_ID/cancelaciones/atajo" '{"categoriaMotivo":"Cliente","detalleMotivo":"smoke","iniciador":2,"decision":1}' >/dev/null
say "cancelación (atajo) OK"

# ---- 5) Lecturas ----
say "Buscar disponibilidad (Viajero) — ejerce la ruta específica del Gateway hacia Reservas"
ok V GET "/api/v1/habitaciones/disponibles?ciudad=Bogota&entrada=2026-08-01&salida=2026-08-03&huespedes=2" >/dev/null
say "disponibilidad OK (200; el contenido depende de la proyección async, no se asevera aquí)"
LISTA=$(ok A GET /api/v1/reservas)
printf '%s' "$LISTA" | grep -q "$RESERVA_ID" \
  && say "listado de reservas OK (contiene la reserva creada)" \
  || die "la reserva $RESERVA_ID no aparece en el listado del agente"
ok A GET "/api/v1/reservas/$RESERVA_ID" >/dev/null && say "detalle de reserva OK"

# ---- 6) Idempotencia (replay del Idempotency-Key -> 200 misma reserva) ----
say "Idempotencia: crear reserva con Idempotency-Key y reintentar la misma clave"
IDK="smoke-$HAB_ID-sep"
BODY_IDEM="$(reserva_body "$HAB_ID" 2026-09-01 2026-09-03)"
R1=$(ok A POST /api/v1/reservas "$BODY_IDEM" "Idempotency-Key: $IDK")
R2=$(ok A POST /api/v1/reservas "$BODY_IDEM" "Idempotency-Key: $IDK")
ID1=$(id_of "$R1")
ID2=$(id_of "$R2")
[ -n "$ID1" ] && [ "$ID1" = "$ID2" ] || die "idempotencia rota: primera=$ID1 replay=$ID2"
say "idempotencia OK (misma reserva $ID1 en el replay)"

# ---- 7) Cancelación en DOS pasos: solicitud -> pendientes -> resolución ----
say "Cancelación en dos pasos (reserva nueva, fechas oct)"
RESERVA3=$(ok A POST /api/v1/reservas "$(reserva_body "$HAB_ID" 2026-10-01 2026-10-03)")
RESERVA3_ID=$(id_of "$RESERVA3")
ok A POST "/api/v1/reservas/$RESERVA3_ID/solicitud-cancelacion" '{"categoriaMotivo":"Cliente","detalleMotivo":"smoke 2 pasos","iniciador":2}' >/dev/null
say "solicitud de cancelación OK (reserva $RESERVA3_ID)"
ok A GET /api/v1/reservas/cancelaciones-pendientes >/dev/null && say "cancelaciones-pendientes OK"
ok A POST "/api/v1/reservas/$RESERVA3_ID/cancelacion/resolucion" '{"decision":1}' >/dev/null
say "resolución de cancelación OK"

# ---- 8) Negativos ----
say "Casos negativos"
expect 401 - GET /api/v1/reservas
expect 403 V POST /api/v1/hoteles '{"nombre":"X","ciudad":"Bogota","direccion":"Cll 1","descripcion":"x","estado":1}'
expect 404 A GET /api/v1/reservas/00000000-0000-0000-0000-000000000000
# 409: editar el hotel con el rowVersion original (stale tras las mutaciones de arriba).
expect 409 A PUT "/api/v1/hoteles/$HOTEL_ID" "{\"rowVersion\":\"$HOTEL_RV0\",\"nombre\":\"conflicto\",\"ciudad\":\"Bogota\",\"direccion\":\"Cll 9\",\"descripcion\":\"stale\"}"

# ---- 9) Verificar propagación del evento (worker consume del broker y notifica) ----
if [ -n "$RG" ] && command -v az >/dev/null 2>&1; then
  say "Revisando logs del worker de Notificaciones ($APP_NOTIF) por evidencia del evento (Azure)"
  az containerapp logs show -n "$APP_NOTIF" -g "$RG" --tail 100 --type console 2>/dev/null \
    | grep -iE "reserva|notific|cancel" | tail -20 || say "(no se pudieron leer logs; revisar en App Insights)"
else
  COMPOSE="${COMPOSE_FILE:-$HERE/../docker-compose.yml}"
  if command -v docker >/dev/null 2>&1 && [ -f "$COMPOSE" ]; then
    say "Revisando logs del worker de Notificaciones por evidencia del evento (docker compose)"
    if docker compose -f "$COMPOSE" logs --tail 200 notificaciones 2>/dev/null | grep -iE "reserva|notific|cancel" | tail -20; then
      say "evidencia del evento encontrada en los logs del worker"
    else
      say "(sin coincidencias todavía; el worker puede tardar en consumir del broker)"
    fi
  else
    say "(no se pudieron leer logs del worker; revisar manualmente)"
  fi
fi

say "SMOKE OK — health/alive + catálogo (CRUD+ciclo de vida) + reserva + lecturas + idempotencia + cancelación en 2 pasos + negativos (401/403/404/409)."
