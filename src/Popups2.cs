// Phase 3 flyouts: to-do, habits, calendar events, translate, converter, calculator, volume mixer,
// and the screen color picker. Same look as the existing flyouts (Flyout base, its fonts and helpers).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace StayBar
{
    // A text field that matches the note flyout's field.
    static class FlyoutField
    {
        public static Color Back(Palette p) { return p.Light ? Color.FromArgb(250, 250, 252) : Color.FromArgb(30, 30, 36); }

        public static TextBox Make(Flyout f, Palette p, float sc, Rectangle bounds)
        {
            var box = new TextBox();
            box.BorderStyle = BorderStyle.None;
            box.Font = new Font("Segoe UI", 13f * sc, GraphicsUnit.Pixel);
            box.BackColor = Back(p);
            box.ForeColor = p.Ink;
            box.SetBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            f.Controls.Add(box);
            return box;
        }
    }

    class TodoFlyout : Flyout
    {
        readonly TextBox box;
        int scroll;
        const int Rows = 8;
        RectangleF clearDone;

        public TodoFlyout(float sc) : base(sc)
        {
            ClientSize = new Size(S(360), S(110) + Rows * S(40));
            box = FlyoutField.Make(this, P, sc, new Rectangle(S(28), S(58), S(304), S(22)));
            box.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                Todo.Add(box.Text);
                box.Text = "";
                Invalidate();
            };
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); box.Focus(); Native.SendCue(box.Handle, "Add a to-do and press Enter"); }

        RectangleF RowRect(int v) { return new RectangleF(S(10), S(96) + v * S(40), ClientSize.Width - S(20), S(38)); }

        protected override void PaintContent(Graphics g, Size s)
        {
            Header(g, "To do", Todo.Open + " left", S(16));
            Theme.FillRound(g, new RectangleF(S(14), S(50), s.Width - S(28), S(38)), S(10), FlyoutField.Back(P));
            var items = Todo.Items;
            var fmt = Theme.MakeFormat(StringAlignment.Near);
            for (int v = 0; v < Rows && scroll + v < items.Count; v++)
            {
                TodoItem it = items[scroll + v];
                RectangleF row = RowRect(v);
                if (Hot(row)) Theme.FillRound(g, row, S(8), P.Light ? Color.FromArgb(14, 0, 0, 0) : Color.FromArgb(18, 255, 255, 255));
                var cb = new RectangleF(row.X + S(10), row.Y + (row.Height - S(18)) / 2f, S(18), S(18));
                if (it.Done) { Theme.FillRound(g, cb, S(5), Accent); Gfx.Glyph(g, "\uE73E", FGlyphSmall, Gfx.On(Accent), cb); }
                else Theme.StrokeRound(g, cb, S(5), Color.FromArgb(150, Ink), 1.4f);
                Gfx.TextIn(g, it.Text, FText, it.Done ? Faint : Ink, new RectangleF(cb.Right + S(10), row.Y, row.Width - S(76), row.Height), fmt);
                if (Hot(row)) Gfx.Glyph(g, "\uE711", FGlyphSmall, Dim, new RectangleF(row.Right - S(34), row.Y + S(5), S(28), S(28)));
            }
            if (items.Count == 0) Gfx.TextIn(g, "Nothing here. Type above and press Enter.", FSmall, Dim, new RectangleF(S(20), S(100), s.Width - S(40), S(30)), fmt);
            clearDone = new RectangleF(s.Width - S(130), s.Height - S(34), S(116), S(26));
            if (items.Any(i => i.Done)) { HotFill(g, clearDone, S(6)); Gfx.TextIn(g, "Clear done", FSmall, Accent, clearDone, Gfx.Mid); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            var items = Todo.Items;
            if (clearDone.Contains(e.Location)) { Todo.ClearDone(); Invalidate(); return; }
            for (int v = 0; v < Rows && scroll + v < items.Count; v++)
            {
                RectangleF row = RowRect(v);
                if (!row.Contains(e.Location)) continue;
                TodoItem it = items[scroll + v];
                if (e.X > row.Right - S(36)) items.Remove(it); else it.Done = !it.Done;
                Todo.Save();
                Invalidate();
                return;
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            scroll = Math.Max(0, Math.Min(Math.Max(0, Todo.Items.Count - Rows), scroll + (e.Delta > 0 ? -1 : 1)));
            Invalidate();
        }
    }

    class HabitFlyout : Flyout
    {
        readonly TextBox box;
        const int Days = 14;

        public HabitFlyout(float sc) : base(sc)
        {
            ClientSize = new Size(S(520), S(130) + Math.Max(1, Habits.Names.Count) * S(46));
            box = FlyoutField.Make(this, P, sc, new Rectangle(S(28), ClientSize.Height - S(44), S(300), S(22)));
            box.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Enter || box.Text.Trim().Length == 0) return;
                e.SuppressKeyPress = true;
                Habits.Add(box.Text.Trim());
                box.Text = "";
                ClientSize = new Size(S(520), S(130) + Math.Max(1, Habits.Names.Count) * S(46));
                box.Top = ClientSize.Height - S(44);
                Invalidate();
            };
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); Native.SendCue(box.Handle, "Add a habit and press Enter"); }

        RectangleF Dot(int row, int day) { float d = S(20), gap = S(6); return new RectangleF(S(140) + day * (d + gap), S(76) + row * S(46) + S(4), d, d); }

        protected override void PaintContent(Graphics g, Size s)
        {
            Header(g, "Habits", "Click a dot to tick a day", S(16));
            var fmt = Theme.MakeFormat(StringAlignment.Near);
            for (int d = 0; d < Days; d++)
            {
                DateTime day = DateTime.Today.AddDays(d - Days + 1);
                RectangleF dr = Dot(0, d);
                Gfx.TextIn(g, day.ToString("ddd", CultureInfo.CurrentCulture).Substring(0, 1), FTiny, day == DateTime.Today ? Accent : Faint, new RectangleF(dr.X - S(4), S(50), dr.Width + S(8), S(18)), Gfx.Mid);
            }
            var names = Habits.Names;
            for (int i = 0; i < names.Count; i++)
            {
                string h = names[i];
                float y = S(76) + i * S(46);
                Gfx.TextIn(g, h, FText, Ink, new RectangleF(S(18), y, S(110), S(28)), fmt);
                int streak = Habits.Streak(h);
                Gfx.TextIn(g, streak > 0 ? streak + " day streak" : "", FTiny, Dim, new RectangleF(S(18), y + S(22), S(120), S(18)), fmt);
                for (int d = 0; d < Days; d++)
                {
                    DateTime day = DateTime.Today.AddDays(d - Days + 1);
                    RectangleF dr = Dot(i, d);
                    if (Habits.Done(h, day)) using (var b = new SolidBrush(Accent)) g.FillEllipse(b, dr);
                    else using (var pen = new Pen(Color.FromArgb(Hot(dr) ? 200 : day == DateTime.Today ? 150 : 60, Ink), 1.4f)) g.DrawEllipse(pen, dr.X + 0.7f, dr.Y + 0.7f, dr.Width - 1.4f, dr.Height - 1.4f);
                }
            }
            Theme.FillRound(g, new RectangleF(S(14), s.Height - S(52), s.Width - S(28), S(38)), S(10), FlyoutField.Back(P));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            var names = Habits.Names;
            for (int i = 0; i < names.Count; i++)
                for (int d = 0; d < Days; d++)
                    if (RectangleF.Inflate(Dot(i, d), S(3), S(3)).Contains(e.Location)) { Habits.Toggle(names[i], DateTime.Today.AddDays(d - Days + 1)); Invalidate(); return; }
            if (e.Button == MouseButtons.Right)
                for (int i = 0; i < names.Count; i++)
                    if (new RectangleF(S(10), S(76) + i * S(46), S(130), S(40)).Contains(e.Location) &&
                        MessageBox.Show(this, "Remove the habit \"" + names[i] + "\" and its history?", "StayBar", MessageBoxButtons.YesNo) == DialogResult.Yes)
                    { Habits.Remove(names[i]); Close(); return; }
        }
    }

    class EventsFlyout : Flyout
    {
        readonly List<CalEvent> events;

        public EventsFlyout(float sc, List<CalEvent> events) : base(sc)
        {
            this.events = events.Take(9).ToList();
            ClientSize = new Size(S(380), S(60) + Math.Max(1, this.events.Count) * S(48) + S(8));
        }

        protected override void PaintContent(Graphics g, Size s)
        {
            Header(g, "Coming up", null, S(16));
            var fmt = Theme.MakeFormat(StringAlignment.Near);
            if (events.Count == 0) { Gfx.TextIn(g, "No upcoming events.", FSmall, Dim, new RectangleF(S(18), S(54), s.Width - S(36), S(30)), fmt); return; }
            for (int i = 0; i < events.Count; i++)
            {
                CalEvent e = events[i];
                var row = new RectangleF(S(10), S(52) + i * S(48), s.Width - S(20), S(46));
                Theme.FillRound(g, row, S(8), P.Light ? Color.FromArgb(8, 0, 0, 0) : Color.FromArgb(10, 255, 255, 255));
                Theme.FillRound(g, new RectangleF(row.X + S(10), row.Y + S(10), S(4), row.Height - S(20)), S(2), Accent);
                Gfx.TextIn(g, e.Title, FText, Ink, new RectangleF(row.X + S(24), row.Y + S(4), row.Width - S(34), S(22)), fmt);
                Gfx.TextIn(g, IcsWidget.When(e) + (e.Location.Length > 0 ? "  ·  " + e.Location : ""), FTiny, Dim, new RectangleF(row.X + S(24), row.Y + S(24), row.Width - S(34), S(18)), fmt);
            }
        }
    }

    class TranslateFlyout : Flyout
    {
        public static readonly string[] Langs = { "en", "es", "ca", "uk", "ru", "fr", "de", "it", "pt", "pl", "zh", "ja", "ar" };
        public static readonly string[] LangNames = { "English", "Spanish", "Catalan", "Ukrainian", "Russian", "French", "German", "Italian", "Portuguese", "Polish", "Chinese", "Japanese", "Arabic" };
        readonly Widget owner;
        readonly TextBox box;
        string result = "", from, to;
        bool busy;
        RectangleF swap, fromR, toR;

        public TranslateFlyout(float sc, Widget owner) : base(sc)
        {
            this.owner = owner;
            from = owner.Opt("from", "en"); to = owner.Opt("to", "es");
            ClientSize = new Size(S(400), S(250));
            box = FlyoutField.Make(this, P, sc, new Rectangle(S(28), S(98), S(344), S(22)));
            box.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Go(); } };
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); box.Focus(); Native.SendCue(box.Handle, "Type and press Enter"); }

        static string LangName(string code) { int i = Array.IndexOf(Langs, code); return i >= 0 ? LangNames[i] : code; }

        void Go()
        {
            string text = box.Text.Trim(), f = from, t = to;
            if (text.Length == 0 || busy) return;
            busy = true; result = "Translating…"; Invalidate();
            Bg.Pool("translate", delegate
            {
                string r;
                try { r = Online.Translate(text, f, t); } catch { r = "Couldn't translate (offline?)"; }
                BarForm.Post(delegate { busy = false; result = r; if (!IsDisposed) Invalidate(); });
            });
        }

        protected override void PaintContent(Graphics g, Size s)
        {
            Header(g, "Translate", "MyMemory", S(16));
            fromR = new RectangleF(S(14), S(50), S(150), S(32));
            swap = new RectangleF(S(176), S(50), S(48), S(32));
            toR = new RectangleF(S(236), S(50), S(150), S(32));
            foreach (var kv in new[] { new KeyValuePair<RectangleF, string>(fromR, LangName(from)), new KeyValuePair<RectangleF, string>(toR, LangName(to)) })
            {
                Theme.FillRound(g, kv.Key, S(8), P.Light ? Color.FromArgb(14, 0, 0, 0) : Color.FromArgb(18, 255, 255, 255));
                HotFill(g, kv.Key, S(8));
                Gfx.TextIn(g, kv.Value + "  ▾", FSmall, Ink, kv.Key, Gfx.Mid);
            }
            HotFill(g, swap, S(8));
            Gfx.Glyph(g, "\uE8AB", FGlyph, Dim, swap);
            Theme.FillRound(g, new RectangleF(S(14), S(90), s.Width - S(28), S(38)), S(10), FlyoutField.Back(P));
            Gfx.TextIn(g, result, FText, busy ? Dim : Ink, new RectangleF(S(18), S(140), s.Width - S(36), s.Height - S(170)), Theme.MakeFormat(StringAlignment.Near));
            if (result.Length > 0 && !busy) Gfx.TextIn(g, "Click the text to copy it", FTiny, Faint, new RectangleF(S(18), s.Height - S(28), s.Width - S(36), S(18)), Theme.MakeFormat(StringAlignment.Near));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (swap.Contains(e.Location)) { string t = from; from = to; to = t; Save(); Invalidate(); return; }
            if (fromR.Contains(e.Location) || toR.Contains(e.Location))
            {
                bool isFrom = fromR.Contains(e.Location);
                var m = new ContextMenuStrip();
                m.Renderer = new DarkRenderer(); m.BackColor = P.MenuBack; m.ForeColor = Ink;
                for (int i = 0; i < Langs.Length; i++)
                {
                    string code = Langs[i];
                    var it = new ToolStripMenuItem(LangNames[i]);
                    it.Checked = code == (isFrom ? from : to);
                    it.Click += delegate { if (isFrom) from = code; else to = code; Save(); Invalidate(); };
                    m.Items.Add(it);
                }
                ShowMenu(m, e.Location);
                return;
            }
            if (e.Y > S(136) && result.Length > 0 && !busy) { try { Clipboard.SetText(result); } catch { } }
        }

        void Save() { owner.SetOpt("from", from); owner.SetOpt("to", to); }
    }

    class ConverterFlyout : Flyout
    {
        static readonly string[] Kinds = { "Length", "Weight", "Temperature", "Speed", "Currency" };
        static readonly Dictionary<string, string[][]> Units = new Dictionary<string, string[][]>
        {
            { "Length", new[] { new[] { "m", "1" }, new[] { "km", "1000" }, new[] { "cm", "0.01" }, new[] { "mm", "0.001" }, new[] { "mi", "1609.344" }, new[] { "yd", "0.9144" }, new[] { "ft", "0.3048" }, new[] { "in", "0.0254" } } },
            { "Weight", new[] { new[] { "kg", "1" }, new[] { "g", "0.001" }, new[] { "lb", "0.45359237" }, new[] { "oz", "0.028349523" }, new[] { "t", "1000" }, new[] { "st", "6.35029318" } } },
            { "Temperature", new[] { new[] { "°C", "c" }, new[] { "°F", "f" }, new[] { "K", "k" } } },
            { "Speed", new[] { new[] { "km/h", "1" }, new[] { "m/s", "3.6" }, new[] { "mph", "1.609344" }, new[] { "kn", "1.852" } } },
        };
        static readonly string[] Currencies = { "EUR", "USD", "GBP", "UAH", "JPY", "CHF", "PLN", "CAD", "CNY" };

        string kind;
        int fromI, toI = 1;
        readonly TextBox box;
        RectangleF[] tabs = new RectangleF[0];
        RectangleF fromR, toR, swap;

        public ConverterFlyout(float sc) : this(sc, "Length") { }

        public ConverterFlyout(float sc, string start) : base(sc)
        {
            kind = start;
            ClientSize = new Size(S(420), S(250));
            box = FlyoutField.Make(this, P, sc, new Rectangle(S(28), S(106), S(364), S(22)));
            box.Text = "1";
            box.TextChanged += delegate { Invalidate(); };
            if (kind == "Currency" && Rates.For("EUR") == null && Rates.Stale("EUR")) Bg.Pool("rates", delegate { Rates.Fetch("EUR"); BarForm.Post(delegate { if (!IsDisposed) Invalidate(); }); });
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); box.Focus(); box.SelectAll(); }

        string[] Names { get { return kind == "Currency" ? Currencies : Units[kind].Select(u => u[0]).ToArray(); } }

        string Result()
        {
            double v;
            if (!double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return "";
            double r;
            if (kind == "Currency")
            {
                var rates = Rates.For("EUR");
                if (rates == null) return "Loading rates…";
                double a, b;
                if (!rates.TryGetValue(Currencies[fromI], out a) || !rates.TryGetValue(Currencies[toI], out b)) return "No rate";
                r = v / a * b;
            }
            else if (kind == "Temperature")
            {
                string f = Units[kind][fromI][1], t = Units[kind][toI][1];
                double c = f == "c" ? v : f == "f" ? (v - 32) * 5 / 9 : v - 273.15;
                r = t == "c" ? c : t == "f" ? c * 9 / 5 + 32 : c + 273.15;
            }
            else
            {
                double fa = double.Parse(Units[kind][fromI][1], CultureInfo.InvariantCulture), ta = double.Parse(Units[kind][toI][1], CultureInfo.InvariantCulture);
                r = v * fa / ta;
            }
            return Math.Round(r, Math.Abs(r) >= 100 ? 2 : 6).ToString("#,0.######", CultureInfo.CurrentCulture) + " " + Names[toI];
        }

        protected override void PaintContent(Graphics g, Size s)
        {
            Header(g, "Convert", kind == "Currency" ? "ECB rates" : null, S(16));
            tabs = new RectangleF[Kinds.Length];
            float tw = (s.Width - S(28)) / Kinds.Length;
            for (int i = 0; i < Kinds.Length; i++)
            {
                tabs[i] = new RectangleF(S(14) + i * tw, S(48), tw - S(4), S(28));
                if (Kinds[i] == kind) Theme.FillRound(g, tabs[i], S(7), Color.FromArgb(P.Light ? 40 : 46, Accent));
                else HotFill(g, tabs[i], S(7));
                Gfx.TextIn(g, Kinds[i], FTiny, Kinds[i] == kind ? Ink : Dim, tabs[i], Gfx.Mid);
            }
            Theme.FillRound(g, new RectangleF(S(14), S(98), s.Width - S(28), S(38)), S(10), FlyoutField.Back(P));
            fromR = new RectangleF(S(14), S(146), S(160), S(32));
            swap = new RectangleF(S(186), S(146), S(48), S(32));
            toR = new RectangleF(S(246), S(146), S(160), S(32));
            string[] n = Names;
            foreach (var kv in new[] { new KeyValuePair<RectangleF, string>(fromR, n[Math.Min(fromI, n.Length - 1)]), new KeyValuePair<RectangleF, string>(toR, n[Math.Min(toI, n.Length - 1)]) })
            {
                Theme.FillRound(g, kv.Key, S(8), P.Light ? Color.FromArgb(14, 0, 0, 0) : Color.FromArgb(18, 255, 255, 255));
                HotFill(g, kv.Key, S(8));
                Gfx.TextIn(g, kv.Value + "  ▾", FSmall, Ink, kv.Key, Gfx.Mid);
            }
            HotFill(g, swap, S(8));
            Gfx.Glyph(g, "\uE8AB", FGlyph, Dim, swap);
            Gfx.TextIn(g, Result(), FTitle, Accent, new RectangleF(S(18), S(190), s.Width - S(36), S(40)), Theme.MakeFormat(StringAlignment.Near));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            for (int i = 0; i < tabs.Length; i++)
                if (tabs[i].Contains(e.Location))
                {
                    kind = Kinds[i]; fromI = 0; toI = 1;
                    if (kind == "Currency" && Rates.Stale("EUR")) Bg.Pool("rates", delegate { Rates.Fetch("EUR"); BarForm.Post(delegate { if (!IsDisposed) Invalidate(); }); });
                    Invalidate(); return;
                }
            if (swap.Contains(e.Location)) { int t = fromI; fromI = toI; toI = t; Invalidate(); return; }
            if (fromR.Contains(e.Location) || toR.Contains(e.Location))
            {
                bool isFrom = fromR.Contains(e.Location);
                var m = new ContextMenuStrip();
                m.Renderer = new DarkRenderer(); m.BackColor = P.MenuBack; m.ForeColor = Ink;
                string[] n = Names;
                for (int i = 0; i < n.Length; i++)
                {
                    int k = i;
                    var it = new ToolStripMenuItem(n[i]);
                    it.Checked = (isFrom ? fromI : toI) == i;
                    it.Click += delegate { if (isFrom) fromI = k; else toI = k; Invalidate(); };
                    m.Items.Add(it);
                }
                ShowMenu(m, e.Location);
            }
        }
    }

    class CalcFlyout : Flyout
    {
        static readonly List<string> history = new List<string>();
        readonly TextBox box;
        string preview = "";

        public CalcFlyout(float sc) : base(sc)
        {
            ClientSize = new Size(S(340), S(230));
            box = FlyoutField.Make(this, P, sc, new Rectangle(S(28), S(58), S(284), S(22)));
            box.TextChanged += delegate { string r = LauncherForm.Calc(box.Text.Trim()); preview = r == null ? "" : "= " + r; Invalidate(); };
            box.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                string r = LauncherForm.Calc(box.Text.Trim());
                if (r == null) return;
                history.Insert(0, box.Text.Trim() + " = " + r);
                if (history.Count > 4) history.RemoveAt(4);
                try { Clipboard.SetText(r); } catch { }
                box.Text = r;
                box.SelectionStart = box.Text.Length;
                Invalidate();
            };
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); box.Focus(); Native.SendCue(box.Handle, "Like 12*7+3, Enter copies the result"); }

        protected override void PaintContent(Graphics g, Size s)
        {
            Header(g, "Calculator", null, S(16));
            Theme.FillRound(g, new RectangleF(S(14), S(50), s.Width - S(28), S(38)), S(10), FlyoutField.Back(P));
            Gfx.TextIn(g, preview, FTitle, Accent, new RectangleF(S(18), S(96), s.Width - S(36), S(30)), Theme.MakeFormat(StringAlignment.Near));
            for (int i = 0; i < history.Count; i++)
                Gfx.TextIn(g, history[i], FSmall, Dim, new RectangleF(S(18), S(132) + i * S(22), s.Width - S(36), S(20)), Theme.MakeFormat(StringAlignment.Near));
        }
    }

    class MixerFlyout : Flyout
    {
        List<AudioSession> sessions;
        int dragRow = -1;

        public MixerFlyout(float sc) : base(sc)
        {
            sessions = AudioX.Sessions();
            ClientSize = new Size(S(380), S(60) + Math.Max(1, sessions.Count) * S(56) + S(10));
        }

        RectangleF Row(int i) { return new RectangleF(S(10), S(52) + i * S(56), ClientSize.Width - S(20), S(52)); }
        RectangleF Track(int i) { RectangleF r = Row(i); return new RectangleF(r.X + S(48), r.Y + S(32), r.Width - S(100), S(8)); }
        RectangleF MuteBox(int i) { RectangleF r = Row(i); return new RectangleF(r.X + S(6), r.Y + S(10), S(32), S(32)); }

        protected override void PaintContent(Graphics g, Size s)
        {
            Header(g, "Volume mixer", Audio.DeviceName, S(16));
            var fmt = Theme.MakeFormat(StringAlignment.Near);
            if (sessions.Count == 0) { Gfx.TextIn(g, "No apps are playing sound right now.", FSmall, Dim, new RectangleF(S(18), S(54), s.Width - S(36), S(30)), fmt); return; }
            for (int i = 0; i < sessions.Count; i++)
            {
                AudioSession a = sessions[i];
                RectangleF r = Row(i), tr = Track(i), mb = MuteBox(i);
                HotFill(g, mb, S(8));
                Gfx.Glyph(g, a.Muted ? "\uE74F" : "\uE767", FGlyph, a.Muted ? Faint : Ink, mb);
                Gfx.TextIn(g, a.Name, FText, Ink, new RectangleF(tr.X, r.Y + S(6), tr.Width, S(22)), fmt);
                string pct = (int)Math.Round(a.Volume * 100) + "%";
                Gfx.TextIn(g, pct, FSmall, Dim, new RectangleF(tr.Right + S(8), r.Y + S(22), S(44), S(24)), fmt);
                Gfx.Meter(g, tr, a.Volume * 100, a.Muted ? Faint : Accent, P.Track);
            }
        }

        void SetFrom(int i, int x)
        {
            RectangleF tr = Track(i);
            AudioX.SetVolume(sessions[i], Math.Max(0, Math.Min(1, (x - tr.X) / tr.Width)));
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            for (int i = 0; i < sessions.Count; i++)
            {
                if (MuteBox(i).Contains(e.Location)) { AudioX.SetMute(sessions[i], !sessions[i].Muted); Invalidate(); return; }
                if (RectangleF.Inflate(Track(i), S(6), S(10)).Contains(e.Location)) { dragRow = i; SetFrom(i, e.X); return; }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragRow >= 0 && e.Button == MouseButtons.Left) SetFrom(dragRow, e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragRow = -1; }
    }

    // ------------------------------------------------------------------
    // Color picker: a magnifier follows the mouse; click copies the color's hex code, Esc cancels.
    // A nearly invisible window covers the screens to catch that click.
    // ------------------------------------------------------------------
    class ColorPickerWindow : Form
    {
        public static Color Last = Color.Empty;
        readonly Timer timer;
        readonly Bitmap grab = new Bitmap(11, 11);
        readonly BarForm bar;
        Color current;
        Loupe loupe;

        public static void Start(BarForm bar) { new ColorPickerWindow(bar).Show(); }

        ColorPickerWindow(BarForm bar)
        {
            this.bar = bar;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.Black;
            Opacity = 0.01;
            Bounds = SystemInformation.VirtualScreen;
            Cursor = Cursors.Cross;
            KeyPreview = true;
            loupe = new Loupe(bar.UiScale);
            timer = new Timer();
            timer.Interval = 30;
            timer.Tick += delegate { Sample(); };
        }

        protected override CreateParams CreateParams { get { CreateParams cp = base.CreateParams; cp.ExStyle |= Native.WS_EX_TOOLWINDOW; return cp; } }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            loupe.Show();
            timer.Start();
            Sample();
        }

        void Sample()
        {
            Point c = Cursor.Position;
            try
            {
                using (Graphics g = Graphics.FromImage(grab)) g.CopyFromScreen(c.X - 5, c.Y - 5, 0, 0, grab.Size);
                current = grab.GetPixel(5, 5);
            }
            catch { }
            loupe.Show(c, grab, current);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                Sample();
                Last = Color.FromArgb(current.R, current.G, current.B);
                string hex = "#" + Config.Hex(Last);
                try { Clipboard.SetText(hex); } catch { }
                Close();
                bar.ShowToast("Copied " + hex, "rgb(" + Last.R + ", " + Last.G + ", " + Last.B + ")");
                BarForm.Poke();
            }
            else Close();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();
            loupe.Close();
            grab.Dispose();
            base.OnFormClosed(e);
        }

        // The magnifier next to the mouse.
        class Loupe : GlassWindow
        {
            Bitmap view;
            Color color;
            public Loupe(float sc) : base(sc, true) { ClientSize = new Size(S(132), S(160)); }
            protected override bool Rounded { get { return true; } }

            public void Show(Point at, Bitmap px, Color c)
            {
                view = px; color = c;
                Rectangle vs = SystemInformation.VirtualScreen;
                int x = at.X + S(24), y = at.Y + S(24);
                if (x + Width > vs.Right) x = at.X - S(24) - Width;
                if (y + Height > vs.Bottom) y = at.Y - S(24) - Height;
                Location = new Point(x, y);
                Invalidate();
            }

            protected override void PaintContent(Graphics g, Size s)
            {
                if (view == null) return;
                var r = new Rectangle(S(10), S(10), s.Width - S(20), s.Width - S(20));
                var old = g.InterpolationMode; var oldOff = g.PixelOffsetMode;
                g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(view, r);
                g.InterpolationMode = old; g.PixelOffsetMode = oldOff;
                float cell = r.Width / 11f;
                using (var pen = new Pen(Color.White, 1.5f)) g.DrawRectangle(pen, r.X + cell * 5, r.Y + cell * 5, cell, cell);
                var sw = new RectangleF(S(10), r.Bottom + S(8), S(22), S(22));
                Theme.FillRound(g, sw, S(5), color);
                using (var f = new Font("Segoe UI Semibold", 12.5f * Sc, GraphicsUnit.Pixel))
                    Gfx.TextIn(g, "#" + Config.Hex(color), f, Ink, new RectangleF(sw.Right + S(8), sw.Y, s.Width - sw.Right - S(14), sw.Height), Gfx.Left);
            }
        }
    }
}
