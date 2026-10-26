using System;

namespace TableGameRoom.Mobile.Core;

/// <summary>
/// 人机思考时长计算：模拟真人玩家的压力感。
/// - 对局越靠后（阶段/轮次越大）思考越久；
/// - 下注金额越大压力越大，思考越久；小注几乎立刻出牌；
/// - 每次叠加随机波动，避免节奏呆板。
/// 玩家的操作不经过这里，始终即时响应。
/// </summary>
public static class ThinkTiming
{
    private static readonly Random Rng = new();

    /// <summary>
    /// 计算一次思考的毫秒数。
    /// </summary>
    /// <param name="stage">对局进度：0=开局，越大越靠后（建议 0..8）。</param>
    /// <param name="stake">本局下注额，用于衡量压力。</param>
    /// <param name="tierMax">当前档位单次下注上限，用于归一化金额；无上限时传 null。</param>
    public static int Delay(int stage, int stake, int? tierMax)
    {
        // 进度系数 0..1
        var progress = Math.Clamp(stage / 8.0, 0, 1);

        // 压力系数：以「下注额 / 档位上限」为主；无上限档用绝对金额的对数尺度。
        double pressure;
        if (tierMax is > 0)
        {
            pressure = Math.Clamp((double)stake / tierMax.Value, 0, 1);
        }
        else
        {
            // 极强档：$10,000 起，10k→0.35，100k→0.65，1M→0.9
            pressure = stake <= 0 ? 0 : Math.Clamp(Math.Log10(stake) / 6.0, 0, 1);
        }

        // 综合压力：进度占 45%，金额占 55%
        var weight = progress * 0.45 + pressure * 0.55;

        // 基础时长：前期小注 ~0.9s，后期大注最高 ~20s
        var maxMs = 900 + weight * 19_100;
        // 低压力时更接近“秒出”
        var baseMs = 700 + weight * weight * 8_000;

        var span = Math.Min(baseMs, maxMs);

        // 随机波动 ±30%
        var jitter = span * (Rng.NextDouble() * 0.6 - 0.3);
        var result = span + jitter;

        return (int)Math.Clamp(result, 450, 20_000);
    }

    /// <summary>德州扑克各街对应的压力阶段。</summary>
    public static int PokerStage(int stage) => stage switch
    {
        0 => 1,   // 翻牌前
        1 => 3,   // 翻牌
        2 => 5,   // 转牌
        3 => 7,   // 河牌
        _ => 8
    };
}
