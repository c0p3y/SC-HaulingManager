// SC Hauling Manager - C0P3Y | Icarus Interstellar Inc.
// Cargo hold model: every container you record lives here until it's delivered - in the MANIFEST (waiting to be
// placed), ON SHIP (at a grid position) or DELIVERED. Includes the delivery-aware auto packer.
// Packing ideas (deep end first, later drops deeper, gravity / support) follow SuperCargo's packer.
using System;
using System.Collections.Generic;
using System.Linq;

namespace HaulingManager
{
    public enum BoxState { Manifest = 0, OnShip = 1, Delivered = 2, Archived = 3 }

    public class CargoBox
    {
        public int Id { get; set; }
        public int Size { get; set; }            // SCU: 1,2,4,8,16,24,32
        public int ContractNumber { get; set; }
        public int LegIndex { get; set; }
        public string Material { get; set; }
        public string Pickup { get; set; }
        public string Dropoff { get; set; }
        public BoxState State { get; set; }
        public string Bay { get; set; }          // bay id while on ship (kept after delivery so "undeliver" can put it back)
        public int X { get; set; }               // local cell position inside the bay (min corner)
        public int Y { get; set; }
        public int Z { get; set; }
        public bool Rot { get; set; }            // footprint turned 90 degrees
        public int Batch { get; set; }           // which load (pickup) created it
        public string DeliveredAt { get; set; }
        public int DeliveredRecord { get; set; }

        public CargoBox() { Material = ""; Pickup = ""; Dropoff = ""; Bay = ""; DeliveredAt = ""; }

        // container dims in cells: w (across), l (long), h (height) - containers never stand on end
        public static int[] Dims(int size)
        {
            switch (size)
            {
                case 1: return new[] { 1, 1, 1 };
                case 2: return new[] { 1, 2, 1 };
                case 4: return new[] { 2, 2, 1 };
                case 8: return new[] { 2, 2, 2 };
                case 16: return new[] { 2, 4, 2 };
                case 24: return new[] { 2, 6, 2 };
                case 32: return new[] { 2, 8, 2 };
                default: return new[] { 1, 1, 1 };
            }
        }
    }

    public class HoldState
    {
        public string Ship { get; set; }
        public int NextId { get; set; }
        public int NextBatch { get; set; }
        public List<CargoBox> Boxes { get; set; }
        public HoldState() { Ship = ""; NextId = 1; NextBatch = 1; Boxes = new List<CargoBox>(); }
    }

    // Local box extent inside a bay for a given orientation (axis order x,y,z).
    public struct Placement
    {
        public BayInfo Bay;
        public int[] P;      // min corner, local cells
        public int[] D;      // dims along x,y,z
        public bool Rot;
        public bool Valid;
    }

    public class HoldModel
    {
        public HoldState State;
        public ShipInfo Ship;
        public List<BayInfo> Bays = new List<BayInfo>();

        public HoldModel(HoldState state) { State = state; }

        public const string OffGridId = "off-grid";

        public void SetShip(ShipInfo ship)
        {
            Ship = ship;
            Bays = ship != null ? new List<BayInfo>(ship.BaysOrApprox()) : new List<BayInfo>();
            if (ship != null) Bays.Add(MakeOffGrid(Bays));
        }

        // A stash area next to the ship for cargo that isn't on the cargo grid (loose on the floor, in a
        // vehicle bay, waiting to be sorted). The packer never fills it; you drag crates there yourself.
        static BayInfo MakeOffGrid(List<BayInfo> bays)
        {
            double maxX = 0, minY = 0, minZ = 0; bool first = true;
            int cap = 0;
            foreach (BayInfo b in bays)
            {
                for (int i = 0; i < 8; i++)
                {
                    double[] cw = b.ToWorld((i & 1) == 0 ? b.X : b.X + b.W, (i & 2) == 0 ? b.Y : b.Y + b.H, (i & 4) == 0 ? b.Z : b.Z + b.L);
                    if (first) { maxX = cw[0]; minY = cw[1]; minZ = cw[2]; first = false; }
                    maxX = Math.Max(maxX, cw[0]); minY = Math.Min(minY, cw[1]); minZ = Math.Min(minZ, cw[2]);
                }
                if (b.AutoLoad) cap += b.Cells();
            }
            int w = cap >= 400 ? 12 : cap >= 120 ? 10 : 6;
            int l = cap >= 400 ? 10 : cap >= 120 ? 8 : 6;
            int h = cap >= 400 ? 4 : cap >= 64 ? 3 : 2;
            return new BayInfo { Id = OffGridId, Name = "OFF GRID", X = Math.Ceiling(maxX) + 6, Y = minY, Z = minZ, W = w, L = l, H = h, AutoLoad = false, OffGrid = true };
        }

        // First free resting spot in one bay (scans the floor, gravity drops the crate onto whatever is there).
        public bool PlaceAnywhere(BayInfo bay, CargoBox box)
        {
            int[] s = bay.Size();
            int g = bay.GravityAxis(), sign = bay.GravitySign();
            int a1, a2; Lateral(g, out a1, out a2);
            foreach (bool rot in new[] { box.Rot, !box.Rot })
            {
                int[] d = DimsIn(bay, box.Size, rot);
                for (int i = 0; i + d[a1] <= s[a1]; i++)
                    for (int j = 0; j + d[a2] <= s[a2]; j++)
                    {
                        int[] p = new int[3]; p[a1] = i; p[a2] = j; p[g] = sign < 0 ? s[g] - d[g] : 0;
                        Placement pl = Drop(bay, box.Size, rot, p, box);
                        if (pl.Valid) { Place(box, pl); return true; }
                    }
            }
            return false;
        }

        public int OffGridScu() { return OnShip().Where(b => b.Bay == OffGridId).Sum(b => b.Size); }

        public BayInfo BayById(string id) { return Bays.FirstOrDefault(b => b.Id == id); }

        public IEnumerable<CargoBox> OnShip() { return State.Boxes.Where(b => b.State == BoxState.OnShip); }
        public IEnumerable<CargoBox> InManifest() { return State.Boxes.Where(b => b.State == BoxState.Manifest); }

        public int LoadedScu() { return OnShip().Where(b => b.Bay != OffGridId).Sum(b => b.Size); }
        public int Capacity() { return Bays.Where(b => b.AutoLoad && !b.OffGrid).Sum(b => b.Cells()); }

        // ---------------------------------------------------------------- geometry helpers
        public static int[] DimsIn(BayInfo bay, int size, bool rot)
        {
            int[] d = CargoBox.Dims(size);
            int w = rot ? d[1] : d[0], l = rot ? d[0] : d[1], h = d[2];
            int g = bay.GravityAxis();
            int[] r = new int[3];
            r[g] = h;
            int a1, a2; Lateral(g, out a1, out a2);
            r[a1] = w; r[a2] = l;
            return r;
        }

        // the two axes across the floor for a gravity axis
        public static void Lateral(int g, out int a1, out int a2)
        {
            if (g == 1) { a1 = 0; a2 = 2; }
            else if (g == 0) { a1 = 2; a2 = 1; }
            else { a1 = 0; a2 = 1; }
        }

        public bool[,,] Occupancy(BayInfo bay, CargoBox ignore)
        {
            bool[,,] occ = new bool[bay.W, bay.H, bay.L];
            foreach (CargoBox b in OnShip())
            {
                if (b == ignore || b.Bay != bay.Id) continue;
                int[] d = DimsIn(bay, b.Size, b.Rot);
                Mark(occ, bay, new[] { b.X, b.Y, b.Z }, d, true);
            }
            return occ;
        }

        static void Mark(bool[,,] occ, BayInfo bay, int[] p, int[] d, bool v)
        {
            for (int x = p[0]; x < p[0] + d[0]; x++)
                for (int y = p[1]; y < p[1] + d[1]; y++)
                    for (int z = p[2]; z < p[2] + d[2]; z++)
                        if (x >= 0 && y >= 0 && z >= 0 && x < bay.W && y < bay.H && z < bay.L) occ[x, y, z] = v;
        }

        public static bool InBounds(BayInfo bay, int[] p, int[] d)
        {
            int[] s = bay.Size();
            for (int a = 0; a < 3; a++) if (p[a] < 0 || p[a] + d[a] > s[a]) return false;
            return true;
        }

        public static bool Free(bool[,,] occ, int[] p, int[] d)
        {
            for (int x = p[0]; x < p[0] + d[0]; x++)
                for (int y = p[1]; y < p[1] + d[1]; y++)
                    for (int z = p[2]; z < p[2] + d[2]; z++)
                        if (occ[x, y, z]) return false;
            return true;
        }

        // fraction of the footprint resting on the floor or on other boxes
        public static double Support(BayInfo bay, bool[,,] occ, int[] p, int[] d)
        {
            int g = bay.GravityAxis(), sign = bay.GravitySign();
            int[] s = bay.Size();
            int below = sign < 0 ? p[g] - 1 : p[g] + d[g];
            if (below < 0 || below >= s[g]) return 1.0;
            int a1, a2; Lateral(g, out a1, out a2);
            int total = 0, ok = 0;
            int[] c = new int[3];
            for (int i = p[a1]; i < p[a1] + d[a1]; i++)
                for (int j = p[a2]; j < p[a2] + d[a2]; j++)
                {
                    c[a1] = i; c[a2] = j; c[g] = below;
                    total++;
                    if (occ[c[0], c[1], c[2]]) ok++;
                }
            return total == 0 ? 0 : ok / (double)total;
        }

        // Drop a box toward the bay floor from the given start position until it rests on something.
        public Placement Drop(BayInfo bay, int size, bool rot, int[] start, CargoBox ignore)
        {
            Placement pl = new Placement { Bay = bay, Rot = rot, D = DimsIn(bay, size, rot), P = (int[])start.Clone() };
            if (bay.MaxSize > 0 && size > bay.MaxSize) return pl;
            int[] s = bay.Size();
            for (int a = 0; a < 3; a++) pl.P[a] = Math.Max(0, Math.Min(s[a] - pl.D[a], pl.P[a]));
            if (!InBounds(bay, pl.P, pl.D)) return pl;
            bool[,,] occ = Occupancy(bay, ignore);
            if (!Free(occ, pl.P, pl.D))
            {
                // the spot is taken: try stepping up (away from the floor) until there's room
                int g0 = bay.GravityAxis(), sg = bay.GravitySign();
                bool found = false;
                for (int k = 0; k < s[g0]; k++)
                {
                    pl.P[g0] -= sg;
                    if (!InBounds(bay, pl.P, pl.D)) break;
                    if (Free(occ, pl.P, pl.D)) { found = true; break; }
                }
                if (!found) return pl;
            }
            int g = bay.GravityAxis(), sign = bay.GravitySign();
            while (true)
            {
                int[] next = (int[])pl.P.Clone();
                next[g] += sign;
                if (!InBounds(bay, next, pl.D) || !Free(occ, next, pl.D)) break;
                pl.P = next;
            }
            pl.Valid = true;
            return pl;
        }

        public void Place(CargoBox b, Placement pl)
        {
            b.State = BoxState.OnShip; b.Bay = pl.Bay.Id; b.Rot = pl.Rot;
            b.X = pl.P[0]; b.Y = pl.P[1]; b.Z = pl.P[2];
        }

        public bool CanStay(CargoBox b)
        {
            BayInfo bay = BayById(b.Bay);
            if (bay == null) return false;
            int[] d = DimsIn(bay, b.Size, b.Rot);
            int[] p = { b.X, b.Y, b.Z };
            return InBounds(bay, p, d) && Free(Occupancy(bay, b), p, d);
        }

        // ---------------------------------------------------------------- packer
        // rank: lower = delivered sooner. Boxes delivered last are packed first, into the deep end of each bay
        // (away from the exit), lowest layer first, so cargo for the next stop ends up nearest the exit and on top.
        // group (optional): boxes with the same key are packed together, one group after another.
        // sub (optional): order inside a group (e.g. cargo type, so each cargo stays together within a destination).
        // gap: empty cells left along the bay (toward the exit) between one group and the next.
        // Placement keeps the cargo as short as possible along the bay (floor space): it stacks up before it
        // spreads out, and turns containers to whichever way uses less floor.
        public int Pack(List<CargoBox> boxes, Func<CargoBox, int> rank, Func<CargoBox, string> group = null, int gap = 0, Func<CargoBox, string> sub = null)
        {
            List<BayInfo> bays = Bays.Where(b => b.AutoLoad && !b.OffGrid).ToList();
            HashSet<CargoBox> set = new HashSet<CargoBox>(boxes);
            Dictionary<string, bool[,,]> occ = new Dictionary<string, bool[,,]>();
            Dictionary<string, int> frontier = new Dictionary<string, int>();     // first free slab from the deep end
            Dictionary<string, string> lastGroup = new Dictionary<string, string>();
            Dictionary<string, int> groupStart = new Dictionary<string, int>();
            Dictionary<string, int> minDepth = new Dictionary<string, int>();
            foreach (BayInfo bay in bays)
            {
                occ[bay.Id] = Occupancy(bay, null);
                frontier[bay.Id] = 0; lastGroup[bay.Id] = null; groupStart[bay.Id] = 0; minDepth[bay.Id] = 0;
                // cargo already aboard that isn't being packed now
                int bestEnd = -1;
                foreach (CargoBox b in OnShip().Where(x => x.Bay == bay.Id && !set.Contains(x)))
                {
                    int[] d = DimsIn(bay, b.Size, b.Rot);
                    int dp = DepthPos(bay, new[] { b.X, b.Y, b.Z }, d);
                    int end = dp + d[ExitIdx(bay)];
                    if (end > bestEnd) { bestEnd = end; lastGroup[bay.Id] = group != null ? group(b) : null; }
                }
                frontier[bay.Id] = Math.Max(0, bestEnd);
                if (lastGroup[bay.Id] != null)
                    groupStart[bay.Id] = OnShip().Where(x => x.Bay == bay.Id && !set.Contains(x) && group(x) == lastGroup[bay.Id])
                                                 .Min(x => DepthPos(bay, new[] { x.X, x.Y, x.Z }, DimsIn(bay, x.Size, x.Rot)));
            }

            IEnumerable<CargoBox> order;
            if (group == null)
                order = boxes.OrderByDescending(rank).ThenByDescending(b => b.Size).ThenBy(b => b.Id);
            else
                order = boxes.GroupBy(group)
                             .Select(g => new { g.Key, Rank = g.Min(rank), Items = g.OrderBy(b => sub != null ? sub(b) : "").ThenByDescending(b => b.Size).ThenBy(b => b.Id).ToList() })
                             .OrderByDescending(g => g.Rank).ThenBy(g => g.Key)
                             .SelectMany(g => g.Items).ToList();

            int placed = 0;
            string cur = null; bool first = true;
            foreach (CargoBox box in order)
            {
                string key = group != null ? group(box) : null;
                if (group != null && (first || key != cur))
                {
                    cur = key; first = false;
                    foreach (BayInfo bay in bays)
                    {
                        int f = frontier[bay.Id];
                        if (gap <= 0 || f == 0) minDepth[bay.Id] = 0;
                        else if (lastGroup[bay.Id] == key) minDepth[bay.Id] = groupStart[bay.Id];
                        else minDepth[bay.Id] = f + gap;
                    }
                }
                bool done = false;
                foreach (BayInfo bay in bays)
                {
                    if (bay.MaxSize > 0 && box.Size > bay.MaxSize) continue;
                    Placement best = new Placement(); long bestScore = long.MaxValue;
                    bool[,,] o = occ[bay.Id];
                    int[] s = bay.Size();
                    int g = bay.GravityAxis(), sign = bay.GravitySign();
                    int e = ExitIdx(bay);
                    int a1, a2; Lateral(g, out a1, out a2);
                    int lat = a1 == e ? a2 : a1;
                    int md = minDepth[bay.Id];
                    foreach (bool rot in new[] { false, true })
                    {
                        int[] d = DimsIn(bay, box.Size, rot);
                        if (rot && d[a1] == d[a2]) continue;
                        if (d[0] > s[0] || d[1] > s[1] || d[2] > s[2]) continue;
                        int[] p = new int[3];
                        for (p[0] = 0; p[0] + d[0] <= s[0]; p[0]++)
                            for (p[1] = 0; p[1] + d[1] <= s[1]; p[1]++)
                                for (p[2] = 0; p[2] + d[2] <= s[2]; p[2]++)
                                {
                                    long depth = DepthPos(bay, p, d);
                                    if (depth < md) continue;
                                    if (!Free(o, p, d)) continue;
                                    if (Support(bay, o, p, d) < 0.999) continue;
                                    long height = sign < 0 ? p[g] : s[g] - (p[g] + d[g]);
                                    long reach = Math.Max(frontier[bay.Id], depth + d[e]);   // how far along the bay cargo now reaches
                                    long score = ((reach * 1000 + depth) * 1000 + p[lat]) * 1000 + height;
                                    if (score < bestScore) { bestScore = score; best = new Placement { Bay = bay, P = (int[])p.Clone(), D = d, Rot = rot, Valid = true }; }
                                }
                    }
                    if (best.Valid)
                    {
                        Place(box, best);
                        Mark(o, bay, best.P, best.D, true);
                        int dp = DepthPos(bay, best.P, best.D);
                        frontier[bay.Id] = Math.Max(frontier[bay.Id], dp + best.D[e]);
                        if (group != null)
                        {
                            if (lastGroup[bay.Id] != key) { lastGroup[bay.Id] = key; groupStart[bay.Id] = dp; }
                            else groupStart[bay.Id] = Math.Min(groupStart[bay.Id], dp);
                        }
                        placed++; done = true;
                        break;
                    }
                }
                if (!done && box.State == BoxState.OnShip) box.State = BoxState.Manifest;
            }
            return placed;
        }

        // axis cargo is unloaded along (never the gravity axis)
        static int ExitIdx(BayInfo bay)
        {
            int g = bay.GravityAxis();
            int e = bay.ExitAxisIndex(); if (e == g) e = g == 2 ? 0 : 2;
            return e;
        }

        // distance (cells) of a box's far side from the deep end of the bay
        static int DepthPos(BayInfo bay, int[] p, int[] d)
        {
            int e = ExitIdx(bay);
            int[] s = bay.Size();
            return bay.ExitDirection() > 0 ? p[e] : s[e] - (p[e] + d[e]);
        }

        // Free grid cells left (not counting the off-grid area).
        public int FreeCells()
        {
            HashSet<string> ids = new HashSet<string>(Bays.Where(b => b.AutoLoad && !b.OffGrid).Select(b => b.Id));
            return Capacity() - OnShip().Where(b => ids.Contains(b.Bay)).Sum(b => b.Size);
        }

        public CargoBox NewBox(int size, Contract c, int legIndex, int batch)
        {
            CargoLeg l = legIndex >= 0 && legIndex < c.Legs.Count ? c.Legs[legIndex] : null;
            CargoBox b = new CargoBox
            {
                Id = State.NextId++, Size = size, ContractNumber = c.Number, LegIndex = legIndex,
                Material = l != null ? l.Material : "", Pickup = l != null ? l.Pickup : "", Dropoff = l != null ? l.Dropoff : "",
                State = BoxState.Manifest, Batch = batch
            };
            State.Boxes.Add(b);
            return b;
        }
    }
}
