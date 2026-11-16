#!/usr/bin/env bash
# Velopack packaging for Linux. Runs on a Linux host with .NET SDK and the vpk tool.
# Produces a linux-x64 channel feed under releases-velopack/ for VelopackUpdateService.
set -euo pipefail

VERSION="${1:?usage: build-velopack.sh <version>}"
REPO_ROOT="${REPO_ROOT:-$(cd "$(dirname "$0")/../.." && pwd)}"
# dotnet on WSL defaults global-packages to ~/.nuget/packages but may inherit a Windows
# path from the environment; pin it explicitly so assets.json gets a Linux path.
export NUGET_PACKAGES="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
APP_PROJECT="$REPO_ROOT/Spectralis.App/Spectralis.App.csproj"
PUBLISH_DIR="$REPO_ROOT/publish-velopack-linux"
RELEASE_DIR="$REPO_ROOT/releases-velopack"
CHANNEL="linux-x64"

# Use the portable runtime pack so recovery does not depend on the build host's newer libc.
dotnet publish "$REPO_ROOT/Spectralis.Installer/Updater/Spectralis.Updater.csproj" \
  -c Release -r linux-x64 --self-contained true \
  /p:PublishSingleFile=true /p:PublishTrimmed=true /p:EnableCompressionInSingleFile=true \
  /p:IncludeNativeLibrariesForSelfExtract=true -o "$REPO_ROOT/output/updater/linux-x64"

for required in SPECTRALIS_SPOTIFY_CLIENT_ID SPECTRALIS_DISCORD_CLIENT_ID; do
  if [[ -z "${!required:-}" ]]; then
    echo "error: $required is not set. Releases must ship with baked-in client IDs; set it before building." >&2
    exit 1
  fi
done

rm -rf "$PUBLISH_DIR"
mkdir -p "$RELEASE_DIR"

# Wipe Windows-generated obj/ artifacts so restore rebuilds project.assets.json
# with WSL-native package paths instead of /mnt/c/Users/.../.nuget paths.
find "$REPO_ROOT" -path "*/obj/project.assets.json" -delete 2>/dev/null || true

dotnet restore "$APP_PROJECT" \
  -r linux-x64 \
  /p:TargetFrameworks=net8.0 \
  /p:EnableWindowsTargeting=true

dotnet publish "$APP_PROJECT" \
  -c Release \
  -f net8.0 \
  -r linux-x64 \
  --self-contained true \
  --no-restore \
  /p:TargetFrameworks=net8.0 \
  /p:Version="$VERSION" \
  /p:SPECTRALIS_SPOTIFY_CLIENT_ID="$SPECTRALIS_SPOTIFY_CLIENT_ID" \
  /p:SPECTRALIS_DISCORD_CLIENT_ID="$SPECTRALIS_DISCORD_CLIENT_ID" \
  -o "$PUBLISH_DIR"

test -f "$PUBLISH_DIR/Updater/UpdateSpectralis" || { echo "Recovery updater missing from publish output" >&2; exit 1; }
chmod +x "$PUBLISH_DIR/Updater/UpdateSpectralis"

rm -f "$RELEASE_DIR/Spectralis-$VERSION-$CHANNEL-full.nupkg" \
      "$RELEASE_DIR/releases.$CHANNEL.json"

export DOTNET_ROLL_FORWARD=LatestMajor

# Three ways to reach vpk, in order:
#   1. VPK_DLL, set by build-velopack-linux.ps1 when building through WSL: vpk.dll straight out
#      of the Windows NuGet cache, because a tool restore against that cache fails under DrvFS.
#   2. The repo's tool manifest (.config/dotnet-tools.json), which is what CI uses on a real
#      Linux runner. Same pinned version as the Velopack package the app references.
#   3. A global install, pinned to that version too, for a bare Linux machine.
VPK_VERSION="0.0.915"
if [[ -n "${VPK_DLL:-}" && -f "$VPK_DLL" ]]; then
    VPK_CMD="dotnet $VPK_DLL"
elif (cd "$REPO_ROOT" && dotnet tool restore >/dev/null 2>&1 && dotnet vpk --help >/dev/null 2>&1); then
    VPK_CMD="dotnet vpk"
else
    export PATH="$PATH:$HOME/.dotnet/tools"
    if ! command -v vpk &>/dev/null; then
        dotnet tool install --global vpk --version "$VPK_VERSION"
    fi
    VPK_CMD="vpk"
fi

# `dotnet vpk` resolves through the tool manifest, which is found from the working directory.
cd "$REPO_ROOT"

$VPK_CMD pack \
  --packId "Spectralis" \
  --packVersion "$VERSION" \
  --packDir "$PUBLISH_DIR" \
  --mainExe "Spectralis.App" \
  --packTitle "Spectralis" \
  --packAuthors "DeltaV Devs" \
  --icon "$REPO_ROOT/Assets/icon.png" \
  --categories "AudioVideo;Audio;Player" \
  --channel "$CHANNEL" \
  -o "$RELEASE_DIR"

echo "[linux] Velopack release artifacts in $RELEASE_DIR."
