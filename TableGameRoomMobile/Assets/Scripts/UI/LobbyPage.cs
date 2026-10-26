using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TableGameRoom.Mobile.Core;
using TableGameRoom.Mobile.Games;
using TableGameRoom.Mobile.UI;

namespace TableGameRoom.Mobile
{
    /// <summary>玩法大厅 —— 对应桌面版 ShowLobby()。</summary>
    public static class LobbyPage
    {
        public sealed class GameDef
        {
            public string Key { get; }
            public string Name { get; }
            public string Emoji { get; }
            public string Desc { get; }
            public int MinSeats { get; }
            public int MaxSeats { get; }

            public GameDef(string key, string name, string emoji, string desc, int minSeats, int maxSeats)
            {
                Key = key; Name = name; Emoji = emoji; Desc = desc;
                MinSeats = minSeats; MaxSeats = maxSeats;
            }
        }

        public static readonly GameDef[] Games =
        {
            new GameDef("roulette",  "欧洲轮盘", "🎡", "37 个号码，多种投注方式，单零轮盘", 2, 7),
            new GameDef("blackjack", "21 点",    "🃏", "要牌/停牌/双倍，庄家 17 点停牌",     1, 7),
            new GameDef("poker",     "德州扑克", "♠",  "翻牌前到河牌四街下注，摊牌比牌型",   2, 7),
            new GameDef("three",     "炸金花",   "🌸", "三张牌，看牌/跟注/加注/比牌",        2, 7),
            new GameDef("liar",      "骗子扑克", "🎭", "轮流盖牌声明，随时可以质疑",         2, 2)
        };

        public static void Build(RectTransform root)
        {
            var app = App.Instance;
            var content = UiFactory.ScrollView(root, out _);
            var flow = new FlowLayout(content, padding: 28, spacing: 16);

            // 顶部操作行
            var topRow = UiFactory.HorizontalLayout(content, 10, null, TextAnchor.MiddleLeft);
            flow.Add(topRow.transform as RectTransform, 100);

            var back = UiFactory.Button(topRow.transform, "← 返回", () => app.ShowModeSelect());
            UiFactory.Size(back.gameObject, 200, 92);

            var tierBtn = UiFactory.Button(topRow.transform, $"档位 {app.TierName}", () => ShowTierPicker());
            UiFactory.Size(tierBtn.gameObject, 300, 92);

            var seatsBtn = UiFactory.Button(topRow.transform, $"桌 {app.Seats} 人", () => ShowSeatPicker());
            UiFactory.Size(seatsBtn.gameObject, 240, 92);

            // 筛选标题
            var filterTitle = UiFactory.Label(content, "筛选", 36, Theme.Ink, TextAnchor.UpperLeft, bold: true);
            flow.AddText(filterTitle, 56);

            AddFilterRow(content, flow, "游戏",
                new[] { "全部游戏", "轮盘", "21点", "扑克", "炸金花", "骗子" },
                app.Category, v => { app.Category = v; app.ShowLobby(); });

            AddFilterRow(content, flow, "人数",
                new[] { "全部人数", "单人", "双人", "3 人", "4 人以上" },
                app.People, v => { app.People = v; app.ShowLobby(); });

            // 游戏卡片
            foreach (var g in Games)
            {
                if (!MatchesFilter(g)) continue;
                AddGameCard(content, flow, g);
            }

            flow.Finish();
        }

        private static bool MatchesFilter(GameDef g)
        {
            var app = App.Instance;

            var catOk = app.Category switch
            {
                "全部游戏" => true,
                "轮盘" => g.Key == "roulette",
                "21点" => g.Key == "blackjack",
                "扑克" => g.Key == "poker",
                "炸金花" => g.Key == "three",
                "骗子" => g.Key == "liar",
                _ => true
            };
            if (!catOk) return false;

            return app.People switch
            {
                "全部人数" => true,
                "单人" => g.MinSeats <= 1,
                "双人" => g.MinSeats <= 2 && g.MaxSeats >= 2,
                "3 人" => g.MaxSeats >= 3,
                "4 人以上" => g.MaxSeats >= 4,
                _ => true
            };
        }

        private static void AddFilterRow(RectTransform content, FlowLayout flow, string label,
            string[] options, string current, Action<string> onPick)
        {
            var row = UiFactory.HorizontalLayout(content, 8, null, TextAnchor.MiddleLeft);
            flow.Add((RectTransform)row.transform, 96);

            var lab = UiFactory.Label(row.transform, label, 28, Theme.Muted, TextAnchor.MiddleLeft);
            UiFactory.Size(lab.gameObject, 80, 60);

            foreach (var opt in options)
            {
                var picked = opt == current;
                var btn = UiFactory.Button(row.transform, opt, () => onPick(opt),
                    picked ? Theme.AccentSoft : Theme.Panel);
                UiFactory.Size(btn.gameObject, 150, 82);

                var txt = btn.GetComponentInChildren<Text>();
                if (picked && txt != null) txt.fontStyle = FontStyle.Bold;
            }
        }

        private static void AddGameCard(RectTransform content, FlowLayout flow, GameDef g)
        {
            flow.AddCard((inner, f) =>
            {
                // 标题行：emoji + 名称
                var headRow = UiFactory.HorizontalLayout(inner, 14, null, TextAnchor.MiddleLeft);
                f.Add((RectTransform)headRow.transform, 80);

                var emoji = UiFactory.Label(headRow.transform, g.Emoji, 52, Theme.Ink, TextAnchor.MiddleCenter);
                UiFactory.Size(emoji.gameObject, 80, 76);

                var name = UiFactory.Label(headRow.transform, g.Name, 40, Theme.Ink,
                    TextAnchor.MiddleLeft, bold: true);
                UiFactory.Size(name.gameObject, 400, 76);

                // 描述
                var desc = UiFactory.Label(inner, g.Desc, 26, Theme.Muted);
                f.AddText(desc, 40);

                // 支持人数
                var seats = UiFactory.Label(inner, $"支持 {g.MinSeats}–{g.MaxSeats} 人", 24, Theme.Accent);
                f.AddText(seats, 36);

                // 进入按钮
                var btn = UiFactory.PrimaryButton(inner, "进入牌桌", () => EnterGame(g));
                f.AddButton(btn, 96);

                return f.TotalHeight;
            }, Theme.Panel, Theme.PanelBorder);
        }

        private static void EnterGame(GameDef g)
        {
            var app = App.Instance;
            switch (g.Key)
            {
                case "roulette":
                    PageBuilder.Run(app.BeginPage("roulette"), RoulettePage.Build);
                    break;
                case "blackjack":
                    PageBuilder.Run(app.BeginPage("blackjack"), BlackjackPage.Build);
                    break;
                case "poker":
                    PageBuilder.Run(app.BeginPage("poker"), PokerPage.Build);
                    break;
                case "three":
                    PageBuilder.Run(app.BeginPage("three"), ThreePage.Build);
                    break;
                case "liar":
                    PageBuilder.Run(app.BeginPage("liar"), LiarPage.Build);
                    break;
                default:
                    app.Msg($"{g.Name} 正在开发中");
                    break;
            }
        }

        private static void ShowTierPicker()
        {
            var app = App.Instance;
            var options = new List<string>();
            for (var i = 0; i < BetTiers.Count; i++)
            {
                var t = BetTiers.Get(i);
                var range = t.Max is null ? "无上限" : $"${t.Max.Value:N0}";
                options.Add($"{t.Name}    ${t.Min:N0} – {range}");
            }

            PickerDialog.Show(app.CanvasTransform, "选择下注档位", options, app.TierIndex, idx =>
            {
                app.Save.TierIndex = idx;
                app.PersistSave();
                app.ShowLobby();
            });
        }

        private static void ShowSeatPicker()
        {
            var app = App.Instance;
            var options = new List<string>();
            for (var n = 2; n <= 7; n++)
            {
                options.Add($"{n} 人桌{(n == app.Seats ? "   ← 当前" : "")}");
            }

            PickerDialog.Show(app.CanvasTransform, "选择桌人数", options, app.Seats - 2, idx =>
            {
                app.Save.Seats = idx + 2;
                app.PersistSave();
                app.ShowLobby();
            });
        }
    }
}
