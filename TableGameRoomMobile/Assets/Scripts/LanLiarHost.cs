using System;
using System.Collections.Generic;
using System.Linq;
using TableGameRoom.Mobile.Core;
using TableGameRoom.Mobile.Net;

namespace TableGameRoom.Mobile
{
    /// <summary>
    /// 联机骗子扑克主机逻辑 —— 对应桌面版 Net/LanLiar.cs。
    ///
    /// 线格式（扁平 Data 字典）：
    ///   phase / current / pile / lastClaim / lastCount / awaiting / winner / log / stake
    ///   hand（只发给本人）/ handCount / cnt.{id}（其他玩家手牌数）
    /// </summary>
    public sealed class LanLiarHost
    {
        private readonly LanSession _session;
        private readonly System.Random _rng = new System.Random();

        private LiarStateModel _state;

        public LanLiarHost(LanSession session)
        {
            _session = session;
        }

        // ==================== 状态模型 ====================

        public sealed class LiarStateModel
        {
            public string RoomId = "";
            public string Phase = "playing";
            public Dictionary<string, List<Card>> Hands = new Dictionary<string, List<Card>>();
            public List<Card> Pile = new List<Card>();
            public List<Card> LastCards = new List<Card>();
            public string LastClaim = "";
            public string LastPlayerId = "";
            public string CurrentPlayerId = "";
            public bool AwaitingDecision;
            public string Winner = "";
            public string Log = "";
            public int Stake = 100;
        }

        public LiarStateModel State => _state;

        // ==================== 消息处理 ====================

        public bool Handle(string playerId, NetMessage msg)
        {
            var room = _session.CurrentRoom;
            if (room == null) return false;

            switch (msg.Type)
            {
                case "start_game": return StartGame(playerId, room);
                case "liar_start": return StartGame(playerId, room);
                case "liar_play": return Play(playerId, msg);
                case "liar_challenge": return Challenge(playerId);
                case "liar_pass": return Pass(playerId);
                default: return false;
            }
        }

        private bool StartGame(string playerId, RoomInfo room)
        {
            if (room.HostId != playerId) return false;

            var playingIds = room.Members.Where(m => m.Playing).Select(m => m.PlayerId).ToList();

            // 骗子扑克最多 2 人
            var aiNeeded = Math.Max(0, Math.Min(2, room.Seats) - playingIds.Count);
            for (var i = 0; i < aiNeeded; i++) playingIds.Add($"ai-{i + 1}");

            if (playingIds.Count < 2) return false;

            var st = new LiarStateModel { RoomId = room.Id, Phase = "playing", Stake = 100 };
            var deck = Deck.New(_rng);

            foreach (var pid in playingIds)
            {
                st.Hands[pid] = new List<Card> { deck[0], deck[1], deck[2], deck[3], deck[4] };
                deck.RemoveRange(0, 5);
            }

            st.CurrentPlayerId = playingIds[0];
            st.Log = "新局开始，请出牌并声明。";

            _state = st;
            room.State = "playing";
            _session.RequestSessionBroadcast();

            Broadcast();
            return true;
        }

        private bool Play(string playerId, NetMessage msg)
        {
            var st = _state;
            if (st == null || st.Phase != "playing") return false;
            if (st.CurrentPlayerId != playerId || st.AwaitingDecision) return false;

            var hand = st.Hands.GetValueOrDefault(playerId);
            if (hand == null) return false;

            if (msg.Data == null) return false;

            var indices = (msg.Data.GetValueOrDefault("indices") ?? "")
                .Split(',')
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => int.TryParse(s, out var v) ? v : -1)
                .Where(i => i >= 0 && i < hand.Count)
                .Distinct()
                .OrderByDescending(i => i)
                .ToList();

            if (indices.Count == 0) return false;

            var claim = msg.Data.GetValueOrDefault("claim") ?? "A";
            var played = new List<Card>();
            foreach (var i in indices)
            {
                played.Add(hand[i]);
                hand.RemoveAt(i);
            }

            st.Pile.AddRange(played);
            st.LastCards = played;
            st.LastClaim = claim;
            st.LastPlayerId = playerId;
            st.AwaitingDecision = true;

            var next = NextPlayer(st, playerId);
            st.CurrentPlayerId = next ?? playerId;

            if (next == null)
            {
                st.Phase = "settled";
                st.Winner = "对局结束";
            }

            st.Log = $"{(hand.Count == 0 ? "出完手牌！" : $"盖下 {played.Count} 张")}，声明 {claim}。等待质疑或继续。";

            Broadcast();
            return true;
        }

        private bool Challenge(string playerId)
        {
            var st = _state;
            if (st == null || st.Phase != "playing") return false;
            if (st.CurrentPlayerId != playerId || !st.AwaitingDecision) return false;

            var truthful = st.LastCards.All(c => c.RankText == st.LastClaim);
            var challenged = st.LastPlayerId;

            var loser = truthful ? playerId : challenged;
            var loserHand = st.Hands.GetValueOrDefault(loser);

            var pileCount = st.Pile.Count;
            if (loserHand != null) loserHand.AddRange(st.Pile);

            st.Pile.Clear();
            st.LastCards.Clear();
            st.AwaitingDecision = false;
            st.Log = $"{(truthful ? "声明属实" : "声明不实")}，{NameOf(loser)} 收走桌面 {pileCount} 张牌。";

            var winner = st.Hands.FirstOrDefault(h => h.Value.Count == 0);
            if (winner.Key != null)
            {
                st.Phase = "settled";
                st.Winner = $"{NameOf(winner.Key)} 出完手牌，获胜！";
            }
            else
            {
                st.CurrentPlayerId = loser;
            }

            Broadcast();
            return true;
        }

        private bool Pass(string playerId)
        {
            var st = _state;
            if (st == null || st.Phase != "playing") return false;
            if (st.CurrentPlayerId != playerId || !st.AwaitingDecision) return false;

            st.AwaitingDecision = false;
            st.Log = "继续出牌。";

            Broadcast();
            return true;
        }

        private string NextPlayer(LiarStateModel st, string current)
        {
            var ids = st.Hands.Keys.ToList();
            var idx = ids.IndexOf(current);
            if (idx < 0) return ids.FirstOrDefault();
            return ids[(idx + 1) % ids.Count];
        }

        /// <summary>广播 —— 只把本人手牌发给本人（防作弊）。</summary>
        private void Broadcast()
        {
            var baseData = new Dictionary<string, string>
            {
                ["phase"] = _state.Phase,
                ["current"] = _state.CurrentPlayerId,
                ["pile"] = _state.Pile.Count.ToString(),
                ["lastClaim"] = _state.LastClaim,
                ["lastCount"] = _state.LastCards.Count.ToString(),
                ["awaiting"] = _state.AwaitingDecision ? "1" : "0",
                ["winner"] = _state.Winner,
                ["log"] = _state.Log,
                ["stake"] = _state.Stake.ToString()
            };

            foreach (var kv in _state.Hands)
            {
                if (kv.Key.StartsWith("ai-")) continue;

                var mine = new Dictionary<string, string>(baseData)
                {
                    ["hand"] = EncodeCards(kv.Value),
                    ["handCount"] = kv.Value.Count.ToString()
                };

                // 其他玩家手牌数
                foreach (var other in _state.Hands.Where(h => h.Key != kv.Key))
                    mine[$"cnt.{other.Key}"] = other.Value.Count.ToString();

                _session.SendGameToPlayer(kv.Key, new NetMessage
                {
                    Type = "liar_state",
                    RoomId = _state.RoomId,
                    Data = mine
                });
            }

            // 观战者
            var room = _session.CurrentRoom;
            if (room != null)
            {
                foreach (var m in room.Members)
                {
                    if (_state.Hands.ContainsKey(m.PlayerId)) continue;
                    if (m.PlayerId == _session.MyId) continue;

                    _session.SendGameToPlayer(m.PlayerId, new NetMessage
                    {
                        Type = "liar_state",
                        RoomId = _state.RoomId,
                        Data = new Dictionary<string, string>(baseData)
                    });
                }
            }

            // AI 自动行动
            if (_state.Phase == "playing" && _state.CurrentPlayerId.StartsWith("ai-"))
            {
                AutoPlayAi();
            }
        }

        private void AutoPlayAi()
        {
            var aiId = _state.CurrentPlayerId;
            var hand = _state.Hands.GetValueOrDefault(aiId);
            if (hand == null || hand.Count == 0) return;

            var claim = hand[0].RankText;
            var validClaims = new[] { "7", "8", "9", "10", "J", "Q", "K", "A" };
            if (!validClaims.Contains(claim)) claim = "A";

            var data = new Dictionary<string, string>
            {
                ["indices"] = "0",
                ["claim"] = claim,
                ["count"] = "1"
            };

            Play(aiId, new NetMessage { Type = "liar_play", Data = data });
        }

        // ==================== 辅助 ====================

        private string NameOf(string playerId)
        {
            if (playerId.StartsWith("ai-")) return $"AI {playerId.Substring(3)}";
            var p = _session.Session?.Players.FirstOrDefault(x => x.Id == playerId);
            return p?.Name ?? playerId;
        }

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
    }
}
