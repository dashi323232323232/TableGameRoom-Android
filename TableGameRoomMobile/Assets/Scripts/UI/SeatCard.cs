using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TableGameRoom.Mobile.Core;

namespace TableGameRoom.Mobile.UI
{
    /// <summary>
    /// 座位卡片 —— 与桌面版 Poker.cs 的 SeatView 对齐，但把头像放进卡片。
    ///
    /// 桌面版结构（宽 132）：名字+表情 → 两张牌(compact) → 状态·注额 → 筹码
    /// 安卓竖屏空间更紧，改成左右两栏：
    ///
    ///   ┌──────────────────────┐
    ///   │  ╭──╮  电脑 2         │
    ///   │  │🙂│  [牌][牌]       │
    ///   │  ╰──╯  跟注 · $200    │
    ///   └──────────────────────┘
    ///
    /// 头像用烘焙的圆形脸（不是 emoji 字符 —— Noto CJK 没有 emoji 字形）。
    /// 筹码只以文字「· $200」表达，卡片里不再叠图形，避免压住牌面。
    /// </summary>
    public static class SeatCard
    {
        /// <summary>座位卡片尺寸（与 TableRing.SeatWidth 对应）。</summary>
        public const float Width = TableRing.SeatWidth;

        /// <summary>卡片内小牌尺寸。</summary>
        public const float MiniCardWidth = 26f;
        public const float MiniCardHeight = 36f;

        /// <summary>卡片内头像直径。</summary>
        private const float AvatarSize = 40f;

        /// <summary>创建座位卡片。返回卡片根节点。</summary>
        /// <param name="cards">该座位的底牌；为空则显示 “—”。</param>
        /// <param name="hidden">是否盖牌（未摊牌时对手的牌显示背面）。</param>
        public static RectTransform Create(
            Transform parent,
            string name,
            string emoji,
            int seatIndex,
            string status,
            int committed,
            bool folded,
            bool isCurrent,
            string result = "",
            IReadOnlyList<Card> cards = null,
            bool hidden = true)
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

            // ---------- 左侧：头像 ----------
            var avatar = AvatarVisual.Create(root, name, emoji, seatIndex, false, AvatarSize);
            var ar = avatar;
            ar.anchorMin = ar.anchorMax = new Vector2(0f, 0.5f);
            ar.pivot = new Vector2(0f, 0.5f);
            ar.anchoredPosition = new Vector2(7f, 0f);

            // 弃牌的人整张卡压暗，一眼能看出出局
            if (folded)
            {
                var avImg = avatar.GetComponent<Image>();
                if (avImg != null) avImg.color = new Color(1f, 1f, 1f, 0.45f);
            }

            // 右侧内容区的左边界
            var textLeft = 7f + AvatarSize + 7f;
            var textWidth = Width - textLeft - 6f;

            // ---------- 右上：名字 ----------
            var nameText = UiFactory.Label(root, Shorten(name), 17,
                folded ? Theme.Muted : Theme.Ink, TextAnchor.MiddleLeft, bold: true);
            var nr = nameText.rectTransform;
            nr.anchorMin = nr.anchorMax = new Vector2(0f, 1f);
            nr.pivot = new Vector2(0f, 1f);
            nr.sizeDelta = new Vector2(textWidth, 22f);
            nr.anchoredPosition = new Vector2(textLeft, -5f);

            // ---------- 右中：底牌 ----------
            var row = UiFactory.Node(root, "Cards");
            var rr2 = row;
            rr2.anchorMin = rr2.anchorMax = new Vector2(0f, 1f);
            rr2.pivot = new Vector2(0f, 1f);
            rr2.sizeDelta = new Vector2(textWidth, MiniCardHeight);
            rr2.anchoredPosition = new Vector2(textLeft, -27f);

            if (cards == null || cards.Count == 0)
            {
                var dash = UiFactory.Label(row, "—", 15, Theme.Muted, TextAnchor.MiddleLeft);
                UiFactory.Fill(dash.rectTransform);
            }
            else
            {
                const float gap = 3f;
                var x = 0f;
                for (var i = 0; i < cards.Count; i++)
                {
                    var mini = MiniCard(row, cards[i], hidden);
                    mini.anchorMin = mini.anchorMax = new Vector2(0f, 1f);
                    mini.pivot = new Vector2(0f, 1f);
                    mini.anchoredPosition = new Vector2(x, 0f);
                    x += MiniCardWidth + gap;
                }
            }

            // ---------- 右下：状态 · 注额 ----------
            var info = folded ? "已弃牌"
                : !string.IsNullOrEmpty(result) ? result
                : committed > 0 ? $"{status} · ${committed:N0}"
                : status;

            var infoText = UiFactory.Label(root, info, 15,
                folded ? Theme.Muted : isCurrent ? Theme.Accent : Theme.Muted,
                TextAnchor.MiddleLeft, bold: isCurrent || !string.IsNullOrEmpty(result));

            var ir = infoText.rectTransform;
            ir.anchorMin = ir.anchorMax = new Vector2(0f, 0f);
            ir.pivot = new Vector2(0f, 0f);
            ir.sizeDelta = new Vector2(textWidth, 20f);
            ir.anchoredPosition = new Vector2(textLeft, 5f);

            return rt;
        }

        /// <summary>卡片里的小牌：盖牌显示蓝底 “?”，明牌显示点数 + 花色。</summary>
        private static RectTransform MiniCard(Transform parent, Card card, bool hidden)
        {
            var go = new GameObject("MiniCard", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var img = go.GetComponent<Image>();
            img.sprite = UiFactory.RoundSprite;
            img.type = Image.Type.Sliced;
            img.color = hidden ? Theme.Accent : Theme.CardFace;
            img.raycastTarget = false;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = hidden ? Theme.AccentDeep : Theme.CardBorder;
            outline.effectDistance = new Vector2(1.2f, -1.2f);

            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(MiniCardWidth, MiniCardHeight);

            if (hidden)
            {
                var q = UiFactory.Label(go.transform, "?", 19, Color.white,
                    TextAnchor.MiddleCenter, bold: true);
                UiFactory.Fill(q.rectTransform);
                return rt;
            }

            var color = card.IsRed ? Theme.CardRed : Theme.CardBlack;

            var rank = UiFactory.Label(go.transform, card.RankText, 16, color,
                TextAnchor.MiddleCenter, bold: true);
            var rr = rank.rectTransform;
            rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 0.66f);
            rr.sizeDelta = new Vector2(MiniCardWidth - 2f, 20f);

            var suit = UiFactory.Label(go.transform, card.SuitName, 10, color,
                TextAnchor.MiddleCenter);
            var sr = suit.rectTransform;
            sr.anchorMin = sr.anchorMax = new Vector2(0.5f, 0.26f);
            sr.sizeDelta = new Vector2(MiniCardWidth - 2f, 15f);

            return rt;
        }

        /// <summary>名字太长会撑破卡片，截断。</summary>
        private static string Shorten(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";

            // "电脑 1" 这种保持原样；其他过长的截断
            if (name.StartsWith("电脑")) return name;
            return name.Length <= 5 ? name : name.Substring(0, 5);
        }
    }
}
