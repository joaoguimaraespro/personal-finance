<#
.SYNOPSIS
    Restores a backup into the running stack (Windows port of deploy/scripts/restore.sh). DESTRUCTIVE: replaces all
    current data.

.DESCRIPTION
    Accepts archives made by Backup.ps1 or by backup.sh on the Linux server, so data can move either way.
    Decrypts, verifies the checksums, asks for confirmation, stops the app, restores the database as the schema owner,
    re-applies the least-privilege grants, restores the key ring and restarts.

    The identity (private key) file is brought in from offline storage only for the duration of the restore.

.EXAMPLE
    .\Restore.ps1 -Archive ..\..\backups\finance-20260925T221516Z.tar.age -Identity E:\finance-backup-identity.txt
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Archive,
    [Parameter(Mandatory)][string]$Identity,
    # Skip the typed confirmation (scripts and drills only).
    [switch]$Force
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

Import-Module (Join-Path $PSScriptRoot 'PersonalFinance.psm1') -Force -DisableNameChecking
$paths = Get-PfLayout -Root ([System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..')))
Start-PfLog -Directory $paths.Logs -Name 'restore'

$Archive = (Resolve-Path -LiteralPath $Archive).Path
$Identity = (Resolve-Path -LiteralPath $Identity).Path
$settings = Read-PfEnvFile -Path $paths.EnvFile
$age = Find-PfTool -Name 'age' -Paths $paths
if (-not $age) { throw 'age.exe is not installed: winget install --id FiloSottile.age -e (see docs/windows.md).' }
$tar = Join-Path $env:SystemRoot 'System32\tar.exe'

$work = Join-Path ([System.IO.Path]::GetTempPath()) ('pf-restore-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work -Force | Out-Null
try {
    $bundle = Join-Path $work 'bundle.tar'
    # age asks for the passphrase here if the identity file is protected with one.
    & $age -d -i $Identity -o $bundle $Archive
    if ($LASTEXITCODE -ne 0) { throw 'Decryption failed (wrong identity file?).' }
    Invoke-PfNative -FilePath $tar -Quiet -ArgumentList @('-xf', $bundle, '-C', $work) | Out-Null

    Write-Host 'Verifying checksums...'
    $dump = Join-Path $work 'finance.dump'
    $keys = Join-Path $work 'dp-keys.tar'
    $manifestPath = Join-Path $work 'manifest.txt'
    foreach ($f in @($dump, $keys, $manifestPath)) {
        if (-not (Test-Path -LiteralPath $f)) { throw "Not a Personal Finance backup: $(Split-Path -Leaf $f) is missing." }
    }
    $manifest = [System.IO.File]::ReadAllText($manifestPath)
    if (-not (Test-PfManifest -ManifestText $manifest -DumpSha256 (Get-PfFileSha256 -Path $dump) -KeysSha256 (Get-PfFileSha256 -Path $keys))) {
        throw 'Checksum mismatch: the archive is damaged. Nothing was changed.'
    }
    Write-Host $manifest

    if (-not $Force) {
        $answer = Read-Host "This will REPLACE all data in the running stack. Type 'restore' to continue"
        if ($answer -ne 'restore') { Write-Host 'Aborted.'; exit 1 }
    }

    Start-PfDockerDesktop
    Invoke-PfCompose -Paths $paths -ArgumentList @('stop', 'api', 'web') | Out-Null
    Invoke-PfCompose -Paths $paths -ArgumentList @('up', '-d', 'db') | Out-Null
    $ready = Wait-PfCondition -Timeout ([TimeSpan]::FromMinutes(2)) -Condition {
        Invoke-PfCompose -Paths $paths -Quiet -AllowFailure -ArgumentList @('exec', '-T', 'db', 'pg_isready', '-U', 'postgres', '-d', 'finance') | Out-Null
        $LASTEXITCODE -eq 0
    }
    if (-not $ready) { throw 'The database did not become ready.' }

    Push-Location -LiteralPath $work
    try {
        Invoke-PfCompose -Paths $paths -ArgumentList @('cp', 'finance.dump', 'db:/tmp/finance-restore.dump') | Out-Null
    }
    finally {
        Pop-Location
    }
    try {
        # Restore as the schema owner so the objects keep the least-privilege layout.
        Invoke-PfCompose -Paths $paths -ArgumentList @('exec', '-T', '-e', "PGPASSWORD=$($settings['FINANCE_MIGRATOR_PASSWORD'])", 'db',
            'pg_restore', '--clean', '--if-exists', '--no-owner', '--role=finance_migrator', '-U', 'finance_migrator',
            '-d', 'finance', '/tmp/finance-restore.dump') | Out-Null
    }
    finally {
        Invoke-PfCompose -Paths $paths -Quiet -AllowFailure -ArgumentList @('exec', '-T', 'db', 'rm', '-f', '/tmp/finance-restore.dump') | Out-Null
    }

    # The dump carries data and structure; privileges are always re-applied from the versioned layout.
    Invoke-PfCompose -Paths $paths -ArgumentList @('exec', '-T', 'db', 'psql', '-v', 'ON_ERROR_STOP=1', '-q', '-U', 'postgres',
        '-d', 'finance', '-f', '/docker-entrypoint-initdb.d/20-grants.sql') | Out-Null

    Invoke-PfCompose -Paths $paths -ArgumentList @('run', '--rm', '--no-deps', '-T', '--user', '1654:1654', '--entrypoint', 'sh',
        '-v', 'personal-finance_dp-keys:/keys', '-v', "${work}:/in:ro", 'web',
        '-c', 'rm -rf /keys/* && tar -C /keys -xf /in/dp-keys.tar') | Out-Null

    Invoke-PfCompose -Paths $paths -ArgumentList @('up', '-d') | Out-Null
    $port = [int]$settings['HTTP_PORT']
    if (-not (Wait-PfCondition -Condition { Test-PfAppReady -Port $port } -Timeout ([TimeSpan]::FromMinutes(3)))) {
        throw 'Restored, but the app does not answer yet. Check: docker compose -p personal-finance logs api'
    }
    Write-PfLog -Message 'Restore complete. Sign in and verify the latest transactions. Remove the identity file from this PC.'
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
