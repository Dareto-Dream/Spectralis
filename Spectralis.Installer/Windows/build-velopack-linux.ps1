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

# PowerShell normalizes CRLF → LF before WSL sees the file.
# bash -c quoting through PowerShell → wsl.exe is unreliable; simpler to write a temp
# file with clean line endings and run bash <path> with no quoting games.
$scriptWin = Join-Path $repoRoot "Spectralis.Installer\Linux\build-velopack.sh"
$tmpWin    = Join-Path $env:TEMP "spectralis-build-velopack.sh"
[System.IO.File]::WriteAllText(
    $tmpWin,
    [System.IO.File]::ReadAllText($scriptWin).Replace("`r`n", "`n").Replace("`r", "`n"),
    [System.Text.Encoding]::UTF8)
$tmpWsl = "/mnt/" + $tmpWin[0].ToString().ToLower() + ($tmpWin.Substring(2).Replace('\', '/'))

# Locate vpk.dll from the Windows NuGet cache and pass it to WSL via DrvFS so the
# shell script can run it with 'dotnet <path>' instead of relying on dotnet tool install,
# which fails on Linux because the vpk package lacks DotnetToolSettings.xml there.
$vpkDllWin = "$env:USERPROFILE\.nuget\packages\vpk\0.0.915\tools\net6.0\any\vpk.dll"
if (-not (Test-Path $vpkDllWin)) {
    throw "vpk.dll not found in Windows NuGet cache at $vpkDllWin. Run 'dotnet tool restore' on Windows first."
}
$vpkDllWsl = "/mnt/" + $vpkDllWin[0].ToString().ToLower() + ($vpkDllWin.Substring(2).Replace('\', '/'))

$env:WSLENV    = "SPECTRALIS_SPOTIFY_CLIENT_ID/u:SPECTRALIS_DISCORD_CLIENT_ID/u:REPO_ROOT:VPK_DLL"
$env:REPO_ROOT = $repoWsl
$env:VPK_DLL   = $vpkDllWsl

Write-Host "[linux] Building Velopack linux-x64 v$Version via WSL ($WslDistro)..."
wsl -d $WslDistro -- bash "$tmpWsl" $Version
Assert-LastExitCode "build-velopack.sh"
Remove-Item $tmpWin -Force -ErrorAction SilentlyContinue

$artifact = Join-Path $repoRoot "releases-velopack\releases.linux-x64.json"
if (-not (Test-Path $artifact)) {
    throw "Velopack Linux feed not found after build: $artifact"
}

Write-Host "[linux] Velopack linux-x64 ready: $artifact"
