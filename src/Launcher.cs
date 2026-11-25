// Alt+Space launcher: fuzzy app search, open windows, a calculator, commands and web search.
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace StayBar
{
    class LaunchItem
    {
        public string Title, Sub, Glyph, IconSource, Key;
        public Bitmap Icon;
        public int Score;
        public Action Run;
        public AppEntry App;
    }

    class LauncherForm : GlassWindow
    {
        readonly BarForm bar;
        readonly TextBox box;
        readonly Font fTitle, fSub, fGlyph, fGlyphBig, fHint;
        List<LaunchItem> items = new List<LaunchItem>();
        int sel;
        Point restMouse = new Point(-1, -1);
        const int MaxRows = 8;
        static Dictionary<string, int> history;
        readonly Timer poll;

        static string HistoryFile { get { return Path.Combine(Program.DataDir, "launcher-history.txt"); } }

        public LauncherForm(BarForm bar) : base(bar.U.Scale, false)
        {
            this.bar = bar;
            KeyPreview = true;
            fTitle = new Font("Segoe UI", 14f * Sc, GraphicsUnit.Pixel);
            fSub = new Font("Segoe UI", 11f * Sc, GraphicsUnit.Pixel);
            fHint = new Font("Segoe UI", 10.5f * Sc, GraphicsUnit.Pixel);
            fGlyph = new Font(Gfx.GlyphFamily(), 16f * Sc, GraphicsUnit.Pixel);
            fGlyphBig = new Font(Gfx.GlyphFamily(), 18f * Sc, GraphicsUnit.Pixel);
            box = new TextBox();
            box.BorderStyle = BorderStyle.None;
            box.Font = new Font("Segoe UI", 19f * Sc, GraphicsUnit.Pixel);
            box.BackColor = FieldColor;
            box.ForeColor = P.Ink;
            box.TextChanged += delegate { Search(); };
            Controls.Add(box);
            LoadHistory();
            poll = new Timer();
            poll.Interval = 400;
            poll.Tick += delegate { if (AppIndex.Items.Count > 0 && items.Count <= 1) { Search(); } if (AppIndex.Items.Count > 0) poll.Stop(); };
        }

        Color FieldColor { get { return P.Light ? Color.FromArgb(252, 252, 253) : Color.FromArgb(32, 32, 38); } }
        protected override Color Tint { get { return Color.FromArgb(P.Light ? 230 : 225, P.Tint.R, P.Tint.G, P.Tint.B); } }

        int W { get { return S(640); } }
        int FieldH { get { return S(58); } }
        int RowH { get { return S(52); } }

        public void ShowLauncher()
        {
            AppIndex.Ensure();
            Search();
            Rectangle sb = Screen.PrimaryScreen.Bounds;
            Location = new Point(sb.X + (sb.Width - W) / 2, sb.Y + (int)(sb.Height * 0.22));
            Show();
            Win.ForceForeground(Handle);
            Activate();
            box.Focus();
            restMouse = PointToClient(Cursor.Position);
            if (AppIndex.Items.Count == 0) poll.Start();
        }

        public void Demo(string query) { box.Text = query; Search(); }

        void Relayout()
        {
            int rows = Math.Max(1, Math.Min(MaxRows, items.Count));
            ClientSize = new Size(W, S(10) + FieldH + S(8) + rows * RowH + S(34));
            box.SetBounds(S(58), S(10) + (FieldH - box.PreferredHeight) / 2, W - S(80), box.PreferredHeight);
        }

        // ---------- search ----------

        void Search()
        {
            string q = box.Text.Trim();
            string ql = q.ToLowerInvariant();
            var list = new List<LaunchItem>();

            string calc = Calc(q);
            if (calc != null)
            {
                string result = calc;
                list.Add(new LaunchItem { Title = "= " + calc, Sub = "Press Enter to copy the result", Glyph = "\uE8EF", Score = 5000, Run = () => { Clip.Copy(result); BarForm.Toast("Copied", result); } });
            }

            foreach (AppEntry e in AppIndex.Items)
            {
                int s = q.Length == 0 ? -1 : AppIndex.Score(ql, e.Lower, e.Initials);
                int h = Used("app:" + e.Launch);
                if (q.Length == 0) { if (h == 0) continue; s = 300; }
                if (s < 0) continue;
                AppEntry entry = e;
                list.Add(new LaunchItem
                {
                    Title = e.Name, Sub = e.Kind == "store" ? "App  ·  Microsoft Store" : "App", IconSource = e.IconSource, App = e,
                    Key = "app:" + e.Launch, Score = s + Math.Min(160, h * 30),
                    Run = () => { Win.Launch(entry.Launch); },
                });
            }

            foreach (AppGroup g in bar.AllApps())
                foreach (IntPtr w in g.Windows)
                {
                    string title = Win.TitleOf(w);
                    int s = q.Length == 0 ? 200 : Math.Max(AppIndex.Score(ql, title.ToLowerInvariant(), null), AppIndex.Score(ql, (g.Name ?? "").ToLowerInvariant(), null) - 40);
                    if (s < 0) continue;
                    IntPtr win = w;
                    list.Add(new LaunchItem
                    {
                        Title = title, Sub = "Switch to  ·  " + g.Name, Icon = g.Icon, Key = "win:" + g.Id, Score = s - 60,
                        Run = () => Win.Activate(win),
                    });
                }

            foreach (LaunchItem c in Commands())
            {
                int s = q.Length == 0 ? -1 : AppIndex.Score(ql, c.Title.ToLowerInvariant(), null);
                if (s < 0) continue;
                c.Score = s - 30 + Math.Min(100, Used(c.Key) * 20);
                list.Add(c);
            }

            list = list.OrderByDescending(i => i.Score).Take(MaxRows - (q.Length > 0 ? 1 : 0)).ToList();
            if (q.Length > 0)
            {
                string query = q;
                list.Add(new LaunchItem { Title = "Search the web for “" + q + "”", Sub = "Opens your browser", Glyph = "\uE774", Score = -1,
                    Run = () => Win.Launch("https://www.google.com/search?q=" + Uri.EscapeDataString(query)) });
            }
            if (list.Count == 0)
                list.Add(new LaunchItem { Title = AppIndex.Items.Count == 0 ? "Finding your apps…" : "Type to search apps, windows and settings", Sub = "Tip: try “12*7” or “settings”", Glyph = "\uE721" });
            items = list;
            sel = 0;
            Relayout();
            Invalidate();
        }

        IEnumerable<LaunchItem> Commands()
        {
            yield return Cmd("StayBar settings", "Change the look and behavior", "\uE713", "cmd:settings", () => bar.OpenSettings(null));
            yield return Cmd("Widget gallery", "Add widgets to the bar", "\uE71D", "cmd:gallery", bar.OpenGallery);
            yield return Cmd("Windows Settings", "Open the Settings app", "\uE713", "cmd:winsettings", () => Win.Launch("ms-settings:"));
            yield return Cmd("Task Manager", "See what's running", "\uE9D9", "cmd:taskmgr", () => Win.Launch("taskmgr.exe"));
            yield return Cmd("Lock PC", "Lock the screen (Win+L)", "\uE72E", "cmd:lock", () => Native.LockWorkStation());
            yield return Cmd("Empty Recycle Bin", "Asks before deleting", "\uE74D", "cmd:recycle", () => Recycle.Empty(IntPtr.Zero));
            yield return Cmd("Restart StayBar", "Reload the bar", "\uE72C", "cmd:restart", bar.Restart);
            yield return Cmd("Downloads", "Open your Downloads folder", "\uE896", "cmd:downloads", () => Win.Launch("shell:Downloads"));
        }

        static LaunchItem Cmd(string title, string sub, string glyph, string key, Action run)
        {
            return new LaunchItem { Title = title, Sub = sub, Glyph = glyph, Key = key, Run = run };
        }

        public static string Calc(string q)
        {
            if (q.Length < 3 || !Regex.IsMatch(q, @"^[\d\s\.\,\+\-\*/\(\)%]+$") || !Regex.IsMatch(q, @"\d\s*[\+\-\*/%]\s*[\d\(]")) return null;
            try
            {
                object v = new DataTable().Compute(q.Replace(",", "."), "");
                double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                if (double.IsNaN(d) || double.IsInfinity(d)) return null;
                return Math.Round(d, 10).ToString("0.##########", CultureInfo.InvariantCulture);
            }
            catch { return null; }
        }

        // ---------- history ----------

        static void LoadHistory()
        {
            if (history != null) return;
            history = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(HistoryFile))
                    foreach (string line in File.ReadAllLines(HistoryFile))
                    {
                        int tab = line.IndexOf('\t');
                        int n;
                        if (tab > 0 && int.TryParse(line.Substring(0, tab), out n)) history[line.Substring(tab + 1)] = n;
                    }
            }
            catch { }
        }

        static int Used(string key) { int n; return key != null && history.TryGetValue(key, out n) ? n : 0; }

        static void Remember(string key)
        {
            if (key == null) return;
            history[key] = Used(key) + 1;
            try { File.WriteAllLines(HistoryFile, history.OrderByDescending(kv => kv.Value).Take(300).Select(kv => kv.Value + "\t" + kv.Key).ToArray()); } catch { }
        }

        // ---------- running ----------

        void RunSelected(bool admin)
        {
            if (sel < 0 || sel >= items.Count) return;
            LaunchItem it = items[sel];
            if (it.Run == null) return;
            Remember(it.Key);
            Close();
            if (admin && it.App != null)
            {
                try
                {
                    var psi = new ProcessStartInfo(it.App.Launch);
                    psi.UseShellExecute = true;
                    psi.Verb = "runas";
                    Process.Start(psi);
                }
                catch { }
                return;
            }
            try { it.Run(); } catch (Exception ex) { Program.Log("Launcher: " + ex.Message); }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape: Close(); return true;
                case Keys.Down: sel = Math.Min(items.Count - 1, sel + 1); Invalidate(); return true;
                case Keys.Up: sel = Math.Max(0, sel - 1); Invalidate(); return true;
                case Keys.Enter: RunSelected(false); return true;
                case Keys.Control | Keys.Shift | Keys.Enter: RunSelected(true); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!IsDisposed) BeginInvoke(new Action(Close));
        }

        RectangleF Row(int i) { return new RectangleF(S(8), S(10) + FieldH + S(8) + i * RowH, W - S(16), RowH); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (e.Location == restMouse) return;   // the window appeared under a resting mouse
            restMouse = new Point(-1, -1);
            for (int i = 0; i < items.Count; i++) if (Row(i).Contains(e.Location) && sel != i) { sel = i; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            for (int i = 0; i < items.Count; i++)
            {
                if (!Row(i).Contains(e.Location)) continue;
                sel = i;
                if (e.Button == MouseButtons.Left) RunSelected(false);
                else if (e.Button == MouseButtons.Right && items[i].App != null)
                {
                    AppEntry app = items[i].App;
                    var m = bar.NewMenu(false);
                    BarForm.Item(m, "Open", () => RunSelected(false));
                    BarForm.Item(m, "Run as administrator", () => RunSelected(true));
                    BarForm.Item(m, "Pin to bar", () => { bar.PinPath(app.Launch, app.Name); BarForm.Toast("Pinned", app.Name); });
                    if (File.Exists(app.Launch))
                        BarForm.Item(m, "Open file location", () => Process.Start("explorer.exe", "/select,\"" + app.Launch + "\""));
                    m.Show(this, e.Location);
                }
                return;
            }
        }

        // ---------- painting ----------

        protected override void PaintContent(Graphics g, Size s)
        {
            var field = new RectangleF(S(10), S(10), s.Width - S(20), FieldH);
            Theme.FillRound(g, field, S(12), FieldColor);
            Theme.StrokeRound(g, field, S(12), Color.FromArgb(120, Accent), 1.3f);
            Gfx.Glyph(g, "\uE721", fGlyphBig, Accent, new RectangleF(field.X + S(8), field.Y, S(40), field.Height));
            if (box.TextLength == 0)
                Gfx.TextIn(g, "Search apps, windows, settings…", box.Font, Faint, new RectangleF(S(58), field.Y, field.Width - S(60), field.Height), Gfx.Left);
            else if (Svc.Demo)
                Gfx.TextIn(g, box.Text, box.Font, Ink, new RectangleF(S(58), field.Y, field.Width - S(60), field.Height), Gfx.Left);

            for (int i = 0; i < items.Count && i < MaxRows; i++)
            {
                LaunchItem it = items[i];
                RectangleF r = Row(i);
                if (i == sel && it.Run != null)
                {
                    Theme.FillRound(g, r, S(10), P.Light ? Color.FromArgb(20, 0, 0, 0) : Color.FromArgb(26, 255, 255, 255));
                    Theme.FillRound(g, new RectangleF(r.X, r.Y + S(14), S(3), r.Height - S(28)), S(1.5f), Accent);
                }
                var ir = new RectangleF(r.X + S(14), r.Y + (r.Height - S(30)) / 2, S(30), S(30));
                Bitmap icon = it.Icon;
                if (icon == null && it.IconSource != null) icon = it.Icon = bar.IconFor(it.IconSource);
                if (icon != null) g.DrawImage(icon, ir);
                else
                {
                    Theme.FillRound(g, ir, S(8), Color.FromArgb(P.Light ? 30 : 40, Accent));
                    Gfx.Glyph(g, it.Glyph ?? "\uE7C3", fGlyph, P.Light ? Theme.Darken(Accent, 0.3f) : Theme.Lighten(Accent, 0.3f), ir);
                }
                float tx = ir.Right + S(14), tw = r.Right - tx - S(12);
                Gfx.TextIn(g, it.Title, fTitle, Ink, new RectangleF(tx, r.Y + S(7), tw, fTitle.GetHeight()), Gfx.Left);
                Gfx.TextIn(g, it.Sub, fSub, Dim, new RectangleF(tx, r.Y + S(8) + fTitle.GetHeight(), tw, fSub.GetHeight()), Gfx.Left);
                if (i == sel && it.Run != null)
                    Gfx.TextIn(g, "↵", fTitle, Faint, new RectangleF(r.Right - S(40), r.Y, S(30), r.Height), Gfx.Mid);
            }
            string hint = "↑↓ move   ↵ open   Ctrl+Shift+↵ admin   right-click: pin   Esc close";
            Gfx.TextIn(g, hint, fHint, Faint, new RectangleF(S(18), s.Height - S(28), s.Width - S(36), S(22)), Gfx.Left);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            poll.Stop();
            base.OnFormClosed(e);
        }
    }
}
