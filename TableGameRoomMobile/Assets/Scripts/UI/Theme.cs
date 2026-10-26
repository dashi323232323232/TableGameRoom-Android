using UnityEngine;
using System;

namespace TableGameRoom.Mobile.UI
{
    /// <summary>
    /// 与桌面版一致的配色。桌面版用的是浅色卡片风（白底 + 浅蓝面板 + 淡绿牌桌），
    /// 这里原样搬到移动端，保持视觉统一。
    /// </summary>
    public static class Theme
    {
        // 主文字 / 次要文字
        public static readonly Color Ink = Hex("#202A30");
        public static readonly Color Muted = Hex("#53636B");

        // 面板
        public static readonly Color Panel = Hex("#EFF7FC");
        public static readonly Color PanelBorder = Hex("#D9E7E8");

        // 牌桌（淡绿）
        public static readonly Color Table = Hex("#E2F4EC");
        public static readonly Color TableBorder = Hex("#A9D7CC");

        // 强调色（浅蓝）
        public static readonly Color Accent = Hex("#5B9FD0");
        public static readonly Color AccentSoft = Hex("#CBDFF1");

        // 标题栏深色
        public static readonly Color TitleBar = Hex("#202A30");
        public static readonly Color TitleBarHover = Hex("#35434C");

        // 状态色
        public static readonly Color Danger = Hex("#C0392B");
        public static readonly Color Warning = Hex("#B8860B");
        public static readonly Color Success = Hex("#78B478");

        // 牌面
        public static readonly Color CardFace = Color.white;
        public static readonly Color CardBorder = Hex("#C9D6DA");
        public static readonly Color CardRed = Hex("#C0392B");
        public static readonly Color CardBlack = Hex("#202A30");

        // 背景
        public static readonly Color Background = Color.white;
        public static readonly Color Overlay = new Color(0f, 0f, 0f, 0.55f);

        /// <summary>"#RRGGBB" → Color</summary>
        public static Color Hex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Color.white;
            if (hex[0] == '#') hex = hex.Substring(1);
            if (hex.Length < 6) return Color.white;

            var r = System.Convert.ToInt32(hex.Substring(0, 2), 16) / 255f;
            var g = System.Convert.ToInt32(hex.Substring(2, 2), 16) / 255f;
            var b = System.Convert.ToInt32(hex.Substring(4, 2), 16) / 255f;
            var a = hex.Length >= 8 ? System.Convert.ToInt32(hex.Substring(6, 2), 16) / 255f : 1f;
            return new Color(r, g, b, a);
        }

        /// <summary>参考分辨率：桌面版窗口 1200×780。移动端按 1080×1920 竖屏设计。</summary>
        public static readonly Vector2 ReferenceResolution = new Vector2(1080, 1920);
    }
}
