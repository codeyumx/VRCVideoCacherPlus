# Changelog

VRCVideoCacherPlus is codeyumx's fork of [EllyVR/VRCVideoCacher](https://github.com/EllyVR/VRCVideoCacher).
It caches the videos VRChat plays so they can be replayed from disk instead of re-downloaded.

Releases are bare version tags — `2026.8.14` — that match `<Version>` in
`VRCVideoCacher/VRCVideoCacher.csproj`, and each one attaches `VRCVideoCacherPlus.exe` (win-x64)
and `VRCVideoCacherPlus` (linux-x64); a tag carrying a suffix, `2026.9.1-rc1`, is a pre-release.
A GitHub Actions workflow (`.github/workflows/release.yml`) publishes them: pushing a tag builds
both binaries and the signed browser extension and publishes the release. Newest release first; every entry
names the commits it came from so the history stays checkable.

Everything up to and including `2026.5.2` is upstream work the fork merged in, listed at the
bottom as tags only. The fork's own releases start at `2026.6.17`.

## Releasing

1. Move what matters out of `Unreleased` into a new section for the version being cut, with
   today's date: `## [2026.9.1] — 2026-09-01`.
2. Bump `<Version>` in `VRCVideoCacher/VRCVideoCacher.csproj` to match, commit, and push a tag
   with that name (`2026.9.1`, or `2026.9.1-rc1` for a pre-release).
3. Push the tag. The release workflow checks that this file has a section for it, builds both
   binaries, and publishes the release with that section as its notes — `scripts/release-notes.sh`
   is what reads the section, and it refuses a tag that has none.

---

## Unreleased

## [2026.9.11] — 2026-09-11

Everything from [PR #1](https://github.com/codeyumx/VRCVideoCacherPlus/pull/1)
by [@Bluscream](https://github.com/Bluscream), curated down from its 138 commits, plus fixes on
top. Published as a pre-release first because of the size of the change.

### Added

- **Regex URI rules engine.** `Cache`, `Resolve`, `Redirect`, `Rewrite`, `Block` and `Direct`
  actions with capture-group substitution, a Rules tab with a live Test URL matcher, priority
  reordering, and defaults for Dropbox/Google Drive share links and several CDN bypasses.
- **Rules enforced wherever a URL enters**: the HTTP handler, History's Save-to-cache, the
  manual queue and the start-up pre-cache.
- **Site integrations** refactored into `Integrations/**` with a real base class, plus a fix
  for PyPyDance downloads all sharing one cache file.
- **Video Players toggle** that blocks in-game requests, with connection severing
  (`ss -t -K` and procfs on Linux, `SetTcpEntry` via Vanara on Windows, polkit/UAC elevation
  or a clear "needs root" message).
- **VRChat log monitor, Now Playing card and Active Connections grid.**
- Smaller UI: yt-dlp/Deno/FFmpeg tools card, transfer rate and time remaining in the download
  queue, Open File / Copy File Path in the cache browser, "Show" to open the settings folder.

### Changed

- Release assets are named `VRCVideoCacherPlus.exe` / `VRCVideoCacherPlus`; the updater, README
  links and extension packages follow. The SteamVR app key is now
  `com.github.codeyumx.vrcvideocacherplus`, and the PlusPlus fork's key is cleared as legacy.
- Builds up to 2026.8.14 kept the download rate limit, idle timeout and VP9 preference in
  `PlusConfig.json`. They are now read from it once into `Config.json`, and the file is renamed
  to `PlusConfig.json.bak`.

### Deprecated

- The `VRCVideoCacher.exe` / `VRCVideoCacher` release assets are kept only so installs from before
  the rename can still self-update. Remove them from `release.yml` once their download count
  stops growing.

### Fixed

- **Manual Download no longer freezes the UI when a playlist is queued.** Every queued video
  fired a queue-changed event and each event rebuilt the whole list with one SQLite title
  query per row — O(N²) queries for an N-video playlist. Titles are now looked up in one
  batched query, event bursts coalesce into a single refresh, and the enqueue loop yields
  every 25 items. The same commit fixed a yt-dlp pipe deadlock in `RunYtdlpAsync` and
  `GetPlaylistVideoInfos`, which read stdout to EOF before touching stderr. (`c0f054e`)
- Hardening: zip-slip and manifest validation, GitHub digest checks for downloaded tools,
  `yt-dlp` arguments passed via `ArgumentList` instead of a command line, CORS restricted to
  the origins that need it, link-local/SSRF rejection, atomic config writes, cache eviction
  fixes, database race fixes, and a schema reconciler for columns added by a new build.

### Internal

- `BrowserExtension/chrome.pem` is ignored, so a signing key cannot be committed again; the
  extension is signed from the `CHROME_EXTENSION_PEM` repository secret. (`46a22b0`)

## [2026.8.14] — 2026-08-14

Fixes a startup crash introduced in 2026.8.13: when the cache held more than one format of the
same video, the Stats view threw while the main window was being constructed and the app never
opened. 2026.8.13 was removed rather than patched.

### Added

- Stats view: cache hit rate, bandwidth saved, most replayed videos. (`2e76075`)
- Pre-cache a list of video URLs at start-up, including multi-URL entries. (`37ed9aa`,
  `180d86e`)
- YouTube login cookie expiry shown in the Cookies panel. (`f6d51e1`)
- A test project covering cookie expiry parsing, cache stats maths and the BulkPreCache
  manifest loop. (`db94e14`, `01cd99d`)

### Fixed

- Startup crash when one video has several cached formats. (`dea17ad`)
- BulkPreCache aborted every remaining manifest when one URL failed. (`aee3dfa`)

### Documentation

- README: screenshots added, Features and FAQ collapsed, uninstall instructions moved out of
  the upstream FAQ section. (`d2bca58`, `d4549f9`, `0359e8a`, `102ca0f`)

## [2026.7.16] — 2026-07-16

Same commit as `2026.6.17`, re-tagged. (`c28b5f5`)

## [2026.6.17] — 2026-07-16

The fork's first release after the PlusPlus history restart. (`c28b5f5`)

### Added

- **Disable Error Popups** setting, wired through the log service and the settings view, with
  translations for all shipped locales. (`4316c6c`, `0d088c9`, `17661d5`, `73a9f6e`,
  `91bc82a`, `fe9d4ed`, `d00e923`, `d6202b9`, `973382b`, `8f08c38`, `8e34ba0`)
- Italian locale. (`eab9c5b`, `08ed8f2`)
- Beta extension cookie panel; the remote VVC config it used is gone. (`67639a6`)
- Selectable text in the Log Viewer, so errors can be copied. (`f7d9734`)
- Start-up check that every helper utility is present and working; Deno is validated by
  asking it `--version`. (`3c8cb4f`, `b555649`)
- In-flight YouTube/PyPyDance/VRDancing downloads are quarantined to their own temporary
  subdirectory while they download. (`4e3e34a`, `442ff2d`)

### Fixed

- Backend errors said nothing useful; they now name what failed. (`aa86a48`)
- Message of the day fetch. (`5386c6c`)
- AVPro requests fall back to 360p instead of failing. (`0b22213`)
- Patching failures log a warning rather than an error. (`04cd363`, `08ed8f2`)
- VVC config service error handling. (`d91e1b7`)
- VRDancing download logs aligned with the other two download paths. (`f34229f`)
- The admin warning was removed: Steam sometimes launches the app as admin, and the warning
  was noise. (`3fc4414`, `08ed8f2`)

### Changed

- Handle YouTube live URLs by playing them without caching. (`2efea00`)

### Internal

- `Initial commit (VRCVideoCacherPlus)` — the fork's history restarted here. (`ce78d78`)
- Version bumps and locale clean-up. (`b5128f0`, `2c3b164`, `96e0c82`, `6fa9e12`)

## Earlier releases

Merged in from upstream; these tags are the upstream project's releases, not this fork's.

| Tag | Date |
|---|---|
| `2026.7.2` | 2026-07-08 |
| `2026.7.1` | 2026-07-07 |
| `2026.5.2` | 2026-05-25 |
| `2026.4.3` | 2026-04-13 |
| `2026.4.2` | 2026-04-13 |
| `2026.4.1` | 2026-04-04 |
| `2026.3.17` | 2026-03-29 |
| `2026.3.16` | 2026-03-29 |
| `2026.3.15` | 2026-03-29 |
| `2026.3.14` | 2026-03-18 |
| `2026.3.13` | 2026-03-14 |
| `2026.3.12` | 2026-03-12 |
| `2026.3.11` | 2026-03-09 |
| `2026.3.10` | 2026-03-08 |
| `2026.3.9` | 2026-03-07 |
| `2026.3.8` | 2026-03-07 |
| `2026.3.7.1` | 2026-03-07 |
| `2026.3.7-1` | 2026-03-07 |
| `2026.3.7` | 2026-03-07 |
| `2026.1.9` | 2026-01-09 |
| `2026.1.4` | 2026-01-04 |
| `2026.04.01` | 2026-01-04 |
| `2025.11.24` | 2025-11-24 |
| `2025.11.8-ResoDev` | 2025-11-08 |
| `2025.11.5` | 2025-11-05 |
| `2025.10.3` | 2025-10-03 |
| `2025.9.29` | 2025-09-29 |
| `2025.8.6` | 2025-08-06 |
| `2025.7.16` | 2025-07-16 |
| `2025.7.14` | 2025-07-14 |
| `2025.5.18` | 2025-05-18 |
| `2025.5.14` | 2025-05-14 |
| `2025.5.12` | 2025-05-12 |
| `2025.5.9` | 2025-05-09 |
| `2025.5.7` | 2025-05-07 |
| `2025.4.21` | 2025-04-22 |
| `2025.1.8` | 2025-01-08 |
| `2024.12.9` | 2024-12-09 |
| `2024.11.27` | 2024-12-03 |

---

Published releases: <https://github.com/codeyumx/VRCVideoCacherPlus/releases> ·
Upstream: <https://github.com/EllyVR/VRCVideoCacher>
