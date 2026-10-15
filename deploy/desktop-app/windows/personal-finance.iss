; Windows installer for the desktop app (ADR-0010). Built by .github/workflows/desktop.yml:
;   iscc /DSourceDir=<published app> /DAppVersion=1.2.3 personal-finance.iss
; Per-user install (no administrator rights), Start menu and optional desktop shortcut. Uninstalling keeps the data in
; %LOCALAPPDATA%\PersonalFinanceApp\data unless the user deletes it.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{6B3F2B8E-5A61-4C1D-9C7E-2E8F4A5D9B10}
AppName=Personal Finance
AppVersion={#AppVersion}
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
