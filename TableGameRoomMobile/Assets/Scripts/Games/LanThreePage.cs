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
    /// 联机炸金花对局页 —— 对应桌面版 Net/LanThree.cs 的渲染部分。
    /// </summary>
    public sealed class LanThreePage : MonoBehaviour
    {
        // ==================== 状态 ====================

        public sealed class ThreePlayer
        {
            public string PlayerId = "";
            public int Committed;
            public bool Folded;
            public bool Seen;
            public string Result = "";
            public bool IsAi;
        }

        public sealed class ThreeState
        {
            public string RoomId = "";
            public string Phase = "betting";
            public List<Card> MyCards = new List<Card>();
            public List<ThreePlayer> Players = new List<ThreePlayer>();
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

        private ThreeState _state = new ThreeState();

        private Text _potText;
        private Text _statusText;
        private Text _logText;
        private Text _resultText;
        private RectTransform _playersHost;
        private RectTransform _myCardsHost;
        private RectTransform _actionHost;

        public string RoomId => _room?.Id ?? "";

        // ==================== 入口 ====================

        public static LanThreePage Show(RectTransform content, RoomInfo room, string myId)
        {
            var parent = content.parent ?? content;

            var go = new GameObject("LanThreePage", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiFactory.Fill((RectTransform)go.transform);

            var page = go.AddComponent<LanThreePage>();
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

        /// <summary>收到主机的 three_state。</summary>
        public void ApplyState(NetMessage msg)
        {
            if (msg?.Data == null) return;

            var s = new ThreeState { RoomId = msg.RoomId ?? "" };

            if (msg.Data.TryGetValue("phase", out var phase)) s.Phase = phase;
            if (msg.Data.TryGetValue("current", out var cur)) s.CurrentPlayerId = cur;
            if (msg.Data.TryGetValue("pot", out var pot) && int.TryParse(pot, out var pv)) s.Pot = pv;
            if (msg.Data.TryGetValue("stake", out var stake) && int.TryParse(stake, out var sv)) s.Stake = sv;
            if (msg.Data.TryGetValue("winner", out var w)) s.Winner = w;
            if (msg.Data.TryGetValue("log", out var log)) s.Log = log;
            if (msg.Data.TryGetValue("cards", out var cards))
                s.MyCards = LanThreeHost.DecodeCards(cards);

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
                var p = new ThreePlayer
                {
                    PlayerId = id,
                    IsAi = id.StartsWith("ai-")
                };

                if (msg.Data.TryGetValue($"p.{id}.committed", out var c) && int.TryParse(c, out var cv))
                    p.Committed = cv;
                if (msg.Data.TryGetValue($"p.{id}.folded", out var f))
                    p.Folded = f == "1";
                if (msg.Data.TryGetValue($"p.{id}.seen", out var sn))
                    p.Seen = sn == "1";
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
                _potText = UiFactory.Label(inner, "底池 $0", Theme.FontTitle, Theme.Ink,
                    TextAnchor.MiddleCenter, bold: true);
                UiFactory.TopStretch(_potText.rectTransform, 50f);

                _statusText = UiFactory.Label(inner, "准备开始", Theme.FontBody, Theme.Accent,
                    TextAnchor.MiddleCenter, bold: true);
                UiFactory.TopStretch(_statusText.rectTransform, 40f, 52f);

                _playersHost = UiFactory.Node(inner, "Players");
                var pr = _playersHost;
                pr.anchorMin = new Vector2(0f, 0.5f);
                pr.anchorMax = new Vector2(1f, 0.5f);
                pr.pivot = new Vector2(0.5f, 0.5f);
                pr.offsetMin = new Vector2(0f, -110f);
                pr.offsetMax = new Vector2(0f, 40f);

                _myCardsHost = UiFactory.Node(inner, "MyCards");
                UiFactory.BottomStretch(_myCardsHost, 160f, 6f);
            }, 640f);

            _resultText = stack.CenterText("", Theme.FontHead, Theme.Accent, bold: true, height: 48f);
            _logText = stack.Text("", Theme.FontSmall, Theme.Muted);

            _actionHost = UiFactory.Node(_content, "Actions");
            stack.Add(_actionHost, 120f);

            stack.Finish();
        }

        private void Render()
        {
            var me = _state.Players.FirstOrDefault(p => p.PlayerId == _myId);
            var myTurn = _state.CurrentPlayerId == _myId && _state.Phase == "betting";

            _potText.text = $"底池 ${_state.Pot:N0}";

            _statusText.text = _state.Phase == "showdown" ? "摊牌"
                : me == null ? "观战中"
                : me.Folded ? "已弃牌"
                : me.Seen ? "已看牌"
                : "未看牌";

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

                    var info = p.Folded ? "已弃牌"
                        : _state.Phase == "showdown" && !string.IsNullOrEmpty(p.Result) ? p.Result
                        : $"${p.Committed:N0}";

                    var it = UiFactory.Label(cell, info, geo.StatusFont,
                        Theme.Muted, TextAnchor.UpperLeft);
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

            // ---------- 我的牌 ----------
            UiFactory.Clear(_myCardsHost);
            if (_state.MyCards.Count > 0)
            {
                var avatar = AvatarVisual.Create(_myCardsHost, "你", AvatarVisual.Mood.Neutral, 0, true, 56f);
                var avr = avatar;
                avr.anchorMin = avr.anchorMax = new Vector2(0f, 0.5f);
                avr.pivot = new Vector2(0f, 0.5f);
                avr.anchoredPosition = new Vector2(4f, 0f);

                // 看牌前显示暗牌，看牌或摊牌后显示明牌
                var show = me?.Seen == true || _state.Phase == "showdown";
                LayoutRow(_myCardsHost, _state.MyCards.Count, 92f, 8f,
                    i => MakeCard(_myCardsHost, _state.MyCards[i], !show, 0.8f));
            }
            else
            {
                var hint = UiFactory.Label(_myCardsHost, "等待发牌", Theme.FontBody, Theme.Muted,
                    TextAnchor.MiddleCenter);
                UiFactory.Center(hint.rectTransform, 300f, 60f);
            }

            _resultText.text = _state.Phase == "showdown" ? _state.Winner : "";
            _logText.text = _state.Log;

            BuildActions(me, myTurn);
        }

        private void BuildActions(ThreePlayer me, bool myTurn)
        {
            UiFactory.Clear(_actionHost);

            var buttons = new List<Button>();

            if (_state.Phase == "showdown")
            {
                if (_isHost)
                    buttons.Add(UiFactory.Primary(_actionHost, "下一局", () => Send("three_start")));
            }
            else if (me != null && !me.Folded)
            {
                if (!me.Seen)
                    buttons.Add(UiFactory.Button(_actionHost, "看 牌", () => Send("three_see")));

                if (myTurn)
                {
                    buttons.Add(UiFactory.Button(_actionHost, $"跟注 ${_state.Stake:N0}", () => Send("three_call")));
                    buttons.Add(UiFactory.Primary(_actionHost, $"加注 ${_state.Stake * 2:N0}", () => Send("three_raise")));
                    buttons.Add(UiFactory.Button(_actionHost, "弃牌", () => Send("three_fold")));
                    buttons.Add(UiFactory.Button(_actionHost, "比 牌", () => Send("three_showdown")));
                }
            }

            if (buttons.Count == 0)
            {
                var text = _state.Phase == "showdown" ? "等待房主开下一局…"
                    : me?.Folded == true ? "你已弃牌，观战中…"
                    : "等待其他玩家行动…";
                var wait = UiFactory.Label(_actionHost, text, Theme.FontBody, Theme.Muted,
                    TextAnchor.MiddleCenter);
                UiFactory.Fill(wait.rectTransform);
                return;
            }

            const float gap = 10f;
            var totalWidth = _actionHost.rect.width;
            if (totalWidth <= 1f) totalWidth = 1000f;

            var count = buttons.Count;
            var btnWidth = Mathf.Min(240f, (totalWidth - gap * (count - 1)) / count);

            for (var i = 0; i < count; i++)
            {
                var rt = buttons[i].GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(btnWidth, 92f);
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
    }
}
