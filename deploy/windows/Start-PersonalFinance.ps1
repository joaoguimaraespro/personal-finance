<#
.SYNOPSIS
    Opens Personal Finance like a desktop app, starting whatever is not running yet.

.DESCRIPTION
    Target of the Start Menu / Desktop shortcut (runs hidden). Works from any state:
    - Docker Desktop not running -> starts it and waits;
    - containers stopped -> docker compose up -d, then waits until the app answers;
    - already running -> just opens the window.
    The app opens in its own window (Edge --app, else Chrome --app, else the default browser) with a dedicated browser
    profile. A tray notification shows while a slow start is in progress; errors are shown in a message box and logged
    to logs\launcher.log.

    When backups are configured and the newest one is older than 24 hours (the PC was off at 03:30), a backup starts
    in the background.

    -Background: start everything but open no window (used by the sign-in task, see Install.ps1 -AutoStart).
#>
[CmdletBinding()]
param(
    [switch]$Background,
    [switch]$NoBackup
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

Import-Module (Join-Path $PSScriptRoot 'PersonalFinance.psm1') -Force -DisableNameChecking
$paths = Get-PfLayout -Root ([System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..')))
Start-PfLog -Directory $paths.Logs -Name 'launcher'

# One launcher at a time: a click during the sign-in task's start-up waits for it, then just opens the window.
$mutex = New-Object System.Threading.Mutex($false, 'Local\PersonalFinanceLauncher')
$owned = $false
$tray = $null
$exitCode = 0
try {
    try { $owned = $mutex.WaitOne([TimeSpan]::FromMinutes(10)) }
    catch [System.Threading.AbandonedMutexException] { $owned = $true }

    $settings = Read-PfEnvFile -Path $paths.EnvFile
    $port = [int]$settings['HTTP_PORT']
    if (-not (Test-PfAppReady -Port $port)) {
        Write-PfLog -Message 'App not answering; starting it.' -Quiet
        if (-not $Background) {
            $tray = New-PfTrayStatus -Icon $paths.Icon `
                -Text 'Starting Personal Finance... Right after Windows starts this can take a minute or two.'
        }
        $port = Start-PfStack -Paths $paths -Timeout ([TimeSpan]::FromMinutes(6))
    }
    Write-PfLog -Message "Ready on http://localhost:$port" -Quiet

    if (-not $Background) {
        Open-PfAppWindow -Url "http://localhost:$port/" -ProfileDir $paths.BrowserProfile
    }

    if (-not $NoBackup -and (Test-PfAgeRecipient -Recipient $settings['BACKUP_AGE_RECIPIENT'])) {
        $backupDir = $paths.Backups
        if ($settings.Contains('BACKUP_DIR') -and $settings['BACKUP_DIR']) { $backupDir = $settings['BACKUP_DIR'] }
        $names = @()
        if (Test-Path -LiteralPath $backupDir) {
            $names = @(Get-ChildItem -LiteralPath $backupDir -Filter 'finance-*.tar.age' -File | ForEach-Object { $_.Name })
        }
        if (Test-PfBackupOverdue -Names $names -NowUtc ([DateTime]::UtcNow)) {
            Write-PfLog -Message 'Last backup is older than 24 h; starting one in the background.' -Quiet
            $exe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
            Start-Process -FilePath $exe -WindowStyle Hidden -WorkingDirectory $paths.Root `
                -ArgumentList (Get-PfLauncherCommand -Script $paths.BackupScript -Extra '-Quiet') | Out-Null
        }
    }
}
catch {
    $exitCode = 1
    $message = $_.Exception.Message
    Write-PfLog -Message "ERROR: $message" -Quiet
    if (-not $Background) {
        Close-PfTrayStatus -Tray $tray
        $tray = $null
        Show-PfMessage -Kind Error -Text "Personal Finance could not start.`n`n$message`n`nLog: $(Join-Path $paths.Logs 'launcher.log')"
    }
}
finally {
    Close-PfTrayStatus -Tray $tray
    if ($owned) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
exit $exitCode
