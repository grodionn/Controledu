param(
    [ValidateSet('all', 'teacher', 'student', 'agent')]
    [string]$Mode = 'all'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

function Start-ControleduProcess {
    param(
        [Parameter(Mandatory = $true)][string]$Title,
        [Parameter(Mandatory = $true)][string]$Command
    )

    Start-Process powershell -ArgumentList @(
        '-NoExit',
        '-Command',
        "`$Host.UI.RawUI.WindowTitle = '$Title'; Set-Location '$root'; $Command"
    ) | Out-Null
}

if ($Mode -eq 'all' -or $Mode -eq 'teacher') {
    Start-ControleduProcess -Title 'Controledu Teacher Host' -Command 'dotnet run --project src/Controledu.Teacher.Host'
}

if ($Mode -eq 'all' -or $Mode -eq 'student') {
    Start-ControleduProcess -Title 'Controledu Student Host' -Command 'dotnet run --project src/Controledu.Student.Host'
}

if ($Mode -eq 'agent') {
    Start-ControleduProcess -Title 'Controledu Student Agent' -Command 'dotnet run --project src/Controledu.Student.Agent'
}

Write-Host "Started mode: $Mode"
