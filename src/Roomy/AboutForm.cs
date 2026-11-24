using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace Roomy
{
    static class Project
    {
        public const string Author = "skriptkiddo";
        public const string Url = "https://github.com/skriptkiddo1/roomy";
        public const string IssuesUrl = Url + "/issues";

        public static string Version
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return v.Major + "." + v.Minor + "." + v.Build;
            }
        }

        public static void Open(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Write("Не удалось открыть ссылку: " + ex.Message); }
        }
    }

    // "О Roomy": версия, автор, ссылки. Робот в окне живой - его можно потыкать.
    class AboutForm : Form
    {
        public AboutForm(Icon icon)
        {
            Text = L.T("О Roomy");
            Font = new Font("Segoe UI", 9.5f);
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Ui.Card;
            ClientSize = new Size(Ui.S(440), Ui.S(280));
            if (icon != null) Icon = icon;

            var mascot = new MascotView { BackColor = Ui.Card, Bounds = new Rectangle(Ui.S(24), Ui.S(24), Ui.S(96), Ui.S(96)), Chatty = true };
            int x = Ui.S(140);
            var name = new Label { AutoSize = true, Text = "Roomy " + Project.Version, Font = new Font("Segoe UI Semibold", 18f), ForeColor = Ui.Text, Location = new Point(x, Ui.S(24)) };
            var what = new Label
            {
                AutoSize = false, ForeColor = Ui.Muted, Location = new Point(x, Ui.S(64)), Size = new Size(Ui.S(280), Ui.S(56)),
                Text = L.T("Помощник для слабых компьютеров: память, место на диске и понятные объяснения.")
            };
            var author = new Label
            {
                AutoSize = true, ForeColor = Ui.Text, Location = new Point(x, Ui.S(124)),
                Text = L.T("Сделал ") + Project.Author + L.T(" (местоимения: broski)")
            };
            var site = new LinkLabel { AutoSize = true, Text = L.T("Страница проекта на GitHub"), LinkColor = Ui.Accent, Location = new Point(x, Ui.S(152)) };
            site.LinkClicked += (o, e) => Project.Open(Project.Url);
            var bug = new LinkLabel { AutoSize = true, Text = L.T("Нашли ошибку? Расскажите"), LinkColor = Ui.Accent, Location = new Point(x, Ui.S(176)) };
            bug.LinkClicked += (o, e) => Project.Open(Project.IssuesUrl);
            var lic = new Label
            {
                AutoSize = false, ForeColor = Ui.Muted, Location = new Point(x, Ui.S(204)), Size = new Size(Ui.S(280), Ui.S(22)),
                Text = L.T("Лицензия GPL-3.0. Без рекламы и слежки.")
            };
            var hint = new Label
            {
                AutoSize = false, ForeColor = Ui.Muted, Location = new Point(Ui.S(16), Ui.S(126)), Size = new Size(Ui.S(112), Ui.S(20)),
                TextAlign = ContentAlignment.MiddleCenter, Text = L.T("нажми на меня"), Font = new Font("Segoe UI", 8f)
            };
            var ok = new Button { Text = L.T("Закрыть"), FlatStyle = FlatStyle.System, DialogResult = DialogResult.OK, Size = new Size(Ui.S(96), Ui.S(30)) };
            ok.Location = new Point(ClientSize.Width - ok.Width - Ui.S(16), ClientSize.Height - ok.Height - Ui.S(12));
            AcceptButton = ok;
            CancelButton = ok;
            Controls.AddRange(new Control[] { mascot, hint, name, what, author, site, bug, lic, ok });
        }
    }
}
