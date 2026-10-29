// SC Hauling Manager - C0P3Y | Icarus Interstellar Inc.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace HaulingManager
{
    public class SettingsForm : Form
    {
        public AppSettings Settings;
        int hkKey, hkMods;
        readonly TextBox hotkeyBox = new TextBox();
        readonly ComboBox layoutBox = new ComboBox(), hotkeyMethod = new ComboBox(), captureMethod = new ComboBox(), ocrMode = new ComboBox(), after = new ComboBox(), delay = new ComboBox();
        readonly TextBox processName = new TextBox();
        readonly NumericUpDown upscale = new NumericUpDown();
        readonly CheckBox useRegion = new CheckBox(), saveDebug = new CheckBox(), beep = new CheckBox(), onTop = new CheckBox(), dcOutpost = new CheckBox(), askBoxes = new CheckBox();
        static readonly int[] Delays = { 3, 5, 8, 10, 15 };
        ThemeEditor colorsPage;
        public Action<Button> RequestLocationUpdate;
        readonly int[] stepKey = new int[2], undoKey = new int[2], ovKey = new int[2];   // key, mods
        readonly NumericUpDown ovOpacity = new NumericUpDown();
        readonly CheckBox ovClick = new CheckBox();

        // a click-then-press box for one hotkey; Backspace clears it
        TextBox HotkeyBox(int[] state)
        {
            TextBox b = new TextBox { ReadOnly = true, Width = 300, Text = HotkeyText(state[1], state[0]) };
            b.PreviewKeyDown += delegate(object s, PreviewKeyDownEventArgs e) { e.IsInputKey = true; };
            b.KeyDown += delegate(object s, KeyEventArgs e)
            {
                e.SuppressKeyPress = true; e.Handled = true;
                Keys k = e.KeyCode;
                if (k == Keys.Back || k == Keys.Delete && !e.Control && !e.Alt && !e.Shift) { state[0] = 0; state[1] = 0; b.Text = HotkeyText(0, 0); return; }
                if (k == Keys.ControlKey || k == Keys.ShiftKey || k == Keys.Menu || k == Keys.LWin || k == Keys.RWin) return;
                int mods = (e.Control ? 2 : 0) | (e.Alt ? 1 : 0) | (e.Shift ? 4 : 0);
                state[0] = (int)k; state[1] = mods;
                b.Text = HotkeyText(mods, (int)k);
            };
            return b;
        }

        public static string HotkeyText(int mods, int key)
        {
            if (key == 0) return "(none)";
            string s = "";
            if ((mods & 2) != 0) s += "Ctrl + ";
            if ((mods & 1) != 0) s += "Alt + ";
            if ((mods & 4) != 0) s += "Shift + ";
            if ((mods & 8) != 0) s += "Win + ";
            return s + ((Keys)key).ToString();
        }

        public SettingsForm(AppSettings current)
        {
            Settings = Json.Deserialize<AppSettings>(Json.Serialize(current)); // copy
            hkKey = Settings.HotkeyKey; hkMods = Settings.HotkeyMods;

            Text = "Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(760, 820);
            Theme.ApplyForm(this);

            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill; t.Padding = new Padding(12); t.ColumnCount = 2; t.AutoScroll = true;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            hotkeyBox.ReadOnly = true; hotkeyBox.Width = 300;
            hotkeyBox.Text = HotkeyText(hkMods, hkKey);
            hotkeyBox.PreviewKeyDown += delegate(object s, PreviewKeyDownEventArgs e) { e.IsInputKey = true; };
            hotkeyBox.KeyDown += OnHotkeyKeyDown;
            Add(t, "Read-contract hotkey", hotkeyBox, "Click the box, then press the key combination. Backspace clears.");

            hotkeyMethod.DropDownStyle = ComboBoxStyle.DropDownList; hotkeyMethod.Width = 300;
            hotkeyMethod.Items.AddRange(new object[] { "Auto - keyboard hook, works in game (recommended)", "Keyboard polling", "System hotkey only (does not work in game)" });
            hotkeyMethod.SelectedIndex = Clamp(Settings.HotkeyMethod, 2);
            Add(t, "Hotkey method", hotkeyMethod, "If Star Citizen runs as administrator, this app must too (use Restart as Admin).");

            stepKey[0] = Settings.StepHotkeyKey; stepKey[1] = Settings.StepHotkeyMods;
            undoKey[0] = Settings.UndoHotkeyKey; undoKey[1] = Settings.UndoHotkeyMods;
            ovKey[0] = Settings.OverlayHotkeyKey; ovKey[1] = Settings.OverlayHotkeyMods;
            Add(t, "Complete route step", HotkeyBox(stepKey), "Ticks off the highlighted step in the route (and on the overlay) without leaving the game.");
            Add(t, "Undo route step", HotkeyBox(undoKey), "Un-ticks the last step you ticked.");
            Add(t, "Show / hide overlay", HotkeyBox(ovKey), "The in-game route overlay (also the Overlay button). Needs Borderless or Windowed mode.");
            ovOpacity.Minimum = 30; ovOpacity.Maximum = 100; ovOpacity.Increment = 5; ovOpacity.Width = 80;
            ovOpacity.Value = Math.Max(30, Math.Min(100, Settings.OverlayOpacity));
            Add(t, "Overlay opacity (%)", ovOpacity, null);
            ovClick.Text = "Click-through (mouse goes to the game; move it with this off)"; ovClick.AutoSize = true; ovClick.Checked = Settings.OverlayClickThrough;
            Add(t, "Overlay", ovClick, null);

            delay.DropDownStyle = ComboBoxStyle.DropDownList; delay.Width = 120;
            foreach (int d in Delays) delay.Items.Add(d + " seconds");
            int di = Array.IndexOf(Delays, Settings.CaptureDelaySeconds);
            delay.SelectedIndex = di >= 0 ? di : 1;
            Add(t, "'Read in' countdown", delay, "Click the button, switch to the game, it reads when the countdown ends.");

            processName.Text = Settings.ProcessName; processName.Width = 300;
            Add(t, "Game process name", processName, "Default: StarCitizen");

            captureMethod.DropDownStyle = ComboBoxStyle.DropDownList; captureMethod.Width = 300;
            captureMethod.Items.AddRange(new object[] { "Screen copy (Borderless / Windowed)", "PrintWindow (try if capture is black)" });
            captureMethod.SelectedIndex = Clamp(Settings.CaptureMethod, 1);
            Add(t, "Capture method", captureMethod, null);

            useRegion.Text = "Only read the selected region (set with 'Select Region')"; useRegion.AutoSize = true; useRegion.Checked = Settings.UseRegion;
            Add(t, "Capture region", useRegion, null);

            ocrMode.DropDownStyle = ComboBoxStyle.DropDownList; ocrMode.Width = 300;
            ocrMode.Items.AddRange(new object[] { "Auto (two passes, keep best)", "Normal image", "Inverted image" });
            ocrMode.SelectedIndex = Clamp(Settings.OcrMode, 2);
            Add(t, "OCR mode", ocrMode, null);

            upscale.DecimalPlaces = 1; upscale.Increment = 0.5m; upscale.Minimum = 1; upscale.Maximum = 4;
            upscale.Value = (decimal)Math.Max(1, Math.Min(4, Settings.Upscale)); upscale.Width = 80;
            Add(t, "OCR upscale", upscale, "Bigger helps small text (limited by the Windows OCR max image size).");

            after.DropDownStyle = ComboBoxStyle.DropDownList; after.Width = 300;
            after.Items.AddRange(new object[] { "Add automatically + show popup", "Open review window" });
            after.SelectedIndex = Clamp(Settings.AfterCapture, 1);
            Add(t, "After a capture", after, null);

            dcOutpost.Text = "Count distribution centers as planetary outposts"; dcOutpost.AutoSize = true; dcOutpost.Checked = Settings.DistCentersAsOutposts;
            Add(t, "Outpost check", dcOutpost, null);

            askBoxes.Text = "Ask for the containers when I tick a PICK UP in the route"; askBoxes.AutoSize = true; askBoxes.Checked = Settings.AskContainersOnPickup;
            Add(t, "Containers", askBoxes, null);

            layoutBox.DropDownStyle = ComboBoxStyle.DropDownList; layoutBox.Width = 300;
            layoutBox.Items.AddRange(new object[] { "Aligned grid - bigger cargo hold (default)", "Classic (v0.7.1)" });
            layoutBox.SelectedIndex = Clamp(Settings.LayoutMode, 1);
            Add(t, "Window layout", layoutBox, "Applies after a restart (offered when you save). In the aligned grid, drag the lines between the panels to resize - the app remembers them.");

            beep.Text = "Play a sound on capture"; beep.AutoSize = true; beep.Checked = Settings.Beep;
            Add(t, "", beep, null);
            onTop.Text = "Keep this window on top"; onTop.AutoSize = true; onTop.Checked = Settings.AlwaysOnTop;
            Add(t, "", onTop, null);
            saveDebug.Text = "Save captures + OCR text to the 'debug' folder"; saveDebug.AutoSize = true; saveDebug.Checked = Settings.SaveDebug;
            Add(t, "Troubleshooting", saveDebug, null);

            FlowLayoutPanel links = new FlowLayoutPanel(); links.AutoSize = true;
            Button openData = new Button(); openData.Text = "Open data folder"; openData.AutoSize = true;
            openData.Click += delegate { Process.Start("explorer.exe", "\"" + Storage.BaseDir + "\""); };
            Button openLoc = new Button(); openLoc.Text = "Edit locations.json"; openLoc.AutoSize = true;
            openLoc.Click += delegate
            {
                string p = Path.Combine(Storage.BaseDir, "locations.json");
                if (File.Exists(p)) Process.Start("notepad.exe", "\"" + p + "\"");
            };
            Button updLoc = new Button(); updLoc.Text = "Update locations (UEX)"; updLoc.AutoSize = true;
            updLoc.Click += delegate { if (RequestLocationUpdate != null) RequestLocationUpdate(updLoc); };
            links.Controls.Add(openData); links.Controls.Add(openLoc); links.Controls.Add(updLoc);
            Add(t, "Files", links, "locations.json changes apply after restarting the app. 'Update locations' downloads the current list of stations, outposts, cities and distribution centres from UEX Corp (uexcorp.space).");

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Bottom; buttons.FlowDirection = FlowDirection.RightToLeft; buttons.Height = 44; buttons.Padding = new Padding(8);
            Button ok = Theme.MakeButton("Save", Theme.ButtonKind.Primary, OnOk);
            Button cancel = Theme.MakeButton("Cancel", Theme.ButtonKind.Secondary, null); cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(cancel); buttons.Controls.Add(ok);
            AcceptButton = ok; CancelButton = cancel;

            buttons.BackColor = Theme.Header;

            // two pages: General | Colors & Themes
            colorsPage = new ThemeEditor(Settings);
            colorsPage.Visible = false;
            Panel pages = new Panel { Dock = DockStyle.Fill };
            pages.Controls.Add(t); pages.Controls.Add(colorsPage);
            FlowLayoutPanel tabs = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(10, 6, 10, 0), BackColor = Theme.Header };
            Button gTab = Theme.MakeButton("General", Theme.ButtonKind.Secondary, null);
            Button cTab = Theme.MakeButton("Colors && Themes", Theme.ButtonKind.Secondary, null);
            Action<bool> show = colorsTab =>
            {
                t.Visible = !colorsTab; colorsPage.Visible = colorsTab;
                Theme.StyleButton(gTab, colorsTab ? Theme.ButtonKind.Secondary : Theme.ButtonKind.Accent);
                Theme.StyleButton(cTab, colorsTab ? Theme.ButtonKind.Accent : Theme.ButtonKind.Secondary);
            };
            gTab.Click += delegate { show(false); };
            cTab.Click += delegate { show(true); };
            tabs.Controls.Add(gTab); tabs.Controls.Add(cTab);
            Controls.Add(pages); Controls.Add(tabs); Controls.Add(buttons);
            Theme.ApplyControls(t);
            show(false);
        }

        static int Clamp(int v, int max) { return v < 0 ? 0 : (v > max ? max : v); }

        static void Add(TableLayoutPanel t, string label, Control c, string tip)
        {
            Label l = new Label(); l.Text = label; l.AutoSize = true; l.Padding = new Padding(0, 6, 0, 0);
            t.Controls.Add(l);
            if (tip == null) t.Controls.Add(c);
            else
            {
                FlowLayoutPanel f = new FlowLayoutPanel(); f.FlowDirection = FlowDirection.TopDown; f.AutoSize = true; f.WrapContents = false;
                Label tl = new Label(); tl.Text = tip; tl.AutoSize = true; tl.ForeColor = Theme.Muted; tl.Font = Theme.Small; tl.MaximumSize = new Size(360, 0);
                f.Controls.Add(c); f.Controls.Add(tl);
                t.Controls.Add(f);
            }
        }

        void OnHotkeyKeyDown(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true; e.Handled = true;
            Keys k = e.KeyCode;
            if (k == Keys.Back || k == Keys.Delete) { hkKey = 0; hkMods = 0; hotkeyBox.Text = HotkeyText(0, 0); return; }
            if (k == Keys.ControlKey || k == Keys.ShiftKey || k == Keys.Menu || k == Keys.LWin || k == Keys.RWin) return;
            int mods = 0;
            if (e.Control) mods |= 2;
            if (e.Alt) mods |= 1;
            if (e.Shift) mods |= 4;
            hkKey = (int)k; hkMods = mods;
            hotkeyBox.Text = HotkeyText(hkMods, hkKey);
        }

        void OnOk(object sender, EventArgs e)
        {
            Settings.HotkeyKey = hkKey; Settings.HotkeyMods = hkMods;
            Settings.HotkeyMethod = hotkeyMethod.SelectedIndex;
            Settings.ProcessName = processName.Text.Trim().Length > 0 ? processName.Text.Trim() : "StarCitizen";
            Settings.CaptureMethod = captureMethod.SelectedIndex;
            Settings.UseRegion = useRegion.Checked;
            Settings.OcrMode = ocrMode.SelectedIndex;
            Settings.Upscale = (double)upscale.Value;
            Settings.AfterCapture = after.SelectedIndex;
            Settings.Beep = beep.Checked;
            Settings.AlwaysOnTop = onTop.Checked;
            Settings.SaveDebug = saveDebug.Checked;
            Settings.DistCentersAsOutposts = dcOutpost.Checked;
            Settings.AskContainersOnPickup = askBoxes.Checked;
            Settings.CaptureDelaySeconds = Delays[Math.Max(0, delay.SelectedIndex)];
            colorsPage.Commit(Settings);
            Settings.StepHotkeyKey = stepKey[0]; Settings.StepHotkeyMods = stepKey[1];
            Settings.UndoHotkeyKey = undoKey[0]; Settings.UndoHotkeyMods = undoKey[1];
            Settings.OverlayHotkeyKey = ovKey[0]; Settings.OverlayHotkeyMods = ovKey[1];
            Settings.OverlayOpacity = (int)ovOpacity.Value;
            Settings.OverlayClickThrough = ovClick.Checked;
            if (layoutBox.SelectedIndex != Settings.LayoutMode) { Settings.LayoutMode = layoutBox.SelectedIndex; Settings.LayoutColumn = 0; Settings.LayoutRow = 0; }
            DialogResult = DialogResult.OK;
        }
    }
}
