; Windows installer for the desktop app (ADR-0010). Built by .github/workflows/desktop.yml:
;   iscc /DSourceDir=<published app> /DAppVersion=1.2.3 personal-finance.iss
; Per-user install (no administrator rights), Start menu and optional desktop shortcut. Uninstalling asks whether to
; delete the data in %LOCALAPPDATA%\PersonalFinanceApp too (kept by default).

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{6B3F2B8E-5A61-4C1D-9C7E-2E8F4A5D9B10}
AppName=Personal Finance
AppVersion={#AppVersion}
; Shown as just the name in the wizard and in Settings → Apps (the version has its own column there).
AppVerName=Personal Finance
UninstallDisplayName=Personal Finance
AppPublisher=Personal Finance (open source)
AppPublisherURL=https://github.com/joaoguimaraespro/personal-finance
DefaultDirName={localappdata}\Programs\PersonalFinanceApp
DefaultGroupName=Personal Finance
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=PersonalFinance-Setup-{#AppVersion}-x64
SetupIconFile=..\..\windows\personal-finance.ico
UninstallDisplayIcon={app}\PersonalFinance.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "portuguese"; MessagesFile: "compiler:Languages\Portuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Personal Finance"; Filename: "{app}\PersonalFinance.exe"; IconFilename: "{app}\personal-finance.ico"
Name: "{autodesktop}\Personal Finance"; Filename: "{app}\PersonalFinance.exe"; IconFilename: "{app}\personal-finance.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\PersonalFinance.exe"; Description: "{cm:LaunchProgram,Personal Finance}"; Flags: nowait postinstall skipifsilent

[Code]
// Uninstalling asks whether to delete the data too (database, broker keys, logs); by default it is kept, so
// reinstalling or updating finds everything as it was.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\PersonalFinanceApp');
    if DirExists(DataDir) and not UninstallSilent and
       (MsgBox('Also delete your Personal Finance data (database, broker keys and logs) from this computer?' + #13#10#13#10 +
               'Choose No to keep it for a later reinstall. Deleted data cannot be recovered without an export.',
               mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
      DelTree(DataDir, True, True, True);
  end;
end;
