@echo off
setlocal

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy-simple.ps1" %*
set EXIT_CODE=%ERRORLEVEL%

if not "%EXIT_CODE%"=="0" (
  echo.
  echo [deploy-simple] failed with exit code %EXIT_CODE%
  pause
  exit /b %EXIT_CODE%
)

echo [deploy-simple] completed successfully
exit /b 0
