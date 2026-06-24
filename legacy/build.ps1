param(
    [string]$Version,
    [string]$SpotifyClientId = $env:SPECTRALIS_SPOTIFY_CLIENT_ID,
    [string]$SignWithParams = $env:SPECTRALIS_SIGNTOOL_PARAMS,
    [switch]$NoDelta
)

$ErrorActionPreference = "Stop"

# This script lives in legacy/; shared assets, the nuspec, and the release
# feed stay at the repo root so legacy and Avalonia builds share one feed.
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$startupProject = Join-Path $PSScriptRoot "Startup\Startup.csproj"
$nuspecPath = Join-Path $repoRoot "spectralis.nuspec"
$publishDir = Join-Path $repoRoot "publish"
$buildDir = Join-Path $repoRoot "build"
$releaseDir = Join-Path $repoRoot "releases"
$iconScriptPath = Join-Path $buildDir "Generate-Icon.ps1"
$iconSourcePath = Join-Path $repoRoot "Assets\icon.png"
$setupIconPath = Join-Path $PSScriptRoot "obj\build\Spectralis.ico"
$startupProjectXml = [xml](Get-Content $startupProject)

if (-not $Version) {
    $Version = [string]($startupProjectXml.Project.PropertyGroup.Version | Select-Object -First 1)
}

if (-not $Version) {
    throw "No version was provided and Startup.csproj does not define one."
}

$squirrelWindowsVersion = [string]($startupProjectXml.Project.PropertyGroup.SquirrelWindowsVersion | Select-Object -First 1)
if (-not $squirrelWindowsVersion) {
    throw "Startup.csproj does not define SquirrelWindowsVersion."
}

$squirrelExe = Join-Path $env:USERPROFILE ".nuget\packages\squirrel.windows\$squirrelWindowsVersion\tools\Squirrel.exe"
$rceditExe = Join-Path (Split-Path -Parent $squirrelExe) "rcedit.exe"
$nugetToolsDir = Join-Path $env:LOCALAPPDATA "Spectralis\BuildTools"
$nugetExePath = Join-Path $nugetToolsDir "nuget.exe"
$nugetDownloadUrl = "https://dist.nuget.org/win-x86-commandline/latest/nuget.exe"
$packagePath = Join-Path $buildDir "Spectralis.$Version.nupkg"
$deltaPackagePath = Join-Path $releaseDir "Spectralis-$Version-delta.nupkg"
$fullPackagePath = Join-Path $releaseDir "Spectralis-$Version-full.nupkg"
$publishExePath = Join-Path $publishDir "Spectralis.exe"

function Assert-LastExitCode([string]$CommandName) {
    if ($LASTEXITCODE -ne 0) {
        throw "$CommandName failed with exit code $LASTEXITCODE."
    }
}

function Remove-OptionalArtifact([string]$Path) {
    for ($attempt = 0; $attempt -lt 10; $attempt++) {
        if (-not (Test-Path $Path)) {
            return
        }

        Remove-Item $Path -Force -ErrorAction SilentlyContinue

        if (-not (Test-Path $Path)) {
            return
        }

        Start-Sleep -Milliseconds 500
    }
}

function Assert-ExpectedArtifact([string]$Path) {
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        if (Test-Path -LiteralPath $Path) {
            return
        }

        Start-Sleep -Milliseconds 250
    }

    $directory = Split-Path -Parent $Path
    $nearbyArtifacts = if (Test-Path -LiteralPath $directory) {
        Get-ChildItem -LiteralPath $directory -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 8 -ExpandProperty Name
    }
    else {
        @()
    }

    $nearbyText = if ($nearbyArtifacts.Count -gt 0) {
        " Recent release artifacts: $($nearbyArtifacts -join ', ')"
    }
    else {
        ""
    }

    throw "Expected release artifact was not found after waiting: $Path.$nearbyText"
}

function Resolve-NuGetExe {
    $nugetCommand = Get-Command nuget -ErrorAction SilentlyContinue
    if ($nugetCommand) {
        return $nugetCommand.Source
    }

    if (Test-Path -LiteralPath $nugetExePath) {
        return $nugetExePath
    }

    New-Item -ItemType Directory -Force -Path $nugetToolsDir | Out-Null
    Write-Host "nuget.exe was not found on PATH. Downloading NuGet CLI to $nugetExePath"
    Invoke-WebRequest -Uri $nugetDownloadUrl -OutFile $nugetExePath

    if (-not (Test-Path -LiteralPath $nugetExePath)) {
        throw "Unable to download nuget.exe to $nugetExePath"
    }

    return $nugetExePath
}

function Write-ReleaseTrustSummary([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $signature = Get-AuthenticodeSignature -FilePath $Path
    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $Path

    Write-Host "Release artifact: $Path"
    Write-Host "SHA-256: $($hash.Hash)"
    Write-Host "Signature: $($signature.Status)"

    if ($signature.Status -ne "Valid") {
        Write-Warning "The release installer is not signed. Unsigned installers are more likely to be flagged by endpoint protection."
    }
}

# Clean publish (safe)
Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue

# DO NOT delete releases (critical for updates)
if (!(Test-Path $releaseDir)) {
    New-Item -ItemType Directory -Path $releaseDir | Out-Null
}
New-Item -ItemType Directory -Force -Path $buildDir | Out-Null

$publishArgs = @(
    "publish",
    $startupProject,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "true",
    "-o", $publishDir
)
$publishArgs += "/p:Version=$Version"
if (-not [string]::IsNullOrWhiteSpace($SpotifyClientId)) {
    $publishArgs += "/p:SpotifyClientId=$SpotifyClientId"
}

dotnet @publishArgs
Assert-LastExitCode "dotnet publish"

if ((Test-Path -LiteralPath $iconScriptPath) -and (Test-Path -LiteralPath $iconSourcePath)) {
    powershell -NoProfile -ExecutionPolicy Bypass -File $iconScriptPath -Source $iconSourcePath -Destination $setupIconPath
    Assert-LastExitCode "Generate-Icon.ps1"
}

if (-not (Test-Path $publishExePath)) {
    throw "Expected publish output was not found: $publishExePath"
}

if (-not (Test-Path $rceditExe)) {
    throw "rcedit.exe was not found at $rceditExe"
}

& $rceditExe $publishExePath --set-version-string SquirrelAwareVersion 1
Assert-LastExitCode "rcedit SquirrelAwareVersion"

$bundledYtDlpExe = Join-Path $publishDir "yt-dlp.exe"
$bundledYtDlpPayload = Join-Path $publishDir "yt-dlp.bin"
if (Test-Path -LiteralPath $bundledYtDlpExe) {
    Move-Item -LiteralPath $bundledYtDlpExe -Destination $bundledYtDlpPayload -Force
}

$bundledFfmpegExe = Join-Path $publishDir "ffmpeg.exe"
$bundledFfmpegPayload = Join-Path $publishDir "ffmpeg.bin"
if (Test-Path -LiteralPath $bundledFfmpegExe) {
    Move-Item -LiteralPath $bundledFfmpegExe -Destination $bundledFfmpegPayload -Force
}

$createdumpPath = Join-Path $publishDir "createdump.exe"
if (Test-Path -LiteralPath $createdumpPath) {
    Remove-Item -LiteralPath $createdumpPath -Force
}

$nugetExe = Resolve-NuGetExe
& $nugetExe pack $nuspecPath -Version $Version -OutputDirectory $buildDir -NoPackageAnalysis
Assert-LastExitCode "nuget pack"

if (-not (Test-Path $squirrelExe)) {
    throw "Squirrel.exe was not found at $squirrelExe"
}

Remove-OptionalArtifact $deltaPackagePath

$squirrelArgs = @("--releasify", $packagePath, "--releaseDir", $releaseDir, "--no-msi", "--no-delta")
if (Test-Path -LiteralPath $setupIconPath) {
    $squirrelArgs += @("--setupIcon", $setupIconPath)
}

if (-not [string]::IsNullOrWhiteSpace($SignWithParams)) {
    $squirrelArgs += @("--signWithParams", $SignWithParams)
}

if ($NoDelta) {
    Write-Host "Delta packages are already disabled for Spectralis releases."
}

& $squirrelExe @squirrelArgs
Assert-LastExitCode "Squirrel releasify"

foreach ($cleanupPath in @(
    (Join-Path $releaseDir "Setup.wixobj"),
    (Join-Path $releaseDir "Setup.wxs"),
    (Join-Path $releaseDir "Setup.msi"),
    $deltaPackagePath,
    (Join-Path $releaseDir "Spectralis.$Version.nupkg")
)) {
    Remove-OptionalArtifact $cleanupPath
}

foreach ($expectedPath in @(
    (Join-Path $releaseDir "Setup.exe"),
    (Join-Path $releaseDir "RELEASES"),
    $fullPackagePath
)) {
    Assert-ExpectedArtifact $expectedPath
}

Write-ReleaseTrustSummary (Join-Path $releaseDir "Setup.exe")

$global:LASTEXITCODE = 0
return
