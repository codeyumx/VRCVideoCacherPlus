# VRCVideoCacherPlus

**Language:** [English](./README.md) | [日本語](./README_ja-JP.md) | [Magyar](./README_hu-HU.md) | **한국어** | [Português do Brasil](./README_pt-BR.md)

### 다운로드

- [Windows — VRCVideoCacher.exe](https://github.com/codeyumx/VRCVideoCacherPlus/releases/latest/download/VRCVideoCacher.exe)
- [Linux — VRCVideoCacher](https://github.com/codeyumx/VRCVideoCacherPlus/releases/latest/download/VRCVideoCacher)

**원본 VRCVideoCacher 쿠키 확장 프로그램을 설치하세요**(기본값 — 이것을 사용):
- [Chrome 확장 프로그램](https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge)
- [Firefox 확장 프로그램](https://addons.mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter)

VRCVideoCacherPlus 확장 프로그램은 알파 단계이며 테스트되지 않았습니다 — 테스트 목적이 아니라면 사용하지 마세요. 사용해 보려면 Chrome 또는 Firefox에 압축 해제된 확장 프로그램을 수동으로 설치하세요.

---

VRCVideoCacherPlus는 VRChat 동영상 재생을 도와줍니다. YouTube는 사용자 클라이언트가 쿠키를 제공하지 않으면 동영상을 차단하거나 제한하는 경우가 있습니다. 이 앱은 VRChat을 플레이하는 동안 YouTube에 쿠키를 전달하여 동영상이 고화질로 부드럽게 재생되도록 합니다. 또한 해당 설정을 켜면 동영상을 지능적으로 다운로드하여 자주 재생하는 동영상(예: VRDancing)을 매번 인터넷에서 다시 다운로드할 필요가 없게 합니다. 동영상 캐시를 관리하고, 백그라운드 다운로드 속도를 변경하며, 동영상 다운로드를 지연시킬 수 있습니다(두 개 이상의 동영상 파일을 동시에 다운로드하지 않도록).
VRCVideoCacherPlus는 VRCVideoCacher를 기반으로 하며, HLS 및 스트리밍 재생 목록(.m3u8) 동영상 지원과 같은 여러 개선 사항을 추가했습니다.

#### 스트리밍 중 캐시 다운로드 일시정지

VRChat이 스트리밍 동영상을 재생할 때 캐시 다운로드가 자동으로 일시정지되도록 설정할 수 있습니다. 스트림이 멈춘 후 다운로드가 재개되기까지의 지연 시간(초)을 설정하세요. 0으로 설정하면 비활성화됩니다.

**팁:** 긴 동영상이나 반복 콘텐츠를 시청하는 경우, 아래의 속도 제한을 대신(또는 함께) 사용하세요.

#### 캐시 다운로드 속도 제한

캐시 다운로드 속도(MB/s)를 제한할 수 있습니다. 0으로 설정하면 무제한입니다.

**권장 사용법:** 일시정지 지연을 300초로 설정하여 동영상 전환이나 곡 대기열을 포함하고, 더 긴 재생을 위한 백업으로 속도 제한을 사용하세요.

#### 다운로드 대기열 및 수동 다운로드

**Downloads** 탭에서 동영상을 수동으로 캐시 대기열에 추가할 수 있습니다. 하나 이상의 YouTube URL(한 줄에 하나씩)을 텍스트 상자에 붙여넣고 **Add**를 클릭하세요. YouTube 재생 목록도 지원됩니다 — 재생 목록 URL을 붙여넣으면 그 안의 모든 동영상이 자동으로 대기열에 추가됩니다.

#### HLS / 스트리밍 동영상 재생 목록 캐시

완료된 HLS 스트리밍 재생 목록(`.m3u8` 및 VRDancing의 베타 mpegts 동영상 같은 mpegts 변형)을 이제 나중에 재생할 수 있도록 MP4로 캐시할 수 있습니다. 감지는 콘텐츠 기반이므로 `.m3u8` 확장자 없이 제공되는 재생 목록도 인식됩니다. 라이브 스트림(`#EXT-X-ENDLIST` 없음)은 건너뛰며, 최대 길이 제한은 **Cache Settings**에서 설정할 수 있습니다(0으로 설정하면 무제한).

**클라우드 공유 URL:** `?dl=0`이 붙은 Dropbox 링크(기본 공유 형식)와 Google Drive `/file/d/<id>/view` 링크는 가져오기 전에 자동으로 직접 다운로드 형식으로 변환되므로 어느 형식이든 붙여넣을 수 있습니다. Mega.nz는 지원되지 않습니다(암호화됨, JS 전용). 세그먼트 URL이 다른 보호된 파일을 가리키는 재생 목록은 작동하지 않습니다 — 매니페스트 자체와 그 세그먼트가 모두 직접 가져올 수 있는 호스트에 있어야 합니다.

#### 기타 개선 사항

- 업데이트 배너 — 새 버전이 제공되면 배너를 표시
- 로그 뷰어의 로그 항목 개선
- 통계 추적이 포함된 시청 기록이 캐시 공간을 지능적으로 절약하여 좋아하는 동영상을 유지
- 대기 중인 항목의 "Download Now" 버튼 — 유휴 대기 지연을 건너뛰고 특정 항목을 즉시 다운로드 시작
- 다운로드 대기열에 동영상 제목 표시

#### 빌드
##### Windows
Windows에서 사용자 테스트 완료.
##### Linux
앱 시작 및 기본 기능을 Linux에서 테스트함.
##### Steam 앱 통합
Steam 앱 통합은 아직 지원되지 않습니다. SteamVR 통합은 테스트되었습니다(예: SteamVR로 이 앱 시작).

### 피드백
코드 피드백, 기능 아이디어, 버그는 GitHub 이슈로 게시해 주세요.
일반적인 의견과 피드백은 여기에 남길 수 있습니다: [피드백](https://tally.so/r/kdrM2r)

---

## EllyVR VRCVideoCacher README의 FAQ

### 어떻게 작동하나요?

VRChat의 yt-dlp.exe를 자체 스텁 yt-dlp로 교체합니다. 이는 앱 시작 시 교체되고 종료 시 복원됩니다.

누락된 코덱 자동 설치: [VP9](https://apps.microsoft.com/detail/9n4d0msmp0pt) | [AV1](https://apps.microsoft.com/detail/9mvzqvxjbq9v) | [AC-3](https://apps.microsoft.com/detail/9nvjqjbdkn97)

### 어떤 위험이 있나요?

VRC나 EAC에서? 아니요.

YouTube/Google에서? 그럴 수도 있습니다. 가능하다면 대체 Google 계정을 사용하는 것을 강력히 권장합니다.

### YouTube 봇 감지를 우회하는 방법

YouTube 동영상이 로드되지 않는 문제를 해결하려면 Chrome 또는 Firefox 확장 프로그램을 설치해야 합니다. VRCVideoCacher가 실행되는 동안 로그인한 상태로 YouTube를 최소 한 번 방문하세요. VRCVideoCacher가 쿠키를 획득한 후, 앱이 동영상 재생을 위해 이를 YouTube에 전송합니다.

### YouTube 동영상이 가끔 재생되지 않는 문제 해결

> Loading failed. File not found, codec not supported, video resolution too high or insufficient system resources.

YouTube는 시스템 시간을 확인합니다. 해결 방법: 시스템 시간을 동기화하세요. Windows 설정 -> 시간 및 언어 -> 날짜 및 시간을 열고 "추가 설정" 아래에서 "지금 동기화"를 클릭하세요.

### 제거 방법

**Windows:**
- VRCX를 사용하는 경우 `%AppData%\VRCX\startup`에서 시작 바로 가기 "VRCVideoCacher"를 삭제하세요
- `%AppData%\VRCVideoCacher`에서 설정과 캐시를 삭제하세요
- `%AppData%\..\LocalLow\VRChat\VRChat\Tools`에서 "yt-dlp.exe"를 삭제하세요. VRChat을 다시 시작하세요.

**Linux:**
- `~/.config/VRCVideoCacher`에서 설정과 캐시를 삭제하세요
- VRChat은 Proton에서 실행되므로 Steam compat 프리픽스에서 "yt-dlp.exe"를 삭제하세요: `~/.steam/steam/steamapps/compatdata/438100/pfx/drive_c/users/steamuser/AppData/LocalLow/VRChat/VRChat/Tools`. VRChat을 다시 시작하세요.
