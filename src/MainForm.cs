// SC Hauling Manager - C0P3Y | Icarus Interstellar Inc.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HaulingManager
{
    public class MainForm : Form
    {
        public const string AppName = "SC Hauling Manager";
        public const string Version = "0.9.0";
        const int HotkeyId = 0xC0B3;

        AppSettings settings;
        AppData data;
        LocationDb db;
        RouteResult route;
        bool busy, hotkeyRegistered, pollWasDown, populatingRoute, outpostHighlight;
        DateTime lastTrigger = DateTime.MinValue;
        Bitmap lastFullCapture, lastProcessed;
        string lastText = "";
        int countdown;
        int checkedPid = -1; bool? gameElevated;
        readonly bool selfElevated = NativeMethods.IsCurrentProcessElevated();
        KeyboardHook keyHook;

        DataGridView contractsGrid, legsGrid, routeGrid;
        HoldState holdState;
        HoldModel hold;
        HoldPanel holdPanel;
        readonly ComboBox shipBox = new ComboBox();
        readonly Label shipScuLabel = new Label();
        bool settingShip;
        SuggestCombo shipSuggest, startSuggest;
        readonly TextBox capacityBox = new TextBox();
        readonly ComboBox startBox = new ComboBox();
        readonly Label routeSummary = new Label();
        readonly Label totalsLabel = new Label();
        readonly Label outpostBanner = new Label();
        readonly Label gameStatus = new Label(), hotkeyStatus = new Label(), messageStatus = new Label();
        Button outpostButton, delayButton, adminButton;
        readonly Timer gameTimer = new Timer(), pollTimer = new Timer(), countdownTimer = new Timer();

        public MainForm()
        {
            settings = Storage.Load<AppSettings>("settings.json");
            Theme.Apply(Theme.Resolve(settings.ThemeName ?? Theme.DefaultTheme, settings.ThemeColors));
            if (settings.LayoutVersion < 2) { settings.LayoutColumn = 0.5; settings.LayoutRow = 0.5; settings.LayoutVersion = 2; }   // v0.8.1: four equal panels
            data = Storage.Load<AppData>("contracts.json");
            if (data.Contracts == null) data.Contracts = new List<Contract>();
            if (data.Journal == null) data.Journal = new List<StepRecord>();
            if (data.NextRecord < 1) data.NextRecord = 1;
            db = LocationDb.LoadOrCreate();
            ContractParser.Places = db;
            holdState = Storage.Load<HoldState>("hold.json");
            if (holdState.Boxes == null) holdState.Boxes = new List<CargoBox>();
            hold = new HoldModel(holdState);
            hold.SetShip(ShipDb.Find(settings.ShipName));
            RepairContracts();
            Renumber();
            // route history that no longer matches what's loaded (older versions didn't correct it after hand edits)
            foreach (Contract c in data.Contracts)
                for (int li = 0; li < c.Legs.Count; li++)
                {
                    int rec = data.Journal.Where(r => r.IsPickup && r.ContractNumber == c.Number).SelectMany(r => r.Parts).Where(p => p.LegIndex == li).Sum(p => p.Scu);
                    if (rec > c.Legs[li].PickedClamped()) ReconcileJournal(c, c.Legs[li]);
                }

            Text = AppName + "  v" + Version;
            Size = new Size(1600, 960);
            MinimumSize = new Size(1200, 720);
            StartPosition = FormStartPosition.CenterScreen;
            Theme.ApplyForm(this);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildUi();
            TopMost = settings.AlwaysOnTop;
            capacityBox.Text = settings.ShipCapacity > 0 ? settings.ShipCapacity.ToString() : "";
            SelectShipInBox();
            startBox.Text = settings.StartLocation ?? "";

            gameTimer.Interval = 2000; gameTimer.Tick += delegate { UpdateGameStatus(); }; gameTimer.Start();
            pollTimer.Interval = 30; pollTimer.Tick += OnPollTick;
            countdownTimer.Interval = 1000; countdownTimer.Tick += OnCountdownTick;

            RefreshAll();
            UpdateGameStatus();
            Shown += delegate
            {
                try { ApplyHotkey(); }
                catch (Exception ex) { hotkeyStatus.Text = "Hotkey failed: " + ex.Message; }
                if (Environment.GetEnvironmentVariable("SCHM_SKIP_OCR_CHECK") == "1") return;
                Task.Run(() => WindowsOcr.Initialize()).ContinueWith(t =>
                {
                    if (t.Result != null) BeginInvoke(new Action(() => MessageBox.Show(this, t.Result, "OCR", MessageBoxButtons.OK, MessageBoxIcon.Warning)));
                });
            };
            FormClosing += delegate
            {
                if (overlay != null && !overlay.IsDisposed) { settings.OverlayOn = overlay.Visible; }
                SaveAll(); UnregisterHotkey(); if (keyHook != null) keyHook.Dispose();
                foreach (StepHotkey h in stepKeys) { if (h.Hook != null) h.Hook.Dispose(); if (h.Registered) NativeMethods.UnregisterHotKey(Handle, h.Id); }
                if (overlay != null && !overlay.IsDisposed) overlay.Dispose();
            };
            Shown += delegate { if (settings.OverlayOn) ShowOverlay(true); };
        }

        // ================================================================ UI
        void BuildUi()
        {
            // ---- header
            Panel header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Theme.Header, Padding = new Padding(16, 8, 12, 8) };
            Panel titles = new Panel { Dock = DockStyle.Left, Width = 330 };
            Label title = new Label { Text = "SC HAULING MANAGER", Font = Theme.TitleFont, ForeColor = Theme.Accent, AutoSize = true, Location = new Point(0, 2) };
            Label sub = new Label { Text = "C0P3Y | Icarus Interstellar Inc.   ·   v" + Version, Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true, Location = new Point(2, 30) };
            titles.Controls.Add(title); titles.Controls.Add(sub);

            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
            actions.Controls.Add(Theme.MakeButton("About", Theme.ButtonKind.Secondary, delegate { ShowAbout(); }));
            actions.Controls.Add(Theme.MakeButton("Settings", Theme.ButtonKind.Secondary, delegate { OpenSettings(); }));
            overlayButton = Theme.MakeButton("Overlay", Theme.ButtonKind.Secondary, delegate { ToggleOverlay(); });
            new ToolTip().SetToolTip(overlayButton, "In-game route overlay: shows your route steps over Star Citizen (Borderless / Windowed).\r\nDrag it to move, drag an edge to resize. Hotkeys in Settings tick off steps without leaving the game.");
            actions.Controls.Add(overlayButton);
            actions.Controls.Add(Theme.MakeButton("Add Manually", Theme.ButtonKind.Secondary, delegate { AddManual(); }));
            actions.Controls.Add(Theme.MakeButton("Last Capture", Theme.ButtonKind.Secondary, delegate { ShowLastCapture(); }));
            actions.Controls.Add(Theme.MakeButton("Select Region", Theme.ButtonKind.Secondary, delegate { SelectRegion(); }));
            delayButton = Theme.MakeButton("Read in " + settings.CaptureDelaySeconds + "s", Theme.ButtonKind.Accent, delegate { StartCountdown(); });
            actions.Controls.Add(delayButton);
            actions.Controls.Add(Theme.MakeButton("READ CONTRACT", Theme.ButtonKind.Primary, delegate { Trigger(); }));
            header.Controls.Add(actions); header.Controls.Add(titles);

            // ---- status bar
            Panel status = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Theme.Header, Padding = new Padding(14, 0, 8, 0) };
            foreach (Label l in new[] { gameStatus, hotkeyStatus })
            {
                l.AutoSize = true; l.Dock = DockStyle.Left; l.Padding = new Padding(0, 7, 22, 0); l.Font = Theme.Semibold;
            }
            messageStatus.Dock = DockStyle.Fill; messageStatus.ForeColor = Theme.Muted; messageStatus.TextAlign = ContentAlignment.MiddleLeft; messageStatus.AutoEllipsis = true;
            adminButton = Theme.MakeButton("Restart as Admin", Theme.ButtonKind.Warning, delegate { RestartAsAdmin(); });
            adminButton.Dock = DockStyle.Right; adminButton.Visible = false; adminButton.Margin = new Padding(0); adminButton.Padding = new Padding(8, 0, 8, 0);
            status.Controls.Add(messageStatus); status.Controls.Add(hotkeyStatus); status.Controls.Add(gameStatus); status.Controls.Add(adminButton);

            // ---- contracts card
            Panel cBody; Label cTitle;
            Panel cCard = Theme.MakeCard("Accepted Contracts", out cBody, out cTitle);
            contractsGrid = Theme.MakeList(true);
            Theme.Col(contractsGrid, "Num", "#", 6, 40, false);
            Theme.Col(contractsGrid, "Title", "CONTRACT", 44, 120, false);
            Theme.Col(contractsGrid, "Reward", "REWARD", 13, 70, true);
            Theme.Col(contractsGrid, "Left", "SCU LEFT", 10, 60, true);
            Theme.Col(contractsGrid, "Lines", "LINES", 7, 45, true);
            Theme.Col(contractsGrid, "Outposts", "OUTPOSTS", 11, 70, false);
            Theme.Col(contractsGrid, "Status", "STATUS", 12, 80, false);
            contractsGrid.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) EditSelected(); };
            contractsGrid.SelectionChanged += delegate { HighlightLegs(); };
            contractsGrid.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Delete) RemoveSelected(); };

            outpostBanner.Dock = DockStyle.Top; outpostBanner.Height = 0; outpostBanner.Padding = new Padding(8, 0, 8, 0);
            outpostBanner.TextAlign = ContentAlignment.MiddleLeft; outpostBanner.Font = Theme.Semibold;

            FlowLayoutPanel cBtns = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(0, 6, 0, 0), WrapContents = false };
            cBtns.Controls.Add(Theme.MakeButton("Edit", Theme.ButtonKind.Secondary, delegate { EditSelected(); }));
            cBtns.Controls.Add(Theme.MakeButton("Remove", Theme.ButtonKind.Secondary, delegate { RemoveSelected(); }));
            cBtns.Controls.Add(Theme.MakeButton("Mark Complete", Theme.ButtonKind.Secondary, delegate { MarkSelectedComplete(); }));
            cBtns.Controls.Add(Theme.MakeButton("Clear Completed", Theme.ButtonKind.Secondary, delegate { ClearCompleted(); }));
            outpostButton = Theme.MakeButton("⚠ Check Outposts", Theme.ButtonKind.Warning, delegate { ToggleOutpostCheck(); });
            cBtns.Controls.Add(outpostButton);
            totalsLabel.AutoSize = true; totalsLabel.ForeColor = Theme.Muted; totalsLabel.Padding = new Padding(10, 8, 0, 0);
            cBtns.Controls.Add(totalsLabel);
            cBody.Controls.Add(contractsGrid); cBody.Controls.Add(outpostBanner); cBody.Controls.Add(cBtns);

            // ---- cargo card
            Panel lBody; Label lTitle;
            Panel lCard = Theme.MakeCard("Cargo to Move", out lBody, out lTitle);
            legsGrid = Theme.MakeList(false);
            Theme.Col(legsGrid, "Num", "#", 5, 36, false);
            Theme.Col(legsGrid, "Material", "MATERIAL", 12, 70, false);
            Theme.Col(legsGrid, "Scu", "SCU", 6, 45, true);
            Theme.Col(legsGrid, "Picked", "LOADED", 9, 72, true);
            Theme.Col(legsGrid, "Boxes", "CONTAINERS", 17, 110, false);
            Theme.Col(legsGrid, "Delivered", "DELIV.", 7, 52, true);
            Theme.Col(legsGrid, "Pickup", "PICKUP", 22, 90, false);
            Theme.Col(legsGrid, "PickupType", "TYPE", 11, 70, false);
            Theme.Col(legsGrid, "Dropoff", "DROP-OFF", 22, 90, false);
            Theme.Col(legsGrid, "DropoffType", "TYPE", 11, 70, false);
            legsGrid.CellFormatting += OnLegsCellFormatting;
            // LOADED is editable: type what you actually picked up (needed when a contract doesn't say how much waits at a pickup)
            legsGrid.ReadOnly = false;
            foreach (DataGridViewColumn col in legsGrid.Columns) col.ReadOnly = col.Name != "Picked";
            legsGrid.Columns["Picked"].HeaderText = "LOADED \u270E";
            legsGrid.EditMode = DataGridViewEditMode.EditOnEnter;
            legsGrid.CellEndEdit += OnLegsLoadedEdited;
            legsGrid.CellClick += delegate(object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0 || legsGrid.Columns[e.ColumnIndex].Name != "Boxes") return;
                DataGridViewRow row = legsGrid.Rows[e.RowIndex];
                CargoLeg leg = row.Cells["Boxes"].Tag as CargoLeg;
                Contract c = row.Tag as Contract;
                if (leg != null && c != null) BeginInvoke(new Action(() => OpenContainers(c, leg)));
            };
            legsGrid.EditingControlShowing += delegate(object s, DataGridViewEditingControlShowingEventArgs e)
            {
                TextBox tb = e.Control as TextBox;
                if (tb == null) return;
                tb.BackColor = Theme.Surface; tb.ForeColor = Theme.Highlight;
                tb.KeyPress -= DigitsOnly; tb.KeyPress += DigitsOnly;
            };
            lBody.Controls.Add(legsGrid);

            // ---- route card
            Panel rBody; Label rTitle;
            Panel rCard = Theme.MakeCard("Route Plan", out rBody, out rTitle);
            TableLayoutPanel rTop = new TableLayoutPanel { Dock = DockStyle.Top, Height = 74, ColumnCount = 4, RowCount = 2 };
            rTop.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
            rTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            rTop.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            rTop.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            rTop.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            rTop.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            shipBox.DropDownStyle = ComboBoxStyle.DropDown; shipBox.Dock = DockStyle.Fill; Theme.StyleInput(shipBox); shipBox.FlatStyle = FlatStyle.Flat;
            shipBox.MaxDropDownItems = 20;
            shipSuggest = new SuggestCombo(shipBox);
            shipSuggest.SetItems(new[] { CustomShip }.Concat(ShipDb.All.Select(x => x.Name)));
            shipSuggest.Picked += name => { if (!settingShip) OnShipChosen(name); };
            shipBox.Leave += delegate
            {
                if (settingShip) return;
                string typed = shipBox.Text.Trim();
                ShipInfo exact = ShipDb.Find(typed);
                if (exact != null && exact.Name != settings.ShipName) OnShipChosen(exact.Name);
                else SelectShipInBox();     // half-typed text: go back to the current ship
            };
            shipScuLabel.AutoSize = true; shipScuLabel.Padding = new Padding(8, 8, 6, 0); shipScuLabel.Font = Theme.Semibold; shipScuLabel.ForeColor = Theme.Highlight;
            capacityBox.Width = 70; Theme.StyleInput(capacityBox); capacityBox.Visible = false;
            capacityBox.KeyPress += delegate(object s, KeyPressEventArgs e) { if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled = true; };
            capacityBox.TextChanged += delegate { if (settingShip) return; int v; int.TryParse(capacityBox.Text, out v); settings.ShipCapacity = v; shipScuLabel.Text = "SCU"; };
            FlowLayoutPanel scuFlow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            scuFlow.Controls.Add(capacityBox); scuFlow.Controls.Add(shipScuLabel);
            startBox.DropDownStyle = ComboBoxStyle.DropDown; startBox.Dock = DockStyle.Fill; Theme.StyleInput(startBox); startBox.FlatStyle = FlatStyle.Flat;
            startBox.TextChanged += delegate { settings.StartLocation = startBox.Text; };
            startBox.MaxDropDownItems = 20;
            startSuggest = new SuggestCombo(startBox);
            startSuggest.Picked += loc => { startBox.Text = loc; startBox.SelectionStart = loc.Length; };
            Button plan = Theme.MakeButton("PLAN ROUTE", Theme.ButtonKind.Primary, delegate { PlanRoute(); });
            plan.AutoSize = false; plan.Size = new Size(130, 32); plan.Margin = new Padding(6, 2, 0, 0);
            rTop.Controls.Add(new Label { Text = "Ship", AutoSize = true, Padding = new Padding(0, 9, 4, 0) }, 0, 0);
            rTop.Controls.Add(shipBox, 1, 0);
            rTop.Controls.Add(scuFlow, 2, 0);
            rTop.Controls.Add(plan, 3, 0);
            rTop.Controls.Add(new Label { Text = "Start at", AutoSize = true, Padding = new Padding(0, 8, 4, 0) }, 0, 1);
            rTop.Controls.Add(startBox, 1, 1);
            Label capHint = new Label { Text = "blank = best first stop", AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Small, Padding = new Padding(8, 9, 0, 0) };
            rTop.Controls.Add(capHint, 2, 1); rTop.SetColumnSpan(capHint, 2);

            routeGrid = Theme.MakeList(false);
            routeGrid.ReadOnly = false;
            DataGridViewCheckBoxColumn done = new DataGridViewCheckBoxColumn { Name = "Done", HeaderText = "DONE", FillWeight = 7, MinimumWidth = 46, FlatStyle = FlatStyle.Flat };
            routeGrid.Columns.Add(done);
            Theme.Col(routeGrid, "Action", "ACTION", 12, 75, false).ReadOnly = true;
            Theme.Col(routeGrid, "Scu", "SCU", 8, 50, true).ReadOnly = true;
            Theme.Col(routeGrid, "Material", "MATERIAL", 18, 70, false).ReadOnly = true;
            Theme.Col(routeGrid, "Contract", "CONTRACT", 9, 60, false).ReadOnly = true;
            Theme.Col(routeGrid, "Dest", "DELIVER TO", 40, 120, false).ReadOnly = true;
            routeGrid.RowPrePaint += OnRouteRowPrePaint;
            foreach (DataGridView g in new[] { contractsGrid, legsGrid, routeGrid })
                g.DataError += delegate(object s, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };
            routeGrid.CurrentCellDirtyStateChanged += delegate
            {
                if (routeGrid.IsCurrentCellDirty && routeGrid.CurrentCell is DataGridViewCheckBoxCell) routeGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            routeGrid.CellValueChanged += OnRouteCellValueChanged;

            routeSummary.Dock = DockStyle.Bottom; routeSummary.Height = 58; routeSummary.Padding = new Padding(2, 8, 2, 0);
            routeSummary.ForeColor = Theme.Muted;
            rBody.Controls.Add(routeGrid); rBody.Controls.Add(routeSummary); rBody.Controls.Add(rTop);

            // ---- cargo hold card (3D)
            Panel hBody; Label hTitle;
            Panel hCard = Theme.MakeCard("Cargo Hold", out hBody, out hTitle);
            holdPanel = new HoldPanel(hold);
            holdPanel.RankOf = RankOfBox;
            holdPanel.DestinationOf = b => CanonicalPlace(LegOf(b) != null ? LegOf(b).Dropoff : b.Dropoff);
            if (string.IsNullOrWhiteSpace(settings.HoldColors) || settings.HoldColors == HoldPanel.OldDefaultColors) settings.HoldColors = HoldPanel.DefaultColors;
            HoldPanel.SetColors(settings.HoldColors);
            holdPanel.PackMode = settings.PackMode;
            holdPanel.PackModeChanged += delegate { settings.PackMode = holdPanel.PackMode; SaveAll(); };
            holdPanel.ExtraNeeded = ScuStillToCome;
            holdPanel.ContractLabelOf = b => "#" + b.ContractNumber;
            holdPanel.Undeliver = id => { StepRecord r = data.Journal.FirstOrDefault(x => x.Id == id); if (r != null) UndoRecord(r); };
            holdPanel.Changed += delegate { SaveHold(); };
            holdPanel.Status = t => messageStatus.Text = t;
            holdPanel.ExpandClicked += delegate { if (toggleHoldExpand != null) toggleHoldExpand(); };
            hBody.Controls.Add(holdPanel);

            Panel content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 6, 6, 4) };
            if (settings.LayoutMode == 1) BuildClassicLayout(content, cCard, lCard, rCard, hCard);
            else BuildGridLayout(content, cCard, lCard, rCard, hCard);
            Controls.Add(content); Controls.Add(Theme.MakeAccentLine()); Controls.Add(header); Controls.Add(status);
        }

        static SplitContainer Split(Orientation o)
        {
            return new SplitContainer { Dock = DockStyle.Fill, Orientation = o, BackColor = Theme.Bg, SplitterWidth = 6 };
        }

        // Classic (v0.7.1): two independent columns, each split top / bottom.
        void BuildClassicLayout(Panel content, Control contracts, Control cargo, Control route, Control holdCard)
        {
            SplitContainer left = Split(Orientation.Horizontal), right = Split(Orientation.Horizontal), main = Split(Orientation.Vertical);
            left.Panel1.Controls.Add(contracts); left.Panel2.Controls.Add(cargo);
            right.Panel1.Controls.Add(route); right.Panel2.Controls.Add(holdCard);
            main.Panel1.Controls.Add(left); main.Panel2.Controls.Add(right);
            content.Controls.Add(main);
            toggleHoldExpand = () =>
            {
                holdExpanded = !holdExpanded;
                main.Panel1Collapsed = holdExpanded; right.Panel1Collapsed = holdExpanded;
                holdPanel.SetExpanded(holdExpanded);
            };
            Load += delegate
            {
                main.SplitterDistance = (int)(ClientSize.Width * 0.47);
                left.SplitterDistance = (int)(left.Height * 0.42);
                right.SplitterDistance = (int)(right.Height * 0.47);
            };
        }

        // Aligned grid (default): two rows that share one column line, so the four cards line up.
        // Top: Accepted Contracts | Route Plan.  Bottom: Cargo to Move | Cargo Hold.
        // The lines you drag are kept as proportions, so the layout looks the same at any window size.
        void BuildGridLayout(Panel content, Control contracts, Control cargo, Control route, Control holdCard)
        {
            SplitContainer rows = Split(Orientation.Horizontal), top = Split(Orientation.Vertical), bottom = Split(Orientation.Vertical);
            foreach (SplitContainer sc in new[] { rows, top, bottom }) { sc.Panel1MinSize = 120; sc.Panel2MinSize = 120; }
            top.Panel1.Controls.Add(contracts); top.Panel2.Controls.Add(route);
            bottom.Panel1.Controls.Add(cargo); bottom.Panel2.Controls.Add(holdCard);
            rows.Panel1.Controls.Add(top); rows.Panel2.Controls.Add(bottom);
            content.Controls.Add(rows);

            bool applying = false, loaded = false;
            Func<double, double> clamp = v => v > 0.15 && v < 0.85 ? v : 0.5;
            Action apply = () =>
            {
                if (!loaded || applying || WindowState == FormWindowState.Minimized || rows.Height < 300 || rows.Width < 300) return;
                applying = true;
                try
                {
                    if (!rows.Panel1Collapsed && !rows.Panel2Collapsed)
                        rows.SplitterDistance = (int)Math.Round((rows.Height - rows.SplitterWidth) * clamp(settings.LayoutRow));
                    foreach (SplitContainer sc in new[] { top, bottom })
                        if (!sc.Panel1Collapsed && !sc.Panel2Collapsed)
                            sc.SplitterDistance = (int)Math.Round((sc.Width - sc.SplitterWidth) * clamp(settings.LayoutColumn));
                }
                catch { }
                applying = false;
            };
            // only a drag by you changes the proportions; resizes and our own adjustments don't
            bool dragCol = false, dragRow = false;
            top.SplitterMoving += delegate { dragCol = true; };
            bottom.SplitterMoving += delegate { dragCol = true; };
            rows.SplitterMoving += delegate { dragRow = true; };
            SplitterEventHandler colMoved = (s, e) =>
            {
                if (applying || !dragCol) return;
                dragCol = false;
                SplitContainer sc = (SplitContainer)s;
                if (sc.Width > sc.SplitterWidth) settings.LayoutColumn = sc.SplitterDistance / (double)(sc.Width - sc.SplitterWidth);
                apply();   // moves the other row's line to match
            };
            top.SplitterMoved += colMoved; bottom.SplitterMoved += colMoved;
            rows.SplitterMoved += delegate
            {
                if (applying || !dragRow) return;
                dragRow = false;
                if (rows.Height > rows.SplitterWidth) settings.LayoutRow = rows.SplitterDistance / (double)(rows.Height - rows.SplitterWidth);
            };
            Load += delegate { loaded = true; apply(); };
            Shown += delegate { apply(); };
            rows.SizeChanged += delegate { apply(); };
            Resize += delegate { apply(); };

            // Expand: the cargo hold takes the whole window; press again to put everything back
            toggleHoldExpand = () =>
            {
                holdExpanded = !holdExpanded;
                rows.Panel1Collapsed = holdExpanded;
                bottom.Panel1Collapsed = holdExpanded;
                holdPanel.SetExpanded(holdExpanded);
                apply();
            };
        }

        Action toggleHoldExpand;
        bool holdExpanded;

        // ================================================================ Hotkey
        void ApplyHotkey()
        {
            ApplyReadHotkey();
            ApplyStepHotkeys();
        }

        void ApplyReadHotkey()
        {
            UnregisterHotkey();
            pollTimer.Stop();
            if (keyHook != null) { keyHook.Dispose(); keyHook = null; }
            if (settings.HotkeyKey == 0) { SetHotkeyStatus("Hotkey: none", Theme.Muted); return; }
            string hk = SettingsForm.HotkeyText(settings.HotkeyMods, settings.HotkeyKey);

            if (settings.HotkeyMethod == 1)
            {
                pollWasDown = false; pollTimer.Start();
                SetHotkeyStatus("Hotkey: " + hk + "  (polling)", Theme.Accent);
                return;
            }
            bool hookOk = false;
            if (settings.HotkeyMethod == 0)
            {
                keyHook = new KeyboardHook();
                keyHook.Key = settings.HotkeyKey; keyHook.Mods = settings.HotkeyMods;
                keyHook.Pressed += delegate { Trigger(); };
                hookOk = keyHook.Install();
            }
            hotkeyRegistered = NativeMethods.RegisterHotKey(Handle, HotkeyId, (uint)settings.HotkeyMods | NativeMethods.MOD_NOREPEAT, (uint)(settings.HotkeyKey & 0xFFFF));
            if (hookOk || hotkeyRegistered) SetHotkeyStatus("Hotkey: " + hk + (hookOk ? "  (works in game)" : "  (system hotkey)"), Theme.Accent);
            else
            {
                SetHotkeyStatus("Hotkey: " + hk + "  (unavailable - pick another)", Theme.Red);
                ToastForm.Popup("Hotkey unavailable", hk + " could not be registered. Choose a different one in Settings.", true);
            }
        }

        void SetHotkeyStatus(string text, Color c) { hotkeyStatus.Text = text; hotkeyStatus.ForeColor = c; }

        void UnregisterHotkey()
        {
            if (hotkeyRegistered) { NativeMethods.UnregisterHotKey(Handle, HotkeyId); hotkeyRegistered = false; }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId) Trigger();
            if (m.Msg == NativeMethods.WM_HOTKEY)
                foreach (StepHotkey h in stepKeys) if (m.WParam.ToInt32() == h.Id) h.Fire();
            base.WndProc(ref m);
        }

        static bool Down(int vk) { return (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0; }

        void OnPollTick(object sender, EventArgs e)
        {
            int mods = settings.HotkeyMods;
            bool down = Down(settings.HotkeyKey & 0xFFFF)
                && ((mods & 2) == 0 || Down(0x11))
                && ((mods & 1) == 0 || Down(0x12))
                && ((mods & 4) == 0 || Down(0x10))
                && ((mods & 8) == 0 || Down(0x5B) || Down(0x5C));
            if (down && !pollWasDown && settings.HotkeyKey != 0) Trigger();
            pollWasDown = down;
            if (settings.HotkeyMethod == 1)
                foreach (StepHotkey h in stepKeys)
                {
                    bool d = Down(h.Key & 0xFFFF) && ((h.Mods & 2) == 0 || Down(0x11)) && ((h.Mods & 1) == 0 || Down(0x12))
                             && ((h.Mods & 4) == 0 || Down(0x10)) && ((h.Mods & 8) == 0 || Down(0x5B) || Down(0x5C));
                    if (d && !h.WasDown) h.Fire();
                    h.WasDown = d;
                }
        }

        // ================================================================ In-game route overlay + step hotkeys
        class StepHotkey
        {
            public int Id, Key, Mods; public Action Act; public KeyboardHook Hook; public bool Registered, WasDown;
            DateTime last = DateTime.MinValue;
            public Control Owner;
            public void Fire()
            {
                if ((DateTime.Now - last).TotalMilliseconds < 500) return;   // hook + system hotkey can both fire
                last = DateTime.Now;
                Owner.BeginInvoke(Act);                                       // never work inside the keyboard hook
            }
        }
        readonly List<StepHotkey> stepKeys = new List<StepHotkey>();
        OverlayForm overlay;
        List<OverlayLine> overlayLines = new List<OverlayLine>();
        List<RouteRow> plannedRows = new List<RouteRow>();

        void ApplyStepHotkeys()
        {
            foreach (StepHotkey h in stepKeys)
            {
                if (h.Hook != null) h.Hook.Dispose();
                if (h.Registered) NativeMethods.UnregisterHotKey(Handle, h.Id);
            }
            stepKeys.Clear();
            Add(HotkeyId + 1, settings.StepHotkeyKey, settings.StepHotkeyMods, CompleteCurrentStep);
            Add(HotkeyId + 2, settings.UndoHotkeyKey, settings.UndoHotkeyMods, UndoLastStep);
            Add(HotkeyId + 3, settings.OverlayHotkeyKey, settings.OverlayHotkeyMods, ToggleOverlay);
            if (settings.HotkeyMethod == 1 && stepKeys.Count > 0) pollTimer.Start();
            UpdateOverlay();
        }

        void Add(int id, int key, int mods, Action act)
        {
            if (key == 0) return;
            StepHotkey h = new StepHotkey { Id = id, Key = key, Mods = mods, Act = act, Owner = this };
            if (settings.HotkeyMethod == 0)
            {
                h.Hook = new KeyboardHook { Key = key, Mods = mods };
                StepHotkey cap = h;
                h.Hook.Pressed += delegate { cap.Fire(); };
                if (!h.Hook.Install()) { h.Hook.Dispose(); h.Hook = null; }
            }
            if (settings.HotkeyMethod != 1)
                h.Registered = NativeMethods.RegisterHotKey(Handle, id, (uint)mods | NativeMethods.MOD_NOREPEAT, (uint)(key & 0xFFFF));
            stepKeys.Add(h);
        }

        // Hotkey: tick off the highlighted step. No dialogs (they would pull you out of the game): a pickup records
        // the planned amount and any containers you already entered - fix it later in Cargo to Move if needed.
        void CompleteCurrentStep()
        {
            RouteRow rr = plannedRows.FirstOrDefault(r => !r.Applied);
            if (rr == null)
            {
                Notify(route == null ? "No route - press PLAN ROUTE first." : "Route complete - nothing left to tick.", true);
                return;
            }
            RouteAction a = rr.Actions[0];
            string what = (rr.IsPickup ? "Picked up " : "Delivered ") + rr.Actions.Sum(x => x.Scu) + " SCU " + a.Ref.Leg.Material + " at " +
                          HoldPanel.PlaceName(rr.IsPickup ? a.Ref.Leg.Pickup : a.Ref.Leg.Dropoff);
            if (rr.IsPickup) DoPickup(rr, true); else DoDrop(rr);
            Notify("✓ " + what, false);
        }

        void UndoLastStep()
        {
            HashSet<int> live = new HashSet<int>(data.Contracts.Select(c => c.Number));
            StepRecord last = data.Journal.LastOrDefault(r => live.Contains(r.ContractNumber));
            if (last == null) { Notify("Nothing to undo.", true); return; }
            string what = (last.IsPickup ? "pickup of " : "drop-off of ") + last.Scu() + " SCU " + last.Material;
            if (UndoRecord(last, true)) Notify("↶ Undid the " + what, false);
        }

        // feedback while you're in the game: the overlay updates itself; without it, a small popup
        void Notify(string text, bool warn)
        {
            messageStatus.Text = text;
            if (settings.Beep) { if (warn) SystemSounds.Exclamation.Play(); else SystemSounds.Asterisk.Play(); }
            if (overlay == null || !overlay.Visible) ToastForm.Popup("Route", text, warn);
        }

        void ToggleOverlay()
        {
            settings.OverlayOn = overlay == null || !overlay.Visible;
            ShowOverlay(settings.OverlayOn);
            SaveAll();
        }

        void ShowOverlay(bool on)
        {
            if (!on) { if (overlay != null) overlay.Hide(); UpdateOverlayButton(); return; }
            if (overlay == null || overlay.IsDisposed || overlay.ClickThrough != settings.OverlayClickThrough)
            {
                if (overlay != null && !overlay.IsDisposed) overlay.Dispose();
                overlay = new OverlayForm { ClickThrough = settings.OverlayClickThrough };
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                int w = Math.Max(220, settings.OverlayW), h = Math.Max(120, settings.OverlayH);
                int x = settings.OverlayX >= 0 ? settings.OverlayX : wa.Right - w - 40, y = settings.OverlayY >= 0 ? settings.OverlayY : wa.Top + 120;
                Rectangle r = new Rectangle(x, y, w, h);
                if (!Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(r))) r = new Rectangle(wa.Right - w - 40, wa.Top + 120, w, h);
                overlay.Bounds = r;
                overlay.Moved += delegate
                {
                    settings.OverlayX = overlay.Left; settings.OverlayY = overlay.Top; settings.OverlayW = overlay.Width; settings.OverlayH = overlay.Height;
                };
                overlay.VisibleChanged += delegate { if (!overlay.Visible) { settings.OverlayOn = false; } UpdateOverlayButton(); };
            }
            overlay.Opacity = Math.Max(30, Math.Min(100, settings.OverlayOpacity)) / 100.0;
            UpdateOverlay();
            if (!overlay.Visible) overlay.Show();
            UpdateOverlayButton();
        }

        void UpdateOverlayButton()
        {
            if (overlayButton == null) return;
            bool on = overlay != null && overlay.Visible;
            Theme.StyleButton(overlayButton, on ? Theme.ButtonKind.Accent : Theme.ButtonKind.Secondary);
            overlayButton.Text = on ? "Overlay ✓" : "Overlay";
        }
        Button overlayButton;

        void UpdateOverlay()
        {
            if (overlay == null || overlay.IsDisposed) return;
            List<string> keys = new List<string>();
            if (settings.StepHotkeyKey != 0) keys.Add(SettingsForm.HotkeyText(settings.StepHotkeyMods, settings.StepHotkeyKey) + " = done");
            if (settings.UndoHotkeyKey != 0) keys.Add(SettingsForm.HotkeyText(settings.UndoHotkeyMods, settings.UndoHotkeyKey) + " = undo");
            overlay.SetLines(overlayLines, string.Join("   ", keys.ToArray()));
        }

        // All hotkey paths end here; several can fire for one key press, so debounce.
        void Trigger()
        {
            if ((DateTime.Now - lastTrigger).TotalMilliseconds < 700) return;
            lastTrigger = DateTime.Now;
            BeginInvoke(new Action(CaptureAndRead));   // never do the work inside the keyboard hook
        }

        void StartCountdown()
        {
            if (countdownTimer.Enabled) { countdownTimer.Stop(); delayButton.Text = "Read in " + settings.CaptureDelaySeconds + "s"; messageStatus.Text = "Countdown cancelled."; return; }
            countdown = Math.Max(1, settings.CaptureDelaySeconds);
            delayButton.Text = "Cancel (" + countdown + ")";
            messageStatus.Text = "Switch to Star Citizen - reading in " + countdown + " s...";
            countdownTimer.Start();
        }

        void OnCountdownTick(object sender, EventArgs e)
        {
            countdown--;
            if (countdown <= 0)
            {
                countdownTimer.Stop();
                delayButton.Text = "Read in " + settings.CaptureDelaySeconds + "s";
                CaptureAndRead();
                return;
            }
            delayButton.Text = "Cancel (" + countdown + ")";
            messageStatus.Text = "Switch to Star Citizen - reading in " + countdown + " s...";
        }

        // ================================================================ Capture
        void UpdateGameStatus()
        {
            IntPtr h = ScreenCapture.FindGameWindow(settings.ProcessName);
            if (h == IntPtr.Zero)
            {
                gameStatus.Text = "●  Star Citizen: not running"; gameStatus.ForeColor = Theme.Muted;
                adminButton.Visible = false; checkedPid = -1;
                return;
            }
            int pid = ScreenCapture.GamePid(settings.ProcessName);
            if (pid != checkedPid) { checkedPid = pid; gameElevated = NativeMethods.IsProcessElevated(pid); }
            if (gameElevated == true && !selfElevated)
            {
                gameStatus.Text = "●  Star Citizen is running as admin - hotkey can't reach it"; gameStatus.ForeColor = Theme.Highlight;
                adminButton.Visible = true;
            }
            else
            {
                gameStatus.Text = "●  Star Citizen: running"; gameStatus.ForeColor = Theme.Green;
                adminButton.Visible = false;
            }
        }

        void RestartAsAdmin()
        {
            try
            {
                SaveAll();
                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, "--restart");
                psi.UseShellExecute = true; psi.Verb = "runas";
                Process.Start(psi);
                Close();
            }
            catch (Exception ex) { messageStatus.Text = "Restart cancelled: " + ex.Message; }
        }

        async void CaptureAndRead()
        {
            if (busy) return;
            busy = true;
            messageStatus.Text = "Reading contract...";
            try
            {
                // make sure our own popup is not in the screenshot
                if (ToastForm.CloseCurrent()) await Task.Delay(150);

                string err;
                Bitmap full = ScreenCapture.CaptureWindow(settings.ProcessName, settings.CaptureMethod, out err);
                if (full == null) { Fail(err); return; }
                if (lastFullCapture != null) lastFullCapture.Dispose();
                lastFullCapture = full;
                if (err != null) { Fail(err); return; }

                Bitmap region = settings.UseRegion
                    ? ImagePrep.Crop(full, settings.RegionX, settings.RegionY, settings.RegionW, settings.RegionH)
                    : (Bitmap)full.Clone();
                AppSettings snap = settings;
                CaptureResult cr = await Task.Run(() => { try { return ContractReader.Read(region, snap); } finally { region.Dispose(); } });

                lastText = cr.Text;
                if (lastProcessed != null) lastProcessed.Dispose();
                lastProcessed = cr.Processed;
                if (settings.SaveDebug) SaveDebug(full, cr);
                if (settings.Beep) SystemSounds.Asterisk.Play();

                ParseResult p = cr.Parse;
                if (p.Legs.Count == 0)
                {
                    Fail("No hauling objectives found. Open the contract details, or use Select Region / Last Capture to check what was read.");
                    return;
                }
                Contract c = p.ToContract();
                List<string> outposts = OutpostsOf(c);
                if (settings.AfterCapture == 1)
                {
                    Activate();
                    AddWithReview(c, "Review captured contract");
                }
                else
                {
                    string summary = AddOrUpdate(c);
                    string legsText = string.Join("\n", c.Legs.Select(l => l.Scu + " SCU " + l.Material + ":  " + Short(l.Pickup) + "  →  " + Short(l.Dropoff)).ToArray());
                    string warn = outposts.Count > 0 ? "⚠ Planetary outpost: " + string.Join(", ", outposts.ToArray()) : null;
                    if (c.Warnings.Length > 0) legsText += "\nCheck: " + c.Warnings;
                    ToastForm.Popup(summary, legsText, false, warn);
                }
                messageStatus.Text = "Read OK (" + cr.PassUsed + " pass, " + p.Legs.Count + " cargo lines)";
            }
            catch (Exception ex)
            {
                Fail("Read failed: " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message));
            }
            finally { busy = false; }
        }

        static string Short(string s) { return string.IsNullOrEmpty(s) ? "?" : s; }

        void Fail(string msg)
        {
            messageStatus.Text = msg;
            ToastForm.Popup("Hauling Manager", msg, true, null);
        }

        void SaveDebug(Bitmap full, CaptureResult cr)
        {
            try
            {
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string d = Storage.DebugDir;
                full.Save(Path.Combine(d, stamp + "_capture.png"), System.Drawing.Imaging.ImageFormat.Png);
                if (cr.Processed != null) cr.Processed.Save(Path.Combine(d, stamp + "_ocr_input.png"), System.Drawing.Imaging.ImageFormat.Png);
                File.WriteAllText(Path.Combine(d, stamp + "_ocr.txt"), cr.Text);
            }
            catch { }
        }

        void SelectRegion()
        {
            if (lastFullCapture == null)
            {
                string err;
                Bitmap b = ScreenCapture.CaptureWindow(settings.ProcessName, settings.CaptureMethod, out err);
                if (b == null || err != null || ScreenCapture.FindGameWindow(settings.ProcessName) == IntPtr.Zero)
                {
                    if (b != null) b.Dispose();
                    MessageBox.Show(this, "Open a contract in Star Citizen's mobiGlas and press the hotkey once, then come back to Select Region.\r\n\r\n" + (err ?? ""), "Select Region");
                    return;
                }
                lastFullCapture = b;
            }
            using (RegionSelectForm f = new RegionSelectForm(lastFullCapture,
                new RectangleF((float)settings.RegionX, (float)settings.RegionY, (float)settings.RegionW, (float)settings.RegionH)))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                RectangleF r = f.Result;
                settings.RegionX = r.X; settings.RegionY = r.Y; settings.RegionW = r.Width; settings.RegionH = r.Height;
                settings.UseRegion = !(r.Width >= 0.999f && r.Height >= 0.999f);
                SaveAll();
                messageStatus.Text = settings.UseRegion ? "Capture region saved." : "Reading the whole window.";
            }
        }

        void ShowLastCapture()
        {
            if (lastText.Length == 0 && lastProcessed == null) { MessageBox.Show(this, "Nothing captured yet this session."); return; }
            using (TextViewForm f = new TextViewForm("Last capture - what the OCR read", lastText, lastProcessed))
            {
                f.ShowDialog(this);
            }
            if (lastText.Length > 0 && MessageBox.Show(this, "Open this text in the contract editor to add it manually?", "Last capture", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                ParseResult p = ContractParser.Parse(lastText);
                Contract c = p.ToContract(); c.RawText = lastText;
                AddWithReview(c, "Add contract from last capture");
            }
        }

        // ================================================================ Outposts
        List<string> OutpostsOf(Contract c)
        {
            List<string> list = new List<string>();
            foreach (CargoLeg l in c.Legs)
                foreach (string loc in new[] { l.Pickup, l.Dropoff })
                {
                    if (string.IsNullOrWhiteSpace(loc)) continue;
                    if (db.Resolve(loc).IsOutpost(settings.DistCentersAsOutposts) && !list.Contains(LocationDb.Clean(loc))) list.Add(LocationDb.Clean(loc));
                }
            return list;
        }

        void ToggleOutpostCheck()
        {
            if (outpostHighlight)
            {
                outpostHighlight = false;
                outpostButton.Text = "⚠ Check Outposts";
                ShowBanner(null, Theme.Muted);
                RefreshContracts();
                return;
            }
            outpostHighlight = true;
            outpostButton.Text = "Clear Highlight";
            List<Contract> hits = data.Contracts.Where(c => !c.IsDone() && OutpostsOf(c).Count > 0).ToList();
            RefreshContracts();
            contractsGrid.ClearSelection();
            foreach (DataGridViewRow r in contractsGrid.Rows) if (hits.Contains((Contract)r.Tag)) r.Selected = true;
            if (hits.Count == 0) ShowBanner("✔  No planetary outposts in your active contracts - stations and cities only.", Theme.Green);
            else
                ShowBanner("⚠  " + hits.Count + (hits.Count == 1 ? " contract uses" : " contracts use") + " planetary outposts: " +
                           string.Join(", ", hits.Select(c => c.Label()).ToArray()) + ".  They are selected - press Remove to drop them, or keep them knowingly.", Theme.Highlight);
        }

        void ShowBanner(string text, Color color)
        {
            if (text == null) { outpostBanner.Height = 0; outpostBanner.Text = ""; return; }
            outpostBanner.Text = text; outpostBanner.ForeColor = color;
            outpostBanner.BackColor = color == Theme.Highlight ? Theme.OutpostRow : Theme.Surface;
            outpostBanner.Height = 34;
        }

        // ================================================================ Contracts
        IEnumerable<string> KnownLocations()
        {
            List<string> l = new List<string>();
            foreach (Contract c in data.Contracts) foreach (CargoLeg g in c.Legs) { l.Add(g.Pickup); l.Add(g.Dropoff); }
            l.AddRange(db.Data.Locations.Select(x => x.Match));
            return l.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x);
        }

        // Start-at list: every known place once, by its proper name ("CRU-L4 Shallow Fields Station", "Rat's Nest"),
        // plus places from your contracts that aren't in the catalog (shortened when that still finds the same place).
        public IEnumerable<string> StartLocations()
        {
            Dictionary<string, string> best = new Dictionary<string, string>();
            foreach (LocationHint h in db.Data.Locations)
                if (!string.IsNullOrWhiteSpace(h.Name)) best[PlaceKey(h.Name)] = h.Name;
            HashSet<string> catalog = new HashSet<string>(best.Keys);
            List<string> fromContracts = new List<string>();
            foreach (Contract ct in data.Contracts) foreach (CargoLeg g in ct.Legs) { fromContracts.Add(g.Pickup); fromContracts.Add(g.Dropoff); }
            foreach (string x in fromContracts.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string k = PlaceKey(x);
                if (catalog.Contains(k)) continue;
                string shortName = HoldPanel.PlaceName(x);
                string c = shortName.Length > 0 && PlaceKey(shortName) == k ? shortName : x;
                string cur;
                if (!best.TryGetValue(k, out cur) || c.Length > cur.Length && !c.Contains(" at the ") || cur.Contains(" at the ") && c.Length < cur.Length) best[k] = c;
            }
            return best.Values.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        }

        string PlaceKey(string x)
        {
            ResolvedLocation r = db.Resolve(x);
            string k = r.Key.StartsWith("@") ? r.Key : LocationDb.KeyOf(HoldPanel.PlaceName(x));
            return k.Replace(" ", "");
        }

        // Adds a contract; if the same contract was captured before, updates its progress instead.
        string AddOrUpdate(Contract c)
        {
            string sig = ContractParser.Signature(c);
            Contract existing = data.Contracts.FirstOrDefault(x => !x.IsDone() && ContractParser.Signature(x) == sig);
            if (existing != null)
            {
                foreach (CargoLeg n in c.Legs)
                {
                    CargoLeg e = existing.Legs.FirstOrDefault(x => LocationDb.KeyOf(x.Material) == LocationDb.KeyOf(n.Material) &&
                                                         x.Scu == n.Scu && LocationDb.KeyOf(x.Dropoff) == LocationDb.KeyOf(n.Dropoff));
                    if (e != null && n.Delivered > e.Delivered) { e.Delivered = n.Delivered; e.PickedUp = Math.Max(e.PickedUp, e.Delivered); }
                }
                SaveAll(); RefreshAll();
                return "Already tracked: " + existing.Label() + " (progress updated)";
            }
            c.Number = data.Contracts.Count + 1;
            data.Contracts.Add(c);
            Renumber();
            route = null;
            SaveAll(); RefreshAll();
            return "Contract " + c.Label() + " added - " + c.TotalScu() + " SCU";
        }

        void AddWithReview(Contract c, string heading)
        {
            using (ContractEditForm f = new ContractEditForm(c, heading, KnownLocations()))
            {
                if (f.ShowDialog(this) == DialogResult.OK) messageStatus.Text = AddOrUpdate(f.Result);
            }
        }

        void AddManual()
        {
            AddWithReview(new Contract(), "Add contract - paste the contract text below and press 'Parse text', or fill in the table");
        }

        List<Contract> SelectedContracts()
        {
            return contractsGrid.SelectedRows.Cast<DataGridViewRow>().Select(r => (Contract)r.Tag).ToList();
        }

        void EditSelected()
        {
            Contract c = SelectedContracts().FirstOrDefault();
            if (c == null) return;
            using (ContractEditForm f = new ContractEditForm(c, "Edit contract " + c.Label(), KnownLocations()))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                int i = data.Contracts.IndexOf(c);
                f.Result.Number = c.Number;
                data.Contracts[i] = f.Result;
                route = null;
                SaveAll(); RefreshAll();
            }
        }

        void RemoveSelected()
        {
            List<Contract> sel = SelectedContracts();
            if (sel.Count == 0) return;
            if (MessageBox.Show(this, "Remove " + sel.Count + " contract(s): " + string.Join(", ", sel.Select(c => c.Label()).ToArray()) + "?\r\n\r\n(Remember to abandon them in mobiGlas too.)",
                                "Remove", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            foreach (Contract c in sel) { data.Contracts.Remove(c); data.Journal.RemoveAll(r => r.ContractNumber == c.Number); }
            Renumber();
            route = null;
            SaveAll(); RefreshAll();
            if (outpostHighlight) { outpostHighlight = false; ToggleOutpostCheck(); }
        }

        void MarkSelectedComplete()
        {
            foreach (Contract c in SelectedContracts())
            {
                c.ManuallyCompleted = true;
                foreach (CargoLeg l in c.Legs) { l.Delivered = l.Scu; l.PickedUp = l.Scu; }
            }
            route = null;
            SaveAll(); RefreshAll();
        }

        void ClearCompleted()
        {
            HashSet<int> gone = new HashSet<int>(data.Contracts.Where(c => c.IsDone()).Select(c => c.Number));
            data.Journal.RemoveAll(r => gone.Contains(r.ContractNumber));
            int n = data.Contracts.RemoveAll(c => c.IsDone());
            Renumber();
            messageStatus.Text = "Removed " + n + " completed contract(s).";
            SaveAll(); RefreshAll();
        }

        // ================================================================ Ship
        const string CustomShip = "(Custom - type the SCU)";

        void SelectShipInBox()
        {
            settingShip = true;
            shipSuggest.Reset();
            ShipInfo sh = ShipDb.Find(settings.ShipName);
            if (sh != null)
            {
                shipBox.SelectedItem = sh.Name;
                capacityBox.Visible = false;
                shipScuLabel.Text = sh.LoadableScu.ToString("N0") + " SCU" + (sh.LoadableScu != sh.Scu ? "  (of " + sh.Scu.ToString("N0") + ")" : "");
            }
            else
            {
                shipBox.SelectedIndex = 0;
                capacityBox.Visible = true;
                capacityBox.Text = settings.ShipCapacity > 0 ? settings.ShipCapacity.ToString() : "";
                shipScuLabel.Text = "SCU";
            }
            settingShip = false;
        }

        void OnShipChosen(string name)
        {
            if (name == null) return;
            ShipInfo sh = name == CustomShip ? null : ShipDb.Find(name);
            string old = settings.ShipName ?? "";
            string now = sh != null ? sh.Name : "";
            if (old == now && !(sh == null && !capacityBox.Visible)) { SelectShipInBox(); return; }
            int aboard = hold.OnShip().Count();
            if (aboard > 0 && old != now)
            {
                if (MessageBox.Show(this, "Switch ship to " + (sh != null ? sh.Name : "a custom SCU") + "?\r\n\r\nThe " + aboard + " container(s) placed on the current ship go back to the manifest, ready to pack into the new one.",
                                    "Change ship", MessageBoxButtons.YesNo) != DialogResult.Yes) { SelectShipInBox(); return; }
                foreach (CargoBox x in hold.OnShip().ToList()) x.State = BoxState.Manifest;
            }
            settings.ShipName = now;
            if (sh != null) settings.ShipCapacity = sh.LoadableScu;
            holdState.Ship = now;
            hold.SetShip(sh);
            SelectShipInBox();
            SaveAll();
            holdPanel.RefreshLists();
            holdPanel.View.ResetView();
            messageStatus.Text = sh != null ? "Ship set to " + sh.Name + " - " + sh.LoadableScu.ToString("N0") + " SCU of cargo grid." : "Type your ship's SCU.";
        }

        // ================================================================ Cargo boxes (3D hold) <-> contracts
        CargoLeg LegOf(CargoBox b)
        {
            Contract c = data.Contracts.FirstOrDefault(x => x.Number == b.ContractNumber);
            return c != null && b.LegIndex >= 0 && b.LegIndex < c.Legs.Count ? c.Legs[b.LegIndex] : null;
        }

        // Keeps the containers in the hold in step with each cargo line's container list. Adds new boxes to the
        // manifest, removes ones that no longer exist (manifest first, then on-ship; delivered boxes are never removed).
        List<CargoBox> SyncAllBoxes()
        {
            List<CargoBox> created = new List<CargoBox>();
            int batch = 0;
            HashSet<int> live = new HashSet<int>(data.Contracts.Select(c => c.Number));
            holdState.Boxes.RemoveAll(b => !live.Contains(b.ContractNumber));
            foreach (Contract c in data.Contracts)
            {
                holdState.Boxes.RemoveAll(b => b.ContractNumber == c.Number && (b.LegIndex < 0 || b.LegIndex >= c.Legs.Count) && b.State != BoxState.Delivered);
                for (int li = 0; li < c.Legs.Count; li++)
                {
                    CargoLeg leg = c.Legs[li];
                    SortedDictionary<int, int> want = Boxes.Parse(leg.Containers);
                    List<CargoBox> have = holdState.Boxes.Where(b => b.ContractNumber == c.Number && b.LegIndex == li).ToList();
                    foreach (CargoBox b in have) { b.Material = leg.Material; b.Pickup = leg.Pickup; b.Dropoff = leg.Dropoff; }
                    HashSet<int> sizes = new HashSet<int>(want.Keys.Concat(have.Select(x => x.Size)));
                    foreach (int size in sizes)
                    {
                        int w; want.TryGetValue(size, out w);
                        List<CargoBox> h = have.Where(x => x.Size == size).ToList();
                        for (int k = h.Count; k < w; k++)
                        {
                            if (batch == 0) batch = holdState.NextBatch++;
                            created.Add(hold.NewBox(size, c, li, batch));
                        }
                        int extra = h.Count - w;
                        foreach (CargoBox x in h.OrderBy(x => x.State == BoxState.Manifest ? 0 : x.State == BoxState.OnShip ? 1 : 9).ThenByDescending(x => x.Id))
                        {
                            if (extra <= 0) break;
                            if (x.State == BoxState.Delivered || x.State == BoxState.Archived) continue;
                            holdState.Boxes.Remove(x); extra--;
                        }
                    }
                }
            }
            return created;
        }

        // The same place can be written differently on different contracts ("Ruin Station above Pyro" vs
        // "... above Pyro VI"). Use one name per place (the longest) so colours, legend and groups match.
        string CanonicalPlace(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw ?? "";
            string key = db.Resolve(raw).Key;
            string best = raw;
            foreach (Contract c in data.Contracts)
                foreach (CargoLeg l in c.Legs)
                    foreach (string p in new[] { l.Dropoff, l.Pickup })
                        if (!string.IsNullOrWhiteSpace(p) && p.Length > best.Length && db.Resolve(p).Key == key) best = p;
            return best;
        }

        // Extra hold space the rest of the route still needs beyond the containers you already have.
        int ScuStillToCome()
        {
            int live = holdState.Boxes.Where(b => b.State == BoxState.OnShip || b.State == BoxState.Manifest).Sum(b => b.Size);
            if (route != null)
            {
                int peak = 0;
                foreach (RouteStop s in route.Stops)
                    if (s.Actions.Any(a => !a.Applied)) peak = Math.Max(peak, s.LoadAfter);
                return Math.Max(0, peak - live);
            }
            int toCollect = 0;
            foreach (Contract c in data.Contracts.Where(x => !x.IsDone()))
                foreach (CargoLeg l in c.Legs) toCollect += Math.Max(0, l.Scu - l.PickedClamped());
            return toCollect;
        }

        // Contracts are always numbered 1..N in list order. Boxes and route history follow the new numbers.
        void Renumber()
        {
            Dictionary<int, int> map = new Dictionary<int, int>();
            for (int i = 0; i < data.Contracts.Count; i++) map[data.Contracts[i].Number] = i + 1;
            if (map.Count != data.Contracts.Count)
            {
                // duplicate numbers from older versions: give every contract a unique temporary number first
                int t = -1;
                foreach (Contract c in data.Contracts) c.Number = t--;
                map.Clear();
                for (int i = 0; i < data.Contracts.Count; i++) map[data.Contracts[i].Number] = i + 1;
            }
            holdState.Boxes.RemoveAll(b => !map.ContainsKey(b.ContractNumber));
            data.Journal.RemoveAll(r => !map.ContainsKey(r.ContractNumber));
            foreach (CargoBox b in holdState.Boxes) b.ContractNumber = map[b.ContractNumber];
            foreach (StepRecord r in data.Journal) r.ContractNumber = map[r.ContractNumber];
            foreach (Contract c in data.Contracts) c.Number = map[c.Number];
            data.NextNumber = data.Contracts.Count + 1;
        }

        // Rank = when the box gets delivered on the planned route (lower = sooner). Drives the packer and colours.
        int RankOfBox(CargoBox b)
        {
            if (route != null)
            {
                int i = 0;
                foreach (RouteStop s in route.Stops)
                {
                    foreach (RouteAction a in s.Actions)
                        if (!a.IsPickup && !a.Applied && a.Ref.Contract.Number == b.ContractNumber && a.Ref.Contract.Legs.IndexOf(a.Ref.Leg) == b.LegIndex) return i;
                    i++;
                }
            }
            return 1000 + b.ContractNumber;
        }

        void SaveHold()
        {
            try { holdState.Ship = settings.ShipName ?? ""; Storage.Save("hold.json", holdState); }
            catch (Exception ex) { messageStatus.Text = "Could not save the hold: " + ex.Message; }
        }

        void RefreshHold()
        {
            // colours go to the destinations of the cargo in the hold, first drop-off first, starting from the top of the list
            List<string> order = new List<string>();
            foreach (CargoBox b in holdState.Boxes.Where(x => x.State == BoxState.OnShip || x.State == BoxState.Manifest).OrderBy(RankOfBox).ThenBy(x => x.Id))
            { CargoLeg l = LegOf(b); order.Add(CanonicalPlace(l != null ? l.Dropoff : b.Dropoff)); }
            holdPanel.AssignColors(order);
            holdPanel.RefreshLists();
        }

        // ================================================================ Route
        void PlanRoute()
        {
            List<LegRef> legs = new List<LegRef>();
            foreach (Contract c in data.Contracts.Where(x => !x.IsDone()))
                foreach (CargoLeg l in c.Legs) legs.Add(new LegRef(c, l));
            if (legs.Count == 0) { MessageBox.Show(this, "No active contracts to plan."); return; }
            Cursor = Cursors.WaitCursor;
            try
            {
                route = RouteOptimizer.Optimize(legs, settings.ShipCapacity, startBox.Text, db, 60);
                SaveAll();
                RefreshRoute();
                RefreshHold();
            }
            finally { Cursor = Cursors.Default; }
        }

        // One visible route row; can stand for several planner actions (e.g. one drop-off of cargo
        // that was collected from several pickup points).
        class RouteRow
        {
            public List<RouteAction> Actions = new List<RouteAction>();
            public bool IsPickup { get { return Actions[0].IsPickup; } }
            public bool Applied { get { return Actions.All(a => a.Applied); } }
        }

        class StopHeader
        {
            public string Title = "";
            public ResolvedLocation Loc;
            public bool Done;
            public string Right = "";
        }

        void OnRouteCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (populatingRoute || e.RowIndex < 0 || e.ColumnIndex != 0) return;
            DataGridViewRow row = routeGrid.Rows[e.RowIndex];
            bool on = Convert.ToBoolean(row.Cells[0].Value);
            StepRecord rec = row.Tag as StepRecord;
            if (rec != null)
            {
                if (!on)
                {
                    populatingRoute = true; row.Cells[0].Value = true; populatingRoute = false;   // UndoRecord redraws the list
                    BeginInvoke(new Action(() => UndoRecord(rec)));
                }
                return;
            }
            RouteRow rr = row.Tag as RouteRow;
            if (rr == null || !on) return;
            populatingRoute = true; row.Cells[0].Value = false; populatingRoute = false;
            if (rr.IsPickup) BeginInvoke(new Action(() => DoPickup(rr)));
            else BeginInvoke(new Action(() => DoDrop(rr)));
        }

        // Ticking a PICK UP: ask what came up the elevator, record it, and put the containers in the hold manifest.
        void DoPickup(RouteRow rr, bool quiet = false)
        {
            RouteAction first = rr.Actions[0];
            Contract c = first.Ref.Contract; CargoLeg leg = first.Ref.Leg;
            int li = c.Legs.IndexOf(leg);
            if (li < 0) return;
            string where = string.IsNullOrEmpty(leg.Pickup) ? "this pickup" : leg.Pickup;
            int planned = rr.Actions.Sum(x => x.Scu);
            string claimed = Boxes.Merge(data.Journal.Where(r => r.IsPickup && r.ContractNumber == c.Number && r.Parts.Any(p => p.LegIndex == li)).Select(r => r.Containers).ToArray());
            string unclaimed = Boxes.Subtract(leg.Containers, claimed);
            // not asking: whatever containers were already entered for this cargo count as this pickup
            string entered = unclaimed; int got = unclaimed.Length > 0 ? Boxes.Total(unclaimed) : planned;
            if ((settings.AskContainersOnPickup || leg.Estimated) && !quiet)
            {
                using (ContainerForm f = new ContainerForm(leg.Material + "  [" + c.Label() + "]  at\r\n" + where, unclaimed, unclaimed.Length > 0 ? Boxes.Total(unclaimed) : planned))
                {
                    if (f.ShowDialog(this) != DialogResult.OK) return;
                    entered = f.ResultContainers; got = f.ResultTotal;
                }
            }
            leg.Containers = Boxes.Merge(claimed, entered);

            StepRecord rec = new StepRecord { Id = data.NextRecord++, IsPickup = true, ContractNumber = c.Number, Location = leg.Pickup, Material = leg.Material, Containers = entered };
            rec.Parts.Add(new StepPart { LegIndex = li, Scu = got });
            data.Journal.Add(rec);
            int claimedScu = data.Journal.Where(r => r.IsPickup && r.ContractNumber == c.Number).SelectMany(r => r.Parts).Where(p => p.LegIndex == li).Sum(p => p.Scu);

            SyncAllBoxes();
            // the record owns the boxes it brought aboard
            HashSet<int> owned = new HashSet<int>(data.Journal.Where(r => r != rec).SelectMany(r => r.BoxIds));
            foreach (KeyValuePair<int, int> kv in Boxes.Parse(entered))
                rec.BoxIds.AddRange(holdState.Boxes.Where(b => b.ContractNumber == c.Number && b.LegIndex == li && b.Size == kv.Key && !owned.Contains(b.Id) &&
                                                               (b.State == BoxState.Manifest || b.State == BoxState.OnShip))
                                                   .OrderByDescending(b => b.Id).Take(kv.Value).Select(b => b.Id));
            int newest = rec.BoxIds.Count > 0 ? holdState.NextBatch++ : 0;
            foreach (CargoBox b in holdState.Boxes.Where(b => rec.BoxIds.Contains(b.Id))) b.Batch = newest;   // "last load" for Repack

            foreach (RouteAction a in rr.Actions) a.Applied = true;
            string msg = "Picked up " + got + " SCU " + leg.Material + (entered.Length > 0 ? " (" + Boxes.Pretty(entered) + ")" : "") + " at " + where + ".";
            if (rec.BoxIds.Count > 0) msg += "  " + rec.BoxIds.Count + " container(s) added to the hold manifest - press PACK CARGO to place them.";
            if (leg.Estimated || (!string.IsNullOrEmpty(leg.Group) && got != planned))
            {
                ApplyLoaded(c, leg, Math.Max(leg.PickedClamped(), claimedScu));
                messageStatus.Text = msg + "  Route re-planned.";
            }
            else
            {
                leg.PickedUp = Math.Min(leg.Scu, Math.Max(leg.PickedClamped(), claimedScu));
                if (got != planned) msg += "  That's different from the plan (" + planned + " SCU) - press PLAN ROUTE to re-plan.";
                SaveAll();
                RefreshContracts(); RefreshLegs(); RefreshRoute(); RefreshHold();
                messageStatus.Text = msg;
            }
            SaveHold();
        }

        // Ticking a DROP OFF: the containers for that cargo leave the ship and move to DELIVERED.
        void DoDrop(RouteRow rr)
        {
            RouteAction first = rr.Actions[0];
            Contract c = first.Ref.Contract;
            string where = first.Ref.Leg.Dropoff;
            StepRecord rec = new StepRecord { Id = data.NextRecord++, IsPickup = false, ContractNumber = c.Number, Location = where, Material = first.Ref.Leg.Material };
            foreach (var g in rr.Actions.GroupBy(a => c.Legs.IndexOf(a.Ref.Leg)))
            {
                int li = g.Key; if (li < 0) continue;
                CargoLeg leg = c.Legs[li];
                int scu = g.Sum(a => a.Scu);
                rec.Parts.Add(new StepPart { LegIndex = li, Scu = scu });
                // pick the containers that make up this delivery
                List<CargoBox> pool = holdState.Boxes.Where(b => b.ContractNumber == c.Number && b.LegIndex == li && (b.State == BoxState.OnShip || b.State == BoxState.Manifest))
                                                     .OrderByDescending(b => b.Size).ToList();
                int remaining = scu;
                while (remaining > 0 && pool.Count > 0)
                {
                    CargoBox pick = pool.FirstOrDefault(b => b.Size <= remaining) ?? pool.Last();
                    pool.Remove(pick);
                    pick.State = BoxState.Delivered; pick.DeliveredAt = where; pick.DeliveredRecord = rec.Id;
                    rec.BoxIds.Add(pick.Id);
                    remaining -= pick.Size;
                }
                leg.Delivered = Math.Min(leg.Scu, leg.Delivered + scu);
                leg.PickedUp = Math.Max(leg.PickedUp, leg.Delivered);
            }
            rec.Containers = Boxes.Format(CountSizes(holdState.Boxes.Where(b => rec.BoxIds.Contains(b.Id))));
            data.Journal.Add(rec);
            foreach (RouteAction a in rr.Actions) a.Applied = true;
            SaveAll(); SaveHold();
            RefreshContracts(); RefreshLegs(); RefreshRoute(); RefreshHold();
            messageStatus.Text = "Delivered " + rec.Scu() + " SCU " + rec.Material + " at " + where + "." +
                                 (rec.BoxIds.Count > 0 ? "  " + rec.BoxIds.Count + " container(s) moved to DELIVERED." : "");
        }

        static SortedDictionary<int, int> CountSizes(IEnumerable<CargoBox> boxes)
        {
            SortedDictionary<int, int> d = new SortedDictionary<int, int>();
            foreach (CargoBox b in boxes) { int n; d.TryGetValue(b.Size, out n); d[b.Size] = n + 1; }
            return d;
        }

        // Un-ticking a completed step (or "Undeliver" in the hold) puts everything back the way it was.
        bool UndoRecord(StepRecord rec, bool quiet = false)
        {
            Contract c = data.Contracts.FirstOrDefault(x => x.Number == rec.ContractNumber);
            List<CargoBox> boxes = holdState.Boxes.Where(b => rec.BoxIds.Contains(b.Id)).ToList();
            if (rec.IsPickup)
            {
                if (boxes.Any(b => b.State == BoxState.Delivered || b.State == BoxState.Archived) ||
                    (c != null && rec.Parts.Any(p => p.LegIndex < c.Legs.Count && c.Legs[p.LegIndex].PickedClamped() - p.Scu < c.Legs[p.LegIndex].Delivered)))
                {
                    if (quiet) Notify("Can't undo that pickup - some of it was already delivered. Undo the drop-off first.", true);
                    else MessageBox.Show(this, "Some of this cargo has already been delivered.\r\nUn-tick that DROP OFF first, then undo the pickup.", "Undo pickup");
                    RefreshRoute();
                    return false;
                }
                if (c != null)
                    foreach (StepPart p in rec.Parts)
                    {
                        if (p.LegIndex >= c.Legs.Count) continue;
                        CargoLeg leg = c.Legs[p.LegIndex];
                        leg.PickedUp = Math.Max(leg.Delivered, leg.PickedClamped() - p.Scu);
                        leg.Containers = Boxes.Subtract(leg.Containers, rec.Containers);
                    }
                foreach (CargoBox b in boxes) holdState.Boxes.Remove(b);
                messageStatus.Text = "Undid the pickup of " + rec.Scu() + " SCU " + rec.Material + " at " + rec.Location + ".";
            }
            else
            {
                if (c != null)
                    foreach (StepPart p in rec.Parts)
                    {
                        if (p.LegIndex >= c.Legs.Count) continue;
                        CargoLeg leg = c.Legs[p.LegIndex];
                        leg.Delivered = Math.Max(0, leg.Delivered - p.Scu);
                        c.ManuallyCompleted = false;
                    }
                int back = 0;
                foreach (CargoBox b in boxes)
                {
                    b.DeliveredAt = ""; b.DeliveredRecord = 0;
                    b.State = BoxState.OnShip;
                    if (!hold.CanStay(b)) b.State = BoxState.Manifest; else back++;
                }
                messageStatus.Text = "Undelivered " + rec.Scu() + " SCU " + rec.Material + " - " +
                                     (back > 0 ? back + " container(s) back in their spot on the ship" : "") +
                                     (boxes.Count > back ? (back > 0 ? ", " : "") + (boxes.Count - back) + " back in the manifest" : "") + ".";
            }
            data.Journal.Remove(rec);
            // let the matching planned steps show up again
            if (route != null && c != null)
                foreach (RouteStop s in route.Stops)
                    foreach (RouteAction a in s.Actions)
                        if (a.Applied && a.IsPickup == rec.IsPickup && a.Ref.Contract == c && rec.Parts.Any(p => p.LegIndex == c.Legs.IndexOf(a.Ref.Leg))) a.Applied = false;
            SaveAll(); SaveHold();
            RefreshContracts(); RefreshLegs(); RefreshRoute(); RefreshHold();
            return true;
        }

        void StyleActionRow(DataGridViewRow row, RouteRow rr)
        {
            Color c = rr.IsPickup ? Theme.Accent : Theme.Highlight;
            row.DefaultCellStyle.ForeColor = Theme.Text;
            row.Cells["Action"].Style.ForeColor = c;
            row.Cells["Action"].Style.Font = Theme.Semibold;
            if (rr.Actions.Any(a => a.Ref.Leg.Estimated && a.IsPickup)) row.Cells["Scu"].Style.ForeColor = Theme.Highlight;
        }

        // Stop header rows are drawn as one wide banner: STOP n / DONE, location, type pill, body and hold.
        void OnRouteRowPrePaint(object sender, DataGridViewRowPrePaintEventArgs e)
        {
            StopHeader s = routeGrid.Rows[e.RowIndex].Tag as StopHeader;
            if (s == null) return;
            e.Handled = true;
            Graphics g = e.Graphics;
            int visibleWidth = routeGrid.ClientSize.Width;
            foreach (Control c in routeGrid.Controls) if (c is VScrollBar && c.Visible) visibleWidth -= c.Width;
            Rectangle r = new Rectangle(e.RowBounds.Left, e.RowBounds.Top, visibleWidth, e.RowBounds.Height);
            using (SolidBrush bg = new SolidBrush(s.Done ? Theme.Panel : Theme.Surface)) g.FillRectangle(bg, r);
            using (SolidBrush bar = new SolidBrush(s.Done ? Theme.Border : Theme.Highlight)) g.FillRectangle(bar, r.Left, r.Top + 4, 3, r.Height - 8);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            float mid = r.Top + r.Height / 2f;
            float x = r.Left + 12;
            using (SolidBrush b = new SolidBrush(s.Done ? Theme.Muted : Theme.Highlight))
            {
                SizeF sz = g.MeasureString(s.Title, Theme.Semibold);
                g.DrawString(s.Title, Theme.Semibold, b, x, mid - sz.Height / 2);
                x += sz.Width + 8;
            }
            using (SolidBrush b = new SolidBrush(s.Done ? Theme.Muted : Theme.Text))
            {
                SizeF sz = g.MeasureString(s.Loc.Display, Theme.Semibold);
                g.DrawString(s.Loc.Display, Theme.Semibold, b, x, mid - sz.Height / 2);
                x += sz.Width + 8;
            }
            Theme.DrawPill(g, ResolvedLocation.KindText(s.Loc.Kind).ToUpperInvariant(), Theme.Small, s.Done ? Theme.Muted : Theme.KindColor(s.Loc.Kind, settings.DistCentersAsOutposts), ref x, mid);
            using (SolidBrush b = new SolidBrush(Theme.Muted))
            {
                SizeF sz = g.MeasureString(s.Right, Theme.Small);
                float rx = Math.Max(x + 10, r.Right - sz.Width - 12);
                g.DrawString(s.Right, Theme.Small, b, rx, mid - sz.Height / 2);
            }
        }

        void AddHeader(StopHeader h)
        {
            int hi = routeGrid.Rows.Add(false);
            DataGridViewRow row = routeGrid.Rows[hi];
            row.Tag = h; row.Height = 34; row.ReadOnly = true;
        }

        void RefreshRoute()
        {
            populatingRoute = true;
            routeGrid.Rows.Clear();
            bool anyEstimate = false;

            // ---- what you've already done (greyed out, un-tick to undo)
            HashSet<int> live = new HashSet<int>(data.Contracts.Select(c => c.Number));
            string lastKey = null;
            List<OverlayLine> ov = new List<OverlayLine>();
            plannedRows = new List<RouteRow>();
            foreach (StepRecord rec in data.Journal.Where(r => live.Contains(r.ContractNumber)))
            {
                ResolvedLocation loc = db.Resolve(rec.Location);
                if (loc.Key != lastKey) ov.Add(new OverlayLine { Header = true, Done = true, Text = "DONE · " + HoldPanel.PlaceName(rec.Location) });
                {
                    Contract oc = data.Contracts.First(x => x.Number == rec.ContractNumber);
                    ov.Add(new OverlayLine
                    {
                        Done = true, IsPickup = rec.IsPickup,
                        Text = (rec.IsPickup ? "PICKED UP  " : "DELIVERED  ") + rec.Scu() + " SCU  " + rec.Material,
                        Sub = rec.IsPickup && rec.Parts.Count > 0 && rec.Parts[0].LegIndex < oc.Legs.Count ? "→ " + HoldPanel.PlaceName(oc.Legs[rec.Parts[0].LegIndex].Dropoff) : ""
                    });
                }
                if (loc.Key != lastKey)
                {
                    AddHeader(new StopHeader { Title = "DONE", Loc = loc, Done = true, Right = loc.BodyText() + "   ·   " + rec.Time });
                    lastKey = loc.Key;
                }
                Contract c = data.Contracts.First(x => x.Number == rec.ContractNumber);
                string detail = rec.IsPickup
                    ? "→ " + (rec.Parts.Count > 0 && rec.Parts[0].LegIndex < c.Legs.Count ? c.Legs[rec.Parts[0].LegIndex].Dropoff : "")
                    : "delivered";
                if (!string.IsNullOrEmpty(rec.Containers)) detail += "   ▣ " + Boxes.Pretty(rec.Containers);
                int ri = routeGrid.Rows.Add(true, rec.IsPickup ? "PICKED UP" : "DELIVERED", rec.Scu(), rec.Material, c.Label(), detail);
                DataGridViewRow row = routeGrid.Rows[ri];
                row.Tag = rec;
                row.DefaultCellStyle.ForeColor = Theme.Muted;
                row.DefaultCellStyle.SelectionForeColor = Theme.Muted;
                row.DefaultCellStyle.BackColor = Theme.Panel;
                row.Cells["Action"].Style.Font = Theme.Semibold;
            }
            int firstPlanned = -1;

            // ---- what's left to do
            if (route != null)
            {
                int n = 1;
                foreach (RouteStop s in route.Stops)
                {
                    // merge drop-offs of the same multi-pickup delivery into one row
                    List<RouteRow> rows = new List<RouteRow>();
                    foreach (RouteAction a in s.Actions.Where(x => !x.Applied))
                    {
                        RouteRow existing = null;
                        if (!a.IsPickup && !string.IsNullOrEmpty(a.Ref.Leg.Group))
                            existing = rows.FirstOrDefault(x => !x.IsPickup && x.Actions[0].Ref.Contract == a.Ref.Contract && x.Actions[0].Ref.Leg.Group == a.Ref.Leg.Group);
                        if (existing == null) { existing = new RouteRow(); rows.Add(existing); }
                        existing.Actions.Add(a);
                    }
                    if (rows.Count == 0) continue;
                    if (firstPlanned < 0) firstPlanned = routeGrid.Rows.Count;
                    ov.Add(new OverlayLine { Header = true, Text = "STOP " + n + " · " + HoldPanel.PlaceName(s.Location.Display),
                                             Tag = s.Location.IsOutpost(settings.DistCentersAsOutposts) ? "OUTPOST" : "" });
                    AddHeader(new StopHeader
                    {
                        Title = "STOP " + n, Loc = s.Location, Done = false,
                        Right = s.Location.BodyText() + "   ·   hold after: " + s.LoadAfter + (route.Capacity > 0 ? " / " + route.Capacity : "") + " SCU"
                    });
                    foreach (RouteRow rr in rows)
                    {
                        RouteAction a = rr.Actions[0];
                        CargoLeg leg = a.Ref.Leg;
                        int scu = rr.Actions.Sum(x => x.Scu);
                        string scuText = scu.ToString();
                        string dest;
                        if (rr.IsPickup)
                        {
                            dest = leg.Dropoff;
                            if (leg.Estimated) { scuText = "~" + scu; dest = "load ALL " + leg.Material + " here → " + leg.Dropoff; anyEstimate = true; }
                        }
                        else
                        {
                            dest = "deliver here";
                            if (!string.IsNullOrEmpty(leg.Group))
                            {
                                List<CargoLeg> grp = a.Ref.Contract.Legs.Where(x => x.Group == leg.Group && x.Scu > 0).ToList();
                                if (grp.Count > 1)
                                {
                                    if (rr.Actions.Count >= grp.Count) dest = "deliver all " + grp.Sum(x => x.Scu) + " SCU  (collected from " + grp.Count + " pickups)";
                                    else
                                    {
                                        dest = "deliver what you carry  (" + rr.Actions.Count + " of " + grp.Count + " pickups, " + grp.Sum(x => x.Scu) + " SCU total)";
                                        if (rr.Actions.Any(x => x.Ref.Leg.Estimated)) scuText = "~" + scu;
                                    }
                                }
                            }
                            // containers of this cargo that are aboard
                            List<CargoBox> aboard = holdState.Boxes.Where(b => b.ContractNumber == a.Ref.Contract.Number && (b.State == BoxState.OnShip || b.State == BoxState.Manifest) &&
                                                                            rr.Actions.Any(x => a.Ref.Contract.Legs.IndexOf(x.Ref.Leg) == b.LegIndex)).ToList();
                            if (aboard.Count > 0) dest += "   ▣ " + Boxes.Pretty(Boxes.Format(CountSizes(aboard)));
                        }
                        int ri = routeGrid.Rows.Add(false, rr.IsPickup ? "PICK UP" : "DROP OFF", scuText, leg.Material, a.Ref.Contract.Label(), dest);
                        {
                            string sub;
                            if (rr.IsPickup) sub = (leg.Estimated ? "load ALL here  " : "") + "→ " + HoldPanel.PlaceName(leg.Dropoff);
                            else
                            {
                                int bx = dest.IndexOf("▣", StringComparison.Ordinal);
                                sub = bx >= 0 ? dest.Substring(bx) : "";
                            }
                            ov.Add(new OverlayLine { IsPickup = rr.IsPickup, Current = plannedRows.Count == 0,
                                                     Text = (rr.IsPickup ? "PICK UP  " : "DROP OFF  ") + scuText + " SCU  " + leg.Material, Sub = sub });
                            plannedRows.Add(rr);
                        }
                        DataGridViewRow row = routeGrid.Rows[ri];
                        row.Tag = rr;
                        StyleActionRow(row, rr);
                    }
                    n++;
                }
                List<string> lines = new List<string>();
                if (route.Stops.Count > 0)
                {
                    int outpostStops = route.Stops.Count(s => s.Location.IsOutpost(settings.DistCentersAsOutposts));
                    lines.Add(route.Stops.Count + " stops   ·   travel score " + route.TotalCost.ToString("0.#") + "   ·   peak hold " + route.PeakLoad + " SCU" +
                              (outpostStops > 0 ? "   ·   ⚠ " + outpostStops + " outpost stop" + (outpostStops == 1 ? "" : "s") : "") +
                              (route.NaiveStops > 0 ? "      (one contract at a time: " + route.NaiveStops + " stops, score " + route.NaiveCost.ToString("0.#") + ")" : ""));
                    lines.Add(anyEstimate
                        ? "~N = the contract doesn't say how much waits at that pickup - load all of it there."
                        : "Tick each step as you do it - done steps stay greyed out; un-tick one to undo it.");
                }
                lines.AddRange(route.Messages);
                routeSummary.Text = string.Join("\r\n", lines.ToArray());
                if (plannedRows.Count == 0 && ov.Count > 0) ov.Add(new OverlayLine { Header = true, Text = "Route complete ✓" });
            }
            else routeSummary.Text = data.Journal.Count > 0
                ? "Press PLAN ROUTE to plan the remaining steps."
                : "Press PLAN ROUTE to build the best pickup / drop-off order for all active contracts.";
            routeGrid.ClearSelection();
            if (firstPlanned > 0 && firstPlanned < routeGrid.Rows.Count)
            {
                try { routeGrid.FirstDisplayedScrollingRowIndex = Math.Max(0, firstPlanned - 3); } catch { }
            }
            overlayLines = ov;
            UpdateOverlay();
            populatingRoute = false;
        }

        // Contracts captured by older versions: tidy OCR noise in place names, and re-read contracts whose
        // cargo lines were clearly misread (0 SCU / missing places / junk title) - only if nothing was done on them yet.
        void RepairContracts()
        {
            bool changed = false;
            foreach (Contract c in data.Contracts)
            {
                foreach (CargoLeg l in c.Legs)
                {
                    string p = ContractParser.NormalizeLocation(l.Pickup), d = ContractParser.NormalizeLocation(l.Dropoff);
                    if (p != l.Pickup || d != l.Dropoff) { l.Pickup = p; l.Dropoff = d; changed = true; }
                    if (l.Group == null) l.Group = "";
                }
                if (CollapseSamePlacePickups(c)) changed = true;
                if (string.IsNullOrEmpty(c.RawText) || c.IsDone()) continue;
                bool untouched = c.Legs.All(l => l.PickedUp == 0 && l.Delivered == 0);
                bool badLegs = c.Legs.Any(l => l.Scu <= 0 || l.Pickup.Length == 0 || l.Dropoff.Length == 0);
                bool badTitle = c.Title.Contains("No delivery found") || c.Title.Contains("->") || c.Title.Contains("→") || c.Title == c.Title.ToUpperInvariant();
                if (!untouched || !(badLegs || badTitle || c.Reward == 0)) continue;
                ParseResult r = ContractParser.Parse(c.RawText);
                if (r.Legs.Count == 0 || r.Legs.Any(l => l.Scu <= 0)) continue;
                if (badLegs) { c.Legs = r.Legs.Select(x => x.Clone()).ToList(); c.Warnings = string.Join(" | ", r.Warnings.ToArray()); }
                if (r.Title.Length > 0 && badTitle) c.Title = r.Title;
                if (c.Reward == 0 && r.Reward > 0) c.Reward = r.Reward;
                changed = true;
            }
            if (changed) { try { Storage.Save("contracts.json", data); Storage.Save("hold.json", holdState); } catch { } }
        }

        // Older reads could split one pickup into two "pickup points" when OCR spelled it two ways
        // ("Patch City at the L3 ..." and "Petch City"). Joins those back into one line per drop-off,
        // keeping what you loaded, the containers in the hold and the route history.
        bool CollapseSamePlacePickups(Contract c)
        {
            bool changed = false;
            // one spelling per place within the contract
            foreach (var g in c.Legs.SelectMany(l => new[] { l.Pickup, l.Dropoff }).Where(x => !string.IsNullOrWhiteSpace(x))
                                     .Distinct().GroupBy(x => ContractParser.PlaceKey(x)))
            {
                string best = g.OrderByDescending(x => x.Length).First();
                foreach (CargoLeg l in c.Legs)
                {
                    if (g.Contains(l.Pickup) && l.Pickup != best) { l.Pickup = best; changed = true; }
                    if (g.Contains(l.Dropoff) && l.Dropoff != best) { l.Dropoff = best; changed = true; }
                }
            }
            foreach (StepRecord r in data.Journal.Where(r => r.ContractNumber == c.Number && !string.IsNullOrEmpty(r.Location)))
            {
                string k = ContractParser.PlaceKey(r.Location);
                string best = c.Legs.SelectMany(l => new[] { l.Pickup, l.Dropoff }).FirstOrDefault(x => !string.IsNullOrEmpty(x) && ContractParser.PlaceKey(x) == k);
                if (best != null && best != r.Location) { r.Location = best; changed = true; }
            }
            List<CargoLeg> legs = new List<CargoLeg>();
            int[] map = new int[c.Legs.Count];
            Dictionary<string, int> mergedAt = new Dictionary<string, int>();
            bool merged = false;
            for (int i = 0; i < c.Legs.Count; i++)
            {
                CargoLeg l = c.Legs[i];
                List<CargoLeg> grp = string.IsNullOrEmpty(l.Group) ? null : c.Legs.Where(x => x.Group == l.Group).ToList();
                bool samePlace = grp != null && grp.Count > 1 && grp.Select(x => ContractParser.PlaceKey(x.Pickup)).Distinct().Count() == 1;
                if (!samePlace) { map[i] = legs.Count; legs.Add(l); continue; }
                int at;
                if (mergedAt.TryGetValue(l.Group, out at)) { map[i] = at; continue; }
                CargoLeg m = l.Clone();
                m.Group = ""; m.Estimated = false;
                m.Scu = grp.Sum(x => x.Scu); m.PickedUp = grp.Sum(x => x.PickedClamped()); m.Delivered = grp.Sum(x => x.Delivered);
                m.Containers = Boxes.Merge(grp.Select(x => x.Containers ?? "").ToArray());
                mergedAt[l.Group] = legs.Count; map[i] = legs.Count; legs.Add(m);
                merged = true;
            }
            if (!merged) return changed;
            c.Legs = legs;
            foreach (CargoBox b in holdState.Boxes.Where(b => b.ContractNumber == c.Number && b.LegIndex >= 0 && b.LegIndex < map.Length)) b.LegIndex = map[b.LegIndex];
            foreach (StepRecord r in data.Journal.Where(r => r.ContractNumber == c.Number))
                foreach (StepPart p in r.Parts) if (p.LegIndex >= 0 && p.LegIndex < map.Length) p.LegIndex = map[p.LegIndex];
            return true;
        }

        // ================================================================ Refresh / save
        void RefreshAll()
        {
            SyncAllBoxes();
            RefreshContracts(); RefreshLegs(); RefreshRoute(); RefreshStartLocations();
            if (holdPanel != null) RefreshHold();
        }

        void RefreshStartLocations()
        {
            startSuggest.SetItems(StartLocations());
        }

        void RefreshContracts()
        {
            List<Contract> selected = SelectedContracts();
            contractsGrid.Rows.Clear();
            long reward = 0; int scu = 0, active = 0;
            foreach (Contract c in data.Contracts)
            {
                bool done = c.IsDone();
                int outposts = OutpostsOf(c).Count;
                // the "N pickup points" note is information, not a problem
                bool needsCheck = (c.Warnings ?? "").Split(new[] { " | " }, StringSplitOptions.RemoveEmptyEntries).Any(w => !w.Contains("pickup points"));
                string status = done ? "Done" : (needsCheck ? "Check details" : "Active");
                int i = contractsGrid.Rows.Add(c.Label(), c.Title, c.Reward > 0 ? c.Reward.ToString("N0") : "", c.RemainingScu().ToString("N0"),
                                               c.Legs.Count, outposts > 0 ? "⚠ " + outposts : "—", status);
                DataGridViewRow r = contractsGrid.Rows[i];
                r.Tag = c;
                r.Cells["Outposts"].Style.ForeColor = outposts > 0 ? Theme.Highlight : Theme.Muted;
                r.Cells["Status"].Style.ForeColor = done ? Theme.Muted : (needsCheck ? Theme.Highlight : Theme.Green);
                r.Cells["Reward"].Style.ForeColor = Theme.Highlight;
                if (done) r.DefaultCellStyle.ForeColor = Theme.Muted;
                if (!string.IsNullOrEmpty(c.Warnings)) foreach (DataGridViewCell cell in r.Cells) cell.ToolTipText = c.Warnings;
                if (outpostHighlight && outposts > 0 && !done)
                {
                    r.DefaultCellStyle.BackColor = Theme.OutpostRow;
                    r.DefaultCellStyle.SelectionBackColor = Theme.Mix(Theme.OutpostRow, Theme.Highlight, 0.3f);
                    r.DefaultCellStyle.ForeColor = Theme.Highlight;
                }
                r.Selected = selected.Contains(c);
                if (!done) { active++; reward += c.Reward; scu += c.RemainingScu(); }
            }
            if (selected.Count == 0) contractsGrid.ClearSelection();
            totalsLabel.Text = active + " active   ·   " + scu.ToString("N0") + " SCU left   ·   " + reward.ToString("N0") + " aUEC";
        }

        void RefreshLegs()
        {
            legsGrid.Rows.Clear();
            foreach (Contract c in data.Contracts.Where(x => !x.IsDone()))
            {
                HashSet<string> shownGroups = new HashSet<string>();
                foreach (CargoLeg l in c.Legs)
                {
                    List<CargoLeg> group = string.IsNullOrEmpty(l.Group) ? null : c.Legs.Where(x => x.Group == l.Group).ToList();
                    if (group != null && group.Count > 1)
                    {
                        if (!shownGroups.Add(l.Group)) continue;
                        AddGroupRows(c, group);
                        continue;
                    }
                    ResolvedLocation p = db.Resolve(l.Pickup), d = db.Resolve(l.Dropoff);
                    int i = legsGrid.Rows.Add(c.Label(), l.Material, l.Scu, l.PickedClamped(), BoxesText(l), l.Delivered,
                                              string.IsNullOrEmpty(l.Pickup) ? "?" : l.Pickup, KindLabel(l.Pickup, p),
                                              string.IsNullOrEmpty(l.Dropoff) ? "?" : l.Dropoff, KindLabel(l.Dropoff, d));
                    DataGridViewRow r = legsGrid.Rows[i];
                    r.Tag = c;
                    r.Cells["PickupType"].Tag = p.Kind; r.Cells["DropoffType"].Tag = d.Kind;
                    MarkLoadedCell(r.Cells["Picked"], l);
                    MarkBoxesCell(r.Cells["Boxes"], l);
                    if (l.IsDone()) r.DefaultCellStyle.ForeColor = Theme.Muted;
                }
            }
            legsGrid.ClearSelection();
            HighlightLegs();
        }

        // One delivery collected from several pickup points: a summary row with the real total,
        // then one indented row per pickup point.
        void AddGroupRows(Contract c, List<CargoLeg> group)
        {
            CargoLeg first = group[0];
            ResolvedLocation d = db.Resolve(first.Dropoff);
            int total = group.Sum(x => x.Scu), loaded = group.Sum(x => x.PickedClamped()), delivered = group.Sum(x => x.Delivered);
            List<ResolvedLocation> pts = group.Select(x => db.Resolve(x.Pickup)).ToList();
            bool anyOutpost = pts.Any(x => x.IsOutpost(settings.DistCentersAsOutposts));
            string allBoxes = Boxes.Merge(group.Select(x => x.Containers).ToArray());
            int i = legsGrid.Rows.Add(c.Label(), first.Material, total, loaded, Boxes.Pretty(allBoxes), delivered,
                                      "⇉ " + group.Count + " pickup points (any order)",
                                      anyOutpost ? "⚠ incl. outpost" : "mixed",
                                      string.IsNullOrEmpty(first.Dropoff) ? "?" : first.Dropoff, KindLabel(first.Dropoff, d));
            DataGridViewRow r = legsGrid.Rows[i];
            r.Tag = c;
            r.DefaultCellStyle.Font = Theme.Semibold;
            r.Cells["Pickup"].Style.ForeColor = Theme.Highlight;
            LocKind common = pts.Select(x => x.Kind).Distinct().Count() == 1 ? pts[0].Kind : LocKind.Unknown;
            if (!anyOutpost && common != LocKind.Unknown) r.Cells["PickupType"].Value = "all " + ResolvedLocation.KindText(common);
            r.Cells["PickupType"].Tag = anyOutpost ? LocKind.Outpost : common;
            r.Cells["DropoffType"].Tag = d.Kind;
            r.Cells["Picked"].ReadOnly = true;
            r.Cells["Boxes"].Style.ForeColor = Theme.Accent;
            if (group.All(x => x.IsDone())) r.DefaultCellStyle.ForeColor = Theme.Muted;

            foreach (CargoLeg l in group)
            {
                ResolvedLocation p = db.Resolve(l.Pickup);
                int ci = legsGrid.Rows.Add("", "", l.Estimated ? (l.Scu == 0 ? "skip" : "~" + l.Scu) : l.Scu.ToString(), l.PickedClamped(), BoxesText(l), "",
                                           "   ↳ " + (string.IsNullOrEmpty(l.Pickup) ? "? (unnamed in game - edit)" : l.Pickup), KindLabel(l.Pickup, p),
                                           "", "");
                DataGridViewRow cr = legsGrid.Rows[ci];
                cr.Tag = c;
                cr.Cells["PickupType"].Tag = p.Kind;
                cr.DefaultCellStyle.ForeColor = l.IsDone() ? Theme.Muted : Theme.Text;
                MarkLoadedCell(cr.Cells["Picked"], l);
                MarkBoxesCell(cr.Cells["Boxes"], l);
                if (l.Estimated)
                {
                    cr.Cells["Scu"].Style.ForeColor = Theme.Highlight;
                    cr.Cells["Scu"].ToolTipText = "Estimated: the contract doesn't say how much waits here. Load everything, then type the real amount in LOADED.";
                }
            }
        }

        string BoxesText(CargoLeg l)
        {
            if (!string.IsNullOrEmpty(l.Containers)) return Boxes.Pretty(l.Containers);
            return l.IsDone() ? "" : "+ add";
        }

        void MarkBoxesCell(DataGridViewCell cell, CargoLeg l)
        {
            cell.Tag = l;
            cell.Style.BackColor = Theme.Surface;
            cell.Style.ForeColor = string.IsNullOrEmpty(l.Containers) ? Theme.Muted : Theme.Accent;
            cell.ToolTipText = "Click to enter the containers that came up the freight elevator (e.g. 3 x 8 SCU, 1 x 4 SCU).";
        }

        void OpenContainers(Contract c, CargoLeg leg)
        {
            string where = string.IsNullOrEmpty(leg.Pickup) ? "this pickup" : leg.Pickup;
            int initial = leg.PickedClamped() > 0 ? leg.PickedClamped() : (leg.Scu > 0 ? leg.Scu : 0);
            using (ContainerForm f = new ContainerForm(leg.Material + "  [" + c.Label() + "]  at\r\n" + where, leg.Containers, initial))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                leg.Containers = f.ResultContainers;
                if (f.ResultTotal != leg.PickedClamped()) ApplyLoaded(c, leg, f.ResultTotal, true);
                else
                {
                    List<CargoBox> added = SyncAllBoxes();
                    ReconcileJournal(c, leg);
                    SaveAll(); RefreshLegs(); RefreshRoute(); RefreshHold();
                    messageStatus.Text = "Containers saved for " + leg.Material + " at " + where + "." +
                                         (added.Count > 0 ? "  " + added.Count + " added to the hold manifest." : "");
                }
            }
        }

        void MarkLoadedCell(DataGridViewCell cell, CargoLeg l)
        {
            cell.Tag = l;
            cell.Style.BackColor = Theme.Surface;
            cell.Style.ForeColor = l.Estimated ? Theme.Highlight : Theme.Text;
            cell.ToolTipText = l.Estimated
                ? "Click and type how much you actually loaded here. The estimate for the other pickups and the route update automatically."
                : "Click and type how much of this cargo is loaded in your ship.";
        }

        static void DigitsOnly(object sender, KeyPressEventArgs e)
        {
            if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled = true;
        }

        void OnLegsLoadedEdited(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || legsGrid.Columns[e.ColumnIndex].Name != "Picked") return;
            DataGridViewRow row = legsGrid.Rows[e.RowIndex];
            DataGridViewCell cell = row.Cells[e.ColumnIndex];
            CargoLeg leg = cell.Tag as CargoLeg;
            Contract c = row.Tag as Contract;
            if (leg == null || c == null) return;
            string digits = new string(Convert.ToString(cell.Value).Where(char.IsDigit).ToArray());
            int n;
            bool skipped = IsSkippedPickup(leg);
            if (!int.TryParse(digits, out n) || (n == leg.PickedClamped() && !(n == 0 && skipped)))
            {
                BeginInvoke(new Action(RefreshLegs));   // put the old value back
                return;
            }
            if (!string.IsNullOrEmpty(leg.Containers) && Boxes.Total(leg.Containers) != n) leg.Containers = "";
            // rows can't be rebuilt while the grid is still finishing the edit
            BeginInvoke(new Action(() => ApplyLoaded(c, leg, n, true)));
        }

        // Records how much was actually loaded for one cargo line. For a delivery collected from several pickups
        // with unknown amounts, the rest of the contract total is re-spread over the pickups not visited yet.
        // a pickup of a multi-pickup group that was marked as visited with nothing loaded
        static bool IsSkippedPickup(CargoLeg l) { return !string.IsNullOrEmpty(l.Group) && !l.Estimated && l.Scu == 0 && l.PickedUp == 0; }

        // manual = typed in Cargo to Move (not a ticked route step). There, 0 means "nothing loaded here yet":
        // the pickup goes back to an estimate instead of being treated as visited-and-empty, and its route
        // history line is corrected to match.
        void ApplyLoaded(Contract c, CargoLeg leg, int n, bool manual = false)
        {
            string where = string.IsNullOrEmpty(leg.Pickup) ? "the pickup" : leg.Pickup;
            bool reset = manual && n == 0 && leg.Delivered == 0;
            if (reset) leg.Containers = "";
            string msg = reset ? "Cleared what was loaded of " + leg.Material + " at " + where + "."
                               : "Loaded " + n + " SCU " + leg.Material + " at " + where + ".";
            List<CargoLeg> grp = string.IsNullOrEmpty(leg.Group) ? null : c.Legs.Where(x => x.Group == leg.Group).ToList();
            if (grp != null && grp.Count > 1)
            {
                int total = grp.Sum(x => x.Scu);
                leg.Scu = Math.Max(n, leg.Delivered);
                leg.PickedUp = n;
                leg.Estimated = reset;
                List<CargoLeg> est = grp.Where(x => x.Estimated).ToList();
                int known = grp.Where(x => !x.Estimated).Sum(x => x.Scu);
                int remaining = Math.Max(0, total - known);
                for (int i = 0; i < est.Count; i++)
                {
                    int share = remaining / est.Count + (i < remaining % est.Count ? 1 : 0);
                    est[i].Scu = Math.Max(share, est[i].PickedClamped());
                }
                if (est.Count > 0)
                    msg += remaining > 0 ? "  ~" + remaining + " SCU left to collect from " + est.Count + (reset ? "" : " other") + " pickup" + (est.Count == 1 ? "" : "s") + "."
                                         : "  That covers the full " + total + " SCU - the other pickups can be skipped.";
                else if (known != total)
                    msg += "  Note: the pickups add up to " + known + " SCU, the contract said " + total + " SCU.";
            }
            else
            {
                leg.PickedUp = Math.Max(leg.Delivered, Math.Min(leg.Scu, n));
            }
            SaveAll();
            if (route != null && reset)
            {
                PlanRoute();
                RefreshContracts(); RefreshLegs();
                msg += "  Route re-planned.";
            }
            else if (route != null && !string.IsNullOrEmpty(leg.Pickup))
            {
                startBox.Text = leg.Pickup;   // you are standing at this pickup now
                PlanRoute();
                RefreshContracts(); RefreshLegs();
                msg += "  Route re-planned from here.";
            }
            else { RefreshContracts(); RefreshLegs(); }
            SyncAllBoxes();
            if (manual) { ReconcileJournal(c, leg); SaveAll(); }
            SaveHold(); RefreshRoute(); RefreshHold();
            messageStatus.Text = msg;
        }

        // After a hand edit of LOADED / CONTAINERS: the route history (DONE pickups) for that cargo line is
        // replaced by one entry that matches what's loaded now - or removed when nothing is loaded.
        void ReconcileJournal(Contract c, CargoLeg leg)
        {
            int li = c.Legs.IndexOf(leg);
            if (li < 0) return;
            int pos = -1;
            for (int i = data.Journal.Count - 1; i >= 0; i--)
            {
                StepRecord r = data.Journal[i];
                if (!r.IsPickup || r.ContractNumber != c.Number || !r.Parts.Any(p => p.LegIndex == li)) continue;
                pos = i;
                if (r.Parts.All(p => p.LegIndex == li)) data.Journal.RemoveAt(i);
                else
                {
                    r.Parts.RemoveAll(p => p.LegIndex == li);
                    HashSet<int> mine = new HashSet<int>(holdState.Boxes.Where(b => b.ContractNumber == c.Number && b.LegIndex == li).Select(b => b.Id));
                    r.BoxIds.RemoveAll(mine.Contains);
                }
            }
            int n = leg.PickedClamped();
            if (n <= 0 || pos < 0) return;     // nothing loaded, or it was never a ticked step
            StepRecord rec = new StepRecord { Id = data.NextRecord++, IsPickup = true, ContractNumber = c.Number, Location = leg.Pickup, Material = leg.Material, Containers = leg.Containers ?? "" };
            rec.Parts.Add(new StepPart { LegIndex = li, Scu = n });
            rec.BoxIds.AddRange(holdState.Boxes.Where(b => b.ContractNumber == c.Number && b.LegIndex == li && b.State != BoxState.Archived).Select(b => b.Id));
            data.Journal.Insert(Math.Min(pos, data.Journal.Count), rec);
        }

        string KindLabel(string raw, ResolvedLocation r)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string t = ResolvedLocation.KindText(r.Kind);
            return r.IsOutpost(settings.DistCentersAsOutposts) ? "⚠ " + t : t;
        }

        void OnLegsCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            string col = legsGrid.Columns[e.ColumnIndex].Name;
            if (col != "PickupType" && col != "DropoffType") return;
            object k = legsGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Tag;
            if (!(k is LocKind)) return;
            LocKind kind = (LocKind)k;
            e.CellStyle.ForeColor = Theme.KindColor(kind, settings.DistCentersAsOutposts);
            e.CellStyle.SelectionForeColor = e.CellStyle.ForeColor;
            if (kind == LocKind.Outpost || (kind == LocKind.DistributionCenter && settings.DistCentersAsOutposts)) e.CellStyle.Font = Theme.Semibold;
        }

        void HighlightLegs()
        {
            List<Contract> sel = SelectedContracts();
            foreach (DataGridViewRow r in legsGrid.Rows)
            {
                bool on = sel.Contains((Contract)r.Tag);
                r.DefaultCellStyle.BackColor = on ? Theme.RelatedRow : Color.Empty;
            }
        }

        void SaveAll()
        {
            try
            {
                Storage.Save("settings.json", settings);
                Storage.Save("contracts.json", data);
                holdState.Ship = settings.ShipName ?? "";
                Storage.Save("hold.json", holdState);
            }
            catch (Exception ex) { messageStatus.Text = "Could not save: " + ex.Message; }
        }

        // ================================================================ Settings / about
        void OpenSettings()
        {
            using (SettingsForm f = new SettingsForm(settings))
            {
                f.RequestLocationUpdate = btn => UpdateLocations(f, btn);
                if (f.ShowDialog(this) != DialogResult.OK) return;
                AppSettings s = f.Settings;
                // keep values owned by the main window
                s.ShipCapacity = settings.ShipCapacity; s.StartLocation = startBox.Text;
                s.RegionX = settings.RegionX; s.RegionY = settings.RegionY; s.RegionW = settings.RegionW; s.RegionH = settings.RegionH;
                s.OverlayX = settings.OverlayX; s.OverlayY = settings.OverlayY; s.OverlayW = settings.OverlayW; s.OverlayH = settings.OverlayH; s.OverlayOn = settings.OverlayOn;
                string oldTheme = ThemeKey(settings), oldCargo = settings.HoldColors ?? "";
                int oldLayout = settings.LayoutMode;
                if (s.LayoutMode == oldLayout) { s.LayoutColumn = settings.LayoutColumn; s.LayoutRow = settings.LayoutRow; }
                settings = s;
                TopMost = settings.AlwaysOnTop;
                if ((settings.HoldColors ?? "") != oldCargo) { HoldPanel.SetColors(settings.HoldColors); RefreshHold(); holdPanel.View.Invalidate(); }
                delayButton.Text = "Read in " + settings.CaptureDelaySeconds + "s";
                SaveAll();
                ApplyHotkey();
                if (overlay != null && overlay.Visible) ShowOverlay(true);
                UpdateGameStatus();
                RefreshAll();
                bool themeChanged = ThemeKey(settings) != oldTheme, layoutChanged = settings.LayoutMode != oldLayout;
                if ((themeChanged || layoutChanged) &&
                    MessageBox.Show(this, "Restart the app now to apply the new " + (themeChanged && layoutChanged ? "colors and layout" : themeChanged ? "colors" : "layout") + "?",
                                    "Settings", MessageBoxButtons.YesNo) == DialogResult.Yes)
                    RestartApp();
            }
        }

        // Settings -> Update locations: fetch the UEX lists in the background, merge them into locations.json.
        void UpdateLocations(Form owner, Button btn)
        {
            btn.Enabled = false; string old = btn.Text; btn.Text = "Downloading...";
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string err; List<Place> places = UexUpdater.Download(out err);
                BeginInvoke(new Action(delegate
                {
                    btn.Enabled = true; btn.Text = old;
                    if (places.Count == 0)
                    {
                        MessageBox.Show(owner, "Couldn't download the location lists from UEX Corp." + (err != null ? "\r\n\r\n" + err : "") +
                                               "\r\n\r\nThe built-in list is still used.", "Update locations");
                        return;
                    }
                    int added = LocationDb.AddPlaces(db.Data, places);
                    db.ClearCache();
                    try { Storage.Save("locations.json", db.Data); } catch { }
                    startSuggest.SetItems(StartLocations());
                    MessageBox.Show(owner, "Downloaded " + places.Count + " places from UEX Corp" + (err != null ? " (some lists failed: " + err + ")" : "") + ".\r\n" +
                                           (added > 0 ? added + " new place(s) added." : "No new places - your list is up to date."), "Update locations");
                }));
            });
        }

        static string ThemeKey(AppSettings s)
        {
            Dictionary<string, string> d = Theme.Resolve(s.ThemeName ?? Theme.DefaultTheme, s.ThemeColors);
            return string.Join(",", d.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value).ToArray());
        }

        void RestartApp()
        {
            try
            {
                SaveAll(); SaveHold();
                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, "--restart");
                psi.UseShellExecute = false;
                Process.Start(psi);
                Close();
            }
            catch (Exception ex) { messageStatus.Text = "Couldn't restart: " + ex.Message; }
        }

        void ShowAbout()
        {
            MessageBox.Show(this,
                AppName + " v" + Version + "\r\nC0P3Y | Icarus Interstellar Inc.\r\n\r\n" +
                "Reads accepted hauling contracts from the Star Citizen window with the OCR built into Windows, " +
                "and plans the pickup / drop-off order across all of them.\r\n\r\n" +
                "Portable: no install, no registry. Data is stored next to the exe:\r\n" + Storage.BaseDir + "\r\n\r\n" +
                "OCR language: " + (WindowsOcr.LanguageTag.Length > 0 ? WindowsOcr.LanguageTag : "not started") + "\r\n" +
                "Running as admin: " + (selfElevated ? "yes" : "no") + "\r\n" +
                "Not affiliated with Cloud Imperium Games. Read-only screen capture - nothing touches the game.",
                "About", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
