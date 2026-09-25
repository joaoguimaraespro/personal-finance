# Deployment (home server)

Target: a Linux host running Tailscale, with one unprivileged user per project and rootless Docker.

## 1. Host preparation (once, as an admin)

```bash
sudo deploy/scripts/bootstrap-user.sh
```

Creates the `finance` user, enables lingering, installs rootless Docker for that user only, and
installs `age`.

## 2. Configure (as `finance`)

```bash
sudo -iu finance
git clone https://github.com/joaoguimaraespro/personal-finance.git ~/personal-finance
cd ~/personal-finance/deploy
cp .env.example .env && chmod 600 .env
# fill in the passwords and AUTH_SETUP_TOKEN (openssl rand -base64 32 …)
```

Create the backup key pair **on another machine** and put only the public key in `.env`:

```bash
age-keygen -o finance-backup-identity.txt   # keep this file offline (password manager / USB)
grep 'public key' finance-backup-identity.txt   # → BACKUP_AGE_RECIPIENT=age1…
```

## 3. Start

```bash
docker compose --env-file .env up -d --build
docker compose ps        # db, api, web healthy; migrate exited 0
```

## 4. HTTPS on the tailnet

```bash
sudo tailscale serve --bg --https=443 http://127.0.0.1:8080
```

Browse to `https://<host>.<tailnet>.ts.net`, create the owner with the setup token, enrol an
authenticator app, save the recovery codes. Then clear `AUTH_SETUP_TOKEN` in `.env` and
`docker compose up -d` — the setup endpoint is also refused once an owner exists.

## 5. Backups

```bash
mkdir -p ~/.config/systemd/user
cp systemd/finance-backup.* ~/.config/systemd/user/
systemctl --user daemon-reload && systemctl --user enable --now finance-backup.timer
deploy/scripts/backup.sh     # run once now
```

Copy `backups/` off the machine regularly (another disk, rclone to object storage — the files are
already encrypted).

## Upgrades

```bash
git pull && docker compose --env-file .env up -d --build
```

The `migrate` job applies schema changes with the migrator role before the API starts.

## Observability (optional)

Set `OTEL_EXPORTER_OTLP_ENDPOINT` to an OpenTelemetry collector. The application does not require it.
