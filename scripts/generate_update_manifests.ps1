param(
    [string]$ArtifactsRoot = "artifacts",
    [string]$BaseUrl = "https://controledu.kilocraft.org",
    [string]$Version
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = & cmd /c scripts\version.cmd
    $Version = "$Version".Trim()
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Version is required."
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$artifactsPath = if ([System.IO.Path]::IsPathRooted($ArtifactsRoot)) {
    $ArtifactsRoot
} else {
    Join-Path $repoRoot $ArtifactsRoot
}

$installersRoot = Join-Path $artifactsPath "installers"
$updatesRoot = Join-Path $artifactsPath "updates"

$teacherInstallerSource = Join-Path $installersRoot "TeacherInstaller.exe"
$studentInstallerSource = Join-Path $installersRoot "StudentInstaller.exe"

if (!(Test-Path $teacherInstallerSource)) { throw "Missing $teacherInstallerSource" }
if (!(Test-Path $studentInstallerSource)) { throw "Missing $studentInstallerSource" }

$normalizedBaseUrl = $BaseUrl.TrimEnd("/")

function New-Manifest {
    param(
        [string]$ProductKey,
        [string]$ProductName,
        [string]$SourceInstallerPath,
        [string]$PublicFilePrefix
    )

    $targetDir = Join-Path $updatesRoot $ProductKey
    New-Item -ItemType Directory -Force -Path $targetDir | Out-Null

    $fileName = "{0}.exe" -f $PublicFilePrefix
    $targetInstallerPath = Join-Path $targetDir $fileName
    Copy-Item -Force -Path $SourceInstallerPath -Destination $targetInstallerPath

    $hash = (Get-FileHash -Algorithm SHA256 -Path $targetInstallerPath).Hash.ToLowerInvariant()
    $size = (Get-Item $targetInstallerPath).Length

    $manifest = [ordered]@{
        product = $ProductName
        version = $Version
        installerUrl = "$normalizedBaseUrl/updates/$ProductKey/$fileName"
        sha256 = $hash
        sizeBytes = $size
        mandatory = $true
        publishedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        releaseNotes = "Automatic update package for Controledu $Version"
    }

    $manifestPath = Join-Path $targetDir "manifest.json"
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -Path $manifestPath -Encoding UTF8

    Write-Host "[updates] $ProductKey -> $manifestPath"
}

New-Manifest -ProductKey "teacher" -ProductName "teacher-host" -SourceInstallerPath $teacherInstallerSource -PublicFilePrefix "TeacherInstaller"
New-Manifest -ProductKey "student" -ProductName "student-host" -SourceInstallerPath $studentInstallerSource -PublicFilePrefix "StudentInstaller"
