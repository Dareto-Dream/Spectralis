#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-5.0.0}"
OUT_DIR="dist/macos"
PUBLISH_DIR="publish/osx-arm64"
APP_DIR="$OUT_DIR/Spectralis.app"

echo "Building Spectralis $VERSION for macOS..."

dotnet publish Spectralis.App/Spectralis.App.csproj \
    -r osx-arm64 \
    -c Release \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:Version="$VERSION" \
    -o "$PUBLISH_DIR"

mkdir -p "$APP_DIR/Contents/MacOS"
mkdir -p "$APP_DIR/Contents/Resources"

cp "$PUBLISH_DIR/Spectralis.App" "$APP_DIR/Contents/MacOS/Spectralis"
chmod +x "$APP_DIR/Contents/MacOS/Spectralis"

cat > "$APP_DIR/Contents/Info.plist" << EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>Spectralis</string>
    <key>CFBundleIdentifier</key>
    <string>dev.deltav.spectralis</string>
    <key>CFBundleVersion</key>
    <string>${VERSION}</string>
    <key>CFBundleShortVersionString</key>
    <string>${VERSION}</string>
    <key>CFBundleExecutable</key>
    <string>Spectralis</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>LSMinimumSystemVersion</key>
    <string>13.0</string>
</dict>
</plist>
EOF

cp "packaging/macos/Spectralis.icns" "$APP_DIR/Contents/Resources/" 2>/dev/null || true

DMG_PATH="$OUT_DIR/Spectralis-$VERSION-arm64.dmg"
hdiutil create -volname "Spectralis $VERSION" \
    -srcfolder "$APP_DIR" \
    -ov -format UDZO \
    "$DMG_PATH"

echo "DMG built: $DMG_PATH"
