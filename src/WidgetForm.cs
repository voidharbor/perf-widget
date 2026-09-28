using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace PerfWidget
{
    // The strip that lives inside the taskbar: a layered child window of Shell_TrayWnd painted with
    // per-pixel alpha, so the taskbar's own acrylic tint shows through and nothing has to guess the colour.
    // Everything is laid out in 96-dpi design pixels and multiplied by the taskbar's DPI scale.
    // Bars, mercury and the hover glow ease toward their targets at 30 fps; the frame timer stops
    // itself once everything has settled, so an idle strip costs nothing between samples.
    class WidgetForm : Form
    {
        const int DesignHeight = 48;
        const float DesignLeftInset = 68;     // clear of the widgets/weather button at the far left

        public event EventHandler LeftClick;
        public event EventHandler RightClick;

        readonly Font labelFont = new Font("Segoe UI", 7.5f, FontStyle.Bold, GraphicsUnit.Point);
        readonly Font valueFont = new Font("Segoe UI", 10.5f, FontStyle.Bold, GraphicsUnit.Point);
        readonly Font glyphFont = new Font("Segoe UI", 8.5f, FontStyle.Bold, GraphicsUnit.Point);
        readonly StringFormat fmt = (StringFormat)StringFormat.GenericTypographic.Clone();
        readonly Timer anim = new Timer();

        // Fixed slot widths (device px at the current DPI) so proportional digits never make the strip jitter.
        float wLabel, wVramLabel, wTemp, wPct, wWatts, wGHz, wGigs, wRate, wPing, wArrow, textH;
        const float PadX = 12, IconW = 13, Gap = 7, GroupGap = 18, BarY = 35, BarH = 3;   // design px
        int dpi = 96;
        float scale = 1f;
        int widthPx, heightPx;
        Reading last;
        IntPtr parentTray = IntPtr.Zero;   // set before CreateHandle so the window is born inside the taskbar

        // Animated quantities: a = displayed, t = target.
        double aGpu, aVram, aCpu, aMem, aDown, aUp, aMerc, aHover;
        double tGpu, tVram, tCpu, tMem, tDown, tUp, tMerc, tHover;
        bool primed, hover;

        public WidgetForm()
        {
            TopLevel = false;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            Cursor = Cursors.Hand;
            fmt.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
            anim.Interval = 33;
            anim.Tick += delegate { Frame(); };
            ApplyDpi(96);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Parent = parentTray;   // a WS_CHILD window must have a parent at creation, or CreateWindowEx fails
                cp.Style |= Native.WS_CHILD;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        float S(float designPx) { return designPx * scale; }

        Bitmap NewBitmap(int w, int h)
        {
            var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            bmp.SetResolution(dpi, dpi);   // point-sized fonts then scale with the taskbar, not with GDI+'s startup guess
            return bmp;
        }

        void ApplyDpi(int newDpi)
        {
            dpi = newDpi;
            scale = dpi / 96f;
            heightPx = (int)Math.Round(DesignHeight * scale);
            using (var bmp = NewBitmap(8, 8))
            using (var g = Graphics.FromImage(bmp))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                wLabel = g.MeasureString("GPU", labelFont, 200, fmt).Width;
                wLabel = Math.Max(wLabel, g.MeasureString("MEM", labelFont, 200, fmt).Width);
                wLabel = Math.Max(wLabel, g.MeasureString("NET", labelFont, 200, fmt).Width);
                wVramLabel = g.MeasureString("VRAM", labelFont, 200, fmt).Width;
                wTemp  = g.MeasureString("100\u00B0", valueFont, 200, fmt).Width;
                wPct   = g.MeasureString("100%", valueFont, 200, fmt).Width;
                wWatts = g.MeasureString("300W", valueFont, 200, fmt).Width;
                wGHz   = g.MeasureString("5.5GHz", valueFont, 200, fmt).Width;
                wGigs  = g.MeasureString("31.6G", valueFont, 200, fmt).Width;
                wRate  = g.MeasureString("99.9M", valueFont, 200, fmt).Width;
                wPing  = g.MeasureString("999ms", valueFont, 200, fmt).Width;
                wArrow = g.MeasureString("\u2193", glyphFont, 200, fmt).Width;
                textH  = g.MeasureString("Xg", valueFont, 200, fmt).Height;
            }
            float x = S(PadX + IconW + 10);
            x += GroupWidth(wTemp, wPct, wWatts) + S(Gap + 4) + wVramLabel + S(6) + wGigs + S(GroupGap);   // GPU incl. VRAM
            x += GroupWidth(wPct, wGHz) + S(GroupGap);                                                     // CPU
            x += GroupWidth(wGigs) + S(GroupGap);                                                          // MEM
            x += wLabel + S(6) + (wArrow + S(2) + wRate) * 2 + S(Gap) * 2 + wPing;                          // NET
            widthPx = (int)Math.Ceiling(x + S(PadX));
            Size = new Size(widthPx, heightPx);
        }

        float GroupWidth(params float[] slots)
        {
            float w = wLabel + S(6);
            for (int i = 0; i < slots.Length; i++) w += slots[i] + (i < slots.Length - 1 ? S(Gap) : 0);
            return w;
        }

        public bool IsEmbedded
        {
            get { return IsHandleCreated && parentTray != IntPtr.Zero && Native.IsWindow(parentTray) && Native.GetParent(Handle) == parentTray; }
        }

        // Create inside the taskbar and sit at the left. Returns false when there is no taskbar yet.
        public bool Embed()
        {
            IntPtr tray = Native.Taskbar();
            if (tray == IntPtr.Zero) return false;
            parentTray = tray;
            if (!IsHandleCreated) CreateHandle();
            if (Native.GetParent(Handle) != tray) Native.SetParent(Handle, tray);
            Reposition();
            Visible = true;
            Native.SetWindowPos(Handle, Native.HWND_TOP, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            if (last != null) Draw();
            return true;
        }

        // Follow the taskbar's DPI and height, stay vertically centred, and stay on top of the XAML sibling.
        public void Reposition()
        {
            if (parentTray == IntPtr.Zero || !IsHandleCreated) return;
            int d = Native.DpiOf(parentTray);
            if (d != dpi) { ApplyDpi(d); if (last != null) Draw(); }
            Rectangle tr = Native.WindowRect(parentTray);
            int y = Math.Max(0, (tr.Height - heightPx) / 2);
            Native.SetWindowPos(Handle, Native.HWND_TOP, (int)Math.Round(S(DesignLeftInset)), y, widthPx, heightPx, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }

        public Rectangle ScreenRect
        {
            get { return IsHandleCreated ? Native.WindowRect(Handle) : Rectangle.Empty; }
        }

        // New sample: set the targets and let the frame timer ease the display toward them.
        public void Render(Reading r)
        {
            last = r;
            tGpu = r.GpuOk ? r.GpuLoad / 100.0 : 0;
            tVram = r.GpuOk && r.VramTotalGB > 0 ? r.VramUsedGB / r.VramTotalGB : 0;
            tCpu = r.CpuUtility / 100.0;
            tMem = r.MemFraction;
            tDown = r.NetOk ? Fmt.RateBar(r.NetDown) : 0;
            tUp = r.NetOk ? Fmt.RateBar(r.NetUp) : 0;
            tMerc = r.GpuOk ? Math.Max(0, Math.Min(1, (r.GpuTemp - 20) / 70.0)) : 0;   // 20..90 C spans the tube
            if (!primed)
            {
                aGpu = tGpu; aVram = tVram; aCpu = tCpu; aMem = tMem; aDown = tDown; aUp = tUp; aMerc = tMerc;
                primed = true;
            }
            if (!IsHandleCreated) return;
            Draw();
            anim.Start();
        }

        static bool Step(ref double a, double t)
        {
            double d = t - a;
            if (Math.Abs(d) < 0.0015) { a = t; return false; }
            a += d * 0.22;
            return true;
        }

        void Frame()
        {
            if (!IsHandleCreated || last == null) { anim.Stop(); return; }
            bool moving = false;
            moving |= Step(ref aGpu, tGpu);
            moving |= Step(ref aVram, tVram);
            moving |= Step(ref aCpu, tCpu);
            moving |= Step(ref aMem, tMem);
            moving |= Step(ref aDown, tDown);
            moving |= Step(ref aUp, tUp);
            moving |= Step(ref aMerc, tMerc);
            moving |= Step(ref aHover, tHover);
            bool pulsing = last.Overall == Level.Critical;
            Draw();
            if (!moving && !pulsing) anim.Stop();
        }

        void Draw()
        {
            using (var bmp = NewBitmap(widthPx, heightPx))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    PaintStrip(g, last);
                }
                Native.PushLayered(Handle, bmp);
            }
        }

        static Color Mix(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        void PaintStrip(Graphics g, Reading r)
        {
            double pulse = r.Overall == Level.Critical ? 0.5 + 0.5 * Math.Sin(Environment.TickCount / 1000.0 * 2 * Math.PI / 1.2) : 0;

            // Pill, brighter under the mouse, with a breathing red halo only while actually throttling.
            var pillRect = new RectangleF(0.5f, S(5.5f), widthPx - 1, S(37));
            using (var path = Theme.RoundRect(pillRect, S(9)))
            using (var fill = new SolidBrush(Mix(Theme.Pill, Theme.PillHover, aHover)))
            using (var edge = new Pen(Mix(Theme.PillEdge, Theme.PillEdgeHover, aHover), 1f))
            {
                g.FillPath(fill, path);
                g.DrawPath(edge, path);
            }
            if (pulse > 0)
            {
                using (var halo = Theme.RoundRect(new RectangleF(pillRect.X - S(1), pillRect.Y - S(1), pillRect.Width + S(2), pillRect.Height + S(2)), S(10)))
                using (var pen = new Pen(Color.FromArgb((int)(40 + 130 * pulse), Theme.Critical), S(2)))
                    g.DrawPath(pen, halo);
            }

            float x = S(PadX);
            DrawThermometer(g, x, r, pulse);
            x += S(IconW + 10);

            float textY = S(24) - textH / 2 - S(2);   // optical centre above the bar

            // GPU
            {
                float gx = x;
                x = DrawLabel(g, x, textY, "GPU");
                Color tempColor = Theme.ForLevel(r.GpuLevel, Theme.Text);
                x = DrawValue(g, x, textY, wTemp, r.GpuOk ? Theme.Temp(r.GpuTemp) : "--", tempColor) + S(Gap);
                x = DrawValue(g, x, textY, wPct, r.GpuOk ? Theme.Pct(r.GpuLoad) : "--", Theme.Text) + S(Gap);
                x = DrawValue(g, x, textY, wWatts, r.GpuOk ? Theme.Watts(r.PowerW) : "--", Theme.Text);
                DrawBar(g, gx, x, aGpu, Theme.ForLevel(r.GpuLevel, Theme.Bar));
                x += S(Gap + 4);
                float vx = x;
                x = DrawLabelW(g, x, textY, "VRAM", wVramLabel);
                x = DrawValue(g, x, textY, wGigs, r.GpuOk ? Theme.Gigs(r.VramUsedGB) : "--", Theme.Text);
                DrawBar(g, vx, x, aVram, Theme.Bar);
                x += S(GroupGap);
                DrawDivider(g, x - S(GroupGap / 2));
            }
            // CPU
            {
                float gx = x;
                x = DrawLabel(g, x, textY, "CPU");
                x = DrawValue(g, x, textY, wPct, Theme.Pct(r.CpuUtility), Theme.Text) + S(Gap);
                x = DrawValue(g, x, textY, wGHz, r.CpuBaseGHz > 0 ? Theme.GHz(r.CpuEffectiveGHz) : "--", Theme.ForLevel(r.CpuLevel, Theme.Text));
                DrawBar(g, gx, x, aCpu, Theme.ForLevel(r.CpuLevel, Theme.Bar));
                x += S(GroupGap);
                DrawDivider(g, x - S(GroupGap / 2));
            }
            // MEM
            {
                float gx = x;
                x = DrawLabel(g, x, textY, "MEM");
                x = DrawValue(g, x, textY, wGigs, Theme.Gigs(r.MemUsedGB), Theme.ForLevel(r.MemLevel, Theme.Text));
                DrawBar(g, gx, x, aMem, Theme.ForLevel(r.MemLevel, Theme.Bar));
                x += S(GroupGap);
                DrawDivider(g, x - S(GroupGap / 2));
            }
            // NET
            {
                x = DrawLabel(g, x, textY, "NET");
                float dx = x;
                x = DrawGlyph(g, x, textY, "\u2193");
                x = DrawValue(g, x, textY, wRate, r.NetOk ? Fmt.Rate(r.NetDown) : "--", Theme.Text);
                DrawBar(g, dx, x, aDown, Theme.Bar);
                x += S(Gap);
                float ux = x;
                x = DrawGlyph(g, x, textY, "\u2191");
                x = DrawValue(g, x, textY, wRate, r.NetOk ? Fmt.Rate(r.NetUp) : "--", Theme.Text);
                DrawBar(g, ux, x, aUp, Theme.SparkUp);
                x += S(Gap);
                string ping = r.PingMs >= 0 ? r.PingMs + "ms" : r.PingMs == -1 ? "--" : "";
                x = DrawValue(g, x, textY, wPing, ping, Theme.ForLevel(r.PingLevel, Theme.Text));
            }
        }

        float DrawLabel(Graphics g, float x, float textY, string s)
        {
            return DrawLabelW(g, x, textY, s, wLabel);
        }

        float DrawLabelW(Graphics g, float x, float textY, string s, float width)
        {
            using (var b = new SolidBrush(Theme.Dim))
                g.DrawString(s, labelFont, b, x, textY + S(4), fmt);
            return x + width + S(6);
        }

        float DrawGlyph(Graphics g, float x, float textY, string s)
        {
            using (var b = new SolidBrush(Theme.Dim))
                g.DrawString(s, glyphFont, b, x, textY + S(2.5f), fmt);
            return x + wArrow + S(2);
        }

        // Right-aligned inside a fixed-width slot.
        float DrawValue(Graphics g, float x, float textY, float slot, string s, Color c)
        {
            float w = g.MeasureString(s, valueFont, 500, fmt).Width;
            using (var b = new SolidBrush(c))
                g.DrawString(s, valueFont, b, x + slot - w, textY, fmt);
            return x + slot;
        }

        void DrawBar(Graphics g, float x0, float x1, double frac, Color fill)
        {
            frac = Math.Max(0, Math.Min(1, frac));
            float h = S(BarH);
            var track = new RectangleF(x0, S(BarY), x1 - x0, h);
            using (var p = Theme.RoundRect(track, h / 2)) using (var b = new SolidBrush(Theme.BarTrack)) g.FillPath(b, p);
            float w = (float)(track.Width * frac);
            if (w >= h)
                using (var p = Theme.RoundRect(new RectangleF(x0, S(BarY), w, h), h / 2)) using (var b = new SolidBrush(fill)) g.FillPath(b, p);
        }

        void DrawDivider(Graphics g, float x)
        {
            using (var p = new Pen(Theme.Faint, 1f)) g.DrawLine(p, x, S(14), x, S(34));
        }

        // Small thermometer, the Mac widget's glyph. Tinted by the overall level, mercury eases to the GPU temp.
        void DrawThermometer(Graphics g, float x, Reading r, double pulse)
        {
            Color tint = Theme.ForLevel(r.Overall, Color.FromArgb(225, 255, 255, 255));
            if (pulse > 0) tint = Color.FromArgb((int)(140 + 115 * pulse), tint);
            float cx = x + S(IconW) / 2, top = S(12), bottom = S(37);
            float tubeW = S(5), bulbR = S(4.5f);
            using (var pen = new Pen(tint, S(1.4f)))
            using (var brush = new SolidBrush(tint))
            {
                var tube = new RectangleF(cx - tubeW / 2, top, tubeW, bottom - bulbR - top);
                using (var tp = Theme.RoundRect(tube, tubeW / 2)) g.DrawPath(pen, tp);
                g.DrawEllipse(pen, cx - bulbR, bottom - bulbR * 2, bulbR * 2, bulbR * 2);
                g.FillEllipse(brush, cx - bulbR + S(1.5f), bottom - bulbR * 2 + S(1.5f), bulbR * 2 - S(3), bulbR * 2 - S(3));
                float innerTop = tube.Y + S(3), innerBottom = tube.Bottom - S(1);
                float h = (float)((innerBottom - innerTop) * aMerc);
                if (h > 0) g.FillRectangle(brush, cx - S(1f), innerBottom - h, S(2f), h + S(1));
            }
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case Native.WM_MOUSEACTIVATE: m.Result = (IntPtr)Native.MA_NOACTIVATE; return;
                case Native.WM_LBUTTONUP: if (LeftClick != null) LeftClick(this, EventArgs.Empty); break;
                case Native.WM_RBUTTONUP: if (RightClick != null) RightClick(this, EventArgs.Empty); break;
                case Native.WM_MOUSEMOVE:
                    if (!hover) { hover = true; tHover = 1; Native.TrackLeave(Handle); if (primed) anim.Start(); }
                    break;
                case Native.WM_MOUSELEAVE:
                    hover = false; tHover = 0; if (primed) anim.Start();
                    break;
            }
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { anim.Dispose(); labelFont.Dispose(); valueFont.Dispose(); glyphFont.Dispose(); fmt.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
