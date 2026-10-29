#!/usr/bin/env bash
set -euo pipefail
[[ "$(uname -s)" == Darwin ]] || { echo 'Native macOS packaging requires a macOS host/runner.' >&2; exit 1; }
rid="${1:-osx-arm64}"
version="${2:-1.0.0}"
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo 'Version must be major.minor.patch' >&2; exit 1; }
case "$rid" in osx-arm64) label=AppleSilicon; expected=arm64;; osx-x64) label=Intel; expected=x86_64;; *) echo 'Use osx-arm64 or osx-x64' >&2; exit 1;; esac
[[ "$(uname -m)" == "$expected" ]] || { echo 'Use a matching native architecture runner to bundle OCR libraries.' >&2; exit 1; }
repo="$(cd "$(dirname "$0")/.." && pwd)"
stage="$(mktemp -d "${TMPDIR:-/tmp}/prasaresearch-package.XXXXXX")"
mounted=''
cleanup() { if [[ -n "$mounted" ]]; then hdiutil detach "$mounted" -quiet || true; fi; rm -rf -- "$stage"; }
trap cleanup EXIT
app="$stage/PrasaResearch.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources" "$repo/Releases/macOS"
# Keep managed PE assemblies inside the .NET apphost. Loose .dll assemblies in
# Contents/MacOS are incorrectly treated as unsigned nested code by codesign.
# Native dylibs stay beside the host and are signed individually below.
dotnet publish "$repo/PrasaResearch.Desktop" -c Release -r "$rid" --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -p:Version="$version" -o "$app/Contents/MacOS"
cp "$repo/packaging/macos/Info.plist" "$app/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $version" "$app/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion $version" "$app/Contents/Info.plist"
iconset="$stage/PrasaResearch.iconset"; mkdir -p "$iconset"
for size in 16 32 128 256 512; do
 sips -z "$size" "$size" "$repo/packaging/branding/app.png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
 double=$((size*2)); sips -z "$double" "$double" "$repo/packaging/branding/app.png" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/PrasaResearch.icns"
ocr="$app/Contents/MacOS/ocr"; mkdir -p "$ocr/tessdata"
cp "$(command -v tesseract)" "$ocr/tesseract"
data="$(brew --prefix)/share/tessdata"
cp "$data/eng.traineddata" "$data/osd.traineddata" "$ocr/tessdata/"
if [[ -d "$data/configs" ]]; then cp -R "$data/configs" "$ocr/tessdata/"; fi
dylibbundler -od -b -x "$ocr/tesseract" -d "$ocr/lib" -p '@executable_path/lib/'
cp "$repo/packaging/THIRD-PARTY-NOTICES.txt" "$app/Contents/Resources/"
cp "$repo/packaging/Distribution-Notice.txt" "$app/Contents/Resources/"
pwsh -NoProfile -File "$repo/packaging/Collect-Licenses.ps1" -OutputDirectory "$app/Contents/Resources/licenses"
for component in tesseract leptonica; do
 prefix="$(brew --prefix "$component")"
 mkdir -p "$app/Contents/Resources/licenses/$component"
 find "$prefix" -maxdepth 2 -iname '*license*' -type f -exec cp {} "$app/Contents/Resources/licenses/$component/" \;
done
find "$app" -type d -exec chmod 755 {} +
chmod 755 "$app/Contents/MacOS/PrasaResearch" "$ocr/tesseract"
identity="${MACOS_SIGN_IDENTITY:--}"
while IFS= read -r -d '' file; do
 if file -b "$file" | grep -q 'Mach-O'; then
  if [[ "$identity" == '-' ]]; then codesign --force --timestamp=none --sign - "$file";
  else codesign --force --options runtime --timestamp --sign "$identity" "$file"; fi
 fi
done < <(find "$app/Contents/MacOS" -type f -print0)
if [[ "$identity" == '-' ]]; then
 codesign --force --deep --sign - --entitlements "$repo/packaging/macos/entitlements.plist" "$app"
else
 codesign --force --deep --options runtime --timestamp --sign "$identity" --entitlements "$repo/packaging/macos/entitlements.plist" "$app"
fi
codesign --verify --deep --strict --verbose=2 "$app"
"$ocr/tesseract" --version
export PRASARESEARCH_DATA_DIR="$stage/smoke-data"
python3 - "$app/Contents/MacOS/PrasaResearch" "$stage/smoke.json" <<'PY'
import json, pathlib, subprocess, sys
subprocess.run([sys.argv[1], '--smoke-output=' + sys.argv[2]], check=True, timeout=120)
assert json.loads(pathlib.Path(sys.argv[2]).read_text())['launched'] is True
PY
test -s "$stage/smoke.json"
cp "$stage/smoke.json" "$stage/smoke.json.png" "$repo/Releases/macOS/"
unset PRASARESEARCH_DATA_DIR
# Keep a distributable app archive in addition to the Finder DMG.
ditto -c -k --keepParent "$app" "$repo/Releases/macOS/PrasaResearch-$label.app.zip"
content="$stage/dmg"; mkdir -p "$content/.background"
ditto "$app" "$content/PrasaResearch.app"
ln -s /Applications "$content/Applications"
cp "$repo/packaging/branding/dmg-background.png" "$content/.background/background.png"
volume="PrasaResearch-$label"
hdiutil create -quiet -volname "$volume" -srcfolder "$content" -format UDRW "$stage/edit.dmg"
mounted="$stage/mount"; mkdir -p "$mounted"
hdiutil attach "$stage/edit.dmg" -nobrowse -mountpoint "$mounted" -quiet
osascript <<EOF
tell application "Finder"
 tell disk "$volume"
  open
  set current view of container window to icon view
  set toolbar visible of container window to false
  set statusbar visible of container window to false
  set bounds of container window to {100, 100, 900, 580}
  set options to the icon view options of container window
  set arrangement of options to not arranged
  set icon size of options to 104
  set background picture of options to file ".background:background.png"
  set position of item "PrasaResearch.app" of container window to {200, 260}
  set position of item "Applications" of container window to {600, 260}
  update without registering applications
  close
 end tell
end tell
EOF
sync
hdiutil detach "$mounted" -quiet; mounted=''
destination="$repo/Releases/macOS/PrasaResearch-Setup-macOS-$label.dmg"
hdiutil convert "$stage/edit.dmg" -format UDZO -imagekey zlib-level=9 -o "$destination" -quiet
if [[ -n "${MACOS_NOTARY_PROFILE:-}" ]]; then
 [[ "$identity" != '-' ]] || { echo 'Notarization needs a Developer ID identity.' >&2; exit 1; }
 xcrun notarytool submit "$destination" --keychain-profile "$MACOS_NOTARY_PROFILE" --wait
 xcrun stapler staple "$destination"
 xcrun stapler validate "$destination"
fi
hdiutil verify "$destination"
shasum -a 256 "$destination"
echo "Created $destination; identity=$identity; notarization requested=${MACOS_NOTARY_PROFILE:+yes}"
