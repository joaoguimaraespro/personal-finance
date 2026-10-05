<#
.SYNOPSIS
    Installs or updates Personal Finance on a Windows PC (Docker Desktop with the WSL 2 backend).

.DESCRIPTION
    Idempotent: run it again to update (git pull + rebuild); deploy\.env, the database and the backups are kept.

    - checks Git, Docker Desktop (Linux containers) and WSL 2;
    - clones or fast-forwards the repository into -InstallDir (default %LOCALAPPDATA%\PersonalFinance);
    - generates deploy\.env once, with random secrets, readable only by you;
    - builds the images and starts the stack on 127.0.0.1 only, then waits until the app answers;
    - adds "Personal Finance" to the Start Menu and the Desktop (opens the app in its own window);
    - -AutoStart: brings the app up quietly whenever you sign in to Windows (starting Docker Desktop if needed);
    - -BackupRecipient age1...: enables the daily encrypted backup (03:30, or as soon as the PC is on after that).

    No administrator rights are needed once Docker Desktop is installed. See docs/windows.md.

.EXAMPLE
    git clone https://github.com/joaoguimaraespro/personal-finance.git "$env:LOCALAPPDATA\PersonalFinance"
    powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\PersonalFinance\deploy\windows\Install.ps1" -AutoStart
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'PersonalFinance'),
    [string]$Ref = 'main',
    [string]$RepoUrl = 'https://github.com/joaoguimaraespro/personal-finance.git',
    # Only used when deploy\.env is created; afterwards change HTTP_PORT there.
    [ValidateRange(1024, 65535)][int]$Port = 8080,
    [switch]$AutoStart,
    [switch]$DisableAutoStart,
    [string]$BackupRecipient = '',
    [switch]$NoShortcuts,
    [switch]$NoOpen,
    # Internal: set when the freshly pulled copy of this script takes over.
    [switch]$SkipSourceUpdate
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

if ([Environment]::OSVersion.Platform -ne 'Win32NT') { throw 'Install.ps1 is for Windows. On Linux use deploy/scripts/server-deploy.sh.' }
if ($AutoStart -and $DisableAutoStart) { throw 'Use either -AutoStart or -DisableAutoStart.' }

Import-Module (Join-Path $PSScriptRoot 'PersonalFinance.psm1') -Force -DisableNameChecking

function Write-Step([string]$Text) { Write-Host ''; Write-Host "==> $Text" -ForegroundColor Cyan }

if ($BackupRecipient -and -not (Test-PfAgeRecipient -Recipient $BackupRecipient)) {
    throw '-BackupRecipient must be an age public key (age1...), never the private key.'
}

$InstallDir = [System.IO.Path]::GetFullPath($InstallDir)

# ---------------------------------------------------------------------------------------------------------------------
# 1. Prerequisites and source code (then hand over to the copy of this script in the install folder)
# ---------------------------------------------------------------------------------------------------------------------
if (-not $SkipSourceUpdate) {
    Write-Step 'Prerequisites'
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw 'Git is not installed. Install it (winget install --id Git.Git -e), open a new PowerShell window and run this again.'
    }
    if (-not (Get-Command docker -ErrorAction SilentlyContinue) -and -not (Get-PfDockerDesktopPath)) {
        throw 'Docker Desktop is not installed. Install it (winget install --id Docker.DockerDesktop -e, or https://www.docker.com/products/docker-desktop/), keep the WSL 2 backend, start it once, then run this again.'
    }
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & wsl.exe --status 2>&1 | Out-Null
    $wslOk = ($LASTEXITCODE -eq 0)
    $ErrorActionPreference = $previous
    if (-not $wslOk) {
        Write-Warning 'WSL 2 does not look installed. Docker Desktop needs it: run "wsl --install" in an administrator PowerShell and restart Windows.'
    }
    Write-Host 'Git, Docker Desktop: found.'

    Write-Step "Source ($Ref) in $InstallDir"
    if (Test-Path -LiteralPath (Join-Path $InstallDir '.git')) {
        Invoke-PfNative -FilePath 'git' -ArgumentList @('-C', $InstallDir, 'fetch', '--quiet', 'origin') | Out-Null
        Invoke-PfNative -FilePath 'git' -ArgumentList @('-C', $InstallDir, 'checkout', '--quiet', $Ref) | Out-Null
        $branch = (Invoke-PfNative -FilePath 'git' -ArgumentList @('-C', $InstallDir, 'rev-parse', '--abbrev-ref', 'HEAD') -Quiet)[0]
        if ($branch -ne 'HEAD') {
            Invoke-PfNative -FilePath 'git' -ArgumentList @('-C', $InstallDir, 'pull', '--quiet', '--ff-only') | Out-Null
        }
    }
    elseif ((Test-Path -LiteralPath $InstallDir) -and (Get-ChildItem -LiteralPath $InstallDir -Force | Select-Object -First 1)) {
        throw "$InstallDir exists and is not a Personal Finance installation. Choose another folder with -InstallDir."
    }
    else {
        Invoke-PfNative -FilePath 'git' -ArgumentList @('clone', '--quiet', '--branch', $Ref, $RepoUrl, $InstallDir) | Out-Null
    }

    $next = Join-Path $InstallDir 'deploy\windows\Install.ps1'
    $forward = @{}
    foreach ($key in $PSBoundParameters.Keys) { $forward[$key] = $PSBoundParameters[$key] }
    $forward['InstallDir'] = $InstallDir
    & $next @forward -SkipSourceUpdate
    return
}

# ---------------------------------------------------------------------------------------------------------------------
# 2. Settings, containers, shortcuts and tasks (runs from the up-to-date copy)
# ---------------------------------------------------------------------------------------------------------------------
$paths = Get-PfLayout -Root $InstallDir
foreach ($dir in @($paths.Backups, $paths.Logs)) {
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
}
Start-PfLog -Directory $paths.Logs -Name 'install'
Write-PfLog -Message "Install/update of $InstallDir ($Ref)" -Quiet

$newEnv = $false
if (-not (Test-Path -LiteralPath $paths.EnvFile)) {
    Write-Step 'Secrets (deploy\.env, readable only by you)'
    $listening = Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue
    if ($listening) { throw "Port $Port is already in use on this PC. Run again with -Port 8081 (or another free port)." }
    $settings = New-PfEnvSetting -BackupDir $paths.Backups -HttpPort $Port -BackupRecipient $BackupRecipient
    Write-PfEnvFile -Path $paths.EnvFile -Settings $settings
    $newEnv = $true
}
elseif ($BackupRecipient) {
    Set-PfEnvValue -Path $paths.EnvFile -Name 'BACKUP_AGE_RECIPIENT' -Value $BackupRecipient
}
Protect-PfFile -Path $paths.EnvFile
$settings = Read-PfEnvFile -Path $paths.EnvFile
$port = [int]$settings['HTTP_PORT']

Write-Step 'Docker Desktop'
Start-PfDockerDesktop -OnWait { Write-Host '.' -NoNewline }
$osType = (Invoke-PfNative -FilePath 'docker' -ArgumentList @('info', '--format', '{{.OSType}}') -Quiet)[0]
if ($osType -ne 'linux') { throw 'Docker Desktop is using Windows containers. Right-click its tray icon and choose "Switch to Linux containers", then run this again.' }
Invoke-PfNative -FilePath 'docker' -ArgumentList @('compose', 'version') -Quiet | Out-Null
Write-Host 'Docker Desktop is running (Linux containers).'

Write-Step 'Build and start (the first build takes several minutes)'
Invoke-PfCompose -Paths $paths -ArgumentList @('up', '-d', '--build', '--wait', '--quiet-pull', '--remove-orphans') | Out-Null
if (-not (Wait-PfCondition -Condition { Test-PfAppReady -Port $port } -Timeout ([TimeSpan]::FromMinutes(3)))) {
    throw "The containers started but the app does not answer on http://localhost:$port. Check: docker compose -p personal-finance logs api"
}
Write-Host "Personal Finance is running on http://localhost:$port (this PC only)."

$powershellExe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$shortcuts = Get-PfShortcutPath
$taskNames = Get-PfTaskName
if (-not $NoShortcuts) {
    Write-Step 'Shortcuts'
    foreach ($lnk in @($shortcuts.StartMenu, $shortcuts.Desktop)) {
        New-PfShortcut -Path $lnk -Target $powershellExe -Arguments (Get-PfLauncherCommand -Script $paths.Launcher) `
            -Icon $paths.Icon -WorkingDirectory $paths.Root -Description 'Open Personal Finance' | Out-Null
    }
    Write-Host 'Start Menu and Desktop: "Personal Finance".'
}

if ($AutoStart) {
    Write-Step 'Start automatically when you sign in'
    $ok = Register-PfTask -Name $taskNames.AutoStart -Script $paths.Launcher -ScriptArguments '-Background' -When Logon `
        -WorkingDirectory $paths.Root -Description 'Starts Docker Desktop (if needed) and the Personal Finance containers, without opening a window.' `
        -TimeLimit ([TimeSpan]::FromMinutes(30))
    if ($ok) {
        if (Test-Path -LiteralPath $shortcuts.Startup) { Remove-Item -LiteralPath $shortcuts.Startup -Force }
        Write-Host "Scheduled task '$($taskNames.AutoStart)' registered."
    }
    else {
        New-PfShortcut -Path $shortcuts.Startup -Target $powershellExe `
            -Arguments (Get-PfLauncherCommand -Script $paths.Launcher -Extra '-Background') -Icon $paths.Icon `
            -WorkingDirectory $paths.Root -Description 'Start Personal Finance in the background' | Out-Null
        Write-Host 'Task Scheduler refused; added a shortcut to your Startup folder instead.'
    }
}
elseif ($DisableAutoStart) {
    Unregister-PfTask -Name $taskNames.AutoStart
    if (Test-Path -LiteralPath $shortcuts.Startup) { Remove-Item -LiteralPath $shortcuts.Startup -Force }
    Write-Host 'Automatic start disabled.'
}

Write-Step 'Backups'
if (Test-PfAgeRecipient -Recipient $settings['BACKUP_AGE_RECIPIENT']) {
    $ok = Register-PfTask -Name $taskNames.Backup -Script $paths.BackupScript -ScriptArguments '-Quiet' -When Daily -At '03:30' `
        -WorkingDirectory $paths.Root -Description 'Encrypted Personal Finance backup. Runs at 03:30, or as soon as possible after a missed run.' `
        -TimeLimit ([TimeSpan]::FromHours(2))
    if ($ok) { Write-Host "Daily encrypted backup scheduled ('$($taskNames.Backup)'); missed runs start as soon as the PC is on." }
    else { Write-Host 'Could not schedule the daily backup; the launcher still runs one whenever the last backup is older than 24 h.' }
    if (-not (Find-PfTool -Name 'age' -Paths $paths)) {
        Write-Warning 'age.exe is not installed yet, so backups will fail until it is: winget install --id FiloSottile.age -e (then open a new window). See docs/windows.md.'
    }
}
else {
    Write-Host 'Skipped: no age public key yet. Run again with -BackupRecipient age1... to enable daily encrypted backups.'
}

$setupRequired = $false
try {
    $me = Invoke-RestMethod -Uri "http://127.0.0.1:$port/api/auth/me" -UseBasicParsing -TimeoutSec 5
    $setupRequired = [bool]$me.setupRequired
}
catch { Write-PfLog -Message "Could not read the setup state: $($_.Exception.Message)" -Quiet }

Write-Host ''
Write-Host "Done. Open 'Personal Finance' from the Start Menu or the Desktop (http://localhost:$port)." -ForegroundColor Green
if ($setupRequired) {
    Write-Host 'Create the owner account with this one-time setup token (refused forever once the owner exists):'
    Write-Host "  $($settings['AUTH_SETUP_TOKEN'])" -ForegroundColor Yellow
}
elseif (-not $newEnv) {
    Write-Host 'Existing settings, data and backups were kept.'
}
Write-Host "Keep a copy of $($paths.EnvFile) in your password manager."

if (-not $NoOpen) {
    Open-PfAppWindow -Url "http://localhost:$port/" -ProfileDir $paths.BrowserProfile
}
