using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TableGameRoom.Mobile.UI
{
    /// <summary>
    /// 竖排流式布局器 —— 手工计算 Y 坐标，不用 UGUI 的 LayoutGroup。
    ///
    /// 为什么不用 LayoutGroup：
    /// Unity 的 LayoutGroup + ContentSizeFitter 嵌套时很容易出现「高度塌陷 / 元素重叠」，
    /// 尤其是 ScrollView 里再嵌 LayoutGroup 的情况。桌面版是 WPF 的 StackPanel 自动撑高，
    /// 这里用显式累加 Y 坐标的方式模拟同样效果，行为完全可预测。
    ///
    /// 关键点：锚点统一用 (0,1)-(1,1) + pivot(0.5,1)，
    /// 只设置 offsetMin.x / offsetMax.x 控制左右边距，
    /// 高度用 sizeDelta.y，位置用 anchoredPosition.y。
    /// </summary>
    public sealed class FlowLayout
    {
        private readonly RectTransform _host;
        private readonly float _paddingLeft;
        private readonly float _paddingRight;
        private readonly float _paddingTop;
        private readonly float _paddingBottom;
        private readonly float _spacing;
        private float _cursorY;

        public float TotalHeight => _paddingTop + (-_cursorY - _paddingTop) + _paddingBottom;

        public FlowLayout(RectTransform host, float padding = 28, float spacing = 16, float paddingBottom = 60)
        {
            _host = host;
            _paddingLeft = padding;
            _paddingRight = padding;
            _paddingTop = padding;
            _paddingBottom = paddingBottom;
            _spacing = spacing;
            _cursorY = -padding;
        }

        /// <summary>可用宽度（host 宽度 - 左右 padding）。</summary>
        public float ContentWidth
        {
            get
            {
                var w = _host.rect.width;
                if (w <= 1f) w = 1080f;
                return w - _paddingLeft - _paddingRight;
            }
        }

        /// <summary>添加一个固定高度的元素。</summary>
        public RectTransform Add(RectTransform element, float height)
        {
            if (_cursorY < -_paddingTop) _cursorY -= _spacing;

            element.SetParent(_host, false);

            // 锚点：左右拉伸到父级宽度，顶部对齐
            element.anchorMin = new Vector2(0f, 1f);
            element.anchorMax = new Vector2(1f, 1f);
            element.pivot = new Vector2(0.5f, 1f);

            // 左右边距
            element.offsetMin = new Vector2(_paddingLeft, 0f);
            element.offsetMax = new Vector2(-_paddingRight, 0f);

            // 高度与位置（必须在设置完 offset 之后）
            element.sizeDelta = new Vector2(element.sizeDelta.x, height);
            element.anchoredPosition = new Vector2(0f, _cursorY);

            _cursorY -= height;
            return element;
        }

        /// <summary>添加文本，高度按字号 × 行数自动估算。</summary>
        public RectTransform AddText(Text text, float explicitHeight = -1)
        {
            var height = explicitHeight > 0 ? explicitHeight : EstimateTextHeight(text, ContentWidth);
            return Add(text.rectTransform, height);
        }

        /// <summary>添加按钮（固定高度）。</summary>
        public RectTransform AddButton(Button button, float height = 96)
        {
            return Add(button.GetComponent<RectTransform>(), height);
        }

        /// <summary>
        /// 添加一组按钮（自动换行网格）。
        /// 每行放 columns 个，宽度均分；总高度按行数计算。
        /// </summary>
        public RectTransform AddGrid(IList<Button> buttons, int columns, float cellHeight = 86,
            float gap = 10)
        {
            if (buttons == null || buttons.Count == 0) return null;

            if (_cursorY < -_paddingTop) _cursorY -= _spacing;

            var totalWidth = ContentWidth;
            var rows = Mathf.CeilToInt(buttons.Count / (float)columns);
            var cellWidth = (totalWidth - gap * (columns - 1)) / columns;
            var totalHeight = rows * cellHeight + (rows - 1) * gap;

            // 用一个容器承载整块网格
            var gridGo = new GameObject("Grid", typeof(RectTransform));
            gridGo.transform.SetParent(_host, false);
            var gridRect = (RectTransform)gridGo.transform;

            gridRect.anchorMin = new Vector2(0f, 1f);
            gridRect.anchorMax = new Vector2(0f, 1f);
            gridRect.pivot = new Vector2(0f, 1f);
            gridRect.anchoredPosition = new Vector2(_paddingLeft, _cursorY);
            gridRect.sizeDelta = new Vector2(totalWidth, totalHeight);

            for (var i = 0; i < buttons.Count; i++)
            {
                var row = i / columns;
                var col = i % columns;

                var btnRect = buttons[i].GetComponent<RectTransform>();
                btnRect.SetParent(gridRect, false);

                // 用左上角锚点 + pivot，按格子绝对定位
                btnRect.anchorMin = new Vector2(0f, 1f);
                btnRect.anchorMax = new Vector2(0f, 1f);
                btnRect.pivot = new Vector2(0f, 1f);
                btnRect.anchoredPosition = new Vector2(
                    col * (cellWidth + gap),
                    -(row * (cellHeight + gap)));
                btnRect.sizeDelta = new Vector2(cellWidth, cellHeight);
            }

            _cursorY -= totalHeight;
            return gridRect;
        }

        /// <summary>
        /// 添加卡片：内部用子 FlowLayout 排版，卡片高度由子布局决定。
        /// </summary>
        public RectTransform AddCard(
            Func<RectTransform, FlowLayout, float> buildInner,
            Color fill,
            Color? border = null,
            float innerPadding = 26,
            float innerSpacing = 12)
        {
            if (_cursorY < -_paddingTop) _cursorY -= _spacing;

            var card = UiFactory.Card(_host, fill, border);
            var cardRect = card.rectTransform;

            cardRect.anchorMin = new Vector2(0f, 1f);
            cardRect.anchorMax = new Vector2(1f, 1f);
            cardRect.pivot = new Vector2(0.5f, 1f);
            cardRect.offsetMin = new Vector2(_paddingLeft, 0f);
            cardRect.offsetMax = new Vector2(-_paddingRight, 0f);
            cardRect.anchoredPosition = new Vector2(0f, _cursorY);

            // 内部容器
            var innerGo = new GameObject("Inner", typeof(RectTransform));
            innerGo.transform.SetParent(cardRect, false);
            var innerRect = (RectTransform)innerGo.transform;

            innerRect.anchorMin = Vector2.zero;
            innerRect.anchorMax = Vector2.one;
            innerRect.offsetMin = Vector2.zero;
            innerRect.offsetMax = Vector2.zero;

            var innerFlow = new FlowLayout(innerRect, innerPadding, innerSpacing, innerPadding);
            buildInner(innerRect, innerFlow);

            // 卡片高度 = 子内容总高
            var cardHeight = innerFlow.TotalHeight;
            cardRect.sizeDelta = new Vector2(cardRect.sizeDelta.x, cardHeight);

            _cursorY -= cardHeight;
            return cardRect;
        }

        /// <summary>结束排版：设置 host 高度，让 ScrollView 能滚动。</summary>
        public void Finish()
        {
            var h = TotalHeight;
            _host.sizeDelta = new Vector2(0f, h);
        }

        /// <summary>估算文本高度（按字符宽度粗略折行）。</summary>
        public static float EstimateTextHeight(Text text, float width)
        {
            if (text == null) return 40f;
            if (string.IsNullOrEmpty(text.text)) return text.fontSize * 1.4f;
            if (width <= 1f) width = 1000f;

            var lines = 0;
            foreach (var para in text.text.Split('\n'))
            {
                var w = 0f;
                var lineCount = 1;
                foreach (var ch in para)
                {
                    var cw = ch > 0x2E80 ? text.fontSize : text.fontSize * 0.55f;
                    if (w + cw > width)
                    {
                        lineCount++;
                        w = cw;
                    }
                    else w += cw;
                }
                lines += lineCount;
            }

            return lines * text.fontSize * text.lineSpacing + text.fontSize * 0.4f;
        }
    }
}
