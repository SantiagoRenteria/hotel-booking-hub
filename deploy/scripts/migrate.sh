#!/usr/bin/env bash
# Task 4 (Story 8.2) — Aplica las migraciones EF Core idempotentes contra la SQL de Azure por auth AAD.
#
# Los .sql se generan offline del modelo (deploy/scripts/sql/*.sql, versionados) con:
#   dotnet ef migrations script --idempotent --context <Ctx> --project <Infra.csproj> -o <out.sql>
# Aquí solo se APLICAN, sin contraseña SQL: `sqlcmd` con Azure AD (la identidad de la sesión az/OIDC, que es
# AAD admin del server, ADR-021/022). Reintenta por el cold start del auto-resume de GP_S serverless (~20-60s).
#
# Uso:  SQL_SERVER=hbh-dev-sql.database.windows.net ./migrate.sh
set -euo pipefail

SQL_SERVER="${SQL_SERVER:?Define SQL_SERVER=<server>.database.windows.net}"
AUTH="${AUTH:-ActiveDirectoryAzCli}"   # usa el token de `az` (go-sqlcmd); passwordless
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RETRIES="${RETRIES:-8}"

# db -> script
declare -A DBS=(
  [db-reservas]="$HERE/sql/reservas.sql"
  [db-hoteles]="$HERE/sql/hoteles.sql"
)

apply_with_retry() {
  local db="$1" file="$2" attempt=1
  while true; do
    echo ">> Aplicando $file -> $db (intento $attempt/$RETRIES)"
    if sqlcmd -S "$SQL_SERVER" -d "$db" \
        --authentication-method "$AUTH" \
        -b -l 60 -i "$file"; then
      echo "   OK: $db"
      return 0
    fi
    if [ "$attempt" -ge "$RETRIES" ]; then
      echo "   FALLO tras $RETRIES intentos en $db" >&2
      return 1
    fi
    # Cold start del auto-resume (error 40613 / timeout): espera con backoff y reintenta.
    local wait=$(( attempt * 10 ))
    echo "   BD posiblemente pausada/despertando; reintento en ${wait}s..."
    sleep "$wait"
    attempt=$(( attempt + 1 ))
  done
}

for db in "${!DBS[@]}"; do
  apply_with_retry "$db" "${DBS[$db]}"
done

echo ">> Migraciones aplicadas en ambas BD."
