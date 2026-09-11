#!/usr/bin/env bash
# Packages the browser extensions, and verifies the files shared between them have not
# drifted apart.
#
#   ./build.sh          check shared files, then build both zips into dist/
#   ./build.sh --check  check only, no packaging (this is what CI runs)
#
# chrome/ and firefox/ are separate load-unpacked targets, so the shared files have to
# physically exist in both. That makes silent divergence easy — a fix applied to one copy
# and not the other — which is what the check below exists to catch.
set -euo pipefail

cd "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Identical in both. background.js and manifest.json are intentionally per-browser:
# Chrome is MV3 with a service worker, Firefox is MV2 with background scripts.
SHARED_FILES=(cookies.js i18n.js popup.js popup.html)

CHECK_ONLY=false
[ "${1:-}" = "--check" ] && CHECK_ONLY=true

echo "=== Checking shared files ==="
drift=0
for file in "${SHARED_FILES[@]}"; do
    if ! diff -q "chrome/${file}" "firefox/${file}" >/dev/null 2>&1; then
        echo "DRIFT: chrome/${file} and firefox/${file} differ" >&2
        diff -u "chrome/${file}" "firefox/${file}" >&2 || true
        drift=1
    fi
done

if [ "$drift" -ne 0 ]; then
    echo "Shared extension files have diverged. Reconcile them before packaging." >&2
    exit 1
fi
echo "All ${#SHARED_FILES[@]} shared files match."

# Manifests must not disagree about the version.
chrome_version=$(grep -oE '"version"[[:space:]]*:[[:space:]]*"[^"]+"' chrome/manifest.json | head -1 | grep -oE '[0-9][^"]*')
firefox_version=$(grep -oE '"version"[[:space:]]*:[[:space:]]*"[^"]+"' firefox/manifest.json | head -1 | grep -oE '[0-9][^"]*')
if [ "$chrome_version" != "$firefox_version" ]; then
    echo "Manifest version mismatch: chrome=${chrome_version} firefox=${firefox_version}" >&2
    exit 1
fi
echo "Manifest version: ${chrome_version}"

if [ "$CHECK_ONLY" = true ]; then
    exit 0
fi

command -v zip >/dev/null || { echo "zip is not installed" >&2; exit 1; }

# Shared with the application's release artifacts, so only this script's own outputs may
# be cleared here. `rm -rf "$DIST_DIR"` would delete the VRCVideoCacher-*.zip files that
# ../build.sh --artifacts puts in the same directory, and whichever ran second would win.
DIST_DIR="$(pwd)/../dist"
echo "=== Packaging ==="
mkdir -p "$DIST_DIR"
rm -f "${DIST_DIR}"/VRCVideoCacherPlusPlus-*.zip \
      "${DIST_DIR}"/VRCVideoCacherPlusPlus-*.xpi \
      "${DIST_DIR}"/VRCVideoCacherPlusPlus-*.crx
for browser in chrome firefox; do
    out="${DIST_DIR}/VRCVideoCacherPlusPlus-${browser}-${chrome_version}.zip"
    (cd "$browser" && zip -qr "$out" . -x '.*')
    echo "  dist/$(basename "$out")"
done

# Copy Firefox zip to xpi
cp "${DIST_DIR}/VRCVideoCacherPlusPlus-firefox-${chrome_version}.zip" "${DIST_DIR}/VRCVideoCacherPlusPlus-firefox-${chrome_version}.xpi"
echo "  dist/VRCVideoCacherPlusPlus-firefox-${chrome_version}.xpi"

# Build Chrome CRX
if command -v npx >/dev/null; then
    npx -y crx3 chrome -p chrome.pem -o "${DIST_DIR}/VRCVideoCacherPlusPlus-chrome-${chrome_version}.crx"
    echo "  dist/VRCVideoCacherPlusPlus-chrome-${chrome_version}.crx"
else
    echo "WARNING: npx not found, skipping CRX packaging" >&2
fi

# The .zip files stay in dist/ as byproducts — .xpi is a copy of the Firefox zip and the
# .crx is built from chrome/ — but ../build.sh --release attaches only the four real
# assets, so a release does not list the same extension twice in two formats.
echo "=== Done ==="
