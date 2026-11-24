using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Roomy
{
    enum CleanMode { Delete, Recycle, EmptyBin, InfoOnly }

    // категория безопасной очистки.
    class CleanCategory
    {
        public string Title, Explain;
        public CleanMode Mode;
        public bool DefaultOn;
        public readonly List<string> Roots = new List<string>();
        public Func<FileInfo, bool> Filter;
        public bool TopLevelOnly;
        public string[] BusyProcesses = new string[0];
        public string ActionTarget; // для InfoOnly - что открыть

        public long Size;
        public int Files;
        public string Busy; // какие программы сейчас запущены и держат файлы

        public string OpenPath { get { return Roots.FirstOrDefault(Directory.Exists); } }
    }

    static class DiskClean
    {
        static string Local { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }
        static string Roaming { get { return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); } }

        // ---------- Список категорий ----------

        public static List<CleanCategory> Build()
        {
            var list = new List<CleanCategory>();
            DateTime dayAgo = DateTime.Now.AddDays(-1);

            var temp = new CleanCategory
            {
                Title = L.T("Временные файлы"),
                Explain = L.T("Остатки установщиков и программ в папке Temp старше суток. Программы создают их и часто забывают удалить."),
                Mode = CleanMode.Delete, DefaultOn = true,
                Filter = f => f.LastWriteTime < dayAgo
            };
            temp.Roots.Add(Path.GetTempPath());
            list.Add(temp);

            var browsers = new CleanCategory
            {
                Title = L.T("Кэш браузеров"),
                Explain = L.T("Сохранённые копии картинок и скриптов сайтов. Пароли, вход на сайты, история и закладки не затрагиваются — ") +
                          L.T("сайты просто первый раз откроются чуть медленнее."),
                Mode = CleanMode.Delete, DefaultOn = true,
                BusyProcesses = new[] { "chrome", "msedge", "firefox", "brave", "vivaldi", "browser", "opera" }
            };
            foreach (var ud in new[] {
                Path.Combine(Local, @"Google\Chrome\User Data"), Path.Combine(Local, @"Microsoft\Edge\User Data"),
                Path.Combine(Local, @"BraveSoftware\Brave-Browser\User Data"), Path.Combine(Local, @"Vivaldi\User Data"),
                Path.Combine(Local, @"Yandex\YandexBrowser\User Data") })
                AddChromiumProfiles(browsers, ud);
            foreach (var op in new[] { @"Opera Software\Opera Stable", @"Opera Software\Opera GX Stable" })
                AddIfExists(browsers, Path.Combine(Local, op, "Cache"));
            var ffProfiles = Path.Combine(Local, @"Mozilla\Firefox\Profiles");
            if (Directory.Exists(ffProfiles))
                foreach (var p in SafeDirs(ffProfiles)) AddIfExists(browsers, Path.Combine(p, "cache2"));
            list.Add(browsers);

            var apps = new CleanCategory
            {
                Title = L.T("Кэш программ (Discord, Steam, Spotify)"),
                Explain = L.T("Кэш встроенных браузеров и музыки. Переписка, игры и плейлисты не затрагиваются."),
                Mode = CleanMode.Delete, DefaultOn = true,
                BusyProcesses = new[] { "discord", "steamwebhelper", "spotify" }
            };
            foreach (var d in new[] { "Cache", "Code Cache", "GPUCache" }) AddIfExists(apps, Path.Combine(Roaming, "discord", d));
            AddIfExists(apps, Path.Combine(Local, @"Steam\htmlcache"));
            AddIfExists(apps, Path.Combine(Local, @"Spotify\Data"));
            list.Add(apps);

            var shaders = new CleanCategory
            {
                Title = L.T("Кэш шейдеров видеокарты"),
                Explain = L.T("Заготовки графики для игр. Безопасно, но после очистки первые запуски игр могут подтормаживать, пока кэш не соберётся заново. ") +
                          L.T("Имеет смысл, если он разросся или игра стала глючить после обновления драйвера."),
                Mode = CleanMode.Delete, DefaultOn = false
            };
            foreach (var d in new[] { "D3DSCache", @"NVIDIA\DXCache", @"NVIDIA\GLCache", @"NVIDIA Corporation\NV_Cache",
                                      @"AMD\DxCache", @"AMD\DxcCache", @"AMD\GLCache", @"Intel\ShaderCache" })
                AddIfExists(shaders, Path.Combine(Local, d));
            list.Add(shaders);

            var wer = new CleanCategory
            {
                Title = L.T("Отчёты об ошибках"),
                Explain = L.T("Отчёты и дампы памяти после вылетов программ. Нужны только разработчикам для разбора ошибок."),
                Mode = CleanMode.Delete, DefaultOn = true
            };
            AddIfExists(wer, Path.Combine(Local, @"Microsoft\Windows\WER\ReportArchive"));
            AddIfExists(wer, Path.Combine(Local, @"Microsoft\Windows\WER\ReportQueue"));
            AddIfExists(wer, Path.Combine(Local, "CrashDumps"));
            list.Add(wer);

            var downloads = KnownDownloads();
            if (downloads != null && Directory.Exists(downloads))
            {
                DateTime monthAgo = DateTime.Now.AddDays(-30);
                var inst = new CleanCategory
                {
                    Title = L.T("Старые установщики в «Загрузках»"),
                    Explain = L.T("Файлы .exe и .msi старше месяца прямо в папке «Загрузки». Программы из них уже установлены, а сами установщики ") +
                              L.T("можно скачать заново. Отправляются в корзину — их можно вернуть."),
                    Mode = CleanMode.Recycle, DefaultOn = false, TopLevelOnly = true,
                    Filter = f => f.LastWriteTime < monthAgo &&
                                  new[] { ".exe", ".msi", ".msix", ".msixbundle", ".appx" }.Contains(f.Extension.ToLowerInvariant())
                };
                inst.Roots.Add(downloads);
                list.Add(inst);
            }

            list.Add(new CleanCategory
            {
                Title = L.T("Корзина"),
                Explain = L.T("Файлы, которые вы уже удалили, но они всё ещё занимают место. Очистка корзины необратима — загляните в неё, если не уверены."),
                Mode = CleanMode.EmptyBin, DefaultOn = false
            });

            list.Add(new CleanCategory
            {
                Title = L.T("Системные файлы Windows (нужны права администратора)"),
                Explain = L.T("Старые обновления Windows и файлы оптимизации доставки часто занимают несколько гигабайт. Их удаляет встроенная ") +
                          L.T("очистка Windows: Параметры → Система → Память → «Временные файлы» → отметить нужное → «Удалить файлы»."),
                Mode = CleanMode.InfoOnly, ActionTarget = "ms-settings:storagesense"
            });
            return list;
        }

        static void AddIfExists(CleanCategory c, string dir)
        {
            if (Directory.Exists(dir)) c.Roots.Add(dir);
        }

        static void AddChromiumProfiles(CleanCategory c, string userData)
        {
            if (!Directory.Exists(userData)) return;
            foreach (var p in SafeDirs(userData))
            {
                string name = Path.GetFileName(p);
                if (name != "Default" && !name.StartsWith("Profile ") && name != "Guest Profile") continue;
                foreach (var d in new[] { "Cache", "Code Cache", "GPUCache" }) AddIfExists(c, Path.Combine(p, d));
            }
        }

        static IEnumerable<string> SafeDirs(string dir)
        {
            try { return Directory.GetDirectories(dir); } catch { return new string[0]; }
        }

        // ---------- Подсчёт ----------

        public static void Measure(CleanCategory c, CancellationToken ct)
        {
            c.Size = 0; c.Files = 0;
            if (c.Mode == CleanMode.InfoOnly) return;
            if (c.Mode == CleanMode.EmptyBin)
            {
                var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO)) };
                if (SHQueryRecycleBin(null, ref info) == 0) { c.Size = info.i64Size; c.Files = (int)Math.Min(int.MaxValue, info.i64NumItems); }
                return;
            }
            foreach (var root in c.Roots)
                Walk(root, c.TopLevelOnly, f =>
                {
                    if (c.Filter != null && !c.Filter(f)) return;
                    c.Size += f.Length; c.Files++;
                }, ct);

            var busy = new List<string>();
            foreach (var p in c.BusyProcesses)
            {
                var ps = Process.GetProcessesByName(p);
                if (ps.Length > 0) busy.Add(p);
                foreach (var x in ps) x.Dispose();
            }
            c.Busy = busy.Count > 0 ? string.Join(", ", busy) : null;
        }

        // ---------- Очистка ----------

        // чистит категорию. Возвращает освобождённое место; skipped - сколько файлов занято и пропущено.
        public static long Clean(CleanCategory c, out int skipped, CancellationToken ct)
        {
            skipped = 0;
            if (c.Mode == CleanMode.InfoOnly) return 0;
            if (c.Mode == CleanMode.EmptyBin)
            {
                var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO)) };
                SHQueryRecycleBin(null, ref info);
                long before = info.i64Size;
                SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4); // без запросов, без окна, без звука
                info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO)) };
                SHQueryRecycleBin(null, ref info);
                return Math.Max(0, before - info.i64Size);
            }

            long freed = 0;
            int skip = 0;
            foreach (var root in c.Roots)
            {
                string rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var files = new List<FileInfo>();
                Walk(root, c.TopLevelOnly, f => { if (c.Filter == null || c.Filter(f)) files.Add(f); }, ct);
                foreach (var f in files)
                {
                    if (ct.IsCancellationRequested) break;
                    // защита: никогда не выходим за пределы папки категории
                    if (!f.FullName.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        long len = f.Length;
                        if (c.Mode == CleanMode.Recycle)
                        {
                            if (!ToRecycleBin(f.FullName)) throw new IOException(L.T("не удалось отправить в корзину"));
                        }
                        else
                        {
                            if ((f.Attributes & FileAttributes.ReadOnly) != 0) f.Attributes &= ~FileAttributes.ReadOnly;
                            f.Delete();
                        }
                        freed += len;
                    }
                    catch { skip++; }
                }
                if (c.Mode == CleanMode.Delete && !c.TopLevelOnly) RemoveEmptyDirs(rootFull, rootFull);
            }
            skipped = skip;
            return freed;
        }

        static void RemoveEmptyDirs(string dir, string root)
        {
            foreach (var d in SafeDirs(dir))
            {
                try
                {
                    var di = new DirectoryInfo(d);
                    if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    RemoveEmptyDirs(d, root);
                    if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d, false);
                }
                catch { }
            }
        }

        // обход файлов без перехода по ссылкам и точкам повторной обработки.
        public static void Walk(string root, bool topOnly, Action<FileInfo> onFile, CancellationToken ct)
        {
            var stack = new Stack<DirectoryInfo>();
            try { stack.Push(new DirectoryInfo(root)); } catch { return; }
            while (stack.Count > 0)
            {
                if (ct.IsCancellationRequested) return;
                var d = stack.Pop();
                FileSystemInfo[] items;
                try { items = d.GetFileSystemInfos(); } catch { continue; }
                foreach (var it in items)
                {
                    if ((it.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    var di = it as DirectoryInfo;
                    if (di != null) { if (!topOnly) stack.Push(di); continue; }
                    var fi = it as FileInfo;
                    if (fi != null) { try { onFile(fi); } catch { } }
                }
            }
        }

        // ---------- WinAPI ----------

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string pTo;
            public ushort fFlags;
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

        // тихо отправить файл в корзину (без окон и запросов).
        static bool ToRecycleBin(string path)
        {
            var op = new SHFILEOPSTRUCT
            {
                wFunc = 3,                                    // FO_DELETE
                pFrom = path + "\0\0",                        // список, заканчивающийся двумя нулями
                fFlags = 0x40 | 0x10 | 0x4 | 0x400             // ALLOWUNDO | NOCONFIRMATION | SILENT | NOERRORUI
            };
            return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted && !File.Exists(path);
        }

        // В 64-битной Windows структура выровнена по 8 байт (в 32-битной - упакована по 1)
        [StructLayout(LayoutKind.Sequential)]
        struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHQueryRecycleBin(string pszRootPath, ref SHQUERYRBINFO info);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, uint dwFlags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

        static string KnownDownloads()
        {
            try
            {
                IntPtr p;
                if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out p) == 0)
                {
                    string s = Marshal.PtrToStringUni(p);
                    Marshal.FreeCoTaskMem(p);
                    return s;
                }
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }
}
