using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Roomy
{
    class MainForm : Form
    {
        readonly Controller C;
        readonly Action<string, ToolTipIcon> notify;
        readonly Action openHealth, openDisk;
        readonly ContextMenuStrip toolsMenu = new ContextMenuStrip();
        readonly LinkLabel linkHealth;

        // шапка
        readonly Panel header;
        readonly Label lblPercent, lblTitle, lblSub, lblHint, lblSwitch;
        readonly MascotView bip;
        readonly ToggleSwitch sw;
        readonly MemBar bar;

        // простой вид
        readonly Panel simplePanel, banner, rowsHost, simpleFooter;
        readonly Label bannerText, simpleCaption, lblEconomy;
        readonly List<AppRow> rows = new List<AppRow>();
        readonly Button btnEconomy;
        readonly LinkLabel linkAll;
        const int RowCount = 6;

        // подробный вид
        readonly Panel detailPanel, detailBottom;
        readonly TableLayoutPanel info;
        readonly Dictionary<string, Label> infoVals = new Dictionary<string, Label>();
        readonly Sparkline spark;
        readonly ListView lv;
        readonly Dictionary<string, ListViewItem> lvItems = new Dictionary<string, ListViewItem>();
        readonly ComboBox cmbMode;
        readonly Button btnCloseApp, btnEconomy2, btnInfo;
        readonly Label lblActions, lblSelected;
        readonly ContextMenuStrip rowMenu;

        readonly Timer timer;
        readonly ToolTip tips = new ToolTip { AutoPopDelay = 20000 };
        bool closing;

        public MainForm(Controller c, Icon icon, Action<string, ToolTipIcon> notify, Action openHealth = null, Action openDisk = null)
        {
            C = c;
            this.notify = notify;
            this.openHealth = openHealth;
            this.openDisk = openDisk;
            Text = "Roomy";
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Ui.Back;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(Ui.S(560), Ui.S(520));
            if (icon != null) Icon = icon;

            // ---------- шапка ----------
            header = new Panel { Dock = DockStyle.Top, Height = Ui.S(130), BackColor = Ui.Card };
            bip = new MascotView { BackColor = Ui.Card, Size = new Size(Ui.S(70), Ui.S(70)) };
            lblPercent = new Label { AutoSize = true, Font = new Font("Segoe UI Semibold", 30f), Text = "–%" };
            lblTitle = new Label { AutoSize = true, Font = new Font("Segoe UI Semibold", 13f), ForeColor = Ui.Text };
            lblSub = new Label { AutoSize = false, AutoEllipsis = true, ForeColor = Ui.Text };
            lblHint = new Label { AutoSize = false, AutoEllipsis = true, ForeColor = Ui.Muted };
            lblSwitch = new Label { AutoSize = true, Text = L.T("Подробно"), ForeColor = Ui.Text };
            sw = new ToggleSwitch();
            bar = new MemBar();
            linkHealth = new LinkLabel { AutoSize = true, Text = L.T("Инструменты ▾"), LinkColor = Ui.Accent, Font = new Font("Segoe UI Semibold", 9f) };
            toolsMenu.Items.Add(L.T("Проверка компьютера…"), null, (o, e) => { if (this.openHealth != null) this.openHealth(); });
            toolsMenu.Items.Add(L.T("Место на диске…"), null, (o, e) => { if (this.openDisk != null) this.openDisk(); });
            linkHealth.LinkClicked += (o, e) => toolsMenu.Show(linkHealth, new Point(0, linkHealth.Height));
            linkHealth.Visible = openHealth != null || openDisk != null;
            header.Controls.AddRange(new Control[] { linkHealth, sw, lblSwitch, bip, lblPercent, lblTitle, lblSub, lblHint, bar });
            header.Resize += (o, e) => LayoutHeader();
            header.Paint += (o, e) => { using (var p = new Pen(Ui.Line)) e.Graphics.DrawLine(p, 0, header.Height - 1, header.Width, header.Height - 1); };
            lblSwitch.Click += (o, e) => sw.Checked = !sw.Checked;
            sw.CheckedChanged += (o, e) => { C.S.Detailed = sw.Checked; C.S.Save(); ApplyView(true); };

            // ---------- простой вид ----------
            simplePanel = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Back };

            rowsHost = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Card };
            for (int i = 0; i < RowCount; i++)
            {
                var r = new AppRow();
                r.ModeChanged += (row, mode) => { C.SetMode(row.Key, row.DisplayName, mode); UpdateAll(); };
                r.InfoRequested += row => { if (row.Group != null) ShowInfo(row.Group); };
                rows.Add(r);
                rowsHost.Controls.Add(r);
            }
            rowsHost.Resize += (o, e) => LayoutRows();

            simpleFooter = new Panel { Dock = DockStyle.Bottom, Height = Ui.S(94), BackColor = Ui.Back };
            btnEconomy = new Button { Text = L.T("Режим экономии"), Font = new Font("Segoe UI Semibold", 9.5f), FlatStyle = FlatStyle.System };
            lblEconomy = new Label { ForeColor = Ui.Muted, AutoSize = false };
            linkAll = new LinkLabel { Text = L.T("Все программы и подробности →"), AutoSize = true, LinkColor = Ui.Accent };
            simpleFooter.Controls.AddRange(new Control[] { btnEconomy, lblEconomy, linkAll });
            simpleFooter.Resize += (o, e) => LayoutFooter();
            btnEconomy.Click += (o, e) => ToggleEconomy();
            linkAll.LinkClicked += (o, e) => sw.Checked = true;

            simpleCaption = new Label
            {
                Dock = DockStyle.Top, Height = Ui.S(36), Text = L.T("Больше всего памяти занимают"),
                Font = new Font("Segoe UI Semibold", 10f), ForeColor = Ui.Text, Padding = new Padding(Ui.S(14), Ui.S(12), 0, 0)
            };

            banner = new Panel { Dock = DockStyle.Top, Height = Ui.S(62), BackColor = Color.FromArgb(255, 247, 222), Padding = new Padding(Ui.S(14), Ui.S(8), Ui.S(14), Ui.S(6)) };
            bannerText = new Label
            {
                Dock = DockStyle.Fill, ForeColor = Color.FromArgb(94, 72, 0),
                Text = L.T("Roomy пока ничего не трогает — он только показывает, что происходит. ") +
                       L.T("Если какую-то программу можно притормаживать, пока вы ею не пользуетесь, или закрывать, ") +
                       L.T("когда нужна память, — выберите это справа от неё.")
            };
            banner.Controls.Add(bannerText);

            simplePanel.Controls.Add(rowsHost);
            simplePanel.Controls.Add(simpleFooter);
            simplePanel.Controls.Add(simpleCaption);
            simplePanel.Controls.Add(banner);

            // ---------- подробный вид ----------
            detailPanel = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Back, Visible = false };

            info = new TableLayoutPanel
            {
                Dock = DockStyle.Top, Height = Ui.S(112), BackColor = Ui.Card, ColumnCount = 4, RowCount = 4,
                Padding = new Padding(Ui.S(14), Ui.S(10), Ui.S(14), Ui.S(8))
            };
            info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
            info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
            info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
            info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
            for (int i = 0; i < 4; i++) info.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
            AddInfo(L.T("Всего памяти"), 0, 0, L.T("Сколько оперативной памяти доступно Windows."));
            AddInfo(L.T("Занято"), 0, 1, L.T("Сколько памяти сейчас используют программы и система."));
            AddInfo(L.T("Свободно"), 0, 2, L.T("Сколько можно отдать программам прямо сейчас (включая кэш)."));
            AddInfo(L.T("Кэш Windows"), 0, 3, L.T("Недавно использованные файлы, которые Windows держит в памяти для скорости.\nЭту память система сама отдаёт программам, когда нужно — чистить её не нужно."));
            AddInfo(L.T("Выделено программам"), 2, 0, L.T("Сколько памяти программы «забронировали» в сумме (оперативная + файл подкачки)\nи предел, после которого Windows начнёт отказывать в памяти."));
            AddInfo(L.T("Подкачка с диска"), 2, 1, L.T("Сколько данных Windows читает с диска в память (среднее за 30 секунд).\nПри запуске программ это нормально. Если памяти мало и чисел много —\nданные возвращаются из файла подкачки, и это главная причина тормозов."));
            AddInfo(L.T("Процессов"), 2, 2, L.T("Сколько процессов запущено в системе."));
            AddInfo(L.T("Притормаживается"), 2, 3, L.T("Сколько программ с режимом «Притормаживать в фоне» сейчас притормаживаются."));

            var chartCaption = new Label
            {
                Dock = DockStyle.Top, Height = Ui.S(30), Text = L.T("Загрузка памяти за 10 минут (пунктир — порог уведомления)"),
                Font = new Font("Segoe UI Semibold", 9.5f), ForeColor = Ui.Text, Padding = new Padding(Ui.S(14), Ui.S(9), 0, 0)
            };
            spark = new Sparkline { Dock = DockStyle.Top, Height = Ui.S(110) };

            var listCaption = new Label
            {
                Dock = DockStyle.Top, Height = Ui.S(30), Text = L.T("Все программы — двойной клик: что это за программа; правый клик: режим или закрыть"),
                Font = new Font("Segoe UI Semibold", 9.5f), ForeColor = Ui.Text, Padding = new Padding(Ui.S(14), Ui.S(9), 0, 0)
            };

            lv = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false,
                HideSelection = false, BorderStyle = BorderStyle.None, BackColor = Ui.Card
            };
            lv.Columns.Add(L.T("Программа"), Ui.S(250));
            lv.Columns.Add(L.T("Память"), Ui.S(80), HorizontalAlignment.Right);
            lv.Columns.Add(L.T("За 10 мин"), Ui.S(80), HorizontalAlignment.Right);
            lv.Columns.Add(L.T("Процессов"), Ui.S(84), HorizontalAlignment.Right);
            lv.Columns.Add(L.T("Режим"), Ui.S(160));
            lv.Columns.Add(L.T("Сейчас"), Ui.S(160));
            lv.ListViewItemSorter = new BytesComparer();
            lv.SelectedIndexChanged += (o, e) => UpdateSelection();
            lv.DoubleClick += (o, e) => { var sg = SelectedGroup(); if (sg != null) ShowInfo(sg); };

            rowMenu = new ContextMenuStrip();
            var miInfo = new ToolStripMenuItem(L.T("Что это?"), null, (o, e) => { var sg = SelectedGroup(); if (sg != null) ShowInfo(sg); });
            miInfo.Font = new Font(miInfo.Font, FontStyle.Bold);
            rowMenu.Items.Add(miInfo);
            rowMenu.Items.Add(new ToolStripSeparator());
            var modeItems = new ToolStripMenuItem[Settings.ModeNames.Length];
            for (int m = 0; m < Settings.ModeNames.Length; m++)
            {
                int mode = m;
                modeItems[m] = new ToolStripMenuItem(Settings.ModeNames[m], null, (o, e) => SetSelectedMode(mode));
                rowMenu.Items.Add(modeItems[m]);
            }
            rowMenu.Items.Add(new ToolStripSeparator());
            var miClose = new ToolStripMenuItem(L.T("Закрыть программу…"), null, (o, e) => CloseSelected());
            rowMenu.Items.Add(miClose);
            rowMenu.Opening += (o, e) =>
            {
                var g = SelectedGroup();
                if (g == null) { e.Cancel = true; return; }
                bool prot = C.S.Untouchable(g.Key);
                int cur = C.S.ModeOf(g.Key);
                for (int m = 0; m < modeItems.Length; m++)
                {
                    modeItems[m].Checked = m == cur; modeItems[m].Enabled = !prot;
                }
                miClose.Enabled = !prot;
            };
            lv.ContextMenuStrip = rowMenu;

            detailBottom = new Panel { Dock = DockStyle.Bottom, Height = Ui.S(128), BackColor = Ui.Back };
            lblSelected = new Label { AutoSize = false, ForeColor = Ui.Text, Text = L.T("Выберите программу в списке") };
            cmbMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Enabled = false };
            cmbMode.Items.AddRange(Settings.ModeNames);
            cmbMode.SelectionChangeCommitted += (o, e) => SetSelectedMode(cmbMode.SelectedIndex);
            btnInfo = new Button { Text = L.T("Что это?"), Enabled = false, FlatStyle = FlatStyle.System };
            btnInfo.Click += (o, e) => { var sg = SelectedGroup(); if (sg != null) ShowInfo(sg); };
            btnCloseApp = new Button { Text = L.T("Закрыть программу"), Enabled = false, FlatStyle = FlatStyle.System };
            btnCloseApp.Click += (o, e) => CloseSelected();
            btnEconomy2 = new Button { Text = L.T("Режим экономии"), FlatStyle = FlatStyle.System };
            btnEconomy2.Click += (o, e) => ToggleEconomy();
            lblActions = new Label { AutoSize = false, ForeColor = Ui.Muted };
            detailBottom.Controls.AddRange(new Control[] { lblSelected, cmbMode, btnInfo, btnCloseApp, btnEconomy2, lblActions });
            detailBottom.Resize += (o, e) => LayoutDetailBottom();

            detailPanel.Controls.Add(lv);
            detailPanel.Controls.Add(detailBottom);
            detailPanel.Controls.Add(listCaption);
            detailPanel.Controls.Add(spark);
            detailPanel.Controls.Add(chartCaption);
            detailPanel.Controls.Add(info);

            Controls.Add(simplePanel);
            Controls.Add(detailPanel);
            Controls.Add(header);

            tips.SetToolTip(btnEconomy, L.T("Закрывает программы с режимом «Можно закрывать». Нажмите ещё раз — они запустятся обратно."));
            tips.SetToolTip(btnEconomy2, L.T("Закрывает программы с режимом «Можно закрывать». Нажмите ещё раз — они запустятся обратно."));
            tips.SetToolTip(cmbMode,
                L.T("Не трогать — Roomy ничего не делает с программой.\n") +
                L.T("Притормаживать в фоне — пока вы в ней не работаете, память в первую очередь забирается у неё;\n") +
                L.T("   если долго не открывали и памяти мало — выгружается из памяти (не закрывается).\n") +
                L.T("Можно закрывать — то же самое, плюс её закрывает «Режим экономии» и потом запускает обратно."));

            sw.Checked = C.S.Detailed;
            ApplyView(false);

            timer = new Timer { Interval = 3000 };
            timer.Tick += (o, e) => { if (Visible && WindowState != FormWindowState.Minimized) { C.RefreshGroups(); UpdateAll(); } };
            Shown += (o, e) => { C.RefreshGroups(); UpdateAll(); timer.Start(); };
            VisibleChanged += (o, e) => { if (Visible) timer.Start(); else timer.Stop(); };
            C.Updated += OnControllerUpdated;
            FormClosed += (o, e) => { closing = true; C.Updated -= OnControllerUpdated; timer.Dispose(); tips.Dispose(); };
        }

        void OnControllerUpdated()
        {
            if (closing || !Visible || IsDisposed) return;
            UpdateHeader();
            if (C.S.Detailed) UpdateInfo();
        }

        // ---------- раскладка ----------

        void LayoutHeader()
        {
            int pad = Ui.S(16);
            bip.Location = new Point(Ui.S(16), Ui.S(14));
            lblPercent.Location = new Point(bip.Right + Ui.S(6), Ui.S(12));
            int x = lblPercent.Right + Ui.S(10);
            sw.Location = new Point(header.Width - sw.Width - pad, Ui.S(18));
            lblSwitch.Location = new Point(sw.Left - lblSwitch.Width - Ui.S(6), sw.Top + (sw.Height - lblSwitch.Height) / 2);
            // "Проверка компьютера" - в одной строке с переключателем, слева от него
            linkHealth.Location = new Point(lblSwitch.Left - linkHealth.Width - Ui.S(18), lblSwitch.Top);
            int textW = Math.Max(60, header.Width - x - pad);
            lblTitle.Location = new Point(x, Ui.S(14));
            lblSub.SetBounds(x, Ui.S(42), textW, lblSub.PreferredHeight);
            lblHint.SetBounds(x, Ui.S(62), textW, lblHint.PreferredHeight);
            // если заголовок длинный и окно узкое - ссылка уходит под полосу памяти справа
            if (linkHealth.Visible && lblTitle.Right + Ui.S(12) > linkHealth.Left)
                linkHealth.Location = new Point(header.Width - pad - linkHealth.Width, Ui.S(108));
            bar.SetBounds(pad, Ui.S(96), Math.Max(10, header.Width - pad * 2), Ui.S(10));
        }

        void LayoutRows()
        {
            int y = 0;
            foreach (var r in rows)
            {
                r.SetBounds(0, y, rowsHost.ClientSize.Width, r.Height);
                if (r.Visible) y += r.Height;
            }
        }

        void LayoutFooter()
        {
            int pad = Ui.S(14);
            btnEconomy.SetBounds(pad, Ui.S(12), Ui.S(200), Ui.S(36));
            lblEconomy.SetBounds(btnEconomy.Right + Ui.S(12), Ui.S(8), Math.Max(50, simpleFooter.Width - btnEconomy.Right - Ui.S(12) - pad), Ui.S(54));
            linkAll.Location = new Point(pad, Ui.S(64));
        }

        void LayoutDetailBottom()
        {
            int pad = Ui.S(14);
            lblSelected.SetBounds(pad, Ui.S(10), Ui.S(300), Ui.S(20));
            cmbMode.SetBounds(pad, Ui.S(34), Ui.S(200), Ui.S(28));
            btnInfo.SetBounds(cmbMode.Right + Ui.S(8), Ui.S(32), Ui.S(100), Ui.S(30));
            btnCloseApp.SetBounds(btnInfo.Right + Ui.S(8), Ui.S(32), Ui.S(160), Ui.S(30));
            btnEconomy2.SetBounds(detailBottom.Width - pad - Ui.S(200), Ui.S(32), Ui.S(200), Ui.S(30));
            lblActions.SetBounds(pad, Ui.S(70), Math.Max(50, detailBottom.Width - pad * 2), Ui.S(54));
        }

        void AddInfo(string key, int col, int row, string tip)
        {
            var k = new Label { Text = key, ForeColor = Ui.Muted, AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            var v = new Label { Text = "…", ForeColor = Ui.Text, Font = new Font("Segoe UI Semibold", 9f), AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            info.Controls.Add(k, col, row);
            info.Controls.Add(v, col + 1, row);
            tips.SetToolTip(k, tip);
            tips.SetToolTip(v, tip);
            infoVals[key] = v;
        }

        void ApplyView(bool resize)
        {
            bool d = C.S.Detailed;
            SuspendLayout();
            simplePanel.Visible = !d;
            detailPanel.Visible = d;
            if (WindowState == FormWindowState.Normal)
            {
                var wa = Screen.FromControl(this).WorkingArea;
                var target = d ? new Size(Ui.S(880), Ui.S(840)) : new Size(Ui.S(620), Ui.S(684));
                target = new Size(Math.Min(target.Width, wa.Width - Ui.S(20)), Math.Min(target.Height, wa.Height - Ui.S(40)));
                ClientSize = target;
                if (resize)
                {
                    if (Bottom > wa.Bottom) Top = Math.Max(wa.Top, wa.Bottom - Height);
                    if (Right > wa.Right) Left = Math.Max(wa.Left, wa.Right - Width);
                }
            }
            ResumeLayout(true);
            if (Visible) UpdateAll();
        }

        // ---------- обновление ----------

        public void UpdateAll()
        {
            if (closing || IsDisposed) return;
            UpdateHeader();
            if (C.S.Detailed) { UpdateInfo(); UpdateList(); }
            else UpdateSimple();
            UpdateEconomy();
        }

        void UpdateHeader()
        {
            var st = C.GetStatus();
            var m = C.Mem;
            lblPercent.Text = m.dwMemoryLoad + "%";
            lblPercent.ForeColor = st.Color;
            bip.Mood = Mascot.MoodOf(st);
            lblTitle.Text = st.Title;
            lblTitle.ForeColor = st.Color;
            lblSub.Text = L.T("Занято ") + Fmt.Bytes(C.UsedBytes) + L.T(" из ") + Fmt.Bytes((long)m.ullTotalPhys) +
                          L.T(" · свободно ") + Fmt.Bytes((long)m.ullAvailPhys);
            lblHint.Text = st.Hint + (C.EconomyOn ? L.T("  Режим экономии включён.") : "");
            bar.Set(m.dwMemoryLoad / 100f, st.Color);
            LayoutHeader();
        }

        void UpdateSimple()
        {
            banner.Visible = C.S.Background.Count == 0;
            var list = C.Groups.Where(g => !C.S.Untouchable(g.Key)).Take(RowCount).ToList();
            long total = (long)C.Mem.ullTotalPhys;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (i < list.Count)
                {
                    if (r.Interacting) continue;
                    var g = list[i];
                    r.SetData(g, total, C.S.ModeOf(g.Key), C.M.StateOf(g.Key), C.S.Untouchable(g.Key), C.H.Growth(g.Key, g.Bytes));
                    r.Visible = true;
                }
                else r.Visible = false;
            }
            LayoutRows();
        }

        void UpdateEconomy()
        {
            string btnText;
            string hint;
            bool enabled = true;
            if (C.EconomyOn)
            {
                btnText = L.T("Вернуть закрытые программы");
                hint = L.T("Режим экономии включён. Нажмите, чтобы запустить закрытые программы обратно.");
            }
            else if (C.S.Economy.Count == 0)
            {
                btnText = L.T("Режим экономии");
                enabled = false;
                hint = L.T("Закрывает программы с режимом «Можно закрывать». Таких пока нет — выберите этот режим у программ выше.");
            }
            else
            {
                btnText = L.T("Режим экономии");
                var cand = C.EconomyCandidates();
                hint = cand.Count == 0
                    ? L.T("Из программ с режимом «Можно закрывать» сейчас ничего не запущено.")
                    : L.T("Закроет: ") + string.Join(", ", cand) + L.T(". Нажмёте ещё раз — запустятся обратно.");
                enabled = cand.Count > 0;
            }
            btnEconomy.Text = btnEconomy2.Text = btnText;
            btnEconomy.Enabled = btnEconomy2.Enabled = enabled;
            lblEconomy.Text = hint;
        }

        void UpdateInfo()
        {
            var m = C.Mem;
            var p = C.Perf;
            infoVals[L.T("Всего памяти")].Text = Fmt.Bytes((long)m.ullTotalPhys);
            infoVals[L.T("Занято")].Text = Fmt.Bytes(C.UsedBytes) + " (" + m.dwMemoryLoad + "%)";
            infoVals[L.T("Свободно")].Text = Fmt.Bytes((long)m.ullAvailPhys);
            infoVals[L.T("Кэш Windows")].Text = p.cb > 0 ? Fmt.Bytes(Native.Pages(p.SystemCache, p.PageSize)) : "—";
            infoVals[L.T("Выделено программам")].Text = p.cb > 0
                ? Fmt.Bytes(Native.Pages(p.CommitTotal, p.PageSize)) + L.T(" из ") + Fmt.Bytes(Native.Pages(p.CommitLimit, p.PageSize))
                : "—";
            var pages = infoVals[L.T("Подкачка с диска")];
            pages.Text = C.PagesInText();
            pages.ForeColor = C.PagingHurts ? Controller.Red : (C.PagesIn >= 50 && C.Mem.dwMemoryLoad >= C.S.AlertThreshold) ? Controller.Orange : Ui.Text;
            infoVals[L.T("Процессов")].Text = p.cb > 0 ? p.ProcessCount.ToString() : "—";
            int lowered = C.Groups.Count(g => C.M.IsLowered(g.Key));
            infoVals[L.T("Притормаживается")].Text = lowered == 0 ? L.T("ничего") : lowered + L.T(" прогр.");
            spark.SetData(C.H.Load, C.S.AlertThreshold);
            lblActions.Text = C.Actions.Count == 0
                ? L.T("Последние действия: пока ничего не делалось.")
                : L.T("Последние действия:\n") + string.Join("\n", C.Actions.Take(3));
        }

        void UpdateList()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            lv.BeginUpdate();
            foreach (var g in C.Groups)
            {
                seen.Add(g.Key);
                ListViewItem it;
                if (!lvItems.TryGetValue(g.Key, out it))
                {
                    it = new ListViewItem(new[] { "", "", "", "", "", "" }) { UseItemStyleForSubItems = false };
                    lvItems[g.Key] = it;
                    lv.Items.Add(it);
                }
                it.Tag = g;
                bool prot = C.S.Untouchable(g.Key);
                long? growth = C.H.Growth(g.Key, g.Bytes);
                Set(it, 0, g.Display);
                Set(it, 1, Fmt.Bytes(g.Bytes));
                Set(it, 2, growth.HasValue ? Fmt.Signed(growth.Value) : "…");
                it.SubItems[2].ForeColor = growth.HasValue && growth.Value > 200L * 1024 * 1024 ? Controller.Red : Ui.Muted;
                Set(it, 3, g.Pids.Count.ToString());
                Set(it, 4, prot ? "—" : Settings.ModeNames[C.S.ModeOf(g.Key)]);
                Set(it, 5, C.M.StateOf(g.Key));
                Color fc = prot ? Ui.Muted : Ui.Text;
                it.SubItems[0].ForeColor = fc;
                it.SubItems[1].ForeColor = fc;
                it.SubItems[4].ForeColor = C.S.ModeOf(g.Key) == Settings.ModeNone ? Ui.Muted : Ui.Accent;
            }
            foreach (var key in lvItems.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                lv.Items.Remove(lvItems[key]);
                lvItems.Remove(key);
            }
            lv.Sort();
            lv.EndUpdate();
            UpdateSelection();
        }

        static void Set(ListViewItem it, int i, string text)
        {
            if (it.SubItems[i].Text != text) it.SubItems[i].Text = text;
        }

        class BytesComparer : System.Collections.IComparer
        {
            public int Compare(object x, object y)
            {
                var a = ((ListViewItem)x).Tag as AppGroup;
                var b = ((ListViewItem)y).Tag as AppGroup;
                long ba = a == null ? 0 : a.Bytes, bb = b == null ? 0 : b.Bytes;
                return bb.CompareTo(ba);
            }
        }

        // ---------- действия ----------

        AppGroup SelectedGroup()
        {
            return lv.SelectedItems.Count > 0 ? lv.SelectedItems[0].Tag as AppGroup : null;
        }

        void UpdateSelection()
        {
            var g = SelectedGroup();
            if (g == null)
            {
                lblSelected.Text = L.T("Выберите программу в списке");
                cmbMode.Enabled = false; btnCloseApp.Enabled = false; btnInfo.Enabled = false;
                if (!cmbMode.DroppedDown) cmbMode.SelectedIndex = -1;
                return;
            }
            bool prot = C.S.Untouchable(g.Key);
            string why = C.S.UntouchableReason(g.Key);
            lblSelected.Text = Fmt.ShortName(g.Display) + (prot ? " — " + why + L.T(", не трогаем") : L.T(" — режим:"));
            cmbMode.Enabled = !prot;
            btnCloseApp.Enabled = !prot;
            btnInfo.Enabled = true;
            if (!cmbMode.DroppedDown) cmbMode.SelectedIndex = prot ? -1 : C.S.ModeOf(g.Key);
        }

        void ShowInfo(AppGroup g)
        {
            var f = new InfoForm(g, C.S, Icon);
            f.Show(this);
        }

        void SetSelectedMode(int mode)
        {
            var g = SelectedGroup();
            if (g == null || mode < 0 || C.S.Untouchable(g.Key)) return;
            C.SetMode(g.Key, g.Display, mode);
            UpdateAll();
        }

        async void CloseSelected()
        {
            var g = SelectedGroup();
            if (g == null || C.S.Untouchable(g.Key)) return;
            string name = Fmt.ShortName(g.Display);
            var r = MessageBox.Show(this,
                L.T("Закрыть «") + name + L.T("»?\n\nСначала программа получит обычную команду закрыться. Если через 3 секунды ") +
                L.T("она всё ещё работает, её процессы будут завершены — несохранённые данные в ней пропадут."),
                "Roomy", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (r != DialogResult.OK) return;
            btnCloseApp.Enabled = false;
            Cursor = Cursors.WaitCursor;
            try
            {
                string msg = await MemInfo.CloseGroupAsync(g.Key, name);
                C.AddAction(msg);
                await Task.Delay(800);
                C.RefreshGroups();
                UpdateAll();
            }
            finally { Cursor = Cursors.Default; }
        }

        void ToggleEconomy()
        {
            string msg;
            if (C.EconomyOn) msg = C.ExitEconomy();
            else
            {
                var cand = C.EconomyCandidates();
                if (cand.Count == 0) { UpdateEconomy(); return; }
                var r = MessageBox.Show(this, L.T("Закрыть на время: ") + string.Join(", ", cand) +
                    L.T("?\n\nКогда нажмёте кнопку ещё раз, они запустятся обратно."),
                    L.T("Roomy — режим экономии"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (r != DialogResult.OK) return;
                msg = C.EnterEconomy();
            }
            notify(msg, ToolTipIcon.Info);
            C.RefreshGroups();
            UpdateAll();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Закрытие окна не выключает программу - она остаётся в трее.
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }
    }
}
