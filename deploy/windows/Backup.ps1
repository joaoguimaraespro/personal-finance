<#
.SYNOPSIS
    Encrypted backup of the database and the Data Protection key ring (Windows port of deploy/scripts/backup.sh).

.DESCRIPTION
    Output: <BACKUP_DIR>\finance-<UTC timestamp>.tar.age, encrypted with age to BACKUP_AGE_RECIPIENT (a public key).
    The archive has the same layout as backup.sh produces (finance.dump, dp-keys.tar, manifest.txt), so it restores
    on either platform with Restore.ps1 or restore.sh.

    Runs from the daily Scheduled Task (03:30, or as soon as possible after a missed run) and from the launcher when
    the newest backup is older than 24 h. Starts Docker Desktop and the stack first if they are not running.

    Retention: archives older than BACKUP_RETENTION_DAYS (default 35) are deleted, but never the newest one.
    Off-site copy (optional): BACKUP_REMOTE=<rclone remote:path> uploads every archive not yet there (rclone copy,
    never deletes remotely unless BACKUP_REMOTE_RETENTION_DAYS is set). See docs/windows.md.

    Needs age.exe (winget install --id FiloSottile.age -e) and, for the off-site copy, rclone.exe.
#>
[CmdletBinding()]
param([switch]$Quiet)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

Import-Module (Join-Path $PSScriptRoot 'PersonalFinance.psm1') -Force -DisableNameChecking
$paths = Get-PfLayout -Root ([System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..')))
Start-PfLog -Directory $paths.Logs -Name 'backup'

$mutex = New-Object System.Threading.Mutex($false, 'Local\PersonalFinanceBackup')
$owned = $false
try { $owned = $mutex.WaitOne(0) } catch [System.Threading.AbandonedMutexException] { $owned = $true }
if (-not $owned) {
    Write-PfLog -Message 'Another backup is already running.' -Quiet:$Quiet
    $mutex.Dispose()
    exit 0
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) ('pf-backup-' + [Guid]::NewGuid().ToString('N'))
$partial = $null
$exitCode = 0
try {
    $settings = Read-PfEnvFile -Path $paths.EnvFile
    $recipient = $settings['BACKUP_AGE_RECIPIENT']
    if (-not (Test-PfAgeRecipient -Recipient $recipient)) {
        throw 'BACKUP_AGE_RECIPIENT (an age public key, age1...) is not set in deploy\.env. Run Install.ps1 -BackupRecipient age1...'
    }
    $backupDir = $paths.Backups
    if ($settings.Contains('BACKUP_DIR') -and $settings['BACKUP_DIR']) { $backupDir = $settings['BACKUP_DIR'] }
    $retention = 35
    if ($settings.Contains('BACKUP_RETENTION_DAYS') -and $settings['BACKUP_RETENTION_DAYS']) { $retention = [int]$settings['BACKUP_RETENTION_DAYS'] }
    $age = Find-PfTool -Name 'age' -Paths $paths
    if (-not $age) { throw 'age.exe is not installed: winget install --id FiloSottile.age -e (see docs/windows.md).' }
    $tar = Join-Path $env:SystemRoot 'System32\tar.exe'
    if (-not (Test-Path -LiteralPath $tar)) { throw 'tar.exe was not found (it ships with Windows 10 1803 and later).' }

    Write-PfLog -Message 'Backup started.' -Quiet:$Quiet
    Start-PfStack -Paths $paths | Out-Null

    $stamp = [DateTime]::UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", [Globalization.CultureInfo]::InvariantCulture)
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    if (-not (Test-Path -LiteralPath $backupDir)) { New-Item -ItemType Directory -Path $backupDir -Force | Out-Null }

    # Consistent logical dump with the read-only backup role. Written inside the container and copied out with
    # `docker compose cp`: piping binary output through PowerShell would corrupt it.
    Invoke-PfCompose -Paths $paths -Quiet:$Quiet -ArgumentList @('exec', '-T', '-e', "PGPASSWORD=$($settings['FINANCE_BACKUP_PASSWORD'])", 'db',
        'pg_dump', '--format=custom', '--no-owner', '--no-privileges', '-U', 'finance_backup', '-d', 'finance',
        '-f', '/tmp/finance-backup.dump') | Out-Null
    Push-Location -LiteralPath $work
    try {
        Invoke-PfCompose -Paths $paths -Quiet:$Quiet -ArgumentList @('cp', 'db:/tmp/finance-backup.dump', 'finance.dump') | Out-Null
    }
    finally {
        Pop-Location
        Invoke-PfCompose -Paths $paths -Quiet -AllowFailure -ArgumentList @('exec', '-T', 'db', 'rm', '-f', '/tmp/finance-backup.dump') | Out-Null
    }

    # The key ring decrypts stored account identifiers and keeps sessions valid after a restore. Key files belong to
    # the API user (uid 1654), so read them as that user.
    Invoke-PfCompose -Paths $paths -Quiet:$Quiet -ArgumentList @('run', '--rm', '--no-deps', '-T', '--user', '1654:1654',
        '--entrypoint', 'tar', '-v', 'personal-finance_dp-keys:/keys:ro', '-v', "${work}:/out", 'web',
        '-C', '/keys', '-cf', '/out/dp-keys.tar', '.') | Out-Null

    $dump = Join-Path $work 'finance.dump'
    $keys = Join-Path $work 'dp-keys.tar'
    foreach ($f in @($dump, $keys)) {
        if (-not (Test-Path -LiteralPath $f) -or (Get-Item -LiteralPath $f).Length -eq 0) { throw "Backup part missing or empty: $f" }
    }
    $manifest = New-PfManifestText -Stamp $stamp -HostName $env:COMPUTERNAME `
        -DumpSha256 (Get-PfFileSha256 -Path $dump) -KeysSha256 (Get-PfFileSha256 -Path $keys)
    [System.IO.File]::WriteAllText((Join-Path $work 'manifest.txt'), $manifest, (New-Object System.Text.ASCIIEncoding))

    $bundle = Join-Path $work 'bundle.tar'
    Invoke-PfNative -FilePath $tar -Quiet -ArgumentList @('-cf', $bundle, '-C', $work, 'finance.dump', 'dp-keys.tar', 'manifest.txt') | Out-Null
    $target = Join-Path $backupDir "finance-$stamp.tar.age"
    $partial = "$target.partial"
    Invoke-PfNative -FilePath $age -Quiet -ArgumentList @('-r', $recipient, '-o', $partial, $bundle) | Out-Null
    Move-Item -LiteralPath $partial -Destination $target -Force
    $sizeMb = [Math]::Round((Get-Item -LiteralPath $target).Length / 1MB, 2)
    Write-PfLog -Message "Backup written: $target ($sizeMb MB)" -Quiet:$Quiet

    $names = @(Get-ChildItem -LiteralPath $backupDir -Filter 'finance-*.tar.age' -File | ForEach-Object { $_.Name })
    foreach ($old in (Get-PfExpiredBackup -Names $names -NowUtc ([DateTime]::UtcNow) -RetentionDays $retention)) {
        Remove-Item -LiteralPath (Join-Path $backupDir $old) -Force
        Write-PfLog -Message "Removed old backup $old" -Quiet:$Quiet
    }

    $remote = ''
    if ($settings.Contains('BACKUP_REMOTE')) { $remote = $settings['BACKUP_REMOTE'] }
    if ($remote) {
        $rclone = Find-PfTool -Name 'rclone' -Paths $paths
        if (-not $rclone) { throw 'OFF-SITE COPY FAILED: rclone is not installed (winget install --id Rclone.Rclone -e). The local backup is fine.' }
        # copy (not sync): never deletes remotely; also catches up on uploads missed while the PC was off.
        $copy = Invoke-PfNative -FilePath $rclone -Quiet:$Quiet -AllowFailure -ArgumentList @('copy', $backupDir, $remote,
            '--include', 'finance-*.tar.age', '--immutable', '--retries', '5')
        if ($LASTEXITCODE -ne 0) {
            throw "OFF-SITE COPY FAILED to $remote. The local backup is fine; the next run retries. $($copy -join ' ')"
        }
        $remoteDays = ''
        if ($settings.Contains('BACKUP_REMOTE_RETENTION_DAYS')) { $remoteDays = $settings['BACKUP_REMOTE_RETENTION_DAYS'] }
        if ($remoteDays) {
            Invoke-PfNative -FilePath $rclone -Quiet:$Quiet -AllowFailure -ArgumentList @('delete', $remote,
                '--include', 'finance-*.tar.age', '--min-age', "${remoteDays}d") | Out-Null
            if ($LASTEXITCODE -ne 0) { Write-PfLog -Message 'Warning: could not prune old remote copies (credential without delete permission?).' -Quiet:$Quiet }
        }
        Write-PfLog -Message "Off-site copy up to date: $remote" -Quiet:$Quiet
    }
}
catch {
    $exitCode = 1
    Write-PfLog -Message "BACKUP FAILED: $($_.Exception.Message)" -Quiet:$Quiet
    if (-not $Quiet) { Write-Error $_.Exception.Message -ErrorAction Continue }
}
finally {
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
    if ($partial -and (Test-Path -LiteralPath $partial)) { Remove-Item -LiteralPath $partial -Force -ErrorAction SilentlyContinue }
    $mutex.ReleaseMutex()
    $mutex.Dispose()
}
exit $exitCode
