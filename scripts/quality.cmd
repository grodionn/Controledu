@echo off
setlocal EnableExtensions

set ROOT=%~dp0..
cd /d "%ROOT%"

echo [quality] api contracts
call scripts\generate_api_contracts.cmd || exit /b 1
git diff --exit-code -- eng/openapi/teacher-server.v1.json apps/shared-api-contracts/src/generated/teacher-server.ts >nul || (
  echo [quality] API contracts are not up-to-date. Run scripts\generate_api_contracts.cmd and commit results.
  exit /b 1
)

call :run_frontend apps\teacher-ui || exit /b 1
call :run_frontend apps\student-ui || exit /b 1

echo [quality] dotnet restore
call dotnet restore Controledu.sln || exit /b 1

echo [quality] dotnet build
call dotnet build Controledu.sln -nologo -v minimal || exit /b 1

echo [quality] dotnet e2e
call dotnet test tests\Controledu.Tests\Controledu.Tests.csproj -nologo -v minimal --filter "Category=E2E" --no-build || exit /b 1

if not exist artifacts\coverage\dotnet mkdir artifacts\coverage\dotnet

echo [quality] dotnet coverage
call dotnet test tests\Controledu.Tests\Controledu.Tests.csproj -nologo -v minimal --no-build /p:CollectCoverage=true /p:CoverletOutput=..\..\artifacts\coverage\dotnet\coverage /p:CoverletOutputFormat=cobertura /p:Threshold=20 /p:ThresholdType=line /p:ThresholdStat=total || exit /b 1

echo [quality] all checks passed
exit /b 0

:run_frontend
set APP=%~1
echo [quality] %APP%
pushd "%APP%" || exit /b 1
call npm ci || (popd & exit /b 1)
call npm run lint || (popd & exit /b 1)
call npm run test:coverage || (popd & exit /b 1)
popd
exit /b 0
