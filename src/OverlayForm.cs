// SC Hauling Manager - C0P3Y | Icarus Interstellar Inc.
// In-game route overlay: a small, semi-transparent, always-on-top list of route steps that never takes focus
// away from the game. The "complete step" hotkey ticks off the highlighted step.
// (Star Citizen must run in Borderless or Windowed mode for any overlay to show over it.)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HaulingManager
{
    public class OverlayLine
    {
        public bool Header;        // "STOP 2 · Bueno Ravine"
        public bool Done;          // already ticked (greyed)
        public bool Current;       // the step the hotkey will tick next
        public bool IsPickup;
        public string Text = "";   // "PICK UP  11 SCU  Processed Food"
        public string Sub = "";    // "→ Jackson's Swap" / "▣ 1×4 1×1"
        public string Tag = "";    // header: kind label ("OUTPOST")
    }

    public class OverlayForm : Form
    {
        List<OverlayLine> lines = new List<OverlayLine>();
        public string HintText = "";
        public bool ClickThrough;
        public event EventHandler Moved;
        readonly Font fTitle, fHead, fStep, fSub;
        bool hovered;

        public OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            MinimumSize = new Size(220, 120);
            Text = "SC Hauling - Route";
            fTitle = new Font("Segoe UI Semibold", 8.5f);
            fHead = new Font("Segoe UI Semibold", 10f);
            fStep = new Font("Segoe UI Semibold", 11f);
            fSub = new Font("Segoe UI", 9f);
            MouseEnter += delegate { hovered = true; Invalidate(); };
            MouseLeave += delegate { hovered = false; Invalidate(); };
            ResizeEnd += delegate { if (Moved != null) Moved(this, EventArgs.Empty); };
            Resize += delegate { Invalidate(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80 | 0x08000000 | 0x8;          // tool window, never activates, topmost
                if (ClickThrough) cp.ExStyle |= 0x20 | 0x80000; // mouse goes straight to the game
                return cp;
            }
        }

        public void SetLines(List<OverlayLine> l, string hint)
        {
            lines = l ?? new List<OverlayLine>();
            HintText = hint ?? "";
            Invalidate();
        }

        // ---- drag anywhere to move, drag the edges / corner to resize (no title bar)
        const int WM_NCHITTEST = 0x84, HTCLIENT = 1, HTCAPTION = 2, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTBOTTOM = 15,
                  HTBOTTOMRIGHT = 17, HTBOTTOMLEFT = 16, HTTOPRIGHT = 14, HTTOPLEFT = 13, WM_EXITSIZEMOVE = 0x232;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                Point p = PointToClient(new Point((short)(m.LParam.ToInt64() & 0xFFFF), (short)((m.LParam.ToInt64() >> 16) & 0xFFFF)));
                if (CloseRect().Contains(p)) { m.Result = (IntPtr)HTCLIENT; return; }
                const int g = 8;
                bool l = p.X < g, r = p.X >= Width - g, t = p.Y < g, b = p.Y >= Height - g;
                m.Result = (IntPtr)(t && l ? HTTOPLEFT : t && r ? HTTOPRIGHT : b && l ? HTBOTTOMLEFT : b && r ? HTBOTTOMRIGHT
                                    : l ? HTLEFT : r ? HTRIGHT : t ? HTTOP : b ? HTBOTTOM : HTCAPTION);
                return;
            }
            if (m.Msg == WM_EXITSIZEMOVE && Moved != null) Moved(this, EventArgs.Empty);
            base.WndProc(ref m);
        }

        Rectangle CloseRect() { return new Rectangle(Width - 24, 4, 18, 18); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (CloseRect().Contains(e.Location)) Hide();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Theme.Bg);
            using (Pen bp = new Pen(Theme.Mix(Theme.Accent, Theme.Bg, 0.45f))) g.DrawRectangle(bp, 0, 0, Width - 1, Height - 1);
            using (SolidBrush ab = new SolidBrush(Theme.Highlight)) g.FillRectangle(ab, 0, 0, Width, 2);

            // title row
            using (SolidBrush tb = new SolidBrush(Theme.Accent)) g.DrawString("ROUTE", fTitle, tb, 10, 7);
            using (SolidBrush mb = new SolidBrush(Theme.Muted))
            {
                g.DrawString(HintText, fTitle, mb, new RectangleF(58, 7, Width - 90, 18), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                if (hovered) g.DrawString("✕", fHead, mb, CloseRect().X + 2, CloseRect().Y - 1);
            }

            float y = 30;
            if (lines.Count == 0)
            {
                using (SolidBrush mb = new SolidBrush(Theme.Muted)) g.DrawString("No route yet - press PLAN ROUTE in the app.", fSub, mb, 10, y);
                return;
            }
            // start a little above the current step so you can see what you just did
            int cur = lines.FindIndex(l => l.Current);
            int start = 0;
            if (cur > 0)
            {
                int doneShown = 0;
                start = cur;
                for (int i = cur - 1; i >= 0 && doneShown < 2; i--) { start = i; if (!lines[i].Header) doneShown++; }
                if (start > 0 && lines[start - 1].Header) start--;
            }
            float w = Width - 20;
            for (int i = start; i < lines.Count && y < Height - 8; i++)
            {
                OverlayLine l = lines[i];
                if (l.Header)
                {
                    y += 4;
                    Color hc = l.Done ? Theme.Muted : Theme.Highlight;
                    using (SolidBrush hb = new SolidBrush(hc)) g.FillRectangle(hb, 8, y + 3, 3, 15);
                    using (SolidBrush hb = new SolidBrush(l.Done ? Theme.Muted : Theme.Text))
                        g.DrawString(l.Text, fHead, hb, new RectangleF(16, y, w - (l.Tag.Length > 0 ? 70 : 6), 20), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                    if (l.Tag.Length > 0)
                        using (SolidBrush tg = new SolidBrush(Theme.Mix(Theme.Highlight, Theme.Bg, l.Done ? 0.6f : 0f)))
                            g.DrawString(l.Tag, fTitle, tg, Width - 72, y + 2);
                    y += 22;
                    continue;
                }
                float rowH = 24 + (l.Sub.Length > 0 ? 16 : 0);
                if (l.Current)
                    using (SolidBrush cb = new SolidBrush(Theme.Mix(Theme.Bg, Theme.Highlight, 0.18f))) g.FillRectangle(cb, 4, y - 1, Width - 8, rowH);
                Color ac = l.Done ? Theme.Muted : l.IsPickup ? Theme.Accent : Theme.Highlight;
                string mark = l.Done ? "✓ " : l.Current ? "▶ " : "   ";
                using (SolidBrush b = new SolidBrush(ac)) g.DrawString(mark + l.Text, fStep, b, new RectangleF(14, y, w, 22), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                if (l.Sub.Length > 0)
                    using (SolidBrush sb = new SolidBrush(l.Done ? Theme.Mix(Theme.Muted, Theme.Bg, 0.3f) : Theme.Muted))
                        g.DrawString(l.Sub, fSub, sb, new RectangleF(34, y + 22, w - 20, 16), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                y += rowH + 2;
            }
        }
    }
}
