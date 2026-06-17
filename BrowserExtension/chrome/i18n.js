// Tiny i18n: explicit language picker (no browser detection). Loaded before popup.js.
// Strings keyed by data-i18n / data-i18n-title attributes in popup.html.
const I18N = {
  en: { lang: "🇬🇧 English", send: "Send cookies to VRCVideoCacher", copy: "Copy cookies to clipboard", auto: "Share cookies automatically", help: "When on, VRCVideoCacher automatically gets fresh YouTube cookies when it starts up (and when you visit YouTube). When off, you'll need to send cookies manually with the button above.", ok: "✓ Cookies sent.", notLoggedIn: "Not logged in to YouTube yet.", appOffline: "Couldn't reach VRCVideoCacher — is it running?", sending: "Sending…", copied: "✓ Copied to clipboard." },
  pt: { lang: "🇧🇷 Português", send: "Enviar cookies ao VRCVideoCacher", copy: "Copiar cookies para a área de transferência", auto: "Compartilhar cookies automaticamente", help: "Quando ativado, o VRCVideoCacher obtém cookies novos do YouTube ao iniciar (e ao visitar o YouTube). Quando desativado, você precisa enviar os cookies manualmente.", ok: "✓ Cookies enviados.", notLoggedIn: "Ainda não conectado ao YouTube.", appOffline: "Não foi possível acessar o VRCVideoCacher — ele está em execução?", sending: "Enviando…", copied: "✓ Copiado." },
  ja: { lang: "🇯🇵 日本語", send: "VRCVideoCacher にクッキーを送信", copy: "クッキーをクリップボードにコピー", auto: "クッキーを自動共有する", help: "オンにすると、VRCVideoCacher は起動時（および YouTube を開いたとき）に新しい YouTube クッキーを自動取得します。オフの場合は手動で送信する必要があります。", ok: "✓ クッキーを送信しました。", notLoggedIn: "まだ YouTube にログインしていません。", appOffline: "VRCVideoCacher に接続できません。起動していますか？", sending: "送信中…", copied: "✓ コピーしました。" },
  zh: { lang: "🇨🇳 中文", send: "向 VRCVideoCacher 发送 Cookie", copy: "复制 Cookie 到剪贴板", auto: "自动共享 Cookie", help: "开启后，VRCVideoCacher 会在启动时（以及访问 YouTube 时）自动获取最新的 YouTube Cookie。关闭后需要手动发送。", ok: "✓ 已发送 Cookie。", notLoggedIn: "尚未登录 YouTube。", appOffline: "无法连接 VRCVideoCacher — 它正在运行吗？", sending: "发送中…", copied: "✓ 已复制。" },
  ko: { lang: "🇰🇷 한국어", send: "VRCVideoCacher에 쿠키 보내기", copy: "쿠키를 클립보드에 복사", auto: "쿠키 자동 공유", help: "켜면 VRCVideoCacher가 시작할 때(및 YouTube 방문 시) 최신 YouTube 쿠키를 자동으로 가져옵니다. 끄면 수동으로 보내야 합니다.", ok: "✓ 쿠키를 보냈습니다.", notLoggedIn: "아직 YouTube에 로그인하지 않았습니다.", appOffline: "VRCVideoCacher에 연결할 수 없습니다 — 실행 중인가요?", sending: "보내는 중…", copied: "✓ 복사됨." },
  hu: { lang: "🇭🇺 Magyar", send: "Sütik küldése a VRCVideoCachernek", copy: "Sütik másolása a vágólapra", auto: "Sütik automatikus megosztása", help: "Bekapcsolva a VRCVideoCacher indításkor (és YouTube megnyitásakor) automatikusan friss YouTube-sütiket szerez. Kikapcsolva kézzel kell elküldeni.", ok: "✓ Sütik elküldve.", notLoggedIn: "Még nincs bejelentkezve a YouTube-ra.", appOffline: "Nem érhető el a VRCVideoCacher — fut egyáltalán?", sending: "Küldés…", copied: "✓ Vágólapra másolva." },
  ru: { lang: "🇷🇺 Русский", send: "Отправить куки в VRCVideoCacher", copy: "Скопировать куки в буфер обмена", auto: "Автоматически делиться куки", help: "Если включено, VRCVideoCacher автоматически получает свежие куки YouTube при запуске (и при посещении YouTube). Если выключено, отправляйте куки вручную.", ok: "✓ Куки отправлены.", notLoggedIn: "Вы ещё не вошли в YouTube.", appOffline: "Не удалось связаться с VRCVideoCacher — он запущен?", sending: "Отправка…", copied: "✓ Скопировано." },
};

let lang = "en";
const t = (key) => (I18N[lang] || I18N.en)[key] ?? I18N.en[key];

function applyI18n() {
  document.querySelectorAll("[data-i18n]").forEach((el) => { el.textContent = t(el.dataset.i18n); });
  document.querySelectorAll("[data-i18n-title]").forEach((el) => { el.title = t(el.dataset.i18nTitle); });
}
