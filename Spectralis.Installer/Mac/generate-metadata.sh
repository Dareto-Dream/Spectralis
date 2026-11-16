#!/usr/bin/env bash
# Build native bundle resources from the same artwork used by the app windows.
set -euo pipefail

VERSION="${1:?usage: generate-metadata.sh <version> <output-directory> [executable]}"
OUT="${2:?output directory required}"
EXECUTABLE="${3:-Spectralis.App}"
REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]]; then
  echo "error: invalid release version: $VERSION" >&2
  exit 1
fi
SHORT_VERSION="${VERSION%%-*}"
ICONSET="$OUT/Spectralis.iconset"
mkdir -p "$ICONSET"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$REPO_ROOT/Assets/icon.png" --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
  double=$((size * 2))
  sips -z "$double" "$double" "$REPO_ROOT/Assets/icon.png" --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil --convert icns --output "$OUT/Spectralis.icns" "$ICONSET"

cat > "$OUT/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>CFBundleName</key><string>Spectralis</string>
  <key>CFBundleDisplayName</key><string>Spectralis</string>
  <key>CFBundleIdentifier</key><string>com.deltavdevs.spectralis</string>
  <key>CFBundleVersion</key><string>$SHORT_VERSION</string>
  <key>CFBundleShortVersionString</key><string>$SHORT_VERSION</string>
  <key>SpectralisReleaseVersion</key><string>$VERSION</string>
  <key>CFBundleExecutable</key><string>$EXECUTABLE</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleIconFile</key><string>Spectralis.icns</string>
  <key>NSPrincipalClass</key><string>NSApplication</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>LSApplicationCategoryType</key><string>public.app-category.music</string>
  <key>CFBundleURLTypes</key><array><dict>
    <key>CFBundleURLName</key><string>Spectralis Protocol</string>
    <key>CFBundleURLSchemes</key><array><string>spectralis</string></array>
  </dict></array>
</dict></plist>
PLIST
plutil -lint "$OUT/Info.plist"
