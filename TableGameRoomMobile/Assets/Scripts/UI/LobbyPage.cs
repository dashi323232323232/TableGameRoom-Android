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
        public sealed record GameDef(string Key, string Name, string Emoji, string Desc, int MinSeats, int MaxSeats);

        public static readonly GameDef[] Games =
        {
            new("roulette",  "欧洲轮盘", "🎡", "37 个号码，多种投注方式，单零轮盘", 2, 7),
            new("blackjack", "21 点",    "🃏", "要牌/停牌/双倍，庄家 17 点停牌",     1, 7),
            new("poker",     "德州扑克", "♠",  "翻牌前到河牌四街下注，摊牌比牌型",   2, 7),
            new("three",     "炸金花",   "🌸", "三张牌，看牌/跟注/加注/比牌",        2, 7),
            new("liar",      "骗子扑克", "🎭", "轮流盖牌声明，随时可以质疑",         2, 2)
        };

        public static void Build(RectTransform root)
        {
            var app = App.Instance;
            var scroll = UiFactory.ScrollView(root, out _, 24);
            UiFactory.Stretch((RectTransform)scroll.parent.parent);

            // 顶部操作
            var topRow = UiFactory.HorizontalLayout(scroll, 16, null, TextAnchor.MiddleLeft);
            UiFactory.Size(topRow.gameObject, -1, 96);

            var back = UiFactory.Button(topRow.transform, "← 模式选择", () => app.ShowModeSelect());
            UiFactory.Size(back.gameObject, 240, 88);

            var tierBtn = UiFactory.Button(topRow.transform, $"档位：{app.TierName}", () => ShowTierPicker());
            UiFactory.Size(tierBtn.gameObject, 300, 88);

            var seatsBtn = UiFactory.Button(topRow.transform, $"桌人数：{app.Seats}", () => ShowSeatPicker());
            UiFactory.Size(seatsBtn.gameObject, 300, 88);

            // 筛选
            var filterTitle = UiFactory.Label(scroll, "筛选", 36, Theme.Ink, TextAnchor.UpperLeft, bold: true);
            UiFactory.Size(filterTitle.gameObject, -1, 56);

            BuildFilterRow(scroll, "游戏", new[] { "全部游戏", "轮盘", "21点", "扑克", "炸金花", "骗子" },
                app.Category, v => { app.Category = v; app.ShowLobby(); });

            BuildFilterRow(scroll, "人数", new[] { "全部人数", "单人", "双人", "3 人", "4 人以上" },
                app.People, v => { app.People = v; app.ShowLobby(); });

            // 游戏卡片
            foreach (var g in Games)
            {
                if (!MatchesFilter(g)) continue;
                BuildGameCard(scroll, g);
            }
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

        private static void BuildFilterRow(RectTransform parent, string label, string[] options,
            string current, Action<string> onPick)
        {
            var row = UiFactory.HorizontalLayout(parent, 12, null, TextAnchor.MiddleLeft);
            UiFactory.Size(row.gameObject, -1, 96);

            var lab = UiFactory.Label(row.transform, label, 28, Theme.Muted, TextAnchor.MiddleLeft);
            UiFactory.Size(lab.gameObject, 90, 60);

            foreach (var opt in options)
            {
                var picked = opt == current;
                var btn = UiFactory.Button(row.transform, opt, () => onPick(opt),
                    picked ? Theme.AccentSoft : Theme.Panel);
                UiFactory.Size(btn.gameObject, -1, 76);

                var txt = btn.GetComponentInChildren<Text>();
                if (picked && txt != null) txt.fontStyle = FontStyle.Bold;
            }
        }

        private static void BuildGameCard(RectTransform parent, GameDef g)
        {
            var card = UiFactory.Card(parent, Theme.Panel, Theme.PanelBorder);
            var layout = UiFactory.HorizontalLayout(card.transform, 20, new RectOffset(36, 36, 30, 30), TextAnchor.MiddleLeft);
            UiFactory.Stretch((RectTransform)layout.transform);

            var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var emoji = UiFactory.Label(layout.transform, g.Emoji, 64, Theme.Ink, TextAnchor.MiddleCenter);
            UiFactory.Size(emoji.gameObject, 110, 110);

            var col = UiFactory.VerticalLayout(layout.transform, 8, null, TextAnchor.MiddleLeft);
            var le = col.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1;

            var name = UiFactory.Label(col.transform, g.Name, 40, Theme.Ink, TextAnchor.MiddleLeft, bold: true);
            UiFactory.Size(name.gameObject, -1, 52);

            var desc = UiFactory.Label(col.transform, g.Desc, 26, Theme.Muted, TextAnchor.MiddleLeft);
            UiFactory.Size(desc.gameObject, -1, 40);

            var seats = UiFactory.Label(col.transform, $"支持 {g.MinSeats}–{g.MaxSeats} 人", 24, Theme.Accent, TextAnchor.MiddleLeft);
            UiFactory.Size(seats.gameObject, -1, 34);

            var btn = UiFactory.PrimaryButton(layout.transform, "进入", () => EnterGame(g));
            UiFactory.Size(btn.gameObject, 180, 96);
        }

        private static void EnterGame(GameDef g)
        {
            var app = App.Instance;
            switch (g.Key)
            {
                case "roulette":
                    RoulettePage.Build(app.BeginPage("roulette"));
                    break;
                case "blackjack":
                    BlackjackPage.Build(app.BeginPage("blackjack"));
                    break;
                case "poker":
                    PokerPage.Build(app.BeginPage("poker"));
                    break;
                case "three":
                    ThreePage.Build(app.BeginPage("three"));
                    break;
                case "liar":
                    LiarPage.Build(app.BeginPage("liar"));
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
