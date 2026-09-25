#!/usr/bin/env bash
# Development only: wipes the local dev database and restarts the API so migrations and seeds run again.
set -euo pipefail
cd "$(dirname "$0")/.."
docker compose -f deploy/compose.dev.yml exec -T db psql -q -U finance_dev -d postgres \
  -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname='finance' AND pid <> pg_backend_pid();" \
  -c "DROP DATABASE IF EXISTS finance;" -c "CREATE DATABASE finance;" >/dev/null
pid=$(ss -ltnpH 'sport = :5080' | grep -oE 'pid=[0-9]+' | head -1 | cut -d= -f2 || true)
[ -n "${pid}" ] && kill "${pid}" && sleep 2
ASPNETCORE_ENVIRONMENT=Development nohup dotnet run --project src/Host.Api --no-launch-profile \
  --urls http://localhost:5080 > "${API_LOG:-/tmp/pf-api.log}" 2>&1 &
for _ in $(seq 1 60); do curl -sf localhost:5080/health/ready >/dev/null && echo "API ready" && exit 0; sleep 2; done
echo "API did not become ready" >&2; exit 1
