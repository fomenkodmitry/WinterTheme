#!/usr/bin/env bash
# Builds a self-contained WinterTheme.app (menu bar only, no Dock icon) and a drag-to-install .dmg.
#   ./build-app.sh              -> Apple Silicon (arm64)
#   ARCH=x64 ./build-app.sh     -> Intel
#   ARCH=all ./build-app.sh     -> both
set -euo pipefail
cd "$(dirname "$0")"

VERSION="${VERSION:-1.1.0}"
ARCH="${ARCH:-arm64}"
[[ "$ARCH" == "all" ]] && ARCHES=(arm64 x64) || ARCHES=("$ARCH")

build_one() {
  local arch="$1"
  local rid="osx-$arch"
  local out="publish/$rid"
  local stage="dist/$arch"
  local app="$stage/WinterTheme.app"
  local dmg="dist/WinterTheme-$VERSION-$arch.dmg"

  echo "==> publish $rid"
  rm -rf "$out"
  dotnet publish WinterTheme/WinterTheme.csproj -c Release -r "$rid" --self-contained true \
    -p:DebugType=none -p:Version="$VERSION" -o "$out" | grep -E "error|->" || true
  [[ -x "$out/WinterTheme" ]] || { echo "publish failed"; exit 1; }

  echo "==> bundle"
  rm -rf "$stage"
  mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
  cp -R "$out/." "$app/Contents/MacOS/"

  # icon (rendered by the app itself; the x64 binary can run under Rosetta, or reuse the arm64 icon)
  local iconset="publish/AppIcon.iconset"
  if [[ ! -f publish/AppIcon.icns ]]; then
    rm -rf "$iconset"
    "$out/WinterTheme" --export-iconset "$iconset"
    iconutil -c icns "$iconset" -o publish/AppIcon.icns
  fi
  cp publish/AppIcon.icns "$app/Contents/Resources/AppIcon.icns"

  cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>WinterTheme</string>
  <key>CFBundleDisplayName</key><string>Зимняя тема</string>
  <key>CFBundleIdentifier</key><string>local.wintertheme</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleExecutable</key><string>WinterTheme</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>LSUIElement</key><true/>
  <key>LSApplicationCategoryType</key><string>public.app-category.entertainment</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST

  echo "==> sign (ad-hoc)"
  codesign --force --deep --timestamp=none -s - "$app"
  codesign --verify --deep "$app"

  echo "==> dmg"
  local dmgsrc="publish/dmg-$arch"
  rm -rf "$dmgsrc" "$dmg"
  mkdir -p "$dmgsrc"
  cp -R "$app" "$dmgsrc/"
  ln -s /Applications "$dmgsrc/Applications"
  hdiutil create -quiet -volname "Зимняя тема" -srcfolder "$dmgsrc" -ov -format UDZO "$dmg"
  rm -rf "$dmgsrc"

  echo "Built $app"
  echo "Built $dmg"
}

rm -f publish/AppIcon.icns
for a in "${ARCHES[@]}"; do build_one "$a"; done
