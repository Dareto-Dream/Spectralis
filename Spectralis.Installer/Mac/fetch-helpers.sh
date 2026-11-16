#!/usr/bin/env bash
# Keep the resolver unpacked and its JavaScript runtime available even when Finder supplies a minimal PATH.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
TOOLS="$ROOT/output/macos-tools"
RID="${1:-osx-$(uname -m)}"
RID="${RID/osx-x86_64/osx-x64}"
case "$RID" in
  osx-arm64) ARCH=arm64; DENO_ARCH=aarch64 ;;
  osx-x64) ARCH=x86_64; DENO_ARCH=x86_64 ;;
  *) echo "Unsupported Mac RID: $RID" >&2; exit 1 ;;
esac
mkdir -p "$TOOLS/yt-dlp" "$TOOLS/deno"
YTDLP_VERSION=2026.10.26
DENO_VERSION=v2.9.8

if [[ ! -f "$TOOLS/yt-dlp/yt-dlp_macos" ]]; then
  base="https://github.com/yt-dlp/yt-dlp/releases/download/$YTDLP_VERSION"
  curl --fail --location --retry 3 "$base/yt-dlp_macos.zip" -o "$TOOLS/yt-dlp_macos.zip"
  curl --fail --location --retry 3 "$base/SHA2-256SUMS" -o "$TOOLS/ytdlp-sha256.txt"
  expected="$(awk '$2 == "yt-dlp_macos.zip" {print $1}' "$TOOLS/ytdlp-sha256.txt")"
  [[ "$expected" =~ ^[0-9a-fA-F]{64}$ ]]
  printf '%s  %s\n' "$expected" "$TOOLS/yt-dlp_macos.zip" | shasum -a 256 -c -
  unzip -qo "$TOOLS/yt-dlp_macos.zip" -d "$TOOLS/yt-dlp"
fi

if [[ ! -f "$TOOLS/deno/deno" ]]; then
  archive="deno-$DENO_ARCH-apple-darwin.zip"
  base="https://github.com/denoland/deno/releases/download/$DENO_VERSION"
  curl --fail --location --retry 3 "$base/$archive" -o "$TOOLS/$archive"
  curl --fail --location --retry 3 "$base/$archive.sha256sum" -o "$TOOLS/deno-sha256.txt"
  expected="$(awk 'NR == 1 {print $1}' "$TOOLS/deno-sha256.txt")"
  [[ "$expected" =~ ^[0-9a-fA-F]{64}$ ]]
  printf '%s  %s\n' "$expected" "$TOOLS/$archive" | shasum -a 256 -c -
  unzip -qo "$TOOLS/$archive" -d "$TOOLS/deno"
fi

if [[ ! -f "$ROOT/ffmpeg-osx" ]] || ! lipo "$ROOT/ffmpeg-osx" -verify_arch "$ARCH" >/dev/null 2>&1; then
  if [[ "$RID" == osx-arm64 ]]; then
    # This native static build requires macOS 13; the ARM64 app bundle declares the same minimum.
    curl --fail --location --retry 3 https://www.osxexperts.net/ffmpeg902arm.zip -o "$TOOLS/ffmpeg.zip"
    unzip -qo "$TOOLS/ffmpeg.zip" -d "$TOOLS/ffmpeg"
    printf '%s  %s\n' 6deb2860c3dc0c08e0f96bab119196c024103046fa3cc7ef72e3a77797d8553c "$TOOLS/ffmpeg/ffmpeg" | shasum -a 256 -c -
  else
    curl --fail --location --retry 3 https://www.osxexperts.net/ffmpeg80intel.zip -o "$TOOLS/ffmpeg.zip"
    unzip -qo "$TOOLS/ffmpeg.zip" -d "$TOOLS/ffmpeg"
    printf '%s  %s\n' df3f1e3facdc1ae0ad0bd898cdfb072fbc9641bf47b11f172844525a05db8d11 "$TOOLS/ffmpeg/ffmpeg" | shasum -a 256 -c -
  fi
  cp "$TOOLS/ffmpeg/ffmpeg" "$ROOT/ffmpeg-osx"
fi

chmod +x "$TOOLS/yt-dlp/yt-dlp_macos" "$TOOLS/deno/deno" "$ROOT/ffmpeg-osx"
for helper in "$TOOLS/yt-dlp/yt-dlp_macos" "$TOOLS/deno/deno" "$ROOT/ffmpeg-osx"; do
  lipo "$helper" -verify_arch "$ARCH"
done
"$TOOLS/yt-dlp/yt-dlp_macos" --version
"$TOOLS/deno/deno" --version
"$ROOT/ffmpeg-osx" -version > "$TOOLS/ffmpeg-version.txt"
head -n 1 "$TOOLS/ffmpeg-version.txt"
"$ROOT/ffmpeg-osx" -hide_banner -devices > "$TOOLS/ffmpeg-devices.txt" 2>&1
grep -Eq '^[[:space:]]*E[[:space:]]+audiotoolbox[[:space:]]' "$TOOLS/ffmpeg-devices.txt" || {
  echo "FFmpeg is missing the AudioToolbox output device." >&2
  exit 1
}
shasum -a 256 "$TOOLS/yt-dlp/yt-dlp_macos" "$TOOLS/deno/deno" "$ROOT/ffmpeg-osx"
