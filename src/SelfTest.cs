// --selftest: renders the bar, themes, flyouts, launcher, settings and gallery to PNGs
// with realistic demo data, without touching the taskbar or saving any settings.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace StayBar
{
    static class DemoData
    {
        static MediaInfo media;
        static WeatherInfo weather;

        public static MediaInfo DemoMedia()
        {
            if (media != null) return media;
            var m = new MediaInfo();
            m.Title = "Midnight City"; m.Artist = "M83"; m.Album = "Hurry Up, We're Dreaming"; m.AppName = "Spotify"; m.AppId = "Spotify.exe";
            m.Playing = true; m.CanNext = true; m.CanPrev = true; m.DurSec = 243; m.PosSec = 97; m.Stamp = DateTime.UtcNow;
            var art = new Bitmap(96, 96, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(art))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var lb = new LinearGradientBrush(new Rectangle(0, 0, 96, 96), Color.FromArgb(76, 29, 149), Color.FromArgb(236, 72, 153), 45f)) g.FillRectangle(lb, 0, 0, 96, 96);
                using (var b = new SolidBrush(Color.FromArgb(200, 253, 224, 71))) g.FillEllipse(b, 30, 22, 36, 36);
                using (var b = new SolidBrush(Color.FromArgb(120, 15, 23, 42))) g.FillRectangle(b, 0, 62, 96, 34);
            }
            m.Art = art; m.ArtColor = Color.FromArgb(192, 90, 230);
            media = m;
            return m;
        }

        public static WeatherInfo DemoWeather()
        {
            if (weather != null) return weather;
            var w = new WeatherInfo();
            w.City = "London"; w.Temp = 18.4; w.Feels = 16.9; w.Code = 2; w.Day = true; w.Wind = 12; w.Humidity = 54; w.Fetched = DateTime.Now;
            int[] codes = { 2, 0, 1, 61, 80, 3, 0 };
            double[] max = { 19, 21, 22, 17, 15, 16, 20 }, min = { 9, 10, 12, 11, 8, 7, 9 };
            int[] rain = { 5, 0, 10, 80, 60, 20, 0 };
            w.Days = Enumerable.Range(0, 7).Select(i => DateTime.Today.AddDays(i)).ToArray();
            w.Codes = codes; w.Max = max; w.Min = min; w.Rain = rain;
            weather = w;
            return w;
        }

        public static void Fill()
        {
            Svc.Demo = true;
            Media.Now = DemoMedia();
            Weather.Now = DemoWeather();
            Sys.Cpu = 23; Sys.Ram = 61; Sys.Gpu = 37; Sys.TempC = 64; Sys.RamUsedGb = 9.8; Sys.RamTotalGb = 16; Sys.Down = 2.4 * 1048576; Sys.Up = 180 * 1024;
            var rnd = new Random(7);
            for (int i = 0; i < 48; i++)
            {
                Sys.CpuH.Push(15 + 25 * (float)Math.Abs(Math.Sin(i * 0.35)) + rnd.Next(8));
                Sys.RamH.Push(58 + rnd.Next(5));
                Sys.GpuH.Push(20 + 30 * (float)Math.Abs(Math.Sin(i * 0.2)) + rnd.Next(10));
                Sys.DownH.Push((float)(1048576 * (1 + 2 * Math.Abs(Math.Sin(i * 0.3))) + rnd.Next(200000)));
                Sys.UpH.Push((float)(150000 + rnd.Next(80000)));
            }
            Radios.Wifi = 1; Radios.Bt = 0; Radios.Network = "Home Wi-Fi";
            Brightness.Level = 80; Brightness.Supported = 1;
            Recycle.Items = 4; Recycle.Bytes = 12 * 1048576;
            Disk.FreeGb = 214; Disk.TotalGb = 476;
            Keyboard.Code = "ENG"; Keyboard.Region = "US";
            ScreenTime.DemoFill();
            Focus.DemoFill();
            Clip.Items.Clear();
            Clip.Items.Add("https://github.com/staybar/staybar");
            Clip.Items.Add("Meeting moved to 15:30, room 4B");
            Clip.Items.Add("git commit -m \"Add widget gallery\"");
            Note.Text = "Call mom at 7\nBuy coffee beans";
            DiskIo.DemoFill();
            NetInfo.Poll(); Wlan.Poll(); BatteryHealth.Poll(); Privacy.Poll(); Usb.Poll(); ClaudeUsage.Poll(); Downloads.Poll();
        }

        // Gallery previews: borrow demo data only where real data is missing.
        public static void With(Action a)
        {
            bool swapM = Media.Now == null;
            WeatherInfo w = Weather.Now;
            bool swapW = w == null || w.Error != null;
            if (swapM) Media.Now = DemoMedia();
            if (swapW) Weather.Now = DemoWeather();
            try { a(); }
            finally
            {
                if (swapM && Media.Now == media) Media.Now = null;
                if (swapW && Weather.Now == weather) Weather.Now = w;
            }
        }
    }

    static class SelfTest
    {
        static Bitmap wallpaper;

        // The user's own wallpaper, blurred like acrylic, as a backdrop for previews.
        public static void Wallpaper(Graphics g, Rectangle r)
        {
            try
            {
                if (wallpaper == null)
                {
                    string p = WallpaperColor.WallpaperPath();
                    if (p != null)
                        using (var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var img = Image.FromStream(fs))
                        {
                            Rectangle sb = Screen.PrimaryScreen.Bounds;
                            var full = new Bitmap(sb.Width, sb.Height);
                            using (Graphics fg = Graphics.FromImage(full))
                            {
                                float k = Math.Max(sb.Width / (float)img.Width, sb.Height / (float)img.Height);
                                float w = img.Width * k, h = img.Height * k;
                                fg.DrawImage(img, (sb.Width - w) / 2, (sb.Height - h) / 2, w, h);
                            }
                            wallpaper = full;
                        }
                }
            }
            catch { }
            if (wallpaper == null)
            {
                using (var bg = new LinearGradientBrush(r, Color.FromArgb(54, 80, 150), Color.FromArgb(160, 70, 120), 0f)) g.FillRectangle(bg, r);
                return;
            }
            var src = new Rectangle(0, Math.Max(0, wallpaper.Height - r.Height), Math.Min(wallpaper.Width, r.Width), Math.Min(wallpaper.Height, r.Height));
            using (var small = new Bitmap(Math.Max(1, src.Width / 14), Math.Max(1, src.Height / 14)))
            {
                using (Graphics sg = Graphics.FromImage(small))
                {
                    sg.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    sg.DrawImage(wallpaper, new Rectangle(0, 0, small.Width, small.Height), src, GraphicsUnit.Pixel);
                }
                var old = g.InterpolationMode;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(small, r);
                g.InterpolationMode = old;
            }
            if (r.Width > src.Width) using (var b = new SolidBrush(Color.FromArgb(40, 40, 60))) g.FillRectangle(b, src.Width, r.Y, r.Width - src.Width, r.Height);
        }

        static string Out(string name) { return Path.Combine(Program.DataDir, name); }

        public static int Run(string what)
        {
            Native.MakeDpiAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (what == "data") return RealData();
            DemoData.Fill();
            var bar = new BarForm(true);
            bar.RefreshGroups();
            var sb = new StringBuilder();
            sb.AppendLine("scale=" + bar.U.Scale + "  glyphFont=" + Gfx.GlyphFamily() + "  style=" + Config.Style + "  theme=" + Config.ThemeId);
            foreach (AppGroup g in bar.Groups)
                sb.AppendLine(string.Format("{0,-30} pinned={1,-5} folder={2,-5} windows={3} icon={4} launch={5}",
                    g.Name, g.Pinned, g.IsFolder, g.Windows.Count, g.Icon != null ? g.Icon.Width + "px" : "none", g.LaunchPath));
            int width = Screen.PrimaryScreen.Bounds.Width;
            if (what == "bench") { Bench(bar, width); return 0; }
            using (Bitmap bmp = bar.RenderPreview(width)) bmp.Save(Out("selftest.png"), ImageFormat.Png);

            string theme = Config.ThemeId, style = Config.Style;
            var rows = new List<KeyValuePair<string, Bitmap>>();
            Config.Style = "tiles";
            foreach (Palette p in Palette.All)
            {
                Config.ThemeId = p.Id;
                bar.U.Build(bar.U.Scale);
                rows.Add(new KeyValuePair<string, Bitmap>(p.Name + " (tiles)", bar.RenderPreview(width)));
            }
            Config.Style = "dock";
            int iconSize = Config.IconSize; bool blur = Config.Blur;
            foreach (Palette p in Palette.All)
            {
                Config.ThemeId = p.Id;
                bar.U.Build(bar.U.Scale);
                rows.Add(new KeyValuePair<string, Bitmap>(p.Name + " (dock)", bar.RenderPreview(width)));
            }
            foreach (int size in new[] { 24, 38 })
            {
                Config.ThemeId = "glass"; Config.IconSize = size;
                bar.U.Build(bar.U.Scale);
                rows.Add(new KeyValuePair<string, Bitmap>("Glass (dock, " + (size < 30 ? "small" : "large") + ")", bar.RenderPreview(width)));
            }
            Config.IconSize = iconSize;
            Config.Blur = false;
            foreach (string t in new[] { "midnight", "light" })
            {
                Config.ThemeId = t;
                bar.U.Build(bar.U.Scale);
                rows.Add(new KeyValuePair<string, Bitmap>(Palette.Get(t).Name + " (dock, blur off)", bar.RenderPreview(width)));
            }
            Config.Blur = blur;
            Stack(rows, Out("selftest-themes.png"));
            Config.ThemeId = theme; Config.Style = style;
            bar.U.Build(bar.U.Scale);

            // a crowded bar: compact widgets kick in so the apps keep their size
            List<string> right = Config.Zones[Config.ZRight];
            Config.Zones[Config.ZRight] = Config.SplitItems("media,weather,screentime,cpu,net,volume,brightness,connect,battery,focus,keyboard,clock,desktop");
            using (Bitmap bmp = bar.RenderPreview(width)) bmp.Save(Out("selftest-crowded.png"), ImageFormat.Png);
            Config.Zones[Config.ZRight] = right;

            // layouts: zones, spacers, separators, widths, edit mode
            Layouts(bar, width);
            // every widget, by category
            WidgetSheet(bar, width);

            // flyouts + launcher
            float sc = bar.U.Scale;
            AppIndex.BuildNow();
            var fly = new List<KeyValuePair<string, Bitmap>>();
            var windows = new GlassWindow[]
            {
                new WeatherFlyout(sc), new CalendarFlyout(sc), new ScreenTimeFlyout(bar), new ClipboardFlyout(sc), new NoteFlyout(sc),
            };
            foreach (GlassWindow w in windows) { fly.Add(new KeyValuePair<string, Bitmap>(w.GetType().Name, w.Render())); w.Dispose(); }
            var launcher = new LauncherForm(bar);
            launcher.Demo("code");
            fly.Add(new KeyValuePair<string, Bitmap>("Launcher", launcher.Render()));
            launcher.Dispose();
            Side(fly, Out("selftest-flyouts.png"));
            // the phase-3 flyouts
            var fly2 = new List<KeyValuePair<string, Bitmap>>();
            var windows2 = new GlassWindow[]
            {
                new TodoFlyout(sc), new HabitFlyout(sc), new EventsFlyout(sc, Ics.For("")), new ConverterFlyout(sc), new CalcFlyout(sc),
                new MixerFlyout(sc), new TranslateFlyout(sc, Widgets.Get("translate")),
            };
            foreach (GlassWindow w in windows2) { fly2.Add(new KeyValuePair<string, Bitmap>(w.GetType().Name, w.Render())); w.Dispose(); }
            Side(fly2, Out("selftest-flyouts2.png"));

            // settings pages + gallery, rendered off-screen
            var settings = new SettingsForm(bar, "Dock");
            settings.StartPosition = FormStartPosition.Manual;
            settings.Location = new Point(-20000, -20000);
            settings.ShowInTaskbar = false;
            settings.Show();
            foreach (string page in new[] { "Dock", "Appearance", "Widgets", "Apps", "Behavior", "Profiles", "Automations", "Screens", "General" })
            {
                settings.ShowPage(page);
                Application.DoEvents();
                using (var bmp = new Bitmap(settings.Width, settings.Height))
                {
                    settings.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                    bmp.Save(Out("selftest-settings-" + page.ToLowerInvariant() + ".png"), ImageFormat.Png);
                }
            }
            settings.ShowPage("w:cpu");
            Application.DoEvents();
            using (Bitmap bmp = settings.RenderPage()) bmp.Save(Out("selftest-settings-widget.png"), ImageFormat.Png);
            // the long pages in full (just the page, nothing scrolled away)
            foreach (string page in new[] { "Dock", "Widgets", "Behavior" })
            {
                settings.ShowPage(page);
                Application.DoEvents();
                using (Bitmap bmp = settings.RenderPage()) bmp.Save(Out("selftest-settings-" + page.ToLowerInvariant() + "-full.png"), ImageFormat.Png);
            }
            settings.Close();
            var gallery = new GalleryForm(bar);
            gallery.StartPosition = FormStartPosition.Manual;
            gallery.Location = new Point(-20000, -20000);
            gallery.ShowInTaskbar = false;
            gallery.Show();
            Application.DoEvents();
            using (var bmp = new Bitmap(gallery.Width, gallery.Height))
            {
                gallery.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                bmp.Save(Out("selftest-gallery.png"), ImageFormat.Png);
            }
            gallery.Close();

            sb.AppendLine("apps indexed for the launcher: " + AppIndex.Items.Count);
            File.WriteAllText(Out("selftest.txt"), sb.ToString());
            return 0;
        }

        // --selftest data: every data source for real (no demo data), once; what each widget would say.
        // Touches nothing; network sources are fetched once. Written to selftest-data.txt.
        static int RealData()
        {
            Config.Load();
            var sb = new StringBuilder();
            Action<string, Action> step = (name, a) =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try { a(); sb.AppendLine(string.Format("{0,-16} ok    {1,6} ms", name, sw.ElapsedMilliseconds)); }
                catch (Exception ex) { sb.AppendLine(string.Format("{0,-16} FAIL  {1,6} ms  {2}", name, sw.ElapsedMilliseconds, ex.GetType().Name + ": " + ex.Message)); }
            };
            step("proctop", () => { ProcTop.Sample(); System.Threading.Thread.Sleep(1000); ProcTop.Sample(); });
            step("diskio", () => { DiskIo.Sample(); System.Threading.Thread.Sleep(1000); DiskIo.Sample(); });
            step("netinfo", NetInfo.Poll);
            step("wlan", Wlan.Poll);
            step("batteryhealth", BatteryHealth.Poll);
            step("power", () => sb.AppendLine("    plans: " + string.Join(", ", PowerPlans.Plans().Select(p => p.Value).ToArray()) + "; mode " + PowerPlans.Mode + "; active " + PowerPlans.ActiveName));
            step("privacy", Privacy.Poll);
            step("usb", Usb.Poll);
            step("claude", ClaudeUsage.Poll);
            step("downloads", Downloads.Poll);
            step("audio", () => sb.AppendLine("    peak " + AudioX.Peak + "; mic muted " + AudioX.MicMuted + "; sessions " + string.Join(", ", AudioX.Sessions().Select(s => s.Name + " " + (int)(s.Volume * 100) + "%").ToArray()) +
                                                 "; outputs " + string.Join(", ", AudioX.Outputs().Select(o => o.Name + (o.Default ? "*" : "")).ToArray())));
            step("toggles", () => sb.AppendLine("    dark " + Toggles.Dark + "; night " + Toggles.NightLight + "; dnd " + Toggles.Dnd + "; game " + Toggles.GameMode + "; icons hidden " + Toggles.IconsHidden));
            step("rates", () => { Rates.Fetch("EUR"); var r = Rates.For("EUR"); sb.AppendLine("    EUR->USD " + r["USD"] + ", UAH " + (r.ContainsKey("UAH") ? r["UAH"].ToString() : "-")); });
            step("crypto", () => sb.AppendLine("    " + string.Join(", ", Online.Crypto("bitcoin,ethereum", "eur").Select(q => q.Symbol + " " + q.Price + " " + q.Change.ToString("0.0")).ToArray())));
            step("stocks", () => sb.AppendLine("    " + string.Join(", ", Online.Stocks("AAPL,MSFT").Select(q => q.Symbol + " " + q.Price + " " + q.Change.ToString("0.0")).ToArray())));
            double lat, lon; string city;
            if (Geo.Get(out lat, out lon, out city))
            {
                step("air", () => { var a = Online.Air(lat, lon); sb.AppendLine("    " + city + " AQI " + a[0] + " pm2.5 " + a[1]); });
                step("sun", () => { DateTime r, s; Sky.SunTimes(DateTime.Today, lat, lon, out r, out s); sb.AppendLine("    rise " + r.ToString("HH:mm") + " set " + s.ToString("HH:mm")); });
            }
            step("moon", () => sb.AppendLine("    " + Sky.MoonName(Sky.MoonPhase(DateTime.UtcNow)) + " " + (int)(Sky.Illumination(Sky.MoonPhase(DateTime.UtcNow)) * 100) + "%"));
            step("rss", () => { var f = Online.Feeds(new[] { "https://feeds.bbci.co.uk/news/world/rss.xml", "https://news.ycombinator.com/rss" }); sb.AppendLine("    " + f.Count + " headlines; first: " + (f.Count > 0 ? f[0].Key : "")); });
            step("github", () => sb.AppendLine("    torvalds: " + Online.GitHub("torvalds").Count + " days"));
            step("translate", () => sb.AppendLine("    hello -> " + Online.Translate("hello", "en", "es")));
            step("custom", () => sb.AppendLine("    " + CustomRunner.Run("echo Hello", false, 5000)));
            sb.AppendLine();
            var u = Ui.Make(1f);
            foreach (Widget w in Widgets.All)
            {
                string line = "";
                try { line = w.LineText(u) ?? "(no line)"; } catch (Exception ex) { line = "LINE FAIL " + ex.Message; }
                string tip = "";
                try { tip = (w.Tip(u, new Rectangle(0, 0, 100, 40), new Point(-1, -1)) ?? "").Replace("\n", " | "); } catch (Exception ex) { tip = "TIP FAIL " + ex.Message; }
                sb.AppendLine(string.Format("{0,-14} {1,-34} {2}", w.Id, line, tip));
            }
            File.WriteAllText(Out("selftest-data.txt"), sb.ToString());
            return 0;
        }

        // Every widget on a dock, a few rows per category.
        static void WidgetSheet(BarForm bar, int width)
        {
            string style = Config.Style;
            List<string>[] zones = Config.Zones.Select(z => z.ToList()).ToArray();
            Config.Style = "dock";
            bar.U.Build(bar.U.Scale);
            var rows = new List<KeyValuePair<string, Bitmap>>();
            int gap = bar.U.S(Config.TileGap), max = width - bar.U.S(200);
            foreach (string cat in Widgets.Categories)
            {
                var ids = Widgets.All.Where(w => w.Category == cat && w.Available && w.Id != "desktop").Select(w => w.Id).ToList();
                int part = 1;
                while (ids.Count > 0)
                {
                    var row = new List<string>();
                    int sum = 0;
                    while (ids.Count > 0)
                    {
                        Widget w = Widgets.Get(ids[0]);
                        int ww = w.Width(bar.U) + gap;
                        if (row.Count > 0 && sum + ww > max) break;
                        row.Add(ids[0]); sum += ww; ids.RemoveAt(0);
                    }
                    Config.Zones = new[] { new List<string>(), new List<string>(), row };
                    rows.Add(new KeyValuePair<string, Bitmap>(cat + (part > 1 || ids.Count > 0 ? " (" + part + ")" : "") + ": " + string.Join(", ", row.Select(k => Widgets.Get(k).Name).ToArray()), bar.RenderPreview(width)));
                    part++;
                }
            }
            Config.Style = style;
            Config.Zones = zones;
            bar.U.Build(bar.U.Scale);
            Stack(rows, Out("selftest-widgets.png"));
        }

        // Layout variations, rendered without touching settings.txt.
        static void Layouts(BarForm bar, int width)
        {
            string style = Config.Style, wm = Config.WidthMode, align = Config.Align, fm = Config.FloatMode;
            int pct = Config.WidthPct, off = Config.OffsetX, bh = Config.BarHeight, gap = Config.TileGap, pad = Config.TilePad;
            List<string>[] zones = Config.Zones.Select(z => z.ToList()).ToArray();
            string right = string.Join(",", zones[Config.ZRight].ToArray());
            var rows = new List<KeyValuePair<string, Bitmap>>();
            Action<string, Action> shot = (label, setup) =>
            {
                Config.Style = style; Config.WidthMode = "auto"; Config.Align = "center"; Config.FloatMode = "auto";
                Config.WidthPct = 80; Config.OffsetX = 0; Config.BarHeight = 0; Config.TileGap = 6; Config.TilePad = -1;
                Config.Zones = zones.Select(z => z.ToList()).ToArray();
                setup();
                bar.U.Build(bar.U.Scale);
                rows.Add(new KeyValuePair<string, Bitmap>(label, bar.RenderPreview(width)));
                bar.EditPreview = false;
            };
            shot("Tiles, apps in the center zone (Windows 11 style)", () =>
            {
                Config.Style = "tiles";
                Config.Zones = new[] { Config.SplitItems("start,search"), Config.SplitItems("apps"), Config.SplitItems(right) };
            });
            shot("Tiles, 70% wide, floating, separators, clock in the center", () =>
            {
                Config.Style = "tiles"; Config.WidthMode = "percent"; Config.WidthPct = 70; Config.FloatMode = "on";
                Config.Zones = new[] { Config.SplitItems("start,search,sep,apps"), Config.SplitItems("clock"), Config.SplitItems("weather,sep,cpu,volume,battery") };
            });
            shot("Tiles, fit to content, left aligned, offset 40 px", () =>
            {
                Config.Style = "tiles"; Config.WidthMode = "fit"; Config.Align = "left"; Config.OffsetX = 40; Config.FloatMode = "on";
            });
            shot("Dock, full width, flexible spacers", () =>
            {
                Config.Style = "dock"; Config.WidthMode = "full";
                Config.Zones = new[] { Config.SplitItems("start,search,spacer,apps,spacer"), new List<string>(), Config.SplitItems("weather,clock") };
            });
            shot("Dock, 72 px tall, 12 px between tiles, a 40 px spacer", () =>
            {
                Config.Style = "dock"; Config.BarHeight = 72; Config.TileGap = 12;
                Config.Zones = new[] { Config.SplitItems("start,search,apps"), new List<string>(), Config.SplitItems("weather,spacer:40,battery,clock") };
            });
            shot("Edit mode (dock): outlines, Ã— to remove, drop area for the empty center zone", () =>
            {
                Config.Style = "dock";
                Config.Zones = new[] { Config.SplitItems("start,search,apps"), new List<string>(), Config.SplitItems("weather,sep,spacer:30,battery,clock") };
                bar.EditPreview = true;
            });
            shot("Edit mode (tiles)", () => { Config.Style = "tiles"; bar.EditPreview = true; });
            shot("Stacks (CPU + RAM, battery + network, clock + keyboard) and sizes: weather small, screen time large", () =>
            {
                Config.Style = "dock";
                Config.Zones = new[] { Config.SplitItems("start,search,apps"), new List<string>(), Config.SplitItems("weather,screentime,volume,stack:cpu+ram,stack:battery+net,stack:clock+keyboard") };
                Widget.DemoOpts = new Dictionary<string, string> { { "weather.size", "s" }, { "screentime.size", "l" }, { "cpu.label", "false" } };
            });
            Widget.DemoOpts = null;
            Config.Style = style; Config.WidthMode = wm; Config.Align = align; Config.FloatMode = fm;
            Config.WidthPct = pct; Config.OffsetX = off; Config.BarHeight = bh; Config.TileGap = gap; Config.TilePad = pad;
            Config.Zones = zones;
            bar.U.Build(bar.U.Scale);
            Stack(rows, Out("selftest-layouts.png"));
        }

        // --selftest bench: how long one full paint of the bar takes, and what costs the most.
        static void Bench(BarForm bar, int width)
        {
            var sb = new StringBuilder();
            var size = new Size(width, bar.BarH);
            using (var surface = new DibSurface(size.Width, size.Height))
            {
                Action<string, Action<Graphics>> run = (label, setup) =>
                {
                    using (Graphics g = Graphics.FromImage(surface.Bitmap)) bar.PaintBar(g, size);   // warm up
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    for (int i = 0; i < 40; i++)
                        using (Graphics g = Graphics.FromImage(surface.Bitmap)) { setup(g); bar.PaintBar(g, size); }
                    sb.AppendLine(string.Format("{0,-34} {1,6:0.00} ms", label, sw.Elapsed.TotalMilliseconds / 40));
                };
                run("full paint", g => { });
                Gfx.BenchFlags = 1; run("no tile backgrounds", g => { }); Gfx.BenchFlags = 0;
                Gfx.BenchFlags = 2; run("no text", g => { }); Gfx.BenchFlags = 0;
                Gfx.BenchFlags = 4; run("no images", g => { }); Gfx.BenchFlags = 0;
                Gfx.BenchFlags = 8; run("fast compositing", g => { }); Gfx.BenchFlags = 0;
            }
            File.WriteAllText(Out("bench.txt"), sb.ToString());
        }

        static void Stack(List<KeyValuePair<string, Bitmap>> rows, string path)
        {
            using (var f = new Font("Segoe UI Semibold", 16f, GraphicsUnit.Pixel))
            {
                int w = rows.Max(r => r.Value.Width), h = rows.Sum(r => r.Value.Height + 28);
                using (var all = new Bitmap(w, h))
                using (Graphics g = Graphics.FromImage(all))
                {
                    g.Clear(Color.FromArgb(18, 18, 22));
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    int y = 0;
                    foreach (var kv in rows)
                    {
                        g.DrawString(kv.Key, f, Brushes.White, 8, y + 4);
                        g.DrawImage(kv.Value, 0, y + 28);
                        y += kv.Value.Height + 28;
                        kv.Value.Dispose();
                    }
                    all.Save(path, ImageFormat.Png);
                }
            }
        }

        static void Side(List<KeyValuePair<string, Bitmap>> items, string path)
        {
            int gap = 24, w = items.Sum(i => i.Value.Width + gap) + gap, h = items.Max(i => i.Value.Height) + gap * 2;
            using (var all = new Bitmap(w, h))
            using (Graphics g = Graphics.FromImage(all))
            {
                Wallpaper(g, new Rectangle(0, 0, w, h));
                int x = gap;
                foreach (var kv in items)
                {
                    g.DrawImage(kv.Value, x, gap);
                    x += kv.Value.Width + gap;
                    kv.Value.Dispose();
                }
                all.Save(path, ImageFormat.Png);
            }
        }
    }
}
