#!/usr/bin/env bash
# AppImage build for Linux. Downloads appimagetool automatically if not on PATH.
set -euo pipefail

VERSION="${1:?usage: build-appimage.sh <version>}"
REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
APP_PROJECT="$REPO_ROOT/Spectralis.App/Spectralis.App.csproj"
APPDIR="$REPO_ROOT/publish-linux/Spectralis.AppDir"
RELEASE_DIR="$REPO_ROOT/releases"
OUTPUT="$RELEASE_DIR/Spectralis-$VERSION-x86_64.AppImage"

for required in SPECTRALIS_SPOTIFY_CLIENT_ID SPECTRALIS_DISCORD_CLIENT_ID; do
  if [[ -z "${!required:-}" ]]; then
    echo "error: $required is not set. Releases must ship with baked-in client IDs; set it before building." >&2
    exit 1
  fi
done

# Ensure appimagetool is available, downloading to a local cache if needed.
TOOL_CACHE="$HOME/.local/bin/appimagetool"
if command -v appimagetool &>/dev/null; then
  APPIMAGETOOL="appimagetool"
elif [[ -x "$TOOL_CACHE" ]]; then
  APPIMAGETOOL="$TOOL_CACHE"
else
  echo "[linux] appimagetool not found — downloading to $TOOL_CACHE..."
  mkdir -p "$(dirname "$TOOL_CACHE")"
  TOOL_URL="https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage"
  curl -fsSL "$TOOL_URL" -o "$TOOL_CACHE"
  chmod +x "$TOOL_CACHE"
  APPIMAGETOOL="$TOOL_CACHE"
fi
# WSL typically lacks FUSE; use the extract-and-run workaround.
if ! "$APPIMAGETOOL" --version &>/dev/null 2>&1; then
  APPIMAGETOOL="$APPIMAGETOOL --appimage-extract-and-run"
fi

rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin" "$RELEASE_DIR"

dotnet publish "$APP_PROJECT" \
  -c Release \
  -f net8.0 \
  -r linux-x64 \
  --self-contained true \
  /p:TargetFrameworks=net8.0 \
  /p:Version="$VERSION" \
  /p:SPECTRALIS_SPOTIFY_CLIENT_ID="$SPECTRALIS_SPOTIFY_CLIENT_ID" \
  /p:SPECTRALIS_DISCORD_CLIENT_ID="$SPECTRALIS_DISCORD_CLIENT_ID" \
  -o "$APPDIR/usr/bin"

cat > "$APPDIR/AppRun" <<'RUN'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/bin/Spectralis.App" "$@"
RUN
chmod +x "$APPDIR/AppRun"

cat > "$APPDIR/spectralis.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=Spectralis
Exec=Spectralis.App %U
Icon=spectralis
Categories=AudioVideo;Audio;Player;
MimeType=audio/mpeg;audio/flac;audio/ogg;audio/x-wav;audio/mp4;x-scheme-handler/spectralis;
DESKTOP

if [[ -f "$REPO_ROOT/Assets/icon.png" ]]; then
  cp "$REPO_ROOT/Assets/icon.png" "$APPDIR/spectralis.png"
fi

ARCH=x86_64 $APPIMAGETOOL "$APPDIR" "$OUTPUT"
echo "[linux] Built $OUTPUT"
