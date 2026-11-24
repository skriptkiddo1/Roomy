// Отмечает вкладку, если на странице начали что-то вводить, чтобы её не усыпили
// и не потеряли набранный текст. Сам текст никуда не отправляется и не читается.
(function () {
  if (window.__roomyTabsInjected) return;
  window.__roomyTabsInjected = true;

  const B = globalThis.browser || globalThis.chrome;
  let dirty = false;

  const SKIP_TYPES = { search: 1, checkbox: 1, radio: 1, range: 1, color: 1, button: 1, submit: 1, reset: 1, hidden: 1, image: 1, file: 1 };

  function isTextField(el) {
    if (!el) return false;
    if (el.isContentEditable) return true;
    const tag = (el.tagName || "").toLowerCase();
    if (tag === "textarea") return true;
    if (tag === "input") return !SKIP_TYPES[(el.type || "text").toLowerCase()];
    return false;
  }

  document.addEventListener("input", function (e) {
    if (dirty || !isTextField(e.target)) return;
    dirty = true;
    try { B.runtime.sendMessage({ type: "dirty", value: true }); } catch (err) { }
  }, true);
})();
