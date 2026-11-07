using System;
using System.Collections.Generic;
using System.Linq;
using TableGameRoom.Mobile.Core;

namespace TableGameRoom.Mobile.Games
{
    /// <summary>炸金花 AI 决策引擎（纯逻辑）。</summary>
    public sealed class ThreeAiEngine
    {
        private readonly Random _rng;
        public ThreeAiEngine(Random rng) => _rng = rng;

        /// <summary>
        /// 炸金花 AI 是否继续跟注。
        ///
        /// 关键设计（修掉「玩家一直跟注 → 全桌弃光」）：
        ///
        /// 1. **闷牌几乎不弃**。没看牌时跟注只要 1 倍，很便宜，真人也不会走。
        /// 2. **看牌后按牌型决定**，且跟了就锁定（<see cref="ThreeAiProfile.Staying"/>），
        ///    本局后续轮次不再重新掷骰。
        ///    之前每轮独立重掷，跟 3 轮后每个 AI 被抽掉的概率是 1-0.42³ ≈ 93%，
        ///    6 人桌几乎必然全灭 —— 这就是「无论什么操作，只要不弃牌，所有人都会弃」。
        /// 3. 牌型基准概率按真实牌局校准，单张也有 40% 以上会跟，不会一边倒。
        /// </summary>
        public bool Continues(ThreeAiContext ctx, ThreeAiProfile seat)
        {
            // 已经表态过「跟」，本局不再反悔
            if (seat.Staying) return true;

            var tierIndex = TierIndex(seat.EffectiveTier);
            var strength = AiTuning.Strength(tierIndex);

            var score = ThreeScore.Of(seat.Cards);
            var category = (int)(score / 1_000_000L);

            // ---------- 1) 闷牌：几乎必跟 ----------
            // 没看牌时跟注只要 1 倍底注，很便宜。只有被连续加注才会走。
            // 注意这里**不锁** Staying —— 看过牌之后还要重新判断一次。
            if (!seat.Looked)
            {
                var blindFold = 0.04;
                if (ctx.PlayerRaises >= 2) blindFold += 0.12;
                if (ctx.PlayerRaises >= 4) blindFold += 0.15;
                return _rng.NextDouble() >= blindFold;
            }

            // ---------- 2) 看牌后：按牌型 ----------
            double keep;
            switch (category)
            {
                case 5: keep = 0.98; break;    // 同花顺
                case 4: keep = 0.96; break;    // 豹子
                case 3: keep = 0.90; break;    // 顺子
                case 2: keep = 0.84; break;    // 同花
                case 1:                        // 对子：看对子大小
                    {
                        var pairRank = PairRank(seat.Cards);
                        keep = 0.62 + (pairRank - 7) * 0.025;   // A 对 ~0.80，小对 ~0.50
                        break;
                    }
                default:                       // 单张：看最大牌
                    {
                        var high = HighCard(seat.Cards);
                        keep = 0.40 + (high - 7) * 0.040;       // A 高 ~0.68，小牌 ~0.40
                        break;
                    }
            }

            // 低档更随机：向 0.55 回归，但幅度很小（之前回 0.45 太狠，把所有牌型拉平了）
            if (strength < 0.5)
                keep = 0.55 + (keep - 0.55) * (0.75 + strength * 0.5);

            // 风险容忍：低档更莽
            keep += (seat.RiskTolerance - 0.5) * 0.12;

            // 被玩家加注施压（低档更怕，高档几乎不怕）
            if (ctx.PlayerRaises >= 1)
                keep -= AiTuning.Lerp(0.18, 0.04, strength)
                        * (1.10 - seat.PressureResistance * 0.55);

            // 计算误差
            keep += (_rng.NextDouble() - 0.5) * seat.CalculationNoise;

            // ---------- 3) 真实胜率修正（高档才做模拟） ----------
            var trials = (int)Math.Round(AiTuning.Lerp(0, 2000, strength * strength));
            if (trials >= 30)
            {
                var liveOpp = Math.Max(1, ctx.LiveOpponents);
                var winRate = EstimateWinRate(seat.Cards, liveOpp, trials);

                var callCost = Math.Max(1, ctx.Stake);
                var potOdds = callCost / (double)(ctx.Pot + callCost);
                var required = potOdds * 0.85;

                // 胜率高于所需 → 更愿意跟；低于 → 更想走
                var adjust = (winRate - required) * 1.2;
                keep = AiTuning.BlendThree(keep, keep + adjust, tierIndex);
            }

            // ---------- 4) 连续跟注的粘性 ----------
            // 玩家一直跟注说明要么强牌要么在诈，AI 陪下去的意愿会上升
            keep += Math.Min(0.30, seat.DecisionsUsed * 0.06);

            keep = Math.Clamp(keep, 0.05, 0.97);

            var stays = _rng.NextDouble() < keep;

            // 看牌后决定跟，本局就锁定，不再重掷
            if (stays) seat.Staying = true;
            return stays;
        }

        /// <summary>
        /// 炸金花胜率蒙特卡洛：从剩余牌里给对手发牌，比较牌型。
        /// 只用自己三张牌 + 未知牌，不偷看对手底牌。
        /// </summary>
        private double EstimateWinRate(IReadOnlyList<Card> mine, int opponents, int trials)
        {
            var known = new HashSet<Card>(mine);
            var pool = new List<Card>(52);
            foreach (var suit in Card.Suits)
                for (var rank = 2; rank <= 14; rank++)
                {
                    var c = new Card(rank, suit);
                    if (!known.Contains(c)) pool.Add(c);
                }

            var needed = opponents * 3;
            if (pool.Count < needed) return 0.5;

            var mineScore = ThreeScore.Of(mine);
            var buf = pool.ToArray();
            var completed = 0;
            var wins = 0.0;

            for (var t = 0; t < trials; t++)
            {
                var count = buf.Length;
                var bestOpp = long.MinValue;

                for (var oi = 0; oi < opponents; oi++)
                {
                    var hand = new Card[3];
                    for (var k = 0; k < 3; k++)
                    {
                        var idx = _rng.Next(count);
                        hand[k] = buf[idx];
                        buf[idx] = buf[--count];
                    }

                    var s = ThreeScore.Of(hand);
                    if (s > bestOpp) bestOpp = s;
                }

                wins += mineScore > bestOpp ? 1 : mineScore == bestOpp ? 0.5 : 0;
                completed++;

                // 还原被抽走的牌（只交换了前 count 个位置，直接重置引用即可）
                for (var i = 0; i < buf.Length; i++) buf[i] = pool[i];
            }

            return completed == 0 ? 0.5 : wins / completed;
        }

        /// <summary>对子的大小（取对子那张的 rank）。</summary>
        private static int PairRank(IReadOnlyList<Card> cards)
        {
            if (cards.Count < 3) return 0;
            if (cards[0].Rank == cards[1].Rank) return cards[0].Rank;
            if (cards[1].Rank == cards[2].Rank) return cards[1].Rank;
            if (cards[0].Rank == cards[2].Rank) return cards[0].Rank;
            return cards.Max(c => c.Rank);
        }

        /// <summary>最大牌。</summary>
        private static int HighCard(IReadOnlyList<Card> cards) =>
            cards.Count == 0 ? 0 : cards.Max(c => c.Rank);

        /// <summary>AI 是否加注。同样按牌型定基准，不再用错误的归一化。</summary>
        public bool Raises(ThreeAiContext ctx, ThreeAiProfile seat)
        {
            var tierIndex = TierIndex(seat.EffectiveTier);
            var strength = AiTuning.Strength(tierIndex);

            var score = ThreeScore.Of(seat.Cards);
            var category = (int)(score / 1_000_000L);

            // 牌型越强越愿意加注
            double baseProb;
            switch (category)
            {
                case 5: baseProb = 0.85; break;
                case 4: baseProb = 0.80; break;
                case 3: baseProb = 0.55; break;
                case 2: baseProb = 0.42; break;
                case 1: baseProb = 0.30; break;
                default: baseProb = 0.12; break;
            }

            // 高档更敢加
            baseProb *= AiTuning.Lerp(0.75, 1.35, strength);

            // 诈唬
            var bluffProb = seat.BluffTendency * (ctx.PlayerFolded ? 1.4 : 1.0);
            if (_rng.NextDouble() < Math.Clamp(bluffProb, 0.01, 0.16)) return true;

            return _rng.NextDouble() < Math.Clamp(baseProb, 0.02, 0.92);
        }

        /// <summary>AI 是否比牌。</summary>
        public bool Compares(ThreeAiContext ctx, ThreeAiProfile seat)
        {
            var tierIndex = TierIndex(seat.EffectiveTier);
            var strength = AiTuning.Strength(tierIndex);

            var score = ThreeScore.Of(seat.Cards);
            var category = (int)(score / 1_000_000L);

            // 强牌更愿意比，弱牌偶尔偷鸡
            double threshold;
            switch (category)
            {
                case 5: threshold = 0.90; break;
                case 4: threshold = 0.85; break;
                case 3: threshold = 0.65; break;
                case 2: threshold = 0.50; break;
                case 1: threshold = 0.35; break;
                default: threshold = 0.15; break;
            }

            threshold *= AiTuning.Lerp(1.15, 0.85, strength);

            if (_rng.NextDouble() < threshold) return true;
            return _rng.NextDouble() < seat.BluffTendency * 0.5;
        }

        private static int TierIndex(double effectiveTier) =>
            (int)Math.Round(Math.Clamp(effectiveTier - 1, 0, 6));
    }

    public readonly struct ThreeAiContext
    {
        public readonly int Stake;
        public readonly int Pot;
        public readonly int PlayerRaises;
        public readonly bool PlayerFolded;

        /// <summary>仍在局中的对手数（不含自己），用于胜率模拟。</summary>
        public readonly int LiveOpponents;

        public ThreeAiContext(int stake, int pot, int playerRaises, bool playerFolded,
            int liveOpponents = 1)
        {
            Stake = stake;
            Pot = pot;
            PlayerRaises = playerRaises;
            PlayerFolded = playerFolded;
            LiveOpponents = liveOpponents;
        }
    }

    public sealed class ThreeAiProfile
    {
        public List<Card> Cards { get; } = new List<Card>();
        public double EffectiveTier { get; set; } = 1;
        public double PressureResistance { get; set; }
        public double RiskTolerance { get; set; } = 0.5;
        public double CalculationNoise { get; set; }
        public double BluffTendency { get; set; }

        /// <summary>
        /// 已经用过的「继续」判定次数。
        /// 炸金花里玩家每点一次跟注，都会轮到 AI 表态；
        /// 如果每次都重新掷骰，跟得越多全桌越可能被抽干（概率是累积的）。
        /// </summary>
        public int DecisionsUsed { get; set; }

        /// <summary>
        /// 本局已经决定「继续跟」。一旦为 true，后续轮次不再重新判定。
        /// 这是修掉「一直跟注就全员弃牌」的关键 —— 每轮重掷会让概率累积，
        /// 6 人桌跟 3 轮几乎必然全灭。
        /// </summary>
        public bool Staying { get; set; }

        /// <summary>本局是否已经看过牌（影响后续的加注/比牌决策）。</summary>
        public bool Looked { get; set; }

        /// <summary>
        /// 是否已经做过「要不要看牌」的判定。
        /// 看牌是整局一次的决定，不能每轮重掷 —— 否则几轮下来全员都会看牌。
        /// </summary>
        public bool LookDecided { get; set; }

        /// <summary>
        /// 看牌后已经跟过几轮（用于 3 倍 / 2 倍规则）。
        /// 和玩家侧的 _playerCallCount 同一套逻辑。
        /// </summary>
        public int CallCount { get; set; }
    }
}
