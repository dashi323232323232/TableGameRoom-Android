using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TableGameRoom.Mobile.Core;
using TableGameRoom.Mobile.Games;
using TableGameRoom.Mobile.Net;

namespace TableGameRoom.Mobile
{
    /// <summary>
    /// 联机对局路由 —— 决定「开始游戏」后进入哪个页面，并把游戏消息分发过去。
    ///
    /// 主机端：跑 LanBjHost 计算规则，广播状态
    /// 客户端：只接收状态并渲染
    /// </summary>
    public sealed class LanGameRouter
    {
        private static LanGameRouter _instance;
        public static LanGameRouter Instance => _instance ??= new LanGameRouter();

        private readonly LanSession _session = LanSession.Instance;

        private LanBjHost _bjHost;
        private LanBlackjackPage _bjPage;

        public MonoBehaviour CurrentPage { get; private set; }
        public string CurrentGameKey { get; private set; } = "";

        private bool _initialized;

        // ==================== 初始化 ====================

        public void Init()
        {
            if (_initialized) return;
            _initialized = true;

            _session.GameMessage += OnGameMessage;
            _session.SessionChanged += OnSessionChanged;
        }

        private void OnSessionChanged()
        {
            var room = _session.CurrentRoom;
            if (room == null)
            {
                LeaveGame();
                return;
            }

            // 房间进入 playing 且还没进对局页 → 自动进入
            if (room.State == "playing" && CurrentPage == null)
                EnterGame(room);
        }

        private void OnGameMessage(NetMessage msg)
        {
            if (msg == null) return;

            // 主机端：先把玩家动作路由给主机逻辑
            if (_session.IsHost && IsGameAction(msg.Type))
                HandleHostAction(msg);

            // 状态广播 → 更新界面
            if (msg.Type == "bj_state")
                _bjPage?.ApplyState(msg);
        }

        private static bool IsGameAction(string type)
        {
            switch (type)
            {
                case "start_game":
                case "bj_bet":
                case "bj_deal":
                case "bj_hit":
                case "bj_stand":
                case "bj_double":
                case "bj_reset":
                    return true;
                default:
                    return false;
            }
        }

        // ==================== 主机逻辑 ====================

        private void HandleHostAction(NetMessage msg)
        {
            var room = _session.CurrentRoom;
            if (room == null) return;

            if (msg.Type == "start_game")
            {
                CurrentGameKey = room.GameKey;

                switch (room.GameKey)
                {
                    case "blackjack":
                        _bjHost = new LanBjHost(_session);
                        _bjHost.Handle(_session.MyId, msg);
                        break;

                    default:
                        Debug.Log($"[LanGameRouter] {room.GameKey} 联机逻辑尚未实现");
                        break;
                }
                return;
            }

            if (room.GameKey == "blackjack")
            {
                _bjHost ??= new LanBjHost(_session);
                _bjHost.Handle(msg.PlayerId ?? _session.MyId, msg);
            }
        }

        // ==================== 进入游戏页 ====================

        public void EnterGame(RoomInfo room)
        {
            if (CurrentPage != null) return;

            var app = App.Instance;
            CurrentGameKey = room.GameKey;

            switch (room.GameKey)
            {
                case "blackjack":
                    app.ShowPage("lan-bj", content =>
                    {
                        _bjPage = LanBlackjackPage.Show(content, room, _session.MyId);
                        CurrentPage = _bjPage;
                    });
                    break;

                default:
                    app.Msg($"{LanPage.GameName(room.GameKey)} 联机对局开发中");
                    break;
            }
        }

        public void LeaveGame()
        {
            CurrentPage = null;
            _bjPage = null;
            CurrentGameKey = "";
        }
    }
}
