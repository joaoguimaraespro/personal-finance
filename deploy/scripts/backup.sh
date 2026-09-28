#!/usr/bin/env bash
# Encrypted backup of the database and the Data Protection key ring.
#
#   deploy/scripts/backup.sh            # uses deploy/.env
#
# Output: $BACKUP_DIR/finance-<UTC timestamp>.tar.age — encrypted to BACKUP_AGE_RECIPIENT (an age public key).
# The matching private key must NOT be stored on this server; without it the backups are unreadable.
#
# Off-site copy (optional): set BACKUP_REMOTE to an rclone destination (e.g. b2:my-bucket/finance) and every
# local archive not yet there is uploaded. Files are already encrypted, so any storage provider works.
# BACKUP_REMOTE_RETENTION_DAYS prunes old remote copies; leave it empty when the remote credential can't
# delete (recommended — let a bucket lifecycle rule expire files, so a compromised server can't wipe them).
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

if [ -n "${BACKUP_REMOTE:-}" ]; then
  trap - ERR
  command -v rclone >/dev/null || { echo "OFF-SITE COPY FAILED: rclone is not installed (apt install rclone)." >&2; exit 1; }
  # copy (not sync): never deletes remotely; also catches up on uploads missed while the remote was down.
  if ! rclone copy "${BACKUP_DIR}" "${BACKUP_REMOTE}" --include 'finance-*.tar.age' --immutable --retries 5; then
    echo "OFF-SITE COPY FAILED to ${BACKUP_REMOTE}. The local backup is fine; the next run retries." >&2
    exit 1
  fi
  if [ -n "${BACKUP_REMOTE_RETENTION_DAYS:-}" ]; then
    rclone delete "${BACKUP_REMOTE}" --include 'finance-*.tar.age' --min-age "${BACKUP_REMOTE_RETENTION_DAYS}d" \
      || echo "Warning: could not prune old remote copies (credential without delete permission?)." >&2
  fi
  echo "Off-site copy up to date: ${BACKUP_REMOTE}"
fi
