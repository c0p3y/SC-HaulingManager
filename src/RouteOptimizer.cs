// SC Hauling Manager - C0P3Y | Icarus Interstellar Inc.
// Pickup-and-delivery route planner with cargo capacity.
// Beam search over "which location next"; at each stop all cargo for that stop is dropped and
// cargo is loaded using a few different loading strategies (largest first, smallest first,
// "same destination as what I already carry", whole-legs-only). Legs larger than the hold are split.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HaulingManager
{
    public class LegRef
    {
        public Contract Contract;
        public CargoLeg Leg;
        public LegRef(Contract c, CargoLeg l) { Contract = c; Leg = l; }
    }

    public class RouteAction
    {
        public bool IsPickup;
        public int Scu;
        public LegRef Ref;
        public bool Applied;

        public string Describe()
        {
            return (IsPickup ? "Pick up " : "Drop off ") + Scu + " SCU " + Ref.Leg.Material + "  [" + Ref.Contract.Label() + "]" +
                   (IsPickup ? "  -> " + Ref.Leg.Dropoff : "");
        }
    }

    public class RouteStop
    {
        public ResolvedLocation Location;
        public List<RouteAction> Actions = new List<RouteAction>();
        public int LoadAfter;
        public double Travel;
    }

    public class RouteResult
    {
        public List<RouteStop> Stops = new List<RouteStop>();
        public double TotalCost;
        public int PeakLoad;
        public int Capacity;
        public List<string> Messages = new List<string>();
        public int NaiveStops;
        public double NaiveCost;
    }

    public static class RouteOptimizer
    {
        // A unit is (part of) a cargo leg that is moved in one go. Legs bigger than the hold are split.
        class Unit { public int Leg; public int Qty; public int From; public int To; public bool OnBoard; }
        class Ev { public int U; public bool Pick; public int Loc; }

        const double StopPenalty = 0.01;   // prefers fewer stops when travel is equal
        const int START = -1, NONE = -2;

        class Ctx
        {
            public double[,] Cost; public int Start; public int Cap; public int InitLoad; public List<Unit> Units;
            public double C(int a, int b)
            {
                if (b == NONE) return 0;
                if (a == START) return Start < 0 ? StopPenalty : Cost[Start, b] + (Start != b ? StopPenalty : 0);
                return Cost[a, b] + (a != b ? StopPenalty : 0);
            }
        }

        public static RouteResult Optimize(List<LegRef> legsIn, int capacity, string startLocation, LocationDb db, int effort)
        {
            RouteResult res = new RouteResult();
            res.Capacity = capacity;
            List<ResolvedLocation> locs = new List<ResolvedLocation>();
            Dictionary<string, int> index = new Dictionary<string, int>();
            Func<string, int> idx = delegate(string s)
            {
                ResolvedLocation r = db.Resolve(s);
                int i;
                if (!index.TryGetValue(r.Key, out i)) { i = locs.Count; locs.Add(r); index[r.Key] = i; }
                else if (r.Display.Length > locs[i].Display.Length) locs[i] = r;   // show the most complete spelling
                return i;
            };

            List<LegRef> legs = new List<LegRef>();
            List<Unit> units = new List<Unit>();
            int initLoad = 0;
            foreach (LegRef lr in legsIn)
            {
                CargoLeg l = lr.Leg;
                if (l.IsDone()) continue;
                if (string.IsNullOrWhiteSpace(l.Pickup) || string.IsNullOrWhiteSpace(l.Dropoff) || l.Scu <= 0)
                {
                    res.Messages.Add("Skipped " + lr.Contract.Label() + " " + l.Material + ": missing pickup, drop-off or SCU (edit the contract).");
                    continue;
                }
                int f = idx(l.Pickup), t = idx(l.Dropoff);
                if (f == t) { res.Messages.Add("Skipped " + lr.Contract.Label() + " " + l.Material + ": pickup and drop-off are the same place."); continue; }
                int li = legs.Count; legs.Add(lr);
                int on = l.OnBoard(), wait = l.WaitingAtPickup();
                if (on > 0) { units.Add(new Unit { Leg = li, Qty = on, From = f, To = t, OnBoard = true }); initLoad += on; }
                while (wait > 0)
                {
                    int q = capacity > 0 ? Math.Min(capacity, wait) : wait;
                    units.Add(new Unit { Leg = li, Qty = q, From = f, To = t });
                    wait -= q;
                }
            }
            if (units.Count == 0) { res.Messages.Add("Nothing left to haul."); return res; }
            if (capacity > 0 && initLoad > capacity)
                res.Messages.Add("Cargo already on board (" + initLoad + " SCU) is more than the ship capacity (" + capacity + " SCU).");

            int startIdx = string.IsNullOrWhiteSpace(startLocation) ? -1 : idx(startLocation);
            int n = locs.Count;
            Ctx ctx = new Ctx();
            ctx.Cost = new double[n, n];
            for (int a = 0; a < n; a++) for (int b = 0; b < n; b++) ctx.Cost[a, b] = db.Cost(locs[a], locs[b]);
            ctx.Start = startIdx; ctx.Cap = capacity > 0 ? Math.Max(capacity, initLoad) : 0; ctx.InitLoad = initLoad; ctx.Units = units;

            // Several construction orders + local search; keep the cheapest.
            int restarts = Math.Max(4, Math.Min(effort, units.Count <= 20 ? 60 : (units.Count <= 50 ? 20 : 6)));
            Random rnd = new Random(12345);
            List<Ev> best = null; double bestCost = double.MaxValue;
            for (int r = 0; r < restarts; r++)
            {
                List<int> order = Enumerable.Range(0, units.Count).ToList();
                if (r == 0) order = order.OrderByDescending(u => units[u].OnBoard ? 1 : 0).ThenByDescending(u => units[u].Qty).ToList();
                else if (r == 1) order = order.OrderBy(u => units[u].Qty).ToList();
                else order = order.OrderBy(u => rnd.Next()).ToList();

                List<Ev> seq = new List<Ev>();
                foreach (int u in order) Insert(ctx, seq, u);
                LocalSearch(ctx, seq, rnd);
                double c = Total(ctx, seq);
                if (c < bestCost - 1e-9) { bestCost = c; best = seq; }
            }

            BuildStops(res, best, units, legs, locs, ctx);
            Naive(res, legs, units, ctx, startIdx);
            return res;
        }

        static double Total(Ctx ctx, List<Ev> seq)
        {
            double t = 0; int prev = START;
            foreach (Ev e in seq) { t += ctx.C(prev, e.Loc); prev = e.Loc; }
            return t;
        }

        static int[] LoadBefore(Ctx ctx, List<Ev> seq)
        {
            int[] lb = new int[seq.Count + 1];
            int load = ctx.InitLoad;
            for (int k = 0; k < seq.Count; k++)
            {
                lb[k] = load;
                Ev e = seq[k];
                load += e.Pick ? ctx.Units[e.U].Qty : -ctx.Units[e.U].Qty;
            }
            lb[seq.Count] = load;
            return lb;
        }

        // Cheapest feasible insertion of a unit (pickup at i, drop at j >= i).
        static void Insert(Ctx ctx, List<Ev> seq, int u)
        {
            Unit un = ctx.Units[u];
            int n = seq.Count;
            int[] loc = new int[n];
            for (int k = 0; k < n; k++) loc[k] = seq[k].Loc;
            Func<int, int> prevOf = i => i == 0 ? START : loc[i - 1];
            Func<int, int> nextOf = i => i == n ? NONE : loc[i];
            int P = un.From, D = un.To, q = un.Qty;
            double bestDelta = double.MaxValue; int bi = n, bj = n;

            if (un.OnBoard)
            {
                for (int j = 0; j <= n; j++)
                {
                    double d = ctx.C(prevOf(j), D) + ctx.C(D, nextOf(j)) - ctx.C(prevOf(j), nextOf(j));
                    if (d < bestDelta) { bestDelta = d; bj = j; }
                }
                seq.Insert(bj, new Ev { U = u, Pick = false, Loc = D });
                return;
            }

            int[] lb = LoadBefore(ctx, seq);
            for (int i = 0; i <= n; i++)
            {
                if (ctx.Cap > 0 && lb[i] + q > ctx.Cap) continue;
                double dp = ctx.C(prevOf(i), P) + ctx.C(P, nextOf(i)) - ctx.C(prevOf(i), nextOf(i));
                // j == i: pickup immediately followed by drop
                double d0 = ctx.C(prevOf(i), P) + ctx.C(P, D) + ctx.C(D, nextOf(i)) - ctx.C(prevOf(i), nextOf(i));
                if (d0 < bestDelta) { bestDelta = d0; bi = i; bj = i; }
                for (int j = i + 1; j <= n; j++)
                {
                    if (ctx.Cap > 0 && lb[j] + q > ctx.Cap) break;   // hold would overflow while carrying this unit
                    double d = dp + ctx.C(prevOf(j), D) + ctx.C(D, nextOf(j)) - ctx.C(prevOf(j), nextOf(j));
                    if (d < bestDelta) { bestDelta = d; bi = i; bj = j; }
                }
            }
            // insert drop first (higher index) so the pickup index stays valid
            seq.Insert(bj, new Ev { U = u, Pick = false, Loc = D });
            seq.Insert(bi, new Ev { U = u, Pick = true, Loc = P });
        }

        // Remove-and-reinsert each unit until nothing improves. Reinsertion can always restore the
        // original position, so the route never gets worse.
        static void LocalSearch(Ctx ctx, List<Ev> seq, Random rnd)
        {
            int count = ctx.Units.Count;
            for (int pass = 0; pass < 30; pass++)
            {
                bool improved = false;
                double before = Total(ctx, seq);
                foreach (int u in Enumerable.Range(0, count).OrderBy(x => rnd.Next()))
                {
                    List<Ev> backup = new List<Ev>(seq);
                    double old = Total(ctx, seq);
                    seq.RemoveAll(e => e.U == u);
                    Insert(ctx, seq, u);
                    double now = Total(ctx, seq);
                    if (now > old + 1e-9) { seq.Clear(); seq.AddRange(backup); }
                    else if (now < old - 1e-9) improved = true;
                }
                if (!improved || Total(ctx, seq) >= before - 1e-9) break;
            }
        }

        static void BuildStops(RouteResult res, List<Ev> seq, List<Unit> units, List<LegRef> legs, List<ResolvedLocation> locs, Ctx ctx)
        {
            int load = ctx.InitLoad; int prev = START;
            RouteStop stop = null;
            foreach (Ev e in seq)
            {
                if (stop == null || e.Loc != prev)
                {
                    stop = new RouteStop();
                    stop.Location = locs[e.Loc];
                    stop.Travel = prev == START ? (ctx.Start >= 0 ? ctx.Cost[ctx.Start, e.Loc] : 0) : ctx.Cost[prev, e.Loc];
                    res.Stops.Add(stop);
                    res.TotalCost += stop.Travel;
                }
                Unit u = units[e.U];
                RouteAction existing = stop.Actions.FirstOrDefault(a => a.IsPickup == e.Pick && a.Ref == legs[u.Leg]);
                if (existing != null) existing.Scu += u.Qty;
                else stop.Actions.Add(new RouteAction { IsPickup = e.Pick, Scu = u.Qty, Ref = legs[u.Leg] });
                load += e.Pick ? u.Qty : -u.Qty;
                stop.LoadAfter = load;
                prev = e.Loc;
            }
            // within a stop: drop off first, then pick up (that is what you do in game, and it never overflows the hold)
            foreach (RouteStop s in res.Stops) s.Actions = s.Actions.OrderBy(a => a.IsPickup ? 1 : 0).ToList();
            res.PeakLoad = ctx.InitLoad;
            foreach (RouteStop s in res.Stops) res.PeakLoad = Math.Max(res.PeakLoad, s.LoadAfter);
        }

        // "Do each contract on its own, in list order" - shown so you can see what the planner saves.
        static void Naive(RouteResult res, List<LegRef> legs, List<Unit> units, Ctx ctx, int startIdx)
        {
            List<int> seq = new List<int>();
            foreach (Contract c in legs.Select(x => x.Contract).Distinct())
            {
                List<int> mine = Enumerable.Range(0, units.Count).Where(i => legs[units[i].Leg].Contract == c).ToList();
                int scu = mine.Sum(i => units[i].Qty);
                if (ctx.Cap <= 0 || scu <= ctx.Cap)
                {
                    foreach (int i in mine) if (!units[i].OnBoard) seq.Add(units[i].From);
                    foreach (int i in mine) seq.Add(units[i].To);
                }
                else foreach (int i in mine) { if (!units[i].OnBoard) seq.Add(units[i].From); seq.Add(units[i].To); }
            }
            int stops = 0; double total = 0; int prev = startIdx;
            foreach (int x in seq)
            {
                if (x == prev) continue;
                if (prev >= 0) total += ctx.Cost[prev, x];
                stops++; prev = x;
            }
            res.NaiveStops = stops; res.NaiveCost = total;
        }
    }
}
