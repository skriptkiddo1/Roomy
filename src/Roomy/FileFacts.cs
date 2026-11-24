using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32.SafeHandles;

namespace Roomy
{
    enum SignState { Unknown, Valid, Catalog, Unsigned, Invalid }

    // факты о файле программы: путь, издатель, подпись, подозрительные признаки.
    class FileFacts
    {
        public string Path;
        public string Description, Company, Product, Version;
        public SignState Sign = SignState.Unknown;
        public string Signer;
        public readonly List<string> Warnings = new List<string>();
        public bool InWindowsDir;
        public bool Masquerade; // системное имя, но файл не в папке Windows

        static readonly string WinDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";

        public static FileFacts Inspect(string key, IEnumerable<int> pids)
        {
            var f = new FileFacts();
            foreach (int pid in pids)
            {
                f.Path = Native.ImagePath(pid);
                if (f.Path != null) break;
            }
            if (f.Path == null) return f;

            f.InWindowsDir = f.Path.StartsWith(WinDir, StringComparison.OrdinalIgnoreCase);
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(f.Path);
                f.Description = Clean(vi.FileDescription);
                f.Company = Clean(vi.CompanyName);
                f.Product = Clean(vi.ProductName);
                f.Version = Clean(vi.FileVersion);
            }
            catch { }

            try { f.CheckSignature(); } catch (Exception ex) { Log.Write("Проверка подписи " + f.Path + ": " + ex.Message); }
            f.CheckSuspicious(key);
            return f;
        }

        static string Clean(string s)
        {
            s = (s ?? "").Trim();
            return s.Length == 0 ? null : s;
        }

        // ---------- Подозрительные признаки ----------

        void CheckSuspicious(string key)
        {
            string lower = Path.ToLowerInvariant();
            string temp = System.IO.Path.GetTempPath().ToLowerInvariant();

            Masquerade = ProcessKb.SystemNames.Contains(key) && !InWindowsDir;
            if (Masquerade)
                Warnings.Add(L.T("У этой программы имя системного процесса Windows, но лежит она не в папке Windows. ") +
                             L.T("Так часто маскируются вредоносные программы — проверьте компьютер антивирусом и файл на VirusTotal."));

            if (lower.StartsWith(temp) || lower.Contains("\\temp\\"))
                Warnings.Add(L.T("Запущена из папки временных файлов. Так обычно работают установщики, но если программа висит там постоянно — это подозрительно."));
            else if (lower.Contains("\\downloads\\") || lower.Contains("\\загрузки\\"))
                Warnings.Add(L.T("Запущена прямо из папки «Загрузки». Ничего страшного, если вы сами её скачали и запустили."));

            if ((Sign == SignState.Unsigned || Sign == SignState.Invalid) && !InWindowsDir &&
                (lower.Contains("\\appdata\\") || lower.Contains("\\programdata\\")) && ProcessKb.Find(key) == null)
                Warnings.Add(L.T("Незнакомая программа без цифровой подписи в служебной папке пользователя. ") +
                             L.T("Многие нормальные программы так устроены, но и вредоносные тоже — если вы её не узнаёте, проверьте на VirusTotal."));

            if (Sign == SignState.Invalid)
                Warnings.Add(L.T("Цифровая подпись файла повреждена или недействительна. Это повод проверить файл антивирусом."));
        }

        // ---------- Цифровая подпись ----------

        void CheckSignature()
        {
            int r = VerifyEmbedded(Path);
            if (r == 0)
            {
                Sign = SignState.Valid;
                try
                {
                    var cert = X509Certificate.CreateFromSignedFile(Path);
                    Signer = CommonName(cert.Subject);
                }
                catch { }
                return;
            }
            const int TRUST_E_NOSIGNATURE = unchecked((int)0x800B0100);
            const int TRUST_E_SUBJECT_FORM_UNKNOWN = unchecked((int)0x800B0003);
            const int TRUST_E_PROVIDER_UNKNOWN = unchecked((int)0x800B0001);
            if (r == TRUST_E_NOSIGNATURE || r == TRUST_E_SUBJECT_FORM_UNKNOWN || r == TRUST_E_PROVIDER_UNKNOWN)
            {
                Sign = InCatalog(Path) ? SignState.Catalog : SignState.Unsigned;
                if (Sign == SignState.Catalog) Signer = L.T("Microsoft Windows (каталог)");
                return;
            }
            Sign = SignState.Invalid;
        }

        static string CommonName(string subject)
        {
            foreach (var part in subject.Split(','))
            {
                var p = part.Trim();
                if (p.StartsWith("CN=", StringComparison.OrdinalIgnoreCase)) return p.Substring(3).Trim('"');
            }
            return subject;
        }

        public string SignText
        {
            get
            {
                switch (Sign)
                {
                    case SignState.Valid: return L.T("есть, действительна") + (Signer != null ? " — " + Signer : "");
                    case SignState.Catalog: return L.T("есть — подписан каталогом Windows");
                    case SignState.Unsigned: return L.T("нет цифровой подписи");
                    case SignState.Invalid: return L.T("недействительна или повреждена");
                    default: return L.T("не удалось проверить");
                }
            }
        }

        public string Sha256()
        {
            using (var sha = SHA256.Create())
            using (var fs = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }

        // --- WinVerifyTrust ---

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            public IntPtr pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
        static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WINTRUST_DATA data);

        static Guid GenericVerifyV2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        static int VerifyEmbedded(string path)
        {
            IntPtr pPath = Marshal.StringToHGlobalUni(path);
            var fileInfo = new WINTRUST_FILE_INFO
            {
                cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)),
                pcwszFilePath = pPath
            };
            IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
            Marshal.StructureToPtr(fileInfo, pFile, false);
            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA)),
                dwUIChoice = 2,          // WTD_UI_NONE
                fdwRevocationChecks = 0, // WTD_REVOKE_NONE
                dwUnionChoice = 1,       // WTD_CHOICE_FILE
                pFile = pFile,
                dwStateAction = 1,       // WTD_STATEACTION_VERIFY
                dwProvFlags = 0x1000     // WTD_CACHE_ONLY_URL_RETRIEVAL — без обращения к сети
            };
            try
            {
                var action = GenericVerifyV2;
                int result = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
                data.dwStateAction = 2;  // WTD_STATEACTION_CLOSE
                WinVerifyTrust(IntPtr.Zero, ref action, ref data);
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(pFile);
                Marshal.FreeHGlobal(pPath);
            }
        }

        // --- Каталоги Windows (так подписана большая часть системных файлов) ---

        [DllImport("wintrust.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CryptCATAdminAcquireContext2(out IntPtr hCatAdmin, IntPtr pgSubsystem, string pwszHashAlgorithm, IntPtr pStrongHashPolicy, uint dwFlags);

        [DllImport("wintrust.dll", SetLastError = true)]
        static extern bool CryptCATAdminCalcHashFromFileHandle2(IntPtr hCatAdmin, SafeFileHandle hFile, ref uint pcbHash, byte[] pbHash, uint dwFlags);

        [DllImport("wintrust.dll", SetLastError = true)]
        static extern IntPtr CryptCATAdminEnumCatalogFromHash(IntPtr hCatAdmin, byte[] pbHash, uint cbHash, uint dwFlags, IntPtr phPrevCatInfo);

        [DllImport("wintrust.dll")]
        static extern bool CryptCATAdminReleaseCatalogContext(IntPtr hCatAdmin, IntPtr hCatInfo, uint dwFlags);

        [DllImport("wintrust.dll")]
        static extern bool CryptCATAdminReleaseContext(IntPtr hCatAdmin, uint dwFlags);

        static bool InCatalog(string path)
        {
            foreach (var alg in new[] { "SHA256", null })
            {
                IntPtr admin;
                if (!CryptCATAdminAcquireContext2(out admin, IntPtr.Zero, alg, IntPtr.Zero, 0)) continue;
                try
                {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        uint size = 64;
                        var hash = new byte[size];
                        if (!CryptCATAdminCalcHashFromFileHandle2(admin, fs.SafeFileHandle, ref size, hash, 0)) continue;
                        IntPtr cat = CryptCATAdminEnumCatalogFromHash(admin, hash, size, 0, IntPtr.Zero);
                        if (cat != IntPtr.Zero)
                        {
                            CryptCATAdminReleaseCatalogContext(admin, cat, 0);
                            return true;
                        }
                    }
                }
                catch { }
                finally { CryptCATAdminReleaseContext(admin, 0); }
            }
            return false;
        }
    }
}
