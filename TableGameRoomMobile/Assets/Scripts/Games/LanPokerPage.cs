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
    /// 联机德州扑克对局页 —— 对应桌面版 Net/LanPoker.cs 的渲染部分。
    ///
    /// 状态来自主机广播的 poker_state（扁平 Data 字典）。
    /// 底牌只发给本人，所以别人看到的永远是暗牌。
    /// </summary>
    public sealed class LanPokerPage : MonoBehaviour
    {
        // ==================== 状态 ====================

        public sealed class PokerPlayer
        {
            public string PlayerId = "";
            public int Committed;
            public bool Folded;
            public string Result = "";
            public bool IsAi;
        }

        public sealed class PokerState
        {
            public string RoomId = "";
            public string Phase = "preflop";
            public List<Card> Community = new List<Card>();
            public List<Card> MyCards = new List<Card>();
            public List<PokerPlayer> Players = new List<PokerPlayer>();
            public string CurrentPlayerId = "";
            public int Pot;
            public int Stake = 100;
            public string Winner = "";
            public string Log = "";
        }

        // ==================== 依赖 ====================

        private App _app;
        private RectTransform _content;
        private RoomInfo _room;
        private string _myId = "";
        private bool _isHost;

        private PokerState _state = new PokerState();

        private Text _phaseText;
        private Text _potText;
        private Text _logText;
        private Text _resultText;
        private RectTransform _communityHost;
        private RectTransform _playersHost;
        private RectTransform _myCardsHost;
        private RectTransform _actionHost;

        public string RoomId => _room?.Id ?? "";

        // ==================== 入口 ====================

        public static LanPokerPage Show(RectTransform content, RoomInfo room, string myId)
        {
            var parent = content.parent ?? content;

            var go = new GameObject("LanPokerPage", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiFactory.Fill((RectTransform)go.transform);

            var page = go.AddComponent<LanPokerPage>();
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

        /// <summary>收到主机的 poker_state。</summary>
        public void ApplyState(NetMessage msg)
        {
            if (msg?.Data == null) return;

            var s = new PokerState { RoomId = msg.RoomId ?? "" };

            if (msg.Data.TryGetValue("phase", out var phase)) s.Phase = phase;
            if (msg.Data.TryGetValue("current", out var cur)) s.CurrentPlayerId = cur;
            if (msg.Data.TryGetValue("pot", out var pot) && int.TryParse(pot, out var pv)) s.Pot = pv;
            if (msg.Data.TryGetValue("stake", out var stake) && int.TryParse(stake, out var sv)) s.Stake = sv;
            if (msg.Data.TryGetValue("winner", out var w)) s.Winner = w;
            if (msg.Data.TryGetValue("log", out var log)) s.Log = log;
            if (msg.Data.TryGetValue("community", out var comm))
                s.Community = LanPokerHost.DecodeCards(comm);
            if (msg.Data.TryGetValue("cards", out var cards))
                s.MyCards = LanPokerHost.DecodeCards(cards);

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
                var p = new PokerPlayer
                {
                    PlayerId = id,
                    IsAi = id.StartsWith("ai-")
                };

                if (msg.Data.TryGetValue($"p.{id}.committed", out var c) && int.TryParse(c, out var cv))
                    p.Committed = cv;
                if (msg.Data.TryGetValue($"p.{id}.folded", out var f))
                    p.Folded = f == "1";
                if (msg.Data.TryGetValue($"p.{id}.result", out var r))
                    p.Result = r;

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
                _phaseText = UiFactory.Label(inner, "翻牌前", Theme.FontSmall, Theme.Muted,
                    TextAnchor.MiddleCenter);
                UiFactory.TopStretch(_phaseText.rectTransform, 36f);

                _potText = UiFactory.Label(inner, "底池 $0", Theme.FontTitle, Theme.Ink,
                    TextAnchor.MiddleCenter, bold: true);
                UiFactory.TopStretch(_potText.rectTransform, 50f, 38f);

                _communityHost = UiFactory.Node(inner, "Community");
                UiFactory.TopStretch(_communityHost, 140f, 92f);

                _playersHost = UiFactory.Node(inner, "Players");
                var pr = _playersHost;
                pr.anchorMin = new Vector2(0f, 0.5f);
                pr.anchorMax = new Vector2(1f, 0.5f);
                pr.pivot = new Vector2(0.5f, 0.5f);
                pr.offsetMin = new Vector2(0f, -110f);
                pr.offsetMax = new Vector2(0f, 40f);

                _myCardsHost = UiFactory.Node(inner, "MyCards");
                UiFactory.BottomStretch(_myCardsHost, 160f, 6f);
            }, 660f);

            _resultText = stack.CenterText("", Theme.FontHead, Theme.Accent, bold: true, height: 48f);
            _logText = stack.Text("", Theme.FontSmall, Theme.Muted);

            _actionHost = UiFactory.Node(_content, "Actions");
            stack.Add(_actionHost, 120f);

            stack.Finish();
        }

        private void Render()
        {
            var me = _state.Players.FirstOrDefault(p => p.PlayerId == _myId);
            var myTurn = _state.CurrentPlayerId == _myId &&
                         _state.Phase != "showdown" && _state.Phase != "waiting";

            _phaseText.text = PhaseName(_state.Phase);
            _potText.text = $"底池 ${_state.Pot:N0}";

            // ---------- 公共牌 ----------
            UiFactory.Clear(_communityHost);
            LayoutRow(_communityHost, 5, 96f, 8f, i =>
                i < _state.Community.Count
                    ? MakeCard(_communityHost, _state.Community[i], false, 0.82f)
                    : MakePlaceholder(_communityHost, 0.82f));

            // ---------- 其他玩家 ----------
            UiFactory.Clear(_playersHost);
            var others = _state.Players.Where(p => p.PlayerId != _myId).ToList();

            if (others.Count > 0)
            {
                var geo = SeatGeometry.For(others.Count, _playersHost.rect.width);

                for (var i = 0; i < others.Count; i++)
                {
                    var p = others[i];
                    var pos = geo.Position(i);

                    var cell = UiFactory.Node(_playersHost, $"P{i}");
                    var cr = cell;
                    cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 0.5f);
                    cr.pivot = new Vector2(0f, 0.5f);
                    cr.sizeDelta = new Vector2(geo.CellWidth, geo.CellHeight + 20f);
                    cr.anchoredPosition = pos;

                    var name = p.IsAi ? $"电脑 {i + 1}" : DisplayName(p.PlayerId);
                    var isCurrent = p.PlayerId == _state.CurrentPlayerId;

                    var mood = p.Folded ? AvatarVisual.Mood.Sad
                        : p.Result.StartsWith("赢") ? AvatarVisual.Mood.Happy
                        : isCurrent ? AvatarVisual.Mood.Angry
                        : AvatarVisual.Mood.Neutral;

                    var avatar = AvatarVisual.Create(cell, name, mood, i, false, geo.AvatarSize);
                    var ar = avatar;
                    ar.anchorMin = ar.anchorMax = new Vector2(0f, 1f);
                    ar.pivot = new Vector2(0f, 1f);
                    ar.anchoredPosition = Vector2.zero;

                    var nt = UiFactory.Label(cell, geo.ShortName(name), geo.NameFont,
                        isCurrent ? Theme.Accent : (p.Folded ? Theme.Muted : Theme.Ink),
                        TextAnchor.UpperLeft, bold: true);
                    var nr = nt.rectTransform;
                    nr.anchorMin = nr.anchorMax = new Vector2(0f, 1f);
                    nr.pivot = new Vector2(0f, 1f);
                    nr.sizeDelta = new Vector2(geo.CellWidth - geo.AvatarSize - 4f, 26f);
                    nr.anchoredPosition = new Vector2(geo.AvatarSize + 4f, 0f);

                    var info = p.Folded ? "已弃牌" : $"${p.Committed:N0}";
                    var it = UiFactory.Label(cell, info, geo.StatusFont,
                        p.Folded ? Theme.Muted : Theme.Muted, TextAnchor.UpperLeft);
                    var ir = it.rectTransform;
                    ir.anchorMin = ir.anchorMax = new Vector2(0f, 1f);
                    ir.pivot = new Vector2(0f, 1f);
                    ir.sizeDelta = new Vector2(geo.CellWidth - geo.AvatarSize - 4f, 24f);
                    ir.anchoredPosition = new Vector2(geo.AvatarSize + 4f, -26f);

                    if (p.Committed > 0 && !p.Folded)
                    {
                        var chips = ChipVisual.Create(cell, p.Committed, geo.ChipScale * 0.9f);
                        chips.anchoredPosition = new Vector2(geo.AvatarSize * 0.5f, -geo.CellHeight * 0.32f);
                    }
                }
            }

            // ---------- 我的底牌 ----------
            UiFactory.Clear(_myCardsHost);
            if (_state.MyCards.Count > 0)
            {
                var avatar = AvatarVisual.Create(_myCardsHost, "你", AvatarVisual.Mood.Neutral, 0, true, 56f);
                var avr = avatar;
                avr.anchorMin = avr.anchorMax = new Vector2(0f, 0.5f);
                avr.pivot = new Vector2(0f, 0.5f);
                avr.anchoredPosition = new Vector2(4f, 0f);

                LayoutRow(_myCardsHost, _state.MyCards.Count, 92f, 8f,
                    i => MakeCard(_myCardsHost, _state.MyCards[i], false, 0.8f));
            }
            else
            {
                var hint = UiFactory.Label(_myCardsHost, "等待发牌", Theme.FontBody, Theme.Muted,
                    TextAnchor.MiddleCenter);
                UiFactory.Center(hint.rectTransform, 300f, 60f);
            }

            // ---------- 结果 ----------
            if (_state.Phase == "showdown" && !string.IsNullOrEmpty(_state.Winner))
            {
                // Winner 字段在主机端已经包含"获胜，赢得底池 $X"，这里不再拼接
                _resultText.text = _state.Winner.Contains("获胜")
                    ? _state.Winner
                    : (_state.Winner == _myId ? "你赢得底池" : $"{DisplayName(_state.Winner)} 赢得底池");
            }
            else
            {
                _resultText.text = "";
            }

            _logText.text = _state.Log;

            BuildActions(me, myTurn);
        }

        private void BuildActions(PokerPlayer me, bool myTurn)
        {
            UiFactory.Clear(_actionHost);

            var buttons = new List<Button>();

            if (_state.Phase == "showdown")
            {
                if (_isHost)
                    buttons.Add(UiFactory.Primary(_actionHost, "下一局", () => Send("poker_start")));
            }
            else if (myTurn)
            {
                buttons.Add(UiFactory.Button(_actionHost, $"跟注 ${_state.Stake:N0}", () => Send("poker_call")));
                buttons.Add(UiFactory.Primary(_actionHost, $"加注 ${_state.Stake * 2:N0}", () => Send("poker_raise")));
                buttons.Add(UiFactory.Button(_actionHost, "弃牌", () => Send("poker_fold")));
            }

            if (buttons.Count == 0)
            {
                var text = _state.Phase == "showdown" ? "等待房主开下一局…" : "等待其他玩家行动…";
                var wait = UiFactory.Label(_actionHost, text, Theme.FontBody, Theme.Muted,
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

        // ==================== 辅助 ====================

        private string DisplayName(string playerId)
        {
            if (playerId.StartsWith("ai-")) return $"电脑 {playerId.Substring(3)}";
            var p = LanSession.Instance.Session?.Players.FirstOrDefault(x => x.Id == playerId);
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
                case "showdown": return "摊牌";
                default: return phase;
            }
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
                var q = UiFactory.Label(go.transform, "?", Mathf.RoundToInt(50 * scale), Color.white,
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

        private static RectTransform MakePlaceholder(Transform parent, float scale)
        {
            var go = new GameObject("Empty", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var img = go.GetComponent<Image>();
            img.sprite = UiFactory.RoundSprite;
            img.type = Image.Type.Sliced;
            img.color = Theme.AccentSoft;
            img.raycastTarget = false;

            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(110f * scale, 155f * scale);
            return rt;
        }
    }
}
