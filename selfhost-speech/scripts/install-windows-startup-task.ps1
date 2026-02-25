param(
  [string]$TaskName = "ControleduSpeechGpuWorker",
  [string]$ProjectPath = "",
  [string]$EnvFile = ".env.gpu-vpn.example"
)

$ErrorActionPreference = "Stop"

if (-not $ProjectPath) {
  $ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$scriptPath = Join-Path $ProjectPath "scripts\\run-gpu-worker.ps1"
if (-not (Test-Path $scriptPath)) {
  throw "Script not found: $scriptPath"
}

$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$scriptPath`" -EnvFile `"$EnvFile`""
$trigger = New-ScheduledTaskTrigger -AtStartup
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -StartWhenAvailable

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Description "Controledu self-host speech GPU worker" -Force
Write-Host "Scheduled task '$TaskName' installed. Edit credentials if you want it to run under a specific user."
