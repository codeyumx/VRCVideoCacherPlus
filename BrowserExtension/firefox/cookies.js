// Shared cookie logic, loaded by both the background worker (importScripts) and
// the popup (<script>). Use `ext` so promises work in Chrome (chrome.*) and
// Firefox (browser.* — Firefox's chrome.* is callback-only).
const ext = globalThis.browser ?? globalThis.chrome;
const ENDPOINT = "http://localhost:9696/youtube-cookies";
// Identifies this (new) extension to the app, so its new-only features (app-start refresh)
// never trigger for the old EllyVR/upstream extension, which doesn't send this header.
const EXT_HEADER = { "X-VRCVideoCacher-Ext": "VRCVideoCacherPlus" };

async function buildCookieText() {
  const cookies = await ext.cookies.getAll({ domain: "youtube.com" });
  // domain \t includeSubdomains \t path \t secure \t expiration \t name \t value
  const lines = cookies.map((c) =>
    [
      c.domain,
      c.domain.startsWith(".") ? "TRUE" : "FALSE",
      c.path,
      c.secure ? "TRUE" : "FALSE",
      Math.floor(c.expirationDate || 0), // 0 = session cookie
      c.name,
      c.value,
    ].join("\t")
  );
  const loggedIn = cookies.some((c) => c.name === "LOGIN_INFO");
  return { text: "# Netscape HTTP Cookie File\n" + lines.join("\n") + "\n", loggedIn };
}

// Returns "ok" | "notLoggedIn" | "appOffline".
async function sendCookies() {
  const { text, loggedIn } = await buildCookieText();
  if (!loggedIn) return "notLoggedIn";
  try {
    const res = await fetch(ENDPOINT, {
      method: "POST",
      headers: { "Content-Type": "text/plain", ...EXT_HEADER },
      body: text,
    });
    return res.ok ? "ok" : "appOffline";
  } catch (e) {
    return "appOffline";
  }
}
