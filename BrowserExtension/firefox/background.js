// cookies.js is loaded first via manifest background.scripts.

// AUTOMATIC mode (opt-in): push cookies on every YouTube page load. This is the only
// behaviour gated by the "Share cookies automatically" toggle.
ext.tabs.onUpdated.addListener(async (_id, info, tab) => {
  if (info.status !== "complete" || !isYouTubeUrl(tab.url)) return;
  const { autoShare } = await ext.storage.local.get("autoShare");
  if (autoShare) sendCookies();
});

// Matches youtube.com and its subdomains by hostname, so lookalike hosts (notyoutube.com) and
// URLs that merely mention it (?next=youtube.com) don't trigger an upload.
function isYouTubeUrl(url) {
  if (!url) return false;
  let host;
  try {
    host = new URL(url).hostname;
  } catch {
    return false;
  }
  return host === "youtube.com" || host.endsWith(".youtube.com");
}

// MANUAL (app-triggered) refresh via long-poll: hold a request open so the app can wake it
// the instant the user clicks "Request fresh cookies" — they never need to open this popup
// and click Send. This runs regardless of the auto-share toggle, because it only ever sends
// when the app (i.e. the user, via the app) explicitly asks. The loop re-arms itself; the
// alarm/startup hooks restart it if it ever stops.
// Waits after a failure grow from 5 s to a 60 s cap (the alarm period, so a suspended worker
// loses nothing by sleeping through them) and reset once the app answers normally or cookies
// go through. That covers the app not running and the app asking while the browser is logged
// out of YouTube, both of which otherwise retried every 5 s indefinitely.
const MIN_BACKOFF_MS = 5000;
const MAX_BACKOFF_MS = 60000;
let refreshLooping = false;
async function refreshLoop() {
  if (refreshLooping) return;
  refreshLooping = true;
  let backoff = MIN_BACKOFF_MS;
  const failed = async () => {
    await delay(backoff);
    backoff = Math.min(backoff * 2, MAX_BACKOFF_MS);
  };
  try {
    while (true) {
      try {
        const res = await fetch(ENDPOINT + "/refresh-wait", { headers: EXT_HEADER });
        if (!res.ok) {
          await failed();
        } else if ((await res.text()).trim() !== "1") {
          backoff = MIN_BACKOFF_MS;
        } else if ((await sendCookies()) === "ok") {
          backoff = MIN_BACKOFF_MS;
        } else {
          await failed(); // Not logged in, or the app rejected them.
        }
      } catch (e) {
        await failed(); // App not running.
      }
    }
  } finally {
    refreshLooping = false;
  }
}
const delay = (ms) => new Promise((r) => setTimeout(r, ms));

ext.alarms.create("cookieRefresh", { periodInMinutes: 1 });
ext.alarms.onAlarm.addListener(refreshLoop);
ext.runtime.onStartup.addListener(refreshLoop);
refreshLoop();
