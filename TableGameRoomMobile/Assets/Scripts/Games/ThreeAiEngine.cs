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
        /// 用「牌型查表 + 真实胜率」混合，和桌面版 Three.cs::AiThreeKeeps 同一套思路。
        /// 之前这里把 ThreeScore 直接归一化成 0..1 再和 0.42 比，
        /// 但单张只算得出 0.02、对子 0.17 —— 而单张和对子占实际牌型的大多数，
        /// 结果就是玩家一直跟注时 AI 一轮轮全弃，桌面版没这个问题。
        /// </summary>
        public bool Continues(ThreeAiContext ctx, ThreeAiProfile seat)
        {
            var tierIndex = TierIndex(seat.EffectiveTier);
            var strength = AiTuning.Strength(tierIndex);

            var score = ThreeScore.Of(seat.Cards);
            var category = (int)(score / 1_000_000L);

            // ---------- 1) 查表概率：按牌型定基准 ----------
            double tableProb;
            switch (category)
            {
                case 5: tableProb = 0.96; break;   // 同花顺
                case 4: tableProb = 0.94; break;   // 豹子
                case 3: tableProb = 0.78; break;   // 顺子
                case 2: tableProb = 0.66; break;   // 同花
                case 1:                            // 对子：看对子大小
                    {
                        var pairRank = PairRank(seat.Cards);
                        tableProb = 0.52 + (pairRank - 7) * 0.03;
                        break;
                    }
                default:                           // 单张：看最大牌
                    {
                        var high = HighCard(seat.Cards);
                        tableProb = 0.28 + (high - 7) * 0.022;
                        break;
                    }
            }

            // 风险容忍：高风险更愿意用弱牌跟
            tableProb += (seat.RiskTolerance - 0.5) * 0.10;

            // 低档向 0.45 回归（行为更随机）
            if (strength < 0.5)
                tableProb = 0.45 + (tableProb - 0.45) * (0.55 + strength * 0.9);

            // 玩家加注施压（低档更怕，高档几乎不怕）
            var pressure = 0.0;
            if (ctx.PlayerRaises >= 1)
                pressure = AiTuning.Lerp(0.22, 0.05, strength) * (1.10 - seat.PressureResistance * 0.55);
            tableProb -= pressure;

            // 计算误差
            tableProb += (_rng.NextDouble() - 0.5) * seat.CalculationNoise;
            tableProb = Math.Clamp(tableProb, 0.05, 0.98);

            // ---------- 2) 真实胜率：从剩余牌模拟 ----------
            var liveOpponents = ctx.LiveOpponents;
            if (liveOpponents < 1) liveOpponents = 1;

            var trials = (int)Math.Round(AiTuning.Lerp(0, 2400, strength * strength));
            double winRateProb;
            if (trials < 30)
            {
                winRateProb = tableProb;    // 低档不做模拟
            }
            else
            {
                var winRate = EstimateWinRate(seat.Cards, liveOpponents, trials);
                var callCost = Math.Max(1, ctx.Stake);
                var potOdds = callCost / (double)(ctx.Pot + callCost);
                var required = potOdds * 0.9 + 0.02;
                if (ctx.PlayerRaises >= 1) required += 0.06;

                winRateProb = winRate >= required
                    ? Math.Clamp(0.55 + (winRate - required) * 1.5, 0, 0.99)
                    : Math.Clamp(0.55 - (required - winRate) * 1.5, 0.02, 0.55);
            }

            // ---------- 3) 按档位混合 ----------
            var finalProb = AiTuning.BlendThree(tableProb, winRateProb, tierIndex);

            // ---------- 4) 粘性 ----------
            // 炸金花里玩家一直跟注不会改变注额，如果每轮都独立重掷同一个概率，
            // 6 个 AI 掷 8 轮几乎必然全灭（实测 8 轮后剩 0.4 个）。
            // 真实牌局里 AI 会「读人」：对面一直跟说明要么强牌要么在诈，
            // 陪下去的意愿会上升。这里让续跟次数线性抬升继续概率。
            var stickiness = Math.Min(0.55, seat.DecisionsUsed * 0.11);
            finalProb += stickiness;

            // 弱牌诈唬
            if (category <= 1 && _rng.NextDouble() < seat.BluffTendency) return true;

            return _rng.NextDouble() < Math.Clamp(finalProb, 0.03, 0.97);
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
        /// 这里按轮次缓存结论，同一局里每个 AI 只掷有限次。
        /// </summary>
        public int DecisionsUsed { get; set; }
    }
}
