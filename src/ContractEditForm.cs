// SC Hauling Manager - C0P3Y | Icarus Interstellar Inc.
// Review / edit a contract. Also used for manual entry: paste contract text and press "Parse text".
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace HaulingManager
{
    public class ContractEditForm : Form
    {
        public Contract Result;
        readonly Contract working;
        readonly TextBox title = new TextBox(), reward = new TextBox(), raw = new TextBox();
        readonly DataGridView grid = new DataGridView();
        readonly Label warnings = new Label();

        public ContractEditForm(Contract c, string heading, IEnumerable<string> knownLocations)
        {
            working = c.Clone();
            Text = heading;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1100, 680);
            MinimumSize = new Size(800, 500);
            Theme.ApplyForm(this);

            TableLayoutPanel top = new TableLayoutPanel();
            top.Dock = DockStyle.Top; top.Height = 70; top.ColumnCount = 4; top.Padding = new Padding(8, 8, 8, 0);
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            top.Controls.Add(new Label { Text = "Title", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            title.Dock = DockStyle.Fill; top.Controls.Add(title);
            top.Controls.Add(new Label { Text = "Reward", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            reward.Dock = DockStyle.Fill; top.Controls.Add(reward);
            warnings.AutoSize = false; warnings.Dock = DockStyle.Fill; warnings.ForeColor = Theme.Highlight; warnings.Font = Theme.Small;
            top.Controls.Add(warnings); top.SetColumnSpan(warnings, 4);

            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = true; grid.AllowUserToDeleteRows = true;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.RowHeadersWidth = 28; Theme.StyleGrid(grid); grid.RowHeadersDefaultCellStyle.ForeColor = Theme.Muted;
            grid.EditMode = DataGridViewEditMode.EditOnEnter;
            AddCol("Material", "Material", 22);
            AddCol("Scu", "SCU", 7);
            AddCol("Picked", "Picked up", 8);
            AddCol("Delivered", "Delivered", 8);
            AddCol("Pickup", "Pickup location", 28);
            AddCol("Dropoff", "Drop-off location", 28);
            AddCol("Note", "Note", 14);
            grid.Columns["Note"].ReadOnly = true;
            grid.Columns["Note"].DefaultCellStyle.ForeColor = Theme.Highlight;
            AddCol("Containers", "Containers (e.g. 3x8 1x4)", 16);
            grid.EditingControlShowing += delegate(object s, DataGridViewEditingControlShowingEventArgs e)
            {
                TextBox tb = e.Control as TextBox;
                if (tb == null) return;
                string col = grid.CurrentCell.OwningColumn.Name;
                if (col == "Pickup" || col == "Dropoff")
                {
                    tb.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                    tb.AutoCompleteSource = AutoCompleteSource.CustomSource;
                    AutoCompleteStringCollection ac = new AutoCompleteStringCollection();
                    ac.AddRange(knownLocations.Distinct().ToArray());
                    tb.AutoCompleteCustomSource = ac;
                }
                else tb.AutoCompleteMode = AutoCompleteMode.None;
            };
            grid.DataError += delegate(object s, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };

            Panel rawBox = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 8, 4) };
            Label rawTitle = new Label { Dock = DockStyle.Top, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Font = Theme.SectionFont, ForeColor = Theme.Accent,
                                         Text = "CONTRACT TEXT   —   edit or paste here, then Parse Text to rebuild the cargo list" };
            raw.Multiline = true; raw.ScrollBars = ScrollBars.Vertical; raw.Dock = DockStyle.Fill; raw.Font = Theme.Mono;
            rawBox.Controls.Add(raw); rawBox.Controls.Add(rawTitle);

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill; split.Orientation = Orientation.Horizontal;
            split.Panel1.Controls.Add(grid);
            split.Panel2.Controls.Add(rawBox);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Bottom; buttons.Height = 44; buttons.Padding = new Padding(8); buttons.FlowDirection = FlowDirection.RightToLeft;
            Button ok = Theme.MakeButton("Save Contract", Theme.ButtonKind.Primary, null);
            Button cancel = Theme.MakeButton("Cancel", Theme.ButtonKind.Secondary, null); cancel.DialogResult = DialogResult.Cancel;
            Button parse = Theme.MakeButton("Parse Text", Theme.ButtonKind.Accent, null);
            Button swap = Theme.MakeButton("Swap Pickup / Drop-off", Theme.ButtonKind.Secondary, null);
            ok.Click += OnOk;
            parse.Click += delegate { ApplyParse(ContractParser.Parse(raw.Text)); };
            swap.Click += delegate
            {
                foreach (DataGridViewRow r in grid.SelectedRows.Count > 0 ? grid.SelectedRows.Cast<DataGridViewRow>().ToList() : grid.Rows.Cast<DataGridViewRow>().ToList())
                {
                    if (r.IsNewRow) continue;
                    object a = r.Cells["Pickup"].Value; r.Cells["Pickup"].Value = r.Cells["Dropoff"].Value; r.Cells["Dropoff"].Value = a;
                }
            };
            buttons.Controls.Add(cancel); buttons.Controls.Add(ok); buttons.Controls.Add(parse); buttons.Controls.Add(swap);
            AcceptButton = null; CancelButton = cancel;

            buttons.BackColor = Theme.Header; split.BackColor = Theme.Bg; split.SplitterWidth = 4;
            Controls.Add(split); Controls.Add(top); Controls.Add(buttons);
            Theme.ApplyControls(this);
            Load += delegate { split.SplitterDistance = (int)(split.Height * 0.55); };

            title.Text = working.Title;
            reward.Text = working.Reward > 0 ? working.Reward.ToString("N0") : "";
            raw.Text = working.RawText ?? "";
            warnings.Text = string.IsNullOrEmpty(working.Warnings) ? "" : "Check: " + working.Warnings;
            FillGrid(working.Legs);
        }

        static string NoteFor(CargoLeg l, List<CargoLeg> all)
        {
            if (string.IsNullOrEmpty(l.Group)) return "";
            int n = all.Count(x => x.Group == l.Group);
            if (n < 2) return "";
            int total = all.Where(x => x.Group == l.Group).Sum(x => x.Scu);
            return (l.Estimated ? "est. share of " : "part of ") + total + " SCU (" + n + " pickups)";
        }

        void AddCol(string name, string header, int weight)
        {
            DataGridViewTextBoxColumn col = new DataGridViewTextBoxColumn();
            col.Name = name; col.HeaderText = header; col.FillWeight = weight;
            grid.Columns.Add(col);
        }

        void FillGrid(List<CargoLeg> legs)
        {
            grid.Rows.Clear();
            foreach (CargoLeg l in legs)
            {
                int i = grid.Rows.Add(l.Material, l.Scu, l.PickedUp, l.Delivered, l.Pickup, l.Dropoff, NoteFor(l, legs), Boxes.Normalize(l.Containers));
                grid.Rows[i].Tag = l;
            }
        }

        void ApplyParse(ParseResult p)
        {
            if (p.Legs.Count == 0)
            {
                MessageBox.Show(this, "No 'Collect ... from ...' / 'Deliver x/y SCU ... to ...' objectives were found in the text.", "Parse");
                return;
            }
            if (p.Title.Length > 0) title.Text = p.Title;
            if (p.Reward > 0) reward.Text = p.Reward.ToString("N0");
            FillGrid(p.Legs);
            warnings.Text = p.Warnings.Count > 0 ? "Check: " + string.Join(" | ", p.Warnings.ToArray()) : "";
        }

        static int ToInt(object v)
        {
            if (v == null) return 0;
            string s = new string(v.ToString().Where(char.IsDigit).ToArray());
            int i; int.TryParse(s, out i); return i;
        }

        static string Str(object v) { return v == null ? "" : LocationDb.Clean(v.ToString()); }

        void OnOk(object sender, EventArgs e)
        {
            grid.EndEdit();
            List<CargoLeg> legs = new List<CargoLeg>();
            foreach (DataGridViewRow r in grid.Rows)
            {
                if (r.IsNewRow) continue;
                CargoLeg l = new CargoLeg();
                l.Material = Str(r.Cells["Material"].Value);
                l.Scu = ToInt(r.Cells["Scu"].Value);
                l.Delivered = Math.Min(l.Scu, ToInt(r.Cells["Delivered"].Value));
                l.PickedUp = Math.Min(l.Scu, Math.Max(l.Delivered, ToInt(r.Cells["Picked"].Value)));
                l.Pickup = Str(r.Cells["Pickup"].Value);
                l.Dropoff = Str(r.Cells["Dropoff"].Value);
                l.Containers = Boxes.Normalize(Convert.ToString(r.Cells["Containers"].Value));
                CargoLeg orig = r.Tag as CargoLeg;
                if (orig != null)
                {
                    l.Group = orig.Group ?? "";
                    l.Estimated = orig.Estimated && l.Scu == orig.Scu;   // typing a real amount clears the estimate
                }
                if (l.Material.Length == 0 && l.Scu == 0 && l.Pickup.Length == 0 && l.Dropoff.Length == 0) continue;
                legs.Add(l);
            }
            if (legs.Count == 0) { MessageBox.Show(this, "Add at least one cargo line."); return; }
            List<string> problems = new List<string>();
            foreach (CargoLeg l in legs)
            {
                if (l.Scu <= 0) problems.Add("a line has no SCU");
                if (l.Pickup.Length == 0) problems.Add("a line has no pickup location");
                if (l.Dropoff.Length == 0) problems.Add("a line has no drop-off location");
            }
            if (problems.Count > 0 &&
                MessageBox.Show(this, "Heads up: " + string.Join(", ", problems.Distinct().ToArray()) + ".\r\nThose lines will be skipped by the route planner until fixed.\r\n\r\nSave anyway?",
                                "Incomplete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            working.Title = title.Text.Trim().Length > 0 ? title.Text.Trim() : "Hauling Contract";
            working.Reward = ToInt(reward.Text);
            working.RawText = raw.Text;
            working.Legs = legs;
            working.Warnings = problems.Count > 0 ? string.Join(", ", problems.Distinct().ToArray()) : "";
            Result = working;
            DialogResult = DialogResult.OK;
        }
    }
}
