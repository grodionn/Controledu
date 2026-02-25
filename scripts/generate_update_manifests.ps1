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

function Get-SafeVersionSegment {
    param([string]$Value)
    $safe = ($Value -replace '[^A-Za-z0-9._-]', '-').Trim('-')
    if ([string]::IsNullOrWhiteSpace($safe)) {
        throw "Version '$Value' cannot be converted to a safe file name segment."
    }
    return $safe
}

function New-Manifest {
    param(
        [string]$ProductKey,
        [string]$ProductName,
        [string]$SourceInstallerPath,
        [string]$PublicFilePrefix
    )

    $targetDir = Join-Path $updatesRoot $ProductKey
    New-Item -ItemType Directory -Force -Path $targetDir | Out-Null

    $safeVersion = Get-SafeVersionSegment -Value $Version
    $fileName = "{0}-{1}.exe" -f $PublicFilePrefix, $safeVersion
    $targetInstallerPath = Join-Path $targetDir $fileName
    Copy-Item -Force -Path $SourceInstallerPath -Destination $targetInstallerPath

    Get-ChildItem -Path $targetDir -File -Filter "$PublicFilePrefix*.exe" |
        Where-Object { $_.Name -ne $fileName } |
        ForEach-Object {
            Remove-Item -Force -Path $_.FullName
            Write-Host "[updates] removed stale local installer $($_.Name)"
        }

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

    Write-Host "[updates] $ProductKey -> $manifestPath ($fileName)"
}

New-Manifest -ProductKey "teacher" -ProductName "teacher-host" -SourceInstallerPath $teacherInstallerSource -PublicFilePrefix "TeacherInstaller"
New-Manifest -ProductKey "student" -ProductName "student-host" -SourceInstallerPath $studentInstallerSource -PublicFilePrefix "StudentInstaller"
