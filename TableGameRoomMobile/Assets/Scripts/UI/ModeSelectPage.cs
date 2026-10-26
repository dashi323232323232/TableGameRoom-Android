using System;
using UnityEngine;
using UnityEngine.UI;
using TableGameRoom.Mobile.Core;
using TableGameRoom.Mobile.UI;

namespace TableGameRoom.Mobile
{
    /// <summary>模式选择页 —— 对应桌面版 ShowModeSelect()。</summary>
    public static class ModeSelectPage
    {
        public static void Build(RectTransform root)
        {
            var content = UiFactory.ScrollView(root, out _);
            var flow = new FlowLayout(content, padding: 28, spacing: 18);

            // 标题
            var title = UiFactory.Label(content, "欢迎来到 TABLE 游戏厅", 52, Theme.Ink,
                TextAnchor.UpperLeft, bold: true);
            flow.AddText(title, 80);

            var sub = UiFactory.Label(content,
                "全部筹码均为虚拟筹码，程序离线运行，不使用数据库、不联网、不涉及真实货币。",
                26, Theme.Muted);
            flow.AddText(sub);

            // 两张模式卡片
            flow.AddCard((inner, f) =>
            {
                var head = UiFactory.Label(inner, "普通模式", 42, Theme.Ink, TextAnchor.UpperLeft, bold: true);
                f.AddText(head, 58);

                var tag = UiFactory.Label(inner, "存档模式", 26, Theme.Accent);
                f.AddText(tag, 38);

                var desc = UiFactory.Label(inner,
                    "从 $1,000 开始，输光可以靠任务奖金翻身。进度单独保存。", 26, Theme.Muted);
                f.AddText(desc);

                var btn = UiFactory.PrimaryButton(inner, "选择普通模式", () =>
                {
                    App.Instance.SetMode("存档");
                    App.Instance.ShowLobby();
                });
                f.AddButton(btn, 100);

                return f.TotalHeight;
            }, Theme.Panel, Theme.PanelBorder);

            flow.AddCard((inner, f) =>
            {
                var head = UiFactory.Label(inner, "无限模式", 42, Theme.Ink, TextAnchor.UpperLeft, bold: true);
                f.AddText(head, 58);

                var tag = UiFactory.Label(inner, "无限筹码", 26, Theme.Success);
                f.AddText(tag, 38);

                var desc = UiFactory.Label(inner,
                    "余额显示为 ∞，随便玩不受限制。进度与普通模式完全独立。", 26, Theme.Muted);
                f.AddText(desc);

                var btn = UiFactory.PrimaryButton(inner, "选择无限模式", () =>
                {
                    App.Instance.SetMode("无限");
                    App.Instance.ShowLobby();
                });
                f.AddButton(btn, 100);

                return f.TotalHeight;
            }, Theme.Panel, Theme.PanelBorder);

            // 功能入口
            var funcTitle = UiFactory.Label(content, "功能", 38, Theme.Ink, TextAnchor.UpperLeft, bold: true);
            flow.AddText(funcTitle, 58);

            AddEntry(content, flow, "🎯 任务中心", "做任务赚奖金，也是破产保护",
                () => PageBuilder.Run(App.Instance.BeginPage("tasks"), TasksPage.Build));

            AddEntry(content, flow, "📅 历史记录", "按日期查看每一局的输赢",
                () => PageBuilder.Run(App.Instance.BeginPage("history"), HistoryPage.Build));

            AddEntry(content, flow, "🌐 局域网联机", "和同一 Wi-Fi 下的设备对战",
                () => App.Instance.Msg("联机功能开发中"));

            flow.Finish();
        }

        private static void AddEntry(RectTransform content, FlowLayout flow,
            string title, string desc, Action onClick)
        {
            flow.AddCard((inner, f) =>
            {
                var t = UiFactory.Label(inner, title, 36, Theme.Ink, TextAnchor.UpperLeft, bold: true);
                f.AddText(t, 50);

                var d = UiFactory.Label(inner, desc, 25, Theme.Muted);
                f.AddText(d, 36);

                var btn = UiFactory.Button(inner, "进入", onClick);
                f.AddButton(btn, 88);

                return f.TotalHeight;
            }, Color.white, Theme.PanelBorder);
        }
    }
}
