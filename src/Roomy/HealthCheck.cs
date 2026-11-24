using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Roomy
{
    enum Sev { Critical = 0, Useful = 1, Info = 2, Ok = 3 }

    class CheckResult
    {
        public Sev Severity;
        public string Title;
        public string Text;
        public string ActionText;   // текст кнопки (null - без кнопки)
        public string ActionTarget; // ms-settings:..., путь к программе и т. п.

        public static CheckResult Make(Sev s, string title, string text, string actionText = null, string target = null)
        {
            return new CheckResult { Severity = s, Title = title, Text = text, ActionText = actionText, ActionTarget = target };
        }
    }

    // "Проверка компьютера": только читает состояние системы и объясняет, что можно улучшить.
    // Ничего не меняет.
    static class HealthCheck
    {
        [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
        [DllImport("powrprof.dll")] static extern uint PowerGetEffectiveOverlayScheme(out Guid overlay);

        static readonly Guid OverlayBestEfficiency = new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a");
        static readonly Guid OverlayBestPerformance = new Guid("ded574b5-45a0-4f42-8737-46345c09c238");

        public static List<CheckResult> Run(Settings s)
        {
            var mem = Native.MemoryStatus();
            List<AppGroup> groups = null;
            try { groups = MemInfo.Snapshot(); } catch (Exception ex) { Log.Write("Проверка: снимок процессов: " + ex.Message); }
            if (groups == null) groups = new List<AppGroup>();
            var byKey = groups.ToDictionary(g => g.Key, g => g, StringComparer.OrdinalIgnoreCase);

            var list = new List<CheckResult>();
            Try(list, L.T("память"), () => CheckRam(mem));
            Try(list, L.T("диск"), () => CheckDisk());
            Try(list, L.T("подкачка"), () => CheckPagefile());
            Try(list, L.T("сжатие памяти"), () => CheckCompression(byKey));
            Try(list, L.T("автозагрузка"), () => CheckStartup());
            Try(list, L.T("питание"), () => CheckPower());
            Try(list, L.T("перезагрузка"), () => CheckUptime());
            Try(list, L.T("браузер"), () => CheckBrowser(byKey, (long)mem.ullTotalPhys));
            Try(list, L.T("фоновые помощники"), () => CheckHelpers(byKey, s));
            Try(list, L.T("изоляция ядра"), () => CheckMemoryIntegrity());
            return list.OrderBy(r => r.Severity).ToList();
        }

        static void Try(List<CheckResult> list, string name, Func<IEnumerable<CheckResult>> check)
        {
            try
            {
                var r = check();
                if (r != null) list.AddRange(r.Where(x => x != null));
            }
            catch (Exception ex) { Log.Write("Проверка «" + name + "» не удалась: " + ex.Message); }
        }

        static IEnumerable<CheckResult> One(CheckResult r) { return r == null ? new CheckResult[0] : new[] { r }; }

        static double GB(double bytes) { return bytes / 1024.0 / 1024.0 / 1024.0; }

        static string G(double gb)
        {
            return gb.ToString(gb >= 10 ? "0" : "0.#", L.Culture) + L.T(" ГБ");
        }

        static ManagementObjectCollection Query(string scope, string wql)
        {
            var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(wql));
            searcher.Options.Timeout = TimeSpan.FromSeconds(10);
            return searcher.Get();
        }

        static object Prop(ManagementBaseObject o, string name)
        {
            try { return o[name]; } catch { return null; }
        }

        // ---------- Память ----------

        static IEnumerable<CheckResult> CheckRam(Native.MEMORYSTATUSEX mem)
        {
            double total = GB(mem.ullTotalPhys);
            int modules = 0, slots = 0, smType = 0, formFactor = 0, speed = 0;
            double installed = 0;
            try
            {
                foreach (ManagementObject m in Query(@"root\cimv2", "SELECT Capacity, SMBIOSMemoryType, FormFactor, Speed, ConfiguredClockSpeed FROM Win32_PhysicalMemory"))
                {
                    modules++;
                    installed += Convert.ToDouble(Prop(m, "Capacity") ?? 0);
                    smType = Convert.ToInt32(Prop(m, "SMBIOSMemoryType") ?? 0);
                    formFactor = Convert.ToInt32(Prop(m, "FormFactor") ?? 0);
                    int sp = Convert.ToInt32(Prop(m, "ConfiguredClockSpeed") ?? 0);
                    if (sp == 0) sp = Convert.ToInt32(Prop(m, "Speed") ?? 0);
                    speed = Math.Max(speed, sp);
                }
                foreach (ManagementObject a in Query(@"root\cimv2", "SELECT MemoryDevices FROM Win32_PhysicalMemoryArray"))
                    slots += Convert.ToInt32(Prop(a, "MemoryDevices") ?? 0);
            }
            catch (Exception ex) { Log.Write("Проверка памяти (WMI): " + ex.Message); }

            string typeName = MemoryTypeName(smType);
            bool lp = smType == 29 || smType == 30 || smType == 35;
            string details = (installed > 0 ? G(GB(installed)) : G(total)) +
                             (typeName != null ? " " + typeName : "") + (speed > 0 ? ", " + speed + L.T(" МТ/с") : "");

            if (total >= 15)
                return One(CheckResult.Make(Sev.Ok, L.T("Оперативной памяти достаточно"),
                    L.T("Установлено ") + details + L.T(". Для обычной работы и большинства игр этого хватает.")));

            Sev upgradeSev = total <= 4.5 ? Sev.Critical : Sev.Useful;

            if (lp)
                return One(CheckResult.Make(Sev.Info, L.T("Память распаяна на плате — добавить нельзя"),
                    L.T("Установлено ") + details + L.T(". Память типа LPDDR припаяна к плате, слотов для дополнительных планок нет. ") +
                    L.T("Поэтому на этом компьютере особенно важно не держать в памяти лишнее: автозагрузка, вкладки браузера, фоновые программы.")));

            if (slots > modules && modules > 0)
                return One(CheckResult.Make(upgradeSev, L.T("Можно добавить оперативную память"),
                    L.T("Установлено ") + details + L.T(", занято слотов: ") + modules + L.T(" из ") + slots + ". " +
                    L.T("Добавить ещё одну планку того же типа — самое заметное улучшение для этого компьютера: меньше тормозов при переключении программ и в играх. ") +
                    L.T("Перед покупкой уточните по точной модели компьютера, какая память подходит: данные из BIOS иногда неточны.")));

            if (modules > 0 && slots > 0)
                return One(CheckResult.Make(Sev.Info, L.T("Все слоты памяти заняты"),
                    L.T("Установлено ") + details + L.T(", занято слотов: ") + modules + L.T(" из ") + slots + ". " +
                    L.T("Увеличить память можно только заменой планок на более ёмкие — уточните по модели компьютера, сколько он поддерживает.")));

            return One(CheckResult.Make(Sev.Info, L.T("Оперативная память: ") + G(total),
                L.T("Не удалось определить, можно ли добавить память. Посмотрите характеристики по точной модели компьютера.")));
        }

        static string MemoryTypeName(int t)
        {
            switch (t)
            {
                case 24: return "DDR3";
                case 26: return "DDR4";
                case 29: return "LPDDR3";
                case 30: return "LPDDR4";
                case 34: return "DDR5";
                case 35: return "LPDDR5";
                default: return null;
            }
        }

        // ---------- Диск ----------

        static IEnumerable<CheckResult> CheckDisk()
        {
            var res = new List<CheckResult>();
            string root = Path.GetPathRoot(Environment.SystemDirectory);
            var drive = new DriveInfo(root);
            double free = GB(drive.AvailableFreeSpace), size = GB(drive.TotalSize);
            string letter = root.Substring(0, 1).ToUpperInvariant();

            if (free < 10 || free / size < 0.08)
                res.Add(CheckResult.Make(free < 5 ? Sev.Critical : Sev.Useful, L.T("На диске ") + letter + L.T(": мало места"),
                    L.T("Свободно ") + G(free) + L.T(" из ") + G(size) + L.T(". Windows нужно место для файла подкачки и обновлений — когда его мало, ") +
                    L.T("компьютер начинает тормозить. Освободите хотя бы 15–20 ГБ: откройте в Roomy «Инструменты → Место на диске» — там безопасная очистка и список того, что занимает больше всего."),
                    L.T("Открыть «Память»"), "ms-settings:storagesense"));
            else
                res.Add(CheckResult.Make(Sev.Ok, L.T("На диске ") + letter + L.T(": достаточно места"), L.T("Свободно ") + G(free) + L.T(" из ") + G(size) + "."));

            // Тип диска: HDD или SSD
            try
            {
                string diskNumber = null;
                foreach (ManagementObject p in Query(@"root\Microsoft\Windows\Storage", "SELECT DiskNumber, DriveLetter FROM MSFT_Partition"))
                {
                    var dl = Prop(p, "DriveLetter");
                    if (dl != null && Convert.ToChar(dl).ToString().ToUpperInvariant() == letter)
                    {
                        diskNumber = Convert.ToString(Prop(p, "DiskNumber"));
                        break;
                    }
                }
                if (diskNumber != null)
                {
                    foreach (ManagementObject d in Query(@"root\Microsoft\Windows\Storage", "SELECT DeviceId, MediaType, BusType, FriendlyName FROM MSFT_PhysicalDisk"))
                    {
                        if (Convert.ToString(Prop(d, "DeviceId")) != diskNumber) continue;
                        int media = Convert.ToInt32(Prop(d, "MediaType") ?? 0);
                        int bus = Convert.ToInt32(Prop(d, "BusType") ?? 0);
                        string name = Convert.ToString(Prop(d, "FriendlyName") ?? "").Trim();
                        if (media == 3)
                            res.Add(CheckResult.Make(Sev.Critical, L.T("Windows стоит на жёстком диске (HDD)"),
                                L.T("Системный диск") + (name.Length > 0 ? " «" + name + "»" : "") + L.T(" — механический жёсткий диск. Это самое медленное место ") +
                                L.T("в компьютере: Windows и программы запускаются долго, а когда не хватает памяти, всё упирается в диск. ") +
                                L.T("Замена на SSD — лучшее вложение для слабого компьютера: загрузка и работа станут быстрее в разы. ") +
                                L.T("Уточните по модели, какой SSD подходит (2,5\" SATA или M.2).")));
                        else if (media == 4)
                            res.Add(CheckResult.Make(Sev.Ok, L.T("Windows стоит на SSD") + (bus == 17 ? " (NVMe)" : ""),
                                L.T("Системный диск") + (name.Length > 0 ? " «" + name + "»" : "") + L.T(" — быстрый твердотельный накопитель.")));
                        break;
                    }
                }
            }
            catch (Exception ex) { Log.Write("Проверка типа диска: " + ex.Message); }
            return res;
        }

        // ---------- Файл подкачки ----------

        static IEnumerable<CheckResult> CheckPagefile()
        {
            bool automatic = false;
            foreach (ManagementObject cs in Query(@"root\cimv2", "SELECT AutomaticManagedPagefile FROM Win32_ComputerSystem"))
                automatic = Convert.ToBoolean(Prop(cs, "AutomaticManagedPagefile") ?? false);

            double allocatedMb = 0, usedMb = 0;
            foreach (ManagementObject u in Query(@"root\cimv2", "SELECT AllocatedBaseSize, CurrentUsage FROM Win32_PageFileUsage"))
            {
                allocatedMb += Convert.ToDouble(Prop(u, "AllocatedBaseSize") ?? 0);
                usedMb += Convert.ToDouble(Prop(u, "CurrentUsage") ?? 0);
            }
            string now = allocatedMb > 0 ? L.T(" Сейчас его размер ") + G(allocatedMb / 1024) + L.T(", занято ") + G(usedMb / 1024) + "." : "";
            const string target = @"C:\Windows\System32\SystemPropertiesPerformance.exe";

            if (automatic)
                return One(CheckResult.Make(Sev.Ok, L.T("Файл подкачки настроен правильно"),
                    L.T("Размер выбирает сама Windows — так и должно быть.") + now));

            double maxMb = 0;
            int settings = 0;
            foreach (ManagementObject p in Query(@"root\cimv2", "SELECT InitialSize, MaximumSize FROM Win32_PageFileSetting"))
            {
                settings++;
                double mx = Convert.ToDouble(Prop(p, "MaximumSize") ?? 0);
                if (mx == 0) mx = double.MaxValue; // "размер по выбору системы" для этого диска
                maxMb = Math.Max(maxMb, mx);
            }

            if (settings == 0 && allocatedMb == 0)
                return One(CheckResult.Make(Sev.Critical, L.T("Файл подкачки отключён"),
                    L.T("Без файла подкачки, когда оперативная память заканчивается, программы и игры просто вылетают с ошибкой нехватки памяти. ") +
                    L.T("Включите его: «Дополнительно» → «Виртуальная память» → «Изменить» → галочка «Автоматически выбирать объём файла подкачки»."),
                    L.T("Открыть настройки быстродействия"), target));

            if (maxMb < 4096)
                return One(CheckResult.Make(Sev.Useful, L.T("Файл подкачки ограничен вручную"),
                    L.T("Максимальный размер задан вручную: ") + G(maxMb / 1024) + L.T(". Этого может не хватить, и программы будут вылетать. ") +
                    L.T("Лучше вернуть автоматический размер: «Дополнительно» → «Виртуальная память» → «Изменить» → «Автоматически выбирать объём»."),
                    L.T("Открыть настройки быстродействия"), target));

            return One(CheckResult.Make(Sev.Info, L.T("Файл подкачки задан вручную"),
                L.T("Размер настроен вручную, но достаточный.") + now + L.T(" Обычно лучше доверить его Windows, но так тоже можно работать."),
                L.T("Открыть настройки быстродействия"), target));
        }

        // ---------- Сжатие памяти ----------

        static IEnumerable<CheckResult> CheckCompression(Dictionary<string, AppGroup> groups)
        {
            if (groups.Count == 0) return null;
            if (groups.ContainsKey("memory compression"))
                return One(CheckResult.Make(Sev.Ok, L.T("Сжатие памяти включено"),
                    L.T("Windows сжимает редко используемые данные прямо в оперативной памяти вместо того, чтобы выгружать их на диск. Это быстрее подкачки.")));
            return One(CheckResult.Make(Sev.Useful, L.T("Сжатие памяти, похоже, выключено"),
                L.T("Обычно оно включено, но некоторые «оптимизаторы» его отключают. Со сжатием Windows реже обращается к диску, когда памяти мало. ") +
                L.T("Включить: откройте PowerShell от имени администратора, введите  Enable-MMAgent -MemoryCompression  и перезагрузите компьютер.")));
        }

        // ---------- Автозагрузка ----------

        static IEnumerable<CheckResult> CheckStartup()
        {
            var names = new List<string>();
            var keys = new[]
            {
                new { Hive = Registry.CurrentUser, Path = @"Software\Microsoft\Windows\CurrentVersion\Run", Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run" },
                new { Hive = Registry.LocalMachine, Path = @"Software\Microsoft\Windows\CurrentVersion\Run", Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run" },
                new { Hive = Registry.LocalMachine, Path = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32" },
            };
            foreach (var k in keys)
            {
                using (var run = k.Hive.OpenSubKey(k.Path, false))
                {
                    if (run == null) continue;
                    foreach (var v in run.GetValueNames())
                    {
                        if (string.IsNullOrWhiteSpace(v)) continue;
                        if (IsDisabled(k.Hive, k.Approved, v)) continue;
                        names.Add(v);
                    }
                }
            }
            foreach (var folder in new[] { Environment.SpecialFolder.Startup, Environment.SpecialFolder.CommonStartup })
            {
                string dir = Environment.GetFolderPath(folder);
                if (!Directory.Exists(dir)) continue;
                var hive = folder == Environment.SpecialFolder.Startup ? Registry.CurrentUser : Registry.LocalMachine;
                foreach (var f in Directory.GetFiles(dir))
                {
                    string fn = Path.GetFileName(f);
                    if (fn.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    if (IsDisabled(hive, @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder", fn)) continue;
                    names.Add(Path.GetFileNameWithoutExtension(f));
                }
            }
            names = names.Where(n => n.IndexOf("roomy", StringComparison.OrdinalIgnoreCase) < 0)
                         .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();

            const string target = "ms-settings:startupapps";
            if (names.Count == 0)
                return One(CheckResult.Make(Sev.Ok, L.T("Автозагрузка чистая"), L.T("Вместе с Windows не запускается ничего лишнего.")));
            string list = string.Join(", ", names.Take(15)) + (names.Count > 15 ? L.T(" и ещё ") + (names.Count - 15) : "");
            Sev sev = names.Count >= 8 ? Sev.Useful : names.Count >= 4 ? Sev.Info : Sev.Ok;
            string title = sev == Sev.Ok ? L.T("Автозагрузка в порядке") : L.T("Вместе с Windows запускается программ: ") + names.Count;
            return One(CheckResult.Make(sev, title,
                L.T("Запускаются сами: ") + list + L.T(". Каждая из них занимает память весь день, даже если вы ей не пользуетесь. ") +
                L.T("Отключите те, что не нужны сразу после включения — сами программы никуда не денутся, их можно открыть вручную."),
                L.T("Открыть автозагрузку"), target));
        }

        // отключено ли в "Автозагрузке" Windows: первый байт значения StartupApproved нечётный.
        static bool IsDisabled(RegistryKey hive, string approvedPath, string name)
        {
            try
            {
                using (var k = hive.OpenSubKey(approvedPath, false))
                {
                    var data = k == null ? null : k.GetValue(name) as byte[];
                    return data != null && data.Length > 0 && (data[0] & 1) == 1;
                }
            }
            catch { return false; }
        }

        // ---------- Питание ----------

        static IEnumerable<CheckResult> CheckPower()
        {
            var ps = SystemInformation.PowerStatus;
            bool laptop = (ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) == 0 &&
                          ps.BatteryChargeStatus != BatteryChargeStatus.Unknown;
            Guid overlay;
            if (PowerGetEffectiveOverlayScheme(out overlay) != 0) return null;
            string mode = overlay == OverlayBestPerformance ? L.T("«Максимальная производительность»") :
                          overlay == OverlayBestEfficiency ? L.T("«Максимальная эффективность»") : L.T("«Сбалансированный»");
            const string target = "ms-settings:powersleep";

            if (overlay == OverlayBestPerformance)
                return One(CheckResult.Make(Sev.Ok, L.T("Режим питания: максимальная производительность"), L.T("Процессор работает в полную силу.")));

            if (laptop && ps.PowerLineStatus == PowerLineStatus.Online)
                return One(CheckResult.Make(Sev.Useful, L.T("На зарядке можно включить максимальную производительность"),
                    L.T("Сейчас режим питания ") + mode + L.T(". Когда ноутбук подключён к зарядке, режим «Максимальная производительность» ") +
                    L.T("заметно ускоряет слабый процессор — особенно в играх и при запуске программ. От батареи Windows может держать отдельный режим. ") +
                    L.T("Параметры → Система → Питание и батарея → Режим питания."),
                    L.T("Открыть настройки питания"), target));

            if (laptop)
                return One(CheckResult.Make(Sev.Info, L.T("Режим питания: ") + mode.Trim('«', '»').ToLowerInvariant(),
                    L.T("Ноутбук работает от батареи — экономный режим здесь уместен. На зарядке для скорости можно включить «Максимальная производительность»."),
                    L.T("Открыть настройки питания"), target));

            return One(CheckResult.Make(Sev.Useful, L.T("Можно включить максимальную производительность"),
                L.T("Сейчас режим питания ") + mode + L.T(". На настольном компьютере режим «Максимальная производительность» даёт больше скорости почти без минусов."),
                L.T("Открыть настройки питания"), target));
        }

        // ---------- Перезагрузка ----------

        static IEnumerable<CheckResult> CheckUptime()
        {
            double days = GetTickCount64() / 1000.0 / 3600.0 / 24.0;
            int d = (int)Math.Floor(days);
            if (days >= 7)
                return One(CheckResult.Make(Sev.Useful, L.T("Компьютер не перезагружался ") + Days(d),
                    L.T("За долгую работу программы и драйверы «расползаются» по памяти, и система медленнее. Перезагрузка это сбрасывает. ") +
                    L.T("Важно: «Завершение работы» в Windows по умолчанию не очищает систему полностью (быстрый запуск) — нужна именно «Перезагрузка».")));
            if (days >= 3)
                return One(CheckResult.Make(Sev.Info, L.T("Компьютер работает без перезагрузки ") + Days(d),
                    L.T("Пока не критично. Если станет тормозить — сначала сделайте «Перезагрузку» (не «Завершение работы»).")));
            return One(CheckResult.Make(Sev.Ok, L.T("Недавняя перезагрузка"),
                d == 0 ? L.T("Компьютер перезагружался сегодня.") : L.T("Компьютер перезагружался ") + Days(d) + L.T(" назад.")));
        }

        static string Days(int n)
        {
            if (L.En) return n + (n == 1 ? " day" : " days");
            int m10 = n % 10, m100 = n % 100;
            string w = (m10 == 1 && m100 != 11) ? L.T("день") : (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) ? L.T("дня") : L.T("дней");
            return n + " " + w;
        }

        // ---------- Браузер ----------

        static readonly Dictionary<string, string> Browsers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "firefox", "Firefox" }, { "chrome", "Chrome" }, { "msedge", "Edge" }, { "opera", "Opera" },
            { "browser", L.T("Яндекс Браузер") }, { "vivaldi", "Vivaldi" }, { "brave", "Brave" }
        };

        static IEnumerable<CheckResult> CheckBrowser(Dictionary<string, AppGroup> groups, long totalPhys)
        {
            AppGroup top = null;
            string name = null;
            foreach (var b in Browsers)
            {
                AppGroup g;
                if (groups.TryGetValue(b.Key, out g) && !g.ServiceOnly && (top == null || g.Bytes > top.Bytes)) { top = g; name = b.Value; }
            }
            if (top == null || top.Bytes < 500L * 1024 * 1024) return null;

            string tip;
            switch (top.Key)
            {
                case "chrome": tip = L.T("В Chrome включите «Экономию памяти»: Настройки → Производительность. Или поставьте расширение Roomy Tabs."); break;
                case "msedge": tip = L.T("В Edge включите «Спящие вкладки»: Настройки → Система и производительность. Или поставьте расширение Roomy Tabs."); break;
                default: tip = L.T("Поставьте расширение Roomy Tabs — оно усыпляет давно не открытые вкладки (скачать можно на странице Roomy на GitHub)."); break;
            }
            double share = totalPhys > 0 ? (double)top.Bytes / totalPhys : 0;
            return One(CheckResult.Make(share >= 0.15 ? Sev.Useful : Sev.Info,
                name + L.T(" занимает ") + Fmt.Bytes(top.Bytes),
                L.T("Это ") + Math.Round(share * 100) + L.T("% всей памяти (") + Fmt.Processes(top.Pids.Count) + L.T("). Каждая открытая вкладка держит память, ") +
                L.T("даже если вы не открывали её несколько часов. ") + tip));
        }

        // ---------- Фоновые помощники ----------

        class Helper
        {
            public string[] Keys;
            public string Name;
            public string How;
        }

        static readonly Helper[] Helpers =
        {
            new Helper { Keys = new[] { "widgets", "widgetservice", "widgetboard" }, Name = L.T("Виджеты Windows"),
                How = L.T("Параметры → Персонализация → Панель задач → «Мини-приложения»: выкл.") },
            new Helper { Keys = new[] { "phoneexperiencehost", "yourphone", "phonelink" }, Name = L.T("Связь с телефоном"),
                How = L.T("Параметры → Bluetooth и устройства → Мобильные устройства: выкл., если не пользуетесь.") },
            new Helper { Keys = new[] { "ms-teams", "msteams", "teams" }, Name = "Microsoft Teams",
                How = L.T("уберите из автозагрузки, если не пользуетесь каждый день.") },
            new Helper { Keys = new[] { "onedrive" }, Name = "OneDrive",
                How = L.T("если не храните файлы в OneDrive — закройте и уберите из автозагрузки.") },
            new Helper { Keys = new[] { "gamebar", "gamebarftserver", "gamebarpresencewriter" }, Name = "Xbox Game Bar",
                How = L.T("Параметры → Игры → Game Bar: выкл., если не записываете игры.") },
            new Helper { Keys = new[] { "copilot", "m365copilot" }, Name = "Copilot",
                How = L.T("уберите из автозагрузки, если не пользуетесь.") },
            new Helper { Keys = new[] { "memreduct", "wisememoryoptimizer", "cleanmem", "ramrush", "mz ram booster" }, Name = L.T("«Очиститель» памяти"),
                How = L.T("лучше удалить: он выталкивает данные программ на диск, и они потом медленнее работают.") },
            new Helper { Keys = new[] { "adobearm", "acrotray", "ccxprocess", "adobecollabsync", "adobeipcbroker" }, Name = L.T("Фоновые службы Adobe"),
                How = L.T("уберите из автозагрузки — Adobe-программы работают и без них.") },
            new Helper { Keys = new[] { "googledrivefs", "dropbox" }, Name = L.T("Синхронизация облака"),
                How = L.T("если не нужна постоянно — уберите из автозагрузки.") },
        };

        static IEnumerable<CheckResult> CheckHelpers(Dictionary<string, AppGroup> groups, Settings s)
        {
            if (groups.Count == 0) return null;
            var lines = new List<string>();
            long total = 0;
            foreach (var h in Helpers)
            {
                long bytes = 0;
                foreach (var k in h.Keys)
                {
                    AppGroup g;
                    if (groups.TryGetValue(k, out g) && !g.ServiceOnly) bytes += g.Bytes;
                }
                if (bytes <= 0) continue;
                total += bytes;
                lines.Add("• " + h.Name + " — " + Fmt.Bytes(bytes) + ": " + h.How);
            }
            if (lines.Count == 0)
                return One(CheckResult.Make(Sev.Ok, L.T("Лишних фоновых помощников нет"),
                    L.T("Виджеты, «Связь с телефоном», Teams, Game Bar и подобное сейчас не работают в фоне.")));
            return One(CheckResult.Make(total >= 200L * 1024 * 1024 ? Sev.Useful : Sev.Info,
                L.T("Фоновые помощники занимают ") + Fmt.Bytes(total),
                L.T("Работают в фоне, хотя нужны не всем:\n") + string.Join("\n", lines),
                L.T("Открыть автозагрузку"), "ms-settings:startupapps"));
        }

        // ---------- Изоляция ядра ----------

        static IEnumerable<CheckResult> CheckMemoryIntegrity()
        {
            using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", false))
            {
                object v = k == null ? null : k.GetValue("Enabled");
                bool on = v is int && (int)v == 1;
                if (!on) return null;
                return One(CheckResult.Make(Sev.Info, L.T("Изоляция ядра («Целостность памяти») включена"),
                    L.T("Она защищает от вредоносных драйверов, но на слабых компьютерах отнимает несколько процентов производительности, заметнее всего в играх. ") +
                    L.T("Выключать стоит, только если не хватает скорости в играх и вы понимаете, что защита станет слабее. Решать вам — по умолчанию лучше оставить."),
                    L.T("Открыть «Изоляцию ядра»"), "windowsdefender://coreisolation"));
            }
        }
    }
}
