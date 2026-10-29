// SC Hauling Manager - C0P3Y | Icarus Interstellar Inc.
// "Update locations": downloads the current station / outpost / city / distribution-centre lists from the
// UEX Corp API (community-maintained, https://uexcorp.space) and merges them into locations.json.
// Read-only GET requests; nothing about you is sent.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace HaulingManager
{
    public static class UexUpdater
    {
        const string Base = "https://api.uexcorp.uk/2.0/";
        static readonly Regex Lagrange = new Regex(@"^(?<b>.+?) Lagrange Point \d", RegexOptions.IgnoreCase);
        static readonly Regex LCode = new Regex(@"^[A-Z]{3}-L\d$");
        static readonly Regex DcWords = new Regex(@"Workcenter|Depot|Distribution|Production Complex", RegexOptions.IgnoreCase);

        public static List<Place> Download(out string error)
        {
            error = null;
            List<Place> all = new List<Place>();
            try
            {
                try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }   // TLS 1.2
                foreach (Dictionary<string, object> r in Get("space_stations"))
                {
                    if (!Flag(r, "is_available_live", true) || Str(r, "name").StartsWith("Wikelo", StringComparison.OrdinalIgnoreCase)) continue;
                    string name = Str(r, "name"), nick = Str(r, "nickname"), sys = Str(r, "star_system_name");
                    Match m = Lagrange.Match(Str(r, "orbit_name"));
                    string body = m.Success ? m.Groups["b"].Value : First(Str(r, "moon_name"), Str(r, "planet_name"));
                    if (name.IndexOf("Gateway", StringComparison.OrdinalIgnoreCase) >= 0) body = sys + " Jump Points";
                    bool grim = nick.Equals("Grim HEX", StringComparison.OrdinalIgnoreCase);
                    all.Add(new Place { Name = grim ? nick : name, Full = name, Short = LCode.IsMatch(nick) ? nick : "", System = sys, Body = body, Kind = "Station" });
                }
                foreach (Dictionary<string, object> r in Get("outposts"))
                {
                    if (!Flag(r, "is_available_live", true)) continue;
                    string name = Str(r, "name");
                    all.Add(new Place { Name = name, Full = name, System = Str(r, "star_system_name"), Body = First(Str(r, "moon_name"), Str(r, "planet_name")),
                                        Kind = DcWords.IsMatch(name) ? "DistributionCenter" : "Outpost" });
                }
                foreach (Dictionary<string, object> r in Get("cities"))
                {
                    if (!Flag(r, "is_available_live", true)) continue;
                    string name = Str(r, "name");
                    all.Add(new Place { Name = name, Full = name, System = Str(r, "star_system_name"), Body = First(Str(r, "moon_name"), Str(r, "planet_name")), Kind = "City" });
                }
                try
                {
                    foreach (Dictionary<string, object> r in Get("poi"))
                    {
                        string name = Str(r, "name");
                        if (!DcWords.IsMatch(name)) continue;    // only distribution centres; the rest aren't hauling stops
                        all.Add(new Place { Name = name, Full = name, System = Str(r, "star_system_name"), Body = First(Str(r, "moon_name"), Str(r, "planet_name")), Kind = "DistributionCenter" });
                    }
                }
                catch { }   // optional list
            }
            catch (Exception ex) { error = ex.Message; }
            // one entry per name
            return all.Where(p => p.Name.Length > 0).GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        }

        static IEnumerable<Dictionary<string, object>> Get(string resource)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Base + resource + "/");
            req.UserAgent = "SCHaulingManager";
            req.Timeout = 20000;
            req.Accept = "application/json";
            string text;
            using (WebResponse resp = req.GetResponse())
            using (StreamReader rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) text = rd.ReadToEnd();
            JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            Dictionary<string, object> root = js.DeserializeObject(text) as Dictionary<string, object>;
            object data;
            if (root == null || !root.TryGetValue("data", out data) || !(data is IEnumerable)) throw new Exception("Unexpected reply from UEX for " + resource);
            List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
            foreach (object o in (IEnumerable)data) { Dictionary<string, object> d = o as Dictionary<string, object>; if (d != null) list.Add(d); }
            return list;
        }

        static string Str(Dictionary<string, object> r, string k)
        {
            object v;
            if (!r.TryGetValue(k, out v) || v == null) return "";
            string s = Convert.ToString(v).Trim();
            return s.Equals("null", StringComparison.OrdinalIgnoreCase) ? "" : s;
        }

        static bool Flag(Dictionary<string, object> r, string k, bool dflt)
        {
            object v;
            if (!r.TryGetValue(k, out v) || v == null) return dflt;
            string s = Convert.ToString(v);
            return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        static string First(params string[] v) { return v.FirstOrDefault(x => !string.IsNullOrEmpty(x)) ?? ""; }
    }
}
