using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Roomy
{
    // окно "Что это?": объяснение, вердикт и факты о файле программы.
    class InfoForm : Form
    {
        readonly AppGroup G;
        readonly Settings S;
        readonly FlowLayoutPanel flow;
        readonly FlowLayoutPanel buttons;
        readonly Button btnFolder, btnVirusTotal, btnSearch, btnClose;
        readonly List<Label> wrapLabels = new List<Label>();
        FileFacts facts;
        Label badge;

        // для проверки интерфейса без Windows.
        public static Func<string, FileFacts> TestFacts;

        public InfoForm(AppGroup g, Settings s, Icon icon)
        {
            G = g;
            S = s;
            Text = L.T("Что это? — ") + Fmt.ShortName(g.Display);
            Font = new Font("Segoe UI", 9.5f);
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Ui.Card;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(Ui.S(560), Ui.S(600));
            MinimumSize = new Size(Ui.S(420), Ui.S(360));
            ShowInTaskbar = false;
            if (icon != null) Icon = icon;

            buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, FlowDirection = FlowDirection.LeftToRight, Height = Ui.S(52),
                Padding = new Padding(Ui.S(12), Ui.S(10), Ui.S(12), Ui.S(8)), BackColor = Ui.Back
            };
            btnFolder = MakeButton(L.T("Открыть папку"));
            btnVirusTotal = MakeButton(L.T("Проверить на VirusTotal"));
            btnSearch = MakeButton(L.T("Найти в интернете"));
            btnClose = MakeButton(L.T("Закрыть"));
            btnFolder.Click += (o, e) => OpenFolder();
            btnVirusTotal.Click += (o, e) => OpenVirusTotal();
            btnSearch.Click += (o, e) => Open("https://www.google.com/search?q=" + Uri.EscapeDataString(G.Key + L.T(".exe что это за процесс")));
            btnClose.Click += (o, e) => Close();
            btnFolder.Enabled = btnVirusTotal.Enabled = false;
            buttons.Controls.AddRange(new Control[] { btnFolder, btnVirusTotal, btnSearch, btnClose });
            CancelButton = btnClose;

            flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
                Padding = new Padding(Ui.S(18), Ui.S(14), Ui.S(18), Ui.S(10)), BackColor = Ui.Card
            };
            flow.Resize += (o, e) => Rewrap();

            Controls.Add(flow);
            Controls.Add(buttons);

            BuildStatic();
            Shown += async (o, e) =>
            {
                var test = TestFacts;
                facts = test != null ? test(G.Key) : await Task.Run(() => FileFacts.Inspect(G.Key, G.Pids));
                if (!IsDisposed) BuildFacts();
            };
        }

        Button MakeButton(string text)
        {
            return new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.System, Margin = new Padding(0, 0, Ui.S(8), 0) };
        }

        // ---------- Содержимое ----------

        KbEntry Entry { get { return ProcessKb.Find(G.Key); } }

        void BuildStatic()
        {
            flow.SuspendLayout();
            flow.Controls.Clear();
            wrapLabels.Clear();
            var e = Entry;

            AddLabel(e != null ? e.Name : Fmt.ShortName(G.Display), new Font("Segoe UI Semibold", 15f), Ui.Text, 0);
            AddLabel(G.Key + ".exe · " + Fmt.Processes(G.Pids.Count) + " · " + Fmt.Bytes(G.Bytes) + L.T(" памяти"),
                new Font("Segoe UI", 9f), Ui.Muted, 2);

            Verdict v = e != null ? e.Verdict : Verdict.Unknown;
            AddBadge(ProcessKb.VerdictTitle(v), ColorOf(v));

            if (e != null)
            {
                AddHeading(L.T("Что это"));
                AddText(e.What);
                AddHeading(L.T("Если закрыть или отключить"));
                AddText(e.IfClosed);
            }
            else
            {
                AddHeading(L.T("Что это"));
                AddText(L.T("Этой программы нет во встроенной базе Roomy. Ниже — что о ней известно из самого файла: ") +
                        L.T("кто издатель, есть ли цифровая подпись, где она лежит. Если не узнаёте программу, нажмите «Найти в интернете» ") +
                        L.T("или «Проверить на VirusTotal»."));
            }

            string why = S.UntouchableReason(G.Key);
            if (why != null && (e == null || e.Verdict != Verdict.System))
            {
                AddHeading(L.T("В Roomy"));
                AddText(L.T("Помечено как «") + why + L.T("» — Roomy её не притормаживает и не закрывает."));
            }

            AddHeading(L.T("Файл"));
            AddText(L.T("Проверяю файл и цифровую подпись…"), Ui.Muted);
            flow.ResumeLayout();
            Rewrap();
        }

        void BuildFacts()
        {
            // убрать строку "Проверяю..."
            var last = flow.Controls[flow.Controls.Count - 1];
            flow.Controls.Remove(last);
            wrapLabels.Remove(last as Label);
            last.Dispose();

            flow.SuspendLayout();
            var f = facts;
            if (f != null && f.Masquerade && badge != null)
            {
                badge.Text = L.T("ПОДОЗРИТЕЛЬНО — ВЫДАЁТ СЕБЯ ЗА ПРОЦЕСС WINDOWS");
                badge.ForeColor = Controller.Red;
                var note = new Label
                {
                    Text = L.T("Описание ниже — о настоящем системном процессе с таким именем. Но этот файл лежит не в папке Windows, ") +
                           L.T("поэтому это, скорее всего, не он. Проверьте файл на VirusTotal и компьютер антивирусом."),
                    ForeColor = Controller.Red, AutoSize = true, UseMnemonic = false, Font = Font,
                    Margin = new Padding(0, 0, 0, Ui.S(4))
                };
                flow.Controls.Add(note);
                flow.Controls.SetChildIndex(note, flow.Controls.GetChildIndex(badge) + 1);
                wrapLabels.Add(note);
            }
            if (f == null || f.Path == null)
            {
                AddText(L.T("Путь к файлу недоступен. Так бывает у системных процессов и служб, которые работают с правами выше, чем у Roomy."), Ui.Muted);
            }
            else
            {
                AddFact(L.T("Путь"), f.Path);
                if (f.Description != null) AddFact(L.T("Описание"), f.Description);
                if (f.Company != null) AddFact(L.T("Издатель"), f.Company);
                if (f.Product != null && f.Product != f.Description) AddFact(L.T("Продукт"), f.Product);
                if (f.Version != null) AddFact(L.T("Версия"), f.Version);
                AddFact(L.T("Цифровая подпись"), f.SignText,
                    f.Sign == SignState.Valid || f.Sign == SignState.Catalog ? Controller.Green :
                    f.Sign == SignState.Invalid ? Controller.Red : Ui.Text);

                if (Entry == null)
                {
                    AddHeading(L.T("Вывод"));
                    AddText(UnknownVerdict(f));
                }

                if (f.Warnings.Count > 0)
                {
                    AddHeading(L.T("Внимание"), Controller.Red);
                    foreach (var w in f.Warnings) AddText("• " + w, Controller.Red);
                }
                btnFolder.Enabled = true;
                btnVirusTotal.Enabled = true;
            }
            flow.ResumeLayout();
            Rewrap();
        }

        static string UnknownVerdict(FileFacts f)
        {
            bool signed = f.Sign == SignState.Valid || f.Sign == SignState.Catalog;
            if (f.InWindowsDir && signed)
                return L.T("Это компонент Windows: файл лежит в папке Windows и подписан. Если не знаете точно, зачем он, — лучше не трогать.");
            if (signed)
                return L.T("Обычная программа от издателя «") + (f.Signer ?? f.Company ?? L.T("неизвестно")) + L.T("» с действительной подписью. ") +
                       L.T("Подпись значит, что файл не подменён после выпуска. Нужна ли она вам — решайте по названию и издателю.");
            if (f.Warnings.Count > 0)
                return L.T("Программа без подписи и с подозрительными признаками (см. ниже). Если не узнаёте её — проверьте на VirusTotal и антивирусом.");
            return L.T("Программа без цифровой подписи. Это не обязательно плохо — многие небольшие и бесплатные программы не подписаны ") +
                   L.T("(Roomy пока тоже). Если не узнаёте её — проверьте на VirusTotal.");
        }

        static Color ColorOf(Verdict v)
        {
            switch (v)
            {
                case Verdict.System: return Color.FromArgb(90, 110, 140);
                case Verdict.Windows: return Color.FromArgb(90, 110, 140);
                case Verdict.Optional: return Controller.Orange;
                case Verdict.App: return Controller.Green;
                case Verdict.Caution: return Controller.Red;
                default: return Ui.Muted;
            }
        }

        // ---------- Элементы ----------

        Label AddLabel(string text, Font font, Color color, int bottomGap)
        {
            var l = new Label
            {
                Text = text, Font = font, ForeColor = color, AutoSize = true, UseMnemonic = false,
                Margin = new Padding(0, 0, 0, Ui.S(bottomGap))
            };
            flow.Controls.Add(l);
            wrapLabels.Add(l);
            return l;
        }

        void AddBadge(string text, Color color)
        {
            var l = badge = AddLabel(text.ToUpperInvariant(), new Font("Segoe UI Semibold", 9f), color, 4);
            l.Margin = new Padding(0, Ui.S(8), 0, Ui.S(4));
        }

        void AddHeading(string text, Color? color = null)
        {
            var l = AddLabel(text, new Font("Segoe UI Semibold", 10.5f), color ?? Ui.Text, 2);
            l.Margin = new Padding(0, Ui.S(12), 0, Ui.S(2));
        }

        void AddText(string text, Color? color = null)
        {
            AddLabel(text, Font, color ?? Color.FromArgb(60, 64, 70), 2);
        }

        void AddFact(string key, string value, Color? color = null)
        {
            var l = AddLabel(key + ": " + value, Font, color ?? Ui.Text, 2);
            l.Margin = new Padding(0, Ui.S(2), 0, Ui.S(2));
        }

        void Rewrap()
        {
            int w = flow.ClientSize.Width - flow.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth;
            if (w < 100) return;
            foreach (var l in wrapLabels) l.MaximumSize = new Size(w, 0);
        }

        // ---------- Действия ----------

        void OpenFolder()
        {
            if (facts == null || facts.Path == null) return;
            try { Process.Start("explorer.exe", "/select,\"" + facts.Path + "\""); }
            catch (Exception ex) { MessageBox.Show(this, L.T("Не удалось открыть папку: ") + ex.Message, "Roomy"); }
        }

        async void OpenVirusTotal()
        {
            if (facts == null || facts.Path == null) return;
            btnVirusTotal.Enabled = false;
            UseWaitCursor = true;
            try
            {
                // Файл никуда не отправляется: открываем страницу отчёта по его хешу.
                string hash = await Task.Run(() => facts.Sha256());
                Open("https://www.virustotal.com/gui/file/" + hash);
            }
            catch (Exception ex) { MessageBox.Show(this, L.T("Не удалось прочитать файл: ") + ex.Message, "Roomy"); }
            finally { UseWaitCursor = false; btnVirusTotal.Enabled = true; }
        }

        void Open(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, L.T("Не удалось открыть браузер: ") + ex.Message, "Roomy"); }
        }
    }
}
