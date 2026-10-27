using System;
using System.Collections.Generic;
using UnityEngine;

namespace TableGameRoom.Mobile.UI
{
    /// <summary>
    /// 贴图烘焙工具 —— 把矢量图形画进 Texture2D。
    ///
    /// 为什么不用 UGUI 的 Image 拼：
    /// 一个筹码要 20+ 个 Image 节点，一桌 7 个座位就是 140+ 个，
    /// 而且 Outline 描边在透明 Image 上的表现不可靠。
    /// 直接烘一张贴图，每个筹码只要 1 个 Image，性能好且外观稳定。
    ///
    /// 坐标系：x 向右、y 向下（左上角为原点），写像素时再翻转到 Unity 的底部原点。
    /// 抗锯齿：靠 coverage 函数里的 ±0.5 像素羽化，等价于 1px 的 AA 过渡带。
    /// </summary>
    public static class Bake
    {
        public delegate float Coverage(float x, float y);

        /// <summary>图形覆盖度 0..1。</summary>
        public sealed class Painter
        {
            private readonly int _size;
            private readonly List<(Color Color, Coverage Cov)> _layers =
                new List<(Color, Coverage)>();

            public Painter(int size)
            {
                _size = size;
            }

            /// <summary>按顺序叠加一层（后加的盖在先前之上）。</summary>
            public void Add(Color color, Coverage cov)
            {
                if (cov == null) return;
                _layers.Add((color, cov));
            }

            /// <summary>烘焙成贴图。</summary>
            public Texture2D Finish()
            {
                var px = new Color32[_size * _size];

                for (var y = 0; y < _size; y++)
                {
                    for (var x = 0; x < _size; x++)
                    {
                        float r = 0f, g = 0f, b = 0f, a = 0f;

                        foreach (var (color, cov) in _layers)
                        {
                            var c = Mathf.Clamp01(cov(x + 0.5f, y + 0.5f)) * color.a;
                            if (c <= 0.0001f) continue;

                            // source-over 合成
                            var inv = 1f - c;
                            r = color.r * c + r * inv;
                            g = color.g * c + g * inv;
                            b = color.b * c + b * inv;
                            a = c + a * inv;
                        }

                        // 反预乘
                        if (a > 0.0001f)
                        {
                            r /= a; g /= a; b /= a;
                        }

                        // y 翻转到 Unity 的底部原点
                        var idx = (_size - 1 - y) * _size + x;
                        px[idx] = new Color32(
                            (byte)(Mathf.Clamp01(r) * 255f),
                            (byte)(Mathf.Clamp01(g) * 255f),
                            (byte)(Mathf.Clamp01(b) * 255f),
                            (byte)(Mathf.Clamp01(a) * 255f));
                    }
                }

                var tex = new Texture2D(_size, _size, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                tex.SetPixels32(px);
                tex.Apply();
                return tex;
            }

            public Sprite FinishSprite()
            {
                var tex = Finish();
                return Sprite.Create(tex, new Rect(0, 0, _size, _size),
                    new Vector2(0.5f, 0.5f), 100f);
            }
        }

        // ==================== 基本图形 ====================

        /// <summary>实心圆。</summary>
        public static Coverage Circle(float cx, float cy, float r) =>
            (x, y) =>
            {
                var dx = x - cx;
                var dy = y - cy;
                return Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
            };

        /// <summary>实心椭圆。</summary>
        public static Coverage Ellipse(float cx, float cy, float rx, float ry)
        {
            var minR = Mathf.Min(rx, ry);
            return (x, y) =>
            {
                var dx = (x - cx) / rx;
                var dy = (y - cy) / ry;
                var d = Mathf.Sqrt(dx * dx + dy * dy);
                return Mathf.Clamp01((1f - d) * minR + 0.5f);
            };
        }

        /// <summary>圆环（描边圆）。</summary>
        public static Coverage Ring(float cx, float cy, float r, float width) =>
            (x, y) =>
            {
                var dx = x - cx;
                var dy = y - cy;
                var d = Mathf.Sqrt(dx * dx + dy * dy);
                return Mathf.Clamp01(width * 0.5f - Mathf.Abs(d - r) + 0.5f);
            };

        /// <summary>椭圆环。</summary>
        public static Coverage EllipseRing(float cx, float cy, float rx, float ry, float width)
        {
            var minR = Mathf.Min(rx, ry);
            return (x, y) =>
            {
                var dx = (x - cx) / rx;
                var dy = (y - cy) / ry;
                var d = Mathf.Sqrt(dx * dx + dy * dy);
                return Mathf.Clamp01((width * 0.5f / minR - Mathf.Abs(d - 1f)) * minR + 0.5f);
            };
        }

        /// <summary>粗线段（圆头）。</summary>
        public static Coverage Segment(float x1, float y1, float x2, float y2, float width)
        {
            var half = width * 0.5f;
            return (x, y) =>
            {
                var vx = x2 - x1;
                var vy = y2 - y1;
                var wx = x - x1;
                var wy = y - y1;
                var len2 = vx * vx + vy * vy;

                var t = len2 <= 0.0001f ? 0f : Mathf.Clamp01((wx * vx + wy * vy) / len2);
                var px = x1 + vx * t;
                var py = y1 + vy * t;
                var dx = x - px;
                var dy = y - py;
                return Mathf.Clamp01(half - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
            };
        }

        /// <summary>圆角矩形。</summary>
        public static Coverage RoundRect(float cx, float cy, float w, float h, float radius)
        {
            var hw = w * 0.5f;
            var hh = h * 0.5f;
            return (x, y) =>
            {
                var dx = Mathf.Abs(x - cx) - (hw - radius);
                var dy = Mathf.Abs(y - cy) - (hh - radius);
                var ox = Mathf.Max(dx, 0f);
                var oy = Mathf.Max(dy, 0f);
                var d = Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(dx, dy), 0f) - radius;
                return Mathf.Clamp01(-d + 0.5f);
            };
        }

        /// <summary>竖直渐变（作为遮罩，返回 0..1）。</summary>
        public static Coverage GradientV(float y0, float y1) =>
            (x, y) => Mathf.Clamp01((y - y0) / Mathf.Max(0.0001f, y1 - y0));

        // ==================== 组合 ====================

        public static Coverage Union(params Coverage[] parts) =>
            (x, y) =>
            {
                var m = 0f;
                foreach (var p in parts) m = Mathf.Max(m, p(x, y));
                return m;
            };

        public static Coverage Multiply(Coverage a, Coverage b) =>
            (x, y) => a(x, y) * b(x, y);

        public static Coverage Offset(Coverage c, float dx, float dy) =>
            (x, y) => c(x - dx, y - dy);

        /// <summary>颜色插值（用于渐变层）。</summary>
        public static Color Lerp(Color a, Color b, float t) => Color.Lerp(a, b, Mathf.Clamp01(t));

        public static Color Darken(Color c, float f) =>
            new Color(c.r * f, c.g * f, c.b * f, c.a);

        public static Color Lighten(Color c, float f) =>
            new Color(c.r + (1f - c.r) * f, c.g + (1f - c.g) * f, c.b + (1f - c.b) * f, c.a);

        public static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, a);
    }
}
