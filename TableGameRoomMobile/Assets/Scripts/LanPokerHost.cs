using System;
using System.Collections.Generic;
using System.Linq;
using TableGameRoom.Mobile.Core;
using TableGameRoom.Mobile.Net;

namespace TableGameRoom.Mobile
{
    /// <summary>
    /// 联机德州扑克主机逻辑 —— 对应桌面版 Net/LanPoker.cs。
    ///
    /// 线格式必须与 Windows 端一致（扁平 Data 字典）：
    ///   phase / current / pot / stake / winner / log / community
    ///   p.{id}.committed / p.{id}.folded / p.{id}.result
    ///   cards（只发给本人）
    /// </summary>
    public sealed class LanPokerHost
    {
        private readonly LanSession _session;
        private readonly System.Random _rng = new System.Random();

        private PokerStateModel _state;

        public LanPokerHost(LanSession session)
        {
            _session = session;
        }

        // ==================== 状态模型 ====================

        public sealed class PokerPlayerModel
        {
            public string PlayerId = "";
            public List<Card> Cards = new List<Card>();
            public int Committed;
            public bool Folded;
            public string Result = "";
            public bool IsAi;
        }

        public sealed class PokerStateModel
        {
            public string RoomId = "";
            public string Phase = "preflop";   // preflop / flop / turn / river / showdown
            public List<Card> Community = new List<Card>();
            public List<PokerPlayerModel> Players = new List<PokerPlayerModel>();
            public string CurrentPlayerId = "";
            public int Pot;
            public int Stake = 100;
            public string Winner = "";
            public string Log = "";
            public HashSet<string> ActedThisRound = new HashSet<string>();
        }

        public PokerStateModel State => _state;

        // ==================== 消息处理 ====================

        public bool Handle(string playerId, NetMessage msg)
        {
            var room = _session.CurrentRoom;
            if (room == null) return false;

            switch (msg.Type)
            {
                case "start_game": return StartGame(playerId, room);
                case "poker_start": return StartGame(playerId, room);
                case "poker_call": return Action(playerId, room, "call");
                case "poker_raise": return Action(playerId, room, "raise");
                case "poker_fold": return Action(playerId, room, "fold");
                default: return false;
            }
        }

        private bool StartGame(string playerId, RoomInfo room)
        {
            if (room.HostId != playerId) return false;

            var playingIds = room.Members.Where(m => m.Playing).Select(m => m.PlayerId).ToList();

            // AI 补位
            var aiNeeded = Math.Max(0, room.Seats - playingIds.Count);
            for (var i = 0; i < aiNeeded; i++) playingIds.Add($"ai-{i + 1}");

            if (playingIds.Count < 2) return false;

            var deck = Deck.New(_rng);
            var st = new PokerStateModel
            {
                RoomId = room.Id,
                Phase = "preflop",
                Stake = 100,
                Pot = 0,
                Community = new List<Card>()
            };

            foreach (var pid in playingIds)
            {
                var p = new PokerPlayerModel
                {
                    PlayerId = pid,
                    Cards = new List<Card> { deck[0], deck[1] },
                    IsAi = pid.StartsWith("ai-")
                };
                deck.RemoveRange(0, 2);

                var member = room.Members.FirstOrDefault(m => m.PlayerId == pid);
                var ante = member?.Unlimited == true ? 100 : Math.Min(100, member?.Chips ?? 100);
                member?.Pay(ante);
                p.Committed = ante;
                st.Pot += ante;
                st.Players.Add(p);
            }

            st.CurrentPlayerId = playingIds[0];
            st.ActedThisRound.Clear();
            st.Log = "新局开始（翻牌前）。";

            _state = st;
            room.State = "playing";
            _session.RequestSessionBroadcast();

            Broadcast();
            return true;
        }

        private bool Action(string playerId, RoomInfo room, string action)
        {
            var st = _state;
            if (st == null) return false;
            if (st.Phase == "waiting" || st.Phase == "showdown") return false;

            var p = st.Players.FirstOrDefault(x => x.PlayerId == playerId);
            if (p == null || p.Folded) return false;
            if (st.CurrentPlayerId != playerId) return false;

            var member = room.Members.FirstOrDefault(x => x.PlayerId == playerId);

            switch (action)
            {
                case "call":
                {
                    var pay = Math.Min(st.Stake, member?.Chips ?? st.Stake);
                    member?.Pay(pay);
                    p.Committed += pay;
                    st.Pot += pay;
                    st.Log = $"{NameOf(playerId)} 跟注 ${pay:N0}。";
                    break;
                }
                case "raise":
                {
                    st.Stake *= 2;
                    var pay = Math.Min(st.Stake, member?.Chips ?? st.Stake);
                    member?.Pay(pay);
                    p.Committed += pay;
                    st.Pot += pay;
                    st.Log = $"{NameOf(playerId)} 加注到 ${st.Stake:N0}。";
                    break;
                }
                case "fold":
                    p.Folded = true;
                    st.Log = $"{NameOf(playerId)} 弃牌。";
                    break;
            }

            st.ActedThisRound.Add(playerId);
            Advance(st, playerId);
            Broadcast();
            return true;
        }

        private void Advance(PokerStateModel st, string current)
        {
            var alive = st.Players.Where(p => !p.Folded).ToList();
            if (alive.Count <= 1)
            {
                Settle(st);
                return;
            }

            var idx = st.Players.FindIndex(p => p.PlayerId == current);

            for (var i = 1; i <= st.Players.Count; i++)
            {
                var next = st.Players[(idx + i) % st.Players.Count];
                if (next.Folded) continue;
                if (st.ActedThisRound.Contains(next.PlayerId)) continue;

                st.CurrentPlayerId = next.PlayerId;

                // AI 自动行动
                if (next.IsAi)
                {
                    var random = _rng.NextDouble();
                    if (random < 0.18)
                    {
                        next.Folded = true;
                        st.Log += $" {NameOf(next.PlayerId)} 弃牌。";
                    }
                    else if (random > 0.85)
                    {
                        st.Stake *= 2;
                        next.Committed += st.Stake;
                        st.Pot += st.Stake;
                        st.Log += $" {NameOf(next.PlayerId)} 加注到 ${st.Stake:N0}。";
                    }
                    else
                    {
                        next.Committed += st.Stake;
                        st.Pot += st.Stake;
                        st.Log += $" {NameOf(next.PlayerId)} 跟注。";
                    }

                    st.ActedThisRound.Add(next.PlayerId);
                    Advance(st, next.PlayerId);
                }
                return;
            }

            // 本轮结束 → 下一街
            st.ActedThisRound.Clear();

            switch (st.Phase)
            {
                case "preflop":
                    st.Phase = "flop";
                    DealCommunity(st, 3);
                    break;
                case "flop":
                    st.Phase = "turn";
                    DealCommunity(st, 1);
                    break;
                case "turn":
                    st.Phase = "river";
                    DealCommunity(st, 1);
                    break;
                case "river":
                    Settle(st);
                    return;
            }

            st.Log = $"进入{PhaseName(st.Phase)}。";
            st.CurrentPlayerId = st.Players.First(p => !p.Folded).PlayerId;

            // 若下一个是 AI，继续自动行动
            var nextPlayer = st.Players.FirstOrDefault(p => p.PlayerId == st.CurrentPlayerId);
            if (nextPlayer != null && nextPlayer.IsAi)
            {
                var random = _rng.NextDouble();
                if (random < 0.18) nextPlayer.Folded = true;
                else if (random > 0.85) { st.Stake *= 2; nextPlayer.Committed += st.Stake; st.Pot += st.Stake; }
                else { nextPlayer.Committed += st.Stake; st.Pot += st.Stake; }
                st.ActedThisRound.Add(nextPlayer.PlayerId);
                Advance(st, nextPlayer.PlayerId);
            }
        }

        private void DealCommunity(PokerStateModel st, int count)
        {
            var deck = Deck.New(_rng);
            for (var i = 0; i < count; i++) st.Community.Add(deck[i]);
        }

        private void Settle(PokerStateModel st)
        {
            st.Phase = "showdown";

            var alive = st.Players.Where(p => !p.Folded).ToList();

            if (alive.Count == 1)
            {
                st.Winner = alive[0].PlayerId;
                alive[0].Result = "赢（对手全弃牌）";
                st.Log = $"{NameOf(st.Winner)} 赢得 ${st.Pot:N0}（对手全部弃牌）。";
            }
            else
            {
                PokerPlayerModel best = null;
                long bestScore = -1;

                foreach (var p in alive)
                {
                    var all = new List<Card>(p.Cards);
                    all.AddRange(st.Community);
                    var score = FastPokerScore.Best(all.ToArray());

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = p;
                    }
                }

                if (best != null)
                {
                    st.Winner = best.PlayerId;
                    best.Result = "赢";
                    foreach (var p in alive.Where(x => x != best))
                        p.Result = "输";

                    st.Log = $"摊牌：{NameOf(st.Winner)} 以 {PokerScore.Name(bestScore)} 赢得 ${st.Pot:N0}。";
                }
            }

            // 赢家收筹码
            var winner = st.Players.FirstOrDefault(p => p.PlayerId == st.Winner);
            if (winner != null)
            {
                var member = _session.CurrentRoom?.Members.FirstOrDefault(m => m.PlayerId == winner.PlayerId);
                member?.Win(st.Pot);
            }

            _session.RequestSessionBroadcast();
        }

        /// <summary>广播 —— 每人只收到自己的底牌。</summary>
        private void Broadcast()
        {
            var baseData = new Dictionary<string, string>
            {
                ["phase"] = _state.Phase,
                ["current"] = _state.CurrentPlayerId,
                ["pot"] = _state.Pot.ToString(),
                ["stake"] = _state.Stake.ToString(),
                ["winner"] = _state.Winner,
                ["log"] = _state.Log,
                ["community"] = EncodeCards(_state.Community)
            };

            foreach (var p in _state.Players)
            {
                baseData[$"p.{p.PlayerId}.committed"] = p.Committed.ToString();
                baseData[$"p.{p.PlayerId}.folded"] = p.Folded ? "1" : "0";
                baseData[$"p.{p.PlayerId}.result"] = p.Result;
            }

            // 发给每个参战玩家（带自己的底牌）
            foreach (var p in _state.Players)
            {
                if (p.IsAi) continue;

                var mine = new Dictionary<string, string>(baseData)
                {
                    ["cards"] = EncodeCards(p.Cards)
                };

                _session.SendGameToPlayer(p.PlayerId, new NetMessage
                {
                    Type = "poker_state",
                    RoomId = _state.RoomId,
                    Data = mine
                });
            }

            // 观战者（无底牌）
            var room = _session.CurrentRoom;
            if (room != null)
            {
                foreach (var m in room.Members)
                {
                    if (_state.Players.Any(p => p.PlayerId == m.PlayerId)) continue;
                    if (m.PlayerId == _session.MyId) continue;

                    _session.SendGameToPlayer(m.PlayerId, new NetMessage
                    {
                        Type = "poker_state",
                        RoomId = _state.RoomId,
                        Data = new Dictionary<string, string>(baseData)
                    });
                }
            }

            // 主机本地
            if (_state.Players.Any(p => p.PlayerId == _session.MyId))
            {
                var mineHost = new Dictionary<string, string>(baseData);
                var me = _state.Players.First(p => p.PlayerId == _session.MyId);
                mineHost["cards"] = EncodeCards(me.Cards);

                _session.DeliverLocalGameMessage(new NetMessage
                {
                    Type = "poker_state",
                    RoomId = _state.RoomId,
                    Data = mineHost
                });
            }
        }

        // ==================== 辅助 ====================

        private string NameOf(string playerId)
        {
            if (playerId.StartsWith("ai-"))
            {
                return $"AI {playerId.Substring(3)}";
            }
            var p = _session.Session?.Players.FirstOrDefault(x => x.Id == playerId);
            return p?.Name ?? playerId;
        }

        private static string PhaseName(string phase)
        {
            switch (phase)
            {
                case "preflop": return "翻牌前";
                case "flop": return "翻牌";
                case "turn": return "转牌";
                case "river": return "河牌";
                default: return phase;
            }
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
