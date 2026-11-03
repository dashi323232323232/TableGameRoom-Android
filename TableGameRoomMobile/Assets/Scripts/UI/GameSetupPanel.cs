using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TableGameRoom.Mobile.Core;

namespace TableGameRoom.Mobile.UI
{
    /// <summary>
    /// 游戏内的设置栏 —— 对应桌面版的 StakeBox + 人数/档位选择。
    ///
    /// 桌面版进入游戏后仍然可以调整：下注金额、桌人数、档位。
    /// 安卓端早期版本把这些全丢了，进游戏只剩一个「开始」按钮，
    /// 玩家没法改金额也没法改人数。这个组件把它们补回来。
    ///
    /// 布局（竖向）：
    ///   档位 xxx · 桌 n 人            [改档位] [改人数]
    ///   下注金额
    ///   [ $10 ] [ $20 ] [ $50 ] [ $100 ] ...
    ///   当前下注 $100（上限 $500）
    /// </summary>
    public static class GameSetupPanel
    {
        /// <summary>设置变化回调。</summary>
        public sealed class Callbacks
        {
            /// <summary>下注金额变化（已是有效值）。</summary>
            public Action<int> OnStakeChanged;

            /// <summary>桌人数变化。</summary>
            public Action<int> OnSeatsChanged;

            /// <summary>档位变化。</summary>
            public Action<int> OnTierChanged;
        }

        /// <summary>
        /// 在 parent 里画一套设置控件（竖向堆叠，由调用方给出高度）。
        /// 返回当前选中的下注金额。
        /// </summary>
        public static int Build(
            Transform parent,
            int currentStake,
            Callbacks callbacks,
            bool showSeats = true,
            bool showTier = true)
        {
            var app = App.Instance;
            var stake = ClampStake(currentStake, app);

            var root = UiFactory.Node(parent, "GameSetup");
            var rt = root;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, -1f);
            rt.offsetMax = new Vector2(0f, 0f);

            var y = 0f;

            // ---------- 第一行：档位 / 人数 ----------
            var infoText = UiFactory.Label(root,
                $"档位 {app.TierName}　·　桌 {app.Seats} 人",
                Theme.FontSmall, Theme.Muted, TextAnchor.MiddleLeft);
            var ir = infoText.rectTransform;
            ir.anchorMin = ir.anchorMax = new Vector2(0f, 1f);
            ir.pivot = new Vector2(0f, 1f);
            ir.sizeDelta = new Vector2(460f, 44f);
            ir.anchoredPosition = new Vector2(0f, 0f);

            var rowButtons = new List<Button>();

            if (showTier)
            {
                var tierBtn = UiFactory.Button(root, "改档位", () => ShowTierPicker(callbacks), Theme.Panel);
                rowButtons.Add(tierBtn);
            }

            if (showSeats)
            {
                var seatBtn = UiFactory.Button(root, "改人数", () => ShowSeatPicker(callbacks), Theme.Panel);
                rowButtons.Add(seatBtn);
            }

            if (rowButtons.Count > 0)
            {
                const float btnW = 150f;
                const float gap = 10f;
                var startX = 1000f - (rowButtons.Count * btnW + (rowButtons.Count - 1) * gap);

                for (var i = 0; i < rowButtons.Count; i++)
                {
                    var br = rowButtons[i].GetComponent<RectTransform>();
                    br.anchorMin = br.anchorMax = new Vector2(0f, 1f);
                    br.pivot = new Vector2(0f, 1f);
                    br.sizeDelta = new Vector2(btnW, 62f);
                    br.anchoredPosition = new Vector2(startX + i * (btnW + gap), 0f);
                }
            }

            y += 52f;

            // ---------- 下注金额 ----------
            var stakeLabel = UiFactory.Label(root, "下注金额", Theme.FontSmall, Theme.Ink,
                TextAnchor.MiddleLeft, bold: true);
            var slr = stakeLabel.rectTransform;
            slr.anchorMin = slr.anchorMax = new Vector2(0f, 1f);
            slr.pivot = new Vector2(0f, 1f);
            slr.sizeDelta = new Vector2(300f, 36f);
            slr.anchoredPosition = new Vector2(0f, -y);

            y += 40f;

            // 快捷金额按钮（按档位上下限生成）
            var quick = QuickStakes(app);
            var chips = new List<Button>();
            foreach (var amount in quick)
            {
                var v = amount;
                var picked = v == stake;

                var btn = UiFactory.Button(root, $"${v:N0}", () =>
                {
                    callbacks?.OnStakeChanged?.Invoke(v);
                }, picked ? Theme.AccentSoft : Theme.Panel);

                if (picked)
                {
                    var t = btn.GetComponentInChildren<Text>();
                    if (t != null) t.fontStyle = FontStyle.Bold;
                }

                chips.Add(btn);
            }

            if (chips.Count > 0)
            {
                const float gap = 8f;
                var cols = Mathf.Min(4, chips.Count);
                var cellW = (1000f - gap * (cols - 1)) / cols;

                for (var i = 0; i < chips.Count; i++)
                {
                    var row = i / cols;
                    var col = i % cols;
                    var br = chips[i].GetComponent<RectTransform>();
                    br.anchorMin = br.anchorMax = new Vector2(0f, 1f);
                    br.pivot = new Vector2(0f, 1f);
                    br.sizeDelta = new Vector2(cellW, 76f);
                    br.anchoredPosition = new Vector2(col * (cellW + gap), -y - row * 84f);
                }

                var rows = Mathf.CeilToInt(chips.Count / (float)cols);
                y += rows * 84f;
            }

            // ---------- 当前下注说明 ----------
            var capText = app.TierMax == int.MaxValue ? "无上限" : $"上限 ${app.TierMax:N0}";
            var hint = UiFactory.Label(root, $"当前下注 ${stake:N0}　·　{capText}",
                Theme.FontSmall - 2, Theme.Accent, TextAnchor.MiddleLeft);
            var hr = hint.rectTransform;
            hr.anchorMin = hr.anchorMax = new Vector2(0f, 1f);
            hr.pivot = new Vector2(0f, 1f);
            hr.sizeDelta = new Vector2(1000f, 36f);
            hr.anchoredPosition = new Vector2(0f, -y);

            return stake;
        }

        /// <summary>这套设置大约需要多高（供调用方分配空间）。</summary>
        public static float EstimateHeight()
        {
            var app = App.Instance;
            var quick = QuickStakes(app).Count;
            var cols = Mathf.Max(1, Mathf.Min(4, quick));
            var rows = Mathf.CeilToInt(quick / (float)cols);
            return 52f + 40f + rows * 84f + 36f + 8f;
        }

        /// <summary>
        /// 设置栏占位宿主 —— 内容为空时自动收缩到 0 高度，避免牌局中留一大块空白。
        /// 用法：把 Setup 节点的高度交给它自己管理。
        /// </summary>
        public static void CollapseIfEmpty(RectTransform host)
        {
            if (host == null) return;

            var isEmpty = host.childCount == 0;
            var h = isEmpty ? 0f : EstimateHeight() + 12f;

            host.sizeDelta = new Vector2(host.sizeDelta.x, h);
        }

        // ==================== 弹窗 ====================

        private static void ShowTierPicker(Callbacks callbacks)
        {
            var app = App.Instance;
            var options = new List<string>();

            for (var i = 0; i < BetTiers.Count; i++)
            {
                var t = BetTiers.Get(i);
                var range = t.Max is null ? "无上限" : $"${t.Max.Value:N0}";
                options.Add($"{t.Name}    ${t.Min:N0} – {range}");
            }

            Dialog.Pick(app.CanvasRoot, "选择下注档位", options, app.TierIndex, idx =>
            {
                app.Save.TierIndex = idx;
                app.PersistSave();
                callbacks?.OnTierChanged?.Invoke(idx);
            });
        }

        private static void ShowSeatPicker(Callbacks callbacks)
        {
            var app = App.Instance;
            var options = new List<string>();

            for (var n = 2; n <= 7; n++)
            {
                options.Add($"{n} 人桌{(n == app.Seats ? "   ← 当前" : "")}");
            }

            Dialog.Pick(app.CanvasRoot, "选择桌人数", options, app.Seats - 2, idx =>
            {
                app.Save.Seats = idx + 2;
                app.PersistSave();
                callbacks?.OnSeatsChanged?.Invoke(idx + 2);
            });
        }

        // ==================== 工具 ====================

        /// <summary>按档位上下限生成快捷金额。</summary>
        public static List<int> QuickStakes(App app)
        {
            var list = new List<int>();
            var min = Mathf.Max(1, app.TierMin);
            var max = app.TierMax;

            foreach (var v in new[] { 10, 20, 50, 100, 500, 1000, 5000, 10000 })
            {
                if (v < min) continue;
                if (v > max) continue;
                list.Add(v);
            }

            // 档位下限本身也放进去（比如极强档从 10000 起）
            if (!list.Contains(min) && min <= max) list.Insert(0, min);

            // 太少就补几个按倍率递增值
            if (list.Count < 4)
            {
                var v = min;
                while (list.Count < 4 && v <= max)
                {
                    if (!list.Contains(v)) list.Add(v);
                    v *= 5;
                    if (v <= 0) break;
                }
            }

            list.Sort();
            return list;
        }

        /// <summary>把金额夹到当前档位范围内。</summary>
        public static int ClampStake(int stake, App app)
        {
            var min = Mathf.Max(1, app.TierMin);
            var max = app.TierMax;

            if (stake < min) stake = min;
            if (stake > max) stake = max;
            return stake;
        }
    }
}
