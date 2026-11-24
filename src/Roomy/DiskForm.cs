using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Roomy
{
    // панель с прокруткой, которая не прыгает к элементу, получившему фокус.
    class StablePanel : Panel
    {
        protected override Point ScrollToControl(Control activeControl) { return DisplayRectangle.Location; }
    }

    static class TextFit
    {
        // задать метке ширину и высоту под перенесённый по словам текст.
        public static void Fit(Label l, int width)
        {
            l.AutoSize = false;
            var sz = TextRenderer.MeasureText(l.Text ?? "", l.Font, new Size(width, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);
            l.Size = new Size(width, Math.Max(sz.Height, l.Font.Height));
        }
    }

    // строка категории очистки: галочка, размер, объяснение, ссылка на папку.
    class CleanRow : Panel
    {
        public readonly CleanCategory C;
        public readonly CheckBox Check;
        readonly Label size, explain;
        readonly LinkLabel open;
        public event Action Changed;

        public CleanRow(CleanCategory c)
        {
            C = c;
            BackColor = Ui.Card;
            Check = new CheckBox
            {
                Text = c.Title, AutoSize = true, Checked = c.DefaultOn, Font = new Font("Segoe UI Semibold", 10f),
                ForeColor = Ui.Text, Enabled = c.Mode != CleanMode.InfoOnly
            };
            if (c.Mode == CleanMode.InfoOnly) Check.Checked = false;
            Check.CheckedChanged += (o, e) => { var h = Changed; if (h != null) h(); };
            size = new Label { AutoSize = true, Font = new Font("Segoe UI Semibold", 10f), ForeColor = Ui.Text, Text = c.Mode == CleanMode.InfoOnly ? "" : L.T("считаю…") };
            explain = new Label { AutoSize = true, ForeColor = Ui.Muted, Text = c.Explain, UseMnemonic = false };
            string linkText = c.Mode == CleanMode.InfoOnly ? L.T("Открыть «Память» в Параметрах") :
                              c.Mode == CleanMode.EmptyBin ? L.T("Открыть корзину") : L.T("Открыть папку");
            open = new LinkLabel { Text = linkText, AutoSize = true, LinkColor = Ui.Accent };
            open.LinkClicked += (o, e) => OpenTarget();
            Controls.AddRange(new Control[] { Check, size, explain, open });
        }

        void OpenTarget()
        {
            string target = C.Mode == CleanMode.InfoOnly ? C.ActionTarget :
                            C.Mode == CleanMode.EmptyBin ? "shell:RecycleBinFolder" : C.OpenPath;
            if (target == null) return;
            try { Process.Start(new ProcessStartInfo(C.Mode == CleanMode.EmptyBin ? "explorer.exe" : target) { UseShellExecute = true, Arguments = C.Mode == CleanMode.EmptyBin ? target : "" }); }
            catch (Exception ex) { MessageBox.Show(L.T("Не удалось открыть: ") + ex.Message, "Roomy"); }
        }

        public void Refresh(bool measured)
        {
            if (C.Mode == CleanMode.InfoOnly) { size.Text = ""; return; }
            if (!measured) { size.Text = L.T("считаю…"); return; }
            size.Text = C.Size > 0 ? Fmt.Bytes(C.Size) : L.T("пусто");
            size.ForeColor = C.Size >= 500L * 1024 * 1024 ? Controller.Orange : Ui.Text;
            string text = C.Explain;
            if (C.Busy != null) text += L.T("\nСейчас запущены: ") + C.Busy + L.T(" — их занятые файлы будут пропущены. Для полной очистки закройте эти программы.");
            explain.Text = text;
            Check.Enabled = C.Size > 0;
            if (C.Size == 0) Check.Checked = false;
            open.Visible = C.Mode == CleanMode.EmptyBin || C.OpenPath != null;
        }

        public int LayoutRow(int width)
        {
            int pad = Ui.S(12);
            Width = width;
            Check.Location = new Point(pad, Ui.S(8));
            size.Location = new Point(width - pad - size.PreferredWidth, Ui.S(10));
            TextFit.Fit(explain, Math.Max(80, width - pad * 2 - Ui.S(20)));
            explain.Location = new Point(pad + Ui.S(20), Check.Bottom + Ui.S(2));
            open.Location = new Point(pad + Ui.S(20), explain.Bottom + Ui.S(3));
            int h = (open.Visible ? open.Bottom : explain.Bottom) + Ui.S(10);
            Height = h;
            return h;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Ui.Line)) e.Graphics.DrawLine(pen, Ui.S(12), Height - 1, Width - Ui.S(12), Height - 1);
        }
    }

    // окно "Место на диске".
    class DiskForm : Form
    {
        readonly Panel header, body;
        readonly Label lblTitle, lblDrive, lblCleanHead, lblMapHead, lblMapStatus, lblResult;
        readonly MemBar driveBar;
        readonly Button btnClean, btnRescan;
        readonly List<CleanRow> rows = new List<CleanRow>();
        readonly ListView lv;
        readonly ContextMenuStrip lvMenu;
        CancellationTokenSource cts = new CancellationTokenSource();
        bool measuring, mapping, cleaning;
        void SyncButtons()
        {
            btnRescan.Enabled = !measuring && !mapping && !cleaning;
            UpdateCleanButton();
        }

        // для проверки интерфейса без Windows.
        public static Func<List<CleanCategory>> TestCategories;
        public static Func<DiskMapResult> TestMap;

        public DiskForm(Icon icon)
        {
            Text = L.T("Roomy — место на диске");
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Ui.Back;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(Ui.S(560), Ui.S(480));
            if (icon != null) Icon = icon;
            var wa = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(Ui.S(760), wa.Width - Ui.S(20)), Math.Min(Ui.S(860), wa.Height - Ui.S(40)));

            header = new Panel { Dock = DockStyle.Top, Height = Ui.S(96), BackColor = Ui.Card };
            lblTitle = new Label { AutoSize = true, Text = L.T("Место на диске"), Font = new Font("Segoe UI Semibold", 15f), ForeColor = Ui.Text };
            lblDrive = new Label { AutoSize = true, ForeColor = Ui.Text };
            driveBar = new MemBar();
            btnRescan = new Button { Text = L.T("Проверить снова"), AutoSize = true, FlatStyle = FlatStyle.System };
            btnRescan.Click += (o, e) => RunAll();
            header.Controls.AddRange(new Control[] { lblTitle, lblDrive, driveBar, btnRescan });
            header.Resize += (o, e) => LayoutHeader();
            header.Paint += (o, e) => { using (var p = new Pen(Ui.Line)) e.Graphics.DrawLine(p, 0, header.Height - 1, header.Width, header.Height - 1); };

            body = new StablePanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Ui.Back };
            body.Resize += (o, e) => LayoutBody();

            lblCleanHead = Heading(L.T("Можно безопасно очистить"));
            foreach (var c in (TestCategories != null ? TestCategories() : DiskClean.Build()))
            {
                var r = new CleanRow(c);
                r.Changed += UpdateCleanButton;
                rows.Add(r);
                body.Controls.Add(r);
            }
            btnClean = new Button { Text = L.T("Очистить выбранное"), AutoSize = true, FlatStyle = FlatStyle.System, Font = new Font("Segoe UI Semibold", 9.5f), Enabled = false };
            btnClean.Click += (o, e) => CleanSelected();
            lblResult = new Label { AutoSize = true, ForeColor = Ui.Muted, UseMnemonic = false };
            body.Controls.Add(btnClean);
            body.Controls.Add(lblResult);

            lblMapHead = Heading(L.T("Что занимает больше всего"));
            lblMapStatus = new Label { AutoSize = true, ForeColor = Ui.Muted, UseMnemonic = false,
                Text = L.T("Сканирую диск — это может занять минуту. Ничего не удаляется.") };
            body.Controls.Add(lblMapStatus);

            lv = new ListView { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, BorderStyle = BorderStyle.FixedSingle };
            lv.Columns.Add(L.T("Где"), Ui.S(440));
            lv.Columns.Add(L.T("Размер"), Ui.S(80), HorizontalAlignment.Right);
            lv.Columns.Add(L.T("Что это"), Ui.S(240));
            lv.Groups.Add(new ListViewGroup("folders", L.T("Крупные папки")));
            lv.Groups.Add(new ListViewGroup("files", L.T("Крупные файлы (от 200 МБ)")));
            lv.Groups.Add(new ListViewGroup("dups", L.T("Одинаковые файлы — лишние копии")));
            lv.DoubleClick += (o, e) => ShowSelected();
            lvMenu = new ContextMenuStrip();
            lvMenu.Items.Add(L.T("Показать в Проводнике"), null, (o, e) => ShowSelected());
            lv.ContextMenuStrip = lvMenu;
            body.Controls.Add(lv);

            Controls.Add(body);
            Controls.Add(header);
            Shown += (o, e) => RunAll();
            FormClosed += (o, e) => cts.Cancel();
            UpdateDrive();
        }

        Label Heading(string text)
        {
            var l = new Label { AutoSize = true, Text = text, Font = new Font("Segoe UI Semibold", 11f), ForeColor = Ui.Text };
            body.Controls.Add(l);
            return l;
        }

        // ---------- Раскладка ----------

        void LayoutHeader()
        {
            int pad = Ui.S(16);
            lblTitle.Location = new Point(pad, Ui.S(10));
            btnRescan.Location = new Point(header.Width - pad - btnRescan.Width, Ui.S(14));
            lblDrive.Location = new Point(pad, Ui.S(50));
            driveBar.SetBounds(pad, Ui.S(74), Math.Max(10, header.Width - pad * 2), Ui.S(10));
        }

        void LayoutBody()
        {
            int pad = Ui.S(14);
            int scroll = body.VerticalScroll.Visible ? 0 : SystemInformation.VerticalScrollBarWidth;
            int w = body.ClientSize.Width - pad * 2 - scroll - Ui.S(4);
            if (w < 100) return;
            int y = body.AutoScrollPosition.Y + pad;
            body.SuspendLayout();
            lblCleanHead.Location = new Point(pad, y); y = lblCleanHead.Bottom + Ui.S(6);
            foreach (var r in rows)
            {
                r.Location = new Point(pad, y);
                y += r.LayoutRow(w);
            }
            y += Ui.S(10);
            btnClean.Location = new Point(pad, y);
            TextFit.Fit(lblResult, Math.Max(80, w - btnClean.Width - Ui.S(12)));
            lblResult.Location = new Point(btnClean.Right + Ui.S(12), y + Ui.S(6));
            y = Math.Max(btnClean.Bottom, lblResult.Bottom) + Ui.S(22);
            lblMapHead.Location = new Point(pad, y); y = lblMapHead.Bottom + Ui.S(4);
            TextFit.Fit(lblMapStatus, w);
            lblMapStatus.Location = new Point(pad, y); y = lblMapStatus.Bottom + Ui.S(6);
            lv.SetBounds(pad, y, w, Ui.S(380));
            lv.Columns[0].Width = Math.Max(Ui.S(200), w - Ui.S(80) - Ui.S(240) - Ui.S(24));
            body.ResumeLayout(true);
        }

        // ---------- Данные ----------

        void UpdateDrive()
        {
            try
            {
                var d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                long free = d.AvailableFreeSpace, total = d.TotalSize;
                float used = total > 0 ? 1f - (float)free / total : 0;
                lblDrive.Text = L.T("Диск ") + d.Name.TrimEnd('\\') + L.T("  свободно ") + Fmt.Bytes(free) + L.T(" из ") + Fmt.Bytes(total);
                driveBar.Set(used, used >= 0.92f ? Controller.Red : used >= 0.8f ? Controller.Orange : Controller.Green);
            }
            catch { lblDrive.Text = ""; }
            LayoutHeader();
        }

        async void RunAll()
        {
            if (measuring || mapping || cleaning) return;
            measuring = true;
            SyncButtons();
            cts.Cancel();
            cts = new CancellationTokenSource();
            var ct = cts.Token;
            lblResult.Text = "";
            foreach (var r in rows) r.Refresh(false);
            UpdateCleanButton();
            LayoutBody();
            try
            {
                foreach (var r in rows)
                {
                    var cat = r.C;
                    if (TestCategories == null) await Task.Run(() => DiskClean.Measure(cat, ct));
                    if (IsDisposed || ct.IsCancellationRequested) return;
                    r.Refresh(true);
                }
                measuring = false;
                mapping = true;
                SyncButtons();
                LayoutBody();

                lblMapStatus.Text = L.T("Сканирую диск — это может занять минуту. Ничего не удаляется.");
                DiskMapResult map;
                if (TestMap != null) map = TestMap();
                else
                {
                    var ui = SynchronizationContext.Current;
                    map = await Task.Run(() => DiskMap.Scan((n, bytes) =>
                        ui.Post(_ => { if (!IsDisposed) lblMapStatus.Text = L.T("Сканирую… просмотрено файлов: ") + n.ToString("N0") + " (" + Fmt.Bytes(bytes) + ")"; }, null), ct));
                }
                if (IsDisposed || ct.IsCancellationRequested) return;
                ShowMap(map);
            }
            catch (Exception ex) { Log.Write("Место на диске: " + ex); }
            finally
            {
                measuring = false;
                mapping = false;
                if (!IsDisposed) { SyncButtons(); LayoutBody(); }
            }
        }

        void ShowMap(DiskMapResult m)
        {
            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (var f in m.Folders)
                lv.Items.Add(new ListViewItem(new[] { f.Path, Fmt.Bytes(f.Size), f.Note ?? "" }, lv.Groups[0]) { Tag = f.Path });
            foreach (var f in m.Files)
                lv.Items.Add(new ListViewItem(new[] { f.Path, Fmt.Bytes(f.Size), f.Note ?? "" }, lv.Groups[1]) { Tag = f.Path });
            foreach (var g in m.Duplicates)
                foreach (var p in g.Paths)
                    lv.Items.Add(new ListViewItem(new[] { p, Fmt.Bytes(g.Size), p == g.Paths[0] ? g.Paths.Count + L.T(" одинаковых — лишнего ") + Fmt.Bytes(g.Wasted) : L.T("копия") }, lv.Groups[2]) { Tag = p });
            lv.EndUpdate();
            long wasted = m.Duplicates.Sum(g => g.Wasted);
            lblMapStatus.Text = L.T("Просмотрено файлов: ") + m.FileCount.ToString("N0") + " (" + Fmt.Bytes(m.Scanned) + "). " +
                                (wasted > 0 ? L.T("Одинаковые копии занимают лишних ") + Fmt.Bytes(wasted) + ". " : "") +
                                L.T("Двойной клик — показать в Проводнике. Удаляйте отсюда только то, что узнаёте; программы и игры — через их удаление.");
            LayoutBody();
        }

        void ShowSelected()
        {
            if (lv.SelectedItems.Count == 0) return;
            string path = lv.SelectedItems[0].Tag as string;
            if (path == null) return;
            try
            {
                if (Directory.Exists(path)) Process.Start("explorer.exe", "\"" + path + "\"");
                else Process.Start("explorer.exe", "/select,\"" + path + "\"");
            }
            catch (Exception ex) { MessageBox.Show(this, L.T("Не удалось открыть: ") + ex.Message, "Roomy"); }
        }

        // ---------- Очистка ----------

        List<CleanRow> Selected()
        {
            return rows.Where(r => r.Check.Checked && r.C.Mode != CleanMode.InfoOnly && r.C.Size > 0).ToList();
        }

        void UpdateCleanButton()
        {
            var sel = Selected();
            long total = sel.Sum(r => r.C.Size);
            btnClean.Text = total > 0 ? L.T("Очистить выбранное (") + Fmt.Bytes(total) + ")" : L.T("Очистить выбранное");
            btnClean.Enabled = total > 0 && !measuring && !cleaning;
            LayoutBody();
        }

        async void CleanSelected()
        {
            var sel = Selected();
            if (sel.Count == 0 || measuring || cleaning) return;

            var del = sel.Where(r => r.C.Mode == CleanMode.Delete).ToList();
            var rec = sel.Where(r => r.C.Mode == CleanMode.Recycle).ToList();
            var bin = sel.FirstOrDefault(r => r.C.Mode == CleanMode.EmptyBin);
            var msg = new System.Text.StringBuilder();
            if (del.Count > 0)
            {
                msg.AppendLine(L.T("Удалить насовсем (программы создадут это заново):"));
                foreach (var r in del) msg.AppendLine("  • " + r.C.Title + " — " + Fmt.Bytes(r.C.Size));
            }
            if (rec.Count > 0)
            {
                msg.AppendLine(L.T("Отправить в корзину (можно вернуть):"));
                foreach (var r in rec) msg.AppendLine("  • " + r.C.Title + " — " + Fmt.Bytes(r.C.Size));
            }
            if (bin != null)
                msg.AppendLine(L.T("Очистить корзину — ") + Fmt.Bytes(bin.C.Size) + L.T(". Это необратимо: удалённые раньше файлы нельзя будет вернуть."));
            msg.AppendLine();
            msg.Append(L.T("Ваши документы, фото, пароли, история браузера и переписка не затрагиваются. Продолжить?"));
            if (MessageBox.Show(this, msg.ToString(), L.T("Roomy — очистка"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            cleaning = true;
            SyncButtons();
            lblResult.Text = L.T("Очищаю…");
            UseWaitCursor = true;
            long freed = 0;
            int skipped = 0;
            var ct = cts.Token;
            try
            {
                // корзину - в последнюю очередь, чтобы установщики, отправленные в неё, не удалились насовсем
                foreach (var r in sel.OrderBy(x => x.C.Mode == CleanMode.EmptyBin ? 1 : 0))
                {
                    var cat = r.C;
                    int sk = 0;
                    freed += await Task.Run(() => { int s; long f = DiskClean.Clean(cat, out s, ct); sk = s; return f; });
                    skipped += sk;
                }
                Log.Write("Очистка диска: освобождено " + Fmt.Bytes(freed) + ", пропущено занятых файлов: " + skipped);
            }
            catch (Exception ex) { Log.Write("Очистка диска: " + ex); }
            finally
            {
                UseWaitCursor = false;
                cleaning = false;
            }
            if (IsDisposed) return;
            UpdateDrive();
            string res = L.T("Освобождено ") + Fmt.Bytes(freed) + ".";
            if (rec.Count > 0 && bin == null) res += L.T(" Установщики — в корзине: место освободится после её очистки.");
            if (skipped > 0) res += L.T(" Пропущено занятых файлов: ") + skipped + L.T(" — их держат запущенные программы.");
            // пересчитать размеры категорий, но не пересканировать весь диск
            foreach (var r in rows)
            {
                var cat = r.C;
                await Task.Run(() => DiskClean.Measure(cat, ct));
                if (IsDisposed) return;
                r.Refresh(true);
            }
            lblResult.Text = res;
            SyncButtons();
        }
    }
}
