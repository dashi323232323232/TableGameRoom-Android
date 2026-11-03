using UnityEngine;
using UnityEngine.UI;

namespace TableGameRoom.Mobile.UI
{
    /// <summary>
    /// 座位卡片 —— 与桌面版 Poker.cs 的 SeatView 对齐。
    ///
    /// 桌面版结构（宽 132）：
    ///   名字 + 表情        11pt
    ///   两张牌（compact）   小尺寸
    ///   状态 · 注额        9pt
    ///   筹码图形
    ///
    /// 安卓照搬这个结构，只是尺寸按手机缩放。整个卡片带边框和圆角，
    /// 每个玩家一个独立框，人多也分得清。
    /// </summary>
    public static class SeatCard
    {
        /// <summary>座位卡片尺寸（与 TableRing.SeatWidth 对应）。</summary>
        public const float Width = TableRing.SeatWidth;

        /// <summary>创建座位卡片。返回卡片根节点。</summary>
        public static RectTransform Create(
            Transform parent,
            string name,
            string emoji,
            int seatIndex,
            string status,
            int committed,
            bool folded,
            bool isCurrent,
            string result = "")
        {
            var root = UiFactory.Node(parent, $"Seat_{name}");
            var rt = root;
            rt.sizeDelta = new Vector2(Width, TableRing.SeatHeight);

            // ---------- 卡片底 ----------
            var bg = UiFactory.Card(root,
                folded ? Theme.Hex("#EEF2F3") : Theme.Surface,
                isCurrent ? Theme.Accent : Theme.PanelBorder);

            var bgRt = bg.rectTransform;
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;

            if (isCurrent)
            {
                var outline = bg.gameObject.GetComponent<Outline>();
                if (outline != null)
                {
                    outline.effectColor = Theme.Accent;
                    outline.effectDistance = new Vector2(2.5f, -2.5f);
                }
            }

            // ---------- 第一行：名字 + 表情 ----------
            var nameText = UiFactory.Label(root, $"{Shorten(name)} {emoji}", 20,
                folded ? Theme.Muted : Theme.Ink, TextAnchor.MiddleCenter, bold: true);
            var nr = nameText.rectTransform;
            nr.anchorMin = nr.anchorMax = new Vector2(0.5f, 1f);
            nr.pivot = new Vector2(0.5f, 1f);
            nr.sizeDelta = new Vector2(Width - 8f, 26f);
            nr.anchoredPosition = new Vector2(0f, -4f);

            // ---------- 第二行：状态 · 注额 ----------
            var info = folded ? "已弃牌"
                : !string.IsNullOrEmpty(result) ? result
                : committed > 0 ? $"{status} · ${committed:N0}"
                : status;

            var infoText = UiFactory.Label(root, info, 17,
                folded ? Theme.Muted : isCurrent ? Theme.Accent : Theme.Muted,
                TextAnchor.MiddleCenter, bold: isCurrent || !string.IsNullOrEmpty(result));

            var ir = infoText.rectTransform;
            ir.anchorMin = ir.anchorMax = new Vector2(0.5f, 1f);
            ir.pivot = new Vector2(0.5f, 1f);
            ir.sizeDelta = new Vector2(Width - 6f, 22f);
            ir.anchoredPosition = new Vector2(0f, -30f);

            // 注额已用文字表达，卡片内不再叠筹码图形
            // （卡片只有 84 高，再放图形会把文字挤掉）
            return rt;
        }

        /// <summary>名字太长会撑破卡片，截断。</summary>
        private static string Shorten(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";

            // "电脑 1" 这种保持原样；其他过长的截断
            if (name.StartsWith("电脑")) return name;
            return name.Length <= 6 ? name : name.Substring(0, 6);
        }
    }
}
