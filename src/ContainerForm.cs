// SC Hauling Manager - C0P3Y | Icarus Interstellar Inc.
// "What came up the freight elevator": count of containers per size, live SCU total.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace HaulingManager
{
    public class ContainerForm : Form
    {
        readonly Dictionary<int, TextBox> counts = new Dictionary<int, TextBox>();
        readonly TextBox totalBox = new TextBox();
        readonly Label summary = new Label();
        bool updating;

        public string ResultContainers = "";
        public int ResultTotal;

        public ContainerForm(string heading, string existing, int initialTotal)
        {
            Text = "Containers loaded";
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(470, 470);
            Theme.ApplyForm(this);
            TopMost = true;
            KeyPreview = true;

            Label head = new Label { Text = heading, AutoSize = true, MaximumSize = new Size(440, 0), Location = new Point(16, 14), Font = Theme.Semibold };
            Controls.Add(head);
            Label sub = new Label { Text = "Enter how many containers of each size came up the elevator.", AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Small, Location = new Point(16, 58) };
            Controls.Add(sub);

            SortedDictionary<int, int> start = Boxes.Parse(existing);
            int y = 84;
            foreach (int size in Boxes.Sizes)
            {
                Label l = new Label { Text = size + " SCU", AutoSize = false, Width = 70, Height = 28, TextAlign = ContentAlignment.MiddleRight, Location = new Point(16, y), ForeColor = Theme.Accent, Font = Theme.Semibold };
                Button minus = Theme.MakeButton("−", Theme.ButtonKind.Secondary, null);
                minus.AutoSize = false; minus.Size = new Size(32, 28); minus.Padding = new Padding(0); minus.Location = new Point(96, y);
                TextBox tb = new TextBox { Width = 56, Location = new Point(134, y + 2), TextAlign = HorizontalAlignment.Center, Font = Theme.Semibold };
                Theme.StyleInput(tb);
                int c; tb.Text = start.TryGetValue(size, out c) ? c.ToString() : "0";
                Button plus = Theme.MakeButton("+", Theme.ButtonKind.Secondary, null);
                plus.AutoSize = false; plus.Size = new Size(32, 28); plus.Padding = new Padding(0); plus.Location = new Point(196, y);
                Label scu = new Label { AutoSize = true, ForeColor = Theme.Muted, Location = new Point(240, y + 5) };
                counts[size] = tb;
                int s = size;
                tb.KeyPress += delegate(object o, KeyPressEventArgs e) { if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled = true; };
                tb.TextChanged += delegate { scu.Text = Get(s) > 0 ? "= " + (Get(s) * s) + " SCU" : ""; Recalc(); };
                tb.Enter += delegate { tb.SelectAll(); };
                minus.Click += delegate { tb.Text = Math.Max(0, Get(s) - 1).ToString(); };
                plus.Click += delegate { tb.Text = (Get(s) + 1).ToString(); };
                Controls.Add(l); Controls.Add(minus); Controls.Add(tb); Controls.Add(plus); Controls.Add(scu);
                scu.Text = Get(s) > 0 ? "= " + (Get(s) * s) + " SCU" : "";
                y += 34;
            }

            summary.AutoSize = true; summary.Location = new Point(16, y + 8); summary.Font = new Font("Segoe UI Semibold", 12f); summary.ForeColor = Theme.Highlight;
            Controls.Add(summary);
            Label orLbl = new Label { Text = "No boxes to count? Just the total:", AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Small, Location = new Point(16, y + 42) };
            totalBox.Location = new Point(220, y + 38); totalBox.Width = 70; Theme.StyleInput(totalBox); totalBox.TextAlign = HorizontalAlignment.Center;
            totalBox.Text = initialTotal > 0 ? initialTotal.ToString() : "";
            totalBox.KeyPress += delegate(object o, KeyPressEventArgs e) { if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled = true; };
            totalBox.TextChanged += delegate
            {
                if (updating) return;
                updating = true;
                foreach (TextBox tb in counts.Values) tb.Text = "0";   // a typed total replaces the box list
                updating = false;
                Recalc();
            };
            Label orUnit = new Label { Text = "SCU", AutoSize = true, ForeColor = Theme.Muted, Location = new Point(296, y + 42) };
            Controls.Add(orLbl); Controls.Add(totalBox); Controls.Add(orUnit);

            Button ok = Theme.MakeButton("Save", Theme.ButtonKind.Primary, delegate { Accept(); });
            Button clear = Theme.MakeButton("Clear", Theme.ButtonKind.Secondary, delegate { foreach (TextBox tb in counts.Values) tb.Text = "0"; });
            Button cancel = Theme.MakeButton("Cancel", Theme.ButtonKind.Secondary, null); cancel.DialogResult = DialogResult.Cancel;
            ok.Location = new Point(220, y + 82); clear.Location = new Point(300, y + 82); cancel.Location = new Point(375, y + 82);
            Controls.Add(ok); Controls.Add(clear); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
            ClientSize = new Size(470, y + 128);

            Recalc();
            Shown += delegate { counts[8].Focus(); counts[8].SelectAll(); };
        }

        int Get(int size)
        {
            int v; int.TryParse(counts[size].Text, out v); return Math.Max(0, v);
        }

        string CurrentList()
        {
            SortedDictionary<int, int> d = new SortedDictionary<int, int>();
            foreach (int size in Boxes.Sizes) if (Get(size) > 0) d[size] = Get(size);
            return Boxes.Format(d);
        }

        void Recalc()
        {
            string list = CurrentList();
            if (list.Length > 0)
            {
                if (!updating) { updating = true; totalBox.Text = Boxes.Total(list).ToString(); updating = false; }
                summary.Text = "Total: " + Boxes.Total(list) + " SCU in " + Boxes.Count(list) + " container" + (Boxes.Count(list) == 1 ? "" : "s");
            }
            else
            {
                int t; int.TryParse(totalBox.Text, out t);
                summary.Text = "Total: " + t + " SCU";
            }
        }

        void Accept()
        {
            ResultContainers = CurrentList();
            if (ResultContainers.Length > 0) ResultTotal = Boxes.Total(ResultContainers);
            else
            {
                int t;
                if (!int.TryParse(totalBox.Text, out t)) { totalBox.Focus(); return; }
                ResultTotal = t;
            }
            DialogResult = DialogResult.OK;
        }
    }
}
