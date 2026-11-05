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
            public string Result = "";

            /// <summary>本轮具体做了什么，如「加注 $200」。</summary>
            public string LastAction = "";

            /// <summary>
            /// 行动倒计时（30 秒，像真人坐在那儿考虑）。
            /// 和「真实思考时间」是两回事：这个一直显示，让玩家看到「他还有多少时间做决定」。
            /// </summary>
            public float TurnLeft;

            /// <summary>
            /// 真实思考剩余时间（3~25 秒，随压力增长）。
            /// 到点才真正做决定。看牌后会重置这个值，再想一轮。
            /// </summary>
            public float ThinkLeft;

            /// <summary>是否正在思考（显示倒计时气泡）。</summary>
            public bool Thinking;
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

        /// <summary>本局已经进行了多少轮（玩家每次跟/加算一轮）。</summary>
        private int _rounds;

        /// <summary>玩家看牌后已经跟过几轮（用于 3 倍 / 2 倍规则）。</summary>
        private int _playerCallCount;

        /// <summary>
        /// 轮数上限。真实炸金花也有封顶，否则玩家可以无限跟注，
        /// 每轮都逼所有 AI 重新表态 —— 概率累积下来必然把全桌抽干。
        /// 到顶后强制摊牌，牌大的赢。
        /// </summary>
        private const int MaxRounds = 6;

        private Text _potText;
        private Text _statusText;
        private Text _logText;
        private Text _resultText;
        private RectTransform _seatsHost;
        private RectTransform _playerHost;
        private RectTransform _potChipsHost;
        private RectTransform _actionHost;
        private RectTransform _setupHost;
        private RectTransform _tableHost;

        /// <summary>当前正在行动的座位名（用于高亮）。</summary>
        private string _currentSeatName = "";

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

            // ---------- 牌桌（横向椭圆，与桌面版一致） ----------
            stack.FixedCard(inner =>
            {
                _statusText = UiFactory.Label(inner, "准备开始", Theme.FontSmall, Theme.Muted,
                    TextAnchor.MiddleCenter);
                UiFactory.TopStretch(_statusText.rectTransform, 34f);

                // 椭圆桌面 + 座位层
                _tableHost = UiFactory.Node(inner, "TableArea");
                var thr = _tableHost;
                thr.anchorMin = new Vector2(0f, 1f);
                thr.anchorMax = new Vector2(1f, 1f);
                thr.pivot = new Vector2(0.5f, 1f);
                thr.offsetMin = new Vector2(0f, -520f);
                thr.offsetMax = new Vector2(0f, -38f);

                // 椭圆背景（在座位之下）
                var tableBg = UiFactory.Node(_tableHost, "TableBg");
                UiFactory.Fill(tableBg);
                TableRing.BuildTableBackground(tableBg, 1000f, 482f);

                // 中央：底池金额 + 底池筹码
                // 筹码放到椭圆下沿之外，不再压在牌桌中间
                _potText = UiFactory.Label(_tableHost, "底池 $0", Theme.FontSmall, Theme.Muted,
                    TextAnchor.MiddleCenter, bold: true);
                var ptr = _potText.rectTransform;
                ptr.anchorMin = ptr.anchorMax = new Vector2(0.5f, 0.5f);
                ptr.pivot = new Vector2(0.5f, 0.5f);
                ptr.sizeDelta = new Vector2(300f, 34f);
                ptr.anchoredPosition = new Vector2(0f, 20f);

                _potChipsHost = UiFactory.Node(_tableHost, "PotChips");
                var pcr = _potChipsHost;
                pcr.anchorMin = pcr.anchorMax = new Vector2(0.5f, 0.5f);
                pcr.pivot = new Vector2(0.5f, 0.5f);
                pcr.sizeDelta = new Vector2(300f, 40f);
                // 下移到椭圆下沿，避开中央区域
                pcr.anchoredPosition = new Vector2(0f, -34f);

                // 座位层
                _seatsHost = UiFactory.Node(_tableHost, "Seats");
                UiFactory.Fill(_seatsHost);

                // 玩家自己（底部中央）
                _playerHost = UiFactory.Node(_tableHost, "SelfSeat");
                var phr = _playerHost;
                phr.anchorMin = phr.anchorMax = new Vector2(0.5f, 0.5f);
                phr.pivot = new Vector2(0.5f, 0.5f);
                phr.sizeDelta = new Vector2(320f, 150f);

                var selfPos = TableRing.ToLocal(TableRing.PlayerPos, 1000f, 482f, 320f, 150f);
                phr.anchoredPosition = selfPos;
            }, 540f);

            // ---------- 结果 ----------
            _resultText = stack.CenterText("", Theme.FontHead, Theme.Accent, bold: true, height: 54f);

            // ---------- 设置（下注 / 人数 / 档位） ----------
            _setupHost = UiFactory.Node(content, "Setup");
            stack.Add(_setupHost, GameSetupPanel.EstimateHeight() + 12f);

            // ---------- 日志 ----------
            _logText = stack.Text("", Theme.FontSmall, Theme.Muted);

            // ---------- 操作 ----------
            _actionHost = UiFactory.Node(content, "Actions");
            stack.Add(_actionHost, 110f);

            RefreshTable();
            BuildActions();

            stack.Finish();
        }

        /// <summary>重建设置栏（开局前可调，开局后显示对局信息）。</summary>
        private void BuildSetup()
        {
            if (_setupHost == null) return;

            UiFactory.Clear(_setupHost);

            if (_inRound && !_showdown)
            {
                var info = UiFactory.Label(_setupHost,
                    $"下注 ${_stake:N0}　·　底池 ${_pot:N0}　·　档位 {_app.TierName}",
                    Theme.FontBody, Theme.Muted, TextAnchor.MiddleCenter);
                var ir = info.rectTransform;
                ir.anchorMin = ir.anchorMax = new Vector2(0.5f, 0.5f);
                ir.pivot = new Vector2(0.5f, 0.5f);
                ir.sizeDelta = new Vector2(1000f, 60f);
                ir.anchoredPosition = Vector2.zero;
                return;
            }

            var cbs = new GameSetupPanel.Callbacks
            {
                OnStakeChanged = v => { _stake = v; BuildSetup(); BuildActions(); },
                OnSeatsChanged = _ => BuildSetup(),
                OnTierChanged = _ =>
                {
                    _stake = GameSetupPanel.ClampStake(_stake, _app);
                    BuildSetup();
                    BuildActions();
                }
            };

            _stake = GameSetupPanel.Build(_setupHost, _stake, cbs);
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

                // 跟注金额随规则变化，按钮上直接写清楚
                var callCost = CallCost;
                var callTag = !_playerLooked ? "闷" : _playerCallCount == 0 ? "看3×" : "2×";
                buttons.Add(UiFactory.Button(_actionHost,
                    $"跟 ${callCost:N0}（{callTag}）", () => PlayerAct(1)));

                buttons.Add(UiFactory.Primary(_actionHost, "加注", ChooseRaise));
                buttons.Add(UiFactory.Button(_actionHost, "比牌", Compare));
                buttons.Add(UiFactory.Button(_actionHost, "弃牌", () => PlayerAct(0)));
            }

            if (buttons.Count == 0) return;

            // 按可用宽度均分，不要写死上限 —— 否则 5 个按钮放不下，
            // 每个都会被压窄导致文字截断。
            const float gap = 8f;
            var totalWidth = _actionHost.rect.width;
            if (totalWidth <= 1f) totalWidth = 1000f;

            var count = buttons.Count;
            var btnWidth = (totalWidth - gap * (count - 1)) / count;

            for (var i = 0; i < count; i++)
            {
                var rt = buttons[i].GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(btnWidth, 100f);
                rt.anchoredPosition = new Vector2(i * (btnWidth + gap), 0f);

                // 按钮文字随宽度自适应缩小，避免被截断
                var label = rt.GetComponentInChildren<Text>();
                if (label != null)
                    label.fontSize = Mathf.Clamp(Mathf.RoundToInt(btnWidth * 0.12f), 20, 30);
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
            _rounds = 0;
            _playerCallCount = 0;

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
                        RiskTolerance = AiTuning.RiskTolerance(tierIndex),
                        CalculationNoise = AiTuning.CalculationNoise(tierIndex),
                        BluffTendency = AiTuning.BluffTendency(tierIndex)
                    }
                };

                for (var k = 0; k < 3; k++)
                {
                    seat.Cards.Add(deck[deck.Count - 1]);
                    deck.RemoveAt(deck.Count - 1);
                }

                // AI 决策引擎从 Profile.Cards 读牌，必须同步一份，
                // 否则 ThreeScore.Of 会拿到空列表直接越界崩掉。
                foreach (var c in seat.Cards) seat.Profile.Cards.Add(c);

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
            _log = $"你看牌了：{ThreeScore.Name(ThreeScore.Of(_player))}。" +
                   " 看牌后跟注变为 3 倍，之后每轮 2 倍。";
            RefreshTable();
            BuildActions();
        }

        /// <summary>
        /// 本轮跟注需要多少。
        ///
        /// 炸金花的规则：
        ///   · 闷牌（没看牌）    → 1 倍底注
        ///   · 看牌后的第一次跟注 → 3 倍底注
        ///   · 看牌之后的每轮     → 2 倍底注
        ///
        /// 这是「看牌要付代价」的核心：看牌能知道牌力，但注额也翻上去了。
        /// </summary>
        private int CallCost
        {
            get
            {
                if (!_playerLooked) return _stake;
                return _playerCallCount == 0 ? _stake * 3 : _stake * 2;
            }
        }

        /// <summary>加注倍数的基准金额（跟注额）。</summary>
        private int RaiseBase => CallCost;

        /// <summary>
        /// 加注：直接弹输入框让玩家填金额。
        ///
        /// 之前给了一堆快捷倍数（1.5/2/2.5/3…），但既然能自己填，
        /// 那些选项就是多余的 —— 直接输入更快也更自由。
        /// 只保留最低 1.5 倍和档位上限的校验。
        /// </summary>
        private void ChooseRaise()
        {
            if (!_inRound || _showdown || _playerFolded) return;

            var baseCost = RaiseBase;
            var minCost = Mathf.CeilToInt(baseCost * MinRaiseMultiplier);

            PromptCustomRaise(minCost);
        }

        /// <summary>最低加注倍数。</summary>
        private const float MinRaiseMultiplier = 1.5f;

        /// <summary>弹输入框让玩家自己填加注金额。</summary>
        private void PromptCustomRaise(int minCost)
        {
            var cap = _app.Tier.Max ?? int.MaxValue;
            var capText = _app.Tier.Max is int c ? $"${c:N0}" : "无上限";
            var baseCost = RaiseBase;

            Dialog.Input(_app.CanvasRoot, "加注金额",
                $"跟注额 ${baseCost:N0}　最低 ${minCost:N0}（{MinRaiseMultiplier:0.#} 倍）　上限 {capText}",
                minCost.ToString(),
                text =>
                {
                    if (!int.TryParse(text?.Trim(), out var amount))
                    {
                        _app.Msg("请输入有效的整数金额。", "输入有误");
                        return;
                    }

                    if (amount < minCost)
                    {
                        _app.Msg($"加注金额不能低于 ${minCost:N0}（{MinRaiseMultiplier:0.#} 倍）。", "金额过低");
                        return;
                    }

                    if (amount > cap)
                    {
                        _app.Msg($"超过档位单次上限 ${cap:N0}。", "金额过高");
                        return;
                    }

                    PlayerActRaise(amount);
                });
        }

        /// <summary>按指定金额加注。</summary>
        private void PlayerActRaise(int cost)
        {
            if (!_inRound || _showdown || _playerFolded) return;

            if (!_app.TryPay(cost))
            {
                _app.Msg("余额不足。", "筹码不足");
                return;
            }

            _playerCommitted += cost;
            _pot += cost;
            _playerRaises++;
            _rounds++;

            // 注额跟着涨，后续跟注按新注额算
            _stake = Mathf.Max(_stake, cost / 2);

            _log = $"你加注 ${cost:N0}。";

            // 停掉自己的倒计时
            AiPlayOut();
        }

        /// <summary>玩家行动：0=弃牌，1=跟注。</summary>
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

            // 跟注金额按「闷牌 1× / 看牌首轮 3× / 之后 2×」算
            var cost = CallCost;

            if (!_app.TryPay(cost))
            {
                _app.Msg($"余额不足：需要 ${cost:N0}。", "筹码不足");
                return;
            }

            _playerCommitted += cost;
            _pot += cost;
            _rounds++;
            _playerCallCount++;

            var tag = !_playerLooked ? "闷牌" : _playerCallCount == 1 ? "看牌首轮" : "看牌";
            _log = $"你跟注 ${cost:N0}（{tag}）。";

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

        /// <summary>
        /// 电脑逐个行动，模拟真人的两段式思考。
        ///
        /// 每位 AI 有两套时间：
        ///   1. **行动倒计时**（30 秒，固定在头顶显示）：像真人坐在那儿，还有多少时间做决定。
        ///   2. **真实思考时间**（3~25 秒，随压力增长）：到点才真正做决定。
        ///
        /// 流程：
        ///   思考 → 决定看牌 → 看完**重置思考时间**再想一轮 → 决定跟/加/弃
        ///
        /// 这样玩家看到的就是「对面先犹豫、翻牌看一眼、又想了半天才加注」，
        /// 而不是一瞬间全员出结果。
        /// </summary>
        private void AiPlayOut()
        {
            StartCoroutine(AiPlayOutRoutine());
        }

        /// <summary>行动倒计时总长（秒）。</summary>
        private const float TurnSeconds = 30f;

        /// <summary>
        /// 真实思考时间：3~25 秒，压力越大越久。
        /// 压力来自：轮次（越往后越紧张）+ 注额相对档位上限的比例 + 是否被加注。
        /// </summary>
        private float RollThinkSeconds(int round, int stake, bool pressured)
        {
            var roundPressure = Mathf.Clamp01(round / 6f);
            var stakePressure = _app.Tier.Max is int cap && cap > 0
                ? Mathf.Clamp01((float)stake / cap)
                : Mathf.Clamp01(Mathf.Log10(Mathf.Max(1, stake)) / 6f);

            var pressure = roundPressure * 0.5f + stakePressure * 0.5f;
            if (pressured) pressure = Mathf.Min(1f, pressure + 0.2f);

            // 3 秒打底，最高 25 秒
            var seconds = Mathf.Lerp(3f, 25f, pressure);

            // ±25% 随机波动，避免节奏呆板
            seconds *= UnityEngine.Random.Range(0.75f, 1.25f);

            return Mathf.Clamp(seconds, 3f, 25f);
        }

        /// <summary>
        /// 跑一段思考过程：真实思考时间递减，同时 30 秒行动倒计时也在走。
        ///
        /// 注意两者的长度不一样：真实思考只有 3~25 秒，行动倒计时固定 30 秒。
        /// 如果都按 1:1 递减，倒计时永远走不到 0（最多减到 5），玩家会以为卡住了。
        /// 所以倒计时按比例折算 —— 思考结束的那一刻，倒计时正好归零。
        /// </summary>
        private System.Collections.IEnumerator ThinkPhase(Seat s, bool refill)
        {
            if (refill && s.ThinkLeft <= 0f)
                s.ThinkLeft = RollThinkSeconds(_rounds, _stake, _playerRaises > 0);

            var total = Mathf.Max(0.1f, s.ThinkLeft);
            var turnTotal = s.TurnLeft > 0f ? s.TurnLeft : TurnSeconds;

            while (s.ThinkLeft > 0.01f)
            {
                yield return new WaitForSeconds(0.1f);

                s.ThinkLeft -= 0.1f;

                // 倒计时按「剩余思考 / 总思考」等比缩放
                var ratio = Mathf.Clamp01(s.ThinkLeft / total);
                s.TurnLeft = turnTotal * ratio;

                RefreshTable();
            }

            s.TurnLeft = 0f;
        }

        private System.Collections.IEnumerator AiPlayOutRoutine()
        {
            _currentSeatName = "";

            foreach (var s in _seats.Where(x => !x.Folded).ToList())
            {
                if (s.Folded) continue;

                // ---------- 开始行动：亮出 30 秒倒计时 ----------
                _currentSeatName = s.Name;
                s.Thinking = true;
                s.TurnLeft = TurnSeconds;
                s.ThinkLeft = RollThinkSeconds(_rounds, _stake, _playerRaises > 0);
                RefreshTable();

                // ---------- 第一段：思考要不要看牌 ----------
                yield return ThinkPhase(s, refill: false);

                // ---------- 决定看牌 ----------
                if (!s.Profile.Looked && _rng.NextDouble() < 0.7)
                {
                    s.Profile.Looked = true;
                    s.LastAction = "看牌";
                    RefreshTable();

                    // 看完牌重置思考时间，再想一轮（真人也是看完再算）
                    s.ThinkLeft = RollThinkSeconds(_rounds, _stake, _playerRaises > 0);
                    yield return ThinkPhase(s, refill: true);
                }

                // ---------- 第二段结束，正式做决定 ----------
                s.Thinking = false;
                s.TurnLeft = 0f;
                s.ThinkLeft = 0f;
                _currentSeatName = "";

                // 还在局中的其他座位数（不含自己），玩家未弃牌也算一个
                var others = _seats.Count(x => !x.Folded && !ReferenceEquals(x, s))
                             + (_playerFolded ? 0 : 1);

                var ctx = new ThreeAiContext(_stake, _pot, _playerRaises, _playerFolded,
                    Math.Max(1, others));

                if (!_ai.Continues(ctx, s.Profile))
                {
                    s.Folded = true;
                    s.Status = "弃牌";
                    s.Emoji = "😞";
                    s.LastAction = "已弃牌";
                    _log += $" {s.Name} 弃牌。";
                    RefreshTable();
                    yield return new WaitForSeconds(0.3f);
                    continue;
                }

                s.Profile.DecisionsUsed++;

                if (_ai.Raises(ctx, s.Profile))
                {
                    var cost = _stake * 2;
                    s.Committed += cost;
                    _pot += cost;
                    s.Status = "加注";
                    s.Emoji = "😄";
                    s.LastAction = $"加注 ${cost:N0}";
                    _log += $" {s.Name} 加注 ${cost:N0}。";
                }
                else
                {
                    s.Committed += _stake;
                    _pot += _stake;
                    s.Status = "跟注";
                    s.Emoji = "😐";
                    s.LastAction = $"跟注 ${_stake:N0}";
                }

                RefreshTable();
                yield return new WaitForSeconds(0.3f);
            }

            var alive = _seats.Count(s => !s.Folded);

            _currentSeatName = "";

            // 轮数封顶：到了上限就摊牌，避免玩家无限跟注把 AI 一个个抽干
            if (_rounds >= MaxRounds && alive > 0 && !_playerFolded)
            {
                _log += $" 已达 {MaxRounds} 轮上限，强制摊牌。";
                Showdown();
                yield break;
            }

            if (alive == 0 && !_playerFolded)
            {
                WinPot("全部对手弃牌");
                yield break;
            }

            if (_playerFolded && alive <= 1)
            {
                _showdown = true;
                _inRound = false;
                _log += " 牌局结束。";
                RefreshTable();
                BuildActions();
                yield break;
            }

            if (alive <= 1 && !_playerFolded)
            {
                Showdown();
                yield break;
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
            }            else
            {
                _log = $"摊牌：{winner} 以{ThreeScore.Name(best)}赢得 ${_pot:N0}";
                _app.Settle("three", _playerCommitted, 0, $"摊牌输给 {winner}（{ThreeScore.Name(best)}）");
            }

            // 每个座位显示自己的牌型，一眼看出谁大谁小
            foreach (var s in _seats)
            {
                if (s.Folded)
                {
                    s.Result = "弃牌";
                    continue;
                }

                var handName = ThreeScore.Name(ThreeScore.Of(s.Cards));
                s.Result = s.Name == winner ? $"{handName} 胜" : handName;
                s.Status = s.Result;
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
                var potChips = ChipVisual.Create(_potChipsHost, _pot, 0.70f);
                potChips.anchoredPosition = Vector2.zero;
            }

            _statusText.text = _showdown ? "摊牌"
                : _playerFolded ? "已弃牌"
                : _playerLooked ? $"已看牌：{ThreeScore.Name(ThreeScore.Of(_player))}"
                : "未看牌";

            UiFactory.Clear(_seatsHost);
            var seatCount = _seats.Count;

            if (seatCount > 0)
            {
                var positions = TableRing.ComputerPositions(seatCount);

                // 用容器真实尺寸（不同分辨率下才能对齐椭圆）
                var tableW = _tableHost.rect.width;
                var tableH = _tableHost.rect.height;
                if (tableW <= 1f) tableW = 1000f;
                if (tableH <= 1f) tableH = 482f;

                for (var i = 0; i < seatCount; i++)
                {
                    var s = _seats[i];
                    var isCurrent = s.Name == _currentSeatName;

                    var card = SeatCard.Create(
                        _seatsHost,
                        s.Name, s.Emoji, i,
                        s.Status, s.Committed, s.Folded, isCurrent,
                        s.Result, s.Cards, hidden: !_showdown,
                        looked: s.Profile.Looked,
                        lastAction: s.LastAction,
                        thinking: s.Thinking,
                        thinkLeft: s.ThinkLeft,
                        turnLeft: s.TurnLeft);

                    var cr = card;
                    cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 0.5f);
                    cr.pivot = new Vector2(0.5f, 0.5f);

                    var norm = positions[Mathf.Min(i, positions.Length - 1)];
                    cr.anchoredPosition = TableRing.ToLocal(norm, tableW, tableH,
                        SeatCard.Width, TableRing.SeatHeight);
                }
            }

            UiFactory.Clear(_playerHost);
            if (_player.Count == 0)
            {
                var hint = UiFactory.Label(_playerHost, "等待发牌", Theme.FontSmall, Theme.Muted,
                    TextAnchor.MiddleCenter);
                UiFactory.Center(hint.rectTransform, 280f, 50f);
            }
            else
            {
                // 头像（左侧）
                var avatar = AvatarVisual.Create(_playerHost, "你", _playerEmoji, 0,
                    isPlayer: true, size: 52f);
                var avr = avatar;
                avr.anchorMin = avr.anchorMax = new Vector2(0f, 0.5f);
                avr.pivot = new Vector2(0f, 0.5f);
                avr.anchoredPosition = new Vector2(2f, 0f);

                var show = _playerLooked || _showdown;
                LayoutRow(_playerHost, _player.Count, 56f, 6f,
                    i => MakeCard(_playerHost, _player[i], !show, 0.5f));

                // 已下注筹码
                if (_playerCommitted > 0)
                {
                    var chips = ChipVisual.Create(_playerHost, _playerCommitted, 0.42f);
                    var crt = chips;
                    crt.anchorMin = crt.anchorMax = new Vector2(1f, 0.5f);
                    crt.pivot = new Vector2(1f, 0.5f);
                    crt.anchoredPosition = new Vector2(-2f, 0f);
                }
            }

            // 设置栏随牌局状态显示 / 隐藏
            BuildSetup();

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
