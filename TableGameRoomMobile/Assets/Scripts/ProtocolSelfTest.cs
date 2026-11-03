using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TableGameRoom.Mobile.Core;
using TableGameRoom.Mobile.Net;

namespace TableGameRoom.Mobile
{
    /// <summary>
    /// 联机协议自检 —— 在设备上验证 Android 的 JSON 编解码与 Windows 端兼容。
    ///
    /// 用 BuildScript 的 -executeMethod 调用，或加一个调试入口触发。
    /// 检查项：
    ///   1. NetMessage 编码出来的 JSON 与 System.Text.Json 的格式一致（PascalCase、忽略 null）
    ///   2. 能解析 Windows 端发出的 JSON
    ///   3. 嵌套的 Session / Room / List / Dictionary 都能往返
    /// </summary>
    public static class ProtocolSelfTest
    {
        public static string Run()
        {
            var sb = new StringBuilder();
            var pass = 0;
            var fail = 0;

            void Check(string name, bool ok, string detail = "")
            {
                if (ok) { pass++; sb.AppendLine($"  ✓ {name}"); }
                else { fail++; sb.AppendLine($"  ✗ {name}  {detail}"); }
            }

            sb.AppendLine("=== 联机协议自检 ===");

            // ---------- 1. 简单消息 ----------
            var hello = new NetMessage { Type = "hello", PlayerName = "测试玩家" };
            var helloJson = WireFormat.Encode(hello);
            sb.AppendLine($"  hello → {helloJson}");

            Check("字段名 PascalCase", helloJson.Contains("\"Type\":\"hello\""));
            Check("null 字段被忽略", !helloJson.Contains("RoomId"));
            Check("中文不乱码", helloJson.Contains("测试玩家"));

            var back = WireFormat.Decode(helloJson);
            Check("hello 往返 Type", back.Type == "hello", $"得到 {back.Type}");
            Check("hello 往返 PlayerName", back.PlayerName == "测试玩家", $"得到 {back.PlayerName}");

            // ---------- 2. 解析 Windows 端发出的 JSON ----------
            const string fromWindows =
                "{\"Type\":\"welcome\",\"PlayerId\":\"abc12345\",\"Session\":{" +
                "\"SessionName\":\"qy130 的会话\",\"Port\":47800," +
                "\"Players\":[{\"Id\":\"abc12345\",\"Name\":\"qy130\",\"IsSessionHost\":true}]," +
                "\"Rooms\":[{\"Id\":\"r1\",\"Name\":\"测试房\",\"HostId\":\"abc12345\"," +
                "\"Members\":[{\"PlayerId\":\"abc12345\",\"Name\":\"qy130\",\"Playing\":true," +
                "\"Chips\":10000,\"Unlimited\":true,\"Busted\":false}]," +
                "\"GameKey\":\"blackjack\",\"TierIndex\":2,\"Chips\":10000,\"TurnSeconds\":30," +
                "\"State\":\"waiting\",\"Seats\":5}]}}";

            var w = WireFormat.Decode(fromWindows);
            Check("解析 Windows welcome.Type", w.Type == "welcome", $"得到 '{w.Type}'");
            Check("解析 Windows welcome.PlayerId", w.PlayerId == "abc12345", $"得到 '{w.PlayerId}'");
            Check("解析 Session 非空", w.Session != null);
            Check("解析 SessionName", w.Session?.SessionName == "qy130 的会话", $"得到 '{w.Session?.SessionName}'");
            Check("解析 Port", w.Session?.Port == 47800, $"得到 {w.Session?.Port}");
            Check("解析 Players 数量", w.Session?.Players?.Count == 1, $"得到 {w.Session?.Players?.Count}");
            Check("解析 Player.IsSessionHost", w.Session?.Players?[0].IsSessionHost == true);
            Check("解析 Rooms 数量", w.Session?.Rooms?.Count == 1, $"得到 {w.Session?.Rooms?.Count}");
            Check("解析 Room.Seats", w.Session?.Rooms?[0].Seats == 5, $"得到 {w.Session?.Rooms?[0].Seats}");
            Check("解析 Member.Unlimited", w.Session?.Rooms?[0].Members?[0].Unlimited == true);
            Check("解析 Member.Chips", w.Session?.Rooms?[0].Members?[0].Chips == 10000,
                $"得到 {w.Session?.Rooms?[0].Members?[0].Chips}");

            // ---------- 3. Data 字典 ----------
            var withData = new NetMessage
            {
                Type = "set_chips",
                RoomId = "r1",
                Data = new Dictionary<string, string>
                {
                    ["playerId"] = "abc12345",
                    ["chips"] = "-1"
                }
            };
            var dataJson = WireFormat.Encode(withData);
            sb.AppendLine($"  set_chips → {dataJson}");

            var dataBack = WireFormat.Decode(dataJson);
            Check("Data 往返 playerId", dataBack.Data?["playerId"] == "abc12345");
            Check("Data 往返 chips", dataBack.Data?["chips"] == "-1");

            // ---------- 4. 特殊字符转义 ----------
            var tricky = new NetMessage
            {
                Type = "chat",
                Text = "引号\" 反斜杠\\ 换行\n 制表\t"
            };
            var trickyJson = WireFormat.Encode(tricky);
            var trickyBack = WireFormat.Decode(trickyJson);
            Check("转义往返", trickyBack.Text == tricky.Text, $"得到 '{trickyBack.Text}'");

            // ---------- 5. 完整会话往返 ----------
            var state = new SessionState
            {
                SessionName = "自检会话",
                Port = 47800,
                Players = new List<PlayerInfo>
                {
                    new PlayerInfo { Id = "p1", Name = "主机", IsSessionHost = true },
                    new PlayerInfo { Id = "p2", Name = "玩家2", IsSessionHost = false }
                },
                Rooms = new List<RoomInfo>
                {
                    new RoomInfo
                    {
                        Id = "room1",
                        Name = "房间1",
                        HostId = "p1",
                        GameKey = "poker",
                        TierIndex = 3,
                        Chips = 20000,
                        TurnSeconds = 45,
                        State = "playing",
                        Seats = 6,
                        Members = new List<RoomMember>
                        {
                            new RoomMember { PlayerId = "p1", Playing = true, Chips = 20000 },
                            new RoomMember { PlayerId = "p2", Playing = false, Chips = 5000, Busted = true }
                        }
                    }
                }
            };

            var stateMsg = new NetMessage { Type = "session", Session = state };
            var stateJson = WireFormat.Encode(stateMsg);
            var stateBack = WireFormat.Decode(stateJson);

            Check("会话往返 Players", stateBack.Session?.Players?.Count == 2);
            Check("会话往返 Rooms", stateBack.Session?.Rooms?.Count == 1);
            Check("会话往返 GameKey", stateBack.Session?.Rooms?[0].GameKey == "poker");
            Check("会话往返 TierIndex", stateBack.Session?.Rooms?[0].TierIndex == 3);
            Check("会话往返 TurnSeconds", stateBack.Session?.Rooms?[0].TurnSeconds == 45);
            Check("会话往返 Members", stateBack.Session?.Rooms?[0].Members?.Count == 2);
            Check("会话往返 Busted", stateBack.Session?.Rooms?[0].Members?[1].Busted == true);
            Check("会话往返 Playing", stateBack.Session?.Rooms?[0].Members?[1].Playing == false);

            // ---------- 汇总 ----------
            sb.AppendLine($"=== 结果：{pass} 通过，{fail} 失败 ===");

            var result = sb.ToString();
            Debug.Log(result);
            return result;
        }
    }
}
