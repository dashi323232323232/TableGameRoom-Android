using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TableGameRoom.Mobile.Core;

namespace TableGameRoom.Mobile.UI
{
    /// <summary>
    /// 座位卡片。
    ///
    ///   ┌────────────────────────┐
    ///   │        ╭─────╮          │  ← 倒计时气泡（思考时才出现）
    ///   │  ╭──╮  电脑 2 · 已看牌   │
    ///   │  │🙂│  [牌][牌]         │
    ///   │  ╰──╯  加注 $200        │
    ///   └────────────────────────┘
    ///
    /// 每行都带明确信息：
    ///   · 名字后面标「已看牌 / 未看牌」
    ///   · 底牌用小牌展示（未摊牌时是背面）
    ///   · 最后一行写清本轮具体动作和金额
    /// </summary>
    public static class SeatCard
    {
        public const float Width = TableRing.SeatWidth;

        public const float MiniCardWidth = 26f;
        public const float MiniCardHeight = 36f;

        private const float AvatarSize = 38f;

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
            bool hidden = true,
            bool looked = false,
            string lastAction = "",
            bool thinking = false,
            float thinkLeft = 0f)
        {
            var root = UiFactory.Node(parent, $"Seat_{name}");
            var rt = root;
            rt.sizeDelta = new Vector2(Width, TableRing.SeatHeight);

            // ---------- 卡片底 ----------
            var bg = UiFactory.Card(root,
                folded ? Theme.Hex("#EEF2F3") : Theme.Surface,
                isCurrent || thinking ? Theme.Accent : Theme.PanelBorder);

            var bgRt = bg.rectTransform;
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;

            if (isCurrent || thinking)
            {
                var outline = bg.gameObject.GetComponent<Outline>();
                if (outline != null)
                {
                    outline.effectColor = Theme.Accent;
                    outline.effectDistance = new Vector2(2.5f, -2.5f);
                }
            }

            // ---------- 头顶倒计时气泡 ----------
            // 画在卡片内部左上角（头像正上方），不覆盖名字那一行。
            // 名字从 textLeft 开始，气泡只占头像那一条竖线的宽度。
            if (thinking)
            {
                var bubble = UiFactory.Card(root,
                    thinkLeft <= 5f ? Theme.Danger : Theme.Accent, Color.white);
                var br = bubble.rectTransform;
                br.anchorMin = br.anchorMax = new Vector2(0f, 1f);
                br.pivot = new Vector2(0.5f, 1f);
                br.sizeDelta = new Vector2(46f, 22f);
                // 正对头像中线，紧贴卡片顶部
                br.anchoredPosition = new Vector2(6f + AvatarSize * 0.5f, -1f);

                var secs = UiFactory.Label(bubble.transform,
                    Mathf.CeilToInt(Mathf.Max(0f, thinkLeft)).ToString(),
                    15, Color.white, TextAnchor.MiddleCenter, bold: true);
                UiFactory.Fill(secs.rectTransform);
            }

            // ---------- 左侧头像 ----------
            var avatar = AvatarVisual.Create(root, name, emoji, seatIndex, false, AvatarSize);
            var ar = avatar;
            ar.anchorMin = ar.anchorMax = new Vector2(0f, 0.5f);
            ar.pivot = new Vector2(0f, 0.5f);
            ar.anchoredPosition = new Vector2(6f, 0f);

            if (folded)
            {
                var avImg = avatar.GetComponent<Image>();
                if (avImg != null) avImg.color = new Color(1f, 1f, 1f, 0.45f);
            }

            var textLeft = 6f + AvatarSize + 6f;
            var textWidth = Width - textLeft - 5f;

            // ---------- 第一行：名字 + 看牌状态 ----------
            var lookTag = folded ? "" : looked ? " · 已看牌" : " · 未看牌";
            var nameText = UiFactory.Label(root, Shorten(name) + lookTag, 15,
                folded ? Theme.Muted : Theme.Ink, TextAnchor.MiddleLeft, bold: true);
            var nr = nameText.rectTransform;
            nr.anchorMin = nr.anchorMax = new Vector2(0f, 1f);
            nr.pivot = new Vector2(0f, 1f);
            nr.sizeDelta = new Vector2(textWidth, 20f);
            nr.anchoredPosition = new Vector2(textLeft, -4f);

            // ---------- 第二行：底牌 ----------
            var row = UiFactory.Node(root, "Cards");
            var rr2 = row;
            rr2.anchorMin = rr2.anchorMax = new Vector2(0f, 1f);
            rr2.pivot = new Vector2(0f, 1f);
            rr2.sizeDelta = new Vector2(textWidth, MiniCardHeight);
            rr2.anchoredPosition = new Vector2(textLeft, -24f);

            if (cards == null || cards.Count == 0)
            {
                var dash = UiFactory.Label(row, "—", 14, Theme.Muted, TextAnchor.MiddleLeft);
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

            // ---------- 第三行：本轮动作 + 累计注额 ----------
            // 优先级：摊牌结果 > 本轮具体动作 > 状态
            string info;
            Color infoColor;

            if (folded)
            {
                info = "已弃牌";
                infoColor = Theme.Muted;
            }
            else if (!string.IsNullOrEmpty(result))
            {
                info = result;
                infoColor = isCurrent ? Theme.Accent : Theme.Ink;
            }
            else if (!string.IsNullOrEmpty(lastAction))
            {
                info = lastAction;
                infoColor = lastAction.StartsWith("加注") ? Theme.Danger
                          : lastAction.StartsWith("跟注") ? Theme.Accent
                          : Theme.Muted;
            }
            else if (committed > 0)
            {
                info = $"{status} · ${committed:N0}";
                infoColor = Theme.Muted;
            }
            else
            {
                info = status;
                infoColor = Theme.Muted;
            }

            var infoText = UiFactory.Label(root, info, 14, infoColor,
                TextAnchor.MiddleLeft, bold: !string.IsNullOrEmpty(lastAction) || !string.IsNullOrEmpty(result));

            var ir = infoText.rectTransform;
            ir.anchorMin = ir.anchorMax = new Vector2(0f, 0f);
            ir.pivot = new Vector2(0f, 0f);
            ir.sizeDelta = new Vector2(textWidth, 18f);
            ir.anchoredPosition = new Vector2(textLeft, 4f);

            return rt;
        }

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

        private static string Shorten(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            if (name.StartsWith("电脑")) return name;
            return name.Length <= 5 ? name : name.Substring(0, 5);
        }
    }
}
