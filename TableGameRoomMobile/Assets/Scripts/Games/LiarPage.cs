using System.Collections;
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
    ///
    /// 玩法：轮流盖牌并声明点数，对方可以质疑。
    /// 质疑成功（对方诈唬）→ 对方收牌；质疑失败 → 自己收牌。
    /// 先出完手牌的人赢。
    /// </summary>
    public sealed class LiarPage : MonoBehaviour
    {
        private const int HandSize = 5;

        private App _app;

        private readonly List<Card> _player = new List<Card>();
        private readonly List<Card> _aiCards = new List<Card>();
        private readonly List<Card> _pile = new List<Card>();

        private int _declareRank = 7;
        private bool _myTurn;
        private bool _inRound;
        private bool _finished;
        private string _log = "";
        private int _stake;

        private Text _statusText;
        private Text _pileText;
        private Text _logText;
        private Text _resultText;
        private RectTransform _aiHost;
        private RectTransform _playerHost;
        private RectTransform _actionHost;

        // ==================== 入口 ====================

        public static void Build(RectTransform content)
        {
            var go = new GameObject("LiarPage", typeof(RectTransform));
            go.transform.SetParent(content, false);
            UiFactory.Fill((RectTransform)go.transform);

            go.AddComponent<LiarPage>().Init(content);
        }

        private void Init(RectTransform content)
        {
            _app = App.Instance;
            _stake = Mathf.Max(_app.TierMin, 100);

            var stack = new VStack(content);

            stack.Button("← 返回大厅", () => _app.ShowLobby(), 88f);

            // ---------- 牌桌 ----------
            stack.FixedCard(inner =>
            {
                _statusText = UiFactory.Label(inner, "准备开始", Theme.FontHead, Theme.Ink,
                    TextAnchor.MiddleCenter, bold: true);
                UiFactory.TopStretch(_statusText.rectTransform, 52f);

                _pileText = UiFactory.Label(inner, "牌堆 0 张", Theme.FontBody, Theme.Muted,
                    TextAnchor.MiddleCenter);
                UiFactory.TopStretch(_pileText.rectTransform, 40f, 56f);

                _aiHost = UiFactory.Node(inner, "AiHands");
                var ar = _aiHost;
                ar.anchorMin = new Vector2(0f, 0.5f);
                ar.anchorMax = new Vector2(1f, 0.5f);
                ar.pivot = new Vector2(0.5f, 0.5f);
                ar.offsetMin = new Vector2(0f, -40f);
                ar.offsetMax = new Vector2(0f, 60f);

                var ml = UiFactory.Label(inner, "我的手牌", Theme.FontSmall, Theme.Muted,
                    TextAnchor.MiddleCenter);
                UiFactory.BottomStretch(ml.rectTransform, 36f, 176f);

                _playerHost = UiFactory.Node(inner, "PlayerCards");
                UiFactory.BottomStretch(_playerHost, 160f, 8f);
            }, 600f);

            // ---------- 结果 ----------
            _resultText = stack.CenterText("", Theme.FontHead, Theme.Accent, bold: true, height: 54f);

            // ---------- 日志 ----------
            _logText = stack.Text("", Theme.FontSmall, Theme.Muted);

            // ---------- 操作 ----------
            _actionHost = UiFactory.Node(content, "Actions");
            stack.Add(_actionHost, 220f);

            RefreshTable();
            BuildActions();

            stack.Finish();
        }

        // ==================== 操作 ====================

        private void BuildActions()
        {
            UiFactory.Clear(_actionHost);

            if (!_inRound)
            {
                var start = UiFactory.Primary(_actionHost, _finished ? "再来一局" : "开始牌局", StartRound);
                var sr = start.GetComponent<RectTransform>();
                sr.anchorMin = sr.anchorMax = new Vector2(0f, 0.5f);
                sr.pivot = new Vector2(0f, 0.5f);
                sr.sizeDelta = new Vector2(360f, 100f);
                sr.anchoredPosition = Vector2.zero;
                return;
            }

            if (!_myTurn)
            {
                var wait = UiFactory.Label(_actionHost, "等待电脑出牌…", Theme.FontBody,
                    Theme.Muted, TextAnchor.MiddleCenter);
                UiFactory.Fill(wait.rectTransform);
                return;
            }

            var totalWidth = _actionHost.rect.width;
            if (totalWidth <= 1f) totalWidth = 1000f;

            // ---------- 第一行：声明点数 ----------
            var ranks = new[] { 7, 8, 9, 10, 11, 12, 13, 14 };
            const float gap = 8f;
            const float rowH = 88f;

            var cellWidth = (totalWidth - gap * (ranks.Length - 1)) / ranks.Length;

            for (var i = 0; i < ranks.Length; i++)
            {
                var r = ranks[i];
                var picked = _declareRank == r;

                var label = r == 11 ? "J" : r == 12 ? "Q" : r == 13 ? "K" : r == 14 ? "A" : r.ToString();

                var btn = UiFactory.Button(_actionHost, label, () =>
                {
                    _declareRank = r;
                    BuildActions();
                }, picked ? Theme.AccentSoft : Theme.Panel);

                var rt = btn.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.sizeDelta = new Vector2(cellWidth, rowH);
                rt.anchoredPosition = new Vector2(i * (cellWidth + gap), 0f);

                if (picked)
                {
                    var txt = btn.GetComponentInChildren<Text>();
                    if (txt != null) txt.fontStyle = FontStyle.Bold;
                }
            }

            // ---------- 第二行：出牌 / 质疑 ----------
            var secondRow = new List<Button>
            {
                UiFactory.Primary(_actionHost, "出 1 张", () => Play(1)),
                UiFactory.Button(_actionHost, "出 2 张", () => Play(2)),
                UiFactory.Button(_actionHost, "质 疑", Doubt)
            };

            var secondWidth = (totalWidth - gap * 2) / 3f;
            for (var i = 0; i < secondRow.Count; i++)
            {
                var rt = secondRow[i].GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
                rt.pivot = new Vector2(0f, 0f);
                rt.sizeDelta = new Vector2(secondWidth, 100f);
                rt.anchoredPosition = new Vector2(i * (secondWidth + gap), 0f);
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

            _player.Clear();
            _aiCards.Clear();
            _pile.Clear();

            _finished = false;
            _inRound = true;
            _myTurn = true;
            _log = "";
            _declareRank = 7;

            var deck = Deck.New(new System.Random());

            for (var i = 0; i < HandSize; i++)
            {
                _player.Add(deck[deck.Count - 1]);
                deck.RemoveAt(deck.Count - 1);
            }

            for (var i = 0; i < HandSize; i++)
            {
                _aiCards.Add(deck[deck.Count - 1]);
                deck.RemoveAt(deck.Count - 1);
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
                _pile.Add(_player[_player.Count - 1]);
                _player.RemoveAt(_player.Count - 1);
            }

            _log = $"你出了 {count} 张，声明是 {RankLabel(_declareRank)}。";
            _myTurn = false;

            RefreshTable();
            BuildActions();

            if (_player.Count == 0)
            {
                EndRound("你先出完了手牌", true);
                return;
            }

            StartCoroutine(AiTurn());
        }

        private IEnumerator AiTurn()
        {
            yield return new WaitForSeconds(0.8f);

            if (!_inRound) yield break;

            if (_aiCards.Count == 0)
            {
                EndRound("电脑先出完了手牌", false);
                yield break;
            }

            // 电脑决策：有真牌出真牌，没有就大概率质疑
            var hasReal = _aiCards.Any(c => c.Rank == _declareRank);
            var shouldDoubt = !hasReal && UnityEngine.Random.value < 0.55f;

            if (shouldDoubt)
            {
                var last = _pile.Count > 0 ? _pile[_pile.Count - 1] : null;
                var honest = last != null && last.Rank == _declareRank;

                if (honest)
                {
                    // 我诚实 → 质疑失败，电脑收牌
                    foreach (var c in _pile) _aiCards.Add(c);
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
                var outCards = _aiCards.Where(c => c.Rank == _declareRank).Take(2).ToList();
                if (outCards.Count == 0) outCards.Add(_aiCards[_aiCards.Count - 1]);

                foreach (var c in outCards)
                {
                    _pile.Add(c);
                    _aiCards.Remove(c);
                }

                _log = $"电脑出了 {outCards.Count} 张，声明是 {RankLabel(_declareRank)}。";
            }

            _myTurn = true;
            RefreshTable();
            BuildActions();

            if (_aiCards.Count == 0)
                EndRound("电脑先出完了手牌", false);
        }

        private void Doubt()
        {
            if (!_myTurn || !_inRound)
            {
                _app.Msg("现在没有可质疑的牌", "提示");
                return;
            }

            if (_pile.Count == 0)
            {
                _app.Msg("牌堆是空的，没有可质疑的牌", "提示");
                return;
            }

            var last = _pile[_pile.Count - 1];
            var honest = last.Rank == _declareRank;

            if (honest)
            {
                foreach (var c in _pile) _player.Add(c);
                _log = $"你质疑电脑。它出的是 {last.Rank} 点，确实诚实。质疑失败，你收走 {_pile.Count} 张牌。";
            }
            else
            {
                foreach (var c in _pile) _aiCards.Add(c);
                _log = $"你质疑电脑。它出的是 {last.Rank} 点，却在声明 {_declareRank} 点。质疑成功！电脑收走 {_pile.Count} 张牌。";
            }

            _pile.Clear();

            RefreshTable();
            BuildActions();
        }

        private void EndRound(string reason, bool playerWon)
        {
            _inRound = false;
            _finished = true;

            if (playerWon)
            {
                var gained = _stake * 3;
                _app.Win(gained);
                _log = $"{reason}，你赢得 ${gained:N0}";
                _resultText.text = $"你赢了 ${gained - _stake:N0}";
                _app.Settle("liar", _stake, gained, reason);
            }
            else
            {
                _log = $"{reason}，你输了 ${_stake:N0}";
                _resultText.text = $"你输了 ${_stake:N0}";
                _app.Settle("liar", _stake, 0, reason);
            }

            RefreshTable();
            BuildActions();
        }

        private static string RankLabel(int rank) => rank switch
        {
            11 => "J",
            12 => "Q",
            13 => "K",
            14 => "A",
            _ => rank.ToString()
        };

        // ==================== 渲染 ====================

        private void RefreshTable()
        {
            _statusText.text = _finished ? "本局结束"
                : !_inRound ? "准备开始"
                : _myTurn ? "轮到你出牌" : "等待电脑…";

            _pileText.text = $"牌堆 {_pile.Count} 张";

            UiFactory.Clear(_aiHost);
            var aiLine = UiFactory.Label(_aiHost, $"🤖 电脑：{_aiCards.Count} 张手牌",
                Theme.FontHead, Theme.Ink, TextAnchor.MiddleCenter);
            UiFactory.Center(aiLine.rectTransform, 600f, 52f);

            UiFactory.Clear(_playerHost);
            if (_player.Count == 0 && !_inRound)
            {
                var hint = UiFactory.Label(_playerHost, "等待发牌", Theme.FontBody, Theme.Muted,
                    TextAnchor.MiddleCenter);
                UiFactory.Center(hint.rectTransform, 300f, 60f);
            }
            else
            {
                LayoutRow(_playerHost, _player.Count, 92f, 8f,
                    i => MakeCard(_playerHost, _player[i]));
            }

            _logText.text = _log;
        }

        private static void LayoutRow(RectTransform host, int count, float itemWidth, float gap,
            System.Func<int, RectTransform> create)
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

        private static RectTransform MakeCard(Transform parent, Card card)
        {
            var go = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var img = go.GetComponent<Image>();
            img.sprite = UiFactory.RoundSprite;
            img.type = Image.Type.Sliced;
            img.color = Theme.CardFace;
            img.raycastTarget = false;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = Theme.CardBorder;
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(92f, 132f);

            var color = card.IsRed ? Theme.CardRed : Theme.CardBlack;

            var rank = UiFactory.Label(go.transform, card.RankText, 38, color,
                TextAnchor.MiddleCenter, bold: true);
            var rr = rank.rectTransform;
            rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 0.64f);
            rr.sizeDelta = new Vector2(76f, 48f);

            var suit = UiFactory.Label(go.transform, card.SuitName, 18, color, TextAnchor.MiddleCenter);
            var sr = suit.rectTransform;
            sr.anchorMin = sr.anchorMax = new Vector2(0.5f, 0.30f);
            sr.sizeDelta = new Vector2(76f, 32f);

            return rt;
        }
    }
}
