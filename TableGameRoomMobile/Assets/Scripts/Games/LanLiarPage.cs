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
    /// 联机骗子扑克对局页 —— 对应桌面版 Net/LanLiar.cs 的渲染部分。
    /// </summary>
    public sealed class LanLiarPage : MonoBehaviour
    {
        // ==================== 状态 ====================

        public sealed class LiarState
        {
            public string RoomId = "";
            public string Phase = "playing";
            public List<Card> Hand = new List<Card>();
            public Dictionary<string, int> OtherCounts = new Dictionary<string, int>();
            public int Pile;
            public string LastClaim = "";
            public int LastCount;
            public bool Awaiting;
            public string CurrentPlayerId = "";
            public string Winner = "";
            public string Log = "";
            public int Stake = 100;
        }

        private static readonly string[] ClaimRanks = { "7", "8", "9", "10", "J", "Q", "K", "A" };

        // ==================== 依赖 ====================

        private App _app;
        private RectTransform _content;
        private RoomInfo _room;
        private string _myId = "";
        private bool _isHost;

        private LiarState _state = new LiarState();
        private readonly HashSet<int> _selected = new HashSet<int>();
        private string _claim = "7";

        private Text _statusText;
        private Text _pileText;
        private Text _logText;
        private Text _resultText;
        private RectTransform _opponentHost;
        private RectTransform _myHandHost;
        private RectTransform _actionHost;

        public string RoomId => _room?.Id ?? "";

        // ==================== 入口 ====================

        public static LanLiarPage Show(RectTransform content, RoomInfo room, string myId)
        {
            var parent = content.parent ?? content;

            var go = new GameObject("LanLiarPage", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiFactory.Fill((RectTransform)go.transform);

            var page = go.AddComponent<LanLiarPage>();
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

        /// <summary>收到主机的 liar_state。</summary>
        public void ApplyState(NetMessage msg)
        {
            if (msg?.Data == null) return;

            var s = new LiarState { RoomId = msg.RoomId ?? "" };

            if (msg.Data.TryGetValue("phase", out var phase)) s.Phase = phase;
            if (msg.Data.TryGetValue("current", out var cur)) s.CurrentPlayerId = cur;
            if (msg.Data.TryGetValue("pile", out var pile) && int.TryParse(pile, out var pv)) s.Pile = pv;
            if (msg.Data.TryGetValue("lastClaim", out var lc)) s.LastClaim = lc;
            if (msg.Data.TryGetValue("lastCount", out var lcn) && int.TryParse(lcn, out var lcv)) s.LastCount = lcv;
            if (msg.Data.TryGetValue("awaiting", out var aw)) s.Awaiting = aw == "1";
            if (msg.Data.TryGetValue("winner", out var w)) s.Winner = w;
            if (msg.Data.TryGetValue("log", out var log)) s.Log = log;
            if (msg.Data.TryGetValue("stake", out var st) && int.TryParse(st, out var stv)) s.Stake = stv;
            if (msg.Data.TryGetValue("hand", out var hand))
                s.Hand = LanLiarHost.DecodeCards(hand);

            foreach (var key in msg.Data.Keys)
            {
                if (!key.StartsWith("cnt.")) continue;
                var id = key.Substring(4);
                if (int.TryParse(msg.Data[key], out var cnt))
                    s.OtherCounts[id] = cnt;
            }

            _state = s;

            // 清理越界选中
            _selected.RemoveWhere(i => i >= s.Hand.Count);

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
                _statusText = UiFactory.Label(inner, "准备开始", Theme.FontHead, Theme.Ink,
                    TextAnchor.MiddleCenter, bold: true);
                UiFactory.TopStretch(_statusText.rectTransform, 50f);

                _pileText = UiFactory.Label(inner, "牌堆 0 张", Theme.FontBody, Theme.Muted,
                    TextAnchor.MiddleCenter);
                UiFactory.TopStretch(_pileText.rectTransform, 38f, 54f);

                _opponentHost = UiFactory.Node(inner, "Opponent");
                var or = _opponentHost;
                or.anchorMin = new Vector2(0f, 0.5f);
                or.anchorMax = new Vector2(1f, 0.5f);
                or.pivot = new Vector2(0.5f, 0.5f);
                or.offsetMin = new Vector2(0f, -50f);
                or.offsetMax = new Vector2(0f, 40f);

                var ml = UiFactory.Label(inner, "我的手牌", Theme.FontSmall, Theme.Muted,
                    TextAnchor.MiddleCenter);
                UiFactory.BottomStretch(ml.rectTransform, 32f, 176f);

                _myHandHost = UiFactory.Node(inner, "MyHand");
                UiFactory.BottomStretch(_myHandHost, 170f, 6f);
            }, 600f);

            _resultText = stack.CenterText("", Theme.FontHead, Theme.Accent, bold: true, height: 48f);
            _logText = stack.Text("", Theme.FontSmall, Theme.Muted);

            _actionHost = UiFactory.Node(_content, "Actions");
            stack.Add(_actionHost, 300f);

            stack.Finish();
        }

        private void Render()
        {
            var myTurn = _state.CurrentPlayerId == _myId && _state.Phase == "playing";

            _statusText.text = _state.Phase == "settled" ? "本局结束"
                : myTurn ? "轮到你出牌" : "等待对手…";

            _pileText.text = $"牌堆 {_state.Pile} 张" +
                (string.IsNullOrEmpty(_state.LastClaim) ? "" : $"　·　上次声明 {_state.LastClaim} × {_state.LastCount}");

            // ---------- 对手 ----------
            UiFactory.Clear(_opponentHost);
            var idx = 0;
            foreach (var kv in _state.OtherCounts)
            {
                var id = kv.Key;
                var count = kv.Value;

                var cell = UiFactory.Node(_opponentHost, $"Opp{idx}");
                var cr = cell;
                cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 0.5f);
                cr.pivot = new Vector2(0f, 0.5f);
                cr.sizeDelta = new Vector2(220f, 96f);
                cr.anchoredPosition = new Vector2(-110f + idx * 230f, 0f);

                var name = id.StartsWith("ai-") ? $"电脑 {id.Substring(3)}" : DisplayName(id);
                var isCurrent = id == _state.CurrentPlayerId;

                var avatar = AvatarVisual.Create(cell, name,
                    isCurrent ? AvatarVisual.Mood.Angry : AvatarVisual.Mood.Neutral,
                    idx, false, 64f);
                var ar = avatar;
                ar.anchorMin = ar.anchorMax = new Vector2(0f, 1f);
                ar.pivot = new Vector2(0f, 1f);
                ar.anchoredPosition = Vector2.zero;

                var nt = UiFactory.Label(cell, name, Theme.FontSmall - 4,
                    isCurrent ? Theme.Accent : Theme.Ink, TextAnchor.UpperLeft, bold: true);
                var nr = nt.rectTransform;
                nr.anchorMin = nr.anchorMax = new Vector2(0f, 1f);
                nr.pivot = new Vector2(0f, 1f);
                nr.sizeDelta = new Vector2(150f, 26f);
                nr.anchoredPosition = new Vector2(68f, 0f);

                var ct = UiFactory.Label(cell, $"{count} 张手牌", Theme.FontSmall - 8,
                    Theme.Muted, TextAnchor.UpperLeft);
                var ctr = ct.rectTransform;
                ctr.anchorMin = ctr.anchorMax = new Vector2(0f, 1f);
                ctr.pivot = new Vector2(0f, 1f);
                ctr.sizeDelta = new Vector2(150f, 24f);
                ctr.anchoredPosition = new Vector2(68f, -26f);

                idx++;
            }

            // ---------- 我的手牌 ----------
            UiFactory.Clear(_myHandHost);
            if (_state.Hand.Count > 0)
            {
                LayoutRow(_myHandHost, _state.Hand.Count, 92f, 8f, i =>
                {
                    var selected = _selected.Contains(i);
                    return MakeCard(_myHandHost, _state.Hand[i], false, 0.8f, selected, i);
                });
            }
            else
            {
                var hint = UiFactory.Label(_myHandHost, "没有手牌", Theme.FontBody, Theme.Muted,
                    TextAnchor.MiddleCenter);
                UiFactory.Center(hint.rectTransform, 300f, 60f);
            }

            _resultText.text = _state.Phase == "settled" ? _state.Winner : "";
            _logText.text = _state.Log;

            BuildActions(myTurn);
        }

        private void BuildActions(bool myTurn)
        {
            UiFactory.Clear(_actionHost);

            var totalWidth = _actionHost.rect.width;
            if (totalWidth <= 1f) totalWidth = 1000f;

            // ---------- 第一行：声明点数 ----------
            if (myTurn && !_state.Awaiting)
            {
                const float gap = 6f;
                var cellWidth = (totalWidth - gap * (ClaimRanks.Length - 1)) / ClaimRanks.Length;

                for (var i = 0; i < ClaimRanks.Length; i++)
                {
                    var r = ClaimRanks[i];
                    var picked = _claim == r;

                    var btn = UiFactory.Button(_actionHost, r, () =>
                    {
                        _claim = r;
                        Render();
                    }, picked ? Theme.AccentSoft : Theme.Panel);

                    var rt = btn.GetComponent<RectTransform>();
                    rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                    rt.pivot = new Vector2(0f, 1f);
                    rt.sizeDelta = new Vector2(cellWidth, 76f);
                    rt.anchoredPosition = new Vector2(i * (cellWidth + gap), 0f);

                    if (picked)
                    {
                        var txt = btn.GetComponentInChildren<Text>();
                        if (txt != null) txt.fontStyle = FontStyle.Bold;
                    }
                }
            }

            // ---------- 第二行：操作 ----------
            var buttons = new List<Button>();

            if (_state.Phase == "settled")
            {
                if (_isHost)
                    buttons.Add(UiFactory.Primary(_actionHost, "下一局", () => Send("liar_start")));
            }
            else if (myTurn)
            {
                if (_state.Awaiting)
                {
                    buttons.Add(UiFactory.Primary(_actionHost, "质 疑", () => Send("liar_challenge")));
                    buttons.Add(UiFactory.Button(_actionHost, "继续出牌", () => Send("liar_pass")));
                }
                else
                {
                    var count = Math.Max(1, _selected.Count);
                    buttons.Add(UiFactory.Primary(_actionHost, $"出 {count} 张", () => PlaySelected()));
                    buttons.Add(UiFactory.Button(_actionHost, "清空选择", () =>
                    {
                        _selected.Clear();
                        Render();
                    }));
                }
            }

            if (buttons.Count == 0)
            {
                var text = _state.Phase == "settled" ? "等待房主开下一局…" : "等待对手行动…";
                var wait = UiFactory.Label(_actionHost, text, Theme.FontBody, Theme.Muted,
                    TextAnchor.MiddleCenter);
                var wr = wait.rectTransform;
                wr.anchorMin = new Vector2(0f, 0f);
                wr.anchorMax = new Vector2(1f, 0f);
                wr.pivot = new Vector2(0.5f, 0f);
                wr.sizeDelta = new Vector2(0f, 90f);
                wr.anchoredPosition = Vector2.zero;
                return;
            }

            const float gap2 = 12f;
            var count2 = buttons.Count;
            var btnWidth = Mathf.Min(300f, (totalWidth - gap2 * (count2 - 1)) / count2);

            for (var i = 0; i < count2; i++)
            {
                var rt = buttons[i].GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
                rt.pivot = new Vector2(0f, 0f);
                rt.sizeDelta = new Vector2(btnWidth, 92f);
                rt.anchoredPosition = new Vector2(i * (btnWidth + gap2), 0f);
            }
        }

        // ==================== 动作 ====================

        private void PlaySelected()
        {
            if (_selected.Count == 0) return;

            var indices = string.Join(",", _selected.OrderBy(i => i));

            LanSession.Instance.SendToRoom(new NetMessage
            {
                Type = "liar_play",
                Data = new Dictionary<string, string>
                {
                    ["indices"] = indices,
                    ["claim"] = _claim,
                    ["count"] = _selected.Count.ToString()
                }
            });

            _selected.Clear();
        }

        private void Send(string type)
        {
            LanSession.Instance.SendToRoom(new NetMessage { Type = type });
        }

        private void ToggleSelect(int index)
        {
            if (_selected.Contains(index)) _selected.Remove(index);
            else _selected.Add(index);
            Render();
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

        private RectTransform MakeCard(Transform parent, Card card, bool hidden, float scale,
            bool selected, int index)
        {
            var go = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var img = go.GetComponent<Image>();
            img.sprite = UiFactory.RoundSprite;
            img.type = Image.Type.Sliced;
            img.color = hidden ? Theme.Accent : (selected ? Theme.AccentSoft : Theme.CardFace);

            var outline = go.AddComponent<Outline>();
            outline.effectColor = selected ? Theme.Accent : (hidden ? Theme.AccentDeep : Theme.CardBorder);
            outline.effectDistance = new Vector2(selected ? 3f : 1.5f, selected ? -3f : -1.5f);

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => ToggleSelect(index));

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
