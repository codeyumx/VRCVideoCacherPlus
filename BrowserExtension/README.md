# VRCVideoCacher Cookies Exporter

Sends your YouTube cookies to VRCVideoCacher (`http://127.0.0.1:9696/api/youtube-cookies`)
in Netscape format so yt-dlp can use them. Pushes automatically every time a
youtube.com tab finishes loading (when the "share automatically" toggle is on), on demand
from the toolbar popup, or when the app itself asks for a refresh.

## Layout

`chrome/` and `firefox/` are each a self-contained, load-unpacked-able extension. Four
files are byte-identical between them:

    cookies.js  i18n.js  popup.js  popup.html

They have to physically exist in both directories, which makes it easy to fix something in
one copy and forget the other. `build.sh --check` fails if they drift, and CI runs it.

Two files are deliberately per-browser:

| File | Chrome | Firefox |
| --- | --- | --- |
| `manifest.json` | MV3 | MV2 |
| `background.js` | service worker, `importScripts("cookies.js")` | background scripts, loaded via the manifest |

### A note on Manifest V2

The Firefox extension is still MV2. Firefox continues to support it, but it is on a
deprecation path, and Firefox's MV3 is not the same shape as Chrome's — it uses event
pages with `background.scripts`, not a service worker — so the two manifests will still
differ after a migration. Worth doing, but it needs testing in an actual Firefox profile.

## Build

```sh
./build.sh          # verify shared files, then package both into dist/
./build.sh --check  # verify only (what CI runs)
```

`dist/` is generated and gitignored. Packaged zips are not committed: the previous ones
had gone stale against the source, and were being bundled *inside* newly built packages.

## Load unpacked (for testing)

**Chrome:** [`chrome://extensions`](chrome://extensions) → Developer mode → Load unpacked → pick `chrome/`.

**Firefox:** [`about:debugging`](about:debugging) → This Firefox → Load Temporary Add-on → pick `firefox/manifest.json`. Temporary add-ons are removed when Firefox closes. For permanent unsigned installation in Firefox Developer Edition / Nightly, set both [`xpinstall.signatures.required`](about:config#xpinstall.signatures.required) and [`extensions.langpacks.signatures.required`](about:config#extensions.langpacks.signatures.required) to `false` in [`about:config`](about:config).
