#define MyAppName "LangLayoutBeacon"
#ifndef MyAppVersion
  #define MyAppVersion "1.1.0"
#endif
#define MyAppPublisher "Pavel / Newton"
#define MyAppExeName "LangLayoutBeacon.exe"

#ifndef PublishDir
  #define PublishDir "build\\publish-fd-single"
#endif

#ifndef OutputName
  #define OutputName "LangLayoutBeacon_setup_fd-single"
#endif

[Setup]
AppId={{6C1EF1EA-4A7D-4D40-AB6F-4D9D5D6A2F11}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableDirPage=no
OutputDir=build\installer
OutputBaseFilename={#OutputName}
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=lowest

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
