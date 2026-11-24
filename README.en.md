# Roomy

<p align="center"><img src="docs/roomy.gif" width="160" alt="Roomy the memory-chip robot"></p>

<p align="center"><i>"You don't clean memory. You find out who's eating it."</i> - Roomy, memory-chip robot</p>

**A helper for low-end Windows PCs.** Roomy shows where your RAM and disk space go, explains in plain words what all those background programs are, and helps you free things up - carefully, and only when you say so.

[Русский](README.md)

> ⚠️ **Not code-signed yet** (certificates cost money and broski is saving up). On first launch Windows may show a blue SmartScreen box - click "More info" -> "Run anyway". Windows 11 Smart App Control may block it entirely. The source is right here, so you can read it or build it yourself.

Speaks English and Russian: picks the language of your Windows, or switch it yourself - right-click the tray icon -> "Language / Язык".

## Principles

- **Never does stuff behind your back.** Out of the box Roomy only watches. What to throttle, close or delete is your call.
- **Explains instead of scaring.** No "YOUR PC IS AT RISK", no "10x faster". Just real reasons and honest advice.
- **Doesn't "clean" RAM.** Forcing memory out of programs (like Mem Reduct does) doesn't make a PC faster - it just makes those programs reload stuff from disk later. Roomy goes after the cause: which programs and tabs are holding the memory.
- **Tiny.** One ~300 KB exe, no installer. Runs on .NET Framework 4.8, which Windows already has.
- **Private.** Collects nothing, sends nothing. See [PRIVACY.md](PRIVACY.md).

## What it does

**Memory**
- A memory-chip robot named **Roomy** (yes, same as the app) lives in your tray: chill when there's enough RAM, sweaty when there isn't, and it blinks now and then so you know it's alive. Prefer numbers? Right-click the icon -> "Tray icon" -> "Memory percent".
- Programs sorted by memory, with processes grouped (19 browser processes = one row), growth over the last 10 minutes, and a chart.
- A mode per program: **leave alone** / **throttle in background** (while you're not using it, Windows takes memory from it first) / **closable** (Economy mode closes it and starts it again later).
- A notification when memory runs low, listing the hungriest programs.

**"What is this process?"**
- Built-in notes on ~100 common Windows processes, drivers and apps: what it is, whether it's safe to touch, what happens if you close it.
- File facts: path, publisher, digital signature (Windows catalogs included).
- Warnings for sketchy stuff: a system name outside the Windows folder, running from a temp folder, an unknown unsigned program. A VirusTotal lookup by file hash (the file itself never leaves your PC).

**Disk space**
- Safe cleanup by category, with explanations: temp files, browser and app caches, error reports, shader cache, old installers (to the Recycle Bin), the Recycle Bin itself.
- What's taking the space: biggest folders (down to the exact subfolder), biggest files, duplicate copies of your files.

**PC check-up**
- RAM (can you add a stick or is it soldered), system drive type (HDD or SSD), free space, page file, memory compression, startup apps, power mode, uptime, background "helpers", browser, core isolation - each with an explanation and a button that opens the right Windows setting.

## Roomy Tabs - browser extension

Puts tabs you haven't looked at in a while to sleep. Never touches tabs playing audio, tabs where you've typed something, pinned tabs, or sites on your exceptions list. In Chrome and Edge it also watches free memory: when it's low, tabs fall asleep sooner. Source is in [`extension/`](extension/).

## Download

Grab the files from [Releases](../../releases): `Roomy.exe` and the extension archives. `SHA256SUMS.txt` is there too if you want to check them.

Needs Windows 10 (1903+) or Windows 11, 64-bit.

## Building

You'll need the .NET 8 SDK (on Windows):

```
dotnet build src/Roomy/Roomy.csproj -c Release -o out
python extension/build.py
```

Every push to `main` gets built by GitHub Actions. Publishing a release attaches the built files to it automatically.

## Questions nobody asked

**Why a robot?** Because staring at a percentage is boring, and a little chip that sweats when you're out of RAM isn't. Want it grown-up? Switch the tray icon to a plain percentage.

**Why does it blink?** So you can see Roomy is alive and not frozen. Annoying? Turn it off in the same tray menu.

**What happens if I click it in the main window?** Try it.

**Why is the author a script kiddie?** Everyone starts somewhere. What matters is the app only does what you allowed.

## Author

Made by **[skriptkiddo](https://github.com/skriptkiddo1)**. Pronouns: **broski** - not "he", broski. ("broski wrote it", "ask broski", "broski will fix it".)

Found a bug or got an idea? [Open an issue](../../issues), broski will take a look. If Roomy saved your laptop, drop a ⭐ - it makes broski happy.

## License

[GPL-3.0](LICENSE). The code is open: read it, change it, share it - just keep anything you build on it open too.
