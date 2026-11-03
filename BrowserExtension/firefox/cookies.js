// Shared cookie logic, loaded by both the background worker (importScripts) and
// the popup (<script>). Use `ext` so promises work in Chrome (chrome.*) and
// Firefox (browser.* — Firefox's chrome.* is callback-only).
const ext = globalThis.browser ?? globalThis.chrome;
const ENDPOINT = "http://127.0.0.1:9696/api/youtube-cookies";
// Identifies this (new) extension to the app, so its new-only features (app-start refresh)
// never trigger for the old EllyVR/upstream extension, which doesn't send this header.
// The browser header lets the app's Cookies panel show which browsers the extension runs in
// and which one the current cookies came from.
let headersPromise;
function extHeaders() {
  headersPromise ??= detectBrowser().then((browser) => ({
    "X-VRCVideoCacher-Ext": "VRCVideoCacherPlus",
    "X-VRCVideoCacher-Browser": browser,
  }));
  return headersPromise;
}

// Firefox (and forks that keep the API) report their own name; Chromium browsers expose
// brands via User-Agent Client Hints. The brand list always holds "Chromium" and a GREASE
// entry ("Not_A Brand" and variants), so the vendor brand is whichever one is left.
async function detectBrowser() {
  try {
    if (ext.runtime.getBrowserInfo) {
      const info = await ext.runtime.getBrowserInfo();
      return `${info.name} ${info.version.split(".")[0]}`;
    }
  } catch {}
  const brands = navigator.userAgentData?.brands ?? [];
  const real = brands.filter((b) => !/not.?a.?brand/i.test(b.brand));
  const vendor = real.find((b) => b.brand !== "Chromium") ?? real[0];
  return vendor ? `${vendor.brand} ${vendor.version}` : "Chromium";
}

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
      headers: { "Content-Type": "text/plain", ...(await extHeaders()) },
      body: text,
    });
    return res.ok ? "ok" : "appOffline";
  } catch (e) {
    return "appOffline";
  }
}
