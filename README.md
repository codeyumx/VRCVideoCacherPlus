# VRCVideoCacherPlus

**Language:** **English** | [日本語](./README_ja-JP.md) | [Magyar](./README_hu-HU.md) | [한국어](./README_ko-KR.md) | [Português do Brasil](./README_pt-BR.md)

### Download

- [Windows — VRCVideoCacherPlus.exe](https://github.com/codeyumx/VRCVideoCacherPlus/releases/latest/download/VRCVideoCacherPlus.exe)
- [Linux — VRCVideoCacherPlus](https://github.com/codeyumx/VRCVideoCacherPlus/releases/latest/download/VRCVideoCacherPlus)

**Install the original VRCVideoCacher cookie extension** (default — use these):
- [Chrome Extension](https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge)
- [Firefox Extension](https://addons.mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter)

The VRCVideoCacherPlus extensions ([BrowserExtension/](BrowserExtension/)) are in early testing and should send cookies. You can choose: manual send & copy, automatic sharing, and/or app-triggered cookie refresh. They're not on the extension stores yet, so install them by hand as described below. Hand-installed extensions don't update automatically: after downloading a new version, replace the files and reload the extension yourself.

### Installing the VRCVideoCacherPlus extension

Download the extension from the [latest release](https://github.com/codeyumx/VRCVideoCacherPlus/releases/latest) (under **Assets**): `VRCVideoCacherPlus-chrome-*.zip` for Chrome and other Chromium browsers, `VRCVideoCacherPlus-firefox-*.xpi` for Firefox.

**Chrome, Edge, Brave, Opera (any Chromium browser):**
1. Unzip `VRCVideoCacherPlus-chrome-*.zip` into a folder you'll keep, e.g. `Documents\VRCVideoCacherPlus-extension`. The browser loads the extension from that folder every time it starts, so don't delete or move it.
2. Open the extensions page: `chrome://extensions` (Edge: `edge://extensions`, Brave: `brave://extensions`, Opera: `opera://extensions`).
3. Turn on **Developer mode** (toggle in the top right; in Edge it's in the left sidebar).
4. Drag the unzipped folder (the one containing `manifest.json`) onto the extensions page and drop it. Or click **Load unpacked** and select that folder.
5. Optional: pin the extension from the puzzle-piece menu so its popup is one click away.

Drag the **unzipped folder**, not the `.zip`. Chromium browsers refuse zipped or `.crx` extensions that don't come from their web store.

**Firefox:**

The `.xpi` isn't signed by Mozilla, so regular Firefox only loads it until you close the browser:
1. Open `about:debugging#/runtime/this-firefox`
2. Click **Load Temporary Add-on…** and select the downloaded `.xpi`
3. Repeat this each time Firefox restarts.

To keep it installed permanently, use [Firefox Developer Edition or Nightly](https://www.mozilla.org/firefox/channel/desktop/) (or Firefox ESR / LibreWolf): set `xpinstall.signatures.required` to `false` in `about:config`, then drag the `.xpi` onto the Firefox window (or open `about:addons` → gear icon → **Install Add-on From File…**) and confirm **Add**.

**From a clone of this repo:** load `BrowserExtension/chrome/` the same way as the unzipped folder, or in Firefox pick `BrowserExtension/firefox/manifest.json` under **Load Temporary Add-on…**.

After installing, open YouTube while signed in. The app's **Cookies** panel shows which browser the extension is connected from and which browser the current cookies came from.

---

VRCVideoCacherPlus helps VRChat videos play. Sometimes, YouTube blocks or throttles videos when a user client does not provide cookies. This app gives YouTube the cookies while you play VRChat so that videos play smoothly and in high definition. This app also downloads videos intelligently if you turn that setting on, so that videos you play often (e.g. VRDancing) don't need to download from the internet again. You can manage the video cache, change the background download speed, and delay the video download (so that you're not downloading two video files or more at the same time).
VRCVideoCacherPlus is based on VRCVideoCacher and adds many improvements, like HLS and streaming playlist (.m3u8) video support.

![Main screen](docs/screenshots/main-info.png)
*Main screen — status, cache size, and what the app is doing right now.*

![Download speed and cache delay settings](docs/screenshots/download-settings.png)
*Cache settings — limit background download speed and delay downloads so they don't compete with the video you're watching.*

![Automatic cookie refresh with the beta extension](docs/screenshots/cookie-refresh.png)
*Cookies — the beta browser extension can refresh your YouTube cookies automatically, on request from the app.*

### Features

- Pause cache downloads while streaming
- Cache download speed limit
- Download queue & manual downloads
- Cache HLS / streaming-video playlists
- Other improvements
- Builds

<details>
<summary><b>Details on each feature</b></summary>

#### Pause cache downloads while streaming

You can make cache downloads pause automatically when VRChat is playing a streaming video. Set the delay (in seconds) to how long after the stream stops before downloads resume. Set to 0 to disable.

**Tip:** If you watch long videos or looping content, use the speed limit below instead (or alongside this).

#### Cache download speed limit

You can limit how fast cache downloads run (in MB/s). Set to 0 for unlimited.

**Recommended usage:** Set the pause delay to 300 seconds to cover switching videos or queuing songs, and use the speed limit as a backup for longer playback.

#### Download queue & manual downloads

You can manually queue videos for caching from the **Downloads** tab. Paste one or more YouTube URLs (one per line) into the text box and click **Add**. YouTube playlists are also supported — paste the playlist URL and all videos in the playlist will be added to the queue automatically.

#### Cache HLS / streaming-video playlists

Finished HLS streaming playlists (`.m3u8` and mpegts variants like VRDancing's beta mpegts videos) can now be cached as MP4 for later playback. Detection is content-based, so playlists served without a `.m3u8` extension still get picked up. Live streams (no `#EXT-X-ENDLIST`) are skipped, and a max-length cap is configurable in **Cache Settings** (set to 0 for unlimited).

**Cloud share URLs:** Dropbox links with `?dl=0` (the default share form) and Google Drive `/file/d/<id>/view` links are automatically rewritten to their direct-download form before fetching, so you can paste either form. Mega.nz isn't supported (encrypted, JS-only). Playlists whose segment URLs point to other protected files won't work — the manifest itself plus its segments must be on a directly-fetchable host.

#### Other improvements

- Update banner — shows a banner when a new version is available
- Better log entries in the log viewer
- Watch history with stats tracking intelligently saves cache space, keeping your favorite videos
- "Download Now" button on queued items — immediately starts downloading a specific item, skipping the idle-wait delay
- Video titles shown in the download queue

#### Builds
##### Windows
Fully user tested on Windows.
##### Linux
App start and basic functionality tested on Linux.
##### Steam App integration
Steam app integration isn't supported yet. SteamVR integration is tested (e.g. starting this app with SteamVR)

</details>

### Feedback
For code feedback, feature ideas, and bugs, post a GitHub issue.
You can leave general comments and feedback here: [Feedback](https://tally.so/r/kdrM2r)

---

<details>
<summary><b>FAQ from the EllyVR VRCVideoCacher README</b></summary>

### How does it work?

It replaces VRChat's yt-dlp.exe with our own stub yt-dlp, this gets replaced on application startup and is restored on exit.

Auto install missing codecs: [VP9](https://apps.microsoft.com/detail/9n4d0msmp0pt) | [AV1](https://apps.microsoft.com/detail/9mvzqvxjbq9v) | [AC-3](https://apps.microsoft.com/detail/9nvjqjbdkn97)

### Are there any risks involved?

From VRC or EAC? no.

From YouTube/Google? maybe, we strongly recommend you use an alternative Google account if possible.

### How to circumvent YouTube bot detection

In order to fix YouTube videos failing to load, you'll need to install the Chrome or Firefox extension. Visit YouTube, while signed in, at least once while VRCVideoCacher is running, and after VRCVideoCacher has obtained your cookies, the app will send those to YouTube for playing videos.

### Fix YouTube videos sometimes failing to play

> Loading failed. File not found, codec not supported, video resolution too high or insufficient system resources.

YouTube checks system time. Fix: Sync system time, Open Windows Settings -> Time & Language -> Date & Time, under "Additional settings" click "Sync now"

</details>

---

## How to uninstall

**Windows:**
- If you have VRCX, delete the startup shortcut "VRCVideoCacher" from `%AppData%\VRCX\startup`
- Delete config and cache from `%AppData%\VRCVideoCacher`
- Delete "yt-dlp.exe" from `%AppData%\..\LocalLow\VRChat\VRChat\Tools`. Restart VRChat.

**Linux:**
- Delete config and cache from `~/.config/VRCVideoCacher`
- VRChat runs under Proton, so delete "yt-dlp.exe" from the Steam compat prefix: `~/.steam/steam/steamapps/compatdata/438100/pfx/drive_c/users/steamuser/AppData/LocalLow/VRChat/VRChat/Tools`. Restart VRChat.
