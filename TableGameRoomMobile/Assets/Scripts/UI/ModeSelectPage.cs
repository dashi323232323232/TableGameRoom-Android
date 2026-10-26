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
            var scroll = UiFactory.ScrollView(root, out _, 28);
            UiFactory.Stretch((RectTransform)scroll.parent.parent);

            var app = App.Instance;

            // 标题
            var title = UiFactory.Label(scroll, "欢迎来到 TABLE 游戏厅", 52, Theme.Ink, TextAnchor.UpperLeft, bold: true);
            UiFactory.Size(title.gameObject, -1, 76);

            var sub = UiFactory.Label(scroll,
                "全部筹码均为虚拟筹码，程序离线运行，不使用数据库、不联网、不涉及真实货币。",
                28, Theme.Muted);
            sub.lineSpacing = 1.3f;

            // 两张模式卡片
            BuildModeCard(scroll, "普通模式", "存档模式",
                "从 $1,000 开始，输光可以靠任务奖金翻身。进度单独保存。",
                "选择普通模式", Theme.Accent);

            BuildModeCard(scroll, "无限模式", "无限筹码",
                "余额显示为 ∞，随便玩不受限制。进度与普通模式完全独立。",
                "选择无限模式", Theme.Success);

            // 功能入口
            var funcTitle = UiFactory.Label(scroll, "功能", 36, Theme.Ink, TextAnchor.UpperLeft, bold: true);
            UiFactory.Size(funcTitle.gameObject, -1, 56);

            BuildEntry(scroll, "🎯 任务中心", "做任务赚奖金，也是破产保护", () => TasksPage.Build(App.Instance.BeginPage("tasks")));
            BuildEntry(scroll, "📅 历史记录", "按日期查看每一局的输赢", () => HistoryPage.Build(App.Instance.BeginPage("history")));
            BuildEntry(scroll, "🌐 局域网联机", "和同一 Wi-Fi 下的设备对战", () => App.Instance.Msg("联机功能开发中"));
        }

        private static void BuildModeCard(
            RectTransform parent, string title, string subtitle, string desc,
            string buttonText, Color accent)
        {
            var card = UiFactory.Card(parent, Theme.Panel, Theme.PanelBorder);
            var layout = UiFactory.VerticalLayout(card.transform, 14, new RectOffset(36, 36, 32, 32));
            UiFactory.Stretch((RectTransform)layout.transform);

            var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var head = UiFactory.Label(layout.transform, title, 40, Theme.Ink, TextAnchor.UpperLeft, bold: true);
            UiFactory.Size(head.gameObject, -1, 54);

            var tag = UiFactory.Label(layout.transform, subtitle, 26, accent);
            UiFactory.Size(tag.gameObject, -1, 36);

            var body = UiFactory.Label(layout.transform, desc, 28, Theme.Muted);
            body.lineSpacing = 1.3f;

            var btn = UiFactory.PrimaryButton(layout.transform, buttonText, () =>
            {
                App.Instance.SetMode(title == "无限模式" ? "无限" : "存档");
                App.Instance.ShowLobby();
            });
            UiFactory.Size(btn.gameObject, -1, 96);
        }

        private static void BuildEntry(RectTransform parent, string title, string desc, Action onClick)
        {
            var card = UiFactory.Card(parent, Color.white, Theme.PanelBorder);
            var layout = UiFactory.HorizontalLayout(card.transform, 16, new RectOffset(32, 32, 26, 26), TextAnchor.MiddleLeft);
            UiFactory.Stretch((RectTransform)layout.transform);

            var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var col = UiFactory.VerticalLayout(layout.transform, 6, null, TextAnchor.MiddleLeft);
            var le = col.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1;

            var t = UiFactory.Label(col.transform, title, 34, Theme.Ink, TextAnchor.MiddleLeft, bold: true);
            UiFactory.Size(t.gameObject, -1, 46);

            var d = UiFactory.Label(col.transform, desc, 25, Theme.Muted, TextAnchor.MiddleLeft);
            UiFactory.Size(d.gameObject, -1, 34);

            var btn = UiFactory.Button(layout.transform, "进入", onClick);
            UiFactory.Size(btn.gameObject, 160, 84);
        }
    }
}
