using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Roomy
{
    // история: загрузка памяти каждые 5 с и размер программ каждые ~30 с, за 10 минут.
    class History
    {
        public readonly List<float> Load = new List<float>();
        public const int LoadPoints = 120;
        readonly Dictionary<string, Queue<KeyValuePair<DateTime, long>>> apps =
            new Dictionary<string, Queue<KeyValuePair<DateTime, long>>>(StringComparer.OrdinalIgnoreCase);
        DateTime lastAppSample = DateTime.MinValue;

        public void AddLoad(float v)
        {
            Load.Add(v);
            if (Load.Count > LoadPoints) Load.RemoveAt(0);
        }

        public void AddSnapshot(List<AppGroup> groups)
        {
            DateTime now = DateTime.Now;
            if ((now - lastAppSample).TotalSeconds < 25) return;
            lastAppSample = now;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in groups)
            {
                seen.Add(g.Key);
                Queue<KeyValuePair<DateTime, long>> q;
                if (!apps.TryGetValue(g.Key, out q)) { q = new Queue<KeyValuePair<DateTime, long>>(); apps[g.Key] = q; }
                q.Enqueue(new KeyValuePair<DateTime, long>(now, g.Bytes));
                while (q.Count > 0 && (now - q.Peek().Key).TotalMinutes > 10.5) q.Dequeue();
            }
            foreach (var k in apps.Keys.Where(k => !seen.Contains(k)).ToList()) apps.Remove(k);
        }

        // насколько выросла программа за последние ~10 минут (null - мало данных).
        public long? Growth(string key, long current)
        {
            Queue<KeyValuePair<DateTime, long>> q;
            if (!apps.TryGetValue(key, out q) || q.Count == 0) return null;
            var oldest = q.Peek();
            if ((DateTime.Now - oldest.Key).TotalMinutes < 3) return null;
            return current - oldest.Value;
        }

        public int MinutesCovered
        {
            get
            {
                if (apps.Count == 0) return 0;
                var q = apps.Values.First();
                return q.Count == 0 ? 0 : (int)(DateTime.Now - q.Peek().Key).TotalMinutes;
            }
        }
    }

    class Status
    {
        public string Title;
        public string Hint;
        public Color Color;
    }

    // общее состояние программы: настройки, управление фоном, история, режим экономии.
    class Controller
    {
        public static readonly Color Green = Color.FromArgb(34, 150, 84);
        public static readonly Color Orange = Color.FromArgb(214, 128, 10);
        public static readonly Color Red = Color.FromArgb(208, 52, 52);

        public readonly Settings S;
        public readonly Manager M;
        public readonly History H = new History();

        public Native.MEMORYSTATUSEX Mem;
        public Native.PERFORMANCE_INFORMATION Perf;
        public float PagesIn = -1;
        readonly Queue<float> pagesWindow = new Queue<float>();
        public List<AppGroup> Groups = new List<AppGroup>();
        public readonly List<string> Actions = new List<string>();

        public bool EconomyOn { get; private set; }
        readonly Dictionary<string, string> economyClosed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        PerformanceCounter pagesCounter;
        DateTime lastGroups = DateTime.MinValue;

        public event Action Updated;

        public Controller()
        {
            S = Settings.Load();
            M = new Manager(S, AddAction);
            Task.Run(() => InitCounter());
        }

        void InitCounter()
        {
            string[][] names = {
                new[] { "Memory", "Pages Input/sec" },
                new[] { "Память", "Ввод страниц/с" }
            };
            foreach (var n in names)
            {
                try
                {
                    var c = new PerformanceCounter(n[0], n[1], true);
                    c.NextValue();
                    pagesCounter = c;
                    return;
                }
                catch { }
            }
        }

        public void AddAction(string s)
        {
            Actions.Insert(0, DateTime.Now.ToString("HH:mm") + "  " + s);
            if (Actions.Count > 30) Actions.RemoveAt(Actions.Count - 1);
            Log.Write(s);
        }

        public void Tick()
        {
            if (S.FileChanged())
            {
                M.RestoreAll();
                var f = Settings.Load();
                S.AlertThreshold = f.AlertThreshold; S.AlertCooldownMinutes = f.AlertCooldownMinutes;
                S.TrimThreshold = f.TrimThreshold; S.IdleMinutes = f.IdleMinutes;
                S.SmartBackground = f.SmartBackground; S.Notifications = f.Notifications; S.Detailed = f.Detailed;
                S.Background = f.Background; S.Economy = f.Economy; S.Protected = f.Protected;
                S.LoadedMtime = f.LoadedMtime;
                AddAction(L.T("Настройки перечитаны из файла"));
            }

            Mem = Native.MemoryStatus();
            Perf = Native.PerformanceInfo();
            var pc = pagesCounter;
            if (pc != null)
            {
                try
                {
                    pagesWindow.Enqueue(pc.NextValue());
                    while (pagesWindow.Count > 6) pagesWindow.Dequeue(); // среднее за ~30 секунд
                    PagesIn = pagesWindow.Average();
                }
                catch { PagesIn = -1; }
            }
            H.AddLoad(Mem.dwMemoryLoad);
            M.Tick(Mem.dwMemoryLoad);
            if ((DateTime.Now - lastGroups).TotalSeconds >= 30) RefreshGroups();
            var u = Updated;
            if (u != null) u();
        }

        public List<AppGroup> RefreshGroups()
        {
            Groups = MemInfo.Snapshot();
            var services = Settings.Set();
            foreach (var g in Groups) if (g.ServiceOnly) services.Add(g.Key);
            S.ServiceKeys = services;
            H.AddSnapshot(Groups);
            lastGroups = DateTime.Now;
            return Groups;
        }

        public long UsedBytes { get { return (long)(Mem.ullTotalPhys - Mem.ullAvailPhys); } }

        public Status GetStatus()
        {
            uint load = Mem.dwMemoryLoad;
            if (load < 70)
                return new Status { Title = L.T("Всё в порядке"), Hint = L.T("Памяти хватает."), Color = Green };
            if (load < S.AlertThreshold)
                return new Status { Title = L.T("Памяти впритык"), Hint = L.T("Пока работает нормально, но запаса почти нет."), Color = Orange };
            if (PagesIn >= 300 && Mem.dwMemoryLoad >= S.AlertThreshold)
                return new Status { Title = L.T("Не хватает памяти"), Hint = L.T("Windows активно подгружает данные с диска — отсюда тормоза."), Color = Red };
            return new Status { Title = L.T("Памяти мало"), Hint = L.T("Возможны подтормаживания при переключении программ."), Color = Red };
        }

        // подкачка мешает, только когда памяти мало: при свободной памяти это обычное чтение файлов.
        public bool PagingHurts { get { return PagesIn >= 300 && Mem.dwMemoryLoad >= S.AlertThreshold; } }

        public string PagesInText()
        {
            if (PagesIn < 0) return L.T("нет данных");
            string level;
            if (PagesIn < 50) level = L.T("почти нет");
            else if (Mem.dwMemoryLoad < S.AlertThreshold) level = L.T("чтение файлов, не страшно");
            else level = PagesIn < 300 ? L.T("умеренно") : L.T("много — это тормозит");
            return ((int)PagesIn) + L.T(" стр/с · ") + level;
        }

        public void SetMode(string key, string display, int mode)
        {
            if (S.ModeOf(key) == mode) return;
            S.SetMode(key, mode);
            M.RestoreAll();
            AddAction(Fmt.ShortName(display) + ": " + Settings.ModeNames[mode].ToLowerInvariant());
            var u = Updated;
            if (u != null) u();
        }

        // ---------- Режим экономии ----------

        // запущенные программы с режимом "Можно закрывать".
        public List<string> EconomyCandidates()
        {
            var res = new List<string>();
            foreach (var key in S.Economy.OrderBy(k => k))
            {
                if (S.Untouchable(key)) continue;
                var ps = MemInfo.UserProcesses(key);
                if (ps.Count > 0) res.Add(key);
                foreach (var p in ps) p.Dispose();
            }
            return res;
        }

        public string EnterEconomy()
        {
            var keys = EconomyCandidates();
            if (keys.Count == 0) return L.T("Нечего закрывать: нет запущенных программ с режимом «Можно закрывать».");
            economyClosed.Clear();
            long freed = 0;
            int failed = 0;
            foreach (var key in keys)
            {
                var ps = MemInfo.UserProcesses(key);
                string path = null;
                foreach (var p in ps)
                {
                    if (path == null) path = Native.ImagePath(p.Id);
                    long b = Native.PrivateWorkingSet(p.Id);
                    if (b > 0) freed += b;
                }
                if (path != null && !key.EndsWith("webhelper")) economyClosed[key] = path;
                foreach (var p in ps)
                {
                    try { if (!p.HasExited) p.Kill(); } catch { failed++; }
                    finally { p.Dispose(); }
                }
            }
            EconomyOn = true;
            string msg = L.T("Режим экономии: закрыто ") + string.Join(", ", keys) + L.T(", освобождено ~") + Fmt.Bytes(freed) +
                         (failed > 0 ? L.T(". Часть процессов закрыть не удалось") : "");
            AddAction(msg);
            return msg;
        }

        public string ExitEconomy()
        {
            var started = new List<string>();
            foreach (var kv in economyClosed)
            {
                try
                {
                    var running = MemInfo.UserProcesses(kv.Key);
                    bool already = running.Count > 0;
                    foreach (var p in running) p.Dispose();
                    if (already) continue;
                    Process.Start(new ProcessStartInfo(kv.Value)
                    {
                        UseShellExecute = true,
                        WorkingDirectory = Path.GetDirectoryName(kv.Value)
                    });
                    started.Add(kv.Key);
                }
                catch (Exception ex) { AddAction(L.T("Не удалось запустить ") + kv.Key + ": " + ex.Message); }
            }
            economyClosed.Clear();
            EconomyOn = false;
            string msg = started.Count > 0 ? L.T("Режим экономии выключен, запускаю: ") + string.Join(", ", started)
                                           : L.T("Режим экономии выключен");
            AddAction(msg);
            return msg;
        }

        public void Shutdown()
        {
            M.RestoreAll();
            Log.Write("Roomy закрыт, приоритеты программ возвращены");
        }
    }
}
