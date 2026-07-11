#!/usr/bin/env bash
# Task 5 (Story 8.2) — Emite un JWT HS256 de prueba para el smoke, firmado con la MISMA clave que valida el
# sistema (recuperada de Key Vault en smoke.sh). Réplica de tests/TestKit.Auth/TokenDePrueba.cs:
#   claims: sub=email, email, role; issuer/audience por defecto = los de appsettings de PRODUCCIÓN.
# No hay secretos en el repo: la clave se pasa por argumento (viene de Key Vault en runtime).
#
# Uso:  ./mint-jwt.sh "<signing-key>" [rol] [issuer] [audience] [email]
set -euo pipefail

KEY="${1:?clave de firma (desde Key Vault)}"
ROLE="${2:-Agente}"
ISS="${3:-hotel-booking-hub}"
AUD="${4:-hotel-booking-hub-api}"
EMAIL="${5:-agente-smoke@ejemplo.com}"

b64url() { openssl base64 -A | tr '+/' '-_' | tr -d '='; }

now=$(date +%s)
nbf=$(( now - 60 ))
exp=$(( now + 3600 ))

header='{"alg":"HS256","typ":"JWT"}'
payload=$(printf '{"sub":"%s","email":"%s","role":"%s","iss":"%s","aud":"%s","iat":%d,"nbf":%d,"exp":%d}' \
  "$EMAIL" "$EMAIL" "$ROLE" "$ISS" "$AUD" "$now" "$nbf" "$exp")

h=$(printf '%s' "$header"  | b64url)
p=$(printf '%s' "$payload" | b64url)
sig=$(printf '%s' "$h.$p" | openssl dgst -sha256 -hmac "$KEY" -binary | b64url)

printf '%s.%s.%s\n' "$h" "$p" "$sig"
