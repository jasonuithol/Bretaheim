using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Bretaheim.Data
{
    /// <summary>
    /// Minimal JSON reader for the mapping tables (Unity's JsonUtility cannot read dictionaries).
    /// Objects become Dictionary&lt;string, object&gt;, arrays List&lt;object&gt;, numbers double.
    /// </summary>
    public static class Json
    {
        public static object Parse(string text)
        {
            int p = 0;
            var v = Value(text, ref p);
            Skip(text, ref p);
            if (p != text.Length) throw Error(text, p, "trailing characters");
            return v;
        }

        public static Dictionary<string, object> Obj(object o) => (Dictionary<string, object>)o;
        public static List<object> Arr(object o) => (List<object>)o;

        public static object Get(Dictionary<string, object> o, string key) => o.TryGetValue(key, out var v) ? v : null;
        public static string Str(Dictionary<string, object> o, string key) => Get(o, key) as string;
        public static double Num(Dictionary<string, object> o, string key, double fallback = 0)
            => Get(o, key) is double d ? d : fallback;
        public static bool Bool(Dictionary<string, object> o, string key) => Get(o, key) is bool b && b;

        static object Value(string s, ref int p)
        {
            Skip(s, ref p);
            if (p >= s.Length) throw Error(s, p, "unexpected end");
            char c = s[p];
            if (c == '{') return Object(s, ref p);
            if (c == '[') return Array(s, ref p);
            if (c == '"') return String(s, ref p);
            if (Lit(s, ref p, "true")) return true;
            if (Lit(s, ref p, "false")) return false;
            if (Lit(s, ref p, "null")) return null;
            return Number(s, ref p);
        }

        static Dictionary<string, object> Object(string s, ref int p)
        {
            var r = new Dictionary<string, object>();
            p++;
            Skip(s, ref p);
            if (s[p] == '}') { p++; return r; }
            while (true)
            {
                Skip(s, ref p);
                string key = String(s, ref p);
                Skip(s, ref p);
                if (s[p++] != ':') throw Error(s, p - 1, "':' expected");
                r[key] = Value(s, ref p);
                Skip(s, ref p);
                char c = s[p++];
                if (c == '}') return r;
                if (c != ',') throw Error(s, p - 1, "',' or '}' expected");
            }
        }

        static List<object> Array(string s, ref int p)
        {
            var r = new List<object>();
            p++;
            Skip(s, ref p);
            if (s[p] == ']') { p++; return r; }
            while (true)
            {
                r.Add(Value(s, ref p));
                Skip(s, ref p);
                char c = s[p++];
                if (c == ']') return r;
                if (c != ',') throw Error(s, p - 1, "',' or ']' expected");
            }
        }

        static string String(string s, ref int p)
        {
            if (s[p] != '"') throw Error(s, p, "string expected");
            var sb = new StringBuilder();
            p++;
            while (s[p] != '"')
            {
                char c = s[p++];
                if (c != '\\') { sb.Append(c); continue; }
                c = s[p++];
                switch (c)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(p, 4), 16)); p += 4; break;
                    default: sb.Append(c); break;
                }
            }
            p++;
            return sb.ToString();
        }

        static double Number(string s, ref int p)
        {
            int start = p;
            while (p < s.Length && "+-0123456789.eE".IndexOf(s[p]) >= 0) p++;
            if (p == start) throw Error(s, p, "value expected");
            return double.Parse(s.Substring(start, p - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static bool Lit(string s, ref int p, string lit)
        {
            if (string.CompareOrdinal(s, p, lit, 0, lit.Length) != 0) return false;
            p += lit.Length;
            return true;
        }

        static void Skip(string s, ref int p)
        {
            while (p < s.Length && char.IsWhiteSpace(s[p])) p++;
        }

        static FormatException Error(string s, int p, string what)
        {
            int line = 1;
            for (int i = 0; i < p && i < s.Length; i++) if (s[i] == '\n') line++;
            return new FormatException($"JSON line {line}: {what}");
        }
    }
}
