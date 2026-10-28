using System;
using System.Collections.Generic;
using System.Linq;
using TableGameRoom.Mobile.Core;
using TableGameRoom.Mobile.Net;

namespace TableGameRoom.Mobile
{
    /// <summary>
    /// 联机炸金花主机逻辑 —— 对应桌面版 Net/LanThree.cs。
    ///
    /// 线格式（扁平 Data 字典）：
    ///   phase / current / pot / stake / winner / log
    ///   p.{id}.committed / p.{id}.folded / p.{id}.seen / p.{id}.result
    ///   cards（只发给本人）
    /// </summary>
    public sealed class LanThreeHost
    {
        private readonly LanSession _session;
        private readonly System.Random _rng = new System.Random();

        private ThreeStateModel _state;

        public LanThreeHost(LanSession session)
        {
            _session = session;
        }

        // ==================== 状态模型 ====================

        public sealed class ThreePlayerModel
        {
            public string PlayerId = "";
            public List<Card> Cards = new List<Card>();
            public int Committed;
            public bool Folded;
            public bool Seen;
            public string Result = "";
            public bool IsAi;
        }

        public sealed class ThreeStateModel
        {
            public string RoomId = "";
            public string Phase = "betting";   // betting / showdown
            public List<ThreePlayerModel> Players = new List<ThreePlayerModel>();
            public string CurrentPlayerId = "";
            public int Pot;
            public int Stake = 100;
            public string Winner = "";
            public string Log = "";
        }

        public ThreeStateModel State => _state;

        // ==================== 消息处理 ====================

        public bool Handle(string playerId, NetMessage msg)
        {
            var room = _session.CurrentRoom;
            if (room == null) return false;

            switch (msg.Type)
            {
                case "start_game": return StartGame(playerId, room);
                case "three_start": return StartGame(playerId, room);
                case "three_see": return Action(playerId, room, "see");
                case "three_call": return Action(playerId, room, "call");
                case "three_raise": return Action(playerId, room, "raise");
                case "three_fold": return Action(playerId, room, "fold");
                case "three_showdown": return Action(playerId, room, "showdown");
                default: return false;
            }
        }

        private bool StartGame(string playerId, RoomInfo room)
        {
            if (room.HostId != playerId) return false;

            var playingIds = room.Members.Where(m => m.Playing).Select(m => m.PlayerId).ToList();

            var aiNeeded = Math.Max(0, room.Seats - playingIds.Count);
            for (var i = 0; i < aiNeeded; i++) playingIds.Add($"ai-{i + 1}");

            if (playingIds.Count < 2) return false;

            var deck = Deck.New(_rng);
            var st = new ThreeStateModel
            {
                RoomId = room.Id,
                Phase = "betting",
                Stake = 100,
                Pot = 0
            };

            foreach (var pid in playingIds)
            {
                var p = new ThreePlayerModel
                {
                    PlayerId = pid,
                    Cards = new List<Card> { deck[0], deck[1], deck[2] },
                    IsAi = pid.StartsWith("ai-")
                };
                deck.RemoveRange(0, 3);

                var member = room.Members.FirstOrDefault(m => m.PlayerId == pid);
                var ante = member?.Unlimited == true ? 100 : Math.Min(100, member?.Chips ?? 100);
                member?.Pay(ante);
                p.Committed = ante;
                st.Pot += ante;
                st.Players.Add(p);
            }

            st.CurrentPlayerId = playingIds[0];
            st.Log = "新局开始，请下注。";

            _state = st;
            room.State = "playing";
            _session.RequestSessionBroadcast();

            Broadcast();
            return true;
        }

        private bool Action(string playerId, RoomInfo room, string action)
        {
            var st = _state;
            if (st == null || st.Phase != "betting") return false;

            var p = st.Players.FirstOrDefault(x => x.PlayerId == playerId);
            if (p == null || p.Folded) return false;

            if (action == "see")
            {
                p.Seen = true;
                st.Log = $"{NameOf(playerId)} 看了牌。";
                Broadcast();
                return true;
            }

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
                case "showdown":
                    Settle(st);
                    Broadcast();
                    return true;
            }

            Advance(st, playerId);
            Broadcast();
            return true;
        }

        private void Advance(ThreeStateModel st, string current)
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

                st.CurrentPlayerId = next.PlayerId;

                if (next.IsAi)
                {
                    var cat = (int)(ThreeScore.Of(next.Cards) / 1_000_000L);
                    var rnd = _rng.NextDouble();

                    if (cat == 0 && rnd < 0.45)
                    {
                        next.Folded = true;
                        st.Log += $" {NameOf(next.PlayerId)} 弃牌。";
                    }
                    else if (rnd > 0.88)
                    {
                        st.Stake *= 2;
                        next.Committed += st.Stake;
                        st.Pot += st.Stake;
                        st.Log += $" {NameOf(next.PlayerId)} 加注。";
                    }
                    else
                    {
                        next.Committed += st.Stake;
                        st.Pot += st.Stake;
                        st.Log += $" {NameOf(next.PlayerId)} 跟注。";
                    }

                    Advance(st, next.PlayerId);
                }
                return;
            }
        }

        private void Settle(ThreeStateModel st)
        {
            var alive = st.Players.Where(p => !p.Folded).ToList();

            if (alive.Count == 0)
            {
                st.Phase = "showdown";
                st.Winner = "无人获胜";
                return;
            }

            var best = alive[0];
            foreach (var p in alive.Skip(1))
            {
                if (ThreeScore.Of(p.Cards) > ThreeScore.Of(best.Cards)) best = p;
            }

            st.Phase = "showdown";
            st.Winner = $"{NameOf(best.PlayerId)} 获胜，赢得底池 ${st.Pot:N0}";

            var member = _session.CurrentRoom?.Members.FirstOrDefault(m => m.PlayerId == best.PlayerId);
            member?.Win(st.Pot);

            foreach (var p in st.Players)
            {
                p.Result = p.Folded ? "弃牌" : ThreeScore.Name(ThreeScore.Of(p.Cards));
            }

            _session.RequestSessionBroadcast();
        }

        /// <summary>广播 —— 每人只收到自己的牌。</summary>
        private void Broadcast()
        {
            var baseData = new Dictionary<string, string>
            {
                ["phase"] = _state.Phase,
                ["current"] = _state.CurrentPlayerId,
                ["pot"] = _state.Pot.ToString(),
                ["stake"] = _state.Stake.ToString(),
                ["winner"] = _state.Winner,
                ["log"] = _state.Log
            };

            foreach (var p in _state.Players)
            {
                baseData[$"p.{p.PlayerId}.committed"] = p.Committed.ToString();
                baseData[$"p.{p.PlayerId}.folded"] = p.Folded ? "1" : "0";
                baseData[$"p.{p.PlayerId}.seen"] = p.Seen ? "1" : "0";
                baseData[$"p.{p.PlayerId}.result"] = p.Result;
            }

            // 参战玩家（带自己的牌）
            foreach (var p in _state.Players)
            {
                if (p.IsAi) continue;

                var mine = new Dictionary<string, string>(baseData)
                {
                    ["cards"] = EncodeCards(p.Cards)
                };

                _session.SendGameToPlayer(p.PlayerId, new NetMessage
                {
                    Type = "three_state",
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
                    if (_state.Players.Any(p => p.PlayerId == m.PlayerId)) continue;
                    if (m.PlayerId == _session.MyId) continue;

                    _session.SendGameToPlayer(m.PlayerId, new NetMessage
                    {
                        Type = "three_state",
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
                    Type = "three_state",
                    RoomId = _state.RoomId,
                    Data = mineHost
                });
            }
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
