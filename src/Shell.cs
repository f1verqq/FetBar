// Windows, processes, keys, icons, launching, the app catalog and recent files.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace StayBar
{
    // Custom click actions for widgets: "app:<path>", "file:<path>", "folder:<path>", "url:<address>",
    // "cmd:<command line>" (runs hidden), "ps:<PowerShell>" (runs hidden).
    static class Actions
    {
        public static readonly string[] Kinds = { "", "app", "folder", "url", "cmd" };
        public static readonly string[] KindNames = { "Default", "App/file", "Folder", "Website", "Command" };

        public static void Run(string spec)
        {
            if (string.IsNullOrWhiteSpace(spec)) return;
            string kind, arg;
            Split(spec, out kind, out arg);
            if (arg.Length == 0) return;
            try
            {
                if (kind == "cmd" || kind == "ps")
                {
                    var psi = kind == "cmd" ? new ProcessStartInfo("cmd.exe", "/c " + arg)
                                            : new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command " + arg);
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.WindowStyle = ProcessWindowStyle.Hidden;
                    Process.Start(psi);
                    return;
                }
                if (kind == "url" && !arg.Contains("://")) arg = "https://" + arg;
                Win.Launch(arg);
            }
            catch (Exception ex) { Program.Log("Click action '" + spec + "': " + ex.Message); }
        }

        public static void Split(string spec, out string kind, out string arg)
        {
            kind = ""; arg = (spec ?? "").Trim();
            int c = arg.IndexOf(':');
            if (c > 0)
            {
                string k = arg.Substring(0, c).ToLowerInvariant();
                if (k == "app" || k == "file" || k == "folder" || k == "url" || k == "cmd" || k == "ps") { kind = k == "file" ? "app" : k; arg = arg.Substring(c + 1).Trim(); }
            }
        }
    }

    // Stable ids and names for screens (so each keeps its own bar layout across restarts).
    static class ScreenIds
    {
        public static string Of(System.Windows.Forms.Screen s)
        {
            try
            {
                var dd = new Native.DISPLAY_DEVICE();
                dd.cb = Marshal.SizeOf(dd);
                if (Native.EnumDisplayDevices(s.DeviceName, 0, ref dd, 1) && !string.IsNullOrEmpty(dd.DeviceID))
                {
                    // \\?\DISPLAY#GSM5B7F#5&2c2e2b2&0&UID4352#{...}  ->  GSM5B7F-52c2e2b20UID4352
                    string[] parts = dd.DeviceID.Split('#');
                    string raw = parts.Length >= 3 ? parts[1] + "-" + parts[2] : dd.DeviceID;
                    string id = new string(raw.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
                    if (id.Length > 0) return id.ToLowerInvariant();
                }
            }
            catch { }
            return new string(s.DeviceName.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        }

        public static string Name(System.Windows.Forms.Screen s)
        {
            try
            {
                var dd = new Native.DISPLAY_DEVICE();
                dd.cb = Marshal.SizeOf(dd);
                if (Native.EnumDisplayDevices(s.DeviceName, 0, ref dd, 0) && !string.IsNullOrEmpty(dd.DeviceString)) return dd.DeviceString;
            }
            catch { }
            return s.DeviceName.Replace("\\\\.\\", "");
        }
    }

    static class Win
    {
        static readonly uint MyPid = (uint)Process.GetCurrentProcess().Id;
        static readonly string[] HiddenClasses = { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow" };
        static readonly PROPERTYKEY AumidKey = new PROPERTYKEY(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

        public static string ClassOf(IntPtr h)
        {
            var sb = new StringBuilder(256);
            Native.GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string TitleOf(IntPtr h)
        {
            int n = Native.GetWindowTextLength(h);
            if (n <= 0) return "";
            var sb = new StringBuilder(n + 1);
            Native.GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static uint PidOf(IntPtr h)
        {
            uint pid;
            Native.GetWindowThreadProcessId(h, out pid);
            return pid;
        }

        public static bool IsOwn(IntPtr h) { return h != IntPtr.Zero && PidOf(h) == MyPid; }

        public static string ProcessPath(uint pid)
        {
            IntPtr hp = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hp == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return Native.QueryFullProcessImageName(hp, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { Native.CloseHandle(hp); }
        }

        public static bool IsCloaked(IntPtr h)
        {
            int v;
            try { return Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out v, 4) == 0 && v != 0; }
            catch { return false; }
        }

        // Same rules the real taskbar roughly uses to decide what gets a button.
        public static bool IsTaskWindow(IntPtr h)
        {
            if (!Native.IsWindowVisible(h)) return false;
            if (PidOf(h) == MyPid) return false;
            int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
            if ((ex & Native.WS_EX_APPWINDOW) == 0)
            {
                if (Native.GetWindow(h, Native.GW_OWNER) != IntPtr.Zero) return false;
                if ((ex & Native.WS_EX_TOOLWINDOW) != 0) return false;
                if ((ex & Native.WS_EX_NOACTIVATE) != 0) return false;
            }
            if (Native.GetWindowTextLength(h) == 0) return false;
            if (IsCloaked(h)) return false;
            return Array.IndexOf(HiddenClasses, ClassOf(h)) < 0;
        }

        public static List<IntPtr> TaskWindows()
        {
            var list = new List<IntPtr>();
            Native.EnumWindows(delegate(IntPtr h, IntPtr l) { if (IsTaskWindow(h)) list.Add(h); return true; }, IntPtr.Zero);
            return list;   // top of the z-order first
        }

        // UWP apps live inside ApplicationFrameHost; find the real app's process.
        public static string HostedAppPath(IntPtr frame)
        {
            uint hostPid = PidOf(frame);
            string found = null;
            Native.EnumChildWindows(frame, delegate(IntPtr c, IntPtr l)
            {
                uint pid = PidOf(c);
                if (pid != hostPid) { found = ProcessPath(pid); return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        public static string ExeOfWindow(IntPtr h)
        {
            string exe = ProcessPath(PidOf(h));
            if (exe != null && exe.EndsWith("\\ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
                exe = HostedAppPath(h) ?? exe;
            return exe;
        }

        static string ReadAumid(IPropertyStore ps)
        {
            PROPERTYKEY key = AumidKey;
            PROPVARIANT pv;
            if (ps.GetValue(ref key, out pv) != 0) return null;
            try { return pv.vt == 31 ? Marshal.PtrToStringUni(pv.p) : null; }
            finally { Native.PropVariantClear(ref pv); }
        }

        public static string WindowAumid(IntPtr h)
        {
            IPropertyStore ps = null;
            try
            {
                Guid iid = typeof(IPropertyStore).GUID;
                if (Native.SHGetPropertyStoreForWindow(h, ref iid, out ps) != 0 || ps == null) return null;
                return ReadAumid(ps);
            }
            catch { return null; }
            finally { if (ps != null) Marshal.ReleaseComObject(ps); }
        }

        public static string FileAumid(string path)
        {
            IPropertyStore ps = null;
            try
            {
                Guid iid = typeof(IPropertyStore).GUID;
                if (Native.SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, 0, ref iid, out ps) != 0 || ps == null) return null;
                return ReadAumid(ps);
            }
            catch { return null; }
            finally { if (ps != null) Marshal.ReleaseComObject(ps); }
        }

        // Returns { targetPath, arguments } of a .lnk file.
        public static string[] ResolveLnk(string path)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(t);
                object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                Type lt = lnk.GetType();
                string target = (string)lt.InvokeMember("TargetPath", BindingFlags.GetProperty, null, lnk, null);
                string args = (string)lt.InvokeMember("Arguments", BindingFlags.GetProperty, null, lnk, null);
                Marshal.ReleaseComObject(lnk);
                Marshal.ReleaseComObject(shell);
                return new[] { target ?? "", args ?? "" };
            }
            catch { return new[] { "", "" }; }
        }

        // Icon of any file, shortcut or "shell:AppsFolder\<AppID>", with transparency.
        public static Bitmap ShellImage(string parsingName, int px)
        {
            IShellItemImageFactory f = null;
            IntPtr hbm = IntPtr.Zero;
            try
            {
                Guid iid = typeof(IShellItemImageFactory).GUID;
                if (Native.SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out f) != 0 || f == null) return null;
                SIZE sz; sz.cx = px; sz.cy = px;
                if (f.GetImage(sz, Native.SIIGBF_ICONONLY | Native.SIIGBF_BIGGERSIZEOK, out hbm) != 0 || hbm == IntPtr.Zero) return null;
                return FromHBitmap(hbm);
            }
            catch { return null; }
            finally
            {
                if (hbm != IntPtr.Zero) Native.DeleteObject(hbm);
                if (f != null) Marshal.ReleaseComObject(f);
            }
        }

        // Reads the shell's 32-bit icon bitmap pixel-for-pixel, keeping its transparency.
        static Bitmap FromHBitmap(IntPtr hbm)
        {
            var bm = new Native.BITMAP();
            if (Native.GetObject(hbm, Marshal.SizeOf(typeof(Native.BITMAP)), ref bm) == 0) return null;
            int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
            if (w <= 0 || h <= 0) return null;
            var bi = new Native.BITMAPINFOHEADER();
            bi.biSize = 40; bi.biWidth = w; bi.biHeight = -h; bi.biPlanes = 1; bi.biBitCount = 32;
            byte[] buf = new byte[w * h * 4];
            IntPtr dc = Native.GetDC(IntPtr.Zero);
            try { if (Native.GetDIBits(dc, hbm, 0, (uint)h, buf, ref bi, 0) == 0) return null; }
            finally { Native.ReleaseDC(IntPtr.Zero, dc); }

            bool anyAlpha = false, premultiplied = true;
            for (int i = 0; i < buf.Length; i += 4)
            {
                byte a = buf[i + 3];
                if (a != 0) anyAlpha = true;
                if (buf[i] > a || buf[i + 1] > a || buf[i + 2] > a) premultiplied = false;
            }
            if (!anyAlpha) for (int i = 3; i < buf.Length; i += 4) buf[i] = 255;
            PixelFormat pf = anyAlpha && premultiplied ? PixelFormat.Format32bppPArgb : PixelFormat.Format32bppArgb;
            var dst = new Bitmap(w, h, pf);
            BitmapData dd = dst.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, pf);
            for (int y = 0; y < h; y++) Marshal.Copy(buf, y * w * 4, dd.Scan0 + y * dd.Stride, w * 4);
            dst.UnlockBits(dd);
            return dst;
        }

        // Brings a window to the front, even when Windows' focus rules say no.
        public static void Activate(IntPtr h)
        {
            if (Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_RESTORE);
            ForceForeground(h);
        }

        public static void ForceForeground(IntPtr h)
        {
            if (Native.SetForegroundWindow(h)) return;
            IntPtr fg = Native.GetForegroundWindow();
            uint dummy;
            uint fgThread = fg == IntPtr.Zero ? 0 : Native.GetWindowThreadProcessId(fg, out dummy);
            uint me = Native.GetCurrentThreadId();
            bool attached = fgThread != 0 && fgThread != me && Native.AttachThreadInput(me, fgThread, true);
            try
            {
                Native.BringWindowToTop(h);
                if (!Native.SetForegroundWindow(h))
                {
                    // Classic trick: a tap of Alt lets us take the foreground.
                    Native.keybd_event(Native.VK_MENU, 0, 0, UIntPtr.Zero);
                    Native.keybd_event(Native.VK_MENU, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
                    Native.SetForegroundWindow(h);
                }
            }
            finally { if (attached) Native.AttachThreadInput(me, fgThread, false); }
        }

        public static void Press(params byte[] keys)
        {
            foreach (byte k in keys) Native.keybd_event(k, 0, 0, UIntPtr.Zero);
            for (int i = keys.Length - 1; i >= 0; i--) Native.keybd_event(keys[i], 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        public static bool Launch(string path) { return Launch(path, null); }

        public static bool Launch(string path, string args)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) { Process.Start("explorer.exe", "\"" + path + "\""); return true; }
                if (path.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase) || path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    var p0 = new ProcessStartInfo(path);
                    p0.UseShellExecute = true;
                    Process.Start(p0);
                    return true;
                }
                var psi = new ProcessStartInfo(path);
                psi.UseShellExecute = true;
                if (!string.IsNullOrEmpty(args)) psi.Arguments = args;
                if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    try { psi.WorkingDirectory = Path.GetDirectoryName(path); } catch { }
                }
                Process.Start(psi);
                return true;
            }
            catch (Exception ex) { Program.Log("Could not open " + path + ": " + ex.Message); return false; }
        }

        public static string KnownFolder(Guid id)
        {
            IntPtr p;
            if (Native.SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out p) != 0) return null;
            try { return Marshal.PtrToStringUni(p); }
            finally { Native.CoTaskMemFree(p); }
        }

        // "{6D809377-...}\App\app.exe" (how Start lists some desktop apps) -> a real path.
        public static string ExpandKnownFolderPath(string s)
        {
            if (string.IsNullOrEmpty(s) || s[0] != '{') return s;
            int end = s.IndexOf('}');
            if (end < 0) return s;
            Guid g;
            if (!Guid.TryParse(s.Substring(0, end + 1), out g)) return s;
            string root = KnownFolder(g);
            return root == null ? s : root + s.Substring(end + 1);
        }

        public static string ShortTitle(string t, int max)
        {
            if (t == null) return "";
            return t.Length > max ? t.Substring(0, max - 1) + "…" : t;
        }
    }

    // ------------------------------------------------------------------
    // Everything the launcher can open: Start menu shortcuts + Store apps
    // ------------------------------------------------------------------
    class AppEntry
    {
        public string Name, Launch, IconSource, Lower, Initials, Kind;
    }

    static class AppIndex
    {
        public static volatile List<AppEntry> Items = new List<AppEntry>();
        static DateTime built = DateTime.MinValue;
        static int building;
        static readonly string[] Junk = { "uninstall", "readme", "read me", "help", "documentation", "website", "release notes",
            "license", "manual", "changelog", "what's new", "support", "faq", "eula" };

        public static void Ensure()
        {
            if ((DateTime.UtcNow - built).TotalMinutes < 10 && Items.Count > 0) return;
            if (Interlocked.Exchange(ref building, 1) == 1) return;
            var t = new Thread(delegate()
            {
                try { Build(); }
                catch (Exception ex) { Bg.LogOnce("appindex", ex); }
                finally { building = 0; }
            });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        public static void BuildNow() { try { Build(); } catch (Exception ex) { Bg.LogOnce("appindex", ex); } }

        static void Build()
        {
            var list = new List<AppEntry>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] roots =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\Start Menu\Programs"),
            };
            foreach (string root in roots)
            {
                if (!Directory.Exists(root)) continue;
                string[] files;
                try { files = Directory.GetFiles(root, "*.lnk", SearchOption.AllDirectories); } catch { continue; }
                foreach (string f in files)
                {
                    string name = Path.GetFileNameWithoutExtension(f);
                    string low = name.ToLowerInvariant();
                    if (Junk.Any(j => low.Contains(j))) continue;
                    if (!names.Add(name)) continue;
                    list.Add(Make(name, f, f, "app"));
                }
            }
            // Store apps and anything else Start knows about.
            try
            {
                Type t = Type.GetTypeFromProgID("Shell.Application");
                object shell = Activator.CreateInstance(t);
                object folder = t.InvokeMember("NameSpace", BindingFlags.InvokeMethod, null, shell, new object[] { "shell:AppsFolder" });
                object items = folder.GetType().InvokeMember("Items", BindingFlags.InvokeMethod, null, folder, null);
                int count = (int)items.GetType().InvokeMember("Count", BindingFlags.GetProperty, null, items, null);
                for (int i = 0; i < count; i++)
                {
                    object it = items.GetType().InvokeMember("Item", BindingFlags.InvokeMethod, null, items, new object[] { i });
                    if (it == null) continue;
                    string name = (string)it.GetType().InvokeMember("Name", BindingFlags.GetProperty, null, it, null);
                    string path = (string)it.GetType().InvokeMember("Path", BindingFlags.GetProperty, null, it, null);
                    Marshal.ReleaseComObject(it);
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path)) continue;
                    string low = name.ToLowerInvariant();
                    if (Junk.Any(j => low.Contains(j)) || path.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!names.Add(name)) continue;
                    string launch = "shell:AppsFolder\\" + path;
                    string real = Win.ExpandKnownFolderPath(path);
                    if (real != path && real.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(real)) launch = real;
                    list.Add(Make(name, launch, launch, path.Contains("!") ? "store" : "app"));
                }
                Marshal.ReleaseComObject(items);
                Marshal.ReleaseComObject(folder);
                Marshal.ReleaseComObject(shell);
            }
            catch (Exception ex) { Bg.LogOnce("appsfolder", ex); }
            Items = list.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
            built = DateTime.UtcNow;
        }

        static AppEntry Make(string name, string launch, string icon, string kind)
        {
            var e = new AppEntry();
            e.Name = name; e.Launch = launch; e.IconSource = icon; e.Kind = kind;
            e.Lower = name.ToLowerInvariant();
            var sb = new StringBuilder();
            bool start = true;
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c)) { if (start || char.IsUpper(c)) sb.Append(char.ToLowerInvariant(c)); start = false; }
                else start = true;
            }
            e.Initials = sb.ToString();
            return e;
        }

        // Fuzzy score: higher is better, below 0 = no match.
        public static int Score(string q, string lower, string initials)
        {
            if (q.Length == 0) return 0;
            if (lower == q) return 1000;
            if (lower.StartsWith(q)) return 900 - lower.Length;
            int wi = lower.IndexOf(" " + q, StringComparison.Ordinal);
            if (wi >= 0) return 800 - wi;
            if (initials != null && initials.StartsWith(q)) return 760 - initials.Length;
            int si = lower.IndexOf(q, StringComparison.Ordinal);
            if (si >= 0) return 600 - si;
            // subsequence with gap penalty
            int pos = 0, gaps = 0, last = -1;
            foreach (char c in q)
            {
                int f = lower.IndexOf(c, pos);
                if (f < 0) return -1;
                if (last >= 0 && f > last + 1) gaps += f - last - 1;
                last = f; pos = f + 1;
            }
            return 400 - gaps * 8 - lower.Length;
        }
    }

    // ------------------------------------------------------------------
    // "Jump list": the recent files Windows remembers, filtered by the app
    // that opens them.
    // ------------------------------------------------------------------
    static class Recent
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        static extern int AssocQueryString(int flags, int str, string assoc, string extra, StringBuilder outStr, ref uint outLen);

        static readonly Dictionary<string, string> handler = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static string HandlerFor(string ext)
        {
            string exe;
            if (handler.TryGetValue(ext, out exe)) return exe;
            var sb = new StringBuilder(520);
            uint len = (uint)sb.Capacity;
            exe = AssocQueryString(0, 2, ext, null, sb, ref len) == 0 ? Path.GetFileName(sb.ToString()) : "";
            handler[ext] = exe;
            return exe;
        }

        // Up to max { name, lnkPath } pairs for files this exe opens (folders for Explorer).
        public static List<string[]> For(string exePath, int max)
        {
            var result = new List<string[]>();
            if (string.IsNullOrEmpty(exePath)) return result;
            string exe = Path.GetFileName(exePath);
            bool explorer = exe.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase);
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
            if (!Directory.Exists(dir)) return result;
            FileInfo[] files;
            try { files = new DirectoryInfo(dir).GetFiles("*.lnk"); } catch { return result; }
            foreach (FileInfo f in files.OrderByDescending(x => x.LastWriteTimeUtc).Take(200))
            {
                string name = Path.GetFileNameWithoutExtension(f.Name);
                string ext = Path.GetExtension(name);
                bool match;
                if (explorer)
                {
                    // folders only: skip protocol links (ms-gamebar:..., {GUID}...) and check the target is a real folder
                    match = (ext.Length == 0 || ext.Length > 6) && !name.StartsWith("ms-", StringComparison.OrdinalIgnoreCase) && name.IndexOf('{') < 0 && name.IndexOf('=') < 0;
                    if (match) { string t = Win.ResolveLnk(f.FullName)[0]; match = t.Length > 0 && Directory.Exists(t); }
                }
                else match = ext.Length > 1 && ext.Length <= 6 && HandlerFor(ext).Equals(exe, StringComparison.OrdinalIgnoreCase);
                if (!match) continue;
                result.Add(new[] { name, f.FullName });
                if (result.Count >= max) break;
            }
            return result;
        }
    }
}
