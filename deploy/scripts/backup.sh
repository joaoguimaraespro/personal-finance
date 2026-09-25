#!/usr/bin/env bash
# Encrypted backup of the database and the Data Protection key ring.
#
#   deploy/scripts/backup.sh            # uses deploy/.env
#
# Output: $BACKUP_DIR/finance-<UTC timestamp>.tar.age — encrypted to BACKUP_AGE_RECIPIENT (an age public key).
# The matching private key must NOT be stored on this server; without it the backups are unreadable.
set -euo pipefail
umask 077
trap 'echo "BACKUP FAILED (line ${LINENO}). No backup was written." >&2' ERR

DEPLOY_DIR="$(cd "$(dirname "$0")/.." && pwd)"
set -a; source "${DEPLOY_DIR}/.env"; set +a
: "${BACKUP_AGE_RECIPIENT:?BACKUP_AGE_RECIPIENT (age public key) is required}"
: "${BACKUP_DIR:?BACKUP_DIR is required}"
RETENTION_DAYS="${BACKUP_RETENTION_DAYS:-35}"
command -v age >/dev/null || { echo "age is not installed (apt install age)" >&2; exit 1; }

compose() { docker compose --project-directory "${DEPLOY_DIR}" -f "${DEPLOY_DIR}/compose.yml" --env-file "${DEPLOY_DIR}/.env" "$@"; }

stamp="$(date -u +%Y%m%dT%H%M%SZ)"
work="$(mktemp -d)"
trap 'rm -rf "${work}"' EXIT
mkdir -p "${BACKUP_DIR}"

# Consistent logical dump with the read-only backup role; custom format supports selective restore.
compose exec -T -e PGPASSWORD="${FINANCE_BACKUP_PASSWORD}" db \
  pg_dump --format=custom --no-owner --no-privileges -U finance_backup -d finance > "${work}/finance.dump"

# The key ring is required to decrypt stored account identifiers and to keep sessions valid after restore.
# Key files are private to the API user (uid 1654), so read them as that user.
compose run --rm --no-deps -T --user 1654:1654 --entrypoint "" -v personal-finance_dp-keys:/keys:ro web \
  tar -C /keys -cf - . > "${work}/dp-keys.tar"

cat > "${work}/manifest.txt" <<MANIFEST
created_utc=${stamp}
host=$(hostname)
dump_sha256=$(sha256sum "${work}/finance.dump" | cut -d' ' -f1)
keys_sha256=$(sha256sum "${work}/dp-keys.tar" | cut -d' ' -f1)
MANIFEST

tar -C "${work}" -cf - finance.dump dp-keys.tar manifest.txt \
  | age -r "${BACKUP_AGE_RECIPIENT}" > "${BACKUP_DIR}/finance-${stamp}.tar.age"

find "${BACKUP_DIR}" -name 'finance-*.tar.age' -mtime "+${RETENTION_DAYS}" -delete
echo "Backup written: ${BACKUP_DIR}/finance-${stamp}.tar.age ($(du -h "${BACKUP_DIR}/finance-${stamp}.tar.age" | cut -f1))"
