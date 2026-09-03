; Harbor — Windows installer (Inno Setup 6)
; Build: publish first, then compile this script from this directory:
;   dotnet publish ..\..\src\Harbor\Harbor.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
;   ISCC.exe harbor.iss
;
; Per-user install, no admin rights. The exe goes to %LOCALAPPDATA%\harbor; the
; configuration lives in %APPDATA%\Harbor (where Harbor always reads it), seeded
; empty on first install and never overwritten on upgrade.

#define AppVersion "0.2.0"

[Setup]
AppName=Harbor
AppVersion={#AppVersion}
AppPublisher=John Bowens
AppPublisherURL=https://github.com/jbowensii/Harbor
DefaultDirName={localappdata}\harbor
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
OutputBaseFilename=HarborSetup
OutputDir=Output
SetupIconFile=..\..\src\Harbor\harbor.ico
UninstallDisplayIcon={app}\harbor.ico
Compression=lzma2
SolidCompression=yes

[Files]
Source: "..\..\src\Harbor\bin\Release\net10.0-windows\win-x64\publish\Harbor.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\src\Harbor\harbor.ico"; DestDir: "{app}"; Flags: ignoreversion
; Empty starter configuration, written where Harbor actually reads it.
; onlyifdoesntexist: an existing config is never touched on reinstall/upgrade.
Source: "config.template.json"; DestDir: "{userappdata}\Harbor"; DestName: "servers.json"; Flags: onlyifdoesntexist uninsneveruninstall

[Icons]
; Icon comes from harbor.ico, not the exe — the shell extracts icons from
; single-file .NET executables unreliably (see README).
Name: "{userdesktop}\Harbor"; Filename: "{app}\Harbor.exe"; WorkingDir: "{app}"; IconFilename: "{app}\harbor.ico"

[Run]
Filename: "{app}\Harbor.exe"; Description: "Launch Harbor"; Flags: nowait postinstall skipifsilent
