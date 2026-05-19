@echo off
setlocal EnableExtensions
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0generate_api_contracts.ps1"
exit /b %ERRORLEVEL%
