// Profiles: named snapshots of the whole layout and settings (profiles\<Name>.txt).
// Automations: "when X, do Y" rules (settings.txt auto.<n>=...), checked on the main tick.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace StayBar
{
    static class Profiles
    {
        public static string Dir { get { return Path.Combine(Program.DataDir, "profiles"); } }
        public static readonly string[] Presets = { "Study", "Gaming", "Coding", "Night" };

        // Keys a profile doesn't touch: this PC's things, not the bar's look and layout.
        public static bool Managed(string key)
        {
            string k = key.ToLowerInvariant();
            return !(k.StartsWith("debug.") || k.StartsWith("profile.") || k.StartsWith("auto.") || k == "weather.geo" ||
                     k.StartsWith("app.name:") || k.StartsWith("app.icon:") || k == "automations.on");
        }

        public static string Current { get { return Settings.Get("profile.current", ""); } }

        public static List<string> Names()
        {
            var order = Settings.Get("profile.order", string.Join(",", Presets)).Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            var files = Directory.Exists(Dir) ? Directory.GetFiles(Dir, "*.txt").Select(f => Path.GetFileNameWithoutExtension(f)).ToList() : new List<string>();
            var list = order.Where(n => files.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
            foreach (string f in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase)) if (!list.Contains(f, StringComparer.OrdinalIgnoreCase)) list.Add(f);
            return list;
        }

        static string FileOf(string name) { return Path.Combine(Dir, Clean(name) + ".txt"); }

        public static string Clean(string name)
        {
            string n = new string((name ?? "").Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_').ToArray()).Trim();
            return n.Length == 0 ? "Profile" : n;
        }

        // First run: four ready-made profiles, built from the current settings.
        public static void EnsurePresets()
        {
            if (Directory.Exists(Dir)) return;
            try
            {
                Directory.CreateDirectory(Dir);
                var now = Snapshot();
                WritePreset("Study", now, new Dictionary<string, string>
                {
                    { "zones.right", "countdown,nextclass,todo,focus,calendar,weather,battery,keyboard,clock,desktop" },
                    { "hide", "fullscreen" },
                });
                WritePreset("Gaming", now, new Dictionary<string, string>
                {
                    { "zones.right", "cpu,gpu,net,volume,battery,clock,desktop" },
                    { "hide", "auto" }, { "dim.idle", "0" },
                });
                WritePreset("Coding", now, new Dictionary<string, string>
                {
                    { "zones.right", "claude,github,stack:cpu+ram,net,clipboard,volume,battery,clock,desktop" },
                    { "hide", "fullscreen" },
                });
                WritePreset("Night", now, new Dictionary<string, string>
                {
                    { "zones.right", "weather,volume,battery,clock,desktop" },
                    { "theme", "midnight" }, { "dim.idle", "8" }, { "dim.opacity", "40" },
                });
                Settings.Set("profile.order", string.Join(",", Presets));
            }
            catch (Exception ex) { Program.Log("Profiles: " + ex.Message); }
        }

        static void WritePreset(string name, Dictionary<string, string> baseline, Dictionary<string, string> over)
        {
            var d = new Dictionary<string, string>(baseline, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in over) d[kv.Key] = kv.Value;
            Write(FileOf(name), d);
        }

        static Dictionary<string, string> Snapshot()
        {
            Config.Save();
            var all = Settings.WithPrefix("");
            return all.Where(kv => Managed(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        }

        static void Write(string path, Dictionary<string, string> d)
        {
            var lines = new List<string> { "# StayBar profile. Any setting from settings.txt can go here." };
            lines.AddRange(d.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => kv.Key + "=" + kv.Value));
            File.WriteAllLines(path, lines.ToArray());
        }

        static Dictionary<string, string> Read(string path)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(path))
            {
                if (line.TrimStart().StartsWith("#")) continue;
                int i = line.IndexOf('=');
                if (i > 0) d[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
            }
            return d;
        }

        public static void SaveAs(string name)
        {
            name = Clean(name);
            Directory.CreateDirectory(Dir);
            Write(FileOf(name), Snapshot());
            var order = Names();
            if (!order.Contains(name, StringComparer.OrdinalIgnoreCase)) order.Add(name);
            Settings.Set("profile.order", string.Join(",", order.ToArray()));
            Settings.Set("profile.current", name);
        }

        public static bool Exists(string name) { return File.Exists(FileOf(name)); }

        // Switches to a profile: its settings replace the current ones (pins, app names and this PC's things stay).
        public static bool Apply(string name, bool toast)
        {
            string path = FileOf(name);
            if (!File.Exists(path)) return false;
            try
            {
                Dictionary<string, string> p = Read(path);
                Settings.ReplaceManaged(p, Managed);
                Settings.Set("profile.current", Clean(name));
                BarForm bar = BarForm.Instance;
                Config.Load();
                if (bar != null) { bar.ApplyConfig(); bar.AfterLayoutChange(); }
                if (toast && bar != null) bar.ShowToast("Profile: " + Clean(name), "Win+Alt+P switches to the next one.");
                return true;
            }
            catch (Exception ex) { Program.Log("Profile " + name + ": " + ex.Message); return false; }
        }

        public static void Delete(string name)
        {
            try { File.Delete(FileOf(name)); } catch { }
            if (Current.Equals(Clean(name), StringComparison.OrdinalIgnoreCase)) Settings.Set("profile.current", "");
        }

        public static void Rename(string name, string to)
        {
            to = Clean(to);
            if (to.Equals(name, StringComparison.OrdinalIgnoreCase) || Exists(to)) return;
            File.Move(FileOf(name), FileOf(to));
            var order = Names().Select(n => n.Equals(name, StringComparison.OrdinalIgnoreCase) ? to : n).ToArray();
            Settings.Set("profile.order", string.Join(",", order));
            if (Current.Equals(name, StringComparison.OrdinalIgnoreCase)) Settings.Set("profile.current", to);
        }

        public static void Export(string name, string to) { File.Copy(FileOf(name), to, true); }

        public static string Import(string from)
        {
            string name = Clean(Path.GetFileNameWithoutExtension(from));
            while (Exists(name)) name += " 2";
            Directory.CreateDirectory(Dir);
            File.Copy(from, FileOf(name));
            var order = Names();
            if (!order.Contains(name)) order.Add(name);
            Settings.Set("profile.order", string.Join(",", order.ToArray()));
            return name;
        }

        // Win+Alt+P (index -1 = the next profile) and Win+Alt+1..4.
        public static void Hotkey(int index)
        {
            List<string> names = Names();
            if (names.Count == 0) return;
            if (index < 0)
            {
                int i = names.FindIndex(n => n.Equals(Current, StringComparison.OrdinalIgnoreCase));
                Apply(names[(i + 1) % names.Count], true);
            }
            else if (index < names.Count) Apply(names[index], true);
        }
    }

    // ------------------------------------------------------------------
    // Automations: settings.txt "auto.<n>=on|<when>|<do>"
    //   when: fullscreen | time:23:00-07:00 | battery | plugged | batterybelow:20 | app:<exe or name> | monitors:2 | wifi:<name>
    //   do:   profile:<Name> | show:<widget> | hide:<widget> | autohide:on | autohide:off | dim | focus | quiet
    // A rule acts while its condition holds and undoes itself afterwards (focus just starts the timer).
    // ------------------------------------------------------------------
    class Rule
    {
        public string Key;          // auto.<n>
        public bool On;
        public string When, Do;
        public bool Active;
        public string Before;       // the profile to go back to
    }

    static class Automations
    {
        static List<Rule> rules = new List<Rule>();
        static int loadedGen = -1;
        public static int Gen;      // bumped when rules change

        public static bool Quiet;                                  // no StayBar sounds
        public static bool DimNow;                                 // dim the bar
        public static readonly HashSet<string> Hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static readonly List<string> Extra = new List<string>();   // widgets shown on top of the layout
        public static string HideOverride;                         // auto | fullscreen, while a rule says so
        static int screens = Screen.AllScreens.Length;

        public static readonly string[][] WhenKinds =
        {
            new[] { "fullscreen", "A fullscreen app or game runs", "" },
            new[] { "time", "Between two times", "23:00-07:00" },
            new[] { "battery", "On battery", "" },
            new[] { "plugged", "Plugged in", "" },
            new[] { "batterybelow", "Battery below N %", "20" },
            new[] { "app", "An app is open", "discord.exe" },
            new[] { "monitors", "At least N screens", "2" },
            new[] { "wifi", "Connected to the Wi-Fi", "Network name" },
        };

        public static readonly string[][] DoKinds =
        {
            new[] { "profile", "Switch to the profile", "Gaming" },
            new[] { "show", "Show the widget", "cpu" },
            new[] { "hide", "Hide the widget", "media" },
            new[] { "autohide", "Auto-hide the bar", "on" },
            new[] { "dim", "Dim the bar", "" },
            new[] { "focus", "Start the focus timer", "" },
            new[] { "quiet", "No StayBar sounds", "" },
        };

        public static List<Rule> Rules
        {
            get
            {
                if (loadedGen != Gen) Load();
                return rules;
            }
        }

        static void Load()
        {
            var old = rules.ToDictionary(r => r.Key, r => r);
            var list = new List<Rule>();
            foreach (var kv in Settings.WithPrefix("auto.").OrderBy(kv => { int n; return int.TryParse(kv.Key, out n) ? n : 9999; }))
            {
                string[] p = kv.Value.Split('|');
                if (p.Length < 3) continue;
                var r = new Rule();
                r.Key = "auto." + kv.Key; r.On = p[0] == "on"; r.When = p[1]; r.Do = p[2];
                Rule prev;
                if (old.TryGetValue(r.Key, out prev) && prev.When == r.When && prev.Do == r.Do) { r.Active = prev.Active; r.Before = prev.Before; }
                list.Add(r);
            }
            rules = list;
            loadedGen = Gen;
            // a rule that was acting and is gone (or changed) now: undo what it did
            foreach (Rule prev in old.Values)
                if (prev.Active && !list.Any(r => r.Key == prev.Key && r.When == prev.When && r.Do == prev.Do && r.Active))
                    Stop(prev, BarForm.Instance);
        }

        // First run: the two example rules, switched off.
        public static void EnsureExamples()
        {
            if (Settings.Get("auto.examples", "") == "done") return;
            if (Settings.WithPrefix("auto.").Count == 0)
            {
                Settings.Set("auto.1", "off|fullscreen|profile:Gaming");
                Settings.Set("auto.2", "off|time:23:00-07:00|profile:Night");
            }
            Settings.Set("auto.examples", "done");
            Gen++;
        }

        public static void Save(Rule r) { Settings.Set(r.Key, (r.On ? "on" : "off") + "|" + r.When + "|" + r.Do); Gen++; }

        public static void Add(string when, string act)
        {
            int n = 1;
            while (Settings.Get("auto." + n, "").Length > 0) n++;
            Settings.Set("auto." + n, "on|" + when + "|" + act);
            Gen++;
        }

        public static void Remove(Rule r)
        {
            if (r.Active) Stop(r, BarForm.Instance);
            Settings.Set(r.Key, "");
            Gen++;
        }

        public static void SetOn(Rule r, bool on)
        {
            if (!on && r.Active) Stop(r, BarForm.Instance);
            r.On = on;
            Save(r);
        }

        public static bool NeedsWifi { get { return Rules.Any(r => r.On && r.When.StartsWith("wifi:")); } }

        public static void ScreensChanged() { screens = Screen.AllScreens.Length; }

        // The main bar's 500 ms tick.
        public static void Tick(BarForm bar)
        {
            List<Rule> list = Rules;
            if (list.Count == 0) return;
            foreach (Rule r in list)
            {
                if (!r.On) continue;
                bool now = Holds(r.When, bar);
                if (now && !r.Active) Start(r, bar);
                else if (!now && r.Active) Stop(r, bar);
            }
        }

        static bool Holds(string when, BarForm bar)
        {
            string kind = when, arg = "";
            int c = when.IndexOf(':');
            if (c > 0) { kind = when.Substring(0, c); arg = when.Substring(c + 1).Trim(); }
            switch (kind)
            {
                case "fullscreen": return bar.FullscreenApp;
                case "time":
                    {
                        string[] t = arg.Split('-');
                        TimeSpan a, b;
                        if (t.Length != 2 || !TimeSpan.TryParse(t[0].Trim(), out a) || !TimeSpan.TryParse(t[1].Trim(), out b)) return false;
                        TimeSpan now = DateTime.Now.TimeOfDay;
                        return a <= b ? now >= a && now < b : now >= a || now < b;
                    }
                case "battery":
                case "plugged":
                case "batterybelow":
                    {
                        PowerStatus ps = BatteryWidget.Battery();
                        if (ps == null) return false;
                        bool onBattery = ps.PowerLineStatus == PowerLineStatus.Offline;
                        if (kind == "battery") return onBattery;
                        if (kind == "plugged") return !onBattery;
                        int n;
                        return onBattery && int.TryParse(arg, out n) && ps.BatteryLifePercent * 100 < n;
                    }
                case "app":
                    {
                        if (arg.Length == 0) return false;
                        string a = arg.ToLowerInvariant();
                        foreach (BarForm b in BarForm.All)
                            foreach (AppGroup g in b.Groups)
                            {
                                if (g.Windows.Count == 0) continue;
                                string exe = g.ExePath == null ? "" : Path.GetFileName(g.ExePath).ToLowerInvariant();
                                if (exe == a || exe == a + ".exe" || (g.Name ?? "").ToLowerInvariant().Contains(a)) return true;
                            }
                        return false;
                    }
                case "monitors": { int n; return int.TryParse(arg, out n) && screens >= n; }
                case "wifi": return arg.Length > 0 && string.Equals(Radios.Network, arg, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        static void Start(Rule r, BarForm bar)
        {
            r.Active = true;
            string kind, arg;
            SplitDo(r.Do, out kind, out arg);
            switch (kind)
            {
                case "profile":
                    if (Profiles.Exists(arg) && !Profiles.Current.Equals(arg, StringComparison.OrdinalIgnoreCase))
                    {
                        r.Before = Profiles.Current;
                        if (r.Before.Length == 0) { Profiles.SaveAs("Before automation"); r.Before = "Before automation"; }
                        Profiles.Apply(arg, true);
                    }
                    break;
                case "show": if (!Extra.Contains(arg)) Extra.Add(arg); break;
                case "hide": Hidden.Add(arg); break;
                case "autohide": HideOverride = arg == "off" ? "fullscreen" : "auto"; break;
                case "dim": DimNow = true; break;
                case "focus": if (StayBar.Focus.State == StayBar.Focus.Phase.Idle) StayBar.Focus.Toggle(); break;
                case "quiet": Quiet = true; break;
            }
            Changed(bar);
        }

        static void Stop(Rule r, BarForm bar)
        {
            r.Active = false;
            string kind, arg;
            SplitDo(r.Do, out kind, out arg);
            switch (kind)
            {
                case "profile":
                    if (!string.IsNullOrEmpty(r.Before) && Profiles.Current.Equals(arg, StringComparison.OrdinalIgnoreCase)) Profiles.Apply(r.Before, true);
                    r.Before = null;
                    break;
                case "show": Extra.Remove(arg); break;
                case "hide": Hidden.Remove(arg); break;
                case "autohide": HideOverride = null; break;
                case "dim": DimNow = false; break;
                case "quiet": Quiet = false; break;
            }
            Changed(bar);
        }

        static void Changed(BarForm bar)
        {
            if (bar == null) return;
            foreach (BarForm b in BarForm.All) { b.AfterLayoutChange(); }
            bar.ApplyConfig();
        }

        public static void SplitDo(string act, out string kind, out string arg)
        {
            kind = act; arg = "";
            int c = act.IndexOf(':');
            if (c > 0) { kind = act.Substring(0, c); arg = act.Substring(c + 1).Trim(); }
        }

        public static string Describe(Rule r)
        {
            string wk = r.When, wa = "";
            int c = r.When.IndexOf(':');
            if (c > 0) { wk = r.When.Substring(0, c); wa = r.When.Substring(c + 1); }
            string when;
            switch (wk)
            {
                case "fullscreen": when = "While a fullscreen app or game runs"; break;
                case "time": when = "Between " + wa.Replace("-", " and "); break;
                case "battery": when = "On battery"; break;
                case "plugged": when = "While plugged in"; break;
                case "batterybelow": when = "Battery below " + wa + "%"; break;
                case "app": when = "While " + wa + " is open"; break;
                case "monitors": when = "With " + wa + " or more screens"; break;
                case "wifi": when = "On the Wi-Fi \"" + wa + "\""; break;
                default: when = r.When; break;
            }
            string dk, da;
            SplitDo(r.Do, out dk, out da);
            string act;
            switch (dk)
            {
                case "profile": act = "switch to the profile " + da; break;
                case "show": act = "show " + BarForm.ItemName(da); break;
                case "hide": act = "hide " + BarForm.ItemName(da); break;
                case "autohide": act = da == "off" ? "turn auto-hide off" : "auto-hide the bar"; break;
                case "dim": act = "dim the bar"; break;
                case "focus": act = "start the focus timer"; break;
                case "quiet": act = "no StayBar sounds"; break;
                default: act = r.Do; break;
            }
            return when + ": " + act;
        }
    }
}
