#define MyAppName "Controledu Student"
#define MyAppPublisher "Controledu"
#define MyAppExeName "Controledu.Student.Host.exe"
#define AgentExeName "Controledu.Student.Agent.exe"

#ifndef MyAppVersion
  #define MyAppVersion "0.1.8b"
#endif

#ifndef SourceHostDir
  #define SourceHostDir "..\\..\\artifacts\\publish\\student-host"
#endif

#ifndef SourceAgentDir
  #define SourceAgentDir "..\\..\\artifacts\\publish\\student-agent"
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
AppId={{89D194CA-DF30-4FD7-853D-5140E3D101B1}
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
DefaultDirName={autopf}\Controledu\Student
DefaultGroupName=Controledu
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir={#OutputDir}
OutputBaseFilename=StudentInstaller
Compression=lzma
SolidCompression=yes
WizardStyle=modern
SetupIconFile={#AppIconFile}
LicenseFile={#LicenseFile}
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName},{#AgentExeName}
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
english.TaskAgentService=Run Student.Agent as Windows Service
russian.TaskAgentService=Запускать Student.Agent как службу Windows
english.GroupAgentStartup=Student.Agent startup:
russian.GroupAgentStartup=Запуск Student.Agent:
english.LaunchStudent=Launch Controledu Student
russian.LaunchStudent=Запустить Controledu Student

[Tasks]
Name: "desktopicon"; Description: "{cm:TaskDesktopIcon}"; GroupDescription: "{cm:GroupAdditionalIcons}"
Name: "agentservice"; Description: "{cm:TaskAgentService}"; GroupDescription: "{cm:GroupAgentStartup}"

[Files]
Source: "{#SourceHostDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.xml,appsettings.Development.json"
Source: "{#SourceAgentDir}\*"; DestDir: "{app}\StudentAgent"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.xml,appsettings.Development.json"

[Icons]
Name: "{autoprograms}\Controledu Student"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\Controledu Student"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command ""$profiles = @(Get-NetConnectionProfile -ErrorAction SilentlyContinue | Where-Object {{ $_.NetworkCategory -eq 'Public' -and $_.IPv4Connectivity -ne 'Disconnected' }}); foreach ($p in $profiles) {{ Set-NetConnectionProfile -InterfaceIndex $p.InterfaceIndex -NetworkCategory Private -ErrorAction SilentlyContinue }}"""; Flags: runhidden
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchStudent}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#MyAppExeName} /T"; Flags: runhidden; RunOnceId: "ControleduStudentHostTaskKill"
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#AgentExeName} /T"; Flags: runhidden; RunOnceId: "ControleduStudentAgentTaskKill"

[UninstallDelete]
; Remove all local Controledu data (shared SQLite, logs, exports, etc.) on uninstall.
Type: filesandordirs; Name: "{commonappdata}\Controledu"
Type: filesandordirs; Name: "{localappdata}\Controledu"

[Code]
var
  AgentServiceExistedBeforeInstall: Boolean;

function AgentServiceExists(): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\sc.exe'), 'query ControleduStudentAgent', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

procedure StopAgentServiceIfPresent();
var
  ResultCode: Integer;
begin
  if AgentServiceExists() then
  begin
    AgentServiceExistedBeforeInstall := True;
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop ControleduStudentAgent', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

procedure StartAgentServiceIfPresent();
var
  ResultCode: Integer;
begin
  if AgentServiceExists() then
  begin
    Exec(ExpandConstant('{sys}\sc.exe'), 'start ControleduStudentAgent', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

procedure RegisterAgentService();
var
  ResultCode: Integer;
  BinPath: String;
begin
  BinPath := '"' + ExpandConstant('{app}\StudentAgent\{#AgentExeName}') + '" --service';

  if not AgentServiceExists() then
  begin
    Exec(ExpandConstant('{sys}\sc.exe'), 'create ControleduStudentAgent binPath= "' + BinPath + '" start= auto DisplayName= "Controledu Student Agent"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;

  Exec(ExpandConstant('{sys}\sc.exe'), 'start ControleduStudentAgent', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure RemoveAgentService();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop ControleduStudentAgent', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\sc.exe'), 'delete ControleduStudentAgent', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    StopAgentServiceIfPresent();
  end;

  if CurStep = ssPostInstall then
  begin
    if WizardIsTaskSelected('agentservice') then
    begin
      RegisterAgentService();
    end
    else if AgentServiceExistedBeforeInstall then
    begin
      StartAgentServiceIfPresent();
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    RemoveAgentService();
  end;
end;
