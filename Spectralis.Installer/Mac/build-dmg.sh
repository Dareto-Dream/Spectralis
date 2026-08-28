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

# x64 only, deliberately. CefGlue's pinned CEF redistribution ships an osx64
# package and nothing for arm64, so an osx-arm64 publish silently comes out with
# no libcef.dylib and no CEF Resources -- the in-app browser would be dead. On
# Apple Silicon this bundle runs under Rosetta.
#
# The explicit -f is required: Spectralis.App multitargets, and `dotnet publish`
# refuses to pick a framework on its own (NETSDK1129).
RID=osx-x64
dotnet publish "$APP_PROJECT" -c Release -f net8.0 -r "$RID" --self-contained true \
  -o "$OUT/$RID" "/p:Version=$VERSION"

if [[ ! -f "$OUT/$RID/libcef.dylib" ]]; then
  echo "error: libcef.dylib missing from the publish output; the CEF redist did not restore." >&2
  exit 1
fi

mkdir -p "$BUNDLE/Contents/MacOS" "$BUNDLE/Contents/Resources"
rsync -a "$OUT/$RID/" "$BUNDLE/Contents/MacOS/"
mv "$BUNDLE/Contents/MacOS/Spectralis.App" "$BUNDLE/Contents/MacOS/Spectralis"

cat > "$BUNDLE/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>Spectralis</string>
  <key>CFBundleIdentifier</key><string>com.deltavdevs.spectralis</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleExecutable</key><string>Spectralis</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>CFBundleURLTypes</key><array><dict>
    <key>CFBundleURLName</key><string>Spectralis Protocol</string>
    <key>CFBundleURLSchemes</key><array><string>spectralis</string></array>
  </dict></array>
</dict></plist>
PLIST

if [[ -n "${SPECTRALIS_MAC_SIGN_IDENTITY:-}" ]]; then
  codesign --deep --force --options runtime --sign "$SPECTRALIS_MAC_SIGN_IDENTITY" "$BUNDLE"
fi

hdiutil create -volname "Spectralis" -srcfolder "$BUNDLE" -ov -format UDZO "$DMG"

if [[ -n "${SPECTRALIS_NOTARY_PROFILE:-}" ]]; then
  xcrun notarytool submit "$DMG" --keychain-profile "$SPECTRALIS_NOTARY_PROFILE" --wait
  xcrun stapler staple "$DMG"
fi

echo "Built $DMG"
