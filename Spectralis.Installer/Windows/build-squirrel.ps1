param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$SignWithParams = $env:SPECTRALIS_SIGNTOOL_PARAMS,
    [string]$SquirrelWindowsVersion = "2.0.1",
    # The first Avalonia release ships as a full package only (migration-sized
    # change); pass this once, then drop it so deltas resume.
    [switch]$FirstAvaloniaRelease
)

# Squirrel build for the Avalonia app — same app id ("Spectralis"), same nuspec,
# same publish\Spectralis.exe layout as the legacy build.ps1, so existing
# WinForms installs upgrade in place through the existing releases/ feed.

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$appProject = Join-Path $repoRoot "Spectralis.App\Spectralis.App.csproj"
$nuspecPath = Join-Path $repoRoot "spectralis.nuspec"
$publishDir = Join-Path $repoRoot "publish"
$buildDir = Join-Path $repoRoot "build"
$releaseDir = Join-Path $repoRoot "releases"
$squirrelExe = Join-Path $env:USERPROFILE ".nuget\packages\squirrel.windows\$SquirrelWindowsVersion\tools\Squirrel.exe"
$rceditExe = Join-Path (Split-Path -Parent $squirrelExe) "rcedit.exe"
$packagePath = Join-Path $buildDir "Spectralis.$Version.nupkg"
$publishExePath = Join-Path $publishDir "Spectralis.exe"

function Assert-LastExitCode([string]$CommandName) {
    if ($LASTEXITCODE -ne 0) { throw "$CommandName failed with exit code $LASTEXITCODE." }
}

foreach ($required in @("SPECTRALIS_SPOTIFY_CLIENT_ID", "SPECTRALIS_DISCORD_CLIENT_ID")) {
    if ([string]::IsNullOrWhiteSpace([System.Environment]::GetEnvironmentVariable($required))) {
        throw "$required is not set. Releases must ship with baked-in client IDs; set it before building."
    }
}

# Resolve nuget.exe - download to build/ if not on PATH.
$nugetCmd = Get-Command nuget -ErrorAction SilentlyContinue
if ($nugetCmd) {
    $nugetExe = $nugetCmd.Source
} else {
    $nugetExe = Join-Path $buildDir "nuget.exe"
    if (-not (Test-Path $nugetExe)) {
        Write-Host "nuget.exe not found on PATH - downloading to $nugetExe..."
        New-Item -ItemType Directory -Force -Path $buildDir | Out-Null
        Invoke-WebRequest "https://dist.nuget.org/win-x86-commandline/latest/nuget.exe" -OutFile $nugetExe
    }
}

Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue
# Never delete releases/ - Squirrel needs prior packages (incl. the legacy
# WinForms releases) to produce deltas against.
New-Item -ItemType Directory -Force -Path $releaseDir, $buildDir | Out-Null

dotnet publish $appProject -c Release -f net8.0-windows10.0.19041.0 -r win-x64 --self-contained true -o $publishDir "/p:Version=$Version"
Assert-LastExitCode "dotnet publish"

# Rename the apphost so the package carries publish\Spectralis.exe — the path
# the shared nuspec and existing Squirrel shortcuts expect. The apphost binds
# to Spectralis.App.dll by embedded name, so only the exe needs renaming.
# (A publish-time AssemblyName override collides with the legacy project name.)
Move-Item (Join-Path $publishDir "Spectralis.App.exe") $publishExePath -Force

if (-not (Test-Path $publishExePath)) { throw "Publish output missing: $publishExePath" }

if (Test-Path $rceditExe) {
    & $rceditExe $publishExePath --set-version-string SquirrelAwareVersion 1
    Assert-LastExitCode "rcedit SquirrelAwareVersion"
}

# Helper exes ship as .bin payloads (endpoint-protection friendliness; the app
# re-materializes them under %LOCALAPPDATA%\Spectralis\tools on first use).
foreach ($tool in @("yt-dlp", "ffmpeg")) {
    $exe = Join-Path $publishDir "$tool.exe"
    if (Test-Path $exe) { Move-Item $exe (Join-Path $publishDir "$tool.bin") -Force }
}
Remove-Item (Join-Path $publishDir "createdump.exe") -Force -ErrorAction SilentlyContinue

& $nugetExe pack $nuspecPath -Version $Version -OutputDirectory $buildDir -NoPackageAnalysis
Assert-LastExitCode "nuget pack"

if (-not (Test-Path $squirrelExe)) { throw "Squirrel.exe not found at $squirrelExe (restore the squirrel.windows package)." }

$squirrelArgs = @("--releasify", $packagePath, "--releaseDir", $releaseDir, "--no-msi")
if ($FirstAvaloniaRelease) {
    $squirrelArgs += "--no-delta"
}
if (-not [string]::IsNullOrWhiteSpace($SignWithParams)) {
    $squirrelArgs += @("--signWithParams", $SignWithParams)
}

& $squirrelExe @squirrelArgs
Assert-LastExitCode "Squirrel releasify"

Write-Host "Release artifacts in $releaseDir (Setup.exe, RELEASES, Spectralis-$Version-full.nupkg)."
if ($FirstAvaloniaRelease) {
    Write-Host "Full package only (first Avalonia release); deltas resume next release."
}
