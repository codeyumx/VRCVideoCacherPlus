<div align="center">

![Header Banner](https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/4296960/d1bac93e4abb00108cda2137260b76a25bcffea4/header.jpg)


<hr>
</div>

**Language:** **English** | [日本語](./README_ja-JP.md) | [Magyar](./README_hu-HU.md) | [한국어](./README_ko-KR.md)

### Download

- [Windows — VRCVideoCacher.exe](https://github.com/codeyumx/VRCVideoCacherPlus/releases/latest/download/VRCVideoCacher.exe)
- [Linux — VRCVideoCacher](https://github.com/codeyumx/VRCVideoCacherPlus/releases/latest/download/VRCVideoCacher)
Use the original extension from VRCVideoCacher for now:
- [Chrome Extension](https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge)
- [Firefox Extension](https://addons.mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter)

---

### Fork changes (vs [EllyVR/VRCVideoCacher](https://github.com/EllyVR/VRCVideoCacher))

This fork adds settings under **Cache Settings** to avoid using too much bandwidth when VRChat is playing a video.

#### Pause cache downloads while streaming

You can make cache downloads pause automatically when VRChat is playing a streaming video. Set the delay (in seconds) to how long after the stream stops before downloads resume. Set to 0 to disable.

**Tip:** If you watch long videos or looping content, use the speed limit below instead (or alongside this).

#### Cache download speed limit

You can limit how fast cache downloads run (in MB/s). Set to 0 for unlimited.

**Recommended usage:** Set the pause delay to 300 seconds to cover switching videos or queuing songs, and use the speed limit as a backup for longer playback.

#### Download queue & manual downloads

You can manually queue videos for caching from the **Downloads** tab. Paste one or more YouTube URLs (one per line) into the text box and click **Add**. YouTube playlists are also supported — paste the playlist URL and all videos in the playlist will be added to the queue automatically.

#### Other improvements

- Update banner — shows a banner when a new version is available
- Better log entries in the log viewer
- Watch history with stats tracking intelligently saves cache space, keeping your favorite videos
- "Download Now" button on queued items — immediately starts downloading a specific item, skipping the idle-wait delay
- Video titles shown in the download queue

#### Builds
##### Windows
Tested on Windows.
##### Linux
Linux is currently untested, but published in releases if you want to try it out.
##### Steam
Not supported, for now.

---

### Wiki
- [Launch Options](https://github.com/EllyVR/VRCVideoCacher/wiki/Launch-Options)
- [Cli Config Options](https://github.com/EllyVR/VRCVideoCacher/wiki/Config-Options)
- [Linux](https://github.com/EllyVR/VRCVideoCacher/wiki/Linux)

### What is VRCVideoCacher?

VRCVideoCacher is a tool used to cache VRChat videos to your local disk and/or fix YouTube videos from failing to load.

### How does it work?

It replaces VRChats yt-dlp.exe with our own stub yt-dlp, this gets replaced on application startup and is restored on exit.

Auto install missing codecs: [VP9](https://apps.microsoft.com/detail/9n4d0msmp0pt) | [AV1](https://apps.microsoft.com/detail/9mvzqvxjbq9v) | [AC-3](https://apps.microsoft.com/detail/9nvjqjbdkn97)

### Are there any risks involved?

From VRC or EAC? no.

From YouTube/Google? maybe, we strongly recommend you use an alternative Google account if possible.

### How to circumvent YouTube bot detection

In order to fix YouTube videos failing to load, you'll need to install our Chrome extension from [here](https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge) or Firefox from [here](https://addons.mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter), more info [here](https://github.com/clienthax/VRCVideoCacherBrowserExtension). Visit [YouTube.com](https://www.youtube.com) while signed in, at least once while VRCVideoCacher is running, after VRCVideoCacher has obtained your cookies you can safely uninstall the extension, although be aware that if you visit YouTube again with the same browser while the account is still logged in, YouTube will refresh you cookies invalidating the cookies stored in VRCVideoCacher. To circumvent this I recommended deleting your YouTube cookies from your browser after VRCVideoCacher has obtained them, or if you're using your main YouTube account leave the extension installed, or maybe even use an entirely separate web browser from your main one to keep things simple.

### Fix YouTube videos sometimes failing to play

> Loading failed. File not found, codec not supported, video resolution too high or insufficient system resources.

Sync system time, Open Windows Settings -> Time & Language -> Date & Time, under "Additional settings" click "Sync now"

### Uninstalling

- If you have VRCX, delete the startup shortcut "VRCVideoCacher" from `%AppData%\VRCX\startup`
- Delete config and cache from `%AppData%\VRCVideoCacher`
- Delete "yt-dlp.exe" from `%AppData%\..\LocalLow\VRChat\VRChat\Tools` and restart VRChat or rejoin world.
