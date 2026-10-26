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

        /// <summary>AI 是否继续跟注。</summary>
        public bool Continues(ThreeAiContext ctx, ThreeAiProfile seat)
        {
            var tierIndex = TierIndex(seat.EffectiveTier);
            var strength = AiTuning.Strength(tierIndex);

            // 牌力归一化到 0..1（ThreeScore 返回整数分，用档位阈值映射）
            var handPower = Normalize(ThreeScore.Of(seat.Cards));

            var callCost = Math.Max(1, ctx.Stake);
            var potOdds = callCost / (double)(ctx.Pot + callCost);
            var threshold = AiTuning.Lerp(0.42, 0.52, strength) + potOdds * AiTuning.Lerp(0.5, 0.25, strength);

            // 低档更容易被加注吓退
            if (ctx.PlayerRaises >= 2)
            {
                var pressure = AiTuning.Lerp(0.28, 0.06, strength) * (1.1 - seat.PressureResistance * 0.6);
                if (_rng.NextDouble() < Math.Clamp(pressure, 0, 0.7)) return false;
            }

            var noise = (_rng.NextDouble() - 0.5) * seat.CalculationNoise;
            return handPower + noise >= threshold;
        }

        /// <summary>AI 是否加注。</summary>
        public bool Raises(ThreeAiContext ctx, ThreeAiProfile seat)
        {
            var tierIndex = TierIndex(seat.EffectiveTier);
            var strength = AiTuning.Strength(tierIndex);
            var handPower = Normalize(ThreeScore.Of(seat.Cards));

            var valueThreshold = AiTuning.Lerp(0.68, 0.58, strength);
            var valueRaise = Math.Clamp((handPower - valueThreshold) * AiTuning.Lerp(0.8, 1.5, strength), 0, 0.6);

            var bluffProb = seat.BluffTendency * (ctx.PlayerFolded ? 1.4 : 1.0);
            var bluff = _rng.NextDouble() < Math.Clamp(bluffProb, 0.01, 0.16);

            return bluff || _rng.NextDouble() < valueRaise;
        }

        /// <summary>AI 是否比牌。</summary>
        public bool Compares(ThreeAiContext ctx, ThreeAiProfile seat)
        {
            var tierIndex = TierIndex(seat.EffectiveTier);
            var strength = AiTuning.Strength(tierIndex);
            var handPower = Normalize(ThreeScore.Of(seat.Cards));

            // 强牌更愿意比，弱牌偶尔偷鸡
            var threshold = AiTuning.Lerp(0.72, 0.62, strength);
            if (handPower >= threshold) return true;
            return _rng.NextDouble() < seat.BluffTendency * 0.5;
        }

        /// <summary>ThreeScore 分值 → 0..1 牌力。用于和阈值比较。</summary>
        private static double Normalize(long score)
        {
            // ThreeScore 结构：category * 1_000_000 + tie[0]*10000 + tie[1]*100 + tie[2]
            // category: 0单张 1对子 2同花 3顺子 4豹子 5同花顺
            if (score <= 0) return 0;

            var category = (int)(score / 1_000_000L);
            var remainder = score % 1_000_000L;

            // 牌型权重占大头（0.12 / 档），tie 部分做细微区分
            var basePower = category * 0.15;
            var tiePower = remainder / 1_000_000.0 * 0.14;
            return Math.Clamp(basePower + tiePower, 0, 1);
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

        public ThreeAiContext(int stake, int pot, int playerRaises, bool playerFolded)
        {
            Stake = stake;
            Pot = pot;
            PlayerRaises = playerRaises;
            PlayerFolded = playerFolded;
        }
    }

    public sealed class ThreeAiProfile
    {
        public List<Card> Cards { get; } = new List<Card>();
        public double EffectiveTier { get; set; } = 1;
        public double PressureResistance { get; set; }
        public double CalculationNoise { get; set; }
        public double BluffTendency { get; set; }
    }
}
