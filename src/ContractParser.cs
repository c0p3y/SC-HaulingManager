// SC Hauling Manager - C0P3Y | Icarus Interstellar Inc.
// Turns OCR text of a mobiGlas hauling contract into cargo legs.
// Built to tolerate OCR noise: wrapped lines, bullet glyphs, "0/24SCU", "O/24 SCU", "0 | 24 SCU", etc.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HaulingManager
{
    public class ParsedObjective
    {
        public bool IsPickup;
        public string Material = "";
        public int Qty;
        public int Done;
        public string Location = "";
    }

    public class ParseResult
    {
        public string Title = "";
        public long Reward;
        public string RawText = "";
        public List<ParsedObjective> Pickups = new List<ParsedObjective>();
        public List<ParsedObjective> Deliveries = new List<ParsedObjective>();
        public List<CargoLeg> Legs = new List<CargoLeg>();
        public List<string> Warnings = new List<string>();

        public int Score()
        {
            int s = Legs.Count * 10 + Deliveries.Count * 3 + Pickups.Count * 2 + (Reward > 0 ? 1 : 0);
            foreach (CargoLeg l in Legs)
                if (l.Scu > 0 && l.Material.Length > 0 && l.Pickup.Length > 0 && l.Dropoff.Length > 0) s += 2;
            return s - Warnings.Count;
        }

        public Contract ToContract()
        {
            Contract c = new Contract();
            c.Title = Title.Length > 0 ? Title : "Hauling Contract";
            c.Reward = Reward;
            c.RawText = RawText;
            c.Warnings = string.Join(" | ", Warnings.ToArray());
            foreach (CargoLeg l in Legs) c.Legs.Add(l.Clone());
            return c;
        }
    }

    public static class ContractParser
    {
        const char LB = '¶'; // marks an original OCR line break inside the flattened text

        // Words that start the next block of text and therefore end a location / material.
        const string Term = @"(?:Collect|Pick\s?-?up|Deliver|Drop\s?-?off|Rewards?|Primary|Secondary|Optional|Objectives?|Contractor|Contract\s+(?:Details|Availability|Deadline)|Accept|Expires?|Time\s+Left|Abandon|Share|Bonus|Penalty|Hazards?|Untrack|Track)\b";

        const string W = @"[\s¶]"; // whitespace or an OCR line break

        static readonly Regex DeliverRx = new Regex(
            @"\b(?:Deliver|Drop\s?-?off)" + W + @"+(?:(?<done>[0-9Oo]{1,5})" + W + @"*[/\\|]" + W + @"*)?(?<qty>[0-9][0-9,]{0,6})" + W + @"*SCU\b" + W + @"*" +
            @"(?:of" + W + @"+(?<mat>[^.]{1,80}?)" + W + @"+)?(?:to|at)" + W + @"+(?<loc>[^.]{2,160}?)(?=" + W + @"*(?:\.|" + Term + @"|$))",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        static readonly Regex PickupRx = new Regex(
            @"\b(?:Collect|Pick\s?-?up(?!" + W + @"*locations)|Retrieve|Obtain|Acquire)" + W + @"+(?:(?<done>[0-9Oo]{1,5})" + W + @"*[/\\|]" + W + @"*)?(?:(?<qty>[0-9][0-9,]{0,6})" + W + @"*SCU\b" + W + @"*(?:of" + W + @"+)?)?" +
            @"(?<mat>[^.]{1,80}?)" + W + @"+(?:from|at)" + W + @"+(?<loc>[^.]{2,160}?)(?=" + W + @"*(?:\.|" + Term + @"|$))",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // The aUEC symbol is usually read as A-umlaut, a-umlaut or the generic currency sign.
        static readonly Regex CurrencyRx = new Regex(@"(?:¤|Ä|ä|Â¤)" + W + @"*(?<r>[0-9]{1,3}(?:[,.][0-9]{3})+|[0-9]{3,})");
        static readonly Regex RewardRx = new Regex(
            @"\bRewards?\b[^0-9]{0,120}?(?<r>[0-9]{1,3}(?:[,. ][0-9]{3})+)", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        static readonly Regex TitleRx = new Regex(@"\b(haul|hauling|cargo|freight|transport|supply|supplies|delivery|logistics|shipment|courier)\b", RegexOptions.IgnoreCase);
        static readonly Regex ContinuationRx = new Regex(@"^(above|on|at|near|orbiting|around|over|in|of|lagrange)\b", RegexOptions.IgnoreCase);
        static readonly Regex DanglingRx = new Regex(@"\b(above|on|at|near|orbiting|around|over|in|of|the|to|from|lagrange)$", RegexOptions.IgnoreCase);
        static readonly Regex HasBodyPhraseRx = new Regex(@"\b(above|on|orbiting|around|over|near|lagrange of)\s+\S+", RegexOptions.IgnoreCase);
        static readonly Regex PlaceholderRx = new Regex(@"^(location|locationaddress|location address|unknown)$", RegexOptions.IgnoreCase);

        public static string Normalize(string raw)
        {
            if (raw == null) return "";
            string t = raw.Replace("\r\n", "\n").Replace('\r', '\n');
            // Bullet / icon glyphs OCR commonly produces at the start of objective lines
            t = Regex.Replace(t, @"[•◆◇▪■□●○►▶·‣⁃∙♦❖]", " ");
            // Common OCR mangles of "SCU"
            t = Regex.Replace(t, @"(?<=\d)\s*(?:SCU|5CU|SCLJ|SCIJ|S\s?CU|SC\s?U|scu)\b", " SCU");
            // "O/24" -> "0/24"
            t = Regex.Replace(t, @"(?<![A-Za-z])[Oo](?=\s*/\s*\d)", "0");
            // Lagrange point names: "et the L2" -> "at the L2", "LS" -> "L5", "Li"/"Ll" -> "L1"
            t = Regex.Replace(t, @"\b[Ee]t(?=\s+the\s+L\S{1,2}\s+Lagrange)", "at");
            t = Regex.Replace(t, @"\bL[Ss](?=\s+Lagrange)", "L5");
            t = Regex.Replace(t, @"\bL[iIl|!](?=\s+Lagrange)", "L1");
            t = Regex.Replace(t, @"\bLagrange\b", "Lagrange", RegexOptions.IgnoreCase);
            // Roman numerals after system names: "Pyro Ill" -> "Pyro III", "pyro V" -> "Pyro V"
            t = Regex.Replace(t, @"\b(pyro|nyx|stanton)(\s+)([IVXl1|]{1,4})\b", delegate(Match m)
            {
                string sys = char.ToUpper(m.Groups[1].Value[0]) + m.Groups[1].Value.Substring(1).ToLower();
                return sys + m.Groups[2].Value + m.Groups[3].Value.Replace('l', 'I').Replace('1', 'I').Replace('|', 'I');
            }, RegexOptions.IgnoreCase);
            // "Dudley S Daughters" / "Dudley B Daughters" -> "Dudley & Daughters"
            t = Regex.Replace(t, @"\b([A-Z][a-z]+) [SB8$] (?=(Daughters|Sons)\b)", "$1 & ");
            return t;
        }

        // Cleans OCR noise out of a single location name ("Dudley S Daughters et the L4 Lagrange of pyro Vl").
        public static string NormalizeLocation(string loc)
        {
            return LocationDb.Clean(Normalize(loc ?? "").Replace('\n', ' '));
        }

        // Set by the app so the parser can tell when two spellings are the same place.
        public static LocationDb Places;

        public static string PlaceKey(string loc)
        {
            if (Places != null && !string.IsNullOrWhiteSpace(loc)) return Places.Resolve(loc).Key;
            return LocationDb.KeyOf(loc ?? "");
        }

        public static ParseResult Parse(string raw)
        {
            ParseResult r = new ParseResult();
            r.RawText = raw ?? "";
            string norm = Normalize(raw);
            string[] lines = norm.Split(new[] { '\n' }).Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            string flat = string.Join(" " + LB + " ", lines);
            flat = Regex.Replace(flat, @"[ \t]+", " ");

            foreach (Match m in DeliverRx.Matches(flat))
            {
                ParsedObjective o = new ParsedObjective();
                o.IsPickup = false;
                o.Material = CleanMaterial(m.Groups["mat"].Value);
                o.Location = CleanLocation(m.Groups["loc"].Value, EndsAtPeriod(flat, m));
                ReadQty(m, o, r);
                if (o.Location.Length > 0) r.Deliveries.Add(o);
            }

            foreach (Match m in PickupRx.Matches(flat))
            {
                ParsedObjective o = new ParsedObjective();
                o.IsPickup = true;
                o.Material = CleanMaterial(m.Groups["mat"].Value);
                o.Location = CleanLocation(m.Groups["loc"].Value, EndsAtPeriod(flat, m));
                if (m.Groups["qty"].Success) ReadQty(m, o, r);
                if (o.Material.IndexOf("location", StringComparison.OrdinalIgnoreCase) >= 0) continue;   // "PICK UP LOCATIONS (ANY ORDER)" header
                if (o.Location.Length > 0 && o.Material.Length > 0) r.Pickups.Add(o);
            }

            // Short names ("Collect Carbon from Gaslight.") -> full names found elsewhere in the text
            // ("Freight elevator at Gaslight at the L2 Lagrange of Pyro V").
            foreach (ParsedObjective o in r.Pickups.Concat(r.Deliveries)) o.Location = Expand(o.Location, flat);

            // One name per place: "Petch City" (OCR slip) and "Patch City at the L3 Lagrange of Pyro III" are the same
            // pickup, so they all take the longest spelling seen in this contract.
            List<ParsedObjective> all = r.Pickups.Concat(r.Deliveries).ToList();
            foreach (var g in all.GroupBy(o => PlaceKey(o.Location)))
            {
                string best = g.Select(o => o.Location).OrderByDescending(x => x.Length).First();
                foreach (ParsedObjective o in g) o.Location = best;
            }

            // de-duplicate pickups (OCR can read the same line twice; some contracts repeat "Collect X from Y" per drop-off)
            r.Pickups = r.Pickups.GroupBy(p => LocationDb.KeyOf(p.Material) + "|" + PlaceKey(p.Location)).Select(g => g.First()).ToList();

            r.Reward = FindReward(flat);

            r.Title = FindTitle(lines);
            BuildLegs(r);
            return r;
        }

        // The mobiGlas header also shows your wallet balance with the same currency symbol, so prefer
        // the amount that follows the word "Reward".
        static long FindReward(string flat)
        {
            Match rw = Regex.Match(flat, @"\bRewards?\b", RegexOptions.IgnoreCase);
            Match pick = null;
            if (rw.Success)
            {
                foreach (Match m in CurrencyRx.Matches(flat))
                    if (m.Index > rw.Index && m.Index - rw.Index < 400) { pick = m; break; }
                if (pick == null) { Match m2 = RewardRx.Match(flat); if (m2.Success) pick = m2; }
            }
            if (pick == null)
            {
                MatchCollection all = CurrencyRx.Matches(flat);
                if (all.Count > 1) pick = all[all.Count - 1]; else if (all.Count == 1) pick = all[0];
            }
            if (pick == null) return 0;
            string digits = new string(pick.Groups["r"].Value.Where(char.IsDigit).ToArray());
            long v;
            return long.TryParse(digits, out v) ? v : 0;
        }

        static string FindTitle(string[] lines)
        {
            // Pass 1 wants the word "haul" (real contract titles: "Member | Stellar Medium Haul | to Ruin Station"),
            // pass 2 accepts other cargo words. Mixed case wins over ALL CAPS (the contract list on the left).
            for (int pass = 0; pass < 2; pass++)
            {
                string fallback = "";
                foreach (string line in lines)
                {
                    if (line.Length > 90 || line.Length < 6) continue;
                    if (Regex.IsMatch(line, @"^\s*[Oo]?\s*(Deliver|Collect|Pick\s?-?up|Drop)", RegexOptions.IgnoreCase)) continue;
                    if (line.Contains("->") || line.Contains("→") || line.Contains(":") || Regex.IsMatch(line, @"No delivery found|Check:|\bfrom\b.*\bSupplies\b", RegexOptions.IgnoreCase)) continue;
                    if (pass == 0 ? !Regex.IsMatch(line, @"\bhaul", RegexOptions.IgnoreCase) : !TitleRx.IsMatch(line)) continue;
                    string t = LocationDb.Clean(Regex.Replace(line, @"(?<=\w)\s+[I1|l]\s+(?=\w)", " | "));
                    if (line.ToUpperInvariant() != line) return t;
                    if (fallback.Length == 0) fallback = t;
                }
                if (fallback.Length > 0) return fallback;
            }
            return "";
        }

        static bool EndsAtPeriod(string flat, Match m)
        {
            int i = m.Index + m.Length;
            while (i < flat.Length && (char.IsWhiteSpace(flat[i]) || flat[i] == LB)) i++;
            return i < flat.Length && flat[i] == '.';
        }

        static string Expand(string loc, string flat)
        {
            if (loc.Length == 0 || HasBodyPhraseRx.IsMatch(loc)) return loc;
            Match m = Regex.Match(flat, Regex.Escape(loc) + W + @"+(?<p>(?:at" + W + @"+the" + W + @"+L\d" + W + @"+Lagrange" + W + @"+of|above|on)" + W + @"+[A-Za-z'\-]+(?:" + W + @"+[IVX]{1,4}\b)?)",
                                  RegexOptions.IgnoreCase);
            return m.Success ? LocationDb.Clean(loc + " " + m.Groups["p"].Value) : loc;
        }

        static void ReadQty(Match m, ParsedObjective o, ParseResult r)
        {
            string q = m.Groups["qty"].Value.Replace(",", "");
            string d = m.Groups["done"].Success ? m.Groups["done"].Value.Replace('O', '0').Replace('o', '0') : "";
            // "0/24" read as "0124": a real quantity never starts with 0, so treat the 2nd char as the slash.
            if (d.Length == 0 && q.Length >= 3 && q[0] == '0')
            {
                d = "0"; q = q.Substring(2);
                r.Warnings.Add("Quantity looked like '" + m.Groups["qty"].Value + "' - read as 0/" + q + ", please check");
            }
            int qi, di;
            int.TryParse(q, out qi); int.TryParse(d, out di);
            o.Qty = qi; o.Done = Math.Min(di, qi);
        }

        static string CleanMaterial(string s)
        {
            s = LocationDb.Clean(s);
            s = Regex.Replace(s, @"^(the|some)\s+", "", RegexOptions.IgnoreCase);
            return s;
        }

        // Rejoins a location that OCR wrapped onto two lines, but stops at unrelated text that follows it.
        static string CleanLocation(string s, bool endedAtPeriod)
        {
            string[] seg = s.Split(new[] { LB });
            string result = seg[0].Trim();
            for (int i = 1; i < seg.Length; i++)
            {
                string next = seg[i].Trim();
                if (next.Length == 0) continue;
                bool join = result.Length == 0 || ContinuationRx.IsMatch(next) || DanglingRx.IsMatch(result)
                            // the sentence ends with a period after this, so everything up to it is the location ("Pyro" / "VI.")
                            || (endedAtPeriod && next.Length <= 40)
                            // wrapped name like "Shubin Mining Facility" / "SCD-1 on Daymar": keep going until we have "on/above <body>"
                            || (!HasBodyPhraseRx.IsMatch(result) && next.Length <= 50);
                if (join) result = result + " " + next;
                else break;
            }
            return LocationDb.Clean(result);
        }

        static bool Same(string a, string b) { return LocationDb.KeyOf(a) == LocationDb.KeyOf(b); }
        static bool IsPlaceholder(string loc) { return PlaceholderRx.IsMatch(LocationDb.KeyOf(loc)); }

        static void BuildLegs(ParseResult r)
        {
            List<string> pickMats = r.Pickups.Select(p => p.Material).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            HashSet<ParsedObjective> used = new HashSet<ParsedObjective>();

            foreach (ParsedObjective d in r.Deliveries)
            {
                string material = d.Material;
                if (material.Length == 0)
                {
                    if (pickMats.Count == 1) material = pickMats[0];
                    else r.Warnings.Add("Material unknown for delivery to " + d.Location);
                }

                List<ParsedObjective> src = r.Pickups.Where(p => material.Length > 0 && Same(p.Material, material)).ToList();
                if (src.Count == 0 && r.Pickups.Count == 1) src.Add(r.Pickups[0]);
                if (src.Count == 0 && r.Pickups.Count > 0 && r.Pickups.Select(p => LocationDb.KeyOf(p.Location)).Distinct().Count() == 1) src.Add(r.Pickups[0]);
                foreach (ParsedObjective p in src) used.Add(p);
                if (d.Qty <= 0) r.Warnings.Add("Quantity unreadable for delivery to " + d.Location);

                if (src.Count <= 1)
                {
                    CargoLeg leg = new CargoLeg();
                    leg.Material = material; leg.Scu = d.Qty; leg.Delivered = d.Done; leg.PickedUp = d.Done; leg.Dropoff = d.Location;
                    leg.Pickup = src.Count == 1 ? src[0].Location : "";
                    if (leg.Pickup.Length == 0) r.Warnings.Add("Pickup location unknown for " + (material.Length > 0 ? material : "cargo") + " -> " + d.Location);
                    FixPlaceholder(leg, r);
                    r.Legs.Add(leg);
                }
                else
                {
                    // One delivery fed by several pickup points ("PICK UP LOCATIONS (ANY ORDER)"): the contract does not say
                    // how much is waiting at each, so split evenly. Every pickup gets visited before the drop either way.
                    int n = src.Count;
                    string group = "d" + (r.Deliveries.IndexOf(d) + 1);
                    for (int i = 0; i < n; i++)
                    {
                        CargoLeg leg = new CargoLeg();
                        leg.Group = group; leg.Estimated = true;
                        leg.Material = material; leg.Dropoff = d.Location; leg.Pickup = src[i].Location;
                        leg.Scu = d.Qty / n + (i < d.Qty % n ? 1 : 0);
                        leg.Delivered = d.Done / n + (i < d.Done % n ? 1 : 0);
                        leg.PickedUp = leg.Delivered;
                        FixPlaceholder(leg, r);
                        r.Legs.Add(leg);
                    }
                    r.Warnings.Add(material + ": " + d.Qty + " SCU collected from " + n + " pickup points - amount at each is not shown (estimated as an even split)");
                }
            }

            // Pickups that never got a delivery (e.g. the delivery line was cut off by the capture region)
            foreach (ParsedObjective p in r.Pickups)
            {
                if (used.Contains(p)) continue;
                CargoLeg leg = new CargoLeg();
                leg.Material = p.Material; leg.Pickup = p.Location; leg.Scu = p.Qty;
                leg.PickedUp = p.Done;
                FixPlaceholder(leg, r);
                r.Legs.Add(leg);
                r.Warnings.Add("No delivery found for " + p.Material + " from " + p.Location);
            }

            if (r.Legs.Count == 0) r.Warnings.Add("No hauling objectives found in the captured text");
        }

        static void FixPlaceholder(CargoLeg leg, ParseResult r)
        {
            if (leg.Pickup.Length > 0 && IsPlaceholder(leg.Pickup))
            {
                leg.Pickup = "";
                r.Warnings.Add("The game shows an unnamed pickup ('location') for " + leg.Material + " - check the contract and fill it in");
            }
            if (leg.Dropoff.Length > 0 && IsPlaceholder(leg.Dropoff))
            {
                leg.Dropoff = "";
                r.Warnings.Add("The game shows an unnamed drop-off for " + leg.Material + " - check the contract and fill it in");
            }
        }

        // Identity of a contract used to spot re-captures of the same contract (e.g. to update 12/24 progress).
        public static string Signature(Contract c)
        {
            // multi-pickup lines are identified without their (estimated / user-corrected) amounts
            List<string> parts = c.Legs.Select(l => LocationDb.KeyOf(l.Material) + "|" + (string.IsNullOrEmpty(l.Group) ? l.Scu.ToString() : "g") + "|" + LocationDb.KeyOf(l.Pickup) + "|" + LocationDb.KeyOf(l.Dropoff)).ToList();
            parts.Sort(StringComparer.Ordinal);
            return string.Join(";", parts.ToArray());
        }
    }
}
