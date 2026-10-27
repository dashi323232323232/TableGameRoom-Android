using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TableGameRoom.Mobile.Core;
using TableGameRoom.Mobile.Net;
using TableGameRoom.Mobile.UI;

namespace TableGameRoom.Mobile
{
    /// <summary>
    /// 局域网联机页面 —— 对应桌面版 Net/LanUi.cs。
    ///
    /// 三个子页面：
    ///   entry  —— 创建 / 加入会话
    ///   lobby  —— 会话内的房间列表
    ///   room   —— 房间内（成员、游戏、开始）
    ///
    /// 与桌面版共用同一套线协议（标准 JSON + 换行分帧），
    /// 所以 Windows 和 Android 可以互相建房、加入。
    /// </summary>
    public sealed class LanPage : MonoBehaviour
    {
        private const int DefaultPort = 47800;

        private enum Phase { Entry, Lobby, Room }

        private App _app;
        private RectTransform _content;
        private Phase _phase = Phase.Entry;

        // 会话
        private LanServer _server;
        private LanClient _client;
        private CancellationTokenSource _cts;

        private SessionState _session;
        private string _myId = "";
        private string _myName = "";
        private string _myRoomId = "";
        private string _status = "";

        // 邀请
        private readonly List<(string FromName, string RoomId, string RoomName)> _invites =
            new List<(string, string, string)>();

        private bool IsHost => _server != null;

        // ==================== 入口 ====================

        public static void Build(RectTransform content)
        {
            var go = new GameObject("LanPage", typeof(RectTransform));
            go.transform.SetParent(content, false);
            UiFactory.Fill((RectTransform)go.transform);

            go.AddComponent<LanPage>().Init(content);
        }

        private void Init(RectTransform content)
        {
            _app = App.Instance;
            _content = content;
            _myName = Environment.MachineName;
            if (string.IsNullOrWhiteSpace(_myName)) _myName = "安卓玩家";
            if (_myName.Length > 16) _myName = _myName.Substring(0, 16);

            Render();
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        private void Cleanup()
        {
            try { _cts?.Cancel(); } catch { }
            try { _server?.Dispose(); } catch { }
            try { _client?.Dispose(); } catch { }
            _server = null;
            _client = null;
            _cts = null;
        }

        // ==================== 渲染 ====================

        private void Render()
        {
            UiFactory.Clear(_content);
            var stack = new VStack(_content);

            stack.Button("← 返回", () =>
            {
                if (_phase == Phase.Entry) { _app.ShowModeSelect(); return; }

                // 从房间/大厅返回 = 离开会话
                Cleanup();
                _session = null;
                _myRoomId = "";
                _phase = Phase.Entry;
                Render();
            }, 88f);

            switch (_phase)
            {
                case Phase.Entry: RenderEntry(stack); break;
                case Phase.Lobby: RenderLobby(stack); break;
                case Phase.Room: RenderRoom(stack); break;
            }

            stack.Finish();
        }

        private void RenderEntry(VStack stack)
        {
            stack.Title("局域网联机");
            stack.Text("把设备接入同一 Wi-Fi，之后就能创建或加入房间，和 Windows / 安卓互相开黑。",
                Theme.FontSmall, Theme.Muted);

            if (!string.IsNullOrEmpty(_status))
                stack.Text(_status, Theme.FontSmall, Theme.Accent);

            // ---------- 创建会话 ----------
            stack.Head("创建会话");
            stack.Text($"以「{_myName}」为主机开启一个会话。端口默认 {DefaultPort}。",
                Theme.FontSmall, Theme.Muted);

            stack.Button("创建会话", () => CreateSession(DefaultPort), Theme.ButtonHeight, Theme.Accent, Color.white);

            // ---------- 加入会话 ----------
            stack.Head("加入会话");
            stack.Text("自动发现同一 Wi-Fi 下的主机；找不到可手动输入 IP。",
                Theme.FontSmall, Theme.Muted);

            stack.Button("自动搜索主机", () => SearchHost(DefaultPort));

            // 手动输入 IP
            stack.Text("手动输入主机 IP", Theme.FontSmall, Theme.Muted);
            var ipRow = new List<Button>();
            foreach (var preset in new[] { "192.168.1.", "192.168.0.", "10.0.0." })
            {
                var p = preset;
                ipRow.Add(UiFactory.Button(_content, p + "…", () => ManualJoinDialog(p)));
            }
            stack.Grid(ipRow, 3, 76f);
        }

        private void RenderLobby(VStack stack)
        {
            stack.Title(IsHost ? "联机大厅（主机）" : "联机大厅");
            stack.Text($"会话：{_session?.SessionName ?? ""}　·　端口 {_session?.Port ?? 0}",
                Theme.FontSmall, Theme.Muted);

            if (!string.IsNullOrEmpty(_status))
                stack.Text(_status, Theme.FontSmall, Theme.Accent);

            // 邀请
            if (_invites.Count > 0)
            {
                stack.Head("待处理邀请");
                foreach (var inv in _invites.ToList())
                {
                    var captured = inv;
                    var row = new List<Button>
                    {
                        UiFactory.Button(_content, $"来自 {captured.FromName} 的邀请", () => { }),
                        UiFactory.Primary(_content, "接受", () => AcceptInvite(captured)),
                        UiFactory.Button(_content, "拒绝", () =>
                        {
                            _invites.Remove(captured);
                            Render();
                        })
                    };
                    stack.Grid(row, 3, 76f);
                }
            }

            // 房间列表
            stack.Head("房间列表");

            var rooms = _session?.Rooms ?? new List<RoomInfo>();
            if (rooms.Count == 0)
            {
                stack.Text("还没有房间。创建一个吧。", Theme.FontSmall, Theme.Muted);
            }
            else
            {
                foreach (var room in rooms)
                {
                    var captured = room;
                    stack.Card(inner =>
                    {
                        inner.Text(captured.Name, Theme.FontHead, Theme.Ink, bold: true, minHeight: 46f);
                        inner.Text($"{GameName(captured.GameKey)} · {captured.Members.Count} 人 · 桌位 {captured.Seats}",
                            Theme.FontSmall, Theme.Muted);

                        var btns = new List<Button>
                        {
                            UiFactory.Primary(inner.Host, "加入", () => JoinRoom(captured.Id))
                        };
                        inner.Grid(btns, 1, 78f);
                    }, Theme.Surface, Theme.PanelBorder);
                }
            }

            // 创建房间
            stack.Head("创建房间");
            var createBtns = new List<Button>();
            foreach (var key in new[] { "blackjack", "poker", "three", "liar" })
            {
                var k = key;
                createBtns.Add(UiFactory.Button(_content, GameName(k), () => CreateRoom(k)));
            }
            stack.Grid(createBtns, 2, 84f);

            stack.Text($"我：{_myName}（{_myId}）", Theme.FontSmall - 4, Theme.Muted);
        }

        private void RenderRoom(VStack stack)
        {
            var room = _session?.Rooms.FirstOrDefault(r => r.Id == _myRoomId);
            if (room == null)
            {
                _phase = Phase.Lobby;
                Render();
                return;
            }

            var isHost = room.HostId == _myId;

            stack.Title(room.Name);
            stack.Text($"{GameName(room.GameKey)} · 桌位 {room.Seats} · 档位 {TierName(room.TierIndex)}",
                Theme.FontSmall, Theme.Muted);

            if (!string.IsNullOrEmpty(_status))
                stack.Text(_status, Theme.FontSmall, Theme.Accent);

            // 成员列表
            stack.Head("成员");

            foreach (var m in room.Members)
            {
                var member = m;
                var name = MemberName(member.PlayerId);
                var me = member.PlayerId == _myId ? "（你）" : "";
                var host = member.PlayerId == room.HostId ? " 👑" : "";
                var chips = member.Unlimited ? "∞" : $"${member.Chips:N0}";
                var state = member.Playing ? "参战" : "观战";

                stack.Card(inner =>
                {
                    inner.Text($"{name}{me}{host}", Theme.FontBody, Theme.Ink, bold: true, minHeight: 44f);
                    inner.Text($"{state} · {chips}", Theme.FontSmall, Theme.Muted);
                }, Theme.Surface, Theme.PanelBorder);
            }

            // 主机操作
            if (isHost)
            {
                stack.Head("设置");

                var gameBtns = new List<Button>();
                foreach (var key in new[] { "blackjack", "poker", "three", "liar" })
                {
                    var k = key;
                    var picked = room.GameKey == k;
                    gameBtns.Add(UiFactory.Button(_content, GameName(k), () => SetGame(k),
                        picked ? Theme.AccentSoft : Theme.Panel));
                }
                stack.Grid(gameBtns, 2, 78f);

                var seatBtns = new List<Button>();
                for (var n = 2; n <= 7; n++)
                {
                    var v = n;
                    var picked = room.Seats == v;
                    seatBtns.Add(UiFactory.Button(_content, $"{v} 人", () => SetSeats(v),
                        picked ? Theme.AccentSoft : Theme.Panel));
                }
                stack.Grid(seatBtns, 3, 72f);
            }

            // 开始游戏
            if (isHost)
            {
                stack.Button("开始游戏", StartGame, Theme.ButtonHeight, Theme.Accent, Color.white);
            }
            else
            {
                stack.Text("等待房主开始游戏…", Theme.FontSmall, Theme.Muted);
            }
        }

        // ==================== 会话 ====================

        private void CreateSession(int port)
        {
            Cleanup();
            _cts = new CancellationTokenSource();

            _server = new LanServer();
            _server.MessageReceived += OnServerMessage;
            _server.SessionChanged += OnSessionChanged;

            try
            {
                _server.Start($"{_myName} 的会话", _myName);
                _myId = _server.HostPlayerId;
                _session = _server.Snapshot();
                _phase = Phase.Lobby;
                _status = $"已创建会话，端口 {_server.Port}。同一 Wi-Fi 下的设备可搜索到。";

                // 启动 UDP 发现应答
                var token = _cts.Token;
                var sessionPort = _server.Port;
                Task.Run(() => LanDiscovery.StartResponder(sessionPort, token), token);
            }
            catch (Exception ex)
            {
                _status = $"创建失败：{ex.Message}";
                Cleanup();
            }

            Render();
        }

        private async void SearchHost(int port)
        {
            _status = "正在搜索同一 Wi-Fi 下的主机…";
            Render();

            var ip = await LanDiscovery.FindHostAsync(port, 2000);
            if (string.IsNullOrEmpty(ip))
            {
                _status = "没有找到主机。确认双方在同一 Wi-Fi，或手动输入主机 IP。";
                Render();
                return;
            }

            _status = $"找到主机 {ip}，正在连接…";
            Render();
            await JoinSession(ip, port);
        }

        private async void ManualJoinDialog(string prefix)
        {
            // 简单起见：在状态栏提示，并用 IP 输入弹窗
            _status = $"请输入完整 IP（例如 {prefix}5）";
            Render();

            // 用三个预设快速拼接（示例：最后一段 1-20）
            var lastOctets = new List<Button>();
            for (var i = 1; i <= 20; i++)
            {
                var n = i;
                lastOctets.Add(UiFactory.Button(_content, n.ToString(),
                    () => _ = JoinSession($"{prefix}{n}", DefaultPort)));
            }

            // 注意：这里直接追加到当前页面底部（不重建）
            var host = UiFactory.Node(_content, "ManualIpHint");
            var hr = host;
            hr.anchorMin = hr.anchorMax = new Vector2(0.5f, 0f);
            hr.pivot = new Vector2(0.5f, 0f);
            hr.sizeDelta = new Vector2(1000f, 40f);
            hr.anchoredPosition = new Vector2(0f, 20f);

            var tip = UiFactory.Label(_content, $"或直接在主机上查看 IP，用 {prefix} 开头",
                Theme.FontSmall, Theme.Muted, TextAnchor.MiddleCenter);
            var tr = tip.rectTransform;
            tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0f);
            tr.pivot = new Vector2(0.5f, 0f);
            tr.sizeDelta = new Vector2(1000f, 40f);
            tr.anchoredPosition = new Vector2(0f, 70f);
        }

        private async Task JoinSession(string ip, int port)
        {
            Cleanup();
            _cts = new CancellationTokenSource();

            _client = new LanClient();
            _client.Received += OnClientMessage;
            _client.Disconnected += OnDisconnected;

            var ok = await _client.ConnectAsync(ip, port, _myName);
            if (!ok)
            {
                _status = $"连接 {ip}:{port} 失败。确认主机已创建会话、防火墙已放行。";
                Cleanup();
                Render();
                return;
            }

            _myId = _client.PlayerId;
            _phase = Phase.Lobby;
            _status = $"已连接到 {ip}:{port}。";
            Render();
        }

        // ==================== 消息处理 ====================

        private void OnServerMessage(string playerId, NetMessage msg)
        {
            HandleMessage(msg);
        }

        private void OnClientMessage(NetMessage msg)
        {
            HandleMessage(msg);
        }

        private void HandleMessage(NetMessage msg)
        {
            if (msg == null) return;

            // 网络线程 → 主线程
            UnityMainThread.Run(() =>
            {
                switch (msg.Type)
                {
                    case "welcome":
                        if (msg.Session != null)
                        {
                            _session = msg.Session;
                            if (!string.IsNullOrEmpty(msg.PlayerId)) _myId = msg.PlayerId!;
                            Render();
                        }
                        break;

                    case "session":
                        if (msg.Session != null)
                        {
                            _session = msg.Session;
                            // 房间被关闭则回大厅
                            if (!string.IsNullOrEmpty(_myRoomId) &&
                                _session.Rooms.All(r => r.Id != _myRoomId))
                            {
                                _myRoomId = "";
                                _phase = Phase.Lobby;
                            }
                            Render();
                        }
                        break;

                    case "invite":
                        if (msg.RoomId != null)
                        {
                            var roomName = _session?.Rooms.FirstOrDefault(r => r.Id == msg.RoomId)?.Name ?? "房间";
                            _invites.Add((msg.PlayerName ?? "玩家", msg.RoomId, roomName));
                            Render();
                        }
                        break;

                    case "error":
                        _status = msg.Text ?? "发生错误";
                        Render();
                        break;
                }
            });
        }

        private void OnSessionChanged()
        {
            UnityMainThread.Run(() =>
            {
                if (_server == null) return;
                _session = _server.Snapshot();
                Render();
            });
        }

        private void OnDisconnected()
        {
            UnityMainThread.Run(() =>
            {
                _status = "与主机的连接已断开。";
                Cleanup();
                _session = null;
                _myRoomId = "";
                _phase = Phase.Entry;
                Render();
            });
        }

        // ==================== 操作 ====================

        private void Send(NetMessage msg)
        {
            if (IsHost) HandleMessage(msg);
            else _client?.Send(msg);
        }

        private void CreateRoom(string gameKey)
        {
            Send(new NetMessage { Type = "create_room", GameKey = gameKey });
        }

        private void JoinRoom(string roomId)
        {
            _myRoomId = roomId;
            Send(new NetMessage { Type = "join_room", RoomId = roomId });
            _phase = Phase.Room;
            Render();
        }

        private void AcceptInvite((string FromName, string RoomId, string RoomName) inv)
        {
            _invites.Remove(inv);
            JoinRoom(inv.RoomId);
        }

        private void SetGame(string gameKey)
        {
            Send(new NetMessage
            {
                Type = "set_game",
                RoomId = _myRoomId,
                GameKey = gameKey
            });
        }

        private void SetSeats(int seats)
        {
            Send(new NetMessage
            {
                Type = "set_game",
                RoomId = _myRoomId,
                Data = new Dictionary<string, string> { ["seats"] = seats.ToString() }
            });
        }

        private void StartGame()
        {
            Send(new NetMessage { Type = "start_game", RoomId = _myRoomId });
            _status = "游戏已开始（联机对局界面开发中）";
            Render();
        }

        // ==================== 辅助 ====================

        private string MemberName(string playerId)
        {
            if (_session == null) return playerId;
            var p = _session.Players.FirstOrDefault(x => x.Id == playerId);
            return p?.Name ?? playerId;
        }

        private static string GameName(string key)
        {
            switch (key)
            {
                case "blackjack": return "21 点";
                case "poker": return "德州扑克";
                case "three": return "炸金花";
                case "liar": return "骗子扑克";
                default: return key;
            }
        }

        private static string TierName(int index)
        {
            var names = new[] { "休闲", "轻松", "普通", "进阶", "困难", "专家", "大师" };
            return index >= 0 && index < names.Length ? names[index] : $"档位 {index + 1}";
        }
    }
}
