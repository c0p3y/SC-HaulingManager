// SC Hauling Manager - C0P3Y | Icarus Interstellar Inc.
// Colour themes (presets + custom) and the shared styling helpers.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;

namespace HaulingManager
{
    public static class Theme
    {
        public static Color Bg;
        public static Color Header;
        public static Color Panel;
        public static Color Surface;
        public static Color SurfaceHover;
        public static Color Border;
        public static Color GridLine;
        public static Color AltRow;
        public static Color Text;
        public static Color Muted;
        public static Color Accent;
        public static Color AccentDim;
        public static Color Highlight;
        public static Color HighlightHover;
        public static Color ButtonText;
        public static Color Green;
        public static Color Red;
        public static Color Selection;
        public static Color RelatedRow;
        public static Color OutpostRow;
        public static Color HoldBg;
        public static Color HoldFloor;
        public static Color CrateBody;
        public static Color CrateFrame;
        public static Color CrateText;

        // ---- themes: every colour above has an id, a friendly name, and a value in each preset
        public static readonly string[][] Roles =
        {
            new[] { "Bg", "App background" },
            new[] { "Header", "Title bar + table headers" },
            new[] { "Panel", "Panels / cards" },
            new[] { "Surface", "Inputs, lists + buttons" },
            new[] { "SurfaceHover", "Button hover" },
            new[] { "Border", "Borders" },
            new[] { "GridLine", "Table grid lines" },
            new[] { "AltRow", "Alternate table rows" },
            new[] { "Text", "Primary text" },
            new[] { "Muted", "Secondary text" },
            new[] { "Accent", "Accent (headings, pickups)" },
            new[] { "AccentDim", "Accent, dim (outlines)" },
            new[] { "Highlight", "Highlight (buttons, drop-offs)" },
            new[] { "HighlightHover", "Highlight hover" },
            new[] { "ButtonText", "Text on highlight buttons" },
            new[] { "Green", "Success / cities" },
            new[] { "Red", "Errors" },
            new[] { "Selection", "Selected row" },
            new[] { "RelatedRow", "Related rows" },
            new[] { "OutpostRow", "Outpost highlight rows" },
            new[] { "HoldBg", "3D hold background" },
            new[] { "HoldFloor", "3D cargo grid floor" },
            new[] { "CrateBody", "Crate body" },
            new[] { "CrateFrame", "Crate frame" },
            new[] { "CrateText", "Crate text" }
        };
        public const string DefaultTheme = "Navy & Pink";
        public const string CustomTheme = "Custom";
        static readonly KeyValuePair<string, string>[] PresetData =
        {
            new KeyValuePair<string, string>("Navy & Pink", "0A1420 0D1A28 101E2D 15283A 1B3349 23405A 1A3044 122133 DCEBF8 7F9AB3 5CC8FF 2E6E95 FF7AC6 FFA6D9 0A1420 4FE0A0 FF6B6B 1E4466 17324A 3A1530 0E1B2A 1E4466 24272B 555B63 E4ECF3"),
            new KeyValuePair<string, string>("Graphite & Orange", "0C0C0D 121214 161618 1E1E21 28282C 34343A 222226 19191B E8E6E3 8E8A85 C3CBD3 4A525A FF6A00 FF8C3A 0C0C0D 6FCF97 FF5C5C 3A2A1C 221C17 3A1F0E 141416 4A3320 2A2A2D 5E5E64 ECEAE6"),
            new KeyValuePair<string, string>("Slate Grays", "111214 17181B 1C1D20 25262A 2F3035 3C3D43 2A2B2F 202124 E6E7EA 8B8D93 C9CCD2 5A5D64 E2E4E8 FFFFFF 111214 A8C9B0 D99A9A 3A3C42 26272B 3A3A3E 18191C 4A4C52 2C2D30 6A6C72 EDEEF0"),
            new KeyValuePair<string, string>("Abyss Teal & Coral", "071615 0B1D1C 0F2423 15302E 1B3B39 254B48 183634 122A28 DDF2EF 7FA6A1 3FD1C2 1F6E66 FF7F66 FFA392 071615 7EE0A0 FF5E6C 1D4A46 143431 3A1B16 0C1F1E 1D4A46 23282A 556064 E4F2F0"),
            new KeyValuePair<string, string>("Daylight Blue", "EEF3F8 DDE7F1 FFFFFF F4F7FB E3ECF5 BFD0E0 E1E9F1 F7FAFD 14263A 5E7389 1F7FD1 9CC6EA E0479E F07AB9 FFFFFF 15936A D64545 CFE3F7 E6F0FA FBE3F0 DCE6F0 9CC6EA 3A3F46 7A828C F2F5F8"),
            new KeyValuePair<string, string>("Paper Mint", "F4F1EA E7E2D6 FCFAF5 F7F4EC ECE6D8 CFC6B2 E6E0D2 F8F5EE 2A2A26 6E6A5E 0F8F80 9ED3CB C2417A D86E9C FFFFFF 2E8B57 C0392B D8EEE9 EEF5F1 F6E1EA E9E4D8 9ED3CB 3B3A36 85817A F5F2EA")
        };
        public static string[] PresetNames() { return PresetData.Select(p => p.Key).ToArray(); }

        public static Dictionary<string, string> Preset(string name)
        {
            string data = PresetData.Where(p => p.Key == name).Select(p => p.Value).FirstOrDefault() ?? PresetData[0].Value;
            string[] v = data.Split(' ');
            Dictionary<string, string> d = new Dictionary<string, string>();
            for (int i = 0; i < Roles.Length; i++) d[Roles[i][0]] = v[i];
            return d;
        }

        // A preset by name, or "Custom": the default preset with your saved colours on top.
        public static Dictionary<string, string> Resolve(string name, Dictionary<string, string> custom)
        {
            if (name != CustomTheme) return Preset(name);
            Dictionary<string, string> d = Preset(DefaultTheme);
            if (custom != null) foreach (KeyValuePair<string, string> kv in custom) if (d.ContainsKey(kv.Key) && ParseHex(kv.Value) != null) d[kv.Key] = kv.Value;
            return d;
        }

        public static void Apply(Dictionary<string, string> colors)
        {
            foreach (string[] r in Roles)
            {
                string v; Color? c = colors != null && colors.TryGetValue(r[0], out v) ? ParseHex(v) : null;
                if (c == null) c = ParseHex(Preset(DefaultTheme)[r[0]]);
                typeof(Theme).GetField(r[0]).SetValue(null, c.Value);
            }
        }

        public static Color? ParseHex(string s)
        {
            s = (s ?? "").Trim().TrimStart('#');
            int v;
            if (s.Length != 6 || !int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out v)) return null;
            return Hex(v);
        }
        public static string ToHex(Color c) { return c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }

        public static bool IsDark { get { return Bg.GetBrightness() < 0.5f; } }

        // a mix of two colours (t = 0 -> a, 1 -> b)
        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb(255, (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        static Theme() { Apply(Preset(DefaultTheme)); }

        public static readonly Font Base = new Font("Segoe UI", 9.5f);
        public static readonly Font Semibold = new Font("Segoe UI Semibold", 9.5f);
        public static readonly Font Small = new Font("Segoe UI", 8.5f);
        public static readonly Font SectionFont = new Font("Segoe UI Semibold", 9f);
        public static readonly Font TitleFont = new Font("Segoe UI Semibold", 15f);
        public static readonly Font Mono = new Font("Consolas", 9.5f);

        static Color Hex(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF); }

        public static Color KindColor(LocKind k, bool dcIsOutpost)
        {
            switch (k)
            {
                case LocKind.Station: return Accent;
                case LocKind.City: return Green;
                case LocKind.Outpost: return Highlight;
                case LocKind.DistributionCenter: return dcIsOutpost ? Highlight : Text;
                default: return Muted;
            }
        }

        // ---- forms
        public static void ApplyForm(Form f)
        {
            f.BackColor = Bg; f.ForeColor = Text; f.Font = Base;
            f.HandleCreated += delegate { NativeMethods.UseDarkTitleBar(f.Handle); };
            if (f.IsHandleCreated) NativeMethods.UseDarkTitleBar(f.Handle);
        }

        // Styles inputs / buttons / grids inside a container (labels and panels inherit the form colours).
        public static void ApplyControls(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is DataGridView) StyleGrid((DataGridView)c);
                else if (c is Button && c.Tag == null) StyleButton((Button)c, ButtonKind.Secondary);
                else if (c is TextBox) StyleInput(c);
                else if (c is ComboBox) { StyleInput(c); ((ComboBox)c).FlatStyle = FlatStyle.Flat; }
                else if (c is NumericUpDown) { StyleInput(c); ((NumericUpDown)c).BorderStyle = BorderStyle.FixedSingle; }
                else if (c is CheckBox) StyleCheck((CheckBox)c);
                if (c.Controls.Count > 0 && !(c is DataGridView) && !(c is NumericUpDown)) ApplyControls(c);
            }
        }

        public static void StyleInput(Control c)
        {
            c.BackColor = Surface; c.ForeColor = Text;
            TextBox tb = c as TextBox;
            if (tb != null)
            {
                tb.BorderStyle = BorderStyle.FixedSingle;
                if (tb.Multiline) tb.HandleCreated += delegate { NativeMethods.UseDarkScrollbars(tb.Handle); };
            }
        }

        public static void StyleCheck(CheckBox c)
        {
            c.FlatStyle = FlatStyle.Flat;
            c.ForeColor = Text;
            c.FlatAppearance.BorderColor = Border;
            c.FlatAppearance.CheckedBackColor = AccentDim;
            c.Cursor = Cursors.Hand;
        }

        public enum ButtonKind { Primary, Secondary, Accent, Warning }

        public static Button MakeButton(string text, ButtonKind kind, EventHandler click)
        {
            Button b = new Button();
            b.Text = text; b.AutoSize = true; b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Padding = new Padding(10, 3, 10, 3);
            b.Margin = new Padding(4, 3, 4, 3);
            StyleButton(b, kind);
            if (click != null) b.Click += click;
            return b;
        }

        public static void StyleButton(Button b, ButtonKind kind)
        {
            b.Tag = b.Tag ?? "styled";
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false;
            switch (kind)
            {
                case ButtonKind.Primary:
                    b.BackColor = Highlight; b.ForeColor = ButtonText; b.Font = Semibold;
                    b.FlatAppearance.BorderColor = Highlight;
                    b.FlatAppearance.MouseOverBackColor = HighlightHover;
                    b.FlatAppearance.MouseDownBackColor = Mix(Highlight, Bg, 0.2f);
                    break;
                case ButtonKind.Accent:
                    b.BackColor = Surface; b.ForeColor = Accent; b.Font = Semibold;
                    b.FlatAppearance.BorderColor = AccentDim;
                    b.FlatAppearance.MouseOverBackColor = SurfaceHover;
                    b.FlatAppearance.MouseDownBackColor = Selection;
                    break;
                case ButtonKind.Warning:
                    b.BackColor = Surface; b.ForeColor = Highlight; b.Font = Semibold;
                    b.FlatAppearance.BorderColor = Mix(Highlight, Surface, 0.45f);
                    b.FlatAppearance.MouseOverBackColor = OutpostRow;
                    b.FlatAppearance.MouseDownBackColor = Mix(OutpostRow, Highlight, 0.15f);
                    break;
                default:
                    b.BackColor = Surface; b.ForeColor = Text; b.Font = Base;
                    b.FlatAppearance.BorderColor = Border;
                    b.FlatAppearance.MouseOverBackColor = SurfaceHover;
                    b.FlatAppearance.MouseDownBackColor = Selection;
                    break;
            }
        }

        public static void StyleGrid(DataGridView g)
        {
            g.BackgroundColor = Panel;
            g.BorderStyle = BorderStyle.None;
            g.GridColor = GridLine;
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            g.ColumnHeadersDefaultCellStyle.BackColor = Header;
            g.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
            g.ColumnHeadersDefaultCellStyle.Font = SectionFont;
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Header;
            g.ColumnHeadersDefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
            g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            g.ColumnHeadersHeight = 30;
            g.RowHeadersDefaultCellStyle.BackColor = Header;
            g.RowHeadersDefaultCellStyle.SelectionBackColor = Selection;
            g.DefaultCellStyle.BackColor = Panel;
            g.DefaultCellStyle.ForeColor = Text;
            g.DefaultCellStyle.SelectionBackColor = Selection;
            g.DefaultCellStyle.SelectionForeColor = Text;
            g.DefaultCellStyle.Font = Base;
            g.DefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
            g.AlternatingRowsDefaultCellStyle.BackColor = AltRow;
            g.RowTemplate.Height = 26;
            g.AllowUserToResizeRows = false;
            g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            typeof(DataGridView).InvokeMember("DoubleBuffered", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty, null, g, new object[] { true });
            g.HandleCreated += delegate
            {
                foreach (Control c in g.Controls) if (c is ScrollBar) NativeMethods.UseDarkScrollbars(c.Handle);
            };
        }

        // Read-only list grid
        public static DataGridView MakeList(bool multiSelect)
        {
            DataGridView g = new DataGridView();
            g.Dock = DockStyle.Fill;
            g.ReadOnly = true;
            g.AllowUserToAddRows = false; g.AllowUserToDeleteRows = false; g.AllowUserToOrderColumns = false;
            g.RowHeadersVisible = false;
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.MultiSelect = multiSelect;
            g.StandardTab = true;
            StyleGrid(g);
            return g;
        }

        public static DataGridViewTextBoxColumn Col(DataGridView g, string name, string header, float weight, int minWidth, bool right)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.Name = name; c.HeaderText = header; c.FillWeight = weight; c.MinimumWidth = minWidth;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            if (right) c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            g.Columns.Add(c);
            return c;
        }

        // A titled "card" panel. Returns the body panel to put content in.
        public static Panel MakeCard(string title, out Panel body, out Label titleLabel)
        {
            Panel outer = new Panel();
            outer.Dock = DockStyle.Fill; outer.Padding = new Padding(8);
            Panel card = new Panel();
            card.Dock = DockStyle.Fill; card.BackColor = Panel; card.Padding = new Padding(12, 6, 12, 10);
            card.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Border)) e.Graphics.DrawRectangle(p, 0, 0, card.Width - 1, card.Height - 1);
                using (SolidBrush b = new SolidBrush(Accent)) e.Graphics.FillRectangle(b, 0, 0, 3, 34);
            };
            Label t = new Label();
            t.Text = title.ToUpperInvariant(); t.Dock = DockStyle.Top; t.Height = 30;
            t.Font = SectionFont; t.ForeColor = Accent; t.TextAlign = ContentAlignment.MiddleLeft;
            body = new Panel(); body.Dock = DockStyle.Fill;
            card.Controls.Add(body); card.Controls.Add(t);
            outer.Controls.Add(card);
            titleLabel = t;
            return outer;
        }

        // Thin highlight -> accent gradient line used under the header
        public static Panel MakeAccentLine()
        {
            Panel p = new Panel();
            p.Dock = DockStyle.Top; p.Height = 2;
            p.Paint += delegate(object s, PaintEventArgs e)
            {
                if (p.Width <= 0) return;
                using (LinearGradientBrush b = new LinearGradientBrush(p.ClientRectangle, Highlight, Accent, LinearGradientMode.Horizontal))
                    e.Graphics.FillRectangle(b, p.ClientRectangle);
            };
            p.Resize += delegate { p.Invalidate(); };
            return p;
        }

        public static void DrawPill(Graphics g, string text, Font f, Color color, ref float x, float midY)
        {
            SizeF sz = g.MeasureString(text, f);
            RectangleF r = new RectangleF(x, midY - sz.Height / 2 - 1, sz.Width + 10, sz.Height + 2);
            using (GraphicsPath path = Rounded(r, 8))
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(40, color)))
            using (Pen pen = new Pen(Color.FromArgb(140, color)))
            {
                g.FillPath(bg, path); g.DrawPath(pen, path);
            }
            using (SolidBrush tb = new SolidBrush(color)) g.DrawString(text, f, tb, x + 5, r.Y + 1);
            x += r.Width + 8;
        }

        static GraphicsPath Rounded(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
