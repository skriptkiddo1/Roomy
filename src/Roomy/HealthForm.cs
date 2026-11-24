using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Roomy
{
    // карточка одного пункта проверки.
    class CheckCard : Control
    {
        public readonly CheckResult R;
        readonly Button action;
        readonly Font titleFont = new Font("Segoe UI Semibold", 10.5f);
        readonly Font badgeFont = new Font("Segoe UI Semibold", 8f);
        const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;

        public static Color ColorOf(Sev s)
        {
            switch (s)
            {
                case Sev.Critical: return Controller.Red;
                case Sev.Useful: return Controller.Orange;
                case Sev.Info: return Color.FromArgb(90, 110, 140);
                default: return Controller.Green;
            }
        }

        public static string LabelOf(Sev s)
        {
            switch (s)
            {
                case Sev.Critical: return L.T("ВАЖНО");
                case Sev.Useful: return L.T("ПОЛЕЗНО");
                case Sev.Info: return L.T("К СВЕДЕНИЮ");
                default: return L.T("В ПОРЯДКЕ");
            }
        }

        public CheckCard(CheckResult r)
        {
            R = r;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.Card;
            Font = new Font("Segoe UI", 9.5f);
            if (r.ActionText != null)
            {
                action = new Button { Text = r.ActionText, AutoSize = true, FlatStyle = FlatStyle.System, Font = new Font("Segoe UI", 9f) };
                action.Click += (o, e) => Open(r.ActionTarget);
                Controls.Add(action);
            }
        }

        static void Open(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(L.T("Не удалось открыть: ") + ex.Message, "Roomy"); }
        }

        int Pad { get { return Ui.S(14); } }
        int Stripe { get { return Ui.S(5); } }

        Rectangle TextArea(int width)
        {
            int left = Stripe + Pad;
            return new Rectangle(left, 0, Math.Max(50, width - left - Pad), 0);
        }

        // высота карточки при заданной ширине.
        public int Measure(int width)
        {
            var ta = TextArea(width);
            int y = Pad;
            y += TextRenderer.MeasureText("A", badgeFont).Height + Ui.S(2);
            y += TextRenderer.MeasureText(R.Title, titleFont, new Size(ta.Width, int.MaxValue), Wrap).Height + Ui.S(4);
            y += TextRenderer.MeasureText(R.Text, Font, new Size(ta.Width, int.MaxValue), Wrap).Height;
            if (action != null) y += Ui.S(10) + action.PreferredSize.Height;
            return y + Pad;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (action == null) return;
            var ta = TextArea(Width);
            action.Location = new Point(ta.Left, Height - Pad - action.PreferredSize.Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Ui.Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Ui.Round(rect, Ui.S(8)))
            {
                using (var b = new SolidBrush(Ui.Card)) g.FillPath(b, path);
                g.SetClip(path);
                using (var b = new SolidBrush(ColorOf(R.Severity))) g.FillRectangle(b, 0, 0, Stripe, Height);
                g.ResetClip();
                using (var pen = new Pen(Ui.Line)) g.DrawPath(pen, path);
            }

            var ta = TextArea(Width);
            int y = Pad;
            var badge = LabelOf(R.Severity);
            var bsz = TextRenderer.MeasureText(badge, badgeFont);
            TextRenderer.DrawText(g, badge, badgeFont, new Point(ta.Left, y), ColorOf(R.Severity));
            y += bsz.Height + Ui.S(2);
            var tsz = TextRenderer.MeasureText(R.Title, titleFont, new Size(ta.Width, int.MaxValue), Wrap);
            TextRenderer.DrawText(g, R.Title, titleFont, new Rectangle(ta.Left, y, ta.Width, tsz.Height), Ui.Text, Wrap);
            y += tsz.Height + Ui.S(4);
            var xsz = TextRenderer.MeasureText(R.Text, Font, new Size(ta.Width, int.MaxValue), Wrap);
            TextRenderer.DrawText(g, R.Text, Font, new Rectangle(ta.Left, y, ta.Width, xsz.Height), Color.FromArgb(60, 64, 70), Wrap);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { titleFont.Dispose(); badgeFont.Dispose(); }
            base.Dispose(disposing);
        }
    }

    // окно "Проверка компьютера".
    class HealthForm : Form
    {
        readonly Settings S;
        readonly Panel header, body;
        readonly Label lblTitle, lblSummary;
        readonly Button btnRun;
        readonly CheckBox chkOk;
        readonly List<CheckCard> cards = new List<CheckCard>();
        bool running;

        // для проверки интерфейса без Windows.
        public static Func<List<CheckResult>> TestOverride;

        public HealthForm(Settings s, Icon icon)
        {
            S = s;
            Text = L.T("Roomy — проверка компьютера");
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Ui.Back;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(Ui.S(520), Ui.S(420));
            if (icon != null) Icon = icon;
            var wa = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(Ui.S(720), wa.Width - Ui.S(20)), Math.Min(Ui.S(820), wa.Height - Ui.S(40)));

            header = new Panel { Dock = DockStyle.Top, Height = Ui.S(96), BackColor = Ui.Card };
            lblTitle = new Label { AutoSize = true, Text = L.T("Проверка компьютера"), Font = new Font("Segoe UI Semibold", 15f), ForeColor = Ui.Text };
            lblSummary = new Label { AutoSize = false, ForeColor = Ui.Muted, Text = L.T("Проверяю…") };
            btnRun = new Button { Text = L.T("Проверить снова"), AutoSize = true, FlatStyle = FlatStyle.System };
            chkOk = new CheckBox { Text = L.T("Показывать то, что в порядке"), AutoSize = true, Checked = true, ForeColor = Ui.Text };
            header.Controls.AddRange(new Control[] { lblTitle, lblSummary, btnRun, chkOk });
            header.Resize += (o, e) => LayoutHeader();
            header.Paint += (o, e) => { using (var p = new Pen(Ui.Line)) e.Graphics.DrawLine(p, 0, header.Height - 1, header.Width, header.Height - 1); };
            btnRun.Click += (o, e) => RunChecks();
            chkOk.CheckedChanged += (o, e) => LayoutCards();

            body = new StablePanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Ui.Back, Padding = new Padding(0) };
            body.Resize += (o, e) => LayoutCards();

            Controls.Add(body);
            Controls.Add(header);
            Shown += (o, e) => RunChecks();
        }

        void LayoutHeader()
        {
            int pad = Ui.S(16);
            lblTitle.Location = new Point(pad, Ui.S(12));
            btnRun.Location = new Point(header.Width - pad - btnRun.Width, Ui.S(14));
            chkOk.Location = new Point(header.Width - pad - chkOk.Width, Ui.S(60));
            lblSummary.SetBounds(pad, Ui.S(50), Math.Max(50, chkOk.Left - pad * 2), Ui.S(40));
        }

        async void RunChecks()
        {
            if (running) return;
            running = true;
            btnRun.Enabled = false;
            lblSummary.Text = L.T("Проверяю память, диск, автозагрузку и настройки Windows… Это займёт несколько секунд. Ничего не меняется.");
            UseWaitCursor = true;
            List<CheckResult> results;
            try
            {
                var test = TestOverride;
                results = test != null ? test() : await Task.Run(() => HealthCheck.Run(S));
            }
            catch (Exception ex)
            {
                Log.Write("Проверка компьютера: " + ex);
                results = new List<CheckResult>();
            }
            finally
            {
                running = false;
                btnRun.Enabled = true;
                UseWaitCursor = false;
            }
            if (IsDisposed) return;
            ShowResults(results);
        }

        void ShowResults(List<CheckResult> results)
        {
            body.SuspendLayout();
            foreach (var c in cards) { body.Controls.Remove(c); c.Dispose(); }
            cards.Clear();
            foreach (var r in results)
            {
                var card = new CheckCard(r);
                cards.Add(card);
                body.Controls.Add(card);
            }
            int crit = results.Count(r => r.Severity == Sev.Critical);
            int useful = results.Count(r => r.Severity == Sev.Useful);
            int ok = results.Count(r => r.Severity == Sev.Ok);
            if (results.Count == 0)
                lblSummary.Text = L.T("Проверка не удалась — подробности в журнале.");
            else if (crit + useful == 0)
                lblSummary.Text = L.T("Серьёзных проблем не найдено. В порядке: ") + ok + ".";
            else
            {
                string t = (crit > 0 ? L.T("важно: ") + crit + " · " : "") + (useful > 0 ? L.T("полезно сделать: ") + useful + " · " : "") +
                           L.T("в порядке: ") + ok + L.T(". Начните сверху — там то, что даст больше всего.");
                lblSummary.Text = char.ToUpper(t[0]) + t.Substring(1);
            }
            body.ResumeLayout();
            LayoutCards();
        }

        void LayoutCards()
        {
            int pad = Ui.S(14);
            int scroll = body.VerticalScroll.Visible ? 0 : SystemInformation.VerticalScrollBarWidth;
            int width = body.ClientSize.Width - pad * 2 - scroll;
            if (width < 100) return;
            int y = pad + body.AutoScrollPosition.Y;
            body.SuspendLayout();
            foreach (var c in cards)
            {
                bool visible = chkOk.Checked || c.R.Severity != Sev.Ok;
                c.Visible = visible;
                if (!visible) continue;
                int h = c.Measure(width);
                c.SetBounds(pad, y, width, h);
                y += h + Ui.S(10);
            }
            body.ResumeLayout(true);
        }
    }
}
