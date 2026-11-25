// Data behind the phase-3 widgets. Everything slow runs off the UI thread (Bg thread or the thread
// pool for network), only while a widget that needs it is on a bar, and publishes plain values.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace StayBar
{
    // ------------------------------------------------------------------
    // Small shared helpers
    // ------------------------------------------------------------------
    static class Net
    {
        public static string Get(string url) { return Get(url, 10000); }

        public static string Get(string url, int timeoutMs)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = timeoutMs; req.ReadWriteTimeout = timeoutMs;
            req.UserAgent = "StayBar/" + Program.Version;
            req.AutomaticDecompression = DecompressionMethods.GZip;   // not Deflate: some servers send zlib, which .NET can't read
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }
    }

    // The weather city's coordinates (Weather saves them as weather.geo = city|lat|lon|name).
    static class Geo
    {
        public static bool Get(out double lat, out double lon, out string name)
        {
            lat = 0; lon = 0; name = "";
            if (Svc.Demo) { lat = 51.5074; lon = -0.1278; name = "London"; return true; }
            string[] p = Settings.Get("weather.geo", "").Split('|');
            if (p.Length < 4) return false;
            name = p[3];
            return double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out lat) &&
                   double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out lon);
        }
    }

    // ------------------------------------------------------------------
    // Sun and moon, computed locally (no network)
    // ------------------------------------------------------------------
    static class Sky
    {
        // NOAA sunrise/sunset equations. Returns false for polar day/night.
        public static bool SunTimes(DateTime date, double lat, double lon, out DateTime rise, out DateTime set)
        {
            rise = set = DateTime.MinValue;
            int n = date.DayOfYear;
            double gamma = 2 * Math.PI / 365 * (n - 1);
            double eqTime = 229.18 * (0.000075 + 0.001868 * Math.Cos(gamma) - 0.032077 * Math.Sin(gamma) - 0.014615 * Math.Cos(2 * gamma) - 0.040849 * Math.Sin(2 * gamma));
            double decl = 0.006918 - 0.399912 * Math.Cos(gamma) + 0.070257 * Math.Sin(gamma) - 0.006758 * Math.Cos(2 * gamma) + 0.000907 * Math.Sin(2 * gamma)
                          - 0.002697 * Math.Cos(3 * gamma) + 0.00148 * Math.Sin(3 * gamma);
            double latR = lat * Math.PI / 180;
            double cosH = (Math.Cos(90.833 * Math.PI / 180) / (Math.Cos(latR) * Math.Cos(decl))) - Math.Tan(latR) * Math.Tan(decl);
            if (cosH < -1 || cosH > 1) return false;
            double ha = Math.Acos(cosH) * 180 / Math.PI;
            double riseUtcMin = 720 - 4 * (lon + ha) - eqTime, setUtcMin = 720 - 4 * (lon - ha) - eqTime;
            DateTime day = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Utc);
            rise = day.AddMinutes(riseUtcMin).ToLocalTime();
            set = day.AddMinutes(setUtcMin).ToLocalTime();
            return true;
        }

        // 0 = new moon, 0.5 = full moon.
        public static double MoonPhase(DateTime utc)
        {
            const double synodic = 29.530588853;
            double days = (utc - new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc)).TotalDays;
            double p = (days % synodic) / synodic;
            return p < 0 ? p + 1 : p;
        }

        public static double Illumination(double phase) { return (1 - Math.Cos(2 * Math.PI * phase)) / 2; }

        public static string MoonName(double p)
        {
            if (p < 0.0339 || p > 0.9661) return "New moon";
            if (p < 0.2161) return "Waxing crescent";
            if (p < 0.2839) return "First quarter";
            if (p < 0.4661) return "Waxing gibbous";
            if (p < 0.5339) return "Full moon";
            if (p < 0.7161) return "Waning gibbous";
            if (p < 0.7839) return "Last quarter";
            return "Waning crescent";
        }
    }

    // ------------------------------------------------------------------
    // Study: to-do list, weekly schedule, habits, calendar (ICS), word of the day
    // ------------------------------------------------------------------
    class TodoItem { public bool Done; public string Text; }

    static class Todo
    {
        static List<TodoItem> items;
        static string FilePath { get { return Path.Combine(Program.DataDir, "todo.txt"); } }

        public static List<TodoItem> Items
        {
            get
            {
                if (items != null) return items;
                items = new List<TodoItem>();
                if (Svc.Demo)
                {
                    foreach (string s in new[] { "Physics problem set 4", "Read chapter 7", "Email the club about Friday" }) items.Add(new TodoItem { Text = s });
                    items.Add(new TodoItem { Text = "Buy notebooks", Done = true });
                    return items;
                }
                try
                {
                    if (File.Exists(FilePath))
                        foreach (string line in File.ReadAllLines(FilePath))
                        {
                            string l = line.TrimEnd();
                            if (l.Length == 0) continue;
                            bool done = l.StartsWith("[x]", StringComparison.OrdinalIgnoreCase);
                            string t = l.StartsWith("[") && l.Length > 3 && l[2] == ']' ? l.Substring(3).Trim() : l.Trim();
                            if (t.Length > 0) items.Add(new TodoItem { Done = done, Text = t });
                        }
                }
                catch { }
                return items;
            }
        }

        public static int Open { get { return Items.Count(i => !i.Done); } }

        public static void Save()
        {
            if (Svc.Demo) return;
            try { File.WriteAllLines(FilePath, Items.Select(i => (i.Done ? "[x] " : "[ ] ") + i.Text).ToArray()); } catch { }
            BarForm.Poke();
        }

        public static void Add(string text) { if (!string.IsNullOrWhiteSpace(text)) { Items.Add(new TodoItem { Text = text.Trim() }); Save(); } }
        public static void ClearDone() { Items.RemoveAll(i => i.Done); Save(); }
    }

    class ClassSlot { public DayOfWeek Day; public TimeSpan Start, End; public string Title = "", Room = ""; public DateTime? Date; }

    // schedule.txt: one class per line, "Mon 08:30-09:25 Maths | Room 12" (days: Mon,Tue... or Mon-Fri).
    static class Schedule
    {
        static List<ClassSlot> slots;
        static DateTime stamp;
        public static string FilePath { get { return Path.Combine(Program.DataDir, "schedule.txt"); } }
        static readonly string[] DayNames = { "sun", "mon", "tue", "wed", "thu", "fri", "sat" };

        public static void EnsureFile()
        {
            if (File.Exists(FilePath)) return;
            File.WriteAllLines(FilePath, new[]
            {
                "# Your weekly timetable, one class per line:",
                "#   <day(s)> <start>-<end> <subject> | <room or note>",
                "# Days: Mon Tue Wed Thu Fri Sat Sun, or ranges like Mon-Fri, or lists like Mon,Wed",
                "# Examples (remove the # to use them):",
                "# Mon,Wed 08:30-09:25 Maths | Room 12",
                "# Tue 10:00-11:30 Physics | Lab B",
                "# Mon-Fri 13:00-13:45 Lunch",
            });
        }

        public static List<ClassSlot> Slots
        {
            get
            {
                if (Svc.Demo) return Demo();
                DateTime t = File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue;
                if (slots != null && t == stamp) return slots;
                stamp = t;
                var list = new List<ClassSlot>();
                try
                {
                    if (File.Exists(FilePath))
                        foreach (string raw in File.ReadAllLines(FilePath))
                        {
                            string line = raw.Trim();
                            if (line.Length == 0 || line.StartsWith("#")) continue;
                            Match m = Regex.Match(line, @"^([A-Za-z,\-]+)\s+(\d{1,2}[:.]\d{2})\s*-\s*(\d{1,2}[:.]\d{2})\s+(.+)$");
                            if (!m.Success) continue;
                            TimeSpan a, b;
                            if (!TimeSpan.TryParse(m.Groups[2].Value.Replace('.', ':'), out a) || !TimeSpan.TryParse(m.Groups[3].Value.Replace('.', ':'), out b)) continue;
                            string rest = m.Groups[4].Value, room = "";
                            int bar = rest.IndexOf('|');
                            if (bar >= 0) { room = rest.Substring(bar + 1).Trim(); rest = rest.Substring(0, bar).Trim(); }
                            foreach (DayOfWeek d in Days(m.Groups[1].Value))
                                list.Add(new ClassSlot { Day = d, Start = a, End = b, Title = rest, Room = room });
                        }
                }
                catch { }
                slots = list;
                return slots;
            }
        }

        static IEnumerable<DayOfWeek> Days(string spec)
        {
            foreach (string part in spec.ToLowerInvariant().Split(','))
            {
                string[] r = part.Split('-');
                int a = Array.IndexOf(DayNames, r[0].Length >= 3 ? r[0].Substring(0, 3) : r[0]);
                if (a < 0) continue;
                if (r.Length == 1) { yield return (DayOfWeek)a; continue; }
                int b = Array.IndexOf(DayNames, r[1].Length >= 3 ? r[1].Substring(0, 3) : r[1]);
                if (b < 0) continue;
                for (int i = a; ; i = (i + 1) % 7) { yield return (DayOfWeek)i; if (i == b) break; }
            }
        }

        static List<ClassSlot> Demo()
        {
            var now = DateTime.Now;
            var d = now.DayOfWeek;
            var s = new TimeSpan(now.Hour, now.Minute, 0);
            return new List<ClassSlot>
            {
                new ClassSlot { Day = d, Start = s.Add(TimeSpan.FromMinutes(-20)), End = s.Add(TimeSpan.FromMinutes(25)), Title = "Maths", Room = "Room 12" },
                new ClassSlot { Day = d, Start = s.Add(TimeSpan.FromMinutes(35)), End = s.Add(TimeSpan.FromMinutes(90)), Title = "Physics", Room = "Lab B" },
            };
        }

        // The class going on now (cur) and the next one (next), from the timetable and today's calendar events.
        public static void Now(IEnumerable<CalEvent> events, out ClassSlot cur, out ClassSlot next, out DateTime nextAt)
        {
            cur = null; next = null; nextAt = DateTime.MaxValue;
            DateTime now = DateTime.Now;
            var all = new List<KeyValuePair<DateTime, ClassSlot>>();
            for (int day = 0; day < 8; day++)
            {
                DateTime date = now.Date.AddDays(day);
                foreach (ClassSlot c in Slots) if (c.Day == date.DayOfWeek) all.Add(new KeyValuePair<DateTime, ClassSlot>(date + c.Start, c));
            }
            if (events != null)
                foreach (CalEvent e in events)
                    if (!e.AllDay && e.Start >= now.Date && e.Start < now.Date.AddDays(8))
                        all.Add(new KeyValuePair<DateTime, ClassSlot>(e.Start, new ClassSlot { Day = e.Start.DayOfWeek, Start = e.Start.TimeOfDay, End = e.End.TimeOfDay, Title = e.Title ?? "", Room = e.Location ?? "", Date = e.Start.Date }));
            foreach (var kv in all.OrderBy(k => k.Key))
            {
                DateTime end = kv.Key.Date + kv.Value.End;
                if (end <= kv.Key) end = kv.Key.AddHours(1);
                if (kv.Key <= now && now < end) { if (cur == null) cur = kv.Value; continue; }
                if (kv.Key > now) { next = kv.Value; nextAt = kv.Key; break; }
            }
        }
    }

    // habits.txt: "Name|2026-11-20,2026-11-21"
    static class Habits
    {
        static Dictionary<string, HashSet<DateTime>> data;
        static List<string> order;
        static string FilePath { get { return Path.Combine(Program.DataDir, "habits.txt"); } }

        static void Load()
        {
            if (data != null) return;
            data = new Dictionary<string, HashSet<DateTime>>(StringComparer.OrdinalIgnoreCase);
            order = new List<string>();
            if (Svc.Demo)
            {
                var rnd = new Random(3);
                foreach (string h in new[] { "Exercise", "Read 20 min", "No phone after 23:00" })
                {
                    order.Add(h);
                    var set = new HashSet<DateTime>();
                    for (int i = 1; i < 30; i++) if (rnd.Next(10) < 7) set.Add(DateTime.Today.AddDays(-i));
                    if (h == "Exercise") set.Add(DateTime.Today);
                    data[h] = set;
                }
                return;
            }
            try
            {
                if (File.Exists(FilePath))
                    foreach (string line in File.ReadAllLines(FilePath))
                    {
                        int bar = line.IndexOf('|');
                        string name = (bar < 0 ? line : line.Substring(0, bar)).Trim();
                        if (name.Length == 0 || name.StartsWith("#")) continue;
                        var set = new HashSet<DateTime>();
                        if (bar >= 0)
                            foreach (string d in line.Substring(bar + 1).Split(','))
                            {
                                DateTime dt;
                                if (DateTime.TryParseExact(d.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) set.Add(dt);
                            }
                        data[name] = set; order.Add(name);
                    }
            }
            catch { }
            if (order.Count == 0) { order.Add("Exercise"); data["Exercise"] = new HashSet<DateTime>(); }
        }

        public static List<string> Names { get { Load(); return order; } }
        public static bool Done(string habit, DateTime day) { Load(); HashSet<DateTime> s; return data.TryGetValue(habit, out s) && s.Contains(day.Date); }

        public static int Streak(string habit)
        {
            int n = 0;
            DateTime d = DateTime.Today;
            if (!Done(habit, d)) d = d.AddDays(-1);
            while (Done(habit, d)) { n++; d = d.AddDays(-1); }
            return n;
        }

        public static void Toggle(string habit, DateTime day)
        {
            Load();
            HashSet<DateTime> s;
            if (!data.TryGetValue(habit, out s)) { s = new HashSet<DateTime>(); data[habit] = s; order.Add(habit); }
            if (!s.Remove(day.Date)) s.Add(day.Date);
            Save();
        }

        public static void Add(string habit) { Load(); if (!data.ContainsKey(habit)) { data[habit] = new HashSet<DateTime>(); order.Add(habit); Save(); } }
        public static void Remove(string habit) { Load(); data.Remove(habit); order.Remove(habit); Save(); }

        static void Save()
        {
            if (Svc.Demo) return;
            try
            {
                File.WriteAllLines(FilePath, order.Select(h => h + "|" + string.Join(",", data[h].Where(d => d > DateTime.Today.AddDays(-400)).OrderBy(d => d).Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToArray())).ToArray());
            }
            catch { }
            BarForm.Poke();
        }
    }

    class CalEvent { public DateTime Start, End; public string Title = "", Location = ""; public bool AllDay; }

    // Calendar events from an ICS link (a Google / Outlook "secret address"). Simple weekly/daily repeats are expanded.
    static class Ics
    {
        static readonly Dictionary<string, List<CalEvent>> cache = new Dictionary<string, List<CalEvent>>();

        public static List<CalEvent> For(string url)
        {
            if (Svc.Demo) return Demo();
            lock (cache) { List<CalEvent> l; return cache.TryGetValue(url ?? "", out l) ? l : null; }
        }

        public static IEnumerable<CalEvent> AllCached()
        {
            if (Svc.Demo) return Demo();
            lock (cache) return cache.Values.SelectMany(l => l).ToList();
        }

        static List<CalEvent> Demo()
        {
            DateTime t = DateTime.Today;
            return new List<CalEvent>
            {
                new CalEvent { Start = t.AddDays(1).AddHours(9), End = t.AddDays(1).AddHours(10), Title = "Chemistry exam", Location = "Hall A" },
                new CalEvent { Start = t.AddDays(2).AddHours(17), End = t.AddDays(2).AddHours(18), Title = "Football practice", Location = "" },
                new CalEvent { Start = t.AddDays(4), End = t.AddDays(5), Title = "Mum's birthday", Location = "", AllDay = true },
            };
        }

        public static void Fetch(string url)
        {
            string text = Net.Get(url.Replace("webcal://", "https://"), 15000);
            var list = Parse(text);
            lock (cache) cache[url] = list;
        }

        static List<CalEvent> Parse(string text)
        {
            var list = new List<CalEvent>();
            // unfold continuation lines
            string[] lines = Regex.Replace(text, "\r?\n[ \t]", "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            Dictionary<string, string> ev = null;
            DateTime from = DateTime.Today.AddDays(-1), to = DateTime.Today.AddDays(31);
            foreach (string line in lines)
            {
                if (line == "BEGIN:VEVENT") { ev = new Dictionary<string, string>(); continue; }
                if (line == "END:VEVENT" && ev != null)
                {
                    try { Expand(ev, from, to, list); } catch { }
                    ev = null; continue;
                }
                if (ev == null) continue;
                int c = line.IndexOf(':');
                if (c <= 0) continue;
                string name = line.Substring(0, c), val = line.Substring(c + 1);
                string key = name.Split(';')[0].ToUpperInvariant();
                if (!ev.ContainsKey(key)) { ev[key] = val; ev[key + "#P"] = name; }
            }
            return list.OrderBy(e => e.Start).ToList();
        }

        static void Expand(Dictionary<string, string> ev, DateTime from, DateTime to, List<CalEvent> list)
        {
            string ds, de, sum, loc, rrule;
            if (!ev.TryGetValue("DTSTART", out ds)) return;
            bool allDay;
            DateTime start = When(ds, ev["DTSTART#P"], out allDay);
            DateTime end = ev.TryGetValue("DTEND", out de) ? When(de, ev["DTEND#P"], out allDay) : start.AddHours(allDay ? 24 : 1);
            if (allDay && !ev["DTSTART#P"].Contains("VALUE=DATE")) allDay = false;
            ev.TryGetValue("SUMMARY", out sum); ev.TryGetValue("LOCATION", out loc);
            sum = Unescape(sum ?? "(no title)"); loc = Unescape(loc ?? "");
            TimeSpan len = end - start;
            if (ev.TryGetValue("RRULE", out rrule))
            {
                var r = rrule.Split(';').Select(p => p.Split('=')).Where(p => p.Length == 2).ToDictionary(p => p[0].ToUpperInvariant(), p => p[1]);
                string freq; r.TryGetValue("FREQ", out freq);
                int step = 1; string iv; if (r.TryGetValue("INTERVAL", out iv)) int.TryParse(iv, out step); step = Math.Max(1, step);
                DateTime until = to; string u; bool ad;
                if (r.TryGetValue("UNTIL", out u)) { until = When(u, "UNTIL", out ad); if (until > to) until = to; }
                if (freq == "DAILY" || freq == "WEEKLY")
                {
                    var days = new HashSet<DayOfWeek>();
                    string by;
                    if (freq == "WEEKLY" && r.TryGetValue("BYDAY", out by))
                        foreach (string d in by.Split(','))
                        {
                            string dd = d.Length >= 2 ? d.Substring(d.Length - 2) : d;
                            int i = Array.IndexOf(new[] { "SU", "MO", "TU", "WE", "TH", "FR", "SA" }, dd);
                            if (i >= 0) days.Add((DayOfWeek)i);
                        }
                    if (freq == "WEEKLY" && days.Count == 0) days.Add(start.DayOfWeek);
                    DateTime weekStart = start.Date.AddDays(-(int)start.DayOfWeek);
                    for (DateTime d = start.Date; d <= until && d <= to; d = d.AddDays(1))
                    {
                        if (freq == "DAILY" && ((d - start.Date).Days % step) != 0) continue;
                        if (freq == "WEEKLY" && (!days.Contains(d.DayOfWeek) || (((d.AddDays(-(int)d.DayOfWeek) - weekStart).Days / 7) % step) != 0)) continue;
                        DateTime s = d + start.TimeOfDay;
                        if (s + len < from) continue;
                        list.Add(new CalEvent { Start = s, End = s + len, Title = sum, Location = loc, AllDay = allDay });
                    }
                    return;
                }
            }
            if (end < from || start > to) return;
            list.Add(new CalEvent { Start = start, End = end, Title = sum, Location = loc, AllDay = allDay });
        }

        static DateTime When(string v, string prop, out bool allDay)
        {
            allDay = v.Length == 8;
            string f = allDay ? "yyyyMMdd" : "yyyyMMdd'T'HHmmss";
            DateTime dt;
            bool utc = v.EndsWith("Z");
            DateTime.TryParseExact(v.TrimEnd('Z'), f, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt);
            if (utc) return DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime();
            Match tz = Regex.Match(prop ?? "", "TZID=([^;:]+)");
            if (tz.Success)
            {
                try { return TimeZoneInfo.ConvertTime(dt, TzFromIana(tz.Groups[1].Value.Trim('"')), TimeZoneInfo.Local); } catch { }
            }
            return dt;
        }

        // ICS files use IANA names (Europe/Madrid); Windows uses its own. A few common ones; otherwise local time.
        static TimeZoneInfo TzFromIana(string iana)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Europe/London", "GMT Standard Time" }, { "Europe/Madrid", "Romance Standard Time" }, { "Europe/Paris", "Romance Standard Time" },
                { "Europe/Berlin", "W. Europe Standard Time" }, { "Europe/Rome", "W. Europe Standard Time" }, { "Europe/Amsterdam", "W. Europe Standard Time" },
                { "Europe/Kyiv", "FLE Standard Time" }, { "Europe/Kiev", "FLE Standard Time" }, { "Europe/Warsaw", "Central European Standard Time" },
                { "America/New_York", "Eastern Standard Time" }, { "America/Chicago", "Central Standard Time" }, { "America/Denver", "Mountain Standard Time" },
                { "America/Los_Angeles", "Pacific Standard Time" }, { "Asia/Tokyo", "Tokyo Standard Time" }, { "UTC", "UTC" }, { "Etc/UTC", "UTC" },
            };
            string id;
            if (map.TryGetValue(iana, out id)) return TimeZoneInfo.FindSystemTimeZoneById(id);
            return TimeZoneInfo.FindSystemTimeZoneById(iana);   // Windows ids work too
        }

        static string Unescape(string s) { return s.Replace("\\n", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\"); }
    }

    static class Words
    {
        public static readonly string[][] List =
        {
            new[] { "serendipity", "finding something good without looking for it" }, new[] { "ephemeral", "lasting a very short time" },
            new[] { "meticulous", "very careful and precise" }, new[] { "ubiquitous", "found everywhere" },
            new[] { "resilient", "able to recover quickly from difficulties" }, new[] { "candid", "honest and direct" },
            new[] { "pragmatic", "dealing with things in a practical way" }, new[] { "eloquent", "fluent and persuasive in speaking or writing" },
            new[] { "tenacious", "holding firmly to a goal; persistent" }, new[] { "lucid", "clear and easy to understand" },
            new[] { "benevolent", "kind and well-meaning" }, new[] { "diligent", "showing care and effort in work" },
            new[] { "audacious", "bold and willing to take risks" }, new[] { "nuance", "a subtle difference in meaning" },
            new[] { "paradigm", "a typical model or pattern" }, new[] { "empathy", "the ability to share someone else's feelings" },
            new[] { "frugal", "careful with money or food" }, new[] { "gregarious", "fond of company; sociable" },
            new[] { "inevitable", "certain to happen" }, new[] { "juxtapose", "to place side by side for contrast" },
            new[] { "kinetic", "relating to motion" }, new[] { "laconic", "using very few words" },
            new[] { "mitigate", "to make less severe" }, new[] { "nostalgia", "a sentimental longing for the past" },
            new[] { "obsolete", "no longer in use" }, new[] { "plausible", "seeming reasonable or probable" },
            new[] { "quintessential", "the most perfect example of something" }, new[] { "reciprocate", "to respond in kind" },
            new[] { "scrutinize", "to examine closely" }, new[] { "trivial", "of little importance" },
            new[] { "verbose", "using more words than needed" }, new[] { "whimsical", "playfully unusual" },
            new[] { "zealous", "full of energy and enthusiasm" }, new[] { "ambiguous", "open to more than one meaning" },
            new[] { "brevity", "the use of few words" }, new[] { "cogent", "clear, logical and convincing" },
            new[] { "dexterity", "skill in using the hands or mind" }, new[] { "enigma", "a person or thing that is hard to understand" },
            new[] { "fortitude", "courage in pain or adversity" }, new[] { "hypothesis", "an idea to be tested" },
            new[] { "impeccable", "without faults or mistakes" }, new[] { "jubilant", "feeling great happiness" },
            new[] { "lethargic", "sluggish and without energy" }, new[] { "mundane", "ordinary and dull" },
            new[] { "novice", "a beginner" }, new[] { "ominous", "giving the feeling that something bad will happen" },
            new[] { "perseverance", "continued effort despite difficulty" }, new[] { "rhetoric", "the art of persuasive speaking" },
            new[] { "succinct", "briefly and clearly expressed" }, new[] { "tangible", "able to be touched; real" },
            new[] { "unprecedented", "never done or known before" }, new[] { "vivid", "producing strong, clear images" },
            new[] { "wistful", "with a feeling of vague longing" }, new[] { "adept", "very skilled" },
            new[] { "catalyst", "something that causes change" }, new[] { "diverge", "to separate and go in different directions" },
            new[] { "facilitate", "to make easier" }, new[] { "innate", "present from birth; natural" },
            new[] { "meander", "to wander slowly with no fixed direction" }, new[] { "pristine", "in its original, perfect condition" },
        };

        public static string[] Today { get { return List[(DateTime.Today.DayOfYear + DateTime.Today.Year * 7) % List.Length]; } }
    }

    // Currency rates (base -> symbol), cached for an hour. frankfurter.app first, open.er-api.com for the rest.
    static class Rates
    {
        static readonly Dictionary<string, Dictionary<string, double>> cache = new Dictionary<string, Dictionary<string, double>>();
        static readonly Dictionary<string, DateTime> fetched = new Dictionary<string, DateTime>();

        public static Dictionary<string, double> For(string baseCur)
        {
            if (Svc.Demo) return new Dictionary<string, double> { { "USD", 1.0832 }, { "GBP", 0.8427 }, { "UAH", 45.21 }, { "JPY", 162.4 }, { "EUR", 1 } };
            lock (cache) { Dictionary<string, double> d; return cache.TryGetValue(baseCur.ToUpperInvariant(), out d) ? d : null; }
        }

        public static bool Stale(string baseCur)
        {
            lock (cache) { DateTime t; return !fetched.TryGetValue(baseCur.ToUpperInvariant(), out t) || (DateTime.UtcNow - t).TotalMinutes > 60; }
        }

        public static void Fetch(string baseCur)
        {
            baseCur = baseCur.ToUpperInvariant();
            var rates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var root = Json.Parse(Net.Get("https://api.frankfurter.app/latest?from=" + baseCur)) as Dictionary<string, object>;
                var r = root == null ? null : root["rates"] as Dictionary<string, object>;
                if (r != null) foreach (var kv in r) rates[kv.Key] = Json.D(kv.Value);
            }
            catch { }
            try
            {
                var root = Json.Parse(Net.Get("https://open.er-api.com/v6/latest/" + baseCur)) as Dictionary<string, object>;
                var r = root == null ? null : root["rates"] as Dictionary<string, object>;
                if (r != null) foreach (var kv in r) if (!rates.ContainsKey(kv.Key)) rates[kv.Key] = Json.D(kv.Value);
            }
            catch { }
            if (rates.Count == 0) throw new Exception("no rates");
            rates[baseCur] = 1;
            lock (cache) { cache[baseCur] = rates; fetched[baseCur] = DateTime.UtcNow; }
        }
    }

    // ------------------------------------------------------------------
    // System
    // ------------------------------------------------------------------
    static class ProcTop
    {
        public static volatile string CpuName = "", RamName = "";
        public static volatile float CpuPct; public static double RamMb;
        static Dictionary<long, long> last = new Dictionary<long, long>();
        static DateTime lastAt;

        [DllImport("ntdll.dll")] static extern int NtQuerySystemInformation(int cls, IntPtr buf, int len, out int ret);

        // One system call for every process (no handle per process): CPU time and working set.
        public static void Sample()
        {
            if (Svc.Demo) return;
            if (IntPtr.Size != 8) { SampleSlow(); return; }
            int len = 512 * 1024, ret;
            IntPtr buf = Marshal.AllocHGlobal(len);
            try
            {
                int st;
                while ((st = NtQuerySystemInformation(5, buf, len, out ret)) == unchecked((int)0xC0000004))
                {
                    Marshal.FreeHGlobal(buf); len = Math.Max(len * 2, ret + 65536); buf = Marshal.AllocHGlobal(len);
                }
                if (st != 0) return;
                DateTime now = DateTime.UtcNow;
                double wall = (now - lastAt).TotalMilliseconds * 10000.0 * Environment.ProcessorCount;
                var cur = new Dictionary<long, long>();
                string topCpu = "", topRam = ""; double bestCpu = 0; long bestRam = 0;
                var byName = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                var ramByName = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                int off = 0;
                while (true)
                {
                    IntPtr p = new IntPtr(buf.ToInt64() + off);
                    int next = Marshal.ReadInt32(p, 0);
                    long user = Marshal.ReadInt64(p, 40), kernel = Marshal.ReadInt64(p, 48);
                    ushort nameLen = (ushort)Marshal.ReadInt16(p, 56);
                    IntPtr namePtr = Marshal.ReadIntPtr(p, 64);
                    long pid = Marshal.ReadInt64(p, 80);
                    long ws = Marshal.ReadInt64(p, 144);
                    if (pid != 0 && namePtr != IntPtr.Zero)
                    {
                        string name = Marshal.PtrToStringUni(namePtr, nameLen / 2);
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
                        long t = user + kernel, prev;
                        cur[pid] = t;
                        if (last.TryGetValue(pid, out prev) && wall > 0)
                        {
                            double d; byName.TryGetValue(name, out d);
                            byName[name] = d + (t - prev) / wall * 100;
                        }
                        long r; ramByName.TryGetValue(name, out r); ramByName[name] = r + ws;
                    }
                    if (next == 0) break;
                    off += next;
                }
                foreach (var kv in byName) if (kv.Value > bestCpu && !kv.Key.Equals("Idle", StringComparison.OrdinalIgnoreCase)) { bestCpu = kv.Value; topCpu = kv.Key; }
                foreach (var kv in ramByName) if (kv.Value > bestRam && kv.Key != "Memory Compression") { bestRam = kv.Value; topRam = kv.Key; }
                last = cur; lastAt = now;
                CpuName = topCpu; CpuPct = (float)bestCpu; RamName = topRam; RamMb = bestRam / 1048576.0;
                BarForm.Poke();
            }
            catch (Exception ex) { Bg.LogOnce("proctop", ex); }
            finally { Marshal.FreeHGlobal(buf); }
        }

        static void SampleSlow()
        {
            Process best = null; long bestWs = 0;
            foreach (Process p in Process.GetProcesses()) { try { if (p.WorkingSet64 > bestWs) { bestWs = p.WorkingSet64; best = p; } } catch { } }
            if (best != null) { RamName = best.ProcessName; RamMb = bestWs / 1048576.0; }
        }
    }

    static class Uptime
    {
        [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
        public static TimeSpan Now { get { return Svc.Demo ? new TimeSpan(2, 5, 17, 0) : TimeSpan.FromMilliseconds(GetTickCount64()); } }

        public static string Format(TimeSpan t)
        {
            if (t.TotalDays >= 1) return (int)t.TotalDays + "d " + t.Hours + "h";
            if (t.TotalHours >= 1) return (int)t.TotalHours + "h " + t.Minutes + "m";
            return t.Minutes + "m";
        }
    }

    // Disk read / write bytes per second, from PDH (on the Bg thread).
    static class DiskIo
    {
        public static double Read, Write;
        public static readonly History ReadH = new History(40), WriteH = new History(40);
        static IntPtr query, cRead, cWrite;

        [StructLayout(LayoutKind.Sequential)] struct PDH_FMT_COUNTERVALUE { public int CStatus; public int pad; public double Value; }
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhGetFormattedCounterValue(IntPtr counter, uint format, IntPtr type, out PDH_FMT_COUNTERVALUE value);

        public static void Sample()
        {
            if (Svc.Demo) return;
            try
            {
                if (query == IntPtr.Zero)
                {
                    if (Native.PdhOpenQuery(null, IntPtr.Zero, out query) != 0) { query = IntPtr.Zero; return; }
                    Native.PdhAddEnglishCounter(query, @"\PhysicalDisk(_Total)\Disk Read Bytes/sec", IntPtr.Zero, out cRead);
                    Native.PdhAddEnglishCounter(query, @"\PhysicalDisk(_Total)\Disk Write Bytes/sec", IntPtr.Zero, out cWrite);
                    Native.PdhCollectQueryData(query);
                    return;
                }
                Native.PdhCollectQueryData(query);
                PDH_FMT_COUNTERVALUE v;
                if (PdhGetFormattedCounterValue(cRead, 0x200, IntPtr.Zero, out v) == 0) Read = v.Value;
                if (PdhGetFormattedCounterValue(cWrite, 0x200, IntPtr.Zero, out v) == 0) Write = v.Value;
                ReadH.Push((float)Read); WriteH.Push((float)Write);
                BarForm.Poke();
            }
            catch (Exception ex) { Bg.LogOnce("diskio", ex); }
        }

        public static void DemoFill()
        {
            Read = 3.4 * 1048576; Write = 860 * 1024;
            var rnd = new Random(9);
            for (int i = 0; i < 40; i++) { ReadH.Push((float)(rnd.Next(4) * 1048576 * Math.Abs(Math.Sin(i * 0.4)))); WriteH.Push((float)(rnd.Next(900) * 1024)); }
        }
    }

    // Ping, public IP and VPN state.
    static class NetInfo
    {
        public static volatile int PingMs = -1;
        public static volatile string PublicIp = "", VpnName = "";
        public static volatile bool Online = true;
        public static string PingHost = "1.1.1.1";
        static DateTime ipAt = DateTime.MinValue;

        public static void Poll()
        {
            if (Svc.Demo) { PingMs = 18; PublicIp = "203.0.113.24"; VpnName = ""; return; }
            try
            {
                using (var p = new Ping())
                {
                    PingReply r = p.Send(PingHost, 2000);
                    PingMs = r.Status == IPStatus.Success ? (int)r.RoundtripTime : -1;
                    Online = r.Status == IPStatus.Success;
                }
            }
            catch { PingMs = -1; Online = false; }
            try
            {
                string vpn = "";
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    string d = (ni.Description + " " + ni.Name).ToLowerInvariant();
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Ppp || d.Contains("vpn") || d.Contains("wireguard") || d.Contains("openvpn") ||
                        d.Contains("tap-windows") || d.Contains("tailscale") || d.Contains("zerotier") || d.Contains("nordlynx") || d.Contains("proton"))
                    { vpn = ni.Name; break; }
                }
                bool changed = vpn != VpnName;
                VpnName = vpn;
                if (changed) ipAt = DateTime.MinValue;   // a VPN changes the public IP
            }
            catch { }
            if (Online && (DateTime.UtcNow - ipAt).TotalMinutes > 30)
            {
                try { PublicIp = Net.Get("https://api.ipify.org", 6000).Trim(); ipAt = DateTime.UtcNow; } catch { }
            }
            BarForm.Poke();
        }
    }

    // Wi-Fi name and signal quality, from the WLAN API.
    static class Wlan
    {
        public static volatile string Ssid = "";
        public static volatile int Quality = -1;   // 0..100, -1 = no Wi-Fi
        static IntPtr handle;

        [DllImport("wlanapi.dll")] static extern int WlanOpenHandle(int ver, IntPtr res, out int neg, out IntPtr h);
        [DllImport("wlanapi.dll")] static extern int WlanEnumInterfaces(IntPtr h, IntPtr res, out IntPtr list);
        [DllImport("wlanapi.dll")] static extern int WlanQueryInterface(IntPtr h, ref Guid iface, int opcode, IntPtr res, out int size, out IntPtr data, IntPtr type);
        [DllImport("wlanapi.dll")] static extern void WlanFreeMemory(IntPtr p);

        public static void Poll()
        {
            if (Svc.Demo) { Ssid = "Home Wi-Fi"; Quality = 78; return; }
            try
            {
                if (handle == IntPtr.Zero) { int neg; if (WlanOpenHandle(2, IntPtr.Zero, out neg, out handle) != 0) { handle = IntPtr.Zero; Quality = -1; return; } }
                IntPtr list;
                if (WlanEnumInterfaces(handle, IntPtr.Zero, out list) != 0) return;
                try
                {
                    int n = Marshal.ReadInt32(list, 0);
                    string ssid = ""; int q = -1;
                    for (int i = 0; i < n && q < 0; i++)
                    {
                        IntPtr item = new IntPtr(list.ToInt64() + 8 + i * 532);
                        Guid g = (Guid)Marshal.PtrToStructure(item, typeof(Guid));
                        int state = Marshal.ReadInt32(item, 16 + 512);
                        if (state != 1) continue;   // connected
                        int size; IntPtr data;
                        if (WlanQueryInterface(handle, ref g, 7, IntPtr.Zero, out size, out data, IntPtr.Zero) != 0) continue;
                        try
                        {
                            int len = Marshal.ReadInt32(data, 520);
                            var b = new byte[Math.Min(32, Math.Max(0, len))];
                            Marshal.Copy(new IntPtr(data.ToInt64() + 524), b, 0, b.Length);
                            ssid = Encoding.UTF8.GetString(b);
                            q = Marshal.ReadInt32(data, 576);
                        }
                        finally { WlanFreeMemory(data); }
                    }
                    Ssid = ssid; Quality = q;
                }
                finally { WlanFreeMemory(list); }
                BarForm.Poke();
            }
            catch (Exception ex) { Bg.LogOnce("wlan", ex); Quality = -1; }
        }
    }

    // Battery health from WMI (root\WMI): design capacity, full charge capacity, cycles.
    static class BatteryHealth
    {
        public static volatile int Design = -1, Full = -1, Cycles = -1;

        public static void Poll()
        {
            if (Svc.Demo) { Design = 52000; Full = 47320; Cycles = 214; return; }
            try
            {
                Design = ReadInt("SELECT DesignedCapacity FROM BatteryStaticData", "DesignedCapacity");
                Full = ReadInt("SELECT FullChargedCapacity FROM BatteryFullChargedCapacity", "FullChargedCapacity");
                Cycles = ReadInt("SELECT CycleCount FROM BatteryCycleCount", "CycleCount");
                BarForm.Poke();
            }
            catch (Exception ex) { Bg.LogOnce("batteryhealth", ex); }
        }

        static int ReadInt(string q, string prop)
        {
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\WMI", q))
                    foreach (ManagementObject o in s.Get()) { object v = o[prop]; if (v != null) return Convert.ToInt32(v); }
            }
            catch { }
            return -1;
        }

        public static int HealthPct { get { return Design > 0 && Full > 0 ? (int)Math.Round(Math.Min(100.0, 100.0 * Full / Design)) : -1; } }
    }

    // Power plans (and, on Windows 11 laptops, the power mode slider).
    static class PowerPlans
    {
        [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr guid);
        [DllImport("powrprof.dll")] static extern uint PowerSetActiveScheme(IntPtr root, ref Guid guid);
        [DllImport("powrprof.dll")] static extern uint PowerEnumerate(IntPtr root, IntPtr sub, IntPtr setting, int access, uint index, IntPtr buf, ref uint size);
        [DllImport("powrprof.dll")] static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr sub, IntPtr setting, IntPtr buf, ref uint size);
        [DllImport("powrprof.dll", EntryPoint = "PowerGetEffectiveOverlayScheme")] static extern uint GetOverlay(out Guid g);
        [DllImport("powrprof.dll", EntryPoint = "PowerSetActiveOverlayScheme")] static extern uint SetOverlay(ref Guid g);
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);

        public static readonly KeyValuePair<Guid, string>[] Modes =
        {
            new KeyValuePair<Guid, string>(new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a"), "Best power efficiency"),
            new KeyValuePair<Guid, string>(Guid.Empty, "Balanced"),
            new KeyValuePair<Guid, string>(new Guid("ded574b5-45a0-4f42-8737-46345c09c238"), "Best performance"),
        };

        public static List<KeyValuePair<Guid, string>> Plans()
        {
            var list = new List<KeyValuePair<Guid, string>>();
            if (Svc.Demo) { list.Add(new KeyValuePair<Guid, string>(Guid.NewGuid(), "Balanced")); return list; }
            for (uint i = 0; ; i++)
            {
                uint size = 16;
                IntPtr buf = Marshal.AllocHGlobal(16);
                try
                {
                    if (PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16, i, buf, ref size) != 0) break;
                    Guid g = (Guid)Marshal.PtrToStructure(buf, typeof(Guid));
                    list.Add(new KeyValuePair<Guid, string>(g, NameOf(g)));
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            return list;
        }

        static string NameOf(Guid g)
        {
            uint size = 0;
            PowerReadFriendlyName(IntPtr.Zero, ref g, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size);
            if (size == 0) return g.ToString();
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try { return PowerReadFriendlyName(IntPtr.Zero, ref g, IntPtr.Zero, IntPtr.Zero, buf, ref size) == 0 ? Marshal.PtrToStringUni(buf) : g.ToString(); }
            finally { Marshal.FreeHGlobal(buf); }
        }

        public static Guid Active
        {
            get
            {
                if (Svc.Demo) return Guid.Empty;
                IntPtr p;
                if (PowerGetActiveScheme(IntPtr.Zero, out p) != 0) return Guid.Empty;
                try { return (Guid)Marshal.PtrToStructure(p, typeof(Guid)); } finally { LocalFree(p); }
            }
        }

        public static string ActiveName { get { if (Svc.Demo) return "Balanced"; try { Guid g = Active; return NameOf(g); } catch { return "Power"; } } }
        public static void Set(Guid g) { PowerSetActiveScheme(IntPtr.Zero, ref g); BarForm.Poke(); }

        public static int Mode
        {
            get
            {
                if (Svc.Demo) return 1;
                try { Guid g; if (GetOverlay(out g) != 0) return -1; for (int i = 0; i < Modes.Length; i++) if (Modes[i].Key == g) return i; } catch { }
                return -1;
            }
        }

        public static bool SetMode(int i)
        {
            try { Guid g = Modes[i].Key; return SetOverlay(ref g) == 0; } catch { return false; }
        }
    }

    // ------------------------------------------------------------------
    // Audio: peak meter, per-app volume, microphone, output device
    // ------------------------------------------------------------------
    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr guid, int flags, out IntPtr ctl);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr guid, int flags, out IntPtr vol);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator e);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int n);
        [PreserveSig] int GetSession(int i, out IAudioSessionControl2 s);
    }

    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName(string name, IntPtr ctx);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath(string path, IntPtr ctx);
        [PreserveSig] int GetGroupingParam(out Guid g);
        [PreserveSig] int SetGroupingParam(ref Guid g, IntPtr ctx);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr n);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr n);
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference(bool optOut);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute(bool mute, ref Guid ctx);
        [PreserveSig] int GetMute(out bool mute);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int n);
        [PreserveSig] int Item(int i, out IMMDevice dev);
    }

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(string id, IntPtr fmt);
        [PreserveSig] int GetDeviceFormat(string id, int def, IntPtr fmt);
        [PreserveSig] int ResetDeviceFormat(string id);
        [PreserveSig] int SetDeviceFormat(string id, IntPtr a, IntPtr b);
        [PreserveSig] int GetProcessingPeriod(string id, int def, IntPtr a, IntPtr b);
        [PreserveSig] int SetProcessingPeriod(string id, IntPtr a);
        [PreserveSig] int GetShareMode(string id, IntPtr mode);
        [PreserveSig] int SetShareMode(string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue(string id, IntPtr key, IntPtr pv);
        [PreserveSig] int SetPropertyValue(string id, IntPtr key, IntPtr pv);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility(string id, int visible);
    }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    class PolicyConfigCom { }

    class AudioSession { public string Name, Exe; public uint Pid; public float Volume; public bool Muted; public ISimpleAudioVolume Ctl; }
    class AudioDevice { public string Id, Name; public bool Default; }

    static class AudioX
    {
        static Guid ctx = Guid.NewGuid();

        static IMMDevice DefaultDevice(int flow)
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            IMMDevice dev;
            return en.GetDefaultAudioEndpoint(flow, 1, out dev) == 0 ? dev : null;
        }

        static T Activate<T>(IMMDevice dev) where T : class
        {
            if (dev == null) return null;
            Guid iid = typeof(T).GUID;
            object o;
            return dev.Activate(ref iid, 23, IntPtr.Zero, out o) == 0 ? o as T : null;
        }

        // ---- peak meter (cheap: read on the UI thread at paint time) ----
        static IAudioMeterInformation meter;
        static DateTime meterAt;

        public static float Peak
        {
            get
            {
                if (Svc.Demo) return 0.62f;
                try
                {
                    if (meter == null || (DateTime.UtcNow - meterAt).TotalSeconds > 10)
                    {
                        if (meter != null) { try { Marshal.ReleaseComObject(meter); } catch { } }
                        meter = Activate<IAudioMeterInformation>(DefaultDevice(0));
                        meterAt = DateTime.UtcNow;
                    }
                    float v;
                    return meter != null && meter.GetPeakValue(out v) == 0 ? v : 0;
                }
                catch { meter = null; return 0; }
            }
        }

        // ---- per-app volume ----
        public static List<AudioSession> Sessions()
        {
            var list = new List<AudioSession>();
            if (Svc.Demo)
            {
                list.Add(new AudioSession { Name = "Spotify", Volume = 0.8f });
                list.Add(new AudioSession { Name = "Google Chrome", Volume = 0.55f });
                list.Add(new AudioSession { Name = "Discord", Volume = 0.9f, Muted = true });
                return list;
            }
            try
            {
                var mgr = Activate<IAudioSessionManager2>(DefaultDevice(0));
                if (mgr == null) return list;
                IAudioSessionEnumerator e;
                if (mgr.GetSessionEnumerator(out e) != 0) return list;
                int n; e.GetCount(out n);
                for (int i = 0; i < n; i++)
                {
                    IAudioSessionControl2 s;
                    if (e.GetSession(i, out s) != 0 || s == null) continue;
                    int state; s.GetState(out state);
                    if (state == 2) continue;   // expired
                    uint pid; s.GetProcessId(out pid);
                    if (s.IsSystemSoundsSession() == 0) continue;
                    var vol = s as ISimpleAudioVolume;
                    if (vol == null) continue;
                    var a = new AudioSession { Pid = pid, Ctl = vol };
                    vol.GetMasterVolume(out a.Volume); vol.GetMute(out a.Muted);
                    string exe = Win.ProcessPath(pid);
                    a.Exe = exe;
                    string dn; s.GetDisplayName(out dn);
                    a.Name = exe != null ? AppNames.Of(exe) : (string.IsNullOrEmpty(dn) || dn.StartsWith("@") ? "App " + pid : dn);
                    list.Add(a);
                }
            }
            catch (Exception ex) { Bg.LogOnce("sessions", ex); }
            return list.GroupBy(s => s.Name).Select(g => g.First()).ToList();
        }

        public static void SetVolume(AudioSession s, float v) { if (s.Ctl != null) { s.Ctl.SetMasterVolume(Math.Max(0, Math.Min(1, v)), ref ctx); s.Volume = v; } }
        public static void SetMute(AudioSession s, bool m) { if (s.Ctl != null) { s.Ctl.SetMute(m, ref ctx); s.Muted = m; } }

        // ---- microphone ----
        public static bool? MicMuted
        {
            get
            {
                if (Svc.Demo) return false;
                try
                {
                    var v = Activate<IAudioEndpointVolume>(DefaultDevice(1));
                    if (v == null) return null;
                    bool m; v.GetMute(out m);
                    Marshal.ReleaseComObject(v);
                    return m;
                }
                catch { return null; }
            }
            set
            {
                try
                {
                    var v = Activate<IAudioEndpointVolume>(DefaultDevice(1));
                    if (v == null) return;
                    v.SetMute(value == true, ref ctx);
                    Marshal.ReleaseComObject(v);
                }
                catch { }
            }
        }

        // ---- output device ----
        public static List<AudioDevice> Outputs()
        {
            var list = new List<AudioDevice>();
            if (Svc.Demo) { list.Add(new AudioDevice { Id = "1", Name = "Speakers", Default = true }); list.Add(new AudioDevice { Id = "2", Name = "Headphones" }); return list; }
            try
            {
                var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                IMMDevice def; string defId = null;
                if (en.GetDefaultAudioEndpoint(0, 1, out def) == 0 && def != null) def.GetId(out defId);
                IntPtr colPtr;
                if (en.EnumAudioEndpoints(0, 1, out colPtr) != 0) return list;
                var col = (IMMDeviceCollection)Marshal.GetObjectForIUnknown(colPtr);
                Marshal.Release(colPtr);
                int n; col.GetCount(out n);
                for (int i = 0; i < n; i++)
                {
                    IMMDevice d;
                    if (col.Item(i, out d) != 0) continue;
                    string id; d.GetId(out id);
                    list.Add(new AudioDevice { Id = id, Name = DeviceName(d) ?? "Audio device", Default = id == defId });
                }
            }
            catch (Exception ex) { Bg.LogOnce("outputs", ex); }
            return list;
        }

        static string DeviceName(IMMDevice dev)
        {
            IPropertyStore ps;
            if (dev.OpenPropertyStore(0, out ps) != 0 || ps == null) return null;
            try
            {
                var key = new PROPERTYKEY(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);   // friendly name
                PROPVARIANT pv;
                if (ps.GetValue(ref key, out pv) != 0) return null;
                try { return pv.vt == 31 ? Marshal.PtrToStringUni(pv.p) : null; }
                finally { Native.PropVariantClear(ref pv); }
            }
            finally { Marshal.ReleaseComObject(ps); }
        }

        public static bool SetDefaultOutput(string id)
        {
            try
            {
                var pc = (IPolicyConfig)new PolicyConfigCom();
                for (int role = 0; role < 3; role++) pc.SetDefaultEndpoint(id, role);
                return true;
            }
            catch (Exception ex) { Bg.LogOnce("setoutput", ex); return false; }
        }
    }

    // Is an app using the microphone or the camera right now? (the privacy "in use" registry state)
    static class Privacy
    {
        public static volatile bool MicInUse, CamInUse;

        public static void Poll()
        {
            if (Svc.Demo) { MicInUse = true; CamInUse = false; return; }
            MicInUse = InUse("microphone");
            CamInUse = InUse("webcam");
            BarForm.Poke();
        }

        static bool InUse(string cap)
        {
            try
            {
                using (RegistryKey root = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\" + cap))
                {
                    if (root == null) return false;
                    if (Check(root)) return true;
                    using (RegistryKey np = root.OpenSubKey("NonPackaged")) if (np != null && Check(np)) return true;
                }
            }
            catch { }
            return false;
        }

        static bool Check(RegistryKey k)
        {
            foreach (string name in k.GetSubKeyNames())
            {
                if (name == "NonPackaged") continue;
                using (RegistryKey app = k.OpenSubKey(name))
                {
                    if (app == null) continue;
                    object start = app.GetValue("LastUsedTimeStart"), stop = app.GetValue("LastUsedTimeStop");
                    if (start is long && stop is long && (long)stop == 0 && (long)start != 0) return true;
                }
            }
            return false;
        }
    }

    // Removable drives, and the Shell's own "Eject".
    static class Usb
    {
        public static volatile string[] Drives = new string[0];   // "E:\ (USB DISK, 14 GB)"

        public static void Poll()
        {
            if (Svc.Demo) { Drives = new[] { "E:\\|SANDISK|28.6 GB" }; return; }
            try
            {
                Drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Removable && d.IsReady)
                    .Select(d => d.Name + "|" + (d.VolumeLabel.Length > 0 ? d.VolumeLabel : "USB drive") + "|" + (d.TotalSize / 1073741824.0).ToString("0.#") + " GB").ToArray();
                BarForm.Poke();
            }
            catch { }
        }

        public static void Eject(string root)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("Shell.Application");
                object shell = Activator.CreateInstance(t);
                object ns = t.InvokeMember("NameSpace", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { 17 });
                object item = ns.GetType().InvokeMember("ParseName", System.Reflection.BindingFlags.InvokeMethod, null, ns, new object[] { root });
                if (item != null) item.GetType().InvokeMember("InvokeVerb", System.Reflection.BindingFlags.InvokeMethod, null, item, new object[] { "Eject" });
            }
            catch (Exception ex) { Program.Log("Eject " + root + ": " + ex.Message); }
            Bg.Run("usb.after", delegate { Thread.Sleep(1500); Poll(); });
        }
    }

    // ------------------------------------------------------------------
    // Windows toggles (current user only, no admin)
    // ------------------------------------------------------------------
    static class Toggles
    {
        const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        const string NightKey = @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate";
        const string NotifKey = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessageTimeout(IntPtr h, int msg, IntPtr w, string l, int flags, int timeout, out IntPtr result);
        [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);
        [DllImport("ntdll.dll")] static extern int NtUpdateWnfStateData(ref ulong state, byte[] data, int len, IntPtr type, IntPtr scope, uint changeStamp, uint checkStamp);

        static int GetDword(string key, string name, int def)
        {
            try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(key)) { object v = k == null ? null : k.GetValue(name); return v is int ? (int)v : def; } }
            catch { return def; }
        }

        static void SetDword(string key, string name, int v)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(key)) k.SetValue(name, v, RegistryValueKind.DWord);
        }

        static void Broadcast(string what)
        {
            IntPtr r;
            SendMessageTimeout(new IntPtr(0xFFFF), Native.WM_SETTINGCHANGE, IntPtr.Zero, what, 2, 500, out r);
        }

        // ---- dark mode (apps + Windows) ----
        public static bool Dark { get { return GetDword(Personalize, "AppsUseLightTheme", 1) == 0; } }
        public static void SetDark(bool on)
        {
            SetDword(Personalize, "AppsUseLightTheme", on ? 0 : 1);
            SetDword(Personalize, "SystemUsesLightTheme", on ? 0 : 1);
            Broadcast("ImmersiveColorSet");
        }

        // ---- night light (undocumented CloudStore blob; falls back to Settings) ----
        public static bool? NightLight
        {
            get
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(NightKey))
                    {
                        var data = k == null ? null : k.GetValue("Data") as byte[];
                        if (data == null || data.Length < 19) return null;
                        return data[18] == 0x15;
                    }
                }
                catch { return null; }
            }
        }

        public static bool ToggleNightLight()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(NightKey, true))
                {
                    var data = k == null ? null : k.GetValue("Data") as byte[];
                    if (data == null || data.Length < 25 || (data[18] != 0x15 && data[18] != 0x13)) return false;
                    var list = data.ToList();
                    if (data[18] == 0x15) { list[18] = 0x13; list.RemoveRange(23, 2); }
                    else { list[18] = 0x15; list.InsertRange(23, new byte[] { 0x10, 0x00 }); }
                    for (int i = 10; i < 15; i++) { if (list[i] != 0xff) { list[i]++; break; } }
                    k.SetValue("Data", list.ToArray(), RegistryValueKind.Binary);
                    return true;
                }
            }
            catch (Exception ex) { Program.Log("Night light: " + ex.Message); return false; }
        }

        // ---- do not disturb ----
        public static bool Dnd { get { return GetDword(NotifKey, "NOC_GLOBAL_SETTING_TOASTS_ENABLED", 1) == 0; } }
        public static bool SetDnd(bool on)
        {
            try
            {
                SetDword(NotifKey, "NOC_GLOBAL_SETTING_TOASTS_ENABLED", on ? 0 : 1);
                // tell the shell (the state Focus assist / do-not-disturb listens to)
                ulong state = 0x0D83063EA3BF1C75;   // WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED
                byte[] data = BitConverter.GetBytes(on ? 2 : 0);
                NtUpdateWnfStateData(ref state, data, data.Length, IntPtr.Zero, IntPtr.Zero, 0, 0);
                Broadcast("Notifications");
                return true;
            }
            catch (Exception ex) { Program.Log("Do not disturb: " + ex.Message); return false; }
        }

        // ---- game mode ----
        public static bool GameMode { get { return GetDword(@"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1) != 0; } }
        public static void SetGameMode(bool on) { SetDword(@"Software\Microsoft\GameBar", "AutoGameModeEnabled", on ? 1 : 0); }

        // ---- desktop icons ----
        public static bool IconsHidden { get { return GetDword(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideIcons", 0) != 0; } }
        public static void ToggleDesktopIcons()
        {
            IntPtr view = FindDefView();
            if (view != IntPtr.Zero) Native.SendMessage(view, 0x111, new IntPtr(0x7402), IntPtr.Zero);   // WM_COMMAND "show desktop icons"
        }

        static IntPtr FindDefView()
        {
            IntPtr progman = Native.FindWindow("Progman", null);
            IntPtr view = Native.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (view != IntPtr.Zero) return view;
            IntPtr w = IntPtr.Zero;
            while ((w = Native.FindWindowEx(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero)
            {
                view = Native.FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (view != IntPtr.Zero) return view;
            }
            return IntPtr.Zero;
        }

        // ---- keep awake ----
        public static bool Awake;
        public static void SetAwake(bool on)
        {
            Awake = on;
            SetThreadExecutionState(on ? 0x80000003u : 0x80000000u);   // ES_CONTINUOUS | SYSTEM | DISPLAY
        }
    }

    // ------------------------------------------------------------------
    // Online info (thread pool, keyless)
    // ------------------------------------------------------------------
    class Quote { public string Symbol, Name; public double Price, Change; }

    static class Online
    {
        static readonly Dictionary<string, object> cache = new Dictionary<string, object>();
        public static void Put(string key, object v) { lock (cache) cache[key] = v; BarForm.Poke(); }
        public static T Get<T>(string key) where T : class { lock (cache) { object o; return cache.TryGetValue(key, out o) ? o as T : null; } }

        // CoinGecko: ids "bitcoin,ethereum" in a fiat currency.
        public static List<Quote> Crypto(string ids, string cur)
        {
            string json = Net.Get("https://api.coingecko.com/api/v3/simple/price?ids=" + Uri.EscapeDataString(ids) + "&vs_currencies=" + cur + "&include_24hr_change=true");
            var root = Json.Parse(json) as Dictionary<string, object>;
            var list = new List<Quote>();
            foreach (string id in ids.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0))
            {
                object o;
                if (root == null || !root.TryGetValue(id, out o)) continue;
                var d = o as Dictionary<string, object>;
                list.Add(new Quote { Symbol = CoinSymbol(id), Name = id, Price = Json.Num(d, cur), Change = Json.Num(d, cur + "_24h_change") });
            }
            return list;
        }

        public static string CoinSymbol(string id)
        {
            switch (id)
            {
                case "bitcoin": return "BTC"; case "ethereum": return "ETH"; case "solana": return "SOL"; case "dogecoin": return "DOGE";
                case "ripple": return "XRP"; case "cardano": return "ADA"; case "litecoin": return "LTC"; case "tether": return "USDT";
            }
            return id.Length <= 5 ? id.ToUpperInvariant() : id.Substring(0, 4).ToUpperInvariant();
        }

        // Yahoo Finance's public chart endpoint (keyless, unofficial): "AAPL,MSFT" -> price and change since yesterday's close.
        public static List<Quote> Stocks(string symbols)
        {
            var list = new List<Quote>();
            foreach (string s in symbols.Split(',').Select(x => x.Trim().ToUpperInvariant()).Where(x => x.Length > 0).Take(8))
            {
                var root = Json.Parse(Net.Get("https://query1.finance.yahoo.com/v8/finance/chart/" + Uri.EscapeDataString(s) + "?range=1d&interval=1d")) as Dictionary<string, object>;
                var chart = root == null ? null : root["chart"] as Dictionary<string, object>;
                var res = chart == null ? null : chart["result"] as List<object>;
                if (res == null || res.Count == 0) continue;
                var meta = ((Dictionary<string, object>)res[0])["meta"] as Dictionary<string, object>;
                double price = Json.Num(meta, "regularMarketPrice"), prev = Json.Num(meta, "chartPreviousClose");
                if (prev <= 0) prev = Json.Num(meta, "previousClose");
                list.Add(new Quote { Symbol = s, Price = price, Change = prev > 0 ? (price - prev) / prev * 100 : 0 });
            }
            return list;
        }

        // Open-Meteo air quality: European AQI and PM2.5.
        public static double[] Air(double lat, double lon)
        {
            var ci = CultureInfo.InvariantCulture;
            var root = Json.Parse(Net.Get("https://air-quality-api.open-meteo.com/v1/air-quality?latitude=" + lat.ToString("0.####", ci) + "&longitude=" + lon.ToString("0.####", ci) + "&current=european_aqi,pm2_5,pm10")) as Dictionary<string, object>;
            var cur = root["current"] as Dictionary<string, object>;
            return new[] { Json.Num(cur, "european_aqi"), Json.Num(cur, "pm2_5"), Json.Num(cur, "pm10") };
        }

        // RSS / Atom headlines.
        public static List<KeyValuePair<string, string>> Feeds(IEnumerable<string> urls)
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (string url in urls)
            {
                try
                {
                    string xml = Net.Get(url, 10000);
                    var doc = new System.Xml.XmlDocument();
                    doc.XmlResolver = null;
                    doc.LoadXml(xml);
                    var items = doc.GetElementsByTagName("item");
                    if (items.Count == 0) items = doc.GetElementsByTagName("entry");
                    int n = 0;
                    foreach (System.Xml.XmlNode it in items)
                    {
                        string title = "", link = "";
                        foreach (System.Xml.XmlNode c in it.ChildNodes)
                        {
                            if (c.LocalName == "title") title = c.InnerText.Trim();
                            else if (c.LocalName == "link") link = c.Attributes != null && c.Attributes["href"] != null ? c.Attributes["href"].Value : c.InnerText.Trim();
                        }
                        if (title.Length > 0) list.Add(new KeyValuePair<string, string>(WebUtility.HtmlDecode(title), link));
                        if (++n >= 10) break;
                    }
                }
                catch (Exception ex) { Bg.LogOnce("rss " + url, ex); }
            }
            return list;
        }

        // A public GitHub contribution calendar: date -> level 0..4.
        public static SortedDictionary<DateTime, int> GitHub(string user)
        {
            string html = Net.Get("https://github.com/users/" + Uri.EscapeDataString(user) + "/contributions", 15000);
            var map = new SortedDictionary<DateTime, int>();
            foreach (Match m in Regex.Matches(html, "<td[^>]*?data-date=\"(\\d{4}-\\d{2}-\\d{2})\"[^>]*?data-level=\"(\\d)\"|<td[^>]*?data-level=\"(\\d)\"[^>]*?data-date=\"(\\d{4}-\\d{2}-\\d{2})\""))
            {
                string d = m.Groups[1].Success && m.Groups[1].Length > 0 ? m.Groups[1].Value : m.Groups[4].Value;
                string l = m.Groups[2].Success && m.Groups[2].Length > 0 ? m.Groups[2].Value : m.Groups[3].Value;
                DateTime dt;
                if (DateTime.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) map[dt] = int.Parse(l);
            }
            return map;
        }

        // MyMemory: free, keyless, on demand only.
        public static string Translate(string text, string from, string to)
        {
            var root = Json.Parse(Net.Get("https://api.mymemory.translated.net/get?q=" + Uri.EscapeDataString(text) + "&langpair=" + from + "|" + to)) as Dictionary<string, object>;
            var d = root["responseData"] as Dictionary<string, object>;
            object t;
            string best = d != null && d.TryGetValue("translatedText", out t) ? WebUtility.HtmlDecode(t as string ?? "") : "";
            // the shared translation memory has the odd junk entry ("testvalue"); fall back to the next match
            if (best.Length == 0 || best.Equals("testvalue", StringComparison.OrdinalIgnoreCase) || best.Equals(text, StringComparison.OrdinalIgnoreCase))
            {
                object mo;
                var matches = root.TryGetValue("matches", out mo) ? mo as List<object> : null;
                if (matches != null)
                    foreach (var m in matches.OfType<Dictionary<string, object>>())
                    {
                        object tr;
                        string s = m.TryGetValue("translation", out tr) ? WebUtility.HtmlDecode(tr as string ?? "") : "";
                        if (s.Length > 0 && !s.Equals("testvalue", StringComparison.OrdinalIgnoreCase) && !s.Equals(text, StringComparison.OrdinalIgnoreCase)) return s;
                    }
            }
            return best;
        }
    }

    // ------------------------------------------------------------------
    // Dev: Claude Code usage today (local logs only), newest download
    // ------------------------------------------------------------------
    static class ClaudeUsage
    {
        public static long Input, Output, CacheRead, CacheWrite;
        public static int Sessions, Messages;
        public static volatile bool Found;
        static DateTime day = DateTime.MinValue;
        static readonly Dictionary<string, long> offsets = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> sessions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static long Total { get { return Input + Output + CacheRead + CacheWrite; } }

        public static void Poll()
        {
            if (Svc.Demo) { Input = 182000; Output = 96400; CacheRead = 2840000; CacheWrite = 312000; Sessions = 4; Messages = 211; Found = true; return; }
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
            if (!Directory.Exists(root)) { Found = false; return; }
            Found = true;
            DateTime today = DateTime.Today;
            if (day != today) { day = today; offsets.Clear(); sessions.Clear(); Input = Output = CacheRead = CacheWrite = 0; Messages = 0; }
            try
            {
                foreach (string f in Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories))
                {
                    var fi = new FileInfo(f);
                    if (fi.LastWriteTime < today) continue;
                    long from; offsets.TryGetValue(f, out from);
                    if (fi.Length <= from) continue;
                    using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        fs.Seek(from, SeekOrigin.Begin);
                        var sr = new StreamReader(fs, Encoding.UTF8);
                        long pos = from;
                        string line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            if (line.IndexOf("\"usage\"", StringComparison.Ordinal) < 0) continue;
                            Count(line, f, today);
                        }
                        pos = fs.Length;
                        offsets[f] = pos;
                    }
                }
                Sessions = sessions.Count;
                BarForm.Poke();
            }
            catch (Exception ex) { Bg.LogOnce("claudeusage", ex); }
        }

        static void Count(string line, string file, DateTime today)
        {
            Match ts = Regex.Match(line, "\"timestamp\"\\s*:\\s*\"([^\"]+)\"");
            DateTime t;
            if (!ts.Success || !DateTime.TryParse(ts.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t)) return;
            if (t.ToLocalTime().Date != today) return;
            long a = Num(line, "input_tokens"), b = Num(line, "output_tokens"), c = Num(line, "cache_read_input_tokens"), d = Num(line, "cache_creation_input_tokens");
            if (a + b + c + d == 0) return;
            Input += a; Output += b; CacheRead += c; CacheWrite += d; Messages++;
            sessions.Add(Path.GetFileNameWithoutExtension(file));
        }

        static long Num(string s, string key)
        {
            Match m = Regex.Match(s, "\"" + key + "\"\\s*:\\s*(\\d+)");
            long v;
            return m.Success && long.TryParse(m.Groups[1].Value, out v) ? v : 0;
        }

        public static string Short(long n)
        {
            if (n >= 1000000) return (n / 1000000.0).ToString(n >= 10000000 ? "0" : "0.0", CultureInfo.InvariantCulture) + "M";
            if (n >= 1000) return (n / 1000.0).ToString("0", CultureInfo.InvariantCulture) + "k";
            return n.ToString();
        }
    }

    static class Downloads
    {
        public static volatile string Newest;
        static FileSystemWatcher watcher;

        public static string Folder
        {
            get
            {
                try
                {
                    Guid g = new Guid("374DE290-123F-4565-9164-39C4925E467B");
                    IntPtr p;
                    if (Native.SHGetKnownFolderPath(ref g, 0, IntPtr.Zero, out p) == 0) { string s = Marshal.PtrToStringUni(p); Native.CoTaskMemFree(p); return s; }
                }
                catch { }
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }
        }

        public static void Poll()
        {
            if (Svc.Demo) { Newest = @"C:\Users\Demo\Downloads\lecture-notes-week7.pdf"; return; }
            try
            {
                string dir = Folder;
                if (!Directory.Exists(dir)) return;
                var newest = new DirectoryInfo(dir).EnumerateFiles().Where(f => !f.Name.EndsWith(".crdownload") && !f.Name.EndsWith(".part") && !f.Name.EndsWith(".tmp") && (f.Attributes & FileAttributes.Hidden) == 0)
                    .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
                Newest = newest == null ? null : newest.FullName;
                if (watcher == null)
                {
                    watcher = new FileSystemWatcher(dir);
                    watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite;
                    FileSystemEventHandler h = delegate { Bg.Kick("downloads"); };
                    watcher.Created += h; watcher.Renamed += delegate { Bg.Kick("downloads"); }; watcher.Deleted += h;
                    watcher.EnableRaisingEvents = true;
                }
                BarForm.Poke();
            }
            catch (Exception ex) { Bg.LogOnce("downloads", ex); }
        }
    }

    // Custom widgets: run a command, keep its first output line.
    static class CustomRunner
    {
        public static string Run(string command, bool powershell, int timeoutMs)
        {
            var psi = powershell ? new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"" + command.Replace("\"", "\\\"") + "\"")
                                 : new ProcessStartInfo("cmd.exe", "/c " + command);
            psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            using (Process p = Process.Start(psi))
            {
                var output = new StringBuilder();
                p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                p.BeginOutputReadLine();
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return "(timed out)"; }
                p.WaitForExit();
                string text;
                lock (output) text = output.ToString();
                string first = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
                return first ?? "";
            }
        }
    }
}
