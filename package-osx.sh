#!/bin/bash

Arch="$1"
OutputPath="$2"
Version="$3"

FileName="v2rayN-${Arch}.zip"
    wget_cmd() {
  local url="$1" out="$2"
  curl -fL --retry 8 --retry-delay 3 --connect-timeout 30 --max-time 600 -o "$out" "$url" \
    || wget -q --tries=8 --timeout=600 -O "$out" "$url"
}
[ -s "$FileName" ] || wget_cmd "https://github.com/2dust/v2rayN-core-bin/raw/refs/heads/master/$FileName" "$FileName"
rm -rf CoreTmp
7z x "$FileName" -oCoreTmp >/dev/null
mkdir -p "$OutputPath"
cp -rf "CoreTmp/v2rayN-${Arch}/." "$OutputPath/"
rm -rf CoreTmp

PackagePath="macdist/v2rayN-Package-${Arch}"
mkdir -p "$PackagePath/v2rayN.app/Contents/Resources" macdist
cp -rf "$OutputPath" "$PackagePath/v2rayN.app/Contents/MacOS"
cp -f "$PackagePath/v2rayN.app/Contents/MacOS/v2rayN.icns" "$PackagePath/v2rayN.app/Contents/Resources/AppIcon.icns"
echo "When this file exists, app will not store configs under this folder" > "$PackagePath/v2rayN.app/Contents/MacOS/NotStoreConfigHere.txt"
chmod +x "$PackagePath/v2rayN.app/Contents/MacOS/v2rayN"

cat >"$PackagePath/v2rayN.app/Contents/Info.plist" <<-EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key>
  <string>en</string>
  <key>CFBundleLocalizations</key>
  <array>
    <string>zh-Hans</string>
    <string>zh-Hant</string>
    <string>en</string>
    <string>fa</string>
    <string>fr</string>
    <string>ru</string>
    <string>hu</string>
  </array>
  <key>CFBundleDisplayName</key>
  <string>v2rayN</string>
  <key>CFBundleExecutable</key>
  <string>v2rayN</string>
  <key>CFBundleIconFile</key>
  <string>AppIcon</string>
  <key>CFBundleIconName</key>
  <string>AppIcon</string>
  <key>CFBundleIdentifier</key>
  <string>2dust.v2rayN</string>
  <key>CFBundleName</key>
  <string>v2rayN</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>${Version}</string>
  <key>CSResourcesFileMapped</key>
  <true/>
  <key>NSHighResolutionCapable</key>
  <true/>
  <key>LSMinimumSystemVersion</key>
  <string>13.6</string>
</dict>
</plist>
EOF

create-dmg \
    --volname "v2rayN Installer" \
    --window-size 700 420 \
    --icon-size 100 \
    --icon "v2rayN.app" 160 185 \
    --hide-extension "v2rayN.app" \
    --app-drop-link 500 185 \
    "macdist/v2rayN-${Arch}.dmg" \
    "$PackagePath/v2rayN.app"
