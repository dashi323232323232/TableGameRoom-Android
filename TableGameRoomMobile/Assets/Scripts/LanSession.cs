using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TableGameRoom.Mobile.Core;
using TableGameRoom.Mobile.Net;

namespace TableGameRoom.Mobile
{
    /// <summary>
    /// 联机会话单例 —— 让多个页面（联机大厅 / 房间 / 各游戏对局）共享同一个连接。
    ///
    /// 之前这些状态都塞在 LanPage 里，页面一切换连接就断了。
    /// 现在抽出来做成单例，页面只负责显示，连接和状态由这里维护。
    ///
    /// 主机权威：房主跑 LanServer，其他人跑 LanClient。
    /// 所有游戏状态变更都由房主计算后广播，其他人只渲染。
    /// </summary>
    public sealed class LanSession
    {
        private static LanSession _instance;
        public static LanSession Instance => _instance ??= new LanSession();

        // ---------- 连接 ----------
        public LanServer Server { get; private set; }
        public LanClient Client { get; private set; }
        public CancellationTokenSource Cts { get; private set; }

        public bool IsHost => Server != null;
        public bool Connected => Server != null || Client != null;

        public string MyId { get; private set; } = "";
        public string MyName { get; set; } = "";

        // ---------- 会话状态 ----------
        public SessionState Session { get; private set; }
        public string MyRoomId { get; set; } = "";

        // ---------- 事件 ----------
        /// <summary>会话状态更新（玩家加入/离开、房间变化）。</summary>
        public event Action SessionChanged;

        /// <summary>收到游戏消息（bj_state / poker_state 等）。</summary>
        public event Action<NetMessage> GameMessage;

        /// <summary>连接断开。</summary>
        public event Action Disconnected;

        /// <summary>调试用：SessionChanged 的订阅者数量。</summary>
        public int SessionSubscriberCount =>
            SessionChanged?.GetInvocationList().Length ?? 0;

        // ==================== 连接管理 ====================

        public void CreateSession(string name, int port)
        {
            Cleanup();

            MyName = name;
            Cts = new CancellationTokenSource();

            Server = new LanServer();
            Server.MessageReceived += OnServerMessage;
            Server.SessionChanged += OnSessionChangedInternal;
            Server.Start($"{name} 的局域网", name);

            MyId = Server.HostPlayerId;
            Session = Server.Snapshot();

            var token = Cts.Token;
            var sessionPort = Server.Port;
            Task.Run(() => LanDiscovery.StartResponder(sessionPort, token), token);
        }

        public async Task<bool> JoinSession(string ip, int port, string name)
        {
            Cleanup();

            MyName = name;
            Cts = new CancellationTokenSource();

            // 用 TaskCompletionSource 等 welcome 到达（里面带 PlayerId + 完整会话状态）
            var welcomed = new TaskCompletionSource<bool>();
            _welcomeTcs = welcomed;

            Client = new LanClient();
            Client.Received += msg =>
            {
                if (msg.Type == "welcome")
                {
                    // 先把状态存下来，再通知等待方
                    if (msg.Session != null) Session = msg.Session;
                    if (!string.IsNullOrEmpty(msg.PlayerId)) MyId = msg.PlayerId!;
                    welcomed.TrySetResult(true);
                }
                OnClientMessage(msg);
            };
            Client.Disconnected += OnClientDisconnected;

            var ok = await Client.ConnectAsync(ip, port, name);
            if (!ok)
            {
                _welcomeTcs = null;
                Cleanup();
                return false;
            }

            // 等 welcome（最多 5 秒）
            var completed = await Task.WhenAny(welcomed.Task, Task.Delay(5000));
            _welcomeTcs = null;

            if (completed != welcomed.Task || !welcomed.Task.Result)
            {
                Cleanup();
                return false;
            }

            // 兜底：如果 welcome 里没带 PlayerId，用客户端的
            if (string.IsNullOrEmpty(MyId)) MyId = Client.PlayerId;

            return true;
        }

        private TaskCompletionSource<bool> _welcomeTcs;

        public void Disconnect()
        {
            Cleanup();
            Session = null;
            MyRoomId = "";
            SessionChanged?.Invoke();
        }

        private void Cleanup()
        {
            try { Cts?.Cancel(); } catch { }
            try { Server?.Dispose(); } catch { }
            try { Client?.Dispose(); } catch { }
            Server = null;
            Client = null;
            Cts = null;
            MyId = "";
        }

        // ==================== 发送 ====================

        /// <summary>发送消息到当前房间（主机本地直接处理）。</summary>
        public void SendToRoom(NetMessage msg)
        {
            if (string.IsNullOrEmpty(MyRoomId)) return;
            msg.RoomId = MyRoomId;

            if (IsHost) OnServerMessage(MyId, msg);
            else Client?.Send(msg);
        }

        /// <summary>发送任意消息。</summary>
        public void Send(NetMessage msg)
        {
            if (IsHost) OnServerMessage(MyId, msg);
            else Client?.Send(msg);
        }

        /// <summary>主机广播游戏消息给所有人（包括自己）。</summary>
        public void BroadcastGameMessage(NetMessage msg)
        {
            if (IsHost)
            {
                // 发给所有客户端
                Server?.BroadcastRaw(msg);

                // 主机本地也要处理（BroadcastRaw 只发给客户端，不经过 HandleMessage）
                HandleMessage(msg);
            }
            else
            {
                Client?.Send(msg);
            }
        }

        // ==================== 接收 ====================

        private void OnServerMessage(string playerId, NetMessage msg)
        {
            UnityMainThread.Run(() =>
            {
                HandleMessage(msg);

                // 远程玩家的操作后刷新界面
                if (playerId != MyId && Server != null)
                {
                    Session = Server.Snapshot();
                    SessionChanged?.Invoke();
                }
            });
        }

        private void OnClientMessage(NetMessage msg)
        {
            UnityMainThread.Run(() => HandleMessage(msg));
        }

        private void OnSessionChangedInternal()
        {
            UnityMainThread.Run(() =>
            {
                if (Server == null) return;
                Session = Server.Snapshot();
                SessionChanged?.Invoke();
            });
        }

        private void OnClientDisconnected()
        {
            UnityMainThread.Run(() =>
            {
                Cleanup();
                Session = null;
                MyRoomId = "";
                Disconnected?.Invoke();
                SessionChanged?.Invoke();
            });
        }

        private void HandleMessage(NetMessage msg)
        {
            if (msg == null) return;

            switch (msg.Type)
            {
                case "welcome":
                    if (msg.Session != null) Session = msg.Session;
                    if (!string.IsNullOrEmpty(msg.PlayerId)) MyId = msg.PlayerId!;
                    SessionChanged?.Invoke();
                    break;

                case "session":
                    if (msg.Session != null)
                    {
                        Session = msg.Session;
                        foreach (var r in Session.Rooms)
                        if (!string.IsNullOrEmpty(MyRoomId) &&
                            Session.Rooms.All(r => r.Id != MyRoomId))
                        {
                            MyRoomId = "";
                        }
                        SessionChanged?.Invoke();
                    }
                    break;

                default:                    // 游戏相关消息（bj_state / poker_state / three_state / liar_state 等）
                    GameMessage?.Invoke(msg);
                    break;
            }
        }

        /// <summary>请求主机广播会话状态（房间信息变更后调用）。</summary>
        public void RequestSessionBroadcast()
        {
            if (IsHost) Server?.BroadcastSession();
        }

        /// <summary>主机把游戏消息发给指定玩家（含自己）。</summary>
        public void SendGameToPlayer(string playerId, NetMessage msg)
        {
            if (!IsHost) return;

            if (playerId == MyId)
            {
                DeliverLocalGameMessage(msg);
                return;
            }

            Server?.SendTo(playerId, msg);
        }

        /// <summary>主机本地直接投递一条游戏消息（不走网络）。</summary>
        public void DeliverLocalGameMessage(NetMessage msg)
        {
            UnityMainThread.Run(() => GameMessage?.Invoke(msg));
        }

        /// <summary>主机把游戏消息广播给所有人（含自己）。</summary>
        public void BroadcastGameToAll(NetMessage msg)
        {
            if (!IsHost) return;

            Server?.BroadcastRaw(msg);
            DeliverLocalGameMessage(msg);
        }

        // ==================== 房间操作 ====================

        public void CreateRoom(string gameKey)
        {
            Send(new NetMessage { Type = "create_room", GameKey = gameKey });
        }

        public void JoinRoom(string roomId)
        {
            MyRoomId = roomId;
            Send(new NetMessage { Type = "join_room", RoomId = roomId });
        }

        public void LeaveRoom()
        {
            Send(new NetMessage { Type = "leave_room", RoomId = MyRoomId });
            MyRoomId = "";
        }

        public void StartGame()
        {
            Send(new NetMessage { Type = "start_game", RoomId = MyRoomId });
        }

        public RoomInfo CurrentRoom =>
            Session?.Rooms.FirstOrDefault(r => r.Id == MyRoomId);
    }
}
