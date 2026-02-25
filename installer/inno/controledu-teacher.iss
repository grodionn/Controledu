#define MyAppName "Controledu Teacher"
#define MyAppPublisher "Controledu"
#define MyAppExeName "Controledu.Teacher.Host.exe"
#define AppMutexName "ControleduTeacherHostMutex"

#ifndef MyAppVersion
  #define MyAppVersion "0.1.8b"
#endif

#ifndef SourceDir
  #define SourceDir "..\\..\\artifacts\\publish\\teacher-host"
#endif

#ifndef OutputDir
  #define OutputDir "..\\..\\artifacts\\installers"
#endif

#ifndef AppIconFile
  #define AppIconFile "..\\..\\favicon.ico"
#endif

#ifndef LicenseFile
  #define LicenseFile "eula-ru.txt"
#endif

[Setup]
AppId={{F90F6E82-2A20-4A7D-A5A3-87A0D70208D4}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
CreateUninstallRegKey=yes
Uninstallable=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
AppMutex={#AppMutexName}
DefaultDirName={autopf}\Controledu\Teacher
DefaultGroupName=Controledu
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir={#OutputDir}
OutputBaseFilename=TeacherInstaller
Compression=lzma
SolidCompression=yes
WizardStyle=modern
SetupIconFile={#AppIconFile}
LicenseFile={#LicenseFile}
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName}
RestartApplications=no
ChangesEnvironment=no
ChangesAssociations=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
english.TaskDesktopIcon=Create a desktop shortcut
russian.TaskDesktopIcon=Создать ярлык на рабочем столе
english.GroupAdditionalIcons=Additional icons:
russian.GroupAdditionalIcons=Дополнительные значки:
english.LaunchTeacher=Launch Controledu Teacher
russian.LaunchTeacher=Запустить Controledu Teacher

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Controledu Teacher"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\Controledu Teacher"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:TaskDesktopIcon}"; GroupDescription: "{cm:GroupAdditionalIcons}"

[Run]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command ""$profiles = @(Get-NetConnectionProfile -ErrorAction SilentlyContinue | Where-Object {{ $_.NetworkCategory -eq 'Public' -and $_.IPv4Connectivity -ne 'Disconnected' }}); foreach ($p in $profiles) {{ Set-NetConnectionProfile -InterfaceIndex $p.InterfaceIndex -NetworkCategory Private -ErrorAction SilentlyContinue }}"""; Flags: runhidden
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""Controledu Teacher Server TCP 40556"" dir=in action=allow protocol=TCP localport=40556 profile=private"; Flags: runhidden
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""Controledu Teacher Discovery UDP 40555"" dir=in action=allow protocol=UDP localport=40555 profile=private"; Flags: runhidden
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchTeacher}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#MyAppExeName} /T"; Flags: runhidden; RunOnceId: "ControleduTeacherHostTaskKill"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""Controledu Teacher Server TCP 40556"""; Flags: runhidden; RunOnceId: "ControleduTeacherFirewallTcp40556"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""Controledu Teacher Discovery UDP 40555"""; Flags: runhidden; RunOnceId: "ControleduTeacherFirewallUdp40555"

[UninstallDelete]
; Remove all local Controledu data on uninstall.
Type: filesandordirs; Name: "{commonappdata}\Controledu"
Type: filesandordirs; Name: "{localappdata}\Controledu"
