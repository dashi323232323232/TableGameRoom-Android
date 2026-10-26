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
            var go = new GameObject("BlackjackPage");
            go.transform.SetParent(root, false);
            UiFactory.Stretch((RectTransform)go.transform);

            var page = go.AddComponent<BlackjackPage>();
            page.Init(root);
        }

        private void Init(RectTransform root)
        {
            _app = App.Instance;
            _root = root;
            _stake = Math.Max(_app.TierMin, 100);
            _bet = _stake;

            var content = UiFactory.ScrollView(root, out var sr, 20);
            UiFactory.Stretch((RectTransform)sr.transform);

            var back = UiFactory.Button(content, "← 返回大厅", () => _app.ShowLobby());
            UiFactory.Size(back.gameObject, 300, 92);

            // ---------- 牌桌 ----------
            var table = UiFactory.Card(content, Theme.Table, Theme.TableBorder);
            var tableLayout = UiFactory.VerticalLayout(table.transform, 14,
                new RectOffset(24, 24, 28, 28), TextAnchor.MiddleCenter);
            UiFactory.Stretch((RectTransform)tableLayout.transform);
            var tf = table.gameObject.AddComponent<ContentSizeFitter>();
            tf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // 庄家
            var dealerLabel = UiFactory.Label(tableLayout.transform, "庄 家", 30, Theme.Muted, TextAnchor.MiddleCenter);
            UiFactory.Size(dealerLabel.gameObject, -1, 44);

            _dealerRow = UiFactory.HorizontalLayout(tableLayout.transform, 10, null, TextAnchor.MiddleCenter).rectTransform;
            UiFactory.Size(_dealerRow.gameObject, -1, 200);

            // 状态
            _statusText = UiFactory.Label(tableLayout.transform, "准备开始", 36, Theme.Ink, TextAnchor.MiddleCenter, bold: true);
            UiFactory.Size(_statusText.gameObject, -1, 56);

            // 玩家
            _playerRow = UiFactory.HorizontalLayout(tableLayout.transform, 10, null, TextAnchor.MiddleCenter).rectTransform;
            UiFactory.Size(_playerRow.gameObject, -1, 240);

            var playerLabel = UiFactory.Label(tableLayout.transform, "你", 30, Theme.Muted, TextAnchor.MiddleCenter);
            UiFactory.Size(playerLabel.gameObject, -1, 44);

            // ---------- 结果 ----------
            _resultText = UiFactory.Label(content, "", 36, Theme.Accent, TextAnchor.MiddleCenter, bold: true);
            UiFactory.Size(_resultText.gameObject, -1, 60);

            // ---------- 下注 ----------
            var betTitle = UiFactory.Label(content, $"下注（单注 ${_stake:N0}）", 34, Theme.Ink, TextAnchor.UpperLeft, bold: true);
            UiFactory.Size(betTitle.gameObject, -1, 54);

            BuildBetRow(content);

            // ---------- 操作 ----------
            _actions = UiFactory.HorizontalLayout(content, 12, null, TextAnchor.MiddleCenter).rectTransform;
            UiFactory.Size(_actions.gameObject, -1, 116);

            RefreshTable();
            BuildActionButtons();
        }

        private void BuildBetRow(RectTransform parent)
        {
            var row = UiFactory.HorizontalLayout(parent, 10, null, TextAnchor.MiddleLeft);
            UiFactory.Size(row.gameObject, -1, 96);

            var amounts = new[] { 10, 20, 50, 100, 500, 1000 };
            foreach (var amt in amounts)
            {
                var v = amt;
                if (v > _app.TierMax) continue;

                var picked = _bet == v;
                var btn = UiFactory.Button(row.transform, $"${v:N0}", () =>
                {
                    _bet = v;
                    RefreshAll();
                }, picked ? Theme.AccentSoft : Theme.Panel);
                UiFactory.Size(btn.gameObject, 150, 88);
            }
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

            if (!_inRound)
            {
                var deal = UiFactory.PrimaryButton(_actions, "开始发牌", Deal);
                UiFactory.Size(deal.gameObject, 360, 110);
                return;
            }

            if (_busted || _stood)
            {
                var next = UiFactory.PrimaryButton(_actions, "下一局", () =>
                {
                    _inRound = false;
                    _result = "";
                    RefreshAll();
                });
                UiFactory.Size(next.gameObject, 360, 110);
                return;
            }

            var hit = UiFactory.Button(_actions, "要牌", Hit);
            UiFactory.Size(hit.gameObject, 240, 110);

            var stand = UiFactory.Button(_actions, "停牌", Stand);
            UiFactory.Size(stand.gameObject, 240, 110);

            if (_player.Count == 2 && !_doubled)
            {
                var dbl = UiFactory.Button(_actions, "双倍", Double);
                UiFactory.Size(dbl.gameObject, 240, 110);
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
                var empty = UiFactory.Label(host, "—", 40, Theme.Muted, TextAnchor.MiddleCenter);
                UiFactory.Size(empty.gameObject, 200, 60);
                return;
            }

            for (var i = 0; i < cards.Count; i++)
            {
                var hidden = hideSecond && i == 1;
                AddCardView(host, hidden ? null : cards[i], hidden);
            }
        }

        private void AddCardView(RectTransform host, Card? card, bool hidden)
        {
            var go = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(host, false);

            var img = go.GetComponent<Image>();
            img.sprite = UiFactory.RoundedSprite;
            img.type = Image.Type.Sliced;
            img.color = hidden ? Theme.Accent : Theme.CardFace;
            img.raycastTarget = false;

            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(150, 210);
            UiFactory.Size(go, 150, 210);

            if (card.HasValue && !hidden)
            {
                var c = card.Value;
                var text = UiFactory.Label(go.transform, $"{c.RankText}\n{c.Suit}", 58,
                    c.IsRed ? Theme.CardRed : Theme.CardBlack, TextAnchor.MiddleCenter, bold: true);
                UiFactory.Stretch(text.rectTransform);
            }
            else if (hidden)
            {
                var text = UiFactory.Label(go.transform, "?", 72, Color.white, TextAnchor.MiddleCenter, bold: true);
                UiFactory.Stretch(text.rectTransform);
            }
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
