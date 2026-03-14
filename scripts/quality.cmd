@echo off
setlocal EnableExtensions

set ROOT=%~dp0..
cd /d "%ROOT%"

call :run_frontend apps\teacher-ui || exit /b 1
call :run_frontend apps\student-ui || exit /b 1

echo [quality] dotnet restore
call dotnet restore Controledu.sln || exit /b 1

echo [quality] dotnet build
call dotnet build Controledu.sln -nologo -v minimal || exit /b 1

echo [quality] dotnet test
call dotnet test Controledu.sln -nologo -v minimal || exit /b 1

echo [quality] all checks passed
exit /b 0

:run_frontend
set APP=%~1
echo [quality] %APP%
pushd "%APP%" || exit /b 1
call npm ci || (popd & exit /b 1)
call npm run lint || (popd & exit /b 1)
call npm run test || (popd & exit /b 1)
popd
exit /b 0
