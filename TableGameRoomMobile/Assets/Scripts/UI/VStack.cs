using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TableGameRoom.Mobile.UI
{
    /// <summary>
    /// 竖直堆叠布局器 —— 取代 UGUI 的 VerticalLayoutGroup。
    /// 命名为 VStack 是为了避免与 System.Collections.Stack 冲突。
    ///
    /// 为什么不用 LayoutGroup：
    /// LayoutGroup 嵌套 ContentSizeFitter 时，Unity 的布局计算顺序会导致
    /// 高度塌陷、元素重叠（尤其是 ScrollView 里的多层嵌套）。
    /// 这里改为显式累加 Y 坐标，行为完全可预测。
    ///
    /// 用法：
    ///   var stack = new VStack(content);
    ///   stack.Text("标题", 42, bold: true);
    ///   stack.Button("确定", () => { });
    ///   stack.Card(inner => { ... });      // 卡片内容自动撑高
    ///   stack.Finish();                    // 提交 content 总高度
    /// </summary>
    public sealed class VStack
    {
        private readonly RectTransform _host;
        private readonly float _padL, _padR, _padT, _padB;
        private readonly float _gap;
        private float _y;          // 当前光标（负值向下累加）
        private bool _any;

        /// <summary>内容总高度。</summary>
        public float Height => -_y + _padB;

        /// <summary>宿主 RectTransform（用于创建子元素）。</summary>
        public RectTransform Host => _host;

        /// <summary>可用宽度。</summary>
        public float Width
        {
            get
            {
                var w = _host.rect.width;
                if (w <= 1f) w = 1080f;   // Canvas 尚未布局时的兜底
                return w - _padL - _padR;
            }
        }

        public VStack(RectTransform host, float padding = Theme.PagePadding, float gap = Theme.ItemGap,
            float bottomPadding = 160f)
        {
            _host = host;
            _padL = _padR = padding;
            _padT = padding;
            _padB = bottomPadding;
            _gap = gap;
            _y = -_padT;

            // 注意：不覆盖 host 的锚点！
            // ScrollPage 已经把 content 设成 anchorMin/Max=(0,1)/(1,1), pivot=(0.5,1)，
            // 卡片内部的 inner 由 Fill() 铺满，锚点也是对的。
            // 只有 host 的 sizeDelta 在 Finish() 里设置。
        }

        // ==================== 核心 ====================

        /// <summary>把元素放到下一行，占满可用宽度。</summary>
        public RectTransform Add(RectTransform element, float height)
        {
            if (_any) _y -= _gap;
            _any = true;

            element.SetParent(_host, false);
            element.anchorMin = new Vector2(0f, 1f);
            element.anchorMax = new Vector2(1f, 1f);
            element.pivot = new Vector2(0.5f, 1f);
            element.offsetMin = new Vector2(_padL, 0f);
            element.offsetMax = new Vector2(-_padR, 0f);
            element.anchoredPosition = new Vector2(0f, _y);
            element.sizeDelta = new Vector2(element.sizeDelta.x, height);

            _y -= height;
            return element;
        }

        // ==================== 快捷方法 ====================

        /// <summary>标题（大号粗体）。</summary>
        public Text Title(string text, Color? color = null) =>
            Text(text, Theme.FontHuge, color, bold: true, minHeight: 76f);

        /// <summary>小标题（中号粗体）。</summary>
        public Text Head(string text, Color? color = null) =>
            Text(text, Theme.FontTitle, color, bold: true, minHeight: 60f);

        /// <summary>正文段落（自动按字数算高度）。</summary>
        public Text Text(string text, int size = Theme.FontBody, Color? color = null,
            bool bold = false, float minHeight = 0f)
        {
            var label = UiFactory.Label(_host, text, size, color, TextAnchor.UpperLeft, bold);

            var h = MeasureText(label, Width);
            if (h < minHeight) h = minHeight;

            Add(label.rectTransform, h);
            return label;
        }

        /// <summary>居中文本（用于状态/结果提示）。</summary>
        public Text CenterText(string text, int size = Theme.FontHead, Color? color = null,
            bool bold = false, float height = 0f)
        {
            var label = UiFactory.Label(_host, text, size, color, TextAnchor.MiddleCenter, bold);
            Add(label.rectTransform, height > 0 ? height : size * 1.6f);
            return label;
        }

        /// <summary>按钮。</summary>
        public Button Button(string label, Action onClick, float height = Theme.ButtonHeight,
            Color? fill = null, Color? textColor = null, int fontSize = Theme.FontBody)
        {
            var btn = UiFactory.Button(_host, label, onClick, fill, textColor, fontSize);
            Add(btn.GetComponent<RectTransform>(), height);
            return btn;
        }

        /// <summary>主按钮。</summary>
        public Button Primary(string label, Action onClick, float height = Theme.ButtonHeight)
        {
            var btn = UiFactory.Primary(_host, label, onClick);
            Add(btn.GetComponent<RectTransform>(), height);
            return btn;
        }

        /// <summary>危险按钮。</summary>
        public Button Danger(string label, Action onClick, float height = Theme.ButtonHeight)
        {
            var btn = UiFactory.Danger(_host, label, onClick);
            Add(btn.GetComponent<RectTransform>(), height);
            return btn;
        }

        /// <summary>
        /// 按钮网格（自动换行）。
        /// </summary>
        public RectTransform Grid(IList<Button> buttons, int columns, float cellHeight = Theme.ButtonHeightSmall,
            float gap = 10f)
        {
            if (buttons == null || buttons.Count == 0) return null;

            if (_any) _y -= _gap;
            _any = true;

            var totalWidth = Width;
            var rows = Mathf.CeilToInt(buttons.Count / (float)columns);
            var cellWidth = (totalWidth - gap * (columns - 1)) / columns;
            var totalHeight = rows * cellHeight + (rows - 1) * gap;

            var gridGo = new GameObject("Grid", typeof(RectTransform));
            gridGo.transform.SetParent(_host, false);
            var gridRect = (RectTransform)gridGo.transform;

            gridRect.anchorMin = new Vector2(0f, 1f);
            gridRect.anchorMax = new Vector2(0f, 1f);
            gridRect.pivot = new Vector2(0f, 1f);
            gridRect.anchoredPosition = new Vector2(_padL, _y);
            gridRect.sizeDelta = new Vector2(totalWidth, totalHeight);

            for (var i = 0; i < buttons.Count; i++)
            {
                var row = i / columns;
                var col = i % columns;

                var rt = buttons[i].GetComponent<RectTransform>();
                rt.SetParent(gridRect, false);
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(col * (cellWidth + gap), -(row * (cellHeight + gap)));
                rt.sizeDelta = new Vector2(cellWidth, cellHeight);
            }

            _y -= totalHeight;
            return gridRect;
        }

        /// <summary>
        /// 卡片：内部用子 Stack 排版，卡片高度自动适配子内容。
        /// buildInner 可返回一个期望的最小高度（返回 0 表示按内容算）。
        /// </summary>
        public RectTransform Card(
            Func<VStack, float> buildInner,
            Color? fill = null,
            Color? border = null,
            float innerPadding = 26f,
            float innerGap = 12f)
        {
            if (_any) _y -= _gap;
            _any = true;

            var card = UiFactory.Card(_host, fill ?? Theme.Panel, border ?? Theme.PanelBorder);
            var cardRect = card.rectTransform;

            cardRect.anchorMin = new Vector2(0f, 1f);
            cardRect.anchorMax = new Vector2(1f, 1f);
            cardRect.pivot = new Vector2(0.5f, 1f);
            cardRect.offsetMin = new Vector2(_padL, 0f);
            cardRect.offsetMax = new Vector2(-_padR, 0f);
            cardRect.anchoredPosition = new Vector2(0f, _y);

            var innerGo = new GameObject("Inner", typeof(RectTransform));
            innerGo.transform.SetParent(cardRect, false);
            var innerRect = (RectTransform)innerGo.transform;
            UiFactory.Fill(innerRect);

            var innerStack = new VStack(innerRect, innerPadding, innerGap, innerPadding);
            var hint = buildInner?.Invoke(innerStack) ?? 0f;

            var cardHeight = Mathf.Max(innerStack.Height, hint);
            cardRect.sizeDelta = new Vector2(cardRect.sizeDelta.x, cardHeight);

            _y -= cardHeight;
            return cardRect;
        }

        /// <summary>
        /// 卡片网格：每行放 columns 个卡片，等宽等高。
        ///
        /// 用于大厅的游戏列表 —— 两列布局能让 5 个游戏一屏显示完，不需要滚动。
        /// </summary>
        /// <param name="items">每项是一个构建回调，参数是卡片内部容器</param>
        /// <param name="columns">每行几个</param>
        /// <param name="cardHeight">每个卡片的固定高度</param>
        /// <param name="gap">卡片间距</param>
        public void CardGrid(
            IList<Action<RectTransform>> items,
            int columns,
            float cardHeight,
            float gap = 14f,
            Color? fill = null,
            Color? border = null,
            float innerPadding = 20f)
        {
            if (items == null || items.Count == 0) return;

            if (_any) _y -= _gap;
            _any = true;

            var totalWidth = Width;
            var rows = Mathf.CeilToInt(items.Count / (float)columns);
            var cardWidth = (totalWidth - gap * (columns - 1)) / columns;
            var totalHeight = rows * cardHeight + (rows - 1) * gap;

            // 容器
            var gridGo = new GameObject("CardGrid", typeof(RectTransform));
            gridGo.transform.SetParent(_host, false);
            var gridRect = (RectTransform)gridGo.transform;

            gridRect.anchorMin = new Vector2(0f, 1f);
            gridRect.anchorMax = new Vector2(0f, 1f);
            gridRect.pivot = new Vector2(0f, 1f);
            gridRect.anchoredPosition = new Vector2(_padL, _y);
            gridRect.sizeDelta = new Vector2(totalWidth, totalHeight);

            for (var i = 0; i < items.Count; i++)
            {
                var row = i / columns;
                var col = i % columns;

                var card = UiFactory.Card(gridRect, fill ?? Theme.Panel, border ?? Theme.PanelBorder);
                var cardRect = card.rectTransform;

                cardRect.anchorMin = cardRect.anchorMax = new Vector2(0f, 1f);
                cardRect.pivot = new Vector2(0f, 1f);
                cardRect.anchoredPosition = new Vector2(
                    col * (cardWidth + gap),
                    -(row * (cardHeight + gap)));
                cardRect.sizeDelta = new Vector2(cardWidth, cardHeight);

                // 内部容器
                var innerGo = new GameObject("Inner", typeof(RectTransform));
                innerGo.transform.SetParent(cardRect, false);
                var innerRect = (RectTransform)innerGo.transform;
                UiFactory.Fill(innerRect, innerPadding, innerPadding, innerPadding, innerPadding);

                items[i]?.Invoke(innerRect);
            }

            _y -= totalHeight;
        }

        /// <summary>
        /// 固定高度的卡片：内部不做竖直排布，用绝对定位摆放元素。
        /// 适用于牌桌这类需要精确控制布局的区域。
        /// </summary>
        public RectTransform FixedCard(
            Action<RectTransform> buildInner,
            float height,
            Color? fill = null,
            Color? border = null,
            float innerPadding = 20f)
        {
            if (_any) _y -= _gap;
            _any = true;

            var card = UiFactory.Card(_host, fill ?? Theme.Table, border ?? Theme.TableBorder);
            var cardRect = card.rectTransform;

            cardRect.anchorMin = new Vector2(0f, 1f);
            cardRect.anchorMax = new Vector2(1f, 1f);
            cardRect.pivot = new Vector2(0.5f, 1f);
            cardRect.offsetMin = new Vector2(_padL, 0f);
            cardRect.offsetMax = new Vector2(-_padR, 0f);
            cardRect.anchoredPosition = new Vector2(0f, _y);
            cardRect.sizeDelta = new Vector2(cardRect.sizeDelta.x, height);

            // 内部容器：铺满卡片，留内边距
            var innerGo = new GameObject("Inner", typeof(RectTransform));
            innerGo.transform.SetParent(cardRect, false);
            var innerRect = (RectTransform)innerGo.transform;
            UiFactory.Fill(innerRect, innerPadding, innerPadding, innerPadding, innerPadding);

            buildInner?.Invoke(innerRect);

            _y -= height;
            return cardRect;
        }

        /// <summary>卡片（无返回值重载，按内容撑高）。</summary>
        public RectTransform Card(
            Action<VStack> buildInner,
            Color? fill = null,
            Color? border = null,
            float innerPadding = 26f,
            float innerGap = 12f)
        {            return Card(s =>
            {
                buildInner?.Invoke(s);
                return 0f;
            }, fill, border, innerPadding, innerGap);
        }

        /// <summary>
        /// 横向按钮行（不换行，均分宽度）。
        /// </summary>
        public RectTransform RowOf(IList<Button> buttons, float height = Theme.ButtonHeight, float gap = 12f)
        {
            if (buttons == null || buttons.Count == 0) return null;

            if (_any) _y -= _gap;
            _any = true;

            var totalWidth = Width;
            var count = buttons.Count;
            var cellWidth = (totalWidth - gap * (count - 1)) / count;

            var rowGo = new GameObject("Row", typeof(RectTransform));
            rowGo.transform.SetParent(_host, false);
            var rowRect = (RectTransform)rowGo.transform;

            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(0f, 1f);
            rowRect.pivot = new Vector2(0f, 1f);
            rowRect.anchoredPosition = new Vector2(_padL, _y);
            rowRect.sizeDelta = new Vector2(totalWidth, height);

            for (var i = 0; i < count; i++)
            {
                var rt = buttons[i].GetComponent<RectTransform>();
                rt.SetParent(rowRect, false);
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(i * (cellWidth + gap), 0f);
                rt.sizeDelta = new Vector2(cellWidth, height);
            }

            _y -= height;
            return rowRect;
        }

        /// <summary>分隔线。</summary>
        public void Divider(float alpha = 0.5f)
        {
            var line = UiFactory.Rect(_host, new Color(0.8f, 0.86f, 0.88f, alpha), "Divider");
            Add(line.rectTransform, 2f);
        }

        /// <summary>提交总高度，让 ScrollView 可以滚动。</summary>
        public void Finish()
        {
            _host.sizeDelta = new Vector2(0f, Height);

            // 显式归零：pivot 在顶部时，anchoredPosition.y = 0 表示内容顶部与视口顶部对齐
            _host.anchoredPosition = new Vector2(_host.anchoredPosition.x, 0f);

            // 内容高度变化后，通知滚动容器重算范围并归位顶部
            var scroller = _host.GetComponentInParent<SimpleScroll>();
            if (scroller != null) scroller.Refresh(resetToTop: true);
        }

        // ==================== 文本高度估算 ====================

        /// <summary>按字符宽度估算文本所需高度。</summary>
        public static float MeasureText(Text text, float width)
        {
            if (text == null) return 40f;
            if (string.IsNullOrEmpty(text.text)) return text.fontSize * 1.5f;
            if (width <= 1f) width = 1000f;

            var lines = 0;
            foreach (var para in text.text.Split('\n'))
            {
                var w = 0f;
                var lineCount = 1;

                foreach (var ch in para)
                {
                    // 中日韩字符宽约等于字号；ASCII 约 0.55 倍
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
