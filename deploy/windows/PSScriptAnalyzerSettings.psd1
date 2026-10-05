# PSScriptAnalyzer settings for deploy/windows (run in CI on windows-latest):
#   Invoke-ScriptAnalyzer -Path deploy/windows -Recurse -Settings deploy/windows/PSScriptAnalyzerSettings.psd1
@{
    Severity     = @('Error', 'Warning')
    ExcludeRules = @(
        # Interactive installers talk to the person running them; the hidden launcher logs to a file instead.
        'PSAvoidUsingWriteHost',
        # Builders such as New-PfSecret / New-PfEnvSetting change no state; real side effects use ShouldProcess.
        'PSUseShouldProcessForStateChangingFunctions'
    )
    Rules        = @{
        # The scripts must keep running on the Windows PowerShell 5.1 that ships with Windows.
        PSUseCompatibleSyntax = @{
            Enable         = $true
            TargetVersions = @('5.1', '7.0')
        }
    }
}
