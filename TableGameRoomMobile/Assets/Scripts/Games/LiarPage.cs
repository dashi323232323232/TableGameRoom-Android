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
    /// 骗子扑克 —— 对应桌面版 Liar.cs。
    /// 玩法：轮流盖牌并声明点数，下家可以质疑；质疑成功则被质疑者收牌，失败则质疑者收牌。
    /// 先出完手牌的人赢。
    /// </summary>
    public sealed class LiarPage : MonoBehaviour
    {
        private sealed class AiHand
        {
            public string Name = "";
            public List<Card> Cards = new();
            public string Emoji = "😐";
        }

        private App _app;

        private readonly List<Card> _player = new();
        private readonly List<AiHand> _ai = new();
        private readonly List<Card> _pile = new();
        private int _declareRank = 7;
        private bool _myTurn;
        private bool _inRound;
        private bool _finished;
        private string _log = "";
        private int _stake;

        private Text _statusText;
        private Text _pileText;
        private Text _logText;
        private RectTransform _playerRow;
        private RectTransform _aiHost;
        private RectTransform _actions;

        public static void Build(RectTransform root)
        {
            var go = new GameObject("LiarPage");
            go.transform.SetParent(root, false);
            UiFactory.Stretch((RectTransform)go.transform);

            var page = go.AddComponent<LiarPage>();
            page.Init(root);
        }

        private void Init(RectTransform root)
        {
            _app = App.Instance;
            _stake = Math.Max(_app.TierMin, 100);

            var content = UiFactory.ScrollView(root, out var sr, 18);
            UiFactory.Stretch((RectTransform)sr.transform);

            var back = UiFactory.Button(content, "← 返回大厅", () => _app.ShowLobby());
            UiFactory.Size(back.gameObject, 300, 92);

            var table = UiFactory.Card(content, Theme.Table, Theme.TableBorder);
            var layout = UiFactory.VerticalLayout(table.transform, 12,
                new RectOffset(20, 20, 24, 24), TextAnchor.MiddleCenter);
            UiFactory.Stretch((RectTransform)layout.transform);
            var tf = table.gameObject.AddComponent<ContentSizeFitter>();
            tf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _statusText = UiFactory.Label(layout.transform, "准备开始", 36, Theme.Ink, TextAnchor.MiddleCenter, bold: true);
            UiFactory.Size(_statusText.gameObject, -1, 56);

            _pileText = UiFactory.Label(layout.transform, "牌堆 0 张", 30, Theme.Muted, TextAnchor.MiddleCenter);
            UiFactory.Size(_pileText.gameObject, -1, 46);

            _aiHost = UiFactory.VerticalLayout(layout.transform, 10, null, TextAnchor.MiddleCenter).rectTransform;

            var myLabel = UiFactory.Label(layout.transform, "我的手牌", 30, Theme.Muted, TextAnchor.MiddleCenter);
            UiFactory.Size(myLabel.gameObject, -1, 44);

            _playerRow = UiFactory.HorizontalLayout(layout.transform, 8, null, TextAnchor.MiddleCenter).rectTransform;
            UiFactory.Size(_playerRow.gameObject, -1, 180);

            _logText = UiFactory.Label(content, "", 28, Theme.Muted);
            _logText.lineSpacing = 1.3f;

            _actions = UiFactory.VerticalLayout(content, 10, null, TextAnchor.MiddleCenter).rectTransform;

            RefreshTable();
            BuildActions();
        }

        private void BuildActions()
        {
            UiFactory.Clear(_actions);

            if (!_inRound)
            {
                var start = UiFactory.PrimaryButton(_actions, _finished ? "再来一局" : "开始牌局", StartRound);
                UiFactory.Size(start.gameObject, 380, 110);
                return;
            }

            if (!_myTurn)
            {
                var wait = UiFactory.Label(_actions, "等待电脑出牌…", 30, Theme.Muted, TextAnchor.MiddleCenter);
                UiFactory.Size(wait.gameObject, -1, 90);
                return;
            }

            // 我的回合：选择声明点数 + 出牌
            var rankRow = UiFactory.HorizontalLayout(_actions, 8, null, TextAnchor.MiddleCenter);
            UiFactory.Size(rankRow.gameObject, -1, 100);

            foreach (var rank in new[] { 7, 8, 9, 10, 11, 12, 13, 14 })
            {
                var r = rank;
                var picked = _declareRank == r;
                var label = r switch { 11 => "J", 12 => "Q", 13 => "K", 14 => "A", _ => r.ToString() };
                var btn = UiFactory.Button(rankRow.transform, label, () =>
                {
                    _declareRank = r;
                    BuildActions();
                }, picked ? Theme.AccentSoft : Theme.Panel);
                UiFactory.Size(btn.gameObject, 110, 92);
            }

            var playRow = UiFactory.HorizontalLayout(_actions, 12, null, TextAnchor.MiddleCenter);
            UiFactory.Size(playRow.gameObject, -1, 110);

            var play = UiFactory.PrimaryButton(playRow.transform, "出 1 张", () => Play(1));
            UiFactory.Size(play.gameObject, 280, 104);

            var play2 = UiFactory.Button(playRow.transform, "出 2 张", () => Play(2));
            UiFactory.Size(play2.gameObject, 280, 104);

            var doubt = UiFactory.Button(playRow.transform, "质 疑", Doubt);
            UiFactory.Size(doubt.gameObject, 240, 104);
        }

        private void StartRound()
        {
            if (!_app.TryPay(_stake))
            {
                _app.Msg($"余额不足：当前 ${_app.Save.Balance:N0}", "筹码不足");
                return;
            }

            _player.Clear();
            _ai.Clear();
            _pile.Clear();
            _finished = false;
            _inRound = true;
            _myTurn = true;
            _log = "";
            _declareRank = 7;

            var rng = new System.Random();
            var deck = Deck.New(rng);

            for (var i = 0; i < 5; i++)
            {
                _player.Add(deck[^1]); deck.RemoveAt(deck.Count - 1);
            }

            _ai.Add(new AiHand { Name = "电脑", Emoji = "🤖" });
            for (var i = 0; i < 5; i++)
            {
                _ai[0].Cards.Add(deck[^1]); deck.RemoveAt(deck.Count - 1);
            }

            _log = "牌局开始。你先出牌。";

            RefreshTable();
            BuildActions();
        }

        private void Play(int count)
        {
            if (!_myTurn || !_inRound) return;
            if (_player.Count < count)
            {
                _app.Msg("手牌不够", "提示");
                return;
            }

            for (var i = 0; i < count; i++)
            {
                _pile.Add(_player[^1]);
                _player.RemoveAt(_player.Count - 1);
            }

            var rankLabel = _declareRank switch
            {
                11 => "J", 12 => "Q", 13 => "K", 14 => "A", _ => _declareRank.ToString()
            };

            _log = $"你出了 {count} 张，声明是 {rankLabel}。";
            _myTurn = false;

            RefreshTable();
            BuildActions();

            if (_player.Count == 0)
            {
                WinRound("你先出完了手牌");
                return;
            }

            // 电脑回合（延迟执行，用协程）
            StartCoroutine(AiTurn());
        }

        private System.Collections.IEnumerator AiTurn()
        {
            yield return new WaitForSeconds(0.9f);

            if (!_inRound) yield break;

            var ai = _ai[0];
            if (ai.Cards.Count == 0)
            {
                WinRound("电脑先出完了手牌", playerWon: false);
                yield break;
            }

            // 电脑决定：有真牌就出真牌，没有就诈唬
            var hasReal = ai.Cards.Any(c => c.Rank == _declareRank);
            var doubt = !hasReal && UnityEngine.Random.value < 0.55f;

            if (doubt)
            {
                // 质疑我
                var myLast = _pile.Count > 0 ? _pile[^1] : (Card?)null;
                var honest = myLast.HasValue && myLast.Value.Rank == _declareRank;

                if (honest)
                {
                    // 我诚实 → 质疑失败，电脑收牌
                    foreach (var c in _pile) ai.Cards.Add(c);
                    _log = $"电脑质疑你。你的确是 {_declareRank} 点，质疑失败，电脑收走 {_pile.Count} 张牌。";
                }
                else
                {
                    // 我诈唬 → 质疑成功，我收牌
                    foreach (var c in _pile) _player.Add(c);
                    _log = $"电脑质疑你。你诈唬被识破，你收走 {_pile.Count} 张牌。";
                }
                _pile.Clear();
            }
            else
            {
                // 电脑出牌
                var outCards = ai.Cards.Where(c => c.Rank == _declareRank).Take(2).ToList();
                if (outCards.Count == 0)
                {
                    outCards.Add(ai.Cards[^1]);
                }

                foreach (var c in outCards)
                {
                    _pile.Add(c);
                    ai.Cards.Remove(c);
                }

                var rankLabel = _declareRank switch
                {
                    11 => "J", 12 => "Q", 13 => "K", 14 => "A", _ => _declareRank.ToString()
                };
                _log = $"电脑出了 {outCards.Count} 张，声明是 {rankLabel}。";
            }

            _myTurn = true;
            RefreshTable();
            BuildActions();

            if (ai.Cards.Count == 0)
            {
                WinRound("电脑先出完了手牌", playerWon: false);
            }
        }

        private void Doubt()
        {
            if (!_myTurn || !_inRound || _pile.Count == 0)
            {
                _app.Msg("现在没有可质疑的牌", "提示");
                return;
            }

            var ai = _ai[0];
            var last = _pile[^1];
            var honest = last.Rank == _declareRank;

            if (honest)
            {
                // 质疑失败，我收牌
                foreach (var c in _pile) _player.Add(c);
                _log = $"你质疑电脑。它出的是 {last.Rank} 点，确实诚实。质疑失败，你收走 {_pile.Count} 张牌。";
            }
            else
            {
                // 质疑成功，电脑收牌
                foreach (var c in _pile) ai.Cards.Add(c);
                _log = $"你质疑电脑。它出的是 {last.Rank} 点，却在声明 {_declareRank} 点。质疑成功！电脑收走 {_pile.Count} 张牌。";
            }
            _pile.Clear();

            RefreshTable();
            BuildActions();
        }

        private void WinRound(string reason, bool playerWon = true)
        {
            _inRound = false;
            _finished = true;

            if (playerWon)
            {
                var gained = _stake * 3;
                _app.Win(gained);
                _log = $"{reason}，你赢得 ${gained:N0}";
            }
            else
            {
                _log = $"{reason}，你输了 ${_stake:N0}";
            }

            RefreshTable();
            BuildActions();
        }

        private void RefreshTable()
        {
            _statusText.text = _finished ? "本局结束"
                : !_inRound ? "准备开始"
                : _myTurn ? "轮到你出牌" : "等待电脑…";

            _pileText.text = $"牌堆 {_pile.Count} 张";

            UiFactory.Clear(_aiHost);
            foreach (var ai in _ai)
            {
                var line = UiFactory.Label(_aiHost, $"{ai.Emoji} {ai.Name}：{ai.Cards.Count} 张手牌",
                    30, Theme.Ink, TextAnchor.MiddleCenter);
                UiFactory.Size(line.gameObject, -1, 44);
            }

            UiFactory.Clear(_playerRow);
            if (_player.Count == 0 && !_inRound)
            {
                var hint = UiFactory.Label(_playerRow, "等待发牌", 30, Theme.Muted, TextAnchor.MiddleCenter);
                UiFactory.Size(hint.gameObject, 300, 60);
            }
            else
            {
                foreach (var c in _player)
                {
                    AddCardView(_playerRow, c);
                }
            }

            _logText.text = _log;
        }

        private void AddCardView(RectTransform host, Card card)
        {
            var go = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(host, false);

            var img = go.GetComponent<Image>();
            img.sprite = UiFactory.RoundedSprite;
            img.type = Image.Type.Sliced;
            img.color = Theme.CardFace;
            img.raycastTarget = false;

            UiFactory.Size(go, 96, 136);
            ((RectTransform)go.transform).sizeDelta = new Vector2(96, 136);

            var text = UiFactory.Label(go.transform, $"{card.RankText}\n{card.Suit}", 36,
                card.IsRed ? Theme.CardRed : Theme.CardBlack, TextAnchor.MiddleCenter, bold: true);
            UiFactory.Stretch(text.rectTransform);
        }
    }
}
