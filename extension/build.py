"""Собирает расширение для Chrome/Edge и Firefox: dist/<browser>/ и dist/roomy-tabs-<browser>-<версия>.zip"""
import json
import os
import shutil
import zipfile

VERSION = "1.0.0"
ROOT = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(ROOT, "src")
DIST = os.path.join(ROOT, "dist")

base = {
    "manifest_version": 3,
    "name": "__MSG_extName__",
    "description": "__MSG_extDescription__",
    "version": VERSION,
    "default_locale": "en",
    "homepage_url": "https://github.com/skriptkiddo1/roomy",
    "icons": {s: f"icons/icon{s}.png" for s in ("16", "32", "48", "128")},
    "action": {
        "default_popup": "popup.html",
        "default_title": "__MSG_extName__",
        "default_icon": {s: f"icons/icon{s}.png" for s in ("16", "32", "48")},
    },
    "permissions": ["tabs", "storage", "alarms", "scripting"],
    "host_permissions": ["http://*/*", "https://*/*"],
    "content_scripts": [{
        "matches": ["http://*/*", "https://*/*"],
        "js": ["content.js"],
        "all_frames": True,
        "run_at": "document_idle",
    }],
}

chrome = json.loads(json.dumps(base))
chrome["permissions"].append("system.memory")
chrome["background"] = {"service_worker": "background.js"}
chrome["minimum_chrome_version"] = "121"

firefox = json.loads(json.dumps(base))
firefox["background"] = {"scripts": ["background.js"]}
firefox["browser_specific_settings"] = {
    "gecko": {
        "id": "{1f8b328c-6e62-45c4-84b2-406605d44334}",
        "strict_min_version": "140.0",
        "data_collection_permissions": {"required": ["none"]},
    },
    "gecko_android": {"strict_min_version": "142.0"},
}


def build(name, manifest):
    out = os.path.join(DIST, name)
    if os.path.exists(out):
        shutil.rmtree(out)
    shutil.copytree(SRC, out)
    with open(os.path.join(out, "manifest.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=2)
    zpath = os.path.join(DIST, f"roomy-tabs-{name}-{VERSION}.zip")
    with zipfile.ZipFile(zpath, "w", zipfile.ZIP_DEFLATED) as z:
        for dirpath, _, files in os.walk(out):
            for fn in sorted(files):
                full = os.path.join(dirpath, fn)
                z.write(full, os.path.relpath(full, out).replace(os.sep, "/"))
    print("built", zpath)


if __name__ == "__main__":
    os.makedirs(DIST, exist_ok=True)
    build("chrome", chrome)
    build("firefox", firefox)
