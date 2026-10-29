// SC Hauling Manager - C0P3Y | Icarus Interstellar Inc.
// Data models. Written for the C# 5 compiler that ships inside Windows (.NET Framework 4.x).
using System;
using System.Collections.Generic;

namespace HaulingManager
{
    public class CargoLeg
    {
        public string Material { get; set; }
        public int Scu { get; set; }
        public int PickedUp { get; set; }
        public int Delivered { get; set; }
        public string Pickup { get; set; }
        public string Dropoff { get; set; }
        // Legs that share a Group came from ONE delivery that is collected at several pickup points.
        // Estimated = the contract does not say how much waits at each pickup, so the SCU is an even split.
        public string Group { get; set; }
        public bool Estimated { get; set; }
        // Containers that came up the freight elevator for this line, e.g. "3x8 1x4 1x2" (count x SCU size)
        public string Containers { get; set; }

        public CargoLeg()
        {
            Material = ""; Pickup = ""; Dropoff = ""; Group = ""; Containers = "";
        }

        // Methods (not properties) so they are not written into the JSON files.
        public int PickedClamped() { return Math.Min(Scu, Math.Max(PickedUp, Delivered)); }
        public int WaitingAtPickup() { return Math.Max(0, Scu - PickedClamped()); }
        public int OnBoard() { return Math.Max(0, PickedClamped() - Delivered); }
        // A pickup inside a multi-pickup delivery can legitimately end up with 0 SCU (nothing left to collect there).
        public bool IsDone() { return Delivered >= Scu && (Scu > 0 || !string.IsNullOrEmpty(Group)); }

        public CargoLeg Clone()
        {
            CargoLeg c = new CargoLeg();
            c.Material = Material; c.Scu = Scu; c.PickedUp = PickedUp; c.Delivered = Delivered;
            c.Pickup = Pickup; c.Dropoff = Dropoff; c.Group = Group; c.Estimated = Estimated; c.Containers = Containers;
            return c;
        }
    }

    public class Contract
    {
        public int Number { get; set; }
        public string Title { get; set; }
        public long Reward { get; set; }
        public string Added { get; set; }
        public string RawText { get; set; }
        public string Warnings { get; set; }
        public bool ManuallyCompleted { get; set; }
        public List<CargoLeg> Legs { get; set; }

        public Contract()
        {
            Title = ""; Added = DateTime.Now.ToString("yyyy-MM-dd HH:mm"); RawText = ""; Warnings = "";
            Legs = new List<CargoLeg>();
        }

        public bool IsDone()
        {
            if (ManuallyCompleted) return true;
            if (Legs.Count == 0) return false;
            foreach (CargoLeg l in Legs) if (!l.IsDone()) return false;
            return true;
        }

        public int TotalScu() { int t = 0; foreach (CargoLeg l in Legs) t += l.Scu; return t; }
        public int RemainingScu() { int t = 0; foreach (CargoLeg l in Legs) t += Math.Max(0, l.Scu - l.Delivered); return t; }
        public string Label() { return "#" + Number; }

        public Contract Clone()
        {
            Contract c = new Contract();
            c.Number = Number; c.Title = Title; c.Reward = Reward; c.Added = Added; c.RawText = RawText;
            c.Warnings = Warnings; c.ManuallyCompleted = ManuallyCompleted;
            foreach (CargoLeg l in Legs) c.Legs.Add(l.Clone());
            return c;
        }
    }

    // One completed route step (a pickup or a drop-off you ticked). Kept so the route can show what you've done,
    // and so un-ticking it can undo exactly that step.
    public class StepPart
    {
        public int LegIndex { get; set; }
        public int Scu { get; set; }
    }

    public class StepRecord
    {
        public int Id { get; set; }
        public bool IsPickup { get; set; }
        public int ContractNumber { get; set; }
        public string Location { get; set; }
        public string Material { get; set; }
        public string Containers { get; set; }       // what came up the elevator (pickups)
        public List<StepPart> Parts { get; set; }
        public List<int> BoxIds { get; set; }
        public string Time { get; set; }
        public StepRecord() { Location = ""; Material = ""; Containers = ""; Parts = new List<StepPart>(); BoxIds = new List<int>(); Time = DateTime.Now.ToString("HH:mm"); }
        public int Scu() { int t = 0; foreach (StepPart p in Parts) t += p.Scu; return t; }
    }

    public class AppData
    {
        public int NextNumber { get; set; }
        public int NextRecord { get; set; }
        public List<Contract> Contracts { get; set; }
        public List<StepRecord> Journal { get; set; }
        public AppData() { NextNumber = 1; NextRecord = 1; Contracts = new List<Contract>(); Journal = new List<StepRecord>(); }
    }

    public class AppSettings
    {
        // Hotkey (System.Windows.Forms.Keys value + MOD_* flags: Alt=1, Ctrl=2, Shift=4, Win=8)
        public int HotkeyKey { get; set; }
        public int HotkeyMods { get; set; }
        public int HotkeyMethod { get; set; }      // 0 = auto (keyboard hook + system hotkey + polling), 1 = polling only, 2 = system hotkey only
        public string ProcessName { get; set; }
        public int CaptureMethod { get; set; }     // 0 = screen copy, 1 = PrintWindow
        public bool UseRegion { get; set; }
        public double RegionX { get; set; }
        public double RegionY { get; set; }
        public double RegionW { get; set; }
        public double RegionH { get; set; }
        public int OcrMode { get; set; }           // 0 = auto (two passes), 1 = normal, 2 = inverted
        public double Upscale { get; set; }
        public int AfterCapture { get; set; }      // 0 = add automatically + toast, 1 = open review window
        public bool SaveDebug { get; set; }
        public bool Beep { get; set; }
        public bool AlwaysOnTop { get; set; }
        public int ShipCapacity { get; set; }
        public string StartLocation { get; set; }
        public bool DistCentersAsOutposts { get; set; }
        public int CaptureDelaySeconds { get; set; }
        public bool AskContainersOnPickup { get; set; }
        public int PackMode { get; set; }           // 0 tightest fit, 1 group by cargo + destination, 2 cargo groups + gaps, 3 destination groups + gaps
        public string HoldColors { get; set; }      // destination colours for the cargo hold, in the order they're used
        public string ThemeName { get; set; }       // a preset name or "Custom"
        public Dictionary<string, string> ThemeColors { get; set; }   // your colours when ThemeName is "Custom" (role id -> hex)
        public int LayoutMode { get; set; }         // 0 aligned grid (default), 1 classic (v0.7.1)
        public double LayoutColumn { get; set; }    // aligned grid: where the column line sits (fraction of the width)
        public double LayoutRow { get; set; }       // aligned grid: where the row line sits (fraction of the height)
        public int LayoutVersion { get; set; }
        // in-game route overlay
        public int StepHotkeyKey { get; set; }      // ticks off the current route step
        public int StepHotkeyMods { get; set; }
        public int UndoHotkeyKey { get; set; }      // un-ticks the last step
        public int UndoHotkeyMods { get; set; }
        public int OverlayHotkeyKey { get; set; }   // shows / hides the overlay
        public int OverlayHotkeyMods { get; set; }
        public bool OverlayOn { get; set; }
        public int OverlayOpacity { get; set; }     // percent
        public bool OverlayClickThrough { get; set; }
        public int OverlayX { get; set; }
        public int OverlayY { get; set; }
        public int OverlayW { get; set; }
        public int OverlayH { get; set; }
        public string ShipName { get; set; }

        public AppSettings()
        {
            HotkeyKey = 0x48;   // H
            HotkeyMods = 2 | 4; // Ctrl + Shift
            HotkeyMethod = 0;
            StepHotkeyKey = 0x23; StepHotkeyMods = 2;       // Ctrl + End
            UndoHotkeyKey = 0x24; UndoHotkeyMods = 2;       // Ctrl + Home
            OverlayHotkeyKey = 0x2E; OverlayHotkeyMods = 2; // Ctrl + Delete
            OverlayOpacity = 85;
            OverlayX = -1; OverlayY = -1; OverlayW = 360; OverlayH = 420;
            ProcessName = "StarCitizen";
            CaptureMethod = 0;
            UseRegion = false;
            RegionX = 0; RegionY = 0; RegionW = 1; RegionH = 1;
            OcrMode = 0;
            Upscale = 2.0;
            AfterCapture = 0;
            SaveDebug = false;
            Beep = true;
            AlwaysOnTop = false;
            ShipCapacity = 0;
            StartLocation = "";
            DistCentersAsOutposts = true;
            CaptureDelaySeconds = 5;
            AskContainersOnPickup = true;
            ShipName = "";
        }
    }

    // Freight-elevator container lists, stored as "3x8 1x4 1x2" (count x size in SCU).
    public static class Boxes
    {
        public static readonly int[] Sizes = { 32, 24, 16, 8, 4, 2, 1 };
        static readonly System.Text.RegularExpressions.Regex Item =
            new System.Text.RegularExpressions.Regex(@"(\d+)\s*[x×*]\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        public static SortedDictionary<int, int> Parse(string s)
        {
            SortedDictionary<int, int> d = new SortedDictionary<int, int>();
            if (string.IsNullOrEmpty(s)) return d;
            foreach (System.Text.RegularExpressions.Match m in Item.Matches(s))
            {
                int count, size;
                if (!int.TryParse(m.Groups[1].Value, out count) || !int.TryParse(m.Groups[2].Value, out size) || count <= 0 || size <= 0) continue;
                int cur; d.TryGetValue(size, out cur); d[size] = cur + count;
            }
            return d;
        }

        public static string Format(IDictionary<int, int> d)
        {
            List<string> parts = new List<string>();
            foreach (KeyValuePair<int, int> kv in d) if (kv.Value > 0) parts.Insert(0, kv.Value + "x" + kv.Key);
            return string.Join(" ", parts.ToArray());   // largest size first
        }

        public static string Normalize(string s) { return Format(Parse(s)); }

        public static string Pretty(string s)
        {
            return Normalize(s).Replace("x", "×").Replace(" ", "  ");
        }

        public static int Total(string s) { int t = 0; foreach (KeyValuePair<int, int> kv in Parse(s)) t += kv.Key * kv.Value; return t; }
        public static int Count(string s) { int t = 0; foreach (KeyValuePair<int, int> kv in Parse(s)) t += kv.Value; return t; }

        // a minus b (never below zero)
        public static string Subtract(string a, string b)
        {
            SortedDictionary<int, int> d = Parse(a);
            foreach (KeyValuePair<int, int> kv in Parse(b)) { int cur; if (d.TryGetValue(kv.Key, out cur)) d[kv.Key] = Math.Max(0, cur - kv.Value); }
            return Format(d);
        }

        public static string Merge(params string[] lists)
        {
            SortedDictionary<int, int> d = new SortedDictionary<int, int>();
            foreach (string l in lists)
                foreach (KeyValuePair<int, int> kv in Parse(l)) { int cur; d.TryGetValue(kv.Key, out cur); d[kv.Key] = cur + kv.Value; }
            return Format(d);
        }
    }
}
