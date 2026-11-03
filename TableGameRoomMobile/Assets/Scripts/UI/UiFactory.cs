using System;
using UnityEngine;
using UnityEngine.UI;

namespace TableGameRoom.Mobile.UI
{
    /// <summary>
    /// 纯代码构建 UGUI 的工具库。
    ///
    /// 核心原则：
    /// 1. **不用 UGUI 的 LayoutGroup**（嵌套时高度会塌陷），全部手工计算坐标
    /// 2. 竖直排布用 <see cref="VStack"/>
    /// 3. 横向排列用 <see cref="Row"/>
    /// </summary>
    public static class UiFactory
    {
        // ==================== 资源缓存 ====================

        private static Font _font;
        private static Sprite _roundSprite;
        private static Sprite _circleSprite;

        /// <summary>中文字体（优先系统字体）。</summary>
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

        /// <summary>圆角矩形（9-slice）。</summary>
        public static Sprite RoundSprite
        {
            get
            {
                if (_roundSprite != null) return _roundSprite;

                const int size = 64;
                const int radius = 18;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };

                var px = new Color32[size * size];
                for (var y = 0; y < size; y++)
                {
                    for (var x = 0; x < size; x++)
                    {
                        var cx = Mathf.Clamp(x, radius, size - radius);
                        var cy = Mathf.Clamp(y, radius, size - radius);
                        var dx = x - cx;
                        var dy = y - cy;
                        var dist = Mathf.Sqrt(dx * dx + dy * dy);
                        var a = Mathf.Clamp01(radius - dist + 0.5f);
                        px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                    }
                }

                tex.SetPixels32(px);
                tex.Apply();

                _roundSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                    100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
                return _roundSprite;
            }
        }

        /// <summary>实心圆。</summary>
        public static Sprite CircleSprite
        {
            get
            {
                if (_circleSprite != null) return _circleSprite;

                const int size = 128;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var px = new Color32[size * size];
                var c = (size - 1) / 2f;

                for (var y = 0; y < size; y++)
                {
                    for (var x = 0; x < size; x++)
                    {
                        var d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                        var a = Mathf.Clamp01(c - d + 0.5f);
                        px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                    }
                }

                tex.SetPixels32(px);
                tex.Apply();
                _circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
                return _circleSprite;
            }
        }

        // ==================== 基础元素 ====================

        public static RectTransform Node(Transform parent, string name = "Node")
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static Image Rect(Transform parent, Color color, string name = "Rect")
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>圆角卡片。</summary>
        public static Image Card(Transform parent, Color fill, Color? border = null, string name = "Card")
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var img = go.GetComponent<Image>();
            img.sprite = RoundSprite;
            img.type = Image.Type.Sliced;
            img.color = fill;
            img.pixelsPerUnitMultiplier = 1f;
            img.raycastTarget = false;

            if (border.HasValue)
            {
                var outline = go.AddComponent<Outline>();
                outline.effectColor = border.Value;
                outline.effectDistance = new Vector2(1.5f, -1.5f);
                outline.useGraphicAlpha = false;
            }

            return img;
        }

        /// <summary>文本。用 .rectTransform 拿 RectTransform。</summary>
        public static Text Label(
            Transform parent,
            string content,
            int size = Theme.FontBody,
            Color? color = null,
            TextAnchor anchor = TextAnchor.UpperLeft,
            bool bold = false)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.font = Font;
            text.text = content;
            text.fontSize = size;
            text.color = color ?? Theme.Ink;
            text.alignment = anchor;
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = true;
            text.lineSpacing = 1.25f;
            text.raycastTarget = false;

            return text;
        }

        /// <summary>按钮。</summary>
        public static Button Button(
            Transform parent,
            string label,
            Action onClick,
            Color? fill = null,
            Color? textColor = null,
            int fontSize = Theme.FontBody)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var img = go.GetComponent<Image>();
            img.sprite = RoundSprite;
            img.type = Image.Type.Sliced;
            img.color = fill ?? Theme.Panel;
            img.pixelsPerUnitMultiplier = 1f;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;

            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.92f, 0.95f, 0.98f);
            colors.pressedColor = new Color(0.82f, 0.88f, 0.93f);
            colors.disabledColor = new Color(1f, 1f, 1f, 0.35f);
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
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            var tr = textGo.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(10, 4);
            tr.offsetMax = new Vector2(-10, -4);

            // 让拖拽事件能冒泡到滚动容器（否则按钮会吃掉拖拽，页面无法滚动）
            go.AddComponent<DragPassThrough>();

            if (onClick != null) btn.onClick.AddListener(() => onClick());
            return btn;
        }

        public static Button Primary(Transform parent, string label, Action onClick, int fontSize = Theme.FontBody) =>
            Button(parent, label, onClick, Theme.Accent, Color.white, fontSize);

        public static Button Danger(Transform parent, string label, Action onClick, int fontSize = Theme.FontBody) =>
            Button(parent, label, onClick, Theme.Danger, Color.white, fontSize);

        // ==================== 定位辅助 ====================

        public static RectTransform Fill(RectTransform rt, float left = 0, float bottom = 0, float right = 0, float top = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        public static RectTransform TopStretch(RectTransform rt, float height, float topOffset = 0,
            float left = 0, float right = 0)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(left, -height - topOffset);
            rt.offsetMax = new Vector2(-right, -topOffset);
            return rt;
        }

        public static RectTransform BottomStretch(RectTransform rt, float height, float bottomOffset = 0,
            float left = 0, float right = 0)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(left, bottomOffset);
            rt.offsetMax = new Vector2(-right, bottomOffset + height);
            return rt;
        }

        public static RectTransform Center(RectTransform rt, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        /// <summary>按绝对位置放置（左上角为原点，y 向下为正）。</summary>
        public static RectTransform Place(RectTransform rt, float x, float y, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(width, height);
            return rt;
        }

        // ==================== 滚动页 ====================

        /// <summary>
        /// 创建滚动页。返回 content 容器，配合 <see cref="VStack"/> 使用。
        /// 用自写的 <see cref="SimpleScroll"/> 而不是 ScrollRect（见该类注释）。
        /// </summary>
        public static RectTransform ScrollPage(Transform parent, out SimpleScroll scroller)
        {
            return SimpleScroll.Create(parent, out scroller);
        }

        public static void Clear(Transform t)
        {
            if (t == null) return;
            for (var i = t.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }
    }
}
