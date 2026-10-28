using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TableGameRoom.Mobile.Core;
using TableGameRoom.Mobile.UI;

namespace TableGameRoom.Mobile.Games
{
    /// <summary>
    /// 炸金花 —— 对应桌面版 Three.cs。
    /// 三张牌，看牌/跟注/加注/比牌/弃牌；AI 走 ThreeAiEngine。
    /// </summary>
    public sealed class ThreePage : MonoBehaviour
    {
        private sealed class Seat
        {
            public string Name = "";
            public List<Card> Cards = new List<Card>();
            public int Committed;
            public bool Folded;
            public ThreeAiProfile Profile = new ThreeAiProfile();
            public string Status = "等待";
            public string Emoji = "😐";
        }

        private App _app;
        private ThreeAiEngine _ai;

        /// <summary>表情池 —— 与桌面版 Three.cs 一致。</summary>
        private static readonly string[] Emojis = { "😐", "😄", "😞", "😠" };

        private string _playerEmoji = "😐";
        private readonly System.Random _rng = new System.Random();

        private readonly List<Seat> _seats = new List<Seat>();
        private readonly List<Card> _player = new List<Card>();

        private int _pot;
        private int _stake;
        private bool _inRound;
        private bool _showdown;
        private bool _playerLooked;
        private bool _playerFolded;
        private string _log = "";
        private int _playerCommitted;
        private int _playerRaises;

        private Text _potText;
        private Text _statusText;
        private Text _logText;
        private Text _resultText;
        private RectTransform _seatsHost;
        private RectTransform _playerHost;
        private RectTransform _potChipsHost;
        private RectTransform _actionHost;

        // ==================== 入口 ====================

        public static void Build(RectTransform content)
        {
            var go = new GameObject("ThreePage", typeof(RectTransform));
            go.transform.SetParent(content, false);
            UiFactory.Fill((RectTransform)go.transform);

            go.AddComponent<ThreePage>().Init(content);
        }

        private void Init(RectTransform content)
        {
            _app = App.Instance;
            _ai = new ThreeAiEngine(new System.Random());
            _stake = Mathf.Max(_app.TierMin, 100);

            var stack = new VStack(content);

            stack.Button("← 返回大厅", () => _app.ShowLobby(), 88f);

            // ---------- 牌桌 ----------
            stack.FixedCard(inner =>
            {
                _potText = UiFactory.Label(inner, "底池 $0", Theme.FontTitle, Theme.Ink,
                    TextAnchor.MiddleCenter, bold: true);
                UiFactory.TopStretch(_potText.rectTransform, 54f);

                _statusText = UiFactory.Label(inner, "准备开始", Theme.FontBody, Theme.Muted,
                    TextAnchor.MiddleCenter);
                UiFactory.TopStretch(_statusText.rectTransform, 42f, 58f);

                // 底池筹码堆
                _potChipsHost = UiFactory.Node(inner, "PotChips");
                var pcr = _potChipsHost;
                pcr.anchorMin = new Vector2(0f, 1f);
                pcr.anchorMax = new Vector2(1f, 1f);
                pcr.pivot = new Vector2(0.5f, 1f);
                pcr.offsetMin = new Vector2(0f, -478f);
                pcr.offsetMax = new Vector2(0f, -396f);

                _seatsHost = UiFactory.Node(inner, "Seats");
                var shr = _seatsHost;
                shr.anchorMin = new Vector2(0f, 0.5f);
                shr.anchorMax = new Vector2(1f, 0.5f);
                shr.pivot = new Vector2(0.5f, 0.5f);
                shr.offsetMin = new Vector2(0f, -110f);
                shr.offsetMax = new Vector2(0f, 60f);

                _playerHost = UiFactory.Node(inner, "PlayerCards");
                UiFactory.BottomStretch(_playerHost, 170f, 10f);
            }, 640f);

            // ---------- 结果 ----------
            _resultText = stack.CenterText("", Theme.FontHead, Theme.Accent, bold: true, height: 54f);

            // ---------- 日志 ----------
            _logText = stack.Text("", Theme.FontSmall, Theme.Muted);

            // ---------- 操作 ----------
            _actionHost = UiFactory.Node(content, "Actions");
            stack.Add(_actionHost, 110f);

            RefreshTable();
            BuildActions();

            stack.Finish();
        }

        // ==================== 操作 ====================

        private void BuildActions()
        {
            UiFactory.Clear(_actionHost);

            var buttons = new List<Button>();

            if (!_inRound || _showdown)
            {
                buttons.Add(UiFactory.Primary(_actionHost, _showdown ? "再来一局" : "开始牌局", StartRound));
            }
            else if (_playerFolded)
            {
                var wait = UiFactory.Label(_actionHost, "你已弃牌，等待结算…", Theme.FontBody,
                    Theme.Muted, TextAnchor.MiddleCenter);
                UiFactory.Fill(wait.rectTransform);
                return;
            }
            else
            {
                if (!_playerLooked)
                    buttons.Add(UiFactory.Button(_actionHost, "看牌", Look));

                buttons.Add(UiFactory.Button(_actionHost, $"跟注 ${_stake:N0}", () => PlayerAct(1)));
                buttons.Add(UiFactory.Primary(_actionHost, $"加注 ${_stake * 2:N0}", () => PlayerAct(2)));
                buttons.Add(UiFactory.Button(_actionHost, "比牌", Compare));
                buttons.Add(UiFactory.Button(_actionHost, "弃牌", () => PlayerAct(0)));
            }

            if (buttons.Count == 0) return;

            const float gap = 10f;
            var totalWidth = _actionHost.rect.width;
            if (totalWidth <= 1f) totalWidth = 1000f;

            var count = buttons.Count;
            var btnWidth = Mathf.Min(260f, (totalWidth - gap * (count - 1)) / count);

            for (var i = 0; i < count; i++)
            {
                var rt = buttons[i].GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(btnWidth, 100f);
                rt.anchoredPosition = new Vector2(i * (btnWidth + gap), 0f);
            }
        }

        // ==================== 开局 ====================

        private void StartRound()
        {
            if (!_app.TryPay(_stake))
            {
                _app.Msg($"余额不足：当前 ${_app.Save.Balance:N0}", "筹码不足");
                return;
            }

            _seats.Clear();
            _player.Clear();

            _pot = 0;
            _inRound = true;
            _showdown = false;
            _playerLooked = false;
            _playerFolded = false;
            _log = "";
            _playerCommitted = 0;
            _playerRaises = 0;

            var aiCount = Mathf.Clamp(_app.Seats - 1, 1, 6);
            var tierIndex = _app.TierIndex;
            var deck = Deck.New(new System.Random());

            for (var i = 0; i < 3; i++)
            {
                _player.Add(deck[deck.Count - 1]);
                deck.RemoveAt(deck.Count - 1);
            }

            for (var i = 0; i < aiCount; i++)
            {
                var seat = new Seat
                {
                    Name = $"电脑 {i + 1}",
                    Emoji = Emojis[_rng.Next(Emojis.Length)],
                    Profile = new ThreeAiProfile
                    {
                        EffectiveTier = tierIndex + 1,
                        PressureResistance = AiTuning.PressureResistance(tierIndex),
                        CalculationNoise = AiTuning.CalculationNoise(tierIndex),
                        BluffTendency = AiTuning.BluffTendency(tierIndex)
                    }
                };

                for (var k = 0; k < 3; k++)
                {
                    seat.Cards.Add(deck[deck.Count - 1]);
                    deck.RemoveAt(deck.Count - 1);
                }

                _seats.Add(seat);
            }

            // 底注
            _playerCommitted = _stake;
            _pot += _stake;

            foreach (var s in _seats)
            {
                s.Committed = _stake;
                _pot += _stake;
            }

            _log = "牌局开始，全部玩家各下底注。";

            RefreshTable();
            BuildActions();
        }

        private void Look()
        {
            _playerLooked = true;
            _log = $"你看牌了：{ThreeScore.Name(ThreeScore.Of(_player))}";
            RefreshTable();
            BuildActions();
        }

        private void PlayerAct(int action)
        {
            if (!_inRound || _showdown) return;

            if (action == 0)
            {
                _playerFolded = true;
                _log = "你弃牌了。";
                AiPlayOut();
                return;
            }

            var cost = action == 2 ? _stake * 2 : _stake;

            if (!_app.TryPay(cost))
            {
                _app.Msg("余额不足。", "筹码不足");
                return;
            }

            _playerCommitted += cost;
            _pot += cost;
            if (action == 2) _playerRaises++;

            _log = action == 2 ? $"你加注 ${cost:N0}。" : $"你跟注 ${cost:N0}。";

            AiPlayOut();
        }

        private void Compare()
        {
            if (!_inRound || _showdown || _playerFolded) return;

            if (!_app.TryPay(_stake))
            {
                _app.Msg("余额不足，无法比牌。", "筹码不足");
                return;
            }

            _playerCommitted += _stake;
            _pot += _stake;

            var alive = _seats.Where(s => !s.Folded).ToList();
            if (alive.Count == 0) { WinPot("对手全部弃牌"); return; }

            var target = alive[0];
            var mine = ThreeScore.Of(_player);
            var theirs = ThreeScore.Of(target.Cards);

            if (mine >= theirs)
            {
                target.Folded = true;
                target.Status = "比牌输";
                target.Emoji = "😞";
                _playerEmoji = "😄";
                _log = $"比牌：你的{ThreeScore.Name(mine)} 胜过 {target.Name} 的{ThreeScore.Name(theirs)}";
            }
            else
            {
                _playerFolded = true;
                _playerEmoji = "😞";
                target.Emoji = "😄";
                _log = $"比牌：你的{ThreeScore.Name(mine)} 输给 {target.Name} 的{ThreeScore.Name(theirs)}";
            }

            AiPlayOut();
        }

        private void AiPlayOut()
        {
            foreach (var s in _seats.Where(x => !x.Folded))
            {
                var ctx = new ThreeAiContext(_stake, _pot, _playerRaises, _playerFolded);

                if (!_ai.Continues(ctx, s.Profile))
                {
                    s.Folded = true;
                    s.Status = "弃牌";
                    s.Emoji = "😞";
                    _log += $" {s.Name} 弃牌。";
                    continue;
                }

                if (_ai.Raises(ctx, s.Profile))
                {
                    var cost = _stake * 2;
                    s.Committed += cost;
                    _pot += cost;
                    s.Status = "加注";
                    s.Emoji = "😄";
                    _log += $" {s.Name} 加注 ${cost:N0}。";
                }
                else
                {
                    s.Committed += _stake;
                    _pot += _stake;
                    s.Status = "跟注";
                    s.Emoji = "😐";
                }
            }

            var alive = _seats.Count(s => !s.Folded);

            if (alive == 0 && !_playerFolded)
            {
                WinPot("全部对手弃牌");
                return;
            }

            if (_playerFolded && alive <= 1)
            {
                _showdown = true;
                _inRound = false;
                _log += " 牌局结束。";
                RefreshTable();
                BuildActions();
                return;
            }

            if (alive <= 1 && !_playerFolded)
            {
                Showdown();
                return;
            }

            RefreshTable();
            BuildActions();
        }

        private void Showdown()
        {
            _showdown = true;
            _inRound = false;

            var mine = ThreeScore.Of(_player);
            var best = mine;
            var winner = "你";

            foreach (var s in _seats.Where(x => !x.Folded))
            {
                var theirs = ThreeScore.Of(s.Cards);
                if (theirs > best) { best = theirs; winner = s.Name; }
            }

            if (winner == "你")
            {
                var gained = _pot;
                _app.Win(gained);
                _log = $"摊牌：你以{ThreeScore.Name(mine)}赢得 ${gained:N0}";
                _app.Settle("three", _playerCommitted, gained, $"摊牌胜出（{ThreeScore.Name(mine)}）");
            }
            else
            {
                _log = $"摊牌：{winner} 以{ThreeScore.Name(best)}赢得 ${_pot:N0}";
                _app.Settle("three", _playerCommitted, 0, $"摊牌输给 {winner}（{ThreeScore.Name(best)}）");
            }

            RefreshTable();
            BuildActions();
        }

        private void WinPot(string reason)
        {
            var gained = _pot;
            _app.Win(gained);
            _log = $"{reason}，你赢得 ${gained:N0}";
            _app.Settle("three", _playerCommitted, gained, reason);

            _showdown = true;
            _inRound = false;

            RefreshTable();
            BuildActions();
        }

        // ==================== 渲染 ====================

        private void RefreshTable()
        {
            _potText.text = $"底池 ${_pot:N0}";
            _resultText.text = "";

            // 底池筹码堆
            UiFactory.Clear(_potChipsHost);
            if (_pot > 0)
            {
                var potChips = ChipVisual.Create(_potChipsHost, _pot, 0.80f);
                potChips.anchoredPosition = Vector2.zero;
            }

            _statusText.text = _showdown ? "摊牌"
                : _playerFolded ? "已弃牌"
                : _playerLooked ? $"已看牌：{ThreeScore.Name(ThreeScore.Of(_player))}"
                : "未看牌";

            UiFactory.Clear(_seatsHost);
            var seatCount = _seats.Count;
            var geo = SeatGeometry.For(seatCount, _seatsHost.rect.width);

            for (var i = 0; i < seatCount; i++)
            {
                var s = _seats[i];
                var pos = geo.Position(i);

                var cell = UiFactory.Node(_seatsHost, $"Cell{i}");
                var cr = cell;
                cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 0.5f);
                cr.pivot = new Vector2(0f, 0.5f);
                cr.sizeDelta = new Vector2(geo.CellWidth, geo.CellHeight);
                cr.anchoredPosition = pos;

                // 头像
                var avatar = AvatarVisual.Create(cell, s.Name, s.Emoji, i,
                    isPlayer: false, size: geo.AvatarSize);
                var ar = avatar;
                ar.anchorMin = ar.anchorMax = new Vector2(0f, 0.5f);
                ar.pivot = new Vector2(0f, 0.5f);
                ar.anchoredPosition = new Vector2(0f, geo.AvatarOffsetY);

                // 名字
                var nameText = UiFactory.Label(cell, geo.ShortName(s.Name), geo.NameFont,
                    s.Folded ? Theme.Muted : Theme.Ink, TextAnchor.UpperLeft, bold: true);
                var nr = nameText.rectTransform;
                nr.anchorMin = nr.anchorMax = new Vector2(0f, 1f);
                nr.pivot = new Vector2(0f, 1f);
                nr.sizeDelta = new Vector2(geo.CellWidth - geo.AvatarSize - 6f, 28f);
                nr.anchoredPosition = new Vector2(geo.AvatarSize + 4f, 0f);

                // 状态
                var statusText = UiFactory.Label(cell, s.Status, geo.StatusFont,
                    Theme.Muted, TextAnchor.UpperLeft);
                var sr = statusText.rectTransform;
                sr.anchorMin = sr.anchorMax = new Vector2(0f, 1f);
                sr.pivot = new Vector2(0f, 1f);
                sr.sizeDelta = new Vector2(geo.CellWidth - geo.AvatarSize - 6f, 24f);
                sr.anchoredPosition = new Vector2(geo.AvatarSize + 4f, -28f);

                // 筹码堆（头像正下方，不与名字/状态重叠）
                if (s.Committed > 0)
                {
                    var chips = ChipVisual.Create(cell, s.Committed, geo.ChipScale);
                    chips.anchoredPosition = new Vector2(geo.AvatarSize * 0.5f, -geo.CellHeight * 0.32f);
                }
            }

            UiFactory.Clear(_playerHost);
            if (_player.Count == 0)
            {
                var hint = UiFactory.Label(_playerHost, "等待发牌", Theme.FontBody, Theme.Muted,
                    TextAnchor.MiddleCenter);
                UiFactory.Center(hint.rectTransform, 300f, 60f);
            }
            else
            {
                // 玩家头像（小一号）
                var avatar = AvatarVisual.Create(_playerHost, "你", _playerEmoji, 0,
                    isPlayer: true, size: 56f);
                var avr = avatar;
                avr.anchorMin = avr.anchorMax = new Vector2(0f, 0.5f);
                avr.pivot = new Vector2(0f, 0.5f);
                avr.anchoredPosition = new Vector2(4f, 0f);

                var show = _playerLooked || _showdown;
                LayoutRow(_playerHost, _player.Count, 88f, 8f,
                    i => MakeCard(_playerHost, _player[i], !show));

                // 已下注筹码
                if (_playerCommitted > 0)
                {
                    var chips = ChipVisual.Create(_playerHost, _playerCommitted, 0.55f);
                    chips.anchoredPosition = new Vector2(300f, 0f);
                }
            }

            _logText.text = _log;
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

        private static RectTransform MakeCard(Transform parent, Card card, bool hidden, float scale = 1f)
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
                var q = UiFactory.Label(go.transform, "?", Mathf.RoundToInt(56 * scale), Color.white,
                    TextAnchor.MiddleCenter, bold: true);
                UiFactory.Fill(q.rectTransform);
            }
            else
            {
                var color = card.IsRed ? Theme.CardRed : Theme.CardBlack;

                var rank = UiFactory.Label(go.transform, card.RankText,
                    Mathf.RoundToInt(44 * scale), color, TextAnchor.MiddleCenter, bold: true);
                var rr = rank.rectTransform;
                rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 0.64f);
                rr.sizeDelta = new Vector2(90f * scale, 56f * scale);

                var suit = UiFactory.Label(go.transform, card.SuitName,
                    Mathf.RoundToInt(22 * scale), color, TextAnchor.MiddleCenter);
                var sr = suit.rectTransform;
                sr.anchorMin = sr.anchorMax = new Vector2(0.5f, 0.30f);
                sr.sizeDelta = new Vector2(90f * scale, 36f * scale);
            }

            return rt;
        }
    }
}
