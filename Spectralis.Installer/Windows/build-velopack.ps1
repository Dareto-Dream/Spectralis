param(
    [Parameter(Mandatory = $true)][string]$Version,
    # TODO 5.1.0: add linux-x64 and osx-x64/osx-arm64 channels so Linux and
    # macOS users get Velopack auto-updates. Each channel needs a vpk pack run,
    # a matching releases.<channel>.json on the CDN, and platform branching in
    # VelopackUpdateService.cs (OperatingSystem.IsLinux / IsOSX).
    [string]$Channel = "win-x64"
)

# Velopack build for the Avalonia app — the in-process update mechanism that
# replaces Squirrel (build-squirrel.ps1, kept only for migrating existing
# WinForms/legacy-Avalonia installs through the old feed during the
# transition; see Spectralis.App.csproj TODO 5.1.0). Produces a release feed
# under releases-velopack/ matching the CDN base VelopackUpdateService.cs
# checks against (https://cdn.deltavdevs.com/spectralis).

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$appProject = Join-Path $repoRoot "Spectralis.App\Spectralis.App.csproj"
$publishDir = Join-Path $repoRoot "publish-velopack"
$releaseDir = Join-Path $repoRoot "releases-velopack"

function Assert-LastExitCode([string]$CommandName) {
    if ($LASTEXITCODE -ne 0) { throw "$CommandName failed with exit code $LASTEXITCODE." }
}

foreach ($required in @("SPECTRALIS_SPOTIFY_CLIENT_ID", "SPECTRALIS_DISCORD_CLIENT_ID")) {
    if ([string]::IsNullOrWhiteSpace([System.Environment]::GetEnvironmentVariable($required))) {
        throw "$required is not set. Releases must ship with baked-in client IDs; set it before building."
    }
}

Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

dotnet publish $appProject -c Release -f net8.0-windows10.0.19041.0 -r win-x64 --self-contained true -o $publishDir "/p:Version=$Version"
Assert-LastExitCode "dotnet publish"

$publishedExePath = Join-Path $publishDir "Spectralis.App.exe"
if (-not (Test-Path $publishedExePath)) { throw "Publish output missing: $publishedExePath" }

Push-Location $repoRoot
try {
    # Remove any existing artifacts for this exact version so vpk never hits the
    # interactive overwrite prompt (non-interactive mode fatals on that prompt).
    Remove-Item (Join-Path $releaseDir "Spectralis-$Version-$Channel-full.nupkg") -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $releaseDir "releases.$Channel.json") -Force -ErrorAction SilentlyContinue

    # vpk 0.0.915 targets net8.0; roll forward to the installed .NET runtime.
    $env:DOTNET_ROLL_FORWARD = "LatestMajor"
    dotnet vpk pack `
        --packId "Spectralis" `
        --packVersion $Version `
        --packDir $publishDir `
        --mainExe "Spectralis.App.exe" `
        --channel $Channel `
        -o $releaseDir
    Assert-LastExitCode "vpk pack"
}
finally {
    Pop-Location
}

Write-Host "Velopack release artifacts in $releaseDir."
