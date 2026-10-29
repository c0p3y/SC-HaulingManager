// SC Hauling Manager - C0P3Y | Icarus Interstellar Inc.
// Settings page "Colors & Themes": pick a preset theme or set every colour yourself, and edit the cargo
// (destination) colours and the order they're handed out in.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace HaulingManager
{
    public class ThemeEditor : Panel
    {
        string themeName;
        Dictionary<string, string> colors;
        readonly List<string> cargo = new List<string>();
        bool loading;

        readonly ComboBox themeBox = new ComboBox();
        readonly Panel preview = new Panel();
        readonly Dictionary<string, Panel> swatches = new Dictionary<string, Panel>();
        readonly Dictionary<string, TextBox> hexes = new Dictionary<string, TextBox>();
        readonly ListBox cargoList = new ListBox();

        static readonly Dictionary<string, string> ColorNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "5CC8FF", "Light blue" }, { "FF7AC6", "Pink" }, { "4FE0A0", "Mint green" }, { "A78BFA", "Lavender" }, { "2DD4BF", "Teal" },
            { "FF6B6B", "Coral red" }, { "60A5FA", "Blue" }, { "C084FC", "Violet" }, { "A3E635", "Lime" }, { "818CF8", "Indigo" },
            { "B8C7D9", "Silver" }, { "F0F4F8", "Ice white" }, { "FF9F43", "Orange" }, { "FF6A00", "Vivid orange" }, { "FB923C", "Tangerine" }, { "FFD166", "Yellow" }
        };

        public ThemeEditor(AppSettings s)
        {
            Dock = DockStyle.Fill; AutoScroll = true; Padding = new Padding(14, 10, 14, 10);
            themeName = string.IsNullOrEmpty(s.ThemeName) ? Theme.DefaultTheme : s.ThemeName;
            colors = Theme.Resolve(themeName, s.ThemeColors);
            cargo.AddRange((s.HoldColors ?? HoldPanel.DefaultColors).Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                                                    .Select(x => x.Trim().TrimStart('#').ToUpperInvariant()).Where(x => Theme.ParseHex(x) != null));
            if (cargo.Count == 0) cargo.AddRange(HoldPanel.DefaultColors.Split(','));

            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // ---- theme picker + preview
            root.Controls.Add(Heading("THEME"));
            FlowLayoutPanel pick = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
            themeBox.DropDownStyle = ComboBoxStyle.DropDownList; themeBox.Width = 240;
            foreach (string n in Theme.PresetNames()) themeBox.Items.Add(n == Theme.DefaultTheme ? n + "  (default)" : n);
            themeBox.Items.Add(Theme.CustomTheme);
            themeBox.SelectedIndexChanged += OnThemePicked;
            Button reset = Theme.MakeButton("Reset colors to this theme", Theme.ButtonKind.Secondary, delegate { ResetToPreset(); });
            pick.Controls.Add(themeBox); pick.Controls.Add(reset);
            root.Controls.Add(pick);
            root.Controls.Add(Note("Dark: Navy & Pink, Graphite & Orange, Slate Grays, Abyss Teal & Coral.  Light: Daylight Blue, Paper Mint.  Theme changes apply when the app restarts (it offers to do that when you save)."));
            preview.Height = 150; preview.Dock = DockStyle.Fill; preview.Margin = new Padding(0, 6, 0, 10);
            preview.Paint += PaintPreview;
            typeof(Panel).InvokeMember("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.SetProperty, null, preview, new object[] { true });
            root.Controls.Add(preview);

            // ---- custom colours
            root.Controls.Add(Heading("CUSTOM COLORS"));
            root.Controls.Add(Note("Click a swatch to pick a colour, or type a hex value (e.g. FF7AC6). Changing any colour switches the theme to Custom."));
            TableLayoutPanel grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 10) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            int half = (Theme.Roles.Length + 1) / 2;
            for (int i = 0; i < half; i++)
            {
                grid.Controls.Add(RoleRow(Theme.Roles[i]), 0, i);
                if (i + half < Theme.Roles.Length) grid.Controls.Add(RoleRow(Theme.Roles[i + half]), 1, i);
            }
            root.Controls.Add(grid);

            // ---- cargo colours
            root.Controls.Add(Heading("CARGO COLORS  (FILL ORDER)"));
            root.Controls.Add(Note("Each destination in the cargo hold takes the next colour in this list, first drop-off first. Changes apply when you save."));
            TableLayoutPanel cg = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4) };
            cg.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            cg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            cargoList.Width = 290; cargoList.Height = 26 * 9; cargoList.BorderStyle = BorderStyle.FixedSingle;
            cargoList.DrawMode = DrawMode.OwnerDrawFixed; cargoList.ItemHeight = 26; cargoList.IntegralHeight = false;
            cargoList.DrawItem += DrawCargoItem;
            cargoList.DoubleClick += delegate { ChangeCargo(); };
            cg.Controls.Add(cargoList, 0, 0);
            FlowLayoutPanel cb = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
            cb.Controls.Add(Theme.MakeButton("▲  Move up", Theme.ButtonKind.Secondary, delegate { MoveCargo(-1); }));
            cb.Controls.Add(Theme.MakeButton("▼  Move down", Theme.ButtonKind.Secondary, delegate { MoveCargo(1); }));
            cb.Controls.Add(Theme.MakeButton("Change…", Theme.ButtonKind.Secondary, delegate { ChangeCargo(); }));
            cb.Controls.Add(Theme.MakeButton("Add…", Theme.ButtonKind.Secondary, delegate { AddCargo(); }));
            cb.Controls.Add(Theme.MakeButton("Remove", Theme.ButtonKind.Secondary, delegate { RemoveCargo(); }));
            cb.Controls.Add(Theme.MakeButton("Reset to default", Theme.ButtonKind.Secondary, delegate { cargo.Clear(); cargo.AddRange(HoldPanel.DefaultColors.Split(',')); RefreshCargo(0); }));
            foreach (Control c in cb.Controls) { Button bt = (Button)c; bt.AutoSize = false; bt.Padding = new Padding(6, 0, 6, 0); bt.TextAlign = ContentAlignment.MiddleLeft; bt.Size = new Size(160, 30); }
            cg.Controls.Add(cb, 1, 0);
            root.Controls.Add(cg);

            Controls.Add(root);
            Theme.ApplyControls(this);

            loading = true;
            int idx = Array.IndexOf(Theme.PresetNames(), themeName);
            themeBox.SelectedIndex = idx >= 0 ? idx : themeBox.Items.Count - 1;
            loading = false;
            RefreshSwatches();
            RefreshCargo(0);
        }

        // ---------------------------------------------------------------- results
        public void Commit(AppSettings s)
        {
            s.ThemeName = themeName;
            s.ThemeColors = themeName == Theme.CustomTheme ? new Dictionary<string, string>(colors) : null;
            s.HoldColors = string.Join(",", cargo.ToArray());
        }

        // ---------------------------------------------------------------- building blocks
        static Label Heading(string text)
        {
            return new Label { Text = text, AutoSize = true, Font = Theme.SectionFont, ForeColor = Theme.Accent, Margin = new Padding(0, 10, 0, 4) };
        }

        static Label Note(string text)
        {
            return new Label { Text = text, AutoSize = true, UseMnemonic = false, Font = Theme.Small, ForeColor = Theme.Muted, MaximumSize = new Size(680, 0), Margin = new Padding(0, 0, 0, 4) };
        }

        Control RoleRow(string[] role)
        {
            string id = role[0];
            FlowLayoutPanel row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 8, 2) };
            Panel sw = new Panel { Size = new Size(30, 22), Cursor = Cursors.Hand, Margin = new Padding(0, 2, 6, 0) };
            sw.Paint += delegate(object o, PaintEventArgs e) { using (Pen p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, sw.Width - 1, sw.Height - 1); };
            sw.Click += delegate { PickRole(id); };
            new ToolTip().SetToolTip(sw, "Pick a colour for: " + role[1]);
            TextBox hex = new TextBox { Width = 64, MaxLength = 7, CharacterCasing = CharacterCasing.Upper, Font = Theme.Mono, Margin = new Padding(0, 2, 6, 0) };
            hex.Leave += delegate { TypedRole(id, hex.Text); };
            hex.KeyDown += delegate(object o, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; TypedRole(id, hex.Text); } };
            Label name = new Label { Text = role[1], AutoSize = true, UseMnemonic = false, Padding = new Padding(0, 5, 0, 0) };
            row.Controls.Add(sw); row.Controls.Add(hex); row.Controls.Add(name);
            swatches[id] = sw; hexes[id] = hex;
            return row;
        }

        void RefreshSwatches()
        {
            foreach (string[] r in Theme.Roles)
            {
                Color c = Col(r[0]);
                swatches[r[0]].BackColor = c;
                hexes[r[0]].Text = Theme.ToHex(c);
            }
            preview.Invalidate();
        }

        Color Col(string id)
        {
            string v; Color? c = colors.TryGetValue(id, out v) ? Theme.ParseHex(v) : null;
            return c ?? Theme.ParseHex(Theme.Preset(Theme.DefaultTheme)[id]).Value;
        }

        // ---------------------------------------------------------------- theme events
        void OnThemePicked(object sender, EventArgs e)
        {
            if (loading) return;
            string[] names = Theme.PresetNames();
            if (themeBox.SelectedIndex < names.Length)
            {
                themeName = names[themeBox.SelectedIndex];
                colors = Theme.Preset(themeName);
            }
            else themeName = Theme.CustomTheme;    // keep the colours as they are, now editable
            RefreshSwatches();
        }

        void ResetToPreset()
        {
            string basis = themeName == Theme.CustomTheme ? Theme.DefaultTheme : themeName;
            colors = Theme.Preset(basis);
            loading = true;
            themeName = basis; themeBox.SelectedIndex = Array.IndexOf(Theme.PresetNames(), basis);
            loading = false;
            RefreshSwatches();
        }

        void SetRole(string id, Color c)
        {
            colors[id] = Theme.ToHex(c);
            if (themeName != Theme.CustomTheme)
            {
                loading = true; themeName = Theme.CustomTheme; themeBox.SelectedIndex = themeBox.Items.Count - 1; loading = false;
            }
            RefreshSwatches();
        }

        void PickRole(string id)
        {
            Color? c = AskColor(Col(id));
            if (c != null) SetRole(id, c.Value);
        }

        void TypedRole(string id, string text)
        {
            Color? c = Theme.ParseHex(text);
            if (c == null) { hexes[id].Text = Theme.ToHex(Col(id)); return; }
            if (Theme.ToHex(c.Value) != Theme.ToHex(Col(id))) SetRole(id, c.Value);
        }

        Color? AskColor(Color start)
        {
            using (ColorDialog d = new ColorDialog { FullOpen = true, AnyColor = true, Color = start })
            {
                d.CustomColors = cargo.Concat(colors.Values).Distinct().Take(16).Select(h => { Color c = Theme.ParseHex(h).Value; return c.R | (c.G << 8) | (c.B << 16); }).ToArray();
                return d.ShowDialog(FindForm()) == DialogResult.OK ? (Color?)d.Color : null;
            }
        }

        // ---------------------------------------------------------------- cargo colour list
        void RefreshCargo(int select)
        {
            cargoList.Items.Clear();
            for (int i = 0; i < cargo.Count; i++) cargoList.Items.Add(cargo[i]);
            if (cargo.Count > 0) cargoList.SelectedIndex = Math.Max(0, Math.Min(cargo.Count - 1, select));
        }

        void DrawCargoItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= cargo.Count) return;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush bg = new SolidBrush(sel ? Theme.Selection : Theme.Surface)) e.Graphics.FillRectangle(bg, e.Bounds);
            string hex = cargo[e.Index];
            Color c = Theme.ParseHex(hex) ?? Color.Gray;
            Rectangle sw = new Rectangle(e.Bounds.X + 34, e.Bounds.Y + 4, 34, e.Bounds.Height - 8);
            using (SolidBrush b = new SolidBrush(c)) e.Graphics.FillRectangle(b, sw);
            using (SolidBrush t = new SolidBrush(Theme.Muted)) e.Graphics.DrawString((e.Index + 1) + ".", Theme.Base, t, e.Bounds.X + 6, e.Bounds.Y + 4);
            string name; ColorNames.TryGetValue(hex, out name);
            using (SolidBrush t = new SolidBrush(Theme.Text)) e.Graphics.DrawString((name ?? "Custom") + "   #" + hex, Theme.Base, t, sw.Right + 10, e.Bounds.Y + 4);
        }

        void MoveCargo(int dir)
        {
            int i = cargoList.SelectedIndex, j = i + dir;
            if (i < 0 || j < 0 || j >= cargo.Count) return;
            string t = cargo[i]; cargo[i] = cargo[j]; cargo[j] = t;
            RefreshCargo(j);
        }

        void ChangeCargo()
        {
            int i = cargoList.SelectedIndex;
            if (i < 0) return;
            Color? c = AskColor(Theme.ParseHex(cargo[i]) ?? Color.Gray);
            if (c != null) { cargo[i] = Theme.ToHex(c.Value); RefreshCargo(i); }
        }

        void AddCargo()
        {
            Color? c = AskColor(Color.White);
            if (c != null) { cargo.Add(Theme.ToHex(c.Value)); RefreshCargo(cargo.Count - 1); }
        }

        void RemoveCargo()
        {
            int i = cargoList.SelectedIndex;
            if (i < 0 || cargo.Count <= 1) return;
            cargo.RemoveAt(i); RefreshCargo(i);
        }

        // ---------------------------------------------------------------- preview (drawn with the colours being edited)
        void PaintPreview(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int w = preview.Width, h = preview.Height;
            Fill(g, "Bg", 0, 0, w, h);
            Fill(g, "Header", 0, 0, w, 30);
            Text(g, "SC HAULING MANAGER", "Accent", Theme.Semibold, 10, 7);
            using (LinearGradientBrush lb = new LinearGradientBrush(new Rectangle(0, 30, Math.Max(1, w), 2), Col("Highlight"), Col("Accent"), LinearGradientMode.Horizontal))
                g.FillRectangle(lb, 0, 30, w, 2);
            Fill(g, "Highlight", w - 250, 5, 120, 21); Text(g, "READ CONTRACT", "ButtonText", Theme.Semibold, w - 240, 7);
            Fill(g, "Surface", w - 122, 5, 112, 21); Border(g, "Border", w - 122, 5, 112, 21); Text(g, "Settings", "Text", Theme.Base, w - 95, 7);

            // a panel with a small table
            int pw = Math.Min(430, w - 150);
            Fill(g, "Panel", 10, 42, pw, h - 52); Border(g, "Border", 10, 42, pw, h - 52); Fill(g, "Accent", 10, 42, 3, 22);
            Text(g, "ROUTE PLAN", "Accent", Theme.SectionFont, 20, 46);
            Fill(g, "Header", 18, 66, pw - 16, 18); Text(g, "ACTION        SCU   MATERIAL", "Muted", Theme.Small, 22, 68);
            Fill(g, "Selection", 18, 86, pw - 16, 18);
            Text(g, "PICK UP", "Accent", Theme.Semibold, 22, 87); Text(g, "90   Potassium   (selected)", "Text", Theme.Base, 110, 87);
            Fill(g, "AltRow", 18, 106, pw - 16, 18);
            Text(g, "DROP OFF", "Highlight", Theme.Semibold, 22, 107); Text(g, "147  Carbon", "Text", Theme.Base, 110, 107);
            Text(g, "secondary text  ·  ", "Muted", Theme.Small, 22, 128); Text(g, "● success", "Green", Theme.Small, 130, 128); Text(g, "● error", "Red", Theme.Small, 200, 128);

            // a crate on the hold floor
            int cx = 10 + pw + 20, cy = 42, cw = w - cx - 10;
            if (cw > 60)
            {
                using (LinearGradientBrush hb = new LinearGradientBrush(new Rectangle(cx, cy, cw, h - 52), Col("HoldBg"), Col("Bg"), LinearGradientMode.Vertical))
                    g.FillRectangle(hb, cx, cy, cw, h - 52);
                using (SolidBrush fl = new SolidBrush(Color.FromArgb(90, Col("HoldFloor")))) g.FillRectangle(fl, cx + 6, h - 34, cw - 12, 20);
                int bx = cx + cw / 2 - 40, by = h - 96, bw = 80, bh = 64;
                Fill(g, "CrateBody", bx, by, bw, bh); Border(g, "CrateFrame", bx, by, bw, bh);
                using (SolidBrush cb = new SolidBrush(Theme.ParseHex(cargo.Count > 0 ? cargo[0] : "5CC8FF") ?? Color.Gray))
                {
                    int l = 14, t = 4;
                    g.FillRectangle(cb, bx, by, l, t); g.FillRectangle(cb, bx, by, t, l);
                    g.FillRectangle(cb, bx + bw - l, by, l, t); g.FillRectangle(cb, bx + bw - t, by, t, l);
                    g.FillRectangle(cb, bx, by + bh - t, l, t); g.FillRectangle(cb, bx, by + bh - l, t, l);
                    g.FillRectangle(cb, bx + bw - l, by + bh - t, l, t); g.FillRectangle(cb, bx + bw - t, by + bh - l, t, l);
                    g.DrawString("8 SCU", Theme.Small, cb, bx + 7, by + 6);
                }
                Text(g, "Carbon", "CrateText", Theme.Semibold, bx + 16, by + 24);
                Text(g, "→ Ruin", "CrateText", Theme.Small, bx + 20, by + 44);
            }
        }

        void Fill(Graphics g, string id, int x, int y, int w, int h) { using (SolidBrush b = new SolidBrush(Col(id))) g.FillRectangle(b, x, y, w, h); }
        void Border(Graphics g, string id, int x, int y, int w, int h) { using (Pen p = new Pen(Col(id))) g.DrawRectangle(p, x, y, w - 1, h - 1); }
        void Text(Graphics g, string s, string id, Font f, int x, int y) { using (SolidBrush b = new SolidBrush(Col(id))) g.DrawString(s, f, b, x, y); }
    }
}
