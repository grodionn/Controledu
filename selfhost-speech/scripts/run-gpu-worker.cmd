@echo off
setlocal EnableExtensions

set "SCRIPT_DIR=%~dp0"
set "PS1=%SCRIPT_DIR%run-gpu-worker.ps1"

if not exist "%PS1%" (
  echo [run-gpu-worker] ERROR: Script not found: "%PS1%"
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%" %*
exit /b %ERRORLEVEL%
