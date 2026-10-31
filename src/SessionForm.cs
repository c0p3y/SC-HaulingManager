// SC Hauling Manager - C0P3Y | Icarus Interstellar Inc.
// Session summary: time, legs, aUEC with timestamps and the session stats, for the current session and earlier ones.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace HaulingManager
{
    public class SessionForm : Form
    {
        readonly SessionStore store;
        readonly Action save, togglePause, endSession;
        readonly ListBox list = new ListBox();
        readonly FlowLayoutPanel tiles = new FlowLayoutPanel();
        readonly DataGridView grid;
        readonly Label heading = new Label();
        readonly Button pauseBtn, endBtn, deleteBtn, exportBtn;
        readonly Timer tick = new Timer { Interval = 1000 };
        readonly Dictionary<string, Label> values = new Dictionary<string, Label>();
        readonly Dictionary<string, Label> notes = new Dictionary<string, Label>();
        List<WorkSession> shown = new List<WorkSession>();
        int lastEventCount = -1;
        WorkSession lastSession;

        public SessionForm(SessionStore store, Action save, Action togglePause, Action endSession)
        {
            this.store = store; this.save = save; this.togglePause = togglePause; this.endSession = endSession;
            Text = "Session summary";
            Size = new Size(1180, 760); MinimumSize = new Size(900, 560);
            StartPosition = FormStartPosition.CenterParent;
            Theme.ApplyForm(this);
            ShowInTaskbar = false;

            // ---- sessions list
            Panel left = new Panel { Dock = DockStyle.Left, Width = 270, Padding = new Padding(12, 12, 6, 12) };
            Label lh = new Label { Text = "SESSIONS", Dock = DockStyle.Top, Height = 26, Font = Theme.SectionFont, ForeColor = Theme.Accent };
            list.Dock = DockStyle.Fill; list.BorderStyle = BorderStyle.None; list.BackColor = Theme.Panel; list.ForeColor = Theme.Text;
            list.DrawMode = DrawMode.OwnerDrawFixed; list.ItemHeight = 44; list.IntegralHeight = false;
            list.DrawItem += DrawSessionItem;
            list.SelectedIndexChanged += delegate { ShowSelected(true); };
            left.Controls.Add(list); left.Controls.Add(lh);

            // ---- right side
            Panel right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 12, 12, 8) };
            heading.Dock = DockStyle.Top; heading.Height = 28; heading.Font = Theme.Semibold; heading.ForeColor = Theme.Text; heading.UseMnemonic = false;
            tiles.Dock = DockStyle.Top; tiles.Height = 176; tiles.Resize += delegate { SizeTiles(); }; tiles.WrapContents = true; tiles.Padding = new Padding(0, 2, 0, 6);
            foreach (string[] t in new[] {
                new[] { "earned", "aUEC EARNED" }, new[] { "perhour", "aUEC PER HOUR" }, new[] { "active", "ACTIVE TIME" }, new[] { "total", "TOTAL TIME" },
                new[] { "contracts", "CONTRACTS" }, new[] { "boxes", "BOXES DELIVERED" }, new[] { "scu", "SCU DELIVERED" }, new[] { "max", "MAX SCU ABOARD" },
                new[] { "legs", "ROUTE LEGS" }, new[] { "avgleg", "AVERAGE LEG" } })
                tiles.Controls.Add(Tile(t[0], t[1]));

            grid = Theme.MakeList(false);
            Theme.Col(grid, "Time", "TIME", 9, 70, false);
            Theme.Col(grid, "Active", "ACTIVE", 8, 64, false);
            Theme.Col(grid, "Leg", "LEG TIME", 8, 64, false);
            Theme.Col(grid, "Event", "EVENT", 14, 128, false);
            Theme.Col(grid, "Contract", "#", 5, 40, false);
            Theme.Col(grid, "Detail", "DETAIL", 32, 160, false);
            Theme.Col(grid, "Scu", "SCU", 6, 46, false);
            Theme.Col(grid, "Reward", "aUEC", 10, 80, false);
            Theme.Col(grid, "Running", "SESSION TOTAL", 11, 90, false);
            grid.DataError += delegate(object s, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };

            FlowLayoutPanel btns = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(0, 8, 0, 0), WrapContents = false };
            pauseBtn = Theme.MakeButton("Pause", Theme.ButtonKind.Accent, delegate { togglePause(); RefreshAll(); });
            endBtn = Theme.MakeButton("End session", Theme.ButtonKind.Secondary, delegate { EndCurrent(); });
            exportBtn = Theme.MakeButton("Export CSV...", Theme.ButtonKind.Secondary, delegate { Export(); });
            deleteBtn = Theme.MakeButton("Delete session", Theme.ButtonKind.Secondary, delegate { DeleteSelected(); });
            Button close = Theme.MakeButton("Close", Theme.ButtonKind.Secondary, delegate { Close(); });
            new ToolTip().SetToolTip(pauseBtn, "Paused time doesn't count toward aUEC per hour.");
            new ToolTip().SetToolTip(endBtn, "Closes this session and keeps it in the list. The next contract you take starts a new one.");
            btns.Controls.AddRange(new Control[] { pauseBtn, endBtn, exportBtn, deleteBtn, close });

            right.Controls.Add(grid); right.Controls.Add(btns); right.Controls.Add(tiles); right.Controls.Add(heading);
            Controls.Add(right); Controls.Add(left);

            tick.Tick += delegate { ShowSelected(false); };
            Shown += delegate { RefreshAll(); tick.Start(); };
            FormClosed += delegate { tick.Stop(); tick.Dispose(); };
        }

        Control Tile(string key, string caption)
        {
            Panel p = new Panel { Width = 172, Height = 78, Margin = new Padding(0, 0, 8, 8), BackColor = Theme.Panel };
            p.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen b = new Pen(Theme.Border)) e.Graphics.DrawRectangle(b, 0, 0, p.Width - 1, p.Height - 1);
                using (SolidBrush a = new SolidBrush(key == "earned" || key == "perhour" ? Theme.Highlight : Theme.Accent)) e.Graphics.FillRectangle(a, 0, 0, 3, p.Height);
            };
            Label c = new Label { Text = caption, Location = new Point(12, 7), AutoSize = true, Font = Theme.Small, ForeColor = Theme.Muted };
            Label v = new Label { Text = "-", Location = new Point(11, 24), AutoSize = true, Font = new Font("Segoe UI Semibold", 15f),
                                  ForeColor = key == "earned" || key == "perhour" ? Theme.Highlight : Theme.Text, UseMnemonic = false };
            Label n = new Label { Text = "", Location = new Point(12, 56), AutoSize = true, Font = Theme.Small, ForeColor = Theme.Muted, UseMnemonic = false };
            p.Controls.Add(c); p.Controls.Add(v); p.Controls.Add(n);
            values[key] = v; notes[key] = n;
            return p;
        }

        void SizeTiles()
        {
            int w = Math.Max(130, (tiles.ClientSize.Width - 5 * 8) / 5);
            foreach (Control c in tiles.Controls) c.Width = w;
        }

        void Set(string key, string value, string note) { values[key].Text = value; notes[key].Text = note ?? ""; }

        // ---------------------------------------------------------------- sessions list
        public void RefreshAll()
        {
            WorkSession keep = Selected();
            shown = new List<WorkSession>();
            if (store.Current != null) shown.Add(store.Current);
            shown.AddRange(store.Past);
            list.BeginUpdate();
            list.Items.Clear();
            foreach (WorkSession s in shown) list.Items.Add(s);
            list.EndUpdate();
            int i = keep != null ? shown.IndexOf(keep) : -1;
            if (i < 0 && shown.Count > 0) i = 0;
            if (i >= 0) list.SelectedIndex = i;
            ShowSelected(true);
        }

        WorkSession Selected()
        {
            int i = list.SelectedIndex;
            return i >= 0 && i < shown.Count ? shown[i] : null;
        }

        void DrawSessionItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= shown.Count) return;
            WorkSession s = shown[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush bg = new SolidBrush(sel ? Theme.Selection : Theme.Panel)) e.Graphics.FillRectangle(bg, e.Bounds);
            SessionStats st = Sessions.Stats(s);
            DateTime start = Sessions.Parse(s.Started);
            string top = (s == store.Current ? (s.Running ? "● NOW  " : s.Paused ? "II PAUSED  " : "■ CURRENT  ") : "") + start.ToString("ddd d MMM  HH:mm");
            string bottom = Sessions.Short(st.Active) + "  ·  " + Sessions.Money(st.Earned) + " aUEC  ·  " + st.Completed + " done";
            Color tc = s == store.Current ? (s.Running ? Theme.Green : Theme.Highlight) : Theme.Text;
            using (SolidBrush b = new SolidBrush(tc)) e.Graphics.DrawString(top, Theme.Semibold, b, e.Bounds.X + 8, e.Bounds.Y + 4);
            using (SolidBrush b = new SolidBrush(Theme.Muted)) e.Graphics.DrawString(bottom, Theme.Small, b, e.Bounds.X + 8, e.Bounds.Y + 23);
            using (Pen p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, e.Bounds.X, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        }

        // ---------------------------------------------------------------- one session
        void ShowSelected(bool full)
        {
            WorkSession s = Selected();
            bool isCurrent = s != null && s == store.Current;
            pauseBtn.Visible = isCurrent;
            endBtn.Visible = isCurrent;
            deleteBtn.Visible = s != null && !isCurrent;
            exportBtn.Enabled = s != null;
            if (isCurrent) pauseBtn.Text = s.Running ? "II  Pause timer" : "▶  " + (s.Paused ? "Resume timer" : "Start timer");
            if (s == null)
            {
                heading.Text = "No sessions yet - the timer starts by itself when you take a contract.";
                foreach (string k in values.Keys.ToList()) Set(k, "-", "");
                grid.Rows.Clear(); lastSession = null;
                return;
            }
            SessionStats st = Sessions.Stats(s);
            DateTime start = Sessions.Parse(s.Started);
            heading.Text = (isCurrent ? (s.Running ? "Current session - timer running" : s.Paused ? "Current session - PAUSED" : "Current session - timer stopped (all delivered)") : "Session")
                           + "   ·   started " + start.ToString("ddd d MMM yyyy  HH:mm") + (string.IsNullOrEmpty(s.Ended) ? "" : "   ·   ended " + Sessions.Parse(s.Ended).ToString("HH:mm"));
            Set("earned", Sessions.Money(st.Earned), st.Completed + " contract(s) paid");
            Set("perhour", Sessions.Money((long)Math.Round(st.PerHour)), "active time only");
            Set("active", Sessions.Clock(st.Active), s.Running ? "timer running" : s.Paused ? "timer paused" : "timer stopped");
            Set("total", Sessions.Clock(st.Total), "paused " + Sessions.Short(st.Paused));
            Set("contracts", st.Taken + " / " + st.Completed, "taken / completed");
            Set("boxes", st.Boxes.ToString(), "containers dropped off");
            Set("scu", st.ScuDelivered.ToString(), "SCU delivered");
            Set("max", st.MaxAboard.ToString(), "most SCU aboard at once");
            Set("legs", (st.Pickups + st.Drops).ToString(), st.Pickups + " pick-ups, " + st.Drops + " drop-offs");
            Set("avgleg", st.Pickups + st.Drops > 0 ? Sessions.Short(st.AvgLeg) : "-", st.Pickups + st.Drops > 0 ? "longest " + Sessions.Short(st.LongestLeg) : "");
            if (full || s != lastSession || s.Events.Count != lastEventCount || EventsChanged(s)) FillGrid(s);
        }

        string lastSig = "";
        bool EventsChanged(WorkSession s)
        {
            string sig = string.Join(",", s.Events.Select(e => (e.Undone ? "u" : "") + e.Reward).ToArray());
            return sig != lastSig;
        }

        void FillGrid(WorkSession s)
        {
            lastSession = s; lastEventCount = s.Events.Count;
            lastSig = string.Join(",", s.Events.Select(e => (e.Undone ? "u" : "") + e.Reward).ToArray());
            Dictionary<SessionEvent, double> legs = Sessions.Legs(s).ToDictionary(k => k.Key, k => k.Value);
            int firstVisible = grid.FirstDisplayedScrollingRowIndex;
            grid.Rows.Clear();
            long running = 0;
            foreach (SessionEvent e in s.Events)
            {
                if (e.Kind == "complete" && !e.Undone) running += e.Reward;
                double leg; bool isLeg = legs.TryGetValue(e, out leg);
                string detail = e.Text + (e.Undone ? "   (undone - not counted)" : "") + (e.Kind == "drop" && e.Boxes > 0 ? "   [" + e.Boxes + " box" + (e.Boxes == 1 ? "" : "es") + "]" : "");
                int r = grid.Rows.Add(Sessions.Parse(e.Time).ToString("HH:mm:ss"), Sessions.Clock(TimeSpan.FromSeconds(e.Active)),
                                      isLeg ? Sessions.Short(TimeSpan.FromSeconds(leg)) : "", Sessions.Describe(e), e.Contract > 0 ? "#" + e.Contract : "",
                                      detail, e.Scu > 0 && e.Kind != "taken" && e.Kind != "complete" ? e.Scu.ToString() : e.Kind == "taken" ? e.Scu.ToString() : "",
                                      e.Kind == "complete" ? "+" + Sessions.Money(e.Reward) : e.Kind == "taken" && e.Reward > 0 ? "(" + Sessions.Money(e.Reward) + ")" : "",
                                      e.Kind == "complete" && !e.Undone ? Sessions.Money(running) : "");
                DataGridViewRow row = grid.Rows[r];
                Color c = e.Undone ? Theme.Mix(Theme.Muted, Theme.Bg, 0.35f)
                        : e.Kind == "complete" ? Theme.Highlight
                        : e.Kind == "pickup" ? Theme.Accent
                        : e.Kind == "drop" ? Theme.Text
                        : e.Kind == "taken" ? Theme.Green : Theme.Muted;
                row.DefaultCellStyle.ForeColor = c;
                if (e.Kind == "complete" && !e.Undone) row.DefaultCellStyle.Font = Theme.Semibold;
            }
            if (grid.Rows.Count > 0)
            {
                try { grid.FirstDisplayedScrollingRowIndex = firstVisible >= 0 && firstVisible < grid.Rows.Count && s == lastSession && firstVisible > 0 ? firstVisible : Math.Max(0, grid.Rows.Count - 1); }
                catch { }
                grid.ClearSelection();
            }
        }

        // ---------------------------------------------------------------- buttons
        void EndCurrent()
        {
            if (store.Current == null) return;
            if (MessageBox.Show(this, "End this session? It stays in the list on the left.\r\nThe next contract you take starts a new session.", "End session", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            endSession();
            RefreshAll();
        }

        void DeleteSelected()
        {
            WorkSession s = Selected();
            if (s == null || s == store.Current) return;
            if (MessageBox.Show(this, "Delete the session from " + Sessions.Parse(s.Started).ToString("ddd d MMM HH:mm") + "? This can't be undone.", "Delete session", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            store.Past.Remove(s);
            save();
            RefreshAll();
        }

        void Export()
        {
            WorkSession s = Selected();
            if (s == null) return;
            using (SaveFileDialog d = new SaveFileDialog { Filter = "CSV (spreadsheet)|*.csv", FileName = "hauling-session-" + Sessions.Parse(s.Started).ToString("yyyy-MM-dd_HHmm") + ".csv" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try { File.WriteAllText(d.FileName, Sessions.Csv(s), new System.Text.UTF8Encoding(true)); }
                catch (Exception ex) { MessageBox.Show(this, "Could not save: " + ex.Message, "Export"); }
            }
        }
    }
}
