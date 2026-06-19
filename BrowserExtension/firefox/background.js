// cookies.js is loaded first via manifest background.scripts.

// Auto-send is opt-in: only pushes on page load when the user enabled it in the popup.
ext.tabs.onUpdated.addListener(async (_id, info, tab) => {
  if (info.status !== "complete" || !tab.url || !tab.url.includes("youtube.com")) return;
  const { autoShare } = await ext.storage.local.get("autoShare");
  if (autoShare) sendCookies();
});

// App-driven refresh: poll the app; when it asks (e.g. just started), push cookies.
// Gated by the same opt-in. Alarm survives the MV3 service worker being suspended.
async function pollRefresh() {
  const { autoShare } = await ext.storage.local.get("autoShare");
  if (!autoShare) return;
  try {
    const res = await fetch(ENDPOINT + "/refresh-needed", { headers: EXT_HEADER });
    if (res.ok && (await res.text()).trim() === "1") sendCookies();
  } catch (e) {
    // App not running.
  }
}
ext.alarms.create("cookieRefresh", { periodInMinutes: 1 });
ext.alarms.onAlarm.addListener(pollRefresh);
ext.runtime.onStartup.addListener(pollRefresh);
