using UnityEngine;

namespace TableGameRoom.Mobile.UI
{
    /// <summary>
    /// 全局视觉常量。与桌面版 WPF 的配色保持一致（浅色卡片风）。
    /// </summary>
    public static class Theme
    {
        // ---------- 文字 ----------
        public static readonly Color Ink = Hex("#202A30");
        public static readonly Color Muted = Hex("#53636B");
        public static readonly Color OnDark = Color.white;

        // ---------- 面板 ----------
        public static readonly Color Panel = Hex("#EFF7FC");
        public static readonly Color PanelBorder = Hex("#D9E7E8");
        public static readonly Color Surface = Color.white;

        // ---------- 牌桌 ----------
        public static readonly Color Table = Hex("#E2F4EC");
        public static readonly Color TableBorder = Hex("#A9D7CC");

        // ---------- 强调 ----------
        public static readonly Color Accent = Hex("#5B9FD0");
        public static readonly Color AccentSoft = Hex("#CBDFF1");
        public static readonly Color AccentDeep = Hex("#3E7FA8");

        // ---------- 顶栏 ----------
        public static readonly Color TopBar = Hex("#202A30");

        // ---------- 语义色 ----------
        public static readonly Color Danger = Hex("#C0392B");
        public static readonly Color Warning = Hex("#B8860B");
        public static readonly Color Success = Hex("#5FA85F");

        // ---------- 牌面 ----------
        public static readonly Color CardFace = Color.white;
        public static readonly Color CardBorder = Hex("#C9D6DA");
        public static readonly Color CardRed = Hex("#C0392B");
        public static readonly Color CardBlack = Hex("#202A30");

        // ---------- 尺寸 ----------
        public const float TopBarHeight = 120f;
        public const float PagePadding = 28f;
        public const float ItemGap = 16f;

        public const float ButtonHeight = 96f;
        public const float ButtonHeightSmall = 80f;
        public const float RowGap = 12f;

        // 字号
        public const int FontHuge = 52;
        public const int FontTitle = 42;
        public const int FontHead = 34;
        public const int FontBody = 28;
        public const int FontSmall = 24;

        /// <summary>设计分辨率（竖屏）。</summary>
        public static readonly Vector2 ReferenceResolution = new Vector2(1080, 1920);

        /// <summary>"#RRGGBB" 或 "#RRGGBBAA" → Color。</summary>
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

        public static Color WithAlpha(Color c, float alpha) => new Color(c.r, c.g, c.b, alpha);
    }
}
