using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Roomy
{
    static class Ui
    {
        public static readonly Color Back = Color.FromArgb(248, 249, 251);
        public static readonly Color Card = Color.White;
        public static readonly Color Text = Color.FromArgb(28, 30, 33);
        public static readonly Color Muted = Color.FromArgb(110, 116, 124);
        public static readonly Color Line = Color.FromArgb(228, 231, 235);
        public static readonly Color Track = Color.FromArgb(232, 235, 239);
        public static readonly Color Accent = Color.FromArgb(0, 103, 192);

        public static float Scale = 1f;
        public static int S(float px) { return (int)Math.Round(px * Scale); }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            rad = Math.Min(rad, Math.Min(r.Width, r.Height) / 2f);
            if (rad <= 0.5f) { p.AddRectangle(r); return p; }
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Color LoadColor(float load, int alert)
        {
            return load >= alert ? Controller.Red : load >= 70 ? Controller.Orange : Controller.Green;
        }
    }

    // переключатель в стиле Windows 11.
    class ToggleSwitch : Control
    {
        bool isOn;
        public event EventHandler CheckedChanged;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Size = new Size(Ui.S(40), Ui.S(20));
            TabStop = true;
        }

        public bool Checked
        {
            get { return isOn; }
            set { if (isOn == value) return; isOn = value; Invalidate(); var h = CheckedChanged; if (h != null) h(this, EventArgs.Empty); }
        }

        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) Checked = !Checked;
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(1, 1, Width - 3, Height - 3);
            using (var path = Ui.Round(r, r.Height / 2))
            {
                if (isOn) using (var b = new SolidBrush(Ui.Accent)) g.FillPath(b, path);
                else
                {
                    using (var b = new SolidBrush(Color.White)) g.FillPath(b, path);
                    using (var pen = new Pen(Ui.Muted, Math.Max(1f, Ui.Scale))) g.DrawPath(pen, path);
                }
            }
            float d = r.Height - Ui.S(8);
            float x = isOn ? r.Right - d - Ui.S(4) : r.X + Ui.S(4);
            using (var b = new SolidBrush(isOn ? Color.White : Ui.Muted))
                g.FillEllipse(b, x, r.Y + (r.Height - d) / 2, d, d);
            if (Focused)
                using (var pen = new Pen(Ui.Text) { DashStyle = DashStyle.Dot })
                    g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    // полоса заполнения памяти.
    class MemBar : Control
    {
        float value;
        Color color = Controller.Green;

        public MemBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = Ui.S(10);
        }

        public void Set(float fraction, Color c)
        {
            fraction = Math.Max(0, Math.Min(1, fraction));
            if (Math.Abs(fraction - value) < 0.001f && c == color) return;
            value = fraction; color = c; Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : BackColor);
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var path = Ui.Round(r, r.Height / 2))
            using (var b = new SolidBrush(Ui.Track)) g.FillPath(b, path);
            if (value > 0.005f)
            {
                var f = new RectangleF(0, 0, Math.Max(r.Height, (Width - 1) * value), Height - 1);
                using (var path = Ui.Round(f, f.Height / 2))
                using (var b = new SolidBrush(color)) g.FillPath(b, path);
            }
        }
    }

    // график загрузки памяти.
    class Sparkline : Control
    {
        List<float> data = new List<float>();
        int capacity = History.LoadPoints;
        int alert = 85;

        public Sparkline()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = new Font("Segoe UI", 8f);
        }

        public void SetData(List<float> values, int alertThreshold)
        {
            data = new List<float>(values);
            alert = alertThreshold;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Ui.Card);
            int left = Ui.S(34), top = Ui.S(6), bottom = Ui.S(18);
            var plot = new RectangleF(left, top, Width - left - Ui.S(6), Height - top - bottom);
            if (plot.Width < 10 || plot.Height < 10) return;

            using (var pen = new Pen(Ui.Line))
            using (var dash = new Pen(Color.FromArgb(160, Controller.Red)) { DashStyle = DashStyle.Dash })
            using (var brush = new SolidBrush(Ui.Muted))
            {
                foreach (int lvl in new[] { 0, 50, 100 })
                {
                    float y = plot.Bottom - plot.Height * lvl / 100f;
                    g.DrawLine(pen, plot.Left, y, plot.Right, y);
                    var sz = g.MeasureString(lvl + "%", Font);
                    g.DrawString(lvl + "%", Font, brush, plot.Left - sz.Width - Ui.S(2), y - sz.Height / 2);
                }
                float ya = plot.Bottom - plot.Height * alert / 100f;
                g.DrawLine(dash, plot.Left, ya, plot.Right, ya);
                g.DrawString(L.T("10 минут назад"), Font, brush, plot.Left, plot.Bottom + Ui.S(2));
                var now = g.MeasureString(L.T("сейчас"), Font);
                g.DrawString(L.T("сейчас"), Font, brush, plot.Right - now.Width, plot.Bottom + Ui.S(2));
            }

            if (data.Count < 2) return;
            var pts = new List<PointF>();
            float step = plot.Width / (capacity - 1);
            float x0 = plot.Right - step * (data.Count - 1);
            for (int i = 0; i < data.Count; i++)
                pts.Add(new PointF(x0 + step * i, plot.Bottom - plot.Height * Math.Min(100, data[i]) / 100f));
            Color c = Ui.LoadColor(data[data.Count - 1], alert);
            var area = new List<PointF>(pts);
            area.Add(new PointF(pts[pts.Count - 1].X, plot.Bottom));
            area.Add(new PointF(pts[0].X, plot.Bottom));
            using (var b = new SolidBrush(Color.FromArgb(40, c))) g.FillPolygon(b, area.ToArray());
            using (var pen = new Pen(c, Math.Max(1.5f, 2 * Ui.Scale))) g.DrawLines(pen, pts.ToArray());
        }
    }

    // строка программы в простом виде: имя, размер, полоса доли, режим.
    class AppRow : Control
    {
        public string Key { get; private set; }
        public string DisplayName { get; private set; }
        public readonly ComboBox Combo;
        public event Action<AppRow, int> ModeChanged;
        public event Action<AppRow> InfoRequested;
        public AppGroup Group { get; private set; }
        readonly LinkLabel infoLink;

        string name = "", sub = "", size = "";
        float frac;
        Color barColor = Ui.Accent;
        readonly Font nameFont, subFont;
        readonly ToolTip tip = new ToolTip();

        public AppRow()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.Card;
            nameFont = new Font("Segoe UI Semibold", 10f);
            subFont = new Font("Segoe UI", 8.5f);
            Height = Ui.S(58);
            Combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9f), Width = Ui.S(200) };
            Combo.Items.AddRange(Settings.ModeNames);
            Combo.SelectionChangeCommitted += (o, e) => { var h = ModeChanged; if (h != null) h(this, Combo.SelectedIndex); };
            Controls.Add(Combo);
            infoLink = new LinkLabel { Text = L.T("что это?"), AutoSize = true, LinkColor = Ui.Accent, Font = new Font("Segoe UI", 8.5f), BackColor = Ui.Card };
            infoLink.LinkClicked += (o, e) => { var h = InfoRequested; if (h != null) h(this); };
            Controls.Add(infoLink);
            DoubleClick += (o, e) => { var h = InfoRequested; if (h != null) h(this); };
            tip.SetToolTip(this, L.T("Двойной клик — что это за программа"));
            tip.SetToolTip(Combo,
                L.T("Не трогать — Roomy ничего не делает с программой.\n") +
                L.T("Притормаживать в фоне — пока вы в ней не работаете, память в первую очередь забирается у неё;\n") +
                L.T("   если долго не открывали и памяти мало — выгружается из памяти (не закрывается).\n") +
                L.T("Можно закрывать — то же самое, плюс её закрывает «Режим экономии» и потом запускает обратно."));
        }

        public bool Interacting { get { return Combo.DroppedDown; } }

        public void SetData(AppGroup g, long totalPhys, int mode, string state, bool isProtected, long? growth)
        {
            Group = g;
            Key = g.Key;
            DisplayName = g.Display;
            name = Fmt.ShortName(g.Display);
            size = Fmt.Bytes(g.Bytes);
            frac = totalPhys > 0 ? (float)g.Bytes / totalPhys : 0;
            var parts = new List<string> { Fmt.Processes(g.Pids.Count) };
            if (totalPhys > 0) parts.Add((frac < 0.01f ? "<1" : Math.Round(frac * 100).ToString()) + L.T("% всей памяти"));
            if (growth.HasValue && Math.Abs(growth.Value) >= 50L * 1024 * 1024) parts.Add(Fmt.Signed(growth.Value) + L.T(" за 10 мин"));
            if (!string.IsNullOrEmpty(state)) parts.Add(state);
            sub = string.Join(" · ", parts);
            barColor = frac >= 0.15f ? Controller.Orange : Ui.Accent;
            if (!Combo.DroppedDown && Combo.SelectedIndex != mode) Combo.SelectedIndex = mode;
            Combo.Enabled = !isProtected;
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Combo == null) return;
            Combo.Width = Ui.S(200);
            Combo.Location = new Point(Width - Combo.Width - Ui.S(12), (Height - Combo.Height) / 2);
            if (infoLink != null) infoLink.Location = new Point(Combo.Left - Ui.S(16) - infoLink.Width, Ui.S(37));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(BackColor);
            if (Combo == null || nameFont == null) return;
            int pad = Ui.S(12);
            int right = Combo.Left - Ui.S(16);
            int leftW = right - pad;
            if (leftW < 50) return;

            var sizeSz = TextRenderer.MeasureText(size, nameFont);
            TextRenderer.DrawText(g, size, nameFont, new Point(right - sizeSz.Width, Ui.S(6)), Ui.Text);
            TextRenderer.DrawText(g, name, nameFont, new Rectangle(pad, Ui.S(6), leftW - sizeSz.Width - Ui.S(8), nameFont.Height + 4),
                Ui.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            var bar = new RectangleF(pad, Ui.S(30), leftW, Ui.S(5));
            using (var path = Ui.Round(bar, bar.Height / 2)) using (var b = new SolidBrush(Ui.Track)) g.FillPath(b, path);
            if (frac > 0)
            {
                var f = new RectangleF(bar.X, bar.Y, Math.Max(bar.Height, bar.Width * Math.Min(1f, frac)), bar.Height);
                using (var path = Ui.Round(f, f.Height / 2)) using (var b = new SolidBrush(barColor)) g.FillPath(b, path);
            }
            int subW = infoLink != null ? Math.Max(30, infoLink.Left - pad - Ui.S(8)) : leftW;
            TextRenderer.DrawText(g, sub, subFont, new Rectangle(pad, Ui.S(38), subW, subFont.Height + 4), Ui.Muted,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            using (var pen = new Pen(Ui.Line)) g.DrawLine(pen, pad, Height - 1, Width - pad, Height - 1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { tip.Dispose(); nameFont.Dispose(); subFont.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
