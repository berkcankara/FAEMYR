#!/bin/bash
# Builds App-macOS/Faemyr.app with Xcode Command Line Tools only (no Xcode, no .NET).
# Usage: bash macOS/build.sh [--debug]
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CONFIGURATION=release
[[ "${1:-}" == "--debug" ]] && CONFIGURATION=debug
OUTPUT="$ROOT/App-macOS"
BUILD="$ROOT/macOS/.build"
mkdir -p "$OUTPUT" "$BUILD"
# The repo may live in an iCloud-synced Documents folder; rebuilding the bundle there makes iCloud keep
# conflict copies ("Faemyr 2.app", …) that show up as extra apps. Keep build outputs out of sync.
xattr -w 'com.apple.fileprovider.ignore#P' 1 "$OUTPUT" 2>/dev/null || true
xattr -w 'com.apple.fileprovider.ignore#P' 1 "$BUILD" 2>/dev/null || true

# Keep the shared assets identical to the Windows build.
cp "$ROOT/Source/Assets/en.json" "$ROOT/macOS/Sources/FaemyrCore/Resources/en.json"
cp "$ROOT/Source/Assets/Icons/"*.svg "$ROOT/macOS/Sources/FaemyrCore/Resources/Icons/"

ARGS=(--package-path "$ROOT/macOS" --build-system native --configuration "$CONFIGURATION" --product Faemyr)
swift build "${ARGS[@]}"
BIN="$(swift build "${ARGS[@]}" --show-bin-path)"

STAGE="$(mktemp -d "${TMPDIR:-/tmp}/faemyr-build.XXXXXX")"
trap 'rm -rf "$STAGE"' EXIT
APP="$STAGE/Faemyr.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
ditto --norsrc --noextattr "$BIN/Faemyr" "$APP/Contents/MacOS/Faemyr"
ditto --norsrc --noextattr "$BIN/Faemyr_FaemyrCore.bundle" "$APP/Contents/Resources/Faemyr_FaemyrCore.bundle"
cp "$ROOT/macOS/Info.plist" "$APP/Contents/Info.plist"
printf 'APPL????' > "$APP/Contents/PkgInfo"

ICONSET="$BUILD/Faemyr.iconset"; rm -rf "$ICONSET"; mkdir -p "$ICONSET"
# Icon/Faemyr.png in the repo is truncated; take the largest PNG frame from the intact Windows .ico,
# then place it on the macOS icon grid (824 px artwork centred on a 1024 px canvas).
MASTER="$BUILD/icon-master.png"
python3 - "$ROOT/Icon/Faemyr.ico" "$BUILD/icon-ico.png" <<'PY'
import struct, sys
d = open(sys.argv[1], 'rb').read(); n = struct.unpack('<H', d[4:6])[0]
best = max((struct.unpack('<BBBBHHII', d[6 + 16*i:22 + 16*i]) for i in range(n)), key=lambda e: (e[0] or 256))
open(sys.argv[2], 'wb').write(d[best[7]:best[7] + best[6]])
PY
sips -s format png -z 824 824 "$BUILD/icon-ico.png" --out "$MASTER" >/dev/null
sips -p 1024 1024 "$MASTER" --out "$MASTER" >/dev/null
for SIZE in 16 32 128 256 512; do
  sips -s format png -z $SIZE $SIZE "$MASTER" --out "$ICONSET/icon_${SIZE}x${SIZE}.png" >/dev/null
  sips -s format png -z $((SIZE*2)) $((SIZE*2)) "$MASTER" --out "$ICONSET/icon_${SIZE}x${SIZE}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/Faemyr.icns"

# Optional FFmpeg helpers for Ogg/Opus/APE/WavPack/DSD (FAEMYR_AUDIO_HELPERS=/dir with ffmpeg + ffprobe).
if [[ -n "${FAEMYR_AUDIO_HELPERS:-}" ]]; then
  mkdir -p "$APP/Contents/Helpers"
  for TOOL in ffmpeg ffprobe; do ditto --norsrc --noextattr "$FAEMYR_AUDIO_HELPERS/$TOOL" "$APP/Contents/Helpers/$TOOL"; done
fi

xattr -cr "$APP"
codesign --force --deep --sign "${FAEMYR_SIGN_IDENTITY:--}" "$APP"
VERSION="$(plutil -extract CFBundleShortVersionString raw -o - "$ROOT/macOS/Info.plist")"
ditto -c -k --norsrc --noextattr --keepParent "$APP" "$OUTPUT/Faemyr-macOS-$(uname -m)-$VERSION.zip"
# Update the bundle in place (same folder identity) so macOS keeps one app entry instead of a new one per build.
mkdir -p "$OUTPUT/Faemyr.app"
rsync -a --delete "$APP/" "$OUTPUT/Faemyr.app/"
/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "$OUTPUT/Faemyr.app" 2>/dev/null || true
echo "Hazır: $OUTPUT/Faemyr.app"
