using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace Roomy
{
    class BigItem
    {
        public string Path;
        public long Size;
        public string Note;
    }

    class DupGroup
    {
        public long Size;               // размер одного файла
        public List<string> Paths = new List<string>();
        public long Wasted { get { return Size * (Paths.Count - 1); } }
    }

    class DiskMapResult
    {
        public List<BigItem> Folders = new List<BigItem>();
        public List<BigItem> Files = new List<BigItem>();
        public List<DupGroup> Duplicates = new List<DupGroup>();
        public long Scanned;
        public int FileCount;
    }

    // что занимает место на системном диске: крупные папки, файлы и одинаковые копии.
    static class DiskMap
    {
        const long FolderMin = 300L * 1024 * 1024;
        const long FileMin = 200L * 1024 * 1024;
        const long DupMin = 50L * 1024 * 1024;

        static readonly HashSet<string> SkipTop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Windows", "$Recycle.Bin", "System Volume Information", "Recovery", "PerfLogs", "$WinREAgent",
            "Config.Msi", "Documents and Settings", "MSOCache", "$SysReset", "$Windows.~BT", "$Windows.~WS", "Users"
        };

        public static DiskMapResult Scan(Action<int, long> progress, CancellationToken ct)
        {
            string sysRoot = System.IO.Path.GetPathRoot(Environment.SystemDirectory);
            var roots = new List<string> { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
            try
            {
                foreach (var d in Directory.GetDirectories(sysRoot))
                    if (!SkipTop.Contains(System.IO.Path.GetFileName(d))) roots.Add(d);
            }
            catch { }

            var res = new DiskMapResult();
            var dirSize = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var bigFiles = new List<BigItem>();
            var dupCandidates = new List<FileInfo>();
            int counter = 0;

            foreach (var root in roots)
            {
                if (ct.IsCancellationRequested) break;
                string rootParent = System.IO.Path.GetDirectoryName(root.TrimEnd('\\'));
                DiskClean.Walk(root, false, f =>
                {
                    long len = f.Length;
                    res.Scanned += len;
                    res.FileCount++;
                    if (++counter % 2000 == 0) progress(res.FileCount, res.Scanned);
                    if (len >= FileMin) bigFiles.Add(new BigItem { Path = f.FullName, Size = len });
                    if (len >= DupMin && IsUserContent(f.FullName)) dupCandidates.Add(f);
                    // размер всех папок-предков до корня обхода
                    string dir = f.DirectoryName;
                    int guard = 0;
                    while (dir != null && dir.Length >= root.TrimEnd('\\').Length && guard++ < 64)
                    {
                        long cur;
                        dirSize.TryGetValue(dir, out cur);
                        dirSize[dir] = cur + len;
                        if (string.Equals(dir.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) break;
                        dir = System.IO.Path.GetDirectoryName(dir);
                    }
                }, ct);
            }
            progress(res.FileCount, res.Scanned);

            res.Folders = PickFolders(dirSize, roots);
            res.Files = bigFiles.OrderByDescending(b => b.Size).Take(20).ToList();
            foreach (var f in res.Files) f.Note = Hint(f.Path);
            if (!ct.IsCancellationRequested) res.Duplicates = FindDuplicates(dupCandidates, ct);
            return res;
        }

        // Самые "говорящие" папки: крупные, но без одной подпапки, которая занимает почти всё
        // (тогда показываем саму подпапку - она точнее).
        static List<BigItem> PickFolders(Dictionary<string, long> sizes, List<string> roots)
        {
            var rootSet = new HashSet<string>(roots.Select(r => r.TrimEnd(System.IO.Path.DirectorySeparatorChar)), StringComparer.OrdinalIgnoreCase);
            var children = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase); // папка -> крупнейшая подпапка
            foreach (var kv in sizes)
            {
                string parent = System.IO.Path.GetDirectoryName(kv.Key.TrimEnd(System.IO.Path.DirectorySeparatorChar));
                if (parent == null) continue;
                long cur;
                if (!children.TryGetValue(parent, out cur) || kv.Value > cur) children[parent] = kv.Value;
            }

            var picked = new List<BigItem>();
            foreach (var kv in sizes.Where(k => k.Value >= FolderMin).OrderByDescending(k => k.Value))
            {
                string path = kv.Key.TrimEnd(System.IO.Path.DirectorySeparatorChar);
                if (rootSet.Contains(path) && path.Count(ch => ch == '\\') <= 2 && IsProfile(path)) continue; // сам профиль целиком неинформативен
                if (IsContainer(path)) continue;
                long biggestChild;
                if (children.TryGetValue(path, out biggestChild) && biggestChild >= kv.Value * 0.6) continue;
                if (picked.Any(p => path.StartsWith(p.Path + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) continue;
                picked.Add(new BigItem { Path = path, Size = kv.Value, Note = Hint(path) });
                if (picked.Count >= 15) break;
            }
            return picked;
        }

        // Папки с файлами самого пользователя. Внутри программ (AppData, Program Files, скрытые папки)
        // одинаковые файлы - часть программы, и удалять их нельзя.
        static bool IsUserContent(string path)
        {
            string p = path.ToLowerInvariant();
            string[] bad = { "\\appdata\\", "\\program files", "\\programdata\\", "\\windows\\", "\\$recycle.bin\\",
                             "\\node_modules\\", "\\.git\\", "\\steamapps\\", "\\epic games\\" };
            if (bad.Any(b => p.Contains(b))) return false;
            if (p.Contains("\\.")) return false; // скрытые служебные папки вида .ollama, .vscode
            return true;
        }

        // общие "контейнеры", которые сами по себе ничего не говорят - показываем их содержимое.
        static bool IsContainer(string path)
        {
            string p = path.TrimEnd(System.IO.Path.DirectorySeparatorChar).ToLowerInvariant();
            return p.EndsWith("\\appdata") || p.EndsWith("\\appdata\\local") || p.EndsWith("\\appdata\\roaming") ||
                   p.EndsWith("\\appdata\\locallow") || p.EndsWith("\\program files") || p.EndsWith("\\program files (x86)") ||
                   p.EndsWith("\\programdata") || p.EndsWith("\\appdata\\local\\programs") || p.EndsWith("\\appdata\\local\\packages");
        }

        static bool IsProfile(string path)
        {
            return string.Equals(path, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        // подсказка, что это за место.
        static string Hint(string path)
        {
            string p = path.ToLowerInvariant();
            if (p.Contains("\\steamapps\\common\\")) return L.T("игра Steam — удалять через Steam");
            if (p.Contains("\\epic games\\")) return L.T("игра Epic — удалять через лаунчер");
            if (p.Contains("\\steamapps\\shadercache")) return L.T("кэш шейдеров Steam");
            if (p.EndsWith("\\downloads") || p.Contains("\\downloads\\")) return L.T("Загрузки");
            if (p.Contains("\\.ollama\\")) return L.T("модели нейросетей Ollama");
            if (p.Contains("\\node_modules")) return L.T("зависимости проекта (восстанавливаются npm install)");
            if (p.Contains("\\videos") || p.EndsWith(".mp4") || p.EndsWith(".mkv")) return L.T("видео");
            if (p.Contains("\\telegram desktop\\")) return L.T("файлы Telegram");
            if (p.Contains("\\appdata\\local\\temp")) return L.T("временные файлы");
            if (p.Contains("\\program files")) return L.T("установленные программы — удалять через «Установленные приложения»");
            if (p.EndsWith(".iso")) return L.T("образ диска");
            if (p.EndsWith(".zip") || p.EndsWith(".rar") || p.EndsWith(".7z")) return L.T("архив");
            return null;
        }

        static List<DupGroup> FindDuplicates(List<FileInfo> files, CancellationToken ct)
        {
            var groups = new List<DupGroup>();
            foreach (var bySize in files.GroupBy(f => f.Length).Where(g => g.Count() > 1))
            {
                if (ct.IsCancellationRequested) break;
                // быстрый отпечаток: начало и конец файла, затем полный хеш для совпавших
                var byQuick = bySize.GroupBy(f => QuickHash(f)).Where(g => g.Key != null && g.Count() > 1);
                foreach (var q in byQuick)
                    foreach (var full in q.GroupBy(f => FullHash(f, ct)).Where(g => g.Key != null && g.Count() > 1))
                    {
                        var dg = new DupGroup { Size = bySize.Key };
                        dg.Paths.AddRange(full.Select(f => f.FullName).OrderBy(x => x.Length));
                        groups.Add(dg);
                    }
            }
            return groups.OrderByDescending(g => g.Wasted).Take(15).ToList();
        }

        static string QuickHash(FileInfo f)
        {
            try
            {
                using (var sha = SHA1.Create())
                using (var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    const int chunk = 1024 * 1024;
                    var buf = new byte[chunk];
                    int n = fs.Read(buf, 0, chunk);
                    sha.TransformBlock(buf, 0, n, null, 0);
                    if (fs.Length > chunk * 2)
                    {
                        fs.Seek(-chunk, SeekOrigin.End);
                        n = fs.Read(buf, 0, chunk);
                    }
                    sha.TransformFinalBlock(buf, 0, n);
                    return Convert.ToBase64String(sha.Hash);
                }
            }
            catch { return null; }
        }

        static string FullHash(FileInfo f, CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return null;
            try
            {
                using (var sha = SHA256.Create())
                using (var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20))
                    return Convert.ToBase64String(sha.ComputeHash(fs));
            }
            catch { return null; }
        }
    }
}
