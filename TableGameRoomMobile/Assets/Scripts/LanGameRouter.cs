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
    /// 主机端：跑对应的 Lan*Host 计算规则，广播状态
    /// 客户端：只接收状态并渲染
    /// </summary>
    public sealed class LanGameRouter
    {
        private static LanGameRouter _instance;
        public static LanGameRouter Instance => _instance ??= new LanGameRouter();

        private readonly LanSession _session = LanSession.Instance;

        private LanBjHost _bjHost;
        private LanPokerHost _pokerHost;
        private LanThreeHost _threeHost;
        private LanLiarHost _liarHost;

        private MonoBehaviour _page;

        public MonoBehaviour CurrentPage => _page;
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

            // 换了游戏 → 关掉旧对局页，重新进新游戏的页面
            if (_page != null && CurrentGameKey != room.GameKey)
            {
                LeaveGame();
            }

            if (room.State == "playing" && _page == null)
                EnterGame(room);
        }

        private void OnGameMessage(NetMessage msg)
        {
            if (msg == null) return;

            // 主机端：先把玩家动作路由给主机逻辑
            if (_session.IsHost && IsGameAction(msg.Type))
                HandleHostAction(msg);

            // 状态广播 → 更新界面
            switch (msg.Type)
            {
                case "bj_state":
                    (_page as LanBlackjackPage)?.ApplyState(msg);
                    break;
                case "poker_state":
                    (_page as LanPokerPage)?.ApplyState(msg);
                    break;
                case "three_state":
                    (_page as LanThreePage)?.ApplyState(msg);
                    break;
                case "liar_state":
                    (_page as LanLiarPage)?.ApplyState(msg);
                    break;
            }
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
                case "poker_start":
                case "poker_call":
                case "poker_raise":
                case "poker_fold":
                case "three_start":
                case "three_see":
                case "three_call":
                case "three_raise":
                case "three_fold":
                case "three_showdown":
                case "liar_start":
                case "liar_play":
                case "liar_challenge":
                case "liar_pass":
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

            var playerId = msg.PlayerId ?? _session.MyId;

            if (msg.Type == "start_game")
            {
                CurrentGameKey = room.GameKey;

                switch (room.GameKey)
                {
                    case "blackjack":
                        _bjHost = new LanBjHost(_session);
                        _bjHost.Handle(_session.MyId, msg);
                        break;
                    case "poker":
                        _pokerHost = new LanPokerHost(_session);
                        _pokerHost.Handle(_session.MyId, msg);
                        break;
                    case "three":
                        _threeHost = new LanThreeHost(_session);
                        _threeHost.Handle(_session.MyId, msg);
                        break;
                    case "liar":
                        _liarHost = new LanLiarHost(_session);
                        _liarHost.Handle(_session.MyId, msg);
                        break;
                }
                return;
            }

            switch (room.GameKey)
            {
                case "blackjack":
                    _bjHost ??= new LanBjHost(_session);
                    _bjHost.Handle(playerId, msg);
                    break;
                case "poker":
                    _pokerHost ??= new LanPokerHost(_session);
                    _pokerHost.Handle(playerId, msg);
                    break;
                case "three":
                    _threeHost ??= new LanThreeHost(_session);
                    _threeHost.Handle(playerId, msg);
                    break;
                case "liar":
                    _liarHost ??= new LanLiarHost(_session);
                    _liarHost.Handle(playerId, msg);
                    break;
            }
        }

        // ==================== 进入游戏页 ====================

        public void EnterGame(RoomInfo room)
        {
            if (_page != null) return;

            var app = App.Instance;
            CurrentGameKey = room.GameKey;

            switch (room.GameKey)
            {
                case "blackjack":
                    app.ShowPage("lan-bj", content =>
                        _page = LanBlackjackPage.Show(content, room, _session.MyId));
                    break;

                case "poker":
                    app.ShowPage("lan-poker", content =>
                        _page = LanPokerPage.Show(content, room, _session.MyId));
                    break;

                case "three":
                    app.ShowPage("lan-three", content =>
                        _page = LanThreePage.Show(content, room, _session.MyId));
                    break;

                case "liar":
                    app.ShowPage("lan-liar", content =>
                        _page = LanLiarPage.Show(content, room, _session.MyId));
                    break;
            }
        }

        public void LeaveGame()
        {
            // 真正销毁旧对局页，否则它会一直挂在 ScrollRoot 下
            if (_page != null)
            {
                UnityEngine.Object.Destroy(_page.gameObject);
            }

            _page = null;
            CurrentGameKey = "";
            _bjHost = null;
            _pokerHost = null;
            _threeHost = null;
            _liarHost = null;
        }
    }
}
