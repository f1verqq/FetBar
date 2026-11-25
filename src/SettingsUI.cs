// Settings window (sidebar), widget gallery, and the hand-drawn controls they use.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace StayBar
{
    // ------------------------------------------------------------------
    // Controls (drawn by hand so they look like Windows 11)
    // ------------------------------------------------------------------
    abstract class SmoothControl : Control
    {
        protected bool hot;

        protected SmoothControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Cursor = Cursors.Hand;
        }

        protected Graphics Prep(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            return g;
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hot = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = false; Invalidate(); }
    }

    class ToggleSwitch : SmoothControl
    {
        bool on;
        public event EventHandler Changed;

        public bool Checked
        {
            get { return on; }
            set { if (on == value) return; on = value; Invalidate(); if (Changed != null) Changed(this, EventArgs.Empty); }
        }

        protected override void OnClick(EventArgs e) { base.OnClick(e); Checked = !Checked; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Prep(e);
            var r = new RectangleF(1, 1, Width - 3, Height - 3);
            if (on) Theme.FillRound(g, r, r.Height / 2, hot ? Theme.Lighten(Config.Accent, 0.12f) : Config.Accent);
            else
            {
                if (hot) Theme.FillRound(g, r, r.Height / 2, Color.FromArgb(20, 255, 255, 255));
                using (var p = new Pen(Theme.SubText, 1.3f))
                using (GraphicsPath path = Theme.Round(r, r.Height / 2)) g.DrawPath(p, path);
            }
            float d = r.Height * (hot ? 0.62f : 0.56f), pad = (r.Height - d) / 2f;
            float x = on ? r.Right - d - pad : r.X + pad;
            using (var b = new SolidBrush(on ? Color.FromArgb(18, 18, 22) : Theme.SubText)) g.FillEllipse(b, x, r.Y + pad, d, d);
        }
    }

    class Segmented : SmoothControl
    {
        readonly string[] options;
        int selected, hover = -1;
        public event EventHandler Changed;
        public int Selected { get { return selected; } set { selected = value; Invalidate(); } }

        public Segmented(string[] options, int selected) { this.options = options; this.selected = selected; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = Math.Max(0, Math.Min(options.Length - 1, e.X * options.Length / Math.Max(1, Width)));
            if (i != hover) { hover = i; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int i = Math.Max(0, Math.Min(options.Length - 1, e.X * options.Length / Math.Max(1, Width)));
            if (i == selected) return;
            selected = i;
            Invalidate();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Prep(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            float rad = Height * 0.28f;
            Theme.FillRound(g, r, rad, Theme.Field);
            float w = r.Width / options.Length;
            for (int i = 0; i < options.Length; i++)
            {
                var seg = new RectangleF(r.X + i * w + 3, r.Y + 3, w - 6, r.Height - 6);
                if (i == selected) Theme.FillRound(g, seg, rad - 2, Color.FromArgb(68, 68, 80));
                else if (i == hover) Theme.FillRound(g, seg, rad - 2, Color.FromArgb(40, 40, 48));
                using (var b = new SolidBrush(i == selected ? Theme.Text : Theme.SubText))
                    g.DrawString(options[i], Font, b, seg, Theme.Center);
            }
        }
    }

    class Slider : SmoothControl
    {
        readonly int min, max;
        int value;
        bool dragging;
        public event EventHandler Changed;

        public Slider(int min, int max, int value) { this.min = min; this.max = max; this.value = value; }

        public int Value
        {
            get { return value; }
            set
            {
                int v = Math.Max(min, Math.Min(max, value));
                if (v == this.value) return;
                this.value = v;
                Invalidate();
                if (Changed != null) Changed(this, EventArgs.Empty);
            }
        }

        float Knob { get { return Height * 0.62f; } }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); dragging = true; Capture = true; SetFrom(e.X); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging) SetFrom(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; Capture = false; Invalidate(); }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Value += e.Delta > 0 ? 1 : -1; }

        void SetFrom(int x)
        {
            float pad = Knob / 2 + 1;
            float t = Math.Max(0f, Math.Min(1f, (x - pad) / Math.Max(1f, Width - 2 * pad)));
            Value = min + (int)Math.Round(t * (max - min));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Prep(e);
            float pad = Knob / 2 + 1, cy = Height / 2f, th = Math.Max(3f, Height * 0.15f);
            float t = (value - min) / (float)Math.Max(1, max - min);
            float kx = pad + t * (Width - 2 * pad);
            Theme.FillRound(g, new RectangleF(pad, cy - th / 2, Width - 2 * pad, th), th / 2, Theme.Track);
            Theme.FillRound(g, new RectangleF(pad, cy - th / 2, Math.Max(th, kx - pad), th), th / 2, Config.Accent);
            using (var b = new SolidBrush(Color.FromArgb(70, 70, 80))) g.FillEllipse(b, kx - Knob / 2, cy - Knob / 2, Knob, Knob);
            float inner = Knob * (dragging ? 0.62f : hot ? 0.56f : 0.48f);
            using (var b = new SolidBrush(Config.Accent)) g.FillEllipse(b, kx - inner / 2, cy - inner / 2, inner, inner);
        }
    }

    class Swatch : SmoothControl
    {
        public Color Fill;
        public bool Selected;

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Prep(e);
            float d = Math.Min(Width, Height) - 2;
            var r = new RectangleF((Width - d) / 2, (Height - d) / 2, d, d);
            if (Selected || hot) using (var p = new Pen(Selected ? Theme.Text : Theme.SubText, Math.Max(1.5f, d * 0.07f))) g.DrawEllipse(p, r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
            float inset = d * 0.2f;
            var inner = new RectangleF(r.X + inset, r.Y + inset, r.Width - 2 * inset, r.Height - 2 * inset);
            using (var b = new SolidBrush(Fill)) g.FillEllipse(b, inner);
        }
    }

    class IconButton : SmoothControl
    {
        public string Glyph;
        public Color? Color0;
        public IconButton(string glyph) { Glyph = glyph; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Prep(e);
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            if (hot) Theme.FillRound(g, r, S6, Color.FromArgb(50, 50, 60));
            using (var f = new Font(Gfx.GlyphFamily(), Height * 0.42f, GraphicsUnit.Pixel))
                Gfx.Glyph(g, Glyph, f, Enabled ? (Color0 ?? Theme.Text) : Color.FromArgb(80, 80, 90), r);
        }

        float S6 { get { return Height * 0.2f; } }
    }

    class PillButton : SmoothControl
    {
        public bool Primary;
        public PillButton(string text, bool primary) { Text = text; Primary = primary; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Prep(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Color fill = Primary ? (hot ? Theme.Lighten(Config.Accent, 0.12f) : Config.Accent) : (hot ? Color.FromArgb(66, 66, 76) : Color.FromArgb(52, 52, 60));
            Theme.FillRound(g, r, Height * 0.24f, fill);
            using (var b = new SolidBrush(Primary ? Gfx.On(Config.Accent) : Theme.Text)) g.DrawString(Text, Font, b, r, Theme.Center);
        }
    }

    class Card : Panel
    {
        public Card() { DoubleBuffered = true; BackColor = Theme.Card; }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRound(g, new RectangleF(0, 0, Width - 1, Height - 1), Math.Min(10, Height / 4f), Theme.Card);
        }
    }

    class NavButton : SmoothControl
    {
        public string Glyph;
        public bool Selected;
        public NavButton(string glyph, string text) { Glyph = glyph; Text = text; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Prep(e);
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            if (Selected) Theme.FillRound(g, r, Height * 0.2f, Color.FromArgb(40, 40, 48));
            else if (hot) Theme.FillRound(g, r, Height * 0.2f, Color.FromArgb(30, 30, 36));
            if (Selected) Theme.FillRound(g, new RectangleF(0, Height * 0.3f, 3.5f, Height * 0.4f), 1.75f, Config.Accent);
            using (var f = new Font(Gfx.GlyphFamily(), Height * 0.38f, GraphicsUnit.Pixel))
                Gfx.Glyph(g, Glyph, f, Selected ? Theme.Text : Theme.SubText, new RectangleF(Height * 0.2f, 0, Height * 0.8f, Height));
            using (var b = new SolidBrush(Selected ? Theme.Text : Theme.SubText))
                g.DrawString(Text, Font, b, new RectangleF(Height * 1.05f, 0, Width - Height, Height), Gfx.Left);
        }
    }

    // A small picture of a bar in one theme.
    class ThemeCard : SmoothControl
    {
        public Palette Pal;
        public bool Selected;
        readonly float sc;
        public ThemeCard(Palette p, float sc) { Pal = p; this.sc = sc; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Prep(e);
            var outer = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Theme.FillRound(g, outer, 10 * sc, hot ? Theme.CardHover : Theme.Card);
            var pic = new RectangleF(8 * sc, 8 * sc, Width - 16 * sc, Height - 40 * sc);
            using (GraphicsPath pp = Theme.Round(pic, 7 * sc))
            {
                using (var lb = new LinearGradientBrush(pic, Color.FromArgb(48, 92, 170), Color.FromArgb(170, 72, 130), 20f)) g.FillPath(lb, pp);
                Region old = g.Clip;
                g.SetClip(pp, CombineMode.Intersect);
                var barR = new RectangleF(pic.X, pic.Bottom - 26 * sc, pic.Width, 26 * sc);
                using (var b = new SolidBrush(Pal.TintAt(Config.Darkness))) g.FillRectangle(b, barR);
                float x = barR.X + 4 * sc;
                float[] widths = { 20, 46, 30, 34 };
                foreach (float w in widths)
                {
                    var tr = new RectangleF(x, barR.Y + 3.5f * sc, w * sc, barR.Height - 7 * sc);
                    Gfx.Tile(g, tr, Math.Min(5 * sc, Config.Radius * sc * 0.45f), Pal, Config.Accent, 0, 1f);
                    x += w * sc + 3 * sc;
                }
                using (var b = new SolidBrush(Config.Accent)) g.FillRectangle(b, barR.X + 9 * sc, barR.Y + 9 * sc, 9 * sc, 9 * sc);
                using (var f = new Font("Segoe UI Semibold", 9 * sc, GraphicsUnit.Pixel))
                using (var b = new SolidBrush(Pal.Ink))
                    g.DrawString("12:30", f, b, new RectangleF(x - 37 * sc, barR.Y, 34 * sc, barR.Height), Theme.Center);
                g.Clip = old;
            }
            using (var b = new SolidBrush(Theme.Text)) g.DrawString(Pal.Name, Font, b, new RectangleF(10 * sc, Height - 30 * sc, Width - 20 * sc, 24 * sc), Gfx.Left);
            if (Selected) Theme.StrokeRound(g, RectangleF.Inflate(outer, -1, -1), 10 * sc, Config.Accent, 2f);
        }
    }

    // ------------------------------------------------------------------
    // Settings window
    // ------------------------------------------------------------------
    class SettingsForm : Form, IWidgetOptions
    {
        readonly BarForm bar;
        readonly float sc;
        readonly Panel side, content;
        readonly Font rowFont, descFont, headFont, titleFont, pageFont;
        readonly List<NavButton> nav = new List<NavButton>();
        string page;
        int y, cardW;
        Panel host;

        static readonly string[][] Pages =
        {
            new[] { "Dock", "\uE8A1", "Style, position and size" },
            new[] { "Appearance", "\uE790", "Themes, colors and glass" },
            new[] { "Widgets", "\uE71D", "What's on the bar, and widget options" },
            new[] { "Apps", "\uECAA", "How app icons are grouped, labelled and hidden" },
            new[] { "Behavior", "\uE9E9", "Auto-hide, shortcuts and previews" },
            new[] { "Profiles", "\uE77B", "Saved bars you can switch between" },
            new[] { "Automations", "\uE945", "When something happens, change the bar" },
            new[] { "Screens", "\uE7F4", "A bar on each screen, each with its own layout" },
            new[] { "General", "\uE713", "Startup, pinned apps and more" },
        };

        int S(float v) { return (int)Math.Round(v * sc); }

        public SettingsForm(BarForm bar, string startPage)
        {
            this.bar = bar;
            sc = bar.UiScale;
            Text = "StayBar settings";
            ShowIcon = false;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            titleFont = new Font("Segoe UI Semibold", 22f * sc, GraphicsUnit.Pixel);
            pageFont = new Font("Segoe UI Semibold", 26f * sc, GraphicsUnit.Pixel);
            headFont = new Font("Segoe UI Semibold", 14f * sc, GraphicsUnit.Pixel);
            rowFont = new Font("Segoe UI", 14f * sc, GraphicsUnit.Pixel);
            descFont = new Font("Segoe UI", 12f * sc, GraphicsUnit.Pixel);
            Font = rowFont;
            ClientSize = new Size(S(940), S(660));
            MinimumSize = new Size(S(760), S(480));

            content = new Panel();
            content.Dock = DockStyle.Fill;
            content.AutoScroll = true;
            content.BackColor = Theme.Window;
            Controls.Add(content);

            side = new Panel();
            side.Dock = DockStyle.Left;
            side.Width = S(232);
            side.BackColor = Theme.Side;
            Controls.Add(side);

            var logo = new Label();
            logo.Text = "StayBar"; logo.Font = titleFont; logo.ForeColor = Theme.Text; logo.BackColor = Theme.Side; logo.AutoSize = true;
            logo.Location = new Point(S(22), S(22));
            side.Controls.Add(logo);
            var ver = new Label();
            ver.Text = "Version " + Program.Version; ver.Font = descFont; ver.ForeColor = Theme.SubText; ver.BackColor = Theme.Side; ver.AutoSize = true;
            ver.Location = new Point(S(24), S(56));
            side.Controls.Add(ver);
            int ny = S(98);
            foreach (string[] p in Pages)
            {
                var b = new NavButton(p[1], p[0]);
                b.Font = rowFont;
                b.SetBounds(S(10), ny, S(212), S(42));
                string name = p[0];
                b.Click += delegate { ShowPage(name); };
                side.Controls.Add(b);
                nav.Add(b);
                ny += S(46);
            }
            var gal = new PillButton("Widget gallery", true);
            gal.Font = rowFont;
            gal.SetBounds(S(20), ny + S(16), S(192), S(38));
            gal.Click += delegate { bar.OpenGallery(); };
            side.Controls.Add(gal);
            content.Resize += delegate { if (page != null && host != null && Math.Abs(host.Width - (content.ClientSize.Width)) > S(4)) ShowPage(page); };
            ShowPage(startPage ?? "Dock");
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetDwm(Handle, 20, 1);   // dark title bar
            Native.SetDwm(Handle, 38, 2);   // Mica, where available
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try { Native.SetWindowTheme(content.Handle, "DarkMode_Explorer", null); } catch { }
        }

        public void RefreshWidgets() { if (page == "Widgets") ShowPage(page); }
        public void RefreshPage(string name) { if (page == name) ShowPage(page); }
        // --selftest: the whole page as one picture, however long it is
        public Bitmap RenderPage()
        {
            var bmp = new Bitmap(host.Width, host.Height);
            host.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
            return bmp;
        }

        public void ShowPage(string name)
        {
            page = name;
            foreach (NavButton b in nav) { b.Selected = b.Text == name; b.Invalidate(); }
            int scroll = -content.AutoScrollPosition.Y;
            content.SuspendLayout();
            Control old = host;
            host = new Panel();
            host.BackColor = Theme.Window;
            host.Location = new Point(0, 0);
            host.Width = content.ClientSize.Width;
            cardW = host.Width - S(64);
            y = S(26);
            string[] info = name.StartsWith("w:") ? WidgetPageInfo(name.Substring(2)) : Pages.First(p => p[0] == name);
            AddText(info[0], pageFont, Theme.Text, S(30), y); y += S(42);
            AddText(info[2], descFont, Theme.SubText, S(32), y); y += S(34);
            switch (name)
            {
                case "Dock": BuildDock(); break;
                case "Appearance": BuildAppearance(); break;
                case "Widgets": BuildWidgets(); break;
                case "Apps": BuildApps(); break;
                case "Profiles": BuildProfiles(); break;
                case "Automations": BuildAutomations(); break;
                case "Screens": BuildScreens(); break;
                case "Behavior": BuildBehavior(); break;
                default:
                    if (name.StartsWith("w:")) BuildWidgetPage(name.Substring(2));
                    else BuildGeneral();
                    break;
            }
            host.Height = y + S(30);
            content.Controls.Add(host);
            if (old != null) { content.Controls.Remove(old); old.Dispose(); }
            content.ResumeLayout();
            if (old != null && scroll > 0 && name == page) content.AutoScrollPosition = new Point(0, scroll);
        }

        void Apply() { bar.ApplyConfig(); }

        // ---------- a widget's own settings page ("w:<key>") ----------

        Widget current;
        bool optSection;

        static string[] WidgetPageInfo(string key)
        {
            Widget w = Widgets.Get(key);
            if (w == null) return new[] { "Widget", "", "This widget isn't available any more." };
            int hash = key.IndexOf('#');
            string title = w.Name + (hash > 0 ? "  (copy " + key.Substring(hash + 1) + ")" : "");
            return new[] { title, "", w.Desc };
        }

        void BuildWidgetPage(string key)
        {
            Widget w = Widgets.Get(key);
            if (w == null) return;
            current = w;
            optSection = false;
            if (w.Sizable || w.Styles != null || w.HasLabel) Section("Look");
            if (w.Sizable)
            {
                string[] sizes = { "s", "m", "l" };
                var sz = new Segmented(new[] { "Small", "Medium", "Large" }, Math.Max(0, Array.IndexOf(sizes, w.SizeOpt)));
                sz.Size = new Size(S(250), S(34));
                sz.Changed += delegate { w.SetOpt("size", sizes[sz.Selected]); Apply(); };
                Row("Size", "Small uses its compact layout; large gives it more room", sz);
            }
            if (w.Styles != null)
            {
                string[] styles = w.Styles;
                var st = new Segmented(styles, Math.Max(0, Array.IndexOf(styles, w.Style)));
                st.Size = new Size(Math.Min(S(420), S(30) + styles.Length * S(118)), S(34));
                st.Changed += delegate { w.SetOpt("style", styles[st.Selected]); Apply(); };
                Row("Style", null, st);
            }
            if (w.HasLabel) Row("Show its label", "The small caption text", Toggle(w.ShowLabel, v => w.SetOpt("label", v ? "true" : "false")));

            if (!(w is StackWidget))
            {
                Section("Click");
                string kind, arg;
                Actions.Split(w.Opt("click", ""), out kind, out arg);
                var ck = new Segmented(Actions.KindNames, Math.Max(0, Array.IndexOf(Actions.Kinds, kind)));
                ck.Size = new Size(S(420), S(34));
                var target = new TextBox();
                target.BorderStyle = BorderStyle.FixedSingle;
                target.BackColor = Theme.Field; target.ForeColor = Theme.Text; target.Font = descFont;
                target.Width = S(300);
                target.Text = arg;
                var browse = new PillButton("Browse", false);
                browse.Font = descFont;
                browse.Size = new Size(S(84), S(30));
                Action save = delegate
                {
                    string k = Actions.Kinds[ck.Selected];
                    w.SetOpt("click", k.Length == 0 || target.Text.Trim().Length == 0 ? "" : k + ":" + target.Text.Trim());
                };
                ck.Changed += delegate { save(); };
                target.Leave += delegate { save(); };
                target.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; save(); } };
                browse.Click += delegate
                {
                    string k = Actions.Kinds[ck.Selected];
                    if (k == "folder")
                        using (var d = new FolderBrowserDialog()) { if (d.ShowDialog(this) == DialogResult.OK) target.Text = d.SelectedPath; }
                    else
                        using (var d = new OpenFileDialog()) { d.Filter = "Apps and files|*.*"; if (d.ShowDialog(this) == DialogResult.OK) target.Text = d.FileName; }
                    if (ck.Selected == 0) ck.Selected = 1;
                    ck.Invalidate();
                    save();
                };
                Row("When you click it", "Default keeps what it normally does. A command runs hidden (cmd /c)", ck);
                var line = new Panel();
                line.BackColor = Theme.Card;
                line.Size = new Size(S(392), S(32));
                target.Location = new Point(0, (S(32) - target.PreferredHeight) / 2);
                browse.Location = new Point(S(308), S(1));
                line.Controls.Add(target); line.Controls.Add(browse);
                Row("Open or run", "A path, a web address or a command line", line);
            }

            w.Options(this);

            Section("On the bar");
            if (w.CanStack && !(w is StackWidget))
            {
                var others = Config.AllWidgetKeys().Where(k => k != w.Key && !Config.Zones.Any(z => z.Contains("stack:" + k) || z.Any(it => it.StartsWith("stack:") && Config.WidgetKeys(it).Contains(k))))
                                   .Select(k => Widgets.Get(k)).Where(o => o != null && o.CanStack && Config.Zones.Any(z => z.Contains(o.Key))).ToList();
                if (others.Count > 0 && Config.Zones.Any(z => z.Contains(w.Key)))
                {
                    var combo = new ComboBox();
                    combo.DropDownStyle = ComboBoxStyle.DropDownList;
                    combo.FlatStyle = FlatStyle.Flat;
                    combo.BackColor = Theme.Field; combo.ForeColor = Theme.Text; combo.Font = descFont;
                    combo.Width = S(200);
                    foreach (Widget o in others) combo.Items.Add(BarForm.ItemName(o.Key));
                    combo.SelectedIndex = 0;
                    var go = new PillButton("Stack", false);
                    go.Font = descFont;
                    go.Size = new Size(S(84), S(30));
                    go.Click += delegate { bar.StackWith(w.Key, others[combo.SelectedIndex].Key); ShowPage("Widgets"); };
                    var line = new Panel();
                    line.BackColor = Theme.Card;
                    line.Size = new Size(S(292), S(32));
                    combo.Location = new Point(0, S(4)); go.Location = new Point(S(208), S(1));
                    line.Controls.Add(combo); line.Controls.Add(go);
                    Row("Stack with", "Two small widgets in one tile, one above the other. In edit mode you can also drop one onto another", line);
                }
            }
            if (w.Multi) Row("Add another copy", "Each copy has its own settings", Button("Add", () => bar.AddCopy(w)));
            Row("Remove from the bar", null, Button("Remove", () => { bar.RemoveFromBar(w.Key); ShowPage("Widgets"); }));
        }

        void OptSection()
        {
            if (optSection) return;
            optSection = true;
            Section(current.Name + " options");
        }

        Widget IWidgetOptions.Target { get { return current; } }
        void IWidgetOptions.Section(string title) { optSection = true; Section(title); }

        void IWidgetOptions.Note(string text)
        {
            OptSection();
            Label l = AddText(text, descFont, Theme.SubText, S(34), y);
            y += l.GetPreferredSize(new Size(cardW, 0)).Height + S(10);
        }

        void IWidgetOptions.Text(string title, string desc, string opt, string def)
        {
            OptSection();
            Widget w = current;
            var tb = new TextBox();
            tb.BorderStyle = BorderStyle.FixedSingle;
            tb.BackColor = Theme.Field; tb.ForeColor = Theme.Text; tb.Font = descFont;
            tb.Width = S(300);
            tb.Text = w.Opt(opt, def);
            Action save = delegate { if (tb.Text != w.Opt(opt, def)) { w.SetOpt(opt, tb.Text.Trim()); Apply(); } };
            tb.Leave += delegate { save(); };
            tb.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; save(); } };
            Row(title, desc, tb);
        }

        void IWidgetOptions.Number(string title, string desc, string opt, int min, int max, int def, string unit)
        {
            OptSection();
            Widget w = current;
            Row(title, desc, SliderWith(min, max, Math.Max(min, Math.Min(max, w.OptInt(opt, def))), unit, v => w.SetOpt(opt, v.ToString())));
        }

        void IWidgetOptions.Choice(string title, string desc, string opt, string[] values, string[] labels, string def)
        {
            OptSection();
            Widget w = current;
            int sel = Math.Max(0, Array.IndexOf(values, w.Opt(opt, def)));
            if (values.Length <= 4)
            {
                var sg = new Segmented(labels, sel);
                sg.Size = new Size(Math.Min(S(470), S(20) + labels.Length * S(110)), S(34));
                sg.Changed += delegate { w.SetOpt(opt, values[sg.Selected]); Apply(); };
                Row(title, desc, sg);
                return;
            }
            var combo = new ComboBox();
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.FlatStyle = FlatStyle.Flat;
            combo.BackColor = Theme.Field; combo.ForeColor = Theme.Text; combo.Font = descFont;
            combo.Width = S(300);
            combo.DropDownWidth = S(420);
            combo.Items.AddRange(labels);
            combo.SelectedIndex = sel;
            combo.SelectedIndexChanged += delegate { w.SetOpt(opt, values[combo.SelectedIndex]); Apply(); };
            Row(title, desc, combo);
        }

        void IWidgetOptions.Check(string title, string desc, string opt, bool def)
        {
            OptSection();
            Widget w = current;
            Row(title, desc, Toggle(w.OptBool(opt, def), v => w.SetOpt(opt, v ? "true" : "false")));
        }

        void IWidgetOptions.Button(string title, string desc, string text, Action click)
        {
            OptSection();
            Row(title, desc, Button(text, click));
        }

        // ---------- pages ----------

        void BuildDock()
        {
            Section("Layout");
            var style = new Segmented(new[] { "Tiles bar", "Floating dock" }, Config.IsDock ? 1 : 0);
            style.Size = new Size(S(250), S(34));
            style.Changed += delegate { Config.Style = style.Selected == 1 ? "dock" : "tiles"; Apply(); };
            Row("Style", "A full-width bar of tiles, or a floating glass dock", style);
            var pos = new Segmented(new[] { "Bottom", "Top" }, Config.AtTop ? 1 : 0);
            pos.Size = new Size(S(180), S(34));
            pos.Changed += delegate { Config.AtTop = pos.Selected == 1; Apply(); };
            Row("Position", null, pos);
            Row("Center the apps", "Like Windows 11; widgets stay at the edges", Toggle(Config.CenterApps, v => Config.CenterApps = v));
            Row("Recycle Bin at the end of the dock", "Floating dock only: a divider and the Recycle Bin close the dock, like on a Mac", Toggle(Config.DockTrash, v => Config.DockTrash = v));

            Section("Size");
            int preset = Config.IconSize <= 25 ? 0 : Config.IconSize <= 33 ? 1 : 2;
            var size = new Segmented(new[] { "Small", "Medium", "Large" }, preset);
            size.Size = new Size(S(250), S(34));
            size.Changed += delegate { Config.IconSize = new[] { 24, 30, 38 }[size.Selected]; Apply(); ShowPage(page); };
            Row("Size", null, size);
            Row("Icon size", "Fine-tune the size", SliderWith(20, 44, Config.IconSize, "px", v => Config.IconSize = v));

            Section("Size & shape");
            string[] modes = { "auto", "full", "percent", "fixed", "fit" };
            var wm = new Segmented(new[] { "Auto", "Full", "Percent", "Fixed", "Fit" }, Math.Max(0, Array.IndexOf(modes, Config.WidthMode)));
            wm.Size = new Size(S(390), S(34));
            wm.Changed += delegate { Config.WidthMode = modes[wm.Selected]; Apply(); };
            Row("Width", "Auto: the tiles bar fills the screen, the dock fits its content", wm);
            Row("Width in percent", "When the width is Percent", SliderWith(20, 100, Config.WidthPct, "%", v => Config.WidthPct = v));
            Row("Width in pixels", "When the width is Fixed", SliderWith(300, 3000, Config.WidthPx, "px", v => Config.WidthPx = v));
            string[] aligns = { "left", "center", "right" };
            var al = new Segmented(new[] { "Left", "Center", "Right" }, Math.Max(0, Array.IndexOf(aligns, Config.Align)));
            al.Size = new Size(S(250), S(34));
            al.Changed += delegate { Config.Align = aligns[al.Selected]; Apply(); };
            Row("Alignment", "Where a bar narrower than the screen sits", al);
            Row("Offset", "Moves it away from its edge (or off the center)", SliderWith(-800, 800, Config.OffsetX, "px", v => Config.OffsetX = v));
            string[] floats = { "auto", "on", "off" };
            var fl = new Segmented(new[] { "Auto", "Floating", "Attached" }, Math.Max(0, Array.IndexOf(floats, Config.FloatMode)));
            fl.Size = new Size(S(300), S(34));
            fl.Changed += delegate { Config.FloatMode = floats[fl.Selected]; Apply(); };
            Row("Floating", "Floating leaves a gap all around; attached touches the screen edge. Auto: the dock floats, the tiles bar attaches", fl);
            Row("Gap from the screen edge", "When floating", SliderWith(0, 40, Config.Margin, "px", v => Config.Margin = v));
            Row("Bar height", "0 = automatic (follows the icon size)", SliderWith(0, 120, Config.BarHeight, "px", v => Config.BarHeight = v));
            Row("Space between tiles", null, SliderWith(0, 24, Config.TileGap, "px", v => Config.TileGap = v));
            Row("Tile padding", "Space above and below the tiles (and around the dock)", SliderWith(0, 20, Config.TilePad >= 0 ? Config.TilePad : (Config.IsDock ? 6 : 5), "px", v => Config.TilePad = v));

            Section("Motion");
            Row("Magnify icons on hover", "Icons grow in a smooth wave under the mouse", Toggle(Config.Magnify, v => Config.Magnify = v));
            Row("Animations", "Sliding tiles, bouncing launches, fades", Toggle(Config.Animations, v => Config.Animations = v));
        }

        void BuildAppearance()
        {
            Section("Theme");
            int cols = 3, gap = S(12);
            int cw = (cardW - gap * (cols - 1)) / cols, ch = S(132);
            var cards = new List<ThemeCard>();
            for (int i = 0; i < Palette.All.Count; i++)
            {
                Palette p = Palette.All[i];
                var c = new ThemeCard(p, sc);
                c.Font = rowFont;
                c.Selected = Config.ThemeId == p.Id;
                c.SetBounds(S(30) + (i % cols) * (cw + gap), y + (i / cols) * (ch + gap), cw, ch);
                c.Click += delegate { Config.ThemeId = p.Id; Apply(); foreach (ThemeCard o in cards) { o.Selected = o == c; o.Invalidate(); } };
                cards.Add(c);
                host.Controls.Add(c);
            }
            y += ((Palette.All.Count + cols - 1) / cols) * (ch + gap) + S(6);
            var blurb = AddText(Palette.Get(Config.ThemeId).Blurb, descFont, Theme.SubText, S(32), y);
            y += S(30);

            Section("Accent color");
            var mode = new Segmented(new[] { "Pick a color", "Match wallpaper", "Windows accent" }, Config.AccentMode == "wallpaper" ? 1 : Config.AccentMode == "windows" ? 2 : 0);
            mode.Size = new Size(S(390), S(34));
            mode.Changed += delegate
            {
                Config.AccentMode = mode.Selected == 1 ? "wallpaper" : mode.Selected == 2 ? "windows" : "custom";
                if (Config.AccentMode == "wallpaper") Bg.Run("wallpaper.pick", Svc.WallpaperAccent);
                Apply(); RepaintAll(this);
            };
            Row("Accent", "\"Match wallpaper\" picks the most vivid color of your desktop picture", mode);
            var sw = new Panel();
            sw.BackColor = Theme.Card;
            sw.Size = new Size(Config.Accents.Length * S(30), S(30));
            var swatches = new List<Swatch>();
            for (int i = 0; i < Config.Accents.Length; i++)
            {
                var s = new Swatch();
                s.Fill = Config.Accents[i];
                s.Selected = Config.AccentMode == "custom" && Config.Accents[i].ToArgb() == Config.AccentCustom.ToArgb();
                s.SetBounds(i * S(30), 0, S(29), S(30));
                s.Click += delegate
                {
                    Config.AccentCustom = s.Fill; Config.AccentMode = "custom";
                    foreach (Swatch o in swatches) { o.Selected = o == s; o.Invalidate(); }
                    Apply(); ShowPage(page);
                };
                swatches.Add(s);
                sw.Controls.Add(s);
            }
            Row("Colors", null, sw);

            Section("Glass");
            Row("Glass blur", "See your wallpaper, blurred, through the bar", Toggle(Config.Blur, v => Config.Blur = v));
            Row("Glass darkness", "How much the glass is tinted", SliderWith(0, 100, Config.Darkness, "%", v => Config.Darkness = v));
            Row("Corner roundness", "0 = square tiles", SliderWith(0, 22, Config.Radius, "px", v => Config.Radius = v));
        }

        void BuildWidgets()
        {
            Section("On your bar");
            var addBtn = new PillButton("Open the widget gallery", true);
            addBtn.Font = rowFont;
            addBtn.Size = new Size(S(230), S(36));
            addBtn.Click += delegate { bar.OpenGallery(); };
            Row("Add widgets", "Browse every widget with a live preview. Or drag tiles on the bar to reorder them; drag one off the bar to remove it.", addBtn);
            for (int z = 0; z < 3; z++) ZoneList(z);

            Section("Weather");
            var city = new TextBox();
            city.BorderStyle = BorderStyle.FixedSingle;
            city.BackColor = Theme.Field; city.ForeColor = Theme.Text; city.Font = rowFont;
            city.Text = Config.WeatherCity.Length > 0 ? Config.WeatherCity : "";
            city.Width = S(170);
            var save = new PillButton("Save", false);
            save.Font = rowFont;
            save.Size = new Size(S(70), S(32));
            Action commit = delegate
            {
                Config.WeatherCity = city.Text.Trim();
                Settings.Set("weather.geo", "");
                Weather.Now = null;
                Apply();
                Weather.Refresh();
            };
            save.Click += delegate { commit(); };
            city.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; commit(); } };
            var cityHost = new Panel();
            cityHost.BackColor = Theme.Card;
            cityHost.Size = new Size(S(250), S(34));
            city.Location = new Point(0, (S(34) - city.PreferredHeight) / 2);
            save.Location = new Point(S(180), S(1));
            cityHost.Controls.Add(city); cityHost.Controls.Add(save);
            Row("City", "Empty = " + Weather.DefaultCity() + " (from your Windows region)", cityHost);
            Row("Fahrenheit", null, Toggle(Config.Fahrenheit, v => { Config.Fahrenheit = v; Weather.Refresh(); }));

            Section("World clocks");
            var zones = WorldClockWidget.Zones();
            var all = TimeZoneInfo.GetSystemTimeZones();
            for (int i = 0; i < 3; i++)
            {
                var name = new TextBox();
                name.BorderStyle = BorderStyle.FixedSingle;
                name.BackColor = Theme.Field; name.ForeColor = Theme.Text; name.Font = descFont;
                name.Width = S(110);
                var combo = new ComboBox();
                combo.DropDownStyle = ComboBoxStyle.DropDownList;
                combo.FlatStyle = FlatStyle.Flat;
                combo.BackColor = Theme.Field; combo.ForeColor = Theme.Text; combo.Font = descFont;
                combo.Width = S(250);
                combo.DropDownWidth = S(420);
                combo.Items.Add("(none)");
                foreach (TimeZoneInfo z in all) combo.Items.Add(z.DisplayName);
                combo.SelectedIndex = 0;
                if (i < zones.Count)
                {
                    name.Text = zones[i].Key;
                    int idx = all.ToList().FindIndex(z => z.Id == zones[i].Value.Id);
                    if (idx >= 0) combo.SelectedIndex = idx + 1;
                }
                var line = new Panel();
                line.BackColor = Theme.Card;
                line.Size = new Size(S(370), S(30));
                name.Location = new Point(0, 2); combo.Location = new Point(S(118), 2);
                line.Controls.Add(name); line.Controls.Add(combo);
                name.Tag = combo;
                clockRows.Add(name);
                name.Leave += delegate { SaveClocks(all); };
                combo.SelectedIndexChanged += delegate(object s, EventArgs e)
                {
                    var tb = clockRows.First(r => r.Tag == s);
                    if (tb.Text.Trim().Length == 0 && ((ComboBox)s).SelectedIndex > 0)
                    {
                        string dn = all[((ComboBox)s).SelectedIndex - 1].DisplayName;
                        int close = dn.IndexOf(')');
                        tb.Text = (close >= 0 ? dn.Substring(close + 1) : dn).Split(',')[0].Trim();
                    }
                    SaveClocks(all);
                };
                Row("Clock " + (i + 1), i == 0 ? "A name, then the time zone" : null, line);
            }

            Section("Focus timer");
            Row("Focus length", null, SliderWith(5, 90, Config.FocusMin, " min", v => Config.FocusMin = v));
            Row("Break length", null, SliderWith(1, 30, Config.BreakMin, " min", v => Config.BreakMin = v));

            Section("Clipboard history");
            Row("Keep a clipboard history", "Text only. Password managers are skipped automatically.", Toggle(Config.ClipboardOn, v => Config.ClipboardOn = v));
            Row("Remember after restart", "Off = history lives in memory only", Toggle(Config.ClipboardKeep, v => { Config.ClipboardKeep = v; Clip.Save(); }));
        }

        readonly List<TextBox> clockRows = new List<TextBox>();

        void SaveClocks(System.Collections.ObjectModel.ReadOnlyCollection<TimeZoneInfo> all)
        {
            var parts = new List<string>();
            foreach (TextBox tb in clockRows)
            {
                var combo = (ComboBox)tb.Tag;
                if (combo.SelectedIndex <= 0) continue;
                string n = tb.Text.Trim().Replace("|", "").Replace(";", "");
                if (n.Length == 0) n = "Clock";
                parts.Add(n + "|" + all[combo.SelectedIndex - 1].Id);
            }
            Config.WorldClocks = string.Join(";", parts.ToArray());
            Apply();
        }

        // One zone of the bar: its items with move up / down, move to the zone left / right, and remove.
        void ZoneList(int zone)
        {
            List<string> ids = Config.Zones[zone];
            string[] titles = { "Left zone", "Center zone", "Right zone" };
            AddText(titles[zone], descFont, Theme.SubText, S(34), y); y += S(24);
            if (ids.Count == 0) { AddText("Nothing here yet.", descFont, Theme.SubText, S(34), y); y += S(28); }
            for (int i = 0; i < ids.Count; i++)
            {
                string item = ids[i];
                int index = i;
                Widget w = Config.IsSpecial(item) && !item.StartsWith("stack:") ? null : Widgets.Get(item);
                if ((!Config.IsSpecial(item) || item.StartsWith("stack:")) && w == null) continue;
                var tools = new Panel();
                tools.BackColor = Theme.Card;
                tools.Size = new Size(S(216), S(32));
                var gear = new IconButton("\uE713"); gear.SetBounds(S(180), 0, S(32), S(32)); gear.Enabled = w != null || item.StartsWith("stack:");
                gear.Click += delegate { ShowPage("w:" + item); };
                tools.Controls.Add(gear);
                var up = new IconButton("\uE70E"); up.SetBounds(0, 0, S(32), S(32)); up.Enabled = i > 0;
                var down = new IconButton("\uE70D"); down.SetBounds(S(36), 0, S(32), S(32)); down.Enabled = i < ids.Count - 1;
                var toLeft = new IconButton("\uE76B"); toLeft.SetBounds(S(72), 0, S(32), S(32)); toLeft.Enabled = zone > 0;
                var toRight = new IconButton("\uE76C"); toRight.SetBounds(S(108), 0, S(32), S(32)); toRight.Enabled = zone < 2;
                var del = new IconButton("\uE711"); del.SetBounds(S(144), 0, S(32), S(32)); del.Color0 = Color.FromArgb(251, 113, 133);
                up.Click += delegate { if (index > 0) { bar.PushUndo(); ids.RemoveAt(index); ids.Insert(index - 1, item); Apply(); ShowPage(page); } };
                down.Click += delegate { if (index < ids.Count - 1) { bar.PushUndo(); ids.RemoveAt(index); ids.Insert(index + 1, item); Apply(); ShowPage(page); } };
                toLeft.Click += delegate { bar.PushUndo(); ids.RemoveAt(index); Config.Zones[zone - 1].Add(item); Apply(); ShowPage(page); };
                toRight.Click += delegate { bar.PushUndo(); ids.RemoveAt(index); Config.Zones[zone + 1].Insert(0, item); Apply(); ShowPage(page); };
                del.Click += delegate { bar.PushUndo(); ids.RemoveAt(index); Apply(); ShowPage(page); };
                tools.Controls.Add(up); tools.Controls.Add(down); tools.Controls.Add(toLeft); tools.Controls.Add(toRight); tools.Controls.Add(del);
                string title = BarForm.ItemName(item) + (w != null && !w.Available ? "  (not on this PC)" : "");
                Row(title, null, tools, S(46));
            }
            var add = new Panel();
            add.BackColor = Theme.Card;
            add.Size = new Size(S(250), S(34));
            var sp = new PillButton("Spacer", false); sp.Font = descFont; sp.SetBounds(0, S(1), S(120), S(32));
            var sep = new PillButton("Separator", false); sep.Font = descFont; sep.SetBounds(S(128), S(1), S(120), S(32));
            sp.Click += delegate { bar.PushUndo(); ids.Add("spacer"); Apply(); ShowPage(page); };
            sep.Click += delegate { bar.PushUndo(); ids.Add("sep"); Apply(); ShowPage(page); };
            add.Controls.Add(sp); add.Controls.Add(sep);
            Row("Add to the " + titles[zone].ToLowerInvariant(), "A spacer takes the free room (resize it in edit mode); a separator is a thin line", add);
            y += S(8);
        }

        void BuildApps()
        {
            Section("Apps on the bar");
            string[] combines = { "always", "whenfull", "never" };
            var cm = new Segmented(new[] { "Always", "When full", "Never" }, Math.Max(0, Array.IndexOf(combines, Config.Combine)));
            cm.Size = new Size(S(330), S(34));
            cm.Changed += delegate { Config.Combine = combines[cm.Selected]; Apply(); };
            Row("Combine windows", "Never: one tile per window. When full: one tile per window until the bar runs out of room", cm);
            Row("Show window titles", "A label next to each icon (the window title, or the app's name)", Toggle(Config.AppLabels, v => Config.AppLabels = v));
            var show = new Segmented(new[] { "Pinned + running", "Running only" }, Config.RunningOnly ? 1 : 0);
            show.Size = new Size(S(330), S(34));
            show.Changed += delegate { Config.RunningOnly = show.Selected == 1; Apply(); };
            Row("Show", "Running only hides pinned apps until you open them", show);
            Row("Only this screen's windows", "With a bar on more than one screen, each bar shows the windows on its own screen", Toggle(Config.ThisScreen, v => Config.ThisScreen = v));

            Section("Hidden apps");
            if (Config.HiddenApps.Count == 0)
            {
                AddText("Nothing hidden. Right-click an app on the bar > Name and icon > Hide from the bar.", descFont, Theme.SubText, S(34), y);
                y += S(30);
            }
            foreach (string key in Config.HiddenApps.ToList())
            {
                string k = key;
                Row(AppTitle(k), k, Button("Show", () => { Config.HiddenApps.Remove(k); Apply(); ShowPage(page); }));
            }

            Section("Renamed apps and custom icons");
            var custom = Config.AppNamesCustom.Keys.Concat(Config.AppIconsCustom.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (custom.Count == 0)
            {
                AddText("None yet. Right-click an app on the bar > Name and icon.", descFont, Theme.SubText, S(34), y);
                y += S(30);
            }
            foreach (string key in custom)
            {
                string k = key, name;
                string desc = (Config.AppNamesCustom.TryGetValue(k, out name) ? "Shown as \"" + name + "\"" : "") +
                              (Config.AppIconsCustom.ContainsKey(k) ? (Config.AppNamesCustom.ContainsKey(k) ? ", custom icon" : "Custom icon") : "");
                Row(AppTitle(k), desc, Button("Reset", () => { Config.SetAppName(k, null); Config.SetAppIcon(k, null); Apply(); ShowPage(page); }));
            }
        }

        // ---------- profiles ----------

        void BuildProfiles()
        {
            Section("Your profiles");
            Label intro = AddText("A profile is the whole bar: layout, widgets, look and behavior. Win+Alt+P switches to the next one; Win+Alt+1 … 4 picks one. Automations can switch them for you.", descFont, Theme.SubText, S(34), y);
            y += intro.GetPreferredSize(new Size(cardW, 0)).Height + S(12);
            List<string> names = Profiles.Names();
            string cur = Profiles.Current;
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                var tools = new Panel();
                tools.BackColor = Theme.Card;
                tools.Size = new Size(S(340), S(32));
                string[] labels = { "Use", "Rename", "Export", "Delete" };
                for (int b = 0; b < labels.Length; b++)
                {
                    var pb = new PillButton(labels[b], b == 0 && !name.Equals(cur, StringComparison.OrdinalIgnoreCase));
                    pb.Font = descFont;
                    pb.SetBounds(b * S(85), S(1), S(80), S(30));
                    string what = labels[b];
                    pb.Click += delegate { ProfileAction(what, name); };
                    tools.Controls.Add(pb);
                }
                bool isCur = name.Equals(cur, StringComparison.OrdinalIgnoreCase);
                Row(name + (isCur ? "  ✓" : ""), (isCur ? "In use. " : "") + (i < 4 ? "Win+Alt+" + (i + 1) : ""), tools);
            }
            var save = new Panel();
            save.BackColor = Theme.Card;
            save.Size = new Size(S(300), S(34));
            var tb = new TextBox();
            tb.BorderStyle = BorderStyle.FixedSingle;
            tb.BackColor = Theme.Field; tb.ForeColor = Theme.Text; tb.Font = rowFont;
            tb.Width = S(200);
            tb.Location = new Point(0, (S(34) - tb.PreferredHeight) / 2);
            var go = new PillButton("Save", false);
            go.Font = rowFont;
            go.SetBounds(S(210), S(1), S(88), S(32));
            Action doSave = delegate
            {
                if (tb.Text.Trim().Length == 0) return;
                Profiles.SaveAs(tb.Text.Trim());
                ShowPage(page);
            };
            go.Click += delegate { doSave(); };
            tb.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; doSave(); } };
            save.Controls.Add(tb); save.Controls.Add(go);
            Row("Save the current bar as a profile", "Saving under an existing name updates it", save);
            Row("Import a profile", "A .txt file exported from StayBar", Button("Import…", () =>
            {
                using (var d = new OpenFileDialog())
                {
                    d.Filter = "StayBar profile|*.txt|All files|*.*";
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    try { Profiles.Import(d.FileName); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "StayBar"); }
                    ShowPage(page);
                }
            }));
            Row("Profile files", "Each profile is a plain text file you can edit", Button("Open folder", () => { System.IO.Directory.CreateDirectory(Profiles.Dir); Process.Start("explorer.exe", "\"" + Profiles.Dir + "\""); }));
        }

        void ProfileAction(string what, string name)
        {
            switch (what)
            {
                case "Use": Profiles.Apply(name, true); break;
                case "Rename":
                    string to = InputBox.Ask("Rename profile", "New name:", name, sc);
                    if (!string.IsNullOrWhiteSpace(to)) { try { Profiles.Rename(name, to); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "StayBar"); } }
                    break;
                case "Export":
                    using (var d = new SaveFileDialog())
                    {
                        d.Filter = "StayBar profile|*.txt";
                        d.FileName = name + ".txt";
                        if (d.ShowDialog(this) == DialogResult.OK) { try { Profiles.Export(name, d.FileName); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "StayBar"); } }
                    }
                    break;
                case "Delete":
                    if (MessageBox.Show(this, "Delete the profile \"" + name + "\"?", "StayBar", MessageBoxButtons.YesNo) == DialogResult.Yes) Profiles.Delete(name);
                    break;
            }
            if (!IsDisposed) ShowPage(page);
        }

        // ---------- automations ----------

        void BuildAutomations()
        {
            Section("Rules");
            Label intro = AddText("When something happens, StayBar changes the bar, and changes it back when it stops.", descFont, Theme.SubText, S(34), y);
            y += intro.GetPreferredSize(new Size(cardW, 0)).Height + S(12);
            List<Rule> rules = Automations.Rules;
            if (rules.Count == 0) { AddText("No rules yet.", descFont, Theme.SubText, S(34), y); y += S(28); }
            foreach (Rule r in rules.ToList())
            {
                Rule rule = r;
                var tools = new Panel();
                tools.BackColor = Theme.Card;
                tools.Size = new Size(S(96), S(32));
                var sw = new ToggleSwitch();
                sw.SetBounds(0, S(5), S(44), S(22));
                sw.Checked = rule.On;
                sw.Changed += delegate { Automations.SetOn(rule, sw.Checked); };
                var del = new IconButton("\uE711"); del.SetBounds(S(60), 0, S(32), S(32)); del.Color0 = Color.FromArgb(251, 113, 133);
                del.Click += delegate { Automations.Remove(rule); ShowPage(page); };
                tools.Controls.Add(sw); tools.Controls.Add(del);
                Row(Automations.Describe(rule), rule.Active ? "Active right now" : null, tools);
            }

            Section("Add a rule");
            ComboBox when = Combo(Automations.WhenKinds.Select(k => k[1]).ToArray(), S(250));
            TextBox whenArg = Field(S(150));
            ComboBox act = Combo(Automations.DoKinds.Select(k => k[1]).ToArray(), S(250));
            TextBox actArg = Field(S(150));
            Action cues = delegate
            {
                Native.SendCue(whenArg.Handle, Automations.WhenKinds[when.SelectedIndex][2]);
                Native.SendCue(actArg.Handle, Automations.DoKinds[act.SelectedIndex][2]);
                whenArg.Enabled = Automations.WhenKinds[when.SelectedIndex][2].Length > 0;
                actArg.Enabled = Automations.DoKinds[act.SelectedIndex][2].Length > 0;
            };
            when.SelectedIndexChanged += delegate { cues(); };
            act.SelectedIndexChanged += delegate { cues(); };
            Row("When", "Time: like 23:00-07:00. App: the program's file name, like discord.exe", Pair(when, whenArg));
            Row("Do", "Profiles: " + string.Join(", ", Profiles.Names().ToArray()) + ". Widgets: their id, like cpu or weather", Pair(act, actArg));
            Row("", null, Button("Add rule", () =>
            {
                string wk = Automations.WhenKinds[when.SelectedIndex][0], dk = Automations.DoKinds[act.SelectedIndex][0];
                string w = wk + (whenArg.Enabled && whenArg.Text.Trim().Length > 0 ? ":" + whenArg.Text.Trim() : "");
                string d = dk + (actArg.Enabled && actArg.Text.Trim().Length > 0 ? ":" + actArg.Text.Trim() : "");
                if (whenArg.Enabled && whenArg.Text.Trim().Length == 0) { MessageBox.Show(this, "That condition needs a value (the grey hint shows an example).", "StayBar"); return; }
                if (actArg.Enabled && actArg.Text.Trim().Length == 0 && dk != "autohide") { MessageBox.Show(this, "That action needs a value (the grey hint shows an example).", "StayBar"); return; }
                if (dk == "autohide" && !d.Contains(":")) d = "autohide:on";
                Automations.Add(w, d);
                ShowPage(page);
            }));
            BeginInvoke(new Action(delegate { if (!when.IsDisposed) cues(); }));
        }

        ComboBox Combo(string[] items, int width)
        {
            var c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.FlatStyle = FlatStyle.Flat;
            c.BackColor = Theme.Field; c.ForeColor = Theme.Text; c.Font = descFont;
            c.Width = width;
            c.Items.AddRange(items);
            c.SelectedIndex = 0;
            return c;
        }

        TextBox Field(int width)
        {
            var t = new TextBox();
            t.BorderStyle = BorderStyle.FixedSingle;
            t.BackColor = Theme.Field; t.ForeColor = Theme.Text; t.Font = descFont;
            t.Width = width;
            return t;
        }

        Panel Pair(Control a, Control b)
        {
            var p = new Panel();
            p.BackColor = Theme.Card;
            p.Size = new Size(a.Width + S(8) + b.Width, S(32));
            a.Location = new Point(0, (S(32) - a.Height) / 2);
            b.Location = new Point(a.Width + S(8), (S(32) - b.Height) / 2);
            p.Controls.Add(a); p.Controls.Add(b);
            return p;
        }

        // ---------- screens ----------

        void BuildScreens()
        {
            Section("Bars");
            string[] modes = { "all", "primary", "list" };
            var mode = new Segmented(new[] { "Every screen", "Main screen", "Picked screens" }, Math.Max(0, Array.IndexOf(modes, Config.Screens)));
            mode.Size = new Size(S(390), S(34));
            mode.Changed += delegate
            {
                Config.Screens = modes[mode.Selected];
                if (Config.Screens == "list" && Config.ScreenList.Count == 0)
                    foreach (Screen s in Screen.AllScreens) if (!s.Primary) Config.ScreenList.Add(ScreenIds.Of(s));
                Apply(); ShowPage(page);
            };
            Row("Show a bar on", null, mode);

            Section("Screens");
            foreach (Screen s in Screen.AllScreens.OrderBy(x => x.Primary ? 0 : 1).ThenBy(x => x.Bounds.X))
            {
                string id = s.Primary ? null : ScreenIds.Of(s);
                string title = ScreenIds.Name(s) + "  ·  " + s.Bounds.Width + " × " + s.Bounds.Height + (s.Primary ? "  ·  main screen" : "");
                if (s.Primary)
                {
                    Row(title, "Its layout is the one on the Widgets page", Button("Edit its bar", () => bar.SetEdit(true)));
                    continue;
                }
                AddText(title, headFont, Theme.Text, S(34), y); y += S(30);
                if (Config.Screens == "list")
                    Row("A bar on this screen", null, Toggle(Config.ScreenList.Contains(id), v => { if (v) Config.ScreenList.Add(id); else Config.ScreenList.Remove(id); }));
                var lay = new Segmented(new[] { "Same as the main screen", "Its own" }, Config.OwnsLayout(id) ? 1 : 0);
                lay.Size = new Size(S(390), S(34));
                lay.Changed += delegate { if (lay.Selected == 1) Config.OwnLayout(id); else Config.ShareLayout(id); Apply(); };
                Row("Layout", "Its own: edit that bar (right-click it > Edit the bar) and the changes stay on that screen", lay);
                string pos;
                Config.ScreenPos.TryGetValue(id, out pos);
                string[] poss = { "", "bottom", "top" };
                var ps = new Segmented(new[] { "Same as main", "Bottom", "Top" }, Math.Max(0, Array.IndexOf(poss, pos ?? "")));
                ps.Size = new Size(S(330), S(34));
                ps.Changed += delegate { if (ps.Selected == 0) Config.ScreenPos.Remove(id); else Config.ScreenPos[id] = poss[ps.Selected]; Settings.Set("screen." + id + ".position", poss[ps.Selected]); Apply(); };
                Row("Position", null, ps);
                BarForm b = BarForm.ForScreen(id);
                if (b != null) Row("Edit its bar", "Drag widgets in, out and around on that screen", Button("Edit", () => b.SetEdit(true)));
            }
            if (Screen.AllScreens.Length == 1)
            {
                AddText("Only one screen is connected right now. Plug in another one and its bar appears here.", descFont, Theme.SubText, S(34), y);
                y += S(30);
            }
        }

        static string AppTitle(string key)
        {
            if (key.StartsWith("aumid:")) { string a = key.Substring(6); int bang = a.IndexOf('!'); a = bang > 0 ? a.Substring(0, bang) : a; int dot = a.LastIndexOf('.'); return dot >= 0 && dot < a.Length - 1 ? a.Substring(dot + 1) : a; }
            try { return AppNames.Of(key); } catch { return key; }
        }

        void BuildBehavior()
        {
            Section("Hiding");
            string[] hides = { "always", "fullscreen", "smart", "auto" };
            var hm = new Segmented(new[] { "Never", "Fullscreen", "Smart", "Auto-hide" }, Math.Max(0, Array.IndexOf(hides, Config.HideMode)));
            hm.Size = new Size(S(390), S(34));
            hm.Changed += delegate { Config.HideMode = hides[hm.Selected]; Apply(); };
            Row("Hide the bar", "Never · only for fullscreen games and videos · while a window touches it (smart) · auto-hide", hm);
            Row("Reveal delay", "How long the mouse rests at the screen edge before the bar comes back", SliderWith(0, 1500, Config.RevealMs, " ms", v => Config.RevealMs = v));
            Row("Hide delay", "How long the bar waits before it slides away", SliderWith(100, 5000, Config.HideMs, " ms", v => Config.HideMs = v));
            Row("Reserve screen space", "On: maximized windows stop above the bar. Off: the bar floats over them (smart and auto-hide never reserve)", Toggle(Config.Reserve, v => Config.Reserve = v));
            Row("Dim when idle", "Fade the bar after this many seconds without the mouse on it (0 = never)", SliderWith(0, 120, Config.DimIdle, " s", v => Config.DimIdle = v));
            Row("Dimmed opacity", null, SliderWith(10, 100, Config.DimOpacity, "%", v => Config.DimOpacity = v));
            Row("Win key shows the bar", "With auto-hide on, pressing Win peeks at the bar", Toggle(Config.WinPeek, v => Config.WinPeek = v));
            Row("Hot corner shows the desktop", "Rest the mouse in the bottom-right corner of the screen (same as Win+D)", Toggle(Config.HotCorner, v => Config.HotCorner = v));

            Section("Keyboard");
            Row("Win+1 … Win+9", "Open or switch to the 1st…9th app on the bar. Add Shift for a new window.", Toggle(Config.WinKeys, v => Config.WinKeys = v));
            Row("Alt+Space launcher", "Fuzzy search for apps, windows, settings and math", Toggle(Config.LauncherKey, v => Config.LauncherKey = v));
            Row("Win+Alt+E, Win+Alt+P", "Edit the bar, and switch to the next profile (Win+Alt+1 … 4 picks one)", Toggle(Config.BarKeys, v => Config.BarKeys = v));

            Section("Apps");
            Row("Live previews on hover", "Hover an open app to see its windows", Toggle(Config.Previews, v => Config.Previews = v));
            Row("Pulse apps that need attention", "When an app flashes (a new message, a finished download…)", Toggle(Config.Pulse, v => Config.Pulse = v));

            Section("Clock");
            Row("Show seconds", null, Toggle(Config.Seconds, v => Config.Seconds = v));
            Row("Show date", null, Toggle(Config.ShowDate, v => Config.ShowDate = v));
            Row("24-hour clock", null, Toggle(Config.Clock24, v => Config.Clock24 = v));
        }

        void BuildGeneral()
        {
            Section("Startup");
            var startup = new ToggleSwitch();
            startup.Size = new Size(S(44), S(22));
            startup.Checked = Startup.Enabled;
            startup.Changed += delegate { try { Startup.Set(startup.Checked); } catch { } };
            Row("Start with Windows", "Comes back by itself after a restart", startup);

            Section("Apps");
            Row("Pinned apps", "Or right-click an app on the bar to pin it; drag apps to reorder; drop one onto another to make a folder", Button("Edit pins", () => bar.EditPins()));
            Row("Data folder", "Settings, pins, notes and screen time live here", Button("Open", () => Process.Start("explorer.exe", "\"" + Program.DataDir + "\"")));

            Section("StayBar");
            Row("Reset settings", "Back to the defaults (pins are kept)", Button("Reset", () => bar.ResetSettings()));
            Row("Restart StayBar", null, Button("Restart", () => bar.Restart()));
            Row("Quit", "Closes StayBar and brings back the Windows taskbar", Button("Quit", () => bar.Quit()));
            y += S(6);
            AddText("StayBar " + Program.Version + "  ·  inspired by lumabar  ·  made for Windows 11", descFont, Theme.SubText, S(32), y);
            y += S(28);
        }

        // ---------- building blocks ----------

        static void RepaintAll(Control c)
        {
            c.Invalidate();
            foreach (Control k in c.Controls) RepaintAll(k);
        }

        Label AddText(string text, Font f, Color c, int x, int yy)
        {
            var l = new Label();
            l.Text = text; l.Font = f; l.ForeColor = c; l.BackColor = Theme.Window;
            l.UseMnemonic = false; l.AutoSize = true; l.Location = new Point(x, yy);
            l.MaximumSize = new Size(cardW, 0);
            host.Controls.Add(l);
            return l;
        }

        void Section(string text)
        {
            y += S(8);
            AddText(text, headFont, Theme.SubText, S(32), y);
            y += S(30);
        }

        void Row(string title, string desc, Control ctrl) { Row(title, desc, ctrl, 0); }

        void Row(string title, string desc, Control ctrl, int fixedH)
        {
            int textW = cardW - ctrl.Width - S(48);
            var t = new Label();
            t.Text = title; t.Font = rowFont; t.ForeColor = Theme.Text; t.BackColor = Theme.Card; t.AutoSize = true; t.UseMnemonic = false;
            t.MaximumSize = new Size(textW, 0);
            Label d = null;
            if (desc != null)
            {
                d = new Label();
                d.Text = desc; d.Font = descFont; d.ForeColor = Theme.SubText; d.BackColor = Theme.Card; d.AutoSize = true; d.UseMnemonic = false;
                d.MaximumSize = new Size(textW, 0);
            }
            int textH = t.PreferredHeight + (d != null ? d.GetPreferredSize(new Size(textW, 0)).Height + S(3) : 0);
            int h = fixedH > 0 ? fixedH : Math.Max(S(desc == null ? 54 : 66), textH + S(24));
            h = Math.Max(h, ctrl.Height + S(18));
            var card = new Card();
            card.SetBounds(S(30), y, cardW, h);
            int ty = (h - textH) / 2;
            t.Location = new Point(S(18), ty);
            card.Controls.Add(t);
            if (d != null) { d.Location = new Point(S(18), ty + t.PreferredHeight + S(3)); card.Controls.Add(d); }
            ctrl.Location = new Point(cardW - S(18) - ctrl.Width, (h - ctrl.Height) / 2);
            card.Controls.Add(ctrl);
            host.Controls.Add(card);
            y += h + S(6);
        }

        Control Toggle(bool on, Action<bool> set)
        {
            var t = new ToggleSwitch();
            t.Size = new Size(S(44), S(22));
            t.Checked = on;
            t.Changed += delegate { set(t.Checked); Apply(); };
            return t;
        }

        Control Button(string text, Action click)
        {
            var b = new PillButton(text, false);
            b.Font = rowFont;
            b.Size = new Size(Math.Max(S(110), TextRenderer.MeasureText(text, rowFont).Width + S(32)), S(34));
            b.Click += delegate { click(); };
            return b;
        }

        Control SliderWith(int min, int max, int v, string unit, Action<int> set)
        {
            var hostP = new Panel();
            hostP.BackColor = Theme.Card;
            hostP.Size = new Size(S(250), S(30));
            var val = new Label();
            val.AutoSize = false; val.TextAlign = ContentAlignment.MiddleRight;
            val.Font = descFont; val.ForeColor = Theme.SubText; val.BackColor = Theme.Card;
            val.SetBounds(0, 0, S(56), S(30));
            val.Text = v + unit;
            var s = new Slider(min, max, v);
            s.SetBounds(S(62), 0, S(188), S(30));
            var debounce = new Timer();
            debounce.Interval = 120;
            debounce.Tick += delegate { debounce.Stop(); Apply(); };
            s.Changed += delegate { val.Text = s.Value + unit; set(s.Value); debounce.Stop(); debounce.Start(); };
            hostP.Controls.Add(val);
            hostP.Controls.Add(s);
            return hostP;
        }
    }

    // ------------------------------------------------------------------
    // Widget gallery: categories, search, live previews, click to add
    // ------------------------------------------------------------------
    class GalleryForm : Form
    {
        readonly BarForm bar;
        readonly float sc;
        readonly Panel side, top, grid;
        readonly TextBox search;
        readonly List<NavButton> cats = new List<NavButton>();
        readonly List<GalleryCard> cards = new List<GalleryCard>();
        readonly Timer live;
        readonly Font rowFont, descFont, titleFont;
        string category = "All";
        public Ui PreviewUi;

        int S(float v) { return (int)Math.Round(v * sc); }

        public GalleryForm(BarForm bar)
        {
            this.bar = bar;
            sc = bar.UiScale;
            Text = "Widget gallery";
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            MaximizeBox = false;
            rowFont = new Font("Segoe UI", 14f * sc, GraphicsUnit.Pixel);
            descFont = new Font("Segoe UI", 12f * sc, GraphicsUnit.Pixel);
            titleFont = new Font("Segoe UI Semibold", 22f * sc, GraphicsUnit.Pixel);
            Font = rowFont;
            ClientSize = new Size(S(1000), S(680));
            MinimumSize = new Size(S(760), S(480));
            PreviewUi = Ui.Make(sc);
            PreviewUi.Icon = bar.IconFor;

            grid = new Panel();
            grid.Dock = DockStyle.Fill;
            grid.AutoScroll = true;
            grid.BackColor = Theme.Window;
            Controls.Add(grid);

            top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = S(78);
            top.BackColor = Theme.Window;
            Controls.Add(top);

            side = new Panel();
            side.Dock = DockStyle.Left;
            side.Width = S(220);
            side.BackColor = Theme.Side;
            Controls.Add(side);

            var t = new Label();
            t.Text = "Widgets"; t.Font = titleFont; t.ForeColor = Theme.Text; t.BackColor = Theme.Side; t.AutoSize = true;
            t.Location = new Point(S(22), S(22));
            side.Controls.Add(t);
            int cy = S(76);
            foreach (string c in new[] { "All" }.Concat(Widgets.Categories))
            {
                string glyph = c == "All" ? "\uE71D" : c == "Essentials" ? "\uE80F" : c == "Media" ? "\uE8D6" : c == "System" ? "\uE950" :
                               c == "Network" ? "\uE701" : c == "Time & weather" ? "\uE753" : c == "Study" ? "\uE7BE" : c == "Toggles" ? "\uE945" :
                               c == "Info" ? "\uE946" : c == "Dev" ? "\uE943" : c == "Custom" ? "\uE756" : "\uE70F";
                var b = new NavButton(glyph, c);
                b.Font = rowFont;
                b.Selected = c == category;
                b.SetBounds(S(10), cy, S(200), S(40));
                string cat = c;
                b.Click += delegate { category = cat; foreach (NavButton o in cats) { o.Selected = o == b; o.Invalidate(); } Fill(); };
                side.Controls.Add(b);
                cats.Add(b);
                cy += S(44);
            }

            search = new TextBox();
            search.BorderStyle = BorderStyle.FixedSingle;
            search.BackColor = Theme.Field; search.ForeColor = Theme.Text; search.Font = rowFont;
            search.SetBounds(S(28), S(24), S(360), S(30));
            search.TextChanged += delegate { Fill(); };
            search.HandleCreated += delegate { Native.SendCue(search.Handle, "Search widgets…"); };
            top.Controls.Add(search);
            var hint = new Label();
            hint.Text = "Click a widget to add or remove it. Drag tiles on the bar to reorder.";
            hint.Font = descFont; hint.ForeColor = Theme.SubText; hint.BackColor = Theme.Window; hint.AutoSize = true;
            hint.Location = new Point(S(404), S(30));
            top.Controls.Add(hint);

            live = new Timer();
            live.Interval = 1000;
            live.Tick += delegate { foreach (GalleryCard c in cards) c.Invalidate(); };
            grid.Resize += delegate { Fill(); };
            Fill();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetDwm(Handle, 20, 1);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try { Native.SetWindowTheme(grid.Handle, "DarkMode_Explorer", null); } catch { }
            search.Focus();
            live.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e) { live.Stop(); base.OnFormClosed(e); }

        void Fill()
        {
            string q = search.Text.Trim().ToLowerInvariant();
            PreviewUi.Build(sc);
            PreviewUi.Icon = bar.IconFor;
            grid.SuspendLayout();
            foreach (GalleryCard c in cards) { grid.Controls.Remove(c); c.Dispose(); }
            cards.Clear();
            int cols = Math.Max(1, (grid.ClientSize.Width - S(40)) / S(340));
            int gap = S(14), cw = (grid.ClientSize.Width - S(56) - gap * (cols - 1)) / cols, ch = S(196);
            int i = 0;
            foreach (Widget w in Widgets.All)
            {
                if (category != "All" && w.Category != category) continue;
                if (q.Length > 0 && !(w.Name + " " + w.Desc + " " + w.Category).ToLowerInvariant().Contains(q)) continue;
                var c = new GalleryCard(this, w, sc);
                c.Font = rowFont;
                c.SetBounds(S(28) + (i % cols) * (cw + gap), S(4) + (i / cols) * (ch + gap) + grid.AutoScrollPosition.Y, cw, ch);
                Widget ww = w;
                c.Click += delegate { Toggle(ww); };
                grid.Controls.Add(c);
                cards.Add(c);
                i++;
            }
            if (i == 0)
            {
                var none = new GalleryCard(this, null, sc);
                none.Font = rowFont;
                none.SetBounds(S(28), S(4), S(400), S(80));
                grid.Controls.Add(none);
                cards.Add(none);
            }
            grid.ResumeLayout();
        }

        public static bool IsOn(Widget w) { return Config.HasWidget(w.Id); }

        void Toggle(Widget w)
        {
            if (!w.Available) return;
            List<string> left = Config.Zones[Config.ZLeft], rightZone = Config.Zones[Config.ZRight];
            if (IsOn(w)) { foreach (string k in Config.AllWidgetKeys().Where(k => Config.BaseId(k) == w.Id).ToList()) Config.RemoveWidget(k); }
            else if (w.Id == "start" || w.Id == "search") left.Insert(w.Id == "start" ? 0 : Math.Min(left.Count > 0 && left[0] == "start" ? 1 : 0, left.Count), w.Id);
            else
            {
                // keep the clock and the show-desktop strip at the far right
                int at = rightZone.Count;
                foreach (string end in new[] { "desktop", "clock" }) { int k = rightZone.IndexOf(end); if (k >= 0 && k < at) at = k; }
                rightZone.Insert(at, w.Id);
            }
            bar.ApplyConfig();
            foreach (GalleryCard c in cards) c.Invalidate();
        }

        public void RenderPreview(Graphics g, Widget w, RectangleF box)
        {
            Ui u = PreviewUi;
            u.Accent = Config.Accent;
            DemoData.With(delegate
            {
                int ww = w.Width(u);
                float k = Math.Min(1f, Math.Min((box.Width - S(16)) / Math.Max(1, ww), (box.Height - S(8)) / u.H));
                var state = g.Save();
                g.TranslateTransform(box.X + box.Width / 2f, box.Y + box.Height / 2f);
                g.ScaleTransform(k, k);
                var r = new Rectangle(-ww / 2, -u.H / 2, ww, u.H);
                if (!w.Bare) Gfx.Tile(g, u.Tile(r), u.Radius, u.P, u.Accent, 0, 1f);
                w.Paint(g, r, u, new Point(-9999, -9999), 0);
                g.Restore(state);
            });
        }
    }

    class GalleryCard : SmoothControl
    {
        readonly GalleryForm owner;
        readonly Widget w;
        readonly float sc;
        public GalleryCard(GalleryForm owner, Widget w, float sc) { this.owner = owner; this.w = w; this.sc = sc; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Prep(e);
            var outer = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Theme.FillRound(g, outer, 12 * sc, hot ? Theme.CardHover : Theme.Card);
            if (w == null)
            {
                using (var b = new SolidBrush(Theme.SubText)) g.DrawString("No widgets match your search.", Font, b, outer, Theme.Center);
                return;
            }
            bool on = GalleryForm.IsOn(w), avail = w.Available;
            var stage = new RectangleF(10 * sc, 10 * sc, Width - 20 * sc, 92 * sc);
            using (GraphicsPath sp = Theme.Round(stage, 9 * sc))
            {
                using (var lb = new LinearGradientBrush(stage, Color.FromArgb(32, 46, 86), Color.FromArgb(76, 38, 72), 15f)) g.FillPath(lb, sp);
                using (var b = new SolidBrush(Config.P.TintAt(Config.Darkness))) g.FillPath(b, sp);
                Region old = g.Clip;
                g.SetClip(sp, CombineMode.Intersect);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                try { owner.RenderPreview(g, w, stage); } catch { }
                g.Clip = old;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            }
            using (var f = new Font("Segoe UI Semibold", 14.5f * sc, GraphicsUnit.Pixel))
            using (var b = new SolidBrush(Theme.Text))
                g.DrawString(w.Name, f, b, new RectangleF(14 * sc, 110 * sc, Width - 130 * sc, 24 * sc), Gfx.Left);
            using (var f = new Font("Segoe UI", 11.5f * sc, GraphicsUnit.Pixel))
            using (var b = new SolidBrush(Theme.SubText))
            {
                var fmt = new StringFormat(); fmt.Trimming = StringTrimming.EllipsisWord;
                g.DrawString(avail ? w.Desc : "Not available on this PC. " + w.Desc, f, b, new RectangleF(14 * sc, 136 * sc, Width - 28 * sc, 52 * sc), fmt);
            }
            // add / added pill
            string label = !avail ? "N/A" : on ? (hot ? "Remove" : "Added") : "Add";
            var pill = new RectangleF(Width - 108 * sc, 108 * sc, 94 * sc, 28 * sc);
            Color fill = !avail ? Color.FromArgb(50, 50, 58) : on ? (hot ? Color.FromArgb(90, 239, 68, 68) : Color.FromArgb(40, 74, 222, 128)) : Config.Accent;
            Theme.FillRound(g, pill, 14 * sc, fill);
            if (on && !hot) Theme.StrokeRound(g, pill, 14 * sc, Color.FromArgb(160, 74, 222, 128), 1.2f);
            using (var f = new Font("Segoe UI Semibold", 12f * sc, GraphicsUnit.Pixel))
            using (var b = new SolidBrush(!avail ? Theme.SubText : on ? Theme.Text : Gfx.On(Config.Accent)))
                g.DrawString((on && !hot ? "✓ " : on ? "" : avail ? "+ " : "") + label, f, b, pill, Theme.Center);
            if (on) Theme.StrokeRound(g, RectangleF.Inflate(outer, -0.5f, -0.5f), 12 * sc, Color.FromArgb(90, Config.Accent), 1.2f);
        }
    }

    // ------------------------------------------------------------------
    // A tiny dark "type a name" dialog
    // ------------------------------------------------------------------
    static class InputBox
    {
        public static string Ask(string title, string prompt, string value, float sc)
        {
            using (var f = new Form())
            {
                Func<float, int> S = v => (int)Math.Round(v * sc);
                f.Text = title; f.ShowIcon = false; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MaximizeBox = false; f.MinimizeBox = false;
                f.StartPosition = FormStartPosition.CenterScreen; f.AutoScaleMode = AutoScaleMode.None;
                f.BackColor = Theme.Window; f.ForeColor = Theme.Text;
                f.Font = new Font("Segoe UI", 13f * sc, GraphicsUnit.Pixel);
                f.ClientSize = new Size(S(380), S(150));
                f.TopMost = true;
                f.HandleCreated += delegate { Native.SetDwm(f.Handle, 20, 1); };
                var l = new Label(); l.Text = prompt; l.AutoSize = true; l.Location = new Point(S(18), S(16)); f.Controls.Add(l);
                var tb = new TextBox(); tb.Text = value ?? ""; tb.BorderStyle = BorderStyle.FixedSingle; tb.BackColor = Theme.Field; tb.ForeColor = Theme.Text;
                tb.SetBounds(S(18), S(46), S(344), S(30)); f.Controls.Add(tb);
                var ok = new Button(); ok.Text = "OK"; ok.DialogResult = DialogResult.OK; ok.FlatStyle = FlatStyle.Flat; ok.FlatAppearance.BorderSize = 0;
                ok.BackColor = Config.Accent; ok.ForeColor = Gfx.On(Config.Accent); ok.SetBounds(S(172), S(96), S(90), S(34)); f.Controls.Add(ok);
                var cancel = new Button(); cancel.Text = "Cancel"; cancel.DialogResult = DialogResult.Cancel; cancel.FlatStyle = FlatStyle.Flat; cancel.FlatAppearance.BorderSize = 0;
                cancel.BackColor = Color.FromArgb(56, 56, 64); cancel.ForeColor = Theme.Text; cancel.SetBounds(S(272), S(96), S(90), S(34)); f.Controls.Add(cancel);
                f.AcceptButton = ok; f.CancelButton = cancel;
                f.Shown += delegate { Win.ForceForeground(f.Handle); tb.Focus(); tb.SelectAll(); };
                return f.ShowDialog() == DialogResult.OK ? tb.Text : null;
            }
        }
    }
}
