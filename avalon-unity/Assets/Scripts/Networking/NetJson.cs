using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Avalon.Networking
{
    /// <summary>
    /// 极简 JSON 编解码（联机协议专用，零依赖）。
    /// 解析产物：Dictionary&lt;string,object&gt; / List&lt;object&gt; / string / double / bool / null。
    /// </summary>
    public static class NetJson
    {
        public static object Parse(string json)
        {
            int pos = 0;
            var v = ParseValue(json, ref pos);
            SkipWs(json, ref pos);
            return v;
        }

        static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw Err(s, i, "意外的结尾");
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObj(s, ref i);
                case '[': return ParseArr(s, ref i);
                case '"': return ParseStr(s, ref i);
                case 't': Expect(s, ref i, "true"); return true;
                case 'f': Expect(s, ref i, "false"); return false;
                case 'n': Expect(s, ref i, "null"); return null;
                default: return ParseNum(s, ref i);
            }
        }

        static Dictionary<string, object> ParseObj(string s, ref int i)
        {
            var d = new Dictionary<string, object>();
            i++; // {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (true)
            {
                SkipWs(s, ref i);
                string key = ParseStr(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw Err(s, i, "期望 ':'");
                i++;
                d[key] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; return d; }
                throw Err(s, i, "期望 ',' 或 '}'");
            }
        }

        static List<object> ParseArr(string s, ref int i)
        {
            var a = new List<object>();
            i++; // [
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return a; }
            while (true)
            {
                a.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; return a; }
                throw Err(s, i, "期望 ',' 或 ']'");
            }
        }

        static string ParseStr(string s, ref int i)
        {
            if (s[i] != '"') throw Err(s, i, "期望字符串");
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
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
                        if (i + 4 > s.Length) throw Err(s, i, "截断的 \\u");
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber));
                        i += 4;
                        break;
                    default: throw Err(s, i, "坏转义 \\" + e);
                }
            }
            throw Err(s, i, "未闭合字符串");
        }

        static double ParseNum(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-.eE0123456789".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
        }

        static void Expect(string s, ref int i, string word)
        {
            if (i + word.Length > s.Length || s.Substring(i, word.Length) != word) throw Err(s, i, "期望 " + word);
            i += word.Length;
        }

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        }

        static Exception Err(string s, int i, string what)
        {
            return new FormatException("JSON@" + i + " " + what + "：" +
                s.Substring(Math.Max(0, i - 10), Math.Min(30, s.Length - Math.Max(0, i - 10))));
        }

        /* ---------------- 序列化 ---------------- */

        public static string Write(object v)
        {
            var sb = new StringBuilder();
            WriteValue(sb, v);
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, object v)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is bool b) { sb.Append(b ? "true" : "false"); return; }
            if (v is string s) { WriteStr(sb, s); return; }
            if (v is IDictionary<string, object> d)
            {
                sb.Append('{');
                bool first = true;
                foreach (var kv in d)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteStr(sb, kv.Key);
                    sb.Append(':');
                    WriteValue(sb, kv.Value);
                }
                sb.Append('}');
                return;
            }
            if (v is IEnumerable<object> a)
            {
                sb.Append('[');
                bool first = true;
                foreach (var e in a)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteValue(sb, e);
                }
                sb.Append(']');
                return;
            }
            if (v is int || v is long) { sb.Append(Convert.ToInt64(v, CultureInfo.InvariantCulture)); return; }
            if (v is double or_) { sb.Append(or_.ToString("R", CultureInfo.InvariantCulture)); return; }
            WriteStr(sb, v.ToString());
        }

        static void WriteStr(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        /* ---------------- 取值助手 ---------------- */

        public static Dictionary<string, object> Obj(object v) => v as Dictionary<string, object>;
        public static List<object> Arr(object v) => v as List<object>;
        public static object Get(Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) ? v : null;
        public static string Strg(Dictionary<string, object> d, string k) => Get(d, k) as string;
        public static bool Bool(Dictionary<string, object> d, string k) => Get(d, k) is true;
        public static int Int(Dictionary<string, object> d, string k, int fallback = 0)
        {
            var v = Get(d, k);
            return v is double ? (int)(double)v : v is bool bb ? (bb ? 1 : 0) : fallback;
        }
        public static int? NullableInt(Dictionary<string, object> d, string k)
        {
            var v = Get(d, k);
            return v is double ? (int)(double)v : (int?)null;
        }
    }
}
