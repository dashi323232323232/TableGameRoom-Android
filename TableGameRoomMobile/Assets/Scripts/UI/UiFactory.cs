using System;
using UnityEngine;
using UnityEngine.UI;

namespace TableGameRoom.Mobile.UI
{
    /// <summary>
    /// 纯代码构建 UGUI 的工具集。
    ///
    /// 布局约定（重要）：
    /// - 所有内容都放在 ScrollView 的 content（VerticalLayoutGroup + ContentSizeFitter）里
    /// - 需要固定高度的元素用 <see cref="Size"/> 设置 LayoutElement
    /// - **不要**给 LayoutGroup 的子元素再加 ContentSizeFitter（会和父布局打架）
    /// - 卡片如果需要「按内容撑高」，用 <see cref="AutoHeightCard"/>
    /// </summary>
    public static class UiFactory
    {
        private static Font _font;
        private static Sprite _roundedSprite;

        public static Font Font
        {
            get
            {
                if (_font != null) return _font;

                string[] candidates =
                {
                    "Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC",
                    "Noto Sans CJK SC", "Source Han Sans SC", "Droid Sans Fallback",
                    "Arial Unicode MS", "Arial"
                };

                _font = Font.CreateDynamicFontFromOSFont(candidates, 32);
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                return _font;
            }
        }

        public static Sprite RoundedSprite
        {
            get
            {
                if (_roundedSprite != null) return _roundedSprite;

                const int size = 64;
                const int radius = 20;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };

                var pixels = new Color32[size * size];
                for (var y = 0; y < size; y++)
                {
                    for (var x = 0; x < size; x++)
                    {
                        var cx = Mathf.Clamp(x, radius, size - radius);
                        var cy = Mathf.Clamp(y, radius, size - radius);
                        var dx = x - cx;
                        var dy = y - cy;
                        var dist = Mathf.Sqrt(dx * dx + dy * dy);
                        var alpha = Mathf.Clamp01(radius - dist + 0.5f);
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                    }
                }

                tex.SetPixels32(pixels);
                tex.Apply();

                _roundedSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                    100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
                return _roundedSprite;
            }
        }

        // ---------- 基础容器 ----------

        public static RectTransform Panel(Transform parent, string name = "Panel")
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static Image Background(Transform parent, Color color, string name = "Bg")
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        /// <summary>圆角卡片（固定高度，需配合 <see cref="Size"/> 或外部 LayoutGroup 使用）。</summary>
        public static Image Card(Transform parent, Color fill, Color? border = null, string name = "Card")
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var img = go.GetComponent<Image>();
            img.sprite = RoundedSprite;
            img.type = Image.Type.Sliced;
            img.color = fill;
            img.pixelsPerUnitMultiplier = 1f;

            if (border.HasValue)
            {
                var outline = go.AddComponent<Outline>();
                outline.effectColor = border.Value;
                outline.effectDistance = new Vector2(1.5f, -1.5f);
            }

            return img;
        }

        // ---------- 文本 ----------

        public static Text Label(
            Transform parent,
            string content,
            int fontSize = 30,
            Color? color = null,
            TextAnchor anchor = TextAnchor.UpperLeft,
            bool bold = false)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.font = Font;
            text.text = content;
            text.fontSize = fontSize;
            text.color = color ?? Theme.Ink;
            text.alignment = anchor;
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = true;
            text.lineSpacing = 1.2f;

            // 关键：给文本一个 LayoutElement，让父布局能算高度
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = fontSize * 1.35f;
            le.preferredHeight = fontSize * 1.35f;
            le.flexibleWidth = 1;

            return text;
        }

        /// <summary>多行文本（自动按内容撑高）。</summary>
        public static Text Paragraph(Transform parent, string content, int fontSize = 28, Color? color = null)
        {
            var text = Label(parent, content, fontSize, color);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            var fitter = text.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return text;
        }

        // ---------- 按钮 ----------

        /// <summary>圆角按钮（必须用 <see cref="Size"/> 指定尺寸，或在 LayoutGroup 里由 fitter 决定）。</summary>
        public static Button Button(
            Transform parent,
            string label,
            Action onClick,
            Color? fill = null,
            Color? textColor = null,
            int fontSize = 30)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var img = go.GetComponent<Image>();
            img.sprite = RoundedSprite;
            img.type = Image.Type.Sliced;
            img.color = fill ?? Theme.Panel;
            img.pixelsPerUnitMultiplier = 1f;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;

            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.90f, 0.94f, 0.97f);
            colors.pressedColor = new Color(0.80f, 0.86f, 0.91f);
            colors.disabledColor = new Color(1f, 1f, 1f, 0.4f);
            colors.fadeDuration = 0.08f;
            btn.colors = colors;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            var text = textGo.GetComponent<Text>();
            text.font = Font;
            text.text = label;
            text.fontSize = fontSize;
            text.color = textColor ?? Theme.Ink;
            text.alignment = TextAnchor.MiddleCenter;
            text.fontStyle = FontStyle.Normal;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(12, 6);
            textRect.offsetMax = new Vector2(-12, -6);

            if (onClick != null) btn.onClick.AddListener(() => onClick());
            return btn;
        }

        public static Button PrimaryButton(Transform parent, string label, Action onClick, int fontSize = 30) =>
            Button(parent, label, onClick, Theme.Accent, Color.white, fontSize);

        public static Button DangerButton(Transform parent, string label, Action onClick, int fontSize = 30) =>
            Button(parent, label, onClick, Theme.Danger, Color.white, fontSize);

        // ---------- 布局辅助 ----------

        public static RectTransform Stretch(RectTransform rt, float left = 0, float bottom = 0, float right = 0, float top = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        public static RectTransform Center(RectTransform rt, float width, float height)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        public static RectTransform TopBar(RectTransform rt, float height, float top = 0)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(0, -height - top);
            rt.offsetMax = new Vector2(0, -top);
            return rt;
        }

        /// <summary>纵向布局组。返回 LayoutGroup（用 .transform 拿 RectTransform）。</summary>
        public static VerticalLayoutGroup VerticalLayout(
            Transform parent, float spacing = 12, RectOffset padding = null,
            TextAnchor align = TextAnchor.UpperLeft, bool expandHeight = false)
        {
            var go = new GameObject("VLayout", typeof(RectTransform), typeof(VerticalLayoutGroup));
            go.transform.SetParent(parent, false);

            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding ?? new RectOffset(0, 0, 0, 0);
            layout.childAlignment = align;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = expandHeight;

            return layout;
        }

        /// <summary>横向布局组。</summary>
        public static HorizontalLayoutGroup HorizontalLayout(
            Transform parent, float spacing = 12, RectOffset padding = null,
            TextAnchor align = TextAnchor.MiddleLeft)
        {
            var go = new GameObject("HLayout", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(parent, false);

            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding ?? new RectOffset(0, 0, 0, 0);
            layout.childAlignment = align;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            return layout;
        }

        public static LayoutElement Size(GameObject go, float width = -1, float height = -1)
        {
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            if (width >= 0) { le.preferredWidth = width; le.minWidth = width; }
            if (height >= 0) { le.preferredHeight = height; le.minHeight = height; }
            return le;
        }

        /// <summary>让元素在父布局里占满剩余宽度。</summary>
        public static LayoutElement Flex(GameObject go, float widthWeight = 1)
        {
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = widthWeight;
            return le;
        }

        /// <summary>
        /// 滚动视图。返回 content 容器（**不带 LayoutGroup**，由 FlowLayout 手工排版）。
        /// </summary>
        public static RectTransform ScrollView(Transform parent, out ScrollRect scrollRect)
        {
            var root = new GameObject("ScrollView", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
            root.transform.SetParent(parent, false);

            var rootImg = root.GetComponent<Image>();
            rootImg.color = new Color(1, 1, 1, 0);

            var rootRect = (RectTransform)root.transform;
            Stretch(rootRect);

            scrollRect = root.GetComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Elastic;
            scrollRect.elasticity = 0.1f;
            scrollRect.scrollSensitivity = 40f;
            scrollRect.inertia = true;
            scrollRect.decelerationRate = 0.135f;

            // Viewport
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(root.transform, false);
            var vpImg = viewport.GetComponent<Image>();
            vpImg.color = new Color(1, 1, 1, 0.004f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var vpRect = (RectTransform)viewport.transform;
            vpRect.anchorMin = Vector2.zero;
            vpRect.anchorMax = Vector2.one;
            vpRect.offsetMin = Vector2.zero;
            vpRect.offsetMax = Vector2.zero;

            // Content —— 纯容器，高度由 FlowLayout.Finish() 设定
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);

            var contentRect = (RectTransform)content.transform;
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.offsetMin = new Vector2(0, 0);
            contentRect.offsetMax = new Vector2(0, 0);
            contentRect.sizeDelta = new Vector2(0, 0);

            scrollRect.viewport = vpRect;
            scrollRect.content = contentRect;

            return contentRect;
        }

        /// <summary>
        /// 自动高度卡片：外面套一层 LayoutGroup，内部再套一层，
        /// 由内部 LayoutGroup 的 preferredHeight 决定卡片高度。
        /// </summary>
        public static Image AutoHeightCard(
            Transform parent,
            Color fill,
            Color? border,
            out RectTransform inner,
            float spacing = 12,
            RectOffset padding = null)
        {
            var card = Card(parent, fill, border);
            padding ??= new RectOffset(30, 30, 26, 26);

            var layout = VerticalLayout(card.transform, spacing, padding);
            var layoutRect = (RectTransform)layout.transform;
            Stretch(layoutRect);

            inner = layoutRect;
            return card;
        }

        public static void Clear(Transform t)
        {
            for (var i = t.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }
    }
}
