#!/usr/bin/env bash
# One-line install, no Gatekeeper prompt:
#   curl -fsSL https://raw.githubusercontent.com/fomenkodmitry/WinterTheme/main/install.sh | bash
set -euo pipefail

VERSION="1.1.0"
URL="https://github.com/fomenkodmitry/WinterTheme/raw/main/release/WinterTheme-$VERSION-arm64.dmg"

if [[ "$(uname -m)" != "arm64" ]]; then
  echo "Only Apple Silicon builds are published. On Intel, build from source: ARCH=x64 ./build-app.sh" >&2
  exit 1
fi

TMP="$(mktemp -d)"
trap 'hdiutil detach -quiet "$TMP/mnt" 2>/dev/null || true; rm -rf "$TMP"' EXIT

echo "Downloading WinterTheme $VERSION..."
curl -fL --progress-bar "$URL" -o "$TMP/WinterTheme.dmg"

hdiutil attach -quiet -nobrowse -mountpoint "$TMP/mnt" "$TMP/WinterTheme.dmg"

pkill -f "WinterTheme.app/Contents/MacOS/WinterTheme" 2>/dev/null || true
rm -rf /Applications/WinterTheme.app
cp -R "$TMP/mnt/WinterTheme.app" /Applications/
xattr -dr com.apple.quarantine /Applications/WinterTheme.app 2>/dev/null || true

open /Applications/WinterTheme.app
echo "Installed /Applications/WinterTheme.app. Look for the snowflake in the menu bar."
