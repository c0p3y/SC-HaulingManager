// SC Hauling Manager - C0P3Y | Icarus Interstellar Inc.
// Ship list + cargo grid (bay) geometry. Data generated from SuperCargo 0.9.1 (MIT, (c) 2026 Travis),
// whose grid positions come from sc-cargo.space (credits Ratjack). Embedded in the exe; a ships.json next to
// the exe overrides it.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace HaulingManager
{
    public class BayInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public int W { get; set; }   // cells along x
        public int L { get; set; }   // cells along z
        public int H { get; set; }   // cells along y
        public int MaxSize { get; set; }      // 0 = no limit
        public bool AutoLoad { get; set; }
        public int Group { get; set; }
        public string ExitAxis { get; set; }  // "x", "z" or ""
        public int ExitDir { get; set; }
        public string Floor { get; set; }     // face the cargo rests on: "y-" (deck), "x-", "x+", "z-", "z+", "y+"
        public bool OffGrid { get; set; }     // the stash area beside the ship (not part of the cargo grid)
        public double RotX { get; set; }      // how the bay is turned in the ship, degrees about its centre (x, then y, then z)
        public double RotY { get; set; }
        public double RotZ { get; set; }

        public bool Rotated() { return RotX != 0 || RotY != 0 || RotZ != 0; }

        // 3x3 rotation (row-major), Euler X-Y-Z like three.js: M = Rx * Ry * Rz
        public double[] RotationMatrix()
        {
            double a = RotX * Math.PI / 180, b = RotY * Math.PI / 180, c = RotZ * Math.PI / 180;
            double ca = Math.Cos(a), sa = Math.Sin(a), cb = Math.Cos(b), sb = Math.Sin(b), cc = Math.Cos(c), sc = Math.Sin(c);
            return new[]
            {
                cb * cc,                  -cb * sc,                 sb,
                ca * sc + sa * sb * cc,   ca * cc - sa * sb * sc,   -sa * cb,
                sa * sc - ca * sb * cc,   sa * cc + ca * sb * sc,   ca * cb
            };
        }

        public double[] Center() { return new[] { X + W / 2.0, Y + H / 2.0, Z + L / 2.0 }; }

        // a point given in the bay's unturned frame (origin + local cells) -> where it really is in the ship
        public double[] ToWorld(double x, double y, double z)
        {
            if (!Rotated()) return new[] { x, y, z };
            double[] m = RotationMatrix(), c = Center();
            double dx = x - c[0], dy = y - c[1], dz = z - c[2];
            return new[] { c[0] + m[0] * dx + m[1] * dy + m[2] * dz, c[1] + m[3] * dx + m[4] * dy + m[5] * dz, c[2] + m[6] * dx + m[7] * dy + m[8] * dz };
        }

        public BayInfo() { Id = ""; Name = ""; ExitAxis = ""; Floor = "y-"; AutoLoad = true; }

        public int[] Size() { return new[] { W, H, L }; }                    // indexed by axis 0=x 1=y 2=z
        public double[] Origin() { return new[] { X, Y, Z }; }
        public int GravityAxis() { return string.IsNullOrEmpty(Floor) || Floor.Length < 2 ? 1 : (Floor[0] == 'x' ? 0 : Floor[0] == 'z' ? 2 : 1); }
        public int GravitySign() { return !string.IsNullOrEmpty(Floor) && Floor.Length >= 2 && Floor[1] == '+' ? 1 : -1; }
        public int ExitAxisIndex() { return ExitAxis == "x" ? 0 : 2; }
        public int ExitDirection() { return string.IsNullOrEmpty(ExitAxis) ? -1 : (ExitDir >= 0 ? 1 : -1); }
        public int Cells() { return W * L * H; }
    }

    public class ShipInfo
    {
        public string Name { get; set; }
        public int Scu { get; set; }
        public int LoadableScu { get; set; }
        public List<int> ContainerSizes { get; set; }
        public List<BayInfo> Grids { get; set; }
        public string Fore { get; set; }        // which way the bow points: "z-" (most ships) or "z+", "x+", "x-"
        public string Starboard { get; set; }   // right-hand side looking forward; empty = worked out from Fore
        public ShipInfo() { Name = ""; ContainerSizes = new List<int>(); Grids = new List<BayInfo>(); Fore = "z-"; Starboard = ""; }

        public bool Approximate() { return Grids.Count == 0; }

        // Ships without grid data get one rough box-shaped hold so cargo can still be visualised.
        public List<BayInfo> BaysOrApprox()
        {
            if (Grids.Count > 0) return Grids;
            int scu = Math.Max(1, LoadableScu > 0 ? LoadableScu : Scu);
            int h = scu >= 64 ? 2 : 1;
            int area = (int)Math.Ceiling(scu / (double)h);
            int w = Math.Max(1, (int)Math.Floor(Math.Sqrt(area / 2.0)));
            int l = (int)Math.Ceiling(area / (double)w);
            BayInfo b = new BayInfo { Id = "hold", Name = "Hold (approx.)", W = w, L = l, H = h, AutoLoad = true };
            return new List<BayInfo> { b };
        }
    }

    public class ShipFile
    {
        public string Source { get; set; }
        public List<ShipInfo> Ships { get; set; }
        public ShipFile() { Source = ""; Ships = new List<ShipInfo>(); }
    }

    public static class ShipDb
    {
        static List<ShipInfo> ships;

        public static List<ShipInfo> All
        {
            get { if (ships == null) Load(); return ships; }
        }

        static void Load()
        {
            string json = null;
            try
            {
                string p = Path.Combine(Storage.BaseDir, "ships.json");
                if (File.Exists(p)) json = File.ReadAllText(p);
            }
            catch { }
            if (json == null)
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                string res = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("ships.json", StringComparison.OrdinalIgnoreCase));
                if (res != null)
                    using (Stream s = asm.GetManifestResourceStream(res))
                    using (StreamReader r = new StreamReader(s)) json = r.ReadToEnd();
            }
            ships = new List<ShipInfo>();
            if (json != null)
            {
                try { ShipFile f = Json.Deserialize<ShipFile>(json); if (f != null && f.Ships != null) ships = f.Ships; }
                catch { }
            }
            foreach (ShipInfo s in ships)
            {
                if (s.Grids == null) s.Grids = new List<BayInfo>();
                if (s.ContainerSizes == null) s.ContainerSizes = new List<int>();
                foreach (BayInfo b in s.Grids) { if (b.Floor == null) b.Floor = "y-"; if (b.ExitAxis == null) b.ExitAxis = ""; }
                if (s.LoadableScu <= 0) s.LoadableScu = s.Scu;
            }
            ships = ships.OrderBy(x => x.Name).ToList();
        }

        public static ShipInfo Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return All.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
