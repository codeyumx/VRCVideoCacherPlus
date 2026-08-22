# VRCVideoCacherPlusPlus

Caches VRChat videos locally so they play instantly, and gives you control over where
each URL is allowed to go.

A fork of [VRCVideoCacherPlus](https://github.com/codeyumx/VRCVideoCacherPlus), itself a
fork of [EllyVR/VRCVideoCacher](https://github.com/EllyVR/VRCVideoCacher).

**Language:** **English** | [日本語](./README_ja-JP.md) | [Magyar](./README_hu-HU.md) | [한국어](./README_ko-KR.md) | [Português do Brasil](./README_pt-BR.md)

> The translated READMEs are inherited from upstream and describe the original
> VRCVideoCacher. They have not been updated for this fork.

![Dashboard](https://raw.githubusercontent.com/Bluscream/VRCVideoCacherPlusPlus/assets/Dashboard%20Tab.png)

## Download

| | |
| --- | --- |
| Windows | [VRCVideoCacher-win-x64.zip](https://github.com/Bluscream/VRCVideoCacherPlusPlus/releases/latest/download/VRCVideoCacher-win-x64.zip) |
| Linux | [VRCVideoCacher-linux-x64.zip](https://github.com/Bluscream/VRCVideoCacherPlusPlus/releases/latest/download/VRCVideoCacher-linux-x64.zip) |

Unzip anywhere and run it. It has no installer and writes only to its config directory.

### Cookie extension

YouTube blocks requests it thinks come from a bot. The extension hands your existing
YouTube session to the app so playback keeps working. **Install the original
VRCVideoCacher extension** — it is signed and published, and it is what most people want:

- [Chrome Web Store](https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge)
- [Firefox Add-ons](https://addons.mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter)

This repo also ships its own extension, which adds automatic sharing and lets the app
ask for a cookie refresh on demand. It is **not** published to either store, so it has
to be side-loaded — every release includes a `.crx` and a `.xpi`.

<details>
<summary>Side-loading the PlusPlus extension</summary>

**Chrome / Edge / Brave** — open [`chrome://extensions`](chrome://extensions), turn on
**Developer mode**, then drag the `.crx` onto the page. Loading `BrowserExtension/chrome/`
with **Load unpacked** works too if you have the repo cloned.

**Firefox** — release Firefox refuses unsigned add-ons, and the `.xpi` is unsigned.
Either load it temporarily (`about:debugging#/runtime/this-firefox` → **Load Temporary
Add-on…** → pick `BrowserExtension/firefox/manifest.json`, which is dropped when Firefox
closes), or use [Developer Edition or Nightly](https://www.mozilla.org/firefox/channel/desktop/)
with `xpinstall.signatures.required` set to `false` in `about:config` and install the
`.xpi` from `about:addons`.

</details>

## What this fork adds

**Regex URI rules.** Every video URL is matched against an ordered rule list, and the
first match decides what happens: `Cache`, `Direct`, `Redirect`, `Rewrite` or `Block`.
Patterns are real regex with capture substitution (`$1`, `$2`) and tokens like
`{url.domain}` and `{url.path}`. The Rules tab has a live matcher that shows which rule
a URL hits and what it becomes, before you commit anything.

![Rules](https://raw.githubusercontent.com/Bluscream/VRCVideoCacherPlusPlus/assets/Rules%20Tab.png)

![Editing a rule](https://raw.githubusercontent.com/Bluscream/VRCVideoCacherPlusPlus/assets/Edit%20Rule%20Window.png)

**Now Playing** reads VRChat's log live and shows what is actually playing — title,
artist, progress — with per-stream controls.

**Active Connections** lists the sockets carrying video right now and can cut a single
stream or all of them. See [the note on privileges](#why-cant-it-always-stop-a-video-that-is-already-playing).

**Disable Videoplayers** is a single dashboard toggle that blocks all video requests
immediately and closes what it can, for when a world starts playing something you would
rather it did not.

**Cache browser and history** show what is stored and what you have watched, with stats
used to decide what to evict when the cache fills — so videos you actually rewatch stay.

![Cache browser](https://raw.githubusercontent.com/Bluscream/VRCVideoCacherPlusPlus/assets/Cache%20Browser.png)

**Cloud share links** are rewritten to their direct-download form automatically: Dropbox
`?dl=0` links and Google Drive `/file/d/<id>/view` links both work pasted as-is. Mega.nz
does not work and cannot — it is encrypted and JavaScript-only.

**Smaller things:** download ETA and speed on queued items, a "Download Now" button that
skips the idle delay, video titles in the queue, a tools card tracking `yt-dlp` / `Deno` /
`FFmpeg` versions, an update banner, and a dismissable message-of-the-day.

![Settings](https://raw.githubusercontent.com/Bluscream/VRCVideoCacherPlusPlus/assets/Settings%20Tab.png)

<details>
<summary>Features inherited from VRCVideoCacherPlus</summary>

**Pause cache downloads while streaming** — cache downloads stop while VRChat is
actively requesting streams and resume after a configurable idle period. 0 disables it.

**Cache download speed limit** — cap caching in MB/s so it does not eat your bandwidth.
0 is unlimited.

**Manual downloads** — paste YouTube URLs (one per line) into the Downloads tab. Playlist
URLs expand to every video in the playlist.

**HLS caching** — finished `.m3u8` and mpegts playlists are re-muxed to MP4 for later
playback. Detection is content-based, so playlists served without a `.m3u8` extension are
still picked up. Live streams (no `#EXT-X-ENDLIST`) are skipped, and there is a
configurable max length.

</details>

## Platform support

Windows is the tested platform. Linux works — it is what this fork is developed on — but
Resonite patching is Windows-only, and VRChat itself runs under Proton, which changes
where its files live (see [uninstalling](#uninstalling)). SteamVR auto-start is tested on
both.

## FAQ

### How does it work?

It replaces VRChat's `yt-dlp.exe` with a stub that asks this application to resolve the
URL instead. The swap happens at startup and is undone on exit.

On Windows you may also need codecs:
[VP9](https://apps.microsoft.com/detail/9n4d0msmp0pt) ·
[AV1](https://apps.microsoft.com/detail/9mvzqvxjbq9v) ·
[AC-3](https://apps.microsoft.com/detail/9nvjqjbdkn97)

### Are there any risks?

From VRChat or EAC — no. From YouTube — possibly; use an alternative Google account for
the cookie extension if you can.

### Why can't it always stop a video that is already playing?

Closing a connection that belongs to *another* program is a privileged operation, and
VRChat streaming straight from a CDN is exactly that case. Videos served from this app's
own cache are ours to close and stop instantly; direct streams need permission from the
operating system.

Without that permission, blocking still works for everything new — the video already
midway through simply plays to the end. The app tells you which of these happened instead
of claiming success either way.

**Linux** — closing someone else's socket needs the `CAP_NET_ADMIN` capability. Grant it
once and severing works silently from then on, with no password prompt ever:

```bash
sudo setcap cap_net_admin+ep /path/to/VRCVideoCacher
```

Capabilities live on the file, so **this must be redone after every update**, because the
updater replaces the binary. If you would rather not grant it, the `Sever Connection`
buttons ask for authorisation through your desktop's normal dialog each time (polkit —
KDE, GNOME and the rest all work).

**Windows** — run as administrator, or accept the UAC prompt when you press
`Sever Connection`. IPv6 connections cannot be closed on Windows at all; it has never
provided an API for it at any privilege level, so those are reported as unsupported
rather than as a permissions problem.

The automatic **Disable Videoplayers** toggle never prompts for anything. It blocks new
requests and closes what it can, silently.

### Where are the settings stored?

`Config.json`, in the same folder the original VRCVideoCacher uses —
`%AppData%\VRCVideoCacher` on Windows, `~/.config/VRCVideoCacher` on Linux. There is one
config file, shared with the original; PlusPlus settings including your rules sit
alongside the normal ones at the top level.

> **Running the original VRCVideoCacher again rewrites `Config.json` and drops every
> setting it does not recognise**, taking your rules back to defaults. The app warns you
> about this once, on first run.

Coming from an older PlusPlus build, your previous `PlusConfig.json` is left alone and no
longer read. Nothing is migrated automatically — copy anything you want to keep into
`Config.json` yourself.

### What does it connect to?

Beyond the video URL a world asks for:

| Host | Why | When |
| --- | --- | --- |
| `api.github.com`, `objects.githubusercontent.com` | Update checks and downloads for yt-dlp, Deno, FFmpeg and the app | Startup, then hourly for yt-dlp |
| `dl.deno.land` | Fallback Deno download if GitHub fails | Only on failure |
| `vvc.ellyvr.dev` | Message-of-the-day, from the upstream VRCVideoCacher API | Startup |
| `api.pypy.dance`, `dbapi.vrdancing.club`, `docs.google.com` | Track titles and thumbnails for PyPyDance / VRDancing | When such a video plays |
| `www.youtube.com`, `img.youtube.com` | Titles, thumbnails, and validating your saved cookies | When a YouTube video plays |

Two inherited default rules send a request somewhere you might not expect, because the
URL is rewritten before it is resolved:

- **niconico links go to `nicovideo.life`**, an unofficial third-party mirror with no
  affiliation to this project or to niconico. Playing one tells that mirror what you are
  watching.
- **`dmn.moe` links** are rewritten from `/sr/` to `/yt/` and resolved through that site.

Both come from upstream. Delete or edit the rules in the Rules tab if you would rather
not use them.

### YouTube videos fail to play

Install the cookie extension, then visit YouTube while signed in at least once with this
app running. Once it has your cookies it uses them to resolve videos.

If you see *"Loading failed. File not found, codec not supported, video resolution too
high or insufficient system resources"*, check your system clock — YouTube rejects
requests with a skewed time. On Windows: **Settings → Time & Language → Date & Time →
Sync now**.

## Uninstalling

**Windows**
- If you use VRCX, delete the `VRCVideoCacher` shortcut from `%AppData%\VRCX\startup`
- Delete `%AppData%\VRCVideoCacher` (config and cache)
- Delete `yt-dlp.exe` from `%AppData%\..\LocalLow\VRChat\VRChat\Tools`, then restart VRChat

**Linux**
- Delete `~/.config/VRCVideoCacher` (config and cache)
- VRChat runs under Proton, so the stub lives in the Steam prefix — delete `yt-dlp.exe`
  from `~/.steam/steam/steamapps/compatdata/438100/pfx/drive_c/users/steamuser/AppData/LocalLow/VRChat/VRChat/Tools`,
  then restart VRChat

## Building

See [AGENTS.md](AGENTS.md) for the full picture. In short:

```bash
./build.sh --lint       # locales, extension, strict compile, tests
./build.sh --artifacts  # the four release assets into dist/
./build.sh --help       # everything else
```

## Feedback

Bugs and feature ideas: [GitHub issues](https://github.com/Bluscream/VRCVideoCacherPlusPlus/issues).
General comments: [feedback form](https://tally.so/r/kdrM2r).

Screenshots and other binary assets live on the [`assets`](https://github.com/Bluscream/VRCVideoCacherPlusPlus/tree/assets)
branch, kept out of the code history.
