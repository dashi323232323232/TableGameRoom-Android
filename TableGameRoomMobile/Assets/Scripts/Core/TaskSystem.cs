using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TableGameRoom.Mobile.Core;

/// <summary>任务类型。</summary>
public enum TaskKind
{
    PlayGames,      // 玩指定游戏 N 局
    WinRounds,      // 赢 N 局（任意游戏）
    WinGames,       // 用指定游戏赢 N 局
    LoseRounds,     // 输 N 局
    NetProfit,      // 累计净赢 ≥ X
    NetLoss,        // 累计净输 ≥ X（体验输的感觉）
    PlayMode,       // 在无限模式玩 N 局
    WinBig          // 单局净赢 ≥ X
}

/// <summary>一个进行中的任务。</summary>
public sealed class GameTask
{
    public string Id { get; set; } = "";
    public TaskKind Kind { get; set; }
    public string GameKey { get; set; } = "";     // 限定游戏，空=任意
    public string Title { get; set; } = "";       // 显示标题
    public string Detail { get; set; } = "";      // 详细说明
    public int Target { get; set; }               // 目标值
    public int Progress { get; set; }             // 当前进度
    public int Reward { get; set; }               // 完成奖励金额
    public bool Completed { get; set; }
    public bool Claimed { get; set; }
}

/// <summary>
/// 随机任务系统：给玩家生成一个有挑战的小目标，完成可领取奖金。
/// 也充当「破产保护」——余额不足时可以靠做任务重新站起来。
/// </summary>
public static class TaskSystem
{
    private static readonly Random Rng = new();

    private static readonly (string Key, string Name)[] Games =
    [
        ("roulette", "欧洲轮盘"),
        ("blackjack", "21 点"),
        ("poker", "德州扑克"),
        ("three", "炸金花"),
        ("liar", "骗子扑克")
    ];

    public static string TaskPath(string mode) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TableGameRoom",
        mode == "存档" ? "tasks.json" : "custom-tasks.json");

    public static List<GameTask> Load(string mode)
    {
        try
        {
            var path = TaskPath(mode);
            if (!File.Exists(path)) return [];
            return JsonSerializer.Deserialize<List<GameTask>>(File.ReadAllText(path)) ?? [];
        }
        catch { return []; }
    }

    public static void Save(string mode, List<GameTask> tasks)
    {
        try
        {
            var path = TaskPath(mode);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(tasks));
        }
        catch { }
    }

    /// <summary>生成一个新任务（尽量与已有任务不重复）。</summary>
    public static GameTask Generate(int tierIndex, IReadOnlyCollection<GameTask> existing)
    {
        var usedTitles = existing.Where(t => !t.Claimed).Select(t => t.Title).ToHashSet();

        for (var attempt = 0; attempt < 40; attempt++)
        {
            var task = GenerateOnce(tierIndex);
            if (!usedTitles.Contains(task.Title)) return task;
        }
        return GenerateOnce(tierIndex);
    }

    private static GameTask GenerateOnce(int tierIndex)
    {
        var game = Games[Rng.Next(Games.Length)];
        // 目标规模随档位上升（档位越高，玩的局数/金额越大）
        var scale = 1 + tierIndex / 2;
        var kind = (TaskKind)Rng.Next(8);
        var task = new GameTask { Id = Guid.NewGuid().ToString("N"), Kind = kind, GameKey = game.Key };

        switch (kind)
        {
            case TaskKind.PlayGames:
                task.Target = Rng.Next(2, 4) + scale;
                task.Title = $"在{game.Name}玩 {task.Target} 局";
                task.Detail = $"任意输赢都算，累计完成 {task.Target} 局即可。";
                task.Reward = Reward(tierIndex, 0.7);
                break;

            case TaskKind.WinRounds:
                task.Target = Rng.Next(1, 3) + scale / 2;
                task.Title = $"赢下 {task.Target} 局";
                task.Detail = "任意游戏，净赢的局都计入。";
                task.Reward = Reward(tierIndex, 0.9);
                break;

            case TaskKind.WinGames:
                task.Target = Rng.Next(1, 3);
                task.Title = $"用{game.Name}赢 {task.Target} 局";
                task.Detail = $"只有{game.Name}的胜利才计入。";
                task.Reward = Reward(tierIndex, 0.8);
                break;

            case TaskKind.LoseRounds:
                task.Target = Rng.Next(1, 3);
                task.Title = $"坦然接受 {task.Target} 次失利";
                task.Detail = "输掉任意局都算。胜负乃兵家常事。";
                task.Reward = Reward(tierIndex, 0.6);
                break;

            case TaskKind.NetProfit:
                task.Target = Math.Max(20, (int)(Reward(tierIndex, 1.5) / 10.0) * 10);
                task.Title = $"累计净赢 ${task.Target:N0}";
                task.Detail = "从接受任务起，所有对局的净收益累加。";
                task.Reward = Math.Max(20, (int)(task.Target * 0.6 / 10.0) * 10);
                break;

            case TaskKind.NetLoss:
                task.Target = Math.Max(10, (int)(Reward(tierIndex, 0.8) / 10.0) * 10);
                task.Title = $"累计净输 ${task.Target:N0}";
                task.Detail = "体验逆风局，输够这个数就算完成。";
                task.Reward = Math.Max(20, (int)(task.Target * 0.5 / 10.0) * 10);
                break;

            case TaskKind.PlayMode:
                task.Target = Rng.Next(2, 4) + scale;
                task.Title = $"在无限模式玩 {task.Target} 局";
                task.Detail = "无限模式（智能模式）下任意游戏。";
                task.Reward = Reward(tierIndex, 0.8);
                break;

            default:
                task.Target = Math.Max(20, (int)(Reward(tierIndex, 1.2) / 10.0) * 10);
                task.Title = $"单局净赢 ${task.Target:N0}";
                task.Detail = "任意游戏，单局净收益达到目标即完成。";
                task.Reward = Math.Max(20, (int)(task.Target * 0.6 / 10.0) * 10);
                break;
        }

        return task;
    }

    /// <summary>
    /// 奖励金额：约为该档位一次典型下注的 1~3 倍，足够帮玩家翻身，
    /// 但不会高于「认真打一局」的期望收益，避免做任务比赌博划算。
    /// </summary>
    private static int Reward(int tierIndex, double factor)
    {
        var tier = BetTiers[tierIndex];
        // 以档位下限为基准：休闲档 $10 → 奖励 $30~$90
        var baseValue = Math.Max(20, tier.Min);
        var raw = baseValue * factor;
        return (int)Math.Round(raw / 10.0) * 10;
    }

    private static readonly (int Min, int? Max, string Name)[] BetTiers =
    [
        (10, 500, "休闲"),
        (50, 1_000, "轻松"),
        (100, 5_000, "入门"),
        (500, 10_000, "普通"),
        (1_000, 50_000, "进阶"),
        (5_000, 100_000, "强手"),
        (10_000, null, "极强")
    ];

    /// <summary>
    /// 结算一局后更新任务进度。
    /// </summary>
    /// <returns>本次更新后新完成的任务（供 UI 提示）。</returns>
    public static List<GameTask> OnRoundSettled(
        List<GameTask> tasks, string gameKey, int net, string mode)
    {
        var justCompleted = new List<GameTask>();
        foreach (var t in tasks)
        {
            if (t.Completed) continue;
            switch (t.Kind)
            {
                case TaskKind.PlayGames:
                    if (t.GameKey == gameKey) t.Progress++;
                    break;
                case TaskKind.WinRounds:
                    if (net > 0) t.Progress++;
                    break;
                case TaskKind.WinGames:
                    if (t.GameKey == gameKey && net > 0) t.Progress++;
                    break;
                case TaskKind.LoseRounds:
                    if (net < 0) t.Progress++;
                    break;
                case TaskKind.NetProfit:
                    if (net > 0) t.Progress += net;
                    break;
                case TaskKind.NetLoss:
                    if (net < 0) t.Progress += -net;
                    break;
                case TaskKind.PlayMode:
                    if (mode == "智能") t.Progress++;
                    break;
                case TaskKind.WinBig:
                    if (net > t.Progress) t.Progress = net;
                    break;
            }

            if (t.Progress >= t.Target)
            {
                t.Progress = t.Target;
                t.Completed = true;
                justCompleted.Add(t);
            }
        }
        return justCompleted;
    }
}
