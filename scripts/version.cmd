@echo off
setlocal

set VERSION=
for /f "usebackq delims=" %%i in (`powershell -NoProfile -Command "$xml=[xml](Get-Content -Raw 'Directory.Build.props'); $v=$xml.Project.PropertyGroup.ControleduDisplayVersion | Select-Object -First 1; if ($v) { $v }"`) do set VERSION=%%i
if "%VERSION%"=="" (
  for /f "delims=" %%i in ('git describe --tags --always --dirty 2^>nul') do set VERSION=%%i
)
if "%VERSION%"=="" set VERSION=0.1.8b-local

echo %VERSION%
exit /b 0
