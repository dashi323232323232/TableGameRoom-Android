using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TableGameRoom.Mobile.Core;
using TableGameRoom.Mobile.Net;
using TableGameRoom.Mobile.UI;

namespace TableGameRoom.Mobile.Games
{
    /// <summary>
    /// 联机 21 点对局页 —— 对应桌面版 Net/LanBlackjack.cs 的渲染部分。
    ///
    /// 状态来自主机广播的 bj_state，格式是**扁平 Data 字典**（与 Windows 端一致）：
    ///   phase / current / dealerHidden / dealer
    ///   p.{id}.hand / p.{id}.bet / p.{id}.stood / p.{id}.busted / p.{id}.result
    /// </summary>
    public sealed class LanBlackjackPage : MonoBehaviour
    {
        // ==================== 解析后的状态 ====================

        public sealed class BjPlayer
        {
            public string PlayerId = "";
            public List<Card> Hand = new List<Card>();
            public int Bet;
            public bool Stood;
            public bool Busted;
            public bool IsAi;
            public string Result = "";
        }

        public sealed class BjState
        {
            public string RoomId = "";
            public string Phase = "betting";
            public List<Card> Dealer = new List<Card>();
            public List<BjPlayer> Players = new List<BjPlayer>();
            public string CurrentPlayerId = "";
            public bool DealerHidden = true;
        }

        // ==================== 依赖 ====================

        private App _app;
        private RectTransform _content;
        private RoomInfo _room;
        private string _myId = "";
        private bool _isHost;

        private BjState _state = new BjState();
        private int _myBet = 100;

        private Text _statusText;
        private Text _dealerText;
        private Text _logText;
        private RectTransform _dealerHost;
        private RectTransform _playersHost;
        private RectTransform _actionHost;

        public string RoomId => _room?.Id ?? "";

        // ==================== 入口 ====================

        public static LanBlackjackPage Show(RectTransform content, RoomInfo room, string myId)
        {
            // 不能挂在 content 下：Render 里的 Clear(_content) 会删掉自己
            var parent = content.parent ?? content;

            var go = new GameObject("LanBlackjackPage", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiFactory.Fill((RectTransform)go.transform);

            var page = go.AddComponent<LanBlackjackPage>();
            page.Init(content, room, myId);
            return page;
        }

        private void Init(RectTransform content, RoomInfo room, string myId)
        {
            _app = App.Instance;
            _content = content;
            _room = room;
            _myId = myId;
            _isHost = room.HostId == myId;

            BuildUi();
            Render();
        }

        /// <summary>收到主机的 bj_state。</summary>
        public void ApplyState(NetMessage msg)
        {
            if (msg?.Data == null) return;

            var s = new BjState { RoomId = msg.RoomId ?? "" };

            if (msg.Data.TryGetValue("phase", out var phase)) s.Phase = phase;
            if (msg.Data.TryGetValue("current", out var cur)) s.CurrentPlayerId = cur;
            if (msg.Data.TryGetValue("dealerHidden", out var dh)) s.DealerHidden = dh == "1";
            if (msg.Data.TryGetValue("dealer", out var dealer))
                s.Dealer = LanBjHost.DecodeCards(dealer);

            // 解析 p.{id}.*
            var ids = new HashSet<string>();
            foreach (var key in msg.Data.Keys)
            {
                if (!key.StartsWith("p.")) continue;
                var rest = key.Substring(2);
                var dot = rest.IndexOf('.');
                if (dot <= 0) continue;
                ids.Add(rest.Substring(0, dot));
            }

            foreach (var id in ids)
            {
                var p = new BjPlayer
                {
                    PlayerId = id,
                    IsAi = id.StartsWith("ai-")
                };

                if (msg.Data.TryGetValue($"p.{id}.hand", out var hand))
                    p.Hand = LanBjHost.DecodeCards(hand);
                if (msg.Data.TryGetValue($"p.{id}.bet", out var bet) && int.TryParse(bet, out var bv))
                    p.Bet = bv;
                if (msg.Data.TryGetValue($"p.{id}.stood", out var stood))
                    p.Stood = stood == "1";
                if (msg.Data.TryGetValue($"p.{id}.busted", out var busted))
                    p.Busted = busted == "1";
                if (msg.Data.TryGetValue($"p.{id}.result", out var result))
                    p.Result = result;

                s.Players.Add(p);
            }

            _state = s;
            Render();
        }

        // ==================== 界面 ====================

        private void BuildUi()
        {
            var stack = new VStack(_content);

            stack.Button("← 离开牌桌", () =>
            {
                LanGameRouter.Instance.LeaveGame();
                _app.ShowLan();
            }, 88f);

            stack.FixedCard(inner =>
            {
                var dl = UiFactory.Label(inner, "庄家", Theme.FontSmall, Theme.Muted,
                    TextAnchor.MiddleCenter);
                UiFactory.TopStretch(dl.rectTransform, 36f);

                _dealerText = UiFactory.Label(inner, "—", Theme.FontHead, Theme.Ink,
                    TextAnchor.MiddleCenter, bold: true);
                UiFactory.TopStretch(_dealerText.rectTransform, 44f, 36f);

                _dealerHost = UiFactory.Node(inner, "DealerCards");
                UiFactory.TopStretch(_dealerHost, 130f, 82f);

                _statusText = UiFactory.Label(inner, "等待开始", Theme.FontBody, Theme.Accent,
                    TextAnchor.MiddleCenter, bold: true);
                var sr = _statusText.rectTransform;
                sr.anchorMin = new Vector2(0f, 0.5f);
                sr.anchorMax = new Vector2(1f, 0.5f);
                sr.pivot = new Vector2(0.5f, 0.5f);
                sr.offsetMin = new Vector2(0f, -28f);
                sr.offsetMax = new Vector2(0f, 28f);

                _playersHost = UiFactory.Node(inner, "Players");
                UiFactory.BottomStretch(_playersHost, 260f, 10f);
            }, 660f);

            _logText = stack.Text("", Theme.FontSmall, Theme.Muted);

            _actionHost = UiFactory.Node(_content, "Actions");
            stack.Add(_actionHost, 200f);

            stack.Finish();
        }

        private void Render()
        {
            var me = _state.Players.FirstOrDefault(p => p.PlayerId == _myId);
            var myTurn = _state.CurrentPlayerId == _myId && _state.Phase == "playing";

            // ---------- 庄家 ----------
            UiFactory.Clear(_dealerHost);
            if (_state.Dealer.Count == 0)
            {
                var hint = UiFactory.Label(_dealerHost, "—", Theme.FontBody, Theme.Muted,
                    TextAnchor.MiddleCenter);
                UiFactory.Center(hint.rectTransform, 200f, 60f);
                _dealerText.text = "—";
            }
            else
            {
                var hideSecond = _state.DealerHidden;
                LayoutRow(_dealerHost, _state.Dealer.Count, 84f, 8f, i =>
                    MakeCard(_dealerHost, _state.Dealer[i], hideSecond && i == 1, 0.76f));

                _dealerText.text = hideSecond
                    ? $"{Points(new List<Card> { _state.Dealer[0] })} + ?"
                    : $"{Points(_state.Dealer)} 点";
            }

            // ---------- 状态 ----------
            _statusText.text = _state.Phase switch
            {
                "betting" => "下注阶段",
                "playing" => myTurn ? "轮到你行动" : "等待其他玩家…",
                "settled" => "本局结束",
                _ => ""
            };

            // ---------- 玩家 ----------
            UiFactory.Clear(_playersHost);
            var count = _state.Players.Count;
            if (count > 0)
            {
                var geo = SeatGeometry.For(count, _playersHost.rect.width);

                for (var i = 0; i < count; i++)
                {
                    var p = _state.Players[i];
                    var pos = geo.Position(i);

                    var cell = UiFactory.Node(_playersHost, $"P{i}");
                    var cr = cell;
                    cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 0.5f);
                    cr.pivot = new Vector2(0f, 0.5f);
                    cr.sizeDelta = new Vector2(geo.CellWidth, geo.CellHeight + 40f);
                    cr.anchoredPosition = pos;

                    var isMe = p.PlayerId == _myId;
                    var name = p.IsAi ? $"电脑 {i + 1}" : DisplayName(p.PlayerId);

                    var mood = p.Busted ? AvatarVisual.Mood.Sad
                        : p.Result.StartsWith("赢") ? AvatarVisual.Mood.Happy
                        : AvatarVisual.Mood.Neutral;

                    var avatar = AvatarVisual.Create(cell, name, mood, i, isMe, geo.AvatarSize);
                    var ar = avatar;
                    ar.anchorMin = ar.anchorMax = new Vector2(0f, 1f);
                    ar.pivot = new Vector2(0f, 1f);
                    ar.anchoredPosition = Vector2.zero;

                    var nt = UiFactory.Label(cell, geo.ShortName(name), geo.NameFont,
                        isMe ? Theme.Accent : Theme.Ink, TextAnchor.UpperLeft, bold: true);
                    var nr = nt.rectTransform;
                    nr.anchorMin = nr.anchorMax = new Vector2(0f, 1f);
                    nr.pivot = new Vector2(0f, 1f);
                    nr.sizeDelta = new Vector2(geo.CellWidth - geo.AvatarSize - 4f, 26f);
                    nr.anchoredPosition = new Vector2(geo.AvatarSize + 4f, 0f);

                    var info = p.Hand.Count == 0 ? "未下注"
                        : p.Busted ? $"爆牌 {Points(p.Hand)}"
                        : p.Stood ? $"{Points(p.Hand)} 点·停"
                        : $"{Points(p.Hand)} 点";

                    var it = UiFactory.Label(cell, info, geo.StatusFont,
                        p.Busted ? Theme.Danger : Theme.Muted, TextAnchor.UpperLeft);
                    var ir = it.rectTransform;
                    ir.anchorMin = ir.anchorMax = new Vector2(0f, 1f);
                    ir.pivot = new Vector2(0f, 1f);
                    ir.sizeDelta = new Vector2(geo.CellWidth - geo.AvatarSize - 4f, 24f);
                    ir.anchoredPosition = new Vector2(geo.AvatarSize + 4f, -26f);

                    if (!string.IsNullOrEmpty(p.Result))
                    {
                        var rt = UiFactory.Label(cell, p.Result, geo.StatusFont - 2,
                            p.Result.StartsWith("赢") ? Theme.Success : Theme.Danger,
                            TextAnchor.UpperLeft, bold: true);
                        var rr = rt.rectTransform;
                        rr.anchorMin = rr.anchorMax = new Vector2(0f, 1f);
                        rr.pivot = new Vector2(0f, 1f);
                        rr.sizeDelta = new Vector2(geo.CellWidth - geo.AvatarSize - 4f, 24f);
                        rr.anchoredPosition = new Vector2(geo.AvatarSize + 4f, -50f);
                    }

                    if (p.Bet > 0)
                    {
                        var chips = ChipVisual.Create(cell, p.Bet, geo.ChipScale * 0.9f);
                        chips.anchoredPosition = new Vector2(geo.AvatarSize * 0.5f, -geo.CellHeight * 0.30f);
                    }
                }
            }

            _logText.text = BuildLog(me);
            BuildActions(me, myTurn);
        }

        private string BuildLog(BjPlayer me)
        {
            if (_state.Phase == "betting")
                return _isHost ? "设置下注后点「发牌」开始本局。" : "等待房主发牌…";

            if (_state.Phase == "settled")
            {
                var wins = _state.Players.Count(p => p.Result.StartsWith("赢"));
                return $"本局结束：{wins} 人获胜。";
            }

            if (me == null) return "观战中。";
            if (_state.CurrentPlayerId == _myId) return "轮到你了。";
            return "等待其他玩家行动…";
        }

        private void BuildActions(BjPlayer me, bool myTurn)
        {
            UiFactory.Clear(_actionHost);

            var buttons = new List<Button>();

            if (_state.Phase == "betting")
            {
                if (!_isHost)
                    buttons.Add(UiFactory.Button(_actionHost, $"下注 ${_myBet:N0}", SendBet));
                if (_isHost)
                    buttons.Add(UiFactory.Primary(_actionHost, "开始发牌", () => Send("bj_deal")));
            }
            else if (_state.Phase == "playing")
            {
                if (myTurn)
                {
                    buttons.Add(UiFactory.Primary(_actionHost, "要 牌", () => Send("bj_hit")));
                    buttons.Add(UiFactory.Button(_actionHost, "停 牌", () => Send("bj_stand")));
                    if (me != null && me.Hand.Count == 2)
                        buttons.Add(UiFactory.Button(_actionHost, "双 倍", () => Send("bj_double")));
                }
            }
            else if (_state.Phase == "settled")
            {
                if (_isHost)
                    buttons.Add(UiFactory.Primary(_actionHost, "下一局", () => Send("bj_reset")));
            }

            if (buttons.Count == 0)
            {
                var wait = UiFactory.Label(_actionHost, "等待中…", Theme.FontBody, Theme.Muted,
                    TextAnchor.MiddleCenter);
                UiFactory.Fill(wait.rectTransform);
                return;
            }

            const float gap = 12f;
            var totalWidth = _actionHost.rect.width;
            if (totalWidth <= 1f) totalWidth = 1000f;

            var count = buttons.Count;
            var btnWidth = Mathf.Min(300f, (totalWidth - gap * (count - 1)) / count);

            for (var i = 0; i < count; i++)
            {
                var rt = buttons[i].GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(btnWidth, 96f);
                rt.anchoredPosition = new Vector2(i * (btnWidth + gap), 0f);
            }
        }

        // ==================== 动作 ====================

        private void Send(string type)
        {
            LanSession.Instance.SendToRoom(new NetMessage { Type = type });
        }

        private void SendBet()
        {
            LanSession.Instance.SendToRoom(new NetMessage
            {
                Type = "bj_bet",
                Data = new Dictionary<string, string> { ["bet"] = _myBet.ToString() }
            });
        }

        // ==================== 辅助 ====================

        private string DisplayName(string playerId)
        {
            var p = LanSession.Instance.Session?.Players.FirstOrDefault(x => x.Id == playerId);
            return p?.Name ?? playerId;
        }

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

        private static void LayoutRow(RectTransform host, int count, float itemWidth, float gap,
            Func<int, RectTransform> create)
        {
            if (count <= 0) return;
            var totalWidth = count * itemWidth + (count - 1) * gap;
            var startX = -totalWidth / 2f + itemWidth / 2f;
            for (var i = 0; i < count; i++)
            {
                var rt = create(i);
                if (rt == null) continue;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(itemWidth, rt.sizeDelta.y);
                rt.anchoredPosition = new Vector2(startX + i * (itemWidth + gap), 0f);
            }
        }

        private static RectTransform MakeCard(Transform parent, Card card, bool hidden, float scale)
        {
            var go = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var img = go.GetComponent<Image>();
            img.sprite = UiFactory.RoundSprite;
            img.type = Image.Type.Sliced;
            img.color = hidden ? Theme.Accent : Theme.CardFace;
            img.raycastTarget = false;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = hidden ? Theme.AccentDeep : Theme.CardBorder;
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(110f * scale, 155f * scale);

            if (hidden)
            {
                var q = UiFactory.Label(go.transform, "?", Mathf.RoundToInt(52 * scale), Color.white,
                    TextAnchor.MiddleCenter, bold: true);
                UiFactory.Fill(q.rectTransform);
            }
            else
            {
                var color = card.IsRed ? Theme.CardRed : Theme.CardBlack;

                var rank = UiFactory.Label(go.transform, card.RankText,
                    Mathf.RoundToInt(42 * scale), color, TextAnchor.MiddleCenter, bold: true);
                var rr = rank.rectTransform;
                rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 0.64f);
                rr.sizeDelta = new Vector2(88f * scale, 54f * scale);

                var suit = UiFactory.Label(go.transform, card.SuitName,
                    Mathf.RoundToInt(20 * scale), color, TextAnchor.MiddleCenter);
                var sr = suit.rectTransform;
                sr.anchorMin = sr.anchorMax = new Vector2(0.5f, 0.30f);
                sr.sizeDelta = new Vector2(88f * scale, 34f * scale);
            }

            return rt;
        }
    }
}
