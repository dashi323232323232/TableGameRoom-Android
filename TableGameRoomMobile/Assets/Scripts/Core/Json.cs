using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace TableGameRoom.Mobile.Core
{
    /// <summary>
    /// 轻量 JSON 序列化 —— 替代 Unity 里不存在的 System.Text.Json。
    ///
    /// 为什么必须自己写：
    ///   - Unity 的 JsonUtility 不支持 Dictionary、不支持顶层 List、
    ///     不支持 null 值字段，无法表达联机协议
    ///   - System.Text.Json 在 Unity 的 .NET Standard 2.1 下不可用
    ///   - **双端联机要求 Android 与 Windows（System.Text.Json）线格式完全一致**，
    ///     所以这里必须产出标准 JSON，不能再用自定义行式文本
    ///
    /// 实现：反射遍历公共属性/字段，手写递归下降解析器。
    /// 覆盖协议用到的类型（string / int / bool / List / Dictionary / 嵌套对象）。
    /// </summary>
    public static class Json
    {
        // ==================== 序列化 ====================

        public static string Serialize(object obj, bool pretty = false)
        {
            var sb = new StringBuilder();
            WriteValue(sb, obj, pretty ? 0 : -1);
            return sb.ToString();
        }

        public static string SerializeList<T>(IList<T> list, bool pretty = false)
        {
            var sb = new StringBuilder();
            WriteValue(sb, list, pretty ? 0 : -1);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object value, int indent)
        {
            if (value == null) { sb.Append("null"); return; }

            switch (value)
            {
                case string s: WriteString(sb, s); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); return;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); return;
                case float f: sb.Append(f.ToString("R", CultureInfo.InvariantCulture)); return;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); return;
                case decimal m: sb.Append(m.ToString(CultureInfo.InvariantCulture)); return;
            }

            var pretty = indent >= 0;
            var inner = pretty ? indent + 1 : -1;

            // Dictionary
            if (value is IDictionary dict)
            {
                sb.Append('{');
                var first = true;
                foreach (DictionaryEntry entry in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    NewLine(sb, inner);
                    WriteString(sb, Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? "");
                    sb.Append(pretty ? ": " : ":");
                    WriteValue(sb, entry.Value, inner);
                }
                NewLine(sb, indent);
                sb.Append('}');
                return;
            }

            // List / 数组
            if (value is IEnumerable enumerable)
            {
                sb.Append('[');
                var first = true;
                foreach (var item in enumerable)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    NewLine(sb, inner);
                    WriteValue(sb, item, inner);
                }
                NewLine(sb, indent);
                sb.Append(']');
                return;
            }

            // 普通对象 → 公共属性 + 公共字段
            sb.Append('{');
            var firstMember = true;

            foreach (var prop in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead) continue;
                if (prop.GetIndexParameters().Length > 0) continue;

                var v = prop.GetValue(value);
                if (v == null) continue;   // 与 System.Text.Json 默认行为一致（忽略 null）

                if (!firstMember) sb.Append(',');
                firstMember = false;
                NewLine(sb, inner);
                WriteString(sb, prop.Name);
                sb.Append(pretty ? ": " : ":");
                WriteValue(sb, v, inner);
            }

            foreach (var field in value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var v = field.GetValue(value);
                if (v == null) continue;

                if (!firstMember) sb.Append(',');
                firstMember = false;
                NewLine(sb, inner);
                WriteString(sb, field.Name);
                sb.Append(pretty ? ": " : ":");
                WriteValue(sb, v, inner);
            }

            NewLine(sb, indent);
            sb.Append('}');
        }

        private static void NewLine(StringBuilder sb, int indent)
        {
            if (indent < 0) return;
            sb.Append('\n');
            for (var i = 0; i < indent; i++) sb.Append("  ");
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ==================== 反序列化 ====================

        public static T Deserialize<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                var idx = 0;
                var node = ParseValue(json, ref idx);
                return (T)ConvertTo(node, typeof(T));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[Json] 解析失败: {ex.Message}");
                return null;
            }
        }

        public static List<T> DeserializeList<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<T>();

            try
            {
                var idx = 0;
                var node = ParseValue(json, ref idx);
                return ConvertTo(node, typeof(List<T>)) as List<T> ?? new List<T>();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[Json] 列表解析失败: {ex.Message}");
                return new List<T>();
            }
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON 意外结束");

            switch (s[i])
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't': Expect(s, ref i, "true"); return true;
                case 'f': Expect(s, ref i, "false"); return false;
                case 'n': Expect(s, ref i, "null"); return null;
                default: return ParseNumber(s, ref i);
            }
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var dict = new Dictionary<string, object>();
            i++; // {
            SkipWs(s, ref i);

            if (i < s.Length && s[i] == '}') { i++; return dict; }

            while (i < s.Length)
            {
                SkipWs(s, ref i);
                var key = ParseString(s, ref i);
                SkipWs(s, ref i);

                if (i >= s.Length || s[i] != ':') throw new FormatException("JSON 缺少冒号");
                i++;

                dict[key] = ParseValue(s, ref i);
                SkipWs(s, ref i);

                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; break; }
                throw new FormatException("JSON 对象格式错误");
            }

            return dict;
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var list = new List<object>();
            i++; // [
            SkipWs(s, ref i);

            if (i < s.Length && s[i] == ']') { i++; return list; }

            while (i < s.Length)
            {
                list.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);

                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; break; }
                throw new FormatException("JSON 数组格式错误");
            }

            return list;
        }

        private static string ParseString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("JSON 期望字符串");
            i++;

            var sb = new StringBuilder();
            while (i < s.Length)
            {
                var c = s[i++];
                if (c == '"') return sb.ToString();

                if (c == '\\' && i < s.Length)
                {
                    var e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (i + 4 <= s.Length)
                            {
                                var hex = s.Substring(i, 4);
                                i += 4;
                                sb.Append((char)Convert.ToInt32(hex, 16));
                            }
                            break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }

            throw new FormatException("JSON 字符串未闭合");
        }

        private static object ParseNumber(string s, ref int i)
        {
            var start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' ||
                                    s[i] == '-' || s[i] == '+'))
                i++;

            var text = s.Substring(start, i - start);

            if (text.IndexOf('.') >= 0 || text.IndexOf('e') >= 0 || text.IndexOf('E') >= 0)
                return double.Parse(text, CultureInfo.InvariantCulture);

            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                return l;

            return 0L;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static void Expect(string s, ref int i, string token)
        {
            if (i + token.Length > s.Length || s.Substring(i, token.Length) != token)
                throw new FormatException($"JSON 期望 {token}");
            i += token.Length;
        }

        // ==================== 类型转换 ====================

        private static object ConvertTo(object node, Type target)
        {
            if (node == null)
                return target.IsValueType ? Activator.CreateInstance(target) : null;

            if (target == typeof(string)) return node.ToString();

            if (target == typeof(int)) return Convert.ToInt32(node, CultureInfo.InvariantCulture);
            if (target == typeof(long)) return Convert.ToInt64(node, CultureInfo.InvariantCulture);
            if (target == typeof(bool)) return Convert.ToBoolean(node, CultureInfo.InvariantCulture);
            if (target == typeof(float)) return Convert.ToSingle(node, CultureInfo.InvariantCulture);
            if (target == typeof(double)) return Convert.ToDouble(node, CultureInfo.InvariantCulture);

            var underlying = Nullable.GetUnderlyingType(target);
            if (underlying != null) return ConvertTo(node, underlying);

            if (target.IsGenericType && target.GetGenericTypeDefinition() == typeof(List<>))
            {
                var elemType = target.GetGenericArguments()[0];
                var list = (IList)Activator.CreateInstance(target);
                if (node is List<object> arr)
                {
                    foreach (var item in arr) list.Add(ConvertTo(item, elemType));
                }
                return list;
            }

            if (target.IsGenericType && target.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var args = target.GetGenericArguments();
                var dict = (IDictionary)Activator.CreateInstance(target);
                if (node is Dictionary<string, object> obj)
                {
                    foreach (var kv in obj) dict[kv.Key] = ConvertTo(kv.Value, args[1]);
                }
                return dict;
            }

            if (node is Dictionary<string, object> map)
            {
                var instance = Activator.CreateInstance(target);

                foreach (var kv in map)
                {
                    var prop = target.GetProperty(kv.Key, BindingFlags.Public | BindingFlags.Instance);
                    if (prop != null && prop.CanWrite)
                    {
                        try { prop.SetValue(instance, ConvertTo(kv.Value, prop.PropertyType)); }
                        catch { /* 类型不匹配就跳过 */ }
                        continue;
                    }

                    var field = target.GetField(kv.Key, BindingFlags.Public | BindingFlags.Instance);
                    if (field != null)
                    {
                        try { field.SetValue(instance, ConvertTo(kv.Value, field.FieldType)); }
                        catch { /* 同上 */ }
                    }
                }

                return instance;
            }

            return null;
        }
    }

    /// <summary>
    /// C# 9 的 record / init 需要的编译器标记类型。
    /// Unity 的 .NET Standard 2.1 里没有，手动补一个。
    /// </summary>
    internal static class IsExternalInitShim { }
}

namespace System.Runtime.CompilerServices
{
    /// <summary>record / init 所需的编译器特性（Unity 缺失）。</summary>
    internal static class IsExternalInit { }
}
