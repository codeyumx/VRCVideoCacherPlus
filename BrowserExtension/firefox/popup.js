const status = document.getElementById("status");

// Language picker (flags). Persisted; defaults to English.
const langSel = document.getElementById("lang");
for (const code in I18N) langSel.add(new Option(I18N[code].lang, code));
ext.storage.local.get("lang").then(({ lang: saved }) => {
  if (saved && I18N[saved]) lang = saved;
  langSel.value = lang;
  applyI18n();
});
langSel.addEventListener("change", () => {
  lang = langSel.value;
  ext.storage.local.set({ lang });
  applyI18n();
});

document.getElementById("send").addEventListener("click", async () => {
  status.textContent = t("sending");
  status.textContent = t(await sendCookies());
});

document.getElementById("copy").addEventListener("click", async () => {
  const { text, loggedIn } = await buildCookieText();
  if (!loggedIn) { status.textContent = t("notLoggedIn"); return; }
  await navigator.clipboard.writeText(text);
  status.textContent = t("copied");
});

const auto = document.getElementById("auto");
ext.storage.local.get("autoShare").then(({ autoShare }) => {
  auto.checked = !!autoShare;
});
auto.addEventListener("change", () => {
  ext.storage.local.set({ autoShare: auto.checked });
});
