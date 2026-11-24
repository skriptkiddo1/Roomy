using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Roomy
{
    class AppGroup
    {
        public string Key;
        public string Display;
        public long Bytes;
        public List<int> Pids = new List<int>();
        public bool ServiceOnly = true; // все процессы - вне сеанса пользователя (службы)
    }

    static class MemInfo
    {
        static readonly Dictionary<string, string> DisplayCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly int OwnSession = Process.GetCurrentProcess().SessionId;
        static readonly int OwnPid = Process.GetCurrentProcess().Id;

        // для проверки интерфейса без Windows: подставные данные.
        public static Func<List<AppGroup>> TestOverride;

        public static List<AppGroup> Snapshot()
        {
            if (TestOverride != null) return TestOverride();
            var groups = new Dictionary<string, AppGroup>(StringComparer.OrdinalIgnoreCase);
            Process[] procs = Process.GetProcesses();
            try
            {
                foreach (var p in procs)
                {
                    int pid;
                    string key;
                    try { pid = p.Id; key = Settings.Norm(p.ProcessName); }
                    catch { continue; }
                    if (pid == 0 || pid == 4) continue;

                    long bytes = Native.PrivateWorkingSet(pid);
                    if (bytes < 0) { try { bytes = p.WorkingSet64; } catch { bytes = 0; } }
                    int session = 0;
                    try { session = p.SessionId; } catch { }

                    AppGroup g;
                    if (!groups.TryGetValue(key, out g))
                    {
                        g = new AppGroup { Key = key, Display = DisplayName(key, pid) };
                        groups[key] = g;
                    }
                    if (session != 0) g.ServiceOnly = false;
                    g.Bytes += bytes;
                    g.Pids.Add(pid);
                }
            }
            finally { foreach (var p in procs) p.Dispose(); }
            return groups.Values.OrderByDescending(g => g.Bytes).ToList();
        }

        static string DisplayName(string key, int pid)
        {
            string d;
            if (DisplayCache.TryGetValue(key, out d)) return d;
            d = key;
            try
            {
                string path = Native.ImagePath(pid);
                if (path != null)
                {
                    var vi = FileVersionInfo.GetVersionInfo(path);
                    string desc = (vi.FileDescription ?? "").Trim();
                    if (desc.Length == 0) desc = (vi.ProductName ?? "").Trim();
                    if (desc.Length > 0 && desc.Length <= 60)
                        d = string.Equals(desc, key, StringComparison.OrdinalIgnoreCase) ? desc : desc + " (" + key + ")";
                    DisplayCache[key] = d;
                }
            }
            catch { }
            return d;
        }

        // процессы с этим именем в текущей сессии пользователя.
        public static List<Process> UserProcesses(string key)
        {
            var res = new List<Process>();
            foreach (var p in Process.GetProcessesByName(key))
            {
                bool keep = false;
                try { keep = p.SessionId == OwnSession && p.Id != OwnPid; } catch { }
                if (keep) res.Add(p); else p.Dispose();
            }
            return res;
        }

        // аккуратно закрыть все процессы группы; оставшиеся через 3 с - завершить.
        public static Task<string> CloseGroupAsync(string key, string display)
        {
            return Task.Run(() =>
            {
                var procs = UserProcesses(key);
                if (procs.Count == 0) return display + L.T(": процессы не найдены.");
                long before = 0;
                foreach (var p in procs) { long b = Native.PrivateWorkingSet(p.Id); if (b > 0) before += b; }

                foreach (var p in procs)
                {
                    try { if (p.MainWindowHandle != IntPtr.Zero) p.CloseMainWindow(); } catch { }
                }
                Thread.Sleep(3000);
                int failed = 0;
                foreach (var p in procs)
                {
                    try
                    {
                        p.Refresh();
                        if (!p.HasExited) p.Kill();
                    }
                    catch { failed++; }
                    finally { p.Dispose(); }
                }
                string msg = display + L.T(" закрыт, освобождено примерно ") + Fmt.Bytes(before) + ".";
                if (failed > 0) msg += L.T(" Не удалось завершить процессов: ") + failed + L.T(" (нужны права администратора).");
                return msg;
            });
        }
    }

    // притормаживание и выгрузка программ с режимом "Притормаживать в фоне".
    class Manager
    {
        readonly Settings S;
        readonly Action<string> report;
        readonly Dictionary<int, string> lowered = new Dictionary<int, string>();
        readonly HashSet<int> failed = new HashSet<int>();
        readonly Dictionary<string, DateTime> lastActive = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, DateTime> lastTrim = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        readonly DateTime started = DateTime.Now;

        public string Foreground { get; private set; }

        public Manager(Settings s, Action<string> report) { S = s; this.report = report; }

        public bool IsLowered(string key) { return lowered.ContainsValue(key); }

        public DateTime? LastTrimmed(string key)
        {
            DateTime t;
            return lastTrim.TryGetValue(key, out t) ? (DateTime?)t : null;
        }

        // короткое описание того, что сейчас происходит с программой.
        public string StateOf(string key)
        {
            string reason = S.UntouchableReason(key);
            if (reason != null) return reason;
            if (key == Foreground) return L.T("вы в ней работаете");
            var t = LastTrimmed(key);
            if (t.HasValue && (DateTime.Now - t.Value).TotalMinutes < 30) return L.T("выгружена в ") + t.Value.ToString("HH:mm");
            if (IsLowered(key)) return L.T("притормаживается");
            return "";
        }

        public void Tick(uint memoryLoad)
        {
            Foreground = Native.ForegroundProcessName();
            if (!S.SmartBackground) { if (lowered.Count > 0) RestoreAll(); return; }

            DateTime now = DateTime.Now;
            string fg = Foreground;
            var alive = new HashSet<int>();
            var present = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

            Process[] procs = Process.GetProcesses();
            try
            {
                foreach (var p in procs)
                {
                    string name; int pid;
                    try { name = Settings.Norm(p.ProcessName); pid = p.Id; } catch { continue; }
                    if (!S.Background.Contains(name) || S.Untouchable(name)) continue;
                    alive.Add(pid);
                    List<int> list;
                    if (!present.TryGetValue(name, out list)) { list = new List<int>(); present[name] = list; }
                    list.Add(pid);

                    if (name == fg)
                    {
                        lastActive[name] = now;
                        if (lowered.Remove(pid)) Native.RestoreProcess(pid);
                        continue;
                    }
                    if (lowered.ContainsKey(pid) || failed.Contains(pid)) continue;
                    if (Native.LowerProcess(pid)) lowered[pid] = name; else failed.Add(pid);
                }
            }
            finally { foreach (var p in procs) p.Dispose(); }

            foreach (var dead in lowered.Keys.Where(k => !alive.Contains(k)).ToList()) lowered.Remove(dead);
            failed.IntersectWith(alive);

            if (memoryLoad < S.TrimThreshold) return;

            foreach (var kv in present)
            {
                string name = kv.Key;
                if (name == fg) continue;
                DateTime active;
                if (!lastActive.TryGetValue(name, out active)) active = started;
                if ((now - active).TotalMinutes < S.IdleMinutes) continue;
                DateTime trimmed;
                if (lastTrim.TryGetValue(name, out trimmed) && (now - trimmed).TotalMinutes < Math.Max(10, S.IdleMinutes)) continue;

                long before = 0, after = 0;
                foreach (int pid in kv.Value) { long b = Native.PrivateWorkingSet(pid); if (b > 0) before += b; }
                foreach (int pid in kv.Value) Native.TrimProcess(pid);
                foreach (int pid in kv.Value) { long b = Native.PrivateWorkingSet(pid); if (b > 0) after += b; }
                lastTrim[name] = now;
                if (before - after > 5 * 1024 * 1024)
                    report(L.T("Выгружена простаивающая ") + name + ": " + Fmt.Bytes(before) + " → " + Fmt.Bytes(after));
            }
        }

        public void RestoreAll()
        {
            foreach (int pid in lowered.Keys) Native.RestoreProcess(pid);
            lowered.Clear();
            failed.Clear();
        }
    }
}
