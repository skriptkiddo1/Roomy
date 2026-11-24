const B = globalThis.browser || globalThis.chrome;

function t(key, subs) {
  const s = B.i18n.getMessage(key, subs);
  return s || key;
}

function uiLang() {
  return (B.i18n.getMessage("@@ui_locale") || B.i18n.getUILanguage() || "en").replace("_", "-").slice(0, 2);
}

function fmtGB(bytes) {
  const gb = bytes / 1024 / 1024 / 1024;
  return gb.toFixed(1).replace(".", t("decimalSep") || ".") + " " + t("gb");
}

function send(msg) { return B.runtime.sendMessage(msg); }

const $ = id => document.getElementById(id);
let activeTabId = null;
let data = null;

function applyI18n() {
  document.querySelectorAll("[data-i18n]").forEach(el => { el.textContent = t(el.dataset.i18n); });
  document.querySelectorAll("[data-i18n-title]").forEach(el => { el.title = t(el.dataset.i18nTitle); });
  document.documentElement.lang = uiLang();
}

function fillSelect(sel, values, suffixKey) {
  sel.innerHTML = "";
  for (const v of values) {
    const o = document.createElement("option");
    o.value = v;
    o.textContent = v + " " + t(suffixKey);
    sel.appendChild(o);
  }
}

function render() {
  const d = data;
  const s = d.settings;
  $("enabled").checked = s.enabled;

  // память
  const minutes = d.pressure ? s.pressureIdleMinutes : s.idleMinutes;
  if (d.mem) {
    const usedPct = 100 - d.mem.freePct;
    const color = d.pressure ? "var(--red)" : usedPct >= 70 ? "var(--orange)" : "var(--green)";
    $("memText").textContent = t("memFree", [fmtGB(d.mem.available), fmtGB(d.mem.total)]);
    $("memPct").textContent = t("usedPct", [String(usedPct)]);
    $("memPct").style.color = color;
    $("memBar").style.width = usedPct + "%";
    $("memBar").style.background = color;
    $("memBar").parentElement.hidden = false;
  } else {
    $("memText").textContent = t("memUnknown");
    $("memPct").textContent = "";
    $("memBar").parentElement.hidden = true;
  }
  let rule;
  if (!s.enabled) rule = t("ruleOff");
  else if (d.pressure) rule = t("rulePressure", [String(minutes)]);
  else rule = t("ruleNormal", [String(minutes)]);
  $("ruleText").textContent = rule;

  // вкладки
  $("cTotal").textContent = d.counts.total;
  $("cSleeping").textContent = d.counts.sleeping;
  $("cAwake").textContent = d.counts.awake;

  // текущий сайт
  if (d.current) {
    $("siteRow").hidden = false;
    $("siteHost").textContent = d.current.host;
    $("siteState").textContent = d.current.whitelisted ? t("siteNever") : t("siteNormal");
    $("siteToggle").textContent = d.current.whitelisted ? t("siteAllow") : t("siteProtect");
  } else {
    $("siteRow").hidden = true;
  }

  // подробности
  const p = d.protectedBy;
  const parts = [];
  if (p.audible) parts.push(t("protAudible", [String(p.audible)]));
  if (p.pinned) parts.push(t("protPinned", [String(p.pinned)]));
  if (p.form) parts.push(t("protForm", [String(p.form)]));
  if (p.whitelist) parts.push(t("protWhitelist", [String(p.whitelist)]));
  $("protectedText").textContent = parts.length ? parts.join(" · ") : t("protNone");

  $("idleMinutes").value = String(s.idleMinutes);
  $("pressureIdleMinutes").value = String(s.pressureIdleMinutes);
  $("pressureThreshold").value = String(s.pressureThreshold);
  $("pressureRow1").hidden = !d.hasMemoryApi;
  $("pressureRow2").hidden = !d.hasMemoryApi;
  $("protectPinned").checked = s.protectPinned;
  $("protectAudible").checked = s.protectAudible;
  $("protectForms").checked = s.protectForms;
  if (document.activeElement !== $("whitelist")) $("whitelist").value = s.whitelist.join("\n");
  $("statsText").textContent = t("statsLine", [String(d.stats.today), String(d.stats.total)]);
}

async function refresh() {
  data = await send({ type: "summary", tabId: activeTabId });
  if (data && !data.error) render();
}

async function save(patch) {
  await send({ type: "saveSettings", patch });
  await refresh();
}

async function init() {
  applyI18n();
  fillSelect($("idleMinutes"), [10, 15, 30, 60, 120, 240], "min");
  fillSelect($("pressureIdleMinutes"), [2, 5, 10, 15], "min");
  fillSelect($("pressureThreshold"), [10, 15, 20, 25, 30], "percentFree");

  const [tab] = await B.tabs.query({ active: true, currentWindow: true });
  activeTabId = tab ? tab.id : null;

  const ui = (await B.storage.local.get("ui")).ui || {};
  $("detailed").checked = !!ui.detailed;
  $("details").hidden = !ui.detailed;

  $("enabled").addEventListener("change", e => save({ enabled: e.target.checked }));
  $("detailed").addEventListener("change", async e => {
    $("details").hidden = !e.target.checked;
    await B.storage.local.set({ ui: { detailed: e.target.checked } });
  });
  $("sleepNow").addEventListener("click", async () => {
    $("sleepNow").disabled = true;
    const r = await send({ type: "sleepNow" });
    $("sleepNow").disabled = false;
    $("result").textContent = r && r.slept ? t("sleptN", [String(r.slept)]) : t("sleptNone");
    await refresh();
  });
  $("siteToggle").addEventListener("click", async () => {
    if (!data || !data.current) return;
    await send({ type: "toggleSite", host: data.current.host });
    await refresh();
  });
  $("idleMinutes").addEventListener("change", e => save({ idleMinutes: +e.target.value }));
  $("pressureIdleMinutes").addEventListener("change", e => save({ pressureIdleMinutes: +e.target.value }));
  $("pressureThreshold").addEventListener("change", e => save({ pressureThreshold: +e.target.value }));
  $("protectPinned").addEventListener("change", e => save({ protectPinned: e.target.checked }));
  $("protectAudible").addEventListener("change", e => save({ protectAudible: e.target.checked }));
  $("protectForms").addEventListener("change", e => save({ protectForms: e.target.checked }));
  $("saveWhitelist").addEventListener("click", async () => {
    const list = $("whitelist").value.split(/[\s,]+/).map(x => x.trim().toLowerCase()).filter(Boolean);
    await save({ whitelist: Array.from(new Set(list)) });
    $("savedMark").textContent = t("saved");
    setTimeout(() => { $("savedMark").textContent = ""; }, 2000);
  });
  $("reset").addEventListener("click", async () => {
    if (!confirm(t("resetConfirm"))) return;
    await send({ type: "resetSettings" });
    await refresh();
  });

  await refresh();
  setInterval(refresh, 3000);
}

init();
