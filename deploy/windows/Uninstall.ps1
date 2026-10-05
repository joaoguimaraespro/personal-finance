<#
.SYNOPSIS
    Removes Personal Finance from this PC. Keeps your data unless -Purge.

.DESCRIPTION
    Default: stops and removes the containers, the shortcuts and the scheduled tasks. The database and key ring
    (Docker volumes personal-finance_db-data and personal-finance_dp-keys), deploy\.env and the backups folder stay,
    so running Install.ps1 again brings everything back as it was.

    -Purge: also deletes the Docker volumes, the locally built images and the whole install folder - including
    deploy\.env and the backups in it. Asks you to type 'delete' first (or pass -Force). Copy any backup you want to
    keep somewhere else before purging.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = ([System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))),
    [switch]$Purge,
    [switch]$Force
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

$modulePath = Join-Path $PSScriptRoot 'PersonalFinance.psm1'
Import-Module $modulePath -Force -DisableNameChecking
$paths = Get-PfLayout -Root ([System.IO.Path]::GetFullPath($InstallDir))
if (-not (Test-Path -LiteralPath $paths.ComposeFile)) { throw "$InstallDir is not a Personal Finance installation." }

if ($Purge -and -not $Force) {
    Write-Host 'This permanently deletes:' -ForegroundColor Yellow
    Write-Host '  - the database and key ring (Docker volumes personal-finance_db-data, personal-finance_dp-keys)'
    Write-Host "  - $($paths.Root), including deploy\.env and the backups folder"
    Write-Host 'Backups stored elsewhere (off-site copy) are not touched.'
    $answer = Read-Host "Type 'delete' to continue"
    if ($answer -ne 'delete') { Write-Host 'Aborted. Nothing was changed.'; exit 1 }
}

$taskNames = Get-PfTaskName
Unregister-PfTask -Name $taskNames.AutoStart
Unregister-PfTask -Name $taskNames.Backup
$shortcuts = Get-PfShortcutPath
foreach ($lnk in @($shortcuts.StartMenu, $shortcuts.Desktop, $shortcuts.Startup)) {
    if (Test-Path -LiteralPath $lnk) { Remove-Item -LiteralPath $lnk -Force }
}
Write-Host 'Shortcuts and scheduled tasks removed.'

if (Test-PfDockerEngine) {
    if ($Purge) {
        Invoke-PfCompose -Paths $paths -ArgumentList @('down', '--volumes', '--rmi', 'local', '--remove-orphans') | Out-Null
        Write-Host 'Containers, volumes and images removed.'
    }
    else {
        Invoke-PfCompose -Paths $paths -ArgumentList @('down', '--remove-orphans') | Out-Null
        Write-Host 'Containers removed; the data volumes are kept.'
    }
}
elseif ($Purge) {
    throw 'Docker Desktop is not running, so the data volumes cannot be deleted. Start Docker Desktop and run this again.'
}
else {
    Write-Warning 'Docker Desktop is not running; the containers stay stopped. Start Docker Desktop and run this again to remove them.'
}

if ($Purge) {
    Remove-Module PersonalFinance -ErrorAction SilentlyContinue
    Set-Location -LiteralPath ([System.IO.Path]::GetTempPath())
    Remove-Item -LiteralPath $paths.Root -Recurse -Force
    Write-Host "Removed $($paths.Root). Personal Finance is gone from this PC."
}
else {
    Write-Host ''
    Write-Host "Your data is kept: Docker volumes, $($paths.EnvFile) and $($paths.Backups)."
    Write-Host "Reinstall with: powershell -ExecutionPolicy Bypass -File `"$(Join-Path $paths.Windows 'Install.ps1')`""
}
