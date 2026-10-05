# Shared helpers for the Windows scripts (Install, Start-PersonalFinance, Backup, Restore, Uninstall).
# Compatible with Windows PowerShell 5.1 and PowerShell 7. Keep this file ASCII-only: Windows PowerShell 5.1 reads
# BOM-less scripts in the system code page.
#
# Pure functions (no Docker, no Windows APIs) are unit-tested in tests/PersonalFinance.Tests.ps1.

Set-StrictMode -Version 3.0

$script:ProjectName = 'personal-finance'
$script:LogFile = $null

# --------------------------------------------------------------------------------------------------------------------
# Paths and configuration
# --------------------------------------------------------------------------------------------------------------------

function Get-PfLayout {
    <# Every path the scripts use, derived from the install (repository) root. #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Root)
    $deploy = Join-Path $Root 'deploy'
    [pscustomobject]@{
        Root           = $Root
        Deploy         = $deploy
        Windows        = Join-Path $deploy 'windows'
        EnvFile        = Join-Path $deploy '.env'
        ComposeFile    = Join-Path $deploy 'compose.yml'
        ComposeWindows = Join-Path $deploy 'compose.windows.yml'
        Icon           = Join-Path (Join-Path $deploy 'windows') 'personal-finance.ico'
        Launcher       = Join-Path (Join-Path $deploy 'windows') 'Start-PersonalFinance.ps1'
        BackupScript   = Join-Path (Join-Path $deploy 'windows') 'Backup.ps1'
        Backups        = Join-Path $Root 'backups'
        Logs           = Join-Path $Root 'logs'
        BrowserProfile = Join-Path $Root 'browser-profile'
    }
}

function New-PfSecret {
    <#
    A cryptographically random secret of letters and digits (the alphabet server-deploy.sh produces).
    Rejection sampling keeps every character equally likely.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param([ValidateRange(16, 256)][int]$Length = 40)
    $alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789'
    $limit = 256 - (256 % $alphabet.Length) # 248: bytes at or above it would bias the first characters
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $sb = New-Object System.Text.StringBuilder
        $buffer = New-Object byte[] 64
        while ($sb.Length -lt $Length) {
            $rng.GetBytes($buffer)
            foreach ($b in $buffer) {
                if ($b -lt $limit -and $sb.Length -lt $Length) {
                    [void]$sb.Append($alphabet[$b % $alphabet.Length])
                }
            }
        }
        return $sb.ToString()
    }
    finally {
        $rng.Dispose()
    }
}

function Test-PfAgeRecipient {
    <# True for an age public key (age1...). Rejects private keys (AGE-SECRET-KEY-...) and anything else. #>
    [CmdletBinding()]
    [OutputType([bool])]
    param([AllowEmptyString()][AllowNull()][string]$Recipient)
    return [bool]($Recipient -cmatch '^age1[0-9a-z]{58}$')
}

function New-PfEnvSetting {
    <#
    The settings of a new deploy/.env for a Windows PC: fresh random secrets plus Windows defaults. Same keys as
    server-deploy.sh writes on the server, so an .env (and backups) can move between the two.
    #>
    [CmdletBinding()]
    [OutputType([System.Collections.Specialized.OrderedDictionary])]
    param(
        [Parameter(Mandatory)][string]$BackupDir,
        [ValidateRange(1, 65535)][int]$HttpPort = 8080,
        [AllowEmptyString()][string]$BackupRecipient = ''
    )
    $settings = [ordered]@{
        POSTGRES_SUPERUSER_PASSWORD  = New-PfSecret
        FINANCE_MIGRATOR_PASSWORD    = New-PfSecret
        FINANCE_APP_PASSWORD         = New-PfSecret
        FINANCE_BACKUP_PASSWORD      = New-PfSecret
        AUTH_SETUP_TOKEN             = New-PfSecret
        BIND_ADDRESS                 = '127.0.0.1'
        HTTP_PORT                    = [string]$HttpPort
        INTEGRATIONS_ENABLE_DEMO     = 'false'
        OTEL_EXPORTER_OTLP_ENDPOINT  = ''
        API_IMAGE                    = 'personal-finance-api:local'
        WEB_IMAGE                    = 'personal-finance-web:local'
        MCP_IMAGE                    = 'personal-finance-mcp:local'
        BACKUP_AGE_RECIPIENT         = $BackupRecipient
        BACKUP_DIR                   = $BackupDir
        BACKUP_RETENTION_DAYS        = '35'
        BACKUP_REMOTE                = ''
        BACKUP_REMOTE_RETENTION_DAYS = ''
        ANTHROPIC_API_KEY            = ''
        ASSISTANT_MODEL              = 'claude-opus-5'
    }
    return $settings
}

function ConvertTo-PfEnvText {
    <# KEY=value lines with LF endings (read the same by docker compose, backup.sh and these scripts). #>
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)][System.Collections.IDictionary]$Settings)
    $lines = foreach ($key in $Settings.Keys) {
        if ($key -notmatch '^[A-Z][A-Z0-9_]*$') { throw "Invalid setting name: $key" }
        $value = [string]$Settings[$key]
        if ($value -match "[`r`n]") { throw "The value of $key must be a single line." }
        "$key=$value"
    }
    return (($lines -join "`n") + "`n")
}

function ConvertFrom-PfEnvText {
    <# Parses KEY=value lines; ignores blanks and comments; strips matching surrounding quotes. #>
    [CmdletBinding()]
    [OutputType([System.Collections.Specialized.OrderedDictionary])]
    param([AllowEmptyString()][string]$Text)
    $result = [ordered]@{}
    foreach ($raw in ($Text -split "`r?`n")) {
        $line = $raw.Trim()
        if ($line -eq '' -or $line.StartsWith('#')) { continue }
        $eq = $line.IndexOf('=')
        if ($eq -lt 1) { continue }
        $key = $line.Substring(0, $eq).Trim()
        $value = $line.Substring($eq + 1).Trim()
        if ($value.Length -ge 2 -and (($value[0] -eq '"' -and $value[-1] -eq '"') -or ($value[0] -eq "'" -and $value[-1] -eq "'"))) {
            $value = $value.Substring(1, $value.Length - 2)
        }
        $result[$key] = $value
    }
    return $result
}

function Read-PfEnvFile {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { throw "Settings file not found: $Path. Run Install.ps1 first." }
    return ConvertFrom-PfEnvText -Text ([System.IO.File]::ReadAllText($Path))
}

function Write-PfEnvFile {
    <# Writes the settings as ASCII with LF endings (no BOM). Use Protect-PfFile afterwards to restrict access. #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][System.Collections.IDictionary]$Settings
    )
    $text = ConvertTo-PfEnvText -Settings $Settings
    [System.IO.File]::WriteAllText($Path, $text, (New-Object System.Text.ASCIIEncoding))
}

function Set-PfEnvValue {
    <# Changes (or appends) one setting in an existing .env, keeping every other line and comment as is. #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][ValidatePattern('^[A-Z][A-Z0-9_]*$')][string]$Name,
        [AllowEmptyString()][string]$Value
    )
    if ($Value -match "[`r`n]") { throw "The value of $Name must be a single line." }
    $lines = @(([System.IO.File]::ReadAllText($Path)) -split "`r?`n")
    if ($lines.Count -gt 0 -and $lines[-1] -eq '') { $lines = @($lines | Select-Object -First ($lines.Count - 1)) }
    $found = $false
    $updated = foreach ($line in $lines) {
        if ($line -match "^\s*$Name\s*=") { $found = $true; "$Name=$Value" } else { $line }
    }
    if (-not $found) { $updated = @($updated) + "$Name=$Value" }
    if ($PSCmdlet.ShouldProcess($Path, "Set $Name")) {
        [System.IO.File]::WriteAllText($Path, ((@($updated) -join "`n") + "`n"), (New-Object System.Text.ASCIIEncoding))
    }
}

function Protect-PfFile {
    <# Restricts a file to the current user only (no inherited access): the Windows equivalent of chmod 600. #>
    [CmdletBinding(SupportsShouldProcess)]
    param([Parameter(Mandatory)][string]$Path)
    $sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    if ($PSCmdlet.ShouldProcess($Path, 'Restrict to the current user')) {
        $output = & icacls.exe $Path /inheritance:r /grant:r "*${sid}:(F)" 2>&1
        if ($LASTEXITCODE -ne 0) { throw "icacls failed on ${Path}: $output" }
    }
}

# --------------------------------------------------------------------------------------------------------------------
# Logging and native commands
# --------------------------------------------------------------------------------------------------------------------

function Start-PfLog {
    <# Appends to logs/<name>.log (rotated at 1 MB). #>
    [CmdletBinding(SupportsShouldProcess)]
    param([Parameter(Mandatory)][string]$Directory, [Parameter(Mandatory)][string]$Name)
    if (-not $PSCmdlet.ShouldProcess($Directory, "Log to $Name.log")) { return }
    if (-not (Test-Path -LiteralPath $Directory)) { New-Item -ItemType Directory -Path $Directory -Force | Out-Null }
    $path = Join-Path $Directory "$Name.log"
    if ((Test-Path -LiteralPath $path) -and (Get-Item -LiteralPath $path).Length -gt 1MB) {
        Move-Item -LiteralPath $path -Destination "$path.1" -Force
    }
    $script:LogFile = $path
}

function Write-PfLog {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Message, [switch]$Quiet)
    if (-not $Quiet) { Write-Host $Message }
    if ($script:LogFile) {
        $stamp = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
        Add-Content -LiteralPath $script:LogFile -Value "$stamp $Message" -Encoding UTF8
    }
}

function Invoke-PfNative {
    <#
    Runs a native program, logs its output and throws on a non-zero exit code. stderr is captured as text so progress
    messages (docker writes them to stderr) never turn into PowerShell errors. Returns stdout+stderr lines.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [switch]$AllowFailure,
        [switch]$Quiet
    )
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        # Lines are logged (and shown) as they arrive, so a long image build shows its progress.
        $output = @(& $FilePath @ArgumentList 2>&1 | ForEach-Object {
                $line = "$_"
                Write-PfLog -Message "  $line" -Quiet:$Quiet
                $line
            })
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }
    if ($code -ne 0 -and -not $AllowFailure) {
        throw "$FilePath $($ArgumentList -join ' ') failed with exit code $code."
    }
    return , $output
}

# --------------------------------------------------------------------------------------------------------------------
# Docker
# --------------------------------------------------------------------------------------------------------------------

function Get-PfComposeArgument {
    <# The compose invocation shared by every script: server compose file + Windows override + deploy/.env. #>
    [CmdletBinding()]
    [OutputType([string[]])]
    param([Parameter(Mandatory)]$Paths)
    return @('compose', '--project-name', $script:ProjectName, '--project-directory', $Paths.Deploy,
        '-f', $Paths.ComposeFile, '-f', $Paths.ComposeWindows, '--env-file', $Paths.EnvFile)
}

function Invoke-PfCompose {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Paths,
        [Parameter(Mandatory)][string[]]$ArgumentList,
        [switch]$AllowFailure,
        [switch]$Quiet
    )
    $all = @(Get-PfComposeArgument -Paths $Paths) + $ArgumentList
    return Invoke-PfNative -FilePath 'docker' -ArgumentList $all -AllowFailure:$AllowFailure -Quiet:$Quiet
}

function Get-PfDockerDesktopPath {
    [CmdletBinding()]
    [OutputType([string])]
    param()
    $candidates = @()
    foreach ($key in @('HKLM:\SOFTWARE\Docker Inc.\Docker\1.0', 'HKCU:\SOFTWARE\Docker Inc.\Docker\1.0')) {
        $item = Get-ItemProperty -Path $key -ErrorAction SilentlyContinue
        if ($item -and ($item.PSObject.Properties.Name -contains 'AppPath')) {
            $candidates += (Join-Path $item.AppPath 'Docker Desktop.exe')
        }
    }
    if ($env:ProgramFiles) { $candidates += (Join-Path $env:ProgramFiles 'Docker\Docker\Docker Desktop.exe') }
    if ($env:LOCALAPPDATA) { $candidates += (Join-Path $env:LOCALAPPDATA 'Programs\Docker\Docker\Docker Desktop.exe') }
    foreach ($c in $candidates) { if (Test-Path -LiteralPath $c) { return $c } }
    return $null
}

function Test-PfDockerEngine {
    <# True when the docker CLI can reach a running engine. #>
    [CmdletBinding()]
    [OutputType([bool])]
    param()
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { return $false }
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & docker info --format '{{.ServerVersion}}' 2>&1 | Out-Null
        return ($LASTEXITCODE -eq 0)
    }
    finally {
        $ErrorActionPreference = $previous
    }
}

function Wait-PfCondition {
    <#
    Polls a condition until it is true or the timeout expires. Returns $true on success, $false on timeout.
    Sleep and clock are injectable for tests.
    #>
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)][scriptblock]$Condition,
        [Parameter(Mandatory)][TimeSpan]$Timeout,
        [TimeSpan]$Interval = [TimeSpan]::FromSeconds(2),
        [scriptblock]$Sleep = { param($span) Start-Sleep -Milliseconds ([int]$span.TotalMilliseconds) },
        [scriptblock]$Now = { [DateTime]::UtcNow },
        [scriptblock]$OnWait = $null
    )
    $deadline = (& $Now) + $Timeout
    while ($true) {
        if (& $Condition) { return $true }
        if ((& $Now) -ge $deadline) { return $false }
        if ($OnWait) { & $OnWait }
        & $Sleep $Interval
    }
}

function Start-PfDockerDesktop {
    <# Starts Docker Desktop when the engine is not running and waits for it. Throws with a clear message otherwise. #>
    [CmdletBinding(SupportsShouldProcess)]
    param([TimeSpan]$Timeout = [TimeSpan]::FromMinutes(5), [scriptblock]$OnWait = $null)
    if (Test-PfDockerEngine) { return }
    $exe = Get-PfDockerDesktopPath
    if (-not $exe) {
        throw 'Docker Desktop is not installed. Install it from https://www.docker.com/products/docker-desktop/ (WSL 2 backend), start it once to accept its terms, then try again.'
    }
    if (-not (Get-Process -Name 'Docker Desktop' -ErrorAction SilentlyContinue)) {
        if ($PSCmdlet.ShouldProcess($exe, 'Start Docker Desktop')) {
            Write-PfLog -Message 'Starting Docker Desktop...' -Quiet
            Start-Process -FilePath $exe | Out-Null
        }
    }
    if (-not (Wait-PfCondition -Condition { Test-PfDockerEngine } -Timeout $Timeout -Interval ([TimeSpan]::FromSeconds(3)) -OnWait $OnWait)) {
        throw "Docker Desktop did not become ready within $([int]$Timeout.TotalMinutes) minutes. Open Docker Desktop to see why (WSL 2 update, terms to accept, not enough memory)."
    }
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw 'Docker Desktop is running but the docker command is not on PATH. Sign out and in again, or reinstall Docker Desktop.'
    }
}

function Test-PfAppReady {
    <#
    True when the whole chain answers: web (Caddy) -> API -> database. GET /api/auth/me is anonymous, touches the
    database and returns 200 once the API is up.
    #>
    [CmdletBinding()]
    [OutputType([bool])]
    param([Parameter(Mandatory)][int]$Port, [int]$TimeoutSec = 3)
    try {
        $response = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/api/auth/me" -UseBasicParsing -TimeoutSec $TimeoutSec `
            -Headers @{ 'Cache-Control' = 'no-store' } -ErrorAction Stop
        return ($response.StatusCode -eq 200)
    }
    catch {
        return $false
    }
}

function Start-PfStack {
    <#
    Brings the app up from any state: Docker Desktop stopped, stack stopped, or already running (then it returns at
    once). Returns the HTTP port.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([int])]
    param(
        [Parameter(Mandatory)]$Paths,
        [TimeSpan]$Timeout = [TimeSpan]::FromMinutes(5),
        [scriptblock]$OnWait = $null
    )
    $settings = Read-PfEnvFile -Path $Paths.EnvFile
    $port = [int]$settings['HTTP_PORT']
    if (Test-PfAppReady -Port $port) { return $port }
    if (-not $PSCmdlet.ShouldProcess('Personal Finance', 'Start')) { return $port }

    Start-PfDockerDesktop -Timeout $Timeout -OnWait $OnWait
    # Containers come back by themselves (restart: unless-stopped) when Docker Desktop starts; give them a moment.
    if (Wait-PfCondition -Condition { Test-PfAppReady -Port $port } -Timeout ([TimeSpan]::FromSeconds(20)) -OnWait $OnWait) {
        return $port
    }
    Write-PfLog -Message 'Starting the containers...' -Quiet
    Invoke-PfCompose -Paths $Paths -ArgumentList @('up', '-d') -Quiet | Out-Null
    if (-not (Wait-PfCondition -Condition { Test-PfAppReady -Port $port } -Timeout $Timeout -OnWait $OnWait)) {
        throw "Personal Finance did not answer on http://localhost:$port within $([int]$Timeout.TotalMinutes) minutes. See $($Paths.Logs) and 'docker compose logs api'."
    }
    return $port
}

# --------------------------------------------------------------------------------------------------------------------
# Backups
# --------------------------------------------------------------------------------------------------------------------

function Get-PfBackupTimestamp {
    <# UTC time encoded in finance-YYYYMMDDTHHMMSSZ.tar.age, or $null for other names. #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Name)
    if ($Name -match '^finance-(\d{8}T\d{6}Z)\.tar\.age$') {
        return [DateTime]::ParseExact($Matches[1], "yyyyMMdd'T'HHmmss'Z'", [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AdjustToUniversal -bor [Globalization.DateTimeStyles]::AssumeUniversal)
    }
    return $null
}

function Get-PfExpiredBackup {
    <#
    The archives older than the retention period (by the time in their name, like backup.sh's find -mtime).
    Never returns the newest archive, so a long-switched-off PC never ends up with no backup at all.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Names,
        [Parameter(Mandatory)][DateTime]$NowUtc,
        [Parameter(Mandatory)][ValidateRange(1, 36500)][int]$RetentionDays
    )
    $dated = @(foreach ($n in $Names) {
            $t = Get-PfBackupTimestamp -Name $n
            if ($null -ne $t) { [pscustomobject]@{ Name = $n; Time = $t } }
        })
    if ($dated.Count -le 1) { return @() }
    $newest = ($dated | Sort-Object Time -Descending | Select-Object -First 1).Name
    $cutoff = $NowUtc.AddDays(-$RetentionDays)
    return @($dated | Where-Object { $_.Time -lt $cutoff -and $_.Name -ne $newest } | ForEach-Object { $_.Name })
}

function Test-PfBackupOverdue {
    <# True when no archive is younger than MaxAge (default 24 h). #>
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Names,
        [Parameter(Mandatory)][DateTime]$NowUtc,
        [TimeSpan]$MaxAge = [TimeSpan]::FromHours(24)
    )
    $latest = $null
    foreach ($n in $Names) {
        $t = Get-PfBackupTimestamp -Name $n
        if ($null -ne $t -and ($null -eq $latest -or $t -gt $latest)) { $latest = $t }
    }
    if ($null -eq $latest) { return $true }
    return (($NowUtc - $latest) -ge $MaxAge)
}

function Get-PfFileSha256 {
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)][string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function New-PfManifestText {
    <# Same format as backup.sh's manifest.txt, LF endings, so either platform can restore the archive. #>
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][string]$Stamp,
        [Parameter(Mandatory)][string]$HostName,
        [Parameter(Mandatory)][string]$DumpSha256,
        [Parameter(Mandatory)][string]$KeysSha256
    )
    return "created_utc=$Stamp`nhost=$HostName`ndump_sha256=$DumpSha256`nkeys_sha256=$KeysSha256`n"
}

function Test-PfManifest {
    <# True when the manifest lists exactly these checksums (as restore.sh checks). #>
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$ManifestText,
        [Parameter(Mandatory)][string]$DumpSha256,
        [Parameter(Mandatory)][string]$KeysSha256
    )
    $values = ConvertFrom-PfEnvText -Text $ManifestText
    return ($values.Contains('dump_sha256') -and $values.Contains('keys_sha256') -and
        $values['dump_sha256'] -eq $DumpSha256.ToLowerInvariant() -and
        $values['keys_sha256'] -eq $KeysSha256.ToLowerInvariant())
}

function Find-PfTool {
    <# A tool on PATH (winget/scoop shims) or in <install>\tools\<name>\. #>
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)]$Paths)
    $cmd = Get-Command $Name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($cmd) { return $cmd.Source }
    $local = Join-Path (Join-Path (Join-Path $Paths.Root 'tools') $Name) "$Name.exe"
    if (Test-Path -LiteralPath $local) { return $local }
    return $null
}

# --------------------------------------------------------------------------------------------------------------------
# Desktop integration
# --------------------------------------------------------------------------------------------------------------------

function Get-PfLauncherCommand {
    <# powershell.exe arguments to run a script with no console window, regardless of the execution policy. #>
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)][string]$Script, [string]$Extra = '')
    $text = "-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$Script`""
    if ($Extra) { $text = "$text $Extra" }
    return $text
}

function Get-PfAppBrowser {
    <# Edge, then Chrome (both support --app windows); $null means "use the default browser". #>
    [CmdletBinding()]
    param()
    $candidates = @(
        @{ Name = 'msedge.exe'; Paths = @('Microsoft\Edge\Application\msedge.exe') },
        @{ Name = 'chrome.exe'; Paths = @('Google\Chrome\Application\chrome.exe') }
    )
    $roots = @(${env:ProgramFiles(x86)}, $env:ProgramFiles, $env:LOCALAPPDATA) | Where-Object { $_ }
    foreach ($c in $candidates) {
        $appPath = Get-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\$($c.Name)" -ErrorAction SilentlyContinue
        if ($appPath -and ($appPath.PSObject.Properties.Name -contains '(default)')) {
            $exe = $appPath.'(default)'
            if ($exe -and (Test-Path -LiteralPath $exe)) { return $exe }
        }
        foreach ($root in $roots) {
            foreach ($rel in $c.Paths) {
                $p = Join-Path $root $rel
                if (Test-Path -LiteralPath $p) { return $p }
            }
        }
    }
    return $null
}

function Get-PfAppWindowArgument {
    <#
    Arguments that open the app in its own window with a dedicated browser profile: no extensions from everyday
    browsing can read the pages, and the session cookie lives apart from the main profile.
    #>
    [CmdletBinding()]
    [OutputType([string[]])]
    param([Parameter(Mandatory)][string]$Url, [Parameter(Mandatory)][string]$ProfileDir)
    return @("--app=$Url", "--user-data-dir=`"$ProfileDir`"", '--no-first-run', '--no-default-browser-check',
        '--disable-sync')
}

function Open-PfAppWindow {
    [CmdletBinding(SupportsShouldProcess)]
    param([Parameter(Mandatory)][string]$Url, [Parameter(Mandatory)][string]$ProfileDir)
    if (-not $PSCmdlet.ShouldProcess($Url, 'Open')) { return }
    $browser = Get-PfAppBrowser
    if ($browser) {
        Start-Process -FilePath $browser -ArgumentList (Get-PfAppWindowArgument -Url $Url -ProfileDir $ProfileDir) | Out-Null
    }
    else {
        Start-Process -FilePath $Url | Out-Null
    }
}

function New-PfShortcut {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Target,
        [string]$Arguments = '',
        [string]$Icon = '',
        [string]$WorkingDirectory = '',
        [string]$Description = ''
    )
    if (-not $PSCmdlet.ShouldProcess($Path, 'Create shortcut')) { return }
    $dir = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $shell = New-Object -ComObject WScript.Shell
    try {
        $lnk = $shell.CreateShortcut($Path)
        $lnk.TargetPath = $Target
        $lnk.Arguments = $Arguments
        if ($Icon) { $lnk.IconLocation = "$Icon,0" }
        if ($WorkingDirectory) { $lnk.WorkingDirectory = $WorkingDirectory }
        if ($Description) { $lnk.Description = $Description }
        $lnk.WindowStyle = 7 # minimized: the hidden PowerShell window never flashes
        $lnk.Save()
    }
    finally {
        [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
    }
}

function Get-PfShortcutPath {
    <# Start Menu, Desktop and (fallback auto-start) Startup shortcut locations for the current user. #>
    [CmdletBinding()]
    param()
    [pscustomobject]@{
        StartMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'Personal Finance.lnk'
        Desktop   = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Personal Finance.lnk'
        Startup   = Join-Path ([Environment]::GetFolderPath('Startup')) 'Personal Finance (start in background).lnk'
    }
}

$script:TaskNames = @{
    AutoStart = 'PersonalFinance AutoStart'
    Backup    = 'PersonalFinance Backup'
}

function Get-PfTaskName {
    [CmdletBinding()]
    param()
    return [pscustomobject]$script:TaskNames
}

function Show-PfMessage {
    <# A message box (works without a console, e.g. from the hidden launcher). #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Text, [ValidateSet('Information', 'Error', 'Warning')][string]$Kind = 'Information')
    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
        [void][System.Windows.Forms.MessageBox]::Show($Text, 'Personal Finance', 'OK', $Kind)
    }
    catch {
        Write-Host $Text
    }
}

function New-PfTrayStatus {
    <#
    A tray icon with a balloon notification, shown while a slow start-up is in progress. Returns $null where Windows
    Forms is unavailable. Dispose it with Close-PfTrayStatus.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param([Parameter(Mandatory)][string]$Text, [string]$Icon = '')
    if (-not $PSCmdlet.ShouldProcess('tray', 'Show status')) { return $null }
    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
        Add-Type -AssemblyName System.Drawing -ErrorAction Stop
        $tray = New-Object System.Windows.Forms.NotifyIcon
        if ($Icon -and (Test-Path -LiteralPath $Icon)) {
            $tray.Icon = New-Object System.Drawing.Icon($Icon)
        }
        else {
            $tray.Icon = [System.Drawing.SystemIcons]::Information
        }
        $tray.Text = 'Personal Finance - starting'
        $tray.Visible = $true
        $tray.ShowBalloonTip(10000, 'Personal Finance', $Text, [System.Windows.Forms.ToolTipIcon]::Info)
        return $tray
    }
    catch {
        return $null
    }
}

function Close-PfTrayStatus {
    [CmdletBinding()]
    param($Tray)
    if ($null -ne $Tray) {
        $Tray.Visible = $false
        $Tray.Dispose()
    }
}

function Register-PfTask {
    <#
    Registers (or replaces) a Scheduled Task that runs a script hidden, as the current user, only while signed in
    (Docker Desktop runs in the user's session). No administrator rights needed. Returns $false when Task Scheduler
    refuses (e.g. blocked by policy).
    #>
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Script,
        [string]$ScriptArguments = '',
        [Parameter(Mandatory)][ValidateSet('Logon', 'Daily')][string]$When,
        [string]$At = '03:30',
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$Description,
        [TimeSpan]$TimeLimit = [TimeSpan]::FromHours(1)
    )
    if (-not $PSCmdlet.ShouldProcess($Name, 'Register scheduled task')) { return $false }
    try {
        $user = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
        $exe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $action = New-ScheduledTaskAction -Execute $exe -WorkingDirectory $WorkingDirectory `
            -Argument (Get-PfLauncherCommand -Script $Script -Extra $ScriptArguments)
        if ($When -eq 'Logon') {
            $trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
            $trigger.Delay = 'PT30S' # let the desktop settle first
        }
        else {
            $trigger = New-ScheduledTaskTrigger -Daily -At $At
        }
        $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
        # StartWhenAvailable: a run missed while the PC was off or asleep starts as soon as possible afterwards.
        $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
            -MultipleInstances IgnoreNew -ExecutionTimeLimit $TimeLimit
        Register-ScheduledTask -TaskName $Name -Description $Description -Action $action -Trigger $trigger `
            -Principal $principal -Settings $settings -Force -ErrorAction Stop | Out-Null
        return $true
    }
    catch {
        Write-PfLog -Message "Could not register the scheduled task '$Name': $($_.Exception.Message)"
        return $false
    }
}

function Unregister-PfTask {
    [CmdletBinding(SupportsShouldProcess)]
    param([Parameter(Mandatory)][string]$Name)
    $task = Get-ScheduledTask -TaskName $Name -ErrorAction SilentlyContinue
    if ($task -and $PSCmdlet.ShouldProcess($Name, 'Unregister scheduled task')) {
        Unregister-ScheduledTask -TaskName $Name -Confirm:$false
    }
}

Export-ModuleMember -Function *-Pf*
