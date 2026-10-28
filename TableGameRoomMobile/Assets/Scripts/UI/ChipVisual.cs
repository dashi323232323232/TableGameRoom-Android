using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TableGameRoom.Mobile.UI
{
    /// <summary>
    /// 筹码视觉 —— 对应桌面版 ChipVisual.cs。
    ///
    /// 实现方式：把一枚筹码（阴影 + 侧面 + 顶面 + 刻度 + 中心圆 + 面额）
    /// 烘成一张 128×128 贴图，每个筹码只用一个 Image 显示。
    /// 这样一桌 7 个座位 × 6 枚筹码也只有几十个节点，不会拖慢渲染。
    ///
    /// 金额拆成面额后，每种面额最多堆 3 摞，每摞最多 6 枚，
    /// 任意金额都能在有限空间内精确表达。
    /// </summary>
    public static class ChipVisual
    {
        private static readonly (int Value, Color Color)[] Denominations =
        {
            (1000, Theme.Hex("#2B5B91")),   // 深蓝
            (500,  Theme.Hex("#34845B")),   // 绿
            (100,  Theme.Hex("#B93530")),   // 红
            (25,   Theme.Hex("#C59127")),   // 金
            (5,    Theme.Hex("#5B636A")),   // 灰
            (1,    Theme.Hex("#E7E7DB"))    // 米白
        };

        // 贴图缓存：面额 → Sprite（同面额只烘一次）
        private static readonly Dictionary<int, Sprite> Cache = new Dictionary<int, Sprite>();

        private const int TexSize = 256;
        private const float ChipW = 224f;   // 贴图内的筹码宽度
        private const float ChipH = 224f;   // 贴图内的筹码高度（俯视圆）
        private const float StackWidth = 88f;
        private const float StackHeight = 88f;
        private const float ChipStepY = 13f; // 每枚往上偏移（俯视堆叠只露边缘）

        /// <summary>
        /// 创建一堆筹码。amount 为 0 时返回空容器。
        /// 返回的容器尺寸就是这堆筹码的实际占用空间（调用方可据此居中）。
        /// </summary>
        public static RectTransform Create(Transform parent, int amount, float scale = 1f)
        {
            var root = UiFactory.Node(parent, "Chips");
            var rt = root;

            // 锚点统一到中心，便于调用方居中
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            if (amount <= 0)
            {
                rt.sizeDelta = new Vector2(0f, StackHeight * scale);
                return rt;
            }

            // 先算出所有摞的规格
            var remaining = amount;
            var stacks = new List<(int Value, int Count)>();
            foreach (var (value, _) in Denominations)
            {
                var pieces = remaining / value;
                remaining %= value;
                if (pieces == 0) continue;

                var visibleStacks = Mathf.Min(3, pieces);
                var eachCount = pieces / visibleStacks;
                var extra = pieces % visibleStacks;

                for (var i = 0; i < visibleStacks; i++)
                    stacks.Add((value, eachCount + (i < extra ? 1 : 0)));
            }

            // 横向总宽（摞之间重叠 22%，看起来才像一堆）
            var stackW = StackWidth * scale;
            var overlap = stackW * 0.22f;
            var totalW = stacks.Count == 0 ? 0f : stackW + (stacks.Count - 1) * (stackW - overlap);

            rt.sizeDelta = new Vector2(totalW, StackHeight * scale);

            // 从左往右排，整堆在容器内居中
            var x = -totalW * 0.5f;
            foreach (var (value, count) in stacks)
            {
                var stack = MakeStack(rt, value, count, scale);
                stack.anchoredPosition = new Vector2(x, -StackHeight * scale * 0.5f);
                x += stackW - overlap;
            }

            return rt;
        }

        /// <summary>创建一摞同面额的筹码（以容器中心为锚）。</summary>
        private static RectTransform MakeStack(Transform parent, int value, int count, float scale)
        {
            var root = UiFactory.Node(parent, $"Stack{value}");
            var rt = root;

            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 0f);   // 左边缘对齐锚点，方便横向累加
            rt.sizeDelta = new Vector2(StackWidth * scale, StackHeight * scale);

            var sprite = GetChipSprite(value);

            // 每摞最多画 6 枚
            var visible = Mathf.Min(count, 6);
            for (var i = 0; i < visible; i++)
            {
                var go = new GameObject("Chip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(root, false);

                var img = go.GetComponent<Image>();
                img.sprite = sprite;
                img.preserveAspect = false;
                img.raycastTarget = false;

                var cr = (RectTransform)go.transform;
                cr.anchorMin = cr.anchorMax = new Vector2(0f, 0f);
                cr.pivot = new Vector2(0f, 0f);
                cr.sizeDelta = new Vector2(StackWidth * scale, StackHeight * scale);
                cr.anchoredPosition = new Vector2(0f, i * ChipStepY * scale);

                // 面额文字只画在最上面一枚上
                if (i == visible - 1)
                {
                    var label = UiFactory.Label(go.transform, Label(value),
                        Mathf.Max(10, Mathf.RoundToInt(22f * scale)),
                        value == 1 ? Color.black : Color.white,
                        TextAnchor.MiddleCenter, bold: true);

                    var lr = label.rectTransform;
                    lr.anchorMin = lr.anchorMax = new Vector2(0.5f, 0.5f);
                    lr.pivot = new Vector2(0.5f, 0.5f);
                    lr.sizeDelta = new Vector2(StackWidth * scale * 0.86f, StackHeight * scale * 0.5f);
                    lr.anchoredPosition = new Vector2(0f, 0f);
                }
            }

            return rt;
        }

        // ==================== 贴图烘焙 ====================

        /// <summary>取得某面额的筹码贴图（带缓存）。俯视圆形筹码。</summary>
        private static Sprite GetChipSprite(int value)
        {
            if (Cache.TryGetValue(value, out var cached) && cached != null) return cached;

            var color = ColorFor(value);
            var p = new Bake.Painter(TexSize);

            var cx = TexSize * 0.5f;
            var cy = TexSize * 0.5f;
            var R = ChipW * 0.5f;

            // ---------- 1. 投影（右下偏移，制造厚度感） ----------
            p.Add(new Color(0f, 0f, 0f, 0.22f), Bake.Circle(cx + 5f, cy + 7f, R * 0.98f));

            // ---------- 2. 筹码侧面（深色，略向下偏移露出边） ----------
            p.Add(Bake.Darken(color, 0.52f), Bake.Circle(cx, cy + 6f, R));

            // ---------- 3. 顶面（主体色） ----------
            p.Add(color, Bake.Circle(cx, cy, R * 0.97f));

            // ---------- 4. 顶面渐变高光（左上偏亮） ----------
            p.Add(Bake.Lerp(color, Color.white, 0.26f),
                Bake.Ellipse(cx - R * 0.14f, cy - R * 0.16f, R * 0.72f, R * 0.62f));

            // ---------- 5. 外圈白描边 ----------
            p.Add(new Color(1f, 1f, 1f, 0.95f), Bake.Ring(cx, cy, R * 0.97f, 5f));

            // ---------- 6. 八个刻度花纹（沿圆周分布） ----------
            for (var i = 0; i < 8; i++)
            {
                var angle = i * Mathf.PI / 4f;
                var tx = cx + Mathf.Sin(angle) * R * 0.78f;
                var ty = cy - Mathf.Cos(angle) * R * 0.78f;
                p.Add(new Color(1f, 1f, 1f, 0.92f), Bake.Circle(tx, ty, R * 0.085f));
            }

            // ---------- 7. 中心圆（面额区） ----------
            p.Add(Bake.Darken(color, 0.80f), Bake.Circle(cx, cy, R * 0.50f));
            p.Add(Bake.Lerp(color, Color.white, 0.12f), Bake.Circle(cx, cy, R * 0.44f));

            var sprite = p.FinishSprite();
            Cache[value] = sprite;
            return sprite;
        }

        private static Color ColorFor(int value)
        {
            foreach (var (v, c) in Denominations)
            {
                if (v == value) return c;
            }
            return Theme.Hex("#5B636A");
        }

        /// <summary>面额文字（如 "100"、"1K"）。</summary>
        public static string Label(int value) => value >= 1000 ? $"{value / 1000}K" : value.ToString();
    }
}
