// Phase 3 widgets: online info (keyless sources), dev tools, and your own (custom and launcher tiles).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace StayBar
{
    // A widget that cycles through a few lines (several coins, rates or headlines).
    abstract class RotatingWidget : Widget
    {
        protected RotatingWidget(string id, string name, string cat, string glyph, string desc) : base(id, name, cat, glyph, desc) { }
        protected int Pick(int count) { return count <= 1 ? 0 : (int)((DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond / 6) % count); }
        protected static string Pct(double v) { return (v >= 0 ? "+" : "−") + Math.Abs(v).ToString("0.0", CultureInfo.CurrentCulture) + "%"; }
        protected static Color Trend(double v, Ui u) { return v > 0.05 ? Color.FromArgb(74, 222, 128) : v < -0.05 ? Color.FromArgb(251, 113, 133) : u.Dim; }

        // glyph, caption (with a colored change), big value
        protected void QuoteTile(Graphics g, Rectangle r, Ui u, string glyph, string caption, double change, bool showChange, string value)
        {
            RectangleF t = u.Tile(r);
            float gs = u.F(26);
            var gr = new RectangleF(t.X + u.F(8), t.Y + (t.Height - gs) / 2f, gs, gs);
            Gfx.Glyph(g, glyph, u.Glyph, u.Accent, gr);
            float x = gr.Right + u.F(6), w = t.Right - x - u.F(8);
            float th = u.Tiny.GetHeight(), nh = u.Title.GetHeight();
            float y = t.Y + (t.Height - th - nh) / 2f;
            Gfx.TextIn(g, caption, u.Tiny, u.Dim, new RectangleF(x, y, w, th), Gfx.Left);
            if (showChange) Gfx.TextIn(g, Pct(change), u.Tiny, Trend(change, u), new RectangleF(x, y, w, th), Gfx.Right);
            Gfx.TextIn(g, value, u.Title, u.Ink, new RectangleF(x, y + th * 0.95f, w, nh), Gfx.Left);
        }

        public override bool Live { get { return true; } }
    }

    // ==================================================================
    // Info
    // ==================================================================
    class CryptoWidget : RotatingWidget
    {
        public CryptoWidget() : base("crypto", "Crypto prices", "Info", "\uE8C7", "Bitcoin, Ethereum and more, with the 24-hour change (CoinGecko, free).") { }
        public override bool Multi { get { return true; } }
        public override int Width(Ui u) { return u.FI(140); }
        string Coins { get { return Opt("coins", "bitcoin,ethereum").ToLowerInvariant().Replace(" ", ""); } }
        string Cur { get { return Opt("currency", "eur").ToLowerInvariant(); } }

        public override void Tick(BarForm bar)
        {
            string coins = Coins, cur = Cur, key = "crypto:" + Key;
            PollEvery(Math.Max(1, OptInt("refresh", 5)) * 60, true, () => Online.Put(key, Online.Crypto(coins, cur)));
        }

        protected override void OnOptionsChanged() { PollSoon(); }

        List<Quote> Data
        {
            get
            {
                if (Svc.Demo) return new List<Quote> { new Quote { Symbol = "BTC", Price = 84210, Change = 1.24 }, new Quote { Symbol = "ETH", Price = 3125.4, Change = -0.6 } };
                return Online.Get<List<Quote>>("crypto:" + Key) ?? new List<Quote>();
            }
        }

        static string Money(double v, string cur)
        {
            string sym = cur == "eur" ? " €" : cur == "usd" ? "$" : cur == "gbp" ? "£" : " " + cur.ToUpperInvariant();
            string n = v.ToString(v >= 1000 ? "#,0" : v >= 1 ? "#,0.00" : "0.####", CultureInfo.CurrentCulture);
            return sym == "$" || sym == "£" ? sym + n : n + sym;
        }

        public override string LineText(Ui u) { var d = Data; if (d.Count == 0) return "Crypto"; var q = d[Pick(d.Count)]; return q.Symbol + " " + Money(q.Price, Cur); }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            var d = Data;
            if (d.Count == 0) { InfoTile(g, r, u, "\uE8C7", u.Dim, "Crypto", LastError ?? "Loading…", null); return; }
            Quote q = d[Pick(d.Count)];
            QuoteTile(g, r, u, "\uE8C7", q.Symbol, q.Change, true, Money(q.Price, Cur));
        }

        public override string Tip(Ui u, Rectangle r, Point p)
        {
            var d = Data;
            return d.Count == 0 ? "Crypto prices (CoinGecko)" : string.Join("\n", d.Select(q => q.Symbol + "  " + Money(q.Price, Cur) + "  " + Pct(q.Change) + " (24 h)").ToArray()) + "\nClick: CoinGecko";
        }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b) { if (b == MouseButtons.Left) Win.Launch("https://www.coingecko.com/"); }

        public override void Options(IWidgetOptions o)
        {
            o.Text("Coins", "CoinGecko ids, separated by commas: bitcoin, ethereum, solana, dogecoin…", "coins", "bitcoin,ethereum");
            o.Choice("Currency", null, "currency", new[] { "eur", "usd", "gbp", "uah" }, new[] { "EUR", "USD", "GBP", "UAH" }, "eur");
            o.Number("Refresh every", null, "refresh", 1, 120, 5, " min");
        }
    }

    class CurrencyWidget : RotatingWidget
    {
        public CurrencyWidget() : base("currency", "Exchange rates", "Info", "\uE8C7", "Currency rates from the European Central Bank (frankfurter.app, free).") { }
        public override bool Multi { get { return true; } }
        public override int Width(Ui u) { return u.FI(140); }
        string Base { get { return Opt("base", "EUR").ToUpperInvariant(); } }
        string[] Symbols { get { return Opt("symbols", "USD,GBP,UAH").ToUpperInvariant().Split(',').Select(s => s.Trim()).Where(s => s.Length == 3).ToArray(); } }

        public override void Tick(BarForm bar)
        {
            string b = Base;
            if (Rates.Stale(b)) PollEvery(Math.Max(15, OptInt("refresh", 60)) * 60, true, () => Rates.Fetch(b));
        }

        protected override void OnOptionsChanged() { PollSoon(); }

        public override string LineText(Ui u)
        {
            var rates = Rates.For(Base);
            string[] s = Symbols;
            if (rates == null || s.Length == 0) return Base;
            string sym = s[Pick(s.Length)];
            double v; rates.TryGetValue(sym, out v);
            return "1 " + Base + " = " + v.ToString("0.####", CultureInfo.CurrentCulture) + " " + sym;
        }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            var rates = Rates.For(Base);
            string[] s = Symbols;
            if (rates == null || s.Length == 0) { InfoTile(g, r, u, "\uE8C7", u.Dim, "Exchange rates", LastError ?? "Loading…", null); return; }
            string sym = s[Pick(s.Length)];
            double v;
            if (!rates.TryGetValue(sym, out v)) { InfoTile(g, r, u, "\uE8C7", u.Dim, sym, "Not available", u.Bold); return; }
            QuoteTile(g, r, u, "\uE8C7", "1 " + Base, 0, false, v.ToString(v >= 100 ? "0.0" : v >= 10 ? "0.00" : "0.0000", CultureInfo.CurrentCulture) + " " + sym);
        }

        public override string Tip(Ui u, Rectangle r, Point p)
        {
            var rates = Rates.For(Base);
            if (rates == null) return "Exchange rates";
            return string.Join("\n", Symbols.Where(rates.ContainsKey).Select(x => "1 " + Base + " = " + rates[x].ToString("0.####", CultureInfo.CurrentCulture) + " " + x).ToArray());
        }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b) { if (b == MouseButtons.Left) bar.ToggleFlyout("convert", r, () => new ConverterFlyout(bar.U.Scale, "Currency")); }

        public override void Options(IWidgetOptions o)
        {
            o.Text("From", "A currency code", "base", "EUR");
            o.Text("To", "Codes separated by commas", "symbols", "USD,GBP,UAH");
            o.Number("Refresh every", null, "refresh", 15, 720, 60, " min");
        }
    }

    class StocksWidget : RotatingWidget
    {
        public StocksWidget() : base("stocks", "Stocks", "Info", "\uE9D2", "Stock prices and today's change (Yahoo Finance's public data; delayed).") { }
        public override bool Multi { get { return true; } }
        public override int Width(Ui u) { return u.FI(132); }
        string Symbols { get { return Opt("symbols", "AAPL,MSFT").ToUpperInvariant().Replace(" ", ""); } }

        public override void Tick(BarForm bar)
        {
            string s = Symbols, key = "stocks:" + Key;
            PollEvery(Math.Max(1, OptInt("refresh", 5)) * 60, true, () => Online.Put(key, Online.Stocks(s)));
        }

        protected override void OnOptionsChanged() { PollSoon(); }

        List<Quote> Data
        {
            get
            {
                if (Svc.Demo) return new List<Quote> { new Quote { Symbol = "AAPL", Price = 227.52, Change = 0.84 }, new Quote { Symbol = "MSFT", Price = 431.2, Change = -0.31 } };
                return Online.Get<List<Quote>>("stocks:" + Key) ?? new List<Quote>();
            }
        }

        public override string LineText(Ui u) { var d = Data; if (d.Count == 0) return "Stocks"; var q = d[Pick(d.Count)]; return q.Symbol + " " + q.Price.ToString("0.00", CultureInfo.CurrentCulture); }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            var d = Data;
            if (d.Count == 0) { InfoTile(g, r, u, "\uE9D2", u.Dim, "Stocks", LastError ?? "Loading…", null); return; }
            Quote q = d[Pick(d.Count)];
            QuoteTile(g, r, u, "\uE9D2", q.Symbol, q.Change, true, q.Price.ToString(q.Price >= 1000 ? "#,0.0" : "0.00", CultureInfo.CurrentCulture));
        }

        public override string Tip(Ui u, Rectangle r, Point p)
        {
            var d = Data;
            return d.Count == 0 ? "Stocks" : string.Join("\n", d.Select(q => q.Symbol + "  " + q.Price.ToString("0.00", CultureInfo.CurrentCulture) + "  " + Pct(q.Change) + " today").ToArray()) + "\nPrices are delayed";
        }

        public override void Options(IWidgetOptions o)
        {
            o.Text("Symbols", "Ticker symbols, separated by commas: AAPL, MSFT, ^GSPC, SAN.MC…", "symbols", "AAPL,MSFT");
            o.Number("Refresh every", null, "refresh", 1, 120, 5, " min");
        }
    }

    class AirQualityWidget : Widget
    {
        public AirQualityWidget() : base("air", "Air quality", "Time & weather", "\uE9CA", "The European air quality index and PM2.5 for your weather city (Open-Meteo).") { }
        public override bool HasLabel { get { return true; } }
        public override int Width(Ui u) { return u.FI(128); }
        double[] data;

        public override void Tick(BarForm bar)
        {
            double lat, lon; string name;
            if (!Geo.Get(out lat, out lon, out name)) return;
            PollEvery(30 * 60, true, () => { data = Online.Air(lat, lon); });
        }

        double[] Data { get { return Svc.Demo ? new double[] { 23, 6.1, 11 } : data; } }

        static string Level(double aqi) { return aqi < 20 ? "Good" : aqi < 40 ? "Fair" : aqi < 60 ? "Moderate" : aqi < 80 ? "Poor" : aqi < 100 ? "Very poor" : "Extremely poor"; }
        static Color LevelColor(double aqi) { return aqi < 20 ? Color.FromArgb(74, 222, 128) : aqi < 40 ? Color.FromArgb(163, 230, 53) : aqi < 60 ? Color.FromArgb(250, 204, 21) : aqi < 80 ? Color.FromArgb(251, 146, 60) : Color.FromArgb(251, 113, 133); }

        public override string LineText(Ui u) { var d = Data; return d == null ? "AQI …" : "AQI " + (int)d[0] + " " + Level(d[0]); }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            var d = Data;
            if (d == null) { double a, b; string n; InfoTile(g, r, u, "\uE9CA", u.Dim, "Air quality", Geo.Get(out a, out b, out n) ? (LastError ?? "Loading…") : "Set a city", null); return; }
            InfoTile(g, r, u, "\uE9CA", LevelColor(d[0]), "Air · " + Level(d[0]), "AQI " + (int)Math.Round(d[0]), null);
        }

        public override string Tip(Ui u, Rectangle r, Point p)
        {
            var d = Data;
            return d == null ? "Air quality (needs your weather city)" : "European AQI " + (int)d[0] + " (" + Level(d[0]) + ")\nPM2.5 " + d[1].ToString("0.0") + " µg/m³  ·  PM10 " + d[2].ToString("0.0") + " µg/m³";
        }
    }

    class RssWidget : RotatingWidget
    {
        public RssWidget() : base("rss", "Headlines", "Info", "\uE8A1", "Headlines from news feeds you choose (RSS or Atom). Click one to read it.") { }
        public override bool Multi { get { return true; } }
        public override int Width(Ui u) { return u.FI(230); }
        public override int CompactWidth(Ui u) { return u.FI(150); }
        const string DefaultFeeds = "https://feeds.bbci.co.uk/news/world/rss.xml, https://news.ycombinator.com/rss";
        string[] Feeds { get { return Opt("feeds", DefaultFeeds).Split(new[] { ',', ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries).Where(s => s.StartsWith("http")).ToArray(); } }

        public override void Tick(BarForm bar)
        {
            string[] f = Feeds;
            string key = "rss:" + Key;
            PollEvery(Math.Max(5, OptInt("refresh", 15)) * 60, true, () => Online.Put(key, Online.Feeds(f)));
        }

        protected override void OnOptionsChanged() { PollSoon(); }

        List<KeyValuePair<string, string>> Data
        {
            get
            {
                if (Svc.Demo) return new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("Scientists map the ocean floor in record detail", ""), new KeyValuePair<string, string>("Show HN: a tiny taskbar written in C#", "") };
                return Online.Get<List<KeyValuePair<string, string>>>("rss:" + Key) ?? new List<KeyValuePair<string, string>>();
            }
        }

        int Index { get { int n = Data.Count; return n == 0 ? 0 : (int)((DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond / 8) % n); } }
        public override string LineText(Ui u) { var d = Data; return d.Count == 0 ? "Headlines" : d[Index].Key; }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            var d = Data;
            if (d.Count == 0) { InfoTile(g, r, u, "\uE8A1", u.Dim, "Headlines", LastError ?? "Loading…", u.Bold); return; }
            int i = Index;
            InfoTile(g, r, u, "\uE8A1", u.Accent, "Headlines · " + (i + 1) + "/" + d.Count, d[i].Key, u.Bold);
        }

        public override string Tip(Ui u, Rectangle r, Point p) { var d = Data; return d.Count == 0 ? "Headlines" : d[Index].Key + "\nClick to read it; right-click for the latest headlines"; }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            var d = Data;
            if (b == MouseButtons.Left) { if (d.Count > 0 && d[Index].Value.Length > 0) Win.Launch(d[Index].Value); return; }
            if (b != MouseButtons.Right) return;
            ContextMenuStrip m = bar.NewMenu(false);
            foreach (var h in d.Take(12))
            {
                string link = h.Value;
                BarForm.Item(m, Win.ShortTitle(h.Key, 70).Replace("&", "&&"), () => { if (link.Length > 0) Win.Launch(link); });
            }
            if (d.Count > 0) m.Items.Add(new ToolStripSeparator());
            BarForm.Item(m, "Refresh", () => PollSoon());
            bar.ShowMenu(m, p);
        }

        public override void Options(IWidgetOptions o)
        {
            o.Text("Feeds", "RSS or Atom links, separated by commas", "feeds", DefaultFeeds);
            o.Number("Refresh every", null, "refresh", 5, 240, 15, " min");
        }
    }

    // ==================================================================
    // Dev
    // ==================================================================
    class ClaudeWidget : Widget
    {
        public ClaudeWidget() : base("claude", "Claude Code usage", "Dev", "\uE943", "Tokens and sessions today, read from Claude Code's local logs. Nothing is sent anywhere.") { }
        public override string[] Styles { get { return new[] { "All tokens", "Without cache" }; } }
        public override bool HasLabel { get { return true; } }
        public override int Width(Ui u) { return u.FI(142); }
        public override void Tick(BarForm bar) { PollEvery(60, false, ClaudeUsage.Poll); }
        long Tokens { get { return Style == "Without cache" ? ClaudeUsage.Input + ClaudeUsage.Output : ClaudeUsage.Total; } }
        public override string LineText(Ui u) { return ClaudeUsage.Short(Tokens) + " tokens"; }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            if (!ClaudeUsage.Found && !Svc.Demo) { InfoTile(g, r, u, "\uE943", u.Dim, "Claude Code", "Not found", null); return; }
            InfoTile(g, r, u, "\uE943", u.Accent, "Today · " + ClaudeUsage.Sessions + " session" + (ClaudeUsage.Sessions == 1 ? "" : "s"), ClaudeUsage.Short(Tokens) + " tokens", null);
        }

        public override string Tip(Ui u, Rectangle r, Point p)
        {
            return "Claude Code today (from ~/.claude, local only)\nInput " + ClaudeUsage.Short(ClaudeUsage.Input) + "  ·  output " + ClaudeUsage.Short(ClaudeUsage.Output) +
                   "\nCache read " + ClaudeUsage.Short(ClaudeUsage.CacheRead) + "  ·  cache write " + ClaudeUsage.Short(ClaudeUsage.CacheWrite) +
                   "\n" + ClaudeUsage.Messages + " replies in " + ClaudeUsage.Sessions + " session" + (ClaudeUsage.Sessions == 1 ? "" : "s");
        }
    }

    class GitHubWidget : Widget
    {
        public GitHubWidget() : base("github", "GitHub activity", "Dev", "\uE943", "Your public GitHub contribution calendar, the last few months.") { }
        public override bool Multi { get { return true; } }
        public override int Width(Ui u) { return u.FI(184); }
        public override int CompactWidth(Ui u) { return u.FI(120); }
        string User { get { return Opt("user", "").Trim(); } }
        SortedDictionary<DateTime, int> data;

        public override void Tick(BarForm bar)
        {
            string user = User;
            if (user.Length > 0) PollEvery(6 * 3600, true, () => { data = Online.GitHub(user); });
        }

        protected override void OnOptionsChanged() { data = null; PollSoon(); }

        SortedDictionary<DateTime, int> Data
        {
            get
            {
                if (!Svc.Demo) return data;
                var d = new SortedDictionary<DateTime, int>();
                var rnd = new Random(11);
                for (int i = 0; i < 200; i++) d[DateTime.Today.AddDays(-i)] = rnd.Next(10) < 4 ? 0 : rnd.Next(1, 5);
                return d;
            }
        }

        public override string LineText(Ui u) { var d = Data; return d == null ? "GitHub" : d.Where(kv => kv.Key > DateTime.Today.AddDays(-7)).Count(kv => kv.Value > 0) + "/7 days"; }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            var d = Data;
            if (User.Length == 0 && !Svc.Demo) { InfoTile(g, r, u, "\uE943", u.Dim, "GitHub", "Set a username", u.Bold); return; }
            if (d == null) { InfoTile(g, r, u, "\uE943", u.Dim, "GitHub · " + User, LastError ?? "Loading…", u.Bold); return; }
            RectangleF t = u.Tile(r);
            float cell = Math.Max(3f, (t.Height - u.F(12)) / 7f), gap = Math.Max(1f, cell * 0.22f), sq = cell - gap;
            int weeks = Math.Max(4, (int)((t.Width - u.F(16)) / cell));
            DateTime end = DateTime.Today, start = end.AddDays(-(int)end.DayOfWeek - 7 * (weeks - 1));
            float x0 = t.X + (t.Width - weeks * cell + gap) / 2f, y0 = t.Y + (t.Height - 7 * cell + gap) / 2f;
            for (int w = 0; w < weeks; w++)
                for (int dd = 0; dd < 7; dd++)
                {
                    DateTime day = start.AddDays(w * 7 + dd);
                    if (day > end) continue;
                    int lv; d.TryGetValue(day, out lv);
                    Color c = lv == 0 ? Color.FromArgb(u.P.Light ? 22 : 26, u.Ink) : Color.FromArgb(new[] { 0, 90, 140, 200, 255 }[Math.Min(4, lv)], u.Accent);
                    Theme.FillRound(g, new RectangleF(x0 + w * cell, y0 + dd * cell, sq, sq), Math.Min(sq / 3f, u.F(1.5f)), c);
                }
        }

        public override string Tip(Ui u, Rectangle r, Point p)
        {
            var d = Data;
            if (d == null) return "GitHub activity" + (User.Length > 0 ? " for " + User : "\nSet a username in the widget settings");
            int days = d.Where(kv => kv.Key > DateTime.Today.AddDays(-30)).Count(kv => kv.Value > 0);
            return User + ": active " + days + " of the last 30 days\nClick to open the profile";
        }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            if (b != MouseButtons.Left) return;
            if (User.Length == 0) bar.OpenSettings("w:" + Key); else Win.Launch("https://github.com/" + Uri.EscapeDataString(User));
        }

        public override void Options(IWidgetOptions o) { o.Text("GitHub username", "Public contributions only; no sign-in", "user", ""); }
    }

    class DownloadWidget : Widget
    {
        public DownloadWidget() : base("download", "Latest download", "Dev", "\uE896", "The newest file in Downloads. Click to open it, or drag it out of the bar.") { }
        public override bool HasLabel { get { return true; } }
        public override int Width(Ui u) { return u.FI(176); }
        public override int CompactWidth(Ui u) { return u.FI(120); }
        public override string LineText(Ui u) { string f = Downloads.Newest; return f == null ? "No downloads" : Path.GetFileName(f); }

        static string Ago(string f)
        {
            try
            {
                TimeSpan t = Svc.Demo ? TimeSpan.FromMinutes(4) : DateTime.Now - File.GetLastWriteTime(f);
                return t.TotalMinutes < 1 ? "just now" : t.TotalHours < 1 ? (int)t.TotalMinutes + " min ago" : t.TotalDays < 1 ? (int)t.TotalHours + " h ago" : (int)t.TotalDays + " days ago";
            }
            catch { return ""; }
        }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            string f = Downloads.Newest;
            if (f == null) { InfoTile(g, r, u, "\uE896", u.Dim, "Downloads", "Nothing yet", u.Bold); return; }
            RectangleF t = u.Tile(r);
            float is_ = Math.Min(t.Height - u.F(14), u.F(28));
            var ir = new RectangleF(t.X + u.F(9), t.Y + (t.Height - is_) / 2f, is_, is_);
            Bitmap icon = Svc.Demo ? null : u.Icon(f);
            if (icon != null) g.DrawImage(icon, ir); else Gfx.Glyph(g, "\uE8A5", u.Glyph, u.Accent, ir);
            float x = ir.Right + u.F(7), w = t.Right - x - u.F(8);
            float th = ShowLabel ? u.Tiny.GetHeight() : 0, nh = u.Bold.GetHeight();
            float y = t.Y + (t.Height - th - nh) / 2f;
            if (ShowLabel) Gfx.TextIn(g, "Downloaded " + Ago(f), u.Tiny, u.Dim, new RectangleF(x, y, w, th), Gfx.Left);
            Gfx.TextIn(g, Path.GetFileName(f), u.Bold, u.Ink, new RectangleF(x, y + th, w, nh), Gfx.Left);
        }

        public override string Tip(Ui u, Rectangle r, Point p) { string f = Downloads.Newest; return f == null ? "Downloads is empty" : Path.GetFileName(f) + "\nClick to open; drag it out; right-click for more"; }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            string f = Downloads.Newest;
            if (b == MouseButtons.Left) { if (f != null) Win.Launch(f); else Win.Launch(Downloads.Folder); return; }
            if (b != MouseButtons.Right) return;
            ContextMenuStrip m = bar.NewMenu(false);
            if (f != null)
            {
                BarForm.Item(m, "Open", () => Win.Launch(f));
                BarForm.Item(m, "Show in folder", () => { try { Process.Start("explorer.exe", "/select,\"" + f + "\""); } catch { } });
                BarForm.Item(m, "Copy path", () => { try { Clipboard.SetText(f); } catch { } });
                m.Items.Add(new ToolStripSeparator());
            }
            BarForm.Item(m, "Open Downloads", () => Win.Launch(Downloads.Folder));
            bar.ShowMenu(m, p);
        }

        public override bool DragOut(BarForm bar)
        {
            string f = Downloads.Newest;
            if (f == null || !File.Exists(f)) return false;
            var data = new DataObject(DataFormats.FileDrop, new[] { f });
            bar.DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            return true;
        }
    }

    // ==================================================================
    // Your own
    // ==================================================================
    class CustomWidget : Widget
    {
        public CustomWidget() : base("custom", "Custom (script)", "Custom", "\uE756", "Runs a command or PowerShell line every so often and shows what it prints. Like a Rainmeter or Polybar module.") { }
        public override bool Multi { get { return true; } }
        public override bool HasLabel { get { return true; } }
        public override int Width(Ui u) { return u.FI(Math.Max(60, Math.Min(400, OptInt("width", 150)))); }
        volatile string output;

        string Display { get { return Opt("display", "text"); } }
        string Label { get { return Opt("label", "My script"); } }

        public override void Tick(BarForm bar)
        {
            string cmd = Opt("command", "echo Hello");
            bool ps = Opt("shell", "cmd") == "powershell";
            if (cmd.Trim().Length == 0) return;
            PollEvery(Math.Max(2, OptInt("every", 60)), true, () => { output = CustomRunner.Run(cmd, ps, 10000); });
        }

        protected override void OnOptionsChanged() { PollSoon(); }

        string Out { get { return Svc.Demo ? "42%" : output ?? "…"; } }
        public override string LineText(Ui u) { return Out; }

        string GlyphOpt
        {
            get
            {
                string s = Opt("glyph", "E756").Trim();
                int code;
                if (s.Length >= 4 && int.TryParse(s.TrimStart('\\', 'u', 'U'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code)) return ((char)code).ToString();
                return s.Length > 0 ? s.Substring(0, 1) : "\uE756";
            }
        }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            string d = Display;
            if (d == "meter")
            {
                RectangleF t = u.Tile(r);
                double v;
                var m = System.Text.RegularExpressions.Regex.Match(Out, @"-?\d+([.,]\d+)?");
                double.TryParse(m.Success ? m.Value.Replace(',', '.') : "0", NumberStyles.Float, CultureInfo.InvariantCulture, out v);
                float max = Math.Max(1, OptInt("max", 100));
                float x = t.X + u.F(11), w = t.Width - u.F(22);
                float th = ShowLabel ? u.Tiny.GetHeight() : 0, nh = u.Title.GetHeight(), mh = Math.Max(3f, u.F(5));
                float y = t.Y + (t.Height - th - nh - mh - u.F(4)) / 2f;
                if (ShowLabel) Gfx.TextIn(g, Label, u.Tiny, u.Dim, new RectangleF(x, y, w, th), Gfx.Left);
                Gfx.TextIn(g, Out, u.Title, u.Ink, new RectangleF(x, y + th * 0.95f, w, nh), Gfx.Left);
                Gfx.Meter(g, new RectangleF(x, y + th + nh + u.F(3), w, mh), (float)(100 * v / max), Gfx.Load((float)(100 * v / max), u.Accent), u.P.Track);
                return;
            }
            if (d == "glyph") { InfoTile(g, r, u, GlyphOpt, u.Accent, Label, Out, u.Bold); return; }
            RectangleF tt = u.Tile(r);
            float tth = ShowLabel ? u.Tiny.GetHeight() : 0, tnh = u.Bold.GetHeight();
            float ty = tt.Y + (tt.Height - tth - tnh) / 2f;
            if (ShowLabel) Gfx.TextIn(g, Label, u.Tiny, u.Dim, new RectangleF(tt.X + u.F(11), ty, tt.Width - u.F(22), tth), Gfx.Left);
            Gfx.TextIn(g, Out, u.Bold, u.Ink, new RectangleF(tt.X + u.F(11), ty + tth, tt.Width - u.F(22), tnh), Gfx.Left);
        }

        public override string Tip(Ui u, Rectangle r, Point p) { return Label + ": " + Out + "\n" + Opt("command", "echo Hello"); }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b) { if (b == MouseButtons.Left) PollSoon(); }

        public override void Options(IWidgetOptions o)
        {
            o.Text("Command", "What to run. Its first line of output is shown", "command", "echo Hello");
            o.Choice("Run it with", null, "shell", new[] { "cmd", "powershell" }, new[] { "Command Prompt", "PowerShell" }, "cmd");
            o.Number("Every", null, "every", 2, 3600, 60, " s");
            o.Choice("Show", null, "display", new[] { "text", "meter", "glyph" }, new[] { "Text", "Number + meter", "Icon + text" }, "text");
            o.Text("Label", "The small caption", "label", "My script");
            o.Number("Meter maximum", "For Number + meter", "max", 1, 100000, 100, "");
            o.Text("Icon", "A Segoe Fluent Icons code like E756, or any character", "glyph", "E756");
            o.Number("Width", null, "width", 60, 400, 150, " px");
            o.Note("Clicking it runs the command again, unless you set a click action above.");
        }
    }

    class LaunchTileWidget : Widget
    {
        public LaunchTileWidget() : base("launch", "Launcher tile", "Custom", "\uE8A7", "A tile that opens an app, a file, a folder or a website.") { }
        public override bool Multi { get { return true; } }
        public override bool HasLabel { get { return true; } }
        public override bool Sizable { get { return false; } }
        string Target { get { return Opt("target", "https://www.wikipedia.org"); } }
        string Label { get { string l = Opt("label", ""); if (l.Length > 0) return l; string t = Target; if (t.Contains("://")) { try { return new Uri(t).Host.Replace("www.", ""); } catch { } } try { return Path.GetFileNameWithoutExtension(t.TrimEnd('\\')); } catch { return t; } } }
        bool Wide { get { return ShowLabel && Opt("label", "").Length > 0; } }
        public override int Width(Ui u) { return Wide ? u.Cell + (int)Math.Min(u.F(140), Gfx.TW(Label, u.Bold) + u.F(14)) : u.Cell; }
        public override string LineText(Ui u) { return Label; }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            string t = Target;
            var cell = new Rectangle(r.X, r.Y, u.Cell, r.Height);
            float ic = u.S(Config.IconSize) * 0.86f;
            var ir = new RectangleF(cell.X + (cell.Width - ic) / 2f, cell.Y + (cell.Height - ic) / 2f - u.S(1.5f) * hover, ic, ic);
            Bitmap icon = t.Contains("://") || Svc.Demo ? null : u.Icon(t);
            if (icon != null) g.DrawImage(icon, ir);
            else Gfx.Glyph(g, t.Contains("://") ? "\uE774" : "\uE8A7", u.GlyphBig, u.Ink, new RectangleF(cell.X, cell.Y - u.S(1.5f) * hover, cell.Width, cell.Height));
            if (Wide) Gfx.TextIn(g, Label, u.Bold, u.Ink, new RectangleF(cell.Right - u.F(4), r.Y, r.Right - cell.Right, r.Height), Gfx.Left);
        }

        public override string Tip(Ui u, Rectangle r, Point p) { return Label + "\n" + Target; }
        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b) { if (b == MouseButtons.Left) Win.Launch(Target); }

        public override void Options(IWidgetOptions o)
        {
            o.Text("Opens", "An app, a file, a folder or a web address", "target", "https://www.wikipedia.org");
            o.Text("Label", "Optional; shown next to the icon", "label", "");
        }
    }
}
