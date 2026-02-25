@echo off
setlocal

python "%~dp0build_deploy.py" --build --clean %*
set EXIT_CODE=%ERRORLEVEL%

if not "%EXIT_CODE%"=="0" (
  echo.
  echo [build-deploy-python] failed with exit code %EXIT_CODE%
  pause
  exit /b %EXIT_CODE%
)

echo [build-deploy-python] completed successfully
exit /b 0
