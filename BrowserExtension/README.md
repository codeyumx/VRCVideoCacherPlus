# VRCVideoCacher Cookies Exporter

Sends your YouTube cookies to VRCVideoCacher (`http://localhost:9696/youtube-cookies`)
in Netscape format so yt-dlp can use them. Pushes automatically every time a
youtube.com tab finishes loading, or on demand by clicking the toolbar icon.

`chrome/` and `firefox/` each hold a self-contained extension (same `background.js`,
browser-specific `manifest.json`).

## Load unpacked (for testing)

**Chrome:** `chrome://extensions` → Developer mode → Load unpacked → pick `chrome/`.

**Firefox:** `about:debugging` → This Firefox → Load Temporary Add-on → pick
`firefox/manifest.json`.

## Package for upload

Chrome Web Store wants a `.zip` with `manifest.json` at the root:

```sh
cd chrome && zip -r ../chrome-extension.zip . && cd ..
```

Firefox (AMO) the same:

```sh
cd firefox && zip -r ../firefox-extension.zip . && cd ..
```
