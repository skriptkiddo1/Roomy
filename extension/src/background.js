// Roomy Tabs — усыпление неактивных вкладок с учётом свободной памяти.
// Работает в Chrome/Edge (service worker) и Firefox (background script).

const B = globalThis.browser || globalThis.chrome;

const DEFAULTS = {
  enabled: true,
  idleMinutes: 30,          // через сколько минут без внимания вкладка засыпает
  pressureIdleMinutes: 5,   // то же, когда памяти мало
  pressureThreshold: 20,    // «памяти мало», если свободно меньше этого процента (только Chrome/Edge)
  protectPinned: true,
  protectAudible: true,
  protectForms: true,
  whitelist: [
    "meet.google.com", "zoom.us", "teams.microsoft.com", "teams.live.com",
    "discord.com", "web.telegram.org", "web.whatsapp.com",
    "music.yandex.ru", "open.spotify.com", "music.youtube.com"
  ]
};

const TICK_ALARM = "roomy-tick";

// ---------- настройки и состояние ----------

async function getSettings() {
  const s = await B.storage.local.get("settings");
  return Object.assign({}, DEFAULTS, s.settings || {});
}

async function saveSettings(patch) {
  const cur = await getSettings();
  const next = Object.assign(cur, patch);
  await B.storage.local.set({ settings: next });
  return next;
}

// Вкладки с несохранённым вводом и время последней активности.
// storage.session переживает перезапуск service worker, но не браузера — это и нужно.
const sessionStore = B.storage.session || null;
let memState = { dirty: {}, lastActive: {} };

async function loadState() {
  if (!sessionStore) return memState;
  const r = await sessionStore.get(["dirty", "lastActive"]);
  memState = { dirty: r.dirty || {}, lastActive: r.lastActive || {} };
  return memState;
}

async function saveState() {
  if (sessionStore) await sessionStore.set({ dirty: memState.dirty, lastActive: memState.lastActive });
}

async function bumpStats(n) {
  if (!n) return;
  const today = new Date().toISOString().slice(0, 10);
  const r = await B.storage.local.get("stats");
  const st = r.stats || { total: 0, day: today, today: 0 };
  if (st.day !== today) { st.day = today; st.today = 0; }
  st.total += n;
  st.today += n;
  await B.storage.local.set({ stats: st });
}

// ---------- память ----------

async function memoryInfo() {
  try {
    if (B.system && B.system.memory && B.system.memory.getInfo) {
      const m = await B.system.memory.getInfo();
      if (m && m.capacity > 0) {
        return {
          total: m.capacity,
          available: m.availableCapacity,
          freePct: Math.round((m.availableCapacity / m.capacity) * 100)
        };
      }
    }
  } catch (e) { /* нет доступа — работаем по таймеру */ }
  return null;
}

// ---------- правила ----------

function hostOf(url) {
  try { return new URL(url).hostname.toLowerCase(); } catch (e) { return ""; }
}

function isWhitelisted(url, list) {
  const h = hostOf(url);
  if (!h) return false;
  return list.some(d => {
    d = String(d).trim().toLowerCase().replace(/^https?:\/\//, "").replace(/\/.*$/, "");
    return d && (h === d || h.endsWith("." + d));
  });
}

function lastAccess(tab) {
  const own = memState.lastActive[tab.id];
  const native = typeof tab.lastAccessed === "number" ? tab.lastAccessed : 0;
  return Math.max(own || 0, native || 0);
}

/** Почему вкладку нельзя усыплять (null — можно). */
function protectionReason(tab, s) {
  if (tab.discarded) return "sleeping";
  if (tab.active) return "active";
  if (!/^https?:/i.test(tab.url || "")) return "system";
  if (tab.status && tab.status !== "complete") return "loading";
  if (tab.autoDiscardable === false) return "system";
  if (s.protectAudible && tab.audible) return "audible";
  if (s.protectPinned && tab.pinned) return "pinned";
  if (s.protectForms && memState.dirty[tab.id]) return "form";
  if (isWhitelisted(tab.url, s.whitelist)) return "whitelist";
  return null;
}

async function discardTab(tab) {
  try {
    await B.tabs.discard(tab.id);
    return true;
  } catch (e) {
    return false;
  }
}

/**
 * Проход по вкладкам. force=true — «Усыпить сейчас»: не ждём таймер, но все защиты действуют.
 */
async function sweep(force) {
  const s = await getSettings();
  await loadState();
  if (!s.enabled && !force) return { slept: 0 };

  const mem = await memoryInfo();
  const pressure = !!(mem && mem.freePct < s.pressureThreshold);
  const idleMs = (pressure ? s.pressureIdleMinutes : s.idleMinutes) * 60000;
  const now = Date.now();

  const tabs = await B.tabs.query({});
  let slept = 0;
  for (const tab of tabs) {
    if (protectionReason(tab, s)) continue;
    const last = lastAccess(tab);
    if (!force) {
      if (!last) { memState.lastActive[tab.id] = now; continue; } // не знаем — начинаем отсчёт
      if (now - last < idleMs) continue;
    }
    if (await discardTab(tab)) slept++;
  }
  await saveState();
  await bumpStats(slept);
  return { slept, pressure };
}

// ---------- сводка для окна расширения ----------

async function summary(activeTabId) {
  const s = await getSettings();
  await loadState();
  const mem = await memoryInfo();
  const tabs = await B.tabs.query({});
  const counts = { total: tabs.length, sleeping: 0, awake: 0 };
  const protectedBy = { audible: 0, pinned: 0, form: 0, whitelist: 0 };
  for (const t of tabs) {
    if (t.discarded) { counts.sleeping++; continue; }
    counts.awake++;
    const r = protectionReason(t, s);
    if (r && protectedBy.hasOwnProperty(r)) protectedBy[r]++;
  }
  const st = (await B.storage.local.get("stats")).stats || { total: 0, today: 0, day: "" };
  const today = new Date().toISOString().slice(0, 10);
  let current = null;
  if (activeTabId != null) {
    try {
      const t = await B.tabs.get(activeTabId);
      const h = hostOf(t.url);
      if (h && /^https?:/i.test(t.url)) current = { host: h, whitelisted: isWhitelisted(t.url, s.whitelist) };
    } catch (e) { }
  }
  return {
    settings: s,
    mem,
    pressure: !!(mem && mem.freePct < s.pressureThreshold),
    counts,
    protectedBy,
    stats: { total: st.total, today: st.day === today ? st.today : 0 },
    current,
    hasMemoryApi: !!(B.system && B.system.memory)
  };
}

// ---------- события ----------

function ensureAlarm() {
  B.alarms.get(TICK_ALARM).then(a => {
    if (!a) B.alarms.create(TICK_ALARM, { periodInMinutes: 1 });
  });
}

B.alarms.onAlarm.addListener(a => { if (a.name === TICK_ALARM) sweep(false); });

B.tabs.onActivated.addListener(async info => {
  await loadState();
  memState.lastActive[info.tabId] = Date.now();
  await saveState();
});

B.tabs.onUpdated.addListener(async (tabId, change) => {
  if (change.url) {
    await loadState();
    if (memState.dirty[tabId]) { delete memState.dirty[tabId]; await saveState(); }
  }
});

B.tabs.onRemoved.addListener(async tabId => {
  await loadState();
  delete memState.dirty[tabId];
  delete memState.lastActive[tabId];
  await saveState();
});

if (B.tabs.onReplaced) {
  B.tabs.onReplaced.addListener(async (added, removed) => {
    await loadState();
    if (memState.lastActive[removed]) memState.lastActive[added] = memState.lastActive[removed];
    if (memState.dirty[removed]) memState.dirty[added] = true;
    delete memState.lastActive[removed];
    delete memState.dirty[removed];
    await saveState();
  });
}

B.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  (async () => {
    switch (msg && msg.type) {
      case "dirty":
        if (sender.tab && sender.tab.id != null) {
          await loadState();
          if (msg.value) memState.dirty[sender.tab.id] = true;
          else delete memState.dirty[sender.tab.id];
          await saveState();
        }
        return { ok: true };
      case "summary":
        return await summary(msg.tabId);
      case "sleepNow":
        return await sweep(true);
      case "saveSettings":
        return { settings: await saveSettings(msg.patch || {}) };
      case "toggleSite": {
        const s = await getSettings();
        const host = String(msg.host || "").toLowerCase();
        let list = s.whitelist.slice();
        if (isWhitelisted("https://" + host + "/", list)) {
          list = list.filter(d => !(host === d || host.endsWith("." + d)));
        } else {
          list.push(host);
        }
        return { settings: await saveSettings({ whitelist: list }) };
      }
      case "resetSettings":
        await B.storage.local.set({ settings: DEFAULTS });
        return { settings: DEFAULTS };
    }
    return null;
  })().then(sendResponse, err => sendResponse({ error: String(err) }));
  return true; // ответ асинхронный
});

// Открытые до установки вкладки не знают о скрипте отслеживания форм — подключаем его к ним.
async function injectIntoOpenTabs() {
  if (!B.scripting) return;
  const tabs = await B.tabs.query({ url: ["http://*/*", "https://*/*"] });
  for (const t of tabs) {
    if (t.discarded) continue;
    try {
      await B.scripting.executeScript({ target: { tabId: t.id, allFrames: true }, files: ["content.js"] });
    } catch (e) { /* страница магазина расширений и т. п. */ }
  }
}

B.runtime.onInstalled.addListener(async () => {
  ensureAlarm();
  await injectIntoOpenTabs();
});

if (B.runtime.onStartup) B.runtime.onStartup.addListener(ensureAlarm);
ensureAlarm();

// для тестов в Node
if (typeof module !== "undefined") module.exports = { protectionReason, isWhitelisted, sweep, summary, DEFAULTS, _state: () => memState };
