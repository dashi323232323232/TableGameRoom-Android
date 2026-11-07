using TableGameRoom.Mobile.Core;

namespace TableGameRoom.Mobile.UI
{
    /// <summary>
    /// 表情决策器 —— 让 AI 的表情跟着真实牌局状态变化。
    ///
    /// 设计原则：
    ///   · 玩家自己**没有**表情（你能控制自己的脸，不需要系统替你演）
    ///   · AI 的表情由「牌力 + 压力 + 刚发生的事」共同决定
    ///   · 同一局面下不同档位的 AI 表现不同（低档情绪化，高档面无表情）
    ///
    /// 表情不是随机的，而是真实反映 AI 的处境 ——
    /// 拿到豹子会得意，被加注会紧张，弃牌会失落。
    /// </summary>
    public static class MoodEngine
    {
        /// <summary>
        /// 根据牌局状态算出一个 AI 该摆什么表情。
        /// </summary>
        /// <param name="category">牌型等级 0单张 1对子 2同花 3顺子 4豹子 5同花顺</param>
        /// <param name="tierStrength">档位强度 0..1（越高越沉得住气）</param>
        /// <param name="pressure">当前压力 0..1（注额 / 被加注 / 轮次）</param>
        /// <param name="folded">是否已弃牌</param>
        /// <param name="justWon">刚比赢</param>
        /// <param name="justLost">刚比输</param>
        /// <param name="looked">是否看过牌</param>
        /// <param name="rng">随机源</param>
        public static AvatarVisual.Mood ForThree(
            int category,
            double tierStrength,
            double pressure,
            bool folded,
            bool justWon,
            bool justLost,
            bool looked,
            System.Random rng)
        {
            // ---------- 已经弃牌：失落，低档更明显 ----------
            if (folded) return AvatarVisual.Mood.Sad;

            // ---------- 比牌结果优先 ----------
            if (justWon) return AvatarVisual.Mood.Happy;
            if (justLost) return AvatarVisual.Mood.Sad;

            // ---------- 强牌：得意 ----------
            if (category >= 4) return AvatarVisual.Mood.Happy;   // 豹子 / 同花顺
            if (category == 3 && pressure < 0.7) return AvatarVisual.Mood.Happy;  // 顺子

            // ---------- 压力大 + 弱牌：紧张/恼火 ----------
            if (pressure >= 0.7)
            {
                // 高档能压住情绪，低档容易挂脸
                if (rng.NextDouble() > tierStrength * 0.7)
                    return AvatarVisual.Mood.Angry;
                return AvatarVisual.Mood.Neutral;
            }

            // ---------- 中等压力 + 中等牌：微微紧张 ----------
            if (pressure >= 0.4 && category <= 1)
            {
                if (rng.NextDouble() > tierStrength * 0.6)
                    return AvatarVisual.Mood.Sad;
            }

            // ---------- 闷牌：看不出来，平静 ----------
            if (!looked) return AvatarVisual.Mood.Neutral;

            // ---------- 其余情况：平静，偶尔得意 ----------
            if (category >= 2 && rng.NextDouble() < 0.35)
                return AvatarVisual.Mood.Happy;

            return AvatarVisual.Mood.Neutral;
        }

        /// <summary>
        /// 德州扑克版：按牌型强度和底池赔率决定。
        /// </summary>
        /// <param name="category">牌型 0高牌 .. 8同花顺</param>
        /// <param name="equity">胜率 0..1</param>
        /// <param name="tierStrength">档位强度</param>
        /// <param name="folded">已弃牌</param>
        /// <param name="justWon">刚赢</param>
        /// <param name="justLost">刚输</param>
        /// <param name="rng">随机源</param>
        public static AvatarVisual.Mood ForPoker(
            int category,
            double equity,
            double tierStrength,
            bool folded,
            bool justWon,
            bool justLost,
            System.Random rng)
        {
            if (folded) return AvatarVisual.Mood.Sad;
            if (justWon) return AvatarVisual.Mood.Happy;
            if (justLost) return AvatarVisual.Mood.Sad;

            // 强牌得意
            if (category >= 5 || equity >= 0.75) return AvatarVisual.Mood.Happy;  // 同花及以上

            // 胜率很低：沮丧
            if (equity <= 0.25)
            {
                if (rng.NextDouble() > tierStrength * 0.6)
                    return AvatarVisual.Mood.Sad;
            }

            // 中等牌力：平静
            return AvatarVisual.Mood.Neutral;
        }
    }
}
