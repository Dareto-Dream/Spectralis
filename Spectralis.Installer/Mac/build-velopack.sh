#!/usr/bin/env bash
# Velopack packaging for macOS. Runs on a macOS host with the .NET SDK and the
# vpk tool (restored from .config/dotnet-tools.json). Produces osx-arm64 and
# osx-x64 channel feeds under releases-velopack/ for VelopackUpdateService
# (separate channels per arch; no universal binary needed).
#
#   ./Spectralis.Installer/Mac/build-velopack.sh <version>
#
# SPECTRALIS_SPOTIFY_CLIENT_ID and SPECTRALIS_DISCORD_CLIENT_ID must be set - the
# release scripts bake them into the assembly. The repo-root ./build.sh wrapper
# loads them from .env for you; when calling this script directly, export them
# first.
#
# Signing / notarization (all optional - unsigned bundles build without them):
#   SPECTRALIS_MAC_SIGN_IDENTITY          -> vpk --signAppIdentity
#   SPECTRALIS_MAC_INSTALL_SIGN_IDENTITY  -> vpk --signInstallIdentity
#   SPECTRALIS_NOTARY_PROFILE             -> vpk --notaryProfile
set -euo pipefail

VERSION="${1:?usage: build-velopack.sh <version>}"
REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
APP_PROJECT="$REPO_ROOT/Spectralis.App/Spectralis.App.csproj"
RELEASE_DIR="$REPO_ROOT/releases-velopack"

for required in SPECTRALIS_SPOTIFY_CLIENT_ID SPECTRALIS_DISCORD_CLIENT_ID; do
  if [[ -z "${!required:-}" ]]; then
    echo "error: $required is not set. Releases must ship with baked-in client IDs; set it before building." >&2
    exit 1
  fi
done

mkdir -p "$RELEASE_DIR"

# vpk 0.0.915 targets net8.0; roll forward to the installed .NET runtime.
export DOTNET_ROLL_FORWARD=LatestMajor

# vpk is a local tool - it only resolves from a directory under the repo root.
cd "$REPO_ROOT"

sign_args=()
if [[ -n "${SPECTRALIS_MAC_SIGN_IDENTITY:-}" ]]; then
  sign_args+=(--signAppIdentity "$SPECTRALIS_MAC_SIGN_IDENTITY")
fi
if [[ -n "${SPECTRALIS_MAC_INSTALL_SIGN_IDENTITY:-}" ]]; then
  sign_args+=(--signInstallIdentity "$SPECTRALIS_MAC_INSTALL_SIGN_IDENTITY")
fi
if [[ -n "${SPECTRALIS_NOTARY_PROFILE:-}" ]]; then
  sign_args+=(--notaryProfile "$SPECTRALIS_NOTARY_PROFILE")
fi

for RID in osx-arm64 osx-x64; do
  CHANNEL="$RID"
  PUBLISH_DIR="$REPO_ROOT/publish-velopack-$RID"
  rm -rf "$PUBLISH_DIR"

  # The explicit -r overrides the csproj's OSX default of osx-x64, so an
  # osx-arm64 pass really does come out arm64.
  dotnet publish "$APP_PROJECT" \
    -c Release \
    -f net8.0 \
    -r "$RID" \
    --self-contained true \
    /p:Version="$VERSION" \
    /p:SPECTRALIS_SPOTIFY_CLIENT_ID="$SPECTRALIS_SPOTIFY_CLIENT_ID" \
    /p:SPECTRALIS_DISCORD_CLIENT_ID="$SPECTRALIS_DISCORD_CLIENT_ID" \
    -o "$PUBLISH_DIR"

  # CefGlue's pinned CEF redist ships osx64 only, so an osx-arm64 publish comes
  # out with no libcef.dylib and a dead in-app browser (same reason build-dmg.sh
  # is x64-only). Hard-fail if x64 is missing it; only warn for arm64.
  if [[ ! -f "$PUBLISH_DIR/libcef.dylib" ]]; then
    if [[ "$RID" == "osx-x64" ]]; then
      echo "error: libcef.dylib missing from $RID publish output; the CEF redist did not restore." >&2
      exit 1
    fi
    echo "warning: [$RID] libcef.dylib absent - in-app browser is disabled in this build (CEF has no arm64 redist)." >&2
  fi

  rm -f "$RELEASE_DIR/Spectralis-$VERSION-$CHANNEL-full.nupkg" \
        "$RELEASE_DIR/releases.$CHANNEL.json"

  dotnet vpk pack \
    --packId "Spectralis" \
    --packVersion "$VERSION" \
    --packDir "$PUBLISH_DIR" \
    --mainExe "Spectralis.App" \
    --packTitle "Spectralis" \
    --packAuthors "DeltaV Devs" \
    --bundleId "com.deltavdevs.spectralis" \
    --channel "$CHANNEL" \
    ${sign_args[@]+"${sign_args[@]}"} \
    -o "$RELEASE_DIR"

  if [[ ! -f "$RELEASE_DIR/releases.$CHANNEL.json" ]]; then
    echo "error: Velopack feed not found after pack: $RELEASE_DIR/releases.$CHANNEL.json" >&2
    exit 1
  fi

  echo "[mac] Velopack $CHANNEL done."
done

echo "[mac] Velopack release artifacts in $RELEASE_DIR."
