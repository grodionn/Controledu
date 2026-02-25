param(
  [string]$EnvFile = ".env.gpu-vpn.example",
  [switch]$CreateVenv,
  [switch]$InstallDeps
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Test-Python311Command {
  param([string[]]$CommandParts)
  try {
    $args = @()
    if ($CommandParts.Length -gt 1) {
      $args += $CommandParts[1..($CommandParts.Length - 1)]
    }
    $output = & $CommandParts[0] @args --version 2>&1
    if ($LASTEXITCODE -ne 0) { return $false }
    $text = ($output | Out-String).Trim()
    return $text -match "Python 3\.11\."
  } catch {
    return $false
  }
}

function Resolve-Python311 {
  $candidates = @(
    @("py", "-3.11"),
    @("python3.11", ""),
    @("python", "")
  )

  foreach ($candidate in $candidates) {
    $cmd = @($candidate | Where-Object { $_ -ne "" })
    if (Test-Python311Command -CommandParts $cmd) {
      return $cmd
    }
  }

  $commonPaths = @(
    "$env:LocalAppData\Programs\Python\Python311\python.exe",
    "$env:ProgramFiles\Python311\python.exe",
    "${env:ProgramFiles(x86)}\Python311\python.exe"
  ) | Where-Object { $_ -and (Test-Path $_) }

  foreach ($path in $commonPaths) {
    if (Test-Python311Command -CommandParts @($path)) {
      return @($path)
    }
  }

  throw @"
Python 3.11 x64 is required for selfhost-speech GPU worker.

Install options:
  winget install Python.Python.3.11
or download Windows installer (64-bit) from python.org (Python 3.11.x).

After install, verify:
  py -3.11 --version
"@
}

function Import-DotEnv {
  param([string]$Path)
  if (-not (Test-Path $Path)) {
    throw "Env file not found: $Path"
  }
  Get-Content $Path | ForEach-Object {
    $line = $_.Trim()
    if (-not $line -or $line.StartsWith("#")) { return }
    $idx = $line.IndexOf("=")
    if ($idx -lt 1) { return }
    $name = $line.Substring(0, $idx).Trim()
    $value = $line.Substring($idx + 1)
    [Environment]::SetEnvironmentVariable($name, $value, "Process")
  }
}

if ($CreateVenv) {
  $python311 = Resolve-Python311
  $venvArgs = @()
  if ($python311.Length -gt 1) { $venvArgs += $python311[1..($python311.Length - 1)] }
  & $python311[0] @venvArgs -m venv .venv
}

$pythonExe = Join-Path $root ".venv\\Scripts\\python.exe"
if (Test-Path $pythonExe) {
  $versionText = (& $pythonExe --version 2>&1 | Out-String).Trim()
  if ($versionText -notmatch "Python 3\.11\.") {
    throw "Existing .venv is not Python 3.11 (`"$versionText`"). Remove .venv and rerun with -CreateVenv after installing Python 3.11."
  }
  $python = @($pythonExe)
} else {
  $python = Resolve-Python311
}

if ($InstallDeps) {
  $pyArgs = @()
  if ($python.Length -gt 1) { $pyArgs += $python[1..($python.Length - 1)] }
  & $python[0] @pyArgs -m pip install --upgrade pip
  & $python[0] @pyArgs -m pip install -r requirements.txt
}

Import-DotEnv -Path (Join-Path $root $EnvFile)

Write-Host "Starting Controledu speech GPU worker on $env:SPEECH_API_HOST`:$env:SPEECH_API_PORT"
Write-Host "Whisper: model=$env:WHISPER_MODEL device=$env:WHISPER_DEVICE compute=$env:WHISPER_COMPUTE_TYPE"

$runArgs = @()
if ($python.Length -gt 1) { $runArgs += $python[1..($python.Length - 1)] }
& $python[0] @runArgs -m uvicorn app.main:app --host $env:SPEECH_API_HOST --port $env:SPEECH_API_PORT --log-level $env:SPEECH_API_LOG_LEVEL
