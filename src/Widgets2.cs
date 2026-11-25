// Phase 3 widgets: time, study and productivity.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace StayBar
{
    // ==================================================================
    // Time
    // ==================================================================
    class FlipClockWidget : Widget
    {
        public FlipClockWidget() : base("flipclock", "Flip clock", "Time & weather", "\uE823", "Hours and minutes on two flip cards, like an old desk clock.") { }
        public override string[] Styles { get { return new[] { "Hours and minutes", "With seconds" }; } }
        bool Secs { get { return Style == "With seconds"; } }
        public override int Width(Ui u) { return (int)(u.F(10) * 2 + u.F(46) * (Secs ? 3 : 2) + u.F(6) * (Secs ? 2 : 1)); }
        public override string LineText(Ui u) { return DateTime.Now.ToString(Config.Clock24 ? "HH:mm" : "h:mm"); }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            RectangleF t = u.Tile(r);
            DateTime now = DateTime.Now;
            string[] parts = { now.ToString(Config.Clock24 ? "HH" : "hh"), now.ToString("mm"), now.ToString("ss") };
            int n = Secs ? 3 : 2;
            float cw = u.F(46), gap = u.F(6), ch = t.Height - u.F(12);
            float x = t.X + (t.Width - n * cw - (n - 1) * gap) / 2f, y = t.Y + (t.Height - ch) / 2f;
            Color card = u.P.Light ? Color.FromArgb(240, 255, 255, 255) : Color.FromArgb(235, 14, 14, 18);
            Color ink = u.P.Light ? u.Ink : Color.FromArgb(246, 246, 250);
            Font f = Gfx.FontOf("Segoe UI Semibold", (float)Math.Round(ch * 0.62f));
            for (int i = 0; i < n; i++)
            {
                var cr = new RectangleF(x + i * (cw + gap), y, cw, ch);
                Theme.FillRound(g, cr, u.F(6), card);
                Theme.FillRound(g, new RectangleF(cr.X, cr.Y, cr.Width, cr.Height / 2f), u.F(6), Color.FromArgb(u.P.Light ? 8 : 14, 255, 255, 255));
                Gfx.TextIn(g, parts[i], i == 2 ? Gfx.FontOf("Segoe UI Semibold", (float)Math.Round(ch * 0.5f)) : f, i == 2 ? u.Dim : ink, cr, Gfx.Mid);
                using (var pen = new Pen(Color.FromArgb(u.P.Light ? 40 : 160, 0, 0, 0), Math.Max(1f, u.F(1.2f))))
                    g.DrawLine(pen, cr.X, cr.Y + cr.Height / 2f, cr.Right, cr.Y + cr.Height / 2f);
            }
        }

        public override string Tip(Ui u, Rectangle r, Point p) { return DateTime.Now.ToString("D"); }
        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b) { if (b == MouseButtons.Left) bar.ToggleFlyout("calendar", r, () => new CalendarFlyout(bar.U.Scale)); }
    }

    class DigitalClockWidget : Widget
    {
        public DigitalClockWidget() : base("digiclock", "Digital clock", "Time & weather", "\uE823", "A seven-segment clock that glows in your accent color.") { }
        public override string[] Styles { get { return new[] { "Hours and minutes", "With seconds" }; } }
        bool Secs { get { return Style == "With seconds"; } }
        public override int Width(Ui u) { return (int)(u.F(20) + (Secs ? 6 : 4) * u.F(19) + (Secs ? 2 : 1) * u.F(10)); }
        public override string LineText(Ui u) { return DateTime.Now.ToString(Config.Clock24 ? "HH:mm" : "h:mm"); }

        // segments a b c d e f g for 0..9
        static readonly int[] Seg = { 0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F };

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            RectangleF t = u.Tile(r);
            DateTime now = DateTime.Now;
            string s = now.ToString(Config.Clock24 ? "HH" : "hh") + ":" + now.ToString("mm") + (Secs ? ":" + now.ToString("ss") : "");
            float dw = u.F(15), dh = t.Height - u.F(18), sp = u.F(4), colon = u.F(10);
            float total = s.Count(char.IsDigit) * (dw + sp) + s.Count(c => c == ':') * colon - sp;
            float x = t.X + (t.Width - total) / 2f, y = t.Y + (t.Height - dh) / 2f;
            Color on = u.Accent, off = Color.FromArgb(u.P.Light ? 18 : 22, u.Ink);
            foreach (char c in s)
            {
                if (c == ':')
                {
                    bool blink = now.Millisecond < 500 || !Config.Seconds;
                    float d = u.F(3);
                    using (var b = new SolidBrush(blink ? on : off))
                    {
                        g.FillEllipse(b, x + colon / 2f - d / 2f, y + dh * 0.3f - d / 2f, d, d);
                        g.FillEllipse(b, x + colon / 2f - d / 2f, y + dh * 0.7f - d / 2f, d, d);
                    }
                    x += colon;
                    continue;
                }
                Digit(g, x, y, dw, dh, Seg[c - '0'], on, off, u);
                x += dw + sp;
            }
        }

        static void Digit(Graphics g, float x, float y, float w, float h, int mask, Color on, Color off, Ui u)
        {
            float th = Math.Max(2f, w * 0.2f), hh = h / 2f;
            // a, b, c, d, e, f, g
            var rects = new[]
            {
                new RectangleF(x + th * 0.5f, y, w - th, th), new RectangleF(x + w - th, y + th * 0.5f, th, hh - th * 0.5f),
                new RectangleF(x + w - th, y + hh, th, hh - th * 0.5f), new RectangleF(x + th * 0.5f, y + h - th, w - th, th),
                new RectangleF(x, y + hh, th, hh - th * 0.5f), new RectangleF(x, y + th * 0.5f, th, hh - th * 0.5f),
                new RectangleF(x + th * 0.5f, y + hh - th / 2f, w - th, th),
            };
            for (int i = 0; i < 7; i++) Theme.FillRound(g, rects[i], th / 2f, (mask & (1 << i)) != 0 ? on : off);
        }

        public override string Tip(Ui u, Rectangle r, Point p) { return DateTime.Now.ToString("D"); }
        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b) { if (b == MouseButtons.Left) bar.ToggleFlyout("calendar", r, () => new CalendarFlyout(bar.U.Scale)); }
    }

    class AnalogClockWidget : Widget
    {
        public AnalogClockWidget() : base("analog", "Analog clock", "Time & weather", "\uE823", "A small clock face with hands, and the date next to it.") { }
        public override bool HasLabel { get { return true; } }
        public override int Width(Ui u) { float d = u.H - u.TilePad * 2 - u.F(8); return (int)(d + u.F(16) + (ShowLabel ? u.F(52) : 0)); }
        public override string LineText(Ui u) { return DateTime.Now.ToString(Config.Clock24 ? "HH:mm" : "h:mm"); }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            RectangleF t = u.Tile(r);
            float d = t.Height - u.F(8);
            var c = new PointF(t.X + u.F(8) + d / 2f, t.Y + t.Height / 2f);
            float rad = d / 2f;
            using (var b = new SolidBrush(Color.FromArgb(u.P.Light ? 14 : 18, u.Ink))) g.FillEllipse(b, c.X - rad, c.Y - rad, d, d);
            using (var pen = new Pen(Color.FromArgb(70, u.Ink), Math.Max(1f, u.F(1.2f))))
                for (int i = 0; i < 12; i++)
                {
                    double a = i * Math.PI / 6;
                    float k = i % 3 == 0 ? 0.74f : 0.84f;
                    g.DrawLine(pen, c.X + (float)Math.Sin(a) * rad * k, c.Y - (float)Math.Cos(a) * rad * k, c.X + (float)Math.Sin(a) * rad * 0.94f, c.Y - (float)Math.Cos(a) * rad * 0.94f);
                }
            DateTime now = DateTime.Now;
            Hand(g, c, rad * 0.5f, ((now.Hour % 12) + now.Minute / 60.0) / 12.0, u.Ink, Math.Max(2f, u.F(2.6f)));
            Hand(g, c, rad * 0.76f, (now.Minute + now.Second / 60.0) / 60.0, u.Ink, Math.Max(1.5f, u.F(1.8f)));
            if (Config.Seconds) Hand(g, c, rad * 0.84f, now.Second / 60.0, u.Accent, Math.Max(1f, u.F(1f)));
            using (var b = new SolidBrush(u.Accent)) g.FillEllipse(b, c.X - u.F(2), c.Y - u.F(2), u.F(4), u.F(4));
            if (ShowLabel)
            {
                float x = c.X + rad + u.F(8), w = t.Right - x - u.F(6);
                float th = u.Tiny.GetHeight(), nh = u.Title.GetHeight();
                float y = t.Y + (t.Height - th - nh) / 2f;
                Gfx.TextIn(g, now.ToString("ddd", CultureInfo.CurrentCulture), u.Tiny, u.Dim, new RectangleF(x, y, w, th), Gfx.Left);
                Gfx.TextIn(g, now.Day.ToString(), u.Title, u.Ink, new RectangleF(x, y + th * 0.95f, w, nh), Gfx.Left);
            }
        }

        static void Hand(Graphics g, PointF c, float len, double turn, Color col, float width)
        {
            double a = turn * 2 * Math.PI;
            using (var pen = new Pen(col, width)) { pen.StartCap = pen.EndCap = LineCap.Round; g.DrawLine(pen, c.X, c.Y, c.X + (float)Math.Sin(a) * len, c.Y - (float)Math.Cos(a) * len); }
        }

        public override string Tip(Ui u, Rectangle r, Point p) { return DateTime.Now.ToString("F"); }
        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b) { if (b == MouseButtons.Left) bar.ToggleFlyout("calendar", r, () => new CalendarFlyout(bar.U.Scale)); }
    }

    class ProgressWidget : Widget
    {
        public ProgressWidget() : base("progress", "Time progress", "Time & weather", "\uE916", "How much of the day, week, month or year has gone by.") { }
        public override string[] Styles { get { return new[] { "Day", "Week", "Month", "Year" }; } }
        public override bool HasLabel { get { return true; } }
        public override int Width(Ui u) { return u.FI(124); }

        public double Pct
        {
            get
            {
                DateTime now = DateTime.Now, a, b;
                switch (Style)
                {
                    case "Week": a = CalendarWidget.WeekStart(now); b = a.AddDays(7); break;
                    case "Month": a = new DateTime(now.Year, now.Month, 1); b = a.AddMonths(1); break;
                    case "Year": a = new DateTime(now.Year, 1, 1); b = a.AddYears(1); break;
                    default: a = now.Date; b = a.AddDays(1); break;
                }
                return 100.0 * (now - a).TotalSeconds / (b - a).TotalSeconds;
            }
        }

        public override string LineText(Ui u) { return Style + " " + (int)Pct + "%"; }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            RectangleF t = u.Tile(r);
            double pct = Pct;
            float x = t.X + u.F(11), w = t.Width - u.F(22);
            float th = ShowLabel ? u.Tiny.GetHeight() : 0, nh = u.Title.GetHeight(), mh = Math.Max(3f, u.F(5));
            float y = t.Y + (t.Height - th - nh - mh - u.F(4)) / 2f;
            if (ShowLabel) Gfx.TextIn(g, Style == "Day" ? "Today" : "This " + Style.ToLowerInvariant(), u.Tiny, u.Dim, new RectangleF(x, y, w, th), Gfx.Left);
            Gfx.TextIn(g, pct.ToString(pct < 10 ? "0.0" : "0", CultureInfo.CurrentCulture) + "%", u.Title, u.Ink, new RectangleF(x, y + th * 0.95f, w, nh), Gfx.Left);
            Gfx.Meter(g, new RectangleF(x, y + th + nh + u.F(3), w, mh), (float)pct, u.Accent, u.P.Track);
        }

        public override string Tip(Ui u, Rectangle r, Point p)
        {
            DateTime now = DateTime.Now;
            return "Day " + (int)(100 * now.TimeOfDay.TotalHours / 24) + "%  ·  week " + (int)(100 * (now - CalendarWidget.WeekStart(now)).TotalDays / 7) +
                   "%\nMonth " + (int)(100.0 * (now.Day - 1 + now.TimeOfDay.TotalDays) / DateTime.DaysInMonth(now.Year, now.Month)) + "%  ·  year " + (int)(100.0 * (now.DayOfYear - 1) / (DateTime.IsLeapYear(now.Year) ? 366 : 365)) + "%";
        }
    }

    class CountdownWidget : Widget
    {
        public CountdownWidget() : base("countdown", "Countdown", "Study", "\uE787", "Days left until a date: exams, holidays, a deadline.") { }
        public override bool Multi { get { return true; } }
        public override bool HasLabel { get { return true; } }
        public override int Width(Ui u) { return u.FI(140); }
        public override int CompactWidth(Ui u) { return u.FI(100); }

        string Title { get { return Opt("title", "New Year"); } }

        DateTime Target
        {
            get
            {
                DateTime d;
                string s = Opt("date", "");
                if (s.Length > 0 && DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out d)) return d;
                if (s.Length > 0 && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return d;
                return new DateTime(DateTime.Now.Year + 1, 1, 1);
            }
        }

        string Left()
        {
            TimeSpan t = Target - DateTime.Now;
            if (t.TotalSeconds <= 0) return t.TotalDays > -1 ? "Today!" : "Done";
            if (t.TotalDays >= 2) return (int)Math.Ceiling(t.TotalDays - 0.0001) + " days";
            if (t.TotalHours >= 1) return (int)t.TotalHours + "h " + t.Minutes + "m";
            return t.Minutes + "m " + t.Seconds + "s";
        }

        public override string LineText(Ui u) { return Left() + " " + Title; }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            InfoTile(g, r, u, "\uE787", u.Accent, Title, Left(), null);
        }

        public override string Tip(Ui u, Rectangle r, Point p) { return Title + ": " + Target.ToString("f") + "\n" + Left() + " left\nRight-click: widget settings"; }

        public override void Options(IWidgetOptions o)
        {
            o.Text("Name", "What you're counting down to", "title", "New Year");
            o.Text("Date", "Like " + DateTime.Today.AddDays(30).ToString("d") + " or " + DateTime.Today.AddDays(30).ToString("d") + " 09:00", "date", "");
        }
    }

    class StopwatchWidget : Widget
    {
        public StopwatchWidget() : base("stopwatch", "Stopwatch", "Time & weather", "\uE916", "Click to start or pause, right-click to reset.") { }
        public override bool HasLabel { get { return true; } }
        public override int Width(Ui u) { return u.FI(120); }
        readonly Stopwatch sw = new Stopwatch();
        public override int RepaintMs { get { return sw.IsRunning ? 100 : 0; } }

        string Time()
        {
            TimeSpan t = Svc.Demo ? TimeSpan.FromSeconds(83.4) : sw.Elapsed;
            return t.TotalHours >= 1 ? ((int)t.TotalHours) + ":" + t.ToString("mm\\:ss") : t.ToString("mm\\:ss") + "." + (t.Milliseconds / 100);
        }

        public override string LineText(Ui u) { return Time(); }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            InfoTile(g, r, u, "\uE916", sw.IsRunning || Svc.Demo ? u.Accent : u.Dim, sw.IsRunning || Svc.Demo ? "Running" : sw.Elapsed.Ticks > 0 ? "Paused" : "Stopwatch", Time(), null);
        }

        public override string Tip(Ui u, Rectangle r, Point p) { return "Stopwatch " + Time() + "\nClick: " + (sw.IsRunning ? "pause" : "start") + ", right-click: reset"; }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            if (b == MouseButtons.Left) { if (sw.IsRunning) sw.Stop(); else sw.Start(); }
            else if (b == MouseButtons.Right)
            {
                ContextMenuStrip m = bar.NewMenu(false);
                BarForm.Item(m, sw.IsRunning ? "Pause" : "Start", () => { if (sw.IsRunning) sw.Stop(); else sw.Start(); });
                BarForm.Item(m, "Reset", () => sw.Reset());
                BarForm.Item(m, "Copy the time", () => { try { Clipboard.SetText(Time()); } catch { } });
                bar.ShowMenu(m, p);
            }
            bar.Invalidate();
        }
    }

    class TimerWidget : Widget
    {
        public TimerWidget() : base("timer", "Timer", "Time & weather", "\uE916", "A countdown timer. Click to start or pause; right-click for quick times.") { }
        public override bool Multi { get { return true; } }
        public override bool HasLabel { get { return true; } }
        public override int Width(Ui u) { return u.FI(118); }

        DateTime end;
        TimeSpan left = TimeSpan.Zero;
        bool running;
        int Minutes { get { return Math.Max(1, OptInt("minutes", 10)); } }
        TimeSpan Remaining { get { if (Svc.Demo) return TimeSpan.FromSeconds(372); return running ? end - DateTime.UtcNow : left > TimeSpan.Zero ? left : TimeSpan.FromMinutes(Minutes); } }
        public override bool Live { get { return running; } }
        public override string LineText(Ui u) { return Focus.Clock(Remaining); }

        public override void Tick(BarForm bar)
        {
            if (!running || DateTime.UtcNow < end) return;
            running = false; left = TimeSpan.Zero;
            bar.ShowToast(Opt("label", "Timer"), "Time's up (" + Minutes + " min)");
            if (!Automations.Quiet) System.Media.SystemSounds.Exclamation.Play();
            BarForm.Poke();
        }

        void Toggle()
        {
            if (running) { left = end - DateTime.UtcNow; running = false; }
            else { end = DateTime.UtcNow + (left > TimeSpan.Zero ? left : TimeSpan.FromMinutes(Minutes)); running = true; }
        }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            RectangleF t = u.Tile(r);
            float d = t.Height - u.F(14), rw = Math.Max(2.5f, u.F(3.4f));
            var c = new PointF(t.X + u.F(8) + d / 2, t.Y + t.Height / 2);
            double total = Minutes * 60.0;
            float pct = (float)(100 * Math.Max(0, Math.Min(1, Remaining.TotalSeconds / total)));
            Gfx.Ring(g, c, d / 2 - rw / 2, rw, pct, u.Accent, u.P.Track);
            float id = d * 0.62f;
            Gfx.MediaIcon(g, new RectangleF(c.X - id / 2, c.Y - id / 2, id, id), running || Svc.Demo ? 2 : 1, running ? u.Ink : u.Accent);
            float x = c.X + d / 2 + u.F(8), w = t.Right - x - u.F(6);
            float th = ShowLabel ? u.Tiny.GetHeight() : 0, nh = u.Title.GetHeight();
            float y = t.Y + (t.Height - th - nh) / 2f;
            if (ShowLabel) Gfx.TextIn(g, Opt("label", "Timer"), u.Tiny, u.Dim, new RectangleF(x, y, w, th), Gfx.Left);
            Gfx.TextIn(g, Focus.Clock(Remaining), u.Title, u.Ink, new RectangleF(x, y + th, w, nh), Gfx.Left);
        }

        public override string Tip(Ui u, Rectangle r, Point p) { return Opt("label", "Timer") + " · " + Focus.Clock(Remaining) + (running ? " left" : "") + "\nClick to " + (running ? "pause" : "start") + "; right-click for quick times"; }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            if (b == MouseButtons.Left) { Toggle(); bar.Invalidate(); return; }
            if (b != MouseButtons.Right) return;
            ContextMenuStrip m = bar.NewMenu(true);
            BarForm.Item(m, running ? "Pause" : "Start", () => Toggle());
            BarForm.Item(m, "Reset", () => { running = false; left = TimeSpan.Zero; });
            m.Items.Add(new ToolStripSeparator());
            foreach (int mins in new[] { 1, 3, 5, 10, 15, 25, 45, 60 })
            {
                int v = mins;
                BarForm.Item(m, mins + " min", () => { SetOpt("minutes", v.ToString()); running = false; left = TimeSpan.Zero; Toggle(); }).Checked = Minutes == mins;
            }
            bar.ShowMenu(m, p);
        }

        public override void Options(IWidgetOptions o)
        {
            o.Text("Name", "Shown above the time and in the notification", "label", "Timer");
            o.Number("Length", null, "minutes", 1, 180, 10, " min");
        }
    }

    class AlarmWidget : Widget
    {
        public AlarmWidget() : base("alarm", "Alarms", "Time & weather", "\uEA8F", "Daily alarms with a notification and a sound. Set them in the widget settings.") { }
        public override bool HasLabel { get { return true; } }
        public override int Width(Ui u) { return u.FI(128); }
        string firedKey;

        List<KeyValuePair<TimeSpan, string>> Alarms()
        {
            var list = new List<KeyValuePair<TimeSpan, string>>();
            foreach (string part in Opt("alarms", "07:30 Wake up").Split(','))
            {
                string s = part.Trim();
                if (s.Length < 4) continue;
                string[] p = s.Split(new[] { ' ' }, 2);
                TimeSpan t;
                if (TimeSpan.TryParse(p[0].Replace('.', ':'), out t)) list.Add(new KeyValuePair<TimeSpan, string>(t, p.Length > 1 ? p[1].Trim() : "Alarm"));
            }
            return list.OrderBy(a => a.Key).ToList();
        }

        KeyValuePair<TimeSpan, string>? Next(out bool tomorrow)
        {
            tomorrow = false;
            var list = Alarms();
            if (list.Count == 0) return null;
            TimeSpan now = DateTime.Now.TimeOfDay;
            foreach (var a in list) if (a.Key > now) return a;
            tomorrow = true;
            return list[0];
        }

        public override void Tick(BarForm bar)
        {
            DateTime now = DateTime.Now;
            foreach (var a in Alarms())
            {
                if (a.Key.Hours != now.Hour || a.Key.Minutes != now.Minute) continue;
                string k = now.ToString("yyyyMMddHHmm") + a.Value;
                if (k == firedKey) continue;
                firedKey = k;
                bar.ShowToast("⏰ " + a.Value, a.Key.ToString("hh\\:mm"));
                if (!Automations.Quiet) System.Media.SystemSounds.Exclamation.Play();
            }
        }

        public override string LineText(Ui u) { bool tm; var n = Next(out tm); return n == null ? "No alarms" : n.Value.Key.ToString("hh\\:mm"); }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            bool tm;
            var n = Next(out tm);
            InfoTile(g, r, u, "\uEA8F", n == null ? u.Dim : u.Accent, n == null ? "Alarms" : (tm ? "Tomorrow, " : "") + n.Value.Value, n == null ? "None set" : n.Value.Key.ToString("hh\\:mm"), null);
        }

        public override string Tip(Ui u, Rectangle r, Point p)
        {
            var list = Alarms();
            return list.Count == 0 ? "No alarms. Click to add some." : string.Join("\n", list.Select(a => a.Key.ToString("hh\\:mm") + "  " + a.Value).ToArray()) + "\nClick to change them";
        }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b) { if (b == MouseButtons.Left) bar.OpenSettings("w:" + Key); }

        public override void Options(IWidgetOptions o)
        {
            o.Text("Alarms", "Times and names, separated by commas: 07:30 Wake up, 13:00 Lunch", "alarms", "07:30 Wake up");
        }
    }

    class SunWidget : Widget
    {
        public SunWidget() : base("sun", "Sunrise & sunset", "Time & weather", "\uE706", "The next sunrise or sunset for your weather city. Worked out on your PC, no internet needed.") { }
        public override bool HasLabel { get { return true; } }
        public override int Width(Ui u) { return u.FI(124); }

        bool Times(out DateTime rise, out DateTime set, out DateTime riseTomorrow)
        {
            rise = set = riseTomorrow = DateTime.MinValue;
            double lat, lon; string name;
            if (!Geo.Get(out lat, out lon, out name)) return false;
            DateTime d;
            Sky.SunTimes(DateTime.Today, lat, lon, out rise, out set);
            Sky.SunTimes(DateTime.Today.AddDays(1), lat, lon, out riseTomorrow, out d);
            return rise != DateTime.MinValue;
        }

        public override string LineText(Ui u)
        {
            DateTime a, b, c;
            return Times(out a, out b, out c) ? "↑" + a.ToString("HH:mm") + " ↓" + b.ToString("HH:mm") : "–";
        }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            DateTime rise, set, rise2;
            RectangleF t = u.Tile(r);
            float isz = t.Height - u.F(16);
            var ir = new RectangleF(t.X + u.F(8), t.Y + (t.Height - isz) / 2f, isz, isz);
            if (!Times(out rise, out set, out rise2))
            {
                Gfx.Weather(g, ir, 0, true, u.Faint);
                Gfx.TextIn(g, "Set a city", u.Small, u.Dim, new RectangleF(ir.Right + u.F(6), t.Y, t.Right - ir.Right - u.F(12), t.Height), Gfx.Left);
                return;
            }
            DateTime now = DateTime.Now;
            bool day = now >= rise && now < set;
            DateTime next = now < rise ? rise : now < set ? set : rise2;
            Gfx.Weather(g, ir, 0, day, u.Ink);
            float x = ir.Right + u.F(7), w = t.Right - x - u.F(8);
            float th = ShowLabel ? u.Tiny.GetHeight() : 0, nh = u.Title.GetHeight();
            float y = t.Y + (t.Height - th - nh) / 2f;
            if (ShowLabel) Gfx.TextIn(g, next == set ? "Sunset" : "Sunrise", u.Tiny, u.Dim, new RectangleF(x, y, w, th), Gfx.Left);
            Gfx.TextIn(g, next.ToString(Config.Clock24 ? "HH:mm" : "h:mm tt"), u.Title, u.Ink, new RectangleF(x, y + th * 0.95f, w, nh), Gfx.Left);
        }

        public override string Tip(Ui u, Rectangle r, Point p)
        {
            DateTime rise, set, rise2;
            if (!Times(out rise, out set, out rise2)) return "Set your city in Settings > Widgets (Weather)";
            TimeSpan len = set - rise;
            return "Sunrise " + rise.ToString("HH:mm") + "  ·  sunset " + set.ToString("HH:mm") + "\nDaylight " + (int)len.TotalHours + "h " + len.Minutes + "m";
        }
    }

    class MoonWidget : Widget
    {
        public MoonWidget() : base("moon", "Moon phase", "Time & weather", "\uE708", "Tonight's moon and how much of it is lit.") { }
        public override bool HasLabel { get { return true; } }
        public override int Width(Ui u) { return u.FI(140); }
        public override string LineText(Ui u) { double p = Sky.MoonPhase(DateTime.UtcNow); return (int)Math.Round(Sky.Illumination(p) * 100) + "% moon"; }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            RectangleF t = u.Tile(r);
            double p = Sky.MoonPhase(DateTime.UtcNow);
            float d = t.Height - u.F(18);
            var mr = new RectangleF(t.X + u.F(10), t.Y + (t.Height - d) / 2f, d, d);
            DrawMoon(g, mr, p, u);
            float x = mr.Right + u.F(9), w = t.Right - x - u.F(8);
            float th = ShowLabel ? u.Tiny.GetHeight() : 0, nh = u.Title.GetHeight();
            float y = t.Y + (t.Height - th - nh) / 2f;
            if (ShowLabel) Gfx.TextIn(g, Sky.MoonName(p), u.Tiny, u.Dim, new RectangleF(x, y, w, th), Gfx.Left);
            Gfx.TextIn(g, (int)Math.Round(Sky.Illumination(p) * 100) + "% lit", u.Title, u.Ink, new RectangleF(x, y + th * 0.95f, w, nh), Gfx.Left);
        }

        public static void DrawMoon(Graphics g, RectangleF mr, double p, Ui u)
        {
            Color dark = u.P.Light ? Color.FromArgb(40, 0, 0, 0) : Color.FromArgb(40, 255, 255, 255);
            Color lit = u.P.Light ? Color.FromArgb(120, 130, 160) : Color.FromArgb(236, 238, 255);
            using (var b = new SolidBrush(dark)) g.FillEllipse(b, mr);
            double k = Sky.Illumination(p);
            float a = (float)(mr.Width / 2 * Math.Abs(1 - 2 * k));
            float cx = mr.X + mr.Width / 2f;
            bool waxing = p < 0.5;
            using (var half = new GraphicsPath())
            using (var ell = new GraphicsPath())
            {
                half.AddPie(mr.X, mr.Y, mr.Width, mr.Height, waxing ? -90 : 90, 180);
                ell.AddEllipse(cx - a, mr.Y, a * 2, mr.Height);
                using (var reg = new Region(half))
                {
                    if (k < 0.5) reg.Exclude(ell); else reg.Union(ell);
                    using (var b = new SolidBrush(lit)) g.FillRegion(b, reg);
                }
            }
        }

        public override string Tip(Ui u, Rectangle r, Point p)
        {
            double ph = Sky.MoonPhase(DateTime.UtcNow);
            double toFull = ((0.5 - ph + 1) % 1) * 29.53, toNew = ((1 - ph) % 1) * 29.53;
            return Sky.MoonName(ph) + ", " + (int)Math.Round(Sky.Illumination(ph) * 100) + "% lit\nFull moon in " + (int)Math.Round(toFull) + " days, new moon in " + (int)Math.Round(toNew) + " days";
        }
    }

    // ==================================================================
    // Study & productivity
    // ==================================================================
    class TodoWidget : Widget
    {
        public TodoWidget() : base("todo", "To-do list", "Study", "\uE73A", "Your to-dos with checkboxes, saved on this PC. Click to add and tick them off.") { }
        public override bool HasLabel { get { return true; } }
        public override int Width(Ui u) { return u.FI(168); }
        public override int CompactWidth(Ui u) { return u.FI(110); }
        public override string LineText(Ui u) { return Todo.Open + " to do"; }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            TodoItem first = Todo.Items.FirstOrDefault(i => !i.Done);
            int n = Todo.Open;
            InfoTile(g, r, u, "\uE73A", n > 0 ? u.Accent : u.Dim, n == 0 ? "To do" : n + " to do", first == null ? "All done" : first.Text, u.Bold);
        }

        public override string Tip(Ui u, Rectangle r, Point p)
        {
            var open = Todo.Items.Where(i => !i.Done).Take(8).Select(i => "• " + i.Text).ToArray();
            return open.Length == 0 ? "Nothing to do. Click to add something." : string.Join("\n", open);
        }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            if (b == MouseButtons.Left) bar.ToggleFlyout("todo", r, () => new TodoFlyout(bar.U.Scale));
        }
    }

    class NextClassWidget : Widget
    {
        public NextClassWidget() : base("nextclass", "Next class", "Study", "\uE7BE", "The class you're in, or the next one, from a simple weekly timetable you write (and calendar events).") { }
        public override bool HasLabel { get { return true; } }
        public override int Width(Ui u) { return u.FI(168); }
        public override int CompactWidth(Ui u) { return u.FI(116); }

        void Now(out string caption, out string title)
        {
            ClassSlot cur, next; DateTime at;
            Schedule.Now(Ics.AllCached(), out cur, out next, out at);
            if (cur != null)
            {
                caption = "Now" + (cur.Room.Length > 0 ? " · " + cur.Room : "") + " · until " + Fmt(cur.End);
                title = cur.Title;
                return;
            }
            if (next != null)
            {
                TimeSpan in_ = at - DateTime.Now;
                string when = in_.TotalMinutes < 60 ? "in " + Math.Max(1, (int)Math.Ceiling(in_.TotalMinutes)) + " min" :
                              at.Date == DateTime.Today ? "at " + Fmt(next.Start) : at.Date == DateTime.Today.AddDays(1) ? "tomorrow " + Fmt(next.Start) : at.ToString("ddd") + " " + Fmt(next.Start);
                caption = "Next " + when + (next.Room.Length > 0 ? " · " + next.Room : "");
                title = next.Title;
                return;
            }
            caption = "Timetable";
            title = Schedule.Slots.Count == 0 ? "Add your classes" : "No more classes";
        }

        static string Fmt(TimeSpan t) { return Config.Clock24 ? t.ToString("hh\\:mm") : DateTime.Today.Add(t).ToString("h:mm tt"); }

        public override string LineText(Ui u) { string c, t; Now(out c, out t); return t; }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            string c, t;
            Now(out c, out t);
            InfoTile(g, r, u, "\uE7BE", u.Accent, c, t, u.Bold);
        }

        public override string Tip(Ui u, Rectangle r, Point p)
        {
            var today = Schedule.Slots.Where(s => s.Day == DateTime.Today.DayOfWeek).OrderBy(s => s.Start).Select(s => Fmt(s.Start) + "–" + Fmt(s.End) + "  " + s.Title + (s.Room.Length > 0 ? " (" + s.Room + ")" : "")).ToArray();
            return (today.Length == 0 ? "No classes today" : "Today:\n" + string.Join("\n", today)) + "\nClick to edit your timetable";
        }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            if (b == MouseButtons.Left) Edit();
        }

        static void Edit()
        {
            try { Schedule.EnsureFile(); Process.Start("notepad.exe", "\"" + Schedule.FilePath + "\""); } catch { }
        }

        public override void Options(IWidgetOptions o)
        {
            o.Note("Your timetable is a text file, one class per line, like \"Mon,Wed 08:30-09:25 Maths | Room 12\". Calendar events from a Calendar events widget show up here too.");
            o.Button("Timetable", null, "Edit", Edit);
        }
    }

    class HabitWidget : Widget
    {
        public HabitWidget() : base("habits", "Habit tracker", "Study", "\uE73E", "A dot for each day you kept a habit. Click to tick today off.") { }
        public override bool HasLabel { get { return true; } }
        public override int Width(Ui u) { return u.FI(150); }
        public override string LineText(Ui u) { string h = Habits.Names.FirstOrDefault() ?? "Habit"; return h + " " + Habits.Streak(h) + "d"; }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            RectangleF t = u.Tile(r);
            string habit = Habits.Names.FirstOrDefault() ?? "Habit";
            float x = t.X + u.F(11), w = t.Width - u.F(22);
            float th = ShowLabel ? u.Tiny.GetHeight() : 0, dot = u.F(11), gap = u.F(5.5f);
            float y = t.Y + (t.Height - th - dot - u.F(4)) / 2f;
            int streak = Habits.Streak(habit);
            if (ShowLabel) Gfx.TextIn(g, habit + (streak > 1 ? " · " + streak + " days" : ""), u.Tiny, u.Dim, new RectangleF(x, y, w, th), Gfx.Left);
            float dy = y + th + u.F(4);
            int days = Math.Max(3, Math.Min(10, (int)((w + gap) / (dot + gap))));
            for (int i = 0; i < days; i++)
            {
                DateTime d = DateTime.Today.AddDays(i - days + 1);
                var dr = new RectangleF(x + i * (dot + gap), dy, dot, dot);
                bool done = Habits.Done(habit, d);
                if (done) using (var b = new SolidBrush(u.Accent)) g.FillEllipse(b, dr);
                else using (var pen = new Pen(Color.FromArgb(d == DateTime.Today ? 160 : 60, u.Ink), Math.Max(1f, u.F(1.2f)))) g.DrawEllipse(pen, dr.X + 0.5f, dr.Y + 0.5f, dr.Width - 1, dr.Height - 1);
            }
        }

        public override string Tip(Ui u, Rectangle r, Point p)
        {
            return string.Join("\n", Habits.Names.Select(h => h + ": " + (Habits.Done(h, DateTime.Today) ? "done today" : "not yet today") + ", streak " + Habits.Streak(h)).ToArray()) + "\nClick for all habits";
        }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            if (b == MouseButtons.Left) bar.ToggleFlyout("habits", r, () => new HabitFlyout(bar.U.Scale));
        }
    }

    class IcsWidget : Widget
    {
        public IcsWidget() : base("ics", "Calendar events", "Study", "\uE787", "Upcoming events from a calendar link (Google or Outlook \"secret address in iCal format\").") { }
        public override bool Multi { get { return true; } }
        public override bool HasLabel { get { return true; } }
        public override int Width(Ui u) { return u.FI(170); }
        public override int CompactWidth(Ui u) { return u.FI(116); }
        string Url { get { return Opt("url", ""); } }

        public override void Tick(BarForm bar)
        {
            string url = Url;
            if (url.Length > 8) PollEvery(Math.Max(5, OptInt("refresh", 15)) * 60, true, () => Ics.Fetch(url));
        }

        protected override void OnOptionsChanged() { PollSoon(); }

        List<CalEvent> Upcoming()
        {
            var list = Svc.Demo ? Ics.For("") : Ics.For(Url);
            if (list == null) return new List<CalEvent>();
            DateTime now = DateTime.Now;
            return list.Where(e => e.End > now).OrderBy(e => e.Start).ToList();
        }

        public static string When(CalEvent e)
        {
            DateTime now = DateTime.Now;
            string day = e.Start.Date == now.Date ? "Today" : e.Start.Date == now.Date.AddDays(1) ? "Tomorrow" : e.Start.ToString("ddd d");
            if (e.AllDay) return day;
            if (e.Start <= now) return "Now";
            return day + " " + e.Start.ToString(Config.Clock24 ? "HH:mm" : "h:mm tt");
        }

        public override string LineText(Ui u) { var e = Upcoming().FirstOrDefault(); return e == null ? "No events" : e.Title; }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            if (Url.Length < 8 && !Svc.Demo) { InfoTile(g, r, u, "\uE787", u.Dim, "Calendar", "Add a link", u.Bold); return; }
            CalEvent e = Upcoming().FirstOrDefault();
            if (e == null) { InfoTile(g, r, u, "\uE787", u.Dim, LastError ?? "Calendar", Ics.For(Url) == null ? "Loading…" : "Nothing soon", u.Bold); return; }
            InfoTile(g, r, u, "\uE787", u.Accent, When(e) + (e.Location.Length > 0 ? " · " + e.Location : ""), e.Title, u.Bold);
        }

        public override string Tip(Ui u, Rectangle r, Point p)
        {
            var list = Upcoming().Take(6).Select(e => When(e) + "  " + e.Title).ToArray();
            return list.Length == 0 ? "No upcoming events" : string.Join("\n", list);
        }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            if (b != MouseButtons.Left) return;
            if (Url.Length < 8 && !Svc.Demo) { bar.OpenSettings("w:" + Key); return; }
            var list = Upcoming();
            bar.ToggleFlyout("ics:" + Key, r, () => new EventsFlyout(bar.U.Scale, list));
        }

        public override void Options(IWidgetOptions o)
        {
            o.Text("Calendar link", "Google Calendar: Settings > your calendar > \"Secret address in iCal format\". Outlook: Publish a calendar > ICS link", "url", "");
            o.Number("Refresh every", null, "refresh", 5, 240, 15, " min");
        }
    }

    class WordWidget : Widget
    {
        public WordWidget() : base("word", "Word of the day", "Study", "\uE82D", "A new English word every day, with its meaning. Works offline.") { }
        public override bool HasLabel { get { return true; } }
        public override int Width(Ui u) { return u.FI(150); }
        public override string LineText(Ui u) { return Words.Today[0]; }
        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover) { InfoTile(g, r, u, "\uE82D", u.Accent, "Word of the day", Words.Today[0], null); }
        public override string Tip(Ui u, Rectangle r, Point p) { return Words.Today[0] + ": " + Words.Today[1]; }
        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b) { if (b == MouseButtons.Left) bar.ShowToast(Words.Today[0], Words.Today[1]); }
    }

    class TranslateWidget : Widget
    {
        public TranslateWidget() : base("translate", "Quick translate", "Study", "\uF2B7", "Type a word or a sentence and translate it (MyMemory, free; only when you ask).") { }
        public override int Width(Ui u) { return u.Cell; }
        public override bool Sizable { get { return false; } }
        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover) { Gfx.Glyph(g, "\uF2B7", u.GlyphBig, u.Ink, new RectangleF(r.X, r.Y - u.S(1.5f) * hover, r.Width, r.Height)); }
        public override string Tip(Ui u, Rectangle r, Point p) { return "Quick translate"; }
        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b) { if (b == MouseButtons.Left) bar.ToggleFlyout("translate", r, () => new TranslateFlyout(bar.U.Scale, this)); }

        public override void Options(IWidgetOptions o)
        {
            o.Choice("From", null, "from", TranslateFlyout.Langs, TranslateFlyout.LangNames, "en");
            o.Choice("To", null, "to", TranslateFlyout.Langs, TranslateFlyout.LangNames, "es");
        }
    }

    class ConverterWidget : Widget
    {
        public ConverterWidget() : base("convert", "Unit converter", "Study", "\uE8AB", "Length, weight, temperature, speed and currencies (rates from frankfurter.app).") { }
        public override int Width(Ui u) { return u.Cell; }
        public override bool Sizable { get { return false; } }
        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover) { Gfx.Glyph(g, "\uE8AB", u.GlyphBig, u.Ink, new RectangleF(r.X, r.Y - u.S(1.5f) * hover, r.Width, r.Height)); }
        public override string Tip(Ui u, Rectangle r, Point p) { return "Unit and currency converter"; }
        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b) { if (b == MouseButtons.Left) bar.ToggleFlyout("convert", r, () => new ConverterFlyout(bar.U.Scale)); }
    }

    class CalcWidget : Widget
    {
        public CalcWidget() : base("calc", "Calculator", "Study", "\uE8EF", "A quick calculator: type a sum, press Enter. Results stay in a short history.") { }
        public override int Width(Ui u) { return u.Cell; }
        public override bool Sizable { get { return false; } }
        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover) { Gfx.Glyph(g, "\uE8EF", u.GlyphBig, u.Ink, new RectangleF(r.X, r.Y - u.S(1.5f) * hover, r.Width, r.Height)); }
        public override string Tip(Ui u, Rectangle r, Point p) { return "Calculator"; }
        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            if (b == MouseButtons.Left) bar.ToggleFlyout("calc", r, () => new CalcFlyout(bar.U.Scale));
            else if (b == MouseButtons.Right) { ContextMenuStrip m = bar.NewMenu(false); BarForm.Item(m, "Windows Calculator", () => Win.Launch("calculator:")); bar.ShowMenu(m, p); }
        }
    }
}
