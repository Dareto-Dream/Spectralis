#!/usr/bin/env bash
# Static macOS helpers: use the FFmpeg project's linked macOS distributor, not Homebrew dylibs.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
TOOLS="$ROOT/output/macos-tools"
mkdir -p "$TOOLS"
if [[ ! -f "$ROOT/yt-dlp-osx" ]]; then
  curl --fail --location --retry 3 https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_macos -o "$TOOLS/yt-dlp.download"
  mv "$TOOLS/yt-dlp.download" "$ROOT/yt-dlp-osx"
fi
if [[ ! -f "$ROOT/ffmpeg-osx" ]]; then
  curl --fail --location --retry 3 https://evermeet.cx/ffmpeg/getrelease/zip -o "$TOOLS/ffmpeg.zip"
  unzip -o "$TOOLS/ffmpeg.zip" -d "$TOOLS/ffmpeg"
  cp "$TOOLS/ffmpeg/ffmpeg" "$ROOT/ffmpeg-osx"
fi
chmod +x "$ROOT/yt-dlp-osx" "$ROOT/ffmpeg-osx"
file "$ROOT/yt-dlp-osx" "$ROOT/ffmpeg-osx"
file "$ROOT/yt-dlp-osx" | grep -q 'Mach-O'
file "$ROOT/ffmpeg-osx" | grep -q 'Mach-O'
"$ROOT/yt-dlp-osx" --version
"$ROOT/ffmpeg-osx" -version > "$TOOLS/ffmpeg-version.txt"
head -n 1 "$TOOLS/ffmpeg-version.txt"
shasum -a 256 "$ROOT/yt-dlp-osx" "$ROOT/ffmpeg-osx"
