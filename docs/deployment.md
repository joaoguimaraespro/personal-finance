# Deployment (home server)

Target: a Linux host running Tailscale, with one unprivileged user per project and rootless Docker.

## 1. Backup key (on another machine)

Backups are encrypted to an [age](https://age-encryption.org) public key. Create the key pair **off the
server** and keep the private key offline (password manager / USB) — without it backups cannot be read,
and a server compromise cannot read them either.

```bash
age-keygen -o finance-backup-identity.txt     # private key: store offline
age-keygen -y finance-backup-identity.txt     # public key: age1…
```

## 2. Deploy (one command, as an admin)

```bash
git clone https://github.com/joaoguimaraespro/personal-finance.git /tmp/pf
sudo /tmp/pf/deploy/scripts/server-deploy.sh --backup-recipient age1…
```

[`server-deploy.sh`](../deploy/scripts/server-deploy.sh) is idempotent and:

1. creates the `finance` user with lingering and its **own rootless Docker** (no access to other projects);
2. clones the repo to `/home/finance/personal-finance` (or fast-forwards it);
3. generates `deploy/.env` with random secrets, mode 600 — only once, never overwritten;
4. builds the images and starts the stack bound to `127.0.0.1:8080`;
5. runs `tailscale serve --https=8443` — HTTPS with the Tailscale certificate, reachable only from the tailnet
   (8443 leaves 443 free for anything else on the host);
6. installs the daily backup timer when a backup key is given.

It prints the URL and a one-time **setup token**. Run it in your own terminal (not through an AI tool or a
logged session), since it prints that token.

## 3. First login

Open `https://<host>.<tailnet>.ts.net:8443`, create the owner with the setup token, enrol an authenticator
app and store the recovery codes. The setup endpoint is refused once an owner exists.

## 4. Operating

```bash
sudo -iu finance
cd ~/personal-finance/deploy
docker compose --env-file .env ps
docker compose --env-file .env logs -f api
~/personal-finance/deploy/scripts/backup.sh      # on-demand backup
```

Copy `backups/` off the machine regularly (another disk, rclone to object storage — the files are
already encrypted).

## Upgrades

```bash
sudo /home/finance/personal-finance/deploy/scripts/server-deploy.sh
```

Re-running pulls `main`, rebuilds and restarts; secrets and data are kept.

The `migrate` job applies schema changes with the migrator role before the API starts.

## Observability (optional)

Set `OTEL_EXPORTER_OTLP_ENDPOINT` to an OpenTelemetry collector. The application does not require it.
