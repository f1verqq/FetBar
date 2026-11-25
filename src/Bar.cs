// The bar itself: layout, painting, animation, mouse, drag & drop, menus, app groups.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace StayBar
{
    class AppGroup
    {
        public string Id;
        public HashSet<string> Keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> MatchNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public string LaunchPath, PinLine, Name, ExePath;
        public bool Pinned, IsFolder;
        public AppGroup Parent;
        public List<AppGroup> Members = new List<AppGroup>();
        public List<IntPtr> Windows = new List<IntPtr>();   // z-order, most recent first
        public Bitmap Icon;
        public AppGroup Of;          // "never combine": the app this single-window tile was split from
    }

    class PinInfo
    {
        public HashSet<string> Keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> MatchNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public string LaunchPath, IconSource, Name, ExePath;
    }

    class WinInfo { public string Key, Launch, IconSource, Name, ExeName, ExePath; public uint Pid; }

    enum SlotKind { Widget, App, Sep, Spacer }

    class Slot
    {
        public SlotKind Kind;
        public string Key;
        public Widget W;
        public AppGroup G;
        public Rectangle Target;   // where it should be
        public Rectangle Rect;     // where it is drawn now (animated)
        public float Alpha = 1f;
        public int Zone = -1, Index = -1;   // which zone item this belongs to (MyZones[Zone][Index])
        public string Item;        // that item ("clock", "apps", "sep", "spacer:24"...); null for edit-mode placeholders
        public bool Flex;          // a spacer that takes free room
        public bool Divider;       // dock: draw a thin divider just before this slot
        public bool Fixed;         // can't be dragged (the dock's Recycle Bin end cap)
    }

    class Anim { public float X, W, A; }

    class BarForm : Form
    {
        static readonly string[] NoNameMatch = { "explorer.exe", "rundll32.exe", "cmd.exe", "powershell.exe", "pwsh.exe",
            "wscript.exe", "java.exe", "javaw.exe", "python.exe", "pythonw.exe", "chrome_proxy.exe", "msedge_proxy.exe" };
        const int WM_HOOK_WINNUM = Native.WM_APP + 1, WM_HOOK_PEEK = Native.WM_APP + 2, WM_HOOK_LAUNCHER = Native.WM_APP + 3,
                  WM_HOOK_EDIT = Native.WM_APP + 4, WM_HOOK_PROFILE = Native.WM_APP + 5;

        public static BarForm Instance;                                  // the bar on the main screen
        public static readonly List<BarForm> All = new List<BarForm>();  // one bar per screen
        readonly bool selfTest;
        Screen screen;           // the screen this bar is on (null = the main screen)
        public string ScreenId;  // null = the main screen
        public bool Primary { get { return ScreenId == null; } }

        // Each screen uses the main layout until it gets its own.
        public List<string>[] MyZones
        {
            get { return Config.ZonesFor(ScreenId); }
            set { Config.SetZonesFor(ScreenId, value); }
        }

        public bool AtTop
        {
            get
            {
                string p;
                if (ScreenId != null && Config.ScreenPos.TryGetValue(ScreenId, out p) && (p == "top" || p == "bottom")) return p == "top";
                return Config.AtTop;
            }
        }
        public Ui U;
        public float UiScale { get { return U.Scale; } }
        IntPtr hwnd;
        public bool FullscreenApp;   // a game or video fills the screen (automations use this too)
        GlassBack glass;   // the acrylic blur, in its own window under the bar
        System.Windows.Forms.Timer cornerTimer;
        int cornerTicks; bool cornerFired;
        bool allowClose, shown = true, fullscreen, menuOpen, abRegistered, firstLayout = true;
        uint abCallback, msgTaskbarCreated, msgQuit, msgSettings, msgReload, msgLauncher, msgGallery, msgShellHook;
        int ticks, edgeTicks, iconPx;
        float reveal = 1f, curW;
        bool revealTarget = true;
        DateTime lastInside = DateTime.Now, peekUntil = DateTime.MinValue;

        List<string> pins = new List<string>();
        DateTime pinsStamp;
        public List<AppGroup> Groups = new List<AppGroup>();
        readonly List<string> unpinnedOrder = new List<string>();
        readonly Dictionary<string, PinInfo> pinCache = new Dictionary<string, PinInfo>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Bitmap> iconCache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<IntPtr, WinInfo> describeCache = new Dictionary<IntPtr, WinInfo>();

        // layout + animation
        List<Slot> slots = new List<Slot>();
        readonly Dictionary<string, Anim> anims = new Dictionary<string, Anim>();
        readonly Dictionary<string, float> hoverAnim = new Dictionary<string, float>();
        readonly Dictionary<string, DateTime> bounce = new Dictionary<string, DateTime>();
        readonly Dictionary<string, int> bounceWindows = new Dictionary<string, int>();
        readonly Dictionary<IntPtr, DateTime> attention = new Dictionary<IntPtr, DateTime>();
        string hoverKey;
        Point mouse = new Point(-1, -1);
        float magnify, magnifyX;
        DateTime lastAnimPaint;

        // drag
        Slot pressSlot, sliderSlot, dragSlot;
        Point pressPoint;
        bool dragging, dragOff;
        string folderTarget;
        DateTime folderSince;

        IntPtr lastActive, downForeground;
        System.Windows.Forms.Timer timer, animTimer, edgeTimer, hoverTimer;
        DateTime hoverSince = DateTime.Now;
        TipWindow tip;
        string tipFor, noTipKey;
        PreviewWindow preview;
        DateTime previewAway = DateTime.MinValue;
        Flyout flyout;
        string flyoutKey, flyoutClosedKey;
        DateTime flyoutClosedAt;
        SettingsForm settings;
        GalleryForm gallery;
        LauncherForm launcher;
        DibSurface surface;

        static string PinsFile { get { return Path.Combine(Program.DataDir, "pins.txt"); } }
        Rectangle ScreenB { get { return screen != null ? screen.Bounds : Screen.PrimaryScreen.Bounds; } }
        int S(float v) { return U.S(v); }
        public int BarH { get { return U.H; } }
        int MarginPx { get { return Config.Floating ? S(Config.Margin) : 0; } }
        int Gap { get { return S(Config.TileGap); } }

        public BarForm(bool selfTest) : this(selfTest, null, null) { }

        public BarForm(bool selfTest, Screen scr, string screenId)
        {
            this.selfTest = selfTest;
            screen = scr;
            ScreenId = screenId;
            if (screenId == null) { Instance = this; Config.Load(); }
            Text = screenId == null ? "StayBar" : "StayBar (another screen)";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, false);   // we double-buffer ourselves, with alpha
            float sc;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) sc = g.DpiX / 96f;
            U = Ui.Make(sc);
            U.Icon = IconFor;
            iconPx = IconRequestPx();
            LoadPins();
            if (selfTest) return;

            AllowDrop = true;
            timer = new System.Windows.Forms.Timer();
            timer.Interval = 500;
            timer.Tick += delegate { Tick(); };
            animTimer = new System.Windows.Forms.Timer();
            animTimer.Interval = 15;
            animTimer.Tick += delegate { AnimTick(); };
            edgeTimer = new System.Windows.Forms.Timer();
            edgeTimer.Interval = 40;
            edgeTimer.Tick += delegate { EdgeTick(); };
            hoverTimer = new System.Windows.Forms.Timer();
            hoverTimer.Interval = 90;
            hoverTimer.Tick += delegate { HoverTick(); };
            liveTimer = new System.Windows.Forms.Timer();
            liveTimer.Interval = 100;
            liveTimer.Tick += delegate { if (shown) Invalidate(); };
        }

        System.Windows.Forms.Timer liveTimer;   // only while a widget needs fast repaints (stopwatch, level meter)

        void UpdateLiveTimer()
        {
            int live = 0;
            foreach (Slot s in slots)
            {
                if (s.Kind != SlotKind.Widget) continue;
                int ms = s.W.RepaintMs;
                if (ms > 0 && (live == 0 || ms < live)) live = ms;
            }
            if (live > 0 && shown) { if (liveTimer.Interval != live) liveTimer.Interval = live; if (!liveTimer.Enabled) liveTimer.Start(); }
            else if (liveTimer.Enabled) liveTimer.Stop();
        }

        // The main bar runs every widget's Tick (polling, timers, alarms) for widgets on any bar.
        void TickWidgets()
        {
            var keys = new HashSet<string>(Config.AllWidgetKeys().Concat(Automations.Extra));
            if (DockEnd()) keys.Add("trash");
            foreach (string k in keys)
            {
                Widget w = Widgets.Get(k);
                if (w == null) continue;
                try { w.Tick(this); } catch (Exception ex) { Bg.LogOnce("tick." + k, ex); }
            }
        }

        // ---------- cross-thread helpers ----------

        public static void Post(Action a)
        {
            BarForm b = Instance;
            if (b == null || b.selfTest || !b.IsHandleCreated || b.IsDisposed) return;
            try { b.BeginInvoke(a); } catch { }
        }

        static int poked;
        public static void Poke()
        {
            if (Interlocked.Exchange(ref poked, 1) == 1) return;
            Post(delegate { poked = 0; foreach (BarForm b in All) if (b.shown) b.Invalidate(); });
        }

        public static void RefreshLook() { BarForm b = Instance; if (b != null && !b.selfTest) b.ApplyConfig(); }

        public static BarForm ForScreen(string id) { return All.FirstOrDefault(b => b.ScreenId == id); }

        // A bar on every screen that should have one (the main bar runs this).
        public void SyncScreens()
        {
            if (selfTest || !Primary || hwnd == IntPtr.Zero) return;
            var want = new Dictionary<string, Screen>(StringComparer.OrdinalIgnoreCase);
            if (Config.Screens != "primary")
                foreach (Screen s in Screen.AllScreens)
                {
                    if (s.Primary) continue;
                    string id = ScreenIds.Of(s);
                    if (Config.Screens == "all" || Config.ScreenList.Contains(id)) want[id] = s;
                }
            Config.ActiveScreens = new HashSet<string>(want.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (BarForm b in All.Where(x => !x.Primary).ToList())
            {
                Screen s;
                if (!want.TryGetValue(b.ScreenId, out s)) { b.allowClose = true; b.Close(); continue; }
                want.Remove(b.ScreenId);
                if (s.Bounds != b.screen.Bounds) { b.screen = s; b.ApplyLocal(); }
            }
            foreach (var kv in want)
            {
                try { new BarForm(false, kv.Value, kv.Key).Show(); }
                catch (Exception ex) { Program.Log("Bar for screen " + kv.Key + ": " + ex.Message); }
            }
        }

        // Is this window on this bar's screen? (only asked with bars on more than one screen)
        bool OnMyScreen(IntPtr h)
        {
            Rectangle sb = ScreenB;
            var center = new Native.POINT(); center.X = sb.X + sb.Width / 2; center.Y = sb.Y + sb.Height / 2;
            return Native.MonitorFromWindow(h, 2) == Native.MonitorFromPoint(center, 2);
        }

        public static void Toast(string title, string message) { Post(delegate { Instance.ShowToast(title, message); }); }

        // ---------- window setup ----------

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // Tool window = not in Alt+Tab. No-activate = clicking the bar never steals focus.
                // Layered = per-pixel alpha: everything outside the dock's rounded shape is truly transparent.
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST | Native.WS_EX_LAYERED;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            hwnd = Handle;
            try { int dpi = Native.GetDpiForWindow(hwnd); if (dpi > 0 && Math.Abs(dpi / 96f - U.Scale) > 0.01f) { U.Build(dpi / 96f); iconPx = IconRequestPx(); } } catch { }
            abCallback = Native.RegisterWindowMessage("StayBar.AppBarMessage");
            msgTaskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
            msgQuit = Native.RegisterWindowMessage("StayBar.Quit");
            msgSettings = Native.RegisterWindowMessage("StayBar.Settings");
            msgReload = Native.RegisterWindowMessage("StayBar.Reload");
            msgLauncher = Native.RegisterWindowMessage("StayBar.Launcher");
            msgGallery = Native.RegisterWindowMessage("StayBar.Gallery");
            msgShellHook = Native.RegisterWindowMessage("SHELLHOOK");
            Native.RegisterShellHookWindow(hwnd);
            if (!All.Contains(this)) All.Add(this);
            if (Primary)
            {
                Profiles.EnsurePresets();
                Automations.EnsureExamples();
                Clip.Init(hwnd);
                Hooks.Start(hwnd);
                Svc.StartAll();
                AppIndex.Ensure();
            }
            EnsureGlass();
            RefreshGroups();
            curW = TargetWidth();
            revealTarget = !Config.EdgeReveal;
            reveal = revealTarget ? 1f : 0f;
            if (Config.ReservesSpace) RegisterAppBar();
            ApplyGlass();
            Place();
            timer.Start();
            edgeTimer.Enabled = Config.EdgeReveal;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            shown = true;
            if (glass != null) Native.SetOwner(hwnd, glass.Handle);
            ReserveSpace();
            Place();
            UpdateVisibility();
            if (Primary) SyncScreens();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Only our own menu (or Windows shutting down) may close the bar.
            if (!allowClose && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; return; }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (timer != null) { timer.Stop(); animTimer.Stop(); edgeTimer.Stop(); hoverTimer.Stop(); liveTimer.Stop(); }
            if (cornerTimer != null) cornerTimer.Stop();
            if (glass != null) { Native.SetOwner(hwnd, IntPtr.Zero); try { glass.Dispose(); } catch { } }
            All.Remove(this);
            try { Native.DeregisterShellHookWindow(hwnd); } catch { }
            UnregisterAppBar();
            if (Primary)
            {
                // the main bar going away takes the others with it
                foreach (BarForm b in All.ToList()) { try { b.allowClose = true; b.Close(); } catch { } }
                Hooks.Stop();
                try { Native.RemoveClipboardFormatListener(hwnd); } catch { }
                ScreenTime.Save();
                if (Program.ExitCode == 0) Taskbar.Restore();
            }
            base.OnFormClosed(e);
        }

        // Called whenever a setting changes.
        public void ApplyConfig()
        {
            if (!Primary && Instance != null && Instance != this) { Instance.ApplyConfig(); return; }
            Config.Save();
            Hooks.Configure();
            if (hwnd != IntPtr.Zero && !selfTest)
            {
                if (Config.ClipboardOn) Native.AddClipboardFormatListener(hwnd); else Native.RemoveClipboardFormatListener(hwnd);
            }
            ApplyLocal();
            foreach (BarForm b in All.ToList()) if (b != this) b.ApplyLocal();
            SyncScreens();
            if (Config.AccentMode == "wallpaper" && !WallpaperColor.Value.HasValue) Bg.Run("wallpaper.now", Svc.WallpaperAccent);
            Bg.Kick("media"); Bg.Kick("radios"); Bg.Kick("brightness"); Bg.Kick("recycle"); Bg.Kick("disk");
        }

        // This bar only: sizes, glass, app bar, groups, position.
        void ApplyLocal()
        {
            NewLayoutPass();
            U.Build(U.Scale);
            int px = IconRequestPx();
            if (px != iconPx) { iconCache.Clear(); iconPx = px; pinCache.Clear(); }
            if (hwnd == IntPtr.Zero) return;
            if (!Config.ReservesSpace) UnregisterAppBar();
            else if (!abRegistered) RegisterAppBar();
            if (!Config.EdgeReveal) { revealTarget = true; reveal = 1f; }
            else revealTarget = KeepVisible();
            edgeTimer.Enabled = Config.EdgeReveal;
            UpdateCornerTimer();
            ApplyGlass();
            RefreshGroups();
            curW = TargetWidth();
            ReserveSpace();
            Place();
            UpdateVisibility();
            Invalidate();
            Animate();
        }

        void ApplyGlass()
        {
            if (hwnd == IntPtr.Zero) return;
            // The bar draws its own shape and border; DWM must not round or outline the window.
            Native.SetDwm(hwnd, 33, 1);
            Native.SetDwm(hwnd, 34, unchecked((int)0xFFFFFFFE));
            EnsureGlass();
            SyncGlass();
        }

        // The floating dock gets the rounded composition glass (Windows 11); the tiles bar the plain acrylic.
        void EnsureGlass()
        {
            if (selfTest || hwnd == IntPtr.Zero) return;
            bool comp = Config.IsDock && GlassBack.CanCompose;
            if (glass != null && glass.Composited == comp && !glass.Broken) return;
            // Un-own the bar first: destroying an owner window destroys the windows it owns.
            if (glass != null) { Native.SetOwner(hwnd, IntPtr.Zero); try { glass.Dispose(); } catch { } }
            glass = new GlassBack(comp && GlassBack.CanCompose);
            IntPtr gh = glass.Handle;
            if (glass.Broken) { glass.Dispose(); glass = new GlassBack(false); gh = glass.Handle; }
            Native.SetOwner(hwnd, gh);   // owned windows always stay above their owner
        }

        // ---- the dock's shape ----
        int DockPad { get { return (int)Math.Round(U.TilePad); } }  // same as the tiles' inset, on all four sides
        float DockRadius { get { return U.Radius + DockPad; } }     // concentric with the tiles

        // The visible bar inside the window (client coords): the whole window for tiles,
        // the rounded dock (at its animated width) for the floating dock.
        Rectangle ShapeRect(Size size) { return ShapeFor(size, curW); }

        Rectangle ShapeScreen(float revealAmount)
        {
            Rectangle wr = WindowRect(revealAmount);
            Rectangle s = ShapeRect(wr.Size);
            s.Offset(wr.Location);
            return s;
        }

        // Moves the glass window under the shape that is on screen right now.
        void SyncGlass()
        {
            if (glass == null || hwnd == IntPtr.Zero) return;
            bool on = Config.Blur && shown && !fullscreen && reveal > 0.001f;
            glass.Sync(ShapeScreen(reveal), Config.IsDock ? (int)Math.Round(DockRadius) : 0, on, Config.P.TintAt(Config.Darkness));
        }

        void UpdateCornerTimer()
        {
            if (selfTest) return;
            if (Config.HotCorner && cornerTimer == null)
            {
                cornerTimer = new System.Windows.Forms.Timer();
                cornerTimer.Interval = 100;
                cornerTimer.Tick += delegate { CornerTick(); };
            }
            if (cornerTimer != null) cornerTimer.Enabled = Config.HotCorner;
        }

        // Hot corner: rest the mouse in the bottom-right corner of the screen to show the desktop.
        void CornerTick()
        {
            Native.POINT p;
            if (!Native.GetCursorPos(out p)) return;
            Rectangle sb = ScreenB;
            bool at = p.X >= sb.Right - 2 && p.Y >= sb.Bottom - 2;
            if (!at) { cornerTicks = 0; cornerFired = false; return; }
            if (cornerFired || Control.MouseButtons != MouseButtons.None) return;
            if (++cornerTicks >= 3) { cornerFired = true; Win.Press(Native.VK_LWIN, 0x44); }
        }

        // ---------- app bar: reserves screen space so maximized windows stop above it ----------

        Native.APPBARDATA Abd()
        {
            var a = new Native.APPBARDATA();
            a.cbSize = Marshal.SizeOf(typeof(Native.APPBARDATA));
            a.hWnd = hwnd;
            return a;
        }

        void RegisterAppBar()
        {
            if (abRegistered || hwnd == IntPtr.Zero) return;
            var a = Abd();
            a.uCallbackMessage = abCallback;
            Native.SHAppBarMessage(Native.ABM_NEW, ref a);
            abRegistered = true;
            ReserveSpace();
        }

        void UnregisterAppBar()
        {
            if (!abRegistered) return;
            var a = Abd();
            Native.SHAppBarMessage(Native.ABM_REMOVE, ref a);
            abRegistered = false;
        }

        void ReserveSpace()
        {
            if (!abRegistered) return;
            Rectangle sb = ScreenB;
            int t = BarH + MarginPx;
            var a = Abd();
            a.uEdge = AtTop ? Native.ABE_TOP : Native.ABE_BOTTOM;
            a.rc.Left = sb.Left; a.rc.Right = sb.Right; a.rc.Top = sb.Top; a.rc.Bottom = sb.Bottom;
            if (AtTop) a.rc.Bottom = a.rc.Top + t; else a.rc.Top = a.rc.Bottom - t;
            Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref a);
            if (AtTop) a.rc.Bottom = a.rc.Top + t; else a.rc.Top = a.rc.Bottom - t;
            Native.SHAppBarMessage(Native.ABM_SETPOS, ref a);
        }

        static float Ease(float t) { return 1f - (1f - t) * (1f - t) * (1f - t); }

        Rectangle WindowRect(float revealAmount)
        {
            Rectangle sb = ScreenB;
            int h = BarH, m = MarginPx;
            int w = sb.Width;   // the dock is painted inside a full-width window, so growing never resizes it
            int x = sb.Left;
            int y = AtTop ? sb.Top + m : sb.Bottom - m - h;
            int hide = (int)Math.Round((h + m + S(4)) * (1f - Ease(revealAmount)));
            y += AtTop ? -hide : hide;
            return new Rectangle(x, y, w, h);
        }

        public Rectangle ScreenRect { get { return ShapeScreen(1f); } }

        void Place()
        {
            if (hwnd == IntPtr.Zero) return;
            Rectangle r = WindowRect(reveal);
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, r.X, r.Y, r.Width, r.Height, Native.SWP_NOACTIVATE);
            SyncGlass();
        }

        void UpdateVisibility()
        {
            if (hwnd == IntPtr.Zero) return;
            bool want = !fullscreen && reveal > 0.001f;
            if (want && !shown) { Place(); Native.ShowWindow(hwnd, Native.SW_SHOWNA); shown = true; SyncGlass(); }
            else if (!want && shown) { Native.ShowWindow(hwnd, Native.SW_HIDE); shown = false; SyncGlass(); HideTip(); HidePreview(); }
        }

        protected override void WndProc(ref Message m)
        {
            if (abCallback != 0 && m.Msg == (int)abCallback)
            {
                if (m.WParam.ToInt32() == Native.ABN_POSCHANGED) { ReserveSpace(); Place(); }
                return;
            }
            if (msgTaskbarCreated != 0 && m.Msg == (int)msgTaskbarCreated)
            {
                // Explorer restarted: hide its taskbar again and re-claim our space.
                Taskbar.Hide();
                if (abRegistered) { UnregisterAppBar(); RegisterAppBar(); }
                Native.RegisterShellHookWindow(hwnd);
                Place();
                return;
            }
            if (msgShellHook != 0 && m.Msg == (int)msgShellHook) { ShellHook(m.WParam.ToInt32(), m.LParam); return; }
            if (msgQuit != 0 && m.Msg == (int)msgQuit) { Quit(); return; }
            if (msgSettings != 0 && m.Msg == (int)msgSettings) { OpenSettings(null); return; }
            if (msgLauncher != 0 && m.Msg == (int)msgLauncher) { OpenLauncher(); return; }
            if (msgGallery != 0 && m.Msg == (int)msgGallery) { OpenGallery(); return; }
            if (msgReload != 0 && m.Msg == (int)msgReload)
            {
                if (!Primary) return;   // the main bar reloads everything
                Config.Load(); foreach (BarForm b in All) b.LoadPins(); ApplyConfig();
                return;
            }
            switch (m.Msg)
            {
                case Native.WM_MOUSEACTIVATE:
                    m.Result = new IntPtr(Native.MA_NOACTIVATE);
                    return;
                case WM_HOOK_WINNUM: ActivateIndex(m.WParam.ToInt32(), m.LParam != IntPtr.Zero); return;
                case WM_HOOK_PEEK: Peek(); return;
                case WM_HOOK_LAUNCHER: OpenLauncher(); return;
                case WM_HOOK_EDIT: ToggleEdit(); return;
                case WM_HOOK_PROFILE: Profiles.Hotkey(m.WParam.ToInt32()); return;
                case Native.WM_CLIPBOARDUPDATE: Clip.Changed(); return;
                case Native.WM_SETTINGCHANGE:
                    base.WndProc(ref m);
                    if (m.WParam.ToInt32() == 0x14 && Config.AccentMode == "wallpaper") Bg.Run("wallpaper.changed", Svc.WallpaperAccent);
                    else if (Config.AccentMode == "windows") { Config.ResolveAccent(); ApplyConfig(); }
                    return;
                case Native.WM_DPICHANGED:
                    U.Build((m.WParam.ToInt32() & 0xFFFF) / 96f);
                    iconCache.Clear(); pinCache.Clear();
                    iconPx = IconRequestPx();
                    ApplyConfig();
                    m.Result = IntPtr.Zero;
                    return;
                case Native.WM_DISPLAYCHANGE:
                    base.WndProc(ref m);
                    BeginInvoke(new Action(delegate
                    {
                        if (Primary) { SyncScreens(); Automations.ScreensChanged(); }
                        ReserveSpace(); Place();
                    }));
                    return;
                case Native.WM_ACTIVATE:
                    if (abRegistered) { var a = Abd(); Native.SHAppBarMessage(Native.ABM_ACTIVATE, ref a); }
                    break;
                case Native.WM_WINDOWPOSCHANGED:
                    if (abRegistered) { var a = Abd(); Native.SHAppBarMessage(Native.ABM_WINDOWPOSCHANGED, ref a); }
                    break;
                case Native.WM_ENDSESSION:
                    // Signing out / shutting down: leave the user's taskbar as it was.
                    if (m.WParam != IntPtr.Zero) { Program.ExitCode = 0; ScreenTime.Save(); UnregisterAppBar(); Taskbar.Restore(); }
                    break;
            }
            base.WndProc(ref m);
        }

        // Window created / destroyed / activated / flashing.
        void ShellHook(int code, IntPtr h)
        {
            int c = code & 0x7FFF;
            if (code == 0x8006)   // HSHELL_FLASH
            {
                if (Config.Pulse && h != Native.GetForegroundWindow()) { attention[h] = DateTime.UtcNow; Animate(); }
                return;
            }
            if (c == 4 || c == 0x8004) { attention.Remove(h); }   // activated
            if (c == 2) { attention.Remove(h); describeCache.Remove(h); ScreenTime.Forget(h); }
            if (c == 1 || c == 2 || c == 4 || c == 6 || c == 0x8004) SoonRefresh();
        }

        bool refreshQueued;
        public void SoonRefresh()
        {
            if (refreshQueued) return;
            refreshQueued = true;
            BeginInvoke(new Action(delegate
            {
                refreshQueued = false;
                RefreshGroups();
                if (Math.Abs(TargetWidth() - curW) > 0.5f || LayoutChanged()) Animate();
                if (shown) Invalidate();
            }));
        }

        // ---------- loops ----------

        void Tick()
        {
            NewLayoutPass();
            ticks++;
            long tickStart = Stopwatch.GetTimestamp();
            IntPtr fg = Native.GetForegroundWindow();
            if (fg != IntPtr.Zero && fg != hwnd && !Win.IsOwn(fg)) lastActive = fg;

            if (ticks % 4 == 0 && File.Exists(PinsFile) && File.GetLastWriteTimeUtc(PinsFile) != pinsStamp) LoadPins();
            RefreshGroups();
            FullscreenApp = IsFullscreen(fg);
            fullscreen = Config.HideMode != "always" && FullscreenApp;
            if (Config.HideMode == "smart") smartTouched = WindowTouchesBar();
            UpdateDim();
            if (Primary)
            {
                Taskbar.Keep(IsShellPopup(fg));
                Sys.Sample();
                if (Svc.Active("keyboard")) Keyboard.Update(fg);
                ScreenTime.Tick(fg, FullscreenApp, timer.Interval / 1000.0);
                string done = StayBar.Focus.Tick();
                if (done != null) { ShowToast("Focus timer", done); if (!Automations.Quiet) System.Media.SystemSounds.Asterisk.Play(); }
                Automations.Tick(this);
                TickWidgets();
            }
            UpdateLiveTimer();
            UpdateVisibility();
            if (Math.Abs(TargetWidth() - curW) > 0.5f || LayoutChanged()) Animate();
            if (shown && ticks % 4 == 0)
            {
                if (glass != null)
                {
                    if (Native.GetWindow(hwnd, Native.GW_OWNER) != glass.Handle) Native.SetOwner(hwnd, glass.Handle);
                    glass.KeepUnder(hwnd);
                }
                Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
            foreach (var kv in bounce.ToList())
            {
                AppGroup g = Groups.FirstOrDefault(x => x.Id == kv.Key);
                int before;
                bounceWindows.TryGetValue(kv.Key, out before);
                if (g == null || g.Windows.Count > before || (DateTime.UtcNow - kv.Value).TotalSeconds > 2.6) { bounce.Remove(kv.Key); bounceWindows.Remove(kv.Key); }
            }
            if (shown) Invalidate();
            Perf.Add("tick", tickStart);
        }

        public void Animate() { if (animTimer != null && !animTimer.Enabled) animTimer.Start(); }

        static float Approach(float cur, float target, float k)
        {
            float n = cur + (target - cur) * k;
            return Math.Abs(target - n) < 0.015f ? target : n;
        }

        void AnimTick()
        {
            NewLayoutPass();
            bool busy = false, repaint = false, moved = false;
            bool anim = Config.Animations;

            var keys = hoverAnim.Keys.ToList();
            if (hoverKey != null && !keys.Contains(hoverKey)) keys.Add(hoverKey);
            foreach (string k in keys)
            {
                float cur = HoverAmount(k), target = k == hoverKey ? 1f : 0f;
                float n = anim ? Approach(cur, target, 0.28f) : target;
                if (n == 0f && target == 0f) hoverAnim.Remove(k); else hoverAnim[k] = n;
                if (n != cur) repaint = true;
                if (n != target) busy = true;
            }

            bool overApps = hoverKey != null && hoverKey.StartsWith("app:") && !dragging && !editMode;
            float mt = overApps ? 1f : 0f;
            if (magnify != mt) { magnify = anim ? Approach(magnify, mt, 0.22f) : mt; repaint = true; if (magnify != mt) busy = true; }

            float rt = revealTarget ? 1f : 0f;
            if (reveal != rt)
            {
                reveal = anim ? Approach(reveal, rt, revealTarget ? 0.2f : 0.24f) : rt;
                moved = true;
                if (reveal != rt) busy = true;
            }

            float tw = TargetWidth();
            if (curW != tw)
            {
                curW = !anim || Math.Abs(tw - curW) < 1f ? tw : curW + (tw - curW) * 0.3f;
                moved = true;
                if (curW != tw) busy = true;
            }

            if (StepSlots(anim)) { repaint = true; busy = true; }
            if (dragging) busy = true;

            float dt = dimTarget ? 1f : 0f;
            if (dimAmount != dt) { dimAmount = anim ? Approach(dimAmount, dt, dimTarget ? 0.06f : 0.3f) : dt; repaint = true; if (dimAmount != dt) busy = true; }

            bool pulse = false;
            if (bounce.Count > 0 && anim) pulse = true;
            foreach (var kv in attention) if ((DateTime.UtcNow - kv.Value).TotalSeconds < 6) pulse = true;
            if (pulse)
            {
                busy = true;
                // Pulses only need ~30 fps; skip frames to keep CPU low.
                if ((DateTime.UtcNow - lastAnimPaint).TotalMilliseconds >= 32) repaint = true;
            }

            // Paint first, then move the glass, so the shape and its blur change in the same frame.
            if ((repaint || moved) && shown) { Invalidate(); if (moved) Update(); lastAnimPaint = DateTime.UtcNow; }
            if (moved) { Place(); UpdateVisibility(); }
            if (!busy) animTimer.Stop();
        }

        // Moves every slot a step towards its target. Returns true while anything moves.
        bool StepSlots(bool anim)
        {
            bool moving = false;
            foreach (Slot s in slots)
            {
                Anim a;
                if (!anims.TryGetValue(s.Key, out a)) continue;
                if (dragging && s == dragSlot) continue;
                float tx = s.Target.X, tw = s.Target.Width;
                if (anim)
                {
                    float nx = Math.Abs(tx - a.X) < 0.6f ? tx : a.X + (tx - a.X) * 0.3f;
                    float nw = Math.Abs(tw - a.W) < 0.6f ? tw : a.W + (tw - a.W) * 0.3f;
                    float na = a.A >= 0.99f ? 1f : Math.Min(1f, a.A + 0.12f);
                    if (nx != a.X || nw != a.W || na != a.A) moving = true;
                    a.X = nx; a.W = nw; a.A = na;
                }
                else { a.X = tx; a.W = tw; a.A = 1f; }
            }
            return moving;
        }

        bool LayoutChanged()
        {
            List<Slot> fresh = ComputeSlots(ClientSize);
            if (fresh.Count != slots.Count) return true;
            for (int i = 0; i < fresh.Count; i++)
                if (fresh[i].Key != slots[i].Key || fresh[i].Target != slots[i].Target) return true;
            return false;
        }

        // Auto-hide: watch the screen edge and slide the bar in and out.
        void EdgeTick()
        {
            if (!Config.EdgeReveal) return;
            Point c = Cursor.Position;
            Rectangle sb = ScreenB;
            bool atEdge = c.X >= sb.Left && c.X < sb.Right && (AtTop ? c.Y <= sb.Top + 1 : c.Y >= sb.Bottom - 2);
            bool keep = KeepVisible();
            bool smartFree = Config.HideMode == "smart" && !smartTouched;   // smart-hide: nothing touches the bar
            if (!revealTarget)
            {
                if (atEdge && !fullscreen && Control.MouseButtons == MouseButtons.None)
                {
                    if (++edgeTicks * edgeTimer.Interval >= Config.RevealMs) { revealTarget = true; lastInside = DateTime.Now; Animate(); }
                }
                else edgeTicks = 0;
                if (keep || smartFree) { revealTarget = true; Animate(); }
                return;
            }
            Rectangle zone = ShapeScreen(1f);
            zone.Inflate(S(10), S(10));
            if (AtTop) { zone.Height += zone.Y - sb.Top; zone.Y = sb.Top; }
            else zone.Height = sb.Bottom - zone.Y;
            if (keep || atEdge || smartFree || zone.Contains(c)) lastInside = DateTime.Now;
            else if ((DateTime.Now - lastInside).TotalMilliseconds > Config.HideMs)
            {
                revealTarget = false;
                edgeTicks = 0;
                HideTip(); HidePreview();
                Animate();
            }
        }

        // Smart-hide: does any open window overlap the bar right now?
        bool smartTouched;
        bool WindowTouchesBar()
        {
            Rectangle bar = ShapeScreen(1f);
            foreach (AppGroup g in Groups)
                foreach (IntPtr w in g.Windows)
                {
                    if (Native.IsIconic(w)) continue;
                    Native.RECT r;
                    if (!Native.GetWindowRect(w, out r)) continue;
                    if (Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom).IntersectsWith(bar)) return true;
                }
            return false;
        }

        // Dim when idle: the bar fades to dim.opacity after dim.idle seconds without the mouse on it.
        float dimAmount;
        bool dimTarget;
        DateTime lastHover = DateTime.Now;

        void UpdateDim()
        {
            if (shown && ShapeScreen(reveal).Contains(Cursor.Position)) lastHover = DateTime.Now;
            bool want = (Automations.DimNow || (Config.DimIdle > 0 && (DateTime.Now - lastHover).TotalSeconds >= Config.DimIdle)) && !KeepVisible()
                        && (DateTime.Now - lastHover).TotalSeconds >= 2;
            if (want != dimTarget) { dimTarget = want; Animate(); }
        }

        void WakeDim()
        {
            lastHover = DateTime.Now;
            if (dimTarget) { dimTarget = false; Animate(); }
        }

        bool KeepVisible()
        {
            return menuOpen || settings != null || dragging || editMode || (flyout != null && !flyout.IsDisposed) ||
                   (preview != null && preview.Showing) || DateTime.Now < peekUntil ||
                   (DateTime.Now < peekUntil.AddSeconds(30) && IsShellPopup(Native.GetForegroundWindow()));
        }

        // Win key pressed: show the auto-hidden bar for a moment.
        void Peek()
        {
            if (!Config.EdgeReveal || !Config.WinPeek) return;
            peekUntil = DateTime.Now.AddMilliseconds(1600);
            revealTarget = true;
            lastInside = DateTime.Now;
            Animate();
        }

        // Start, Search and the quick-settings flyouts: let the Windows taskbar be while they are open.
        static bool IsShellPopup(IntPtr fg)
        {
            if (fg == IntPtr.Zero) return false;
            string cls = Win.ClassOf(fg);
            if (cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") return true;
            string exe = Win.ProcessPath(Win.PidOf(fg));
            if (exe == null) return false;
            string name = Path.GetFileName(exe).ToLowerInvariant();
            return name == "startmenuexperiencehost.exe" || name == "searchhost.exe" || name == "searchapp.exe"
                || name == "shellexperiencehost.exe" || name == "shellhost.exe";
        }

        // A game or video covering the whole main screen: step out of the way.
        bool IsFullscreen(IntPtr fg)
        {
            if (fg == IntPtr.Zero || fg == hwnd || Win.IsOwn(fg)) return false;
            string cls = Win.ClassOf(fg);
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") return false;
            Native.RECT r;
            if (!Native.GetWindowRect(fg, out r)) return false;
            Rectangle sb = ScreenB;
            return r.Left <= sb.Left && r.Top <= sb.Top && r.Right >= sb.Right && r.Bottom >= sb.Bottom;
        }

        IntPtr CurrentActive()
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == hwnd || Win.IsOwn(fg)) return lastActive;
            return fg;
        }

        // ---------- pinned apps ----------

        void LoadPins()
        {
            try
            {
                if (!File.Exists(PinsFile)) ImportWindowsPins();
                pins = File.ReadAllLines(PinsFile).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToList();
                pinsStamp = File.GetLastWriteTimeUtc(PinsFile);
            }
            catch (Exception ex) { Program.Log("Pins: " + ex.Message); }
            pinCache.Clear();
        }

        // First run: copy the shortcuts pinned to the Windows taskbar.
        static void ImportWindowsPins()
        {
            string src = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
            string dst = Path.Combine(Program.DataDir, "pins");
            Directory.CreateDirectory(dst);
            var lines = new List<string>
            {
                "# StayBar pinned apps, in order. One per line:",
                "#   a .lnk shortcut, an .exe, or shell:AppsFolder\\<AppID>",
                "# Optional display name after a |   e.g.  C:\\Tools\\thing.exe|Thing",
                "# A folder:  folder:Name=<app>;;<app>;;<app>",
            };
            if (Directory.Exists(src))
            {
                var files = Directory.GetFiles(src, "*.lnk")
                    .OrderBy(f => Path.GetFileName(f).StartsWith("File Explorer", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);
                foreach (string f in files)
                {
                    string copy = Path.Combine(dst, Path.GetFileName(f));
                    try { File.Copy(f, copy, true); lines.Add(copy); } catch { lines.Add(f); }
                }
            }
            File.WriteAllLines(PinsFile, lines.ToArray());
        }

        void SavePins()
        {
            var all = File.Exists(PinsFile) ? File.ReadAllLines(PinsFile).Where(l => l.TrimStart().StartsWith("#")).ToList() : new List<string>();
            if (!all.Any(l => l.Contains("folder:"))) all.Add("# A folder:  folder:Name=<app>;;<app>;;<app>");
            all.AddRange(pins);
            File.WriteAllLines(PinsFile, all.ToArray());
            LoadPins();
            RefreshGroups();
            Animate();
            Invalidate();
        }

        PinInfo GetPinInfo(string line)
        {
            PinInfo pi;
            if (pinCache.TryGetValue(line, out pi)) return pi;
            pi = new PinInfo();
            string path = line, name = null;
            int bar = line.IndexOf('|');
            if (bar >= 0) { path = line.Substring(0, bar).Trim(); name = line.Substring(bar + 1).Trim(); }
            path = Environment.ExpandEnvironmentVariables(path);
            pi.LaunchPath = path;
            pi.IconSource = path;

            if (path.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
            {
                string aumid = path.Substring("shell:AppsFolder\\".Length);
                pi.Keys.Add("aumid:" + aumid);
                pi.Name = name ?? aumid;
            }
            else if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                string[] t = Win.ResolveLnk(path);
                if (t[0].Length > 0)
                {
                    pi.Keys.Add(t[0]); AddMatchName(pi, Path.GetFileName(t[0]));
                    if (t[0].EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) pi.ExePath = t[0];
                }
                Match m = Regex.Match(t[1], "--processStart\\s+\"?([^\"\\s]+)", RegexOptions.IgnoreCase);
                if (m.Success) AddMatchName(pi, m.Groups[1].Value);
                string aumid = Win.FileAumid(path);
                if (!string.IsNullOrEmpty(aumid)) pi.Keys.Add("aumid:" + aumid);
                // The File Explorer pin points at a shell folder, not explorer.exe; match its windows anyway.
                if ((aumid ?? "").Equals("Microsoft.Windows.Explorer", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileNameWithoutExtension(path).Equals("File Explorer", StringComparison.OrdinalIgnoreCase))
                {
                    string ex = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                    pi.Keys.Add(ex);
                    pi.ExePath = ex;
                }
                pi.Keys.Add("lnk:" + path);
                pi.Name = name ?? Path.GetFileNameWithoutExtension(path);
            }
            else
            {
                pi.Keys.Add(path);
                try { AddMatchName(pi, Path.GetFileName(path)); } catch { }
                if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) pi.ExePath = path;
                pi.Name = name ?? AppNames.Of(path);
            }
            pinCache[line] = pi;
            return pi;
        }

        static void AddMatchName(PinInfo pi, string fileName)
        {
            if (!string.IsNullOrEmpty(fileName) && !NoNameMatch.Contains(fileName.ToLowerInvariant())) pi.MatchNames.Add(fileName);
        }

        public void Pin(AppGroup g) { PinAt(g, pins.Count); }

        void PinAt(AppGroup g, int index)
        {
            string line = PinLineFor(g);
            if (line == null) return;
            if (pins.Contains(line, StringComparer.OrdinalIgnoreCase)) return;
            pins.Insert(Math.Max(0, Math.Min(pins.Count, index)), line);
            SavePins();
        }

        public void PinPath(string path, string name)
        {
            if (string.IsNullOrEmpty(path)) return;
            string line = path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(name) ? path + "|" + name : path;
            if (pins.Contains(line, StringComparer.OrdinalIgnoreCase)) return;
            pins.Add(line);
            SavePins();
        }

        static string PinLineFor(AppGroup g)
        {
            if (g.PinLine != null) return g.PinLine;
            if (string.IsNullOrEmpty(g.LaunchPath)) return null;
            return g.LaunchPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ? g.LaunchPath + "|" + g.Name : g.LaunchPath;
        }

        public void Unpin(AppGroup g)
        {
            if (g.Parent != null) { RemoveFromFolder(g.Parent, g); return; }
            pins.Remove(g.PinLine);
            SavePins();
        }

        // ---- folders: "folder:Name=line;;line" ----

        static string FolderLine(string name, IEnumerable<string> members)
        {
            return "folder:" + name.Replace("=", "-").Replace(";;", ";") + "=" + string.Join(";;", members.ToArray());
        }

        static void ParseFolder(string line, out string name, out List<string> members)
        {
            string body = line.Substring("folder:".Length);
            int eq = body.IndexOf('=');
            name = eq < 0 ? "Folder" : body.Substring(0, eq).Trim();
            members = (eq < 0 ? "" : body.Substring(eq + 1)).Split(new[] { ";;" }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        }

        void MakeFolder(AppGroup dragged, AppGroup target)
        {
            string dl = PinLineFor(dragged);
            if (dl == null) return;
            int ti = pins.IndexOf(target.PinLine);
            if (ti < 0) return;
            if (dragged.Pinned && dragged.Parent == null) pins.Remove(dragged.PinLine);
            ti = pins.IndexOf(target.PinLine);
            if (target.IsFolder)
            {
                string n; List<string> mem;
                ParseFolder(target.PinLine, out n, out mem);
                if (!mem.Contains(dl)) mem.Add(dl);
                pins[ti] = FolderLine(n, mem);
            }
            else pins[ti] = FolderLine("Folder", new[] { target.PinLine, dl });
            SavePins();
            ShowToast("Folder created", "Right-click an app inside it to rename the folder.");
        }

        public void RemoveFromFolder(AppGroup folder, AppGroup member)
        {
            int i = pins.IndexOf(folder.PinLine);
            if (i < 0) return;
            string n; List<string> mem;
            ParseFolder(folder.PinLine, out n, out mem);
            mem.Remove(member.PinLine);
            if (mem.Count <= 1)
            {
                pins.RemoveAt(i);
                pins.Insert(i, member.PinLine);
                if (mem.Count == 1) pins.Insert(i + 1, mem[0]);
            }
            else
            {
                pins[i] = FolderLine(n, mem);
                pins.Insert(i + 1, member.PinLine);
            }
            SavePins();
        }

        public void RenameFolder(AppGroup folder)
        {
            string name = InputBox.Ask("Rename folder", "Folder name:", folder.Name, U.Scale);
            if (string.IsNullOrWhiteSpace(name)) return;
            int i = pins.IndexOf(folder.PinLine);
            if (i < 0) return;
            string n; List<string> mem;
            ParseFolder(folder.PinLine, out n, out mem);
            pins[i] = FolderLine(name.Trim(), mem);
            SavePins();
        }

        // ---------- open windows -> groups ----------

        int IconRequestPx()
        {
            int px = S(Config.IconSize * 1.35f);
            return px <= 32 ? 32 : px <= 48 ? 48 : px <= 64 ? 64 : 96;
        }

        public Bitmap IconFor(string source)
        {
            if (string.IsNullOrEmpty(source)) return null;
            Bitmap b;
            if (iconCache.TryGetValue(source, out b)) return b;
            b = Win.ShellImage(source, iconPx);
            if (b == null && File.Exists(source))
            {
                try { using (Icon ic = Icon.ExtractAssociatedIcon(source)) b = ic.ToBitmap(); } catch { }
            }
            iconCache[source] = b;
            return b;
        }

        WinInfo Describe(IntPtr h)
        {
            uint pid = Win.PidOf(h);
            WinInfo w;
            if (describeCache.TryGetValue(h, out w) && w.Pid == pid) return w;
            w = new WinInfo();
            w.Pid = pid;
            string title = Win.TitleOf(h);
            string exe = Win.ProcessPath(pid);
            string lower = exe == null ? "" : exe.ToLowerInvariant();

            if (lower.EndsWith("\\applicationframehost.exe") || lower.Contains("\\windowsapps\\"))
            {
                string aumid = Win.WindowAumid(h);
                if (!string.IsNullOrEmpty(aumid))
                {
                    w.Key = "aumid:" + aumid.ToLowerInvariant();
                    w.Launch = "shell:AppsFolder\\" + aumid;
                    w.IconSource = w.Launch;
                    w.Name = title;
                    if (lower.Contains("\\windowsapps\\")) w.ExePath = exe;
                    describeCache[h] = w;
                    return w;
                }
                if (lower.EndsWith("\\applicationframehost.exe"))
                {
                    exe = Win.HostedAppPath(h);
                    lower = exe == null ? "" : exe.ToLowerInvariant();
                }
            }
            if (string.IsNullOrEmpty(exe))
            {
                w.Key = "title:" + title.ToLowerInvariant();
                w.Name = title;
                return w;   // not cached: try again next time
            }
            w.Key = lower;
            w.Launch = exe;
            w.IconSource = exe;
            w.ExeName = Path.GetFileName(exe);
            w.ExePath = exe;
            w.Name = AppNames.Of(exe);
            describeCache[h] = w;
            return w;
        }

        AppGroup PinGroup(string line)
        {
            PinInfo pi = GetPinInfo(line);
            var g = new AppGroup();
            g.Id = "pin:" + line;
            g.Pinned = true; g.PinLine = line; g.Name = pi.Name; g.LaunchPath = pi.LaunchPath; g.ExePath = pi.ExePath;
            g.Keys = pi.Keys; g.MatchNames = pi.MatchNames;
            g.Icon = IconFor(pi.IconSource);
            return g;
        }

        public void RefreshGroups()
        {
            var groups = new List<AppGroup>();
            var leaves = new List<AppGroup>();
            foreach (string line in pins)
            {
                if (line.StartsWith("folder:", StringComparison.OrdinalIgnoreCase))
                {
                    string name; List<string> members;
                    ParseFolder(line, out name, out members);
                    var f = new AppGroup();
                    f.Id = "pin:" + line; f.Pinned = true; f.IsFolder = true; f.PinLine = line; f.Name = name;
                    foreach (string ml in members)
                    {
                        AppGroup m = PinGroup(ml);
                        m.Parent = f;
                        f.Members.Add(m);
                        leaves.Add(m);
                    }
                    groups.Add(f);
                    continue;
                }
                AppGroup g = PinGroup(line);
                groups.Add(g);
                leaves.Add(g);
            }

            var others = new Dictionary<string, AppGroup>(StringComparer.OrdinalIgnoreCase);
            var firstSeen = new List<string>();
            var alive = new HashSet<IntPtr>();
            bool screenFilter = Config.ThisScreen && All.Count > 1;
            foreach (IntPtr h in Win.TaskWindows())
            {
                alive.Add(h);
                if (screenFilter && !OnMyScreen(h)) continue;   // another screen's bar shows it
                WinInfo w = Describe(h);
                AppGroup owner = leaves.FirstOrDefault(g => g.Keys.Contains(w.Key));
                if (owner == null && w.ExeName != null) owner = leaves.FirstOrDefault(g => g.MatchNames.Contains(w.ExeName));
                if (owner != null)
                {
                    if (owner.ExePath == null) owner.ExePath = w.ExePath;
                    owner.Windows.Add(h);
                    if (owner.Parent != null) owner.Parent.Windows.Add(h);
                    continue;
                }
                if (!others.TryGetValue(w.Key, out owner))
                {
                    owner = new AppGroup();
                    owner.Id = "win:" + w.Key;
                    owner.Keys.Add(w.Key);
                    owner.LaunchPath = w.Launch;
                    owner.ExePath = w.ExePath;
                    owner.Name = w.Name;
                    owner.Icon = IconFor(w.IconSource);
                    others[w.Key] = owner;
                    firstSeen.Add(w.Key);
                }
                owner.Windows.Add(h);
            }
            foreach (IntPtr h in describeCache.Keys.ToList()) if (!alive.Contains(h)) describeCache.Remove(h);
            foreach (IntPtr h in attention.Keys.ToList()) if (!alive.Contains(h)) attention.Remove(h);

            // Unpinned apps keep the position they first appeared in.
            unpinnedOrder.RemoveAll(k => !others.ContainsKey(k));
            foreach (string k in firstSeen)
                if (!unpinnedOrder.Contains(k, StringComparer.OrdinalIgnoreCase)) unpinnedOrder.Add(k);
            foreach (string k in unpinnedOrder) groups.Add(others[k]);
            Groups = ArrangeApps(groups);
            NewLayoutPass();
        }

        // ---------- the apps block: hidden apps, running only, custom names / icons, combining ----------

        // A stable key for an app: its exe path, or its AppUserModelID for Store apps.
        public static string AppKey(AppGroup g)
        {
            AppGroup a = g.Of ?? g;
            string aumid = a.Keys.FirstOrDefault(k => k.StartsWith("aumid:"));
            if (aumid != null) return aumid.ToLowerInvariant();
            if (!string.IsNullOrEmpty(a.ExePath)) return a.ExePath.ToLowerInvariant();
            return (a.Keys.FirstOrDefault() ?? a.Id).ToLowerInvariant();
        }

        readonly Dictionary<IntPtr, long> windowSeen = new Dictionary<IntPtr, long>();
        long windowSeq;

        List<AppGroup> ArrangeApps(List<AppGroup> groups)
        {
            var list = new List<AppGroup>();
            foreach (AppGroup g in groups)
            {
                if (!g.IsFolder && Config.HiddenApps.Count > 0 && Config.HiddenApps.Contains(AppKey(g))) continue;
                if (Config.RunningOnly && g.Windows.Count == 0) continue;
                if (!g.IsFolder) CustomLook(g);
                foreach (AppGroup m in g.Members) CustomLook(m);
                list.Add(g);
            }
            // windows keep the order they first appeared in, so split tiles never shuffle
            var alive = new HashSet<IntPtr>();
            foreach (AppGroup g in list) foreach (IntPtr w in g.Windows) { alive.Add(w); if (!windowSeen.ContainsKey(w)) windowSeen[w] = ++windowSeq; }
            foreach (IntPtr w in windowSeen.Keys.ToList()) if (!alive.Contains(w)) windowSeen.Remove(w);

            if (Config.Combine == "always") return list;
            int split = list.Sum(g => g.IsFolder ? 1 : Math.Max(1, g.Windows.Count));
            if (Config.Combine == "whenfull" && split * AppCellWanted() > AppsRoom()) return list;
            var res = new List<AppGroup>();
            foreach (AppGroup g in list)
            {
                if (g.IsFolder || g.Windows.Count <= 1) { res.Add(g); continue; }
                foreach (IntPtr w in g.Windows.OrderBy(x => windowSeen[x]))
                {
                    var c = new AppGroup();
                    c.Id = g.Id + "|" + w.ToInt64();
                    c.Of = g; c.Parent = g.Parent;
                    c.Keys = g.Keys; c.MatchNames = g.MatchNames;
                    c.LaunchPath = g.LaunchPath; c.PinLine = g.PinLine; c.Name = g.Name; c.ExePath = g.ExePath;
                    c.Pinned = g.Pinned; c.Icon = g.Icon;
                    c.Windows.Add(w);
                    res.Add(c);
                }
            }
            return res;
        }

        void CustomLook(AppGroup g)
        {
            string key = AppKey(g), v;
            if (Config.AppNamesCustom.TryGetValue(key, out v)) g.Name = v;
            if (Config.AppIconsCustom.TryGetValue(key, out v)) { Bitmap b = IconFor(v); if (b != null) g.Icon = b; }
        }

        // What a tile says next to its icon (labels on): the window title, or the app's name.
        static string LabelOf(AppGroup g)
        {
            if (g.IsFolder) return g.Name;
            if (g.Windows.Count == 1) { string t = Win.TitleOf(g.Windows[0]); if (!string.IsNullOrEmpty(t)) return t; }
            return g.Name;
        }

        int AppCellWanted() { return U.Cell + (Config.AppLabels ? S(Config.LabelWidth) : 0); }

        void RenameApp(AppGroup g)
        {
            string key = AppKey(g);
            string name = InputBox.Ask("Rename " + g.Name, "Name on the bar (empty = the app's own name):", g.Name, U.Scale);
            if (name == null) return;
            Config.SetAppName(key, name);
            RefreshGroups(); Animate(); Invalidate();
        }

        void ChangeAppIcon(AppGroup g)
        {
            using (var d = new OpenFileDialog())
            {
                d.Title = "Pick an icon for " + g.Name;
                d.Filter = "Icons and images|*.ico;*.png;*.jpg;*.bmp;*.exe;*.dll|All files|*.*";
                if (d.ShowDialog() != DialogResult.OK) return;
                Config.SetAppIcon(AppKey(g), d.FileName);
            }
            RefreshGroups(); Animate(); Invalidate();
        }

        void ResetAppLook(AppGroup g)
        {
            string key = AppKey(g);
            Config.SetAppName(key, null);
            Config.SetAppIcon(key, null);
            pinCache.Clear();
            RefreshGroups(); Animate(); Invalidate();
        }

        void HideApp(AppGroup g)
        {
            Config.HiddenApps.Add(AppKey(g));
            Config.Save();
            RefreshGroups(); Animate(); Invalidate();
            ShowToast("Hidden: " + g.Name, "Show it again in Settings > Apps.");
            if (settings != null && !settings.IsDisposed) settings.RefreshPage("Apps");
        }

        // ---------- layout ----------
        // The bar has three zones (left, center, right). Each zone is a list of items: widgets, the
        // apps block, spacers and separators. Spread layouts (full/percent/fixed width) put the left
        // zone at the left edge, the right zone at the right edge and the center zone in the middle;
        // "fit" (the floating dock) lines everything up and the bar hugs it.

        // The icon cell width: shrinks when there isn't room for every app.
        int Cell
        {
            get
            {
                if (cellGen == layoutGen) return cellCache;
                int c = AppCellWanted(), n = Math.Max(1, Groups.Count);
                int avail = AppsRoom();
                if (n * c > avail) c = Math.Max(S(22), avail / n);
                cellCache = c; cellGen = layoutGen;
                return c;
            }
        }

        // ---- one layout pass: widget sizes, the fit, the cell and the width are worked out once ----
        int layoutGen = 1, sizeGen = -1, fitGen = -1, cellGen = -1, targetGen = -1, cellCache;
        float targetCache;
        readonly Dictionary<Widget, int[]> sizeCache = new Dictionary<Widget, int[]>();

        void NewLayoutPass() { layoutGen++; }

        // shown (1/0), width, compact width
        int[] Sizes(Widget w)
        {
            if (sizeGen != layoutGen) { sizeCache.Clear(); sizeGen = layoutGen; }
            int[] v;
            if (!sizeCache.TryGetValue(w, out v)) { v = new[] { w.Shown(U) ? 1 : 0, w.Width(U), w.CompactWidth(U) }; sizeCache[w] = v; }
            return v;
        }

        int Side { get { return Config.Floating ? S(Config.Margin) : 0; } }       // gap to the screen edges when floating
        int MaxShapeWidth() { return Math.Max(S(120), ScreenB.Width - 2 * Side); }

        // The bar's width for full / percent / fixed widths.
        int BaseWidth()
        {
            int max = MaxShapeWidth();
            switch (Config.EffWidth)
            {
                case "percent": return Math.Max(S(120), max * Config.WidthPct / 100);
                case "fixed": return Math.Max(S(120), Math.Min(max, S(Config.WidthPx)));
                default: return max;
            }
        }

        int AppsRoom() { return (Config.EffWidth == "fit" ? MaxShapeWidth() : BaseWidth()) - FixedWidth() - S(Config.IsDock ? 24 : 0); }

        // When the bar gets crowded (say, music starts playing), these widgets shrink first,
        // so the app icons never get squeezed.
        static readonly string[] CompactOrder = { "media", "volume", "brightness", "weather", "screentime", "note", "net", "worldclock" };
        static readonly string[] NeverCrowd = { "clock", "desktop", "media", "start", "search" };
        readonly HashSet<string> compact = new HashSet<string>();
        readonly HashSet<string> crowdedOut = new HashSet<string>();

        // Width on the bar: the widget's own, shrunk when crowded, or its size option (S / M / L).
        int WidthOf(Widget w)
        {
            string size = w.Sizable ? w.SizeOpt : "m";
            int[] sz = Sizes(w);
            int full = sz[1];
            int width = compact.Contains(w.Key) || size == "s" ? sz[2] : full;
            if (size == "s" && width >= full) width = (int)Math.Round(full * 0.8f);
            if (size == "l") width = (int)Math.Round(width * 1.3f);
            return width;
        }

        void Fit()
        {
            if (fitGen == layoutGen) return;
            fitGen = layoutGen;
            compact.Clear();
            crowdedOut.Clear();
            if (!MyZones.Any(z => z.Contains("apps"))) return;   // no apps on the bar: nothing to make room for
            int n = Math.Max(1, Groups.Count), want = (int)(AppCellWanted() * 0.9f);
            Func<bool> fits = () => (Config.EffWidth == "fit" ? MaxShapeWidth() : BaseWidth()) - FixedWidth() - S(Config.IsDock ? 24 : 0) >= n * want;
            if (fits()) return;
            var keys = Config.WidgetKeysIn(MyZones).ToList();
            foreach (string id in CompactOrder)
            {
                if (fits()) return;
                foreach (string k in keys) if (Config.BaseId(k) == id) compact.Add(k);
            }
            // Still too crowded: hide widgets, the right zone first (from its left end), then center, then left.
            foreach (int z in new[] { Config.ZRight, Config.ZCenter, Config.ZLeft })
                foreach (string item in MyZones[z])
                    foreach (string k in Config.WidgetKeys(item))
                    {
                        if (fits()) return;
                        if (!NeverCrowd.Contains(Config.BaseId(k))) crowdedOut.Add(k);
                    }
        }

        // Everything except the app icons.
        int FixedWidth() { return ContentWidth(0); }

        // The width of everything lined up ("fit"), with app cells of the given width.
        int ContentWidth(int cell)
        {
            ZoneRun[] runs = LayoutZones(WithDockEnd(MyZones), Groups, cell, BarH);
            return EdgePad * 2 + ZonesWidth(runs);
        }

        int ZoneGap { get { return Config.IsDock ? S(14) : Gap; } }
        int EdgePad { get { return Config.IsDock ? DockPad : Gap; } }

        int ZonesWidth(ZoneRun[] runs)
        {
            int w = 0, n = 0;
            foreach (ZoneRun r in runs) if (r.Slots.Count > 0) { w += r.Width; n++; }
            return w + Math.Max(0, n - 1) * ZoneGap;
        }

        float TargetWidth()
        {
            if (Config.EffWidth != "fit") return BaseWidth();
            if (targetGen == layoutGen) return targetCache;
            Fit();
            targetCache = Math.Min(ContentWidth(Cell), MaxShapeWidth());
            targetGen = layoutGen;
            return targetCache;
        }

        // macOS style: the floating dock ends with a divider and the Recycle Bin (unless it's already on the bar).
        bool DockEnd() { return Config.IsDock && Config.DockTrash && !Config.WidgetKeysIn(MyZones).Any(k => Config.BaseId(k) == "trash"); }

        // The zones as shown: plus widgets an automation shows for now, plus the dock's Recycle Bin end.
        List<string>[] WithDockEnd(List<string>[] zones)
        {
            bool end = DockEnd();
            var extra = Primary ? Automations.Extra.Where(k => !Config.WidgetKeysIn(zones).Contains(k)).ToList() : new List<string>();
            if (!end && extra.Count == 0) return zones;
            var copy = zones.Select(z => z.ToList()).ToArray();
            int at = copy[Config.ZRight].FindIndex(k => k == "clock" || k == "desktop");
            copy[Config.ZRight].InsertRange(at < 0 ? copy[Config.ZRight].Count : at, extra);
            if (end) copy[Config.ZRight].Add("@end");
            return copy;
        }

        class ZoneRun { public List<Slot> Slots = new List<Slot>(); public int Width, Flex; }

        ZoneRun[] LayoutZones(List<string>[] zones, List<AppGroup> apps, int cell, int h)
        {
            var runs = new ZoneRun[3];
            for (int z = 0; z < 3; z++) runs[z] = LayoutZone(zones[z], z, apps, cell, h);
            return runs;
        }

        // One zone, from x = 0. Hidden widgets take no room; gaps only go between visible items.
        ZoneRun LayoutZone(List<string> items, int zone, List<AppGroup> apps, int cell, int h)
        {
            var run = new ZoneRun();
            int x = 0;
            bool any = false;
            for (int i = 0; i < items.Count; i++)
            {
                string item = items[i];
                var made = new List<Slot>();
                int w = 0, extra = 0;
                if (item == "apps")
                {
                    if (apps.Count == 0) continue;
                    int inset = Config.IsDock ? 0 : S(4);   // tiles: room for the tile behind the apps
                    for (int k = 0; k < apps.Count; k++) made.Add(ASlot(apps[k], inset + k * cell, cell, h));
                    w = inset * 2 + apps.Count * cell;
                }
                else if (item == "sep")
                {
                    w = Math.Max(S(2), S(14) - 2 * Gap);
                    made.Add(DecoSlot(SlotKind.Sep, w, h));
                }
                else if (item == "spacer" || item.StartsWith("spacer:"))
                {
                    int px = 0;
                    if (item.Length > 7) int.TryParse(item.Substring(7), out px);
                    w = item == "spacer" ? 0 : S(Math.Max(0, Math.Min(2000, px)));
                    Slot sp = DecoSlot(SlotKind.Spacer, w, h);
                    sp.Flex = item == "spacer";
                    if (sp.Flex) run.Flex++;
                    made.Add(sp);
                }
                else if (item == "@end")
                {
                    Widget ew = Widgets.Get("trash");
                    if (ew == null) continue;
                    w = WidthOf(ew);
                    Slot s = WSlot(ew, 0, w, h);
                    s.Fixed = true;
                    s.Divider = any;
                    if (any) extra = S(14) - Gap;   // the divider's room instead of a plain gap
                    made.Add(s);
                }
                else
                {
                    Widget wd = Widgets.Get(item);
                    if (wd == null || Sizes(wd)[0] == 0 || crowdedOut.Contains(item)) continue;
                    if (Automations.Hidden.Count > 0 && (Automations.Hidden.Contains(item) || Automations.Hidden.Contains(Config.BaseId(item)))) continue;
                    w = WidthOf(wd);
                    made.Add(WSlot(wd, 0, w, h));
                }
                if (any) x += Gap + extra;
                foreach (Slot s in made)
                {
                    s.Target = new Rectangle(s.Target.X + x, s.Target.Y, s.Target.Width, s.Target.Height);
                    s.Zone = zone; s.Index = i; s.Item = item;
                    if (s.Kind == SlotKind.Sep || s.Kind == SlotKind.Spacer) s.Key = "d:" + zone + ":" + i;
                    run.Slots.Add(s);
                }
                x += w;
                any = true;
            }
            if (run.Slots.Count == 0 && editMode)
            {
                // edit mode: an empty zone shows a drop area
                Slot ph = DecoSlot(SlotKind.Spacer, S(84), h);
                ph.Zone = zone; ph.Index = 0; ph.Key = "ph:" + zone;   // Item stays null
                run.Slots.Add(ph);
                x = S(84);
            }
            run.Width = x;
            return run;
        }

        // Where the bar is inside the window (client coords), for a given bar width.
        Rectangle ShapeFor(Size size, float width)
        {
            int side = Side;
            int avail = Math.Max(S(40), size.Width - 2 * side);
            int w = Config.EffWidth == "full" ? avail : Math.Max(S(40), Math.Min(avail, (int)Math.Round(width)));
            int x;
            if (Config.Align == "left") x = side + S(Config.OffsetX);
            else if (Config.Align == "right") x = size.Width - side - w - S(Config.OffsetX);
            else x = (size.Width - w) / 2 + S(Config.OffsetX);
            x = Math.Max(0, Math.Min(size.Width - w, x));
            return new Rectangle(x, 0, w, size.Height);
        }

        // Where everything goes (targets, before animation).
        List<Slot> ComputeSlots(Size size)
        {
            var list = new List<Slot>();
            if (size.Width <= 0) return list;
            Fit();
            List<string>[] zones = MyZones;
            List<AppGroup> apps = Groups;
            if (dragging && dragSlot != null) DragOrder(out zones, out apps);
            int h = size.Height;
            ZoneRun[] runs = LayoutZones(WithDockEnd(zones), apps, Cell, h);
            Rectangle shape = ShapeFor(size, TargetWidth());
            int padL = EdgePad, padR = EdgePad;
            // tiles: a bare item at the very right end (the show-desktop strip) sits flush with the edge
            Slot lastRight = runs[Config.ZRight].Slots.LastOrDefault();
            if (!Config.IsDock && lastRight != null && lastRight.Kind == SlotKind.Widget && lastRight.W.Bare) padR = 0;

            var start = new int[3];
            int flex = runs.Sum(r => r.Flex);
            if (Config.EffWidth == "fit" || flex > 0)
            {
                // everything in a row; flexible spacers share the free room
                int used = padL + padR + ZonesWidth(runs);
                int free = Math.Max(0, shape.Width - used);
                if (flex > 0) { GrowSpacers(runs, free, flex); used += free; }
                int x = shape.X + padL + Math.Max(0, (shape.Width - used) / 2);
                bool any = false;
                for (int z = 0; z < 3; z++)
                {
                    if (runs[z].Slots.Count == 0) continue;
                    if (any) { x += ZoneGap; if (Config.IsDock) runs[z].Slots[0].Divider = true; }
                    start[z] = x;
                    x += runs[z].Width;
                    any = true;
                }
            }
            else
            {
                start[0] = shape.X + padL;
                start[2] = shape.Right - padR - runs[2].Width;
                int leftEnd = start[0] + runs[0].Width + (runs[0].Slots.Count > 0 ? ZoneGap : 0);
                int rightStart = start[2] - (runs[2].Slots.Count > 0 ? ZoneGap : 0);
                int c = shape.X + (shape.Width - runs[1].Width) / 2;
                start[1] = Math.Max(leftEnd, Math.Min(c, rightStart - runs[1].Width));
                if (Config.IsDock)
                {
                    if (runs[1].Slots.Count > 0 && runs[0].Slots.Count > 0) runs[1].Slots[0].Divider = true;
                    if (runs[2].Slots.Count > 0 && (runs[0].Slots.Count > 0 || runs[1].Slots.Count > 0)) runs[2].Slots[0].Divider = true;
                }
            }
            for (int z = 0; z < 3; z++)
                foreach (Slot s in runs[z].Slots)
                {
                    s.Target = new Rectangle(s.Target.X + start[z], s.Target.Y, s.Target.Width, s.Target.Height);
                    list.Add(s);
                }
            return list;
        }

        // Flexible spacers share the free room; everything after a spacer in its zone moves right.
        static void GrowSpacers(ZoneRun[] runs, int free, int count)
        {
            int each = free / count, rem = free - each * count;
            foreach (ZoneRun r in runs)
            {
                int shift = 0;
                foreach (Slot s in r.Slots)
                {
                    int grow = 0;
                    if (s.Flex) { grow = each + rem; rem = 0; }
                    s.Target = new Rectangle(s.Target.X + shift, s.Target.Y, s.Target.Width + grow, s.Target.Height);
                    shift += grow;
                }
                r.Width += shift;
            }
        }

        Slot WSlot(Widget w, int x, int width, int h)
        {
            var s = new Slot();
            s.Kind = SlotKind.Widget; s.W = w; s.Key = "w:" + w.Key;
            s.Target = new Rectangle(x, 0, width, h);
            return s;
        }

        Slot ASlot(AppGroup g, int x, int cell, int h)
        {
            var s = new Slot();
            s.Kind = SlotKind.App; s.G = g; s.Key = "app:" + g.Id;
            s.Target = new Rectangle(x, 0, cell, h);
            return s;
        }

        static Slot DecoSlot(SlotKind kind, int width, int h)
        {
            var s = new Slot();
            s.Kind = kind;
            s.Target = new Rectangle(0, 0, width, h);
            return s;
        }

        // Computes target slots and folds in the animated positions.
        void UpdateSlots(Size size)
        {
            List<Slot> fresh = ComputeSlots(size);
            var keep = new HashSet<string>();
            foreach (Slot s in fresh)
            {
                keep.Add(s.Key);
                Anim a;
                if (!anims.TryGetValue(s.Key, out a))
                {
                    a = new Anim();
                    a.X = s.Target.X; a.W = s.Target.Width; a.A = firstLayout || !Config.Animations ? 1f : 0f;
                    anims[s.Key] = a;
                    if (a.A < 1f) Animate();
                }
                if (dragging && dragSlot != null && s.Key == dragSlot.Key && !dragWhole)
                {
                    a.X = mouse.X - dragSlot.Target.Width / 2f;
                    a.W = s.Target.Width;
                    a.A = 1f;
                }
                else if (dragging && dragWhole && s.Zone == dragSlot.Zone && s.Index == dragSlot.Index && s.Kind == SlotKind.App)
                {
                    // edit mode: the whole apps block follows the mouse
                    int x0 = fresh.Where(f => f.Kind == SlotKind.App).Min(f => f.Target.X);
                    int x1 = fresh.Where(f => f.Kind == SlotKind.App).Max(f => f.Target.Right);
                    a.X = mouse.X - (x1 - x0) / 2f + (s.Target.X - x0);
                    a.W = s.Target.Width;
                    a.A = 1f;
                }
                s.Rect = new Rectangle((int)Math.Round(a.X), s.Target.Y, (int)Math.Round(a.W), s.Target.Height);
                s.Alpha = a.A;
            }
            foreach (string k in anims.Keys.ToList()) if (!keep.Contains(k)) anims.Remove(k);
            slots = fresh;
            firstLayout = false;
        }

        Slot HitAt(Point p)
        {
            for (int i = slots.Count - 1; i >= 0; i--)
            {
                Slot s = slots[i];
                if (dragging && s == dragSlot) continue;
                if (dragging && dragWhole && s.Kind == SlotKind.App) continue;
                if (s.Kind == SlotKind.Sep || s.Kind == SlotKind.Spacer)
                {
                    if (!editMode || s.Item == null) continue;   // separators and spacers are only grabbed while editing
                }
                if (s.Rect.Contains(p)) return s;
            }
            return null;
        }

        float HoverAmount(string id)
        {
            float v;
            return id != null && hoverAnim.TryGetValue(id, out v) ? v : 0f;
        }

        // ---------- painting ----------

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Size sz = ClientSize;
            if (sz.Width <= 0 || sz.Height <= 0) return;
            if (surface == null || surface.Width != sz.Width || surface.Height != sz.Height)
            {
                if (surface != null) surface.Dispose();
                surface = new DibSurface(sz.Width, sz.Height);
            }
            long t0 = Stopwatch.GetTimestamp();
            using (Graphics g = Graphics.FromImage(surface.Bitmap)) PaintBar(g, sz);
            Perf.Add("paint", t0);
            surface.Present(hwnd, (byte)Math.Round(255 * (1 - dimAmount * (1 - Config.DimOpacity / 100f))));
            SyncGlass();
        }

        public void PaintBar(Graphics g, Size size)
        {
            NewLayoutPass();
            Gfx.Prep(g);
            Palette p = U.P;
            U.Accent = Config.Accent;
            if (Config.IsDock) PaintDockShape(g, size, p);
            else
            {
                // The tiles bar: the whole window, or part of it (width / alignment settings).
                Rectangle shape = ShapeRect(size);
                g.Clear(Color.Transparent);
                // alpha 1, not 0: invisible, but the layered window still catches clicks on empty bar space
                using (var bb = new SolidBrush(Config.Blur ? Color.FromArgb(1, 0, 0, 0) : p.Solid)) g.FillRectangle(bb, shape);
                if (p.Sheen)
                    using (var sheen = new LinearGradientBrush(new Rectangle(shape.X, 0, shape.Width, size.Height / 2 + 1),
                        Color.FromArgb(p.Light ? 50 : 20, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                        g.FillRectangle(sheen, shape.X, 0, shape.Width, size.Height / 2);
                // hairline on the screen-facing edge
                using (var pen = new Pen(p.Light ? Color.FromArgb(30, 0, 0, 0) : Color.FromArgb(p.Sheen ? 40 : 18, 255, 255, 255)))
                {
                    float y = AtTop ? size.Height - 1 : 0;
                    g.DrawLine(pen, shape.X, y, shape.Right, y);
                }
            }

            UpdateSlots(size);
            IntPtr active = selfTest ? IntPtr.Zero : CurrentActive();

            // tiles behind widgets
            foreach (Slot s in slots)
            {
                if (s.Kind != SlotKind.Widget || s.W.Bare || (dragging && s == dragSlot)) continue;
                Gfx.Tile(g, U.Tile(s.Rect), U.Radius, p, U.Accent, HoverAmount(s.Key), s.Alpha);
            }
            // one tile behind all the apps (tiles style)
            List<Slot> appSlots = slots.Where(s => s.Kind == SlotKind.App && !(dragging && s == dragSlot && !dragWhole)).ToList();
            if (!Config.IsDock && appSlots.Count > 0)
            {
                int x1 = appSlots.Min(s => s.Rect.X) - S(4), x2 = appSlots.Max(s => s.Rect.Right) + S(4);
                Gfx.Tile(g, U.Tile(new Rectangle(x1, 0, x2 - x1, size.Height)), U.Radius, p, U.Accent, 0, 1f);
            }
            // soft dividers: between dock zones (apps | right-hand widgets | Recycle Bin) and separator items
            foreach (Slot s in slots)
            {
                if (dragging && s == dragSlot) continue;
                if (s.Divider) PaintDivider(g, s.Rect.X - S(7), size, s.Alpha);
                if (s.Kind == SlotKind.Sep) PaintDivider(g, s.Rect.X + s.Rect.Width / 2f, size, s.Alpha);
            }

            foreach (Slot s in slots)
            {
                if (dragging && s == dragSlot) continue;
                if (s.Kind == SlotKind.Sep || s.Kind == SlotKind.Spacer) continue;
                PaintSlot(g, s, active, false);
            }
            if (editMode) PaintEdit(g, size);
            if (dragging && dragSlot != null && !dragWhole)
            {
                Slot ds = slots.FirstOrDefault(s => s.Key == dragSlot.Key) ?? dragSlot;
                if (ds.Kind == SlotKind.Sep || ds.Kind == SlotKind.Spacer)
                {
                    // edit mode: a spacer or separator being dragged
                    float w = Math.Max(S(24), ds.Target.Width);
                    var r = U.Tile(new Rectangle((int)(mouse.X - w / 2), 0, (int)w, size.Height));
                    Theme.FillRound(g, r, U.Radius, Color.FromArgb(60, U.Accent));
                    Theme.StrokeRound(g, r, U.Radius, U.Accent, 1.4f);
                }
                else PaintSlot(g, ds, active, true);
            }
        }

        void PaintDivider(Graphics g, float x, Size size, float alpha)
        {
            using (var pen = new Pen(Color.FromArgb((int)(50 * alpha), U.P.Ink), Math.Max(1f, S(1))))
                g.DrawLine(pen, x, size.Height * 0.28f, x, size.Height * 0.72f);
        }

        // The dock's own rounded shape. Outside it every pixel stays fully transparent.
        void PaintDockShape(Graphics g, Size size, Palette p)
        {
            g.Clear(Color.Transparent);
            Rectangle shape = ShapeRect(size);
            float rad = DockRadius;
            using (GraphicsPath path = Theme.Round(shape, rad))
            {
                if (Config.Blur)
                {
                    // alpha 1: invisible over the glass, but clicks on empty dock space still reach the bar
                    using (var hit = new SolidBrush(Color.FromArgb(1, 0, 0, 0))) g.FillPath(hit, path);
                    // The glass window stops 1 px inside the shape (a window region has hard edges);
                    // this rim gets the same tint, anti-aliased, so the outline is smooth.
                    using (GraphicsPath rim = Theme.Round(shape, rad))
                    using (GraphicsPath inner = Theme.Round(RectangleF.Inflate(shape, -1.5f, -1.5f), rad - 1.5f))
                    using (var tb = new SolidBrush(GlassBack.EffectiveTint(p.TintAt(Config.Darkness))))
                    {
                        rim.AddPath(inner, false);
                        rim.FillMode = FillMode.Alternate;
                        g.FillPath(tb, rim);
                    }
                }
                else using (var solid = new SolidBrush(p.Solid)) g.FillPath(solid, path);
                Region old = g.Clip;
                g.SetClip(path, CombineMode.Intersect);
                using (var sheen = new LinearGradientBrush(new Rectangle(shape.X, 0, shape.Width, size.Height / 2 + 1),
                    Color.FromArgb(p.Light ? 50 : 20, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                    g.FillRectangle(sheen, shape.X, 0, shape.Width, size.Height / 2);
                g.Clip = old;
                // the hairline DWM used to draw around the dock
                Color edge = p.Light ? Color.FromArgb(208, 212, 216) : Color.FromArgb(56, 58, 64);
                Theme.StrokeRound(g, new RectangleF(shape.X + 0.5f, shape.Y + 0.5f, shape.Width - 1f, shape.Height - 1f), rad - 0.5f, edge, 1f);
            }
        }

        void PaintSlot(Graphics g, Slot s, IntPtr active, bool floating)
        {
            Rectangle r = s.Rect;
            if (floating)
            {
                r = new Rectangle(mouse.X - s.Rect.Width / 2, s.Rect.Y, s.Rect.Width, s.Rect.Height);
                Gfx.Shadow(g, U.Tile(r), U.Radius, 120, S(6));
            }
            GraphicsState st = null;
            if (s.Alpha < 0.99f || floating)
            {
                st = g.Save();
                float k = floating ? 1.06f : 0.85f + 0.15f * s.Alpha;
                float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
                g.TranslateTransform(cx, cy);
                g.ScaleTransform(k, k);
                g.TranslateTransform(-cx, -cy);
            }
            if (s.Kind == SlotKind.Widget)
            {
                if (floating && !s.W.Bare) Gfx.Tile(g, U.Tile(r), U.Radius, U.P, U.Accent, 1f, dragOff ? 0.5f : 1f);
                Point m = mouse;
                if (!r.Contains(m) || dragging) m = new Point(-1, -1);
                s.W.Paint(g, r, U, m, HoverAmount(s.Key));
            }
            else DrawApp(g, r, s, active, floating);
            if (floating && dragOff)
            {
                var badge = new RectangleF(r.Right - S(18), r.Y + S(4), S(16), S(16));
                using (var b = new SolidBrush(Color.FromArgb(230, 239, 68, 68))) g.FillEllipse(b, badge);
                Gfx.Glyph(g, "\uE711", U.GlyphTiny, Color.White, badge);
            }
            if (st != null) g.Restore(st);
        }

        float MagnifyScale(Rectangle r)
        {
            if (magnify <= 0.001f) return 1f;
            float max = Config.Magnify ? (Config.IsDock ? 0.3f : 0.2f) : 0.06f;
            float cx = r.X + r.Width / 2f;
            float d = Math.Abs(magnifyX - cx) / Math.Max(1f, r.Width);
            float f = Config.Magnify ? (float)Math.Exp(-d * d * 1.1) : (d < 0.5f ? 1f : 0f);
            return 1f + max * f * magnify;
        }

        void DrawApp(Graphics g, Rectangle r, Slot s, IntPtr active, bool floating)
        {
            AppGroup grp = s.G;
            Palette p = U.P;
            bool isActive = active != IntPtr.Zero && grp.Windows.Contains(active);
            float hover = HoverAmount(s.Key);
            // labels on: the icon sits at the left of a wider tile, the title next to it
            Rectangle full = r;
            bool label = Config.AppLabels && r.Width >= U.Cell + S(40);
            if (label) r = new Rectangle(r.X, r.Y, U.Cell, r.Height);
            var inner = new RectangleF(full.X + S(2), r.Y + S(Config.IsDock ? 5 : 7), full.Width - S(4), r.Height - S(Config.IsDock ? 10 : 14));
            if (label)
                Gfx.TextIn(g, LabelOf(grp), U.Small, isActive ? U.Ink : U.Dim, new RectangleF(r.Right - S(4), r.Y, full.Right - r.Right - S(2), r.Height), Gfx.Left);
            if (isActive || hover > 0.01f)
                Theme.FillRound(g, inner, Math.Max(S(6), U.Radius * 0.75f), p.Light ? Color.FromArgb((isActive ? 22 : 0) + (int)(14 * hover), 0, 0, 0)
                                                                                 : Color.FromArgb((isActive ? 30 : 0) + (int)(20 * hover), 255, 255, 255));

            float scale = floating ? 1.1f : MagnifyScale(r);
            float ic = Math.Min(S(Config.IconSize) * scale, r.Height - S(8));
            if (ic > r.Width - S(4) && scale <= 1.001f) ic = r.Width - S(4);
            float lift = (scale - 1f) * S(Config.IconSize) * 0.45f;
            float bounceY = 0;
            DateTime b0;
            if (bounce.TryGetValue(grp.Id, out b0) && Config.Animations)
            {
                double t = (DateTime.UtcNow - b0).TotalSeconds;
                bounceY = (float)(Math.Abs(Math.Sin(t * Math.PI * 2.4)) * S(7) * Math.Max(0, 1 - t / 2.6));
            }
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f - S(1) - lift - bounceY;
            var ir = new RectangleF(cx - ic / 2f, cy - ic / 2f, ic, ic);

            // attention: pulsing glow, then a steady dot
            DateTime flashAt = DateTime.MinValue;
            foreach (IntPtr w in grp.Windows) { DateTime t; if (attention.TryGetValue(w, out t) && t > flashAt) flashAt = t; }
            bool wants = flashAt != DateTime.MinValue;
            if (wants)
            {
                double age = (DateTime.UtcNow - flashAt).TotalSeconds;
                float pulse = age < 6 ? (float)(0.5 + 0.5 * Math.Sin(age * Math.PI * 2)) : 0.35f;
                Color warm = Color.FromArgb(251, 146, 60);
                for (int i = 3; i >= 1; i--)
                {
                    float grow = S(2) * i * (0.6f + pulse);
                    Theme.FillRound(g, RectangleF.Inflate(inner, grow - S(2), grow - S(4)), U.Radius, Color.FromArgb((int)(22 * pulse) + 6, warm));
                }
                Theme.StrokeRound(g, inner, Math.Max(S(6), U.Radius * 0.75f), Color.FromArgb((int)(120 + 120 * pulse), warm), 1.4f);
            }

            if (grp.IsFolder) DrawFolder(g, ir, grp);
            else if (grp.Icon != null) { if ((Gfx.BenchFlags & 4) == 0) g.DrawImage(grp.Icon, ir); }
            else DrawLetterTile(g, ir, grp.Name);

            if (folderTarget == grp.Id && dragging && !editMode)
                Theme.StrokeRound(g, RectangleF.Inflate(ir, S(3), S(3)), S(8), U.Accent, 2f);

            if (grp.Windows.Count > 0 && !floating)
            {
                float ih = Math.Max(2f, S(3));
                float iy = Config.IsDock ? r.Bottom - S(4) - ih : inner.Bottom - ih - S(1);
                if (isActive)
                    Theme.FillRound(g, new RectangleF(cx - S(8), iy, S(16), ih), ih / 2f, U.Accent);
                else
                {
                    int n = Math.Min(3, grp.Windows.Count);
                    float dw = S(5), gap = S(3), total = n * dw + (n - 1) * gap;
                    for (int i = 0; i < n; i++)
                        Theme.FillRound(g, new RectangleF(cx - total / 2f + i * (dw + gap), iy, dw, ih), ih / 2f,
                            wants ? Color.FromArgb(251, 146, 60) : Color.FromArgb(170, p.Ink));
                }
            }
        }

        void DrawFolder(Graphics g, RectangleF ir, AppGroup f)
        {
            Theme.FillRound(g, ir, ir.Width * 0.26f, U.P.Light ? Color.FromArgb(40, 0, 0, 0) : Color.FromArgb(46, 255, 255, 255));
            Theme.StrokeRound(g, ir, ir.Width * 0.26f, Color.FromArgb(40, U.P.Ink), 1f);
            float pad = ir.Width * 0.14f, gap = ir.Width * 0.07f, mini = (ir.Width - 2 * pad - gap) / 2f;
            for (int i = 0; i < Math.Min(4, f.Members.Count); i++)
            {
                var mr = new RectangleF(ir.X + pad + (i % 2) * (mini + gap), ir.Y + pad + (i / 2) * (mini + gap), mini, mini);
                if (f.Members[i].Icon != null) g.DrawImage(f.Members[i].Icon, mr);
                else Theme.FillRound(g, mr, mini * 0.25f, Color.FromArgb(120, U.Accent));
            }
        }

        void DrawLetterTile(Graphics g, RectangleF r, string name)
        {
            string n = string.IsNullOrEmpty(name) ? "?" : name.Trim();
            int hash = 0;
            foreach (char ch in n) hash = hash * 31 + ch;
            Color[] colors = { Color.FromArgb(88, 101, 242), Color.FromArgb(46, 160, 67), Color.FromArgb(219, 97, 45),
                               Color.FromArgb(171, 71, 188), Color.FromArgb(0, 137, 123), Color.FromArgb(198, 40, 40) };
            Theme.FillRound(g, r, r.Width * 0.24f, colors[Math.Abs(hash % colors.Length)]);
            using (var b = new SolidBrush(Color.White))
                g.DrawString(n.Substring(0, 1).ToUpperInvariant(), Gfx.FontOf("Segoe UI Semibold", (float)Math.Round(r.Width * 0.5f)), b, r, Theme.Center);
        }

        // Preview image for --selftest (fake wallpaper behind it, since there is no real blur in a file).
        public Bitmap RenderPreview(int width)
        {
            RefreshGroups();
            curW = TargetWidth();
            int h = BarH;
            int W = Config.IsDock ? Math.Max((int)curW + S(60), S(900)) : Math.Max(width + S(60), S(900)), H = h + S(70);
            int w = Config.IsDock ? W : width;   // the dock is painted centred in a full-width layer, like on screen
            var bmp = new Bitmap(W, H, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                SelfTest.Wallpaper(g, new Rectangle(0, 0, W, H));
                var band = new Rectangle((W - w) / 2, H - h - MarginPx, w, h);
                if (Config.Blur)
                {
                    // what the glass window shows (the dock's glass is clipped 1 px inside the shape)
                    Color tint = Config.P.TintAt(Config.Darkness);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle shape = ShapeRect(band.Size);
                    shape.Offset(band.Location);
                    if (Config.IsDock) Theme.FillRound(g, RectangleF.Inflate(shape, -1, -1), DockRadius - 1, tint);
                    else using (var b = new SolidBrush(tint)) g.FillRectangle(b, shape);
                }
                using (var layer = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
                {
                    firstLayout = true; anims.Clear();
                    using (Graphics lg = Graphics.FromImage(layer)) PaintBar(lg, new Size(w, h));
                    g.DrawImage(layer, band.X, band.Y, w, h);
                }
            }
            return bmp;
        }

        // ---------- hover: tooltips and previews ----------

        void SetHover(string key)
        {
            if (key == hoverKey) return;
            hoverKey = key;
            hoverSince = DateTime.Now;
            noTipKey = null;
            if (tip != null && tip.Showing && key != tipFor) HideTip();
            if (!hoverTimer.Enabled) hoverTimer.Start();
            Animate();
        }

        void HoverTick()
        {
            if (editMode) { HideTip(); HidePreview(); hoverTimer.Stop(); return; }
            Point c = PointToClient(Cursor.Position);
            bool overBar = ShapeRect(ClientSize).Contains(c) && shown;
            bool overPreview = preview != null && preview.Showing && preview.Bounds.Contains(Cursor.Position);
            if (!overBar && !overPreview && hoverKey != null && !dragging) { hoverKey = null; Animate(); }

            Slot s = hoverKey == null ? null : slots.FirstOrDefault(x => x.Key == hoverKey);
            double ms = (DateTime.Now - hoverSince).TotalMilliseconds;
            bool quick = (tip != null && tip.Showing) || (preview != null && preview.Showing);

            // previews
            if (preview != null && preview.Showing)
            {
                bool keep = overPreview || (s != null && s.Kind == SlotKind.App && s.G == preview.Group);
                if (s != null && s.Kind == SlotKind.App && s.G != preview.Group && s.G.Windows.Count > 0 && Config.Previews && !s.G.IsFolder)
                {
                    preview.ShowFor(s.G, RectangleToScreen(s.Rect));
                    keep = true;
                }
                if (keep) previewAway = DateTime.MinValue;
                else if (previewAway == DateTime.MinValue) previewAway = DateTime.Now;
                else if ((DateTime.Now - previewAway).TotalMilliseconds > 280) HidePreview();
            }

            if (s == null || dragging || menuOpen || pressSlot != null || s.Key == noTipKey || (flyout != null && !flyout.IsDisposed))
            {
                if (!overPreview) HideTip();
                if (s == null && (preview == null || !preview.Showing)) hoverTimer.Stop();
                return;
            }
            if (ms < (quick ? 60 : 420)) return;

            if (s.Kind == SlotKind.App && s.G.Windows.Count > 0 && Config.Previews && !s.G.IsFolder)
            {
                HideTip();
                if (preview == null) preview = new PreviewWindow(this);
                if (!preview.Showing || preview.Group == null || preview.Group.Id != s.G.Id) preview.ShowFor(s.G, RectangleToScreen(s.Rect));
                return;
            }
            if (preview != null && preview.Showing) HidePreview();
            string text = TipFor(s, c);
            if (text == null) { HideTip(); return; }
            if (tip == null) tip = new TipWindow(U.Scale);
            if (tipFor != s.Key || !tip.Showing || tip.Tag as string != text)
            {
                tipFor = s.Key;
                tip.Tag = text;
                tip.ShowText(text, RectangleToScreen(s.Rect));
            }
        }

        string TipFor(Slot s, Point c)
        {
            if (s.Kind == SlotKind.Widget) return s.W.Tip(U, s.Rect, c);
            if (s.Kind != SlotKind.App) return null;
            AppGroup g = s.G;
            if (g.IsFolder) return g.Name + "\n" + string.Join(", ", g.Members.Select(m => m.Name).ToArray());
            string extra = g.Windows.Count == 1 ? Win.ShortTitle(Win.TitleOf(g.Windows[0]), 70) : g.Windows.Count > 1 ? g.Windows.Count + " windows" : "Click to open";
            return g.Name + "\n" + extra;
        }

        void HideTip()
        {
            if (tip != null && tip.Showing) tip.HideTip();
            tipFor = null;
        }

        void HidePreview()
        {
            if (preview != null) preview.HidePreview();
            previewAway = DateTime.MinValue;
        }

        // ---------- mouse ----------

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            mouse = e.Location;
            WakeDim();
            if (sliderSlot != null)
            {
                sliderSlot.W.SliderMove(U, sliderSlot.Rect, e.Location);
                Invalidate();
                return;
            }
            if (editMode && EditMouseMove(e)) return;
            if (pressSlot != null && !pressSlot.Fixed && !dragging && e.Button == MouseButtons.Left &&
                (Math.Abs(e.X - pressPoint.X) > S(6) || Math.Abs(e.Y - pressPoint.Y) > S(8)))
            {
                // some widgets drag something out (a file) instead of moving themselves
                if (!editMode && pressSlot.Kind == SlotKind.Widget && pressSlot.W.DragOut(this)) { pressSlot = null; return; }
                dragging = true;
                dragSlot = pressSlot;
                dragWhole = editMode && pressSlot.Kind == SlotKind.App;
                HideTip(); HidePreview();
                Animate();
            }
            if (dragging) { UpdateDrag(e.Location); Invalidate(); return; }

            Slot h = HitAt(e.Location);
            if (editMode) { SetHover(null); Invalidate(); return; }
            if (h != null && h.Kind == SlotKind.App) magnifyX = e.X;
            SetHover(h == null ? null : h.Key);
            if (h != null) Invalidate();   // sub-element hover (buttons, sliders) and magnify
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (dragging) return;
            mouse = new Point(-1, -1);
            hoverKey = null;
            HideTip();
            Animate();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            downForeground = Native.GetForegroundWindow();
            HideTip();
            if (editMode) { if (e.Button == MouseButtons.Left) EditMouseDown(e); return; }
            Slot h = HitAt(e.Location);
            if (e.Button != MouseButtons.Left || h == null) return;
            if (h.Kind == SlotKind.Widget && h.W.SliderAt(U, h.Rect, e.Location))
            {
                sliderSlot = h;
                h.W.SliderMove(U, h.Rect, e.Location);
                Invalidate();
                return;
            }
            pressSlot = h;
            pressPoint = e.Location;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (sliderSlot != null) { sliderSlot = null; pressSlot = null; Invalidate(); return; }
            if (dragging) { EndDrag(); return; }
            if (editMode) { EditMouseUp(e); Invalidate(); return; }
            pressSlot = null;
            Slot h = HitAt(e.Location);
            if (h != null) { noTipKey = h.Key; HideTip(); if (h.Kind == SlotKind.App) HidePreview(); }
            if (h != null && h.Kind == SlotKind.Widget)
            {
                if (e.Button == MouseButtons.Left && !(h.W is StackWidget))
                {
                    string act = h.W.Opt("click", "");
                    if (act.Length > 0) { Actions.Run(act); Invalidate(); return; }
                }
                if (e.Button == MouseButtons.Right && (!h.W.RightClickIsAction || (ModifierKeys & Keys.Shift) != 0))
                {
                    // the widget's own menu (if any), plus "Widget settings…" and friends
                    menuWidget = h.W;
                    menuShown = false;
                    if ((ModifierKeys & Keys.Shift) == 0) h.W.Click(this, h.Rect, e.Location, e.Button);
                    if (!menuShown) ShowMenu(NewMenu(false), e.Location);
                    menuWidget = null;
                    Invalidate();
                    return;
                }
                h.W.Click(this, h.Rect, e.Location, e.Button);
                Invalidate();
                return;
            }
            if (h != null && h.Kind == SlotKind.App)
            {
                if (e.Button == MouseButtons.Left) ClickGroup(h.G, true);
                else if (e.Button == MouseButtons.Middle) LaunchNew(h.G);
                else if (e.Button == MouseButtons.Right) ShowAppMenu(h.G, e.Location);
                Invalidate();
                return;
            }
            if (e.Button == MouseButtons.Right) ShowBarMenu(e.Location);
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Slot h = HitAt(e.Location);
            if (h != null && h.Kind == SlotKind.Widget && h.W.Wheel(this, h.Rect, e.Location, e.Delta)) { HideTip(); Invalidate(); }
        }

        // ---------- drag to reorder ----------

        bool OffBar(Point p)
        {
            int away = S(40);
            return AtTop ? p.Y > ClientSize.Height + away : p.Y < -away;
        }

        void UpdateDrag(Point p)
        {
            dragOff = OffBar(p) && (dragSlot.Kind != SlotKind.App || dragWhole || dragSlot.G.Pinned);
            string ft = null;
            if (dragSlot.Kind == SlotKind.App && !dragWhole && !dragSlot.G.IsFolder && !dragOff)
            {
                foreach (Slot s in slots)
                {
                    if (s.Kind != SlotKind.App || s == dragSlot || !s.G.Pinned || s.G.Parent != null) continue;
                    float cx = s.Rect.X + s.Rect.Width / 2f;
                    if (Math.Abs(p.X - cx) < s.Rect.Width * 0.22f) { ft = s.G.Id; break; }
                }
            }
            if (ft != folderTarget) { folderTarget = ft; folderSince = DateTime.Now; }

            // edit mode: hold a small widget over the middle of another one to stack them in one tile
            string st = null;
            if (editMode && !dragOff && dragSlot.Kind == SlotKind.Widget && !(dragSlot.W is StackWidget) && dragSlot.W.CanStack)
                foreach (Slot s in slots)
                {
                    if (s.Kind != SlotKind.Widget || s == dragSlot || s.Fixed || s.W is StackWidget || !s.W.CanStack) continue;
                    float cx = s.Rect.X + s.Rect.Width / 2f;
                    if (Math.Abs(p.X - cx) < s.Rect.Width * 0.2f) { st = s.W.Key; break; }
                }
            if (st != stackTarget) { stackTarget = st; stackSince = DateTime.Now; }
        }

        string stackTarget;
        DateTime stackSince;
        bool StackReady { get { return stackTarget != null && (DateTime.Now - stackSince).TotalMilliseconds > 450; } }

        bool FolderReady { get { return folderTarget != null && (DateTime.Now - folderSince).TotalMilliseconds > 450; } }

        // The zones (and app order) as they would be if the dragged item were dropped right now.
        void DragOrder(out List<string>[] zones, out List<AppGroup> apps)
        {
            zones = MyZones.Select(z => z.ToList()).ToArray();
            apps = Groups.ToList();
            if (dragSlot == null) return;
            if (dragSlot.Kind == SlotKind.App && !(editMode && dragSlot.Item == "apps" && dragWhole))
            {
                if (dragOff || FolderReady) return;
                AppGroup d = apps.FirstOrDefault(g => g.Id == dragSlot.G.Id);
                if (d == null) return;
                apps.Remove(d);
                int idx = 0;
                foreach (Slot s in slots)
                {
                    if (s.Kind != SlotKind.App || s.G.Id == d.Id) continue;
                    if (mouse.X > s.Target.X + s.Target.Width / 2f) idx++;
                }
                apps.Insert(Math.Min(idx, apps.Count), d);   // dropping an unpinned app among pins pins it
                return;
            }
            if (StackReady) return;   // about to stack: everything stays where it is
            // a widget (in edit mode: any item): take it out of its zone...
            string item = dragSlot.Item;
            int dz = dragSlot.Zone, di = dragSlot.Index;
            if (dz < 0 || dz > 2 || di < 0 || di >= zones[dz].Count || zones[dz][di] != item) return;
            zones[dz].RemoveAt(di);
            if (dragOff) return;
            // ...and put it where the mouse is
            int tz, ti;
            DropTarget(dz, di, out tz, out ti);
            zones[tz].Insert(Math.Max(0, Math.Min(ti, zones[tz].Count)), item);
        }

        // Before the first item whose middle is right of the mouse, or after the item just left of
        // the mouse when that one is closer (so dropping in the gap between two zones picks the nearer one).
        // In edit mode an empty zone can be targeted directly.
        void DropTarget(int dz, int di, out int zone, out int index)
        {
            var groups = new List<int[]>();   // zone, index (after removal), x0, x1
            foreach (Slot s in slots)
            {
                if (s.Zone < 0 || s.Item == null || s.Item.StartsWith("@")) continue;
                if (s.Zone == dz && s.Index == di) continue;
                int idx = s.Index - (s.Zone == dz && s.Index > di ? 1 : 0);
                int[] last = groups.Count > 0 ? groups[groups.Count - 1] : null;
                if (last != null && last[0] == s.Zone && last[1] == idx) { last[3] = Math.Max(last[3], s.Target.Right); continue; }
                groups.Add(new[] { s.Zone, idx, s.Target.X, s.Target.Right });
            }
            if (editMode)
            {
                // empty zones show a drop area while editing
                for (int z = 0; z < 3; z++)
                {
                    Rectangle ez = EmptyZoneRect(z);
                    if (ez.Width > 0 && mouse.X >= ez.X && mouse.X < ez.Right && !groups.Any(g => g[0] == z)) { zone = z; index = 0; return; }
                }
            }
            int[] prev = null, next = null;
            foreach (int[] g in groups)
            {
                if ((g[2] + g[3]) / 2f > mouse.X) { next = g; break; }
                prev = g;
            }
            if (prev == null && next == null) { zone = dz; index = 0; return; }
            if (next == null) { zone = prev[0]; index = prev[1] + 1; return; }
            if (prev == null || prev[0] == next[0]) { zone = next[0]; index = next[1]; return; }
            if (mouse.X - prev[3] < next[2] - mouse.X) { zone = prev[0]; index = prev[1] + 1; }
            else { zone = next[0]; index = next[1]; }
        }

        void EndDrag()
        {
            Slot d = dragSlot;
            bool off = dragOff, folder = FolderReady, whole = dragWhole;
            string ft = folderTarget;
            if (StackReady && d != null && d.Kind == SlotKind.Widget)
            {
                string target = stackTarget;
                dragging = false; dragSlot = null; pressSlot = null; dragOff = false; folderTarget = null; dragWhole = false; stackTarget = null;
                StackWith(target, d.W.Key);
                return;
            }
            stackTarget = null;
            List<string>[] zones; List<AppGroup> apps;
            DragOrder(out zones, out apps);
            dragging = false; dragSlot = null; pressSlot = null; dragOff = false; folderTarget = null; dragWhole = false;
            if (d == null) return;
            if (d.Kind != SlotKind.App || whole)
            {
                if (!zones.Select(z => string.Join(",", z.ToArray())).SequenceEqual(MyZones.Select(z => string.Join(",", z.ToArray()))))
                {
                    PushUndo();
                    MyZones = zones;
                    Config.Save();
                }
                if (off) ShowToast("Removed " + ItemName(d.Item), editMode ? "Ctrl+Z undoes it." : "Add it back any time from the widget gallery.");
                if (settings != null && !settings.IsDisposed) settings.RefreshWidgets();
                if (gallery != null && !gallery.IsDisposed) gallery.Invalidate(true);
            }
            else
            {
                AppGroup g = d.G;
                if (off && g.Pinned) Unpin(g);
                else if (folder && ft != null)
                {
                    AppGroup target = Groups.FirstOrDefault(x => x.Id == ft);
                    if (target != null && target != g) MakeFolder(g, target);
                }
                else
                {
                    // new pin order = pinned groups in their new order (+ the dragged one if it was dropped among them)
                    var newPins = new List<string>();
                    int lastPinned = -1;
                    for (int i = 0; i < apps.Count; i++) if (apps[i].Pinned && apps[i] != g) lastPinned = i;
                    for (int i = 0; i < apps.Count; i++)
                    {
                        AppGroup a = apps[i];
                        if (a == g || a.Id == g.Id)
                        {
                            if (g.Pinned || i <= lastPinned + 1) { string line = PinLineFor(g); if (line != null && !newPins.Contains(line)) newPins.Add(line); }
                            continue;
                        }
                        if (a.Pinned && !newPins.Contains(a.PinLine)) newPins.Add(a.PinLine);   // split windows share one pin
                    }
                    if (!newPins.SequenceEqual(pins)) { pins = newPins; SavePins(); }
                }
            }
            Animate();
            Invalidate();
        }

        // A readable name for a zone item.
        public static string ItemName(string item)
        {
            if (item == null) return "item";
            if (item == "apps") return "Apps";
            if (item == "sep") return "Separator";
            if (item == "spacer") return "Spacer (fills free space)";
            if (item.StartsWith("spacer:")) return "Spacer, " + item.Substring(7) + " px";
            if (item.StartsWith("stack:")) return string.Join(" + ", Config.WidgetKeys(item).Select(k => { Widget w = Widgets.Get(k); return w == null ? k : w.Name; }).ToArray());
            Widget wd = Widgets.Get(item);
            return wd == null ? item : wd.Name;
        }

        // ---------- edit mode: drag items between zones, × removes, drag a spacer's edge to resize ----------

        bool editMode, dragWhole;
        readonly List<string[]> undo = new List<string[]>();
        EditPanel editPanel;
        Slot resizeSlot, closeSlot;
        int resizeStartX, resizeStartW;

        public bool EditMode { get { return editMode; } }
        public bool EditPreview { set { editMode = value; } }   // --selftest only
        public bool CanUndo { get { return undo.Count > 0; } }

        public void ToggleEdit() { SetEdit(!editMode); }

        public void SetEdit(bool on)
        {
            if (selfTest || on == editMode) return;
            editMode = on;
            HideTip(); HidePreview();
            if (on)
            {
                revealTarget = true;
                if (editPanel == null || editPanel.IsDisposed)
                {
                    editPanel = new EditPanel(this);
                    editPanel.FormClosed += delegate { editPanel = null; if (editMode) SetEdit(false); };
                }
                editPanel.ShowAbove(ShapeScreen(1f));
            }
            else if (editPanel != null && !editPanel.IsDisposed) editPanel.Close();
            Animate();
            Invalidate();
        }

        public void PushUndo()
        {
            if (ScreenId != null) Config.OwnLayout(ScreenId);   // this screen's first edit: it gets its own layout
            undo.Add(MyZones.Select(z => string.Join(",", z.ToArray())).ToArray());
            if (undo.Count > 30) undo.RemoveAt(0);
        }

        public void Undo()
        {
            if (undo.Count == 0) return;
            string[] z = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            MyZones = z.Select(s => Config.SplitItems(s)).ToArray();
            Config.Save();
            AfterLayoutChange();
        }

        public void AfterLayoutChange()
        {
            if (settings != null && !settings.IsDisposed) settings.RefreshWidgets();
            if (gallery != null && !gallery.IsDisposed) gallery.Invalidate(true);
            if (editPanel != null && !editPanel.IsDisposed) editPanel.Invalidate();
            Animate();
            Invalidate();
        }

        public void AddItem(string item, int zone)
        {
            PushUndo();
            MyZones[zone].Add(item);
            Config.Save();
            AfterLayoutChange();
        }

        void RemoveItemAt(int zone, int index)
        {
            if (zone < 0 || zone > 2 || index < 0 || index >= MyZones[zone].Count) return;
            PushUndo();
            MyZones[zone].RemoveAt(index);
            Config.Save();
            AfterLayoutChange();
        }

        // One entry per zone item on the bar (the apps block counts once): its first slot and its area.
        List<KeyValuePair<Slot, Rectangle>> EditItems()
        {
            var list = new List<KeyValuePair<Slot, Rectangle>>();
            foreach (Slot s in slots)
            {
                if (s.Zone < 0 || s.Item == null || s.Item.StartsWith("@")) continue;
                if (list.Count > 0)
                {
                    var last = list[list.Count - 1];
                    if (last.Key.Zone == s.Zone && last.Key.Index == s.Index)
                    {
                        list[list.Count - 1] = new KeyValuePair<Slot, Rectangle>(last.Key, Rectangle.Union(last.Value, s.Rect));
                        continue;
                    }
                }
                list.Add(new KeyValuePair<Slot, Rectangle>(s, s.Rect));
            }
            return list;
        }

        // The × of an item: its top-right corner, or under a narrow item (a separator, the show-desktop strip),
        // so it never sits on top of the neighbour's ×.
        RectangleF CloseBox(Rectangle r)
        {
            float d = S(16);
            if (r.Width < S(28))
                return new RectangleF(r.X + r.Width / 2f - d / 2f, Math.Min(r.Bottom - d, r.Bottom - U.TilePad - d / 2f), d, d);
            return new RectangleF(r.Right - d + S(3), Math.Max(0, r.Y + U.TilePad - S(4)), d, d);
        }

        Rectangle EmptyZoneRect(int z)
        {
            Slot ph = slots.FirstOrDefault(s => s.Kind == SlotKind.Spacer && s.Item == null && s.Zone == z);
            return ph == null ? Rectangle.Empty : ph.Rect;
        }

        void PaintEdit(Graphics g, Size size)
        {
            Color acc = U.Accent;
            foreach (Slot ph in slots.Where(s => s.Kind == SlotKind.Spacer && s.Item == null))
            {
                RectangleF r = RectangleF.Inflate(U.Tile(ph.Rect), -S(1), -S(1));
                using (var pen = new Pen(Color.FromArgb(150, acc), 1.4f))
                {
                    pen.DashStyle = DashStyle.Dash;
                    using (GraphicsPath p = Theme.Round(r, U.Radius)) g.DrawPath(pen, p);
                }
                Gfx.TextIn(g, Config.ZoneNames[ph.Zone], U.Tiny, U.Dim, r, Gfx.Mid);
            }
            foreach (var kv in EditItems())
            {
                Slot s = kv.Key;
                if (dragging && dragSlot != null && s.Zone == dragSlot.Zone && s.Index == dragSlot.Index) continue;
                RectangleF r = RectangleF.Inflate(U.Tile(kv.Value), S(1), S(1));
                if (s.Kind == SlotKind.Widget && stackTarget != null && s.W.Key == stackTarget && dragging)
                {
                    // about to stack: a strong outline and a hint
                    Theme.FillRound(g, r, U.Radius, Color.FromArgb(StackReady ? 70 : 30, acc));
                    Theme.StrokeRound(g, RectangleF.Inflate(r, S(2), S(2)), U.Radius + S(2), acc, 2f);
                }
                if (s.Kind == SlotKind.Spacer)
                {
                    Theme.FillRound(g, r, U.Radius, Color.FromArgb(34, acc));
                    if (r.Width > S(30)) Gfx.TextIn(g, s.Flex ? "spacer" : (int)Math.Round(kv.Value.Width / U.Scale) + " px", U.Tiny, U.Dim, r, Gfx.Mid);
                    // the resize grip on the right edge
                    using (var pen = new Pen(Color.FromArgb(200, acc), Math.Max(1.5f, S(2))))
                        g.DrawLine(pen, r.Right - S(3), r.Y + r.Height * 0.3f, r.Right - S(3), r.Bottom - r.Height * 0.3f);
                }
                Theme.StrokeRound(g, r, U.Radius + S(1), Color.FromArgb(150, acc), 1.4f);
                RectangleF cb = CloseBox(kv.Value);
                bool hot = cb.Contains(mouse);
                using (var b = new SolidBrush(hot ? Color.FromArgb(250, 248, 90, 110) : Color.FromArgb(230, 239, 68, 68))) g.FillEllipse(b, cb);
                Gfx.Glyph(g, "\uE711", U.GlyphTiny, Color.White, cb);
            }
        }

        // Mouse down while editing. Returns true when handled.
        bool EditMouseDown(MouseEventArgs e)
        {
            foreach (var kv in EditItems())
                if (CloseBox(kv.Value).Contains(e.Location)) { closeSlot = kv.Key; return true; }
            Slot h = HitAt(e.Location);
            if (h == null) return false;
            if (h.Kind == SlotKind.Spacer && h.Item != null && e.X >= h.Rect.Right - S(8))
            {
                resizeSlot = h; resizeStartX = e.X; resizeStartW = h.Rect.Width;
                PushUndo();
                return true;
            }
            pressSlot = h;
            pressPoint = e.Location;
            return true;
        }

        bool EditMouseMove(MouseEventArgs e)
        {
            if (resizeSlot == null) return false;
            int px = (int)Math.Round(Math.Max(0, resizeStartW + e.X - resizeStartX) / U.Scale);
            int z = resizeSlot.Zone, i = resizeSlot.Index;
            if (z >= 0 && i >= 0 && i < MyZones[z].Count)
            {
                MyZones[z][i] = "spacer:" + px;
                Animate();
                Invalidate();
            }
            return true;
        }

        bool EditMouseUp(MouseEventArgs e)
        {
            if (resizeSlot != null) { resizeSlot = null; Config.Save(); AfterLayoutChange(); return true; }
            if (e.Button == MouseButtons.Left && pressSlot != null && pressSlot.Kind == SlotKind.Widget && pressSlot.Rect.Contains(e.Location))
            {
                // a click (no drag) on a widget while editing opens its settings
                Widget w = pressSlot.W;
                pressSlot = null;
                OpenSettings("w:" + w.Key);
                return true;
            }
            if (closeSlot != null)
            {
                Slot c = closeSlot;
                closeSlot = null;
                var item = EditItems().FirstOrDefault(kv => kv.Key.Zone == c.Zone && kv.Key.Index == c.Index);
                if (item.Key != null && CloseBox(item.Value).Contains(e.Location)) RemoveItemAt(c.Zone, c.Index);
                return true;
            }
            pressSlot = null;
            if (e.Button == MouseButtons.Right) { ShowBarMenu(e.Location); return true; }
            return true;
        }

        // ---------- drop files from Explorer ----------

        Slot dropSlot;
        DateTime dropSince;

        protected override void OnDragEnter(DragEventArgs e)
        {
            base.OnDragEnter(e);
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            base.OnDragOver(e);
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effect = DragDropEffects.None; return; }
            Point p = PointToClient(new Point(e.X, e.Y));
            mouse = p;
            Slot s = HitAt(p);
            if (s != dropSlot) { dropSlot = s; dropSince = DateTime.Now; SetHover(s == null ? null : s.Key); }
            if (s != null && s.Kind == SlotKind.App)
            {
                // hold over an open app for a moment: it comes to the front so you can drop into it
                if (s.G.Windows.Count > 0 && (DateTime.Now - dropSince).TotalMilliseconds > 600)
                {
                    IntPtr w = s.G.Windows[0];
                    if (w != Native.GetForegroundWindow()) Win.Activate(w);
                    dropSince = DateTime.MaxValue;
                }
                e.Effect = DragDropEffects.Copy;
            }
            else e.Effect = DragDropEffects.Link;
            Invalidate();
        }

        protected override void OnDragLeave(EventArgs e) { base.OnDragLeave(e); dropSlot = null; SetHover(null); }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            dropSlot = null;
            if (files == null || files.Length == 0) return;
            Point p = PointToClient(new Point(e.X, e.Y));
            Slot s = HitAt(p);
            if (s != null && s.Kind == SlotKind.App && !s.G.IsFolder)
            {
                string exe = s.G.ExePath;
                if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                {
                    Win.Launch(exe, string.Join(" ", files.Select(f => "\"" + f + "\"").ToArray()));
                    StartBounce(s.G);
                }
                return;
            }
            // dropping programs/shortcuts on the bar pins them
            foreach (string f in files)
                if (f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    string line = f;
                    if (f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        string dst = Path.Combine(Program.DataDir, "pins", Path.GetFileName(f));
                        try { Directory.CreateDirectory(Path.GetDirectoryName(dst)); File.Copy(f, dst, true); line = dst; } catch { }
                    }
                    if (!pins.Contains(line, StringComparer.OrdinalIgnoreCase)) pins.Add(line);
                }
            SavePins();
        }

        // ---------- app actions ----------

        void StartBounce(AppGroup g)
        {
            bounce[g.Id] = DateTime.UtcNow;
            bounceWindows[g.Id] = g.Windows.Count;
            Animate();
        }

        public void ClickGroup(AppGroup g, bool fromBar)
        {
            if (g.IsFolder)
            {
                Slot s = slots.FirstOrDefault(x => x.Kind == SlotKind.App && x.G.Id == g.Id);
                Rectangle r = s != null ? s.Rect : new Rectangle(ClientSize.Width / 2, 0, 1, ClientSize.Height);
                ToggleFlyout("folder:" + g.Id, r, () => new FolderFlyout(this, g));
                return;
            }
            if (g.Windows.Count == 0) { if (Win.Launch(g.LaunchPath)) StartBounce(g.Parent ?? g); return; }
            IntPtr active = fromBar ? downForeground : Native.GetForegroundWindow();
            if (active == IntPtr.Zero || active == hwnd || Win.IsOwn(active)) active = lastActive;
            int idx = g.Windows.IndexOf(active);
            if (g.Windows.Count == 1)
            {
                IntPtr w = g.Windows[0];
                if (idx == 0 && !Native.IsIconic(w)) Native.ShowWindow(w, Native.SW_MINIMIZE);
                else Win.Activate(w);
                return;
            }
            // Several windows: first click brings the latest, more clicks cycle through them.
            Win.Activate(idx >= 0 ? g.Windows[g.Windows.Count - 1] : g.Windows[0]);
        }

        void LaunchNew(AppGroup g)
        {
            if (g.IsFolder) return;
            if (Win.Launch(g.LaunchPath)) StartBounce(g);
        }

        // Win+1..9 (shift = new window)
        void ActivateIndex(int index, bool newWindow)
        {
            if (index < 0 || index >= Groups.Count) return;
            AppGroup g = Groups[index];
            if (newWindow) LaunchNew(g);
            else ClickGroup(g, false);
            if (Config.EdgeReveal) Peek();
        }

        public void ActivateByAppId(string appId)
        {
            if (string.IsNullOrEmpty(appId)) return;
            string id = appId.ToLowerInvariant();
            string simple = id;
            int bang = simple.IndexOf('!'); if (bang >= 0) simple = simple.Substring(0, bang);
            if (simple.EndsWith(".exe")) simple = simple.Substring(0, simple.Length - 4);
            foreach (AppGroup g in Groups.SelectMany(x => x.IsFolder ? x.Members : new List<AppGroup> { x }))
            {
                if (g.Windows.Count == 0) continue;
                bool match = g.Keys.Contains("aumid:" + id) || g.Keys.Any(k => k.StartsWith("aumid:" + simple));
                if (!match && g.ExePath != null)
                {
                    string exe = Path.GetFileNameWithoutExtension(g.ExePath).ToLowerInvariant();
                    match = exe == simple || simple.Contains(exe) || exe.Contains(simple);
                }
                if (match) { Win.Activate(g.Windows[0]); return; }
            }
        }

        // ---------- menus ----------

        public ContextMenuStrip NewMenu(bool checks)
        {
            var m = new ContextMenuStrip();
            m.Renderer = new DarkRenderer();
            m.ShowImageMargin = false;
            m.ShowCheckMargin = checks;
            m.BackColor = Config.P.MenuBack;
            m.ForeColor = Config.P.Ink;
            m.Font = new Font("Segoe UI", 9.5f);
            m.Padding = new Padding(2, 4, 2, 4);
            m.Opened += delegate { menuOpen = true; HideTip(); HidePreview(); Native.SetDwm(m.Handle, 33, 2); };
            m.Closed += delegate { menuOpen = false; lastInside = DateTime.Now; BeginInvoke(new Action(m.Dispose)); };
            return m;
        }

        public static ToolStripMenuItem Item(ContextMenuStrip m, string text, Action onClick)
        {
            var it = new ToolStripMenuItem(text);
            it.Padding = new Padding(4, 3, 4, 3);
            it.Click += delegate { onClick(); };
            m.Items.Add(it);
            return it;
        }

        static void Label(ContextMenuStrip m, string text)
        {
            var it = new ToolStripMenuItem(text);
            it.Enabled = false;
            it.Font = new Font("Segoe UI Semibold", 8.5f);
            m.Items.Add(it);
        }

        Widget menuWidget;
        bool menuShown;

        void AppendWidgetItems(ContextMenuStrip m, Widget w)
        {
            if (m.Items.Count > 0) m.Items.Add(new ToolStripSeparator());
            Item(m, "Widget settings…", () => OpenSettings("w:" + w.Key));
            if (w.Multi) Item(m, "Add another " + w.Name.ToLowerInvariant(), () => AddCopy(w));
            if (w is StackWidget) Item(m, "Split the stack", () => Unstack(w.Key));
            Item(m, "Remove from the bar", () => RemoveFromBar(w.Key));
        }

        // Another copy of a widget, right after the first one.
        public void AddCopy(Widget w)
        {
            string key = Widgets.NextKey(w.Id);
            PushUndo();
            foreach (List<string> z in MyZones)
            {
                int i = z.FindIndex(it => it == w.Key || Config.WidgetKeys(it).Contains(w.Key));
                if (i >= 0) { z.Insert(i + 1, key); Config.Save(); AfterLayoutChange(); OpenSettings("w:" + key); return; }
            }
            MyZones[Config.ZRight].Insert(0, key);
            Config.Save();
            AfterLayoutChange();
            OpenSettings("w:" + key);
        }

        public void RemoveFromBar(string key)
        {
            PushUndo();
            Config.RemoveWidget(MyZones, key);
            Config.Save();
            AfterLayoutChange();
        }

        public void StackWith(string key, string other)
        {
            if (key == other) return;
            PushUndo();
            Config.RemoveWidget(MyZones, other);
            foreach (List<string> z in MyZones)
            {
                int i = z.IndexOf(key);
                if (i >= 0) { z[i] = "stack:" + key + "+" + other; break; }
            }
            Config.Save();
            AfterLayoutChange();
        }

        public void Unstack(string stackKey)
        {
            PushUndo();
            foreach (List<string> z in MyZones)
            {
                int i = z.IndexOf(stackKey);
                if (i < 0) continue;
                var parts = Config.WidgetKeys(stackKey).ToList();
                z.RemoveAt(i);
                z.InsertRange(i, parts);
                break;
            }
            Config.Save();
            AfterLayoutChange();
        }

        public void ShowMenu(ContextMenuStrip m, Point p)
        {
            if (menuWidget != null) { menuShown = true; AppendWidgetItems(m, menuWidget); menuWidget = null; }
            Native.SetForegroundWindow(hwnd);   // so the menu closes when you click elsewhere
            m.Show(this, p, AtTop ? ToolStripDropDownDirection.BelowRight : ToolStripDropDownDirection.AboveRight);
        }

        void ShowAppMenu(AppGroup g, Point p)
        {
            ContextMenuStrip m = NewMenu(false);
            if (g.IsFolder)
            {
                Item(m, "Open folder", () => ClickGroup(g, false));
                Item(m, "Rename folder\u2026", () => RenameFolder(g));
                Item(m, "Ungroup", () =>
                {
                    int i = pins.IndexOf(g.PinLine);
                    if (i < 0) return;
                    pins.RemoveAt(i);
                    pins.InsertRange(i, g.Members.Select(x => x.PinLine));
                    SavePins();
                });
                ShowMenu(m, p);
                return;
            }
            // jump list: recent files + a few useful tasks
            List<string[]> recent = Recent.For(g.ExePath, 7);
            if (recent.Count > 0)
            {
                Label(m, "RECENT");
                foreach (string[] r in recent)
                {
                    string lnk = r[1];
                    Item(m, "    " + Win.ShortTitle(r[0], 48).Replace("&", "&&"), () => Win.Launch(lnk));
                }
                m.Items.Add(new ToolStripSeparator());
            }
            string exeName = g.ExePath == null ? "" : Path.GetFileName(g.ExePath).ToLowerInvariant();
            string privateArg = exeName == "chrome.exe" || exeName == "brave.exe" ? "--incognito" : exeName == "msedge.exe" ? "-inprivate" : exeName == "firefox.exe" ? "-private-window" : null;
            if (privateArg != null)
            {
                string exe = g.ExePath, arg = privateArg;
                Item(m, exeName == "msedge.exe" ? "New InPrivate window" : exeName == "firefox.exe" ? "New private window" : "New incognito window", () => { Win.Launch(exe, arg); StartBounce(g); });
            }
            if (exeName == "explorer.exe")
            {
                Item(m, "Downloads", () => Win.Launch("shell:Downloads"));
                Item(m, "Documents", () => Win.Launch("shell:Personal"));
            }
            if (g.Windows.Count > 1)
            {
                m.Items.Add(new ToolStripSeparator());
                foreach (IntPtr w in g.Windows)
                {
                    IntPtr win = w;
                    Item(m, Win.ShortTitle(Win.TitleOf(win), 60).Replace("&", "&&"), () => Win.Activate(win));
                }
            }
            if (m.Items.Count > 0 && !(m.Items[m.Items.Count - 1] is ToolStripSeparator)) m.Items.Add(new ToolStripSeparator());
            if (!string.IsNullOrEmpty(g.LaunchPath))
                Item(m, g.Windows.Count > 0 ? "Open new window" : "Open " + g.Name.Replace("&", "&&"), () => LaunchNew(g));
            if (g.Pinned) Item(m, g.Parent != null ? "Move out of folder" : "Unpin from bar", () => Unpin(g.Of ?? g));
            else if (!string.IsNullOrEmpty(g.LaunchPath)) Item(m, "Pin to bar", () => Pin(g.Of ?? g));
            var look = new ToolStripMenuItem("Name and icon");
            look.DropDown.BackColor = Config.P.MenuBack;
            foreach (var pair in new[] { new KeyValuePair<string, Action>("Rename…", () => RenameApp(g)),
                                          new KeyValuePair<string, Action>("Change icon…", () => ChangeAppIcon(g)),
                                          new KeyValuePair<string, Action>("Reset name and icon", () => ResetAppLook(g)),
                                          new KeyValuePair<string, Action>("Hide from the bar", () => HideApp(g)) })
            {
                Action act = pair.Value;
                var it = new ToolStripMenuItem(pair.Key);
                it.Click += delegate { act(); };
                look.DropDownItems.Add(it);
            }
            m.Items.Add(look);
            if (g.Windows.Count > 0)
            {
                var wins = g.Windows.ToList();
                Item(m, wins.Count > 1 ? "Close all windows" : "Close window", () =>
                {
                    foreach (IntPtr w in wins) Native.PostMessage(w, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                });
            }
            ShowMenu(m, p);
        }

        void ShowBarMenu(Point p)
        {
            ContextMenuStrip m = NewMenu(true);
            Item(m, "Settings\u2026", () => OpenSettings(null)).Font = new Font("Segoe UI Semibold", 9.5f);
            Item(m, "Widget gallery\u2026", OpenGallery);
            Item(m, "Search apps\u2026  (Alt+Space)", OpenLauncher);
            m.Items.Add(new ToolStripSeparator());
            Item(m, editMode ? "Done editing" : "Edit the bar\u2026  (Win+Alt+E)", ToggleEdit).Checked = editMode;
            if (CanUndo) Item(m, "Undo layout change", Undo);
            var hide = new ToolStripMenuItem("Hide the bar");
            foreach (string[] hm in new[] { new[] { "always", "Never (always visible)" }, new[] { "fullscreen", "Only for fullscreen apps" },
                                             new[] { "smart", "When a window touches it" }, new[] { "auto", "Auto-hide" } })
            {
                string mode = hm[0];
                var it = new ToolStripMenuItem(hm[1]);
                it.Checked = Config.HideMode == mode;
                it.Click += delegate { Config.HideMode = mode; ApplyConfig(); };
                hide.DropDownItems.Add(it);
            }
            hide.DropDown.BackColor = Config.P.MenuBack;
            m.Items.Add(hide);
            Item(m, "Floating dock", () => { Config.Style = Config.IsDock ? "tiles" : "dock"; ApplyConfig(); }).Checked = Config.IsDock;
            var prof = new ToolStripMenuItem("Profile");
            string cur = Profiles.Current;
            foreach (string pn in Profiles.Names())
            {
                string name = pn;
                var it = new ToolStripMenuItem(name);
                it.Checked = name.Equals(cur, StringComparison.OrdinalIgnoreCase);
                it.Click += delegate { Profiles.Apply(name, true); };
                prof.DropDownItems.Add(it);
            }
            if (prof.DropDownItems.Count > 0) prof.DropDownItems.Add(new ToolStripSeparator());
            var saveAs = new ToolStripMenuItem("Save the current bar as a profile…");
            saveAs.Click += delegate
            {
                string n = InputBox.Ask("Save profile", "Profile name:", cur.Length > 0 ? cur : "My profile", U.Scale);
                if (!string.IsNullOrWhiteSpace(n)) { Profiles.SaveAs(n); ShowToast("Saved profile " + Profiles.Clean(n), "Switch with Win+Alt+P or from this menu."); }
            };
            prof.DropDownItems.Add(saveAs);
            var manage = new ToolStripMenuItem("Profiles and automations…");
            manage.Click += delegate { OpenSettings("Profiles"); };
            prof.DropDownItems.Add(manage);
            prof.DropDown.BackColor = Config.P.MenuBack;
            m.Items.Add(prof);
            var themes = new ToolStripMenuItem("Theme");
            foreach (Palette pal in Palette.All)
            {
                Palette pp = pal;
                var it = new ToolStripMenuItem(pal.Name);
                it.Checked = Config.ThemeId == pal.Id;
                it.Click += delegate { Config.ThemeId = pp.Id; ApplyConfig(); };
                themes.DropDownItems.Add(it);
            }
            themes.DropDown.BackColor = Config.P.MenuBack;
            m.Items.Add(themes);
            m.Items.Add(new ToolStripSeparator());
            Item(m, "Task Manager", () => Win.Launch("taskmgr.exe"));
            Item(m, "Edit pinned apps\u2026", EditPins);
            m.Items.Add(new ToolStripSeparator());
            Item(m, "Restart StayBar", Restart);
            Item(m, "Quit (bring back the Windows taskbar)", Quit);
            ShowMenu(m, p);
        }

        public void EditPins() { try { Process.Start("notepad.exe", "\"" + PinsFile + "\""); } catch { } }

        // ---------- windows we own ----------

        public void ToggleFlyout(string key, Rectangle slotRect, Func<Flyout> make)
        {
            if (flyout != null && !flyout.IsDisposed)
            {
                bool same = flyoutKey == key;
                flyout.Close();
                if (same) return;
            }
            if (flyoutClosedKey == key && (DateTime.Now - flyoutClosedAt).TotalMilliseconds < 300) return;
            HideTip(); HidePreview();
            Flyout f = make();
            flyout = f; flyoutKey = key;
            f.Key = key;
            f.FormClosed += delegate
            {
                if (flyout == f) { flyout = null; flyoutKey = null; }
                flyoutClosedKey = key; flyoutClosedAt = DateTime.Now; lastInside = DateTime.Now;
                Invalidate();
            };
            f.ShowNear(RectangleToScreen(slotRect));
            revealTarget = true;
            Animate();
        }

        public void ShowToast(string title, string message)
        {
            if (selfTest) return;
            var t = new ToastWindow(U.Scale, title, message);
            t.ShowAbove(ShapeScreen(1f));
        }

        public void OpenLauncher()
        {
            if (!Primary && Instance != null) { Instance.OpenLauncher(); return; }
            if (launcher != null && !launcher.IsDisposed) { launcher.Close(); return; }
            launcher = new LauncherForm(this);
            launcher.FormClosed += delegate { launcher = null; };
            launcher.ShowLauncher();
        }

        public void OpenSettings(string page)
        {
            if (!Primary && Instance != null) { Instance.OpenSettings(page); return; }
            if (settings != null && !settings.IsDisposed)
            {
                if (page != null) settings.ShowPage(page);
                settings.Activate();
                Win.ForceForeground(settings.Handle);
                return;
            }
            settings = new SettingsForm(this, page);
            settings.FormClosed += delegate { settings = null; lastInside = DateTime.Now; };
            settings.Show();
            Win.ForceForeground(settings.Handle);
            revealTarget = true;
            Animate();
        }

        public void OpenGallery()
        {
            if (!Primary && Instance != null) { Instance.OpenGallery(); return; }
            if (gallery != null && !gallery.IsDisposed) { gallery.Activate(); Win.ForceForeground(gallery.Handle); return; }
            gallery = new GalleryForm(this);
            gallery.FormClosed += delegate { gallery = null; lastInside = DateTime.Now; };
            gallery.Show();
            Win.ForceForeground(gallery.Handle);
        }

        public void ResetSettings()
        {
            Config.Reset();
            ApplyConfig();
            if (settings != null) { settings.Close(); BeginInvoke(new Action(delegate { OpenSettings(null); })); }
        }

        public void Restart() { if (!Primary && Instance != null) { Instance.Restart(); return; } Program.ExitCode = 2; allowClose = true; Close(); }
        public void Quit() { if (!Primary && Instance != null) { Instance.Quit(); return; } Program.ExitCode = 0; allowClose = true; Close(); }

        public List<AppGroup> AllApps() { return Groups; }
    }

    // ------------------------------------------------------------------
    // Context menus that follow the theme
    // ------------------------------------------------------------------
    class MenuColors : ProfessionalColorTable
    {
        Palette P { get { return Config.P; } }
        public override Color ToolStripDropDownBackground { get { return P.MenuBack; } }
        public override Color ImageMarginGradientBegin { get { return P.MenuBack; } }
        public override Color ImageMarginGradientMiddle { get { return P.MenuBack; } }
        public override Color ImageMarginGradientEnd { get { return P.MenuBack; } }
        public override Color MenuBorder { get { return P.MenuLine; } }
        public override Color MenuItemBorder { get { return P.MenuSel; } }
        public override Color MenuItemSelected { get { return P.MenuSel; } }
        public override Color MenuItemSelectedGradientBegin { get { return P.MenuSel; } }
        public override Color MenuItemSelectedGradientEnd { get { return P.MenuSel; } }
        public override Color SeparatorDark { get { return P.MenuLine; } }
        public override Color SeparatorLight { get { return P.MenuBack; } }
        public override Color CheckBackground { get { return P.MenuBack; } }
        public override Color CheckSelectedBackground { get { return P.MenuSel; } }
        public override Color CheckPressedBackground { get { return P.MenuSel; } }
    }

    class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new MenuColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Config.P.Ink : Config.P.InkDim;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Config.P.InkDim;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(3, 1, e.Item.Width - 6, e.Item.Height - 2);
            Theme.FillRound(e.Graphics, r, 4, Config.P.MenuSel);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle r = e.ImageRectangle;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(Config.Accent, Math.Max(1.5f, r.Height / 8f)))
                e.Graphics.DrawLines(p, new[]
                {
                    new PointF(r.Left + r.Width * 0.2f, r.Top + r.Height * 0.55f),
                    new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.75f),
                    new PointF(r.Left + r.Width * 0.8f, r.Top + r.Height * 0.3f),
                });
        }
    }
}
