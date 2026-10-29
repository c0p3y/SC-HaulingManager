// SC Hauling Manager - C0P3Y | Icarus Interstellar Inc.
// Portable storage: everything lives in JSON files next to the .exe (no registry, no AppData unless the folder is read-only).
using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace HaulingManager
{
    public static class Json
    {
        static JavaScriptSerializer Make()
        {
            JavaScriptSerializer s = new JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue;
            return s;
        }

        public static string Serialize(object o) { return Pretty(Make().Serialize(o)); }
        public static T Deserialize<T>(string json) { return Make().Deserialize<T>(json); }

        public static string Pretty(string json)
        {
            StringBuilder sb = new StringBuilder();
            int indent = 0; bool inStr = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inStr)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < json.Length) { sb.Append(json[++i]); }
                    else if (c == '"') inStr = false;
                    continue;
                }
                switch (c)
                {
                    case '"': inStr = true; sb.Append(c); break;
                    case '{':
                    case '[':
                        if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']')) { sb.Append(c).Append(json[i + 1]); i++; break; }
                        sb.Append(c); indent++; NewLine(sb, indent); break;
                    case '}':
                    case ']':
                        indent--; NewLine(sb, indent); sb.Append(c); break;
                    case ',': sb.Append(c); NewLine(sb, indent); break;
                    case ':': sb.Append(": "); break;
                    default: if (!char.IsWhiteSpace(c)) sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        static void NewLine(StringBuilder sb, int indent)
        {
            sb.Append("\r\n");
            sb.Append(' ', Math.Max(0, indent) * 2);
        }
    }

    public static class Storage
    {
        static string baseDir;

        public static string BaseDir
        {
            get
            {
                if (baseDir == null)
                {
                    string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                    if (CanWrite(exeDir)) baseDir = exeDir;
                    else
                    {
                        baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCHaulingManager");
                        Directory.CreateDirectory(baseDir);
                    }
                }
                return baseDir;
            }
        }

        public static string DebugDir
        {
            get { string d = Path.Combine(BaseDir, "debug"); Directory.CreateDirectory(d); return d; }
        }

        static bool CanWrite(string dir)
        {
            try
            {
                string p = Path.Combine(dir, ".write_test");
                File.WriteAllText(p, "x");
                File.Delete(p);
                return true;
            }
            catch { return false; }
        }

        public static T Load<T>(string fileName) where T : new()
        {
            string p = Path.Combine(BaseDir, fileName);
            try
            {
                if (File.Exists(p))
                {
                    T v = Json.Deserialize<T>(File.ReadAllText(p, Encoding.UTF8));
                    if (v != null) return v;
                }
            }
            catch (Exception ex)
            {
                // Keep a copy of the unreadable file so nothing is silently lost.
                try { File.Copy(p, p + ".bad", true); } catch { }
                System.Diagnostics.Debug.WriteLine(ex);
            }
            return new T();
        }

        public static void Save(string fileName, object value)
        {
            string p = Path.Combine(BaseDir, fileName);
            string tmp = p + ".tmp";
            File.WriteAllText(tmp, Json.Serialize(value), Encoding.UTF8);
            if (File.Exists(p)) File.Delete(p);
            File.Move(tmp, p);
        }
    }
}
