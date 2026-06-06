#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-5.0.0}"
OUT_DIR="dist/linux"
PUBLISH_DIR="publish/linux-x64"
APPDIR="$OUT_DIR/Spectralis.AppDir"

echo "Building Spectralis $VERSION for Linux..."

dotnet publish Spectralis.App/Spectralis.App.csproj \
    -r linux-x64 \
    -c Release \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:Version="$VERSION" \
    -o "$PUBLISH_DIR"

rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin"
mkdir -p "$APPDIR/usr/share/applications"
mkdir -p "$APPDIR/usr/share/icons/hicolor/256x256/apps"

cp "$PUBLISH_DIR/Spectralis.App" "$APPDIR/usr/bin/spectralis"
chmod +x "$APPDIR/usr/bin/spectralis"

cat > "$APPDIR/usr/share/applications/spectralis.desktop" << EOF
[Desktop Entry]
Name=Spectralis
Exec=spectralis
Icon=spectralis
Type=Application
Categories=Audio;Player;Music;
EOF

cp "$APPDIR/usr/share/applications/spectralis.desktop" "$APPDIR/spectralis.desktop"

if [ -f "packaging/linux/spectralis.png" ]; then
    cp "packaging/linux/spectralis.png" "$APPDIR/usr/share/icons/hicolor/256x256/apps/spectralis.png"
    cp "packaging/linux/spectralis.png" "$APPDIR/spectralis.png"
fi

cat > "$APPDIR/AppRun" << 'EOF'
#!/usr/bin/env bash
SELF=$(readlink -f "$0")
APPDIR=$(dirname "$SELF")
exec "$APPDIR/usr/bin/spectralis" "$@"
EOF
chmod +x "$APPDIR/AppRun"

APPIMAGETOOL="${APPIMAGETOOL:-appimagetool}"
APPIMAGE_PATH="$OUT_DIR/Spectralis-$VERSION-x86_64.AppImage"
ARCH=x86_64 "$APPIMAGETOOL" "$APPDIR" "$APPIMAGE_PATH"

echo "AppImage built: $APPIMAGE_PATH"
