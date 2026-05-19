$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

$openApiDir = Join-Path $root "eng\openapi"
New-Item -ItemType Directory -Path $openApiDir -Force | Out-Null

$outputPath = Join-Path $openApiDir "teacher-server.v1.json"

Write-Host "[contracts] exporting teacher openapi document"
dotnet run --project "tools\Controledu.ApiContractExporter\Controledu.ApiContractExporter.csproj" --configuration Debug --no-launch-profile -- teacher "$outputPath" | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Failed to export teacher OpenAPI document."
}

Write-Host "[contracts] generating typescript contracts"
Push-Location "apps\shared-api-contracts"
try {
    npm ci | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to install shared-api-contracts dependencies."
    }

    npm run generate:teacher | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to generate TypeScript contracts."
    }
} finally {
    Pop-Location
}

Write-Host "[contracts] done"
