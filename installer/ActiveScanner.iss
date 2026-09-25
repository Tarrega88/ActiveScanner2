; Inno Setup script for ActiveScanner
; Wraps the single-file published ActiveScanner.exe into a per-user Setup.exe.
;
; Build steps (handled automatically by build-installer.ps1):
;   1. dotnet publish -c Release -p:PublishProfile=SingleFile
;   2. ISCC.exe installer\ActiveScanner.iss
;
; Output: installer\output\ActiveScanner-Setup-<version>.exe

#define MyAppName "ActiveScanner"
#define MyAppPublisher "ActiveScanner Team"
#define MyAppExeName "ActiveScanner.exe"
; Version can be overridden from the command line: ISCC /DMyAppVersion=1.2.3
#ifndef MyAppVersion
  #define MyAppVersion "1.1.4"
#endif

[Setup]
AppId={{8F3C2A41-6B7D-4E2A-9C1F-ActiveScanner}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
; Per-user install: no admin rights required
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=ActiveScanner-Setup-{#MyAppVersion}
SetupIconFile=..\Resources\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\publish\single-file\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
