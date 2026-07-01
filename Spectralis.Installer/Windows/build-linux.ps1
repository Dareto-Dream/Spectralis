param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$WslDistro = "Ubuntu"
)

# Builds the Linux AppImage via WSL, invoking Spectralis.Installer/Linux/build-appimage.sh.
# Requires a WSL2 Ubuntu distro with the .NET SDK installed.
# Credentials (SPECTRALIS_SPOTIFY_CLIENT_ID, SPECTRALIS_DISCORD_CLIENT_ID) are inherited
# from the calling PowerShell session via WSLENV.

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")

function Assert-LastExitCode([string]$CommandName) {
    if ($LASTEXITCODE -ne 0) { throw "$CommandName failed with exit code $LASTEXITCODE." }
}

# Verify WSL distro is reachable (wsl --list outputs UTF-16; probe instead).
$probe = wsl -d $WslDistro -- echo ok 2>&1
if ($LASTEXITCODE -ne 0 -or $probe -notmatch "ok") {
    throw "WSL distro '$WslDistro' not found or not running. Install it with: wsl --install -d $WslDistro"
}

foreach ($required in @("SPECTRALIS_SPOTIFY_CLIENT_ID", "SPECTRALIS_DISCORD_CLIENT_ID")) {
    if ([string]::IsNullOrWhiteSpace([System.Environment]::GetEnvironmentVariable($required))) {
        throw "$required is not set. Releases must ship with baked-in client IDs; set it before building."
    }
}

# Convert Windows path to WSL mount path  (C:\foo\bar → /mnt/c/foo/bar).
$repoWsl = "/mnt/" + $repoRoot.ToString()[0].ToString().ToLower() + ($repoRoot.ToString().Substring(2).Replace('\', '/'))

$scriptWin = Join-Path $repoRoot "Spectralis.Installer\Linux\build-appimage.sh"
$tmpWin    = Join-Path $env:TEMP "spectralis-build-appimage.sh"
[System.IO.File]::WriteAllText(
    $tmpWin,
    [System.IO.File]::ReadAllText($scriptWin).Replace("`r`n", "`n").Replace("`r", "`n"),
    [System.Text.Encoding]::UTF8)
$tmpWsl = "/mnt/" + $tmpWin[0].ToString().ToLower() + ($tmpWin.Substring(2).Replace('\', '/'))

$env:WSLENV   = "SPECTRALIS_SPOTIFY_CLIENT_ID/u:SPECTRALIS_DISCORD_CLIENT_ID/u:REPO_ROOT"
$env:REPO_ROOT = $repoWsl

Write-Host "[linux] Building AppImage v$Version via WSL ($WslDistro)..."
wsl -d $WslDistro -- bash "$tmpWsl" $Version
Assert-LastExitCode "build-appimage.sh"
Remove-Item $tmpWin -Force -ErrorAction SilentlyContinue

$artifact = Join-Path $repoRoot "releases\Spectralis-$Version-x86_64.AppImage"
if (-not (Test-Path $artifact)) {
    throw "AppImage not found after build: $artifact"
}

Write-Host "[linux] AppImage ready: $artifact"
