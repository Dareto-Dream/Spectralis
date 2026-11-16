#!/usr/bin/env bash
# Velopack packaging for macOS. Runs on a macOS host with the .NET SDK and the
# vpk tool (restored from .config/dotnet-tools.json). Build on the matching
# Intel or Apple Silicon host so .NET, CEF, Rust and media helpers stay native.
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

VERSION="${1:?usage: build-velopack.sh <version> [osx-x64|osx-arm64]}"
RID="${2:-osx-$(uname -m)}"
RID="${RID/osx-x86_64/osx-x64}"
case "$RID" in
  osx-arm64) NATIVE_ARCH=arm64; export MACOSX_DEPLOYMENT_TARGET=13.0 ;;
  osx-x64) NATIVE_ARCH=x86_64; export MACOSX_DEPLOYMENT_TARGET=12.0 ;;
  *) echo "Unsupported Mac RID: $RID" >&2; exit 1 ;;
esac
if [[ "$(uname -m)" != "$NATIVE_ARCH" ]]; then
  echo "Build $RID on a matching native Mac runner." >&2
  exit 1
fi
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

bash "$REPO_ROOT/Spectralis.Installer/Mac/fetch-helpers.sh" "$RID"
cargo build --release --manifest-path "$REPO_ROOT/wgpu-host/Cargo.toml"
bash "$REPO_ROOT/Spectralis.Installer/Mac/generate-metadata.sh" "$VERSION" "$REPO_ROOT/output/app-metadata/macos" Spectralis.App "$MACOSX_DEPLOYMENT_TARGET"

for RID in "$RID"; do
  CHANNEL="$RID"
  PUBLISH_DIR="$REPO_ROOT/publish-velopack-$RID"
  rm -rf "$PUBLISH_DIR"

  dotnet publish "$REPO_ROOT/Spectralis.Installer/Updater/Spectralis.Updater.csproj" \
    -c Release -r "$RID" --self-contained true \
    /p:PublishSingleFile=true /p:PublishTrimmed=true /p:EnableCompressionInSingleFile=true \
    /p:IncludeNativeLibrariesForSelfExtract=true -o "$REPO_ROOT/output/updater/$RID"
  "$REPO_ROOT/output/updater/$RID/UpdateSpectralis" --version

  # Keep the application, CEF and the recovery executable on the same architecture.
  dotnet publish "$APP_PROJECT" \
    -c Release \
    -f net8.0 \
    /p:TargetFrameworks=net8.0 \
    -r "$RID" \
    --self-contained true \
    /p:Version="$VERSION" \
    /p:SPECTRALIS_SPOTIFY_CLIENT_ID="$SPECTRALIS_SPOTIFY_CLIENT_ID" \
    /p:SPECTRALIS_DISCORD_CLIENT_ID="$SPECTRALIS_DISCORD_CLIENT_ID" \
    -o "$PUBLISH_DIR"

  # Missing CEF would leave the in-app browser unusable even though publishing succeeded.
  if [[ ! -f "$PUBLISH_DIR/libcef.dylib" ]]; then
    echo "error: libcef.dylib missing from $RID publish output; the CEF redist did not restore." >&2
    exit 1
  fi
  test -f "$PUBLISH_DIR/Updater/UpdateSpectralis" || { echo "Recovery updater missing" >&2; exit 1; }
  test -f "$PUBLISH_DIR/libwgpu_host.dylib" || { echo "Native visualizer missing" >&2; exit 1; }
  chmod +x "$PUBLISH_DIR/Updater/UpdateSpectralis" "$PUBLISH_DIR/Tools/yt-dlp/yt-dlp_macos" "$PUBLISH_DIR/Tools/deno/deno"
  for native in Spectralis.App libcef.dylib libwgpu_host.dylib ffmpeg Updater/UpdateSpectralis Tools/deno/deno Tools/yt-dlp/yt-dlp_macos; do
    lipo "$PUBLISH_DIR/$native" -verify_arch "$NATIVE_ARCH"
  done
  "$PUBLISH_DIR/Tools/yt-dlp/yt-dlp_macos" --version
  "$PUBLISH_DIR/Tools/deno/deno" --version

  rm -f "$RELEASE_DIR/Spectralis-$VERSION-$CHANNEL-full.nupkg" \
        "$RELEASE_DIR/releases.$CHANNEL.json"

  dotnet vpk pack \
    --packId "Spectralis" \
    --packVersion "$VERSION" \
    --packDir "$PUBLISH_DIR" \
    --mainExe "Spectralis.App" \
    --packTitle "Spectralis" \
    --packAuthors "DeltaV Devs" \
    --icon "$REPO_ROOT/output/app-metadata/macos/Spectralis.icns" \
    --plist "$REPO_ROOT/output/app-metadata/macos/Info.plist" \
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
