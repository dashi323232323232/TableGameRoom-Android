using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using System.Text;

namespace TableGameRoom.Mobile.Core
{
    /// <summary>
    /// 轻量 JSON 序列化 —— 替代 Unity 里不存在的 System.Text.Json。
    ///
    /// Unity 自带 JsonUtility，但它有两个限制：
    ///   1. 不支持顶层 List（必须包一层）
    ///   2. 不支持 Dictionary
    ///
    /// 这里用 JsonUtility 做基础，加上包装层处理 List；
    /// Dictionary 用简单的 key=value 文本格式（联机协议里只有少量字典字段）。
    /// </summary>
    public static class Json
    {
        /// <summary>序列化单个对象。</summary>
        public static string Serialize(object obj, bool pretty = false)
        {
            if (obj == null) return "{}";
            return JsonUtility.ToJson(obj, pretty);
        }

        /// <summary>反序列化单个对象。</summary>
        public static T Deserialize<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonUtility.FromJson<T>(json); }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Json] 解析失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>序列化 List（包一层容器）。</summary>
        public static string SerializeList<T>(IList<T> list, bool pretty = false)
        {
            var wrapper = new ListWrapper<T> { items = new List<T>(list) };
            return JsonUtility.ToJson(wrapper, pretty);
        }

        /// <summary>反序列化 List。</summary>
        public static List<T> DeserializeList<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<T>();
            try
            {
                var wrapper = JsonUtility.FromJson<ListWrapper<T>>(json);
                return wrapper?.items ?? new List<T>();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Json] 列表解析失败: {ex.Message}");
                return new List<T>();
            }
        }

        [Serializable]
        private class ListWrapper<T>
        {
            public List<T> items = new List<T>();
        }

        // ---------- Dictionary 的极简文本格式 ----------

        /// <summary>把字典编码成 "k1=v1\nk2=v2"（值里的换行/等号做转义）。</summary>
        public static string EncodeDict(Dictionary<string, string> dict)
        {
            if (dict == null || dict.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var kv in dict)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(Escape(kv.Key)).Append('=').Append(Escape(kv.Value ?? ""));
            }
            return sb.ToString();
        }

        /// <summary>解码 "k1=v1\nk2=v2"。</summary>
        public static Dictionary<string, string> DecodeDict(string text)
        {
            var dict = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(text)) return dict;

            foreach (var line in text.Split('\n'))
            {
                if (line.Length == 0) continue;
                var idx = line.IndexOf('=');
                if (idx < 0) continue;
                dict[Unescape(line.Substring(0, idx))] = Unescape(line.Substring(idx + 1));
            }
            return dict;
        }

        private static string Escape(string s) =>
            s.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("=", "\\e");

        private static string Unescape(string s)
        {
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    i++;
                    sb.Append(s[i] switch
                    {
                        'n' => '\n',
                        'e' => '=',
                        '\\' => '\\',
                        _ => s[i]
                    });
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
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
