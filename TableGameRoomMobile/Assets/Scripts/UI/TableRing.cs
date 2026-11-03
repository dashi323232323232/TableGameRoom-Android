using UnityEngine;

namespace TableGameRoom.Mobile.UI
{
    /// <summary>
    /// 椭圆牌桌的座位定位 —— 与桌面版 Core.cs 的 TableRing 完全一致。
    ///
    /// 桌面版用的是「归一化坐标 + 横向椭圆」，座位环绕在椭圆外圈，
    /// 玩家自己固定在底部中央。这样人多也不会挤成一排，视觉上像真牌桌。
    ///
    /// 归一化坐标 (0..1)：
    ///   x = 0 最左，1 最右
    ///   y = 0 最上，1 最下
    /// </summary>
    public static class TableRing
    {
        /// <summary>座位卡片的基准宽度（与桌面版一致）。</summary>
        public const float SeatWidth = 132f;

        /// <summary>座位卡片高度（名字 + 底牌 + 状态三行）。</summary>
        public const float SeatHeight = 100f;

        /// <summary>椭圆在容器里的占比（与桌面版一致）。</summary>
        public const float EllipseWidthRatio = 0.78f;
        public const float EllipseHeightRatio = 0.62f;
        public const float EllipseLeftRatio = 0.11f;
        public const float EllipseTopRatio = 0.14f;

        /// <summary>玩家自己的位置（底部中央）。</summary>
        public static readonly Vector2 PlayerPos = new Vector2(0.50f, 0.84f);

        /// <summary>
        /// 根据电脑数量返回归一化坐标。
        /// 与桌面版 TableRing.ComputerPositions 一致，但 y 值整体压缩一点，
        /// 因为手机竖屏的牌桌区域比桌面版扁，太靠上会顶到阶段文字。
        /// </summary>
        public static Vector2[] ComputerPositions(int computerCount)
        {
            switch (computerCount)
            {
                case 1:
                    return new[] { new Vector2(0.50f, 0.16f) };

                case 2:
                    return new[]
                    {
                        new Vector2(0.20f, 0.22f), new Vector2(0.80f, 0.22f)
                    };

                case 3:
                    return new[]
                    {
                        new Vector2(0.16f, 0.34f), new Vector2(0.50f, 0.14f),
                        new Vector2(0.84f, 0.34f)
                    };

                case 4:
                    return new[]
                    {
                        new Vector2(0.14f, 0.46f), new Vector2(0.30f, 0.16f),
                        new Vector2(0.70f, 0.16f), new Vector2(0.86f, 0.46f)
                    };

                case 5:
                    return new[]
                    {
                        new Vector2(0.12f, 0.52f), new Vector2(0.24f, 0.18f),
                        new Vector2(0.50f, 0.12f), new Vector2(0.76f, 0.18f),
                        new Vector2(0.88f, 0.52f)
                    };

                default: // 6 个及以上
                    return new[]
                    {
                        new Vector2(0.10f, 0.54f), new Vector2(0.22f, 0.18f),
                        new Vector2(0.50f, 0.12f), new Vector2(0.78f, 0.18f),
                        new Vector2(0.90f, 0.54f), new Vector2(0.16f, 0.80f)
                    };
            }
        }

        /// <summary>
        /// 归一化坐标 → 容器内的 anchoredPosition。
        /// 约定：容器 pivot 在中心 (0.5, 0.5)，所以返回值以容器中心为原点、y 向上为正。
        /// </summary>
        public static Vector2 ToLocal(Vector2 norm, float width, float height,
            float seatW, float seatH)
        {
            // 归一化坐标先转成「相对容器左上角」的像素中心
            var centerX = norm.x * width;
            var centerY = norm.y * height;

            // 再转成「相对容器中心」的偏移（y 轴翻转：屏幕向下为正，anchoredPosition 向上为正）
            var x = centerX - width * 0.5f;
            var y = height * 0.5f - centerY;

            return new Vector2(x, y);
        }

        /// <summary>创建椭圆牌桌背景（外圈 + 内圈装饰）。</summary>
        public static void BuildTableBackground(Transform parent, float width, float height)
        {
            var ew = width * EllipseWidthRatio;
            var eh = height * EllipseHeightRatio;
            var left = width * EllipseLeftRatio;
            var top = height * EllipseTopRatio;

            // 外圈桌面
            var outer = MakeEllipse(parent, Theme.Table, "TableOuter");
            var ort = outer;
            ort.anchorMin = ort.anchorMax = new Vector2(0f, 1f);
            ort.pivot = new Vector2(0f, 1f);
            ort.sizeDelta = new Vector2(ew, eh);
            ort.anchoredPosition = new Vector2(left, -top);

            // 内圈描边装饰
            var inner = MakeEllipse(parent, Theme.WithAlpha(Theme.TableBorder, 0.55f), "TableInner");
            var irt = inner;
            irt.anchorMin = irt.anchorMax = new Vector2(0f, 1f);
            irt.pivot = new Vector2(0f, 1f);
            irt.sizeDelta = new Vector2(width * 0.74f, height * 0.58f);
            irt.anchoredPosition = new Vector2(width * 0.13f, -height * 0.16f);

            // 内圈再盖一层底色，做出「桌面中间更亮」的层次
            var innerFill = MakeEllipse(parent, Theme.Table, "TableInnerFill");
            var ifrt = innerFill;
            ifrt.anchorMin = ifrt.anchorMax = new Vector2(0f, 1f);
            ifrt.pivot = new Vector2(0f, 1f);
            ifrt.sizeDelta = new Vector2(width * 0.72f, height * 0.56f);
            ifrt.anchoredPosition = new Vector2(width * 0.14f, -height * 0.17f);

            // 把内圈填充移到描边之上
            innerFill.transform.SetSiblingIndex(parent.childCount - 1);
        }

        // ==================== 工具 ====================

        private static RectTransform MakeEllipse(Transform parent, Color color, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UnityEngine.UI.Image));
            go.transform.SetParent(parent, false);

            var img = go.GetComponent<UnityEngine.UI.Image>();
            img.sprite = EllipseSprite;
            img.type = UnityEngine.UI.Image.Type.Simple;
            img.color = color;
            img.raycastTarget = false;

            return (RectTransform)go.transform;
        }

        private static Sprite _ellipseSprite;

        /// <summary>烘焙一个白色实心椭圆贴图（用作牌桌底）。</summary>
        private static Sprite EllipseSprite
        {
            get
            {
                if (_ellipseSprite != null) return _ellipseSprite;

                const int size = 256;
                var p = new Bake.Painter(size);
                var c = size * 0.5f;
                p.Add(Color.white, Bake.Ellipse(c, c, c - 2f, c - 2f));

                var tex = p.Finish();
                _ellipseSprite = Sprite.Create(tex, new Rect(0, 0, size, size),
                    new Vector2(0.5f, 0.5f), 100f);
                return _ellipseSprite;
            }
        }
    }
}
