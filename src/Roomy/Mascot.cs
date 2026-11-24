using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Roomy
{
    enum Mood { Ok, Tight, Bad, Sleep }

    // Roomy - робот-помощник (тёзка программы): голова-микросхема (планка памяти) с золотыми контактами.
    // Рисуется векторно в координатах 100x100, поэтому чёткий и в трее, и в окне.
    // blink: 0 - глаза открыты, 1 - закрыты.
    static class Mascot
    {
        public static readonly Color Tile = Color.FromArgb(0x1E, 0x29, 0x3B);
        public static readonly Color Board = Color.FromArgb(0x22, 0xC5, 0x5E);
        public static readonly Color Gold = Color.FromArgb(0xF5, 0xC5, 0x42);
        public static readonly Color Ink = Color.FromArgb(0x0B, 0x3B, 0x1E);
        static readonly Color BoardTight = Color.FromArgb(0xF5, 0x9E, 0x0B);
        static readonly Color InkTight = Color.FromArgb(0x5A, 0x34, 0x00);
        static readonly Color BoardBad = Color.FromArgb(0xEF, 0x44, 0x44);
        static readonly Color InkBad = Color.FromArgb(0x5C, 0x0B, 0x0B);
        static readonly Color Drop = Color.FromArgb(0xBF, 0xDB, 0xFE);

        public static Mood MoodOf(Status st)
        {
            if (st == null) return Mood.Ok;
            if (st.Color == Controller.Red) return Mood.Bad;
            if (st.Color == Controller.Orange) return Mood.Tight;
            return Mood.Ok;
        }

        static Color Led(Mood m)
        {
            switch (m)
            {
                case Mood.Bad: return Color.FromArgb(0xF4, 0x3F, 0x5E);
                case Mood.Tight: return Color.FromArgb(0xFB, 0xBF, 0x24);
                case Mood.Sleep: return Color.FromArgb(0x16, 0x65, 0x34);
                default: return Color.FromArgb(0xBB, 0xF7, 0xD0);
            }
        }

        static void Fill(Graphics g, Color c, float x, float y, float w, float h, float r)
        {
            using (var p = Ui.Round(new RectangleF(x, y, w, h), r))
            using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }

        static void Circle(Graphics g, Color c, float cx, float cy, float rx, float ry)
        {
            using (var b = new SolidBrush(c)) g.FillEllipse(b, cx - rx, cy - ry, rx * 2, ry * 2);
        }

        static Pen RoundPen(Color c, float w)
        {
            return new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        }

        // квадратичная кривая (как "q" в SVG) через кубическую Безье GDI+.
        static void Quad(Graphics g, Pen p, float x0, float y0, float qx, float qy, float x2, float y2)
        {
            g.DrawBezier(p, x0, y0, x0 + 2f / 3f * (qx - x0), y0 + 2f / 3f * (qy - y0),
                x2 + 2f / 3f * (qx - x2), y2 + 2f / 3f * (qy - y2), x2, y2);
        }

        static void Zz(Graphics g)
        {
            using (var p = RoundPen(Color.White, 3f))
                g.DrawLines(p, new[] { new PointF(71, 12), new PointF(80, 12), new PointF(71, 22), new PointF(80, 22) });
            using (var p = RoundPen(Color.FromArgb(205, 255, 255, 255), 2.4f))
                g.DrawLines(p, new[] { new PointF(84, 26), new PointF(90, 26), new PointF(84, 33), new PointF(90, 33) });
        }

        static void SweatDrop(Graphics g, float x, float y)
        {
            using (var path = new GraphicsPath())
            {
                path.AddBezier(x, y, x - 3.3f, y + 4.7f, x - 3.3f, y + 8f, x, y + 8f);
                path.AddBezier(x, y + 8f, x + 3.3f, y + 8f, x + 3.3f, y + 4.7f, x, y);
                using (var b = new SolidBrush(Drop)) g.FillPath(b, path);
            }
        }

        // глаз: белок + зрачок. При моргании сплющивается в черту.
        static void Eye(Graphics g, Color ink, float cx, float cy, float r, float pupil, float px, float blink, float lineW)
        {
            if (blink >= 0.85f)
            {
                using (var p = RoundPen(ink, lineW)) g.DrawLine(p, cx - r * 0.8f, cy, cx + r * 0.8f, cy);
                return;
            }
            float k = 1f - blink;
            Circle(g, Color.White, cx, cy, r, Math.Max(r * 0.25f, r * k));
            Circle(g, ink, cx + px, cy + (blink > 0 ? 0 : r * 0.12f), pupil, Math.Max(pupil * 0.3f, pupil * k));
        }

        // полный значок: тёмная плитка + робот (как в логотипе).
        public static void Draw(Graphics g, RectangleF r, Mood mood, float blink)
        {
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TranslateTransform(r.X, r.Y);
            g.ScaleTransform(r.Width / 100f, r.Height / 100f);

            Fill(g, Tile, 0, 0, 100, 100, 24);

            // ручки-манипуляторы (во сне правая опущена)
            using (var p = RoundPen(Gold, 3f))
            {
                g.DrawLines(p, new[] { new PointF(16, 54), new PointF(11, 54), new PointF(11, 45) });
                if (mood == Mood.Sleep) g.DrawLines(p, new[] { new PointF(84, 54), new PointF(89, 54), new PointF(89, 62) });
                else g.DrawLines(p, new[] { new PointF(84, 54), new PointF(89, 54), new PointF(89, 45) });
            }
            Circle(g, Gold, 11, 42, 4, 4);
            Circle(g, Gold, 89, mood == Mood.Sleep ? 65 : 42, 4, 4);

            // плата и контакты
            Fill(g, Board, 16, 28, 68, 44, 9);
            foreach (var x in new float[] { 22, 29, 36, 43, 54, 61, 68, 75 })
                Fill(g, Gold, x, 72, 4.5f, 9, 1.2f);
            Circle(g, Led(mood), 76, 35, 3, 3);

            switch (mood)
            {
                case Mood.Ok:
                    Eye(g, Ink, 37, 47, 8, 4, 1.5f, blink, 3f);
                    Eye(g, Ink, 63, 47, 8, 4, 1.5f, blink, 3f);
                    using (var p = RoundPen(Ink, 3f)) Quad(g, p, 44, 61, 50, 65.5f, 56, 61);
                    break;
                case Mood.Tight:
                    Eye(g, Ink, 37, 47, 8, 4, -2f, blink, 3f);
                    Eye(g, Ink, 63, 47, 8, 4, -2f, blink, 3f);
                    using (var p = RoundPen(Ink, 3f)) g.DrawLine(p, 44, 62, 56, 62);
                    break;
                case Mood.Bad:
                    Circle(g, Color.White, 37, 48, 8, 8);
                    Circle(g, Color.White, 63, 48, 8, 8);
                    Circle(g, Ink, 37, 48, 2.2f, 2.2f);
                    Circle(g, Ink, 63, 48, 2.2f, 2.2f);
                    using (var p = RoundPen(Ink, 2.6f))
                    {
                        g.DrawLine(p, 28, 39, 38, 35.5f);
                        g.DrawLine(p, 72, 39, 62, 35.5f);
                        g.DrawLines(p, new[] { new PointF(41, 63), new PointF(45, 60), new PointF(49, 63),
                            new PointF(53, 60), new PointF(57, 63), new PointF(61, 60) });
                    }
                    SweatDrop(g, 87, 18);
                    break;
                case Mood.Sleep:
                    using (var p = RoundPen(Ink, 3f))
                    {
                        Quad(g, p, 31, 49, 37, 53, 43, 49);
                        Quad(g, p, 57, 49, 63, 53, 69, 49);
                    }
                    using (var p = RoundPen(Ink, 2.6f)) Quad(g, p, 46, 61, 50, 63, 54, 61);
                    Zz(g);
                    break;
            }
            g.Restore(state);
        }

        // Упрощённый робот для трея (16–32 px): без плитки и ручек, крупные глаза.
        // Цвет платы показывает состояние: зелёный - всё хорошо, жёлтый - впритык, красный - мало.
        public static void DrawTray(Graphics g, int size, Mood mood, float blink)
        {
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.ScaleTransform(size / 100f, size / 100f);

            Color board = mood == Mood.Bad ? BoardBad : mood == Mood.Tight ? BoardTight : Board;
            Color ink = mood == Mood.Bad ? InkBad : mood == Mood.Tight ? InkTight : Ink;

            foreach (var x in new float[] { 12, 30, 47, 64, 81 })
                Fill(g, Gold, x, 70, 9, 24, 3);
            Fill(g, board, 2, 10, 96, 68, 16);

            if (mood == Mood.Bad)
            {
                using (var p = RoundPen(Color.White, 10f))
                {
                    g.DrawLines(p, new[] { new PointF(18, 30), new PointF(36, 44), new PointF(18, 58) });
                    g.DrawLines(p, new[] { new PointF(82, 30), new PointF(64, 44), new PointF(82, 58) });
                }
            }
            else
            {
                if (mood == Mood.Sleep) blink = 1f;
                Eye(g, ink, 30, 43, 17, 8.5f, 2f, blink, 9f);
                Eye(g, ink, 70, 43, 17, 8.5f, 2f, blink, 9f);
            }
            g.Restore(state);
        }

        public static Bitmap RenderTray(int size, Mood mood, float blink)
        {
            var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                DrawTray(g, size, mood, blink);
            }
            return bmp;
        }

        // кадры моргания: полузакрыто -> закрыто -> закрыто -> полузакрыто -> открыто.
        public static readonly float[] BlinkFrames = { 0.5f, 1f, 1f, 0.5f, 0f };
    }

    // робот в окне программы: меняет настроение вместе с памятью, иногда моргает, а если на него нажать - что-нибудь скажет.
    class MascotView : System.Windows.Forms.Control
    {
        readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer { Interval = 70 };
        readonly Random rnd = new Random();
        Mood mood = Mood.Ok;
        float blink;
        int frame = -1;
        DateTime nextBlink = DateTime.Now.AddSeconds(2);
        readonly System.Windows.Forms.ToolTip tip = new System.Windows.Forms.ToolTip();
        int lastPhrase = -1;

        static readonly string[] Phrases =
        {
            L.T("Привет! Я Roomy. Слежу за памятью, пока ты занят делом."),
            L.T("Ничего не трогаю без спроса. Честно-честно."),
            L.T("Память не надо «чистить». Надо знать, кто её ест."),
            L.T("Если что — я в трее. Моргаю."),
            L.T("Тсс… вкладки спят."),
            L.T("Я микросхема, но с душой."),
            L.T("Меня сделал skriptkiddo. Про него говорят не «он», а «broski»."),
            L.T("Не тыкай, щекотно!"),
            L.T("8 ГБ — это не приговор. Это вызов."),
            L.T("Если комп тормозит — сначала глянь, кто сверху списка.")
        };

        // нажатие на робота: моргнёт и скажет фразу.
        public bool Chatty = true;

        public MascotView()
        {
            Cursor = System.Windows.Forms.Cursors.Hand;
            Click += (o, e) => Poke();
            SetStyle(System.Windows.Forms.ControlStyles.AllPaintingInWmPaint | System.Windows.Forms.ControlStyles.OptimizedDoubleBuffer
                | System.Windows.Forms.ControlStyles.UserPaint | System.Windows.Forms.ControlStyles.ResizeRedraw, true);
            anim.Tick += (o, e) => Step();
        }

        public Mood Mood
        {
            get { return mood; }
            set { if (mood != value) { mood = value; Invalidate(); } }
        }

        void Poke()
        {
            if (!Chatty) return;
            int i;
            do i = rnd.Next(Phrases.Length); while (i == lastPhrase && Phrases.Length > 1);
            lastPhrase = i;
            if (mood != Mood.Bad && mood != Mood.Sleep && frame < 0) { frame = 0; nextBlink = DateTime.Now; }
            tip.Show(Phrases[i], this, Width / 2, Height + 4, 3500);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) anim.Start(); else anim.Stop();
        }

        void Step()
        {
            if (frame < 0)
            {
                if (DateTime.Now < nextBlink || mood == Mood.Bad || mood == Mood.Sleep) return;
                frame = 0;
            }
            blink = Mascot.BlinkFrames[frame++];
            if (frame >= Mascot.BlinkFrames.Length)
            {
                frame = -1;
                // иногда - двойное моргание
                nextBlink = DateTime.Now.AddMilliseconds(rnd.Next(6) == 0 ? 250 : rnd.Next(3000, 7000));
            }
            Invalidate();
        }

        protected override void OnPaint(System.Windows.Forms.PaintEventArgs e)
        {
            using (var b = new SolidBrush(BackColor)) e.Graphics.FillRectangle(b, ClientRectangle);
            float s = Math.Min(Width, Height);
            Mascot.Draw(e.Graphics, new RectangleF((Width - s) / 2f, (Height - s) / 2f, s, s), mood, blink);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { anim.Dispose(); tip.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
