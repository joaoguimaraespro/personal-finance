# Disaster recovery

Everything needed to rebuild the system:

| Item | Where it lives |
|---|---|
| Code, Compose files, Caddyfile, DB role scripts | This repository |
| Secrets (`deploy/.env`) | Password manager (copy it there after first setup) |
| Database + Data Protection key ring | `backups/finance-<timestamp>.tar.age` (encrypted), plus an off-site copy at `BACKUP_REMOTE` |
| Off-site storage credential (`rclone.conf`) | Password manager |
| Backup private key (`finance-backup-identity.txt`) | Offline — password manager / USB, never on the server |

Each backup archive contains `finance.dump` (`pg_dump -Fc`), `dp-keys.tar` (the key ring that
decrypts account identifiers and keeps sessions valid) and a manifest with SHA-256 checksums.

## Restore onto a new machine

1. Prepare the host ([deployment.md](deployment.md) steps 1–2) and restore `deploy/.env` from the
   password manager.
2. `docker compose --env-file .env up -d --build` — creates an empty database with the right roles.
3. Copy the latest `finance-*.tar.age` and the identity file to the machine (temporarily). If the old
   disk is gone, fetch the archive from the off-site copy:
   `rclone copy b2:<bucket>/finance backups/ --include "finance-<timestamp>.tar.age"`.
4. Restore:

   ```bash
   deploy/scripts/restore.sh backups/finance-20260925T221516Z.tar.age /path/to/finance-backup-identity.txt
   ```

   The script decrypts, verifies checksums, asks for confirmation, stops the app, restores the database
   as the schema owner, re-applies the least-privilege grants, restores the key ring and restarts.
5. Remove the identity file from the machine. Sign in and check the latest transactions.

## Drill

The procedure is exercised end to end (backup → wipe → restore → row count and sum match → API healthy).
Repeat it after major upgrades.

## RPO / RTO

Daily backups at 03:30 (RPO ≤ 24 h; run `backup.sh` manually before risky changes). A restore takes a
few minutes once the host is prepared.
