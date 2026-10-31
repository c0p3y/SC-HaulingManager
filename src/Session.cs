// SC Hauling Manager - C0P3Y | Icarus Interstellar Inc.
// Session timer + income log. A session starts by itself when you take a contract, runs while you haul, can be paused
// (breaks don't count toward aUEC per hour), and stops by itself when every contract is delivered. Everything is
// kept in sessions.json so you can look back at earlier sessions.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HaulingManager
{
    public class SessionEvent
    {
        public string Time { get; set; }       // local "yyyy-MM-dd HH:mm:ss"
        public double Active { get; set; }     // active (unpaused) seconds into the session when it happened
        public string Kind { get; set; }       // start, pause, resume, taken, pickup, drop, complete, removed, finished, end
        public string Uid { get; set; }        // contract
        public int Contract { get; set; }      // contract number at the time
        public string Text { get; set; }
        public long Reward { get; set; }
        public int Scu { get; set; }
        public int Boxes { get; set; }
        public int Record { get; set; }        // route step record id (pickup / drop)
        public bool Undone { get; set; }       // un-ticked / un-completed later: not counted
        public SessionEvent() { Time = ""; Kind = ""; Uid = ""; Text = ""; }
    }

    public class WorkSession
    {
        public string Started { get; set; }
        public string Ended { get; set; }          // set when you end the session (it moves to the history)
        public string RunningSince { get; set; }   // empty = timer stopped (paused or finished)
        public double ActiveSeconds { get; set; }  // banked active time (not counting the current running stretch)
        public bool Paused { get; set; }
        public int BaselineRecord { get; set; }    // route steps before this id belong to an earlier session
        public int MaxAboard { get; set; }
        public List<string> DoneAtStart { get; set; }
        public List<SessionEvent> Events { get; set; }
        public WorkSession() { Started = ""; Ended = ""; RunningSince = ""; DoneAtStart = new List<string>(); Events = new List<SessionEvent>(); }

        public bool Running { get { return !string.IsNullOrEmpty(RunningSince); } }
    }

    public class SessionStore
    {
        public WorkSession Current { get; set; }
        public List<WorkSession> Past { get; set; }
        public List<string> Known { get; set; }    // contracts already seen (so they're not "taken" twice)
        public int SeenRecord { get; set; }        // route steps from this id on haven't been looked at yet
        public SessionStore() { Past = new List<WorkSession>(); }
    }

    public class SessionStats
    {
        public TimeSpan Total, Active, Paused;
        public long Earned;
        public int Taken, Completed, Pickups, Drops, Boxes, ScuDelivered, MaxAboard;
        public double PerHour;
        public TimeSpan AvgLeg, LongestLeg;
    }

    public static class Sessions
    {
        public const string File = "sessions.json";
        const string Fmt = "yyyy-MM-dd HH:mm:ss";

        public static string Stamp(DateTime t) { return t.ToString(Fmt, CultureInfo.InvariantCulture); }
        public static DateTime Parse(string s)
        {
            DateTime t;
            return DateTime.TryParseExact(s ?? "", Fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out t) ? t : DateTime.Now;
        }

        public static double ActiveNow(WorkSession s)
        {
            double a = s.ActiveSeconds;
            if (s.Running) a += Math.Max(0, (DateTime.Now - Parse(s.RunningSince)).TotalSeconds);
            return a;
        }

        public static string Clock(TimeSpan t)
        {
            return ((int)t.TotalHours).ToString("00") + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00");
        }

        public static string Short(TimeSpan t)
        {
            if (t.TotalHours >= 1) return (int)t.TotalHours + "h " + t.Minutes.ToString("00") + "m";
            if (t.TotalMinutes >= 1) return (int)t.TotalMinutes + "m " + t.Seconds.ToString("00") + "s";
            return t.Seconds + "s";
        }

        public static string Money(long v) { return v.ToString("N0", CultureInfo.InvariantCulture); }

        public static SessionEvent Add(WorkSession s, string kind, string text)
        {
            SessionEvent e = new SessionEvent { Time = Stamp(DateTime.Now), Active = ActiveNow(s), Kind = kind, Text = text ?? "" };
            s.Events.Add(e);
            return e;
        }

        public static WorkSession Start(AppData data, string why)
        {
            WorkSession s = new WorkSession { Started = Stamp(DateTime.Now), RunningSince = Stamp(DateTime.Now), BaselineRecord = data.NextRecord };
            s.DoneAtStart = data.Contracts.Where(c => c.IsDone()).Select(c => c.Uid).ToList();
            Add(s, "start", why);
            return s;
        }

        public static void Pause(WorkSession s, string why)
        {
            if (!s.Running) return;
            Add(s, "pause", why);
            s.ActiveSeconds = ActiveNow(s); s.RunningSince = ""; s.Paused = true;
        }

        public static void Stop(WorkSession s, string why)
        {
            if (!s.Running) return;
            Add(s, "finished", why);
            s.ActiveSeconds = ActiveNow(s); s.RunningSince = ""; s.Paused = false;
        }

        public static void Resume(WorkSession s, string why)
        {
            if (s.Running) return;
            s.RunningSince = Stamp(DateTime.Now); s.Paused = false;
            Add(s, "resume", why);
        }

        public static SessionStats Stats(WorkSession s)
        {
            SessionStats st = new SessionStats();
            DateTime start = Parse(s.Started), end = string.IsNullOrEmpty(s.Ended) ? (s.Running ? DateTime.Now : LastTime(s)) : Parse(s.Ended);
            st.Total = end > start ? end - start : TimeSpan.Zero;
            st.Active = TimeSpan.FromSeconds(ActiveNow(s));
            if (st.Active > st.Total) st.Total = st.Active;
            st.Paused = st.Total - st.Active;
            List<SessionEvent> ev = s.Events.Where(e => !e.Undone).ToList();
            st.Taken = ev.Count(e => e.Kind == "taken");
            st.Completed = ev.Count(e => e.Kind == "complete");
            st.Earned = ev.Where(e => e.Kind == "complete").Sum(e => e.Reward);
            st.Pickups = ev.Count(e => e.Kind == "pickup");
            st.Drops = ev.Count(e => e.Kind == "drop");
            st.Boxes = ev.Where(e => e.Kind == "drop").Sum(e => e.Boxes);
            int byDrops = ev.Where(e => e.Kind == "drop").Sum(e => e.Scu);
            // contracts marked complete by hand never had their drop-offs ticked: count what wasn't
            int extra = ev.Where(e => e.Kind == "complete").Sum(c => Math.Max(0, c.Scu - ev.Where(d => d.Kind == "drop" && d.Uid == c.Uid).Sum(d => d.Scu)));
            st.ScuDelivered = byDrops + extra;
            st.MaxAboard = s.MaxAboard;
            st.PerHour = st.Active.TotalHours > 0.0001 ? st.Earned / st.Active.TotalHours : 0;
            List<double> legs = Legs(s).Select(l => l.Value).ToList();
            if (legs.Count > 0) { st.AvgLeg = TimeSpan.FromSeconds(legs.Average()); st.LongestLeg = TimeSpan.FromSeconds(legs.Max()); }
            return st;
        }

        // active time from the previous ticked step (or the session start) to each ticked step
        public static List<KeyValuePair<SessionEvent, double>> Legs(WorkSession s)
        {
            List<KeyValuePair<SessionEvent, double>> r = new List<KeyValuePair<SessionEvent, double>>();
            double prev = 0;
            foreach (SessionEvent e in s.Events.Where(e => !e.Undone && (e.Kind == "pickup" || e.Kind == "drop")))
            {
                r.Add(new KeyValuePair<SessionEvent, double>(e, Math.Max(0, e.Active - prev)));
                prev = e.Active;
            }
            return r;
        }

        static DateTime LastTime(WorkSession s)
        {
            return s.Events.Count > 0 ? Parse(s.Events[s.Events.Count - 1].Time) : Parse(s.Started);
        }

        // ---------------------------------------------------------------- following what happens in the app
        // Called after every save: compares the contracts / route steps with what the session has logged.
        // Returns a message worth showing (session started, all delivered...), or null.
        public static string Sync(SessionStore store, AppData data, Func<int, int> boxesOfRecord)
        {
            string notice = null;
            foreach (Contract c in data.Contracts) if (string.IsNullOrEmpty(c.Uid)) c.Uid = NewUid();
            if (store.Known == null)   // first run with the tracker: what's already loaded isn't "new"
            {
                store.Known = data.Contracts.Select(c => c.Uid).ToList();
                store.SeenRecord = data.NextRecord;
                return null;
            }
            HashSet<string> known = new HashSet<string>(store.Known);
            List<Contract> fresh = data.Contracts.Where(c => !known.Contains(c.Uid)).ToList();
            WorkSession s = store.Current;

            if (fresh.Count > 0)
            {
                // finished a while ago: that session is over, this is a new one
                if (s != null && !s.Running && !s.Paused && (DateTime.Now - LastTime(s)).TotalMinutes > 30) { End(store); s = null; }
                if (s == null) { s = store.Current = Start(data, "Timer started - contract received"); notice = "Session timer started."; }
                else if (!s.Running) { Resume(s, s.Paused ? "Resumed - new contract" : "New contract - timer running again"); notice = "Session timer running."; }
                foreach (Contract c in fresh)
                {
                    SessionEvent e = Add(s, "taken", c.Title);
                    e.Uid = c.Uid; e.Contract = c.Number; e.Reward = c.Reward; e.Scu = c.TotalScu();
                    store.Known.Add(c.Uid);
                }
            }
            if (s == null && data.Journal.Any(r => r.Id >= store.SeenRecord))
            {
                // ticked a step with no session open (e.g. after ending one mid-route): start one from here
                s = store.Current = Start(data, "Timer started - route step ticked");
                s.BaselineRecord = store.SeenRecord;
                notice = "Session timer started.";
            }
            store.SeenRecord = Math.Max(store.SeenRecord, data.NextRecord);
            if (s == null) return notice;

            Dictionary<string, Contract> byUid = data.Contracts.GroupBy(c => c.Uid).ToDictionary(g => g.Key, g => g.First());
            // rewards entered later
            foreach (SessionEvent e in s.Events.Where(e => (e.Kind == "taken" || e.Kind == "complete") && byUid.ContainsKey(e.Uid)))
                if (byUid[e.Uid].Reward != e.Reward && byUid[e.Uid].Reward > 0) e.Reward = byUid[e.Uid].Reward;

            // route steps ticked / un-ticked
            Dictionary<int, SessionEvent> logged = s.Events.Where(e => e.Record > 0).GroupBy(e => e.Record).ToDictionary(g => g.Key, g => g.Last());
            Dictionary<int, StepRecord> journal = data.Journal.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());
            Dictionary<int, Contract> byNumber = data.Contracts.GroupBy(c => c.Number).ToDictionary(g => g.Key, g => g.First());
            foreach (StepRecord r in data.Journal.Where(r => r.Id >= s.BaselineRecord && !logged.ContainsKey(r.Id)).OrderBy(r => r.Id))
            {
                Contract c; byNumber.TryGetValue(r.ContractNumber, out c);
                if (!s.Running) { Resume(s, s.Paused ? "Resumed - route step ticked" : "Route step ticked - timer running again"); notice = "Session timer running."; }
                SessionEvent e = Add(s, r.IsPickup ? "pickup" : "drop", (r.IsPickup ? "Picked up " : "Delivered ") + r.Scu() + " SCU " + r.Material + (r.IsPickup ? " at " : " to ") + r.Location);
                e.Record = r.Id; e.Uid = c != null ? c.Uid : ""; e.Contract = r.ContractNumber; e.Scu = r.Scu();
                e.Boxes = r.IsPickup ? 0 : boxesOfRecord(r.Id);
                if (!r.IsPickup && e.Boxes == 0) e.Boxes = Boxes.Parse(r.Containers).Values.Sum();
            }
            foreach (KeyValuePair<int, SessionEvent> kv in logged)
            {
                bool gone = !journal.ContainsKey(kv.Key);
                // a step that disappears because its contract was cleared or removed still happened; an un-tick didn't
                if (gone && !kv.Value.Undone && byUid.ContainsKey(kv.Value.Uid)) kv.Value.Undone = true;
                else if (!gone && kv.Value.Undone) kv.Value.Undone = false;
            }

            // contracts completed / un-completed
            HashSet<string> doneAtStart = new HashSet<string>(s.DoneAtStart ?? new List<string>());
            foreach (Contract c in data.Contracts)
            {
                SessionEvent done = s.Events.LastOrDefault(e => e.Kind == "complete" && e.Uid == c.Uid);
                if (c.IsDone() && !doneAtStart.Contains(c.Uid))
                {
                    if (done == null)
                    {
                        done = Add(s, "complete", c.Title);
                        done.Uid = c.Uid; done.Contract = c.Number; done.Reward = c.Reward; done.Scu = c.TotalScu();
                    }
                    else if (done.Undone) done.Undone = false;
                }
                else if (!c.IsDone() && done != null && !done.Undone) done.Undone = true;
            }

            // most cargo aboard at once
            int aboard = data.Contracts.Sum(c => c.Legs.Sum(l => Math.Max(0, l.PickedClamped() - l.Delivered)));
            if (aboard > s.MaxAboard) s.MaxAboard = aboard;

            // everything delivered: the clock stops by itself
            bool anyWork = s.Events.Any(e => !e.Undone && (e.Kind == "taken" || e.Kind == "pickup" || e.Kind == "drop" || e.Kind == "complete"));
            if (s.Running && anyWork && data.Contracts.All(c => c.IsDone()))
            {
                Stop(s, "All contracts delivered - timer stopped");
                SessionStats st = Stats(s);
                notice = "All contracts delivered - session timer stopped. " + Money(st.Earned) + " aUEC in " + Short(st.Active) + ". Press Session to see the summary.";
            }
            return notice;
        }

        public static void End(SessionStore store)
        {
            WorkSession s = store.Current;
            if (s == null) return;
            if (s.Running) { s.ActiveSeconds = ActiveNow(s); s.RunningSince = ""; }
            s.Ended = Stamp(DateTime.Now);
            Add(s, "end", "Session ended");
            store.Past.Insert(0, s);
            store.Current = null;
        }

        public static string NewUid() { return Guid.NewGuid().ToString("N").Substring(0, 12); }

        public static string Describe(SessionEvent e)
        {
            switch (e.Kind)
            {
                case "taken": return "Contract taken";
                case "complete": return "Contract complete";
                case "pickup": return "Pick up";
                case "drop": return "Drop off";
                case "pause": return "Paused";
                case "resume": return "Resumed";
                case "start": return "Started";
                case "finished": return "Timer stopped";
                case "end": return "Ended";
                default: return e.Kind;
            }
        }

        // ---------------------------------------------------------------- CSV
        public static string Csv(WorkSession s)
        {
            Func<string, string> q = v => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            SessionStats st = Stats(s);
            sb.AppendLine("Session," + q(s.Started) + ",to," + q(string.IsNullOrEmpty(s.Ended) ? "(open)" : s.Ended));
            sb.AppendLine("Total time," + Clock(st.Total) + ",Active," + Clock(st.Active) + ",Paused," + Clock(st.Paused));
            sb.AppendLine("aUEC earned," + st.Earned + ",aUEC per hour," + Math.Round(st.PerHour));
            sb.AppendLine("Contracts taken," + st.Taken + ",Completed," + st.Completed + ",Boxes delivered," + st.Boxes + ",SCU delivered," + st.ScuDelivered + ",Max SCU aboard," + st.MaxAboard);
            sb.AppendLine();
            sb.AppendLine("Time,Active,Leg,Event,Contract,Detail,SCU,Boxes,aUEC,Running aUEC,Counted");
            Dictionary<SessionEvent, double> legs = Legs(s).ToDictionary(k => k.Key, k => k.Value);
            long running = 0;
            foreach (SessionEvent e in s.Events)
            {
                if (e.Kind == "complete" && !e.Undone) running += e.Reward;
                double leg; bool isLeg = legs.TryGetValue(e, out leg);
                sb.AppendLine(string.Join(",", new[] {
                    q(e.Time), Clock(TimeSpan.FromSeconds(e.Active)), isLeg ? Clock(TimeSpan.FromSeconds(leg)) : "", q(Describe(e)),
                    e.Contract > 0 ? "#" + e.Contract : "", q(e.Text), e.Scu > 0 ? e.Scu.ToString() : "", e.Boxes > 0 ? e.Boxes.ToString() : "",
                    e.Kind == "complete" ? e.Reward.ToString() : "", e.Kind == "complete" ? running.ToString() : "", e.Undone ? "no (undone)" : "yes" }));
            }
            return sb.ToString();
        }
    }
}
