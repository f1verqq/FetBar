// Glass popups: flyouts (weather, calendar, screen time, clipboard, note, folders),
// live window previews, tooltips and toasts.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace StayBar
{
    static class AppNames
    {
        static readonly Dictionary<string, string> cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string Of(string exe)
        {
            if (string.IsNullOrEmpty(exe)) return "";
            string n;
            lock (cache) if (cache.TryGetValue(exe, out n)) return n;
            n = null;
            try
            {
                FileVersionInfo vi = FileVersionInfo.GetVersionInfo(exe);
                if (!string.IsNullOrWhiteSpace(vi.FileDescription)) n = vi.FileDescription.Trim();
            }
            catch { }
            if (n == null) { try { n = Path.GetFileNameWithoutExtension(exe); } catch { n = exe; } }
            if (n.Equals("Windows Explorer", StringComparison.OrdinalIgnoreCase)) n = "File Explorer";
            lock (cache) cache[exe] = n;
            return n;
        }
    }

    // ------------------------------------------------------------------
    // A borderless, rounded, acrylic window painted with GDI+ (alpha kept).
    // ------------------------------------------------------------------
    abstract class GlassWindow : Form
    {
        protected readonly float Sc;
        protected readonly bool NoActivate;
        DibSurface surface;
        protected Point Mouse = new Point(-1, -1);

        protected GlassWindow(float scale, bool noActivate)
        {
            Sc = scale; NoActivate = noActivate;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;
            Text = "StayBar popup";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
        }

        protected int S(float v) { return (int)Math.Round(v * Sc); }
        protected Palette P { get { return Config.P; } }
        protected Color Ink { get { return P.Ink; } }
        protected Color Dim { get { return P.InkDim; } }
        protected Color Faint { get { return P.InkFaint; } }
        protected Color Accent { get { return Config.Accent; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST;
                if (NoActivate) cp.ExStyle |= Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return NoActivate; } }

        protected virtual Color Tint { get { return P.FlyoutTint; } }
        protected virtual bool Rounded { get { return true; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetDwm(Handle, 33, Rounded ? 2 : 1);
            Native.SetDwm(Handle, 34, P.Light ? 0x00D8D4D0 : 0x00403A38);
            Native.SetGlass(Handle, Config.Blur, Tint);
        }

        protected override void WndProc(ref Message m)
        {
            if (NoActivate && m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = new IntPtr(Native.MA_NOACTIVATE); return; }
            base.WndProc(ref m);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Size sz = ClientSize;
            if (sz.Width <= 0 || sz.Height <= 0) return;
            if (surface == null || surface.Width != sz.Width || surface.Height != sz.Height)
            {
                if (surface != null) surface.Dispose();
                surface = new DibSurface(sz.Width, sz.Height);
            }
            using (Graphics g = Graphics.FromImage(surface.Bitmap))
            {
                Gfx.Prep(g);
                g.Clear(Config.Blur ? Color.Transparent : Color.FromArgb(255, Tint));
                PaintContent(g, sz);
            }
            surface.BlitTo(e.Graphics);
        }

        protected abstract void PaintContent(Graphics g, Size s);

        // --selftest: the window as a picture (glass simulated by its tint).
        public Bitmap Render()
        {
            Size sz = ClientSize;
            var bmp = new Bitmap(sz.Width, sz.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                Gfx.Prep(g);
                Theme.FillRound(g, new RectangleF(0, 0, sz.Width, sz.Height), Rounded ? S(8) : 0, Tint);
                Theme.StrokeRound(g, new RectangleF(0.5f, 0.5f, sz.Width - 1, sz.Height - 1), Rounded ? S(8) : 0, Color.FromArgb(40, 255, 255, 255), 1f);
                PaintContent(g, sz);
            }
            return bmp;
        }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); Mouse = e.Location; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Mouse = new Point(-1, -1); Invalidate(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing && surface != null) { surface.Dispose(); surface = null; }
            base.Dispose(disposing);
        }

        // Places the window next to the bar, centred on an anchor rectangle (screen coords).
        public void PlaceNear(Rectangle anchor)
        {
            Rectangle wa = Screen.FromRectangle(anchor).Bounds;
            int gap = S(8);
            int x = anchor.X + anchor.Width / 2 - Width / 2;
            x = Math.Max(wa.Left + gap, Math.Min(wa.Right - gap - Width, x));
            bool below = anchor.Y + anchor.Height / 2 < wa.Y + wa.Height / 2;   // a bar at the top: open downwards
            int y = below ? anchor.Bottom + gap : anchor.Top - gap - Height;
            y = Math.Max(wa.Top + gap, Math.Min(wa.Bottom - gap - Height, y));
            Location = new Point(x, y);
        }
    }

    // ------------------------------------------------------------------
    // Flyouts: activatable popups that close when you click elsewhere
    // ------------------------------------------------------------------
    abstract class Flyout : GlassWindow
    {
        public string Key;
        protected Font FTitle, FBig, FText, FSmall, FTiny, FGlyph, FGlyphSmall;

        protected Flyout(float sc) : base(sc, false)
        {
            KeyPreview = true;
            string gf = Gfx.GlyphFamily();
            FTitle = new Font("Segoe UI Semibold", 15f * sc, GraphicsUnit.Pixel);
            FBig = new Font("Segoe UI Light", 40f * sc, GraphicsUnit.Pixel);
            FText = new Font("Segoe UI", 13f * sc, GraphicsUnit.Pixel);
            FSmall = new Font("Segoe UI", 11.5f * sc, GraphicsUnit.Pixel);
            FTiny = new Font("Segoe UI", 10.5f * sc, GraphicsUnit.Pixel);
            FGlyph = new Font(gf, 14f * sc, GraphicsUnit.Pixel);
            FGlyphSmall = new Font(gf, 11f * sc, GraphicsUnit.Pixel);
        }

        public void ShowNear(Rectangle anchorScreen)
        {
            PlaceNear(anchorScreen);
            Show();
            Win.ForceForeground(Handle);
            Activate();
        }

        protected bool KeepOpen;   // a menu of ours is open (it takes the focus for a moment)

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (KeepOpen) return;
            if (!IsDisposed) BeginInvoke(new Action(Close));
        }

        // Shows a small menu from the flyout without the flyout closing.
        protected void ShowMenu(ContextMenuStrip m, Point p)
        {
            KeepOpen = true;
            m.Closed += delegate { KeepOpen = false; BeginInvoke(new Action(delegate { if (!IsDisposed && Form.ActiveForm != this) Activate(); })); };
            m.Show(this, p);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected void Header(Graphics g, string title, string right, float y)
        {
            Gfx.Text(g, title, FTitle, Ink, S(18), y);
            if (!string.IsNullOrEmpty(right)) Gfx.Text(g, right, FTiny, Dim, Width - S(18) - Gfx.TW(right, FTiny), y + S(4));
        }

        protected bool Hot(RectangleF r) { return r.Contains(Mouse); }

        protected void HotFill(Graphics g, RectangleF r, float rad)
        {
            if (Hot(r)) Theme.FillRound(g, r, rad, P.Light ? Color.FromArgb(16, 0, 0, 0) : Color.FromArgb(20, 255, 255, 255));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) foreach (Font f in new[] { FTitle, FBig, FText, FSmall, FTiny, FGlyph, FGlyphSmall }) f.Dispose();
            base.Dispose(disposing);
        }
    }

    class WeatherFlyout : Flyout
    {
        public WeatherFlyout(float sc) : base(sc) { ClientSize = new Size(S(340), S(430)); }

        protected override void PaintContent(Graphics g, Size s)
        {
            WeatherInfo w = Weather.Now;
            if (w == null || w.Error != null)
            {
                Header(g, "Weather", null, S(16));
                string msg = w == null ? "Loading the forecast…" : w.Error == "City not found" ?
                    "Couldn't find \"" + w.City + "\".\nSet your city in Settings > Widgets." : "No connection right now.\nIt will try again soon.";
                Gfx.TextIn(g, msg, FText, Dim, new RectangleF(S(18), S(50), s.Width - S(36), S(60)), Theme.MakeFormat(StringAlignment.Near));
                return;
            }
            Header(g, w.City, "Updated " + w.Fetched.ToString("HH:mm"), S(16));
            Gfx.Weather(g, new RectangleF(S(14), S(48), S(72), S(72)), Weather.Kind(w.Code), w.Day, Ink);
            Gfx.Text(g, WeatherWidget.Deg(w.Temp), FBig, Ink, S(94), S(50));
            Gfx.Text(g, Weather.Text(w.Code), FText, Ink, S(200), S(58));
            Gfx.Text(g, "Feels " + WeatherWidget.Deg(w.Feels) + "  ·  " + w.Humidity + "%", FSmall, Dim, S(200), S(80));
            Gfx.Text(g, "Wind " + (int)w.Wind + (Config.Fahrenheit ? " mph" : " km/h"), FSmall, Dim, S(200), S(98));
            using (var pen = new Pen(Color.FromArgb(30, Ink))) g.DrawLine(pen, S(18), S(134), s.Width - S(18), S(134));

            double lo = w.Min.Length > 0 ? w.Min.Min() : 0, hi = w.Max.Length > 0 ? w.Max.Max() : 1;
            float y = S(142), rowH = S(38);
            for (int i = 0; i < w.Days.Length; i++)
            {
                var row = new RectangleF(S(10), y, s.Width - S(20), rowH);
                HotFill(g, row, S(8));
                string day = i == 0 ? "Today" : w.Days[i].ToString("ddd", CultureInfo.CurrentCulture);
                Gfx.TextIn(g, day, FText, Ink, new RectangleF(S(20), y, S(60), rowH), Gfx.Left);
                Gfx.Weather(g, new RectangleF(S(80), y + S(7), rowH - S(14), rowH - S(14)), Weather.Kind(w.Codes[i]), true, Ink);
                if (w.Rain[i] >= 20) Gfx.TextIn(g, w.Rain[i] + "%", FTiny, Color.FromArgb(96, 180, 255), new RectangleF(S(110), y, S(40), rowH), Gfx.Left);
                float bx = S(196), bw = s.Width - S(250);
                Gfx.TextIn(g, WeatherWidget.Deg(w.Min[i]), FSmall, Dim, new RectangleF(S(150), y, S(40), rowH), Gfx.Right);
                Gfx.TextIn(g, WeatherWidget.Deg(w.Max[i]), FSmall, Ink, new RectangleF(bx + bw + S(6), y, S(40), rowH), Gfx.Left);
                var track = new RectangleF(bx, y + rowH / 2 - S(2.5f), bw, S(5));
                Theme.FillRound(g, track, track.Height / 2, P.Track);
                double span = Math.Max(1, hi - lo);
                float x1 = bx + (float)((w.Min[i] - lo) / span) * bw, x2 = bx + (float)((w.Max[i] - lo) / span) * bw;
                var seg = new RectangleF(x1, track.Y, Math.Max(track.Height, x2 - x1), track.Height);
                using (var lb = new LinearGradientBrush(new RectangleF(bx - 1, seg.Y, bw + 2, seg.Height), Color.FromArgb(96, 180, 255), Color.FromArgb(251, 146, 60), 0f))
                using (GraphicsPath p = Theme.Round(seg, seg.Height / 2)) g.FillPath(lb, p);
                y += rowH;
            }
        }
    }

    class CalendarFlyout : Flyout
    {
        DateTime month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        RectangleF prev, next, title;

        public CalendarFlyout(float sc) : base(sc)
        {
            int zones = WorldClockWidget.Zones().Count;
            ClientSize = new Size(S(320), S(330) + (zones > 0 ? S(20) + zones * S(30) : 0));
        }

        protected override void PaintContent(Graphics g, Size s)
        {
            DateTime today = DateTime.Today;
            string head = month.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
            head = char.ToUpper(head[0]) + head.Substring(1);
            title = new RectangleF(S(12), S(10), Gfx.TW(head, FTitle) + S(14), S(32));
            HotFill(g, title, S(6));
            Gfx.Text(g, head, FTitle, Ink, S(18), S(15));
            prev = new RectangleF(s.Width - S(84), S(10), S(32), S(32));
            next = new RectangleF(s.Width - S(46), S(10), S(32), S(32));
            HotFill(g, prev, S(6)); HotFill(g, next, S(6));
            Gfx.Glyph(g, "\uE70E", FGlyphSmall, Ink, prev);
            Gfx.Glyph(g, "\uE70D", FGlyphSmall, Ink, next);

            float cw = (s.Width - S(24)) / 7f, x0 = S(12), y0 = S(52), ch = S(36);
            for (int i = 0; i < 7; i++)
            {
                string wd = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames[(i + 1) % 7];
                Gfx.TextIn(g, wd.Substring(0, Math.Min(2, wd.Length)), FTiny, i >= 5 ? Faint : Dim, new RectangleF(x0 + i * cw, y0, cw, S(20)), Gfx.Mid);
            }
            DateTime first = CalendarWidget.WeekStart(month);
            for (int i = 0; i < 42; i++)
            {
                DateTime d = first.AddDays(i);
                var cell = new RectangleF(x0 + (i % 7) * cw, y0 + S(24) + (i / 7) * ch, cw, ch);
                bool inMonth = d.Month == month.Month, isToday = d == today, weekend = i % 7 >= 5;
                if (isToday)
                {
                    float dd = Math.Min(cw, ch) - S(4);
                    using (var b = new SolidBrush(Accent)) g.FillEllipse(b, cell.X + (cell.Width - dd) / 2, cell.Y + (cell.Height - dd) / 2, dd, dd);
                }
                Color c = isToday ? Gfx.On(Accent) : !inMonth ? Color.FromArgb(70, Ink) : weekend ? Dim : Ink;
                Gfx.TextIn(g, d.Day.ToString(), FText, c, cell, Gfx.Mid);
            }

            var zones = WorldClockWidget.Zones();
            if (zones.Count == 0) return;
            float zy = y0 + S(24) + 6 * ch + S(10);
            using (var pen = new Pen(Color.FromArgb(30, Ink))) g.DrawLine(pen, S(18), zy, s.Width - S(18), zy);
            zy += S(8);
            foreach (var z in zones)
            {
                DateTime there = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, z.Value);
                Gfx.TextIn(g, z.Key, FText, Ink, new RectangleF(S(18), zy, S(150), S(30)), Gfx.Left);
                int dd = (there.Date - today).Days;
                string rel = dd == 0 ? "Today" : dd > 0 ? "Tomorrow" : "Yesterday";
                TimeSpan off = z.Value.GetUtcOffset(DateTime.UtcNow) - TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow);
                string diff = (off >= TimeSpan.Zero ? "+" : "−") + Math.Abs(off.TotalHours).ToString("0.#") + "h";
                Gfx.TextIn(g, rel + ", " + diff, FTiny, Dim, new RectangleF(S(140), zy, S(100), S(30)), Gfx.Left);
                Gfx.TextIn(g, there.ToString(Config.Clock24 ? "HH:mm" : "h:mm tt"), FTitle, Ink, new RectangleF(s.Width - S(118), zy, S(100), S(30)), Gfx.Right);
                zy += S(30);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (prev.Contains(e.Location)) month = month.AddMonths(-1);
            else if (next.Contains(e.Location)) month = month.AddMonths(1);
            else if (title.Contains(e.Location)) month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            month = month.AddMonths(e.Delta > 0 ? -1 : 1);
            Invalidate();
        }
    }

    class ScreenTimeFlyout : Flyout
    {
        readonly BarForm bar;
        public ScreenTimeFlyout(BarForm bar) : base(bar.U.Scale)
        {
            this.bar = bar;
            ClientSize = new Size(S(360), S(208) + Math.Max(1, Math.Min(7, ScreenTime.Top(7).Count)) * S(40));
        }

        protected override void PaintContent(Graphics g, Size s)
        {
            Header(g, "Screen time", "Today", S(16));
            using (var f = new Font("Segoe UI Semibold", 30f * Sc, GraphicsUnit.Pixel))
                Gfx.Text(g, ScreenTime.Format(ScreenTime.Total), f, Ink, S(16), S(42));
            if (ScreenTime.Yesterday > 0)
            {
                double diff = ScreenTime.Total - ScreenTime.Yesterday;
                string t = (diff >= 0 ? "▲ " : "▼ ") + ScreenTime.Format(Math.Abs(diff)) + (diff >= 0 ? " more" : " less") + " than yesterday";
                Gfx.Text(g, t, FTiny, Dim, S(18), S(86));
            }
            var chart = new RectangleF(S(18), S(110), s.Width - S(36), S(56));
            Gfx.Bars(g, chart, ScreenTime.Hours, Accent, Color.FromArgb(30, Ink), DateTime.Now.Hour);
            foreach (int h in new[] { 0, 6, 12, 18 })
                Gfx.Text(g, h.ToString("00"), FTiny, Faint, chart.X + chart.Width * h / 24f, chart.Bottom + S(4));
            float y = S(196);
            var top = ScreenTime.Top(7);
            if (top.Count == 0) { Gfx.Text(g, "Nothing tracked yet today.", FText, Dim, S(18), y); return; }
            double max = top[0].Value;
            foreach (var kv in top)
            {
                var row = new RectangleF(S(10), y, s.Width - S(20), S(40));
                HotFill(g, row, S(8));
                Bitmap icon = bar.IconFor(kv.Key);
                var ir = new RectangleF(S(18), y + S(8), S(24), S(24));
                if (icon != null) g.DrawImage(icon, ir);
                string name = AppNames.Of(kv.Key);
                Gfx.TextIn(g, name, FSmall, Ink, new RectangleF(S(52), y + S(3), s.Width - S(140), S(20)), Gfx.Left);
                var track = new RectangleF(S(52), y + S(26), s.Width - S(140), S(4));
                Gfx.Meter(g, track, (float)(100 * kv.Value / Math.Max(1, max)), Accent, P.Track);
                Gfx.TextIn(g, ScreenTime.Format(kv.Value), FSmall, Dim, new RectangleF(s.Width - S(82), y, S(64), S(40)), Gfx.Right);
                y += S(40);
            }
        }
    }

    class ClipboardFlyout : Flyout
    {
        int scroll;
        RectangleF clear;
        const int Rows = 8;

        public ClipboardFlyout(float sc) : base(sc)
        {
            int n = Math.Max(1, Math.Min(Rows, Clip.Items.Count));
            ClientSize = new Size(S(380), S(56) + n * S(50) + S(10));
        }

        RectangleF RowRect(int visible, Size s) { return new RectangleF(S(10), S(50) + visible * S(50), s.Width - S(20), S(48)); }
        RectangleF DelRect(RectangleF row) { return new RectangleF(row.Right - S(34), row.Y + S(10), S(28), S(28)); }

        protected override void PaintContent(Graphics g, Size s)
        {
            Header(g, "Clipboard", null, S(16));
            clear = new RectangleF(s.Width - S(74), S(12), S(60), S(28));
            if (Clip.Items.Count > 0)
            {
                HotFill(g, clear, S(6));
                Gfx.TextIn(g, "Clear", FSmall, Accent, clear, Gfx.Mid);
            }
            if (Clip.Items.Count == 0)
            {
                Gfx.TextIn(g, Config.ClipboardOn ? "Nothing copied yet. Copy some text and it shows up here." : "Clipboard history is off. Turn it on in Settings > Widgets.",
                    FSmall, Dim, new RectangleF(S(18), S(50), s.Width - S(36), S(44)), Theme.MakeFormat(StringAlignment.Near));
                return;
            }
            var fmt = Theme.MakeFormat(StringAlignment.Near);
            for (int v = 0; v < Rows && scroll + v < Clip.Items.Count; v++)
            {
                string item = Clip.Items[scroll + v];
                RectangleF row = RowRect(v, s);
                bool hot = Hot(row);
                Theme.FillRound(g, row, S(8), hot ? (P.Light ? Color.FromArgb(18, 0, 0, 0) : Color.FromArgb(24, 255, 255, 255)) : (P.Light ? Color.FromArgb(8, 0, 0, 0) : Color.FromArgb(10, 255, 255, 255)));
                string one = string.Join(" ", item.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries));
                Gfx.TextIn(g, one, FSmall, Ink, new RectangleF(row.X + S(12), row.Y + S(5), row.Width - S(52), S(20)), fmt);
                string meta = item.Length + " chars" + (item.Contains("\n") ? ", " + (item.Split('\n').Length) + " lines" : "");
                Gfx.TextIn(g, meta, FTiny, Faint, new RectangleF(row.X + S(12), row.Y + S(25), row.Width - S(52), S(18)), fmt);
                if (hot)
                {
                    RectangleF del = DelRect(row);
                    HotFill(g, del, S(6));
                    Gfx.Glyph(g, "\uE711", FGlyphSmall, Dim, del);
                }
            }
            if (Clip.Items.Count > Rows)
            {
                float track = s.Height - S(60), th = track * Rows / Clip.Items.Count;
                float ty = S(50) + (track - th) * scroll / Math.Max(1, Clip.Items.Count - Rows);
                Theme.FillRound(g, new RectangleF(s.Width - S(5), ty, S(3), th), S(1.5f), Color.FromArgb(80, Ink));
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            scroll = Math.Max(0, Math.Min(Math.Max(0, Clip.Items.Count - Rows), scroll + (e.Delta > 0 ? -1 : 1)));
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (clear.Contains(e.Location) && Clip.Items.Count > 0) { Clip.Clear(); Close(); BarForm.Poke(); return; }
            for (int v = 0; v < Rows && scroll + v < Clip.Items.Count; v++)
            {
                RectangleF row = RowRect(v, ClientSize);
                if (!row.Contains(e.Location)) continue;
                string item = Clip.Items[scroll + v];
                if (DelRect(row).Contains(e.Location)) { Clip.Remove(item); scroll = Math.Max(0, Math.Min(scroll, Clip.Items.Count - Rows)); Invalidate(); BarForm.Poke(); return; }
                Clip.Copy(item);
                Close();
                BarForm.Toast("Copied", Win.ShortTitle(item.Trim(), 60));
                return;
            }
        }
    }

    class NoteFlyout : Flyout
    {
        readonly TextBox box;
        readonly Timer save;
        readonly NoteStore store;

        public NoteFlyout(float sc) : this(sc, Note.For("note")) { }

        public NoteFlyout(float sc, NoteStore store) : base(sc)
        {
            this.store = store;
            ClientSize = new Size(S(340), S(300));
            box = new TextBox();
            box.Multiline = true;
            box.AcceptsReturn = true;
            box.AcceptsTab = true;
            box.BorderStyle = BorderStyle.None;
            box.ScrollBars = ScrollBars.None;
            box.Font = new Font("Segoe UI", 13f * sc, GraphicsUnit.Pixel);
            box.BackColor = FieldColor;
            box.ForeColor = P.Ink;
            box.Text = store.Text;
            box.SetBounds(S(26), S(56), ClientSize.Width - S(52), ClientSize.Height - S(80));
            Controls.Add(box);
            save = new Timer();
            save.Interval = 500;
            save.Tick += delegate { save.Stop(); store.Text = box.Text; BarForm.Poke(); };
            box.TextChanged += delegate { save.Stop(); save.Start(); };
        }

        Color FieldColor { get { return P.Light ? Color.FromArgb(250, 250, 252) : Color.FromArgb(30, 30, 36); } }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            box.Focus();
            box.SelectionStart = box.TextLength;
        }

        protected override void PaintContent(Graphics g, Size s)
        {
            Header(g, "Quick note", "Saves as you type", S(16));
            Theme.FillRound(g, new RectangleF(S(14), S(48), s.Width - S(28), s.Height - S(62)), S(10), FieldColor);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            save.Stop();
            store.Text = box.Text;
            BarForm.Poke();
            base.OnFormClosed(e);
        }
    }

    // App folder: a grid of the apps inside it.
    class FolderFlyout : Flyout
    {
        readonly BarForm bar;
        readonly AppGroup folder;
        const int Cols = 4;

        public FolderFlyout(BarForm bar, AppGroup folder) : base(bar.U.Scale)
        {
            this.bar = bar; this.folder = folder;
            int rows = Math.Max(1, (folder.Members.Count + Cols - 1) / Cols);
            ClientSize = new Size(S(24) + Cols * S(84), S(56) + rows * S(92) + S(8));
        }

        RectangleF Cell(int i) { return new RectangleF(S(12) + (i % Cols) * S(84), S(50) + (i / Cols) * S(92), S(84), S(90)); }

        protected override void PaintContent(Graphics g, Size s)
        {
            Header(g, folder.Name, folder.Members.Count + " apps", S(16));
            for (int i = 0; i < folder.Members.Count; i++)
            {
                AppGroup m = folder.Members[i];
                RectangleF c = Cell(i);
                HotFill(g, RectangleF.Inflate(c, -S(3), -S(3)), S(10));
                var ir = new RectangleF(c.X + (c.Width - S(40)) / 2, c.Y + S(10), S(40), S(40));
                if (m.Icon != null) g.DrawImage(m.Icon, ir);
                else Theme.FillRound(g, ir, S(10), Color.FromArgb(80, Accent));
                if (m.Windows.Count > 0)
                    Theme.FillRound(g, new RectangleF(c.X + c.Width / 2 - S(3), ir.Bottom + S(3), S(6), S(3)), S(1.5f), Accent);
                Gfx.TextIn(g, m.Name, FTiny, Ink, new RectangleF(c.X + S(4), c.Y + S(60), c.Width - S(8), S(22)), Gfx.Mid);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            for (int i = 0; i < folder.Members.Count; i++)
            {
                if (!Cell(i).Contains(e.Location)) continue;
                AppGroup m = folder.Members[i];
                if (e.Button == MouseButtons.Left) { Close(); bar.ClickGroup(m, false); }
                else if (e.Button == MouseButtons.Right)
                {
                    var menu = bar.NewMenu(false);
                    BarForm.Item(menu, "Move out of folder", () => { Close(); bar.RemoveFromFolder(folder, m); });
                    BarForm.Item(menu, "Rename folder…", () => { Close(); bar.RenameFolder(folder); });
                    menu.Show(this, e.Location);
                }
                return;
            }
        }
    }

    // ------------------------------------------------------------------
    // Tooltip bubble (no activation)
    // ------------------------------------------------------------------
    class TipWindow : GlassWindow
    {
        string text = "";
        readonly Font head, body;

        public TipWindow(float sc) : base(sc, true)
        {
            head = new Font("Segoe UI Semibold", 12.5f * sc, GraphicsUnit.Pixel);
            body = new Font("Segoe UI", 11.5f * sc, GraphicsUnit.Pixel);
        }

        public bool Showing { get { return IsHandleCreated && Native.IsWindowVisible(Handle); } }

        public void HideTip() { if (IsHandleCreated) Native.ShowWindow(Handle, Native.SW_HIDE); }

        public void ShowText(string t, Rectangle anchorScreen)
        {
            text = t ?? "";
            string[] lines = text.Split('\n');
            float w = 0, h = S(16);
            for (int i = 0; i < lines.Length; i++)
            {
                Font f = i == 0 ? head : body;
                w = Math.Max(w, Gfx.TW(lines[i], f));
                h += f.GetHeight() + (i == 0 ? S(2) : 0);
            }
            Size = new Size((int)Math.Min(S(420), w + S(26)), (int)h);
            PlaceNear(anchorScreen);
            Native.ShowWindow(Handle, Native.SW_SHOWNA);
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, Left, Top, Width, Height, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            Invalidate();
        }

        protected override void PaintContent(Graphics g, Size s)
        {
            string[] lines = text.Split('\n');
            float y = S(8);
            for (int i = 0; i < lines.Length; i++)
            {
                Font f = i == 0 ? head : body;
                Gfx.Text(g, lines[i], f, i == 0 ? Ink : Dim, S(13), y);
                y += f.GetHeight() + (i == 0 ? S(2) : 0);
            }
        }
    }

    // ------------------------------------------------------------------
    // Toast: a short message above the bar
    // ------------------------------------------------------------------
    class ToastWindow : GlassWindow
    {
        readonly string title, message;
        readonly Font head, body;
        readonly Timer life;

        public ToastWindow(float sc, string title, string message) : base(sc, true)
        {
            this.title = title; this.message = message ?? "";
            head = new Font("Segoe UI Semibold", 13.5f * sc, GraphicsUnit.Pixel);
            body = new Font("Segoe UI", 12f * sc, GraphicsUnit.Pixel);
            int w = (int)Math.Min(S(420), Math.Max(S(220), Math.Max(Gfx.TW(title, head), Gfx.TW(this.message, body)) + S(70)));
            Size = new Size(w, S(64));
            life = new Timer();
            life.Interval = 4500;
            life.Tick += delegate { life.Stop(); Close(); };
        }

        public void ShowAbove(Rectangle barScreen)
        {
            Rectangle anchor = new Rectangle(barScreen.Right - Width / 2 - S(20), barScreen.Y, 1, barScreen.Height);
            PlaceNear(anchor);
            Native.ShowWindow(Handle, Native.SW_SHOWNA);
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, Left, Top, Width, Height, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            life.Start();
        }

        protected override void PaintContent(Graphics g, Size s)
        {
            var dot = new RectangleF(S(16), (s.Height - S(30)) / 2, S(30), S(30));
            using (var b = new SolidBrush(Accent)) g.FillEllipse(b, dot);
            Gfx.Glyph(g, "\uE73E", Gfx.FontOf(Gfx.GlyphFamily(), 14f * Sc), Gfx.On(Accent), dot);
            Gfx.TextIn(g, title, head, Ink, new RectangleF(S(56), S(11), s.Width - S(66), head.GetHeight()), Gfx.Left);
            Gfx.TextIn(g, message, body, Dim, new RectangleF(S(56), S(13) + head.GetHeight(), s.Width - S(66), body.GetHeight() + 2), Gfx.Left);
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Close(); }
    }

    // ------------------------------------------------------------------
    // Live window previews (DWM thumbnails) when hovering an app
    // ------------------------------------------------------------------
    class PreviewWindow : GlassWindow
    {
        readonly BarForm bar;
        public AppGroup Group;
        readonly List<IntPtr> wins = new List<IntPtr>();
        readonly List<IntPtr> thumbs = new List<IntPtr>();
        readonly List<RectangleF> cards = new List<RectangleF>();
        readonly Font title;
        const int MaxWindows = 6;

        public PreviewWindow(BarForm bar) : base(bar.U.Scale, true)
        {
            this.bar = bar;
            title = new Font("Segoe UI", 11.5f * Sc, GraphicsUnit.Pixel);
        }

        int ThumbH { get { return S(118); } }
        int Head { get { return S(32); } }

        public void ShowFor(AppGroup g, Rectangle anchorScreen)
        {
            ClearThumbs();
            Group = g;
            wins.Clear();
            wins.AddRange(g.Windows.Take(MaxWindows));
            if (wins.Count == 0) { HidePreview(); return; }
            if (!IsHandleCreated) CreateHandle();
            // measure each card from the window's aspect ratio
            cards.Clear();
            float x = S(8), maxW = Screen.PrimaryScreen.Bounds.Width - S(40);
            var sizes = new List<SizeF>();
            foreach (IntPtr w in wins)
            {
                Native.RECT wr;
                float aspect = 16f / 10f;
                if (Native.GetWindowRect(w, out wr) && wr.Bottom - wr.Top > 50 && !Native.IsIconic(w))
                    aspect = Math.Max(0.6f, Math.Min(2.4f, (wr.Right - wr.Left) / (float)(wr.Bottom - wr.Top)));
                sizes.Add(new SizeF(Math.Max(S(150), ThumbH * aspect), ThumbH));
            }
            float total = sizes.Sum(z => z.Width) + S(8) * (sizes.Count + 1);
            float k = total > maxW ? maxW / total : 1f;
            foreach (SizeF z in sizes)
            {
                cards.Add(new RectangleF(x, S(8), z.Width * k + S(12), Head + z.Height * k + S(10)));
                x += z.Width * k + S(12) + S(6);
            }
            Size = new Size((int)(x + S(2)), (int)(cards.Max(c => c.Bottom) + S(8)));
            PlaceNear(anchorScreen);
            for (int i = 0; i < wins.Count; i++)
            {
                IntPtr th = IntPtr.Zero;
                if (!Native.IsIconic(wins[i]) && Native.DwmRegisterThumbnail(Handle, wins[i], out th) == 0) thumbs.Add(th);
                else thumbs.Add(IntPtr.Zero);
            }
            UpdateThumbs();
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, Left, Top, Width, Height, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            Invalidate();
        }

        RectangleF ThumbArea(RectangleF card) { return new RectangleF(card.X + S(6), card.Y + Head, card.Width - S(12), card.Height - Head - S(6)); }
        RectangleF CloseRect(RectangleF card) { return new RectangleF(card.Right - S(30), card.Y + S(4), S(26), S(26)); }

        void UpdateThumbs()
        {
            for (int i = 0; i < thumbs.Count; i++)
            {
                if (thumbs[i] == IntPtr.Zero) continue;
                RectangleF area = ThumbArea(cards[i]);
                SIZE src;
                Native.DwmQueryThumbnailSourceSize(thumbs[i], out src);
                float sw = Math.Max(1, src.cx), sh = Math.Max(1, src.cy);
                float k = Math.Min(area.Width / sw, area.Height / sh);
                float w = sw * k, h = sh * k;
                var dest = new Rectangle((int)(area.X + (area.Width - w) / 2), (int)(area.Y + (area.Height - h) / 2), (int)w, (int)h);
                var props = new Native.DWM_THUMBNAIL_PROPERTIES();
                props.dwFlags = Native.DWM_TNP_RECTDESTINATION | Native.DWM_TNP_VISIBLE | Native.DWM_TNP_OPACITY | Native.DWM_TNP_SOURCECLIENTAREAONLY;
                props.rcDestination = new Native.RECT(dest);
                props.opacity = 255;
                props.fVisible = true;
                props.fSourceClientAreaOnly = false;
                Native.DwmUpdateThumbnailProperties(thumbs[i], ref props);
            }
        }

        void ClearThumbs()
        {
            foreach (IntPtr t in thumbs) if (t != IntPtr.Zero) Native.DwmUnregisterThumbnail(t);
            thumbs.Clear();
        }

        public void HidePreview()
        {
            ClearThumbs();
            Group = null;
            if (IsHandleCreated) Native.ShowWindow(Handle, Native.SW_HIDE);
        }

        public bool Showing { get { return Group != null && IsHandleCreated && Native.IsWindowVisible(Handle); } }

        protected override void PaintContent(Graphics g, Size s)
        {
            for (int i = 0; i < cards.Count && i < wins.Count; i++)
            {
                RectangleF c = cards[i];
                bool hot = c.Contains(Mouse);
                bool active = wins[i] == Native.GetForegroundWindow();
                Theme.FillRound(g, c, S(10), hot ? (P.Light ? Color.FromArgb(22, 0, 0, 0) : Color.FromArgb(26, 255, 255, 255)) : (P.Light ? Color.FromArgb(8, 0, 0, 0) : Color.FromArgb(10, 255, 255, 255)));
                if (active) Theme.StrokeRound(g, RectangleF.Inflate(c, -0.5f, -0.5f), S(10), Color.FromArgb(150, Accent), 1.2f);
                if (Group != null && Group.Icon != null) g.DrawImage(Group.Icon, new RectangleF(c.X + S(9), c.Y + S(8), S(16), S(16)));
                Gfx.TextIn(g, Win.TitleOf(wins[i]), title, Ink, new RectangleF(c.X + S(31), c.Y + S(4), c.Width - S(66), S(24)), Gfx.Left);
                if (hot)
                {
                    RectangleF cr = CloseRect(c);
                    if (cr.Contains(Mouse)) Theme.FillRound(g, cr, S(6), Color.FromArgb(220, 196, 43, 28));
                    Gfx.Glyph(g, "\uE711", Gfx.FontOf(Gfx.GlyphFamily(), 10f * Sc), cr.Contains(Mouse) ? Color.White : Dim, cr);
                }
                if (i < thumbs.Count && thumbs[i] == IntPtr.Zero)
                {
                    RectangleF a = ThumbArea(c);
                    Theme.FillRound(g, a, S(6), P.Light ? Color.FromArgb(12, 0, 0, 0) : Color.FromArgb(12, 255, 255, 255));
                    if (Group != null && Group.Icon != null)
                    {
                        float d = Math.Min(S(44), a.Height * 0.6f);
                        g.DrawImage(Group.Icon, new RectangleF(a.X + (a.Width - d) / 2, a.Y + (a.Height - d) / 2 - S(6), d, d));
                    }
                    Gfx.TextIn(g, "Minimized", Gfx.FontOf("Segoe UI", 10f * Sc), Faint, new RectangleF(a.X, a.Bottom - S(26), a.Width, S(20)), Gfx.Mid);
                }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            for (int i = 0; i < cards.Count && i < wins.Count; i++)
            {
                if (!cards[i].Contains(e.Location)) continue;
                IntPtr w = wins[i];
                if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && CloseRect(cards[i]).Contains(e.Location)))
                {
                    Native.PostMessage(w, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    HidePreview();
                    bar.SoonRefresh();
                    return;
                }
                if (e.Button == MouseButtons.Left)
                {
                    HidePreview();
                    if (w == Native.GetForegroundWindow() && !Native.IsIconic(w)) Native.ShowWindow(w, Native.SW_MINIMIZE);
                    else Win.Activate(w);
                }
                return;
            }
        }

        protected override void Dispose(bool disposing)
        {
            ClearThumbs();
            base.Dispose(disposing);
        }
    }
}
