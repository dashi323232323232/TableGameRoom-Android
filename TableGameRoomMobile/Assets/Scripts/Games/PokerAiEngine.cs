using System;
using System.Collections.Generic;
using System.Linq;
using TableGameRoom.Mobile.Core;

namespace TableGameRoom.Mobile.Games;

/// <summary>
/// 德州扑克 AI 决策引擎 —— 从桌面版 Poker.cs 抽出，纯计算、无 UI 依赖。
///
/// 设计要点（与桌面版完全一致）：
/// - 只用自己的底牌 + 公共牌 + 未知牌做蒙特卡洛，**不偷看**其他人底牌（信息对等）
/// - 模拟次数、计算误差、抗压、诈唬、风险容忍全部由 AiTuning 按档位平滑插值
/// - 因此档位 1→7 是连续增强，不存在断档跳变
/// </summary>
public sealed class PokerAiEngine
{
    private readonly Random _rng;

    public PokerAiEngine(Random rng) => _rng = rng;

    /// <summary>AI 手牌的真实胜率（蒙特卡洛模拟）。</summary>
    /// <param name="hole">AI 的两张底牌</param>
    /// <param name="community">已发出的公共牌（0–5 张）</param>
    /// <param name="activeOpponents">仍在局中的对手数</param>
    /// <param name="trials">模拟次数（由档位决定）</param>
    public double Equity(
        IReadOnlyList<Card> hole,
        IReadOnlyList<Card> community,
        int activeOpponents,
        int trials)
    {
        if (hole.Count < 2) return 0.5;

        var boardCount = 5 - community.Count;
        var needed = activeOpponents * 2 + boardCount;

        var known = new HashSet<Card>(hole);
        foreach (var c in community) known.Add(c);

        var fullDeck = new List<Card>(52);
        foreach (var suit in Card.Suits)
        {
            for (var rank = 2; rank <= 14; rank++)
            {
                var card = new Card(rank, suit);
                if (!known.Contains(card)) fullDeck.Add(card);
            }
        }
        if (fullDeck.Count < needed) return 0.5;

        trials = Math.Clamp(trials, 20, 2000);

        var completed = 0;
        var equity = 0.0;

        // 复用缓冲区，避免每轮分配
        var pool = new Card[fullDeck.Count];
        var boardBuf = new Card[5];
        var oppBuf = new Card[32];

        for (var t = 0; t < trials; t++)
        {
            if (fullDeck.Count < needed) break;

            var poolCount = 0;
            for (var i = 0; i < fullDeck.Count; i++) pool[poolCount++] = fullDeck[i];

            // 抽取对手底牌
            var op = 0;
            for (var oi = 0; oi < activeOpponents; oi++)
            {
                for (var k = 0; k < 2; k++)
                {
                    var idx = _rng.Next(poolCount);
                    oppBuf[op++] = pool[idx];
                    pool[idx] = pool[--poolCount];
                }
            }

            // 补齐公共牌
            for (var k = 0; k < community.Count; k++) boardBuf[k] = community[k];
            for (var k = 0; k < boardCount; k++)
            {
                var idx = _rng.Next(poolCount);
                boardBuf[community.Count + k] = pool[idx];
                pool[idx] = pool[--poolCount];
            }

            var totalBoard = community.Count + boardCount;

            var mine = new Card[7];
            mine[0] = hole[0];
            mine[1] = hole[1];
            for (var k = 0; k < totalBoard; k++) mine[2 + k] = boardBuf[k];
            var aiScore = FastPokerScore.Best(mine.AsSpan(0, 2 + totalBoard));

            long bestOpp = long.MinValue;
            for (var oi = 0; oi < activeOpponents; oi++)
            {
                var theirs = new Card[7];
                theirs[0] = oppBuf[oi * 2];
                theirs[1] = oppBuf[oi * 2 + 1];
                for (var k = 0; k < totalBoard; k++) theirs[2 + k] = boardBuf[k];
                var s = FastPokerScore.Best(theirs.AsSpan(0, 2 + totalBoard));
                if (s > bestOpp) bestOpp = s;
            }

            if (activeOpponents == 0)
                bestOpp = FastPokerScore.Best(boardBuf.AsSpan(0, totalBoard));

            equity += aiScore > bestOpp ? 1 : aiScore == bestOpp ? 0.5 : 0;
            completed++;
        }

        return completed == 0 ? 0.5 : equity / completed;
    }

    /// <summary>AI 是否继续跟注。</summary>
    public bool Continues(PokerAiContext ctx, PokerAiProfile seat)
    {
        var tierIndex = TierIndex(seat.EffectiveTier);
        var equity = Equity(seat.Hole, ctx.Community, ctx.ActiveOpponents, AiTuning.Trials(tierIndex));

        var callCost = Math.Max(1, ctx.Stake);
        var potOdds = callCost / (double)(ctx.TablePot + callCost);
        var threshold = AiTuning.PokerCallThreshold(tierIndex, potOdds);

        // 风险容忍微调
        threshold -= (seat.RiskTolerance - 0.5) * 0.06;

        // 施压弃牌：档位越低越容易被玩家加注吓退（连续变化）
        if (!ctx.PlayerFolded)
        {
            var strength = AiTuning.Strength(tierIndex);
            double pressure = 0;
            if (ctx.LastPlayerAction == 2) pressure += AiTuning.Lerp(0.22, 0.05, strength);
            if (ctx.PlayerRaises >= 2) pressure += AiTuning.Lerp(0.10, 0.04, strength);
            pressure *= 1.10 - seat.PressureResistance * 0.55;
            if (_rng.NextDouble() < Math.Clamp(pressure, 0, 0.65)) return false;
        }

        // 计算误差随档位连续减小（1 档 0.20 → 7 档 0.00）
        var noise = (_rng.NextDouble() - 0.5) * seat.CalculationNoise;
        return equity + noise >= threshold;
    }

    /// <summary>AI 是否加注。</summary>
    public bool Raises(PokerAiContext ctx, PokerAiProfile seat)
    {
        var tierIndex = TierIndex(seat.EffectiveTier);
        var strength = AiTuning.Strength(tierIndex);

        var equity = Equity(seat.Hole, ctx.Community, ctx.ActiveOpponents,
                            Math.Max(20, AiTuning.Trials(tierIndex) / 2));

        // 价值加注门槛随强度下降
        var valueThreshold = AiTuning.Lerp(0.58, 0.62, strength);
        var valueScale = AiTuning.Lerp(0.85, 1.6, strength);
        var valueCap = AiTuning.Lerp(0.30, 0.55, strength);
        var valueRaise = Math.Clamp((equity - valueThreshold) * valueScale, 0, valueCap);

        // 强牌在翻牌前更愿意做大底池（高档生效）
        if (ctx.Stage == 0 && equity >= 0.75)
            valueRaise = Math.Max(valueRaise, 0.35 * strength);

        // 诈唬
        var bluffBonus = ctx.LiveOpponents <= 2 ? 0.05 * strength : 0.0;
        var bluffProb = seat.BluffTendency * (1.2 - ctx.Stage * 0.15) + bluffBonus;
        var bluff = _rng.NextDouble() < Math.Clamp(bluffProb, 0.01, 0.14);

        return bluff || _rng.NextDouble() < valueRaise;
    }

    private static int TierIndex(double effectiveTier) =>
        (int)Math.Round(Math.Clamp(effectiveTier - 1, 0, 6));
}

/// <summary>AI 决策所需的桌面上下文。</summary>
public readonly struct PokerAiContext
{
    public readonly IReadOnlyList<Card> Community;
    public readonly int ActiveOpponents;
    public readonly int LiveOpponents;
    public readonly int Stake;
    public readonly int TablePot;
    public readonly int Stage;
    public readonly int LastPlayerAction;
    public readonly int PlayerRaises;
    public readonly bool PlayerFolded;

    public PokerAiContext(
        IReadOnlyList<Card> community,
        int activeOpponents,
        int liveOpponents,
        int stake,
        int tablePot,
        int stage,
        int lastPlayerAction,
        int playerRaises,
        bool playerFolded)
    {
        Community = community;
        ActiveOpponents = activeOpponents;
        LiveOpponents = liveOpponents;
        Stake = stake;
        TablePot = tablePot;
        Stage = stage;
        LastPlayerAction = lastPlayerAction;
        PlayerRaises = playerRaises;
        PlayerFolded = playerFolded;
    }
}

/// <summary>单个 AI 座位的性格参数（由档位决定）。</summary>
public sealed class PokerAiProfile
{
    public List<Card> Hole { get; } = new List<Card>();
    public double EffectiveTier { get; set; } = 1;
    public double PressureResistance { get; set; }
    public double RiskTolerance { get; set; }
    public double CalculationNoise { get; set; }
    public double BluffTendency { get; set; }
}
