// Widgets: every tile on the bar except the app icons.
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
    // Sizes, fonts and colors for one rendering of the bar (bar, gallery preview...).
    class Ui
    {
        public float Scale = 1f, K = 1f;
        public int H;
        public bool Dock;
        public Palette P;
        public Color Accent;
        public float Radius;
        public Font Big, Sec, Title, Text, Small, Tiny, Glyph, GlyphSmall, GlyphBig, GlyphTiny, Letter, Bold;
        public Func<string, Bitmap> Icon = delegate { return null; };

        public int S(float v) { return (int)Math.Round(v * Scale); }
        public float F(float v) { return v * Scale * K; }
        public int FI(float v) { return (int)Math.Round(v * Scale * K); }

        public static Ui Make(float scale)
        {
            var u = new Ui();
            u.Build(scale);
            return u;
        }

        public void Build(float scale)
        {
            Scale = scale;
            K = (Config.IconSize + 28) / 58f;
            Dock = Config.IsDock;
            H = Config.BarHeight > 0 ? Math.Max(S(Config.IconSize + 10), S(Config.BarHeight)) : S(Config.IconSize + (Dock ? 26 : 28));
            TilePad = Config.TilePad >= 0 ? Math.Min(S(Config.TilePad), (H - S(16)) / 2f) : (Dock ? S(6) : S(5));
            P = Config.P;
            Accent = Config.Accent;
            Radius = S(Config.Radius) * Math.Min(1.15f, K);
            foreach (Font f in new[] { Big, Sec, Title, Text, Small, Tiny, Glyph, GlyphSmall, GlyphBig, GlyphTiny, Letter, Bold })
                if (f != null) f.Dispose();
            string gf = Gfx.GlyphFamily();
            Big = new Font("Segoe UI Semibold", (Config.IconSize * 0.62f + 3f) * scale, GraphicsUnit.Pixel);
            Sec = new Font("Segoe UI Semibold", (Config.IconSize * 0.4f + 1f) * scale, GraphicsUnit.Pixel);
            Title = new Font("Segoe UI Semibold", 12.5f * scale * K, GraphicsUnit.Pixel);
            Bold = new Font("Segoe UI Semibold", 11f * scale * K, GraphicsUnit.Pixel);
            Text = new Font("Segoe UI", 11.5f * scale * K, GraphicsUnit.Pixel);
            Small = new Font("Segoe UI", 10.5f * scale * K, GraphicsUnit.Pixel);
            Tiny = new Font("Segoe UI", 9.5f * scale * K, GraphicsUnit.Pixel);
            Glyph = new Font(gf, 15f * scale * K, GraphicsUnit.Pixel);
            GlyphSmall = new Font(gf, 12f * scale * K, GraphicsUnit.Pixel);
            GlyphBig = new Font(gf, 18f * scale * K, GraphicsUnit.Pixel);
            GlyphTiny = new Font(gf, 9f * scale * K, GraphicsUnit.Pixel);
            Letter = new Font("Segoe UI Semibold", Config.IconSize * 0.5f * scale, GraphicsUnit.Pixel);
        }

        public int Cell { get { return S(Config.IconSize + 14); } }
        public float TilePad;   // space above and below each tile (and around the floating dock)

        // The tile body inside a full-height slot.
        public RectangleF Tile(Rectangle r)
        {
            return new RectangleF(r.X, r.Y + TilePad, r.Width, r.Height - TilePad * 2);
        }

        public Color Ink { get { return P.Ink; } }
        public Color Dim { get { return P.InkDim; } }
        public Color Faint { get { return P.InkFaint; } }
    }

    abstract class Widget
    {
        public readonly string Id, Name, Category, IconGlyph, Desc;
        public string Key;   // the key on the bar: the id, or "id#2" for a second copy

        protected Widget(string id, string name, string category, string glyph, string desc)
        {
            Id = id; Name = name; Category = category; IconGlyph = glyph; Desc = desc; Key = id;
        }

        public virtual bool Available { get { return true; } }       // can this PC show it at all?
        public virtual bool Shown(Ui u) { return Available; }       // show it right now?
        public virtual bool Bare { get { return false; } }           // no tile behind it
        public virtual bool Live { get { return false; } }           // repaint often (timers, progress)
        public abstract int Width(Ui u);
        public virtual int CompactWidth(Ui u) { return Width(u); }   // used when the bar is crowded
        public abstract void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover);
        public virtual string Tip(Ui u, Rectangle r, Point p) { return Name; }
        public virtual void Click(BarForm bar, Rectangle r, Point p, MouseButtons b) { }
        public virtual bool Wheel(BarForm bar, Rectangle r, Point p, int delta) { return false; }
        public virtual bool SliderAt(Ui u, Rectangle r, Point p) { return false; }
        public virtual void SliderMove(Ui u, Rectangle r, Point p) { }

        // ---- per-copy options: settings.txt "w.<key>.<name>=value", cached until something changes ----
        static int optGen = 1;
        int myGen;
        Dictionary<string, string> opts;

        public static void OptionsChanged() { optGen++; }

        public static Dictionary<string, string> DemoOpts;   // --selftest: options without touching settings.txt ("key.name")

        public string Opt(string name, string def)
        {
            if (DemoOpts != null) { string dv; if (DemoOpts.TryGetValue(Key + "." + name, out dv)) return dv; }
            if (opts == null || myGen != optGen) { opts = Settings.WithPrefix("w." + Key + "."); myGen = optGen; }
            string v;
            return opts.TryGetValue(name, out v) ? v : def;
        }

        public int OptInt(string name, int def) { int v; return int.TryParse(Opt(name, ""), out v) ? v : def; }
        public bool OptBool(string name, bool def) { string v = Opt(name, def ? "true" : "false"); return v == "true" || v == "1"; }

        public void SetOpt(string name, string value)
        {
            Settings.Set("w." + Key + "." + name, value ?? "");
            OptionsChanged();
            OnOptionsChanged();
            BarForm.Poke();
        }

        protected virtual void OnOptionsChanged() { }

        public virtual bool Multi { get { return false; } }               // more than one copy can be on the bar
        public virtual bool RightClickIsAction { get { return false; } }  // right-click does something (no menu)
        public virtual bool Sizable { get { return true; } }              // offers sizes S / M / L
        public virtual string[] Styles { get { return null; } }           // style variants (the first is the default)
        public virtual bool HasLabel { get { return false; } }            // has a caption that can be turned off
        public virtual void Options(IWidgetOptions o) { }                 // its own settings, on its settings page

        public string Style { get { string[] s = Styles; return s == null ? null : Opt("style", s[0]); } }
        public bool ShowLabel { get { return OptBool("label", true); } }
        public string SizeOpt { get { return Opt("size", "m"); } }

        // One short line for stacks (two small widgets in one tile). null = can't be stacked.
        public virtual string LineText(Ui u) { return null; }
        public virtual string LineGlyph { get { return IconGlyph; } }
        public virtual Color LineColor(Ui u) { return u.Ink; }
        public bool CanStack { get { try { return LineText(StackUi) != null; } catch { return false; } } }
        static Ui stackUi;
        static Ui StackUi { get { if (stackUi == null) stackUi = Ui.Make(1f); return stackUi; } }

        // ---- living on the bar ----
        public virtual void Tick(BarForm bar) { }                          // the main bar's 500 ms tick, while on a bar
        public virtual int RepaintMs { get { return 0; } }                  // > 0: repaint this often (stopwatch, meters)
        public virtual bool DragOut(BarForm bar) { return false; }         // true: the drag was a drag-out (no reordering)
        public volatile string LastError;

        DateTime nextPoll = DateTime.MinValue;
        int polling;

        // Runs work off the UI thread every `seconds` while this widget is on a bar (network: thread pool).
        protected void PollEvery(double seconds, bool network, Action work)
        {
            if (Svc.Demo || DateTime.UtcNow < nextPoll) return;
            if (System.Threading.Interlocked.CompareExchange(ref polling, 1, 0) == 1) return;
            nextPoll = DateTime.UtcNow.AddSeconds(seconds);
            Action run = delegate
            {
                try { work(); LastError = null; }
                catch (Exception ex)
                {
                    LastError = ex is System.Net.WebException ? "Offline" : "Error";
                    nextPoll = DateTime.UtcNow.AddSeconds(Math.Min(seconds, network ? 300 : 60));
                    Bg.LogOnce("w." + Key, ex);
                }
                finally { polling = 0; BarForm.Poke(); }
            };
            if (network) Bg.Pool("w." + Key, run); else Bg.Run("w." + Key, run);
        }

        protected void PollSoon() { nextPoll = DateTime.MinValue; }

        // The common layout: a glyph, a small caption above, the value below.
        protected void InfoTile(Graphics g, Rectangle r, Ui u, string glyph, Color glyphColor, string caption, string value, Font valueFont)
        {
            RectangleF t = u.Tile(r);
            float gs = u.F(26);
            var gr = new RectangleF(t.X + u.F(8), t.Y + (t.Height - gs) / 2f, gs, gs);
            Gfx.Glyph(g, glyph, u.Glyph, glyphColor, gr);
            float x = gr.Right + u.F(6), w = t.Right - x - u.F(8);
            bool cap = ShowLabel && !string.IsNullOrEmpty(caption);
            Font vf = valueFont ?? u.Title;
            float th = cap ? u.Tiny.GetHeight() : 0, nh = vf.GetHeight();
            float y = t.Y + (t.Height - th - nh) / 2f;
            if (cap) Gfx.TextIn(g, caption, u.Tiny, u.Dim, new RectangleF(x, y, w, th), Gfx.Left);
            Gfx.TextIn(g, value, vf, u.Ink, new RectangleF(x, y + th * 0.95f, w, nh), Gfx.Left);
        }

        // A one-icon toggle tile; "on" uses the same accent look as the Wi-Fi / Bluetooth pills.
        protected static void ToggleTile(Graphics g, Rectangle r, Ui u, string glyph, bool on, float hover)
        {
            RectangleF t = u.Tile(r);
            if (on) Theme.FillRound(g, RectangleF.Inflate(t, -u.F(3), -u.F(3)), Math.Max(2f, u.Radius - u.F(3)), Color.FromArgb(u.P.Light ? 40 : 46, u.Accent));
            Color ink = on ? (u.P.Light ? Theme.Darken(u.Accent, 0.35f) : Theme.Lighten(u.Accent, 0.35f)) : u.Ink;
            Gfx.Glyph(g, glyph, u.GlyphBig, ink, new RectangleF(r.X, r.Y - u.S(1.5f) * hover, r.Width, r.Height));
        }

        protected static bool In(RectangleF r, Point p) { return r.Contains(p.X, p.Y); }

        protected static void HoverDot(Graphics g, RectangleF r, Point mouse, Ui u)
        {
            if (In(r, mouse)) Theme.FillRound(g, r, Math.Min(r.Width, r.Height) / 2, u.P.Light ? Color.FromArgb(22, 0, 0, 0) : Color.FromArgb(30, 255, 255, 255));
        }
    }

    static class Widgets
    {
        public static readonly string[] Categories = { "Essentials", "Media", "System", "Network", "Time & weather", "Productivity", "Study", "Toggles", "Info", "Dev", "Custom" };

        public static readonly List<Widget> All = new List<Widget>
        {
            new StartWidget(), new SearchWidget(), new ClockWidget(), new QuickWidget(), new KeyboardWidget(),
            new BellWidget(), new DesktopWidget(),
            new MediaWidget(), new VolumeWidget(), new BrightnessWidget(),
            new CpuWidget(), new RamWidget(), new GpuWidget(), new StatsWidget(), new DiskWidget(), new BatteryWidget(),
            new ConnectWidget(), new NetWidget(),
            new WeatherWidget(), new CalendarWidget(), new WorldClockWidget(), new ScreenTimeWidget(),
            new FocusWidget(), new NoteWidget(), new ClipboardWidget(), new TrashWidget(),
            // phase 3
            new FlipClockWidget(), new DigitalClockWidget(), new AnalogClockWidget(), new ProgressWidget(), new CountdownWidget(),
            new StopwatchWidget(), new TimerWidget(), new AlarmWidget(), new SunWidget(), new MoonWidget(),
            new TodoWidget(), new NextClassWidget(), new HabitWidget(), new IcsWidget(), new WordWidget(),
            new TranslateWidget(), new ConverterWidget(), new CalcWidget(),
            new TopProcessWidget(), new UptimeWidget(), new DiskIoWidget(), new PingWidget(), new WifiWidget(),
            new BatteryHealthWidget(), new PowerPlanWidget(), new PeakMeterWidget(), new MixerWidget(), new MicWidget(), new UsbWidget(),
            new DarkModeWidget(), new NightLightWidget(), new DndWidget(), new GameModeWidget(), new DesktopIconsWidget(),
            new ScreenshotWidget(), new ColorPickerWidget(), new KeepAwakeWidget(), new EyeBreakWidget(), new PowerMenuWidget(), new AudioOutWidget(),
            new CryptoWidget(), new CurrencyWidget(), new StocksWidget(), new AirQualityWidget(), new RssWidget(),
            new ClaudeWidget(), new GitHubWidget(), new DownloadWidget(),
            new CustomWidget(), new LaunchTileWidget(),
        };

        static readonly Dictionary<string, Widget> copies = new Dictionary<string, Widget>();
        static Dictionary<string, Widget> byId;

        // The widget for a key on the bar: "cpu", a copy ("worldclock#2"), or a stack ("stack:cpu+ram").
        public static Widget Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (byId == null) { var d = new Dictionary<string, Widget>(); foreach (Widget w in All) d[w.Id] = w; byId = d; }
            Widget found;
            if (byId.TryGetValue(key, out found)) return found;
            Widget c;
            lock (copies) if (copies.TryGetValue(key, out c)) return c;
            if (key.StartsWith("stack:"))
            {
                string[] parts = key.Substring(6).Split('+');
                if (parts.Length != 2) return null;
                Widget a = Get(parts[0]), b = Get(parts[1]);
                if (a == null || b == null) return null;
                c = new StackWidget(key, a, b);
            }
            else
            {
                int hash = key.IndexOf('#');
                if (hash <= 0) return null;
                Widget proto = null;
                string id = key.Substring(0, hash);
                foreach (Widget w in All) if (w.Id == id) proto = w;
                if (proto == null) return null;
                c = (Widget)Activator.CreateInstance(proto.GetType());
                c.Key = key;
            }
            lock (copies) copies[key] = c;
            return c;
        }

        // The first free key for another copy: "note#2", "note#3"...
        public static string NextKey(string id)
        {
            for (int n = 2; ; n++)
            {
                string k = id + "#" + n;
                if (!Config.HasItem(k) && Settings.WithPrefix("w." + k + ".").Count == 0) return k;
            }
        }
    }

    // What a widget's settings page asks for (the Settings window implements it with its usual rows).
    interface IWidgetOptions
    {
        Widget Target { get; }
        void Section(string title);
        void Note(string text);
        void Text(string title, string desc, string opt, string def);
        void Number(string title, string desc, string opt, int min, int max, int def, string unit);
        void Choice(string title, string desc, string opt, string[] values, string[] labels, string def);
        void Check(string title, string desc, string opt, bool def);
        void Button(string title, string desc, string text, Action click);
    }

    // Two small widgets in one tile, one per row.
    class StackWidget : Widget
    {
        public readonly Widget A, B;

        public StackWidget(string key, Widget a, Widget b) : base("stack", a.Name + " + " + b.Name, "Essentials", "\uE8A9", "Two widgets in one tile.")
        {
            Key = key; A = a; B = b;
        }

        public override bool Shown(Ui u) { return A.Shown(u) || B.Shown(u); }
        public override bool Live { get { return A.Live || B.Live; } }
        public override int RepaintMs { get { int a = A.RepaintMs, b = B.RepaintMs; return a == 0 ? b : b == 0 ? a : Math.Min(a, b); } }
        public override bool Sizable { get { return false; } }

        float RowWidth(Widget w, Ui u)
        {
            string t = w.LineText(u) ?? "";
            return u.F(18) + Gfx.TW(t, u.Bold);
        }

        public override int Width(Ui u) { return (int)Math.Max(u.F(70), Math.Max(RowWidth(A, u), RowWidth(B, u)) + u.F(34)); }

        RectangleF Row(Rectangle r, Ui u, int i)
        {
            RectangleF t = u.Tile(r);
            float h = (t.Height - u.F(4)) / 2f;
            return new RectangleF(t.X, t.Y + u.F(2) + i * h, t.Width, h);
        }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            for (int i = 0; i < 2; i++)
            {
                Widget w = i == 0 ? A : B;
                RectangleF row = Row(r, u, i);
                if (!w.Shown(u)) continue;
                var gr = new RectangleF(row.X + u.F(9), row.Y, u.F(16), row.Height);
                Gfx.Glyph(g, w.LineGlyph, u.GlyphSmall, u.Dim, gr);
                Gfx.TextIn(g, w.LineText(u), u.Bold, w.LineColor(u), new RectangleF(gr.Right + u.F(3), row.Y, row.Right - gr.Right - u.F(10), row.Height), Gfx.Left);
            }
            RectangleF t = u.Tile(r);
            using (var pen = new Pen(Color.FromArgb(26, u.Ink)))
                g.DrawLine(pen, t.X + u.F(10), t.Y + t.Height / 2f, t.Right - u.F(10), t.Y + t.Height / 2f);
        }

        Widget At(Rectangle r, Point p, Ui u) { return p.Y < u.Tile(r).Y + u.Tile(r).Height / 2f ? A : B; }

        public override string Tip(Ui u, Rectangle r, Point p) { return At(r, p, u).Tip(u, r, p); }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            Widget w = At(r, p, bar.U);
            if (b == MouseButtons.Left)
            {
                string act = w.Opt("click", "");
                if (act.Length > 0) { Actions.Run(act); return; }
            }
            w.Click(bar, r, p, b);
        }

        public override bool Wheel(BarForm bar, Rectangle r, Point p, int delta) { return At(r, p, bar.U).Wheel(bar, r, p, delta); }

        public override void Options(IWidgetOptions o)
        {
            o.Note("This tile shows " + A.Name + " on top and " + B.Name + " below.");
            o.Button("Split the stack", "Put them back as two separate tiles", "Split", () => BarForm.Instance.Unstack(Key));
        }
    }

    // ==================================================================
    // Essentials
    // ==================================================================
    class StartWidget : Widget
    {
        public StartWidget() : base("start", "Start", "Essentials", "\uE782", "Opens the Start menu. Right-click for the power user menu.") { }
        public override int Width(Ui u) { return u.Cell; }
        public override bool RightClickIsAction { get { return true; } }
        public override bool Sizable { get { return false; } }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            float side = u.S(Config.IconSize) * 0.62f, gap = Math.Max(1.5f, side * 0.09f), sq = (side - gap) / 2f;
            float x0 = r.X + (r.Width - side) / 2f, y0 = r.Y + (r.Height - side) / 2f - u.S(1.5f) * hover;
            Color a = u.Accent;
            using (var b = new LinearGradientBrush(new RectangleF(x0, y0, side, side), Theme.Lighten(a, 0.5f), a, 45f))
                for (int i = 0; i < 4; i++)
                {
                    var q = new RectangleF(x0 + (i % 2) * (sq + gap), y0 + (i / 2) * (sq + gap), sq, sq);
                    using (GraphicsPath p = Theme.Round(q, sq * 0.22f)) g.FillPath(b, p);
                }
        }

        public override string Tip(Ui u, Rectangle r, Point p) { return "Start\nRight-click: power user menu"; }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            if (b == MouseButtons.Left) Win.Press(Native.VK_LWIN);
            else if (b == MouseButtons.Right) Win.Press(Native.VK_LWIN, 0x58);   // Win+X
        }
    }

    class SearchWidget : Widget
    {
        public SearchWidget() : base("search", "Search", "Essentials", "\uE721", "StayBar's own fast app launcher (Alt+Space). Right-click for Windows Search.") { }
        public override int Width(Ui u) { return u.Cell; }
        public override bool RightClickIsAction { get { return true; } }
        public override bool Sizable { get { return false; } }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            var gr = new RectangleF(r.X, r.Y - u.S(1.5f) * hover, r.Width, r.Height);
            Gfx.Glyph(g, "\uE721", u.GlyphBig, u.Ink, gr);
        }

        public override string Tip(Ui u, Rectangle r, Point p) { return "Search apps  (Alt+Space)\nRight-click: Windows Search"; }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            if (b == MouseButtons.Left) bar.OpenLauncher();
            else if (b == MouseButtons.Right) Win.Press(Native.VK_LWIN, 0x53);   // Win+S
        }
    }

    class ClockWidget : Widget
    {
        public ClockWidget() : base("clock", "Clock", "Essentials", "\uE823", "A big clock with seconds and the date. Click for notifications and calendar.") { }

        static string MainTime() { return Config.Clock24 ? DateTime.Now.ToString("HH:mm") : DateTime.Now.ToString("h:mm"); }
        public override string LineText(Ui u) { return MainTime() + (Config.Clock24 ? "" : " " + DateTime.Now.ToString("tt", CultureInfo.InvariantCulture)); }
        public override bool Live { get { return Config.Seconds; } }
        static string Secs() { return ":" + DateTime.Now.ToString("ss"); }
        static string Date() { return DateTime.Now.ToString("ddd d MMM", CultureInfo.CurrentCulture); }

        public override int Width(Ui u)
        {
            float t = Gfx.TW(Config.Clock24 ? "00:00" : "00:00", u.Big) + (Config.Seconds ? Gfx.TW(":00", u.Sec) + u.S(3) : 0);
            if (!Config.Clock24) t += Gfx.TW(" PM", u.Tiny);
            return (int)Math.Max(t, Config.ShowDate ? Gfx.TW("Wed 30 Sep", u.Text) : 0) + u.S(30);
        }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            string main = MainTime(), sec = Config.Seconds ? Secs() : "", ampm = Config.Clock24 ? "" : " " + DateTime.Now.ToString("tt", CultureInfo.InvariantCulture);
            float mw = Gfx.TW(main, u.Big), sw = sec.Length > 0 ? Gfx.TW(sec, u.Sec) + u.S(2) : 0, aw = ampm.Length > 0 ? Gfx.TW(ampm, u.Tiny) : 0;
            float bh = u.Big.GetHeight(), dh = Config.ShowDate ? u.Text.GetHeight() * 0.95f : 0;
            float x = r.X + (r.Width - mw - sw - aw) / 2f;
            float y = r.Y + (r.Height - bh * 0.82f - dh) / 2f - bh * 0.1f;
            if (!Config.ShowDate) y = r.Y + (r.Height - bh) / 2f;
            Gfx.Text(g, main, u.Big, u.Ink, x, y);
            if (sec.Length > 0) Gfx.Text(g, sec, u.Sec, u.Dim, x + mw + u.S(2), y + bh - u.Sec.GetHeight() - bh * 0.12f);
            if (aw > 0) Gfx.Text(g, ampm, u.Tiny, u.Dim, x + mw + sw, y + bh - u.Tiny.GetHeight() - bh * 0.14f);
            if (Config.ShowDate)
            {
                string d = Date();
                Gfx.Text(g, d, u.Text, u.Dim, r.X + (r.Width - Gfx.TW(d, u.Text)) / 2f, y + bh * 0.86f);
            }
        }

        public override string Tip(Ui u, Rectangle r, Point p) { return DateTime.Now.ToString("D") + "\nClick: notifications and calendar"; }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            if (b == MouseButtons.Left) Win.Press(Native.VK_LWIN, 0x4E);   // Win+N
            else if (b == MouseButtons.Right)
            {
                ContextMenuStrip m = bar.NewMenu(true);
                BarForm.Item(m, "Show seconds", () => { Config.Seconds = !Config.Seconds; bar.ApplyConfig(); }).Checked = Config.Seconds;
                BarForm.Item(m, "Show date", () => { Config.ShowDate = !Config.ShowDate; bar.ApplyConfig(); }).Checked = Config.ShowDate;
                BarForm.Item(m, "24-hour clock", () => { Config.Clock24 = !Config.Clock24; bar.ApplyConfig(); }).Checked = Config.Clock24;
                m.Items.Add(new ToolStripSeparator());
                BarForm.Item(m, "Date and time settings", () => Win.Launch("ms-settings:dateandtime"));
                bar.ShowMenu(m, p);
            }
        }
    }

    class QuickWidget : Widget
    {
        public QuickWidget() : base("quick", "Quick settings", "Essentials", "\uE9E9", "Wi-Fi, sound and battery icons that open Windows quick settings.") { }
        public override bool Sizable { get { return false; } }

        static List<string> Glyphs()
        {
            var list = new List<string>();
            list.Add(Radios.Network == "Ethernet" ? "\uE839" : "\uE701");
            list.Add(Audio.Glyph(Audio.Volume, Audio.Muted));
            PowerStatus ps = BatteryWidget.Battery();
            if (ps != null)
            {
                int level = Math.Max(0, Math.Min(10, (int)Math.Round(ps.BatteryLifePercent * 10)));
                bool charging = ps.PowerLineStatus == PowerLineStatus.Online;
                char c = charging ? (level >= 9 ? '\uE83E' : (char)(0xE85A + level)) : (level >= 10 ? '\uE83F' : (char)(0xE850 + level));
                list.Add(c.ToString());
            }
            return list;
        }

        public override int Width(Ui u) { return Glyphs().Count * u.FI(28) + u.S(16); }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            List<string> glyphs = Glyphs();
            int gw = u.FI(28);
            float gx = r.X + (r.Width - glyphs.Count * gw) / 2f;
            for (int i = 0; i < glyphs.Count; i++)
                Gfx.Glyph(g, glyphs[i], u.Glyph, u.Ink, new RectangleF(gx + i * gw, r.Y, gw, r.Height));
        }

        public override string Tip(Ui u, Rectangle r, Point p)
        {
            string s = "Quick settings  (Win+A)\n" + (Radios.Network.Length > 0 ? Radios.Network : "Network") + ", volume " + (int)Audio.Volume + "%";
            PowerStatus ps = BatteryWidget.Battery();
            if (ps != null) s += ", battery " + (int)Math.Round(ps.BatteryLifePercent * 100) + "%";
            return s;
        }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            if (b == MouseButtons.Left) Win.Press(Native.VK_LWIN, 0x41);   // Win+A
        }
    }

    class KeyboardWidget : Widget
    {
        public KeyboardWidget() : base("keyboard", "Keyboard language", "Essentials", "\uE765", "Shows the input language (ENG, UKR, ESP...). Click to switch.") { }
        public override int Width(Ui u) { return Math.Max(u.FI(50), (int)Gfx.TW(Keyboard.Code, u.Bold) + u.S(22)); }
        public override bool RightClickIsAction { get { return true; } }
        public override bool Sizable { get { return false; } }
        public override string LineText(Ui u) { return Keyboard.Code + (Keyboard.Region.Length > 0 ? " " + Keyboard.Region : ""); }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            float th = u.Bold.GetHeight(), sh = u.Tiny.GetHeight();
            float y = r.Y + (r.Height - th - sh * 0.9f) / 2f;
            Gfx.TextIn(g, Keyboard.Code, u.Bold, u.Ink, new RectangleF(r.X, y, r.Width, th), Gfx.Mid);
            Gfx.TextIn(g, Keyboard.Region, u.Tiny, u.Dim, new RectangleF(r.X, y + th * 0.92f, r.Width, sh), Gfx.Mid);
        }

        public override string Tip(Ui u, Rectangle r, Point p) { return "Keyboard: " + Keyboard.Code + (Keyboard.Region.Length > 0 ? " (" + Keyboard.Region + ")" : "") + "\nClick or Win+Space to switch"; }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            if (b == MouseButtons.Left) Win.Press(Native.VK_LWIN, 0x20);   // Win+Space
            else if (b == MouseButtons.Right) Win.Launch("ms-settings:keyboard");
        }
    }

    class BellWidget : Widget
    {
        public BellWidget() : base("bell", "Notifications", "Essentials", "\uEA8F", "Opens the notification center.") { }
        public override int Width(Ui u) { return u.Cell; }
        public override bool RightClickIsAction { get { return true; } }
        public override bool Sizable { get { return false; } }
        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            Gfx.Glyph(g, "\uEA8F", u.GlyphBig, u.Ink, new RectangleF(r.X, r.Y - u.S(1.5f) * hover, r.Width, r.Height));
        }
        public override string Tip(Ui u, Rectangle r, Point p) { return "Notifications  (Win+N)"; }
        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            if (b == MouseButtons.Left) Win.Press(Native.VK_LWIN, 0x4E);
            else if (b == MouseButtons.Right) Win.Launch("ms-settings:notifications");
        }
    }

    class DesktopWidget : Widget
    {
        public DesktopWidget() : base("desktop", "Show desktop", "Essentials", "\uE7F4", "A thin strip at the very edge. Click to show the desktop.") { }
        public override bool Bare { get { return true; } }
        public override bool Shown(Ui u) { return !Config.IsDock; }   // the dock ends macOS style (Win+D or the hot corner still work)
        public override bool Sizable { get { return false; } }
        public override int Width(Ui u) { return u.S(10); }
        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            float cx = r.X + r.Width / 2f;
            using (var pen = new Pen(Color.FromArgb((int)(40 + 120 * hover), u.Ink), Math.Max(1f, u.S(1))))
                g.DrawLine(pen, cx, r.Y + r.Height * 0.3f, cx, r.Y + r.Height * 0.7f);
        }
        public override string Tip(Ui u, Rectangle r, Point p) { return "Show desktop  (Win+D)"; }
        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            if (b == MouseButtons.Left) Win.Press(Native.VK_LWIN, 0x44);
        }
    }

    // ==================================================================
    // Media
    // ==================================================================
    class MediaWidget : Widget
    {
        public MediaWidget() : base("media", "Now playing", "Media", "\uE8D6", "Song, artist, album art and play/pause/next for Spotify, browsers and any player. Hides when nothing plays.") { }
        public override bool Shown(Ui u) { return Media.Now != null; }
        public override bool Live { get { MediaInfo m = Media.Now; return m != null && m.Playing; } }
        public override int Width(Ui u) { return u.FI(236); }
        public override int CompactWidth(Ui u) { return u.FI(142); }

        RectangleF[] Buttons(Rectangle r, Ui u)
        {
            RectangleF t = u.Tile(r);
            float bw = u.F(27), cy = t.Y + t.Height / 2f;
            float x0 = t.Right - u.F(7) - bw * 3;
            var res = new RectangleF[3];
            for (int i = 0; i < 3; i++) res[i] = new RectangleF(x0 + i * bw, cy - bw / 2f, bw, bw);
            return res;
        }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            MediaInfo m = Media.Now;
            if (m == null) return;
            RectangleF t = u.Tile(r);
            if (m.ArtColor != Color.Empty)
            {
                using (GraphicsPath tp = Theme.Round(t, u.Radius))
                using (var lb = new LinearGradientBrush(t, Color.FromArgb(u.P.Light ? 60 : 85, m.ArtColor), Color.FromArgb(0, m.ArtColor), 0f))
                    g.FillPath(lb, tp);
            }
            float side = t.Height - u.F(14);
            var art = new RectangleF(t.X + u.F(7), t.Y + (t.Height - side) / 2f, side, side);
            float ar = Math.Max(u.S(4), u.Radius * 0.6f);
            if (m.Art != null)
            {
                using (GraphicsPath ap = Theme.Round(art, ar))
                {
                    Region old = g.Clip;
                    g.SetClip(ap, CombineMode.Intersect);
                    g.DrawImage(m.Art, art);
                    g.Clip = old;
                }
            }
            else
            {
                Theme.FillRound(g, art, ar, Color.FromArgb(70, u.Accent));
                Gfx.Glyph(g, "\uE8D6", u.Glyph, u.Ink, art);
            }

            RectangleF[] btn = Buttons(r, u);
            float tx = art.Right + u.F(9), tw = btn[0].X - tx - u.F(3);
            float th = u.Bold.GetHeight(), ah = u.Small.GetHeight();
            float ty = t.Y + (t.Height - th - ah - u.F(6)) / 2f;
            bool text = tw > u.F(28);   // compact tile: just art + buttons
            if (text) Gfx.TextIn(g, m.Title.Length > 0 ? m.Title : m.AppName, u.Bold, u.Ink, new RectangleF(tx, ty, tw, th), Gfx.Left);
            if (text) Gfx.TextIn(g, m.Artist.Length > 0 ? m.Artist : m.AppName, u.Small, u.Dim, new RectangleF(tx, ty + th, tw, ah), Gfx.Left);
            if (text && m.DurSec > 1)
            {
                float pw = tw - u.F(4), ph = Math.Max(2f, u.F(2.5f));
                var pr = new RectangleF(tx, ty + th + ah + u.F(3), pw, ph);
                Gfx.Meter(g, pr, (float)(100 * m.Position / m.DurSec), u.P.Light ? Color.FromArgb(200, u.Ink) : Color.FromArgb(220, 255, 255, 255), u.P.Track);
            }

            for (int i = 0; i < 3; i++)
            {
                RectangleF b = btn[i];
                if (i == 1)
                {
                    float d = b.Width * 0.92f;
                    var c = new RectangleF(b.X + (b.Width - d) / 2, b.Y + (b.Height - d) / 2, d, d);
                    Color fill = In(b, mouse) ? (u.P.Light ? Theme.Lighten(u.Ink, 0.2f) : Color.White) : (u.P.Light ? u.Ink : Color.FromArgb(236, 255, 255, 255));
                    using (var br = new SolidBrush(fill)) g.FillEllipse(br, c);
                    Gfx.MediaIcon(g, c, m.Playing ? 2 : 1, u.P.Light ? Color.White : Color.FromArgb(16, 16, 20));
                }
                else
                {
                    HoverDot(g, RectangleF.Inflate(b, -u.F(2), -u.F(2)), mouse, u);
                    bool en = i == 0 ? m.CanPrev : m.CanNext;
                    Gfx.MediaIcon(g, b, i == 0 ? 0 : 3, en ? u.Ink : u.Faint);
                }
            }
        }

        public override string Tip(Ui u, Rectangle r, Point p)
        {
            MediaInfo m = Media.Now;
            if (m == null) return "Nothing playing";
            RectangleF[] btn = Buttons(r, u);
            if (In(btn[0], p)) return "Previous";
            if (In(btn[1], p)) return m.Playing ? "Pause" : "Play";
            if (In(btn[2], p)) return "Next";
            string s = m.Title;
            if (m.Artist.Length > 0) s += "\n" + m.Artist;
            if (m.Album.Length > 0) s += "  ·  " + m.Album;
            return s + "\n" + m.AppName + "  (click to open)";
        }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            MediaInfo m = Media.Now;
            if (m == null || b != MouseButtons.Left) return;
            RectangleF[] btn = Buttons(r, bar.U);
            for (int i = 0; i < 3; i++) if (In(btn[i], p)) { Media.Command(i); bar.Invalidate(); return; }
            bar.ActivateByAppId(m.AppId);
        }
    }

    // Shared look for the volume and brightness tiles: icon, name, %, fat slider.
    abstract class SliderWidget : Widget
    {
        protected SliderWidget(string id, string name, string glyph, string desc) : base(id, name, "Media", glyph, desc) { }
        protected abstract float Value { get; }
        protected abstract void SetValue(float v);
        protected abstract string Label { get; }
        protected abstract string GlyphNow { get; }
        protected virtual bool Dimmed { get { return false; } }

        public override int Width(Ui u) { return u.FI(170); }
        public override int CompactWidth(Ui u) { return u.FI(118); }
        public override string LineText(Ui u) { return Value < 0 ? "…" : (int)Math.Round(Value) + "%"; }
        public override string LineGlyph { get { return GlyphNow; } }

        RectangleF Track(Rectangle r, Ui u)
        {
            RectangleF t = u.Tile(r);
            float pad = u.F(11), h = Math.Max(u.F(9), 5f);
            return new RectangleF(t.X + pad, t.Bottom - u.F(10) - h, t.Width - pad * 2, h);
        }

        protected RectangleF GlyphBox(Rectangle r, Ui u)
        {
            RectangleF t = u.Tile(r);
            float s = u.F(22);
            return new RectangleF(t.X + u.F(6), t.Y + u.F(4), s, s);
        }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            RectangleF t = u.Tile(r), gb = GlyphBox(r, u), tr = Track(r, u);
            HoverDot(g, gb, mouse, u);
            Gfx.Glyph(g, GlyphNow, u.GlyphSmall, Dimmed ? u.Dim : u.Ink, gb);
            float ly = gb.Y + (gb.Height - u.Bold.GetHeight()) / 2f;
            string pct = Value < 0 ? "…" : (int)Math.Round(Value) + "%";
            float pw = Gfx.TW(pct, u.Small);
            Gfx.TextIn(g, Label, u.Bold, u.Ink, new RectangleF(gb.Right + u.F(3), ly, tr.Right - gb.Right - pw - u.F(8), u.Bold.GetHeight()), Gfx.Left);
            Gfx.Text(g, pct, u.Small, u.Dim, tr.Right - pw, gb.Y + (gb.Height - u.Small.GetHeight()) / 2f + u.F(1));
            Color fill = Dimmed ? u.Faint : u.Accent;
            Gfx.Meter(g, tr, Math.Max(0, Value), fill, u.P.Track);
            if (In(RectangleF.Inflate(tr, 0, u.F(6)), mouse) && Value >= 0)
            {
                float kx = tr.X + tr.Width * Math.Max(0, Math.Min(100, Value)) / 100f, kd = tr.Height + u.F(5);
                using (var b = new SolidBrush(u.P.Light ? Color.White : Color.FromArgb(250, 255, 255, 255)))
                    g.FillEllipse(b, Math.Max(tr.X, Math.Min(tr.Right - kd, kx - kd / 2)), tr.Y + tr.Height / 2 - kd / 2, kd, kd);
            }
        }

        public override bool SliderAt(Ui u, Rectangle r, Point p) { return In(RectangleF.Inflate(Track(r, u), u.F(4), u.F(7)), p); }

        public override void SliderMove(Ui u, Rectangle r, Point p)
        {
            RectangleF tr = Track(r, u);
            SetValue(Math.Max(0, Math.Min(100, (p.X - tr.X) * 100f / Math.Max(1f, tr.Width))));
        }

        public override bool Wheel(BarForm bar, Rectangle r, Point p, int delta)
        {
            float v = Math.Max(0, Value);
            SetValue(Math.Max(0, Math.Min(100, (float)Math.Round(v / 2) * 2 + (delta > 0 ? 2 : -2))));
            return true;
        }
    }

    class VolumeWidget : SliderWidget
    {
        public VolumeWidget() : base("volume", "Volume", "\uE767", "Volume slider. Drag it, or scroll over it. Click the speaker to mute.") { }
        protected override float Value { get { return Audio.Volume; } }
        protected override void SetValue(float v) { Audio.Volume = v; BarForm.Poke(); }
        protected override string Label { get { return Audio.DeviceName; } }
        protected override string GlyphNow { get { return Audio.Glyph(Audio.Volume, Audio.Muted); } }
        protected override bool Dimmed { get { return Audio.Muted; } }

        public override string Tip(Ui u, Rectangle r, Point p)
        {
            if (In(GlyphBox(r, u), p)) return Audio.Muted ? "Unmute" : "Mute";
            return "Volume " + (int)Math.Round(Audio.Volume) + "%" + (Audio.Muted ? " (muted)" : "") + "\nScroll or drag to change";
        }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            if (b == MouseButtons.Left && In(RectangleF.Inflate(GlyphBox(r, bar.U), 4, 4), p)) { Audio.Muted = !Audio.Muted; bar.Invalidate(); }
            else if (b == MouseButtons.Right)
            {
                ContextMenuStrip m = bar.NewMenu(true);
                BarForm.Item(m, "Mute", () => { Audio.Muted = !Audio.Muted; bar.Invalidate(); }).Checked = Audio.Muted;
                m.Items.Add(new ToolStripSeparator());
                BarForm.Item(m, "Volume mixer", () => Win.Launch("ms-settings:apps-volume"));
                BarForm.Item(m, "Sound settings", () => Win.Launch("ms-settings:sound"));
                bar.ShowMenu(m, p);
            }
        }
    }

    class BrightnessWidget : SliderWidget
    {
        public BrightnessWidget() : base("brightness", "Brightness", "\uE706", "Screen brightness for laptops and monitors that support it. Scroll or drag.") { }
        public override bool RightClickIsAction { get { return true; } }
        public override bool Available { get { return Svc.Demo || Brightness.Supported != 0; } }
        public override bool Shown(Ui u) { return Svc.Demo || Brightness.Supported == 1; }
        protected override float Value { get { return Svc.Demo && Brightness.Level < 0 ? 80 : Brightness.Level; } }
        protected override void SetValue(float v) { Brightness.Set((int)Math.Round(v)); BarForm.Poke(); }
        protected override string Label { get { return "Brightness"; } }
        protected override string GlyphNow { get { return "\uE706"; } }
        public override string Tip(Ui u, Rectangle r, Point p) { return "Brightness " + Math.Max(0, Brightness.Level) + "%\nScroll or drag to change"; }
        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            if (b == MouseButtons.Right) Win.Launch("ms-settings:display");
        }
    }

    // ==================================================================
    // System
    // ==================================================================
    // A ring gauge, a title, a big number and a sparkline.
    abstract class RingWidget : Widget
    {
        protected RingWidget(string id, string name, string cat, string glyph, string desc) : base(id, name, cat, glyph, desc) { }
        protected abstract float Pct { get; }
        protected abstract string Center { get; }
        protected abstract string Caption { get; }
        protected abstract string Big { get; }
        protected virtual History Hist { get { return null; } }
        protected virtual Color RingColor(Ui u) { return Gfx.Load(Pct, u.Accent); }

        public override int Width(Ui u) { return u.FI(124); }
        public override string[] Styles { get { return Hist == null ? null : new[] { "Ring and graph", "Ring only" }; } }
        public override bool HasLabel { get { return true; } }
        public override string LineText(Ui u) { return Big; }
        public override Color LineColor(Ui u) { return Pct > 65 ? RingColor(u) : u.Ink; }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            RectangleF t = u.Tile(r);
            float d = t.Height - u.F(14), rw = Math.Max(2.5f, u.F(3.6f));
            var c = new PointF(t.X + u.F(8) + d / 2, t.Y + t.Height / 2);
            Gfx.Ring(g, c, d / 2 - rw / 2, rw, Pct, RingColor(u), u.P.Track);
            string center = Center;
            Font cf = center.Length <= 3 ? u.Bold : u.Tiny;
            Gfx.TextIn(g, center, cf, u.Ink, new RectangleF(c.X - d / 2, c.Y - cf.GetHeight() / 2, d, cf.GetHeight()), Gfx.Mid);
            float x = c.X + d / 2 + u.F(8), w = t.Right - x - u.F(9);
            float capH = u.Tiny.GetHeight(), bigH = u.Title.GetHeight();
            History h = Style == "Ring only" ? null : Hist;
            bool label = ShowLabel;
            if (!label) capH = 0;
            if (h != null)
            {
                // number + caption on one line, a generous graph underneath
                float y = t.Y + u.F(5);
                Gfx.TextIn(g, Big, u.Title, u.Ink, new RectangleF(x, y, w, bigH), Gfx.Left);
                // the caption shares the line with the number; leave it out when they'd overlap
                if (label && Gfx.TW(Big, u.Title) + Gfx.TW(Caption, u.Tiny) + u.F(6) <= w)
                    Gfx.TextIn(g, Caption, u.Tiny, u.Dim, new RectangleF(x, y + (bigH - u.Tiny.GetHeight()) / 2 + u.F(1), w, u.Tiny.GetHeight()), Gfx.Right);
                float sy = y + bigH + u.F(1);
                var sr = new RectangleF(x, sy, w, Math.Max(4, t.Bottom - u.F(7) - sy));
                Gfx.Spark(g, sr, h.Data, h.Count, 100, RingColor(u), true);
            }
            else
            {
                float y = t.Y + (t.Height - capH - bigH) / 2f;
                if (label) Gfx.TextIn(g, Caption, u.Tiny, u.Dim, new RectangleF(x, y, w, capH), Gfx.Left);
                Gfx.TextIn(g, Big, u.Title, u.Ink, new RectangleF(x, y + capH * 0.95f, w, bigH), Gfx.Left);
            }
        }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            if (b == MouseButtons.Left) Win.Launch("taskmgr.exe");
        }
    }

    class CpuWidget : RingWidget
    {
        public CpuWidget() : base("cpu", "CPU", "System", "\uE950", "CPU load with a live graph, and the temperature when Windows reports one.") { }
        protected override float Pct { get { return Sys.Cpu; } }
        protected override string Center { get { return Sys.TempC > 0 ? ((int)Math.Round(Sys.TempC)) + "°" : "CPU"; } }
        protected override string Caption { get { return Sys.TempC > 0 ? "CPU" : "Processor"; } }
        protected override string Big { get { return (int)Math.Round(Sys.Cpu) + "%"; } }
        protected override History Hist { get { return Sys.CpuH; } }
        public override string Tip(Ui u, Rectangle r, Point p)
        {
            return "CPU " + (int)Sys.Cpu + "%" + (Sys.TempC > 0 ? "  ·  " + (int)Math.Round(Sys.TempC) + " °C" : "") +
                   "\nRAM " + (int)Sys.Ram + "%  (" + Sys.RamUsedGb.ToString("0.0") + " of " + Sys.RamTotalGb.ToString("0") + " GB)\nClick: Task Manager";
        }
    }

    class RamWidget : RingWidget
    {
        public RamWidget() : base("ram", "Memory", "System", "\uE964", "How much RAM is in use, with a graph.") { }
        protected override float Pct { get { return Sys.Ram; } }
        protected override string Center { get { return "RAM"; } }
        protected override string Caption { get { return "Memory"; } }
        protected override string Big { get { return Sys.RamUsedGb.ToString("0.0") + " GB"; } }
        protected override History Hist { get { return Sys.RamH; } }
        public override string Tip(Ui u, Rectangle r, Point p) { return "RAM " + (int)Sys.Ram + "%  (" + Sys.RamUsedGb.ToString("0.0") + " of " + Sys.RamTotalGb.ToString("0") + " GB)\nClick: Task Manager"; }
    }

    class GpuWidget : RingWidget
    {
        public GpuWidget() : base("gpu", "GPU", "System", "\uE7F4", "Graphics card load (the same number Task Manager shows).") { }
        protected override float Pct { get { return Math.Max(0, Sys.Gpu); } }
        protected override string Center { get { return "GPU"; } }
        protected override string Caption { get { return "Graphics"; } }
        protected override string Big { get { return Sys.Gpu < 0 ? "…" : (int)Math.Round(Sys.Gpu) + "%"; } }
        protected override History Hist { get { return Sys.GpuH; } }
        public override string Tip(Ui u, Rectangle r, Point p) { return "GPU " + (Sys.Gpu < 0 ? "starting…" : (int)Sys.Gpu + "%") + "\nClick: Task Manager"; }
    }

    class DiskWidget : RingWidget
    {
        public DiskWidget() : base("disk", "Disk space", "System", "\uEDA2", "Free space on the Windows drive.") { }
        protected override float Pct { get { return Disk.TotalGb > 0 ? (float)(100 * (1 - Disk.FreeGb / Disk.TotalGb)) : 0; } }
        protected override string Center { get { return Disk.Name; } }
        protected override string Caption { get { return "Free space"; } }
        protected override string Big { get { return Disk.FreeGb < 0 ? "…" : Disk.FreeGb.ToString(Disk.FreeGb < 10 ? "0.0" : "0") + " GB"; } }
        public override string Tip(Ui u, Rectangle r, Point p) { return Disk.Name + "  " + Disk.FreeGb.ToString("0") + " GB free of " + Disk.TotalGb.ToString("0") + " GB\nClick: open This PC"; }
        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b) { if (b == MouseButtons.Left) Win.Launch("shell:MyComputerFolder"); }
    }

    class StatsWidget : Widget
    {
        public StatsWidget() : base("stats", "CPU + RAM bars", "System", "\uE9D9", "Two compact meters for CPU and memory.") { }
        public override int Width(Ui u) { return u.FI(124); }
        public override string LineText(Ui u) { return "CPU " + (int)Math.Round(Sys.Cpu) + "%  RAM " + (int)Math.Round(Sys.Ram) + "%"; }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            RectangleF t = u.Tile(r);
            float rowH = t.Height / 2f;
            Meter(g, u, "CPU", Sys.Cpu, t.X + u.F(10), t.Y + rowH * 0.5f + u.F(1), t.Width - u.F(20));
            Meter(g, u, "RAM", Sys.Ram, t.X + u.F(10), t.Y + rowH * 1.5f - u.F(1), t.Width - u.F(20));
        }

        static void Meter(Graphics g, Ui u, string label, float pct, float x, float cy, float w)
        {
            pct = Math.Max(0, Math.Min(100, pct));
            float lw = u.F(30), vw = u.F(32), bw = w - lw - vw - u.F(4), bh = Math.Max(3f, u.F(4));
            float th = u.Small.GetHeight();
            Gfx.Text(g, label, u.Small, u.Dim, x, cy - th / 2f);
            string v = (int)Math.Round(pct) + "%";
            Gfx.Text(g, v, u.Bold, u.Ink, x + w - Gfx.TW(v, u.Bold), cy - u.Bold.GetHeight() / 2f);
            Gfx.Meter(g, new RectangleF(x + lw, cy - bh / 2f, bw, bh), pct, Gfx.Load(pct, u.Accent), u.P.Track);
        }

        public override string Tip(Ui u, Rectangle r, Point p) { return "CPU " + (int)Sys.Cpu + "%   RAM " + (int)Sys.Ram + "%\nClick: Task Manager"; }
        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b) { if (b == MouseButtons.Left) Win.Launch("taskmgr.exe"); }
    }

    class BatteryWidget : Widget
    {
        public BatteryWidget() : base("battery", "Battery", "System", "\uE83F", "Charge level and time left. Only on PCs with a battery.") { }

        // Battery info, or null when there is no battery (desktop PC) or Windows can't tell.
        public static PowerStatus Battery()
        {
            try
            {
                PowerStatus ps = SystemInformation.PowerStatus;
                if ((ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) != 0) return null;
                if (ps.BatteryChargeStatus == BatteryChargeStatus.Unknown || ps.BatteryLifePercent > 1f) return null;
                return ps;
            }
            catch { return null; }
        }

        public override bool Available { get { return Svc.Demo || Battery() != null; } }
        public override int Width(Ui u) { return u.FI(116); }
        public override bool HasLabel { get { return true; } }
        public override string LineText(Ui u) { float pct; bool c, pl; int s; Read(out pct, out c, out pl, out s); return (int)Math.Round(pct) + "%" + (c ? " ⚡" : ""); }

        static void Read(out float pct, out bool charging, out bool plugged, out int secs)
        {
            if (Svc.Demo) { pct = 84; charging = false; plugged = false; secs = 3 * 3600 + 12 * 60; return; }
            PowerStatus ps = Battery();
            pct = ps == null ? 0 : ps.BatteryLifePercent * 100;
            plugged = ps != null && ps.PowerLineStatus == PowerLineStatus.Online;
            charging = ps != null && (ps.BatteryChargeStatus & BatteryChargeStatus.Charging) != 0;
            secs = ps == null ? -1 : ps.BatteryLifeRemaining;
        }

        static string Sub(float pct, bool charging, bool plugged, int secs)
        {
            if (charging) return "Charging";
            if (plugged) return pct >= 99 ? "Full" : "Plugged in";
            if (secs > 0) return (secs / 3600) + "h " + (secs % 3600 / 60).ToString("00") + "m left";
            return "On battery";
        }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            float pct; bool charging, plugged; int secs;
            Read(out pct, out charging, out plugged, out secs);
            RectangleF t = u.Tile(r);
            float bw = u.F(26), bh = u.F(13);
            var br = new RectangleF(t.X + u.F(11), t.Y + (t.Height - bh) / 2f, bw, bh);
            Gfx.Battery(g, br, pct, charging || plugged, u.Ink, u.Accent);
            if (charging || plugged) Gfx.Glyph(g, "\uE945", u.GlyphTiny, u.P.Light ? Color.White : Color.FromArgb(20, 20, 24), br);
            float x = br.Right + u.F(9), w = t.Right - x - u.F(8);
            float th = u.Title.GetHeight(), sh = u.Tiny.GetHeight();
            float y = t.Y + (t.Height - th - sh) / 2f;
            if (!ShowLabel) y = t.Y + (t.Height - th) / 2f;
            Gfx.TextIn(g, (int)Math.Round(pct) + "%", u.Title, u.Ink, new RectangleF(x, y, w, th), Gfx.Left);
            if (ShowLabel) Gfx.TextIn(g, Sub(pct, charging, plugged, secs), u.Tiny, u.Dim, new RectangleF(x, y + th * 0.95f, w, sh), Gfx.Left);
        }

        public override string Tip(Ui u, Rectangle r, Point p)
        {
            float pct; bool charging, plugged; int secs;
            Read(out pct, out charging, out plugged, out secs);
            return "Battery " + (int)Math.Round(pct) + "%  ·  " + Sub(pct, charging, plugged, secs) + "\nClick: power and battery settings";
        }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b) { if (b == MouseButtons.Left) Win.Launch("ms-settings:batterysaver"); }
    }

    // ==================================================================
    // Network
    // ==================================================================
    class ConnectWidget : Widget
    {
        public ConnectWidget() : base("connect", "Wi-Fi & Bluetooth", "Network", "\uE701", "Two pill buttons: tap to turn Wi-Fi or Bluetooth on and off, the arrow opens settings.") { }

        static bool HasWifi { get { return Svc.Demo || Radios.Wifi != -1; } }
        static bool HasBt { get { return Svc.Demo || Radios.Bt != -1; } }
        public override bool Shown(Ui u) { return HasWifi || HasBt; }

        public override int Width(Ui u)
        {
            int n = (HasWifi ? 1 : 0) + (HasBt ? 1 : 0);
            return n * u.FI(64) + Math.Max(0, n - 1) * u.FI(6) + u.FI(18);
        }

        List<KeyValuePair<bool, RectangleF>> Pills(Rectangle r, Ui u)
        {
            RectangleF t = u.Tile(r);
            float pw = u.F(64), ph = Math.Min(t.Height - u.F(12), u.F(32));
            float x = t.X + u.F(9), y = t.Y + (t.Height - ph) / 2f;
            var list = new List<KeyValuePair<bool, RectangleF>>();
            if (HasWifi) { list.Add(new KeyValuePair<bool, RectangleF>(true, new RectangleF(x, y, pw, ph))); x += pw + u.F(6); }
            if (HasBt) list.Add(new KeyValuePair<bool, RectangleF>(false, new RectangleF(x, y, pw, ph)));
            return list;
        }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            foreach (var kv in Pills(r, u))
            {
                bool wifi = kv.Key;
                RectangleF pr = kv.Value;
                int state = Svc.Demo ? 1 : wifi ? Radios.Wifi : Radios.Bt;
                bool on = state == 1;
                float rad = pr.Height / 2f;
                if (on) Theme.FillRound(g, pr, rad, Color.FromArgb(u.P.Light ? 40 : 46, u.Accent));
                if (In(pr, mouse)) Theme.FillRound(g, pr, rad, u.P.Hover);
                Theme.StrokeRound(g, RectangleF.Inflate(pr, -0.5f, -0.5f), rad, on ? Color.FromArgb(210, u.Accent) : Color.FromArgb(70, u.Ink), Math.Max(1f, u.F(1.2f)));
                float split = pr.X + pr.Width * 0.58f;
                using (var pen = new Pen(on ? Color.FromArgb(90, u.Accent) : Color.FromArgb(40, u.Ink), 1f))
                    g.DrawLine(pen, split, pr.Y + pr.Height * 0.28f, split, pr.Bottom - pr.Height * 0.28f);
                string glyph = wifi ? (Radios.Network == "Ethernet" && !on ? "\uE839" : "\uE701") : "\uE702";
                Color ink = on ? (u.P.Light ? Theme.Darken(u.Accent, 0.35f) : Theme.Lighten(u.Accent, 0.35f)) : u.Dim;
                Gfx.Glyph(g, glyph, u.GlyphSmall, ink, new RectangleF(pr.X + u.F(2), pr.Y, split - pr.X - u.F(2), pr.Height));
                Gfx.Glyph(g, "\uE76C", u.GlyphTiny, ink, new RectangleF(split, pr.Y, pr.Right - split - u.F(2), pr.Height));
            }
        }

        public override string Tip(Ui u, Rectangle r, Point p)
        {
            foreach (var kv in Pills(r, u))
            {
                if (!In(kv.Value, p)) continue;
                bool chevron = p.X > kv.Value.X + kv.Value.Width * 0.58f;
                if (kv.Key)
                {
                    if (chevron) return "Wi-Fi settings";
                    string net = Radios.Network;
                    return Radios.Wifi == 1 ? "Wi-Fi on" + (net.Length > 0 && net != "Ethernet" ? ": " + net : ", not connected") + "\nClick to turn off" : "Wi-Fi off\nClick to turn on";
                }
                if (chevron) return "Bluetooth settings";
                return Radios.Bt == 1 ? "Bluetooth on\nClick to turn off" : "Bluetooth off\nClick to turn on";
            }
            return Name;
        }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            foreach (var kv in Pills(r, bar.U))
            {
                if (!In(kv.Value, p)) continue;
                bool chevron = p.X > kv.Value.X + kv.Value.Width * 0.58f;
                if (b == MouseButtons.Right || chevron) Win.Launch(kv.Key ? "ms-settings:network-wifi" : "ms-settings:bluetooth");
                else { Radios.Toggle(kv.Key); bar.Invalidate(); }
                return;
            }
        }
    }

    class NetWidget : Widget
    {
        public NetWidget() : base("net", "Network speed", "Network", "\uE8CB", "Live download and upload speed with a graph.") { }
        public override int Width(Ui u) { return u.FI(132); }
        public override string LineText(Ui u) { return "↓ " + Sys.Rate(Sys.Down); }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            RectangleF t = u.Tile(r);
            float max = Math.Max(Sys.DownH.Max(), Sys.UpH.Max());
            var sr = new RectangleF(t.X + u.F(4), t.Y + t.Height * 0.35f, t.Width - u.F(8), t.Height * 0.6f);
            Gfx.Spark(g, sr, Sys.DownH.Data, Sys.DownH.Count, Math.Max(1, max), Color.FromArgb(150, u.Accent), true);
            Gfx.Spark(g, sr, Sys.UpH.Data, Sys.UpH.Count, Math.Max(1, max), Color.FromArgb(110, u.Ink), false);
            float lh = u.Bold.GetHeight(), x = t.X + u.F(10), gw = u.F(16);
            float y = t.Y + (t.Height - lh * 2) / 2f;
            Gfx.Glyph(g, "\uE896", u.GlyphTiny, u.Accent, new RectangleF(x, y, gw, lh));
            Gfx.Text(g, Sys.Rate(Sys.Down), u.Bold, u.Ink, x + gw, y);
            Gfx.Glyph(g, "\uE898", u.GlyphTiny, u.Dim, new RectangleF(x, y + lh, gw, lh));
            Gfx.Text(g, Sys.Rate(Sys.Up), u.Bold, u.Dim, x + gw, y + lh);
        }

        public override string Tip(Ui u, Rectangle r, Point p) { return "Download " + Sys.Rate(Sys.Down) + "\nUpload " + Sys.Rate(Sys.Up) + (Radios.Network.Length > 0 ? "\n" + Radios.Network : ""); }
        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b) { if (b == MouseButtons.Left) Win.Launch("ms-settings:network-status"); }
    }

    // ==================================================================
    // Time & weather
    // ==================================================================
    class WeatherWidget : Widget
    {
        public WeatherWidget() : base("weather", "Weather", "Time & weather", "\uE753", "Current weather and a 7-day forecast from Open-Meteo. Set your city in Settings.") { }
        public override int Width(Ui u) { return u.FI(132); }
        public override int CompactWidth(Ui u) { return u.FI(88); }

        public static string Deg(double v) { return ((int)Math.Round(v)) + "°"; }
        public override bool HasLabel { get { return true; } }
        public override string LineText(Ui u) { WeatherInfo w = Weather.Now; return w == null || w.Error != null ? "–°" : Deg(w.Temp) + " " + Weather.Text(w.Code); }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            WeatherInfo w = Weather.Now;
            RectangleF t = u.Tile(r);
            float isz = t.Height - u.F(12);
            var ir = new RectangleF(t.X + u.F(8), t.Y + (t.Height - isz) / 2f - u.S(1) * hover, isz, isz);
            float x = ir.Right + u.F(7), wdt = t.Right - x - u.F(8);
            float th = u.Title.GetHeight() * 1.12f, sh = u.Tiny.GetHeight();
            float y = t.Y + (t.Height - th - sh) / 2f;
            if (w == null || w.Error != null)
            {
                Gfx.Weather(g, ir, 2, true, u.Faint);
                Gfx.TextIn(g, "–°", u.Title, u.Dim, new RectangleF(x, y, wdt, th), Gfx.Left);
                Gfx.TextIn(g, w == null ? "Loading…" : w.Error, u.Tiny, u.Dim, new RectangleF(x, y + th, wdt, sh), Gfx.Left);
                return;
            }
            Gfx.Weather(g, ir, Weather.Kind(w.Code), w.Day, u.Ink);
            if (!ShowLabel) y = t.Y + (t.Height - th) / 2f;
            Gfx.TextIn(g, Deg(w.Temp), Gfx.FontOf("Segoe UI Semibold", u.Title.Size * 1.18f), u.Ink, new RectangleF(x, y - u.F(1), wdt, th), Gfx.Left);
            if (ShowLabel) Gfx.TextIn(g, Weather.Text(w.Code), u.Tiny, u.Dim, new RectangleF(x, y + th, wdt, sh), Gfx.Left);
        }

        public override string Tip(Ui u, Rectangle r, Point p)
        {
            WeatherInfo w = Weather.Now;
            if (w == null) return "Weather: loading…";
            if (w.Error != null) return "Weather: " + w.Error + " (" + (w.City ?? "") + ")\nSet your city in Settings > Widgets";
            return w.City + "  ·  " + Weather.Text(w.Code) + "  " + Deg(w.Temp) + "\nFeels like " + Deg(w.Feels) + ", wind " + (int)w.Wind + (Config.Fahrenheit ? " mph" : " km/h") + "\nClick for the 7-day forecast";
        }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            if (b == MouseButtons.Left) bar.ToggleFlyout("weather", r, () => new WeatherFlyout(bar.U.Scale));
            else if (b == MouseButtons.Right)
            {
                ContextMenuStrip m = bar.NewMenu(false);
                BarForm.Item(m, "Refresh", Weather.Refresh);
                BarForm.Item(m, "Change city…", () => bar.OpenSettings("Widgets"));
                bar.ShowMenu(m, p);
            }
        }
    }

    class CalendarWidget : Widget
    {
        public CalendarWidget() : base("calendar", "Week strip", "Time & weather", "\uE787", "This week at a glance, today highlighted. Click for the month calendar.") { }
        public override int Width(Ui u) { return u.FI(7 * 25 + 20); }

        public static DateTime WeekStart(DateTime d)
        {
            int diff = ((int)d.DayOfWeek + 6) % 7;   // Monday first
            return d.Date.AddDays(-diff);
        }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            RectangleF t = u.Tile(r);
            DateTime today = DateTime.Today, start = WeekStart(today);
            float cw = u.F(25), x0 = t.X + (t.Width - cw * 7) / 2f;
            float th = u.Tiny.GetHeight(), nh = u.Bold.GetHeight();
            float y = t.Y + (t.Height - th - nh - u.F(2)) / 2f;
            for (int i = 0; i < 7; i++)
            {
                DateTime d = start.AddDays(i);
                var col = new RectangleF(x0 + i * cw, y, cw, th + nh + u.F(2));
                bool isToday = d == today, weekend = i >= 5;
                if (isToday) Theme.FillRound(g, RectangleF.Inflate(col, -u.F(1.5f), u.F(3)), u.F(8), u.Accent);
                Color top = isToday ? Gfx.On(u.Accent) : weekend ? u.Faint : u.Dim;
                Color num = isToday ? Gfx.On(u.Accent) : weekend ? u.Dim : u.Ink;
                string wd = d.ToString("ddd", CultureInfo.CurrentCulture).Substring(0, 1).ToUpperInvariant();
                Gfx.TextIn(g, wd, u.Tiny, top, new RectangleF(col.X, col.Y, cw, th), Gfx.Mid);
                Gfx.TextIn(g, d.Day.ToString(), u.Bold, num, new RectangleF(col.X, col.Y + th + u.F(1), cw, nh), Gfx.Mid);
            }
        }

        public override string Tip(Ui u, Rectangle r, Point p) { return DateTime.Now.ToString("D") + "\nWeek " + CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(DateTime.Today, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday) + "  ·  click for the month"; }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            if (b == MouseButtons.Left) bar.ToggleFlyout("calendar", r, () => new CalendarFlyout(bar.U.Scale));
        }
    }

    class WorldClockWidget : Widget
    {
        public WorldClockWidget() : base("worldclock", "World clocks", "Time & weather", "\uE774", "Up to three other time zones. Pick them in Settings > Widgets.") { }

        public override bool Multi { get { return true; } }

        // The first world clock uses the clocks set on the Widgets page; each copy has its own.
        public static List<KeyValuePair<string, TimeZoneInfo>> Zones() { return ZonesFrom(Config.WorldClocks); }

        static readonly Dictionary<string, string> shortNames = new Dictionary<string, string>();

        static string ShortName(string zoneId)
        {
            string n;
            lock (shortNames) if (shortNames.TryGetValue(zoneId, out n)) return n;
            try { n = TimeZoneInfo.FindSystemTimeZoneById(zoneId).StandardName.Split(' ')[0]; } catch { n = "Clock"; }
            lock (shortNames) shortNames[zoneId] = n;
            return n;
        }

        public List<KeyValuePair<string, TimeZoneInfo>> MyZones()
        {
            if (Key == Id) return Zones();
            var parts = new List<string>();
            for (int i = 1; i <= 3; i++)
            {
                string z = Opt("zone" + i, i == 1 ? "GMT Standard Time" : i == 2 ? "Eastern Standard Time" : i == 3 ? "Tokyo Standard Time" : "");
                if (z.Length == 0 || z == "none") continue;
                string n = Opt("name" + i, "");
                if (n.Length == 0) n = ShortName(z);
                parts.Add(n.Replace("|", "").Replace(";", "") + "|" + z);
            }
            return ZonesFrom(string.Join(";", parts.ToArray()));
        }

        public override void Options(IWidgetOptions o)
        {
            if (Key == Id)
            {
                o.Note("The clocks of this widget are set on the Widgets page (World clocks).");
                o.Button("World clocks", null, "Open", () => BarForm.Instance.OpenSettings("Widgets"));
                return;
            }
            var all = TimeZoneInfo.GetSystemTimeZones();
            string[] ids = new[] { "none" }.Concat(all.Select(z => z.Id)).ToArray();
            string[] names = new[] { "(none)" }.Concat(all.Select(z => z.DisplayName)).ToArray();
            for (int i = 1; i <= 3; i++)
            {
                o.Text("Clock " + i + " name", i == 1 ? "Empty = the time zone's own name" : null, "name" + i, "");
                o.Choice("Clock " + i + " time zone", null, "zone" + i, ids, names, i == 1 ? "GMT Standard Time" : i == 2 ? "Eastern Standard Time" : "Tokyo Standard Time");
            }
        }

        public override string LineText(Ui u)
        {
            var z = MyZones();
            if (z.Count == 0) return null;
            return z[0].Key + " " + TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, z[0].Value).ToString(Config.Clock24 ? "HH:mm" : "h:mm");
        }

        static readonly Dictionary<string, List<KeyValuePair<string, TimeZoneInfo>>> zoneCache = new Dictionary<string, List<KeyValuePair<string, TimeZoneInfo>>>();

        // Parsed once per setting (looking time zones up is slow).
        public static List<KeyValuePair<string, TimeZoneInfo>> ZonesFrom(string spec)
        {
            spec = spec ?? "";
            List<KeyValuePair<string, TimeZoneInfo>> cached;
            lock (zoneCache) if (zoneCache.TryGetValue(spec, out cached)) return cached;
            var parsed = ParseZones(spec);
            lock (zoneCache) zoneCache[spec] = parsed;
            return parsed;
        }

        static List<KeyValuePair<string, TimeZoneInfo>> ParseZones(string spec)
        {
            var list = new List<KeyValuePair<string, TimeZoneInfo>>();
            foreach (string part in (spec ?? "").Split(';'))
            {
                string[] kv = part.Split('|');
                if (kv.Length != 2) continue;
                try { list.Add(new KeyValuePair<string, TimeZoneInfo>(kv[0].Trim(), TimeZoneInfo.FindSystemTimeZoneById(kv[1].Trim()))); }
                catch { }
                if (list.Count == 3) break;
            }
            return list;
        }

        public override bool Shown(Ui u) { return MyZones().Count > 0; }
        public override int Width(Ui u) { return Math.Max(1, MyZones().Count) * u.FI(66) + u.FI(12); }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            RectangleF t = u.Tile(r);
            var zones = MyZones();
            float cw = u.F(66), x0 = t.X + u.F(6);
            float th = u.Tiny.GetHeight(), nh = u.Title.GetHeight();
            float y = t.Y + (t.Height - th - nh) / 2f;
            DateTime nowUtc = DateTime.UtcNow, localDay = DateTime.Today;
            for (int i = 0; i < zones.Count; i++)
            {
                DateTime there = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zones[i].Value);
                int dd = (there.Date - localDay).Days;
                var col = new RectangleF(x0 + i * cw, y, cw, th + nh);
                if (i > 0)
                    using (var pen = new Pen(Color.FromArgb(30, u.Ink))) g.DrawLine(pen, col.X, t.Y + t.Height * 0.25f, col.X, t.Bottom - t.Height * 0.25f);
                bool night = there.Hour < 7 || there.Hour >= 21;
                Gfx.TextIn(g, zones[i].Key, u.Tiny, u.Dim, new RectangleF(col.X + u.F(4), col.Y, cw - u.F(8), th), Gfx.Mid);
                string time = there.ToString(Config.Clock24 ? "HH:mm" : "h:mm");
                Gfx.TextIn(g, time, u.Title, night ? u.Dim : u.Ink, new RectangleF(col.X, col.Y + th * 0.95f, cw, nh), Gfx.Mid);
                if (dd != 0)
                    Gfx.Text(g, (dd > 0 ? "+" : "") + dd, u.Tiny, u.Accent, col.X + cw / 2f + Gfx.TW(time, u.Title) / 2f + u.F(1), col.Y + th * 0.95f);
            }
        }

        public override string Tip(Ui u, Rectangle r, Point p)
        {
            var lines = MyZones().Select(z => z.Key + "  " + TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, z.Value).ToString("ddd HH:mm"));
            return string.Join("\n", lines.ToArray()) + "\nRight-click: widget settings";
        }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            if (b == MouseButtons.Left) bar.ToggleFlyout("calendar", r, () => new CalendarFlyout(bar.U.Scale));
        }
    }

    class ScreenTimeWidget : Widget
    {
        public ScreenTimeWidget() : base("screentime", "Screen time", "Time & weather", "\uE916", "How long you've used the PC today, by hour. Click for your top apps.") { }
        public override int Width(Ui u) { return u.FI(172); }
        public override int CompactWidth(Ui u) { return u.FI(118); }
        public override bool HasLabel { get { return true; } }
        public override string LineText(Ui u) { return ScreenTime.Format(ScreenTime.Total); }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            RectangleF t = u.Tile(r);
            float gs = u.F(26);
            var gr = new RectangleF(t.X + u.F(8), t.Y + (t.Height - gs) / 2f, gs, gs);
            Gfx.Glyph(g, "\uE916", u.Glyph, u.Accent, gr);
            float x = gr.Right + u.F(6);
            float th = ShowLabel ? u.Tiny.GetHeight() : 0, nh = u.Title.GetHeight() * 1.1f;
            float y = t.Y + (t.Height - th - nh) / 2f;
            if (ShowLabel) Gfx.Text(g, "Screen time", u.Tiny, u.Dim, x, y);
            Gfx.Text(g, ScreenTime.Format(ScreenTime.Total), Gfx.FontOf("Segoe UI Semibold", u.Title.Size * 1.15f), u.Ink, x, y + th);
            float[] hours = ScreenTime.Hours;
            int now = DateTime.Now.Hour;
            var last = new float[12];
            for (int i = 0; i < 12; i++) { int h = now - 11 + i; last[i] = h >= 0 ? hours[h] : 0; }
            var br = new RectangleF(t.Right - u.F(54), t.Y + t.Height * 0.3f, u.F(46), t.Height * 0.46f);
            if (r.Width >= Width(u) - 2) Gfx.Bars(g, br, last, u.Accent, Color.FromArgb(40, u.Ink), 11);
        }

        public override string Tip(Ui u, Rectangle r, Point p)
        {
            string s = "Screen time today: " + ScreenTime.Format(ScreenTime.Total);
            if (ScreenTime.Yesterday >= 0) s += "\nYesterday: " + ScreenTime.Format(ScreenTime.Yesterday);
            return s + "\nClick for your top apps";
        }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            if (b == MouseButtons.Left) bar.ToggleFlyout("screentime", r, () => new ScreenTimeFlyout(bar));
        }
    }

    // ==================================================================
    // Productivity
    // ==================================================================
    class FocusWidget : Widget
    {
        public FocusWidget() : base("focus", "Focus timer", "Productivity", "\uE916", "A Pomodoro timer: focus, then a short break. Click to start or pause.") { }
        public override bool Live { get { return Focus.Running; } }
        public override int Width(Ui u) { return u.FI(118); }
        public override bool HasLabel { get { return true; } }
        public override string LineText(Ui u) { return Focus.Clock(Focus.Remaining); }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            RectangleF t = u.Tile(r);
            float d = t.Height - u.F(14), rw = Math.Max(2.5f, u.F(3.4f));
            var c = new PointF(t.X + u.F(8) + d / 2, t.Y + t.Height / 2);
            Color col = Focus.State == Focus.Phase.Break ? Color.FromArgb(74, 222, 128) : u.Accent;
            Gfx.Ring(g, c, d / 2 - rw / 2, rw, Focus.Progress * 100, col, u.P.Track);
            float id = d * 0.62f;
            Gfx.MediaIcon(g, new RectangleF(c.X - id / 2, c.Y - id / 2, id, id), Focus.Running ? 2 : 1, Focus.Running ? u.Ink : col);
            float x = c.X + d / 2 + u.F(8), w = t.Right - x - u.F(6);
            float th = ShowLabel ? u.Tiny.GetHeight() : 0, nh = u.Title.GetHeight();
            float y = t.Y + (t.Height - th - nh) / 2f;
            string label = Focus.State == Focus.Phase.Idle ? "Focus" : Focus.State == Focus.Phase.Focus ? (Focus.Paused ? "Paused" : "Focusing") : "Break";
            if (ShowLabel) Gfx.TextIn(g, label, u.Tiny, u.Dim, new RectangleF(x, y, w, th), Gfx.Left);
            Gfx.TextIn(g, Focus.Clock(Focus.Remaining), u.Title, u.Ink, new RectangleF(x, y + th, w, nh), Gfx.Left);
        }

        public override string Tip(Ui u, Rectangle r, Point p)
        {
            if (Focus.State == Focus.Phase.Idle) return "Focus timer: " + Config.FocusMin + " min focus, " + Config.BreakMin + " min break\nClick to start, right-click for options";
            return (Focus.State == Focus.Phase.Focus ? "Focus" : "Break") + " · " + Focus.Clock(Focus.Remaining) + " left" +
                   (Focus.Rounds > 0 ? "\n" + Focus.Rounds + " round" + (Focus.Rounds > 1 ? "s" : "") + " done today" : "");
        }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            if (b == MouseButtons.Left) { Focus.Toggle(); bar.Animate(); bar.Invalidate(); return; }
            if (b != MouseButtons.Right) return;
            ContextMenuStrip m = bar.NewMenu(true);
            BarForm.Item(m, Focus.Running ? "Pause" : "Start", () => { Focus.Toggle(); bar.Invalidate(); });
            if (Focus.State != Focus.Phase.Idle)
            {
                BarForm.Item(m, Focus.State == Focus.Phase.Focus ? "Skip to break" : "Skip to focus", () => { Focus.Skip(); bar.Invalidate(); });
                BarForm.Item(m, "Reset", () => { Focus.Reset(); bar.Invalidate(); });
            }
            m.Items.Add(new ToolStripSeparator());
            foreach (int mins in new[] { 15, 25, 45, 50 })
            {
                int v = mins;
                BarForm.Item(m, mins + " min focus", () => { Config.FocusMin = v; Config.Save(); bar.Invalidate(); }).Checked = Config.FocusMin == mins;
            }
            m.Items.Add(new ToolStripSeparator());
            foreach (int mins in new[] { 5, 10, 15 })
            {
                int v = mins;
                BarForm.Item(m, mins + " min break", () => { Config.BreakMin = v; Config.Save(); bar.Invalidate(); }).Checked = Config.BreakMin == mins;
            }
            bar.ShowMenu(m, p);
        }
    }

    class NoteWidget : Widget
    {
        public NoteWidget() : base("note", "Quick note", "Productivity", "\uE70F", "A sticky note that lives on the bar. Click to write; it saves as you type.") { }
        public override int Width(Ui u) { return u.FI(150); }
        public override int CompactWidth(Ui u) { return u.FI(100); }
        public override bool Multi { get { return true; } }
        NoteStore Store { get { return Note.For(Key); } }
        public override string LineText(Ui u) { string l = Store.FirstLine; return l.Length == 0 ? "Note" : l; }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            RectangleF t = u.Tile(r);
            float gs = u.F(24);
            var gr = new RectangleF(t.X + u.F(8), t.Y + (t.Height - gs) / 2f, gs, gs);
            Theme.FillRound(g, gr, u.F(6), Color.FromArgb(60, 250, 204, 21));
            Gfx.Glyph(g, "\uE70F", u.GlyphSmall, Color.FromArgb(253, 224, 71), gr);
            string line = Store.FirstLine;
            var tr = new RectangleF(gr.Right + u.F(7), t.Y, t.Right - gr.Right - u.F(14), t.Height);
            if (line.Length == 0) Gfx.TextIn(g, "Quick note", u.Small, u.Faint, tr, Gfx.Left);
            else Gfx.TextIn(g, line, u.Small, u.Ink, tr, Gfx.Left);
        }

        public override string Tip(Ui u, Rectangle r, Point p)
        {
            string t = Store.Text.Trim();
            return t.Length == 0 ? "Quick note\nClick to write something down" : Win.ShortTitle(t, 300);
        }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            NoteStore s = Store;
            if (b == MouseButtons.Left) bar.ToggleFlyout("note:" + Key, r, () => new NoteFlyout(bar.U.Scale, s));
        }
    }

    class ClipboardWidget : Widget
    {
        public ClipboardWidget() : base("clipboard", "Clipboard history", "Productivity", "\uE77F", "The last 30 things you copied. Click one to copy it again.") { }
        public override int Width(Ui u) { return u.Cell; }
        public override bool Sizable { get { return false; } }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            var gr = new RectangleF(r.X, r.Y - u.S(1.5f) * hover, r.Width, r.Height);
            Gfx.Glyph(g, "\uE77F", u.GlyphBig, Config.ClipboardOn ? u.Ink : u.Faint, gr);
            int n = Clip.Items.Count;
            if (n > 0)
            {
                string s = n > 9 ? "9+" : n.ToString();
                float d = u.F(14), bx = r.X + r.Width / 2f + u.F(5), by = r.Y + r.Height / 2f - u.F(15);
                var br = new RectangleF(bx, by, Math.Max(d, Gfx.TW(s, u.Tiny) + u.F(6)), d);
                Theme.FillRound(g, br, d / 2, u.Accent);
                Gfx.TextIn(g, s, u.Tiny, Gfx.On(u.Accent), br, Gfx.Mid);
            }
        }

        public override string Tip(Ui u, Rectangle r, Point p) { return Config.ClipboardOn ? "Clipboard history  (" + Clip.Items.Count + ")" : "Clipboard history is off (Settings > Widgets)"; }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            if (b == MouseButtons.Left) bar.ToggleFlyout("clipboard", r, () => new ClipboardFlyout(bar.U.Scale));
            else if (b == MouseButtons.Right)
            {
                ContextMenuStrip m = bar.NewMenu(false);
                BarForm.Item(m, "Clear history", () => { Clip.Clear(); bar.Invalidate(); });
                BarForm.Item(m, "Windows clipboard (Win+V)", () => Win.Press(Native.VK_LWIN, 0x56));
                bar.ShowMenu(m, p);
            }
        }
    }

    class TrashWidget : Widget
    {
        public TrashWidget() : base("trash", "Recycle Bin", "Productivity", "\uE74D", "Shows when the Recycle Bin has something in it. Right-click to empty it.") { }
        public override int Width(Ui u) { return u.Cell; }
        public override bool Sizable { get { return false; } }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            var gr = new RectangleF(r.X, r.Y - u.S(1.5f) * hover, r.Width, r.Height);
            int n = Svc.Demo ? 4 : Recycle.Items;
            Gfx.Glyph(g, "\uE74D", u.GlyphBig, n > 0 ? u.Ink : u.Dim, gr);
            if (n > 0)
            {
                float d = u.F(7);
                using (var b = new SolidBrush(u.Accent)) g.FillEllipse(b, r.X + r.Width / 2f + u.F(6), r.Y + r.Height / 2f - u.F(12), d, d);
            }
        }

        public override string Tip(Ui u, Rectangle r, Point p)
        {
            int n = Recycle.Items;
            if (n <= 0) return "Recycle Bin is empty";
            return "Recycle Bin: " + n + " item" + (n == 1 ? "" : "s") + ", " + (Recycle.Bytes / 1048576.0).ToString("0.#") + " MB\nRight-click to empty";
        }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            if (b == MouseButtons.Left) Win.Launch("shell:RecycleBinFolder");
            else if (b == MouseButtons.Right)
            {
                ContextMenuStrip m = bar.NewMenu(false);
                BarForm.Item(m, "Open", () => Win.Launch("shell:RecycleBinFolder"));
                var empty = BarForm.Item(m, "Empty Recycle Bin", () => Recycle.Empty(bar.Handle));
                empty.Enabled = Recycle.Items != 0;
                bar.ShowMenu(m, p);
            }
        }
    }
}
