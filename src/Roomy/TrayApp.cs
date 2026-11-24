using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Roomy
{
    class TrayApp : ApplicationContext
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "Roomy";

        readonly Controller C;
        readonly NotifyIcon tray;
        readonly ContextMenuStrip menu;
        readonly System.Windows.Forms.Timer timer;
        readonly ToolStripMenuItem miHealth, miDisk;
        HealthForm healthForm;
        DiskForm diskForm;
        readonly ToolStripMenuItem miStatus, miOpen, miEconomy, miNotify, miAutostart, miSettings, miLog, miExit;
        readonly Icon appIcon;

        Icon currentIcon;
        uint lastIconLoad = 999;
        Color lastIconColor;
        string lastIconKey;
        // Робот в трее: готовые значки по ключу "настроение+кадр", чтобы не плодить HICON
        readonly System.Collections.Generic.Dictionary<string, Icon> robotIcons = new System.Collections.Generic.Dictionary<string, Icon>();
        int robotIconSize;
        Mood trayMood = Mood.Ok;
        float trayBlink;
        int blinkFrame = -1;
        readonly System.Windows.Forms.Timer blinkTimer = new System.Windows.Forms.Timer();
        readonly Random rnd = new Random();
        ToolStripMenuItem miTrayRobot, miTrayPercent, miTrayBlink;
        int highTicks;
        DateTime lastAlert = DateTime.MinValue;
        MainForm form;

        public const string ShowEventName = @"Local\Roomy_ShowWindow";
        readonly Control invoker = new Control();
        EventWaitHandle showEvent;

        public TrayApp()
        {
            invoker.CreateControl();
            try
            {
                showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                var waiter = new Thread(() =>
                {
                    while (true)
                    {
                        try { showEvent.WaitOne(); } catch { return; }
                        try { invoker.BeginInvoke((Action)ShowMain); } catch { return; }
                    }
                }) { IsBackground = true, Name = "Roomy show-window listener" };
                waiter.Start();
            }
            catch (Exception ex) { Log.Write("Не удалось создать сигнал показа окна: " + ex.Message); }

            C = new Controller();
            MigrateAutostart();
            appIcon = LoadAppIcon();
            Log.Write("Roomy запущен");

            menu = new ContextMenuStrip();
            miStatus = new ToolStripMenuItem(L.T("Память: …")) { Enabled = false };
            miOpen = new ToolStripMenuItem(L.T("Открыть Roomy"), null, (o, e) => ShowMain());
            miOpen.Font = new Font(miOpen.Font, FontStyle.Bold);
            miEconomy = new ToolStripMenuItem(L.T("Режим экономии"), null, (o, e) => ToggleEconomy());
            miHealth = new ToolStripMenuItem(L.T("Проверка компьютера…"), null, (o, e) => ShowHealth());
            miDisk = new ToolStripMenuItem(L.T("Место на диске…"), null, (o, e) => ShowDisk());
            miNotify = new ToolStripMenuItem(L.T("Уведомления о нехватке памяти"), null, (o, e) => { C.S.Notifications = !C.S.Notifications; C.S.Save(); SyncMenu(); });
            miAutostart = new ToolStripMenuItem(L.T("Запускать вместе с Windows"), null, (o, e) => ToggleAutostart());
            miTrayRobot = new ToolStripMenuItem(L.T("Робот Roomy"), null, (o, e) => SetTrayStyle(true));
            miTrayPercent = new ToolStripMenuItem(L.T("Процент памяти"), null, (o, e) => SetTrayStyle(false));
            miTrayBlink = new ToolStripMenuItem(L.T("Робот моргает"), null, (o, e) => { C.S.TrayBlink = !C.S.TrayBlink; C.S.Save(); SyncMenu(); });
            var miLang = new ToolStripMenuItem(L.En ? "Language / Язык" : "Язык / Language");
            foreach (var opt in new[] { new[] { "auto", L.En ? "Like Windows" : "Как в Windows" }, new[] { "ru", "Русский" }, new[] { "en", "English" } })
            {
                string code = opt[0];
                var item = new ToolStripMenuItem(opt[1], null, (o, e) => SetLanguage(code));
                item.Tag = code;
                miLang.DropDownItems.Add(item);
            }
            miLang.DropDownOpening += (o, e) => { foreach (ToolStripMenuItem it in miLang.DropDownItems) it.Checked = (string)it.Tag == C.S.Language; };
            var miTray = new ToolStripMenuItem(L.T("Значок в трее"));
            miTray.DropDownItems.AddRange(new ToolStripItem[] { miTrayRobot, miTrayPercent, new ToolStripSeparator(), miTrayBlink });
            miSettings = new ToolStripMenuItem(L.T("Файл настроек…"), null, (o, e) => OpenFile(Settings.FilePath));
            miLog = new ToolStripMenuItem(L.T("Журнал…"), null, (o, e) => OpenFile(Log.FilePath));
            miExit = new ToolStripMenuItem(L.T("Выход"), null, (o, e) => ExitApp());
            var miAbout = new ToolStripMenuItem(L.T("О Roomy…"), null, (o, e) => ShowAbout());

            menu.Items.AddRange(new ToolStripItem[] {
                miStatus, new ToolStripSeparator(),
                miOpen, miHealth, miDisk, miEconomy, new ToolStripSeparator(),
                miNotify, miAutostart, miTray, miLang, new ToolStripSeparator(),
                miSettings, miLog, miAbout, new ToolStripSeparator(),
                miExit });
            menu.Opening += (o, e) => SyncMenu();

            tray = new NotifyIcon { ContextMenuStrip = menu, Visible = true, Text = "Roomy" };
            tray.MouseClick += (o, e) => { if (e.Button == MouseButtons.Left) ShowMain(); };
            tray.BalloonTipClicked += (o, e) => ShowMain();

            blinkTimer.Interval = 3000;
            blinkTimer.Tick += (o, e) => BlinkStep();
            blinkTimer.Start();

            C.Updated += OnUpdated;
            timer = new System.Windows.Forms.Timer { Interval = 5000 };
            timer.Tick += (o, e) => SafeTick();
            timer.Start();
            SafeTick();

            string marker = Path.Combine(Settings.Dir, ".welcomed2");
            if (!File.Exists(marker))
            {
                try { File.WriteAllText(marker, "1"); } catch { }
                ShowMain();
            }
        }

        void SafeTick()
        {
            try { C.Tick(); }
            catch (Exception ex) { Log.Write("Ошибка: " + ex.Message); }
        }

        void OnUpdated()
        {
            var m = C.Mem;
            var st = C.GetStatus();
            trayMood = Mascot.MoodOf(st);
            UpdateIcon(m.dwMemoryLoad, st.Color);
            string tip = "Roomy — " + m.dwMemoryLoad + "%, " + st.Title.ToLowerInvariant();
            if (C.EconomyOn) tip += L.T(" · экономия");
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
            CheckAlert(m);
        }

        void CheckAlert(Native.MEMORYSTATUSEX m)
        {
            if (!C.S.Notifications || m.dwMemoryLoad < C.S.AlertThreshold) { highTicks = 0; return; }
            highTicks++;
            if (highTicks < 3) return; // держится ~15 секунд, а не короткий всплеск
            if ((DateTime.Now - lastAlert).TotalMinutes < C.S.AlertCooldownMinutes) return;
            if (form != null && form.Visible && form.ContainsFocus) return;
            lastAlert = DateTime.Now;

            var top = C.RefreshGroups().Where(g => !C.S.Untouchable(g.Key)).Take(3)
                .Select(g => Fmt.ShortName(g.Display) + " — " + Fmt.Bytes(g.Bytes));
            var st = C.GetStatus();
            tray.ShowBalloonTip(15000, st.Title + L.T(": занято ") + m.dwMemoryLoad + "%",
                L.T("Больше всего: ") + string.Join(", ", top) + L.T(".\nНажмите, чтобы посмотреть подробнее."),
                ToolTipIcon.Warning);
            Log.Write("Уведомление: память " + m.dwMemoryLoad + "%");
        }

        void SyncMenu()
        {
            var m = C.Mem;
            miStatus.Text = L.T("Память: ") + m.dwMemoryLoad + "% · " + Fmt.Bytes(C.UsedBytes) + L.T(" из ") + Fmt.Bytes((long)m.ullTotalPhys);
            miNotify.Checked = C.S.Notifications;
            miAutostart.Checked = IsAutostart();
            miEconomy.Text = C.EconomyOn ? L.T("Вернуть закрытые программы") : L.T("Режим экономии");
            miEconomy.Enabled = C.EconomyOn || C.S.Economy.Count > 0;
            miTrayRobot.Checked = C.S.TrayRobot;
            miTrayPercent.Checked = !C.S.TrayRobot;
            miTrayBlink.Checked = C.S.TrayBlink;
            miTrayBlink.Enabled = C.S.TrayRobot;
        }

        void SetLanguage(string code)
        {
            if (C.S.Language == code) return;
            C.S.Language = code;
            C.S.Save();
            var r = MessageBox.Show(L.En ? "The language changes after Roomy restarts. Restart now?\nЯзык сменится после перезапуска Roomy. Перезапустить сейчас?"
                                         : "Язык сменится после перезапуска Roomy. Перезапустить сейчас?\nThe language changes after Roomy restarts. Restart now?",
                "Roomy", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;
            try { Process.Start(Application.ExecutablePath, "--restart"); }
            catch (Exception ex) { Log.Write("Не удалось перезапуститься: " + ex.Message); return; }
            ExitApp();
        }

        void SetTrayStyle(bool robot)
        {
            C.S.TrayRobot = robot;
            C.S.Save();
            SyncMenu();
            UpdateIcon(C.Mem.dwMemoryLoad, C.GetStatus().Color);
        }

        void Notify(string text, ToolTipIcon icon)
        {
            tray.ShowBalloonTip(6000, "Roomy", text, icon);
        }

        // ---------- Иконка в трее ----------

        void UpdateIcon(uint load, Color bg)
        {
            if (C.S.TrayRobot) { ShowRobot(); return; }
            if (load == lastIconLoad && bg == lastIconColor && lastIconKey == "percent") return;
            lastIconKey = "percent";
            lastIconLoad = load;
            lastIconColor = bg;
            int size = SystemInformation.SmallIconSize.Width;
            if (size < 16) size = 16;
            using (var bmp = new Bitmap(size, size))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);
                    using (var path = Ui.Round(new RectangleF(0, 0, size - 1, size - 1), size / 4f))
                    using (var brush = new SolidBrush(bg))
                        g.FillPath(brush, path);
                    string text = load >= 100 ? "!!" : load.ToString();
                    using (var font = new Font("Segoe UI", size * 0.66f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(text, font, Brushes.White, new RectangleF(-1, 0, size + 2, size + 1), sf);
                }
                IntPtr h = bmp.GetHicon();
                var icon = (Icon)Icon.FromHandle(h).Clone();
                Native.DestroyIcon(h);
                tray.Icon = icon;
                if (currentIcon != null) currentIcon.Dispose();
                currentIcon = icon;
            }
        }

        void ShowRobot()
        {
            int size = SystemInformation.SmallIconSize.Width;
            if (size < 16) size = 16;
            if (size != robotIconSize)
            {
                foreach (var i in robotIcons.Values) i.Dispose();
                robotIcons.Clear();
                robotIconSize = size;
                lastIconKey = null;
            }
            float blink = trayMood == Mood.Bad ? 0f : trayBlink;
            string key = (int)trayMood + ":" + blink.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (key == lastIconKey) return;
            Icon icon;
            if (!robotIcons.TryGetValue(key, out icon))
            {
                using (var bmp = Mascot.RenderTray(size, trayMood, blink))
                {
                    IntPtr h = bmp.GetHicon();
                    icon = (Icon)Icon.FromHandle(h).Clone();
                    Native.DestroyIcon(h);
                }
                robotIcons[key] = icon;
            }
            tray.Icon = icon;
            lastIconKey = key;
            // значок с процентами больше не нужен
            if (currentIcon != null) { currentIcon.Dispose(); currentIcon = null; }
            lastIconLoad = 999;
        }

        // моргание робота в трее: раз в 4–9 секунд пять быстрых кадров.
        void BlinkStep()
        {
            try
            {
                if (!C.S.TrayRobot || !C.S.TrayBlink || trayMood == Mood.Bad)
                {
                    blinkFrame = -1;
                    if (trayBlink != 0f) { trayBlink = 0f; if (C.S.TrayRobot) ShowRobot(); }
                    blinkTimer.Interval = 3000;
                    return;
                }
                if (blinkFrame < 0) blinkFrame = 0;
                trayBlink = Mascot.BlinkFrames[blinkFrame++];
                ShowRobot();
                if (blinkFrame >= Mascot.BlinkFrames.Length)
                {
                    blinkFrame = -1;
                    blinkTimer.Interval = rnd.Next(5) == 0 ? 220 : rnd.Next(4000, 9000);
                }
                else blinkTimer.Interval = 70;
            }
            catch (Exception ex) { Log.Write("Ошибка значка: " + ex.Message); blinkTimer.Interval = 10000; }
        }

        static Icon LoadAppIcon()
        {
            try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { return null; }
        }

        // ---------- Окно ----------

        AboutForm aboutForm;
        void ShowAbout()
        {
            if (aboutForm != null && !aboutForm.IsDisposed) { aboutForm.Activate(); return; }
            aboutForm = new AboutForm(appIcon);
            aboutForm.FormClosed += (o, e) => { aboutForm.Dispose(); aboutForm = null; };
            aboutForm.Show();
        }

        void ShowMain()
        {
            if (form == null || form.IsDisposed) form = new MainForm(C, appIcon, Notify, ShowHealth, ShowDisk);
            if (!form.Visible) form.Show();
            if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
            form.Activate();
        }

        void ShowDisk()
        {
            if (diskForm == null || diskForm.IsDisposed) diskForm = new DiskForm(appIcon);
            if (!diskForm.Visible) diskForm.Show();
            if (diskForm.WindowState == FormWindowState.Minimized) diskForm.WindowState = FormWindowState.Normal;
            diskForm.Activate();
        }

        void ShowHealth()
        {
            if (healthForm == null || healthForm.IsDisposed) healthForm = new HealthForm(C.S, appIcon);
            if (!healthForm.Visible) healthForm.Show();
            if (healthForm.WindowState == FormWindowState.Minimized) healthForm.WindowState = FormWindowState.Normal;
            healthForm.Activate();
        }

        void ToggleEconomy()
        {
            string msg;
            if (C.EconomyOn) msg = C.ExitEconomy();
            else
            {
                var cand = C.EconomyCandidates();
                if (cand.Count == 0) { Notify(L.T("Из программ с режимом «Можно закрывать» сейчас ничего не запущено."), ToolTipIcon.Info); return; }
                var r = MessageBox.Show(L.T("Закрыть на время: ") + string.Join(", ", cand) +
                    L.T("?\n\nКогда снова выберете «Вернуть закрытые программы», они запустятся обратно."),
                    L.T("Roomy — режим экономии"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (r != DialogResult.OK) return;
                msg = C.EnterEconomy();
            }
            Notify(msg, ToolTipIcon.Info);
            if (form != null && form.Visible) form.UpdateAll();
        }

        // ---------- Автозапуск ----------

        // если был включён автозапуск старого RamPilot - переносим его на Roomy.
        static void MigrateAutostart()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null || k.GetValue("RamPilot") == null) return;
                    k.DeleteValue("RamPilot", false);
                    k.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
                    Log.Write("Автозапуск перенесён с RamPilot на Roomy");
                }
            }
            catch { }
        }

        static bool IsAutostart()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    var v = k == null ? null : k.GetValue(RunName) as string;
                    return v != null && v.Trim('"').Equals(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { return false; }
        }

        void ToggleAutostart()
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (IsAutostart()) k.DeleteValue(RunName, false);
                    else k.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
                }
            }
            catch (Exception ex) { MessageBox.Show(L.T("Не удалось изменить автозапуск: ") + ex.Message, "Roomy"); }
            SyncMenu();
        }

        // ---------- Прочее ----------

        static void OpenFile(string path)
        {
            try
            {
                if (!File.Exists(path)) File.WriteAllText(path, "");
                Process.Start(new ProcessStartInfo("notepad.exe", "\"" + path + "\"") { UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show(L.T("Не удалось открыть файл: ") + ex.Message, "Roomy"); }
        }

        void ExitApp()
        {
            timer.Stop();
            blinkTimer.Stop();
            C.Updated -= OnUpdated;
            C.Shutdown();
            tray.Visible = false;
            tray.Dispose();
            if (form != null && !form.IsDisposed) form.Dispose();
            if (healthForm != null && !healthForm.IsDisposed) healthForm.Dispose();
            if (diskForm != null && !diskForm.IsDisposed) diskForm.Dispose();
            ExitThread();
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool created;
            using (var mutex = new Mutex(true, @"Local\Roomy_SingleInstance", out created))
            {
                // перезапуск (например, после смены языка): ждём, пока старая копия закроется
                if (!created && args.Contains("--restart"))
                {
                    try { created = mutex.WaitOne(15000); }
                    catch (AbandonedMutexException) { created = true; }
                }
                if (!created)
                {
                    // Уже запущен - просим его показать окно и тихо выходим.
                    try
                    {
                        using (var ev = EventWaitHandle.OpenExisting(TrayApp.ShowEventName)) ev.Set();
                        return;
                    }
                    catch { }
                    MessageBox.Show(L.T("Roomy уже запущен — его значок в трее (возможно, под стрелкой ^).\n") +
                                    L.T("Если обновляете программу, сначала закройте старую: правый клик по значку → «Выход»."),
                                    "Roomy", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                try { Native.SetProcessDPIAware(); } catch { }
                try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) Ui.Scale = Math.Max(1f, g.DpiX / 96f); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (o, e) => Log.Write("Необработанная ошибка: " + e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (o, e) => Log.Write("Критическая ошибка: " + e.ExceptionObject);
                Application.Run(new TrayApp());
                GC.KeepAlive(mutex);
            }
        }
    }
}
