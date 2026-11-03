## Install

**App:** download `VRCVideoCacherPlus.exe` (Windows) or `VRCVideoCacherPlus` (Linux) below and run it.

**Browser extension:** the original VRCVideoCacher extension from the [Chrome Web Store](https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge) or [Firefox Add-ons](https://addons.mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter) works as before. The VRCVideoCacherPlus extension attached here adds app-triggered cookie refresh and isn't on the stores yet, so install it by hand:

**Chrome, Edge, Brave, Opera (any Chromium browser):**
1. Download `VRCVideoCacherPlus-chrome-*.zip` and unzip it into a folder you'll keep. The browser loads the extension from that folder every time it starts, so don't delete or move it.
2. Open the extensions page: `chrome://extensions` (Edge: `edge://extensions`, Brave: `brave://extensions`, Opera: `opera://extensions`).
3. Turn on **Developer mode** (toggle in the top right; in Edge it's in the left sidebar).
4. Drag the unzipped folder (the one containing `manifest.json`) onto the extensions page and drop it. Or click **Load unpacked** and select that folder.

Drag the **unzipped folder**, not the `.zip`. Chromium browsers refuse zipped or `.crx` extensions that don't come from their web store.

**Firefox:** the `.xpi` isn't signed by Mozilla, so regular Firefox only loads it until you close the browser:
1. Download `VRCVideoCacherPlus-firefox-*.xpi`.
2. Open `about:debugging#/runtime/this-firefox`, click **Load Temporary Add-on…** and select the `.xpi`.
3. Repeat this each time Firefox restarts.

To keep it installed permanently, use Firefox Developer Edition, Nightly, ESR or LibreWolf: set `xpinstall.signatures.required` to `false` in `about:config`, then drag the `.xpi` onto the Firefox window (or `about:addons` → gear icon → **Install Add-on From File…**).

**Updating:** hand-installed extensions don't update themselves. Replace the folder (Chromium) or `.xpi` (Firefox) with the new release and click the reload icon on the extensions page.
