using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Roomy
{
    // Язык интерфейса. Тексты в коде пишутся по-русски и оборачиваются в L.T(),
    // английский перевод лежит в Lang.En.cs. Нет перевода - покажется русский текст.
    // Язык выбирается при запуске: Language=auto|ru|en в settings.ini (auto - как у Windows).
    static partial class L
    {
        public static readonly bool En;
        public static readonly CultureInfo Culture;

        static L()
        {
            string mode = ReadSetting();
            if (mode == "en") En = true;
            else if (mode == "ru") En = false;
            else
            {
                // русский - для тех, у кого Windows на русском или на языках, где его обычно понимают
                string lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                En = !(lang == "ru" || lang == "be" || lang == "kk" || lang == "ky" || lang == "uz" || lang == "tg");
            }
            Culture = new CultureInfo(En ? "en-US" : "ru-RU");
        }

        // читаем сами, без класса Settings: он сам использует L.T() при загрузке
        static string ReadSetting()
        {
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Roomy", "settings.ini");
                if (!File.Exists(path)) return "auto";
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (!line.StartsWith("language", StringComparison.OrdinalIgnoreCase)) continue;
                    int eq = line.IndexOf('=');
                    if (eq > 0) return line.Substring(eq + 1).Trim().ToLowerInvariant();
                }
            }
            catch { }
            return "auto";
        }

        public static string T(string ru)
        {
            if (!En || ru == null) return ru;
            string en;
            return Dict.TryGetValue(ru, out en) ? en : ru;
        }

        static readonly Dictionary<string, string> Dict = new Dictionary<string, string>(Build());
    }
}
