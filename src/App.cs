// StayBar - an always-on replacement for the Windows taskbar.
// Entry point, watchdog, settings file, taskbar takeover and the background job runner.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace StayBar
{
    // ------------------------------------------------------------------
    // Entry point. Modes:
    //   (no args)   watchdog: starts the bar and restarts it if it dies
    //   --bar       the bar itself
    //   --restore   kill StayBar and bring the Windows taskbar back
    //   --selftest  list apps + render preview PNGs, touches nothing
    // ------------------------------------------------------------------
    static class Program
    {
        public const string Version = "3.0";
        public static string DataDir;
        // 0 = quit, 2 = restart, 3 = crash, 4 = already running. Starts as 3: if the bar's window ever
        // goes away without Quit (or Windows signing out), the watchdog treats it as a crash and restarts it.
        public static int ExitCode = 3;

        public static string ExePath { get { return Assembly.GetExecutingAssembly().Location; } }
        public static string ExeDir { get { return Path.GetDirectoryName(ExePath); } }

        [STAThread]
        static int Main(string[] args)
        {
            DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StayBar");
            Directory.CreateDirectory(DataDir);
            string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            try
            {
                if (mode == "--bar") return RunBar();
                if (mode == "--restore") return RunRestore();
                if (mode == "--selftest") return SelfTest.Run(args.Length > 1 ? args[1] : null);
                if (mode == "--quit") return SendToBar("Quit");
                if (mode == "--settings") return SendToBar("Settings");
                if (mode == "--reload") return SendToBar("Reload");
                if (mode == "--launcher") return SendToBar("Launcher");
                if (mode == "--gallery") return SendToBar("Gallery");
                return RunWatchdog();
            }
            catch (Exception ex)
            {
                Log(ex.ToString());
                return 3;
            }
        }

        static readonly object logGate = new object();
        public static void Log(string text)
        {
            lock (logGate)
            {
                try
                {
                    string path = Path.Combine(DataDir, "error.log");
                    if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Delete(path);
                    File.AppendAllText(path, DateTime.Now.ToString("s") + "  " + text + Environment.NewLine);
                }
                catch { }
            }
        }

        // Talk to a running bar: StayBar.exe --settings / --quit
        static int SendToBar(string what)
        {
            IntPtr h = Native.FindWindow(null, "StayBar");
            if (h == IntPtr.Zero) return 1;
            Native.PostMessage(h, (int)Native.RegisterWindowMessage("StayBar." + what), IntPtr.Zero, IntPtr.Zero);
            return 0;
        }

        static int RunWatchdog()
        {
            bool created;
            using (var mutex = new Mutex(true, "Local\\StayBar.Watchdog", out created))
            {
                if (!created) return 0; // already running
                var crashes = new Queue<DateTime>();
                while (true)
                {
                    var psi = new ProcessStartInfo(ExePath, "--bar");
                    psi.UseShellExecute = false;
                    Process p = Process.Start(psi);
                    p.WaitForExit();
                    int code = p.ExitCode;
                    if (code == 0 || code == 4) break;   // quit on purpose / another bar running
                    if (code == 2) continue;             // restart requested
                    crashes.Enqueue(DateTime.Now);
                    while (crashes.Count > 0 && (DateTime.Now - crashes.Peek()).TotalSeconds > 60) crashes.Dequeue();
                    if (crashes.Count >= 5)
                    {
                        Taskbar.Restore();
                        MessageBox.Show("StayBar crashed 5 times in a minute, so it stopped and gave you the Windows taskbar back.\n\nDetails are in:\n" +
                            Path.Combine(DataDir, "error.log"), "StayBar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        break;
                    }
                    Thread.Sleep(800);
                }
                GC.KeepAlive(mutex);
            }
            return 0;
        }

        static int RunBar()
        {
            Native.MakeDpiAware();
            bool created;
            using (var mutex = new Mutex(true, "Local\\StayBar.Bar", out created))
            {
                if (!created) { ExitCode = 4; return 4; }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
                {
                    Log(e.Exception.ToString());
                    Hooks.Stop();
                    Environment.Exit(3);   // the watchdog starts a fresh bar
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    Log(e.ExceptionObject.ToString());
                };
                Taskbar.Hide();
                Bg.Start();
                Application.Run(new BarForm(false));
                Hooks.Stop();
                GC.KeepAlive(mutex);
            }
            return ExitCode;
        }

        static int RunRestore()
        {
            Process me = Process.GetCurrentProcess();
            for (int pass = 0; pass < 3; pass++)
            {
                foreach (Process p in Process.GetProcessesByName(me.ProcessName))
                {
                    if (p.Id == me.Id) continue;
                    try { p.Kill(); p.WaitForExit(2000); } catch { }
                }
                Thread.Sleep(300);
            }
            Taskbar.Restore();
            MessageBox.Show("StayBar is closed and the Windows taskbar is back.", "StayBar");
            return 0;
        }
    }

    // ------------------------------------------------------------------
    // Tiny key=value settings file (cached, thread-safe)
    // ------------------------------------------------------------------
    static class Settings
    {
        static readonly object gate = new object();
        static Dictionary<string, string> cache;
        static DateTime stamp;

        static string FilePath { get { return Path.Combine(Program.DataDir, "settings.txt"); } }

        static Dictionary<string, string> Load()
        {
            DateTime t = File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue;
            if (cache != null && t == stamp) return cache;
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(FilePath))
                    foreach (string line in File.ReadAllLines(FilePath))
                    {
                        int i = line.IndexOf('=');
                        if (i > 0) d[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                    }
            }
            catch { }
            cache = d; stamp = t;
            return d;
        }

        public static string Get(string key, string def)
        {
            lock (gate)
            {
                string v;
                return Load().TryGetValue(key, out v) ? v : def;
            }
        }

        // A profile switch: every key that managed() says belongs to the profile is replaced by the
        // profile's values (keys the profile doesn't have go back to their defaults); the rest stays.
        public static void ReplaceManaged(Dictionary<string, string> values, Func<string, bool> managed)
        {
            lock (gate)
            {
                var d = Load();
                var next = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in d) if (!managed(kv.Key)) next[kv.Key] = kv.Value;
                foreach (var kv in values) if (managed(kv.Key)) next[kv.Key] = kv.Value;
                try
                {
                    File.WriteAllLines(FilePath, next.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => kv.Key + "=" + kv.Value).ToArray());
                    cache = null;
                }
                catch { }
            }
        }

        // Every key starting with prefix, without the prefix (empty values are skipped).
        public static Dictionary<string, string> WithPrefix(string prefix)
        {
            lock (gate)
            {
                var res = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in Load())
                    if (kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && kv.Value.Length > 0)
                        res[kv.Key.Substring(prefix.Length)] = kv.Value;
                return res;
            }
        }

        public static void Set(string key, string value)
        {
            var d = new Dictionary<string, string>();
            d[key] = value;
            SetMany(d);
        }

        public static void SetMany(Dictionary<string, string> values)
        {
            lock (gate)
            {
                var d = Load();
                bool changed = false;
                foreach (var kv in values)
                {
                    string old;
                    if (!d.TryGetValue(kv.Key, out old) || old != kv.Value) { d[kv.Key] = kv.Value; changed = true; }
                }
                if (!changed) return;
                try
                {
                    File.WriteAllLines(FilePath, d.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => kv.Key + "=" + kv.Value).ToArray());
                    stamp = File.GetLastWriteTimeUtc(FilePath);
                }
                catch { }
            }
        }
    }

    static class Startup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool Enabled
        {
            get
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue("StayBar") != null;
            }
        }

        public static void Set(bool on)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue("StayBar", "\"" + Program.ExePath + "\"");
                else k.DeleteValue("StayBar", false);
            }
        }
    }

    // ------------------------------------------------------------------
    // Hiding / restoring the real Windows taskbar
    // ------------------------------------------------------------------
    static class Taskbar
    {
        static string StateFile { get { return Path.Combine(Program.DataDir, "taskbar-state.txt"); } }

        public static List<IntPtr> Windows()
        {
            var list = new List<IntPtr>();
            IntPtr main = Native.FindWindow("Shell_TrayWnd", null);
            if (main != IntPtr.Zero) list.Add(main);
            IntPtr h = IntPtr.Zero;
            while ((h = Native.FindWindowEx(IntPtr.Zero, h, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero) list.Add(h);
            return list;
        }

        static Native.APPBARDATA Abd()
        {
            var a = new Native.APPBARDATA();
            a.cbSize = Marshal.SizeOf(typeof(Native.APPBARDATA));
            a.hWnd = Native.FindWindow("Shell_TrayWnd", null);
            return a;
        }

        static int GetState()
        {
            var a = Abd();
            return Native.SHAppBarMessage(Native.ABM_GETSTATE, ref a).ToInt32();
        }

        static void SetState(int state)
        {
            var a = Abd();
            a.lParam = new IntPtr(state);
            Native.SHAppBarMessage(Native.ABM_SETSTATE, ref a);
        }

        public static void Hide()
        {
            // Remember the user's own setting once, so a crash never loses it.
            try { if (!File.Exists(StateFile)) File.WriteAllText(StateFile, GetState().ToString()); } catch { }
            Keep(false);
        }

        // Called every tick: auto-hide on (so it frees its space) and its windows hidden.
        public static void Keep(bool allowVisible)
        {
            if (Native.FindWindow("Shell_TrayWnd", null) == IntPtr.Zero) return;
            int st = GetState();
            if ((st & Native.ABS_AUTOHIDE) == 0) SetState(st | Native.ABS_AUTOHIDE);
            if (allowVisible) return;
            foreach (IntPtr h in Windows())
                if (Native.IsWindowVisible(h)) Native.ShowWindow(h, Native.SW_HIDE);
        }

        public static void Restore()
        {
            int state = Native.ABS_ALWAYSONTOP;
            try { if (File.Exists(StateFile)) state = int.Parse(File.ReadAllText(StateFile).Trim()); } catch { }
            SetState(state);
            foreach (IntPtr h in Windows()) Native.ShowWindow(h, Native.SW_SHOWNA);
            try { File.Delete(StateFile); } catch { }
        }
    }

    // ------------------------------------------------------------------
    // Optional timing log: settings.txt "debug.perf=true" writes perf.log every 30 s.
    // ------------------------------------------------------------------
    static class Perf
    {
        static readonly Dictionary<string, double[]> data = new Dictionary<string, double[]>();
        static DateTime next = DateTime.UtcNow.AddSeconds(30);
        static bool? on;

        public static void Add(string name, long startTimestamp)
        {
            if (on == null) on = Settings.Get("debug.perf", "false") == "true";
            if (on != true) return;
            double ms = (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
            double[] d;
            if (!data.TryGetValue(name, out d)) { d = new double[3]; data[name] = d; }
            d[0]++; d[1] += ms; d[2] = Math.Max(d[2], ms);
            if (DateTime.UtcNow < next) return;
            next = DateTime.UtcNow.AddSeconds(30);
            var sb = new StringBuilder(DateTime.Now.ToString("HH:mm:ss"));
            foreach (var kv in data)
                sb.AppendFormat("  {0}: {1}x avg {2:0.00} ms max {3:0.0} ms", kv.Key, kv.Value[0], kv.Value[1] / Math.Max(1, kv.Value[0]), kv.Value[2]);
            data.Clear();
            try { File.AppendAllText(Path.Combine(Program.DataDir, "perf.log"), sb + Environment.NewLine); } catch { }
        }
    }

    // ------------------------------------------------------------------
    // One background thread for periodic, slow-ish work (WinRT, WMI, PDH...).
    // Network calls go to the thread pool so they never stall the others.
    // ------------------------------------------------------------------
    static class Bg
    {
        class Job
        {
            public string Name; public int Every; public DateTime Next; public Action Work; public Func<bool> When; public bool Once;
        }

        static readonly List<Job> jobs = new List<Job>();
        static readonly object gate = new object();
        static readonly HashSet<string> logged = new HashSet<string>();
        static Thread thread;

        public static void Start()
        {
            if (thread != null) return;
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Name = "StayBar.Bg";
            thread.Start();
        }

        // Runs work every ms milliseconds, but only while when() is true (when may be null).
        public static void Every(string name, int ms, int firstDelay, Func<bool> when, Action work)
        {
            lock (gate)
            {
                var j = new Job();
                j.Name = name; j.Every = ms; j.When = when; j.Work = work; j.Next = DateTime.UtcNow.AddMilliseconds(firstDelay);
                jobs.Add(j);
                Monitor.PulseAll(gate);
            }
        }

        public static void Run(string name, Action work)
        {
            lock (gate)
            {
                var j = new Job();
                j.Name = name; j.Work = work; j.Once = true; j.Next = DateTime.UtcNow;
                jobs.Add(j);
                Monitor.PulseAll(gate);
            }
        }

        // Make a periodic job run right away.
        public static void Kick(string name)
        {
            lock (gate)
            {
                foreach (Job j in jobs) if (j.Name == name) j.Next = DateTime.UtcNow;
                Monitor.PulseAll(gate);
            }
        }

        public static void Pool(string name, Action work)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { work(); } catch (Exception ex) { LogOnce(name, ex); }
            });
        }

        static void Loop()
        {
            while (true)
            {
                Job due = null;
                lock (gate)
                {
                    while (due == null)
                    {
                        DateTime now = DateTime.UtcNow;
                        Job best = null;
                        foreach (Job j in jobs) if (best == null || j.Next < best.Next) best = j;
                        if (best != null && best.Next <= now) { due = best; break; }
                        int wait = best == null ? 2000 : (int)Math.Max(1, Math.Min(2000, (best.Next - now).TotalMilliseconds));
                        Monitor.Wait(gate, wait);
                    }
                    if (due.Once) jobs.Remove(due);
                    else due.Next = DateTime.UtcNow.AddMilliseconds(due.Every);
                }
                try { if (due.When == null || due.When()) due.Work(); }
                catch (Exception ex) { LogOnce(due.Name, ex); }
            }
        }

        public static void LogOnce(string name, Exception ex)
        {
            lock (logged)
            {
                if (!logged.Add(name)) return;
            }
            Program.Log("[" + name + "] " + ex);
        }
    }
}
