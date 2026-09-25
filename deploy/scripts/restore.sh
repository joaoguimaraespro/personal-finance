#!/usr/bin/env bash
# Restores a backup produced by backup.sh into the running stack. DESTRUCTIVE: replaces all current data.
#
#   deploy/scripts/restore.sh <backup.tar.age> <age-identity-file>
#
# The identity (private key) file is brought in from offline storage only for the duration of the restore.
set -euo pipefail
umask 077
trap 'echo "RESTORE FAILED (line ${LINENO})." >&2' ERR

archive="${1:?usage: restore.sh <backup.tar.age> <age-identity-file>}"
identity="${2:?usage: restore.sh <backup.tar.age> <age-identity-file>}"
DEPLOY_DIR="$(cd "$(dirname "$0")/.." && pwd)"
set -a; source "${DEPLOY_DIR}/.env"; set +a
command -v age >/dev/null || { echo "age is not installed (apt install age)" >&2; exit 1; }

compose() { docker compose --project-directory "${DEPLOY_DIR}" -f "${DEPLOY_DIR}/compose.yml" --env-file "${DEPLOY_DIR}/.env" "$@"; }

work="$(mktemp -d)"
trap 'rm -rf "${work}"' EXIT
age -d -i "${identity}" "${archive}" | tar -C "${work}" -xf -

echo "Verifying checksums…"
grep -q "dump_sha256=$(sha256sum "${work}/finance.dump" | cut -d' ' -f1)" "${work}/manifest.txt"
grep -q "keys_sha256=$(sha256sum "${work}/dp-keys.tar" | cut -d' ' -f1)" "${work}/manifest.txt"
cat "${work}/manifest.txt"

read -r -p "This will REPLACE all data in the running stack. Type 'restore' to continue: " answer
[ "${answer}" = "restore" ] || { echo "Aborted."; exit 1; }

compose stop api web
compose up -d db
compose exec -T db sh -c 'until pg_isready -U postgres -d finance; do sleep 1; done'

# Restore as the schema owner so the objects keep the least-privilege layout.
compose exec -T -e PGPASSWORD="${FINANCE_MIGRATOR_PASSWORD}" db \
  pg_restore --clean --if-exists --no-owner --role=finance_migrator -U finance_migrator -d finance < "${work}/finance.dump"

# The dump carries data and structure; privileges are always re-applied from the versioned layout.
compose exec -T db psql -v ON_ERROR_STOP=1 -q -U postgres -d finance -f /docker-entrypoint-initdb.d/20-grants.sql

compose run --rm --no-deps -T --user 1654:1654 --entrypoint "" -v personal-finance_dp-keys:/keys web \
  sh -c 'rm -rf /keys/* && tar -C /keys -xf -' < "${work}/dp-keys.tar"

compose up -d
echo "Restore complete. Sign in and verify the latest transactions."
