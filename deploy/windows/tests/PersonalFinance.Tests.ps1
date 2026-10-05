# Pester 5 unit tests for the pure helpers in PersonalFinance.psm1 (no Docker, no Windows APIs needed).
#   Invoke-Pester -Path deploy/windows/tests
# CI runs them on windows-latest under both PowerShell 7 and Windows PowerShell 5.1.

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '..\PersonalFinance.psm1') -Force -DisableNameChecking
}

Describe 'New-PfSecret' {
    It 'returns 40 letters and digits by default' {
        $s = New-PfSecret
        $s.Length | Should -Be 40
        $s | Should -MatchExactly '^[A-Za-z0-9]{40}$'
    }

    It 'honours the requested length' {
        (New-PfSecret -Length 64).Length | Should -Be 64
    }

    It 'never repeats and uses the whole alphabet' {
        $many = 1..200 | ForEach-Object { New-PfSecret }
        ($many | Select-Object -Unique).Count | Should -Be 200
        $chars = ($many -join '').ToCharArray() | Select-Object -Unique
        # 8000 random characters from a 62-character alphabet: every character shows up.
        @($chars | Where-Object { $_ -cmatch '[A-Za-z0-9]' }).Count | Should -Be 62
    }
}

Describe 'Test-PfAgeRecipient' {
    It 'accepts an age public key' {
        Test-PfAgeRecipient -Recipient ('age1' + ('q' * 58)) | Should -BeTrue
    }

    It 'rejects private keys, wrong lengths, upper case and empty values' {
        Test-PfAgeRecipient -Recipient ('AGE-SECRET-KEY-1' + ('Q' * 58)) | Should -BeFalse
        Test-PfAgeRecipient -Recipient ('age1' + ('q' * 57)) | Should -BeFalse
        Test-PfAgeRecipient -Recipient ('AGE1' + ('Q' * 58)) | Should -BeFalse
        Test-PfAgeRecipient -Recipient '' | Should -BeFalse
        Test-PfAgeRecipient -Recipient $null | Should -BeFalse
    }
}

Describe 'deploy/.env settings' {
    It 'creates the same keys as server-deploy.sh, with fresh distinct secrets and loopback binding' {
        $s = New-PfEnvSetting -BackupDir 'C:\pf\backups' -HttpPort 8081 -BackupRecipient ('age1' + ('x' * 58))
        foreach ($key in 'POSTGRES_SUPERUSER_PASSWORD', 'FINANCE_MIGRATOR_PASSWORD', 'FINANCE_APP_PASSWORD',
            'FINANCE_BACKUP_PASSWORD', 'AUTH_SETUP_TOKEN', 'BIND_ADDRESS', 'HTTP_PORT', 'BACKUP_AGE_RECIPIENT',
            'BACKUP_DIR', 'BACKUP_RETENTION_DAYS', 'BACKUP_REMOTE', 'BACKUP_REMOTE_RETENTION_DAYS', 'INTEGRATIONS_ENABLE_DEMO') {
            $s.Contains($key) | Should -BeTrue -Because $key
        }
        $secrets = @($s['POSTGRES_SUPERUSER_PASSWORD'], $s['FINANCE_MIGRATOR_PASSWORD'], $s['FINANCE_APP_PASSWORD'],
            $s['FINANCE_BACKUP_PASSWORD'], $s['AUTH_SETUP_TOKEN'])
        ($secrets | Select-Object -Unique).Count | Should -Be 5
        $s['AUTH_SETUP_TOKEN'].Length | Should -BeGreaterOrEqual 24 # the API refuses shorter setup tokens
        $s['BIND_ADDRESS'] | Should -Be '127.0.0.1'
        $s['HTTP_PORT'] | Should -Be '8081'
        $s['INTEGRATIONS_ENABLE_DEMO'] | Should -Be 'false'
        $s['BACKUP_DIR'] | Should -Be 'C:\pf\backups'
    }

    It 'round-trips through text with LF endings' {
        $s = New-PfEnvSetting -BackupDir 'C:\Users\Jane Doe\AppData\Local\PersonalFinance\backups'
        $text = ConvertTo-PfEnvText -Settings $s
        $text | Should -Not -Match "`r"
        $text.EndsWith("`n") | Should -BeTrue
        $back = ConvertFrom-PfEnvText -Text $text
        foreach ($key in $s.Keys) { $back[$key] | Should -BeExactly $s[$key] }
    }

    It 'refuses values that would break the file' {
        { ConvertTo-PfEnvText -Settings ([ordered]@{ A = "x`ny" }) } | Should -Throw
        { ConvertTo-PfEnvText -Settings ([ordered]@{ 'bad key' = 'x' }) } | Should -Throw
    }

    It 'parses comments, blanks, CRLF and quoted values' {
        $parsed = ConvertFrom-PfEnvText -Text "# comment`r`n`r`nA=1`r`nB=`"two words`"`nC='3'`nD=`nE=x=y"
        $parsed['A'] | Should -Be '1'
        $parsed['B'] | Should -Be 'two words'
        $parsed['C'] | Should -Be '3'
        $parsed['D'] | Should -Be ''
        $parsed['E'] | Should -Be 'x=y'
        $parsed.Contains('# comment') | Should -BeFalse
    }

    It 'writes ASCII without a byte-order mark and with LF endings' {
        $path = Join-Path $TestDrive 'write.env'
        Write-PfEnvFile -Path $path -Settings ([ordered]@{ A = '1'; B = '2' })
        $bytes = [System.IO.File]::ReadAllBytes($path)
        $bytes[0] | Should -Be ([byte][char]'A')
        ($bytes -contains 13) | Should -BeFalse
        [System.IO.File]::ReadAllText($path) | Should -BeExactly "A=1`nB=2`n"
    }

    It 'changes one value and keeps everything else, or appends it' {
        $path = Join-Path $TestDrive 'set.env'
        [System.IO.File]::WriteAllText($path, "# keep me`nBACKUP_AGE_RECIPIENT=`nHTTP_PORT=8080`n")
        Set-PfEnvValue -Path $path -Name 'BACKUP_AGE_RECIPIENT' -Value 'age1abc'
        Set-PfEnvValue -Path $path -Name 'BACKUP_REMOTE' -Value 'b2:bucket/finance'
        [System.IO.File]::ReadAllText($path) |
            Should -BeExactly "# keep me`nBACKUP_AGE_RECIPIENT=age1abc`nHTTP_PORT=8080`nBACKUP_REMOTE=b2:bucket/finance`n"
    }
}

Describe 'Backup retention and schedule' {
    BeforeAll {
        $now = [DateTime]::SpecifyKind([DateTime]'2026-10-05T12:00:00', 'Utc')
    }

    It 'reads the UTC time from the archive name' {
        $t = Get-PfBackupTimestamp -Name 'finance-20260925T221516Z.tar.age'
        $t | Should -Be ([DateTime]::SpecifyKind([DateTime]'2026-09-25T22:15:16', 'Utc'))
        $t.Kind | Should -Be 'Utc'
        Get-PfBackupTimestamp -Name 'notes.txt' | Should -BeNullOrEmpty
        Get-PfBackupTimestamp -Name 'finance-20260925T221516Z.tar.age.partial' | Should -BeNullOrEmpty
    }

    It 'expires archives older than the retention period' {
        $names = @('finance-20260801T033000Z.tar.age', 'finance-20260830T033000Z.tar.age',
            'finance-20260901T033000Z.tar.age', 'finance-20261005T033000Z.tar.age', 'unrelated.tar.age')
        $expired = @(Get-PfExpiredBackup -Names $names -NowUtc $now -RetentionDays 35)
        $expired | Should -Be @('finance-20260801T033000Z.tar.age', 'finance-20260830T033000Z.tar.age')
    }

    It 'never deletes the newest archive, even after months switched off' {
        $names = @('finance-20260101T033000Z.tar.age', 'finance-20260102T033000Z.tar.age')
        @(Get-PfExpiredBackup -Names $names -NowUtc $now -RetentionDays 35) | Should -Be @('finance-20260101T033000Z.tar.age')
        @(Get-PfExpiredBackup -Names @('finance-20260101T033000Z.tar.age') -NowUtc $now -RetentionDays 35).Count | Should -Be 0
        @(Get-PfExpiredBackup -Names @() -NowUtc $now -RetentionDays 35).Count | Should -Be 0
    }

    It 'reports a backup as overdue after 24 hours or when there is none' {
        Test-PfBackupOverdue -Names @() -NowUtc $now | Should -BeTrue
        Test-PfBackupOverdue -Names @('finance-20261005T033000Z.tar.age') -NowUtc $now | Should -BeFalse
        Test-PfBackupOverdue -Names @('finance-20261004T033000Z.tar.age', 'x.txt') -NowUtc $now | Should -BeTrue
        Test-PfBackupOverdue -Names @('finance-20261004T120000Z.tar.age') -NowUtc $now | Should -BeTrue
        Test-PfBackupOverdue -Names @('finance-20261004T120001Z.tar.age') -NowUtc $now | Should -BeFalse
    }
}

Describe 'Backup manifest' {
    It 'matches the format restore.sh checks' {
        $text = New-PfManifestText -Stamp '20261005T033000Z' -HostName 'PC' -DumpSha256 ('a' * 64) -KeysSha256 ('b' * 64)
        $text | Should -BeExactly "created_utc=20261005T033000Z`nhost=PC`ndump_sha256=$('a' * 64)`nkeys_sha256=$('b' * 64)`n"
        Test-PfManifest -ManifestText $text -DumpSha256 ('a' * 64) -KeysSha256 ('b' * 64) | Should -BeTrue
        Test-PfManifest -ManifestText $text -DumpSha256 ('A' * 64) -KeysSha256 ('B' * 64) | Should -BeTrue
    }

    It 'detects a damaged archive or a missing line' {
        $text = New-PfManifestText -Stamp 's' -HostName 'h' -DumpSha256 ('a' * 64) -KeysSha256 ('b' * 64)
        Test-PfManifest -ManifestText $text -DumpSha256 ('c' * 64) -KeysSha256 ('b' * 64) | Should -BeFalse
        Test-PfManifest -ManifestText "dump_sha256=$('a' * 64)`n" -DumpSha256 ('a' * 64) -KeysSha256 ('b' * 64) | Should -BeFalse
        Test-PfManifest -ManifestText '' -DumpSha256 ('a' * 64) -KeysSha256 ('b' * 64) | Should -BeFalse
    }

    It 'hashes files as lower-case SHA-256' {
        $path = Join-Path $TestDrive 'abc.bin'
        [System.IO.File]::WriteAllText($path, 'abc')
        Get-PfFileSha256 -Path $path | Should -BeExactly 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad'
    }
}

Describe 'Wait-PfCondition' {
    BeforeEach {
        $state = @{ Now = [DateTime]'2026-10-05T12:00:00'; Sleeps = 0; Calls = 0 }
        $fakeNow = { $state.Now }.GetNewClosure()
        $fakeSleep = { param($span) $state.Now = $state.Now + $span; $state.Sleeps++ }.GetNewClosure()
    }

    It 'returns at once when the condition already holds' {
        Wait-PfCondition -Condition { $true } -Timeout ([TimeSpan]::FromMinutes(1)) -Now $fakeNow -Sleep $fakeSleep | Should -BeTrue
        $state.Sleeps | Should -Be 0
    }

    It 'keeps polling until the condition holds' {
        $condition = { $state.Calls++; $state.Calls -ge 4 }.GetNewClosure()
        Wait-PfCondition -Condition $condition -Timeout ([TimeSpan]::FromMinutes(1)) -Interval ([TimeSpan]::FromSeconds(2)) `
            -Now $fakeNow -Sleep $fakeSleep | Should -BeTrue
        $state.Sleeps | Should -Be 3
    }

    It 'gives up after the timeout' {
        Wait-PfCondition -Condition { $false } -Timeout ([TimeSpan]::FromSeconds(10)) -Interval ([TimeSpan]::FromSeconds(2)) `
            -Now $fakeNow -Sleep $fakeSleep | Should -BeFalse
        $state.Sleeps | Should -Be 5
        ($state.Now - [DateTime]'2026-10-05T12:00:00').TotalSeconds | Should -Be 10
    }

    It 'reports progress while waiting' {
        $ticks = @{ Count = 0 }
        $onWait = { $ticks.Count++ }.GetNewClosure()
        Wait-PfCondition -Condition { $false } -Timeout ([TimeSpan]::FromSeconds(4)) -Interval ([TimeSpan]::FromSeconds(2)) `
            -Now $fakeNow -Sleep $fakeSleep -OnWait $onWait | Should -BeFalse
        $ticks.Count | Should -Be 2
    }
}

Describe 'Command lines' {
    It 'derives every path from the install folder' {
        $p = Get-PfLayout -Root 'C:\Users\Jane Doe\AppData\Local\PersonalFinance'
        $p.EnvFile | Should -Be (Join-Path (Join-Path 'C:\Users\Jane Doe\AppData\Local\PersonalFinance' 'deploy') '.env')
        $p.ComposeWindows | Should -BeLike '*compose.windows.yml'
        $p.Launcher | Should -BeLike '*Start-PersonalFinance.ps1'
        $p.Icon | Should -BeLike '*personal-finance.ico'
    }

    It 'layers the Windows override on the server compose file, under the fixed project name' {
        $p = Get-PfLayout -Root 'C:\pf'
        $composeArgs = Get-PfComposeArgument -Paths $p
        $composeArgs[0] | Should -Be 'compose'
        ($composeArgs -join ' ') | Should -Match '--project-name personal-finance '
        $files = for ($i = 0; $i -lt $composeArgs.Count; $i++) { if ($composeArgs[$i] -eq '-f') { $composeArgs[$i + 1] } }
        $files | Should -Be @($p.ComposeFile, $p.ComposeWindows)
        $composeArgs[-1] | Should -Be $p.EnvFile
    }

    It 'runs scripts hidden and independent of the execution policy, with the path quoted' {
        $cmd = Get-PfLauncherCommand -Script 'C:\Users\Jane Doe\pf\Start-PersonalFinance.ps1' -Extra '-Background'
        $cmd | Should -Match '-WindowStyle Hidden'
        $cmd | Should -Match '-ExecutionPolicy Bypass'
        $cmd | Should -Match '-NoProfile'
        $cmd | Should -BeLike '*-File "C:\Users\Jane Doe\pf\Start-PersonalFinance.ps1" -Background'
    }

    It 'opens an app window on a dedicated profile, quoting paths with spaces' {
        $a = Get-PfAppWindowArgument -Url 'http://localhost:8080/' -ProfileDir 'C:\Users\Jane Doe\pf\browser-profile'
        $a | Should -Contain '--app=http://localhost:8080/'
        $a | Should -Contain '--user-data-dir="C:\Users\Jane Doe\pf\browser-profile"'
        $a | Should -Contain '--no-first-run'
    }
}
