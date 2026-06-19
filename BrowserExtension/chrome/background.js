importScripts("cookies.js");

// AUTOMATIC mode (opt-in): push cookies on every YouTube page load. This is the only
// behaviour gated by the "Share cookies automatically" toggle.
ext.tabs.onUpdated.addListener(async (_id, info, tab) => {
  if (info.status !== "complete" || !tab.url || !tab.url.includes("youtube.com")) return;
  const { autoShare } = await ext.storage.local.get("autoShare");
  if (autoShare) sendCookies();
});

// MANUAL (app-triggered) refresh via long-poll: hold a request open so the app can wake it
// the instant the user clicks "Request fresh cookies" — they never need to open this popup
// and click Send. This runs regardless of the auto-share toggle, because it only ever sends
// when the app (i.e. the user, via the app) explicitly asks. The loop re-arms itself; the
// alarm/startup hooks restart it if the browser suspended the worker.
let refreshLooping = false;
async function refreshLoop() {
  if (refreshLooping) return;
  refreshLooping = true;
  try {
    while (true) {
      try {
        const res = await fetch(ENDPOINT + "/refresh-wait", { headers: EXT_HEADER });
        if (res.ok && (await res.text()).trim() === "1") {
          // If we couldn't send (e.g. not logged in), back off so we don't hot-loop while
          // the app keeps asking.
          if ((await sendCookies()) !== "ok") await delay(5000);
        }
      } catch (e) {
        await delay(5000); // App not running — retry later.
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
