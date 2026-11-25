// The little glass panel shown above the bar in edit mode: add a spacer or a separator, undo, done.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace StayBar
{
    sealed class EditPanel : GlassWindow
    {
        readonly BarForm bar;
        readonly Font fText, fSmall, fGlyph;
        readonly List<KeyValuePair<string, RectangleF>> buttons = new List<KeyValuePair<string, RectangleF>>();

        static readonly string[][] Buttons =
        {
            new[] { "spacer", "\uE8A0", "Add spacer" },
            new[] { "sep", "\uE76F", "Add separator" },
            new[] { "undo", "\uE7A7", "Undo" },
            new[] { "done", "\uE73E", "Done" },
        };

        public EditPanel(BarForm bar) : base(bar.UiScale, false)
        {
            this.bar = bar;
            KeyPreview = true;
            fText = new Font("Segoe UI Semibold", 13f * Sc, GraphicsUnit.Pixel);
            fSmall = new Font("Segoe UI", 11.5f * Sc, GraphicsUnit.Pixel);
            fGlyph = new Font(Gfx.GlyphFamily(), 13f * Sc, GraphicsUnit.Pixel);
            ClientSize = new Size(S(560), S(92));
            float bw = S(126), gap = S(8), x = S(16), y = S(14);
            foreach (string[] b in Buttons)
            {
                buttons.Add(new KeyValuePair<string, RectangleF>(b[0], new RectangleF(x, y, bw, S(36))));
                x += bw + gap;
            }
        }

        public void ShowAbove(Rectangle barRect)
        {
            PlaceNear(barRect);
            Show();
        }

        protected override void PaintContent(Graphics g, Size s)
        {
            foreach (var kv in buttons)
            {
                string[] info = Array.Find(Buttons, b => b[0] == kv.Key);
                RectangleF r = kv.Value;
                bool enabled = kv.Key != "undo" || bar.CanUndo;
                bool primary = kv.Key == "done";
                Color fill = primary ? Accent : (P.Light ? Color.FromArgb(18, 0, 0, 0) : Color.FromArgb(22, 255, 255, 255));
                if (r.Contains(Mouse) && enabled) fill = primary ? Theme.Lighten(Accent, 0.15f) : (P.Light ? Color.FromArgb(30, 0, 0, 0) : Color.FromArgb(38, 255, 255, 255));
                Theme.FillRound(g, r, r.Height / 2f, fill);
                Color ink = primary ? Gfx.On(Accent) : enabled ? Ink : Faint;
                float tw = Gfx.TW(info[2], fText), gw = S(20), total = gw + S(4) + tw;
                float x0 = r.X + (r.Width - total) / 2f;
                Gfx.Glyph(g, info[1], fGlyph, ink, new RectangleF(x0, r.Y, gw, r.Height));
                Gfx.TextIn(g, info[2], fText, ink, new RectangleF(x0 + gw + S(4), r.Y, tw + S(4), r.Height), Gfx.Left);
            }
            Gfx.TextIn(g, "Drag items between the left, center and right zones.  × removes one.  Drag a spacer's edge to resize it.",
                fSmall, Dim, new RectangleF(S(16), S(56), s.Width - S(32), S(24)), Gfx.Left);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            foreach (var kv in buttons)
            {
                if (!kv.Value.Contains(e.Location)) continue;
                switch (kv.Key)
                {
                    case "spacer": bar.AddItem("spacer", Config.ZLeft); break;
                    case "sep": bar.AddItem("sep", Config.ZLeft); break;
                    case "undo": bar.Undo(); break;
                    case "done": bar.SetEdit(false); return;
                }
                Invalidate();
                return;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Z)) { bar.Undo(); Invalidate(); return true; }
            if (keyData == Keys.Escape || keyData == Keys.Enter) { bar.SetEdit(false); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { fText.Dispose(); fSmall.Dispose(); fGlyph.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
