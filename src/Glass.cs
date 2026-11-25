// The glass behind the bar.
//
// The bar itself is a per-pixel-alpha (layered) window, so everything outside the dock's rounded
// shape can be truly transparent. DWM can't blur behind a layered window, and on Windows 11 the
// acrylic accent ignores window regions (it always fills the whole window rectangle), so the blur
// lives in this separate window, directly under the bar:
//   - floating dock (Windows 11): a Windows.UI.Composition visual tree with the host backdrop brush
//     (the same blur acrylic uses) plus the tint, clipped to a rounded rectangle of any radius.
//     The clip is inset 1 px; the bar paints the anti-aliased rim and hairline on top of it.
//   - tiles bar, or Windows 10: the classic acrylic accent on a plain rectangle (exactly as before).
using System;
using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Comp = Windows.UI.Composition;

namespace StayBar
{
    [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICompositorDesktopInterop
    {
        [PreserveSig] int CreateDesktopWindowTarget(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool isTopmost, [MarshalAs(UnmanagedType.IInspectable)] out object target);
        [PreserveSig] int EnsureOnThread(int threadId);
    }

    sealed class GlassBack : Form
    {
        [StructLayout(LayoutKind.Sequential)] struct DispatcherQueueOptions { public int dwSize, threadType, apartmentType; }
        [DllImport("CoreMessaging.dll")] static extern int CreateDispatcherQueueController(DispatcherQueueOptions o, out IntPtr controller);

        static IntPtr dispatcher;
        static Comp.Compositor compositor;
        static bool? canCompose;

        // Rounded composition glass needs Windows 11 (DWMWA_USE_HOSTBACKDROPBRUSH).
        public static bool CanCompose
        {
            get
            {
                if (canCompose.HasValue) return canCompose.Value;
                canCompose = false;
                try
                {
                    if (Environment.OSVersion.Version.Build < 22000) return false;
                    if (dispatcher == IntPtr.Zero)
                    {
                        var o = new DispatcherQueueOptions();
                        o.dwSize = Marshal.SizeOf(o); o.threadType = 2; o.apartmentType = 2;   // this thread, STA
                        if (CreateDispatcherQueueController(o, out dispatcher) != 0) return false;
                    }
                    compositor = new Comp.Compositor();
                    canCompose = true;
                }
                catch (Exception ex) { Program.Log("Composition glass unavailable: " + ex.Message); }
                return canCompose.Value;
            }
        }

        public readonly bool Composited;
        object target;   // keeps the DesktopWindowTarget alive
        Comp.ContainerVisual root;
        Comp.SpriteVisual blurVisual, tintVisual;
        Comp.CompositionColorBrush tintBrush;
        Comp.CompositionRoundedRectangleGeometry clipGeo;

        Rectangle lastRect;
        int lastRadius = -1;
        Color lastTint;
        bool lastOn, applied;
        DibSurface surface;

        public GlassBack(bool composited)
        {
            Composited = composited;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;
            Text = "StayBar glass";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                if (Composited) cp.ExStyle |= 0x00200000;   // WS_EX_NOREDIRECTIONBITMAP: only the visual tree is shown
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetDwm(Handle, 33, 1);                              // no DWM rounding
            Native.SetDwm(Handle, 34, unchecked((int)0xFFFFFFFE));     // no DWM border
            if (!Composited) return;
            try
            {
                Native.SetDwm(Handle, 17, 1);                          // DWMWA_USE_HOSTBACKDROPBRUSH
                var interop = (ICompositorDesktopInterop)(object)compositor;
                int hr = interop.CreateDesktopWindowTarget(Handle, true, out target);
                if (hr != 0 || target == null) throw new COMException("CreateDesktopWindowTarget", hr);
                var t = (Comp.Desktop.DesktopWindowTarget)target;
                root = compositor.CreateContainerVisual();
                t.Root = root;
                blurVisual = compositor.CreateSpriteVisual();
                blurVisual.Brush = compositor.CreateHostBackdropBrush();
                tintBrush = compositor.CreateColorBrush();
                tintVisual = compositor.CreateSpriteVisual();
                tintVisual.Brush = tintBrush;
                root.Children.InsertAtTop(blurVisual);
                root.Children.InsertAtTop(tintVisual);
                clipGeo = compositor.CreateRoundedRectangleGeometry();
                root.Clip = compositor.CreateGeometricClip(clipGeo);
            }
            catch (Exception ex)
            {
                Program.Log("Composition glass failed, using the plain glass: " + ex.Message);
                canCompose = false;
                root = null;
            }
        }

        public bool Broken { get { return Composited && root == null && IsHandleCreated; } }

        // The acrylic accent tints harder than a plain color layer (it adds its own luminosity layer).
        // Measured on screen: acrylic lets through about (1 - a)^1.55 of the backdrop. The composited
        // glass (and the dock's rim) use this, so the dock looks as dark as the acrylic did.
        public static Color EffectiveTint(Color tint)
        {
            int a = (int)Math.Round(255 * (1 - Math.Pow(1 - tint.A / 255.0, 1.55)));
            return Color.FromArgb(Math.Max(0, Math.Min(255, a)), tint.R, tint.G, tint.B);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = new IntPtr(Native.MA_NOACTIVATE); return; }
            if (m.Msg == 0x84) { m.Result = new IntPtr(-1); return; }  // WM_NCHITTEST: HTTRANSPARENT
            base.WndProc(ref m);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Composited) return;   // no GDI surface at all
            Size sz = ClientSize;
            if (sz.Width <= 0 || sz.Height <= 0) return;
            if (surface == null || surface.Width != sz.Width || surface.Height != sz.Height)
            {
                if (surface != null) surface.Dispose();
                surface = new DibSurface(sz.Width, sz.Height);
                using (Graphics g = Graphics.FromImage(surface.Bitmap)) g.Clear(Color.Transparent);
            }
            surface.BlitTo(e.Graphics);   // fully transparent: the acrylic shows
        }

        // Puts the glass at rect (screen coords). radius > 0 rounds it (composited glass only).
        public void Sync(Rectangle rect, int radius, bool on, Color tint)
        {
            if (!IsHandleCreated) return;
            if (!on || rect.Width <= 0 || rect.Height <= 0)
            {
                if (Native.IsWindowVisible(Handle)) Native.ShowWindow(Handle, Native.SW_HIDE);
                lastOn = false;
                return;
            }
            if (root != null)
            {
                if (tint != lastTint || !applied)
                {
                    Color e = EffectiveTint(tint);
                    tintBrush.Color = Windows.UI.Color.FromArgb(e.A, e.R, e.G, e.B);
                    lastTint = tint; applied = true;
                }
                if (rect.Size != lastRect.Size || radius != lastRadius)
                {
                    var size = new Vector2(rect.Width, rect.Height);
                    root.Size = size; blurVisual.Size = size; tintVisual.Size = size;
                    // 1 px inside the shape: the bar's anti-aliased rim covers the clip's edge
                    clipGeo.Offset = new Vector2(1, 1);
                    clipGeo.Size = new Vector2(Math.Max(0, rect.Width - 2), Math.Max(0, rect.Height - 2));
                    float r = Math.Max(0, radius - 1);
                    clipGeo.CornerRadius = new Vector2(r, r);
                }
            }
            else if (!applied || tint != lastTint || !lastOn) { Native.SetGlass(Handle, true, tint); lastTint = tint; applied = true; }
            if (rect != lastRect || !lastOn)
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, rect.X, rect.Y, rect.Width, rect.Height, Native.SWP_NOACTIVATE);
            lastRect = rect; lastRadius = radius;
            if (!lastOn || !Native.IsWindowVisible(Handle)) Native.ShowWindow(Handle, Native.SW_SHOWNA);
            lastOn = true;
        }

        // Keeps the glass topmost and directly under the bar.
        public void KeepUnder(IntPtr bar)
        {
            if (!IsHandleCreated || !lastOn) return;
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            Native.SetWindowPos(bar, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (surface != null) { surface.Dispose(); surface = null; }
                if (root != null) { try { root.Dispose(); } catch { } root = null; }
                target = null;
            }
            base.Dispose(disposing);
        }
    }
}
