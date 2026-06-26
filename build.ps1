param(
    [string]$Version,
    [switch]$Linux,
    [string]$WslDistro = "Ubuntu",
    # One-time Squirrel bridge: pass for any release that must appear in the
    # Squirrel RELEASES feed so users still on 5.0.0 can receive the update.
    # Drop this flag once the Squirrel feed is permanently retired.
    [switch]$IncludeSquirrel
)

# Release build for Spectralis (Avalonia). Produces:
#   - Velopack artifacts (releases-velopack/) — the in-process update channel
#     VelopackUpdateService checks against.
#   - Squirrel artifacts (releases/) when -IncludeSquirrel is passed — bridge
#     for users still on the old Squirrel feed (5.0.0 and earlier).
#   - Linux AppImage (releases/Spectralis-$Version-x86_64.AppImage) when -Linux
#     is passed; built inside WSL via Spectralis.Installer/Linux/build-appimage.sh.
#
# The legacy WinForms release build lives at legacy\build.ps1.

$ErrorActionPreference = "Stop"

if (-not $Version) {
    throw "Pass -Version (e.g. .\build.ps1 -Version 5.0.2)."
}

$velopackScript = Join-Path $PSScriptRoot "Spectralis.Installer\Windows\build-velopack.ps1"
& $velopackScript -Version $Version

if ($IncludeSquirrel) {
    $squirrelScript = Join-Path $PSScriptRoot "Spectralis.Installer\Windows\build-squirrel.ps1"
    & $squirrelScript -Version $Version
}

if ($Linux) {
    $linuxScript = Join-Path $PSScriptRoot "Spectralis.Installer\Windows\build-linux.ps1"
    & $linuxScript -Version $Version -WslDistro $WslDistro
}
