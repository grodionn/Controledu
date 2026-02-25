param(
    [Alias("Host")]
    [string]$SshHost = "51.195.91.55",
    [int]$Port = 22,
    [string]$User = "ubuntu",
    [switch]$Build,
    [switch]$Clean,
    [switch]$WithInstallers,
    [switch]$WithoutInstallers,
    [string]$UpdatesPath = "/var/www/controledu/updates",
    [string]$InstallersPath = "/var/www/controledu/installers",
    [string]$ArtifactsRoot = "artifacts"
)

$ErrorActionPreference = "Stop"

if ($PSBoundParameters.ContainsKey("WithoutInstallers")) {
    $WithInstallers = $false
} elseif (-not $PSBoundParameters.ContainsKey("WithInstallers")) {
    $WithInstallers = $true
}

function Write-Step {
    param([string]$Message)
    Write-Host "[deploy-simple] $Message"
}

function Require-Command {
    param([string]$Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command not found: $Name"
    }
}

function Read-JsonFile {
    param([string]$Path)
    # Handle manifests generated on Windows with UTF-8 BOM.
    return (Get-Content -Path $Path -Raw -Encoding UTF8 | ConvertFrom-Json)
}

function Get-InstallerNameFromManifest {
    param($Manifest, [string]$ManifestPath)
    $installerUrl = "$($Manifest.installerUrl)"
    if ([string]::IsNullOrWhiteSpace($installerUrl)) {
        throw "installerUrl is missing in manifest: $ManifestPath"
    }
    return [System.IO.Path]::GetFileName(([Uri]$installerUrl).AbsolutePath)
}

function ConvertTo-BashSingleQuoted {
    param([string]$Value)
    return "'" + ($Value -replace "'", "'""'""'") + "'"
}

function Invoke-Ssh {
    param([string]$CommandText)
    $args = @("-p", "$Port", "$User@$SshHost", $CommandText)
    Write-Step ("ssh " + ($args -join " "))
    & ssh @args
    if ($LASTEXITCODE -ne 0) {
        throw "ssh command failed with exit code $LASTEXITCODE"
    }
}

function Invoke-ScpUpload {
    param([string]$LocalPath, [string]$RemotePath)
    $args = @("-P", "$Port", $LocalPath, "$User@$SshHost`:$RemotePath")
    Write-Step ("scp " + ($args -join " "))
    & scp @args
    if ($LASTEXITCODE -ne 0) {
        throw "scp upload failed with exit code $LASTEXITCODE for $LocalPath"
    }
}

function Remove-RemoteStaleUpdateInstallers {
    param(
        [string]$RemoteDir,
        [string]$KeepInstallerName
    )

    $remoteDirQ = ConvertTo-BashSingleQuoted $RemoteDir
    $keepQ = ConvertTo-BashSingleQuoted $KeepInstallerName
    $cmd = "find $remoteDirQ -maxdepth 1 -type f -name '*.exe' ! -name $keepQ -delete"
    Invoke-Ssh -CommandText $cmd
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

Require-Command "ssh"
Require-Command "scp"

if ($Build) {
    Write-Step "Running scripts\\build.cmd"
    & cmd /c scripts\build.cmd
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE"
    }
}

$artifactsPath = if ([System.IO.Path]::IsPathRooted($ArtifactsRoot)) {
    $ArtifactsRoot
} else {
    Join-Path $repoRoot $ArtifactsRoot
}

$updatesLocal = Join-Path $artifactsPath "updates"
$teacherManifestPath = Join-Path $updatesLocal "teacher\manifest.json"
$studentManifestPath = Join-Path $updatesLocal "student\manifest.json"

if (-not (Test-Path $teacherManifestPath)) { throw "Missing $teacherManifestPath. Run build first." }
if (-not (Test-Path $studentManifestPath)) { throw "Missing $studentManifestPath. Run build first." }

$teacherManifest = Read-JsonFile -Path $teacherManifestPath
$studentManifest = Read-JsonFile -Path $studentManifestPath
$teacherInstallerName = Get-InstallerNameFromManifest -Manifest $teacherManifest -ManifestPath $teacherManifestPath
$studentInstallerName = Get-InstallerNameFromManifest -Manifest $studentManifest -ManifestPath $studentManifestPath

$teacherInstallerLocal = Join-Path $updatesLocal ("teacher\" + $teacherInstallerName)
$studentInstallerLocal = Join-Path $updatesLocal ("student\" + $studentInstallerName)

if (-not (Test-Path $teacherInstallerLocal)) { throw "Missing $teacherInstallerLocal" }
if (-not (Test-Path $studentInstallerLocal)) { throw "Missing $studentInstallerLocal" }

$updatesBase = $UpdatesPath.TrimEnd("/")
$teacherRemoteDir = "$updatesBase/teacher"
$studentRemoteDir = "$updatesBase/student"
$installersLocal = Join-Path $artifactsPath "installers"
$installersRemoteDir = $InstallersPath.TrimEnd("/")

if ($WithInstallers -and -not (Test-Path $installersLocal)) {
    throw "Installers folder not found: $installersLocal"
}

if ($Clean) {
    $cleanCmd = "rm -rf '$updatesBase'/*"
    if ($WithInstallers) {
        $cleanCmd += " && rm -rf '$installersRemoteDir'/*"
    }
    Invoke-Ssh -CommandText $cleanCmd
}

$mkdirCmd = "mkdir -p '$teacherRemoteDir' '$studentRemoteDir'"
if ($WithInstallers) {
    $mkdirCmd += " '$installersRemoteDir'"
}
Invoke-Ssh -CommandText $mkdirCmd

Invoke-ScpUpload -LocalPath $teacherInstallerLocal -RemotePath "$teacherRemoteDir/$teacherInstallerName"
Invoke-ScpUpload -LocalPath $studentInstallerLocal -RemotePath "$studentRemoteDir/$studentInstallerName"
Invoke-ScpUpload -LocalPath $teacherManifestPath -RemotePath "$teacherRemoteDir/manifest.json"
Invoke-ScpUpload -LocalPath $studentManifestPath -RemotePath "$studentRemoteDir/manifest.json"

Remove-RemoteStaleUpdateInstallers -RemoteDir $teacherRemoteDir -KeepInstallerName $teacherInstallerName
Remove-RemoteStaleUpdateInstallers -RemoteDir $studentRemoteDir -KeepInstallerName $studentInstallerName

if ($WithInstallers) {
    Get-ChildItem $installersLocal -File | ForEach-Object {
        Invoke-ScpUpload -LocalPath $_.FullName -RemotePath "$installersRemoteDir/$($_.Name)"
    }
}

Write-Step "Done"

