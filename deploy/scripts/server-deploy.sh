#!/usr/bin/env bash
# One-command deploy on the home server (run with sudo by the server owner). Idempotent: re-run to upgrade.
#
#   sudo deploy/scripts/server-deploy.sh [--backup-recipient age1...] [--tailnet-port 8443] [--ref main]
#
# - creates the isolated "finance" user with its own rootless Docker (one user per project);
# - clones/updates the repo in /home/finance/personal-finance and generates deploy/.env with random secrets (once);
# - builds and starts the stack bound to 127.0.0.1 only;
# - publishes it on the tailnet only with `tailscale serve` (HTTPS, Tailscale certificate);
# - enables the daily encrypted backup timer when an age public key is given.
set -euo pipefail
[ "$(id -u)" -eq 0 ] || { echo "Run with sudo." >&2; exit 1; }

APP_USER=finance
REPO_URL=https://github.com/joaoguimaraespro/personal-finance.git
TAILNET_PORT=8443
REF=main
BACKUP_RECIPIENT=""
while [ $# -gt 0 ]; do
  case "$1" in
    --backup-recipient) BACKUP_RECIPIENT="$2"; shift 2 ;;
    --tailnet-port) TAILNET_PORT="$2"; shift 2 ;;
    --ref) REF="$2"; shift 2 ;;
    *) echo "Unknown option: $1" >&2; exit 1 ;;
  esac
done
if [ -n "${BACKUP_RECIPIENT}" ] && [[ ! "${BACKUP_RECIPIENT}" =~ ^age1[0-9a-z]{58}$ ]]; then
  echo "--backup-recipient must be an age public key (age1…), never the private key." >&2; exit 1
fi

step() { printf '\n==> %s\n' "$*"; }

step "Packages"
apt-get install -y -qq uidmap dbus-user-session slirp4netns age docker-ce-rootless-extras git >/dev/null

step "User ${APP_USER}"
id "${APP_USER}" >/dev/null 2>&1 || useradd --create-home --shell /bin/bash "${APP_USER}"
chmod 750 "/home/${APP_USER}"
APP_UID=$(id -u "${APP_USER}")
APP_HOME=$(getent passwd "${APP_USER}" | cut -d: -f6)
APP_DIR="${APP_HOME}/personal-finance"
loginctl enable-linger "${APP_USER}"
for _ in $(seq 1 30); do [ -S "/run/user/${APP_UID}/bus" ] && break; sleep 1; done

# Run a command as the finance user with its systemd user session and rootless Docker socket.
as_app() {
  runuser -u "${APP_USER}" -- env -i HOME="${APP_HOME}" USER="${APP_USER}" PATH=/usr/local/bin:/usr/bin:/bin \
    XDG_RUNTIME_DIR="/run/user/${APP_UID}" DBUS_SESSION_BUS_ADDRESS="unix:path=/run/user/${APP_UID}/bus" \
    DOCKER_HOST="unix:///run/user/${APP_UID}/docker.sock" bash -c "$1"
}

step "Rootless Docker for ${APP_USER}"
if ! as_app 'systemctl --user is-active --quiet docker'; then
  as_app 'dockerd-rootless-setuptool.sh install --skip-iptables >/dev/null && systemctl --user enable --now docker'
fi
as_app "grep -q DOCKER_HOST ~/.bashrc || echo 'export DOCKER_HOST=unix:///run/user/${APP_UID}/docker.sock' >> ~/.bashrc"

step "Source (${REF})"
if [ -d "${APP_DIR}/.git" ]; then
  as_app "cd '${APP_DIR}' && git fetch -q origin && git checkout -q '${REF}' && git pull -q --ff-only"
else
  as_app "git clone -q --branch '${REF}' '${REPO_URL}' '${APP_DIR}'"
fi
as_app "install -d -m 700 '${APP_DIR}/backups'"

ENV_FILE="${APP_DIR}/deploy/.env"
NEW_ENV=false
if [ ! -f "${ENV_FILE}" ]; then
  step "Secrets (deploy/.env, mode 600)"
  gen() { openssl rand -base64 48 | tr -d '/+=\n' | cut -c1-40; }
  SETUP_TOKEN=$(gen)
  as_app "umask 077 && cat > '${ENV_FILE}'" <<EOF
POSTGRES_SUPERUSER_PASSWORD=$(gen)
FINANCE_MIGRATOR_PASSWORD=$(gen)
FINANCE_APP_PASSWORD=$(gen)
FINANCE_BACKUP_PASSWORD=$(gen)
AUTH_SETUP_TOKEN=${SETUP_TOKEN}
BIND_ADDRESS=127.0.0.1
HTTP_PORT=8080
INTEGRATIONS_ENABLE_DEMO=false
OTEL_EXPORTER_OTLP_ENDPOINT=
BACKUP_AGE_RECIPIENT=
BACKUP_DIR=${APP_DIR}/backups
BACKUP_RETENTION_DAYS=35
ANTHROPIC_API_KEY=
ASSISTANT_MODEL=claude-opus-5
EOF
  NEW_ENV=true
fi
if [ -n "${BACKUP_RECIPIENT}" ]; then
  as_app "sed -i 's#^BACKUP_AGE_RECIPIENT=.*#BACKUP_AGE_RECIPIENT=${BACKUP_RECIPIENT}#' '${ENV_FILE}'"
fi
HTTP_PORT=$(as_app "grep -E '^HTTP_PORT=' '${ENV_FILE}' | cut -d= -f2")

step "Build and start (first build takes a few minutes)"
as_app "cd '${APP_DIR}/deploy' && docker compose --env-file .env up -d --build --wait --quiet-pull"

step "Tailnet HTTPS on port ${TAILNET_PORT}"
if ! tailscale status --self --json | grep -q '"CertDomains": *\['; then
  echo "HTTPS certificates are disabled for this tailnet. Enable them at https://login.tailscale.com/admin/dns"
  echo "(HTTPS Certificates → Enable); this step continues automatically once they are on."
fi
tailscale serve --bg --https="${TAILNET_PORT}" "http://127.0.0.1:${HTTP_PORT}"
TS_HOST=$(tailscale status --self --json | python3 -c 'import json,sys; print(json.load(sys.stdin)["Self"]["DNSName"].rstrip("."))')

step "Backups"
if as_app "grep -qE '^BACKUP_AGE_RECIPIENT=age1' '${ENV_FILE}'"; then
  as_app "install -d ~/.config/systemd/user && cp '${APP_DIR}'/deploy/systemd/finance-backup.* ~/.config/systemd/user/ \
    && systemctl --user daemon-reload && systemctl --user enable --now finance-backup.timer >/dev/null"
  echo "Daily encrypted backup enabled (${APP_DIR}/backups)."
else
  echo "Skipped: no age public key yet. Re-run with --backup-recipient age1… to enable daily backups."
fi

printf '\nDone. Open https://%s:%s (Tailscale devices only).\n' "${TS_HOST}" "${TAILNET_PORT}"
if [ "${NEW_ENV}" = true ]; then
  printf 'Setup token (create the owner, then it is refused forever): %s\n' "${SETUP_TOKEN}"
else
  echo "Existing deploy/.env kept; the setup token is in it if the owner was never created."
fi
