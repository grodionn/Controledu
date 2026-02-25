@echo off
setlocal EnableExtensions

set ROOT=%~dp0..
set ARTIFACTS=%ROOT%\artifacts
set PUBLISH_ROOT=%ARTIFACTS%\publish
set INSTALLERS_ROOT=%ARTIFACTS%\installers
set UPDATES_ROOT=%ARTIFACTS%\updates

cd /d "%ROOT%"

echo [publish] stopping running Controledu-related processes
taskkill /F /IM Controledu.Teacher.Host.exe >nul 2>&1
taskkill /F /IM Controledu.Student.Host.exe >nul 2>&1
taskkill /F /IM Controledu.Student.Agent.exe >nul 2>&1
taskkill /F /IM msedgewebview2.exe >nul 2>&1
taskkill /F /IM dotnet.exe >nul 2>&1
taskkill /F /IM node.exe >nul 2>&1

if not exist "%ARTIFACTS%" mkdir "%ARTIFACTS%" || exit /b 1
if exist "%PUBLISH_ROOT%" rmdir /s /q "%PUBLISH_ROOT%"
mkdir "%PUBLISH_ROOT%" || exit /b 1

call :sync_assets || exit /b 1

call :build_ui apps\teacher-ui || exit /b 1
call :build_ui apps\student-ui || exit /b 1

echo [publish] dotnet restore
call dotnet restore Controledu.sln || exit /b 1

echo [publish] Teacher.Host
call dotnet publish src\Controledu.Teacher.Host\Controledu.Teacher.Host.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%PUBLISH_ROOT%\teacher-host" || exit /b 1

echo [publish] Student.Host
call dotnet publish src\Controledu.Student.Host\Controledu.Student.Host.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%PUBLISH_ROOT%\student-host" || exit /b 1

if not exist "%PUBLISH_ROOT%\teacher-host\wwwroot" mkdir "%PUBLISH_ROOT%\teacher-host\wwwroot" || exit /b 1
if not exist "%PUBLISH_ROOT%\student-host\wwwroot" mkdir "%PUBLISH_ROOT%\student-host\wwwroot" || exit /b 1
copy /y "%ROOT%\favicon.ico" "%PUBLISH_ROOT%\teacher-host\wwwroot\favicon.ico" >nul || exit /b 1
copy /y "%ROOT%\favicon.ico" "%PUBLISH_ROOT%\student-host\wwwroot\favicon.ico" >nul || exit /b 1

echo [publish] Student.Agent
call dotnet publish src\Controledu.Student.Agent\Controledu.Student.Agent.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%PUBLISH_ROOT%\student-agent" || exit /b 1

echo [publish] Controledu.Updater
call dotnet publish src\Controledu.Updater\Controledu.Updater.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%PUBLISH_ROOT%\updater" || exit /b 1

if exist "%PUBLISH_ROOT%\student-host\StudentAgent" rmdir /s /q "%PUBLISH_ROOT%\student-host\StudentAgent"
mkdir "%PUBLISH_ROOT%\student-host\StudentAgent" || exit /b 1

call robocopy "%PUBLISH_ROOT%\student-agent" "%PUBLISH_ROOT%\student-host\StudentAgent" /E >nul
if errorlevel 8 exit /b 1

if exist "%PUBLISH_ROOT%\teacher-host\Updater" rmdir /s /q "%PUBLISH_ROOT%\teacher-host\Updater"
mkdir "%PUBLISH_ROOT%\teacher-host\Updater" || exit /b 1
call robocopy "%PUBLISH_ROOT%\updater" "%PUBLISH_ROOT%\teacher-host\Updater" /E >nul
if errorlevel 8 exit /b 1

if exist "%PUBLISH_ROOT%\student-host\Updater" rmdir /s /q "%PUBLISH_ROOT%\student-host\Updater"
mkdir "%PUBLISH_ROOT%\student-host\Updater" || exit /b 1
call robocopy "%PUBLISH_ROOT%\updater" "%PUBLISH_ROOT%\student-host\Updater" /E >nul
if errorlevel 8 exit /b 1

call :build_installers %1 || exit /b 1
call :generate_update_manifests %1 || exit /b 1

echo [publish] outputs:
echo   %PUBLISH_ROOT%\teacher-host
echo   %PUBLISH_ROOT%\student-host
echo   %PUBLISH_ROOT%\student-agent
echo   %INSTALLERS_ROOT%\TeacherInstaller.exe
echo   %INSTALLERS_ROOT%\StudentInstaller.exe
echo   %UPDATES_ROOT%\teacher\manifest.json
echo   %UPDATES_ROOT%\student\manifest.json

exit /b 0

:build_ui
set APP=%~1
echo [publish] build %APP%
pushd "%APP%" || exit /b 1
if not exist "node_modules" (
  call npm install || (popd & exit /b 1)
)
call npm run build || (popd & exit /b 1)
popd
exit /b 0

:sync_assets
echo [publish] syncing shared favicon assets
if not exist "%ROOT%\favicon.ico" (
  echo [publish] missing %ROOT%\favicon.ico
  exit /b 1
)
copy /y "%ROOT%\favicon.ico" "%ROOT%\apps\teacher-ui\public\favicon.ico" >nul || exit /b 1
copy /y "%ROOT%\favicon.ico" "%ROOT%\apps\student-ui\public\favicon.ico" >nul || exit /b 1

set TEACHER_PNG=%ROOT%\apps\teacher-ui\public\favicon.png
set STUDENT_PNG=%ROOT%\apps\student-ui\public\favicon.png

if exist "%ROOT%\favicon.png" (
  copy /y "%ROOT%\favicon.png" "%TEACHER_PNG%" >nul || exit /b 1
  copy /y "%ROOT%\favicon.png" "%STUDENT_PNG%" >nul || exit /b 1
) else if exist "%TEACHER_PNG%" (
  if not exist "%STUDENT_PNG%" (
    copy /y "%TEACHER_PNG%" "%STUDENT_PNG%" >nul || exit /b 1
  )
) else if exist "%STUDENT_PNG%" (
  if not exist "%TEACHER_PNG%" (
    copy /y "%STUDENT_PNG%" "%TEACHER_PNG%" >nul || exit /b 1
  )
) else (
  echo [publish] warning: favicon.png not found, skipping png sync
)

if exist "%ROOT%\favicon.svg" (
  copy /y "%ROOT%\favicon.svg" "%ROOT%\apps\teacher-ui\public\favicon.svg" >nul || exit /b 1
  copy /y "%ROOT%\favicon.svg" "%ROOT%\apps\student-ui\public\favicon.svg" >nul || exit /b 1
)
exit /b 0

:build_installers
set APP_VERSION=%~1
if "%APP_VERSION%"=="" (
  if not "%CONTROLEDU_VERSION%"=="" set APP_VERSION=%CONTROLEDU_VERSION%
)
if "%APP_VERSION%"=="" (
  for /f "delims=" %%i in ('cmd /c scripts\version.cmd') do set APP_VERSION=%%i
)
if "%APP_VERSION%"=="" set APP_VERSION=0.1.1b

if not exist "%INSTALLERS_ROOT%" mkdir "%INSTALLERS_ROOT%" || exit /b 1

set ISCC_PATH=%INNO_ISCC%
if "%ISCC_PATH%"=="" (
  if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" (
    set "ISCC_PATH=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
  ) else (
    if exist "C:\Program Files\Inno Setup 6\ISCC.exe" (
      set "ISCC_PATH=C:\Program Files\Inno Setup 6\ISCC.exe"
    ) else (
      if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" (
        set "ISCC_PATH=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
      )
    )
  )
)

if "%ISCC_PATH%"=="" (
  echo [publish] ERROR: Inno Setup ISCC.exe not found.
  echo [publish] Set INNO_ISCC environment variable to full path of ISCC.exe
  exit /b 1
)

if not exist "%ISCC_PATH%" (
  echo [publish] ERROR: ISCC.exe does not exist at "%ISCC_PATH%"
  exit /b 1
)

echo [publish] building Inno installers (version %APP_VERSION%)
call "%ISCC_PATH%" "installer\inno\controledu-teacher.iss" /DMyAppVersion=%APP_VERSION% /DSourceDir="%PUBLISH_ROOT%\teacher-host" /DOutputDir="%INSTALLERS_ROOT%" || exit /b 1
call "%ISCC_PATH%" "installer\inno\controledu-student.iss" /DMyAppVersion=%APP_VERSION% /DSourceHostDir="%PUBLISH_ROOT%\student-host" /DSourceAgentDir="%PUBLISH_ROOT%\student-agent" /DOutputDir="%INSTALLERS_ROOT%" || exit /b 1
exit /b 0

:generate_update_manifests
set APP_VERSION=%~1
if "%APP_VERSION%"=="" (
  if not "%CONTROLEDU_VERSION%"=="" set APP_VERSION=%CONTROLEDU_VERSION%
)
if "%APP_VERSION%"=="" (
  for /f "delims=" %%i in ('cmd /c scripts\version.cmd') do set APP_VERSION=%%i
)
if "%APP_VERSION%"=="" set APP_VERSION=0.1.1b

set UPDATE_BASE_URL=%CONTROLEDU_UPDATE_BASE_URL%
if "%UPDATE_BASE_URL%"=="" set UPDATE_BASE_URL=https://controledu.kilocraft.org

powershell -NoProfile -ExecutionPolicy Bypass -File "scripts\generate_update_manifests.ps1" -ArtifactsRoot "%ARTIFACTS%" -BaseUrl "%UPDATE_BASE_URL%" -Version "%APP_VERSION%" || exit /b 1
exit /b 0
