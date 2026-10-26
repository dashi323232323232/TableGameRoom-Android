using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TableGameRoom.Mobile.Net;

namespace TableGameRoom.Mobile.Core
{
    /// <summary>
    /// 网络消息的文本编解码 —— 替代 System.Text.Json。
    ///
    /// Unity 的 JsonUtility 不支持 Dictionary，也不支持顶层 List，
    /// 所以联机协议改用一个**自定义的行式文本格式**：
    ///
    ///   Type=hello
    ///   PlayerName=qy130
    ///   Data.a=1
    ///   Data.b=2
    ///
    /// 规则：
    ///   - 每行 `key=value`，key 里的点号表示嵌套（Data.a → Data["a"]）
    ///   - 值里的 \n \\ = 做转义
    ///   - 复杂对象（Session/Room）用 JsonUtility 序列化后放进特殊字段
    ///
    /// 这样既不用引入第三方库，也保留了完整表达能力。
    /// </summary>
    public static class WireFormat
    {
        public const string SessionKey = "__session";
        public const string RoomKey = "__room";
        private const string DataPrefix = "Data.";

        public static string Encode(NetMessage msg)
        {
            var sb = new StringBuilder();

            void Add(string key, string? value)
            {
                if (string.IsNullOrEmpty(value)) return;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(key).Append('=').Append(Escape(value!));
            }

            Add("Type", msg.Type);
            Add("PlayerId", msg.PlayerId);
            Add("PlayerName", msg.PlayerName);
            Add("RoomId", msg.RoomId);
            Add("Text", msg.Text);
            Add("GameKey", msg.GameKey);

            if (msg.Session != null)
                Add(SessionKey, Json.Serialize(msg.Session));

            if (msg.Room != null)
                Add(RoomKey, Json.Serialize(msg.Room));

            if (msg.Data != null)
            {
                foreach (var kv in msg.Data)
                    Add(DataPrefix + kv.Key, kv.Value);
            }

            return sb.ToString();
        }

        public static NetMessage Decode(string text)
        {
            var msg = new NetMessage();
            if (string.IsNullOrEmpty(text)) return msg;

            foreach (var line in text.Split('\n'))
            {
                if (line.Length == 0) continue;
                var idx = line.IndexOf('=');
                if (idx <= 0) continue;

                var key = line.Substring(0, idx);
                var value = Unescape(line.Substring(idx + 1));

                switch (key)
                {
                    case "Type": msg.Type = value; break;
                    case "PlayerId": msg.PlayerId = value; break;
                    case "PlayerName": msg.PlayerName = value; break;
                    case "RoomId": msg.RoomId = value; break;
                    case "Text": msg.Text = value; break;
                    case "GameKey": msg.GameKey = value; break;

                    case SessionKey:
                        msg.Session = Json.Deserialize<SessionState>(value);
                        break;

                    case RoomKey:
                        msg.Room = Json.Deserialize<RoomInfo>(value);
                        break;

                    default:
                        if (key.StartsWith(DataPrefix))
                        {
                            msg.Data ??= new Dictionary<string, string>();
                            msg.Data[key.Substring(DataPrefix.Length)] = value;
                        }
                        break;
                }
            }

            return msg;
        }

        private static string Escape(string s) =>
            s.Replace("\\", "\\\\").Replace("\n", "\\n");

        private static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;

            var sb = new StringBuilder(s.Length);
            for (var i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    i++;
                    sb.Append(s[i] == 'n' ? '\n' : s[i]);
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }
    }
}
