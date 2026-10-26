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
    /// 德州扑克 —— 对应桌面版 Poker.cs。
    /// AI 走 PokerAiEngine（蒙特卡洛，信息对等，不偷看底牌）。
    /// </summary>
    public sealed class PokerPage : MonoBehaviour
    {
        private sealed class Seat
        {
            public string Name = "";
            public List<Card> Hole = new List<Card>();
            public int Committed;
            public bool Folded;
            public PokerAiProfile Profile = new PokerAiProfile();
            public string Status = "等待";
        }

        private static readonly string[] StageNames = { "翻牌前", "翻牌", "转牌", "河牌" };

        private App _app;
        private PokerAiEngine _ai;

        private readonly List<Seat> _seats = new List<Seat>();
        private readonly List<Card> _community = new List<Card>();
        private readonly List<Card> _player = new List<Card>();

        private int _pot;
        private int _stake;
        private int _stage;
        private bool _inRound;
        private bool _playerFolded;
        private bool _showdown;
        private string _log = "";
        private int _playerCommitted;
        private int _playerRaises;
        private int _lastPlayerAction;

        private Text _stageText;
        private Text _potText;
        private Text _logText;
        private Text _resultText;
        private RectTransform _communityHost;
        private RectTransform _seatsHost;
        private RectTransform _playerHost;
        private RectTransform _actionHost;

        // ==================== 入口 ====================

        public static void Build(RectTransform content)
        {
            var go = new GameObject("PokerPage", typeof(RectTransform));
            go.transform.SetParent(content, false);
            UiFactory.Fill((RectTransform)go.transform);

            go.AddComponent<PokerPage>().Init(content);
        }

        private void Init(RectTransform content)
        {
            _app = App.Instance;
            _ai = new PokerAiEngine(new System.Random());
            _stake = Mathf.Max(_app.TierMin, 100);

            var stack = new VStack(content);

            stack.Button("← 返回大厅", () => _app.ShowLobby(), 88f);

            // ---------- 牌桌 ----------
            stack.Card(inner =>
            {
                // 阶段
                _stageText = UiFactory.Label(inner.Host, "翻牌前", Theme.FontSmall, Theme.Muted,
                    TextAnchor.MiddleCenter);
                UiFactory.TopStretch(_stageText.rectTransform, 40f);

                // 底池
                _potText = UiFactory.Label(inner.Host, "底池 $0", Theme.FontTitle, Theme.Ink,
                    TextAnchor.MiddleCenter, bold: true);
                UiFactory.TopStretch(_potText.rectTransform, 54f, 44f);

                // 公共牌
                _communityHost = UiFactory.Node(inner.Host, "Community");
                UiFactory.TopStretch(_communityHost, 170f, 106f);

                // 其他玩家
                _seatsHost = UiFactory.Node(inner.Host, "Seats");
                var shr = _seatsHost;
                shr.anchorMin = new Vector2(0f, 0.5f);
                shr.anchorMax = new Vector2(1f, 0.5f);
                shr.pivot = new Vector2(0.5f, 0.5f);
                shr.offsetMin = new Vector2(0f, -60f);
                shr.offsetMax = new Vector2(0f, 60f);

                // 我的牌
                _playerHost = UiFactory.Node(inner.Host, "PlayerCards");
                UiFactory.BottomStretch(_playerHost, 170f, 0f);

                return 700f;
            }, Theme.Table, Theme.TableBorder, 20f, 8f);

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
                buttons.Add(UiFactory.Button(_actionHost, $"跟注 ${_stake:N0}", () => PlayerAct(1)));
                buttons.Add(UiFactory.Primary(_actionHost, $"加注 ${_stake * 2:N0}", () => PlayerAct(2)));
                buttons.Add(UiFactory.Button(_actionHost, "弃牌", () => PlayerAct(0)));
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

        // ==================== 开局 ====================

        private void StartRound()
        {
            if (!_app.TryPay(_stake))
            {
                _app.Msg($"余额不足：当前 ${_app.Save.Balance:N0}", "筹码不足");
                return;
            }

            _seats.Clear();
            _community.Clear();
            _player.Clear();

            _pot = 0;
            _stage = 0;
            _inRound = true;
            _playerFolded = false;
            _showdown = false;
            _log = "";
            _playerCommitted = 0;
            _playerRaises = 0;
            _lastPlayerAction = 0;

            var aiCount = Mathf.Clamp(_app.Seats - 1, 1, 3);
            var tierIndex = _app.TierIndex;
            var deck = Deck.New(new System.Random());

            // 我的底牌
            _player.Add(deck[deck.Count - 1]); deck.RemoveAt(deck.Count - 1);
            _player.Add(deck[deck.Count - 1]); deck.RemoveAt(deck.Count - 1);

            // AI 座位
            for (var i = 0; i < aiCount; i++)
            {
                var seat = new Seat
                {
                    Name = $"电脑 {i + 1}",
                    Profile = new PokerAiProfile
                    {
                        EffectiveTier = tierIndex + 1,
                        PressureResistance = AiTuning.PressureResistance(tierIndex),
                        RiskTolerance = AiTuning.RiskTolerance(tierIndex),
                        CalculationNoise = AiTuning.CalculationNoise(tierIndex),
                        BluffTendency = AiTuning.BluffTendency(tierIndex)
                    }
                };

                seat.Hole.Add(deck[deck.Count - 1]); deck.RemoveAt(deck.Count - 1);
                seat.Hole.Add(deck[deck.Count - 1]); deck.RemoveAt(deck.Count - 1);
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

        private void PlayerAct(int action)
        {
            if (!_inRound || _showdown) return;

            _lastPlayerAction = action;

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
                _app.Msg($"余额不足，无法{(action == 2 ? "加注" : "跟注")}。", "筹码不足");
                return;
            }

            _playerCommitted += cost;
            _pot += cost;
            if (action == 2) _playerRaises++;

            _log = action == 2 ? $"你加注 ${cost:N0}。" : $"你跟注 ${cost:N0}。";

            AiPlayOut();
        }

        private void AiPlayOut()
        {
            foreach (var s in _seats.Where(x => !x.Folded))
            {
                var live = _seats.Count(x => !x.Folded);
                var ctx = new PokerAiContext(
                    _community, live, live + (_playerFolded ? 0 : 1),
                    _stake, _pot, _stage, _lastPlayerAction, _playerRaises, _playerFolded);

                if (!_ai.Continues(ctx, s.Profile))
                {
                    s.Folded = true;
                    s.Status = "弃牌";
                    _log += $" {s.Name} 弃牌。";
                    continue;
                }

                if (_ai.Raises(ctx, s.Profile))
                {
                    var cost = _stake * 2;
                    s.Committed += cost;
                    _pot += cost;
                    s.Status = "加注";
                    _log += $" {s.Name} 加注 ${cost:N0}。";
                }
                else
                {
                    s.Committed += _stake;
                    _pot += _stake;
                    s.Status = "跟注";
                }
            }

            AdvanceStage();
        }

        private void AdvanceStage()
        {
            var alive = _seats.Count(s => !s.Folded);

            // 全部对手弃牌
            if (alive == 0 && !_playerFolded)
            {
                WinPot("全部对手弃牌");
                return;
            }

            // 我弃牌且只剩一个对手
            if (_playerFolded && alive <= 1)
            {
                _showdown = true;
                _inRound = false;
                _log += " 牌局结束。";
                RefreshTable();
                BuildActions();
                return;
            }

            _stage++;

            if (_stage > 3)
            {
                Showdown();
                return;
            }

            // 发公共牌
            var deck = Deck.New(new System.Random());
            var count = _stage == 1 ? 3 : 1;
            for (var i = 0; i < count; i++) _community.Add(deck[i]);

            _log += $" 进入{StageNames[_stage]}。";

            RefreshTable();
            BuildActions();
        }

        private void Showdown()
        {
            _showdown = true;
            _inRound = false;

            var mine = FastPokerScore.Best(_player.Concat(_community).ToArray());
            var best = mine;
            var winner = "你";

            foreach (var s in _seats.Where(x => !x.Folded))
            {
                var theirs = FastPokerScore.Best(s.Hole.Concat(_community).ToArray());
                if (theirs > best) { best = theirs; winner = s.Name; }
            }

            if (winner == "你")
            {
                var gained = _pot;
                _app.Win(gained);
                _log = $"摊牌：你以 {PokerScore.Name(mine)} 赢得 ${gained:N0}";
                _app.Settle("poker", _playerCommitted, gained, $"摊牌胜出（{PokerScore.Name(mine)}）");
            }
            else
            {
                _log = $"摊牌：{winner} 以 {PokerScore.Name(best)} 赢得 ${_pot:N0}";
                _app.Settle("poker", _playerCommitted, 0, $"摊牌输给 {winner}（{PokerScore.Name(best)}）");
            }

            RefreshTable();
            BuildActions();
        }

        private void WinPot(string reason)
        {
            var gained = _pot;
            _app.Win(gained);
            _log = $"{reason}，你赢得 ${gained:N0}";
            _app.Settle("poker", _playerCommitted, gained, reason);

            _showdown = true;
            _inRound = false;

            RefreshTable();
            BuildActions();
        }

        // ==================== 渲染 ====================

        private void RefreshTable()
        {
            _stageText.text = _showdown ? "摊牌" : StageNames[Mathf.Clamp(_stage, 0, 3)];
            _potText.text = $"底池 ${_pot:N0}";
            _resultText.text = "";

            // 公共牌（5 张位置固定）
            UiFactory.Clear(_communityHost);
            LayoutRow(_communityHost, 5, 110f, 10f, i =>
                i < _community.Count ? MakeCard(_communityHost, _community[i], false)
                                     : MakePlaceholder(_communityHost));

            // 其他玩家
            UiFactory.Clear(_seatsHost);
            for (var i = 0; i < _seats.Count; i++)
            {
                var s = _seats[i];
                var line = UiFactory.Label(_seatsHost,
                    $"{s.Name}：{s.Status} · ${s.Committed:N0}",
                    Theme.FontSmall, s.Folded ? Theme.Muted : Theme.Ink, TextAnchor.MiddleCenter);

                var lr = line.rectTransform;
                lr.anchorMin = new Vector2(0f, 1f);
                lr.anchorMax = new Vector2(1f, 1f);
                lr.pivot = new Vector2(0.5f, 1f);
                lr.sizeDelta = new Vector2(0f, 42f);
                lr.anchoredPosition = new Vector2(0f, -i * 46f);
            }

            // 我的牌
            UiFactory.Clear(_playerHost);
            if (_player.Count == 0)
            {
                var hint = UiFactory.Label(_playerHost, "等待发牌", Theme.FontBody, Theme.Muted,
                    TextAnchor.MiddleCenter);
                UiFactory.Center(hint.rectTransform, 300f, 60f);
            }
            else
            {
                LayoutRow(_playerHost, _player.Count, 110f, 10f,
                    i => MakeCard(_playerHost, _player[i], false));
            }

            _logText.text = _log;
        }

        /// <summary>把 n 个元素横向居中排布。</summary>
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
            rt.sizeDelta = new Vector2(110f, 155f);

            if (hidden)
            {
                var q = UiFactory.Label(go.transform, "?", 56, Color.white, TextAnchor.MiddleCenter, bold: true);
                UiFactory.Fill(q.rectTransform);
            }
            else
            {
                var color = card.IsRed ? Theme.CardRed : Theme.CardBlack;

                var rank = UiFactory.Label(go.transform, card.RankText, 44, color,
                    TextAnchor.MiddleCenter, bold: true);
                var rr = rank.rectTransform;
                rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 0.64f);
                rr.sizeDelta = new Vector2(90f, 56f);

                var suit = UiFactory.Label(go.transform, card.SuitName, 22, color, TextAnchor.MiddleCenter);
                var sr = suit.rectTransform;
                sr.anchorMin = sr.anchorMax = new Vector2(0.5f, 0.30f);
                sr.sizeDelta = new Vector2(90f, 36f);
            }

            return rt;
        }

        private static RectTransform MakePlaceholder(Transform parent)
        {
            var go = new GameObject("Empty", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var img = go.GetComponent<Image>();
            img.sprite = UiFactory.RoundSprite;
            img.type = Image.Type.Sliced;
            img.color = Theme.AccentSoft;
            img.raycastTarget = false;

            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(110f, 155f);
            return rt;
        }
    }
}
