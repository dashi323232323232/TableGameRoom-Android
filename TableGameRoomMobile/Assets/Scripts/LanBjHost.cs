using System;
using System.Collections.Generic;
using System.Linq;
using TableGameRoom.Mobile.Core;
using TableGameRoom.Mobile.Net;

namespace TableGameRoom.Mobile
{
    /// <summary>
    /// 联机 21 点主机逻辑 —— 对应桌面版 Net/LanBlackjack.cs。
    ///
    /// ⚠️ 线格式必须与 Windows 端完全一致，否则双端无法互操作。
    /// Windows 的 bj_state 用**扁平 Data 字典**，不是嵌套 JSON：
    ///
    ///   Data["phase"]              = "betting" / "playing" / "settled"
    ///   Data["current"]            = 当前行动玩家 id
    ///   Data["dealerHidden"]       = "1" / "0"
    ///   Data["dealer"]             = "10S,1H"（点数+花色首字母）
    ///   Data["p.{id}.hand"]        = "10S,1H"
    ///   Data["p.{id}.bet"]         = "100"
    ///   Data["p.{id}.stood"]       = "1"/"0"
    ///   Data["p.{id}.busted"]      = "1"/"0"
    ///   Data["p.{id}.result"]      = "赢" / "输" / "平局"
    /// </summary>
    public sealed class LanBjHost
    {
        private readonly LanSession _session;
        private readonly System.Random _rng = new System.Random();

        private BjStateModel _state;

        public LanBjHost(LanSession session)
        {
            _session = session;
        }

        // ==================== 状态模型 ====================

        public sealed class BjPlayerModel
        {
            public string PlayerId = "";
            public List<Card> Hand = new List<Card>();
            public int Bet;
            public bool Stood;
            public bool Busted;
            public bool Doubled;
            public string Result = "";
            public bool IsAi;
        }

        public sealed class BjStateModel
        {
            public string RoomId = "";
            public string Phase = "betting";
            public List<Card> Dealer = new List<Card>();
            public List<BjPlayerModel> Players = new List<BjPlayerModel>();
            public string CurrentPlayerId = "";
            public bool DealerHidden = true;
        }

        public BjStateModel State => _state;

        // ==================== 消息处理 ====================

        public bool Handle(string playerId, NetMessage msg)
        {
            var room = _session.CurrentRoom;
            if (room == null) return false;

            switch (msg.Type)
            {
                case "start_game": return StartGame(room);
                case "bj_bet": return HandleBet(playerId, msg);
                case "bj_deal": return HandleDeal(playerId, room);
                case "bj_hit": return HandleHit(playerId);
                case "bj_stand": return HandleStand(playerId);
                case "bj_double": return HandleDouble(playerId);
                case "bj_reset": return HandleReset(playerId, room);
                default: return false;
            }
        }

        private bool StartGame(RoomInfo room)
        {
            _state = new BjStateModel
            {
                RoomId = room.Id,
                Phase = "betting"
            };

            room.State = "playing";
            _session.RequestSessionBroadcast();

            Broadcast();
            return true;
        }

        private bool HandleBet(string playerId, NetMessage msg)
        {
            if (_state == null || _state.Phase != "betting") return false;
            if (msg.Data == null) return false;
            if (!msg.Data.TryGetValue("bet", out var betStr)) return false;
            if (!int.TryParse(betStr, out var bet) || bet <= 0) return false;

            var p = _state.Players.FirstOrDefault(x => x.PlayerId == playerId);
            if (p == null)
            {
                p = new BjPlayerModel { PlayerId = playerId };
                _state.Players.Add(p);
            }
            p.Bet = bet;
            Broadcast();
            return true;
        }

        private bool HandleDeal(string playerId, RoomInfo room)
        {
            if (_state == null || _state.Phase != "betting") return false;
            if (room.HostId != playerId) return false;

            var playingIds = room.Members.Where(m => m.Playing)
                .Select(m => m.PlayerId).ToHashSet();

            _state.Players.RemoveAll(p => !playingIds.Contains(p.PlayerId) || p.Bet <= 0);

            // AI 补位
            _state.Players.RemoveAll(p => p.PlayerId.StartsWith("ai-"));
            var realPlaying = room.Members.Count(m => m.Playing);
            var aiNeeded = Math.Max(0, room.Seats - realPlaying);
            for (var i = 0; i < aiNeeded; i++)
            {
                _state.Players.Add(new BjPlayerModel
                {
                    PlayerId = $"ai-{i + 1}",
                    Bet = 100,
                    IsAi = true
                });
            }

            if (_state.Players.Count == 0) return false;

            var deck = Deck.New(_rng);
            _state.Dealer = new List<Card>();

            foreach (var p in _state.Players)
            {
                p.Hand = new List<Card> { deck[deck.Count - 1], deck[deck.Count - 2] };
                deck.RemoveRange(deck.Count - 2, 2);
                p.Stood = false;
                p.Busted = false;
                p.Doubled = false;
                p.Result = "";
            }

            _state.Dealer = new List<Card> { deck[deck.Count - 1], deck[deck.Count - 2] };
            deck.RemoveRange(deck.Count - 2, 2);

            _state.DealerHidden = true;
            _state.Phase = "playing";
            _state.CurrentPlayerId = _state.Players[0].PlayerId;

            foreach (var p in _state.Players)
            {
                if (Points(p.Hand) == 21) p.Stood = true;
            }

            AdvanceTurn();
            Broadcast();
            return true;
        }

        private bool HandleHit(string playerId)
        {
            if (_state == null || _state.Phase != "playing") return false;
            if (_state.CurrentPlayerId != playerId) return false;

            var p = _state.Players.FirstOrDefault(x => x.PlayerId == playerId);
            if (p == null || p.Stood || p.Busted) return false;

            var deck = Deck.New(_rng);
            p.Hand.Add(deck[0]);

            var pts = Points(p.Hand);
            if (pts > 21) p.Busted = true;
            else if (pts == 21) p.Stood = true;

            AdvanceTurn();
            Broadcast();
            return true;
        }

        private bool HandleStand(string playerId)
        {
            if (_state == null || _state.Phase != "playing") return false;
            if (_state.CurrentPlayerId != playerId) return false;

            var p = _state.Players.FirstOrDefault(x => x.PlayerId == playerId);
            if (p == null) return false;

            p.Stood = true;
            AdvanceTurn();
            Broadcast();
            return true;
        }

        private bool HandleDouble(string playerId)
        {
            if (_state == null || _state.Phase != "playing") return false;
            if (_state.CurrentPlayerId != playerId) return false;

            var p = _state.Players.FirstOrDefault(x => x.PlayerId == playerId);
            if (p == null || p.Hand.Count != 2 || p.Doubled) return false;

            p.Bet *= 2;
            p.Doubled = true;

            var deck = Deck.New(_rng);
            p.Hand.Add(deck[0]);

            if (Points(p.Hand) > 21) p.Busted = true;
            p.Stood = true;

            AdvanceTurn();
            Broadcast();
            return true;
        }

        private bool HandleReset(string playerId, RoomInfo room)
        {
            if (_state == null) return false;
            if (room.HostId != playerId) return false;

            _state = new BjStateModel
            {
                RoomId = room.Id,
                Phase = "betting"
            };

            // 房间状态回到 waiting，否则新玩家无法加入
            room.State = "waiting";
            _session.RequestSessionBroadcast();

            Broadcast();
            return true;
        }

        private void AdvanceTurn()
        {
            var pending = _state.Players.FirstOrDefault(p => !p.Stood && !p.Busted);

            if (pending != null)
            {
                _state.CurrentPlayerId = pending.PlayerId;
                return;
            }

            _state.CurrentPlayerId = "";
            _state.DealerHidden = false;

            var deck = Deck.New(_rng);
            while (Points(_state.Dealer) < 17)
            {
                _state.Dealer.Add(deck[0]);
            }

            Settle();
        }

        private void Settle()
        {
            var dealerPts = Points(_state.Dealer);
            var dealerBust = dealerPts > 21;

            foreach (var p in _state.Players)
            {
                var pts = Points(p.Hand);

                if (p.Busted) p.Result = "输（爆牌）";
                else if (dealerBust) p.Result = "赢（庄家爆牌）";
                else if (pts > dealerPts) p.Result = "赢";
                else if (pts < dealerPts) p.Result = "输";
                else p.Result = "平局";
            }

            _state.Phase = "settled";
        }

        /// <summary>广播 —— 必须用 Windows 端的扁平 Data 格式。</summary>
        private void Broadcast()
        {
            var data = new Dictionary<string, string>
            {
                ["phase"] = _state.Phase,
                ["current"] = _state.CurrentPlayerId,
                ["dealerHidden"] = _state.DealerHidden ? "1" : "0",
                ["dealer"] = EncodeCards(_state.Dealer)
            };

            foreach (var p in _state.Players)
            {
                data[$"p.{p.PlayerId}.hand"] = EncodeCards(p.Hand);
                data[$"p.{p.PlayerId}.bet"] = p.Bet.ToString();
                data[$"p.{p.PlayerId}.stood"] = p.Stood ? "1" : "0";
                data[$"p.{p.PlayerId}.busted"] = p.Busted ? "1" : "0";
                data[$"p.{p.PlayerId}.result"] = p.Result;
            }

            _session.BroadcastGameMessage(new NetMessage
            {
                Type = "bj_state",
                RoomId = _state.RoomId,
                Data = data
            });
        }

        // ==================== 牌面编解码 ====================

        /// <summary>
        /// 牌编码 —— 必须与 Windows 端**逐字符一致**。
        ///
        /// Windows 的编码是 `$"{c.Rank}{c.Suit}"`，其中 Suit 是 char（'♠'/'♥'/'♦'/'♣'），
        /// 所以实际格式是 `10♠`、`14♥` 这种，**不是** `10S`。
        /// 注意 Rank 用数值（14=A、13=K、12=Q、11=J）。
        /// </summary>
        public static string EncodeCards(List<Card> cards)
        {
            return string.Join(",", cards.Select(c => $"{c.Rank}{c.Suit}"));
        }

        public static List<Card> DecodeCards(string text)
        {
            var list = new List<Card>();
            if (string.IsNullOrEmpty(text)) return list;

            foreach (var part in text.Split(','))
            {
                if (part.Length < 2) continue;

                var suitChar = part[part.Length - 1];
                var rankStr = part.Substring(0, part.Length - 1);
                if (!int.TryParse(rankStr, out var rank)) continue;

                list.Add(new Card(rank, suitChar));
            }

            return list;
        }

        // ==================== 工具 ====================

        private static int Points(List<Card> hand)
        {
            var total = 0;
            var aces = 0;

            foreach (var c in hand)
            {
                if (c.Rank == 14) { aces++; total += 11; }
                else if (c.Rank >= 11) total += 10;
                else total += c.Rank;
            }

            while (total > 21 && aces > 0) { total -= 10; aces--; }
            return total;
        }
    }
}
