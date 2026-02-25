param(
  [Alias("PiperTarUrl")]
  [string]$PiperArchiveUrl = "https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_windows_amd64.zip",
  [string]$PiperVoiceModelUrl = "",
  [string]$PiperVoiceConfigUrl = ""
)

$ErrorActionPreference = "Stop"

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$runtimeDir = Join-Path $root "runtime/piper"
$modelsDir = Join-Path $root "models/piper"
$tmpDir = Join-Path $root "tmp/downloads"

New-Item -ItemType Directory -Force -Path $runtimeDir, $modelsDir, $tmpDir | Out-Null

function Get-SafeFileNameFromUrl {
  param([string]$Url)
  $uri = [System.Uri]$Url
  $name = [System.IO.Path]::GetFileName($uri.AbsolutePath)
  if ([string]::IsNullOrWhiteSpace($name)) {
    throw "Cannot determine file name from URL: $Url"
  }
  return $name
}

$piperExt = [System.IO.Path]::GetExtension(([System.Uri]$PiperArchiveUrl).AbsolutePath).ToLowerInvariant()
$piperArchive = Join-Path $tmpDir ("piper" + $(if ($piperExt) { $piperExt } else { ".zip" }))
Write-Host "[1/3] Download Piper binary from: $PiperArchiveUrl"
Invoke-WebRequest -Uri $PiperArchiveUrl -OutFile $piperArchive

if ($piperArchive.ToLowerInvariant().EndsWith(".zip")) {
  Expand-Archive -Path $piperArchive -DestinationPath $runtimeDir -Force
} elseif ($piperArchive.ToLowerInvariant().EndsWith(".tar.gz") -or $piperArchive.ToLowerInvariant().EndsWith(".tgz")) {
  if (Get-Command tar -ErrorAction SilentlyContinue) {
    tar -xzf $piperArchive -C $runtimeDir --strip-components=1
  } else {
    throw "tar is required to extract .tar.gz archive."
  }
} else {
  throw "Unsupported Piper archive format: $piperArchive"
}

# Flatten nested folder from zip if needed (e.g., runtime/piper/piper/*.exe)
$nestedPiperExe = Get-ChildItem -Path $runtimeDir -Recurse -Filter "piper.exe" -File -ErrorAction SilentlyContinue | Select-Object -First 1
if ($nestedPiperExe -and $nestedPiperExe.DirectoryName -ne $runtimeDir) {
  Get-ChildItem -Path $nestedPiperExe.DirectoryName -Force | ForEach-Object {
    Move-Item -Force -Path $_.FullName -Destination (Join-Path $runtimeDir $_.Name)
  }
  Get-ChildItem -Path $runtimeDir -Directory | Where-Object { $_.FullName -ne $runtimeDir } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

if ($PiperVoiceModelUrl) {
  Write-Host "[2/3] Download Piper voice model"
  $target = Join-Path $modelsDir (Get-SafeFileNameFromUrl -Url $PiperVoiceModelUrl)
  Invoke-WebRequest -Uri $PiperVoiceModelUrl -OutFile $target
} else {
  Write-Host "[2/3] Skip voice model download (pass -PiperVoiceModelUrl)"
}

if ($PiperVoiceConfigUrl) {
  Write-Host "[3/3] Download Piper voice config"
  $target = Join-Path $modelsDir (Get-SafeFileNameFromUrl -Url $PiperVoiceConfigUrl)
  Invoke-WebRequest -Uri $PiperVoiceConfigUrl -OutFile $target
} else {
  Write-Host "[3/3] Skip voice config download (optional)"
}

Write-Host "Done. Runtime: $runtimeDir ; Models: $modelsDir"
