// SC Hauling Manager - C0P3Y | Icarus Interstellar Inc.
// Toast notification, region picker, text viewer.
using System;
using System.Drawing;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace HaulingManager
{
    // Small always-on-top popup that does not steal focus from the game.
    public class ToastForm : Form
    {
        readonly Timer timer = new Timer();
        static ToastForm current;
        readonly Color accent;

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 | 0x00000080 | 0x00000008; // NOACTIVATE | TOOLWINDOW | TOPMOST
                return cp;
            }
        }

        ToastForm(string title, string body, bool error, string warning)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            accent = error ? Theme.Red : Theme.Accent;
            BackColor = error ? Theme.Mix(Theme.Header, Theme.Red, 0.15f) : Theme.Header;
            ForeColor = Theme.Text;
            Opacity = 0.95;

            Label t = new Label();
            t.Text = title; t.AutoSize = true; t.Font = new Font("Segoe UI Semibold", 11f);
            t.ForeColor = accent;
            t.Location = new Point(18, 10);
            Label b = new Label();
            b.Text = body; b.AutoSize = true; b.MaximumSize = new Size(600, 0); b.Font = Theme.Base;
            b.Location = new Point(18, 36);
            Controls.Add(t); Controls.Add(b);
            Size sz = b.GetPreferredSize(new Size(600, 0));
            int h = 36 + sz.Height + 12;
            int w = Math.Max(t.PreferredWidth, sz.Width);
            if (!string.IsNullOrEmpty(warning))
            {
                Label wl = new Label();
                wl.Text = warning; wl.AutoSize = true; wl.MaximumSize = new Size(600, 0); wl.Font = Theme.Semibold; wl.ForeColor = Theme.Highlight;
                wl.Location = new Point(18, h - 4);
                Controls.Add(wl);
                Size ws = wl.GetPreferredSize(new Size(600, 0));
                h += ws.Height + 4; w = Math.Max(w, ws.Width);
                wl.Click += delegate { Close(); };
            }
            Width = Math.Max(340, w + 36);
            Height = h + 4;

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + 40);

            Paint += delegate(object s, PaintEventArgs e)
            {
                using (SolidBrush br = new SolidBrush(accent)) e.Graphics.FillRectangle(br, 0, 0, 4, Height);
                using (Pen p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            };

            timer.Interval = error ? 6000 : (warning != null ? 6000 : 4500);
            timer.Tick += delegate { timer.Stop(); Close(); };
            Click += delegate { Close(); };
            t.Click += delegate { Close(); };
            b.Click += delegate { Close(); };
        }

        public static void Popup(string title, string body, bool error)
        {
            Popup(title, body, error, null);
        }

        public static void Popup(string title, string body, bool error, string warning)
        {
            CloseCurrent();
            current = new ToastForm(title, body, error, warning);
            current.Show();
            current.timer.Start();
        }

        // Returns true if a toast was open (the caller may want to wait a moment before taking a screenshot).
        public static bool CloseCurrent()
        {
            if (current != null && !current.IsDisposed && current.Visible) { current.Close(); current = null; return true; }
            return false;
        }
    }

    // Shows a capture and lets the user drag the rectangle that contains the contract text.
    public class RegionSelectForm : Form
    {
        readonly Bitmap image;
        Point dragStart; Rectangle sel; bool dragging;
        public RectangleF Result;   // fractions of the image (0..1)
        readonly Panel canvas;

        public RegionSelectForm(Bitmap img, RectangleF current)
        {
            image = img;
            Text = "Select the contract text area - drag a box, then press Enter (Esc = cancel)";
            StartPosition = FormStartPosition.CenterScreen;
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Size = new Size((int)(wa.Width * 0.9), (int)(wa.Height * 0.9));
            KeyPreview = true;
            Theme.ApplyForm(this);

            canvas = new DoubleBufferedPanel();
            canvas.Dock = DockStyle.Fill;
            canvas.Paint += OnPaintCanvas;
            canvas.MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { dragging = true; dragStart = e.Location; sel = Rectangle.Empty; } };
            canvas.MouseMove += delegate(object s, MouseEventArgs e) { if (dragging) { sel = Norm(dragStart, e.Location); canvas.Invalidate(); } };
            canvas.MouseUp += delegate(object s, MouseEventArgs e) { dragging = false; canvas.Invalidate(); };
            canvas.Resize += delegate { canvas.Invalidate(); };

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Bottom; bar.Height = 40; bar.FlowDirection = FlowDirection.RightToLeft; bar.Padding = new Padding(6);
            bar.BackColor = Theme.Header; bar.Height = 46;
            Button ok = Theme.MakeButton("Use Selection", Theme.ButtonKind.Primary, delegate { Accept(); });
            Button full = Theme.MakeButton("Use Whole Window", Theme.ButtonKind.Secondary, delegate { Result = new RectangleF(0, 0, 1, 1); DialogResult = DialogResult.OK; });
            Button cancel = Theme.MakeButton("Cancel", Theme.ButtonKind.Secondary, delegate { DialogResult = DialogResult.Cancel; });
            Label hint = new Label(); hint.AutoSize = true; hint.Padding = new Padding(0, 9, 20, 0); hint.ForeColor = Theme.Muted;
            hint.Text = "Tip: include the contract title, objectives and reward - skip the contract list.";
            bar.Controls.Add(cancel); bar.Controls.Add(full); bar.Controls.Add(ok); bar.Controls.Add(hint);
            Controls.Add(canvas); Controls.Add(bar);

            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) Accept();
                if (e.KeyCode == Keys.Escape) DialogResult = DialogResult.Cancel;
            };
            Shown += delegate
            {
                if (current.Width > 0 && current.Height > 0 && !(current.Width >= 0.999f && current.Height >= 0.999f))
                {
                    Rectangle ir = ImageRect();
                    sel = new Rectangle(ir.X + (int)(current.X * ir.Width), ir.Y + (int)(current.Y * ir.Height),
                                        (int)(current.Width * ir.Width), (int)(current.Height * ir.Height));
                    canvas.Invalidate();
                }
            };
        }

        static Rectangle Norm(Point a, Point b)
        {
            return new Rectangle(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }

        Rectangle ImageRect()
        {
            Size c = canvas.ClientSize;
            double scale = Math.Min((double)c.Width / image.Width, (double)c.Height / image.Height);
            int w = (int)(image.Width * scale), h = (int)(image.Height * scale);
            return new Rectangle((c.Width - w) / 2, (c.Height - h) / 2, w, h);
        }

        void OnPaintCanvas(object sender, PaintEventArgs e)
        {
            Rectangle ir = ImageRect();
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            e.Graphics.DrawImage(image, ir);
            if (sel.Width > 2 && sel.Height > 2)
            {
                using (Region outside = new Region(ir))
                using (SolidBrush dim = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
                {
                    outside.Exclude(sel);
                    e.Graphics.FillRegion(dim, outside);
                }
                using (Pen p = new Pen(Theme.Highlight, 2)) e.Graphics.DrawRectangle(p, sel);
            }
        }

        void Accept()
        {
            Rectangle ir = ImageRect();
            Rectangle s = Rectangle.Intersect(sel, ir);
            if (s.Width < 10 || s.Height < 10) { MessageBox.Show(this, "Drag a box around the contract text first."); return; }
            Result = new RectangleF((float)(s.X - ir.X) / ir.Width, (float)(s.Y - ir.Y) / ir.Height,
                                    (float)s.Width / ir.Width, (float)s.Height / ir.Height);
            DialogResult = DialogResult.OK;
        }

        class DoubleBufferedPanel : Panel
        {
            public DoubleBufferedPanel() { DoubleBuffered = true; ResizeRedraw = true; Cursor = Cursors.Cross; }
        }
    }

    public class TextViewForm : Form
    {
        public TextViewForm(string title, string text, Image image)
        {
            Text = title;
            Size = new Size(1000, 700);
            StartPosition = FormStartPosition.CenterParent;
            Theme.ApplyForm(this);
            TextBox tb = new TextBox();
            tb.Multiline = true; tb.ReadOnly = true; tb.ScrollBars = ScrollBars.Both; tb.WordWrap = false;
            tb.Font = Theme.Mono; tb.Text = text; tb.Dock = DockStyle.Fill; Theme.StyleInput(tb);
            if (image != null)
            {
                SplitContainer sc = new SplitContainer();
                sc.Dock = DockStyle.Fill; sc.Orientation = Orientation.Vertical;
                PictureBox pb = new PictureBox(); pb.Dock = DockStyle.Fill; pb.SizeMode = PictureBoxSizeMode.Zoom; pb.Image = image;
                pb.BackColor = Theme.Bg; sc.BackColor = Theme.Bg;
                sc.Panel1.Controls.Add(pb); sc.Panel2.Controls.Add(tb);
                Controls.Add(sc);
                Load += delegate { sc.SplitterDistance = Width / 2; };
            }
            else Controls.Add(tb);
        }
    }
}

namespace HaulingManager
{
    // Small themed "enter a number" dialog.
    public class InputForm : Form
    {
        readonly TextBox box = new TextBox();
        public int Value;

        InputForm(string title, string prompt, int initial)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(460, 200);
            Theme.ApplyForm(this);
            TopMost = true;

            Label l = new Label { Text = prompt, AutoSize = true, MaximumSize = new Size(410, 0), Location = new Point(16, 14), Font = Theme.Semibold };
            Label unit = new Label { Text = "SCU", AutoSize = true, ForeColor = Theme.Muted, Location = new Point(132, 78) };
            box.Location = new Point(16, 74); box.Width = 110; box.Font = new Font("Segoe UI Semibold", 12f);
            Theme.StyleInput(box); box.ForeColor = Theme.Highlight;
            box.Text = initial.ToString();
            box.KeyPress += delegate(object s, KeyPressEventArgs e) { if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled = true; };
            Label hint = new Label { Text = "The rest of the contract total is spread over the remaining pickups and the route is re-planned.", AutoSize = true,
                                     MaximumSize = new Size(425, 0), ForeColor = Theme.Muted, Font = Theme.Small, Location = new Point(16, 110) };
            Button ok = Theme.MakeButton("Save", Theme.ButtonKind.Primary, delegate { Accept(); });
            Button cancel = Theme.MakeButton("Cancel", Theme.ButtonKind.Secondary, null); cancel.DialogResult = DialogResult.Cancel;
            ok.Location = new Point(270, 154); cancel.Location = new Point(362, 154);
            Controls.Add(l); Controls.Add(box); Controls.Add(unit); Controls.Add(hint); Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
            Shown += delegate { box.Focus(); box.SelectAll(); };
        }

        void Accept()
        {
            int v;
            if (!int.TryParse(box.Text, out v)) { box.Focus(); return; }
            Value = v; DialogResult = DialogResult.OK;
        }

        public static int? AskInt(IWin32Window owner, string title, string prompt, int initial)
        {
            using (InputForm f = new InputForm(title, prompt, initial))
                return f.ShowDialog(owner) == DialogResult.OK ? (int?)f.Value : null;
        }
    }
}

namespace HaulingManager
{
    // Type-to-search for a ComboBox: shows every item that contains what you typed (any word, any position),
    // e.g. "drake" lists all Drake ships, "iron" finds the Ironclad.
    public class SuggestCombo
    {
        readonly ComboBox cb;
        List<string> all = new List<string>();
        bool busy;
        public bool Busy { get { return busy; } }
        public event Action<string> Picked;   // an item was chosen from the list (click or Enter)

        public SuggestCombo(ComboBox combo)
        {
            cb = combo;
            cb.AutoCompleteMode = AutoCompleteMode.None;
            cb.DropDownStyle = ComboBoxStyle.DropDown;
            cb.TextUpdate += delegate { Filter(); };
            cb.SelectionChangeCommitted += delegate
            {
                string s = cb.SelectedItem as string;
                if (s != null && Picked != null) BeginLater(() => Picked(s));
            };
            cb.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                string pick = cb.SelectedItem as string;
                if (pick == null || !Matches(pick, cb.Text)) pick = Match(cb.Text).FirstOrDefault();
                cb.DroppedDown = false;
                if (pick != null && Picked != null) { string p = pick; BeginLater(() => Picked(p)); }
            };
            cb.DropDownClosed += delegate { if (!busy) Cursor.Current = Cursors.Default; };
        }

        void BeginLater(Action a)
        {
            if (cb.IsHandleCreated) cb.BeginInvoke(a); else a();
        }

        public void SetItems(IEnumerable<string> items)
        {
            all = items.ToList();
            Reset();
        }

        // puts the full list back (keeps the current text)
        public void Reset()
        {
            busy = true;
            string t = cb.Text;
            cb.BeginUpdate(); cb.Items.Clear(); cb.Items.AddRange(all.Cast<object>().ToArray()); cb.EndUpdate();
            cb.Text = t;
            busy = false;
        }

        static bool Matches(string item, string text)
        {
            // short bits ("c", "c2") must start a word; longer ones can be anywhere ("iron" finds Ironclad)
            string[] words = (text ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string[] parts = item.Split(new[] { ' ', '-', '.', '(', ')', '\'' }, StringSplitOptions.RemoveEmptyEntries);
            return words.All(w => w.Length >= 3
                ? item.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0
                : parts.Any(p => p.StartsWith(w, StringComparison.OrdinalIgnoreCase)));
        }

        public List<string> Match(string text)
        {
            return all.Where(x => Matches(x, text))
                      .OrderBy(x => x.StartsWith(text ?? "", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(x => x).ToList();
        }

        void Filter()
        {
            if (busy) return;
            busy = true;
            try
            {
                string t = cb.Text;
                int caret = cb.SelectionStart;
                List<string> m = t.Trim().Length == 0 ? all : Match(t);
                cb.BeginUpdate();
                cb.Items.Clear();
                cb.Items.AddRange((m.Count > 0 ? m : all).Cast<object>().ToArray());
                cb.EndUpdate();
                bool show = t.Trim().Length > 0 && m.Count > 0;
                if (cb.DroppedDown != show) cb.DroppedDown = show;
                // opening the list makes the ComboBox replace / select the text - put back what was typed
                cb.Text = t;
                cb.SelectionStart = Math.Min(caret, t.Length);
                cb.SelectionLength = 0;
                Cursor.Current = Cursors.Default;
            }
            finally { busy = false; }
        }
    }
}

namespace HaulingManager
{
    // "Reward not found" prompt after a capture. Star Citizen sometimes doesn't draw the reward until you click
    // off the contract and back on, so the player can type it here or read the contract again.
    public class RewardForm : System.Windows.Forms.Form
    {
        readonly System.Windows.Forms.TextBox box = new System.Windows.Forms.TextBox();
        public long Value;
        public bool ReadAgain;

        RewardForm(string contractTitle)
        {
            Text = "Reward not found";
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            ClientSize = new System.Drawing.Size(520, 250);
            Theme.ApplyForm(this);
            TopMost = true;
            System.Windows.Forms.Label l = new System.Windows.Forms.Label
            {
                Text = "The aUEC reward for this contract couldn't be read" + (string.IsNullOrEmpty(contractTitle) ? "." : ":\r\n" + contractTitle),
                AutoSize = false, Size = new System.Drawing.Size(490, 44), Location = new System.Drawing.Point(16, 12), Font = Theme.Semibold, AutoEllipsis = true
            };
            System.Windows.Forms.Label tip = new System.Windows.Forms.Label
            {
                Text = "Star Citizen sometimes doesn't show the reward until you click off the contract and back on. " +
                       "Check it in mobiGlas, then type the reward here, or press Read again.",
                AutoSize = false, Size = new System.Drawing.Size(490, 52), ForeColor = Theme.Muted, Location = new System.Drawing.Point(16, 60)
            };
            System.Windows.Forms.Label cur = new System.Windows.Forms.Label { Text = "aUEC", AutoSize = true, ForeColor = Theme.Muted, Location = new System.Drawing.Point(206, 128) };
            box.Location = new System.Drawing.Point(16, 122); box.Width = 184; box.Font = new System.Drawing.Font("Segoe UI Semibold", 12f);
            Theme.StyleInput(box); box.ForeColor = Theme.Highlight;
            box.KeyPress += delegate(object s, System.Windows.Forms.KeyPressEventArgs e)
            {
                if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar) && e.KeyChar != ',') e.Handled = true;
            };
            System.Windows.Forms.Button ok = Theme.MakeButton("Save reward", Theme.ButtonKind.Primary, delegate { Accept(); });
            System.Windows.Forms.Button again = Theme.MakeButton("Read again", Theme.ButtonKind.Accent, delegate { ReadAgain = true; DialogResult = System.Windows.Forms.DialogResult.Retry; });
            System.Windows.Forms.Button skip = Theme.MakeButton("Skip", Theme.ButtonKind.Secondary, null); skip.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            ok.Location = new System.Drawing.Point(16, 200); again.Location = new System.Drawing.Point(140, 200); skip.Location = new System.Drawing.Point(440, 200);
            Controls.Add(l); Controls.Add(tip); Controls.Add(box); Controls.Add(cur); Controls.Add(ok); Controls.Add(again); Controls.Add(skip);
            AcceptButton = ok; CancelButton = skip;
            Shown += delegate { Activate(); box.Focus(); };
        }

        void Accept()
        {
            long v;
            string digits = new string(box.Text.Where(char.IsDigit).ToArray());
            if (!long.TryParse(digits, out v) || v <= 0) { box.Focus(); return; }
            Value = v;
            DialogResult = System.Windows.Forms.DialogResult.OK;
        }

        // OK = typed a reward (Value), Retry = read the contract again, Cancel = leave it blank for now
        public static System.Windows.Forms.DialogResult Ask(System.Windows.Forms.IWin32Window owner, string contractTitle, out long value)
        {
            using (RewardForm f = new RewardForm(contractTitle))
            {
                System.Windows.Forms.DialogResult r = f.ShowDialog(owner);
                value = f.Value;
                return r;
            }
        }
    }
}

namespace HaulingManager
{
    // One-line text prompt ("Name for this theme").
    public class TextPromptForm : System.Windows.Forms.Form
    {
        readonly System.Windows.Forms.TextBox box = new System.Windows.Forms.TextBox();
        TextPromptForm(string title, string prompt, string initial)
        {
            Text = title;
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            ClientSize = new System.Drawing.Size(420, 140);
            Theme.ApplyForm(this);
            System.Windows.Forms.Label l = new System.Windows.Forms.Label { Text = prompt, AutoSize = true, Location = new System.Drawing.Point(16, 14), Font = Theme.Semibold };
            box.Location = new System.Drawing.Point(16, 44); box.Width = 386; Theme.StyleInput(box); box.Text = initial ?? "";
            System.Windows.Forms.Button ok = Theme.MakeButton("Save", Theme.ButtonKind.Primary, delegate { if (box.Text.Trim().Length > 0) DialogResult = System.Windows.Forms.DialogResult.OK; });
            System.Windows.Forms.Button cancel = Theme.MakeButton("Cancel", Theme.ButtonKind.Secondary, null); cancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            ok.Location = new System.Drawing.Point(230, 92); cancel.Location = new System.Drawing.Point(320, 92);
            Controls.Add(l); Controls.Add(box); Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
            Shown += delegate { box.Focus(); box.SelectAll(); };
        }

        public static string Ask(System.Windows.Forms.IWin32Window owner, string title, string prompt, string initial)
        {
            using (TextPromptForm f = new TextPromptForm(title, prompt, initial))
                return f.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK ? f.box.Text.Trim() : null;
        }
    }
}
