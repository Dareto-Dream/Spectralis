#!/usr/bin/env bash
# Release build entrypoint for macOS - the peer of build.ps1 (Windows/Linux).
#
#   ./build.sh --version 6.0.0            # macOS Velopack feeds (osx-arm64 + osx-x64)
#   ./build.sh --version 6.0.0 --dmg      # ...also the standalone .dmg bundle
#   ./build.sh --version 6.0.0 --dmg-only # just the .dmg, skip Velopack
#
# Produces, under releases-velopack/ (the same accumulating feed dir the Windows
# and Linux builds write to):
#   - Spectralis-<version>-osx-arm64-full.nupkg + releases.osx-arm64.json
#   - Spectralis-<version>-osx-x64-full.nupkg   + releases.osx-x64.json
#   - Spectralis-osx-*-Setup.pkg / -Portable.zip
# and with --dmg, releases/Spectralis-<version>.dmg.
#
# SPECTRALIS_SPOTIFY_CLIENT_ID and SPECTRALIS_DISCORD_CLIENT_ID must be set;
# a .env file at the repo root is loaded automatically if present.
#
# The Windows/Linux equivalents live in build.ps1. macOS cannot be cross-built
# from either, so this script runs on a macOS host (CI or local).
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

VERSION=""
BUILD_VELOPACK=1
BUILD_DMG=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --version)   VERSION="${2:?--version needs a value}"; shift 2 ;;
    --version=*) VERSION="${1#*=}"; shift ;;
    --dmg)       BUILD_DMG=1; shift ;;
    --dmg-only)  BUILD_DMG=1; BUILD_VELOPACK=0; shift ;;
    -h|--help)   sed -n '2,25p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *)           echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

if [[ -z "$VERSION" ]]; then
  echo "usage: ./build.sh --version <x.y.z> [--dmg | --dmg-only]" >&2
  exit 2
fi

if [[ -f "$REPO_ROOT/.env" ]]; then
  set -a
  # shellcheck disable=SC1091
  . "$REPO_ROOT/.env"
  set +a
fi

for required in SPECTRALIS_SPOTIFY_CLIENT_ID SPECTRALIS_DISCORD_CLIENT_ID; do
  if [[ -z "${!required:-}" ]]; then
    echo "error: $required is not set. Put it in $REPO_ROOT/.env or export it before building." >&2
    exit 1
  fi
done

if ! command -v dotnet >/dev/null 2>&1; then
  echo "error: dotnet SDK not found on PATH." >&2
  exit 1
fi

dotnet tool restore >/dev/null

if [[ "$BUILD_VELOPACK" == "1" ]]; then
  echo "[build] Spectralis $VERSION - macOS Velopack (osx-arm64 + osx-x64)"
  "$REPO_ROOT/Spectralis.Installer/Mac/build-velopack.sh" "$VERSION"
fi

if [[ "$BUILD_DMG" == "1" ]]; then
  echo "[build] Spectralis $VERSION - macOS .dmg"
  "$REPO_ROOT/Spectralis.Installer/Mac/build-dmg.sh" "$VERSION"
fi

echo
echo "[build] done - v$VERSION"
if [[ "$BUILD_VELOPACK" == "1" ]]; then
  for ch in osx-arm64 osx-x64; do
    feed="$REPO_ROOT/releases-velopack/releases.$ch.json"
    [[ -f "$feed" ]] && echo "  Velopack $ch : $feed"
  done
fi
if [[ "$BUILD_DMG" == "1" ]]; then
  echo "  dmg           : $REPO_ROOT/releases/Spectralis-$VERSION.dmg"
fi
