using System;
using System.Collections.Generic;
using System.Linq;
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
    /// 三个子页面：entry（创建/加入会话）→ lobby（房间列表）→ room（房间内）
    ///
    /// 连接和状态由 <see cref="LanSession"/> 单例维护，本类只负责显示。
    /// 游戏对局由 <see cref="LanGameRouter"/> 分发到具体游戏页。
    /// </summary>
    public sealed class LanPage : MonoBehaviour
    {
        private const int DefaultPort = 47800;

        private enum Phase { Entry, Lobby, Room }

        private App _app;
        private RectTransform _content;
        private Phase _phase = Phase.Entry;
        private string _status = "";

        private readonly List<(string FromName, string RoomId, string RoomName)> _invites =
            new List<(string, string, string)>();

        private LanSession S => LanSession.Instance;

        // ==================== 入口 ====================

        public static void Build(RectTransform content)
        {
            // 注意：LanPage 的 GameObject 不能挂在 content 下面！
            // Render() 会 UiFactory.Clear(_content)，那会把 LanPage 自己也删掉，
            // 导致 OnDisable 触发、事件订阅被退掉（表现为 SessionChanged 不刷新界面）。
            // 所以挂在 content 的父级（ScrollRoot）上，与 content 平级。
            var parent = content.parent ?? content;

            var go = new GameObject("LanPage", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiFactory.Fill((RectTransform)go.transform);

            go.AddComponent<LanPage>().Init(content);
        }

        private void Init(RectTransform content)
        {
            _app = App.Instance;
            _content = content;

            if (string.IsNullOrEmpty(S.MyName))
            {
                S.MyName = Environment.MachineName;
                if (string.IsNullOrWhiteSpace(S.MyName)) S.MyName = "安卓玩家";
                if (S.MyName.Length > 16) S.MyName = S.MyName.Substring(0, 16);
            }

            // 已连接则直接进大厅
            if (S.Connected) _phase = string.IsNullOrEmpty(S.MyRoomId) ? Phase.Lobby : Phase.Room;

            // 订阅在 OnEnable 里做（生命周期更可靠）
            Render();
        }

        private bool _subscribed;

        private void OnEnable()
        {
            if (_subscribed) return;
            S.SessionChanged += OnSessionChanged;
            S.GameMessage += OnGameMessage;
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (!_subscribed) return;
            S.SessionChanged -= OnSessionChanged;
            S.GameMessage -= OnGameMessage;
            _subscribed = false;
        }

        private void OnDestroy()
        {
            S.SessionChanged -= OnSessionChanged;
            S.GameMessage -= OnGameMessage;
            _subscribed = false;
        }

        private void OnSessionChanged()
        {
            if (this == null || !isActiveAndEnabled) return;

            if (S.CurrentRoom != null)
            {
                _phase = Phase.Room;
            }
            else if (_phase == Phase.Room)
            {
                _phase = Phase.Lobby;
            }

            Render();
        }

        private void OnGameMessage(NetMessage msg)
        {
            if (msg.Type == "invite" && msg.RoomId != null)
            {
                var roomName = S.Session?.Rooms.FirstOrDefault(r => r.Id == msg.RoomId)?.Name ?? "房间";
                _invites.Add((msg.PlayerName ?? "玩家", msg.RoomId, roomName));
                Render();
            }
        }

        // ==================== 渲染 ====================

        private void Render()
        {
            UiFactory.Clear(_content);
            var stack = new VStack(_content);

            stack.Button("← 返回", () =>
            {
                if (_phase == Phase.Entry) { _app.ShowModeSelect(); return; }

                S.LeaveRoom();
                S.Disconnect();
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
            stack.Text("把设备接入同一 Wi-Fi，就能和 Windows / 安卓互相开黑。",
                Theme.FontSmall, Theme.Muted);

            if (!string.IsNullOrEmpty(_status))
                stack.Text(_status, Theme.FontSmall, Theme.Accent);

            stack.Head("创建会话");
            stack.Text($"以「{S.MyName}」为主机开启会话。端口默认 {DefaultPort}。",
                Theme.FontSmall, Theme.Muted);
            stack.Button("创建会话", () => CreateSession(DefaultPort), Theme.ButtonHeight,
                Theme.Accent, Color.white);

            stack.Head("加入会话");
            stack.Text("自动发现同一 Wi-Fi 下的主机；找不到可手动输入 IP。",
                Theme.FontSmall, Theme.Muted);
            stack.Button("自动搜索主机", () => SearchHost(DefaultPort));

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
            stack.Title(S.IsHost ? "联机大厅（主机）" : "联机大厅");
            stack.Text($"会话：{S.Session?.SessionName ?? ""}　·　端口 {S.Session?.Port ?? 0}",
                Theme.FontSmall, Theme.Muted);

            if (!string.IsNullOrEmpty(_status))
                stack.Text(_status, Theme.FontSmall, Theme.Accent);

            if (_invites.Count > 0)
            {
                stack.Head("待处理邀请");
                foreach (var inv in _invites.ToList())
                {
                    var captured = inv;
                    var row = new List<Button>
                    {
                        UiFactory.Button(_content, $"来自 {captured.FromName}", () => { }),
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

            stack.Head("房间列表");
            var rooms = S.Session?.Rooms ?? new List<RoomInfo>();

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

            // 只有会话主机能建房 —— 游戏规则跑在会话主机上，
            // 如果客户端当房主，没人会去计算牌局（两端都会卡在"等待发牌"）。
            if (S.IsHost)
            {
                stack.Head("创建房间");
                var createBtns = new List<Button>();
                foreach (var key in new[] { "blackjack", "poker", "three", "liar" })
                {
                    var k = key;
                    createBtns.Add(UiFactory.Button(_content, GameName(k), () => CreateRoom(k)));
                }
                stack.Grid(createBtns, 2, 84f);
            }
            else
            {
                stack.Text("（只有会话主机能创建房间；加入主机开的房间即可）",
                    Theme.FontSmall, Theme.Muted);
            }

            stack.Text($"我：{S.MyName}（{S.MyId}）", Theme.FontSmall - 4, Theme.Muted);
        }

        private void RenderRoom(VStack stack)
        {
            var room = S.CurrentRoom;
            if (room == null)
            {
                _phase = Phase.Lobby;
                Render();
                return;
            }

            var isHost = room.HostId == S.MyId;

            stack.Title(room.Name);
            stack.Text($"{GameName(room.GameKey)} · 桌位 {room.Seats} · 档位 {TierName(room.TierIndex)}",
                Theme.FontSmall, Theme.Muted);

            if (!string.IsNullOrEmpty(_status))
                stack.Text(_status, Theme.FontSmall, Theme.Accent);

            stack.Head("成员");
            foreach (var m in room.Members)
            {
                var member = m;
                var name = MemberName(member.PlayerId);
                var me = member.PlayerId == S.MyId ? "（你）" : "";
                var host = member.PlayerId == room.HostId ? " 👑" : "";
                var chips = member.Unlimited ? "∞" : $"${member.Chips:N0}";
                var state = member.Playing ? "参战" : "观战";

                stack.Card(inner =>
                {
                    inner.Text($"{name}{me}{host}", Theme.FontBody, Theme.Ink, bold: true, minHeight: 44f);
                    inner.Text($"{state} · {chips}", Theme.FontSmall, Theme.Muted);
                }, Theme.Surface, Theme.PanelBorder);
            }

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
            try
            {
                S.CreateSession(S.MyName, port);
                _phase = Phase.Lobby;
                _status = $"已创建会话，端口 {S.Session?.Port ?? port}。";
            }
            catch (Exception ex)
            {
                _status = $"创建失败：{ex.Message}";
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
                _status = "没找到主机。确认双方在同一 Wi-Fi，或手动输入 IP。";
                Render();
                return;
            }

            _status = $"找到主机 {ip}，正在连接…";
            Render();

            var ok = await S.JoinSession(ip, port, S.MyName);
            if (ok)
            {
                _phase = Phase.Lobby;
                _status = $"已连接到 {ip}:{port}。";
            }
            else
            {
                _status = $"连接 {ip}:{port} 失败。";
            }
            Render();
        }

        private async void ManualJoinDialog(string prefix)
        {
            _status = $"正在尝试 {prefix}1 ~ {prefix}20 …";
            Render();

            for (var i = 1; i <= 20; i++)
            {
                var ip = $"{prefix}{i}";
                var ok = await S.JoinSession(ip, DefaultPort, S.MyName);
                if (ok)
                {
                    _phase = Phase.Lobby;
                    _status = $"已连接到 {ip}:{DefaultPort}。";
                    Render();
                    return;
                }
            }

            _status = $"未能在 {prefix}1-20 找到主机。";
            Render();
        }

        private void AcceptInvite((string FromName, string RoomId, string RoomName) inv)
        {
            _invites.Remove(inv);
            JoinRoom(inv.RoomId);
        }

        // ==================== 操作 ====================

        private void CreateRoom(string gameKey)
        {
            S.CreateRoom(gameKey);
        }

        private void JoinRoom(string roomId)
        {
            S.JoinRoom(roomId);

            // 不要立刻切到 Room：此时服务端还没广播回来，S.CurrentRoom 是 null，
            // RenderRoom 会误判"房间不存在"而弹回大厅。
            // 先留在 Lobby，等 SessionChanged 触发时再根据 CurrentRoom 自动切换。
            _status = "正在加入房间…";
            Render();
        }

        private void SetGame(string gameKey)
        {
            S.Send(new NetMessage
            {
                Type = "set_game",
                RoomId = S.MyRoomId,
                GameKey = gameKey
            });

            // 换游戏 = 放弃当前对局，清掉对局页
            LanGameRouter.Instance.LeaveGame();
        }

        private void SetSeats(int seats)
        {
            S.Send(new NetMessage
            {
                Type = "set_game",
                RoomId = S.MyRoomId,
                Data = new Dictionary<string, string> { ["seats"] = seats.ToString() }
            });
        }

        private void StartGame()
        {
            S.StartGame();

            // 主机本地立即进入对局；客户端等 session 广播后由 LanGameRouter 自动进入
            var room = S.CurrentRoom;
            if (room != null && S.IsHost)
                LanGameRouter.Instance.EnterGame(room);
        }

        // ==================== 辅助 ====================

        private string MemberName(string playerId)
        {
            var p = S.Session?.Players.FirstOrDefault(x => x.Id == playerId);
            return p?.Name ?? playerId;
        }

        internal static string GameName(string key)
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
