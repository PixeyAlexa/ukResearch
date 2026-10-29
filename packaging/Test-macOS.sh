#!/usr/bin/env bash
set -euo pipefail
[[ "$(uname -s)" == Darwin ]] || { echo 'This test requires macOS.' >&2; exit 1; }
label="${1:?Expected AppleSilicon or Intel}"
case "$label" in AppleSilicon|Intel) ;; *) exit 1;; esac
repo="$(cd "$(dirname "$0")/.." && pwd)"
dmg="$repo/Releases/macOS/PrasaResearch-Setup-macOS-$label.dmg"
stage="$(mktemp -d "${TMPDIR:-/tmp}/prasaresearch-install-test.XXXXXX")"
mounted=''
cleanup() {
  if [[ -n "$mounted" ]]; then hdiutil detach "$mounted" -quiet || true; fi
  rm -rf -- "$stage"
}
trap cleanup EXIT
hdiutil verify "$dmg"
mounted="$stage/mount"; mkdir -p "$mounted"
hdiutil attach "$dmg" -readonly -nobrowse -mountpoint "$mounted" -quiet
test -L "$mounted/Applications"
test -s "$mounted/.background/background.png"
mkdir -p "$stage/Applications"
ditto "$mounted/PrasaResearch.app" "$stage/Applications/PrasaResearch.app"
hdiutil detach "$mounted" -quiet; mounted=''
app="$stage/Applications/PrasaResearch.app"
codesign --verify --deep --strict --verbose=2 "$app"
plutil -lint "$app/Contents/Info.plist"
ocr="$app/Contents/MacOS/ocr"
"$ocr/tesseract" --list-langs --tessdata-dir "$ocr/tessdata" | grep -qx eng
export PRASARESEARCH_DATA_DIR="$stage/user-data"
python3 - "$app" "$repo/Releases/macOS/installed-launch-$label.json" <<'PY'
import json, pathlib, subprocess, sys
app, report = map(pathlib.Path, sys.argv[1:])
subprocess.run([str(app / 'Contents/MacOS/PrasaResearch'), '--smoke-output=' + str(report)], check=True, timeout=120)
data = json.loads(report.read_text())
assert data['launched'] is True, data
assert len(data['sections']) >= 6, data
assert data['width'] > 0 and data['height'] > 0, data
print('Installed application launch and navigation passed.')
PY
codesign -dv --verbose=4 "$app" 2>&1 | tee "$repo/Releases/macOS/signature-$label.txt"
shasum -a 256 "$dmg" > "$dmg.sha256"
echo "DMG mount, copy, OCR dependency and installed application launch checks passed: $label"
