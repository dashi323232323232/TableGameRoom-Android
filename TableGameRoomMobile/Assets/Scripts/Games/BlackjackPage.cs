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
    /// 21 点 —— 对应桌面版 Blackjack.cs。
    /// 移动端为竖屏布局：庄家在顶部、玩家在底部、中间是底池与操作区。
    /// </summary>
    public sealed class BlackjackPage : MonoBehaviour
    {
        private App _app;
        private RectTransform _root;

        private readonly List<Card> _player = new();
        private readonly List<Card> _dealer = new();
        private bool _dealerHidden;
        private bool _inRound;
        private bool _stood;
        private bool _busted;
        private bool _doubled;
        private string _result = "";
        private int _stake;
        private int _bet;

        private Text _statusText;
        private Text _resultText;
        private RectTransform _dealerRow;
        private RectTransform _playerRow;
        private RectTransform _actions;

        public static void Build(RectTransform root)
        {
            var go = new GameObject("BlackjackPage", typeof(RectTransform));
            go.transform.SetParent(root, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, 0f);

            var page = go.AddComponent<BlackjackPage>();
            page.Init(root);
        }

        private FlowLayout _flow;
        private RectTransform _content;

        private void Init(RectTransform root)
        {
            _app = App.Instance;
            _root = root;
            _stake = Math.Max(_app.TierMin, 100);
            _bet = _stake;

            _content = UiFactory.ScrollView(root, out _);
            _flow = new FlowLayout(_content, padding: 24, spacing: 14);

            // 返回
            var back = UiFactory.Button(_content, "← 返回大厅", () => _app.ShowLobby());
            _flow.AddButton(back, 88);

            // ---------- 牌桌 ----------
            var table = UiFactory.Card(_content, Theme.Table, Theme.TableBorder);
            var tableRect = table.rectTransform;
            _flow.Add(tableRect, 620);

            // 牌桌内部用绝对定位
            var innerGo = new GameObject("Inner", typeof(RectTransform));
            innerGo.transform.SetParent(tableRect, false);
            var inner = (RectTransform)innerGo.transform;
            inner.anchorMin = Vector2.zero;
            inner.anchorMax = Vector2.one;
            inner.offsetMin = new Vector2(20, 20);
            inner.offsetMax = new Vector2(-20, -20);

            // 庄家标签（顶部居中）
            var dealerLabel = UiFactory.Label(inner, "庄 家", 26, Theme.Muted, TextAnchor.MiddleCenter);
            var dlr = dealerLabel.rectTransform;
            dlr.anchorMin = new Vector2(0.5f, 1f);
            dlr.anchorMax = new Vector2(0.5f, 1f);
            dlr.pivot = new Vector2(0.5f, 1f);
            dlr.sizeDelta = new Vector2(300, 40);
            dlr.anchoredPosition = new Vector2(0, 0);

            // 庄家手牌区（顶部）
            var dealerHost = new GameObject("DealerCards", typeof(RectTransform));
            dealerHost.transform.SetParent(inner, false);
            _dealerRow = (RectTransform)dealerHost.transform;
            _dealerRow.anchorMin = new Vector2(0f, 1f);
            _dealerRow.anchorMax = new Vector2(1f, 1f);
            _dealerRow.pivot = new Vector2(0.5f, 1f);
            _dealerRow.offsetMin = new Vector2(0, -240);
            _dealerRow.offsetMax = new Vector2(0, -50);

            // 状态文字（正中）
            _statusText = UiFactory.Label(inner, "准备开始", 32, Theme.Ink, TextAnchor.MiddleCenter, bold: true);
            var srt = _statusText.rectTransform;
            srt.anchorMin = new Vector2(0f, 0.5f);
            srt.anchorMax = new Vector2(1f, 0.5f);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.sizeDelta = new Vector2(0, 60);
            srt.anchoredPosition = Vector2.zero;

            // 玩家手牌区（底部）
            var playerHost = new GameObject("PlayerCards", typeof(RectTransform));
            playerHost.transform.SetParent(inner, false);
            _playerRow = (RectTransform)playerHost.transform;
            _playerRow.anchorMin = new Vector2(0f, 0f);
            _playerRow.anchorMax = new Vector2(1f, 0f);
            _playerRow.pivot = new Vector2(0.5f, 0f);
            _playerRow.offsetMin = new Vector2(0, 40);
            _playerRow.offsetMax = new Vector2(0, 210);

            // 玩家标签（底部居中）
            var playerLabel = UiFactory.Label(inner, "你", 26, Theme.Muted, TextAnchor.MiddleCenter);
            var plr = playerLabel.rectTransform;
            plr.anchorMin = new Vector2(0.5f, 0f);
            plr.anchorMax = new Vector2(0.5f, 0f);
            plr.pivot = new Vector2(0.5f, 0f);
            plr.sizeDelta = new Vector2(300, 36);
            plr.anchoredPosition = new Vector2(0, 0);

            // ---------- 结果 ----------
            _resultText = UiFactory.Label(_content, "", 34, Theme.Accent, TextAnchor.MiddleCenter, bold: true);
            _flow.AddText(_resultText, 56);

            // ---------- 下注金额 ----------
            var betTitle = UiFactory.Label(_content, $"下注（单注 ${_stake:N0}）", 32, Theme.Ink,
                TextAnchor.UpperLeft, bold: true);
            _flow.AddText(betTitle, 50);

            var stakeButtons = new List<Button>();
            var amounts = new[] { 10, 20, 50, 100, 500, 1000 };
            foreach (var amt in amounts)
            {
                var v = amt;
                if (v > _app.TierMax) continue;
                var picked = _bet == v;
                var btn = UiFactory.Button(_content, $"${v:N0}", () =>
                {
                    _bet = v;
                    RefreshAll();
                }, picked ? Theme.AccentSoft : Theme.Panel);
                stakeButtons.Add(btn);
            }
            _flow.AddGrid(stakeButtons, 4, 82);

            // ---------- 操作（容器，按钮由 BuildActionButtons 填充）----------
            var actionHost = new GameObject("Actions", typeof(RectTransform));
            actionHost.transform.SetParent(_content, false);
            _actions = (RectTransform)actionHost.transform;
            _flow.Add(_actions, 110);

            RefreshTable();
            BuildActionButtons();

            _flow.Finish();
        }

        /// <summary>设置锚点 + 偏移（left/right/top/bottom 用负值表示从右/下边缘算）。</summary>
        private static void SetRect(RectTransform rt, float axMin, float ayMin, float axMax, float ayMax,
            float left, float bottom, float right, float top, float height)
        {
            rt.anchorMin = new Vector2(axMin, ayMin);
            rt.anchorMax = new Vector2(axMax, ayMax);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(right, top);
            if (height > 0) rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);
        }

        private void RefreshAll()
        {
            // 简化：重建整个页面
            var root = _app.BeginPage("blackjack");
            Build(root);
        }

        private void BuildActionButtons()
        {
            UiFactory.Clear(_actions);

            var buttons = new List<Button>();

            if (!_inRound)
            {
                var deal = UiFactory.PrimaryButton(_actions, "开始发牌", Deal);
                buttons.Add(deal);
            }
            else if (_busted || _stood)
            {
                var next = UiFactory.PrimaryButton(_actions, "下一局", () =>
                {
                    _inRound = false;
                    _result = "";
                    RefreshAll();
                });
                buttons.Add(next);
            }
            else
            {
                var hit = UiFactory.Button(_actions, "要牌", Hit);
                buttons.Add(hit);

                var stand = UiFactory.Button(_actions, "停牌", Stand);
                buttons.Add(stand);

                if (_player.Count == 2 && !_doubled)
                {
                    var dbl = UiFactory.Button(_actions, "双倍", Double);
                    buttons.Add(dbl);
                }
            }

            // 绝对定位：从左侧开始横向排列
            const float gap = 16f;
            var count = buttons.Count;
            if (count == 0) return;

            var totalWidth = _actions.rect.width;
            var btnWidth = Mathf.Min(320f, (totalWidth - gap * (count - 1)) / count);

            for (var i = 0; i < count; i++)
            {
                var rt = buttons[i].GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 0.5f);
                rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(btnWidth, 100f);
                rt.anchoredPosition = new Vector2(i * (btnWidth + gap), 0f);
            }
        }

        // ---------- 牌桌渲染 ----------

        private void RefreshTable()
        {
            RenderHand(_dealerRow, _dealer, hideSecond: _dealerHidden);
            RenderHand(_playerRow, _player, hideSecond: false);

            if (_inRound)
            {
                var ps = Points(_player);
                var ds = _dealerHidden ? "?" : Points(_dealer).ToString();
                _statusText.text = _busted ? $"爆牌（{ps} 点）"
                    : _stood ? $"已停牌（{ps} 点）"
                    : $"你 {ps} 点  ·  庄家 {ds} 点";
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
                var empty = UiFactory.Label(host, "—", 36, Theme.Muted, TextAnchor.MiddleCenter);
                var er = empty.rectTransform;
                er.anchorMin = er.anchorMax = new Vector2(0.5f, 0.5f);
                er.sizeDelta = new Vector2(200, 60);
                return;
            }

            // 手工横向居中排布
            const float cardW = 130f;
            const float cardH = 185f;
            const float gap = 12f;

            var totalW = cards.Count * cardW + (cards.Count - 1) * gap;
            var startX = -totalW / 2f + cardW / 2f;

            for (var i = 0; i < cards.Count; i++)
            {
                var hidden = hideSecond && i == 1;
                var view = AddCardView(host, hidden ? null : cards[i], hidden);
                var rt = view;

                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(cardW, cardH);
                rt.anchoredPosition = new Vector2(startX + i * (cardW + gap), 0f);
            }
        }

        private RectTransform AddCardView(RectTransform host, Card card, bool hidden)
        {
            var go = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(host, false);

            var img = go.GetComponent<Image>();
            img.sprite = UiFactory.RoundedSprite;
            img.type = Image.Type.Sliced;
            img.color = hidden ? Theme.Accent : Theme.CardFace;
            img.raycastTarget = false;

            var rt = (RectTransform)go.transform;

            if (card != null && !hidden)
            {
                var c = card;

                // 点数（偏上）
                var rank = UiFactory.Label(go.transform, c.RankText, 52,
                    c.IsRed ? Theme.CardRed : Theme.CardBlack, TextAnchor.MiddleCenter, bold: true);
                var rr = rank.rectTransform;
                rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 0.62f);
                rr.sizeDelta = new Vector2(90, 70);

                // 花色（偏下）
                var suit = UiFactory.Label(go.transform, c.SuitName, 26,
                    c.IsRed ? Theme.CardRed : Theme.CardBlack, TextAnchor.MiddleCenter);
                var sr = suit.rectTransform;
                sr.anchorMin = sr.anchorMax = new Vector2(0.5f, 0.32f);
                sr.sizeDelta = new Vector2(90, 80);
            }
            else if (hidden)
            {
                var text = UiFactory.Label(go.transform, "?", 72, Color.white, TextAnchor.MiddleCenter, bold: true);
                UiFactory.Stretch(text.rectTransform);
            }

            return rt;
        }

        // ---------- 规则 ----------

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
            while (total > 21 && aces > 0) { total -= 10; aces--; }
            return total;
        }

        // ---------- 流程 ----------

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
            _player.Add(deck[^1]); deck.RemoveAt(deck.Count - 1);
            _player.Add(deck[^1]); deck.RemoveAt(deck.Count - 1);
            _dealer.Add(deck[^1]); deck.RemoveAt(deck.Count - 1);
            _dealer.Add(deck[^1]); deck.RemoveAt(deck.Count - 1);

            // 天然 21 点
            if (Points(_player) == 21) { _stood = true; }

            RefreshTable();
            BuildActionButtons();

            if (_stood) DealerPlay();
        }

        private void Hit()
        {
            if (!_inRound || _busted || _stood) return;

            // 简化：每次要牌重新洗一副，取一张（概率上等价于连续发牌）
            var deck = Deck.New(new System.Random());
            _player.Add(deck[0]);

            var pts = Points(_player);
            if (pts > 21) { _busted = true; DealerPlay(); }
            else if (pts == 21) { _stood = true; DealerPlay(); }

            RefreshTable();
            BuildActionButtons();
        }

        private void Stand()
        {
            if (!_inRound || _busted || _stood) return;
            _stood = true;
            DealerPlay();
            RefreshTable();
            BuildActionButtons();
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
            BuildActionButtons();
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

            var playerBJ = _player.Count == 2 && player == 21;
            var dealerBJ = _dealer.Count == 2 && dealer == 21;

            string outcome;
            var payout = 0;

            if (_busted || player > 21)
            {
                outcome = $"爆牌 {player} 点，输 ${_bet:N0}";
            }
            else if (playerBJ && !dealerBJ)
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
