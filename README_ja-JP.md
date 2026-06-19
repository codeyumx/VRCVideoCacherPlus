# VRCVideoCacherPlus

**Language:** [English](./README.md) | **日本語** | [Magyar](./README_hu-HU.md) | [한국어](./README_ko-KR.md) | [Português do Brasil](./README_pt-BR.md)

### ダウンロード

- [Windows — VRCVideoCacher.exe](https://github.com/codeyumx/VRCVideoCacherPlus/releases/latest/download/VRCVideoCacher.exe)
- [Linux — VRCVideoCacher](https://github.com/codeyumx/VRCVideoCacherPlus/releases/latest/download/VRCVideoCacher)

**VRCVideoCacher 本家のクッキー拡張機能をインストールしてください**（デフォルト — こちらを使用）:
- [Chrome 拡張機能](https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge)
- [Firefox 拡張機能](https://addons.mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter)

VRCVideoCacherPlus の拡張機能はアルファ版で未テストです。テスト目的でなければ使用しないでください。試す場合は、Chrome または Firefox に展開済み拡張機能を手動でインストールしてください。

---

VRCVideoCacherPlus は VRChat の動画再生をサポートします。YouTube は、ユーザーのクライアントがクッキーを提供しない場合、動画をブロックまたは制限することがあります。このアプリは VRChat をプレイ中に YouTube へクッキーを渡すことで、動画が高画質でスムーズに再生されるようにします。また、設定をオンにすれば動画をインテリジェントにダウンロードし、よく再生する動画（例: VRDancing）を毎回インターネットからダウンロードし直す必要がなくなります。動画キャッシュの管理、バックグラウンドのダウンロード速度の変更、動画ダウンロードの遅延（複数の動画ファイルを同時にダウンロードしないようにする）が可能です。
VRCVideoCacherPlus は VRCVideoCacher をベースにしており、HLS やストリーミングプレイリスト（.m3u8）動画のサポートなど、多くの改善が加えられています。

#### ストリーミング中のキャッシュダウンロードを一時停止

VRChat がストリーミング動画を再生しているときに、キャッシュのダウンロードを自動的に一時停止できます。ストリームが停止してからダウンロードを再開するまでの遅延（秒）を設定してください。0 に設定すると無効になります。

**ヒント:** 長い動画やループするコンテンツを視聴する場合は、代わりに（または併用して）下記の速度制限を使用してください。

#### キャッシュダウンロードの速度制限

キャッシュダウンロードの速度（MB/s）を制限できます。0 に設定すると無制限です。

**推奨設定:** 一時停止の遅延を 300 秒に設定して動画の切り替えや曲のキューイングをカバーし、長時間の再生のバックアップとして速度制限を使用してください。

#### ダウンロードキューと手動ダウンロード

**Downloads** タブから動画を手動でキャッシュのためにキューに追加できます。1 つ以上の YouTube URL（1 行に 1 つ）をテキストボックスに貼り付けて **Add** をクリックします。YouTube のプレイリストにも対応しています。プレイリストの URL を貼り付けると、その中のすべての動画が自動的にキューに追加されます。

#### HLS / ストリーミング動画のプレイリストをキャッシュ

完了した HLS ストリーミングプレイリスト（`.m3u8` および VRDancing のベータ版 mpegts 動画のような mpegts 系統）を、後で再生するために MP4 としてキャッシュできるようになりました。検出はコンテンツベースなので、`.m3u8` 拡張子なしで配信されるプレイリストも認識されます。ライブストリーム（`#EXT-X-ENDLIST` がないもの）はスキップされ、最大長の上限は **Cache Settings** で設定できます（0 に設定すると無制限）。

**クラウド共有 URL:** `?dl=0` 付きの Dropbox リンク（デフォルトの共有形式）と Google Drive の `/file/d/<id>/view` リンクは、取得前に自動的に直接ダウンロード形式に書き換えられるため、どちらの形式でも貼り付けられます。Mega.nz は非対応です（暗号化されており JS のみ）。セグメント URL が他の保護されたファイルを指すプレイリストは機能しません。マニフェスト自体とそのセグメントは、直接取得可能なホスト上にある必要があります。

#### その他の改善

- アップデートバナー — 新しいバージョンが利用可能になるとバナーを表示
- ログビューアのログエントリの改善
- 統計トラッキング付きの視聴履歴がキャッシュ容量を賢く節約し、お気に入りの動画を保持
- キューアイテムの「Download Now」ボタン — アイドル待機の遅延をスキップして特定のアイテムを即座にダウンロード開始
- ダウンロードキューに動画タイトルを表示

#### ビルド
##### Windows
Windows で十分にユーザーテスト済み。
##### Linux
アプリの起動と基本的な機能を Linux でテスト済み。
##### Steam アプリ統合
Steam アプリの統合はまだ未対応です。SteamVR 統合はテスト済みです（例: SteamVR でこのアプリを起動）。

### フィードバック
コードへのフィードバック、機能のアイデア、バグは GitHub の issue に投稿してください。
一般的なコメントやフィードバックはこちらに残せます: [フィードバック](https://tally.so/r/kdrM2r)

---

## EllyVR VRCVideoCacher README からの FAQ

### どのように動作しますか？

VRChat の yt-dlp.exe を独自のスタブ yt-dlp に置き換えます。これはアプリ起動時に置き換えられ、終了時に復元されます。

不足しているコーデックの自動インストール: [VP9](https://apps.microsoft.com/detail/9n4d0msmp0pt) | [AV1](https://apps.microsoft.com/detail/9mvzqvxjbq9v) | [AC-3](https://apps.microsoft.com/detail/9nvjqjbdkn97)

### 何かリスクはありますか？

VRC や EAC から？ いいえ。

YouTube/Google から？ あるかもしれません。可能であれば別の Google アカウントの使用を強くおすすめします。

### YouTube のボット検出を回避する方法

YouTube 動画の読み込み失敗を修正するには、Chrome または Firefox の拡張機能をインストールする必要があります。VRCVideoCacher を実行している間に、サインインした状態で少なくとも一度 YouTube にアクセスしてください。VRCVideoCacher がクッキーを取得した後、アプリはそれらを YouTube に送信して動画を再生します。

### YouTube 動画がときどき再生できない問題の修正

> Loading failed. File not found, codec not supported, video resolution too high or insufficient system resources.

YouTube はシステム時刻をチェックします。修正方法: システム時刻を同期します。Windows の設定 -> 時刻と言語 -> 日付と時刻 を開き、「追加の設定」の下にある「今すぐ同期」をクリックします。

### アンインストール方法

**Windows:**
- VRCX を使用している場合は、`%AppData%\VRCX\startup` からスタートアップショートカット「VRCVideoCacher」を削除します
- `%AppData%\VRCVideoCacher` から設定とキャッシュを削除します
- `%AppData%\..\LocalLow\VRChat\VRChat\Tools` から「yt-dlp.exe」を削除します。VRChat を再起動します。

**Linux:**
- `~/.config/VRCVideoCacher` から設定とキャッシュを削除します
- VRChat は Proton 上で動作するため、Steam の compat プレフィックスから「yt-dlp.exe」を削除します: `~/.steam/steam/steamapps/compatdata/438100/pfx/drive_c/users/steamuser/AppData/LocalLow/VRChat/VRChat/Tools`。VRChat を再起動します。
