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
    /// AI 决策走 PokerAiEngine（从桌面版抽出的蒙特卡洛，信息对等不偷看底牌）。
    /// </summary>
    public sealed class PokerPage : MonoBehaviour
    {
        private sealed class Seat
        {
            public string Name = "";
            public bool IsAi;
            public List<Card> Hole = new();
            public int Committed;
            public bool Folded;
            public PokerAiProfile Profile = new();
            public string Status = "等待";
        }

        private App _app;
        private RectTransform _root;
        private PokerAiEngine _ai;

        private readonly List<Seat> _seats = new();
        private readonly List<Card> _community = new();
        private readonly List<Card> _player = new();
        private int _pot;
        private int _stake;
        private int _stage;          // 0翻牌前 1翻牌 2转牌 3河牌
        private bool _inRound;
        private bool _playerFolded;
        private bool _showdown;
        private string _log = "";
        private int _playerCommitted;
        private int _playerRaises;
        private int _lastPlayerAction;   // 0无 1跟注 2加注

        private Text _potText;
        private Text _stageText;
        private Text _logText;
        private RectTransform _communityRow;
        private RectTransform _playerRow;
        private RectTransform _seatsHost;
        private RectTransform _actions;

        private static readonly string[] StageNames = { "翻牌前", "翻牌", "转牌", "河牌" };

        public static void Build(RectTransform root)
        {
            var go = new GameObject("PokerPage");
            go.transform.SetParent(root, false);
            UiFactory.Stretch((RectTransform)go.transform);

            var page = go.AddComponent<PokerPage>();
            page.Init(root);
        }

        private void Init(RectTransform root)
        {
            _app = App.Instance;
            _root = root;
            _ai = new PokerAiEngine(new System.Random());
            _stake = Math.Max(_app.TierMin, 100);

            var content = UiFactory.ScrollView(root, out var sr, 18);
            UiFactory.Stretch((RectTransform)sr.transform);

            var back = UiFactory.Button(content, "← 返回大厅", () => _app.ShowLobby());
            UiFactory.Size(back.gameObject, 300, 92);

            // ---------- 牌桌 ----------
            var table = UiFactory.Card(content, Theme.Table, Theme.TableBorder);
            var layout = UiFactory.VerticalLayout(table.transform, 12,
                new RectOffset(20, 20, 24, 24), TextAnchor.MiddleCenter);
            UiFactory.Stretch((RectTransform)layout.transform);
            var tf = table.gameObject.AddComponent<ContentSizeFitter>();
            tf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _stageText = UiFactory.Label(layout.transform, "翻牌前", 32, Theme.Muted, TextAnchor.MiddleCenter);
            UiFactory.Size(_stageText.gameObject, -1, 46);

            _potText = UiFactory.Label(layout.transform, "底池 $0", 42, Theme.Ink, TextAnchor.MiddleCenter, bold: true);
            UiFactory.Size(_potText.gameObject, -1, 60);

            // 公共牌
            _communityRow = UiFactory.HorizontalLayout(layout.transform, 8, null, TextAnchor.MiddleCenter).rectTransform;
            UiFactory.Size(_communityRow.gameObject, -1, 190);

            // 其他玩家
            _seatsHost = UiFactory.VerticalLayout(layout.transform, 8, null, TextAnchor.MiddleCenter).rectTransform;
            var se = _seatsHost.gameObject.AddComponent<LayoutElement>();
            se.minHeight = 60;

            // 我的牌
            _playerRow = UiFactory.HorizontalLayout(layout.transform, 8, null, TextAnchor.MiddleCenter).rectTransform;
            UiFactory.Size(_playerRow.gameObject, -1, 210);

            // ---------- 日志 ----------
            _logText = UiFactory.Label(content, "", 28, Theme.Muted);
            _logText.lineSpacing = 1.3f;

            // ---------- 操作 ----------
            _actions = UiFactory.HorizontalLayout(content, 12, null, TextAnchor.MiddleCenter).rectTransform;
            UiFactory.Size(_actions.gameObject, -1, 116);

            RefreshTable();
            BuildActions();
        }

        private void BuildActions()
        {
            UiFactory.Clear(_actions);

            if (!_inRound || _showdown)
            {
                var start = UiFactory.PrimaryButton(_actions, _showdown ? "再来一局" : "开始牌局", StartRound);
                UiFactory.Size(start.gameObject, 380, 110);
                return;
            }

            if (_playerFolded)
            {
                var wait = UiFactory.Label(_actions, "你已弃牌，等待结算…", 30, Theme.Muted, TextAnchor.MiddleCenter);
                UiFactory.Size(wait.gameObject, -1, 90);
                return;
            }

            var call = UiFactory.Button(_actions, $"跟注 ${_stake:N0}", () => PlayerAct(1));
            UiFactory.Size(call.gameObject, 260, 110);

            var raise = UiFactory.PrimaryButton(_actions, $"加注 ${_stake * 2:N0}", () => PlayerAct(2));
            UiFactory.Size(raise.gameObject, 300, 110);

            var fold = UiFactory.Button(_actions, "弃牌", () => PlayerAct(0));
            UiFactory.Size(fold.gameObject, 220, 110);
        }

        // ---------- 开局 ----------

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

            var aiCount = Math.Max(1, Math.Min(3, _app.Seats - 1));
            var tierIndex = _app.TierIndex;
            var rng = new System.Random();

            var deck = Deck.New(rng);

            // 我的底牌
            _player.Add(deck[^1]); deck.RemoveAt(deck.Count - 1);
            _player.Add(deck[^1]); deck.RemoveAt(deck.Count - 1);

            // AI 座位
            for (var i = 0; i < aiCount; i++)
            {
                var seat = new Seat
                {
                    Name = $"电脑 {i + 1}",
                    IsAi = true,
                    Profile = new PokerAiProfile
                    {
                        EffectiveTier = tierIndex + 1,
                        PressureResistance = AiTuning.PressureResistance(tierIndex),
                        RiskTolerance = AiTuning.RiskTolerance(tierIndex),
                        CalculationNoise = AiTuning.CalculationNoise(tierIndex),
                        BluffTendency = AiTuning.BluffTendency(tierIndex)
                    }
                };
                seat.Hole.Add(deck[^1]); deck.RemoveAt(deck.Count - 1);
                seat.Hole.Add(deck[^1]); deck.RemoveAt(deck.Count - 1);
                _seats.Add(seat);
            }

            // 盲注
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

            _lastPlayerAction = action == 2 ? 2 : action == 1 ? 1 : 0;

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
            // 每个 AI 依次决策
            foreach (var s in _seats.Where(x => !x.Folded))
            {
                var ctx = new PokerAiContext(
                    _community, _seats.Count(x => !x.Folded), _seats.Count(x => !x.Folded) + (_playerFolded ? 0 : 1),
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
            if (alive == 0 && !_playerFolded)
            {
                // 全部 AI 弃牌，我赢
                WinPot("全部对手弃牌");
                return;
            }

            if (_playerFolded && alive <= 1)
            {
                // 只剩一个 AI，它赢
                _log += " 只剩一位对手，牌局结束。";
                _showdown = true;
                _inRound = false;
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
            var rng = new System.Random();
            var deck = Deck.New(rng);
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

            var myScore = FastPokerScore.Best(
                _player.Concat(_community).ToArray().AsSpan());

            var best = myScore;
            string winner = "你";

            foreach (var s in _seats.Where(x => !x.Folded))
            {
                var theirs = FastPokerScore.Best(
                    s.Hole.Concat(_community).ToArray().AsSpan());
                if (theirs > best) { best = theirs; winner = s.Name; }
            }

            if (winner == "你")
            {
                var gained = _pot;
                _app.Win(gained);
                _log = $"摊牌：你以 {PokerScore.Name(myScore)} 赢得 ${gained:N0}";
                _app.Settle("poker", _playerCommitted, gained, $"摊牌胜出（{PokerScore.Name(myScore)}）");
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

        // ---------- 渲染 ----------

        private void RefreshTable()
        {
            _stageText.text = _showdown ? "摊牌" : StageNames[Math.Clamp(_stage, 0, 3)];
            _potText.text = $"底池 ${_pot:N0}";

            // 公共牌
            UiFactory.Clear(_communityRow);
            for (var i = 0; i < 5; i++)
            {
                if (i < _community.Count) AddCardView(_communityRow, _community[i], false);
                else AddPlaceholder(_communityRow);
            }

            // 其他玩家
            UiFactory.Clear(_seatsHost);
            foreach (var s in _seats)
            {
                var line = UiFactory.Label(_seatsHost, $"{s.Name}：{s.Status} · ${s.Committed:N0}",
                    28, s.Folded ? Theme.Muted : Theme.Ink, TextAnchor.MiddleCenter);
                UiFactory.Size(line.gameObject, -1, 40);
            }

            // 我的牌
            UiFactory.Clear(_playerRow);
            foreach (var c in _player) AddCardView(_playerRow, c, false);
            if (_player.Count == 0)
            {
                var hint = UiFactory.Label(_playerRow, "等待发牌", 30, Theme.Muted, TextAnchor.MiddleCenter);
                UiFactory.Size(hint.gameObject, 300, 60);
            }

            _logText.text = _log;
        }

        private void AddCardView(RectTransform host, Card card, bool hidden)
        {
            var go = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(host, false);

            var img = go.GetComponent<Image>();
            img.sprite = UiFactory.RoundedSprite;
            img.type = Image.Type.Sliced;
            img.color = Theme.CardFace;
            img.raycastTarget = false;

            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(120, 170);
            UiFactory.Size(go, 120, 170);

            if (hidden)
            {
                var q = UiFactory.Label(go.transform, "?", 60, Theme.Accent, TextAnchor.MiddleCenter, bold: true);
                UiFactory.Stretch(q.rectTransform);
            }
            else
            {
                var text = UiFactory.Label(go.transform, $"{card.RankText}\n{card.Suit}", 46,
                    card.IsRed ? Theme.CardRed : Theme.CardBlack, TextAnchor.MiddleCenter, bold: true);
                UiFactory.Stretch(text.rectTransform);
            }
        }

        private void AddPlaceholder(RectTransform host)
        {
            var go = new GameObject("Empty", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(host, false);

            var img = go.GetComponent<Image>();
            img.sprite = UiFactory.RoundedSprite;
            img.type = Image.Type.Sliced;
            img.color = Theme.AccentSoft;
            img.raycastTarget = false;

            UiFactory.Size(go, 120, 170);
            ((RectTransform)go.transform).sizeDelta = new Vector2(120, 170);
        }
    }
}
