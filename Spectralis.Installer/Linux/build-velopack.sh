#!/usr/bin/env bash
# Velopack packaging for Linux. Runs on a Linux host with .NET SDK and the vpk tool.
# Produces a linux-x64 channel feed under releases-velopack/ for VelopackUpdateService.
set -euo pipefail

VERSION="${1:?usage: build-velopack.sh <version>}"
REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
APP_PROJECT="$REPO_ROOT/Spectralis.App/Spectralis.App.csproj"
PUBLISH_DIR="$REPO_ROOT/publish-velopack-linux"
RELEASE_DIR="$REPO_ROOT/releases-velopack"
CHANNEL="linux-x64"

for required in SPECTRALIS_SPOTIFY_CLIENT_ID SPECTRALIS_DISCORD_CLIENT_ID; do
  if [[ -z "${!required:-}" ]]; then
    echo "error: $required is not set. Releases must ship with baked-in client IDs; set it before building." >&2
    exit 1
  fi
done

rm -rf "$PUBLISH_DIR"
mkdir -p "$RELEASE_DIR"

dotnet publish "$APP_PROJECT" \
  -c Release \
  -f net8.0 \
  -r linux-x64 \
  --self-contained true \
  /p:Version="$VERSION" \
  /p:SPECTRALIS_SPOTIFY_CLIENT_ID="$SPECTRALIS_SPOTIFY_CLIENT_ID" \
  /p:SPECTRALIS_DISCORD_CLIENT_ID="$SPECTRALIS_DISCORD_CLIENT_ID" \
  -o "$PUBLISH_DIR"

rm -f "$RELEASE_DIR/Spectralis-$VERSION-$CHANNEL-full.nupkg" \
      "$RELEASE_DIR/releases.$CHANNEL.json"

export DOTNET_ROLL_FORWARD=LatestMajor
dotnet vpk pack \
  --packId "Spectralis" \
  --packVersion "$VERSION" \
  --packDir "$PUBLISH_DIR" \
  --mainExe "Spectralis.App" \
  --channel "$CHANNEL" \
  -o "$RELEASE_DIR"

echo "[linux] Velopack release artifacts in $RELEASE_DIR."
