using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace PerfWidget
{
    // Palette tuned for the purple accent taskbar (dark theme, transparency on).
    static class Theme
    {
        public static readonly Color Text     = Color.FromArgb(242, 246, 240, 252);
        public static readonly Color Dim      = Color.FromArgb(150, 255, 255, 255);
        public static readonly Color Faint    = Color.FromArgb(70, 255, 255, 255);
        public static readonly Color Warm     = Color.FromArgb(255, 255, 214, 84);
        public static readonly Color Hot      = Color.FromArgb(255, 255, 156, 66);
        public static readonly Color Critical = Color.FromArgb(255, 255, 84, 92);
        public static readonly Color Bar      = Color.FromArgb(235, 214, 178, 255);   // lavender, sits well on the purple
        public static readonly Color BarTrack = Color.FromArgb(55, 255, 255, 255);
        public static readonly Color Pill     = Color.FromArgb(26, 255, 255, 255);
        public static readonly Color PillEdge = Color.FromArgb(44, 255, 255, 255);
        public static readonly Color PillHover     = Color.FromArgb(48, 255, 255, 255);
        public static readonly Color PillEdgeHover = Color.FromArgb(90, 255, 255, 255);
        public static readonly Color Spark     = Color.FromArgb(255, 214, 178, 255);
        public static readonly Color SparkUp   = Color.FromArgb(255, 120, 220, 150);

        public static readonly Color PopupBack   = Color.FromArgb(255, 28, 16, 36);
        public static readonly Color PopupEdge   = Color.FromArgb(255, 96, 60, 118);
        public static readonly Color PopupText   = Color.FromArgb(255, 240, 234, 248);
        public static readonly Color PopupDim    = Color.FromArgb(255, 160, 146, 176);
        public static readonly Color PopupButton = Color.FromArgb(255, 58, 34, 74);
        public static readonly Color PopupButtonHot = Color.FromArgb(255, 84, 50, 108);

        public static Color ForLevel(Level l, Color normal)
        {
            switch (l)
            {
                case Level.Warm: return Warm;
                case Level.Hot: return Hot;
                case Level.Critical: return Critical;
                default: return normal;
            }
        }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            float d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static string Temp(int c) { return c + "°"; }
        public static string Pct(double v) { return Math.Round(v) + "%"; }
        public static string Watts(double w) { return Math.Round(w) + "W"; }
        public static string GHz(double g) { return g.ToString("0.0") + "GHz"; }
        public static string Gigs(double g) { return g.ToString("0.0") + "G"; }
    }
}
