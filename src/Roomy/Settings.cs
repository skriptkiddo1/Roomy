using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Roomy
{
    class Settings
    {
        public const int CurrentConfigVersion = 2;

        public const int ModeNone = 0;        // Не трогать
        public const int ModeBackground = 1;  // Притормаживать в фоне
        public const int ModeClosable = 2;    // Можно закрывать (и притормаживать)

        public static readonly string[] ModeNames = { L.T("Не трогать"), L.T("Притормаживать в фоне"), L.T("Можно закрывать") };

        public int AlertThreshold = 85;
        public int AlertCooldownMinutes = 10;
        public int TrimThreshold = 80;
        public int IdleMinutes = 15;
        public bool SmartBackground = true;
        public bool Notifications = true;
        public bool Detailed = false;
        // значок в трее: true - робот Roomy, false - число (процент занятой памяти).
        public bool TrayRobot = true;
        public bool TrayBlink = true;
        public string Language = "auto"; // auto | ru | en, применяется после перезапуска

        // По умолчанию пусто: Roomy ничего не трогает, пока пользователь сам не выберет режим.
        public HashSet<string> Background = Set();
        public HashSet<string> Economy = Set();

        public HashSet<string> Protected = DefaultProtected();

        // службы Windows (работают вне сеанса пользователя) - обновляется при каждом снимке.
        public HashSet<string> ServiceKeys = Set();

        // слишком общие имена: под ними прячутся разные программы (любой установщик - setup.exe).
        public static readonly HashSet<string> GenericNames = Set("setup", "install", "installer", "update", "updater",
            "uninstall", "uninstaller", "unins000", "unins001", "msiexec", "launcher", "autorun", "start", "run", "main", "app");

        static HashSet<string> DefaultProtected()
        {
            return Set("system", "idle", "registry", "memory compression", "smss", "csrss",
            "wininit", "winlogon", "services", "lsass", "svchost", "dwm", "explorer", "fontdrvhost", "sihost",
            "ctfmon", "audiodg", "searchhost", "startmenuexperiencehost", "shellexperiencehost", "textinputhost",
            "runtimebroker", "taskmgr", "msmpeng", "securityhealthservice", "secure system", "lsaiso", "roomy",
            "mpdefendercoreservice", "nissrv", "wmiprvse", "searchindexer", "spoolsv", "wudfhost", "conhost", "dllhost",
            "smartscreen", "lockapp", "winstore.app", "applicationframehost", "systemsettings", "rampilot");
        }

        public static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Roomy");
        public static readonly string FilePath = Path.Combine(Dir, "settings.ini");

        public DateTime LoadedMtime = DateTime.MinValue;

        public static HashSet<string> Set(params string[] items)
        {
            var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var i in items) { var n = Norm(i); if (n.Length > 0) s.Add(n); }
            return s;
        }

        public static string Norm(string name)
        {
            if (name == null) return "";
            name = name.Trim().ToLowerInvariant();
            if (name.EndsWith(".exe")) name = name.Substring(0, name.Length - 4);
            return name;
        }

        public int ModeOf(string key)
        {
            if (Untouchable(key)) return ModeNone;
            if (Economy.Contains(key)) return ModeClosable;
            if (Background.Contains(key)) return ModeBackground;
            return ModeNone;
        }

        public void SetMode(string key, int mode)
        {
            key = Norm(key);
            if (Untouchable(key)) return;
            Background.Remove(key);
            Economy.Remove(key);
            if (mode >= ModeBackground) Background.Add(key);
            if (mode == ModeClosable) Economy.Add(key);
            Save();
        }

        public bool Untouchable(string key) { return UntouchableReason(key) != null; }

        // почему программу нельзя трогать (null - можно).
        public string UntouchableReason(string key)
        {
            if (Protected.Contains(key) || key.StartsWith("roomy") || key.StartsWith("rampilot")) return L.T("системное");
            if (ServiceKeys.Contains(key)) return L.T("служба Windows");
            if (GenericNames.Contains(key)) return L.T("общее имя — не трогаем");
            return null;
        }

        // перенести настройки из папки старого названия программы (RamPilot), если они есть.
        static void MigrateFromRamPilot()
        {
            try
            {
                string old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RamPilot");
                string oldIni = Path.Combine(old, "settings.ini");
                if (File.Exists(FilePath) || !File.Exists(oldIni)) return;
                Directory.CreateDirectory(Dir);
                File.Copy(oldIni, FilePath);
                Log.Write("Настройки перенесены из RamPilot");
            }
            catch { }
        }

        public static Settings Load()
        {
            MigrateFromRamPilot();
            var s = new Settings();
            int version = 0;
            try
            {
                Directory.CreateDirectory(Dir);
                if (!File.Exists(FilePath)) { s.Save(); version = CurrentConfigVersion; }
                foreach (var raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = line.Substring(eq + 1).Trim();
                    switch (key)
                    {
                        case "configversion": version = Int(val, 0, 0, 1000); break;
                        case "alertthreshold": s.AlertThreshold = Int(val, s.AlertThreshold, 50, 99); break;
                        case "alertcooldownminutes": s.AlertCooldownMinutes = Int(val, s.AlertCooldownMinutes, 1, 1440); break;
                        case "trimthreshold": s.TrimThreshold = Int(val, s.TrimThreshold, 50, 99); break;
                        case "idleminutes": s.IdleMinutes = Int(val, s.IdleMinutes, 1, 1440); break;
                        case "smartbackground": s.SmartBackground = Bool(val, s.SmartBackground); break;
                        case "notifications": s.Notifications = Bool(val, s.Notifications); break;
                        case "detailed": s.Detailed = Bool(val, s.Detailed); break;
                        case "trayicon": s.TrayRobot = !val.Trim().Equals("percent", StringComparison.OrdinalIgnoreCase); break;
                        case "trayblink": s.TrayBlink = Bool(val, s.TrayBlink); break;
                        case "language": s.Language = val.Trim().ToLowerInvariant(); break;
                        case "background": s.Background = Set(val.Split(',')); break;
                        case "economy": s.Economy = Set(val.Split(',')); break;
                        case "protected": s.Protected = Set(val.Split(',')); s.Protected.UnionWith(DefaultProtected()); break;
                    }
                }
                if (version < CurrentConfigVersion)
                {
                    // Старые версии сами заполняли списки своими догадками - сбрасываем, решает пользователь.
                    s.Background.Clear();
                    s.Economy.Clear();
                    s.Save();
                }
                s.Background.UnionWith(s.Economy);
                int before = s.Background.Count + s.Economy.Count;
                foreach (var set in new[] { s.Background, s.Economy })
                {
                    set.ExceptWith(s.Protected);
                    set.ExceptWith(GenericNames);
                }
                if (s.Background.Count + s.Economy.Count != before) s.Save();
                s.LoadedMtime = File.GetLastWriteTimeUtc(FilePath);
            }
            catch (Exception ex) { Log.Write("Ошибка чтения настроек: " + ex.Message); }
            return s;
        }

        public bool FileChanged()
        {
            try { return File.Exists(FilePath) && File.GetLastWriteTimeUtc(FilePath) != LoadedMtime; }
            catch { return false; }
        }

        public void Save()
        {
            var sb = new StringBuilder();
            sb.AppendLine(L.T("# Настройки Roomy. Проще менять их в окне программы, но можно и здесь —"));
            sb.AppendLine(L.T("# после сохранения файла программа подхватит изменения сама."));
            sb.AppendLine(L.T("# Имена программ — как имя .exe без расширения (telegram, firefox...), через запятую."));
            sb.AppendLine("ConfigVersion=" + CurrentConfigVersion);
            sb.AppendLine("# Language / язык: auto, ru, en");
            sb.AppendLine("Language=" + Language);
            sb.AppendLine();
            sb.AppendLine(L.T("# Уведомление, когда память занята на столько процентов или больше, и не чаще раза в N минут"));
            sb.AppendLine("AlertThreshold=" + AlertThreshold);
            sb.AppendLine("AlertCooldownMinutes=" + AlertCooldownMinutes);
            sb.AppendLine("Notifications=" + B(Notifications));
            sb.AppendLine();
            sb.AppendLine(L.T("# Значок в трее: robot — робот Roomy (меняет настроение вместе с памятью), percent — процент занятой памяти"));
            sb.AppendLine("TrayIcon=" + (TrayRobot ? "robot" : "percent"));
            sb.AppendLine(L.T("# Робот иногда моргает (только когда значок — робот)"));
            sb.AppendLine("TrayBlink=" + B(TrayBlink));
            sb.AppendLine();
            sb.AppendLine(L.T("# «Притормаживать в фоне»: пока вы не работаете в программе, Windows в первую очередь"));
            sb.AppendLine(L.T("# забирает память у неё. Переключились на программу — всё сразу возвращается."));
            sb.AppendLine("SmartBackground=" + B(SmartBackground));
            sb.AppendLine("Background=" + string.Join(", ", Background.OrderBy(x => x)));
            sb.AppendLine();
            sb.AppendLine(L.T("# Если память занята на TrimThreshold% и больше, «притормаживаемые» программы,"));
            sb.AppendLine(L.T("# которые вы не открывали IdleMinutes минут, выгружаются из памяти (не закрываются)."));
            sb.AppendLine("TrimThreshold=" + TrimThreshold);
            sb.AppendLine("IdleMinutes=" + IdleMinutes);
            sb.AppendLine();
            sb.AppendLine(L.T("# «Можно закрывать»: их закрывает «Режим экономии» и запускает обратно при выключении режима"));
            sb.AppendLine("Economy=" + string.Join(", ", Economy.OrderBy(x => x)));
            sb.AppendLine();
            sb.AppendLine(L.T("# Эти процессы программа никогда не трогает"));
            sb.AppendLine("Protected=" + string.Join(", ", Protected.OrderBy(x => x)));
            sb.AppendLine();
            sb.AppendLine(L.T("# Подробный вид окна"));
            sb.AppendLine("Detailed=" + B(Detailed));
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(true));
                LoadedMtime = File.GetLastWriteTimeUtc(FilePath);
            }
            catch (Exception ex) { Log.Write("Ошибка сохранения настроек: " + ex.Message); }
        }

        static string B(bool v) { return v ? "true" : "false"; }

        static int Int(string v, int def, int min, int max)
        {
            int r;
            if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out r)) return def;
            return Math.Max(min, Math.Min(max, r));
        }

        static bool Bool(string v, bool def)
        {
            v = v.ToLowerInvariant();
            if (v == "true" || v == "1" || v == "yes" || v == "да") return true;
            if (v == "false" || v == "0" || v == "no" || v == "нет") return false;
            return def;
        }
    }

    static class Log
    {
        public static readonly string FilePath = Path.Combine(Settings.Dir, "log.txt");
        static readonly object Lock = new object();

        public static void Write(string msg)
        {
            lock (Lock)
            {
                try
                {
                    Directory.CreateDirectory(Settings.Dir);
                    var fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > 1024 * 1024)
                    {
                        var old = FilePath + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(FilePath, old);
                    }
                    File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine, Encoding.UTF8);
                }
                catch { }
            }
        }
    }

    static class Fmt
    {
        static CultureInfo Ru { get { return L.Culture; } }

        public static string Bytes(long b)
        {
            if (b < 0) return "—";
            double mb = b / 1024.0 / 1024.0;
            if (mb >= 1024) return (mb / 1024.0).ToString("0.0", Ru) + L.T(" ГБ");
            if (mb >= 10) return mb.ToString("0", Ru) + L.T(" МБ");
            return mb.ToString("0.0", Ru) + L.T(" МБ");
        }

        public static string Signed(long b)
        {
            if (Math.Abs(b) < 1024 * 1024) return "0";
            return (b > 0 ? "+" : "−") + Bytes(Math.Abs(b));
        }

        public static string Processes(int n)
        {
            if (L.En) return n + (n == 1 ? " process" : " processes");
            int m10 = n % 10, m100 = n % 100;
            string w = (m10 == 1 && m100 != 11) ? L.T("процесс") :
                       (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) ? L.T("процесса") : L.T("процессов");
            return n + " " + w;
        }

        public static string ShortName(string display)
        {
            int i = display.IndexOf(" (");
            return i > 0 ? display.Substring(0, i) : display;
        }
    }
}
