; Inno Setup script for Blitztext (per-user, self-contained).
; Build:  iscc Blitztext.iss      (optionally /DPublishDir="...\publish")
; Output: installer\Output\BlitztextSetup.exe
;
; Per-user install (no admin needed). Speech models are NOT bundled – they are downloaded
; from within the app into %APPDATA%\Blitztext\models and stay on the local machine.

#define MyAppName "Blitztext"
#ifndef MyAppVersion
  #define MyAppVersion "1.6.1"
#endif
#define MyAppPublisher "Sebastian Schutzbach"
#define MyAppExeName "Blitztext.exe"

#ifndef PublishDir
  #define PublishDir "..\src\Blitztext.App\bin\Release\net8.0-windows\win-x64\publish"
#endif

[Setup]
; Stable AppId so upgrades replace the previous install cleanly.
AppId={{B1A7E5C2-9D34-4E8F-A1B6-3C2D9F0E7A45}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\Blitztext
DefaultGroupName=Blitztext
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=BlitztextSetup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\Blitztext.App\Resources\blitztext.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Close a running Blitztext before overwriting files (e.g. on update).
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
; Asked on the wizard's task page (shown during an interactive install).
Name: "autostart"; Description: "Blitztext automatisch mit Windows starten (beim Anmelden)"; GroupDescription: "Mit Windows starten:"; Flags: unchecked
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\Blitztext"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Blitztext deinstallieren"; Filename: "{uninstallexe}"
Name: "{userdesktop}\Blitztext"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Optional autostart (same HKCU Run value the app's settings toggle uses).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
  ValueName: "Blitztext"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
; Interactive install: offer to launch at the end.
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,Blitztext}"; Flags: nowait postinstall skipifsilent
; Silent in-app update: relaunch the app automatically when started with /RELAUNCH.
Filename: "{app}\{#MyAppExeName}"; Flags: nowait; Check: RelaunchRequested

[Code]
function RelaunchRequested: Boolean;
var
  i: Integer;
begin
  Result := False;
  for i := 1 to ParamCount do
    if CompareText(ParamStr(i), '/RELAUNCH') = 0 then
      Result := True;
end;
