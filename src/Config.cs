// User settings, theme palettes, accent colors and shared drawing helpers.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace StayBar
{
    // ------------------------------------------------------------------
    // User settings (settings.txt), changed from the Settings window
    // ------------------------------------------------------------------
    static class Config
    {
        public static string Style;               // "tiles" (full-width bar) or "dock" (floating pill)
        public static bool AtTop, Blur, Seconds, ShowDate, Clock24;

        // Hiding: always (even over fullscreen apps) | auto (slides away, mouse at the edge brings it back) |
        // smart (hides only while a window touches it) | fullscreen (visible except over fullscreen apps).
        static string hideMode;
        public static string HideMode
        {
            get { return Automations.HideOverride ?? hideMode; }   // an automation may override it for a while
            set { hideMode = value; }
        }
        public static int RevealMs, HideMs, DimIdle, DimOpacity;   // edge reveal delay, hide delay, dim after N s (0 = never), dim to N %
        public static bool Reserve;                                // windows stop above the bar (an AppBar)

        public static bool AutoHide
        {
            get { return HideMode == "auto"; }
            set { HideMode = value ? "auto" : "fullscreen"; }
        }
        public static bool EdgeReveal { get { return HideMode == "auto" || HideMode == "smart"; } }
        public static bool ReservesSpace { get { return Reserve && (HideMode == "always" || HideMode == "fullscreen"); } }
        public static int IconSize, Margin, Darkness, Radius;
        public static string ThemeId, AccentMode;  // accent mode: custom | wallpaper | windows
        public static Color AccentCustom, Accent;
        public static bool Animations, Magnify, Previews, WinKeys, WinPeek, LauncherKey, Pulse;
        public static bool ClipboardOn, ClipboardKeep, Fahrenheit;
        public static bool DockTrash, HotCorner;   // dock: Recycle Bin at the end (macOS style); bottom-right corner shows the desktop
        public static bool BarKeys;                // Win+Alt+E (edit mode), Win+Alt+P / Win+Alt+1..4 (profiles)

        // The apps block
        public static string Combine;              // always | whenfull | never (one tile per window)
        public static bool AppLabels, RunningOnly, ThisScreen;
        public const int LabelWidth = 130;         // px next to each icon when labels are on
        public static HashSet<string> HiddenApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static Dictionary<string, string> AppNamesCustom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public static Dictionary<string, string> AppIconsCustom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static void SetAppName(string key, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) AppNamesCustom.Remove(key); else AppNamesCustom[key] = name.Trim();
            Settings.Set("app.name:" + key, string.IsNullOrWhiteSpace(name) ? "" : name.Trim());
        }

        public static void SetAppIcon(string key, string path)
        {
            if (string.IsNullOrEmpty(path)) AppIconsCustom.Remove(key); else AppIconsCustom[key] = path;
            Settings.Set("app.icon:" + key, path ?? "");
        }
        // The bar's three zones. Items: a widget key ("clock", "worldclock#2"), "apps" (the app icons),
        // "spacer" (takes free space), "spacer:24" (fixed px), "sep" (a thin divider), "stack:a+b".
        public const int ZLeft = 0, ZCenter = 1, ZRight = 2;
        public static List<string>[] Zones = { new List<string>(), new List<string>(), new List<string>() };
        public static readonly string[] ZoneNames = { "Left", "Center", "Right" };

        // ---- more than one screen ----
        // screens: all | primary | a comma list of screen ids (other than the main one) that get a bar.
        // Each other screen uses the main layout until it gets its own ("screen.<id>.own=true").
        public static string Screens;
        public static HashSet<string> ScreenList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static HashSet<string> ActiveScreens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static Dictionary<string, List<string>[]> screenZones = new Dictionary<string, List<string>[]>(StringComparer.OrdinalIgnoreCase);
        public static Dictionary<string, string> ScreenPos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static List<string>[] ZonesFor(string id)
        {
            List<string>[] z;
            return id != null && screenZones.TryGetValue(id, out z) ? z : Zones;
        }

        public static bool OwnsLayout(string id) { return id != null && screenZones.ContainsKey(id); }

        // Gives a screen its own copy of the main layout (before its first edit).
        public static void OwnLayout(string id)
        {
            if (id == null || screenZones.ContainsKey(id)) return;
            screenZones[id] = Zones.Select(z => z.ToList()).ToArray();
        }

        public static void ShareLayout(string id)
        {
            if (id == null) return;
            screenZones.Remove(id);
            Settings.Set("screen." + id + ".own", "false");
        }

        public static void SetZonesFor(string id, List<string>[] z)
        {
            if (id == null) Zones = z; else screenZones[id] = z;
        }

        // Bar geometry. "auto" values follow the style: tiles = full width, attached; dock = fit, floating.
        public static string WidthMode, Align, FloatMode;   // auto|full|percent|fixed|fit ; left|center|right ; auto|on|off
        public static int WidthPct, WidthPx, OffsetX, BarHeight, TileGap, TilePad;
        public static string WeatherCity, WorldClocks;
        public static int FocusMin, BreakMin;

        public const string DefaultLeft = "start,search,apps";
        public const string DefaultRight = "media,weather,cpu,volume,connect,battery,keyboard,clock,desktop";
        public const string DefaultClocks = "London|GMT Standard Time;New York|Eastern Standard Time;Tokyo|Tokyo Standard Time";

        public static readonly Color[] Accents =
        {
            Color.FromArgb(56, 189, 248), Color.FromArgb(99, 140, 255), Color.FromArgb(167, 139, 250),
            Color.FromArgb(244, 114, 182), Color.FromArgb(251, 113, 133), Color.FromArgb(251, 146, 60),
            Color.FromArgb(250, 204, 21), Color.FromArgb(74, 222, 128), Color.FromArgb(45, 212, 191),
            Color.FromArgb(226, 232, 240),
        };

        public static bool IsDock { get { return Style == "dock"; } }
        public static string EffWidth { get { return WidthMode == "auto" ? (IsDock ? "fit" : "full") : WidthMode; } }
        public static bool Floating { get { return FloatMode == "auto" ? IsDock : FloatMode == "on"; } }

        // "Center the apps": the apps block lives in the center zone instead of at the end of the left one.
        public static bool CenterApps
        {
            get { return Zones[ZCenter].Contains("apps"); }
            set
            {
                if (value == CenterApps) return;
                foreach (List<string> z in Zones) z.Remove("apps");
                if (value) Zones[ZCenter].Insert(0, "apps"); else Zones[ZLeft].Add("apps");
            }
        }

        // ---- zone items ----
        public static bool IsSpecial(string item)
        {
            return item == "apps" || item == "sep" || item == "spacer" || item.StartsWith("spacer:") || item.StartsWith("stack:") || item.StartsWith("@");
        }

        public static string BaseId(string key) { int i = key.IndexOf('#'); return i < 0 ? key : key.Substring(0, i); }

        // The widget keys in an item ("cpu" -> cpu; "stack:cpu+ram" -> cpu, ram; "apps" -> none).
        public static IEnumerable<string> WidgetKeys(string item)
        {
            if (item.StartsWith("stack:")) { foreach (string k in item.Substring(6).Split('+')) if (k.Length > 0) yield return k; }
            else if (!IsSpecial(item)) yield return item;
        }

        // Every widget on any bar (the main one, and screens with their own layout).
        public static IEnumerable<string> AllWidgetKeys()
        {
            foreach (string k in WidgetKeysIn(Zones)) yield return k;
            foreach (var kv in screenZones)
                if (ActiveScreens.Contains(kv.Key))
                    foreach (string k in WidgetKeysIn(kv.Value)) yield return k;
        }

        public static IEnumerable<string> WidgetKeysIn(List<string>[] zones)
        {
            foreach (List<string> z in zones) foreach (string item in z) foreach (string k in WidgetKeys(item)) yield return k;
        }

        // Is any copy of this widget on the bar (alone or in a stack)?
        public static bool HasWidget(string id)
        {
            foreach (string k in AllWidgetKeys()) if (BaseId(k) == id) return true;
            return false;
        }

        public static bool HasItem(string key)
        {
            foreach (string k in AllWidgetKeys()) if (k == key) return true;
            return false;
        }

        public static void RemoveWidget(string key) { RemoveWidget(Zones, key); }

        public static void RemoveWidget(List<string>[] zones, string key)
        {
            foreach (List<string> z in zones)
                for (int i = z.Count - 1; i >= 0; i--)
                {
                    if (z[i] == key) { z.RemoveAt(i); continue; }
                    if (!z[i].StartsWith("stack:")) continue;
                    var parts = WidgetKeys(z[i]).Where(k => k != key).ToList();
                    if (parts.Count == 0) z.RemoveAt(i);
                    else if (parts.Count == 1) z[i] = parts[0];
                    else z[i] = "stack:" + string.Join("+", parts.ToArray());
                }
        }

        // Zone list from settings: keeps repeated spacers and separators, drops repeated widgets.
        public static List<string> SplitItems(string csv)
        {
            var list = new List<string>();
            var seen = new HashSet<string>();
            foreach (string raw in (csv ?? "").Split(','))
            {
                string s = raw.Trim().ToLowerInvariant();
                if (s.Length == 0) continue;
                bool unique = !(s == "sep" || s == "spacer" || s.StartsWith("spacer:"));
                if (unique && !seen.Add(s)) continue;
                list.Add(s);
            }
            return list;
        }
        public static Palette P { get { return Palette.Get(ThemeId); } }

        static Config() { Reset(); }

        public static void Reset()
        {
            Style = "tiles"; AtTop = false; HideMode = "fullscreen"; Blur = true; Seconds = true; ShowDate = true; Clock24 = true;
            RevealMs = 120; HideMs = 650; DimIdle = 0; DimOpacity = 45; Reserve = true;
            IconSize = 30; Margin = 8; Darkness = 75; Radius = 12;
            WidthMode = "auto"; Align = "center"; FloatMode = "auto"; WidthPct = 80; WidthPx = 1200; OffsetX = 0;
            BarHeight = 0; TileGap = 6; TilePad = -1;
            ThemeId = "midnight"; AccentMode = "custom"; AccentCustom = Accents[0]; Accent = AccentCustom;
            Animations = true; Magnify = true; Previews = true; WinKeys = true; WinPeek = true; LauncherKey = true; Pulse = true;
            ClipboardOn = true; ClipboardKeep = false; Fahrenheit = false;
            DockTrash = true; HotCorner = false; BarKeys = true;
            Combine = "always"; AppLabels = false; RunningOnly = false; ThisScreen = true;
            HiddenApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AppNamesCustom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            AppIconsCustom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Zones = new[] { SplitItems(DefaultLeft), new List<string>(), SplitItems(DefaultRight) };
            WeatherCity = ""; WorldClocks = DefaultClocks; FocusMin = 25; BreakMin = 5;
        }

        public static void Load()
        {
            Reset();
            Style = Settings.Get("style", Style) == "dock" ? "dock" : "tiles";
            AtTop = Settings.Get("position", "bottom") == "top";
            Screens = Settings.Get("screens", "all").Trim().ToLowerInvariant();
            ScreenList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Screens != "all" && Screens != "primary")
            {
                foreach (string s in Screens.Split(',')) if (s.Trim().Length > 0) ScreenList.Add(s.Trim());
                Screens = "list";
            }
            screenZones = new Dictionary<string, List<string>[]>(StringComparer.OrdinalIgnoreCase);
            ScreenPos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var sc = Settings.WithPrefix("screen.");
            foreach (var kv in sc)
            {
                if (kv.Key.EndsWith(".position")) { ScreenPos[kv.Key.Substring(0, kv.Key.Length - 9)] = kv.Value; continue; }
                if (!kv.Key.EndsWith(".own") || kv.Value != "true") continue;
                string id = kv.Key.Substring(0, kv.Key.Length - 4);
                string l, c, r;
                sc.TryGetValue(id + ".zones.left", out l); sc.TryGetValue(id + ".zones.center", out c); sc.TryGetValue(id + ".zones.right", out r);
                screenZones[id] = new[] { SplitItems(l), SplitItems(c), SplitItems(r) };
            }
            HideMode = Pick("hide", Bool("autohide", false) ? "auto" : "fullscreen", "always", "auto", "smart", "fullscreen");
            RevealMs = Int("hide.reveal", RevealMs, 0, 2000);
            HideMs = Int("hide.delay", HideMs, 100, 10000);
            DimIdle = Int("dim.idle", DimIdle, 0, 3600);
            DimOpacity = Int("dim.opacity", DimOpacity, 10, 100);
            Reserve = Bool("reserve", Reserve);
            Blur = Bool("blur", Blur);
            Seconds = Bool("seconds", Seconds);
            ShowDate = Bool("date", ShowDate);
            Clock24 = Bool("clock24", Clock24);
            WidthMode = Pick("bar.width", "auto", "auto", "full", "percent", "fixed", "fit");
            Align = Pick("bar.align", "center", "left", "center", "right");
            FloatMode = Pick("bar.floating", "auto", "auto", "on", "off");
            WidthPct = Int("bar.widthpct", WidthPct, 20, 100);
            WidthPx = Int("bar.widthpx", WidthPx, 300, 8000);
            OffsetX = Int("bar.offset", OffsetX, -4000, 4000);
            BarHeight = Int("bar.height", BarHeight, 0, 160);
            TileGap = Int("bar.gap", TileGap, 0, 30);
            TilePad = Int("bar.pad", TilePad, -1, 24);
            IconSize = Int("iconsize", IconSize, 20, 48);
            Margin = Int("margin", Margin, 0, 40);
            Darkness = Int("darkness", Darkness, 0, 100);
            Radius = Int("radius", Radius, 0, 22);
            ThemeId = Settings.Get("theme", ThemeId);
            if (Palette.All.All(p => p.Id != ThemeId)) ThemeId = "midnight";
            AccentMode = Settings.Get("accentmode", AccentMode);
            try { AccentCustom = ColorTranslator.FromHtml("#" + Settings.Get("accent", Hex(AccentCustom))); } catch { }
            Animations = Bool("animations", Animations);
            Magnify = Bool("magnify", Magnify);
            Previews = Bool("previews", Previews);
            WinKeys = Bool("winkeys", WinKeys);
            WinPeek = Bool("winpeek", WinPeek);
            LauncherKey = Bool("launcher", LauncherKey);
            Pulse = Bool("pulse", Pulse);
            ClipboardOn = Bool("clipboard", ClipboardOn);
            ClipboardKeep = Bool("clipboard.keep", ClipboardKeep);
            Fahrenheit = Bool("weather.fahrenheit", Fahrenheit);
            Widget.OptionsChanged();
            Automations.Gen++;   // re-read the rules too
            DockTrash = Bool("dock.trash", DockTrash);
            HotCorner = Bool("hotcorner", HotCorner);
            BarKeys = Bool("barkeys", BarKeys);
            Combine = Pick("apps.combine", "always", "always", "whenfull", "never");
            AppLabels = Bool("apps.labels", AppLabels);
            RunningOnly = Settings.Get("apps.show", "all") == "running";
            ThisScreen = Bool("apps.thisscreen", ThisScreen);
            HiddenApps = new HashSet<string>(Settings.Get("apps.hidden", "").Split('|').Select(s => s.Trim()).Where(s => s.Length > 0), StringComparer.OrdinalIgnoreCase);
            AppNamesCustom = Settings.WithPrefix("app.name:");
            AppIconsCustom = Settings.WithPrefix("app.icon:");
            string zl = Settings.Get("zones.left", null);
            if (zl != null)
                Zones = new[] { SplitItems(zl), SplitItems(Settings.Get("zones.center", "")), SplitItems(Settings.Get("zones.right", "")) };
            else
            {
                // settings from before zones: left/right widget lists + "center the apps"
                List<string> l = SplitItems(Settings.Get("left", "start,search")), r = SplitItems(Settings.Get("right", DefaultRight));
                l.Remove("apps"); r.Remove("apps");
                bool center = Bool("centerapps", false);
                var c = new List<string>();
                if (center) c.Add("apps"); else l.Add("apps");
                Zones = new[] { l, c, r };
            }
            WeatherCity = Settings.Get("weather.city", "");
            WorldClocks = Settings.Get("worldclocks", DefaultClocks);
            FocusMin = Int("focus.min", FocusMin, 1, 180);
            BreakMin = Int("break.min", BreakMin, 1, 60);
            ResolveAccent();
        }

        public static void Save()
        {
            var d = new Dictionary<string, string>();
            d["style"] = Style; d["position"] = AtTop ? "top" : "bottom";
            d["autohide"] = B(hideMode == "auto"); d["hide"] = hideMode; d["hide.reveal"] = RevealMs.ToString(); d["hide.delay"] = HideMs.ToString();
            d["dim.idle"] = DimIdle.ToString(); d["dim.opacity"] = DimOpacity.ToString(); d["reserve"] = B(Reserve); d["blur"] = B(Blur); d["seconds"] = B(Seconds); d["date"] = B(ShowDate);
            d["clock24"] = B(Clock24);
            d["bar.width"] = WidthMode; d["bar.align"] = Align; d["bar.floating"] = FloatMode;
            d["bar.widthpct"] = WidthPct.ToString(); d["bar.widthpx"] = WidthPx.ToString(); d["bar.offset"] = OffsetX.ToString();
            d["bar.height"] = BarHeight.ToString(); d["bar.gap"] = TileGap.ToString(); d["bar.pad"] = TilePad.ToString();
            d["iconsize"] = IconSize.ToString(); d["margin"] = Margin.ToString(); d["darkness"] = Darkness.ToString();
            d["radius"] = Radius.ToString(); d["theme"] = ThemeId; d["accentmode"] = AccentMode; d["accent"] = Hex(AccentCustom);
            d["animations"] = B(Animations); d["magnify"] = B(Magnify); d["previews"] = B(Previews);
            d["winkeys"] = B(WinKeys); d["winpeek"] = B(WinPeek); d["launcher"] = B(LauncherKey); d["pulse"] = B(Pulse);
            d["clipboard"] = B(ClipboardOn); d["clipboard.keep"] = B(ClipboardKeep); d["weather.fahrenheit"] = B(Fahrenheit);
            d["dock.trash"] = B(DockTrash); d["hotcorner"] = B(HotCorner); d["barkeys"] = B(BarKeys);
            d["apps.combine"] = Combine; d["apps.labels"] = B(AppLabels); d["apps.show"] = RunningOnly ? "running" : "all";
            d["apps.thisscreen"] = B(ThisScreen); d["apps.hidden"] = string.Join("|", HiddenApps.ToArray());
            d["zones.left"] = string.Join(",", Zones[ZLeft]); d["zones.center"] = string.Join(",", Zones[ZCenter]); d["zones.right"] = string.Join(",", Zones[ZRight]);
            d["screens"] = Screens == "list" ? string.Join(",", ScreenList.ToArray()) : Screens;
            if (d["screens"].Length == 0) d["screens"] = "primary";
            foreach (var kv in screenZones)
            {
                string p = "screen." + kv.Key + ".";
                d[p + "own"] = "true";
                d[p + "zones.left"] = string.Join(",", kv.Value[ZLeft]); d[p + "zones.center"] = string.Join(",", kv.Value[ZCenter]); d[p + "zones.right"] = string.Join(",", kv.Value[ZRight]);
            }
            foreach (var kv in ScreenPos) d["screen." + kv.Key + ".position"] = kv.Value;
            d["weather.city"] = WeatherCity ?? ""; d["worldclocks"] = WorldClocks ?? "";
            d["focus.min"] = FocusMin.ToString(); d["break.min"] = BreakMin.ToString();
            Settings.SetMany(d);
            ResolveAccent();
        }

        public static void ResolveAccent()
        {
            Color c = AccentCustom;
            if (AccentMode == "windows") { Color w; if (WindowsAccent(out w)) c = w; }
            else if (AccentMode == "wallpaper" && WallpaperColor.Value.HasValue) c = WallpaperColor.Value.Value;
            Accent = c;
        }

        static bool WindowsAccent(out Color c)
        {
            c = Color.Empty;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                {
                    object v = k == null ? null : k.GetValue("AccentColor");
                    if (v == null) return false;
                    uint abgr = unchecked((uint)(int)v);
                    c = Color.FromArgb((int)(abgr & 0xFF), (int)((abgr >> 8) & 0xFF), (int)((abgr >> 16) & 0xFF));
                    c = Gfx.Vivid(c);
                    return true;
                }
            }
            catch { return false; }
        }

        public static List<string> Split(string csv)
        {
            return (csv ?? "").Split(',').Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).Distinct().ToList();
        }

        static string B(bool v) { return v ? "true" : "false"; }
        static bool Bool(string k, bool d) { string v = Settings.Get(k, B(d)); return v == "true" || v == "1"; }
        static string Pick(string k, string d, params string[] allowed)
        {
            string v = (Settings.Get(k, d) ?? d).Trim().ToLowerInvariant();
            return allowed.Contains(v) ? v : d;
        }
        static int Int(string k, int d, int min, int max)
        {
            int v;
            if (!int.TryParse(Settings.Get(k, d.ToString()), out v)) v = d;
            return Math.Max(min, Math.Min(max, v));
        }
        public static string Hex(Color c) { return c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }
    }

    // ------------------------------------------------------------------
    // "Match my wallpaper": the most vivid color of the desktop picture
    // ------------------------------------------------------------------
    static class WallpaperColor
    {
        public static Color? Value;
        public static string Source;

        public static string WallpaperPath()
        {
            var sb = new StringBuilder(520);
            string p = null;
            try { if (Native.SystemParametersInfo(0x73, (uint)sb.Capacity, sb, 0)) p = sb.ToString(); } catch { }
            if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
            string t = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
            return File.Exists(t) ? t : null;
        }

        // Runs on a background thread.
        public static void Compute()
        {
            string path = WallpaperPath();
            if (path == null) { Value = null; return; }
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var ms = new MemoryStream())
            {
                fs.CopyTo(ms);
                ms.Position = 0;
                using (Image img = Image.FromStream(ms))
                    Value = Dominant(img);
            }
            Source = path;
        }

        public static Color Dominant(Image img)
        {
            const int W = 48, H = 30;
            using (var small = new Bitmap(W, H, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.DrawImage(img, 0, 0, W, H);
                }
                var weight = new double[24]; var rs = new double[24]; var gs = new double[24]; var bs = new double[24];
                double total = 0;
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        Color c = small.GetPixel(x, y);
                        float hue = c.GetHue(), sat = c.GetSaturation(), bri = c.GetBrightness();
                        double v = Math.Max(c.R, Math.Max(c.G, c.B)) / 255.0;
                        double s = v <= 0 ? 0 : (v - Math.Min(c.R, Math.Min(c.G, c.B)) / 255.0) / v;
                        double w = s * s * v * (v > 0.18 ? 1 : 0.1);
                        int b = Math.Min(23, (int)(hue / 15f));
                        weight[b] += w; rs[b] += c.R * w; gs[b] += c.G * w; bs[b] += c.B * w; total += w;
                    }
                int best = 0;
                for (int i = 1; i < 24; i++) if (weight[i] > weight[best]) best = i;
                if (weight[best] < 0.6 || total <= 0) return Color.FromArgb(226, 232, 240);   // grey wallpaper: soft white
                Color raw = Color.FromArgb((int)(rs[best] / weight[best]), (int)(gs[best] / weight[best]), (int)(bs[best] / weight[best]));
                return Gfx.Vivid(raw);
            }
        }
    }

    // ------------------------------------------------------------------
    // Bar themes
    // ------------------------------------------------------------------
    class Palette
    {
        public string Id, Name, Blurb;
        public bool Light, Glow, Sheen;
        public Color Tint;                 // acrylic tint (rgb); alpha comes from "darkness"
        public int TintMin, TintMax;
        public Color Solid;                // background when blur is off
        public Color Tile, TileTop, Border, BorderTop;
        public Color Ink, InkDim, InkFaint, Track, Hover;
        public Color MenuBack, MenuSel, MenuLine;

        public Color TintAt(int darkness)
        {
            int a = TintMin + (int)((TintMax - TintMin) * darkness / 100f);
            return Color.FromArgb(Math.Max(0, Math.Min(255, a)), Tint.R, Tint.G, Tint.B);
        }

        public Color FlyoutTint { get { return Color.FromArgb(Light ? 225 : 215, Tint.R, Tint.G, Tint.B); } }

        public static readonly List<Palette> All = Make();

        public static Palette Get(string id)
        {
            foreach (Palette p in All) if (p.Id == id) return p;
            return All[0];
        }

        static Palette Dark(string id, string name, string blurb)
        {
            var p = new Palette();
            p.Id = id; p.Name = name; p.Blurb = blurb;
            p.Ink = Color.FromArgb(246, 246, 250); p.InkDim = Color.FromArgb(158, 160, 172); p.InkFaint = Color.FromArgb(96, 98, 110);
            p.Track = Color.FromArgb(34, 255, 255, 255); p.Hover = Color.FromArgb(18, 255, 255, 255);
            p.MenuBack = Color.FromArgb(30, 30, 34); p.MenuSel = Color.FromArgb(56, 56, 64); p.MenuLine = Color.FromArgb(60, 60, 68);
            return p;
        }

        static List<Palette> Make()
        {
            var list = new List<Palette>();

            Palette p = Dark("midnight", "Midnight", "Black tiles, crisp and calm");
            p.Tint = Color.FromArgb(6, 6, 8); p.TintMin = 150; p.TintMax = 252; p.Solid = Color.FromArgb(7, 7, 9);
            p.Tile = Color.FromArgb(232, 23, 23, 26); p.TileTop = Color.FromArgb(10, 255, 255, 255);
            p.Border = Color.FromArgb(14, 255, 255, 255); p.BorderTop = Color.FromArgb(30, 255, 255, 255);
            list.Add(p);

            p = Dark("glass", "Glass", "Frosted acrylic, your wallpaper shows through");
            p.Tint = Color.FromArgb(18, 20, 28); p.TintMin = 30; p.TintMax = 210; p.Solid = Color.FromArgb(28, 30, 38);
            p.Tile = Color.FromArgb(20, 255, 255, 255); p.TileTop = Color.FromArgb(18, 255, 255, 255);
            p.Border = Color.FromArgb(18, 255, 255, 255); p.BorderTop = Color.FromArgb(46, 255, 255, 255);
            p.Sheen = true;
            list.Add(p);

            p = Dark("liquid", "Liquid", "Clear glass with bright edges");
            p.Tint = Color.FromArgb(22, 26, 38); p.TintMin = 0; p.TintMax = 150; p.Solid = Color.FromArgb(30, 34, 46);
            p.Tile = Color.FromArgb(26, 255, 255, 255); p.TileTop = Color.FromArgb(50, 255, 255, 255);
            p.Border = Color.FromArgb(16, 255, 255, 255); p.BorderTop = Color.FromArgb(88, 255, 255, 255);
            p.InkDim = Color.FromArgb(214, 218, 230); p.InkFaint = Color.FromArgb(150, 156, 172);
            p.Sheen = true;
            list.Add(p);

            p = Dark("graphite", "Graphite", "Soft grey, easy on the eyes");
            p.Tint = Color.FromArgb(38, 40, 46); p.TintMin = 140; p.TintMax = 248; p.Solid = Color.FromArgb(40, 42, 48);
            p.Tile = Color.FromArgb(210, 62, 64, 72); p.TileTop = Color.FromArgb(12, 255, 255, 255);
            p.Border = Color.FromArgb(14, 255, 255, 255); p.BorderTop = Color.FromArgb(34, 255, 255, 255);
            p.InkDim = Color.FromArgb(176, 178, 190);
            list.Add(p);

            p = Dark("neon", "Neon", "Deep black with glowing accent edges");
            p.Tint = Color.FromArgb(4, 4, 10); p.TintMin = 170; p.TintMax = 252; p.Solid = Color.FromArgb(5, 5, 12);
            p.Tile = Color.FromArgb(235, 11, 11, 20); p.TileTop = Color.FromArgb(8, 255, 255, 255);
            p.Border = Color.Empty; p.BorderTop = Color.Empty;   // accent, decided at draw time
            p.InkDim = Color.FromArgb(164, 166, 196);
            p.Glow = true;
            list.Add(p);

            p = new Palette();
            p.Id = "light"; p.Name = "Light"; p.Blurb = "Bright and clean, like paper";
            p.Light = true;
            p.Tint = Color.FromArgb(238, 239, 244); p.TintMin = 120; p.TintMax = 240; p.Solid = Color.FromArgb(236, 237, 242);
            p.Tile = Color.FromArgb(205, 255, 255, 255); p.TileTop = Color.FromArgb(60, 255, 255, 255);
            p.Border = Color.FromArgb(20, 0, 0, 0); p.BorderTop = Color.FromArgb(10, 0, 0, 0);
            p.Ink = Color.FromArgb(20, 20, 26); p.InkDim = Color.FromArgb(92, 94, 108); p.InkFaint = Color.FromArgb(150, 152, 164);
            p.Track = Color.FromArgb(26, 0, 0, 0); p.Hover = Color.FromArgb(14, 0, 0, 0);
            p.MenuBack = Color.FromArgb(249, 249, 251); p.MenuSel = Color.FromArgb(232, 232, 238); p.MenuLine = Color.FromArgb(222, 222, 228);
            list.Add(p);

            return list;
        }
    }

    // ------------------------------------------------------------------
    // Colors for the Settings window and other "app" windows (always dark)
    // ------------------------------------------------------------------
    static class Theme
    {
        public static readonly Color Window = Color.FromArgb(24, 24, 28);
        public static readonly Color Side = Color.FromArgb(18, 18, 21);
        public static readonly Color Card = Color.FromArgb(36, 36, 42);
        public static readonly Color CardHover = Color.FromArgb(44, 44, 52);
        public static readonly Color Text = Color.FromArgb(242, 242, 247);
        public static readonly Color SubText = Color.FromArgb(160, 160, 172);
        public static readonly Color Track = Color.FromArgb(78, 78, 90);
        public static readonly Color Field = Color.FromArgb(28, 28, 33);

        public static readonly StringFormat Center = MakeFormat(StringAlignment.Center);

        public static StringFormat MakeFormat(StringAlignment h)
        {
            var f = new StringFormat(StringFormatFlags.NoWrap);
            f.Alignment = h;
            f.LineAlignment = StringAlignment.Center;
            f.Trimming = StringTrimming.EllipsisCharacter;
            return f;
        }

        public static Color Lighten(Color c, float t)
        {
            return Color.FromArgb(c.A, (int)(c.R + (255 - c.R) * t), (int)(c.G + (255 - c.G) * t), (int)(c.B + (255 - c.B) * t));
        }

        public static Color Darken(Color c, float t)
        {
            return Color.FromArgb(c.A, (int)(c.R * (1 - t)), (int)(c.G * (1 - t)), (int)(c.B * (1 - t)));
        }

        public static Color WithAlpha(Color c, int a) { return Color.FromArgb(Math.Max(0, Math.Min(255, a)), c.R, c.G, c.B); }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d < 1f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, RectangleF r, float rad, Color c)
        {
            if (r.Width <= 0 || r.Height <= 0 || c.A == 0) return;
            using (var b = new SolidBrush(c))
            using (GraphicsPath p = Round(r, rad)) g.FillPath(b, p);
        }

        public static void StrokeRound(Graphics g, RectangleF r, float rad, Color c, float width)
        {
            if (r.Width <= 0 || r.Height <= 0 || c.A == 0) return;
            using (var pen = new Pen(c, width))
            using (GraphicsPath p = Round(r, rad)) g.DrawPath(pen, p);
        }
    }

    // ------------------------------------------------------------------
    // Drawing helpers shared by the bar, widgets, flyouts and previews
    // ------------------------------------------------------------------
    static class Gfx
    {
        static readonly Graphics measure = MakeMeasure();
        static Graphics MakeMeasure()
        {
            Graphics g = Graphics.FromImage(new Bitmap(1, 1));
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            return g;
        }

        public static readonly StringFormat Typo = MakeTypo();
        static StringFormat MakeTypo()
        {
            var f = (StringFormat)StringFormat.GenericTypographic.Clone();
            f.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
            return f;
        }
        public static readonly StringFormat Left = Theme.MakeFormat(StringAlignment.Near);
        public static readonly StringFormat Right = Theme.MakeFormat(StringAlignment.Far);
        public static readonly StringFormat Mid = Theme.MakeFormat(StringAlignment.Center);

        static string glyphFamily;
        public static string GlyphFamily()
        {
            if (glyphFamily != null) return glyphFamily;
            string[] wanted = { "Segoe Fluent Icons", "Segoe MDL2 Assets", "Segoe UI Symbol" };
            var installed = new HashSet<string>(FontFamily.Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
            glyphFamily = wanted.FirstOrDefault(n => installed.Contains(n)) ?? "Segoe UI";
            return glyphFamily;
        }

        static readonly Dictionary<string, Font> fontCache = new Dictionary<string, Font>();

        // A shared font (never dispose it). Use for one-off text/glyphs drawn often.
        public static Font FontOf(string family, float px)
        {
            string key = family + "|" + px.ToString("0.0");
            Font f;
            lock (fontCache)
            {
                if (!fontCache.TryGetValue(key, out f)) { f = new Font(family, px, GraphicsUnit.Pixel); fontCache[key] = f; }
            }
            return f;
        }

        public static float TW(string text, Font f)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            lock (measure) return measure.MeasureString(text, f, PointF.Empty, Typo).Width;
        }

        public static int BenchFlags;   // --selftest bench only

        public static void Prep(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.CompositingQuality = CompositingQuality.HighSpeed;   // 5x faster than HighQuality, looks the same on PARGB
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            if ((BenchFlags & 8) != 0) { g.CompositingQuality = CompositingQuality.HighSpeed; g.PixelOffsetMode = PixelOffsetMode.Half; }
        }

        // Pushes a color into a pleasant accent range (saturated, mid-bright).
        public static Color Vivid(Color c)
        {
            double h = c.GetHue(), s = c.GetSaturation(), l = c.GetBrightness();
            s = Math.Max(0.55, Math.Min(0.92, s * 1.25));
            l = Math.Max(0.58, Math.Min(0.70, l));
            return FromHsl(h, s, l);
        }

        public static Color FromHsl(double h, double s, double l)
        {
            double c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = l - c / 2;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; } else if (h < 120) { r = x; g = c; } else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; }
            return Color.FromArgb(Clamp((r + m) * 255), Clamp((g + m) * 255), Clamp((b + m) * 255));
        }

        static int Clamp(double v) { return Math.Max(0, Math.Min(255, (int)Math.Round(v))); }

        public static Color Mix(Color a, Color b, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        // Text color to put on top of a colored fill.
        public static Color On(Color fill)
        {
            double lum = (0.299 * fill.R + 0.587 * fill.G + 0.114 * fill.B) / 255;
            return lum > 0.6 ? Color.FromArgb(18, 18, 22) : Color.White;
        }

        public static Color Load(float pct, Color accent)
        {
            return pct > 85 ? Color.FromArgb(251, 113, 133) : pct > 65 ? Color.FromArgb(251, 191, 36) : accent;
        }

        // A themed tile: body, top light, border, optional neon glow.
        public static void Tile(Graphics g, RectangleF r, float rad, Palette p, Color accent, float hover, float alphaMul)
        {
            if ((BenchFlags & 1) != 0) return;
            if (r.Width <= 1 || r.Height <= 1) return;
            Func<Color, Color> A = c => Color.FromArgb((int)(c.A * alphaMul), c.R, c.G, c.B);
            if (p.Glow)
            {
                for (int i = 3; i >= 1; i--)
                {
                    float grow = i * 1.6f;
                    var gr = RectangleF.Inflate(r, grow, grow);
                    Theme.StrokeRound(g, gr, rad + grow, A(Color.FromArgb((int)((10 + 10 * (3 - i)) * (0.7f + 0.6f * hover)), accent)), 1.6f);
                }
            }
            using (GraphicsPath path = Theme.Round(r, rad))
            {
                using (var b = new SolidBrush(A(p.Tile))) g.FillPath(b, path);
                if (p.TileTop.A > 0)
                {
                    var top = new RectangleF(r.X, r.Y, r.Width, r.Height * 0.62f + 1);
                    using (var lb = new LinearGradientBrush(top, A(p.TileTop), Color.FromArgb(0, p.TileTop), 90f))
                    {
                        Region old = g.Clip;
                        g.SetClip(path, CombineMode.Intersect);
                        g.FillRectangle(lb, top);
                        g.Clip = old;
                    }
                }
                if (hover > 0.01f) using (var hb = new SolidBrush(A(Color.FromArgb((int)(p.Hover.A * hover), p.Hover)))) g.FillPath(hb, path);
                Color bt = p.Glow ? Color.FromArgb((int)(150 + 80 * hover), accent) : p.BorderTop;
                Color bb = p.Glow ? Color.FromArgb((int)(70 + 60 * hover), accent) : p.Border;
                if (bt.A > 0 || bb.A > 0)
                {
                    var br = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1f, r.Height - 1f);
                    using (var lb = new LinearGradientBrush(new RectangleF(br.X, br.Y - 1, br.Width, br.Height + 2), A(bt), A(bb), 90f))
                    using (var pen = new Pen(lb, 1f))
                    using (GraphicsPath bp = Theme.Round(br, rad)) g.DrawPath(pen, bp);
                }
            }
        }

        public static void Text(Graphics g, string s, Font f, Color c, float x, float y)
        {
            if ((BenchFlags & 2) != 0) return;
            if (string.IsNullOrEmpty(s)) return;
            using (var b = new SolidBrush(c)) g.DrawString(s, f, b, x, y, Typo);
        }

        // Text inside a box, vertically centred, trimmed with an ellipsis.
        public static void TextIn(Graphics g, string s, Font f, Color c, RectangleF r, StringFormat fmt)
        {
            if ((BenchFlags & 2) != 0) return;
            if (string.IsNullOrEmpty(s) || r.Width <= 2) return;
            using (var b = new SolidBrush(c)) g.DrawString(s, f, b, r, fmt);
        }

        public static void Glyph(Graphics g, string glyph, Font f, Color c, RectangleF r)
        {
            if ((BenchFlags & 2) != 0) return;
            using (var b = new SolidBrush(c)) g.DrawString(glyph, f, b, r, Theme.Center);
        }

        public static void Meter(Graphics g, RectangleF r, float pct, Color fill, Color track)
        {
            pct = Math.Max(0, Math.Min(100, pct));
            float rad = r.Height / 2f;
            Theme.FillRound(g, r, rad, track);
            if (pct <= 0) return;
            var f = new RectangleF(r.X, r.Y, Math.Max(r.Height, r.Width * pct / 100f), r.Height);
            Theme.FillRound(g, f, rad, fill);
        }

        public static void Ring(Graphics g, PointF c, float radius, float width, float pct, Color fill, Color track)
        {
            var r = new RectangleF(c.X - radius, c.Y - radius, radius * 2, radius * 2);
            using (var pt = new Pen(track, width)) g.DrawEllipse(pt, r);
            pct = Math.Max(0, Math.Min(100, pct));
            if (pct <= 0.5f) return;
            using (var pf = new Pen(fill, width))
            {
                pf.StartCap = LineCap.Round; pf.EndCap = LineCap.Round;
                g.DrawArc(pf, r, -90, 360f * pct / 100f);
            }
        }

        // Line chart of the last values, with a soft fill under it.
        public static void Spark(Graphics g, RectangleF r, float[] data, int count, float max, Color color, bool fill)
        {
            if (data == null || count < 2 || r.Width < 4 || r.Height < 2) return;
            if (max <= 0) { max = 1; foreach (float v in data) if (v > max) max = v; }
            int n = Math.Min(count, data.Length);
            var pts = new PointF[n];
            for (int i = 0; i < n; i++)
            {
                float v = Math.Max(0, Math.Min(max, data[data.Length - n + i]));
                pts[i] = new PointF(r.X + r.Width * i / (n - 1), r.Bottom - r.Height * v / max);
            }
            if (fill)
            {
                using (var path = new GraphicsPath())
                {
                    path.AddLines(pts);
                    path.AddLine(pts[n - 1].X, pts[n - 1].Y, r.Right, r.Bottom);
                    path.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
                    path.CloseFigure();
                    using (var lb = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2),
                        Color.FromArgb(90, color), Color.FromArgb(0, color), 90f))
                        g.FillPath(lb, path);
                }
            }
            using (var pen = new Pen(color, 1.4f)) { pen.LineJoin = LineJoin.Round; g.DrawLines(pen, pts); }
        }

        // Little vertical bars (hourly screen time and similar).
        public static void Bars(Graphics g, RectangleF r, float[] data, Color color, Color faint, int highlight)
        {
            if (data == null || data.Length == 0) return;
            float max = 0; foreach (float v in data) max = Math.Max(max, v);
            float slot = r.Width / data.Length, bw = Math.Max(1.5f, slot * 0.6f);
            for (int i = 0; i < data.Length; i++)
            {
                float h = max <= 0 ? 0 : r.Height * data[i] / max;
                float x = r.X + i * slot + (slot - bw) / 2;
                Color c = i == highlight ? color : data[i] > 0 ? Color.FromArgb(Math.Min(255, color.A * 3 / 4), color) : faint;
                if (h < 1.5f) { using (var b = new SolidBrush(faint)) g.FillRectangle(b, x, r.Bottom - 1.5f, bw, 1.5f); continue; }
                Theme.FillRound(g, new RectangleF(x, r.Bottom - h, bw, h), Math.Min(bw / 2, 2f), c);
            }
        }

        public static void Battery(Graphics g, RectangleF r, float pct, bool charging, Color ink, Color accent)
        {
            float nub = r.Width * 0.08f;
            var body = new RectangleF(r.X, r.Y, r.Width - nub - 1, r.Height);
            Theme.StrokeRound(g, body, r.Height * 0.28f, Color.FromArgb(200, ink), Math.Max(1.2f, r.Height * 0.1f));
            Theme.FillRound(g, new RectangleF(body.Right + 1, r.Y + r.Height * 0.3f, nub, r.Height * 0.4f), nub / 2, Color.FromArgb(200, ink));
            float pad = Math.Max(2f, r.Height * 0.2f);
            var inner = new RectangleF(body.X + pad, body.Y + pad, (body.Width - 2 * pad) * Math.Max(0.04f, pct / 100f), body.Height - 2 * pad);
            Color c = charging ? Color.FromArgb(74, 222, 128) : pct <= 15 ? Color.FromArgb(251, 113, 133) : pct <= 30 ? Color.FromArgb(251, 191, 36) : ink;
            Theme.FillRound(g, inner, r.Height * 0.14f, c);
        }

        // Crisp media symbols: 0 previous, 1 play, 2 pause, 3 next.
        public static void MediaIcon(Graphics g, RectangleF r, int kind, Color c)
        {
            float s = Math.Min(r.Width, r.Height) * 0.36f, cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            using (var b = new SolidBrush(c))
            using (var pen = new Pen(c, Math.Max(1f, s * 0.16f)))
            {
                pen.LineJoin = LineJoin.Round;
                if (kind == 2)
                {
                    float bw = s * 0.36f, gap = s * 0.3f, h = s * 1.15f;
                    Theme.FillRound(g, new RectangleF(cx - gap / 2 - bw, cy - h / 2, bw, h), bw * 0.3f, c);
                    Theme.FillRound(g, new RectangleF(cx + gap / 2, cy - h / 2, bw, h), bw * 0.3f, c);
                    return;
                }
                if (kind == 1)
                {
                    var tri = new[] { new PointF(cx - s * 0.36f, cy - s * 0.6f), new PointF(cx + s * 0.62f, cy), new PointF(cx - s * 0.36f, cy + s * 0.6f) };
                    g.FillPolygon(b, tri); g.DrawPolygon(pen, tri);
                    return;
                }
                float dir = kind == 3 ? 1 : -1, tw = s * 0.62f, th = s * 0.52f;
                var t2 = new[] { new PointF(cx - dir * tw * 0.55f, cy - th), new PointF(cx + dir * tw * 0.45f, cy), new PointF(cx - dir * tw * 0.55f, cy + th) };
                g.FillPolygon(b, t2); g.DrawPolygon(pen, t2);
                float bx = cx + dir * tw * 0.45f + (dir > 0 ? s * 0.06f : -s * 0.06f - s * 0.2f);
                Theme.FillRound(g, new RectangleF(bx, cy - th, s * 0.2f, th * 2), s * 0.08f, c);
            }
        }

        // Soft drop shadow under a rounded rectangle.
        public static void Shadow(Graphics g, RectangleF r, float rad, int alpha, float spread)
        {
            for (int i = 1; i <= 4; i++)
            {
                float grow = spread * i / 4f;
                var rr = new RectangleF(r.X - grow, r.Y - grow + spread * 0.35f, r.Width + grow * 2, r.Height + grow * 2);
                Theme.FillRound(g, rr, rad + grow, Color.FromArgb(alpha / (i + 1), 0, 0, 0));
            }
        }

        // ---- weather icons, drawn so they match the theme ----
        // kind: 0 clear, 1 partly cloudy, 2 cloudy, 3 fog, 4 drizzle, 5 rain, 6 snow, 7 storm
        public static void Weather(Graphics g, RectangleF r, int kind, bool day, Color ink)
        {
            Color sun = Color.FromArgb(253, 200, 48), moon = Color.FromArgb(226, 232, 255);
            bool darkInk = ink.GetBrightness() < 0.5f;   // light theme: grey clouds, not black ones
            Color cloud = darkInk ? Color.FromArgb(240, 156, 168, 186) : Color.FromArgb(235, ink);
            Color cloudBack = darkInk ? Color.FromArgb(170, 200, 208, 220) : Color.FromArgb(150, ink);
            if (darkInk) moon = Color.FromArgb(120, 130, 160);
            float s = Math.Min(r.Width, r.Height);
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            if (kind == 0)
            {
                if (day) Sun(g, cx, cy, s * 0.42f, sun);
                else Moon(g, cx, cy, s * 0.36f, moon);
                return;
            }
            if (kind == 1)
            {
                if (day) Sun(g, cx + s * 0.14f, cy - s * 0.14f, s * 0.3f, sun);
                else Moon(g, cx + s * 0.16f, cy - s * 0.16f, s * 0.24f, moon);
                Cloud(g, cx - s * 0.06f, cy + s * 0.1f, s * 0.7f, cloud);
                return;
            }
            if (kind == 2 || kind == 3)
            {
                Cloud(g, cx + s * 0.12f, cy - s * 0.1f, s * 0.56f, cloudBack);
                Cloud(g, cx - s * 0.05f, cy + s * 0.06f, s * 0.72f, cloud);
                if (kind == 3)
                    using (var pen = new Pen(Color.FromArgb(190, ink), Math.Max(1.2f, s * 0.06f)))
                    {
                        pen.StartCap = pen.EndCap = LineCap.Round;
                        g.DrawLine(pen, cx - s * 0.36f, cy + s * 0.34f, cx + s * 0.3f, cy + s * 0.34f);
                        g.DrawLine(pen, cx - s * 0.26f, cy + s * 0.46f, cx + s * 0.38f, cy + s * 0.46f);
                    }
                return;
            }
            Cloud(g, cx, cy - s * 0.1f, s * 0.74f, cloud);
            float baseY = cy + s * 0.22f;
            if (kind == 4 || kind == 5)
            {
                Color drop = Color.FromArgb(96, 180, 255);
                int n = kind == 4 ? 2 : 3;
                using (var pen = new Pen(drop, Math.Max(1.4f, s * 0.07f)))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    for (int i = 0; i < n; i++)
                    {
                        float x = cx - s * 0.2f + i * s * (n == 2 ? 0.34f : 0.2f);
                        g.DrawLine(pen, x, baseY, x - s * 0.06f, baseY + s * (kind == 4 ? 0.14f : 0.22f));
                    }
                }
            }
            else if (kind == 6)
            {
                using (var b = new SolidBrush(Color.FromArgb(235, 240, 255)))
                    for (int i = 0; i < 3; i++)
                    {
                        float x = cx - s * 0.2f + i * s * 0.2f, y = baseY + (i % 2) * s * 0.1f, d = s * 0.1f;
                        g.FillEllipse(b, x - d / 2, y, d, d);
                    }
            }
            else
            {
                var bolt = new[] { new PointF(cx + s * 0.02f, baseY - s * 0.04f), new PointF(cx - s * 0.12f, baseY + s * 0.16f),
                    new PointF(cx - s * 0.01f, baseY + s * 0.16f), new PointF(cx - s * 0.08f, baseY + s * 0.34f),
                    new PointF(cx + s * 0.14f, baseY + s * 0.08f), new PointF(cx + s * 0.03f, baseY + s * 0.08f) };
                using (var b = new SolidBrush(sun)) g.FillPolygon(b, bolt);
            }
        }

        static void Sun(Graphics g, float cx, float cy, float rad, Color c)
        {
            float core = rad * 0.55f;
            using (var b = new SolidBrush(c)) g.FillEllipse(b, cx - core, cy - core, core * 2, core * 2);
            using (var pen = new Pen(c, Math.Max(1.2f, rad * 0.14f)))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    g.DrawLine(pen, cx + (float)Math.Cos(a) * rad * 0.78f, cy + (float)Math.Sin(a) * rad * 0.78f,
                        cx + (float)Math.Cos(a) * rad, cy + (float)Math.Sin(a) * rad);
                }
            }
        }

        static void Moon(Graphics g, float cx, float cy, float rad, Color c)
        {
            using (var path = new GraphicsPath())
            using (var cut = new GraphicsPath())
            {
                path.AddEllipse(cx - rad, cy - rad, rad * 2, rad * 2);
                cut.AddEllipse(cx - rad * 0.35f, cy - rad * 1.25f, rad * 2, rad * 2);
                using (var reg = new Region(path))
                {
                    reg.Exclude(cut);
                    using (var b = new SolidBrush(c)) g.FillRegion(b, reg);
                }
            }
        }

        static void Cloud(Graphics g, float cx, float cy, float w, Color c)
        {
            float h = w * 0.42f;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(cx - w * 0.5f, cy - h * 0.1f, w * 0.44f, h * 0.9f);
                path.AddEllipse(cx - w * 0.28f, cy - h * 0.75f, w * 0.52f, w * 0.52f);
                path.AddEllipse(cx + w * 0.02f, cy - h * 0.38f, w * 0.48f, h * 1.12f);
                path.AddRectangle(new RectangleF(cx - w * 0.3f, cy + h * 0.1f, w * 0.62f, h * 0.7f));
                path.FillMode = FillMode.Winding;
                using (var b = new SolidBrush(c)) g.FillPath(b, path);
            }
        }
    }
}
