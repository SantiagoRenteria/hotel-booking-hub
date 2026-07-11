#!/usr/bin/env bash
# Task 4 (Story 8.2) — Aplica las migraciones EF Core idempotentes contra la SQL de Azure.
#
# Los .sql se generan offline del modelo (deploy/scripts/sql/*.sql, versionados). Aquí solo se APLICAN con
# auth SQL (usuario admin + contraseña recuperada de Key Vault; la contraseña la generó random_password, nunca
# vive en el repo). Se usa el `sqlcmd` clásico (ODBC) con -U/-P, compatible sin go-sqlcmd. Reintenta por el
# cold start del auto-resume de GP_S serverless (~20-60s, error 40613/timeout).
#
# Uso:  SQL_SERVER=hbh-dev-sql.database.windows.net KV=hbh-dev-kv ./migrate.sh
set -euo pipefail

SQL_SERVER="${SQL_SERVER:?Define SQL_SERVER=<server>.database.windows.net}"
KV="${KV:?Define KV=<nombre-del-key-vault>}"
SQL_ADMIN="${SQL_ADMIN:-hbhadmin}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RETRIES="${RETRIES:-8}"

echo ">> Recuperando contraseña SQL de Key Vault ($KV)"
PW="$(az keyvault secret show --vault-name "$KV" --name sql-admin-password --query value -o tsv)"

# db -> script
declare -A DBS=(
  [db-reservas]="$HERE/sql/reservas.sql"
  [db-hoteles]="$HERE/sql/hoteles.sql"
)

apply_with_retry() {
  local db="$1" file="$2" attempt=1
  # El sqlcmd clásico (Windows/ODBC) requiere ruta de Windows (C:\...), no la MSYS de Git Bash (/c/...).
  local winfile="$file"
  command -v cygpath >/dev/null 2>&1 && winfile="$(cygpath -w "$file")"
  while true; do
    echo ">> Aplicando $winfile -> $db (intento $attempt/$RETRIES)"
    if sqlcmd -S "$SQL_SERVER" -d "$db" -U "$SQL_ADMIN" -P "$PW" -N -C -b -l 60 -i "$winfile"; then
      echo "   OK: $db"
      return 0
    fi
    if [ "$attempt" -ge "$RETRIES" ]; then
      echo "   FALLO tras $RETRIES intentos en $db" >&2
      return 1
    fi
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
