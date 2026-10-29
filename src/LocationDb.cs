// SC Hauling Manager - C0P3Y | Icarus Interstellar Inc.
// Location knowledge used for route costs and for telling stations from planetary outposts.
// Stored in locations.json next to the exe so it can be edited as the game changes.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HaulingManager
{
    public enum LocKind { Unknown, Station, City, DistributionCenter, Outpost }

    public class BodyInfo
    {
        public string Name { get; set; }
        public string Group { get; set; }     // the planet this body belongs to (planet = itself, moon = its planet)
        public string System { get; set; }
        public List<string> Aliases { get; set; }
        public BodyInfo() { Name = ""; Group = ""; System = ""; Aliases = new List<string>(); }
        public BodyInfo(string name, string group, string system, params string[] aliases)
        {
            Name = name; Group = group; System = system; Aliases = new List<string>(aliases);
        }
    }

    public class LocationHint
    {
        public string Match { get; set; }     // case-insensitive text found in the location name
        public string Body { get; set; }
        public string Kind { get; set; }      // Station, City, DistributionCenter, Outpost or empty
        public string Key { get; set; }       // which place this is (several hints can point to one place); empty = the Match text
        public string Name { get; set; }      // name shown in the Start at list; empty = not listed
        public LocationHint() { Match = ""; Body = ""; Kind = ""; Key = ""; Name = ""; }
        public LocationHint(string match, string body, string kind) { Match = match; Body = body; Kind = kind; }
    }

    public class TravelCosts
    {
        public double SameLocation { get; set; }
        public double SameBody { get; set; }
        public double SamePlanetGroup { get; set; }
        public double SameSystem { get; set; }
        public double OtherSystem { get; set; }
        public double Unknown { get; set; }
        public TravelCosts()
        {
            SameLocation = 0; SameBody = 1; SamePlanetGroup = 2; SameSystem = 5; OtherSystem = 20; Unknown = 5;
        }
    }

    // One place from the location catalog (built-in snapshot or a UEX update).
    public class Place
    {
        public string Name { get; set; }    // name shown in lists, e.g. "CRU-L4 Shallow Fields Station"
        public string Full { get; set; }    // full name when Name is a nickname ("Green Imperial Housing Exchange")
        public string Short { get; set; }   // short code, e.g. "CRU-L4"
        public string System { get; set; }
        public string Body { get; set; }
        public string Kind { get; set; }    // Station, City, Outpost, DistributionCenter
        public Place() { Name = ""; Full = ""; Short = ""; System = ""; Body = ""; Kind = ""; }

        public class File { public string Source { get; set; } public List<Place> Places { get; set; } }

        public static List<Place> LoadBuiltIn()
        {
            try
            {
                global::System.Reflection.Assembly asm = global::System.Reflection.Assembly.GetExecutingAssembly();
                string res = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("places.json", global::System.StringComparison.OrdinalIgnoreCase));
                if (res == null) return new List<Place>();
                using (Stream st = asm.GetManifestResourceStream(res))
                using (StreamReader r = new StreamReader(st))
                {
                    File f = Json.Deserialize<File>(r.ReadToEnd());
                    return f != null && f.Places != null ? f.Places : new List<Place>();
                }
            }
            catch { return new List<Place>(); }
        }
    }

    public class LocationData
    {
        public int DataVersion { get; set; }
        public string Notes { get; set; }
        public TravelCosts Costs { get; set; }
        public List<BodyInfo> Bodies { get; set; }
        public List<LocationHint> Locations { get; set; }
        public LocationData()
        {
            Notes = ""; Costs = new TravelCosts(); Bodies = new List<BodyInfo>(); Locations = new List<LocationHint>();
        }
    }

    public class ResolvedLocation
    {
        public string Key = "";
        public string Display = "";
        public BodyInfo Body;   // null when unknown
        public LocKind Kind;

        public string BodyText()
        {
            if (Body == null) return "unknown body";
            if (Body.Group == Body.Name) return Body.Name + ", " + Body.System;
            return Body.Name + " (" + Body.Group + "), " + Body.System;
        }

        public bool IsOutpost(bool countDistributionCenters)
        {
            return Kind == LocKind.Outpost || (countDistributionCenters && Kind == LocKind.DistributionCenter);
        }

        public static string KindText(LocKind k)
        {
            switch (k)
            {
                case LocKind.Station: return "Station";
                case LocKind.City: return "City";
                case LocKind.DistributionCenter: return "Outpost (Dist. Center)";
                case LocKind.Outpost: return "Planetary Outpost";
                default: return "Unknown";
            }
        }
    }

    public class LocationDb
    {
        public const int CurrentDataVersion = 3;
        public LocationData Data;
        readonly Dictionary<string, ResolvedLocation> cache = new Dictionary<string, ResolvedLocation>();

        static readonly Regex BodyPhrase = new Regex(@"\b(?:above|on|near|orbiting|around|over|in orbit of|lagrange of|at)\s+(?<b>[A-Za-z0-9][A-Za-z0-9\-' ]*)$", RegexOptions.IgnoreCase);
        static readonly Regex StationWords = new Regex(@"\b(lagrange|station|harbou?r|gateway|platform|spaceport|rest stop|r&r|refuel(l)?ing|service station|[A-Z]{3}-L\d)\b", RegexOptions.IgnoreCase);
        static readonly Regex OrbitWords = new Regex(@"\b(above|orbiting|in orbit)\b", RegexOptions.IgnoreCase);
        static readonly Regex DistCenter = new Regex(@"\b(distribution\s+cent(er|re)|DC\b)", RegexOptions.IgnoreCase);
        static readonly Regex SurfaceWords = new Regex(@"\bon\s+\S+", RegexOptions.IgnoreCase);

        public LocationDb(LocationData data) { Data = data; }
        public void ClearCache() { cache.Clear(); }

        public static LocationDb LoadOrCreate()
        {
            string p = Path.Combine(Storage.BaseDir, "locations.json");
            LocationData d = null;
            if (File.Exists(p)) d = Storage.Load<LocationData>("locations.json");
            bool save = false;
            if (d == null || d.Bodies == null || d.Bodies.Count == 0) { d = Defaults(); save = true; }
            if (d.Costs == null) d.Costs = new TravelCosts();
            if (d.Locations == null) d.Locations = new List<LocationHint>();
            if (d.DataVersion < CurrentDataVersion) { Merge(d, Defaults()); save = true; }
            if (save) { try { Storage.Save("locations.json", d); } catch { } }
            return new LocationDb(d);
        }

        // Adds new default bodies / locations to an older (possibly user-edited) file without touching user entries.
        static void Merge(LocationData d, LocationData def)
        {
            foreach (BodyInfo b in def.Bodies)
                if (!d.Bodies.Any(x => string.Equals(x.Name, b.Name, StringComparison.OrdinalIgnoreCase))) d.Bodies.Add(b);
                else
                {
                    BodyInfo mine = d.Bodies.First(x => string.Equals(x.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                    if (mine.Aliases == null) mine.Aliases = new List<string>();
                    foreach (string a in b.Aliases) if (!mine.Aliases.Contains(a)) mine.Aliases.Add(a);
                }
            foreach (LocationHint h in def.Locations)
            {
                LocationHint mine = d.Locations.FirstOrDefault(x => string.Equals(x.Match, h.Match, StringComparison.OrdinalIgnoreCase));
                if (mine == null) d.Locations.Add(h);
                else
                {
                    if (string.IsNullOrEmpty(mine.Kind)) mine.Kind = h.Kind;
                    if (string.IsNullOrEmpty(mine.Body)) mine.Body = h.Body;
                    if (string.IsNullOrEmpty(mine.Key)) mine.Key = h.Key;
                    if (string.IsNullOrEmpty(mine.Name)) mine.Name = h.Name;
                }
            }
            d.Notes = def.Notes;
            d.DataVersion = CurrentDataVersion;
        }

        public static string Clean(string s)
        {
            if (s == null) return "";
            s = s.Replace('¶', ' ');
            s = Regex.Replace(s, @"\s+", " ").Trim();
            s = s.Trim(' ', '.', ',', ';', ':', '-', '•', '|');
            return s;
        }

        public static string KeyOf(string s)
        {
            s = Clean(s).ToLowerInvariant();
            StringBuilder sb = new StringBuilder();
            foreach (char c in s) if (char.IsLetterOrDigit(c) || c == ' ') sb.Append(c);
            return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        }

        BodyInfo FindBody(string name)
        {
            string k = KeyOf(name);
            if (k.Length == 0) return null;
            foreach (BodyInfo b in Data.Bodies)
            {
                if (KeyOf(b.Name) == k) return b;
                if (b.Aliases != null) foreach (string a in b.Aliases) if (KeyOf(a) == k) return b;
            }
            return null;
        }

        LocationHint FindHint(string display)
        {
            LocationHint best = null;
            string low = display.ToLowerInvariant();
            foreach (LocationHint h in Data.Locations)
            {
                if (string.IsNullOrEmpty(h.Match)) continue;
                if (low.Contains(h.Match.ToLowerInvariant()) && (best == null || h.Match.Length > best.Match.Length)) best = h;
            }
            return best;
        }

        // OCR slips ("Gastight", "Rustvilte"): compare the place part of the name with known places, allowing a
        // letter or two to differ.
        LocationHint FindHintFuzzy(string display)
        {
            string head = Regex.Split(display, @"\s+(?:at the|above|on|orbiting|near)\s+", RegexOptions.IgnoreCase)[0].Trim().ToLowerInvariant();
            if (head.Length < 6) return null;
            int allow = head.Length >= 12 ? 2 : 1;
            LocationHint best = null; int bestD = int.MaxValue;
            foreach (LocationHint h in Data.Locations)
            {
                if (string.IsNullOrEmpty(h.Match) || h.Match.Length < 6 || Math.Abs(h.Match.Length - head.Length) > allow) continue;
                int d = Distance(head, h.Match.ToLowerInvariant(), allow);
                if (d <= allow && d < bestD) { bestD = d; best = h; }
            }
            return best;
        }

        static int Distance(string a, string b, int cap)
        {
            int[] prev = new int[b.Length + 1], cur = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                cur[0] = i; int rowMin = cur[0];
                for (int j = 1; j <= b.Length; j++)
                {
                    cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                    rowMin = Math.Min(rowMin, cur[j]);
                }
                if (rowMin > cap) return cap + 1;
                int[] t = prev; prev = cur; cur = t;
            }
            return prev[b.Length];
        }

        static LocKind ParseKind(string s)
        {
            if (string.IsNullOrEmpty(s)) return LocKind.Unknown;
            try { return (LocKind)Enum.Parse(typeof(LocKind), s.Replace(" ", ""), true); }
            catch { return LocKind.Unknown; }
        }

        public ResolvedLocation Resolve(string location)
        {
            string display = ContractParser.NormalizeLocation(location);
            string key = KeyOf(display);
            ResolvedLocation r;
            if (cache.TryGetValue(key, out r)) return r;
            r = new ResolvedLocation();
            r.Key = key; r.Display = display;
            LocationHint hint = FindHint(display) ?? FindHintFuzzy(display);

            // Body: 1) "X above Crusader" / "X on Daymar" / "X at the L4 Lagrange of Pyro V"
            Match m = BodyPhrase.Match(display);
            if (m.Success) r.Body = FindBody(m.Groups["b"].Value);
            // 2) Known station / city names
            if (r.Body == null && hint != null && !string.IsNullOrEmpty(hint.Body)) r.Body = FindBody(hint.Body);
            // 3) Any body name appearing as a whole word (longest first, so "Pyro IV" wins over "Pyro I")
            if (r.Body == null)
            {
                List<KeyValuePair<string, BodyInfo>> names = new List<KeyValuePair<string, BodyInfo>>();
                foreach (BodyInfo b in Data.Bodies)
                {
                    names.Add(new KeyValuePair<string, BodyInfo>(b.Name, b));
                    if (b.Aliases != null) foreach (string a in b.Aliases) names.Add(new KeyValuePair<string, BodyInfo>(a, b));
                }
                foreach (KeyValuePair<string, BodyInfo> kv in names.OrderByDescending(x => x.Key.Length))
                {
                    if (kv.Key.Length < 3) continue;
                    if (Regex.IsMatch(display, @"(?<![A-Za-z0-9])" + Regex.Escape(kv.Key) + @"(?![A-Za-z0-9])", RegexOptions.IgnoreCase))
                    { r.Body = kv.Value; break; }
                }
            }

            // Known stations / cities are one place however the contract spells them
            // ("Ruin Station", "Ruin Station above Pyro VI", OCR-truncated "Ruin Station above Pyro").
            // Prefix hints such as "HUR-L" cover several different stations, so they are not merged.
            if (hint != null && !string.IsNullOrEmpty(hint.Key)) r.Key = "@" + KeyOf(hint.Key);
            else if (hint != null && !hint.Match.EndsWith("-L", StringComparison.OrdinalIgnoreCase))
            {
                LocKind hk = ParseKind(hint.Kind);
                if (hk == LocKind.Station || hk == LocKind.City) r.Key = "@" + KeyOf(hint.Match);
            }

            // Kind: station / city / distribution center / planetary outpost
            if (hint != null && ParseKind(hint.Kind) != LocKind.Unknown) r.Kind = ParseKind(hint.Kind);
            else if (DistCenter.IsMatch(display)) r.Kind = LocKind.DistributionCenter;
            else if (StationWords.IsMatch(display) || OrbitWords.IsMatch(display)) r.Kind = LocKind.Station;
            else if (SurfaceWords.IsMatch(display)) r.Kind = LocKind.Outpost;
            else r.Kind = LocKind.Unknown;

            cache[key] = r;
            return r;
        }

        public double Cost(ResolvedLocation a, ResolvedLocation b)
        {
            TravelCosts c = Data.Costs;
            if (a.Key == b.Key) return c.SameLocation;
            if (a.Body == null || b.Body == null) return c.Unknown;
            if (a.Body == b.Body) return c.SameBody;
            if (string.Equals(a.Body.System, b.Body.System, StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(a.Body.Group, b.Body.Group, StringComparison.OrdinalIgnoreCase)) return c.SamePlanetGroup;
                return c.SameSystem;
            }
            return c.OtherSystem;
        }

        public static LocationData Defaults()
        {
            LocationData d = new LocationData();
            d.DataVersion = CurrentDataVersion;
            d.Notes = "Edit freely. Bodies: planets/moons (Group = parent planet). Locations: text that identifies a place -> its Body and Kind " +
                      "(Station, City, DistributionCenter, Outpost). Costs: relative travel effort used by the route planner. " +
                      "Anything 'on <planet/moon>' that is not listed is treated as a planetary outpost. Delete this file to restore defaults.";
            List<BodyInfo> b = d.Bodies;
            // Stanton
            b.Add(new BodyInfo("Hurston", "Hurston", "Stanton", "Stanton I"));
            b.Add(new BodyInfo("Arial", "Hurston", "Stanton"));
            b.Add(new BodyInfo("Aberdeen", "Hurston", "Stanton"));
            b.Add(new BodyInfo("Magda", "Hurston", "Stanton"));
            b.Add(new BodyInfo("Ita", "Hurston", "Stanton"));
            b.Add(new BodyInfo("Crusader", "Crusader", "Stanton", "Stanton II"));
            b.Add(new BodyInfo("Cellin", "Crusader", "Stanton"));
            b.Add(new BodyInfo("Daymar", "Crusader", "Stanton"));
            b.Add(new BodyInfo("Yela", "Crusader", "Stanton"));
            b.Add(new BodyInfo("ArcCorp", "ArcCorp", "Stanton", "Stanton III", "Arc Corp"));
            b.Add(new BodyInfo("Lyria", "ArcCorp", "Stanton"));
            b.Add(new BodyInfo("Wala", "ArcCorp", "Stanton"));
            b.Add(new BodyInfo("microTech", "microTech", "Stanton", "Stanton IV", "Micro Tech"));
            b.Add(new BodyInfo("Calliope", "microTech", "Stanton"));
            b.Add(new BodyInfo("Clio", "microTech", "Stanton"));
            b.Add(new BodyInfo("Euterpe", "microTech", "Stanton"));
            b.Add(new BodyInfo("Stanton Jump Points", "Stanton Jump Points", "Stanton"));
            // Pyro
            b.Add(new BodyInfo("Pyro I", "Pyro I", "Pyro"));
            b.Add(new BodyInfo("Monox", "Monox", "Pyro", "Pyro II"));
            b.Add(new BodyInfo("Bloom", "Bloom", "Pyro", "Pyro III"));
            b.Add(new BodyInfo("Pyro IV", "Pyro IV", "Pyro"));
            b.Add(new BodyInfo("Pyro V", "Pyro V", "Pyro"));
            b.Add(new BodyInfo("Ignis", "Pyro V", "Pyro"));
            b.Add(new BodyInfo("Vatra", "Pyro V", "Pyro"));
            b.Add(new BodyInfo("Adir", "Pyro V", "Pyro"));
            b.Add(new BodyInfo("Fairo", "Pyro V", "Pyro"));
            b.Add(new BodyInfo("Fuego", "Pyro V", "Pyro"));
            b.Add(new BodyInfo("Vuur", "Pyro V", "Pyro"));
            b.Add(new BodyInfo("Terminus", "Terminus", "Pyro", "Pyro VI"));
            b.Add(new BodyInfo("Pyro Jump Points", "Pyro Jump Points", "Pyro"));
            // Nyx
            b.Add(new BodyInfo("Delamar", "Delamar", "Nyx"));
            b.Add(new BodyInfo("Nyx I", "Nyx I", "Nyx"));
            b.Add(new BodyInfo("Nyx II", "Nyx II", "Nyx"));
            b.Add(new BodyInfo("Nyx III", "Nyx III", "Nyx"));
            b.Add(new BodyInfo("Nyx Jump Points", "Nyx Jump Points", "Nyx"));

            List<LocationHint> l = d.Locations;
            // Stanton stations
            l.Add(new LocationHint("Everus Harbor", "Hurston", "Station"));
            l.Add(new LocationHint("HUR-L", "Hurston", "Station"));
            l.Add(new LocationHint("Seraphim Station", "Crusader", "Station"));
            l.Add(new LocationHint("Port Olisar", "Crusader", "Station"));
            l.Add(new LocationHint("CRU-L", "Crusader", "Station"));
            l.Add(new LocationHint("GrimHEX", "Yela", "Station"));
            l.Add(new LocationHint("Grim HEX", "Yela", "Station"));
            l.Add(new LocationHint("Baijini Point", "ArcCorp", "Station"));
            l.Add(new LocationHint("ARC-L", "ArcCorp", "Station"));
            l.Add(new LocationHint("Port Tressler", "microTech", "Station"));
            l.Add(new LocationHint("MIC-L", "microTech", "Station"));
            l.Add(new LocationHint("Pyro Gateway", "Stanton Jump Points", "Station"));
            // Cities / landing zones (planetary, but not outposts)
            l.Add(new LocationHint("Lorville", "Hurston", "City"));
            l.Add(new LocationHint("Orison", "Crusader", "City"));
            l.Add(new LocationHint("Area18", "ArcCorp", "City"));
            l.Add(new LocationHint("Area 18", "ArcCorp", "City"));
            l.Add(new LocationHint("New Babbage", "microTech", "City"));
            l.Add(new LocationHint("Levski", "Delamar", "City"));
            // Pyro stations
            l.Add(new LocationHint("Stanton Gateway", "Pyro Jump Points", "Station"));
            l.Add(new LocationHint("Ruin Station", "Terminus", "Station"));
            l.Add(new LocationHint("Orbituary", "Bloom", "Station"));
            l.Add(new LocationHint("Checkmate", "Monox", "Station"));
            l.Add(new LocationHint("Starlight Service Station", "Bloom", "Station"));
            l.Add(new LocationHint("Rod's Fuel", "Pyro V", "Station"));
            l.Add(new LocationHint("Gaslight", "Pyro V", "Station"));
            l.Add(new LocationHint("Megumi Refueling", "Terminus", "Station"));
            l.Add(new LocationHint("Endgame", "Terminus", "Station"));
            l.Add(new LocationHint("Dudley", "Terminus", "Station"));
            l.Add(new LocationHint("Patch City", "", "Station"));
            l.Add(new LocationHint("Rat's Nest", "", "Station"));
            AddPlaces(d, Place.LoadBuiltIn());
            return d;
        }

        // Every known pickup / drop-off place (from the UEX Corp location lists): each gets a hint for its full name
        // (shown in the Start at list) and, for Lagrange stations, one for its short code ("HUR-L1").
        // Older short hints ("Checkmate", "Dudley", "Area18") are tied to the same place.
        public static int AddPlaces(LocationData d, List<Place> places)
        {
            int added = 0;
            foreach (Place p in places)
            {
                if (string.IsNullOrWhiteSpace(p.Name)) continue;
                added += Upsert(d, p.Name, p, p.Name);
                if (!string.IsNullOrEmpty(p.Full) && !string.Equals(p.Full, p.Name, StringComparison.OrdinalIgnoreCase)) Upsert(d, p.Full, p, "");
                if (!string.IsNullOrEmpty(p.Short)) Upsert(d, p.Short, p, "");
            }
            foreach (LocationHint h in d.Locations)
            {
                if (!string.IsNullOrEmpty(h.Key) || h.Match.EndsWith("-L", StringComparison.OrdinalIgnoreCase) || h.Match.Length < 4) continue;
                string k = KeyOf(h.Match).Replace(" ", "");
                List<Place> hits = places.Where(p => KeyOf(p.Name).Replace(" ", "").Contains(k) && p.Kind == h.Kind).ToList();
                if (hits.Count == 1) h.Key = hits[0].Name;
            }
            return added;
        }

        static int Upsert(LocationData d, string match, Place p, string listName)
        {
            LocationHint h = d.Locations.FirstOrDefault(x => string.Equals(x.Match, match, StringComparison.OrdinalIgnoreCase));
            bool isNew = h == null;
            if (isNew) { h = new LocationHint(match, p.Body, p.Kind); d.Locations.Add(h); }
            if (string.IsNullOrEmpty(h.Body)) h.Body = p.Body;
            if (string.IsNullOrEmpty(h.Kind)) h.Kind = p.Kind;
            h.Key = p.Name;
            if (listName.Length > 0) h.Name = listName;
            return isNew && listName.Length > 0 ? 1 : 0;
        }
    }
}
