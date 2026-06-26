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

for RID in osx-x64 osx-arm64; do
  dotnet publish "$APP_PROJECT" -c Release -r "$RID" --self-contained true \
    -o "$OUT/$RID" "/p:Version=$VERSION"
done

# Universal bundle layout
mkdir -p "$BUNDLE/Contents/MacOS" "$BUNDLE/Contents/Resources"
lipo -create \
  "$OUT/osx-x64/Spectralis.App" \
  "$OUT/osx-arm64/Spectralis.App" \
  -output "$BUNDLE/Contents/MacOS/Spectralis"
# Managed assemblies are architecture-neutral; take the arm64 set.
rsync -a --exclude "Spectralis.App" "$OUT/osx-arm64/" "$BUNDLE/Contents/MacOS/"

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
