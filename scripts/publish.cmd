@echo off
setlocal EnableExtensions

set SCRIPT_DIR=%~dp0
set ROOT=%SCRIPT_DIR%..

cd /d "%ROOT%"

echo [publish] delegating to scripts\build.cmd
call "%SCRIPT_DIR%build.cmd" %*
exit /b %errorlevel%
