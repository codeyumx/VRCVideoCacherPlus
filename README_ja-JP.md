<div align="center">

![Header Banner](https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/4296960/d1bac93e4abb00108cda2137260b76a25bcffea4/header.jpg)

<hr>
</div>

**Language:** [English](./README.md) | **日本語** | [Magyar](./README_hu-HU.md) | [한국어](./README_ko-KR.md) | [Português do Brasil](./README_pt-BR.md)

### ダウンロード

- [Windows — VRCVideoCacher.exe](https://github.com/codeyumx/VRCVideoCacherPlus/releases/latest/download/VRCVideoCacher.exe)
- [Linux — VRCVideoCacher](https://github.com/codeyumx/VRCVideoCacherPlus/releases/latest/download/VRCVideoCacher)

現時点では VRCVideoCacher 本家のブラウザー拡張機能をご利用ください:
- [Chrome 拡張機能](https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge)
- [Firefox 拡張機能](https://addons.mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter)

---

### フォークの変更点 ([EllyVR/VRCVideoCacher](https://github.com/EllyVR/VRCVideoCacher) との違い)

このフォークでは、VRChat が動画を再生中に帯域幅を使いすぎないようにするための設定を**キャッシュ設定**に追加しています。

#### ストリーミング中はキャッシュダウンロードを一時停止

VRChat がストリーミング動画を再生している間、キャッシュダウンロードを自動的に一時停止できます。ストリームが止まってからダウンロードを再開するまでの待ち時間（秒）を設定してください。0 にすると無効になります。

**ヒント:** 長い動画やループするコンテンツを見る場合は、下の速度制限を（併用して）使用してください。

#### キャッシュダウンロード速度制限

キャッシュダウンロードの速度を制限できます（MB/s 単位）。0 にすると無制限になります。

**おすすめの設定:** 一時停止の遅延を 300 秒に設定して動画の切り替えや曲のキューイングに対応し、長時間の再生には速度制限をバックアップとして使用してください。

#### ダウンロードキューと手動ダウンロード

**ダウンロード**タブから手動で動画をキャッシュキューに追加できます。テキストボックスに YouTube の URL を 1 行に 1 つずつ貼り付けて**追加**をクリックしてください。YouTube のプレイリストにも対応しています — プレイリストの URL を貼り付けると、すべての動画が自動的にキューに追加されます。

#### HLS / ストリーミング動画プレイリストのキャッシュ

完了済みの HLS ストリーミングプレイリスト（`.m3u8` および mpegts 系）を後から再生できるように MP4 としてキャッシュできるようになりました。検出はコンテンツベースで行われるため、`.m3u8` 拡張子なしで配信されているプレイリストも認識されます。ライブ配信（`#EXT-X-ENDLIST` がないもの）はスキップされ、最大長の上限は**キャッシュ設定**で設定可能です（0 で無制限）。

**クラウド共有 URL について:** Dropbox の `?dl=0` 付きの共有 URL や、Google Drive の `/file/d/<id>/view` 形式の URL は、取得前に自動で直接ダウンロード形式へ書き換えられるため、どちらの形式を貼っても動作します。Mega.nz は対応していません（暗号化 + JS 専用のため）。プレイリスト内のセグメント URL が他の保護されたファイルを指している場合は機能しません — マニフェスト本体とそのセグメントの両方が、直接フェッチ可能なホスト上にある必要があります。

#### その他の改善

- アップデートバナー — 新しいバージョンが利用可能なときにバナーで通知します
- ログビューアーの表示内容を改善
- 統計情報付きの再生履歴で、お気に入りの動画を優先しつつキャッシュ容量を節約
- キュー内のアイテムに「すぐにダウンロード」ボタンを追加 — アイドル待機をスキップして特定の動画のダウンロードを即時開始
- ダウンロードキューに動画タイトルを表示

#### ビルド
##### Windows
Windows でテスト済みです。
##### Linux
Linux は現在未テストですが、試したい方のためにリリースで配布しています。
##### Steam
現時点では Steam には対応していません。

---

## EllyVR 版 VRCVideoCacher README より抜粋

### VRCVideoCacher とは何でしょうか？

VRCVideoCacher は、VRChat での動画をローカルディスクにキャッシュしたり、YouTube の動画の読み込み失敗を修正などを行うためのツールです。

[![Wiki](https://img.shields.io/badge/Wiki-Info-green)](https://github.com/EllyVR/VRCVideoCacher/wiki)
[![Steam Download](https://img.shields.io/badge/Steam-Download-blue?logo=steam)](https://store.steampowered.com/app/4296960)
[![Github Download](https://img.shields.io/badge/Github-Download-blue?logo=github)](https://github.com/EllyVR/VRCVideoCacher/releases/latest)
[![Discord Server](https://img.shields.io/badge/Discord-Join%20Server-5865F2?logo=discord)](https://discord.gg/z5kVNkmQuS)

### Wiki
- [起動オプション](https://github.com/EllyVR/VRCVideoCacher/wiki/Launch-Options)
- [CLI 設定オプション](https://github.com/EllyVR/VRCVideoCacher/wiki/Config-Options)
- [Linux](https://github.com/EllyVR/VRCVideoCacher/wiki/Linux)

### どのように動作しますか？

VRChat の yt-dlp.exe を独自の stub yt-dlp に置換します。これはアプリケーションの起動時に置換され、終了時に復元されます。

不足しているコーデックを自動でインストール: 
[VP9](https://apps.microsoft.com/detail/9n4d0msmp0pt) | [AV1](https://apps.microsoft.com/detail/9mvzqvxjbq9v) | [AC-3](https://apps.microsoft.com/detail/9nvjqjbdkn97)

### 何かリスクはありますか？

VRC か EAC で何かあったかって？いいえ。

YouTube/Google からは？おそらく可能なのでしょうけど、できるのであれば別の Google アカウントを使用することを強く推奨します。

### YouTube のボット検出を回避する方法

YouTube の動画の読み込みに失敗する問題を解決するには、[こちら](https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge)から Chrome の拡張機能をインストールするか、もしくは[こちら](https://addons.mozilla.org/ja-JP/firefox/addon/vrcvideocachercookiesexporter)から Firefox の拡張機能をインストールする必要があります。詳細は[こちら](https://github.com/clienthax/VRCVideoCacherBrowserExtension)をご覧ください。VRCVideoCacher の実行中に少なくとも、[YouTube.com](https://www.youtube.com) にログインした状態で 1 回アクセスしてください。VRCVideoCacher が Cookie を受信後は、拡張機能を安全にアンインストールできます。ただし、アカウントがログインしたまま同じブラウザーで YouTube に再度アクセスすると、YouTube が Cookie を更新し、VRCVideoCacher に保存されている Cookie を無効にしてしまうことにご注意ください。これを回避するには、VRCVideoCacher が Cookie を受信後にブラウザーの YouTube の Cookie を削除するか、メインの YouTube アカウントを使用している場合であれば拡張機能をインストールしたままにするか、シンプルにするためにメインのアカウントとは全く別の Web ブラウザーを使用することを推奨します。

### YouTube の動画が再生できないことがある問題を修正するには

> 読み込みに失敗しました。この症状はファイルが見つからないかコーデックが非対応か動画の解像度が高すぎるか、システムリソースが不足しています。

システム時刻を同期するには、Windows の設定 -> 時刻と言語 -> 日付と時刻を開き、「追加設定」の下にある「今すぐ同期」をクリックします。

### アンインストール

- VRCX を使用している場合は `%AppData%\VRCX\startup` からスタートアップのショートカット「VRCVideoCacher」を削除します
- `%AppData%\VRCVideoCacher` から設定とキャッシュを削除します
- `%AppData%\..\LocalLow\VRChat\VRChat\Tools` から「yt-dlp.exe」を削除し、VRChat を再起動するかワールドに Join し直してください
