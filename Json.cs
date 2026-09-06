using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace YtDlpGui
{
    /// <summary>
    /// A small recursive-descent JSON reader, kept in-tree so the build needs nothing beyond
    /// the in-box framework assemblies. Objects become Dictionary&lt;string,object&gt;, arrays
    /// List&lt;object&gt;, numbers double, strings string, and true/false/null map to
    /// bool/null. That is everything yt-dlp's "%(formats)j" output contains.
    /// </summary>
    internal static class Json
    {
        private const int MaxDepth = 64;

        public static bool TryParse(string text, out object value)
        {
            value = null;
            if (string.IsNullOrEmpty(text)) return false;
            try
            {
                int i = 0;
                var v = ParseValue(text, ref i, 0);
                SkipWs(text, ref i);
                value = v;
                return true;
            }
            catch { return false; }
        }

        // ---- accessors -------------------------------------------------------
        // Every one of these tolerates a null or wrongly-typed input: the caller is reading
        // a third party's JSON and should never have to guard each field itself.

        public static Dictionary<string, object> Obj(object o)
        {
            return o as Dictionary<string, object>;
        }

        public static List<object> Arr(object o)
        {
            return o as List<object>;
        }

        /// <summary>The named field as text, or null when absent, null or empty.</summary>
        public static string Str(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return null;

            var s = v as string;
            if (s != null) return s.Length == 0 ? null : s;

            if (v is double) return ((double)v).ToString("0.####", CultureInfo.InvariantCulture);
            if (v is bool) return (bool)v ? "true" : "false";
            return null;
        }

        /// <summary>The named field as a number, or null when absent or not numeric.</summary>
        public static double? Num(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return null;

            if (v is double) return (double)v;

            var s = v as string;
            double parsed;
            if (s != null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                return parsed;
            return null;
        }

        public static bool Flag(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return false;
            if (v is bool) return (bool)v;
            if (v is double) return (double)v != 0;
            var s = v as string;
            return s != null && (s == "true" || s == "1");
        }

        // ---- parser ----------------------------------------------------------
        private static object ParseValue(string s, ref int i, int depth)
        {
            if (depth > MaxDepth) throw new FormatException("JSON nested too deeply.");
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end of JSON.");

            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i, depth);
                case '[': return ParseArray(s, ref i, depth);
                case '"': return ParseString(s, ref i);
                case 't': Expect(s, ref i, "true"); return true;
                case 'f': Expect(s, ref i, "false"); return false;
                case 'n': Expect(s, ref i, "null"); return null;
                default: return ParseNumber(s, ref i);
            }
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i, int depth)
        {
            var map = new Dictionary<string, object>(StringComparer.Ordinal);
            i++;                                   // '{'
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return map; }

            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException("Expected a JSON key.");
                var key = ParseString(s, ref i);

                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("Expected ':'.");
                i++;

                map[key] = ParseValue(s, ref i, depth + 1);

                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("Unterminated JSON object.");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return map; }
                throw new FormatException("Expected ',' or '}'.");
            }
        }

        private static List<object> ParseArray(string s, ref int i, int depth)
        {
            var list = new List<object>();
            i++;                                   // '['
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }

            while (true)
            {
                list.Add(ParseValue(s, ref i, depth + 1));

                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("Unterminated JSON array.");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return list; }
                throw new FormatException("Expected ',' or ']'.");
            }
        }

        private static string ParseString(string s, ref int i)
        {
            i++;                                   // opening quote
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw new FormatException("Unterminated JSON string.");
                char c = s[i++];

                if (c == '"') return sb.ToString();

                if (c != '\\') { sb.Append(c); continue; }

                if (i >= s.Length) throw new FormatException("Unterminated escape.");
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("Truncated \\u escape.");
                        sb.Append((char)ushort.Parse(s.Substring(i, 4), NumberStyles.HexNumber,
                                                     CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: throw new FormatException("Unknown escape \\" + e);
                }
            }
        }

        private static double ParseNumber(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' ||
                                    ((s[i] == '-' || s[i] == '+') && (s[i - 1] == 'e' || s[i - 1] == 'E'))))
                i++;

            double d;
            if (i == start || !double.TryParse(s.Substring(start, i - start), NumberStyles.Float,
                                               CultureInfo.InvariantCulture, out d))
                throw new FormatException("Invalid JSON number.");
            return d;
        }

        private static void Expect(string s, ref int i, string word)
        {
            if (i + word.Length > s.Length || string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
                throw new FormatException("Expected '" + word + "'.");
            i += word.Length;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
        }
    }
}
