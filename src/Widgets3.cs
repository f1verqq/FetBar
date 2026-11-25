// Phase 3 widgets: system and Windows toggles.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace StayBar
{
    // ==================================================================
    // System
    // ==================================================================
    class TopProcessWidget : Widget
    {
        public TopProcessWidget() : base("topproc", "Top process", "System", "\uE9D9", "The app using the most CPU right now (or memory). Click for Task Manager.") { }
        public override string[] Styles { get { return new[] { "CPU", "Memory" }; } }
        public override bool HasLabel { get { return true; } }
        public override int Width(Ui u) { return u.FI(150); }
        public override int CompactWidth(Ui u) { return u.FI(104); }
        bool Mem { get { return Style == "Memory"; } }

        public override void Tick(BarForm bar) { PollEvery(2, false, ProcTop.Sample); }

        string Proc { get { if (Svc.Demo) return Mem ? "chrome" : "Code"; string n = Mem ? ProcTop.RamName : ProcTop.CpuName; return string.IsNullOrEmpty(n) ? "…" : n; } }
        string Value { get { if (Svc.Demo) return Mem ? "1.8 GB" : "14%"; return Mem ? (ProcTop.RamMb >= 1024 ? (ProcTop.RamMb / 1024).ToString("0.0") + " GB" : (int)ProcTop.RamMb + " MB") : (int)Math.Round(ProcTop.CpuPct) + "%"; } }
        public override string LineText(Ui u) { return Proc + " " + Value; }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            RectangleF t = u.Tile(r);
            float gs = u.F(26);
            var gr = new RectangleF(t.X + u.F(8), t.Y + (t.Height - gs) / 2f, gs, gs);
            Gfx.Glyph(g, Mem ? "\uE964" : "\uE950", u.Glyph, u.Accent, gr);
            float x = gr.Right + u.F(6), w = t.Right - x - u.F(8);
            float th = ShowLabel ? u.Tiny.GetHeight() : 0, nh = u.Title.GetHeight();
            float y = t.Y + (t.Height - th - nh) / 2f;
            if (ShowLabel) Gfx.TextIn(g, Mem ? "Most memory" : "Most CPU", u.Tiny, u.Dim, new RectangleF(x, y, w, th), Gfx.Left);
            string v = Value;
            float vw = Gfx.TW(v, u.Bold) + u.F(4);
            Gfx.TextIn(g, Proc, u.Bold, u.Ink, new RectangleF(x, y + th, w - vw, nh), Gfx.Left);
            Gfx.TextIn(g, v, u.Bold, u.Accent, new RectangleF(x, y + th, w, nh), Gfx.Right);
        }

        public override string Tip(Ui u, Rectangle r, Point p)
        {
            return "Most CPU: " + ProcTop.CpuName + " " + (int)ProcTop.CpuPct + "%\nMost memory: " + ProcTop.RamName + " " + (int)ProcTop.RamMb + " MB\nClick: Task Manager";
        }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b) { if (b == MouseButtons.Left) Win.Launch("taskmgr.exe"); }
    }

    class UptimeWidget : Widget
    {
        public UptimeWidget() : base("uptime", "Uptime", "System", "\uE823", "How long the PC has been on since the last restart.") { }
        public override bool HasLabel { get { return true; } }
        public override int Width(Ui u) { return u.FI(112); }
        public override string LineText(Ui u) { return "Up " + Uptime.Format(Uptime.Now); }
        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover) { InfoTile(g, r, u, "\uE823", u.Accent, "Uptime", Uptime.Format(Uptime.Now), null); }
        public override string Tip(Ui u, Rectangle r, Point p) { return "On since " + DateTime.Now.Subtract(Uptime.Now).ToString("f") + "\nA restart now and then keeps Windows fresh"; }
    }

    class DiskIoWidget : Widget
    {
        public DiskIoWidget() : base("diskio", "Disk activity", "System", "\uEDA2", "Live disk read and write speed with a graph.") { }
        public override int Width(Ui u) { return u.FI(132); }
        public override void Tick(BarForm bar) { PollEvery(1, false, DiskIo.Sample); }
        public override string LineText(Ui u) { return "R " + Sys.Rate(DiskIo.Read) + " W " + Sys.Rate(DiskIo.Write); }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            RectangleF t = u.Tile(r);
            float max = Math.Max(DiskIo.ReadH.Max(), DiskIo.WriteH.Max());
            var sr = new RectangleF(t.X + u.F(4), t.Y + t.Height * 0.35f, t.Width - u.F(8), t.Height * 0.6f);
            Gfx.Spark(g, sr, DiskIo.ReadH.Data, DiskIo.ReadH.Count, Math.Max(1, max), Color.FromArgb(150, u.Accent), true);
            Gfx.Spark(g, sr, DiskIo.WriteH.Data, DiskIo.WriteH.Count, Math.Max(1, max), Color.FromArgb(110, u.Ink), false);
            float lh = u.Bold.GetHeight(), x = t.X + u.F(10), gw = u.F(16);
            float y = t.Y + (t.Height - lh * 2) / 2f;
            Gfx.TextIn(g, "R", u.Tiny, u.Accent, new RectangleF(x, y, gw, lh), Gfx.Left);
            Gfx.Text(g, Sys.Rate(DiskIo.Read), u.Bold, u.Ink, x + gw, y);
            Gfx.TextIn(g, "W", u.Tiny, u.Dim, new RectangleF(x, y + lh, gw, lh), Gfx.Left);
            Gfx.Text(g, Sys.Rate(DiskIo.Write), u.Bold, u.Dim, x + gw, y + lh);
        }

        public override string Tip(Ui u, Rectangle r, Point p) { return "Disk read " + Sys.Rate(DiskIo.Read) + "\nDisk write " + Sys.Rate(DiskIo.Write) + "\nClick: Resource Monitor"; }
        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b) { if (b == MouseButtons.Left) Win.Launch("resmon.exe"); }
    }

    class PingWidget : Widget
    {
        public PingWidget() : base("ping", "Ping & IP", "Network", "\uE968", "Your internet latency, public IP address, and whether a VPN is on.") { }
        public override bool HasLabel { get { return true; } }
        public override int Width(Ui u) { return u.FI(138); }

        public override void Tick(BarForm bar)
        {
            NetInfo.PingHost = Opt("host", "1.1.1.1");
            PollEvery(Math.Max(5, OptInt("every", 30)), true, NetInfo.Poll);
        }

        protected override void OnOptionsChanged() { PollSoon(); }
        public override string LineText(Ui u) { return NetInfo.PingMs < 0 ? "Offline" : NetInfo.PingMs + " ms"; }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            int ms = NetInfo.PingMs;
            Color c = ms < 0 ? Color.FromArgb(251, 113, 133) : ms > 150 ? Color.FromArgb(251, 191, 36) : u.Accent;
            string cap = NetInfo.VpnName.Length > 0 ? "VPN on · " + NetInfo.PublicIp : (NetInfo.PublicIp.Length > 0 ? NetInfo.PublicIp : "Ping");
            InfoTile(g, r, u, NetInfo.VpnName.Length > 0 ? "\uE72E" : "\uE968", c, cap, ms < 0 ? "Offline" : ms + " ms", null);
        }

        public override string Tip(Ui u, Rectangle r, Point p)
        {
            return "Ping " + NetInfo.PingHost + ": " + (NetInfo.PingMs < 0 ? "no answer" : NetInfo.PingMs + " ms") + "\nPublic IP: " + (NetInfo.PublicIp.Length > 0 ? NetInfo.PublicIp : "unknown") +
                   "\nVPN: " + (NetInfo.VpnName.Length > 0 ? NetInfo.VpnName : "off") + "\nClick to copy the IP";
        }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            if (b == MouseButtons.Left && NetInfo.PublicIp.Length > 0) { try { Clipboard.SetText(NetInfo.PublicIp); bar.ShowToast("Copied", NetInfo.PublicIp); } catch { } }
        }

        public override void Options(IWidgetOptions o)
        {
            o.Text("Ping address", "A server to ping", "host", "1.1.1.1");
            o.Number("Every", null, "every", 5, 600, 30, " s");
        }
    }

    class WifiWidget : Widget
    {
        public WifiWidget() : base("wifi", "Wi-Fi signal", "Network", "\uE701", "The Wi-Fi network you're on and how strong the signal is.") { }
        public override bool HasLabel { get { return true; } }
        public override int Width(Ui u) { return u.FI(140); }
        public override void Tick(BarForm bar) { PollEvery(5, false, Wlan.Poll); }
        public override string LineText(Ui u) { return Wlan.Quality < 0 ? "No Wi-Fi" : Wlan.Quality + "% " + Wlan.Ssid; }

        static string Bars(int q) { return q < 0 ? "\uE701" : q > 75 ? "\uE701" : q > 50 ? "\uE874" : q > 25 ? "\uE873" : "\uE872"; }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            int q = Wlan.Quality;
            InfoTile(g, r, u, Bars(q), q < 0 ? u.Dim : u.Accent, q < 0 ? "Wi-Fi" : q + "% signal", q < 0 ? "Not connected" : Wlan.Ssid, u.Bold);
        }

        public override string Tip(Ui u, Rectangle r, Point p) { return Wlan.Quality < 0 ? "Not connected to Wi-Fi" : Wlan.Ssid + "\nSignal " + Wlan.Quality + "%\nClick: Wi-Fi settings"; }
        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b) { if (b == MouseButtons.Left) Win.Launch("ms-settings:network-wifi"); }
    }

    class BatteryHealthWidget : Widget
    {
        public BatteryHealthWidget() : base("batteryhealth", "Battery health", "System", "\uE83F", "How much charge your battery still holds compared to new, and its charge cycles.") { }
        public override bool Available { get { return Svc.Demo || BatteryWidget.Battery() != null; } }
        public override bool HasLabel { get { return true; } }
        public override int Width(Ui u) { return u.FI(128); }
        public override void Tick(BarForm bar) { PollEvery(3600, false, BatteryHealth.Poll); }
        public override string LineText(Ui u) { int h = BatteryHealth.HealthPct; return h < 0 ? "Health ?" : "Health " + h + "%"; }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            int h = BatteryHealth.HealthPct;
            Color c = h < 0 ? u.Dim : h < 70 ? Color.FromArgb(251, 113, 133) : h < 85 ? Color.FromArgb(251, 191, 36) : Color.FromArgb(74, 222, 128);
            InfoTile(g, r, u, "\uE83F", c, BatteryHealth.Cycles > 0 ? BatteryHealth.Cycles + " cycles" : "Battery health", h < 0 ? "…" : h + "%", null);
        }

        public override string Tip(Ui u, Rectangle r, Point p)
        {
            int h = BatteryHealth.HealthPct;
            if (h < 0) return "Battery health: Windows doesn't report it on this PC";
            return "Holds " + h + "% of its design capacity (" + BatteryHealth.Full / 1000.0 + " of " + BatteryHealth.Design / 1000.0 + " Wh)\nWear " + (100 - h) + "%" + (BatteryHealth.Cycles > 0 ? ", " + BatteryHealth.Cycles + " charge cycles" : "");
        }
    }

    class PowerPlanWidget : Widget
    {
        public PowerPlanWidget() : base("power", "Power mode", "System", "\uE945", "Switch between best efficiency, balanced and best performance (or your power plans).") { }
        public override bool HasLabel { get { return true; } }
        public override int Width(Ui u) { return u.FI(150); }
        string modeName = "";
        public override void Tick(BarForm bar)
        {
            PollEvery(10, false, () =>
            {
                int m = PowerPlans.Mode;
                modeName = m >= 0 ? PowerPlans.Modes[m].Value : PowerPlans.ActiveName;
            });
        }
        string Now { get { return Svc.Demo ? "Balanced" : modeName.Length > 0 ? modeName : "…"; } }
        public override string LineText(Ui u) { return Now; }
        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover) { InfoTile(g, r, u, "\uE945", u.Accent, "Power mode", Now, u.Bold); }
        public override string Tip(Ui u, Rectangle r, Point p) { return "Power mode: " + Now + "\nClick to change"; }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            ContextMenuStrip m = bar.NewMenu(true);
            int mode = PowerPlans.Mode;
            if (mode >= 0)
                for (int i = 0; i < PowerPlans.Modes.Length; i++)
                {
                    int k = i;
                    BarForm.Item(m, PowerPlans.Modes[i].Value, () => { if (!PowerPlans.SetMode(k)) Win.Launch("ms-settings:powersleep"); PollSoon(); }).Checked = mode == i;
                }
            var plans = PowerPlans.Plans();
            if (plans.Count > 1 || mode < 0)
            {
                if (m.Items.Count > 0) m.Items.Add(new ToolStripSeparator());
                Guid act = PowerPlans.Active;
                foreach (var pl in plans) { Guid gid = pl.Key; BarForm.Item(m, pl.Value, () => { PowerPlans.Set(gid); PollSoon(); }).Checked = gid == act; }
            }
            m.Items.Add(new ToolStripSeparator());
            BarForm.Item(m, "Power settings", () => Win.Launch("ms-settings:powersleep"));
            bar.ShowMenu(m, p);
        }
    }

    class PeakMeterWidget : Widget
    {
        public PeakMeterWidget() : base("peak", "Audio level", "Media", "\uE995", "A live level meter for what's playing.") { }
        public override int Width(Ui u) { return u.FI(118); }
        float level, held;
        DateTime heldAt;
        public override int RepaintMs { get { return level > 0.005f || held > 0.005f ? 50 : 0; } }
        public override string LineText(Ui u) { return (int)(AudioX.Peak * 100) + "%"; }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            RectangleF t = u.Tile(r);
            float peak = AudioX.Peak;
            level = peak > level ? peak : level * 0.8f + peak * 0.2f;
            if (peak >= held || (DateTime.UtcNow - heldAt).TotalMilliseconds > 900) { held = peak; heldAt = DateTime.UtcNow; }
            float gs = u.F(22);
            var gr = new RectangleF(t.X + u.F(6), t.Y + (t.Height - gs) / 2f, gs, gs);
            Gfx.Glyph(g, "\uE995", u.GlyphSmall, level > 0.01f ? u.Ink : u.Dim, gr);
            int n = 14;
            float x0 = gr.Right + u.F(4), w = t.Right - x0 - u.F(10), bw = w / n, h = t.Height - u.F(20);
            for (int i = 0; i < n; i++)
            {
                float f = (i + 1f) / n;
                float bh = h * (0.35f + 0.65f * f);
                bool lit = level >= f - 0.5f / n;
                bool peakBar = Math.Abs(held - f) < 0.5f / n;
                Color c = f > 0.85f ? Color.FromArgb(251, 113, 133) : f > 0.65f ? Color.FromArgb(251, 191, 36) : u.Accent;
                Theme.FillRound(g, new RectangleF(x0 + i * bw + bw * 0.2f, t.Y + (t.Height + h) / 2f - bh, bw * 0.6f, bh), Math.Min(bw * 0.3f, u.F(2)),
                    lit || peakBar ? c : Color.FromArgb(u.P.Light ? 20 : 26, u.Ink));
            }
        }

        public override string Tip(Ui u, Rectangle r, Point p) { return "Audio level " + (int)(level * 100) + "%\nClick: sound settings"; }
        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b) { if (b == MouseButtons.Left) Win.Launch("ms-settings:sound"); }
    }

    class MixerWidget : Widget
    {
        public MixerWidget() : base("mixer", "Volume mixer", "Media", "\uE9E9", "The volume of each app that's playing sound, in a flyout.") { }
        public override int Width(Ui u) { return u.Cell; }
        public override bool Sizable { get { return false; } }
        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover) { Gfx.Glyph(g, "\uE9E9", u.GlyphBig, u.Ink, new RectangleF(r.X, r.Y - u.S(1.5f) * hover, r.Width, r.Height)); }
        public override string Tip(Ui u, Rectangle r, Point p) { return "Volume mixer\nEach app's volume"; }
        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            if (b == MouseButtons.Left) bar.ToggleFlyout("mixer", r, () => new MixerFlyout(bar.U.Scale));
            else if (b == MouseButtons.Right) { ContextMenuStrip m = bar.NewMenu(false); BarForm.Item(m, "Windows volume mixer", () => Win.Launch("ms-settings:apps-volume")); bar.ShowMenu(m, p); }
        }
    }

    class MicWidget : Widget
    {
        public MicWidget() : base("mic", "Microphone", "Media", "\uE720", "Mute or unmute your microphone. A dot shows when an app is using the mic or the camera.") { }
        public override int Width(Ui u) { return u.Cell; }
        public override bool Sizable { get { return false; } }
        bool? muted;
        public override void Tick(BarForm bar) { PollEvery(2, false, () => { Privacy.Poll(); muted = AudioX.MicMuted; }); }
        public override string LineText(Ui u) { return muted == true ? "Mic off" : "Mic on"; }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            bool off = muted == true;
            ToggleTile(g, r, u, off ? "\uEC54" : "\uE720", !off && muted != null, hover);
            if (Privacy.MicInUse || Privacy.CamInUse)
            {
                float d = u.F(8);
                using (var b = new SolidBrush(Privacy.CamInUse ? Color.FromArgb(74, 222, 128) : Color.FromArgb(251, 146, 60)))
                    g.FillEllipse(b, r.X + r.Width / 2f + u.F(7), r.Y + r.Height / 2f - u.F(13), d, d);
            }
        }

        public override string Tip(Ui u, Rectangle r, Point p)
        {
            string s = muted == null ? "No microphone" : muted == true ? "Microphone muted" : "Microphone on";
            if (Privacy.MicInUse) s += "\nAn app is using the microphone";
            if (Privacy.CamInUse) s += "\nAn app is using the camera";
            return s + "\nClick to " + (muted == true ? "unmute" : "mute");
        }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            if (b == MouseButtons.Left && muted != null) { muted = !(muted == true); AudioX.MicMuted = muted; bar.Invalidate(); }
            else if (b == MouseButtons.Right) { ContextMenuStrip m = bar.NewMenu(false); BarForm.Item(m, "Microphone privacy settings", () => Win.Launch("ms-settings:privacy-microphone")); BarForm.Item(m, "Sound input settings", () => Win.Launch("ms-settings:sound")); bar.ShowMenu(m, p); }
        }
    }

    class UsbWidget : Widget
    {
        public UsbWidget() : base("usb", "Safely remove", "System", "\uE88E", "USB drives that are plugged in; click one to eject it safely.") { }
        public override bool Shown(Ui u) { return Svc.Demo || Usb.Drives.Length > 0 || !OptBool("hideempty", true); }
        public override int Width(Ui u) { return u.Cell; }
        public override bool Sizable { get { return false; } }
        public override void Tick(BarForm bar) { PollEvery(4, false, Usb.Poll); }
        public override string LineText(Ui u) { return Usb.Drives.Length + " drive" + (Usb.Drives.Length == 1 ? "" : "s"); }

        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            Gfx.Glyph(g, "\uE88E", u.GlyphBig, Usb.Drives.Length > 0 ? u.Ink : u.Dim, new RectangleF(r.X, r.Y - u.S(1.5f) * hover, r.Width, r.Height));
            if (Usb.Drives.Length > 0)
            {
                float d = u.F(14);
                string s = Usb.Drives.Length.ToString();
                var br = new RectangleF(r.X + r.Width / 2f + u.F(5), r.Y + r.Height / 2f - u.F(15), d, d);
                Theme.FillRound(g, br, d / 2, u.Accent);
                Gfx.TextIn(g, s, u.Tiny, Gfx.On(u.Accent), br, Gfx.Mid);
            }
        }

        public override string Tip(Ui u, Rectangle r, Point p)
        {
            return Usb.Drives.Length == 0 ? "No USB drives" : string.Join("\n", Usb.Drives.Select(d => { string[] x = d.Split('|'); return x[0] + "  " + x[1] + " (" + x[2] + ")"; }).ToArray()) + "\nClick to eject";
        }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            ContextMenuStrip m = bar.NewMenu(false);
            foreach (string d in Usb.Drives)
            {
                string[] x = d.Split('|');
                string root = x[0];
                BarForm.Item(m, "Eject " + x[1] + " (" + root.TrimEnd('\\') + ", " + x[2] + ")", () => { Usb.Eject(root); bar.ShowToast("Ejecting " + root.TrimEnd('\\'), "Wait for Windows to say it's safe to remove."); });
            }
            if (m.Items.Count == 0) BarForm.Item(m, "No USB drives", () => { }).Enabled = false;
            m.Items.Add(new ToolStripSeparator());
            BarForm.Item(m, "Open This PC", () => Win.Launch("shell:MyComputerFolder"));
            bar.ShowMenu(m, p);
        }

        public override void Options(IWidgetOptions o) { o.Check("Hide when nothing is plugged in", null, "hideempty", true); }
    }

    // ==================================================================
    // Toggles
    // ==================================================================
    abstract class ToggleWidget : Widget
    {
        protected ToggleWidget(string id, string name, string glyph, string desc) : base(id, name, "Toggles", glyph, desc) { }
        public override int Width(Ui u) { return u.Cell; }
        public override bool Sizable { get { return false; } }
        protected abstract bool On { get; }
        protected abstract void Flip(BarForm bar);
        protected virtual string GlyphFor(bool on) { return IconGlyph; }
        protected virtual string SettingsUri { get { return null; } }
        public override string LineText(Ui u) { return Name + (On ? " on" : " off"); }
        public override string LineGlyph { get { return GlyphFor(On); } }
        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover) { ToggleTile(g, r, u, GlyphFor(On), On, hover); }
        public override string Tip(Ui u, Rectangle r, Point p) { return Name + ": " + (On ? "on" : "off") + "\nClick to turn it " + (On ? "off" : "on"); }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            if (b == MouseButtons.Left) { Flip(bar); bar.Invalidate(); }
            else if (b == MouseButtons.Right && SettingsUri != null) { ContextMenuStrip m = bar.NewMenu(false); BarForm.Item(m, Name + " settings", () => Win.Launch(SettingsUri)); bar.ShowMenu(m, p); }
        }
    }

    class DarkModeWidget : ToggleWidget
    {
        public DarkModeWidget() : base("darkmode", "Dark mode", "\uE708", "Switch Windows and apps between dark and light.") { }
        bool on = Toggles.Dark;
        DateTime check;
        protected override bool On { get { if ((DateTime.UtcNow - check).TotalSeconds > 3) { on = Toggles.Dark; check = DateTime.UtcNow; } return on; } }
        protected override string GlyphFor(bool on) { return on ? "\uE708" : "\uE706"; }
        protected override void Flip(BarForm bar) { on = !On; Toggles.SetDark(on); check = DateTime.UtcNow; }
        protected override string SettingsUri { get { return "ms-settings:colors"; } }
    }

    class NightLightWidget : ToggleWidget
    {
        public NightLightWidget() : base("nightlight", "Night light", "\uE793", "Warmer colors in the evening, easier on the eyes.") { }
        protected override bool On { get { return Toggles.NightLight == true; } }
        protected override void Flip(BarForm bar) { if (!Toggles.ToggleNightLight()) Win.Launch("ms-settings:nightlight"); }
        protected override string SettingsUri { get { return "ms-settings:nightlight"; } }
    }

    class DndWidget : ToggleWidget
    {
        public DndWidget() : base("dnd", "Do not disturb", "\uE708", "Silence Windows notifications.") { }
        protected override bool On { get { return Toggles.Dnd; } }
        protected override string GlyphFor(bool on) { return on ? "\uE708" : "\uEA8F"; }
        protected override void Flip(BarForm bar) { if (!Toggles.SetDnd(!On)) Win.Launch("ms-settings:notifications"); }
        protected override string SettingsUri { get { return "ms-settings:notifications"; } }
    }

    class GameModeWidget : ToggleWidget
    {
        public GameModeWidget() : base("gamemode", "Game mode", "\uE7FC", "Windows Game Mode: puts games first while you play.") { }
        protected override bool On { get { return Toggles.GameMode; } }
        protected override void Flip(BarForm bar) { Toggles.SetGameMode(!On); }
        protected override string SettingsUri { get { return "ms-settings:gaming-gamemode"; } }
    }

    class DesktopIconsWidget : ToggleWidget
    {
        public DesktopIconsWidget() : base("desktopicons", "Desktop icons", "\uECA5", "Show or hide the icons on your desktop.") { }
        protected override bool On { get { return !Toggles.IconsHidden; } }
        protected override void Flip(BarForm bar) { Toggles.ToggleDesktopIcons(); }
    }

    class ScreenshotWidget : Widget
    {
        public ScreenshotWidget() : base("screenshot", "Screenshot", "Toggles", "\uE722", "Snip part of the screen (like Win+Shift+S). Right-click for the Snipping Tool.") { }
        public override int Width(Ui u) { return u.Cell; }
        public override bool Sizable { get { return false; } }
        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover) { Gfx.Glyph(g, "\uE722", u.GlyphBig, u.Ink, new RectangleF(r.X, r.Y - u.S(1.5f) * hover, r.Width, r.Height)); }
        public override string Tip(Ui u, Rectangle r, Point p) { return "Screenshot  (Win+Shift+S)\nRight-click: Snipping Tool"; }
        public override bool RightClickIsAction { get { return true; } }
        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            if (b == MouseButtons.Left) Win.Launch("ms-screenclip:");
            else if (b == MouseButtons.Right) Win.Launch("shell:AppsFolder\\Microsoft.ScreenSketch_8wekyb3d8bbwe!App");
        }
    }

    class ColorPickerWidget : Widget
    {
        public ColorPickerWidget() : base("colorpicker", "Color picker", "Toggles", "\uEF3C", "Pick any color on the screen; its hex code is copied.") { }
        public override int Width(Ui u) { return u.Cell; }
        public override bool Sizable { get { return false; } }
        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover)
        {
            Gfx.Glyph(g, "\uEF3C", u.GlyphBig, u.Ink, new RectangleF(r.X, r.Y - u.S(1.5f) * hover, r.Width, r.Height));
            Color last = ColorPickerWindow.Last;
            if (last != Color.Empty)
            {
                float d = u.F(8);
                using (var b = new SolidBrush(last)) g.FillEllipse(b, r.X + r.Width / 2f + u.F(6), r.Y + r.Height / 2f + u.F(5), d, d);
            }
        }
        public override string Tip(Ui u, Rectangle r, Point p) { return "Color picker\nClick, then click anywhere on the screen" + (ColorPickerWindow.Last != Color.Empty ? "\nLast: #" + Config.Hex(ColorPickerWindow.Last) : ""); }
        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b) { if (b == MouseButtons.Left) ColorPickerWindow.Start(bar); }
    }

    class KeepAwakeWidget : ToggleWidget
    {
        public KeepAwakeWidget() : base("keepawake", "Keep awake", "\uE706", "Stops the PC and the screen from going to sleep (for downloads or a long video).") { }
        protected override bool On { get { return Toggles.Awake; } }
        protected override string GlyphFor(bool on) { return on ? "\uE706" : "\uE708"; }
        protected override void Flip(BarForm bar) { Toggles.SetAwake(!Toggles.Awake); }
    }

    class EyeBreakWidget : Widget
    {
        public EyeBreakWidget() : base("eyebreak", "Eye breaks", "Toggles", "\uE890", "The 20-20-20 rule: every 20 minutes of screen time, look 20 feet away for 20 seconds.") { }
        public override bool HasLabel { get { return true; } }
        public override int Width(Ui u) { return u.FI(118); }
        double usedSec;
        DateTime lastTick = DateTime.UtcNow;
        int Every { get { return Math.Max(5, OptInt("every", 20)); } }

        public override void Tick(BarForm bar)
        {
            double dt = (DateTime.UtcNow - lastTick).TotalSeconds;
            lastTick = DateTime.UtcNow;
            if (dt > 5) dt = 0;
            if (Native.IdleSeconds() > 120) { usedSec = Math.Max(0, usedSec - dt * 4); return; }   // away: counts as a rest
            usedSec += dt;
            if (usedSec >= Every * 60)
            {
                usedSec = 0;
                bar.ShowToast("Eye break", "Look at something far away for 20 seconds.");
                if (!Automations.Quiet && OptBool("sound", false)) System.Media.SystemSounds.Asterisk.Play();
            }
        }

        string Left { get { double s = Svc.Demo ? 7 * 60 + 20 : Every * 60 - usedSec; return Focus.Clock(TimeSpan.FromSeconds(Math.Max(0, s))); } }
        public override string LineText(Ui u) { return "Break in " + Left; }
        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover) { InfoTile(g, r, u, "\uE890", u.Accent, "Eye break in", Left, null); }
        public override string Tip(Ui u, Rectangle r, Point p) { return "Next eye break in " + Left + "\nClick to take one now"; }
        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b) { if (b == MouseButtons.Left) { usedSec = 0; bar.ShowToast("Eye break", "Look at something far away for 20 seconds."); } }

        public override void Options(IWidgetOptions o)
        {
            o.Number("Every", null, "every", 5, 120, 20, " min");
            o.Check("Play a sound", null, "sound", false);
        }
    }

    class PowerMenuWidget : Widget
    {
        public PowerMenuWidget() : base("powermenu", "Power", "Toggles", "\uE7E8", "Lock, sleep, restart, shut down or sign out.") { }
        public override int Width(Ui u) { return u.Cell; }
        public override bool Sizable { get { return false; } }
        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover) { Gfx.Glyph(g, "\uE7E8", u.GlyphBig, u.Ink, new RectangleF(r.X, r.Y - u.S(1.5f) * hover, r.Width, r.Height)); }
        public override string Tip(Ui u, Rectangle r, Point p) { return "Power"; }

        [System.Runtime.InteropServices.DllImport("powrprof.dll")] static extern bool SetSuspendState(bool hibernate, bool force, bool disableWake);

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            ContextMenuStrip m = bar.NewMenu(false);
            BarForm.Item(m, "Lock", () => Native.LockWorkStation());
            BarForm.Item(m, "Sleep", () => SetSuspendState(false, false, false));
            m.Items.Add(new ToolStripSeparator());
            BarForm.Item(m, "Restart…", () => Confirm("Restart the PC now?", "/r /t 0"));
            BarForm.Item(m, "Shut down…", () => Confirm("Shut down the PC now?", "/s /t 0"));
            BarForm.Item(m, "Sign out…", () => Confirm("Sign out now? Unsaved work in open apps may be lost.", "/l"));
            bar.ShowMenu(m, p);
        }

        static void Confirm(string q, string args)
        {
            if (MessageBox.Show(q, "StayBar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { var psi = new ProcessStartInfo("shutdown.exe", args); psi.CreateNoWindow = true; psi.UseShellExecute = false; Process.Start(psi); } catch { }
        }
    }

    class AudioOutWidget : Widget
    {
        public AudioOutWidget() : base("audioout", "Audio output", "Media", "\uE7F5", "Switch between speakers and headphones in one click.") { }
        public override bool HasLabel { get { return true; } }
        public override int Width(Ui u) { return u.FI(140); }
        public override string LineText(Ui u) { return Audio.DeviceName; }
        public override void Paint(Graphics g, Rectangle r, Ui u, Point mouse, float hover) { InfoTile(g, r, u, "\uE7F5", u.Accent, "Output", Audio.DeviceName, u.Bold); }
        public override string Tip(Ui u, Rectangle r, Point p) { return "Playing on " + Audio.DeviceName + "\nClick to switch"; }

        public override void Click(BarForm bar, Rectangle r, Point p, MouseButtons b)
        {
            ContextMenuStrip m = bar.NewMenu(true);
            foreach (AudioDevice d in AudioX.Outputs())
            {
                string id = d.Id;
                BarForm.Item(m, d.Name, () => { if (!AudioX.SetDefaultOutput(id)) Win.Launch("ms-settings:sound"); BarForm.Poke(); }).Checked = d.Default;
            }
            m.Items.Add(new ToolStripSeparator());
            BarForm.Item(m, "Sound settings", () => Win.Launch("ms-settings:sound"));
            bar.ShowMenu(m, p);
        }
    }
}
