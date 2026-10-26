using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UnityEngine;

namespace TableGameRoom.Mobile.Core
{
    /// <summary>
    /// 玩家存档。与桌面版字段一一对应，存档路径改为 Unity 的 persistentDataPath。
    /// 桌面版路径：%LOCALAPPDATA%\TableGameRoom\progress.json
    /// 移动版路径：Application.persistentDataPath/progress.json
    /// </summary>
    [Serializable]
    public sealed class Progress
    {
        public int Balance { get; set; } = 1000;
        public int Rounds { get; set; } = 1;
        public int Seats { get; set; } = 3;
        public int TierIndex { get; set; } = 0;
        public bool Unlimited { get; set; }

        public Progress Clone() => new()
        {
            Balance = Balance,
            Rounds = Rounds,
            Seats = Seats,
            TierIndex = TierIndex,
            Unlimited = Unlimited
        };
    }

    /// <summary>下注档位（与桌面版完全一致）。</summary>
    public static class BetTiers
    {
        public static readonly (int Min, int? Max, string Name)[] All =
        {
            (10, 500, "休闲"),
            (50, 1_000, "轻松"),
            (100, 5_000, "入门"),
            (500, 10_000, "普通"),
            (1_000, 50_000, "进阶"),
            (5_000, 100_000, "强手"),
            (10_000, null, "极强")
        };

        public static int Count => All.Length;

        public static (int Min, int? Max, string Name) Get(int index) =>
            All[Math.Clamp(index, 0, All.Length - 1)];

        public static string NameOf(int index) => Get(index).Name;
    }

    /// <summary>
    /// 存档读写。桌面版用 JSON + 本地文件，这里保持一致，
    /// 只是根目录换成 Unity 的 persistentDataPath（Android 上是应用私有目录）。
    /// </summary>
    public static class SaveStore
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true
        };

        public static string Dir
        {
            get
            {
                var dir = Path.Combine(Application.persistentDataPath, "TableGameRoom");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string PathFor(string mode) =>
            Path.Combine(Dir, mode == "无限" ? "custom-progress.json" : "progress.json");

        public static Progress Load(string mode)
        {
            try
            {
                var path = PathFor(mode);
                if (!File.Exists(path)) return new Progress();

                var json = File.ReadAllText(path);
                var p = JsonSerializer.Deserialize<Progress>(json);
                if (p == null) return new Progress();

                // 存档损坏时回落到默认值
                if (p.Balance < 0) p.Balance = 1000;
                if (p.Rounds < 1) p.Rounds = 1;
                p.Seats = Math.Clamp(p.Seats, 2, 7);
                p.TierIndex = Math.Clamp(p.TierIndex, 0, BetTiers.Count - 1);
                return p;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SaveStore] 读取存档失败：{ex.Message}");
                return new Progress();
            }
        }

        public static void Save(string mode, Progress p)
        {
            try
            {
                File.WriteAllText(PathFor(mode), JsonSerializer.Serialize(p, Options));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SaveStore] 写入存档失败：{ex.Message}");
            }
        }
    }
}
