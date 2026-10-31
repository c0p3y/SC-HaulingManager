// SC Hauling Manager - C0P3Y | Icarus Interstellar Inc.
// Dockable panel layout: the four main panels sit in a tree of side-by-side / stacked splits.
//  - drag a panel by its title bar onto another panel: a shaded preview shows where it will land
//    (left / right / top / bottom half, or the middle to swap the two), or onto the window edge to take a whole side
//  - drag the gaps between panels to resize; sizes are kept as proportions, so they survive window resizing
//  - each title bar has minimize (goes to the bar at the bottom) and close (bring it back from the Panels menu)
//  - double-click a title bar (or the hold's Expand button) to let one panel fill the window
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace HaulingManager
{
    public class DockNode
    {
        public string Id;            // leaf: panel id
        public bool Horizontal;      // split: true = A | B side by side, false = A over B
        public double Ratio = 0.5;   // share of A
        public DockNode A, B, Parent;
        public bool IsLeaf { get { return Id != null; } }
        public IEnumerable<string> Leaves()
        {
            if (IsLeaf) { yield return Id; yield break; }
            foreach (string s in A.Leaves()) yield return s;
            foreach (string s in B.Leaves()) yield return s;
        }
    }

    public class DockHost : Panel
    {
        public const string DefaultLayout = "V(0.5,H(0.5,contracts,route),H(0.5,cargo,hold))";
        const int Gap = 8, MinPanel = 140, EdgeZone = 28;

        readonly Panel area = new Panel { Dock = DockStyle.Fill };
        readonly FlowLayoutPanel tray = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34, Visible = false, WrapContents = false, Padding = new Padding(6, 3, 6, 0) };
        readonly Dictionary<string, Control> cards = new Dictionary<string, Control>();
        readonly Dictionary<string, string> titles = new Dictionary<string, string>();
        readonly List<string> order = new List<string>();
        readonly Dictionary<string, string> hidden = new Dictionary<string, string>();          // id -> "min" or "closed"
        readonly Dictionary<string, KeyValuePair<string, string>> returnTo = new Dictionary<string, KeyValuePair<string, string>>();  // id -> (neighbour, side)
        readonly List<KeyValuePair<Rectangle, DockNode>> splitters = new List<KeyValuePair<Rectangle, DockNode>>();
        readonly Dictionary<string, Rectangle> placed = new Dictionary<string, Rectangle>();
        readonly Label empty = new Label { Text = "All panels are closed - use Panels ▾ at the top to bring them back.", AutoSize = true, Location = new Point(20, 20) };
        DockNode root;
        string maximized;
        public event EventHandler LayoutChanged;
        public event EventHandler MaximizedChanged;

        public DockHost()
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg; area.BackColor = Theme.Bg; tray.BackColor = Theme.Header;
            empty.ForeColor = Theme.Muted;
            Controls.Add(area); Controls.Add(tray);
            area.Controls.Add(empty);
            area.Resize += delegate { DoLayoutPanels(); };
            area.MouseMove += OnAreaMouseMove;
            area.MouseDown += OnAreaMouseDown;
            area.MouseUp += delegate { if (dragSplit != null) { dragSplit = null; area.Capture = false; Raise(); } };
            typeof(Panel).InvokeMember("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.SetProperty, null, area, new object[] { true });
        }

        public string MaximizedId { get { return maximized; } }
        public IEnumerable<string> PanelIds { get { return order; } }
        public string TitleOf(string id) { return titles[id]; }
        public bool IsShown(string id) { return !hidden.ContainsKey(id); }

        // ---------------------------------------------------------------- panels
        // card = the panel to dock; title = its title bar, which gets the drag handle and the minimize / close buttons
        public void Add(string id, string title, Control card, Control titleBar)
        {
            cards[id] = card; titles[id] = title; order.Add(id);
            card.Dock = DockStyle.None;
            area.Controls.Add(card);
            Button close = TitleButton("✕", "Close this panel (bring it back from Panels ▾)");
            Button min = TitleButton("–", "Minimize to the bar at the bottom");
            close.Click += delegate { Hide(id, "closed"); };
            min.Click += delegate { Hide(id, "min"); };
            titleBar.Controls.Add(min); titleBar.Controls.Add(close);
            titleBar.Cursor = Cursors.SizeAll;
            new ToolTip().SetToolTip(titleBar, "Drag to move this panel. Double-click to fill the window.");
            titleBar.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { dragId = id; dragFrom = Cursor.Position; dragging = false; } };
            titleBar.MouseMove += (s, e) => OnTitleDrag();
            titleBar.MouseUp += (s, e) => OnTitleDrop();
            titleBar.DoubleClick += delegate { ToggleMaximize(id); };
        }

        static Button TitleButton(string text, string tip)
        {
            Button b = new Button { Text = text, Dock = DockStyle.Right, Width = 26, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, TabStop = false };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Theme.SurfaceHover;
            b.ForeColor = Theme.Muted; b.BackColor = Theme.Panel; b.Font = Theme.Semibold;
            new ToolTip().SetToolTip(b, tip);
            return b;
        }

        // ---------------------------------------------------------------- save / load ("V(0.5,H(0.5,contracts,route),H(0.5,cargo,hold))")
        public string SaveLayout() { return root == null ? "" : Write(root); }

        public string SaveHidden()
        {
            return string.Join(";", hidden.Select(kv =>
            {
                KeyValuePair<string, string> r; returnTo.TryGetValue(kv.Key, out r);
                return kv.Key + ":" + kv.Value + ":" + (r.Key ?? "") + ":" + (r.Value ?? "");
            }).ToArray());
        }

        static string Write(DockNode n)
        {
            if (n.IsLeaf) return n.Id;
            return (n.Horizontal ? "H(" : "V(") + n.Ratio.ToString("0.###", CultureInfo.InvariantCulture) + "," + Write(n.A) + "," + Write(n.B) + ")";
        }

        public void LoadLayout(string layout, string hiddenList)
        {
            DockNode n = null;
            try { int pos = 0; n = Parse(layout ?? "", ref pos); } catch { n = null; }
            List<string> leaves = n != null ? n.Leaves().ToList() : new List<string>();
            hidden.Clear(); returnTo.Clear();
            foreach (string part in (hiddenList ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = part.Split(':');
                if (f.Length >= 2 && cards.ContainsKey(f[0]) && !leaves.Contains(f[0]))
                {
                    hidden[f[0]] = f[1] == "closed" ? "closed" : "min";
                    if (f.Length >= 4) returnTo[f[0]] = new KeyValuePair<string, string>(f[2], f.Length >= 5 ? f[3] + ":" + f[4] : f[3]);
                }
            }
            // a valid layout has every panel exactly once, either placed or hidden
            bool ok = n != null && leaves.Distinct().Count() == leaves.Count && leaves.All(cards.ContainsKey) &&
                      cards.Keys.All(k => leaves.Contains(k) || hidden.ContainsKey(k));
            if (!ok) { hidden.Clear(); returnTo.Clear(); int p = 0; n = Parse(DefaultLayout, ref p); }
            root = n; if (root != null) root.Parent = null;
            maximized = null;
            Rebuild();
        }

        public void ResetLayout() { LoadLayout(DefaultLayout, ""); Raise(); }

        static DockNode Parse(string s, ref int i)
        {
            s = s.Trim();
            if (s.Length == 0) return null;
            if (i < s.Length && (s[i] == 'H' || s[i] == 'V') && i + 1 < s.Length && s[i + 1] == '(')
            {
                DockNode n = new DockNode { Horizontal = s[i] == 'H' };
                i += 2;
                int c = s.IndexOf(',', i);
                n.Ratio = Math.Max(0.05, Math.Min(0.95, double.Parse(s.Substring(i, c - i), CultureInfo.InvariantCulture)));
                i = c + 1;
                n.A = Parse(s, ref i); n.A.Parent = n;
                if (s[i] != ',') throw new FormatException(); i++;
                n.B = Parse(s, ref i); n.B.Parent = n;
                if (s[i] != ')') throw new FormatException(); i++;
                return n;
            }
            int start = i;
            while (i < s.Length && s[i] != ',' && s[i] != ')') i++;
            return new DockNode { Id = s.Substring(start, i - start).Trim() };
        }

        // ---------------------------------------------------------------- tree edits
        DockNode Find(DockNode n, string id)
        {
            if (n == null) return null;
            if (n.IsLeaf) return n.Id == id ? n : null;
            return Find(n.A, id) ?? Find(n.B, id);
        }

        // takes a leaf out; its sibling takes the parent's place. Remembers where it was so it can come back there.
        void Remove(string id)
        {
            DockNode leaf = Find(root, id);
            if (leaf == null) return;
            DockNode p = leaf.Parent;
            if (p == null) { root = null; returnTo.Remove(id); return; }
            DockNode sib = p.A == leaf ? p.B : p.A;
            string side = p.Horizontal ? (p.A == leaf ? "left" : "right") : (p.A == leaf ? "top" : "bottom");
            returnTo[id] = new KeyValuePair<string, string>(sib.Leaves().First(), side + ":" + (p.A == leaf ? p.Ratio : 1 - p.Ratio).ToString("0.###", CultureInfo.InvariantCulture));
            Replace(p, sib);
        }

        void Replace(DockNode old, DockNode now)
        {
            DockNode g = old.Parent;
            now.Parent = g;
            if (g == null) root = now;
            else if (g.A == old) g.A = now; else g.B = now;
        }

        // puts a panel next to a node: side = left / right / top / bottom; share = how much of the space it gets
        void Insert(string id, DockNode target, string side, double share)
        {
            DockNode leaf = new DockNode { Id = id };
            if (target == null) { root = leaf; return; }
            share = Math.Max(0.15, Math.Min(0.85, share));
            DockNode n = new DockNode { Horizontal = side == "left" || side == "right" };
            bool first = side == "left" || side == "top";
            DockNode parent = target.Parent;
            n.A = first ? leaf : target; n.B = first ? target : leaf;
            n.Ratio = first ? share : 1 - share;
            n.Parent = parent;
            if (parent == null) root = n; else if (parent.A == target) parent.A = n; else parent.B = n;
            n.A.Parent = n; n.B.Parent = n;
        }

        public void Hide(string id, string how)
        {
            if (maximized == id) ToggleMaximize(id);
            if (Find(root, id) == null && !hidden.ContainsKey(id)) return;
            Remove(id);
            hidden[id] = how;
            Rebuild(); Raise();
        }

        public void Show(string id)
        {
            if (!hidden.ContainsKey(id)) return;
            hidden.Remove(id);
            KeyValuePair<string, string> r;
            DockNode target = null; string side = "right"; double share = 0.5;
            if (returnTo.TryGetValue(id, out r)) { target = Find(root, r.Key); string[] f = (r.Value ?? "").Split(':'); if (f.Length > 0 && f[0].Length > 0) side = f[0]; if (f.Length > 1) double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out share); }
            if (target == null) { target = root; side = "right"; share = 0.4; }
            Insert(id, target, side, share);
            Rebuild(); Raise();
        }

        public void ToggleMaximize(string id)
        {
            maximized = maximized == id ? null : id;
            if (maximized != null && !cards.ContainsKey(maximized)) maximized = null;
            DoLayoutPanels();
            if (MaximizedChanged != null) MaximizedChanged(this, EventArgs.Empty);
        }

        void Raise() { if (LayoutChanged != null) LayoutChanged(this, EventArgs.Empty); }

        // ---------------------------------------------------------------- layout
        void Rebuild()
        {
            tray.Controls.Clear();
            foreach (string id in order.Where(x => hidden.ContainsKey(x) && hidden[x] == "min"))
            {
                string cap = id;
                Button b = Theme.MakeButton("▢  " + titles[id], Theme.ButtonKind.Secondary, delegate { Show(cap); });
                new ToolTip().SetToolTip(b, "Restore " + titles[id]);
                tray.Controls.Add(b);
            }
            tray.Visible = tray.Controls.Count > 0;
            DoLayoutPanels();
        }

        void DoLayoutPanels()
        {
            splitters.Clear(); placed.Clear();
            Rectangle all = new Rectangle(Gap, Gap / 2, Math.Max(1, area.ClientSize.Width - 2 * Gap), Math.Max(1, area.ClientSize.Height - Gap));
            area.SuspendLayout();
            foreach (KeyValuePair<string, Control> kv in cards)
            {
                bool vis = maximized != null ? kv.Key == maximized : Find(root, kv.Key) != null;
                kv.Value.Visible = vis;
            }
            if (maximized != null) { cards[maximized].Bounds = all; placed[maximized] = all; }
            else if (root != null) Place(root, all);
            empty.Visible = root == null && maximized == null;
            area.ResumeLayout();
            area.Invalidate();
        }

        void Place(DockNode n, Rectangle r)
        {
            if (n.IsLeaf) { cards[n.Id].Bounds = r; placed[n.Id] = r; return; }
            if (n.Horizontal)
            {
                int avail = Math.Max(0, r.Width - Gap);
                int a = Clamp((int)Math.Round(avail * n.Ratio), avail);
                Place(n.A, new Rectangle(r.X, r.Y, a, r.Height));
                splitters.Add(new KeyValuePair<Rectangle, DockNode>(new Rectangle(r.X + a, r.Y, Gap, r.Height), n));
                Place(n.B, new Rectangle(r.X + a + Gap, r.Y, avail - a, r.Height));
            }
            else
            {
                int avail = Math.Max(0, r.Height - Gap);
                int a = Clamp((int)Math.Round(avail * n.Ratio), avail);
                Place(n.A, new Rectangle(r.X, r.Y, r.Width, a));
                splitters.Add(new KeyValuePair<Rectangle, DockNode>(new Rectangle(r.X, r.Y + a, r.Width, Gap), n));
                Place(n.B, new Rectangle(r.X, r.Y + a + Gap, r.Width, avail - a));
            }
        }

        static int Clamp(int a, int avail)
        {
            if (avail < 2 * MinPanel) return Math.Max(0, Math.Min(avail, a));
            return Math.Max(MinPanel, Math.Min(avail - MinPanel, a));
        }

        // ---------------------------------------------------------------- resizing with the gaps
        KeyValuePair<Rectangle, DockNode>? dragSplit;
        Rectangle dragSpan;

        DockNode SplitAt(Point p, out Rectangle span)
        {
            span = Rectangle.Empty;
            foreach (KeyValuePair<Rectangle, DockNode> s in splitters)
                if (s.Key.Contains(p)) { span = SpanOf(s.Value); return s.Value; }
            return null;
        }

        Rectangle SpanOf(DockNode n)
        {
            List<Rectangle> rs = n.Leaves().Where(placed.ContainsKey).Select(id => placed[id]).ToList();
            if (rs.Count == 0) return Rectangle.Empty;
            int x0 = rs.Min(r => r.Left), y0 = rs.Min(r => r.Top), x1 = rs.Max(r => r.Right), y1 = rs.Max(r => r.Bottom);
            return Rectangle.FromLTRB(x0, y0, x1, y1);
        }

        void OnAreaMouseDown(object sender, MouseEventArgs e)
        {
            Rectangle span;
            DockNode n = SplitAt(e.Location, out span);
            if (n == null || e.Button != MouseButtons.Left) return;
            dragSplit = splitters.First(s => s.Value == n);
            dragSpan = span;
            area.Capture = true;
        }

        void OnAreaMouseMove(object sender, MouseEventArgs e)
        {
            if (dragSplit != null)
            {
                DockNode n = dragSplit.Value.Value;
                double total = n.Horizontal ? dragSpan.Width - Gap : dragSpan.Height - Gap;
                if (total <= 0) return;
                double pos = n.Horizontal ? e.X - dragSpan.X - Gap / 2.0 : e.Y - dragSpan.Y - Gap / 2.0;
                double min = Math.Min(0.45, MinPanel / total);
                n.Ratio = Math.Max(min, Math.Min(1 - min, pos / total));
                DoLayoutPanels();
                return;
            }
            Rectangle span;
            DockNode h = SplitAt(e.Location, out span);
            area.Cursor = h == null ? Cursors.Default : h.Horizontal ? Cursors.VSplit : Cursors.HSplit;
        }

        // ---------------------------------------------------------------- moving panels by their title bar
        string dragId; Point dragFrom; bool dragging;
        Form shadow;
        string dropTarget, dropSide;

        string frozenLayout, previewKey;
        Dictionary<string, Rectangle> frozen = new Dictionary<string, Rectangle>();   // where panels were when the drag began (hit-testing uses these)

        void OnTitleDrag()
        {
            if (dragId == null || (Control.MouseButtons & MouseButtons.Left) == 0) return;
            Point now = Cursor.Position;
            if (!dragging)
            {
                if (Math.Abs(now.X - dragFrom.X) + Math.Abs(now.Y - dragFrom.Y) < 8) return;
                if (maximized != null) return;   // can't rearrange while one panel fills the window
                dragging = true;
                frozenLayout = Write(root);
                frozen = new Dictionary<string, Rectangle>(placed);
                previewKey = null;
            }
            ComputeDrop(area.PointToClient(now));
            string key = dropTarget + "|" + dropSide;
            if (key == previewKey) return;
            previewKey = key;
            // live preview: lay the other panels out as if dropped here; the shadow marks where this one lands
            Rectangle land = Rectangle.Empty;
            Dictionary<string, KeyValuePair<string, string>> keep = new Dictionary<string, KeyValuePair<string, string>>(returnTo);
            if (dropSide != null && ApplyDrop(dragId, dropTarget, dropSide))
            {
                DoLayoutPanels();
                placed.TryGetValue(dragId, out land);
                cards[dragId].Visible = false;
            }
            else
            {
                RestoreFrozen();
                DoLayoutPanels();
            }
            RestoreFrozen();
            returnTo.Clear(); foreach (var kv in keep) returnTo[kv.Key] = kv.Value;
            ShowShadow(land.IsEmpty ? Rectangle.Empty : area.RectangleToScreen(land));
        }

        void RestoreFrozen()
        {
            int pos = 0;
            root = Parse(frozenLayout, ref pos);
            if (root != null) root.Parent = null;
        }

        void OnTitleDrop()
        {
            string id = dragId; bool was = dragging;
            dragId = null; dragging = false;
            ShowShadow(Rectangle.Empty);
            if (!was || id == null) return;
            RestoreFrozen();
            placed.Clear(); foreach (var kv in frozen) placed[kv.Key] = kv.Value;
            if (dropSide == null || !ApplyDrop(id, dropTarget, dropSide)) { RestoreFrozen(); DoLayoutPanels(); return; }
            returnTo.Remove(id);
            Rebuild(); Raise();
        }

        // changes the tree for a drop; false = nothing would change
        bool ApplyDrop(string id, string target, string side)
        {
            if (side == "swap")
            {
                DockNode a = Find(root, id), b = Find(root, target);
                if (a == null || b == null || a == b) return false;
                a.Id = target; b.Id = id;
                return true;
            }
            if (Find(root, id) == null || target == id) return false;
            if (target == "*root")
            {
                Remove(id);
                if (root == null) return false;
                Insert(id, root, side, 0.33);
                return true;
            }
            Remove(id);
            DockNode t = Find(root, target);
            if (t == null) Insert(id, root, "right", 0.4);
            else Insert(id, t, side, 0.5);
            return true;
        }

        // where a drop at p would land, as a rectangle in area coordinates (empty = nowhere)
        Rectangle ComputeDrop(Point p)
        {
            dropTarget = null; dropSide = null;
            Rectangle all = area.ClientRectangle;
            if (!all.Contains(p)) return Rectangle.Empty;
            // window edges: take a whole side
            if (p.X < EdgeZone) { dropTarget = "*root"; dropSide = "left"; return new Rectangle(0, 0, all.Width / 3, all.Height); }
            if (p.X > all.Width - EdgeZone) { dropTarget = "*root"; dropSide = "right"; return new Rectangle(all.Width * 2 / 3, 0, all.Width / 3, all.Height); }
            if (p.Y < EdgeZone / 2) { dropTarget = "*root"; dropSide = "top"; return new Rectangle(0, 0, all.Width, all.Height / 3); }
            if (p.Y > all.Height - EdgeZone) { dropTarget = "*root"; dropSide = "bottom"; return new Rectangle(0, all.Height * 2 / 3, all.Width, all.Height / 3); }
            foreach (KeyValuePair<string, Rectangle> kv in frozen)
            {
                Rectangle r = kv.Value;
                if (!r.Contains(p)) continue;
                double fx = (p.X - r.X) / (double)Math.Max(1, r.Width), fy = (p.Y - r.Y) / (double)Math.Max(1, r.Height);
                if (kv.Key == dragId) return Rectangle.Empty;
                dropTarget = kv.Key;
                if (fx > 0.3 && fx < 0.7 && fy > 0.3 && fy < 0.7) { dropSide = "swap"; return r; }
                double dl = fx, dr = 1 - fx, dt = fy, db = 1 - fy, m = Math.Min(Math.Min(dl, dr), Math.Min(dt, db));
                if (m == dl) { dropSide = "left"; return new Rectangle(r.X, r.Y, r.Width / 2, r.Height); }
                if (m == dr) { dropSide = "right"; return new Rectangle(r.X + r.Width / 2, r.Y, r.Width - r.Width / 2, r.Height); }
                if (m == dt) { dropSide = "top"; return new Rectangle(r.X, r.Y, r.Width, r.Height / 2); }
                dropSide = "bottom"; return new Rectangle(r.X, r.Y + r.Height / 2, r.Width, r.Height - r.Height / 2);
            }
            return Rectangle.Empty;
        }

        void ShowShadow(Rectangle screen)
        {
            if (screen.IsEmpty)
            {
                if (shadow != null) shadow.Hide();
                return;
            }
            if (shadow == null || shadow.IsDisposed)
            {
                shadow = new ShadowForm();
            }
            shadow.Bounds = screen;
            if (!shadow.Visible) shadow.Show(FindForm());
        }

        class ShadowForm : Form
        {
            public ShadowForm()
            {
                FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
                BackColor = Theme.Accent; Opacity = 0.28; TopMost = true;
            }
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams
            {
                get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x80 | 0x08000000 | 0x20; return cp; }   // tool window, no activate, click-through
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                using (Pen p = new Pen(Theme.Highlight, 3)) e.Graphics.DrawRectangle(p, 1, 1, Width - 3, Height - 3);
            }
        }
    }
}
