// SC Hauling Manager - C0P3Y | Icarus Interstellar Inc.
// The cargo hold quadrant: MANIFEST (containers waiting to be loaded), the 3D hold, and DELIVERED.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace HaulingManager
{
    public class HoldPanel : Panel
    {
        public readonly HoldView View = new HoldView();
        public HoldModel Model;
        public Func<CargoBox, int> RankOf = b => 0;
        public Func<CargoBox, string> DestinationOf = b => b.Dropoff;
        public Func<CargoBox, string> ContractLabelOf = b => "#" + b.ContractNumber;
        public Action<int> Undeliver;          // record id
        public event EventHandler Changed;
        public Action<string> Status = s => { };
        public Func<int> ExtraNeeded = () => 0;        // SCU still to be picked up later on the route (beyond what's aboard)
        public event EventHandler PackModeChanged;
        public event EventHandler ExpandClicked;
        Button expandBtn;

        public void SetExpanded(bool on) { if (expandBtn != null) expandBtn.Text = on ? "Restore" : "Expand"; View.Invalidate(); }
        readonly ComboBox modeBox = new ComboBox();
        public static readonly string[] PackModes = { "Tightest fit", "Group by cargo + destination", "Cargo groups + 1 SCU gaps", "Destination groups + 1 SCU gaps" };

        public int PackMode
        {
            get { return Math.Max(0, modeBox.SelectedIndex); }
            set { modeBox.SelectedIndex = Math.Max(0, Math.Min(PackModes.Length - 1, value)); }
        }

        string GroupKey(CargoBox b) { return LocationDb.KeyOf(DestinationOf(b)) + "|" + LocationDb.KeyOf(b.Material); }
        string DestKey(CargoBox b) { return LocationDb.KeyOf(DestinationOf(b)); }
        static string CargoKey(CargoBox b) { return LocationDb.KeyOf(b.Material); }

        readonly ListBox manifest = new ListBox(), delivered = new ListBox();
        readonly Label shipLabel = new Label(), manifestTitle = new Label(), deliveredTitle = new Label();
        readonly Dictionary<string, Color> destColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        // Destination colours, handed out in drop-off order. Editable as "HoldColors" in settings.json.
        public const string DefaultColors = "5CC8FF,FF7AC6,4FE0A0,A78BFA,2DD4BF,FF6B6B,60A5FA,C084FC,A3E635,818CF8,B8C7D9,F0F4F8";
        public const string OldDefaultColors = "5CC8FF,FF7AC6,4FE0A0,FF9F43,A78BFA,2DD4BF,FF6B6B,60A5FA,C084FC,A3E635,FB923C,B8C7D9";
        static Color[] Palette = ParseColors(DefaultColors);

        public static Color[] ParseColors(string list)
        {
            List<Color> l = new List<Color>();
            foreach (string part in (list ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string h = part.Trim().TrimStart('#');
                int v;
                if (h.Length == 6 && int.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out v))
                    l.Add(Color.FromArgb(255, (v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF));
            }
            return l.ToArray();
        }

        public static void SetColors(string list)
        {
            Color[] p = ParseColors(list);
            Palette = p.Length > 0 ? p : ParseColors(DefaultColors);
        }

        class Item
        {
            public List<CargoBox> Boxes = new List<CargoBox>();
            public string Text = "", Sub = "";
            public Color Color;
            public int Record;
        }

        public HoldPanel(HoldModel model)
        {
            Model = model;
            Dock = DockStyle.Fill;
            View.Model = model;
            View.Dock = DockStyle.Fill;
            View.ColorOf = b => ColorFor(DestinationOf(b));
            View.Describe = b => b.Size + " SCU  " + b.Material + "  " + ContractLabelOf(b) + "  →  " + (string.IsNullOrEmpty(DestinationOf(b)) ? "?" : DestinationOf(b));
            View.Legend = LegendItems;
            View.DestShort = b => PlaceName(DestinationOf(b));
            View.FindBox = id => Model.State.Boxes.FirstOrDefault(b => b.Id == id && b.State != BoxState.Delivered && b.State != BoxState.Archived);
            View.Changed += delegate { RefreshLists(); RaiseChanged(); };

            // ---- toolbar
            Panel bar = new Panel { Dock = DockStyle.Top, Height = 38 };
            shipLabel.Dock = DockStyle.Fill; shipLabel.TextAlign = ContentAlignment.MiddleLeft; shipLabel.Font = Theme.Semibold; shipLabel.AutoEllipsis = true;
            FlowLayoutPanel btns = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, 2, 0, 0) };
            modeBox.DropDownStyle = ComboBoxStyle.DropDownList; Theme.StyleInput(modeBox); modeBox.FlatStyle = FlatStyle.Flat;
            modeBox.Width = 250; modeBox.Margin = new Padding(0, 6, 6, 0);
            modeBox.Items.AddRange(PackModes); modeBox.SelectedIndex = 0;
            new ToolTip().SetToolTip(modeBox, "How PACK CARGO and Repack lay out the containers.\r\n" +
                "Tightest fit: least space, cargo for later stops deepest.\r\n" +
                "Group by cargo + destination: each cargo for each stop stacked together, in drop-off order.\r\n" +
                "Cargo groups + gaps: as above, with a 1 SCU gap between every group.\r\n" +
                "Destination groups + gaps: everything for one stop together (sorted by cargo), with a 1 SCU gap between stops.\r\n" +
                "Gaps are only used when the hold has room for the rest of the route.");
            modeBox.SelectedIndexChanged += delegate { if (PackModeChanged != null) PackModeChanged(this, EventArgs.Empty); };
            btns.Controls.Add(modeBox);
            btns.Controls.Add(Theme.MakeButton("PACK CARGO", Theme.ButtonKind.Primary, delegate { PackManifest(); }));
            btns.Controls.Add(Theme.MakeButton("Repack…", Theme.ButtonKind.Accent, delegate { Repack(); }));
            btns.Controls.Add(Small("⟲", "Turn view left (Q)", delegate { View.TurnView(-45); }));
            btns.Controls.Add(Small("⟳", "Turn view right (E)", delegate { View.TurnView(45); }));
            btns.Controls.Add(Small("Top", "Look from above", delegate { View.TopView(); }));
            btns.Controls.Add(Small("Reset", "Reset the view", delegate { View.ResetView(); }));
            expandBtn = Theme.MakeButton("Expand", Theme.ButtonKind.Accent, delegate { if (ExpandClicked != null) ExpandClicked(this, EventArgs.Empty); });
            new ToolTip().SetToolTip(expandBtn, "Make the cargo hold fill the window (press again to restore the other panels)");
            btns.Controls.Add(expandBtn);
            bar.Controls.Add(shipLabel); bar.Controls.Add(btns);

            // ---- left side: manifest + delivered
            TableLayoutPanel side = new TableLayoutPanel { Dock = DockStyle.Left, Width = 240, ColumnCount = 1, RowCount = 5, Padding = new Padding(0, 0, 8, 0) };
            side.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            side.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
            side.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            side.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
            side.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            foreach (Label l in new[] { manifestTitle, deliveredTitle })
            {
                l.Dock = DockStyle.Fill; l.Font = Theme.SectionFont; l.ForeColor = Theme.Accent; l.TextAlign = ContentAlignment.BottomLeft;
            }
            foreach (ListBox lb in new[] { manifest, delivered })
            {
                lb.Dock = DockStyle.Fill; lb.BorderStyle = BorderStyle.None; lb.BackColor = Theme.Surface; lb.ForeColor = Theme.Text;
                lb.DrawMode = DrawMode.OwnerDrawFixed; lb.ItemHeight = 38; lb.IntegralHeight = false;
                lb.DrawItem += DrawListItem;
                ListBox captured = lb;
                lb.HandleCreated += delegate { NativeMethods.UseDarkScrollbars(captured.Handle); };
            }
            manifest.MouseDown += OnManifestMouseDown;
            manifest.MouseMove += OnManifestMouseMove;
            manifest.DoubleClick += delegate { PlaceSelectedManifest(); };
            manifest.SelectedIndexChanged += delegate
            {
                Item it = manifest.SelectedItem as Item;
                View.Highlight = null; View.Invalidate();
                if (it != null) Status("Drag it onto the ship, double-click to auto-place one, or press PACK CARGO.");
            };
            delivered.DoubleClick += delegate { UndeliverSelected(); };
            FlowLayoutPanel dBtns = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 4, 0, 0) };
            dBtns.Controls.Add(Theme.MakeButton("Undeliver", Theme.ButtonKind.Secondary, delegate { UndeliverSelected(); }));
            dBtns.Controls.Add(Theme.MakeButton("Clear list", Theme.ButtonKind.Secondary, delegate { ClearDelivered(); }));
            side.Controls.Add(manifestTitle, 0, 0);
            side.Controls.Add(manifest, 0, 1);
            side.Controls.Add(deliveredTitle, 0, 2);
            side.Controls.Add(delivered, 0, 3);
            side.Controls.Add(dBtns, 0, 4);

            Controls.Add(View); Controls.Add(side); Controls.Add(bar);
            RefreshLists();
        }

        static Button Small(string text, string tip, EventHandler click)
        {
            Button b = Theme.MakeButton(text, Theme.ButtonKind.Secondary, click);
            b.Padding = new Padding(4, 3, 4, 3);
            new ToolTip().SetToolTip(b, tip);
            return b;
        }

        void RaiseChanged() { if (Changed != null) Changed(this, EventArgs.Empty); }

        public Color ColorFor(string dest)
        {
            string k = LocationDb.KeyOf(dest ?? "");
            Color c;
            if (!destColors.TryGetValue(k, out c)) { c = Palette[destColors.Count % Palette.Length]; destColors[k] = c; }
            return c;
        }

        // Colours follow the order cargo is delivered, so the legend reads like the route.
        public void AssignColors(IEnumerable<string> destinationsInOrder)
        {
            destColors.Clear();
            foreach (string d in destinationsInOrder) ColorFor(d);
        }

        List<KeyValuePair<string, Color>> LegendItems()
        {
            List<KeyValuePair<string, Color>> l = new List<KeyValuePair<string, Color>>();
            HashSet<string> seen = new HashSet<string>();
            foreach (CargoBox b in Model.State.Boxes.Where(x => x.State == BoxState.OnShip || x.State == BoxState.Manifest).OrderBy(RankOf))
            {
                string d = DestinationOf(b);
                if (!seen.Add(LocationDb.KeyOf(d))) continue;
                int scu = Model.OnShip().Where(x => LocationDb.KeyOf(DestinationOf(x)) == LocationDb.KeyOf(d)).Sum(x => x.Size);
                l.Add(new KeyValuePair<string, Color>(PlaceName(d) + (scu > 0 ? "  \u00B7  " + scu + " SCU" : ""), ColorFor(d)));
            }
            return l;
        }

        // "Rod's Fuel 'N Supplies at the L4 Lagrange of Pyro V" -> "Rod's Fuel 'N Supplies"
        public static string PlaceName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "?";
            string[] cuts = { " at the ", " above ", " on ", " orbiting " };
            foreach (string c in cuts)
            {
                int i = s.IndexOf(c, StringComparison.OrdinalIgnoreCase);
                if (i > 2) s = s.Substring(0, i);
            }
            return s.Length > 28 ? s.Substring(0, 26) + "\u2026" : s;
        }

        static string Short(string s)
        {
            if (string.IsNullOrEmpty(s)) return "?";
            return s.Length > 46 ? s.Substring(0, 44) + "…" : s;
        }

        // ---------------------------------------------------------------- lists
        public void RefreshLists()
        {
            ShipInfo ship = Model.Ship;
            int cap = Model.Capacity(), loaded = Model.LoadedScu();
            shipLabel.Text = ship == null ? "No ship selected" :
                ship.Name + "    " + loaded.ToString("N0") + " / " + cap.ToString("N0") + " SCU on grid" +
                (Model.OffGridScu() > 0 ? "   +  " + Model.OffGridScu().ToString("N0") + " SCU off grid" : "") +
                (ship.Approximate() ? "   (approximate hold - no grid data)" : "");
            shipLabel.ForeColor = loaded > 0 ? Theme.Text : Theme.Muted;

            object selM = manifest.SelectedItem;
            manifest.BeginUpdate(); manifest.Items.Clear();
            foreach (var g in Model.InManifest().GroupBy(b => b.ContractNumber + "|" + b.LegIndex + "|" + b.Size).OrderBy(g => RankOf(g.First())).ThenByDescending(g => g.First().Size))
            {
                CargoBox f = g.First();
                Item it = new Item();
                it.Boxes = g.ToList();
                it.Text = g.Count() + " × " + f.Size + " SCU   " + f.Material;
                it.Sub = ContractLabelOf(f) + "  →  " + Short(DestinationOf(f));
                it.Color = ColorFor(DestinationOf(f));
                manifest.Items.Add(it);
            }
            manifest.EndUpdate();
            int manScu = Model.InManifest().Sum(b => b.Size);
            manifestTitle.Text = "MANIFEST  -  to load" + (manScu > 0 ? "   (" + Model.InManifest().Count() + " boxes, " + manScu + " SCU)" : "");

            delivered.BeginUpdate(); delivered.Items.Clear();
            foreach (var g in Model.State.Boxes.Where(b => b.State == BoxState.Delivered).GroupBy(b => b.DeliveredRecord).OrderByDescending(g => g.Key))
            {
                CargoBox f = g.First();
                Item it = new Item();
                it.Boxes = g.ToList(); it.Record = g.Key;
                string list = Boxes.Pretty(Boxes.Format(CountSizes(g)));
                it.Text = "✔  " + list + "   " + f.Material;
                it.Sub = ContractLabelOf(f) + "  @  " + Short(f.DeliveredAt);
                it.Color = Theme.Muted;
                delivered.Items.Add(it);
            }
            delivered.EndUpdate();
            deliveredTitle.Text = "DELIVERED";
            View.Invalidate();
        }

        static SortedDictionary<int, int> CountSizes(IEnumerable<CargoBox> boxes)
        {
            SortedDictionary<int, int> d = new SortedDictionary<int, int>();
            foreach (CargoBox b in boxes) { int c; d.TryGetValue(b.Size, out c); d[b.Size] = c + 1; }
            return d;
        }

        void DrawListItem(object sender, DrawItemEventArgs e)
        {
            ListBox lb = (ListBox)sender;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush bg = new SolidBrush(sel ? Theme.Selection : Theme.Surface)) e.Graphics.FillRectangle(bg, e.Bounds);
            if (e.Index < 0 || e.Index >= lb.Items.Count) return;
            Item it = (Item)lb.Items[e.Index];
            using (SolidBrush sw = new SolidBrush(it.Color)) e.Graphics.FillRectangle(sw, e.Bounds.X + 6, e.Bounds.Y + 8, 6, e.Bounds.Height - 16);
            using (SolidBrush tb = new SolidBrush(lb == delivered ? Theme.Muted : Theme.Text))
                e.Graphics.DrawString(it.Text, Theme.Semibold, tb, e.Bounds.X + 18, e.Bounds.Y + 2);
            using (SolidBrush mb = new SolidBrush(Theme.Muted))
                e.Graphics.DrawString(it.Sub, Theme.Small, mb, e.Bounds.X + 18, e.Bounds.Y + 20);
            using (Pen p = new Pen(Theme.GridLine)) e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        }

        Point dragStart; bool mouseDownOnItem;

        void OnManifestMouseDown(object sender, MouseEventArgs e)
        {
            mouseDownOnItem = manifest.IndexFromPoint(e.Location) >= 0;
            dragStart = e.Location;
        }

        void OnManifestMouseMove(object sender, MouseEventArgs e)
        {
            if (!mouseDownOnItem || e.Button != MouseButtons.Left) return;
            if (Math.Abs(e.X - dragStart.X) + Math.Abs(e.Y - dragStart.Y) < 6) return;
            mouseDownOnItem = false;
            int i = manifest.IndexFromPoint(dragStart);
            if (i < 0) return;
            Item it = (Item)manifest.Items[i];
            CargoBox b = it.Boxes.FirstOrDefault(x => x.State == BoxState.Manifest);
            if (b == null) return;
            DataObject d = new DataObject();
            d.SetData(HoldView.DragFormat, b.Id.ToString());
            manifest.DoDragDrop(d, DragDropEffects.Move);
        }

        void PlaceSelectedManifest()
        {
            Item it = manifest.SelectedItem as Item;
            if (it == null) return;
            CargoBox b = it.Boxes.FirstOrDefault(x => x.State == BoxState.Manifest);
            if (b == null) return;
            string note; int n = DoPack(new List<CargoBox> { b }, out note);
            Status(n == 1 ? "Placed one " + b.Size + " SCU container." : "No room for that container on this ship.");
            RefreshLists(); RaiseChanged();
        }

        // ---------------------------------------------------------------- packing
        public void PackManifest()
        {
            if (Model.Ship == null) { Status("Select your ship first (Route Plan)."); return; }
            List<CargoBox> todo = Model.InManifest().ToList();
            if (todo.Count == 0) { Status("Nothing in the manifest to pack - containers appear here when you record them."); return; }
            string note; int n = DoPack(todo, out note);
            int left = todo.Count - n;
            Status("Packed " + n + " container" + (n == 1 ? "" : "s") + "." + (left > 0 ? "  " + left + " didn't fit - they stay in the manifest." : "") + note);
            RefreshLists(); RaiseChanged();
        }

        // Packs with the chosen mode. "Group + gaps" falls back to a tight, grouped pack in drop-off order when the
        // gaps would leave too little room for this cargo or for what's still to be picked up on the route.
        int DoPack(List<CargoBox> set, out string note)
        {
            note = "";
            int mode = PackMode;
            if (mode == 0) return Model.Pack(set, RankOf);
            if (mode == 1) return Model.Pack(set, RankOf, GroupKey);
            Func<CargoBox, string> grp = mode == 3 ? (Func<CargoBox, string>)DestKey : GroupKey;
            Func<CargoBox, string> sub = mode == 3 ? (Func<CargoBox, string>)CargoKey : null;
            var snap = set.Select(b => new { B = b, b.State, b.Bay, b.X, b.Y, b.Z, b.Rot }).ToList();
            int n = Model.Pack(set, RankOf, grp, 1, sub);
            int need = Math.Max(0, ExtraNeeded());
            if (n == set.Count && Model.FreeCells() >= need) return n;
            foreach (var s in snap) { s.B.State = s.State; s.B.Bay = s.Bay; s.B.X = s.X; s.B.Y = s.Y; s.B.Z = s.Z; s.B.Rot = s.Rot; }
            n = Model.Pack(set, RankOf, GroupKey);
            note = n < set.Count ? "" : "  ⚠ Not enough room for gaps between groups" + (need > 0 ? " (about " + need + " SCU still to pick up on the route)" : "") +
                                        " - packed tightly by cargo, in drop-off order, instead.";
            return n;
        }

        void Repack()
        {
            if (Model.Ship == null) { Status("Select your ship first (Route Plan)."); return; }
            List<CargoBox> live = Model.State.Boxes.Where(b => b.State == BoxState.OnShip || b.State == BoxState.Manifest).ToList();
            if (live.Count == 0) { Status("No cargo to repack."); return; }
            int lastBatch = live.Max(b => b.Batch);
            List<CargoBox> lastLoad = live.Where(b => b.Batch == lastBatch).ToList();
            int choice = ChoiceForm.Ask(FindForm(), "Repack cargo", "What do you want to repack?",
                new[] { "Last load placed  (" + lastLoad.Count + " box" + (lastLoad.Count == 1 ? "" : "es") + ", " + lastLoad.Sum(b => b.Size) + " SCU)",
                        "All cargo for the whole route  (" + live.Count + " boxes, " + live.Sum(b => b.Size) + " SCU)" });
            if (choice < 0) return;
            List<CargoBox> set = choice == 0 ? lastLoad : live;
            foreach (CargoBox b in set) b.State = BoxState.Manifest;
            string note; int n = DoPack(set, out note);
            int left = set.Count - n;
            Status("Repacked " + n + " container" + (n == 1 ? "" : "s") + (choice == 0 ? " from the last load." : " for the whole route.") +
                   (left > 0 ? "  " + left + " didn't fit - they're in the manifest." : "") + note);
            RefreshLists(); RaiseChanged();
        }

        void UndeliverSelected()
        {
            Item it = delivered.SelectedItem as Item;
            if (it == null) { Status("Select a delivered load first."); return; }
            if (Undeliver != null) Undeliver(it.Record);
        }

        void ClearDelivered()
        {
            List<CargoBox> d = Model.State.Boxes.Where(b => b.State == BoxState.Delivered).ToList();
            if (d.Count == 0) return;
            if (MessageBox.Show(FindForm(), "Clear " + d.Count + " delivered container(s) from the list?\r\n(The route history keeps the deliveries.)", "Delivered",
                                MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            foreach (CargoBox b in d) b.State = BoxState.Archived;
            RefreshLists(); RaiseChanged();
        }
    }

    // Small themed "pick one" dialog. Returns the chosen index or -1.
    public class ChoiceForm : Form
    {
        public int Choice = -1;

        ChoiceForm(string title, string prompt, string[] options)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Theme.ApplyForm(this);
            Label l = new Label { Text = prompt, AutoSize = true, Location = new Point(18, 16), Font = Theme.Semibold };
            Controls.Add(l);
            int y = 50;
            for (int i = 0; i < options.Length; i++)
            {
                int idx = i;
                Button b = Theme.MakeButton(options[i], i == 0 ? Theme.ButtonKind.Accent : Theme.ButtonKind.Secondary, delegate { Choice = idx; DialogResult = DialogResult.OK; });
                b.AutoSize = false; b.Size = new Size(420, 36); b.Location = new Point(18, y); b.TextAlign = ContentAlignment.MiddleLeft;
                Controls.Add(b);
                y += 44;
            }
            Button cancel = Theme.MakeButton("Cancel", Theme.ButtonKind.Secondary, null);
            cancel.DialogResult = DialogResult.Cancel; cancel.Location = new Point(358, y + 4);
            Controls.Add(cancel);
            CancelButton = cancel;
            ClientSize = new Size(456, y + 48);
        }

        public static int Ask(IWin32Window owner, string title, string prompt, string[] options)
        {
            using (ChoiceForm f = new ChoiceForm(title, prompt, options))
                return f.ShowDialog(owner) == DialogResult.OK ? f.Choice : -1;
        }
    }
}
