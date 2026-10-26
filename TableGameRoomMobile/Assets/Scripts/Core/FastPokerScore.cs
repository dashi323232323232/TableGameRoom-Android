using System;

namespace TableGameRoom.Mobile.Core;

/// <summary>
/// 高性能 5/7 张牌评估器，结果与 <see cref="PokerScore.Of"/> / <see cref="PokerScore.Best"/>
/// 完全一致（同样的分值与牌型），但用计数与查表代替 LINQ 排序，速度快一个数量级以上，
/// 用于最强档 AI 的高次数蒙特卡洛模拟。
/// </summary>
public static class FastPokerScore
{
    private const long Base = 100_000_000_000L;

    // 100^(4-i)，与 PokerScore 的 Math.Pow(100, 4-i) 完全等价。
    private static readonly long[] KickerWeights = new[] { 100_000_000L, 1_000_000L, 10_000L, 100L, 1L };

    /// <summary>五张牌的牌型分值。</summary>
    public static long Of5(Card c0, Card c1, Card c2, Card c3, Card c4)
    {
        Span<int> counts = stackalloc int[15];
        counts[c0.Rank]++;
        counts[c1.Rank]++;
        counts[c2.Rank]++;
        counts[c3.Rank]++;
        counts[c4.Rank]++;

        var flush = c0.Suit == c1.Suit && c1.Suit == c2.Suit && c2.Suit == c3.Suit && c3.Suit == c4.Suit;

        Span<int> distinct = stackalloc int[5];
        var dn = 0;
        for (var r = 14; r >= 2; r--)
        {
            if (counts[r] > 0) distinct[dn++] = r;
        }

        var straight = dn == 5 && distinct[0] - distinct[4] == 4;
        // 轮子顺 10-J-Q-K-A 的特例：A-2-3-4-5
        var wheel = dn == 5 && distinct[0] == 14 && distinct[1] == 5 && distinct[2] == 4 && distinct[3] == 3 && distinct[4] == 2;
        if (!straight && wheel) straight = true;

        var straightHigh = wheel ? 5 : distinct[0];

        // 按 (数量降序, 点数降序) 排出分组，与原实现 ThenByDescending(g => g.Key) 一致。
        Span<int> groupRank = stackalloc int[5];
        Span<int> groupCount = stackalloc int[5];
        var gn = 0;
        for (var cnt = 4; cnt >= 1; cnt--)
        {
            for (var r = 14; r >= 2; r--)
            {
                if (counts[r] != cnt) continue;
                groupRank[gn] = r;
                groupCount[gn] = cnt;
                gn++;
            }
        }

        int category;
        if (straight && flush) category = 8;
        else if (groupCount[0] == 4) category = 7;
        else if (groupCount[0] == 3 && groupCount[1] == 2) category = 6;
        else if (flush) category = 5;
        else if (straight) category = 4;
        else if (groupCount[0] == 3) category = 3;
        else if (groupCount[0] == 2 && groupCount[1] == 2) category = 2;
        else if (groupCount[0] == 2) category = 1;
        else category = 0;

        var score = category * Base;
        switch (category)
        {
            case 8:
            case 4:
                score += straightHigh * KickerWeights[0];
                break;
            case 7:
            case 6:
                score += groupRank[0] * KickerWeights[0] + groupRank[1] * KickerWeights[1];
                break;
            case 5:
            case 0:
                // 同花 / 高牌：五张点数从大到小
                for (var i = 0; i < 5; i++) score += distinct[i] * KickerWeights[i];
                break;
            case 3:
                score += groupRank[0] * KickerWeights[0] + groupRank[1] * KickerWeights[1] + groupRank[2] * KickerWeights[2];
                break;
            case 2:
                score += groupRank[0] * KickerWeights[0] + groupRank[1] * KickerWeights[1] + groupRank[2] * KickerWeights[2];
                break;
            case 1:
                score += groupRank[0] * KickerWeights[0] + groupRank[1] * KickerWeights[1] + groupRank[2] * KickerWeights[2] + groupRank[3] * KickerWeights[3];
                break;
        }

        return score;
    }

    public static long Of(ReadOnlySpan<Card> five)
        => Of5(five[0], five[1], five[2], five[3], five[4]);

    /// <summary>从 5~7 张牌里取最佳五张，与 PokerScore.Best 等价。</summary>
    public static long Best(ReadOnlySpan<Card> cards)
    {
        var n = cards.Length;
        if (n < 5) return 0;
        if (n == 5) return Of5(cards[0], cards[1], cards[2], cards[3], cards[4]);

        var best = long.MinValue;
        for (var a = 0; a < n - 4; a++)
        for (var b = a + 1; b < n - 3; b++)
        for (var c = b + 1; c < n - 2; c++)
        for (var d = c + 1; d < n - 1; d++)
        for (var e = d + 1; e < n; e++)
        {
            var s = Of5(cards[a], cards[b], cards[c], cards[d], cards[e]);
            if (s > best) best = s;
        }

        return best;
    }
}
