param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$WslDistro = "Ubuntu"
)

# Builds the Linux Velopack package via WSL, invoking
# Spectralis.Installer/Linux/build-velopack.sh.
# Requires a WSL2 Ubuntu distro with the .NET SDK and vpk tool installed.
# Credentials are inherited from the calling PowerShell session via WSLENV.

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")

function Assert-LastExitCode([string]$CommandName) {
    if ($LASTEXITCODE -ne 0) { throw "$CommandName failed with exit code $LASTEXITCODE." }
}

$probe = wsl -d $WslDistro -- echo ok 2>&1
if ($LASTEXITCODE -ne 0 -or $probe -notmatch "ok") {
    throw "WSL distro '$WslDistro' not found or not running. Install it with: wsl --install -d $WslDistro"
}

foreach ($required in @("SPECTRALIS_SPOTIFY_CLIENT_ID", "SPECTRALIS_DISCORD_CLIENT_ID")) {
    if ([string]::IsNullOrWhiteSpace([System.Environment]::GetEnvironmentVariable($required))) {
        throw "$required is not set. Releases must ship with baked-in client IDs; set it before building."
    }
}

$repoWsl = "/mnt/" + $repoRoot.ToString()[0].ToString().ToLower() + ($repoRoot.ToString().Substring(2).Replace('\', '/'))
$scriptWsl = "$repoWsl/Spectralis.Installer/Linux/build-velopack.sh"

$env:WSLENV = "SPECTRALIS_SPOTIFY_CLIENT_ID/u:SPECTRALIS_DISCORD_CLIENT_ID/u"

Write-Host "[linux] Building Velopack linux-x64 v$Version via WSL ($WslDistro)..."
wsl -d $WslDistro -- sed -i 's/\r$//' "$scriptWsl"
wsl -d $WslDistro -- bash "$scriptWsl" $Version
Assert-LastExitCode "build-velopack.sh"

$artifact = Join-Path $repoRoot "releases-velopack\releases.linux-x64.json"
if (-not (Test-Path $artifact)) {
    throw "Velopack Linux feed not found after build: $artifact"
}

Write-Host "[linux] Velopack linux-x64 ready: $artifact"
