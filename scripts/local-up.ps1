# Runs the full stack locally on Windows (Docker Desktop required) with fresh random secrets and demo brokers.
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
$envFile = 'deploy/.env.local'
function New-Secret { -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 40 | ForEach-Object { [char]$_ }) }
if (-not (Test-Path $envFile)) {
  @(
    "POSTGRES_SUPERUSER_PASSWORD=$(New-Secret)"
    "FINANCE_MIGRATOR_PASSWORD=$(New-Secret)"
    "FINANCE_APP_PASSWORD=$(New-Secret)"
    "FINANCE_BACKUP_PASSWORD=$(New-Secret)"
    "AUTH_SETUP_TOKEN=$(New-Secret)"
    "API_IMAGE=personal-finance-api:local"
    "WEB_IMAGE=personal-finance-web:local"
    "MCP_IMAGE=personal-finance-mcp:local"
  ) | Set-Content -Encoding ascii $envFile
}
docker compose -f deploy/compose.yml -f deploy/compose.local.yml --env-file $envFile up -d --build --wait
if ($LASTEXITCODE -ne 0) { throw 'docker compose failed' }
$token = (Select-String -Path $envFile -Pattern '^AUTH_SETUP_TOKEN=(.+)$').Matches[0].Groups[1].Value
Write-Host ''
Write-Host 'Open https://localhost:8443 (accept the local certificate).'
Write-Host "Setup token: $token"
Write-Host "Stop:  docker compose -f deploy/compose.yml -f deploy/compose.local.yml --env-file $envFile down"
