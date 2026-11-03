using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TableGameRoom.Mobile.Core;

namespace TableGameRoom.Mobile.UI
{
    /// <summary>
    /// 座位卡片 —— 与桌面版 Poker.cs 的 SeatView 对齐。
    ///
    /// 桌面版结构（宽 132）：
    ///   名字 + 表情        11pt
    ///   两张牌（compact 22×30）
    ///   状态 · 注额        9pt
    ///   筹码图形
    ///
    /// 安卓照搬这个结构，尺寸按手机缩放：牌 30×42，整卡 132×100。
    /// 每个玩家一个独立框，人多也分得清，牌也不会被吞掉。
    /// </summary>
    public static class SeatCard
    {
        /// <summary>座位卡片尺寸（与 TableRing.SeatWidth 对应）。</summary>
        public const float Width = TableRing.SeatWidth;

        /// <summary>卡片内小牌尺寸（桌面版 compact 是 22×30，这里按比例放大）。</summary>
        public const float MiniCardWidth = 30f;
        public const float MiniCardHeight = 42f;

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

            // ---------- 第一行：名字 ----------
            var nameText = UiFactory.Label(root, Shorten(name), 19,
                folded ? Theme.Muted : Theme.Ink, TextAnchor.MiddleCenter, bold: true);
            var nr = nameText.rectTransform;
            nr.anchorMin = nr.anchorMax = new Vector2(0.5f, 1f);
            nr.pivot = new Vector2(0.5f, 1f);
            nr.sizeDelta = new Vector2(Width - 8f, 24f);
            nr.anchoredPosition = new Vector2(0f, -3f);

            // ---------- 第二行：底牌（桌面版 CardRow(compact)） ----------
            var row = UiFactory.Node(root, "Cards");
            var rr2 = row;
            rr2.anchorMin = rr2.anchorMax = new Vector2(0.5f, 1f);
            rr2.pivot = new Vector2(0.5f, 1f);
            rr2.sizeDelta = new Vector2(Width - 6f, MiniCardHeight);
            rr2.anchoredPosition = new Vector2(0f, -27f);

            if (cards == null || cards.Count == 0)
            {
                var dash = UiFactory.Label(row, "—", 18, Theme.Muted, TextAnchor.MiddleCenter);
                UiFactory.Fill(dash.rectTransform);
            }
            else
            {
                const float gap = 4f;
                var total = cards.Count * MiniCardWidth + (cards.Count - 1) * gap;
                var x = -total * 0.5f + MiniCardWidth * 0.5f;
                for (var i = 0; i < cards.Count; i++)
                {
                    var mini = MiniCard(row, cards[i], hidden);
                    mini.anchorMin = mini.anchorMax = new Vector2(0.5f, 0.5f);
                    mini.pivot = new Vector2(0.5f, 0.5f);
                    mini.anchoredPosition = new Vector2(x + i * (MiniCardWidth + gap), 0f);
                }
            }

            // ---------- 第三行：状态 · 注额 ----------
            var info = folded ? "已弃牌"
                : !string.IsNullOrEmpty(result) ? result
                : committed > 0 ? $"{status} · ${committed:N0}"
                : status;

            var infoText = UiFactory.Label(root, info, 16,
                folded ? Theme.Muted : isCurrent ? Theme.Accent : Theme.Muted,
                TextAnchor.MiddleCenter, bold: isCurrent || !string.IsNullOrEmpty(result));

            var ir = infoText.rectTransform;
            ir.anchorMin = ir.anchorMax = new Vector2(0.5f, 0f);
            ir.pivot = new Vector2(0.5f, 0f);
            ir.sizeDelta = new Vector2(Width - 6f, 20f);
            ir.anchoredPosition = new Vector2(0f, 3f);

            // 注额已用文字表达，卡片内不再叠筹码图形
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
                var q = UiFactory.Label(go.transform, "?", 22, Color.white,
                    TextAnchor.MiddleCenter, bold: true);
                UiFactory.Fill(q.rectTransform);
                return rt;
            }

            var color = card.IsRed ? Theme.CardRed : Theme.CardBlack;

            var rank = UiFactory.Label(go.transform, card.RankText, 19, color,
                TextAnchor.MiddleCenter, bold: true);
            var rr = rank.rectTransform;
            rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 0.64f);
            rr.sizeDelta = new Vector2(MiniCardWidth - 2f, 24f);

            var suit = UiFactory.Label(go.transform, card.SuitName, 12, color,
                TextAnchor.MiddleCenter);
            var sr = suit.rectTransform;
            sr.anchorMin = sr.anchorMax = new Vector2(0.5f, 0.28f);
            sr.sizeDelta = new Vector2(MiniCardWidth - 2f, 18f);

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
