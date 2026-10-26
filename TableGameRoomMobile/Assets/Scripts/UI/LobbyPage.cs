using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TableGameRoom.Mobile.Core;
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

        public static void Build(RectTransform content)
        {
            var app = App.Instance;
            var stack = new VStack(content);

            // ---------- 顶部操作 ----------
            var topButtons = new List<Button>
            {
                UiFactory.Button(content, "← 返回", app.ShowModeSelect),
                UiFactory.Button(content, $"档位 {app.TierName}", ShowTierPicker),
                UiFactory.Button(content, $"桌 {app.Seats} 人", ShowSeatPicker)
            };
            stack.RowOf(topButtons, 92f);

            // ---------- 筛选 ----------
            stack.Head("筛选");

            AddFilterRow(stack, content, "游戏",
                new[] { "全部游戏", "轮盘", "21点", "扑克", "炸金花", "骗子" },
                app.Category, v => { app.Category = v; app.ShowLobby(); });

            AddFilterRow(stack, content, "人数",
                new[] { "全部人数", "单人", "双人", "3 人", "4 人以上" },
                app.People, v => { app.People = v; app.ShowLobby(); });

            // ---------- 游戏卡片 ----------
            foreach (var g in Games)
            {
                if (!MatchesFilter(g)) continue;
                AddGameCard(stack, g);
            }

            stack.Finish();
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

        private static void AddFilterRow(VStack stack, RectTransform content, string label,
            string[] options, string current, Action<string> onPick)
        {
            var buttons = new List<Button>();

            foreach (var opt in options)
            {
                var o = opt;
                var picked = opt == current;
                var btn = UiFactory.Button(content, opt, () => onPick(o),
                    picked ? Theme.AccentSoft : Theme.Panel);

                if (picked)
                {
                    var txt = btn.GetComponentInChildren<Text>();
                    if (txt != null) txt.fontStyle = FontStyle.Bold;
                }

                buttons.Add(btn);
            }

            stack.Grid(buttons, 3, 78f);
        }

        private static void AddGameCard(VStack stack, GameDef g)
        {
            stack.Card(inner =>
            {
                inner.Text($"{g.Emoji}  {g.Name}", Theme.FontTitle, Theme.Ink, bold: true, minHeight: 62f);
                inner.Text(g.Desc, Theme.FontBody, Theme.Muted);
                inner.Text($"支持 {g.MinSeats}–{g.MaxSeats} 人", Theme.FontSmall, Theme.Accent);
                inner.Primary("进入牌桌", () => EnterGame(g));
            }, Theme.Panel, Theme.PanelBorder);
        }

        private static void EnterGame(GameDef g)
        {
            var app = App.Instance;
            switch (g.Key)
            {
                case "roulette": app.ShowRoulette(); break;
                case "blackjack": app.ShowBlackjack(); break;
                case "poker": app.ShowPoker(); break;
                case "three": app.ShowThree(); break;
                case "liar": app.ShowLiar(); break;
                default: app.Msg($"{g.Name} 正在开发中"); break;
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

            Dialog.Pick(app.CanvasRoot, "选择下注档位", options, app.TierIndex, idx =>
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

            Dialog.Pick(app.CanvasRoot, "选择桌人数", options, app.Seats - 2, idx =>
            {
                app.Save.Seats = idx + 2;
                app.PersistSave();
                app.ShowLobby();
            });
        }
    }
}
