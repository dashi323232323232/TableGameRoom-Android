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

        // 筹码基准尺寸。之前 88 太宽，三摞叠起来能占掉半个牌桌中央，
        // 把底池数字和公共牌都压住了。缩到 56，视觉上仍能分辨面额。
        private const float StackWidth = 56f;
        private const float StackHeight = 56f;
        private const float ChipStepY = 8f;  // 每枚往上偏移（俯视堆叠只露边缘）

        /// <summary>
        /// 创建一堆筹码。amount 为 0 时返回空容器。
        ///
        /// 关键：**不跨面额凑数**。之前用贪心法把 $500 直接拆成 1 枚 500，
        /// 5 个人各下 100 的底池看起来像「一个 500 的筹码」，很怪。
        /// 现在以基准面额为单位：5×100 就是 5 枚 100，分 3 摞堆起来。
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

            // 选基准面额：从大到小找第一个「枚数合理」的
            var baseValue = 1;
            foreach (var (value, _) in Denominations)
            {
                if (value > amount) continue;
                if (amount / value <= 20)
                {
                    baseValue = value;
                    break;
                }
            }

            var pieces = Mathf.Max(1, amount / baseValue);
            var remainder = amount % baseValue;

            var stacks = new List<(int Value, int Count)>();

            // 主体：同面额分到最多 2 摞（原来是 3 摞，占横向空间太多）
            var visibleStacks = Mathf.Min(2, pieces);
            var eachCount = pieces / visibleStacks;
            var extra = pieces % visibleStacks;

            for (var i = 0; i < visibleStacks; i++)
            {
                var count = eachCount + (i < extra ? 1 : 0);
                if (count > 0) stacks.Add((baseValue, count));
            }

            // 余数：用不超过它的小面额补一枚，保证总额精确
            if (remainder > 0)
            {
                foreach (var (value, _) in Denominations)
                {
                    if (value > remainder) continue;
                    stacks.Add((value, Mathf.Max(1, remainder / value)));
                    break;
                }
            }

            // 横向总宽（摞之间重叠 22%，看起来才像一堆）
            var stackW = StackWidth * scale;
            var overlap = stackW * 0.22f;
            var totalW = stacks.Count == 0 ? 0f : stackW + (stacks.Count - 1) * (stackW - overlap);

            rt.sizeDelta = new Vector2(totalW, StackHeight * scale);

            // 从左往右排，整堆在容器内居中
            var x = -totalW * 0.5f;
            for (var i = 0; i < stacks.Count; i++)
            {
                var (value, count) = stacks[i];

                // 文字只在**最后一摞**上画总金额。
                // 之前每摞都写「133×1」，而摞之间重叠 22%，
                // 三摞的字直接叠成一坨「1×133×133×133」。
                var showLabel = i == stacks.Count - 1;
                var stack = MakeStack(rt, value, count, scale, showLabel,
                    showLabel ? Label(amount) : null);

                stack.anchoredPosition = new Vector2(x, -StackHeight * scale * 0.5f);
                x += stackW - overlap;
            }

            return rt;
        }

        /// <summary>创建一摞同面额的筹码（以容器中心为锚）。</summary>
        /// <param name="showLabel">是否在这一摞上写金额</param>
        /// <param name="labelText">要写的文字（通常只有最右一摞写总额）</param>
        private static RectTransform MakeStack(Transform parent, int value, int count, float scale,
            bool showLabel = true, string labelText = null)
        {
            var root = UiFactory.Node(parent, $"Stack{value}");
            var rt = root;

            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 0f);   // 左边缘对齐锚点，方便横向累加
            rt.sizeDelta = new Vector2(StackWidth * scale, StackHeight * scale);

            var sprite = GetChipSprite(value);

            // 小尺寸下堆叠会让数字糊成一团，只画一枚 + 清晰的数字
            var stackable = scale >= 0.6f;
            var visible = stackable ? Mathf.Min(count, 4) : 1;

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
                if (i != visible - 1) continue;
                if (!showLabel) continue;

                var text = labelText ?? Label(value);

                var label = UiFactory.Label(go.transform, text,
                    Mathf.Max(11, Mathf.RoundToInt(22f * Mathf.Max(scale, 0.55f))),
                    value == 1 ? Color.black : Color.white,
                    TextAnchor.MiddleCenter, bold: true);

                var lr = label.rectTransform;
                lr.anchorMin = lr.anchorMax = new Vector2(0.5f, 0.5f);
                lr.pivot = new Vector2(0.5f, 0.5f);
                // 文字框略窄于筹码，避免溢出到相邻摞
                lr.sizeDelta = new Vector2(StackWidth * scale * 0.86f, StackHeight * scale * 0.5f);
                lr.anchoredPosition = new Vector2(0f, 0f);
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
