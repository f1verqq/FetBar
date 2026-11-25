// Data behind the widgets. Slow sources run on Bg (background thread) and publish
// immutable snapshots; the UI only reads them.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Windows.Devices.Radios;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Networking.Connectivity;
using Windows.Storage.Streams;

namespace StayBar
{
    static class Svc
    {
        public static bool Demo;   // --selftest: fake but realistic data, no system calls

        public static bool Active(string id)
        {
            return Config.HasWidget(id) || (id == "trash" && Config.IsDock && Config.DockTrash);
        }

        public static void StartAll()
        {
            Bg.Every("stats.slow", 1000, 300, () => Active("gpu") || Active("net") || Active("cpu"), Sys.SampleSlow);
            Bg.Every("media", 2500, 200, () => Active("media"), Media.Poll);
            Bg.Every("radios", 2000, 400, () => Active("connect") || Active("quick") || Automations.NeedsWifi, Radios.Poll);
            Bg.Every("brightness", 6000, 500, () => Active("brightness"), Brightness.Read);
            Bg.Every("recycle", 15000, 800, () => Active("trash"), Recycle.Poll);
            Bg.Every("disk", 60000, 900, () => Active("disk"), Disk.Poll);
            Bg.Every("weather", 5 * 60000, 1500, () => Active("weather"), delegate { if (Weather.Stale) Bg.Pool("weather", Weather.Fetch); });
            Bg.Every("wallpaper", 10 * 60000, 100, () => Config.AccentMode == "wallpaper", WallpaperAccent);
            Bg.Every("downloads", 5 * 60000, 700, () => Active("download"), Downloads.Poll);   // + kicked by a folder watcher
        }

        public static void WallpaperAccent()
        {
            Color? old = WallpaperColor.Value;
            WallpaperColor.Compute();
            if (old != WallpaperColor.Value) BarForm.Post(delegate { Config.ResolveAccent(); BarForm.RefreshLook(); });
        }
    }

    // ------------------------------------------------------------------
    // WinRT async helpers (no AsTask in .NET Framework without the SDK)
    // ------------------------------------------------------------------
    static class WinRT
    {
        public static T Wait<T>(IAsyncOperation<T> op, int ms)
        {
            if (op == null) return default(T);
            using (var ev = new ManualResetEvent(false))
            {
                op.Completed = delegate(IAsyncOperation<T> o, AsyncStatus s) { try { ev.Set(); } catch { } };
                if (op.Status == AsyncStatus.Started) ev.WaitOne(ms);
            }
            return op.Status == AsyncStatus.Completed ? op.GetResults() : default(T);
        }

        public static T Wait<T>(IAsyncOperation<T> op) { return Wait(op, 4000); }
    }

    // ------------------------------------------------------------------
    // A rolling window of samples for the mini graphs
    // ------------------------------------------------------------------
    class History
    {
        public readonly float[] Data;
        public int Count;
        public History(int n) { Data = new float[n]; }
        public void Push(float v)
        {
            Array.Copy(Data, 1, Data, 0, Data.Length - 1);
            Data[Data.Length - 1] = v;
            if (Count < Data.Length) Count++;
        }
        public float Max() { float m = 0; foreach (float v in Data) if (v > m) m = v; return m; }
    }

    // ------------------------------------------------------------------
    // CPU, RAM, GPU, network, temperature
    // ------------------------------------------------------------------
    static class Sys
    {
        public static float Cpu, Ram, Gpu = -1, TempC = -1;
        public static double RamUsedGb, RamTotalGb, Down, Up;   // bytes per second
        public static readonly History CpuH = new History(48), RamH = new History(48), GpuH = new History(48),
                                        DownH = new History(48), UpH = new History(48);
        static long lastIdle, lastTotal;
        static int ticks;

        // UI thread, every 500 ms: cheap kernel calls only.
        public static void Sample()
        {
            if (Svc.Demo) return;
            long idle, kernel, user;
            if (Native.GetSystemTimes(out idle, out kernel, out user))
            {
                long total = kernel + user;
                if (lastTotal != 0)
                {
                    long dt = total - lastTotal, di = idle - lastIdle;
                    if (dt > 0) Cpu = Cpu * 0.4f + 0.6f * (float)(100.0 * (dt - di) / dt);
                }
                lastTotal = total; lastIdle = idle;
            }
            var ms = new Native.MEMORYSTATUSEX();
            ms.dwLength = (uint)Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX));
            if (Native.GlobalMemoryStatusEx(ref ms))
            {
                Ram = ms.dwMemoryLoad;
                RamTotalGb = ms.ullTotalPhys / 1073741824.0;
                RamUsedGb = (ms.ullTotalPhys - ms.ullAvailPhys) / 1073741824.0;
            }
            if (++ticks % 2 == 0) { CpuH.Push(Cpu); RamH.Push(Ram); }
        }

        static Pdh gpu, temp;
        static DateTime gpuMade, tempRead;
        static long lastRx, lastTx;
        static DateTime lastNet;

        // Background thread, every second.
        public static void SampleSlow()
        {
            if (Svc.Active("gpu"))
            {
                if (gpu == null || (DateTime.UtcNow - gpuMade).TotalSeconds > 120)
                {
                    if (gpu != null) gpu.Dispose();
                    gpu = new Pdh("\\GPU Engine(*)\\Utilization Percentage");
                    gpuMade = DateTime.UtcNow;
                }
                if (gpu.Ok)
                {
                    var perEngine = new Dictionary<string, double>();
                    foreach (var kv in gpu.Read())
                    {
                        string n = kv.Key;
                        int i = n.IndexOf("_luid_", StringComparison.Ordinal), j = n.IndexOf("_engtype_", StringComparison.Ordinal);
                        string key = i >= 0 && j > i ? n.Substring(i, j - i) : n;
                        double v;
                        perEngine.TryGetValue(key, out v);
                        perEngine[key] = v + kv.Value;
                    }
                    float g = perEngine.Count == 0 ? 0 : (float)Math.Min(100, perEngine.Values.Max());
                    Gpu = Gpu < 0 ? g : Gpu * 0.4f + g * 0.6f;
                    GpuH.Push(Gpu);
                }
            }
            if (Svc.Active("cpu") && (DateTime.UtcNow - tempRead).TotalSeconds >= 3)
            {
                tempRead = DateTime.UtcNow;
                if (temp == null) temp = new Pdh("\\Thermal Zone Information(*)\\Temperature");
                if (temp.Ok)
                {
                    double best = -1;
                    foreach (var kv in temp.Read())
                        if (kv.Value > 273.5 && kv.Value < 400) best = Math.Max(best, kv.Value - 273.15);
                    TempC = (float)best;
                }
            }
            if (Svc.Active("net"))
            {
                long rx = 0, tx = 0;
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    try { IPInterfaceStatistics st = ni.GetIPStatistics(); rx += st.BytesReceived; tx += st.BytesSent; } catch { }
                }
                DateTime now = DateTime.UtcNow;
                if (lastNet != DateTime.MinValue)
                {
                    double sec = Math.Max(0.2, (now - lastNet).TotalSeconds);
                    Down = Math.Max(0, (rx - lastRx) / sec);
                    Up = Math.Max(0, (tx - lastTx) / sec);
                    DownH.Push((float)Down); UpH.Push((float)Up);
                }
                lastRx = rx; lastTx = tx; lastNet = now;
            }
            if (Svc.Active("gpu") || Svc.Active("net")) BarForm.Poke();
        }

        public static string Rate(double bytesPerSec)
        {
            if (bytesPerSec < 1024) return ((int)bytesPerSec) + " B/s";
            if (bytesPerSec < 1024 * 1024) return (bytesPerSec / 1024).ToString(bytesPerSec < 10240 ? "0.0" : "0") + " KB/s";
            return (bytesPerSec / 1048576).ToString(bytesPerSec < 10485760 ? "0.0" : "0") + " MB/s";
        }
    }

    // Performance counters through PDH (works without admin).
    sealed class Pdh : IDisposable
    {
        IntPtr query, counter;
        public bool Ok;

        public Pdh(string path)
        {
            try
            {
                if (Native.PdhOpenQuery(null, IntPtr.Zero, out query) != 0) return;
                if (Native.PdhAddEnglishCounter(query, path, IntPtr.Zero, out counter) != 0) return;
                Native.PdhCollectQueryData(query);
                Ok = true;
            }
            catch { Ok = false; }
        }

        public List<KeyValuePair<string, double>> Read()
        {
            var list = new List<KeyValuePair<string, double>>();
            if (!Ok || Native.PdhCollectQueryData(query) != 0) return list;
            uint size = 0, count;
            const uint FMT = 0x200 | 0x8000;   // double, no 100 cap
            int r = Native.PdhGetFormattedCounterArray(counter, FMT, ref size, out count, IntPtr.Zero);
            if (r != unchecked((int)0x800007D2) || size == 0) return list;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (Native.PdhGetFormattedCounterArray(counter, FMT, ref size, out count, buf) != 0) return list;
                for (int i = 0; i < count; i++)
                {
                    IntPtr item = buf + i * 24;
                    string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item));
                    int status = Marshal.ReadInt32(item, 8);
                    double v = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, 16));
                    if (status == 0 || status == 1) list.Add(new KeyValuePair<string, double>(name ?? "", v));
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
            return list;
        }

        public void Dispose()
        {
            if (query != IntPtr.Zero) { Native.PdhCloseQuery(query); query = IntPtr.Zero; }
            Ok = false;
        }
    }

    // ------------------------------------------------------------------
    // Volume (Core Audio, UI thread; the calls are fast)
    // ------------------------------------------------------------------
    static class Audio
    {
        static IMMDeviceEnumerator en;
        static IAudioEndpointVolume vol;
        static string devId;
        static DateTime checkedAt;
        static Guid ctx = Guid.NewGuid();
        public static string DeviceName = "Speakers";
        static float demoVol = 64; static bool demoMute;

        static bool Ensure()
        {
            if (Svc.Demo) return true;
            if (vol != null && (DateTime.UtcNow - checkedAt).TotalSeconds < 4) return true;
            checkedAt = DateTime.UtcNow;
            try
            {
                if (en == null) en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                IMMDevice dev;
                if (en.GetDefaultAudioEndpoint(0, 1, out dev) != 0 || dev == null) { Drop(); return false; }
                string id;
                dev.GetId(out id);
                if (id != devId || vol == null)
                {
                    Drop();
                    Guid iid = typeof(IAudioEndpointVolume).GUID;
                    object o;
                    if (dev.Activate(ref iid, 23, IntPtr.Zero, out o) == 0) vol = (IAudioEndpointVolume)o;
                    devId = id;
                    DeviceName = ReadName(dev) ?? "Speakers";
                }
                Marshal.ReleaseComObject(dev);
                return vol != null;
            }
            catch (Exception ex) { Bg.LogOnce("audio", ex); Drop(); return false; }
        }

        static string ReadName(IMMDevice dev)
        {
            IPropertyStore ps;
            if (dev.OpenPropertyStore(0, out ps) != 0 || ps == null) return null;
            try
            {
                var key = new PROPERTYKEY(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 2);   // device description
                PROPVARIANT pv;
                if (ps.GetValue(ref key, out pv) != 0) return null;
                try { return pv.vt == 31 ? Marshal.PtrToStringUni(pv.p) : null; }
                finally { Native.PropVariantClear(ref pv); }
            }
            finally { Marshal.ReleaseComObject(ps); }
        }

        static void Drop()
        {
            if (vol != null) { try { Marshal.ReleaseComObject(vol); } catch { } vol = null; }
            devId = null;
        }

        public static bool Available { get { return Ensure(); } }

        public static float Volume
        {
            get
            {
                if (Svc.Demo) return demoVol;
                float v;
                return Ensure() && vol.GetMasterVolumeLevelScalar(out v) == 0 ? v * 100f : 0;
            }
            set
            {
                float v = Math.Max(0, Math.Min(100, value));
                if (Svc.Demo) { demoVol = v; return; }
                if (!Ensure()) return;
                vol.SetMasterVolumeLevelScalar(v / 100f, ref ctx);
                if (v > 0) { bool m; if (vol.GetMute(out m) == 0 && m) vol.SetMute(false, ref ctx); }
            }
        }

        public static bool Muted
        {
            get
            {
                if (Svc.Demo) return demoMute;
                bool m;
                return Ensure() && vol.GetMute(out m) == 0 && m;
            }
            set
            {
                if (Svc.Demo) { demoMute = value; return; }
                if (Ensure()) vol.SetMute(value, ref ctx);
            }
        }

        public static string Glyph(float v, bool muted)
        {
            if (muted) return "\uE74F";
            return v <= 0.5f ? "\uE992" : v < 34 ? "\uE993" : v < 67 ? "\uE994" : "\uE995";
        }
    }

    // ------------------------------------------------------------------
    // Laptop screen brightness through WMI (background thread)
    // ------------------------------------------------------------------
    static class Brightness
    {
        public static volatile int Level = -1;      // -1 = not known yet / not supported
        public static volatile int Supported = -1;  // -1 unknown, 0 no, 1 yes
        static int pending = -1, queued;

        public static void Read()
        {
            if (Svc.Demo) return;
            try
            {
                using (var s = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active=True"))
                using (ManagementObjectCollection res = s.Get())
                {
                    int lvl = -1;
                    foreach (ManagementObject o in res) { lvl = Convert.ToInt32(o["CurrentBrightness"]); o.Dispose(); break; }
                    if (pending < 0 && lvl >= 0) Level = lvl;
                    Supported = lvl >= 0 ? 1 : 0;
                }
            }
            catch { Supported = 0; }
            BarForm.Poke();
        }

        public static void Set(int v)
        {
            v = Math.Max(0, Math.Min(100, v));
            Level = v;
            pending = v;
            if (Svc.Demo) return;
            if (Interlocked.Exchange(ref queued, 1) == 0) Bg.Run("brightness.set", Apply);
        }

        static void Apply()
        {
            queued = 0;
            int v = Interlocked.Exchange(ref pending, -1);
            if (v < 0) return;
            using (var mc = new ManagementClass("root\\WMI", "WmiMonitorBrightnessMethods", null))
            using (ManagementObjectCollection res = mc.GetInstances())
                foreach (ManagementObject o in res)
                {
                    o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)v });
                    o.Dispose();
                }
        }
    }

    // ------------------------------------------------------------------
    // Wi-Fi / Bluetooth radios and the current network (WinRT, background)
    // ------------------------------------------------------------------
    static class Radios
    {
        public static volatile int Wifi = -1, Bt = -1;     // -1 none/unknown, 0 off, 1 on
        public static volatile string Network = "";        // SSID, "Ethernet" or ""
        public static volatile int Bars = -1;
        static Radio wifi, bt;
        static bool init;

        public static void Poll()
        {
            if (Svc.Demo) return;
            if (!init)
            {
                init = true;
                try
                {
                    WinRT.Wait(Radio.RequestAccessAsync());
                    var all = WinRT.Wait(Radio.GetRadiosAsync());
                    if (all != null)
                        foreach (Radio r in all)
                        {
                            if (r.Kind == RadioKind.WiFi && wifi == null) wifi = r;
                            if (r.Kind == RadioKind.Bluetooth && bt == null) bt = r;
                        }
                }
                catch (Exception ex) { Bg.LogOnce("radios", ex); }
            }
            int w = wifi == null ? -1 : wifi.State == RadioState.On ? 1 : 0;
            int b = bt == null ? -1 : bt.State == RadioState.On ? 1 : 0;
            string net = "";
            int bars = -1;
            try
            {
                ConnectionProfile p = NetworkInformation.GetInternetConnectionProfile();
                if (p != null)
                {
                    if (p.IsWlanConnectionProfile) net = p.WlanConnectionProfileDetails.GetConnectedSsid();
                    else if (p.IsWwanConnectionProfile) net = "Cellular";
                    else net = "Ethernet";
                    byte? sb = p.GetSignalBars();
                    if (sb.HasValue) bars = sb.Value;
                }
            }
            catch { }
            bool changed = w != Wifi || b != Bt || net != Network || bars != Bars;
            Wifi = w; Bt = b; Network = net ?? ""; Bars = bars;
            if (changed) BarForm.Poke();
        }

        public static void Toggle(bool isWifi)
        {
            if (Svc.Demo) { if (isWifi) Wifi = Wifi == 1 ? 0 : 1; else Bt = Bt == 1 ? 0 : 1; return; }
            Radio r = isWifi ? wifi : bt;
            if (r == null) return;
            if (isWifi) Wifi = Wifi == 1 ? 0 : 1; else Bt = Bt == 1 ? 0 : 1;   // optimistic
            Bg.Run("radio.toggle", delegate
            {
                RadioState want = r.State == RadioState.On ? RadioState.Off : RadioState.On;
                WinRT.Wait(r.SetStateAsync(want), 6000);
                Thread.Sleep(300);
                Poll();
            });
        }
    }

    // ------------------------------------------------------------------
    // Now playing (GlobalSystemMediaTransportControls)
    // ------------------------------------------------------------------
    class MediaInfo
    {
        public string Title = "", Artist = "", Album = "", AppId = "", AppName = "";
        public bool Playing, CanPrev, CanNext;
        public Bitmap Art;
        public Color ArtColor = Color.Empty;
        public double PosSec, DurSec;
        public DateTime Stamp = DateTime.UtcNow;

        public double Position
        {
            get
            {
                double p = PosSec + (Playing ? (DateTime.UtcNow - Stamp).TotalSeconds : 0);
                return DurSec > 0 ? Math.Min(DurSec, p) : p;
            }
        }
    }

    static class Media
    {
        public static volatile MediaInfo Now;
        static GlobalSystemMediaTransportControlsSessionManager mgr;
        static GlobalSystemMediaTransportControlsSession cur;
        static string artKey;
        static Bitmap artCache;
        static Color artColor;
        static int refreshQueued;

        public static void Poll()
        {
            if (Svc.Demo) return;
            if (mgr == null)
            {
                mgr = WinRT.Wait(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
                if (mgr == null) return;
                try
                {
                    mgr.CurrentSessionChanged += delegate { Queue(); };
                    mgr.SessionsChanged += delegate { Queue(); };
                }
                catch (Exception ex) { Bg.LogOnce("media.events", ex); }
            }
            Refresh();
        }

        static void Queue()
        {
            if (Interlocked.Exchange(ref refreshQueued, 1) == 0) Bg.Run("media.refresh", Refresh);
        }

        static GlobalSystemMediaTransportControlsSession Pick()
        {
            var s = mgr.GetCurrentSession();
            // Prefer a session that is actually playing.
            try
            {
                foreach (var x in mgr.GetSessions())
                {
                    var pi = x.GetPlaybackInfo();
                    if (pi != null && pi.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) return x;
                }
            }
            catch { }
            return s;
        }

        static void Refresh()
        {
            refreshQueued = 0;
            if (mgr == null) return;
            GlobalSystemMediaTransportControlsSession s = Pick();
            if (s == null) { if (Now != null) { Now = null; BarForm.Poke(); } cur = null; return; }
            if (cur == null || cur.SourceAppUserModelId != s.SourceAppUserModelId)
            {
                cur = s;
                try
                {
                    s.MediaPropertiesChanged += delegate { Queue(); };
                    s.PlaybackInfoChanged += delegate { Queue(); };
                    s.TimelinePropertiesChanged += delegate { Queue(); };
                }
                catch (Exception ex) { Bg.LogOnce("media.session.events", ex); }
            }
            var m = new MediaInfo();
            m.AppId = s.SourceAppUserModelId ?? "";
            m.AppName = AppName(m.AppId);
            var props = WinRT.Wait(s.TryGetMediaPropertiesAsync());
            if (props != null)
            {
                m.Title = props.Title ?? ""; m.Artist = props.Artist ?? ""; m.Album = props.AlbumTitle ?? "";
                if (m.Artist.Length == 0) m.Artist = props.AlbumArtist ?? "";
                string key = m.AppId + "|" + m.Title + "|" + m.Album;
                if (key != artKey)
                {
                    artKey = key;
                    artCache = null; artColor = Color.Empty;
                    if (props.Thumbnail != null)
                    {
                        try { artCache = ReadArt(props.Thumbnail, out artColor); }
                        catch (Exception ex) { Bg.LogOnce("media.art", ex); }
                    }
                }
                m.Art = artCache; m.ArtColor = artColor;
            }
            var pb = s.GetPlaybackInfo();
            if (pb != null)
            {
                m.Playing = pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                if (pb.Controls != null) { m.CanNext = pb.Controls.IsNextEnabled; m.CanPrev = pb.Controls.IsPreviousEnabled; }
            }
            try
            {
                var tl = s.GetTimelineProperties();
                if (tl != null)
                {
                    m.DurSec = (tl.EndTime - tl.StartTime).TotalSeconds;
                    m.PosSec = tl.Position.TotalSeconds;
                    DateTime upd = tl.LastUpdatedTime.UtcDateTime;
                    m.Stamp = upd > DateTime.UtcNow.AddDays(-1) ? upd : DateTime.UtcNow;
                }
            }
            catch { }
            if (m.Title.Length == 0 && m.Artist.Length == 0 && !m.Playing) m = null;
            Now = m;
            BarForm.Poke();
        }

        static Bitmap ReadArt(IRandomAccessStreamReference r, out Color dominant)
        {
            dominant = Color.Empty;
            using (IRandomAccessStreamWithContentType stream = WinRT.Wait(r.OpenReadAsync()))
            {
                if (stream == null || stream.Size == 0 || stream.Size > 16 * 1024 * 1024) return null;
                byte[] buf;
                using (var dr = new DataReader(stream.GetInputStreamAt(0)))
                {
                    uint n = WinRT.Wait(dr.LoadAsync((uint)stream.Size));
                    buf = new byte[n];
                    dr.ReadBytes(buf);
                }
                using (var ms = new MemoryStream(buf))
                using (Image img = Image.FromStream(ms))
                {
                    // Spotify & co. pad art with bars; crop to the centre square.
                    int side = Math.Min(img.Width, img.Height);
                    var src = new Rectangle((img.Width - side) / 2, (img.Height - side) / 2, side, side);
                    var bmp = new Bitmap(96, 96, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(img, new Rectangle(0, 0, 96, 96), src, GraphicsUnit.Pixel);
                    }
                    dominant = WallpaperColor.Dominant(bmp);
                    return bmp;
                }
            }
        }

        public static string AppName(string id)
        {
            string l = (id ?? "").ToLowerInvariant();
            if (l.Contains("spotify")) return "Spotify";
            if (l.Contains("chrome")) return "Chrome";
            if (l.Contains("msedge")) return "Edge";
            if (l.Contains("firefox")) return "Firefox";
            if (l.Contains("zunemusic") || l.Contains("media.player")) return "Media Player";
            if (l.Contains("brave")) return "Brave";
            if (l.Contains("opera")) return "Opera";
            if (l.Contains("vlc")) return "VLC";
            string s = id ?? "";
            int bang = s.IndexOf('!'); if (bang > 0) s = s.Substring(bang + 1);
            if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
            int dot = s.LastIndexOf('.'); if (dot > 0 && dot < s.Length - 1) s = s.Substring(dot + 1);
            return s;
        }

        // 0 = previous, 1 = play/pause, 2 = next
        public static void Command(int which)
        {
            MediaInfo m = Now;
            if (Svc.Demo) { if (m != null && which == 1) m.Playing = !m.Playing; return; }
            if (m != null && which == 1) { m.Playing = !m.Playing; m.Stamp = DateTime.UtcNow; }   // instant feedback
            Bg.Run("media.cmd", delegate
            {
                var s = cur ?? (mgr == null ? null : mgr.GetCurrentSession());
                if (s == null) return;
                if (which == 0) WinRT.Wait(s.TrySkipPreviousAsync());
                else if (which == 1) WinRT.Wait(s.TryTogglePlayPauseAsync());
                else WinRT.Wait(s.TrySkipNextAsync());
                Thread.Sleep(250);
                Refresh();
            });
        }
    }

    // ------------------------------------------------------------------
    // Weather from Open-Meteo (no key). Network calls only on pool threads.
    // ------------------------------------------------------------------
    class WeatherInfo
    {
        public string City = "", Error;
        public double Temp, Feels, Wind;
        public int Code, Humidity;
        public bool Day = true;
        public DateTime[] Days = new DateTime[0];
        public int[] Codes = new int[0];
        public double[] Max = new double[0], Min = new double[0];
        public int[] Rain = new int[0];
        public DateTime Fetched;
    }

    static class Weather
    {
        public static volatile WeatherInfo Now;
        static int busy;

        public static void Refresh() { Bg.Pool("weather", Fetch); }

        // Every 30 min when fine; every 5 min while offline or not loaded yet.
        public static bool Stale
        {
            get { WeatherInfo w = Now; return w == null || w.Error != null || (DateTime.Now - w.Fetched).TotalMinutes >= 29; }
        }

        public static void Fetch()
        {
            if (Svc.Demo) return;
            if (Interlocked.Exchange(ref busy, 1) == 1) return;
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2
                string city = (Config.WeatherCity ?? "").Trim();
                if (city.Length == 0) city = DefaultCity();
                double lat, lon; string name;
                if (!Geo(city, out lat, out lon, out name))
                {
                    var bad = new WeatherInfo(); bad.City = city; bad.Error = "City not found";
                    Now = bad; BarForm.Poke(); return;
                }
                var ci = CultureInfo.InvariantCulture;
                string url = "https://api.open-meteo.com/v1/forecast?latitude=" + lat.ToString("0.####", ci) + "&longitude=" + lon.ToString("0.####", ci) +
                    "&current=temperature_2m,apparent_temperature,weather_code,is_day,wind_speed_10m,relative_humidity_2m" +
                    "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max&timezone=auto&forecast_days=7" +
                    (Config.Fahrenheit ? "&temperature_unit=fahrenheit&wind_speed_unit=mph" : "");
                var root = Json.Parse(Http(url)) as Dictionary<string, object>;
                var w = new WeatherInfo();
                w.City = name;
                var cur = (Dictionary<string, object>)root["current"];
                w.Temp = Json.Num(cur, "temperature_2m"); w.Feels = Json.Num(cur, "apparent_temperature");
                w.Code = (int)Json.Num(cur, "weather_code"); w.Day = Json.Num(cur, "is_day") > 0.5;
                w.Wind = Json.Num(cur, "wind_speed_10m"); w.Humidity = (int)Json.Num(cur, "relative_humidity_2m");
                var daily = (Dictionary<string, object>)root["daily"];
                var times = (List<object>)daily["time"];
                int n = times.Count;
                w.Days = new DateTime[n]; w.Codes = new int[n]; w.Max = new double[n]; w.Min = new double[n]; w.Rain = new int[n];
                var codes = (List<object>)daily["weather_code"]; var mx = (List<object>)daily["temperature_2m_max"];
                var mn = (List<object>)daily["temperature_2m_min"];
                object rainObj; daily.TryGetValue("precipitation_probability_max", out rainObj);
                var rain = rainObj as List<object>;
                for (int i = 0; i < n; i++)
                {
                    DateTime.TryParse((string)times[i], ci, DateTimeStyles.None, out w.Days[i]);
                    w.Codes[i] = (int)Json.D(codes[i]); w.Max[i] = Json.D(mx[i]); w.Min[i] = Json.D(mn[i]);
                    w.Rain[i] = rain != null && i < rain.Count ? (int)Json.D(rain[i]) : -1;
                }
                w.Fetched = DateTime.Now;
                Now = w;
            }
            catch (Exception ex)
            {
                var old = Now;
                if (old == null || old.Error != null)
                {
                    var bad = new WeatherInfo(); bad.City = Config.WeatherCity; bad.Error = "Offline";
                    Now = bad;
                }
                Bg.LogOnce("weather", ex);
            }
            finally { busy = 0; BarForm.Poke(); }
        }

        static bool Geo(string city, out double lat, out double lon, out string name)
        {
            lat = lon = 0; name = city;
            string cached = Settings.Get("weather.geo", "");
            string[] parts = cached.Split('|');
            var ci = CultureInfo.InvariantCulture;
            if (parts.Length == 4 && parts[0].Equals(city, StringComparison.OrdinalIgnoreCase) &&
                double.TryParse(parts[1], NumberStyles.Float, ci, out lat) && double.TryParse(parts[2], NumberStyles.Float, ci, out lon))
            {
                name = parts[3];
                return true;
            }
            var root = Json.Parse(Http("https://geocoding-api.open-meteo.com/v1/search?count=1&language=en&format=json&name=" + Uri.EscapeDataString(city))) as Dictionary<string, object>;
            object res;
            if (root == null || !root.TryGetValue("results", out res) || !(res is List<object>) || ((List<object>)res).Count == 0) return false;
            var first = (Dictionary<string, object>)((List<object>)res)[0];
            lat = Json.Num(first, "latitude"); lon = Json.Num(first, "longitude");
            object nm; name = first.TryGetValue("name", out nm) ? (string)nm : city;
            Settings.Set("weather.geo", city + "|" + lat.ToString(ci) + "|" + lon.ToString(ci) + "|" + name);
            return true;
        }

        static string Http(string url)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 10000; req.ReadWriteTimeout = 10000;
            req.UserAgent = "StayBar/" + Program.Version;
            req.AutomaticDecompression = DecompressionMethods.GZip;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }

        [DllImport("kernel32.dll")] static extern int GetUserGeoID(int geoClass);

        // A sensible first guess from the Windows region, so the widget works out of the box.
        public static string DefaultCity()
        {
            int geo = 0;
            try { geo = GetUserGeoID(16); } catch { }
            switch (geo)
            {
                case 217: return "Madrid";
                case 241: return "Kyiv";
                case 244: return "New York";
                case 242: return "London";
                case 94: return "Berlin";
                case 84: return "Paris";
                case 203: return "Moscow";
                case 118: return "Rome";
            }
            string dn = TimeZoneInfo.Local.DisplayName;
            int close = dn.IndexOf(')');
            string rest = close >= 0 ? dn.Substring(close + 1) : dn;
            string first = rest.Split(',')[0].Trim();
            return first.Length > 0 ? first : "London";
        }

        // WMO code -> (icon kind, text)
        public static int Kind(int code)
        {
            if (code == 0) return 0;
            if (code <= 2) return 1;
            if (code == 3) return 2;
            if (code == 45 || code == 48) return 3;
            if (code >= 51 && code <= 57) return 4;
            if ((code >= 61 && code <= 67) || (code >= 80 && code <= 82)) return 5;
            if ((code >= 71 && code <= 77) || code == 85 || code == 86) return 6;
            if (code >= 95) return 7;
            return 2;
        }

        public static string Text(int code)
        {
            if (code == 0) return "Clear";
            if (code == 1) return "Mostly clear";
            if (code == 2) return "Partly cloudy";
            if (code == 3) return "Cloudy";
            if (code == 45 || code == 48) return "Fog";
            if (code >= 51 && code <= 57) return "Drizzle";
            if (code >= 61 && code <= 65) return "Rain";
            if (code == 66 || code == 67) return "Freezing rain";
            if (code >= 71 && code <= 77) return "Snow";
            if (code >= 80 && code <= 82) return "Showers";
            if (code == 85 || code == 86) return "Snow showers";
            if (code >= 95) return "Thunderstorm";
            return "Cloudy";
        }
    }

    // Minimal JSON reader: objects -> Dictionary, arrays -> List, numbers -> double.
    static class Json
    {
        public static object Parse(string s) { int i = 0; return Value(s, ref i); }

        public static double Num(Dictionary<string, object> d, string k)
        {
            object o;
            return d != null && d.TryGetValue(k, out o) ? D(o) : 0;
        }

        public static double D(object o) { return o is double ? (double)o : 0; }

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>();
                i++;
                while (true)
                {
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == '}') { i++; return d; }
                    string key = Str(s, ref i);
                    Ws(s, ref i); i++;   // ':'
                    d[key] = Value(s, ref i);
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return d; }
                    return d;
                }
            }
            if (c == '[')
            {
                var l = new List<object>();
                i++;
                while (true)
                {
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ']') { i++; return l; }
                    l.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return l; }
                    return l;
                }
            }
            if (c == '"') return Str(s, ref i);
            if (s.Length - i >= 4 && s.Substring(i, 4) == "true") { i += 4; return true; }
            if (s.Length - i >= 5 && s.Substring(i, 5) == "false") { i += 5; return false; }
            if (s.Length - i >= 4 && s.Substring(i, 4) == "null") { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            double v;
            double.TryParse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
            return v;
        }

        static string Str(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++;   // opening quote
            while (i < s.Length && s[i] != '"')
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    i++;
                    char e = s[i];
                    if (e == 'u' && i + 4 < s.Length) { sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; }
                    else if (e == 'n') sb.Append('\n');
                    else if (e == 't') sb.Append('\t');
                    else sb.Append(e);
                }
                else sb.Append(s[i]);
                i++;
            }
            i++;   // closing quote
            return sb.ToString();
        }
    }

    // ------------------------------------------------------------------
    // Screen time: which app is in front, minute by minute (UI thread tick)
    // ------------------------------------------------------------------
    static class ScreenTime
    {
        static string day;
        static Dictionary<string, double> apps = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        static double[] hours = new double[24];
        static DateTime lastSave = DateTime.UtcNow;
        static IntPtr lastFg;
        static string lastExe;
        public static double Total, Yesterday = -1;

        static string Dir { get { return Path.Combine(Program.DataDir, "screentime"); } }

        public static void Tick(IntPtr fg, bool fullscreen, double dt)
        {
            if (Svc.Demo) return;
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (day != today) { if (day != null) Save(); Load(today); }
            bool active = Native.IdleSeconds() < 120 || fullscreen || (Media.Now != null && Media.Now.Playing);
            if (!active || fg == IntPtr.Zero) return;
            if (fg != lastFg) { lastFg = fg; lastExe = Win.ExeOfWindow(fg); }
            if (string.IsNullOrEmpty(lastExe) || Win.IsOwn(fg)) return;
            double v;
            apps.TryGetValue(lastExe, out v);
            apps[lastExe] = v + dt;
            hours[DateTime.Now.Hour] += dt;
            Total += dt;
            if ((DateTime.UtcNow - lastSave).TotalSeconds > 60) Save();
        }

        public static void Forget(IntPtr h) { if (h == lastFg) lastFg = IntPtr.Zero; }

        public static float[] Hours { get { return hours.Select(h => (float)h).ToArray(); } }

        public static List<KeyValuePair<string, double>> Top(int n)
        {
            return apps.OrderByDescending(kv => kv.Value).Take(n).ToList();
        }

        public static string Format(double sec)
        {
            int m = (int)(sec / 60);
            if (m < 60) return m + "m";
            return (m / 60) + "h " + (m % 60).ToString("00") + "m";
        }

        static void Load(string today)
        {
            day = today;
            apps = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            hours = new double[24];
            Total = 0;
            ReadFile(today, apps, hours);
            Total = apps.Values.Sum();
            var y = new Dictionary<string, double>();
            ReadFile(DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd"), y, new double[24]);
            Yesterday = y.Count > 0 ? y.Values.Sum() : -1;
        }

        static void ReadFile(string d, Dictionary<string, double> into, double[] h)
        {
            string f = Path.Combine(Dir, d + ".txt");
            if (!File.Exists(f)) return;
            try
            {
                foreach (string line in File.ReadAllLines(f))
                {
                    if (line.StartsWith("hours="))
                    {
                        string[] p = line.Substring(6).Split(',');
                        for (int i = 0; i < Math.Min(24, p.Length); i++) double.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out h[i]);
                        continue;
                    }
                    int tab = line.LastIndexOf('\t');
                    double v;
                    if (tab > 0 && double.TryParse(line.Substring(tab + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                        into[line.Substring(0, tab)] = v;
                }
            }
            catch { }
        }

        public static void Save()
        {
            if (day == null || Svc.Demo) return;
            lastSave = DateTime.UtcNow;
            try
            {
                Directory.CreateDirectory(Dir);
                var lines = new List<string>();
                lines.Add("hours=" + string.Join(",", hours.Select(h => h.ToString("0", CultureInfo.InvariantCulture))));
                foreach (var kv in apps) lines.Add(kv.Key + "\t" + kv.Value.ToString("0", CultureInfo.InvariantCulture));
                File.WriteAllLines(Path.Combine(Dir, day + ".txt"), lines.ToArray());
            }
            catch { }
        }

        public static void DemoFill()
        {
            day = DateTime.Now.ToString("yyyy-MM-dd");
            apps = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            apps[@"C:\Program Files\Google\Chrome\Application\chrome.exe"] = 4100;
            apps[@"C:\Windows\explorer.exe"] = 1300;
            apps[Program.ExePath] = 900;
            hours = new double[24];
            for (int i = 8; i <= DateTime.Now.Hour; i++) hours[i] = 600 + (i * 977 % 2400);
            Total = apps.Values.Sum(); Yesterday = 7400;
        }
    }

    // ------------------------------------------------------------------
    // Clipboard history (text only; password managers are skipped)
    // ------------------------------------------------------------------
    static class Clip
    {
        public static readonly List<string> Items = new List<string>();
        static uint fmtIgnore1, fmtIgnore2;
        static bool loaded;
        const int Max = 30;

        static string FilePath { get { return Path.Combine(Program.DataDir, "clipboard.txt"); } }

        public static void Init(IntPtr hwnd)
        {
            fmtIgnore1 = Native.RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
            fmtIgnore2 = Native.RegisterClipboardFormat("Clipboard Viewer Ignore");
            Load();
            if (Config.ClipboardOn) Native.AddClipboardFormatListener(hwnd);
        }

        public static void Changed()
        {
            if (!Config.ClipboardOn) return;
            try
            {
                if (Native.IsClipboardFormatAvailable(fmtIgnore1) || Native.IsClipboardFormatAvailable(fmtIgnore2)) return;
                if (!Clipboard.ContainsText()) return;
                string t = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(t) || t.Length > 20000) return;
                Items.Remove(t);
                Items.Insert(0, t);
                while (Items.Count > Max) Items.RemoveAt(Items.Count - 1);
                Save();
                BarForm.Poke();
            }
            catch { }
        }

        public static void Copy(string t)
        {
            try { Clipboard.SetText(t); } catch { }
        }

        public static void Clear() { Items.Clear(); Save(); }

        public static void Remove(string t) { Items.Remove(t); Save(); }

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            if (!Config.ClipboardKeep || !File.Exists(FilePath)) return;
            try
            {
                foreach (string line in File.ReadAllLines(FilePath))
                    if (line.Length > 0) Items.Add(Encoding.UTF8.GetString(Convert.FromBase64String(line)));
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                if (!Config.ClipboardKeep) { if (File.Exists(FilePath)) File.Delete(FilePath); return; }
                File.WriteAllLines(FilePath, Items.Select(s => Convert.ToBase64String(Encoding.UTF8.GetBytes(s))).ToArray());
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------
    // Focus / Pomodoro timer
    // ------------------------------------------------------------------
    static class Focus
    {
        public enum Phase { Idle, Focus, Break }
        public static Phase State = Phase.Idle;
        public static bool Paused;
        static DateTime end;
        static TimeSpan left, length;
        public static int Rounds;

        public static TimeSpan Remaining
        {
            get
            {
                if (State == Phase.Idle) return TimeSpan.FromMinutes(Config.FocusMin);
                if (Paused) return left;
                TimeSpan r = end - DateTime.UtcNow;
                return r < TimeSpan.Zero ? TimeSpan.Zero : r;
            }
        }

        public static float Progress
        {
            get
            {
                if (State == Phase.Idle || length.TotalSeconds <= 0) return 0;
                return (float)Math.Max(0, Math.Min(1, 1 - Remaining.TotalSeconds / length.TotalSeconds));
            }
        }

        public static bool Running { get { return State != Phase.Idle && !Paused; } }

        public static void Toggle()
        {
            if (State == Phase.Idle) { Start(Phase.Focus); return; }
            if (Paused) { end = DateTime.UtcNow + left; Paused = false; }
            else { left = Remaining; Paused = true; }
        }

        static void Start(Phase p)
        {
            State = p; Paused = false;
            length = TimeSpan.FromMinutes(p == Phase.Focus ? Config.FocusMin : Config.BreakMin);
            end = DateTime.UtcNow + length;
        }

        public static void Reset() { State = Phase.Idle; Paused = false; }

        public static void Skip()
        {
            if (State == Phase.Focus) { Rounds++; Start(Phase.Break); }
            else Start(Phase.Focus);
        }

        // Returns a message when a phase just finished.
        public static string Tick()
        {
            if (!Running || DateTime.UtcNow < end) return null;
            if (State == Phase.Focus)
            {
                Rounds++;
                Start(Phase.Break);
                return "Focus done. Take a " + Config.BreakMin + " minute break.";
            }
            Start(Phase.Focus);
            Paused = true; left = length;
            return "Break is over. Click the timer to focus again.";
        }

        public static string Clock(TimeSpan t)
        {
            return t.TotalHours >= 1 ? ((int)t.TotalHours) + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00")
                                     : t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00");
        }

        public static void DemoFill() { Start(Phase.Focus); end = DateTime.UtcNow.AddMinutes(17.5); }
    }

    // ------------------------------------------------------------------
    // Quick note, recycle bin, disk space, keyboard layout
    // ------------------------------------------------------------------
    // One quick note: note.txt for the first note widget, note-2.txt for "note#2" and so on.
    class NoteStore
    {
        readonly string path;
        string text;

        public NoteStore(string file) { path = Path.Combine(Program.DataDir, file); }

        public string Text
        {
            get
            {
                if (text == null) { try { text = File.Exists(path) ? File.ReadAllText(path) : ""; } catch { text = ""; } }
                return text;
            }
            set
            {
                text = value ?? "";
                if (Svc.Demo) return;
                try { File.WriteAllText(path, text); } catch { }
            }
        }

        public string FirstLine
        {
            get
            {
                foreach (string l in Text.Split('\n')) { string t = l.Trim(); if (t.Length > 0) return t; }
                return "";
            }
        }
    }

    static class Note
    {
        static readonly Dictionary<string, NoteStore> stores = new Dictionary<string, NoteStore>();

        public static NoteStore For(string key)
        {
            lock (stores)
            {
                NoteStore s;
                if (!stores.TryGetValue(key, out s))
                {
                    int hash = key.IndexOf('#');
                    s = new NoteStore(hash < 0 ? "note.txt" : "note-" + key.Substring(hash + 1) + ".txt");
                    stores[key] = s;
                }
                return s;
            }
        }

        public static string Text { get { return For("note").Text; } set { For("note").Text = value; } }
        public static string FirstLine { get { return For("note").FirstLine; } }
    }

    static class Recycle
    {
        public static volatile int Items = -1;
        public static long Bytes;

        public static void Poll()
        {
            if (Svc.Demo) return;
            var info = new Native.SHQUERYRBINFO();
            info.cbSize = Marshal.SizeOf(typeof(Native.SHQUERYRBINFO));
            if (Native.SHQueryRecycleBin(null, ref info) == 0)
            {
                int n = (int)Math.Min(int.MaxValue, info.i64NumItems);
                bool changed = n != Items;
                Items = n; Bytes = info.i64Size;
                if (changed) BarForm.Poke();
            }
        }

        public static void Empty(IntPtr owner)
        {
            Bg.Run("recycle.empty", delegate { Native.SHEmptyRecycleBin(owner, null, 0); Poll(); });
        }
    }

    static class Disk
    {
        public static double FreeGb = -1, TotalGb;
        public static string Name = "C:";

        public static void Poll()
        {
            if (Svc.Demo) return;
            try
            {
                string sys = Path.GetPathRoot(Environment.SystemDirectory);
                var d = new DriveInfo(sys);
                FreeGb = d.AvailableFreeSpace / 1073741824.0;
                TotalGb = d.TotalSize / 1073741824.0;
                Name = sys.TrimEnd('\\');
                BarForm.Poke();
            }
            catch { }
        }
    }

    static class Keyboard
    {
        static IntPtr lastLayout = new IntPtr(-1);
        public static string Code = "ENG", Region = "US";

        // UI thread; cheap.
        public static bool Update(IntPtr fg)
        {
            if (Svc.Demo) return false;
            uint pid;
            uint tid = fg == IntPtr.Zero ? 0 : Native.GetWindowThreadProcessId(fg, out pid);
            IntPtr hkl = Native.GetKeyboardLayout(tid);
            if (hkl == lastLayout) return false;
            lastLayout = hkl;
            int lang = hkl.ToInt32() & 0xFFFF;
            try
            {
                var ci = new CultureInfo(lang);
                Code = ci.ThreeLetterISOLanguageName.ToUpperInvariant();
                if (Code == "SPA") Code = "ESP";
                string n = ci.Name;
                int dash = n.IndexOf('-');
                Region = dash > 0 ? n.Substring(dash + 1).ToUpperInvariant() : "";
            }
            catch { Code = "?"; Region = ""; }
            return true;
        }
    }
}
