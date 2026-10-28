using System;

namespace TableGameRoom.Mobile.Core;

/// <summary>
/// 人机强度调参器：把「档位 1–7」平滑映射到各项 AI 参数。
/// 设计原则：第 1 档 = 强度 1，第 7 档 = 强度 7，中间均匀过渡，
/// 不存在任何「第 7 档突然换一套算法」的断档跳变。
/// </summary>
public static class AiTuning
{
    /// <summary>档位索引 0..6 → 归一化强度 0..1（1 档=0，7 档=1）。</summary>
    public static double Strength(int tierIndex) => Math.Clamp(tierIndex / 6.0, 0.0, 1.0);

    /// <summary>线性插值。</summary>
    public static double Lerp(double from, double to, double t) => from + (to - from) * Math.Clamp(t, 0.0, 1.0);

    /// <summary>
    /// 蒙特卡洛模拟次数：1 档 40 次 → 7 档 1600 次。
    /// 用二次曲线，低档保持轻快，高档才显著加大算力。
    /// </summary>
    public static int Trials(int tierIndex)
    {
        var s = Strength(tierIndex);
        return (int)Math.Round(Lerp(40, 1600, s * s));
    }

    /// <summary>计算误差：1 档 0.20 → 7 档 0.00（越高越准）。</summary>
    public static double CalculationNoise(int tierIndex) => Lerp(0.20, 0.0, Strength(tierIndex));

    /// <summary>抗压能力：1 档 0.22 → 7 档 1.00（越高越不被加注吓退）。</summary>
    public static double PressureResistance(int tierIndex) => Lerp(0.22, 1.0, Strength(tierIndex));

    /// <summary>诈唬倾向：1 档 0.10 → 7 档 0.02（越高越少乱诈唬）。</summary>
    public static double BluffTendency(int tierIndex) => Lerp(0.10, 0.02, Strength(tierIndex));

    /// <summary>风险容忍度：1 档 0.65 → 7 档 0.50（低档更莽，高档更稳）。</summary>
    public static double RiskTolerance(int tierIndex) => Lerp(0.65, 0.50, Strength(tierIndex));

    /// <summary>
    /// 德州扑克跟注所需胜率。
    /// 低档用固定阈值（容易被吓退），高档用真实底池赔率（跟注是否划算）。
    /// 中间档在两者之间线性混合，所以强度是连续变化的。
    /// </summary>
    public static double PokerCallThreshold(int tierIndex, double potOdds)
    {
        var s = Strength(tierIndex);
        var weakThreshold = Lerp(0.42, 0.32, s);              // 低档：固定偏保守阈值
        var smartThreshold = Math.Max(potOdds + 0.02, 0.25);  // 高档：底池赔率（至少 25% 胜率）
        return Lerp(weakThreshold, smartThreshold, s);
    }

    /// <summary>炸金花：把「查表概率」与「真实胜率」按强度混合。</summary>
    public static double BlendThree(double tableProb, double winRate, int tierIndex)
        => Lerp(tableProb, winRate, Strength(tierIndex));

    /// <summary>骗子扑克：把「启发式质疑率」与「贝叶斯推断」按强度混合。</summary>
    public static double BlendLiar(double heuristicChance, double bayesChance, int tierIndex)
        => Lerp(heuristicChance, bayesChance, Strength(tierIndex));
}
