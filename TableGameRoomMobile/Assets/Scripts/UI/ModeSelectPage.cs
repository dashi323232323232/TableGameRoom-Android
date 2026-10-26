using System;
using UnityEngine;
using TableGameRoom.Mobile.UI;

namespace TableGameRoom.Mobile
{
    /// <summary>模式选择页 —— 对应桌面版 ShowModeSelect()。</summary>
    public static class ModeSelectPage
    {
        public static void Build(RectTransform content)
        {
            var app = App.Instance;
            var stack = new VStack(content);

            stack.Title("欢迎来到 TABLE 游戏厅");

            stack.Text("全部筹码均为虚拟筹码，程序离线运行，不使用数据库、不联网、不涉及真实货币。",
                Theme.FontSmall, Theme.Muted);

            // ---------- 普通模式 ----------
            stack.Card(inner =>
            {
                inner.Text("普通模式", Theme.FontTitle, Theme.Ink, bold: true, minHeight: 60f);
                inner.Text("存档模式", Theme.FontSmall, Theme.Accent);
                inner.Text("从 $1,000 开始，输光可以靠任务奖金翻身。进度单独保存。",
                    Theme.FontBody, Theme.Muted);

                inner.Primary("选择普通模式", () =>
                {
                    app.SetMode("存档");
                    app.ShowLobby();
                });
            }, Theme.Panel, Theme.PanelBorder);

            // ---------- 无限模式 ----------
            stack.Card(inner =>
            {
                inner.Text("无限模式", Theme.FontTitle, Theme.Ink, bold: true, minHeight: 60f);
                inner.Text("无限筹码", Theme.FontSmall, Theme.Success);
                inner.Text("余额显示为 ∞，随便玩不受限制。进度与普通模式完全独立。",
                    Theme.FontBody, Theme.Muted);

                inner.Primary("选择无限模式", () =>
                {
                    app.SetMode("无限");
                    app.ShowLobby();
                });
            }, Theme.Panel, Theme.PanelBorder);

            // ---------- 功能入口 ----------
            stack.Head("功能");

            Entry(stack, "🎯 任务中心", "做任务赚奖金，也是破产保护", app.ShowTasks);
            Entry(stack, "📅 历史记录", "按日期查看每一局的输赢", app.ShowHistory);
            Entry(stack, "🌐 局域网联机", "和同一 Wi-Fi 下的设备对战",
                () => app.Msg("联机功能开发中"));

            stack.Finish();
        }

        private static void Entry(VStack stack, string title, string desc, Action onClick)
        {
            stack.Card(inner =>
            {
                inner.Text(title, Theme.FontHead, Theme.Ink, bold: true, minHeight: 48f);
                inner.Text(desc, Theme.FontSmall, Theme.Muted);
                inner.Button("进入", onClick, Theme.ButtonHeightSmall);
            }, Theme.Surface, Theme.PanelBorder);
        }
    }
}
