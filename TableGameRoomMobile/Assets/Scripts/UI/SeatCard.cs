using System;
using UnityEngine;
using UnityEngine.UI;

namespace TableGameRoom.Mobile.UI
{
    /// <summary>
    /// 座位卡片 —— 统一牌桌上「一个玩家」的显示方式。
    ///
    /// 之前每个游戏各写一套，导致人一多就分不清谁是谁（名字、状态、筹码挤在一行）。
    /// 现在统一成竖排 + 卡片边框，信息层级清楚：
    ///
    ///   ┌─────────────┐
    ///   │   ◯  头像    │   ← 颜色区分玩家，表情反映状态
    ///   │    名字      │   ← 粗体，人少时更长
    ///   │   状态/注额   │   ← 弃牌变灰，当前行动高亮
    ///   └─────────────┘
    /// </summary>
    public static class SeatCard
    {
        /// <summary>创建座位卡片。返回卡片根节点。</summary>
        public static RectTransform Create(
            Transform parent,
            SeatGeometry geo,
            string name,
            string emoji,
            int seatIndex,
            bool isPlayer,
            string status,
            int committed,
            bool folded,
            bool isCurrent,
            string result = "")
        {
            var root = UiFactory.Node(parent, $"Seat_{name}");
            var rt = root;
            rt.sizeDelta = new Vector2(geo.CellWidth, geo.CellHeight);

            // ---------- 卡片底（区分当前行动 / 弃牌） ----------
            var bg = UiFactory.Card(root,
                folded ? Theme.WithAlpha(Theme.Panel, 0.55f) : Theme.Surface,
                isCurrent ? Theme.Accent : Theme.PanelBorder);

            var bgRt = bg.rectTransform;
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;

            // 当前行动：加粗边框 + 顶部高亮条
            if (isCurrent)
            {
                var outline = bg.gameObject.GetComponent<Outline>();
                if (outline != null)
                {
                    outline.effectColor = Theme.Accent;
                    outline.effectDistance = new Vector2(2.5f, -2.5f);
                }

                var bar = UiFactory.Rect(root, Theme.Accent, "TurnBar");
                var barRt = bar.rectTransform;
                barRt.anchorMin = new Vector2(0f, 1f);
                barRt.anchorMax = new Vector2(1f, 1f);
                barRt.pivot = new Vector2(0.5f, 1f);
                barRt.sizeDelta = new Vector2(0f, 5f);
                barRt.anchoredPosition = Vector2.zero;
            }

            // ---------- 头像（居中偏上） ----------
            var avatar = AvatarVisual.Create(root, name, emoji, seatIndex, isPlayer, geo.AvatarSize);
            var ar = avatar;
            ar.anchorMin = ar.anchorMax = new Vector2(0.5f, 1f);
            ar.pivot = new Vector2(0.5f, 1f);
            ar.anchoredPosition = new Vector2(0f, -8f);

            // ---------- 名字（头像下方，居中） ----------
            var nameY = -(8f + geo.AvatarSize + 2f);
            var nameText = UiFactory.Label(root, geo.ShortName(name), geo.NameFont,
                folded ? Theme.Muted : (isPlayer ? Theme.Accent : Theme.Ink),
                TextAnchor.UpperCenter, bold: true);

            var nr = nameText.rectTransform;
            nr.anchorMin = nr.anchorMax = new Vector2(0.5f, 1f);
            nr.pivot = new Vector2(0.5f, 1f);
            nr.sizeDelta = new Vector2(geo.CellWidth - 6f, geo.NameFont + 8f);
            nr.anchoredPosition = new Vector2(0f, nameY);

            // ---------- 状态 / 注额（最下） ----------
            var statusY = nameY - (geo.NameFont + 6f);

            var info = folded ? "已弃牌"
                : !string.IsNullOrEmpty(result) ? result
                : committed > 0 ? $"${committed:N0}"
                : status;

            var statusText = UiFactory.Label(root, info, geo.StatusFont,
                folded ? Theme.Muted
                    : isCurrent ? Theme.Accent
                    : !string.IsNullOrEmpty(result) ? Theme.Ink
                    : Theme.Muted,
                TextAnchor.UpperCenter, bold: isCurrent || !string.IsNullOrEmpty(result));

            var sr = statusText.rectTransform;
            sr.anchorMin = sr.anchorMax = new Vector2(0.5f, 1f);
            sr.pivot = new Vector2(0.5f, 1f);
            sr.sizeDelta = new Vector2(geo.CellWidth - 4f, geo.StatusFont + 8f);
            sr.anchoredPosition = new Vector2(0f, statusY);

            // 注额已经用文字显示了，不再叠筹码图形 ——
            // 小卡片里图形会和名字/状态抢位置，反而看不清是谁

            return rt;
        }

        /// <summary>创建「我」的座位卡片（横向：头像在左，牌在右）。</summary>
        public static RectTransform CreateSelf(
            Transform parent,
            float avatarSize,
            string emoji,
            string label,
            float width,
            float height)
        {
            var root = UiFactory.Node(parent, "SelfSeat");
            var rt = root;
            rt.sizeDelta = new Vector2(width, height);

            var avatar = AvatarVisual.Create(root, "你", emoji, 0, true, avatarSize);
            var ar = avatar;
            ar.anchorMin = ar.anchorMax = new Vector2(0f, 0.5f);
            ar.pivot = new Vector2(0f, 0.5f);
            ar.anchoredPosition = new Vector2(4f, 0f);

            if (!string.IsNullOrEmpty(label))
            {
                var t = UiFactory.Label(root, label, Theme.FontSmall - 4, Theme.Accent,
                    TextAnchor.MiddleLeft, bold: true);
                var tr = t.rectTransform;
                tr.anchorMin = tr.anchorMax = new Vector2(0f, 0f);
                tr.pivot = new Vector2(0f, 0f);
                tr.sizeDelta = new Vector2(180f, 30f);
                tr.anchoredPosition = new Vector2(avatarSize + 8f, 0f);
            }

            return rt;
        }
    }
}
