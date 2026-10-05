<#
.SYNOPSIS
    Updates Personal Finance: an encrypted backup first (when configured), then git pull + rebuild via Install.ps1.

.DESCRIPTION
    Settings, data, shortcuts and scheduled tasks are kept. The schema migrations run before the API starts, as on
    the server. -SkipBackup skips the pre-update backup.
#>
[CmdletBinding()]
param(
    [string]$Ref = 'main',
    [switch]$SkipBackup
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'PersonalFinance.psm1') -Force -DisableNameChecking
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$paths = Get-PfLayout -Root $root

$settings = Read-PfEnvFile -Path $paths.EnvFile
if (-not $SkipBackup -and (Test-PfAgeRecipient -Recipient $settings['BACKUP_AGE_RECIPIENT'])) {
    Write-Host '==> Backup before updating' -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'Backup.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'The pre-update backup failed (see logs\backup.log). Fix it, or run Update.ps1 -SkipBackup.' }
}

& (Join-Path $PSScriptRoot 'Install.ps1') -InstallDir $root -Ref $Ref -NoOpen
