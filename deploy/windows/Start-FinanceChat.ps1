<#
.SYNOPSIS
    Optional: keeps a Claude Code Remote Control "Finance" session running on this PC (Windows counterpart of
    deploy/remote-control/finance-chat.service). See docs/windows.md#ask-from-your-phone-optional.

.DESCRIPTION
    Reads PF_MCP_TOKEN from %APPDATA%\finance-chat\env (one line: PF_MCP_TOKEN=pf_...; readable only by you), starts
    `claude remote-control` in the finance-chat folder (a copy of deploy/remote-control) and restarts it 30 seconds
    after it stops, like the systemd unit's Restart=on-failure. Run it from a sign-in Scheduled Task or by hand; it
    needs a console window (Remote Control is interactive), so start it minimized rather than hidden.
#>
[CmdletBinding()]
param(
    [string]$ChatDir = (Join-Path $env:USERPROFILE 'finance-chat'),
    [string]$TokenFile = (Join-Path $env:APPDATA 'finance-chat\env')
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'PersonalFinance.psm1') -Force -DisableNameChecking
$values = Read-PfEnvFile -Path $TokenFile
if (-not ($values.Contains('PF_MCP_TOKEN') -and $values['PF_MCP_TOKEN'])) { throw "PF_MCP_TOKEN is not set in $TokenFile." }
if (-not (Test-Path -LiteralPath (Join-Path $ChatDir '.mcp.json'))) { throw "$ChatDir is not a copy of deploy\remote-control." }
if (-not (Get-Command claude -ErrorAction SilentlyContinue)) { throw 'Claude Code (claude) is not installed or not on PATH.' }
$env:PF_MCP_TOKEN = $values['PF_MCP_TOKEN']

Set-Location -LiteralPath $ChatDir
while ($true) {
    & claude remote-control --name 'Finance' --spawn same-dir --capacity 4
    Write-Host "Remote Control stopped (exit code $LASTEXITCODE); restarting in 30 seconds. Close this window to stop."
    Start-Sleep -Seconds 30
}
