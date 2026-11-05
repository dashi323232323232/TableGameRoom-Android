using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TableGameRoom.Mobile.Core;

namespace TableGameRoom.Mobile.UI
{
    /// <summary>
    /// 游戏配置页 —— 对应桌面版 MainWindow.ShowGameSetup。
    ///
    /// 桌面版的层级是三层：
    ///   1. 选择模式（普通 / 无限）
    ///   2. 选择玩法（大厅里点某款游戏）
    ///   3. **配置页**（选档位 / 人数 / 下注，然后「开始游戏」）
    ///   4. 游戏内
    ///
    /// 安卓之前从大厅直接跳进游戏页，少了第 3 层，档位和人数只能在游戏里临时改。
    /// 这里补上，和桌面版对齐。
    /// </summary>
    public static class GameSetupPage
    {
        /// <summary>游戏元信息（与大厅卡片一致）。</summary>
        public sealed class GameInfo
        {
            public string Key = "";
            public string Name = "";
            public string Emoji = "";
            public string Intro = "";
            public string Rule = "";
            public int MinSeats = 2;
            public int MaxSeats = 7;
            /// <summary>是否支持桌人数选择（轮盘 / 21 点没有）。</summary>
            public bool TableSeats = true;
        }

        public static void Build(RectTransform content, GameInfo game)
        {
            var app = App.Instance;
            var stack = new VStack(content);

            stack.Button("← 返回大厅", () => app.ShowLobby(), 88f);

            // ---------- 标题 ----------
            stack.Title($"{game.Emoji} {game.Name}");
            stack.Text(game.Intro, Theme.FontBody, Theme.Muted);
            stack.Text($"规则：{game.Rule}", Theme.FontSmall, Theme.Muted);

            // ---------- 桌人数 ----------
            if (game.TableSeats)
            {
                stack.Head("桌人数");
                stack.Text($"当前：{app.Seats} 人（你 + {app.Seats - 1} 位电脑）",
                    Theme.FontBody, Theme.Accent, bold: true);

                var seatButtons = new List<Button>();
                for (var n = Mathf.Max(3, game.MinSeats); n <= game.MaxSeats; n++)
                {
                    var value = n;
                    var picked = app.Seats == value;
                    seatButtons.Add(UiFactory.Button(stack.Host, $"{value} 人", () =>
                    {
                        app.SetSeats(value);
                        app.ShowGameSetup(game.Key);
                    }, picked ? Theme.AccentSoft : Theme.Panel));
                }

                if (seatButtons.Count > 0)
                    stack.Grid(seatButtons, 5, Theme.ButtonHeightSmall + 6f);
            }
            else
            {
                stack.Text("此游戏不支持人数选择。", Theme.FontSmall, Theme.Muted);
            }

            // ---------- 下注档位 ----------
            stack.Head("下注档位");
            var tier = app.Tier;
            var tierRange = tier.Max is null ? "无上限" : $"${tier.Max.Value:N0}";
            stack.Text($"当前：{tier.Name}（${tier.Min:N0} – {tierRange}）",
                Theme.FontBody, Theme.Accent, bold: true);

            var tierButtons = new List<Button>();
            for (var i = 0; i < BetTiers.Count; i++)
            {
                var index = i;
                var t = BetTiers.Get(i);
                var picked = app.TierIndex == index;
                var max = t.Max is null ? "∞" : $"${t.Max.Value:N0}";
                tierButtons.Add(UiFactory.Button(stack.Host, $"{t.Name}\n${t.Min:N0}–{max}", () =>
                {
                    app.SetTier(index);
                    app.ShowGameSetup(game.Key);
                }, picked ? Theme.AccentSoft : Theme.Panel, null, 22));
            }

            if (tierButtons.Count > 0)
                stack.Grid(tierButtons, 2, 92f);

            // ---------- 当前选择汇总 ----------
            stack.Divider();
            var seatsText = game.TableSeats ? $"人数 {app.Seats} 人" : "人数 不适用";
            stack.Text($"当前选择：{game.Name} · {seatsText} · 档位 {app.TierName}",
                Theme.FontBody, Theme.Ink, bold: true);

            // ---------- 开始游戏 ----------
            stack.Primary("开始游戏 →", () => EnterGame(game.Key), 110f);
            stack.Button("返回选择玩法", () => app.ShowLobby(), 88f);

            stack.Finish();
        }

        private static void EnterGame(string key)
        {
            var app = App.Instance;
            switch (key)
            {
                case "roulette": app.ShowRoulette(); break;
                case "blackjack": app.ShowBlackjack(); break;
                case "poker": app.ShowPoker(); break;
                case "three": app.ShowThree(); break;
                case "liar": app.ShowLiar(); break;
                default: app.Msg("该游戏正在开发中"); break;
            }
        }
    }
}
