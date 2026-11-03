using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TableGameRoom.Mobile.UI
{
    /// <summary>
    /// 玩家头像 —— 安卓端此前完全没有，只有一行文字。
    ///
    /// ⚠️ 关键教训：**不能用 emoji 字符画表情**。
    /// Android 系统字体（Noto Sans CJK）不含 emoji 字形，Unity 的 legacy Text
    /// 又不做字体回退，结果就是头像里一片空白。
    ///
    /// 因此头像整体烘成一张贴图：
    ///   圆形底色 + 球面高光 + 白描边 + 手绘五官
    /// 四种情绪靠嘴巴曲线的方向区分：
    ///   平静 = 平直；开心 = 两端上翘；失落 = 两端下垂；生气 = 下垂 + 内压怒眉
    /// </summary>
    public static class AvatarVisual
    {
        /// <summary>情绪。</summary>
        public enum Mood
        {
            Neutral,  // 平静
            Happy,    // 开心
            Sad,      // 失落
            Angry     // 生气
        }

        private static readonly Color[] Palette =
        {
            Theme.Hex("#7BAFD4"),  // 蓝
            Theme.Hex("#8FC49B"),  // 绿
            Theme.Hex("#D4A17B"),  // 橙
            Theme.Hex("#B39DDB"),  // 紫
            Theme.Hex("#E0918C"),  // 红
            Theme.Hex("#7FC4C4"),  // 青
            Theme.Hex("#C9B458")   // 金
        };

        public const float DefaultSize = 88f;

        private const int TexSize = 128;

        // 缓存：底色 + 情绪 → Sprite
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        /// <summary>把桌面版的 emoji 字符串转成情绪枚举。</summary>
        public static Mood FromEmoji(string emoji)
        {
            switch (emoji)
            {
                case "😄": return Mood.Happy;
                case "😞": return Mood.Sad;
                case "😠": return Mood.Angry;
                default: return Mood.Neutral;
            }
        }

        /// <summary>情绪的中文标签。</summary>
        public static string MoodLabel(Mood mood)
        {
            switch (mood)
            {
                case Mood.Happy: return "得意";
                case Mood.Sad: return "失落";
                case Mood.Angry: return "生气";
                default: return "平静";
            }
        }

        /// <summary>创建头像（用 emoji 字符串指定情绪）。</summary>
        public static RectTransform Create(
            Transform parent,
            string name,
            string emoji = "😐",
            int seatIndex = 0,
            bool isPlayer = false,
            float size = DefaultSize)
        {
            return Create(parent, name, FromEmoji(emoji), seatIndex, isPlayer, size);
        }

        /// <summary>创建头像（直接指定情绪）。</summary>
        public static RectTransform Create(
            Transform parent,
            string name,
            Mood mood,
            int seatIndex,
            bool isPlayer,
            float size = DefaultSize)
        {
            var go = new GameObject($"Avatar_{name}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var img = go.GetComponent<Image>();
            img.sprite = GetSprite(mood, seatIndex, isPlayer);
            img.preserveAspect = true;
            img.raycastTarget = false;

            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(size, size);
            return rt;
        }

        /// <summary>更新头像表情（会换 sprite）。</summary>
        public static void SetMood(RectTransform avatar, Mood mood, int seatIndex, bool isPlayer)
        {
            if (avatar == null) return;

            var img = avatar.GetComponent<Image>();
            if (img != null) img.sprite = GetSprite(mood, seatIndex, isPlayer);
        }

        public static void SetEmoji(RectTransform avatar, string emoji, int seatIndex, bool isPlayer) =>
            SetMood(avatar, FromEmoji(emoji), seatIndex, isPlayer);

        // ==================== 贴图烘焙 ====================

        private static Sprite GetSprite(Mood mood, int seatIndex, bool isPlayer)
        {
            var baseColor = isPlayer ? Theme.Accent : Palette[Mathf.Abs(seatIndex) % Palette.Length];
            var key = $"{ColorUtility.ToHtmlStringRGB(baseColor)}_{mood}";

            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var p = new Bake.Painter(TexSize);

            const float cx = TexSize * 0.5f;
            const float cy = TexSize * 0.5f;
            const float R = 52f;   // 头像半径（留出描边和发光的边距）

            // ---------- 1. 外发光（玩家自己） ----------
            if (isPlayer)
            {
                p.Add(Bake.Alpha(baseColor, 0.22f), Bake.Circle(cx, cy, R + 8f));
            }

            // ---------- 2. 主体圆 ----------
            p.Add(baseColor, Bake.Circle(cx, cy, R));

            // ---------- 3. 球面高光（左上偏亮） ----------
            p.Add(new Color(1f, 1f, 1f, 0.22f),
                Bake.Ellipse(cx - R * 0.22f, cy - R * 0.32f, R * 0.62f, R * 0.40f));

            // ---------- 4. 白描边 ----------
            p.Add(new Color(1f, 1f, 1f, 0.95f), Bake.Ring(cx, cy, R, 4f));

            // ---------- 5. 五官 ----------
            DrawFace(p, mood, cx, cy, R);

            var sprite = p.FinishSprite();
            Cache[key] = sprite;
            return sprite;
        }

        private static void DrawFace(Bake.Painter p, Mood mood, float cx, float cy, float R)
        {
            var ink = new Color(0.14f, 0.18f, 0.21f, 0.90f);

            var eyeR = R * 0.105f;
            var eyeX = R * 0.36f;
            var eyeY = cy - R * 0.18f;   // y 向下为正，所以「上」是减小 y

            // ---------- 眼睛 ----------
            if (mood == Mood.Happy)
            {
                // 开心：眯眼（短横杠）
                p.Add(ink, Bake.Segment(cx - eyeX - R * 0.13f, eyeY, cx - eyeX + R * 0.13f, eyeY, R * 0.075f));
                p.Add(ink, Bake.Segment(cx + eyeX - R * 0.13f, eyeY, cx + eyeX + R * 0.13f, eyeY, R * 0.075f));
            }
            else
            {
                p.Add(ink, Bake.Circle(cx - eyeX, eyeY, eyeR));
                p.Add(ink, Bake.Circle(cx + eyeX, eyeY, eyeR));
            }

            // ---------- 怒眉 ----------
            if (mood == Mood.Angry)
            {
                var by = eyeY - R * 0.26f;
                // 内低外高
                p.Add(ink, Bake.Segment(cx - eyeX - R * 0.15f, by - R * 0.05f,
                                        cx - eyeX + R * 0.15f, by + R * 0.05f, R * 0.07f));
                p.Add(ink, Bake.Segment(cx + eyeX - R * 0.15f, by + R * 0.05f,
                                        cx + eyeX + R * 0.15f, by - R * 0.05f, R * 0.07f));
            }

            // ---------- 嘴巴 ----------
            var mouthY = cy + R * 0.34f;
            var mouthW = R * 0.52f;
            var curve = R * 0.20f;

            const int dots = 11;
            var dotR = R * 0.052f;

            for (var i = 0; i < dots; i++)
            {
                var t = i / (float)(dots - 1);        // 0..1
                var x = cx - mouthW + mouthW * 2f * t;
                var s = 2f * t - 1f;                  // -1..1

                float y;
                switch (mood)
                {
                    case Mood.Happy:
                        // 两端上翘：中间最低（y 大 = 低）
                        y = mouthY + curve * (1f - s * s);
                        break;
                    case Mood.Sad:
                    case Mood.Angry:
                        // 两端下垂：中间最高
                        y = mouthY - curve * (1f - s * s);
                        break;
                    default:
                        y = mouthY;
                        break;
                }

                p.Add(ink, Bake.Circle(x, y, dotR));
            }
        }
    }
}
