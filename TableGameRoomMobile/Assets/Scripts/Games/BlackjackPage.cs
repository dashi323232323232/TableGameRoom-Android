using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TableGameRoom.Mobile.Core;
using TableGameRoom.Mobile.UI;

namespace TableGameRoom.Mobile.Games
{
    /// <summary>
    /// 21 点 —— 对应桌面版 Blackjack.cs。
    ///
    /// 布局：庄家在牌桌上方、状态居中、玩家在下方。
    /// 规则：A 可算 1 或 11、庄家 17 点停牌、Blackjack 赔 2.5 倍、双倍只发一张。
    /// </summary>
    public sealed class BlackjackPage : MonoBehaviour
    {
        private App _app;
        private RectTransform _content;

        // 牌局状态
        private readonly List<Card> _player = new List<Card>();
        private readonly List<Card> _dealer = new List<Card>();
        private bool _dealerHidden;
        private bool _inRound;
        private bool _stood;
        private bool _busted;
        private bool _doubled;
        private string _result = "";
        private int _stake;
        private int _bet;

        // UI 引用
        private RectTransform _dealerHost;
        private RectTransform _playerHost;
        private Text _statusText;
        private Text _resultText;
        private RectTransform _actionHost;
        private Text _betLabel;

        // ==================== 入口 ====================

        public static void Build(RectTransform content)
        {
            var go = new GameObject("BlackjackPage", typeof(RectTransform));
            go.transform.SetParent(content, false);
            UiFactory.Fill((RectTransform)go.transform);

            go.AddComponent<BlackjackPage>().Init(content);
        }

        private void Init(RectTransform content)
        {
            _app = App.Instance;
            _content = content;
            _stake = Mathf.Max(_app.TierMin, 100);
            _bet = _stake;

            var stack = new VStack(content);

            stack.Button("← 返回大厅", () => _app.ShowLobby(), 88f);

            // ---------- 牌桌 ----------
            stack.FixedCard(inner =>
            {
                // 庄家标签
                var dl = UiFactory.Label(inner, "庄 家", Theme.FontSmall, Theme.Muted,
                    TextAnchor.MiddleCenter);
                UiFactory.TopStretch(dl.rectTransform, 40f);

                // 庄家手牌
                _dealerHost = UiFactory.Node(inner, "DealerCards");
                UiFactory.TopStretch(_dealerHost, 200f, 44f);

                // 状态（垂直居中）
                _statusText = UiFactory.Label(inner, "准备开始", Theme.FontHead, Theme.Ink,
                    TextAnchor.MiddleCenter, bold: true);
                var sr = _statusText.rectTransform;
                sr.anchorMin = new Vector2(0f, 0.5f);
                sr.anchorMax = new Vector2(1f, 0.5f);
                sr.pivot = new Vector2(0.5f, 0.5f);
                sr.offsetMin = new Vector2(0f, -32f);
                sr.offsetMax = new Vector2(0f, 32f);

                // 玩家手牌
                _playerHost = UiFactory.Node(inner, "PlayerCards");
                UiFactory.BottomStretch(_playerHost, 200f, 48f);

                // 玩家标签
                var pl = UiFactory.Label(inner, "你", Theme.FontSmall, Theme.Muted,
                    TextAnchor.MiddleCenter);
                UiFactory.BottomStretch(pl.rectTransform, 40f);
            }, 620f);

            // ---------- 结果 ----------
            _resultText = stack.CenterText("", Theme.FontHead, Theme.Accent, bold: true, height: 56f);

            // ---------- 下注 ----------
            _betLabel = stack.Text($"下注（单注 ${_stake:N0}）", Theme.FontHead, Theme.Ink,
                bold: true, minHeight: 52f);

            var stakeButtons = new List<Button>();
            foreach (var amt in new[] { 10, 20, 50, 100, 500, 1000 })
            {
                var v = amt;
                if (v > _app.TierMax) continue;

                var picked = _bet == v;
                var btn = UiFactory.Button(content, $"${v:N0}", () =>
                {
                    _bet = v;
                    _app.ShowBlackjack();
                }, picked ? Theme.AccentSoft : Theme.Panel);

                stakeButtons.Add(btn);
            }
            stack.Grid(stakeButtons, 4, 84f);

            // ---------- 操作区 ----------
            _actionHost = UiFactory.Node(content, "Actions");
            stack.Add(_actionHost, 110f);

            RefreshTable();
            BuildActions();

            stack.Finish();
        }

        // ==================== 操作按钮 ====================

        private void BuildActions()
        {
            UiFactory.Clear(_actionHost);

            var buttons = new List<Button>();

            if (!_inRound)
            {
                buttons.Add(UiFactory.Primary(_actionHost, "开始发牌", Deal));
            }
            else if (_busted || _stood)
            {
                buttons.Add(UiFactory.Primary(_actionHost, "下一局", () =>
                {
                    _inRound = false;
                    _result = "";
                    _app.ShowBlackjack();
                }));
            }
            else
            {
                buttons.Add(UiFactory.Button(_actionHost, "要牌", Hit));
                buttons.Add(UiFactory.Button(_actionHost, "停牌", Stand));

                if (_player.Count == 2 && !_doubled)
                    buttons.Add(UiFactory.Button(_actionHost, "双倍", Double));
            }

            if (buttons.Count == 0) return;

            const float gap = 14f;
            var totalWidth = _actionHost.rect.width;
            if (totalWidth <= 1f) totalWidth = 1000f;

            var count = buttons.Count;
            var btnWidth = Mathf.Min(300f, (totalWidth - gap * (count - 1)) / count);

            for (var i = 0; i < count; i++)
            {
                var rt = buttons[i].GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(btnWidth, 100f);
                rt.anchoredPosition = new Vector2(i * (btnWidth + gap), 0f);
            }
        }

        // ==================== 渲染 ====================

        private void RefreshTable()
        {
            RenderHand(_dealerHost, _dealer, _dealerHidden);
            RenderHand(_playerHost, _player, false);

            if (_inRound)
            {
                var ps = Points(_player);
                var ds = _dealerHidden ? "?" : Points(_dealer).ToString();

                _statusText.text = _busted ? $"爆牌（{ps} 点）"
                    : _stood ? $"已停牌（{ps} 点）"
                    : $"你 {ps} 点   ·   庄家 {ds} 点";
            }
            else
            {
                _statusText.text = "准备开始";
            }

            _resultText.text = _result;
        }

        private void RenderHand(RectTransform host, List<Card> cards, bool hideSecond)
        {
            UiFactory.Clear(host);

            if (cards.Count == 0)
            {
                var empty = UiFactory.Label(host, "—", Theme.FontHead, Theme.Muted, TextAnchor.MiddleCenter);
                UiFactory.Center(empty.rectTransform, 200f, 60f);
                return;
            }

            const float cardW = 128f;
            const float cardH = 180f;
            const float gap = 12f;

            var totalW = cards.Count * cardW + (cards.Count - 1) * gap;
            var startX = -totalW / 2f + cardW / 2f;

            for (var i = 0; i < cards.Count; i++)
            {
                var hidden = hideSecond && i == 1;
                var rt = MakeCard(host, hidden ? null : cards[i], hidden);

                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(cardW, cardH);
                rt.anchoredPosition = new Vector2(startX + i * (cardW + gap), 0f);
            }
        }

        /// <summary>画一张牌（点数 + 花色文字）。</summary>
        private static RectTransform MakeCard(Transform parent, Card card, bool hidden)
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

            if (hidden)
            {
                var q = UiFactory.Label(go.transform, "?", 68, Color.white, TextAnchor.MiddleCenter, bold: true);
                UiFactory.Fill(q.rectTransform);
            }
            else
            {
                var color = card.IsRed ? Theme.CardRed : Theme.CardBlack;

                // 点数（偏上）
                var rank = UiFactory.Label(go.transform, card.RankText, 52, color,
                    TextAnchor.MiddleCenter, bold: true);
                var rr = rank.rectTransform;
                rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 0.64f);
                rr.sizeDelta = new Vector2(100f, 64f);

                // 花色（偏下）
                var suit = UiFactory.Label(go.transform, card.SuitName, 24, color, TextAnchor.MiddleCenter);
                var sr = suit.rectTransform;
                sr.anchorMin = sr.anchorMax = new Vector2(0.5f, 0.30f);
                sr.sizeDelta = new Vector2(100f, 40f);
            }

            return rt;
        }

        // ==================== 规则 ====================

        private static int Points(IReadOnlyList<Card> hand)
        {
            var total = 0;
            var aces = 0;

            foreach (var c in hand)
            {
                if (c.Rank == 14) { total += 11; aces++; }
                else if (c.Rank >= 10) total += 10;
                else total += c.Rank;
            }

            // A 超 21 点时按 1 算
            while (total > 21 && aces > 0) { total -= 10; aces--; }
            return total;
        }

        // ==================== 流程 ====================

        private void Deal()
        {
            if (!_app.TryPay(_bet))
            {
                _app.Msg($"余额不足：当前 ${_app.Save.Balance:N0}", "筹码不足");
                return;
            }

            _player.Clear();
            _dealer.Clear();
            _result = "";
            _stood = false;
            _busted = false;
            _doubled = false;
            _dealerHidden = true;
            _inRound = true;

            var deck = Deck.New(new System.Random());

            _player.Add(deck[deck.Count - 1]); deck.RemoveAt(deck.Count - 1);
            _player.Add(deck[deck.Count - 1]); deck.RemoveAt(deck.Count - 1);
            _dealer.Add(deck[deck.Count - 1]); deck.RemoveAt(deck.Count - 1);
            _dealer.Add(deck[deck.Count - 1]); deck.RemoveAt(deck.Count - 1);

            // 天然 21 点直接停牌
            if (Points(_player) == 21) _stood = true;

            RefreshTable();
            BuildActions();

            if (_stood) DealerPlay();
        }

        private void Hit()
        {
            if (!_inRound || _busted || _stood) return;

            var deck = Deck.New(new System.Random());
            _player.Add(deck[0]);

            var pts = Points(_player);

            if (pts > 21) { _busted = true; DealerPlay(); }
            else if (pts == 21) { _stood = true; DealerPlay(); }

            RefreshTable();
            BuildActions();
        }

        private void Stand()
        {
            if (!_inRound || _busted || _stood) return;

            _stood = true;
            DealerPlay();

            RefreshTable();
            BuildActions();
        }

        private void Double()
        {
            if (!_inRound || _busted || _stood || _player.Count != 2) return;

            if (!_app.TryPay(_bet))
            {
                _app.Msg($"余额不足，无法双倍。当前 ${_app.Save.Balance:N0}", "筹码不足");
                return;
            }

            _bet *= 2;
            _doubled = true;

            var deck = Deck.New(new System.Random());
            _player.Add(deck[0]);

            if (Points(_player) > 21) _busted = true;
            _stood = true;

            DealerPlay();
            RefreshTable();
            BuildActions();
        }

        private void DealerPlay()
        {
            _dealerHidden = false;

            // 庄家 17 点停牌
            while (Points(_dealer) < 17)
            {
                var deck = Deck.New(new System.Random());
                _dealer.Add(deck[0]);
            }

            Settle();
        }

        private void Settle()
        {
            var player = Points(_player);
            var dealer = Points(_dealer);

            var playerBj = _player.Count == 2 && player == 21;
            var dealerBj = _dealer.Count == 2 && dealer == 21;

            string outcome;
            var payout = 0;

            if (_busted || player > 21)
            {
                outcome = $"爆牌 {player} 点，输 ${_bet:N0}";
            }
            else if (playerBj && !dealerBj)
            {
                payout = (int)(_bet * 2.5);
                outcome = $"Blackjack！赢 ${payout - _bet:N0}";
            }
            else if (dealer > 21)
            {
                payout = _bet * 2;
                outcome = $"庄家爆牌 {dealer} 点，赢 ${_bet:N0}";
            }
            else if (player > dealer)
            {
                payout = _bet * 2;
                outcome = $"你 {player} 点 > 庄家 {dealer} 点，赢 ${_bet:N0}";
            }
            else if (player == dealer)
            {
                payout = _bet;
                outcome = $"平局 {player} 点，退回本金";
            }
            else
            {
                outcome = $"你 {player} 点 < 庄家 {dealer} 点，输 ${_bet:N0}";
            }

            if (payout > 0) _app.Win(payout);

            _app.Settle("blackjack", _bet, payout, outcome);

            _result = outcome;
            _inRound = false;
            _bet = _stake;
        }
    }
}
