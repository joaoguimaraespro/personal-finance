#!/usr/bin/env bash
# Runs the full stack locally (Docker required) with fresh random secrets and fictitious demo brokers.
set -euo pipefail
cd "$(dirname "$0")/.."
ENV_FILE=deploy/.env.local
if [ ! -f "$ENV_FILE" ]; then
  gen() { openssl rand -base64 48 | tr -d '/+=\n' | cut -c1-40; }
  umask 077
  cat > "$ENV_FILE" <<ENV
POSTGRES_SUPERUSER_PASSWORD=$(gen)
FINANCE_MIGRATOR_PASSWORD=$(gen)
FINANCE_APP_PASSWORD=$(gen)
FINANCE_BACKUP_PASSWORD=$(gen)
AUTH_SETUP_TOKEN=$(gen)
API_IMAGE=personal-finance-api:local
WEB_IMAGE=personal-finance-web:local
MCP_IMAGE=personal-finance-mcp:local
ENV
fi
docker compose -f deploy/compose.yml -f deploy/compose.local.yml --env-file "$ENV_FILE" up -d --build --wait
echo
echo "Open https://localhost:8443 (accept the local certificate)."
echo "Setup token: $(grep AUTH_SETUP_TOKEN "$ENV_FILE" | cut -d= -f2)"
echo "Stop:  docker compose -f deploy/compose.yml -f deploy/compose.local.yml --env-file $ENV_FILE down"
echo "Reset: add -v to the command above (deletes the local database)."
