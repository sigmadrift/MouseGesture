; Inno Setup script for MouseGesture
; Build via scripts/publish.ps1 (which passes /D defines).

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#define MyAppName       "MouseGesture"
#define MyAppPublisher  "ksy"
#define MyAppExeName    "MouseGesture.App.exe"
#define MyAppId         "{{B1F2C4D8-7A55-4D88-9F0E-7C9F0E1D9A11}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir={#OutputDir}
OutputBaseFilename=MouseGestureSetup-{#MyAppVersion}
SetupIconFile=..\src\MouseGesture.App\Assets\app.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
CloseApplications=force
RestartApplications=no

[Languages]
Name: "korean";  MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon";    Description: "{cm:CreateDesktopIcon}";   GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart";      Description: "Windows 시작 시 자동 실행"; GroupDescription: "옵션:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}";              Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{#MyAppName} 제거";         Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}";        Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; HKCU autostart entry — only created if user opted in. Removed on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MouseGesture"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{#MyAppName} 실행"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Best-effort: kill running instance before uninstall.
Filename: "{cmd}"; Parameters: "/C taskkill /IM {#MyAppExeName} /F"; Flags: runhidden; RunOnceId: "KillApp"
