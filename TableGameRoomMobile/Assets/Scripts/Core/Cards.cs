using System.Collections.Generic;
using System.Linq;
using System;

namespace TableGameRoom.Mobile.Core;

/// <summary>一张扑克牌。Rank 使用 2..14，其中 14=A、13=K、12=Q、11=J。</summary>
public sealed class Card : IEquatable<Card>
{
    public int Rank { get; }
    public char Suit { get; }

    public Card(int rank, char suit) { Rank = rank; Suit = suit; }

    public static readonly char[] Suits = new[] { '♠', '♥', '♦', '♣' };

    public bool Equals(Card? other) =>
        other is not null && Rank == other.Rank && Suit == other.Suit;

    public override bool Equals(object? obj) => Equals(obj as Card);

    public override int GetHashCode() => Rank * 31 + Suit;

    public string RankText => Rank switch
    {
        14 => "A",
        13 => "K",
        12 => "Q",
        11 => "J",
        _ => Rank.ToString()
    };

    public bool IsRed => Suit is '♥' or '♦';

    public override string ToString() => RankText + Suit;

    public static bool operator ==(Card? a, Card? b) =>
        a is null ? b is null : a.Equals(b);

    public static bool operator !=(Card? a, Card? b) => !(a == b);
}

public static class Deck
{
    public static List<Card> New(Random rng)
    {
        var cards = new List<Card>(52);
        foreach (var suit in Card.Suits)
        {
            for (var v = 2; v <= 14; v++) cards.Add(new Card(v, suit));
        }

        for (var i = cards.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (cards[i], cards[j]) = (cards[j], cards[i]);
        }

        return cards;
    }
}

/// <summary>五张牌牌型评分，分数越大牌型越强。</summary>
public static class PokerScore
{
    private static readonly string[] Names =
    new[] { 
        "高牌", "对子", "两对", "三条", "顺子", "同花", "葫芦", "四条", "同花顺"
     };

    private const long Base = 100_000_000_000L;

    public static int Category(long score) => (int)(score / Base);

    public static string Name(long score) => Names[Category(score)];

    public static long Of(IReadOnlyList<Card> five)
    {
        var ranks = five.Select(c => c.Rank).OrderByDescending(x => x).ToArray();
        var flush = five.All(c => c.Suit == five[0].Suit);
        var distinct = ranks.Distinct().ToArray();
        var straight = distinct.Length == 5 && distinct[0] - distinct[4] == 4;
        if (!straight && ranks.SequenceEqual(new[] { 14, 5, 4, 3, 2 })) straight = true;

        var groups = ranks.GroupBy(x => x)
                          .OrderByDescending(g => g.Count())
                          .ThenByDescending(g => g.Key)
                          .ToArray();

        int category;
        int[] tie;

        if (straight && flush)
        {
            category = 8;
            tie = new[] { straightHigh(ranks) };
        }
        else if (groups[0].Count() == 4)
        {
            category = 7;
            tie = new[] { groups[0].Key, groups[1].Key };
        }
        else if (groups[0].Count() == 3 && groups[1].Count() == 2)
        {
            category = 6;
            tie = new[] { groups[0].Key, groups[1].Key };
        }
        else if (flush)
        {
            category = 5;
            tie = ranks;
        }
        else if (straight)
        {
            category = 4;
            tie = new[] { straightHigh(ranks) };
        }
        else if (groups[0].Count() == 3)
        {
            category = 3;
            tie = new[] { groups[0].Key, groups[1].Key, groups[2].Key };
        }
        else if (groups[0].Count() == 2 && groups[1].Count() == 2)
        {
            category = 2;
            tie = new[] { groups[0].Key, groups[1].Key, groups[2].Key };
        }
        else if (groups[0].Count() == 2)
        {
            category = 1;
            tie = new[] { groups[0].Key, groups[1].Key, groups[2].Key, groups[3].Key };
        }
        else
        {
            category = 0;
            tie = ranks;
        }

        long score = category * Base;
        for (var i = 0; i < 5; i++)
        {
            var v = i < tie.Length ? tie[i] : 0;
            score += v * (long)Math.Pow(100, 4 - i);
        }

        return score;

        static int straightHigh(int[] r) => r[0] == 14 && r[4] == 2 ? 5 : r[0];
    }

    /// <summary>从 5~7 张牌里选出最好的五张。</summary>
    public static long Best(IReadOnlyList<Card> cards)
    {
        if (cards.Count < 5) return 0;
        long best = -1;
        var n = cards.Count;
        for (var a = 0; a < n - 4; a++)
            for (var b = a + 1; b < n - 3; b++)
                for (var c = b + 1; c < n - 2; c++)
                    for (var d = c + 1; d < n - 1; d++)
                        for (var e = d + 1; e < n; e++)
                        {
                            long s = Of(new[] { cards[a], cards[b], cards[c], cards[d], cards[e] });
                            if (s > best) best = s;
                        }

        return best;
    }
}

/// <summary>炸金花三张牌牌型评分。</summary>
public static class ThreeScore
{
    private static readonly string[] Names =
    new[] { 
        "单张", "对子", "同花", "顺子", "豹子", "同花顺"
     };

    public static string Name(long score) => Names[(int)(score / 1_000_000L)];

    public static long Of(IReadOnlyList<Card> three)
    {
        var ranks = three.Select(c => c.Rank).OrderByDescending(x => x).ToArray();
        var flush = three.All(c => c.Suit == three[0].Suit);
        var distinct = ranks.Distinct().ToArray();
        var straight = distinct.Length == 3 && distinct[0] - distinct[2] == 2;
        if (!straight && ranks.SequenceEqual(new[] { 14, 3, 2 })) straight = true;

        int category;
        int[] tie;

        if (flush && straight)
        {
            category = 5;
            tie = new[] { straightHigh(ranks) };
        }
        else if (distinct.Length == 1)
        {
            category = 4;
            tie = new[] { ranks[0] };
        }
        else if (straight)
        {
            category = 3;
            tie = new[] { straightHigh(ranks) };
        }
        else if (flush)
        {
            category = 2;
            tie = ranks;
        }
        else if (distinct.Length == 2)
        {
            category = 1;
            var pair = ranks.GroupBy(x => x).OrderByDescending(g => g.Count()).First().Key;
            var kicker = ranks.First(x => x != pair);
            tie = new[] { pair, kicker };
        }
        else
        {
            category = 0;
            tie = ranks;
        }

        long score = category * 1_000_000L;
        for (var i = 0; i < 3; i++) score += (i < tie.Length ? tie[i] : 0) * (long)Math.Pow(100, 2 - i);
        return score;

        static int straightHigh(int[] r) => r[0] == 14 && r[2] == 2 ? 3 : r[0];
    }
}

/// <summary>结构化历史记录：每局一次，带时间戳与唯一id。</summary>
public sealed class GameHistoryEntry
{
    public string Id { get; }
    public DateTime Timestamp { get; }
    public string GameKey { get; }
    public string GameName { get; }
    public int Stake { get; }
    public int Net { get; }
    public string Summary { get; }

    public GameHistoryEntry(string Id, DateTime Timestamp, string GameKey, string GameName, int Stake, int Net, string Summary)
    {
        Id = Id;
        Timestamp = Timestamp;
        GameKey = GameKey;
        GameName = GameName;
        Stake = Stake;
        Net = Net;
        Summary = Summary;
    }
}

public static class ModeDisplay
{
    public static string ToDisplay(string internalMode) => internalMode == "智能" ? "无限模式" : "普通模式";
    public static string FileName(string internalMode) => internalMode == "存档" ? "progress.json" : "custom-progress.json";
    public static DateTime HistoryCutoff => DateTime.Today.AddMonths(-3).Date;
    public static bool IsInWindow(DateTime ts) => ts.Date >= HistoryCutoff;
    public static string GameName(string key) => key switch
    {
        "poker" => "德州扑克",
        "three" => "炸金花",
        "blackjack" => "21点",
        "roulette" => "欧洲轮盘",
        "liar" => "骗子扑克",
        _ => key
    };
}
