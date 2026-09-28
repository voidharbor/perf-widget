using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace PerfWidget
{
    // The click-through view: everything the strip has no room for, three-minute sparklines,
    // the busiest processes, plus Start at login and Quit.
    class DetailsForm : Form
    {
        public event EventHandler QuitRequested;

        readonly Sampler sampler;
        readonly Font titleFont = new Font("Segoe UI", 12f, FontStyle.Bold, GraphicsUnit.Point);
        readonly Font sectionFont = new Font("Segoe UI", 8f, FontStyle.Bold, GraphicsUnit.Point);
        readonly Font rowFont = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
        readonly Font rowBold = new Font("Segoe UI", 9.5f, FontStyle.Bold, GraphicsUnit.Point);
        readonly Font smallFont = new Font("Segoe UI", 7.5f, FontStyle.Regular, GraphicsUnit.Point);
        readonly Font buttonFont = new Font("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Point);

        Reading r;
        string busiest = "";
        Rectangle startupButton, quitButton;
        Point mouse = new Point(-1, -1);
        const int W = 400, H = 600, Pad = 18, RowH = 22;

        public DetailsForm(Sampler sampler)
        {
            this.sampler = sampler;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.PopupBack;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            Size = new Size(W, H);
            KeyPreview = true;
            using (var p = Theme.RoundRect(new RectangleF(0, 0, W, H), 12)) Region = new Region(p);
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= Native.WS_EX_TOOLWINDOW; return cp; }
        }

        public void ShowAbove(Rectangle anchor)
        {
            var screen = Screen.FromPoint(new Point(anchor.Left, anchor.Top));
            int x = Math.Max(screen.Bounds.Left + 8, Math.Min(anchor.Left, screen.Bounds.Right - W - 8));
            int y = anchor.Top - H - 10;
            if (y < screen.Bounds.Top + 8) y = anchor.Bottom + 10;   // taskbar at the top of the screen
            Location = new Point(x, y);
            sampler.ForgetProcs();
            busiest = "";
            Show();
            Activate();
        }

        public void Update(Reading reading)
        {
            r = reading;
            if (!Visible) return;
            try { string b = sampler.TopCpuText(); if (b.Length > 0) busiest = b; } catch { }
            Invalidate();
        }

        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); Hide(); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { Hide(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            mouse = e.Location;
            Invalidate(Rectangle.Union(startupButton, quitButton));
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { mouse = new Point(-1, -1); Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            if (startupButton.Contains(e.Location))
            {
                try { Startup.Set(!Startup.IsEnabled); } catch (Exception ex) { MessageBox.Show(this, "Could not change the Startup shortcut: " + ex.Message, "Performance widget"); }
                Invalidate();
            }
            else if (quitButton.Contains(e.Location))
            {
                Hide();
                if (QuitRequested != null) QuitRequested(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Theme.PopupBack);
            using (var edge = new Pen(Theme.PopupEdge, 1.5f))
            using (var p = Theme.RoundRect(new RectangleF(0.75f, 0.75f, W - 1.5f, H - 1.5f), 12))
                g.DrawPath(edge, p);

            int y = Pad - 2;
            using (var b = new SolidBrush(Theme.PopupText)) g.DrawString("Performance", titleFont, b, Pad, y);
            if (r != null)
            {
                string status = Severity.Describe(r.Overall);
                Color sc = Theme.ForLevel(r.Overall, Color.FromArgb(255, 120, 220, 150));
                var sz = g.MeasureString(status, buttonFont);
                var pill = new RectangleF(W - Pad - sz.Width - 16, y + 3, sz.Width + 16, sz.Height + 4);
                using (var bp = Theme.RoundRect(pill, pill.Height / 2)) using (var pb = new SolidBrush(Color.FromArgb(40, sc))) using (var pe = new Pen(Color.FromArgb(160, sc), 1f)) { g.FillPath(pb, bp); g.DrawPath(pe, bp); }
                using (var sb = new SolidBrush(sc)) g.DrawString(status, buttonFont, sb, pill.X + 8, pill.Y + 2);
            }
            y += 34;

            if (r == null)
            {
                using (var b = new SolidBrush(Theme.PopupDim)) g.DrawString("Waiting for the first sample.", rowFont, b, Pad, y);
            }
            else
            {
                y = Section(g, y, r.GpuName, r.GpuLevel);
                if (r.GpuOk)
                {
                    y = Row(g, y, "Temperature", Theme.Temp(r.GpuTemp) + "C" + (r.FanPct >= 0 ? "   fan " + r.FanPct + "%" : ""), Theme.ForLevel(r.GpuLevel, Theme.PopupText));
                    y = Row(g, y, "Load", r.GpuLoad + "%   memory bus " + r.GpuMemLoad + "%", Theme.PopupText);
                    y = Row(g, y, "VRAM", r.VramUsedGB.ToString("0.0") + " of " + r.VramTotalGB.ToString("0.0") + " GB   (" + (r.VramTotalGB > 0 ? (r.VramUsedGB / r.VramTotalGB * 100).ToString("0") : "0") + "%)", Theme.PopupText);
                    y = Row(g, y, "Power", r.PowerW.ToString("0") + " of " + r.PowerLimitW.ToString("0") + " W", Theme.PopupText);
                    y = Row(g, y, "Clock", r.ClockMHz + " of " + r.ClockMaxMHz + " MHz   P" + r.PState, Theme.PopupText);
                    ulong shown = r.Reasons & ~Throttle.GpuIdle;
                    y = Row(g, y, "Limits", Severity.DescribeReasons(shown), (shown & Throttle.AnySlowdown) != 0 ? Theme.Critical : Theme.PopupText);
                }
                else
                {
                    y = Row(g, y, "GPU", "not readable (" + r.GpuError + ")", Theme.PopupDim);
                }
                y += 6;
                y = Section(g, y, ShortCpuName(r.CpuName), r.CpuLevel);
                y = Row(g, y, "Load", r.CpuUtility.ToString("0") + "%", Theme.PopupText);
                y = Row(g, y, "Clock", r.CpuBaseGHz > 0 ? r.CpuEffectiveGHz.ToString("0.00") + " GHz   base " + r.CpuBaseGHz.ToString("0.0") + " GHz" : "--", Theme.ForLevel(r.CpuLevel, Theme.PopupText));
                y = Row(g, y, "Busiest", busiest.Length > 0 ? busiest : "measuring", busiest.Length > 0 ? Theme.PopupText : Theme.PopupDim);
                y = Row(g, y, "Temperature", "needs an admin-installed sensor driver", Theme.PopupDim);
                y += 6;
                y = Section(g, y, "Memory", r.MemLevel);
                y = Row(g, y, "In use", r.MemUsedGB.ToString("0.0") + " of " + r.MemTotalGB.ToString("0.0") + " GB   (" + (r.MemFraction * 100).ToString("0") + "%)", Theme.ForLevel(r.MemLevel, Theme.PopupText));
                y += 6;
                y = Section(g, y, "Network", r.PingLevel);
                y = Row(g, y, "Throughput", r.NetOk ? "\u2193 " + Fmt.Rate(r.NetDown) + "B/s   \u2191 " + Fmt.Rate(r.NetUp) + "B/s" : "--", Theme.PopupText);
                y = Row(g, y, "Ping", r.PingMs >= 0 ? r.PingMs + " ms to 1.1.1.1" : r.PingMs == -1 ? "no reply from 1.1.1.1" : "measuring", Theme.ForLevel(r.PingLevel, Theme.PopupText));
                y += 6;
                y = Section(g, y, "Last 3 minutes", Level.Normal);
                DrawSparklines(g, y);
            }

            // Buttons
            int by = H - Pad - 30;
            startupButton = new Rectangle(Pad, by, 150, 30);
            quitButton = new Rectangle(W - Pad - 78, by, 78, 30);
            bool startupOn = false;
            try { startupOn = Startup.IsEnabled; } catch { }
            Button(g, startupButton, (startupOn ? "\u2713  " : "     ") + "Start at login", startupButton.Contains(mouse));
            Button(g, quitButton, "Quit", quitButton.Contains(mouse));
        }

        void DrawSparklines(Graphics g, int y)
        {
            var h = sampler.History;
            int gap = 12, w = (W - 2 * Pad - 2 * gap) / 3, graphH = 44;
            var gpu = h.Ordered(h.GpuLoad); var cpu = h.Ordered(h.Cpu); var down = h.Ordered(h.Down); var up = h.Ordered(h.Up);
            float netMax = 1e5f;
            foreach (float v in down) if (v > netMax) netMax = v;
            foreach (float v in up) if (v > netMax) netMax = v;
            int x = Pad;
            Spark(g, new Rectangle(x, y, w, graphH), "GPU load", gpu, 100f, Theme.Spark, null, 0);
            x += w + gap;
            Spark(g, new Rectangle(x, y, w, graphH), "CPU load", cpu, 100f, Theme.Spark, null, 0);
            x += w + gap;
            Spark(g, new Rectangle(x, y, w, graphH), "Network  (peak " + Fmt.Rate(netMax) + "B/s)", down, netMax, Theme.Spark, up, netMax);
        }

        // Area chart of a series, newest at the right edge; an optional second series drawn as a line.
        void Spark(Graphics g, Rectangle rc, string title, float[] a, float aMax, Color c, float[] b, float bMax)
        {
            using (var bg = new SolidBrush(Color.FromArgb(18, 255, 255, 255))) using (var p = Theme.RoundRect(new RectangleF(rc.X, rc.Y, rc.Width, rc.Height), 5)) g.FillPath(bg, p);
            using (var tb = new SolidBrush(Theme.PopupDim)) g.DrawString(title, smallFont, tb, rc.X, rc.Bottom + 2);
            DrawSeries(g, rc, a, aMax, c, true);
            if (b != null) DrawSeries(g, rc, b, bMax, Theme.SparkUp, false);
        }

        void DrawSeries(Graphics g, Rectangle rc, float[] s, float max, Color c, bool area)
        {
            if (s.Length < 2 || max <= 0) return;
            float stepX = (float)(rc.Width - 6) / (History.Cap - 1);
            float x0 = rc.Right - 3 - stepX * (s.Length - 1);
            var pts = new PointF[s.Length];
            for (int i = 0; i < s.Length; i++)
            {
                float v = Math.Max(0, Math.Min(1, s[i] / max));
                pts[i] = new PointF(x0 + stepX * i, rc.Bottom - 3 - v * (rc.Height - 6));
            }
            if (area)
            {
                using (var path = new GraphicsPath())
                {
                    path.AddLines(pts);
                    path.AddLine(pts[pts.Length - 1], new PointF(pts[pts.Length - 1].X, rc.Bottom - 3));
                    path.AddLine(new PointF(pts[pts.Length - 1].X, rc.Bottom - 3), new PointF(pts[0].X, rc.Bottom - 3));
                    path.CloseFigure();
                    using (var br = new LinearGradientBrush(new Rectangle(rc.X, rc.Y, rc.Width, rc.Height), Color.FromArgb(120, c), Color.FromArgb(10, c), LinearGradientMode.Vertical))
                        g.FillPath(br, path);
                }
            }
            using (var pen = new Pen(Color.FromArgb(area ? 230 : 255, c), 1.5f)) g.DrawLines(pen, pts);
        }

        static string ShortCpuName(string n)
        {
            return n.Replace(" 8-Core Processor", "").Replace("(R)", "").Replace("(TM)", "").Trim();
        }

        int Section(Graphics g, int y, string title, Level level)
        {
            using (var b = new SolidBrush(Theme.ForLevel(level, Theme.PopupDim)))
                g.DrawString(title.ToUpperInvariant(), sectionFont, b, Pad, y);
            using (var p = new Pen(Color.FromArgb(50, 255, 255, 255), 1f)) g.DrawLine(p, Pad, y + 17, W - Pad, y + 17);
            return y + 22;
        }

        int Row(Graphics g, int y, string label, string value, Color c)
        {
            using (var b = new SolidBrush(Theme.PopupDim)) g.DrawString(label, rowFont, b, Pad, y);
            using (var b = new SolidBrush(c)) g.DrawString(value, rowBold, b, Pad + 96, y);
            return y + RowH;
        }

        void Button(Graphics g, Rectangle rc, string text, bool hot)
        {
            using (var p = Theme.RoundRect(new RectangleF(rc.X, rc.Y, rc.Width, rc.Height), 7))
            using (var b = new SolidBrush(hot ? Theme.PopupButtonHot : Theme.PopupButton))
            using (var e = new Pen(Theme.PopupEdge, 1f))
            { g.FillPath(b, p); g.DrawPath(e, p); }
            var sz = g.MeasureString(text, buttonFont);
            using (var tb = new SolidBrush(Theme.PopupText))
                g.DrawString(text, buttonFont, tb, rc.X + (rc.Width - sz.Width) / 2, rc.Y + (rc.Height - sz.Height) / 2);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { titleFont.Dispose(); sectionFont.Dispose(); rowFont.Dispose(); rowBold.Dispose(); smallFont.Dispose(); buttonFont.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
