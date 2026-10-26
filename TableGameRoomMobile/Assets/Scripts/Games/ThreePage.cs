using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TableGameRoom.Mobile.Core;
using TableGameRoom.Mobile.UI;

namespace TableGameRoom.Mobile.Games
{
    /// <summary>炸金花 —— 对应桌面版 Three.cs。</summary>
    public sealed class ThreePage : MonoBehaviour
    {
        private sealed class Seat
        {
            public string Name = "";
            public List<Card> Cards = new();
            public int Committed;
            public bool Folded;
            public bool Looked;
            public ThreeAiProfile Profile = new();
            public string Status = "等待";
        }

        private App _app;
        private ThreeAiEngine _ai;

        private readonly List<Seat> _seats = new();
        private readonly List<Card> _player = new();
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
        private RectTransform _playerRow;
        private RectTransform _seatsHost;
        private RectTransform _actions;

        public static void Build(RectTransform root)
        {
            var go = new GameObject("ThreePage");
            go.transform.SetParent(root, false);
            UiFactory.Stretch((RectTransform)go.transform);

            var page = go.AddComponent<ThreePage>();
            page.Init(root);
        }

        private void Init(RectTransform root)
        {
            _app = App.Instance;
            _ai = new ThreeAiEngine(new System.Random());
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

            _potText = UiFactory.Label(layout.transform, "底池 $0", 42, Theme.Ink, TextAnchor.MiddleCenter, bold: true);
            UiFactory.Size(_potText.gameObject, -1, 60);

            _statusText = UiFactory.Label(layout.transform, "准备开始", 32, Theme.Muted, TextAnchor.MiddleCenter);
            UiFactory.Size(_statusText.gameObject, -1, 48);

            _seatsHost = (RectTransform)UiFactory.VerticalLayout(layout.transform, 8, null, TextAnchor.MiddleCenter).transform;

            _playerRow = (RectTransform)UiFactory.HorizontalLayout(layout.transform, 10, null, TextAnchor.MiddleCenter).transform;
            UiFactory.Size(_playerRow.gameObject, -1, 210);

            _logText = UiFactory.Label(content, "", 28, Theme.Muted);
            _logText.lineSpacing = 1.3f;

            _actions = (RectTransform)UiFactory.HorizontalLayout(content, 12, null, TextAnchor.MiddleCenter).transform;
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

            if (!_playerLooked)
            {
                var look = UiFactory.Button(_actions, "看牌", Look);
                UiFactory.Size(look.gameObject, 240, 110);
            }

            var call = UiFactory.Button(_actions, $"跟注 ${_stake:N0}", () => PlayerAct(1));
            UiFactory.Size(call.gameObject, 260, 110);

            var raise = UiFactory.PrimaryButton(_actions, $"加注 ${_stake * 2:N0}", () => PlayerAct(2));
            UiFactory.Size(raise.gameObject, 300, 110);

            var compare = UiFactory.Button(_actions, "比牌", Compare);
            UiFactory.Size(compare.gameObject, 220, 110);

            var fold = UiFactory.Button(_actions, "弃牌", () => PlayerAct(0));
            UiFactory.Size(fold.gameObject, 220, 110);
        }

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

            var aiCount = Math.Max(1, Math.Min(3, _app.Seats - 1));
            var tierIndex = _app.TierIndex;
            var rng = new System.Random();
            var deck = Deck.New(rng);

            for (var i = 0; i < 3; i++)
            {
                _player.Add(deck[^1]); deck.RemoveAt(deck.Count - 1);
            }

            for (var i = 0; i < aiCount; i++)
            {
                var seat = new Seat
                {
                    Name = $"电脑 {i + 1}",
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
                    seat.Cards.Add(deck[^1]); deck.RemoveAt(deck.Count - 1);
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
                _app.Msg($"余额不足。", "筹码不足");
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

            var cost = _stake;
            if (!_app.TryPay(cost))
            {
                _app.Msg("余额不足，无法比牌。", "筹码不足");
                return;
            }
            _playerCommitted += cost;
            _pot += cost;

            var alive = _seats.Where(s => !s.Folded).ToList();
            if (alive.Count == 0) { WinPot("对手全部弃牌"); return; }

            var target = alive[0];
            var mine = ThreeScore.Of(_player);
            var theirs = ThreeScore.Of(target.Cards);

            if (mine >= theirs)
            {
                target.Folded = true;
                target.Status = "比牌输";
                _log = $"比牌：你的{ThreeScore.Name(mine)} 胜过 {target.Name} 的{ThreeScore.Name(theirs)}";
            }
            else
            {
                _playerFolded = true;
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

            // 结算
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
            string winner = "你";

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

        private void RefreshTable()
        {
            _potText.text = $"底池 ${_pot:N0}";

            _statusText.text = _showdown ? "摊牌"
                : _playerFolded ? "已弃牌"
                : _playerLooked ? $"已看牌：{ThreeScore.Name(ThreeScore.Of(_player))}"
                : "未看牌";

            UiFactory.Clear(_seatsHost);
            foreach (var s in _seats)
            {
                var line = UiFactory.Label(_seatsHost,
                    $"{s.Name}：{s.Status} · ${s.Committed:N0}",
                    28, s.Folded ? Theme.Muted : Theme.Ink, TextAnchor.MiddleCenter);
                UiFactory.Size(line.gameObject, -1, 40);
            }

            UiFactory.Clear(_playerRow);
            if (_player.Count == 0)
            {
                var hint = UiFactory.Label(_playerRow, "等待发牌", 30, Theme.Muted, TextAnchor.MiddleCenter);
                UiFactory.Size(hint.gameObject, 300, 60);
            }
            else
            {
                foreach (var c in _player)
                {
                    var show = _playerLooked || _showdown;
                    AddCardView(_playerRow, c, !show);
                }
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
            img.color = hidden ? Theme.Accent : Theme.CardFace;
            img.raycastTarget = false;

            UiFactory.Size(go, 120, 170);
            ((RectTransform)go.transform).sizeDelta = new Vector2(120, 170);

            var text = hidden
                ? UiFactory.Label(go.transform, "?", 60, Color.white, TextAnchor.MiddleCenter, bold: true)
                : UiFactory.Label(go.transform, $"{card.RankText}\n{card.Suit}", 46,
                    card.IsRed ? Theme.CardRed : Theme.CardBlack, TextAnchor.MiddleCenter, bold: true);
            UiFactory.Stretch(text.rectTransform);
        }
    }
}
