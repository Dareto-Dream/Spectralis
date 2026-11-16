#!/usr/bin/env bash
# .dmg build for macOS. Requires a macOS host with Xcode CLT.
# Signing/notarization hooks: set SPECTRALIS_MAC_SIGN_IDENTITY and
# SPECTRALIS_NOTARY_PROFILE to enable; unsigned bundles build without them.
set -euo pipefail

VERSION="${1:?usage: build-dmg.sh <version>}"
REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
APP_PROJECT="$REPO_ROOT/Spectralis.App/Spectralis.App.csproj"
OUT="$REPO_ROOT/publish-mac"
BUNDLE="$OUT/Spectralis.app"
DMG="$REPO_ROOT/releases/Spectralis-$VERSION.dmg"

for required in SPECTRALIS_SPOTIFY_CLIENT_ID SPECTRALIS_DISCORD_CLIENT_ID; do
  if [[ -z "${!required:-}" ]]; then
    echo "error: $required is not set. Releases must ship with baked-in client IDs; set it before building." >&2
    exit 1
  fi
done

rm -rf "$OUT"
mkdir -p "$REPO_ROOT/releases"

RID="osx-$(uname -m)"
RID="${RID/osx-x86_64/osx-x64}"
MINIMUM_OS=12.0
[[ "$RID" == osx-arm64 ]] && MINIMUM_OS=13.0
export MACOSX_DEPLOYMENT_TARGET="$MINIMUM_OS"
bash "$REPO_ROOT/Spectralis.Installer/Mac/fetch-helpers.sh" "$RID"
dotnet publish "$APP_PROJECT" -c Release -f net8.0 -r "$RID" --self-contained true \
  -o "$OUT/$RID" "/p:Version=$VERSION"

if [[ ! -f "$OUT/$RID/libcef.dylib" ]]; then
  echo "error: libcef.dylib missing from the publish output; the CEF redist did not restore." >&2
  exit 1
fi

mkdir -p "$BUNDLE/Contents/MacOS" "$BUNDLE/Contents/Resources"
rsync -a "$OUT/$RID/" "$BUNDLE/Contents/MacOS/"
mv "$BUNDLE/Contents/MacOS/Spectralis.App" "$BUNDLE/Contents/MacOS/Spectralis"

METADATA="$REPO_ROOT/output/app-metadata/macos-legacy"
bash "$REPO_ROOT/Spectralis.Installer/Mac/generate-metadata.sh" "$VERSION" "$METADATA" Spectralis "$MINIMUM_OS"
cp "$METADATA/Info.plist" "$BUNDLE/Contents/Info.plist"
cp "$METADATA/Spectralis.icns" "$BUNDLE/Contents/Resources/Spectralis.icns"

if [[ -n "${SPECTRALIS_MAC_SIGN_IDENTITY:-}" ]]; then
  codesign --deep --force --options runtime --sign "$SPECTRALIS_MAC_SIGN_IDENTITY" "$BUNDLE"
fi

hdiutil create -volname "Spectralis" -srcfolder "$BUNDLE" -ov -format UDZO "$DMG"

if [[ -n "${SPECTRALIS_NOTARY_PROFILE:-}" ]]; then
  xcrun notarytool submit "$DMG" --keychain-profile "$SPECTRALIS_NOTARY_PROFILE" --wait
  xcrun stapler staple "$DMG"
fi

echo "Built $DMG"
