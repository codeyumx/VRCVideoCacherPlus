<div align="center">

![Header Banner](https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/4296960/d1bac93e4abb00108cda2137260b76a25bcffea4/header.jpg)

[![Wiki](https://img.shields.io/badge/Wiki-Info-green)](https://github.com/EllyVR/VRCVideoCacher/wiki)
[![Steam Download](https://img.shields.io/badge/Steam-Download-blue?logo=steam)](https://store.steampowered.com/app/4296960)
[![Github Download](https://img.shields.io/badge/Github-Download-blue?logo=github)](https://github.com/EllyVR/VRCVideoCacher/releases/latest)
[![Discord Server](https://img.shields.io/badge/Discord-Join%20Server-5865F2?logo=discord)](https://discord.gg/t6x6p6Tzs)

<hr>
</div>

**Language:** [English](./README.md) | [日本語](./README_ja-JP.md) | [Magyar](./README_hu-HU.md) | 한국어

### 다운로드

- [Chrome 확장 프로그램](https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge)
- [Firefox 확장 프로그램](https://addons.mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter)

### 포크 변경 사항 ([EllyVR/VRCVideoCacher](https://github.com/EllyVR/VRCVideoCacher) 대비)

이 포크는 VRChat이 동영상을 재생 중일 때 대역폭을 너무 많이 사용하지 않도록 **캐시 설정**에 설정을 추가합니다.

#### 스트리밍 중 캐시 다운로드 일시 중지

VRChat이 스트리밍 동영상을 재생하는 동안 캐시 다운로드를 자동으로 일시 중지할 수 있습니다. 스트림이 멈춘 후 다운로드가 재개될 때까지의 지연 시간(초)을 설정하세요. 0으로 설정하면 비활성화됩니다.

**팁:** 긴 동영상이나 반복 콘텐츠를 시청하는 경우 아래의 속도 제한을 함께 사용하세요.

#### 캐시 다운로드 속도 제한

캐시 다운로드 속도를 제한할 수 있습니다 (MB/s 단위). 0으로 설정하면 무제한입니다.

**권장 설정:** 일시 중지 지연을 300초로 설정하여 동영상 전환이나 노래 대기열에 대응하고, 긴 재생에는 속도 제한을 백업으로 사용하세요.

#### 다운로드 대기열 및 수동 다운로드

**다운로드** 탭에서 수동으로 동영상을 캐시 대기열에 추가할 수 있습니다. 텍스트 상자에 YouTube URL을 한 줄에 하나씩 붙여넣고 **추가**를 클릭하세요. YouTube 재생목록도 지원됩니다 — 재생목록 URL을 붙여넣으면 모든 동영상이 자동으로 대기열에 추가됩니다.

---

### 위키 (영문)
- [실행 옵션](https://github.com/EllyVR/VRCVideoCacher/wiki/Launch-Options)
- [CLI 설정](https://github.com/EllyVR/VRCVideoCacher/wiki/Config-Options)
- [Linux](https://github.com/EllyVR/VRCVideoCacher/wiki/Linux)

### VRCVideoCacher가 무엇인가요?

VRChat에서 동영상을 로컬 디스크에 캐시하거나 YouTube 동영상이 재생되지 않는 문제를 수정하는 데 사용되는 도구입니다.

### 어떻게 작동하나요?

VRChat의 yt-dlp.exe를 자체 stub yt-dlp로 대체하며, 애플리케이션 시작 시 대체되고 종료 시 복원됩니다.

자동으로 없는 코덱을 설치합니다: [VP9](https://apps.microsoft.com/detail/9n4d0msmp0pt) | [AV1](https://apps.microsoft.com/detail/9mvzqvxjbq9v) | [AC-3](https://apps.microsoft.com/detail/9nvjqjbdkn97)

### 어떠한 위험이 있나요?

VRChat 또는 EAC에서요? 아니요.

YouTube나 Google에서요? 아마도요, 가능하다면 Google 부계정을 사용하는 것을 강력히 권장합니다.

### YouTube 봇 감지 회피하기

YouTube 동영상이 로드되지 않는 문제를 해결하려면 [여기](https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge)에서 Chrome 확장 프로그램을 설치하거나 [여기](https://addons.mozilla.org/ko-KR/firefox/addon/vrcvideocachercookiesexporter)에서 Firefox 확장 프로그램을 설치해야 합니다. 자세한 내용은 [여기](https://github.com/clienthax/VRCVideoCacherBrowserExtension)를 참고하세요. VRCVideoCacher가 실행 중인 상태로 최소 한 번은 로그인된 상태로 [YouTube.com](https://www.youtube.com)을 방문하세요. VRCVideoCacher가 쿠키를 가져온 이후 확장 프로그램을 제거할 수 있지만, 계정이 로그인된 상태로 동일한 브라우저에서 YouTube를 다시 방문하면 YouTube가 쿠키를 새로 고쳐 VRCVideoCacher에 저장된 쿠키가 무효화된다는 점에 유의하세요. 이를 방지하려면 VRCVideoCacher가 쿠키를 가져온 후 브라우저에서 YouTube 쿠키를 삭제하거나, 메인 YouTube 계정을 사용하는 경우 확장 프로그램을 설치된 상태로 유지하거나 메인 브라우저와 별도의 웹 브라우저를 사용하는 것을 권장합니다.

### YouTube 동영상이 가끔 재생되지 않는 문제 수정하기

> Loading failed. File not found, codec not supported, video resolution too high or insufficient system resources.

시스템 시간을 동기화하세요. Windows 설정 -> 시간 및 언어 -> 날짜 및 시간으로 이동한 후 "추가 설정"에서 "지금 동기화"를 클릭하세요.

### 제거하기

- VRCX가 있고 자동 실행을 사용 중이라면, `%AppData%\VRCX\startup`에서 "VRCVideoCacher" 바로가기를 삭제하세요.
- `%AppData%\VRCVideoCacher`에서 설정과 캐시를 삭제하세요.
- `%AppData%\..\LocalLow\VRChat\VRChat\Tools`의 "yt-dlp.exe"를 삭제하고 VRChat을 재시작하거나 월드에 재입장하세요.
