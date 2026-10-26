using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TableGameRoom.Mobile.Core;
using TableGameRoom.Mobile.UI;

namespace TableGameRoom.Mobile
{
    /// <summary>任务中心 —— 对应桌面版 Tasks.cs。</summary>
    public static class TasksPage
    {
        public static void Build(RectTransform root)
        {
            var app = App.Instance;
            var scroll = UiFactory.ScrollView(root, out var sr, 16);
            UiFactory.Stretch((RectTransform)sr.transform);

            var back = UiFactory.Button(scroll, "← 返回", () => app.ShowModeSelect());
            UiFactory.Size(back.gameObject, 240, 92);

            var title = UiFactory.Label(scroll, "任务中心", 48, Theme.Ink, TextAnchor.UpperLeft, bold: true);
            UiFactory.Size(title.gameObject, -1, 68);

            var hint = UiFactory.Label(scroll,
                "完成任务可以领取虚拟奖金。余额不足时，这里也是重新站起来的机会。",
                28, Theme.Muted);
            hint.lineSpacing = 1.3f;

            var balance = UiFactory.Label(scroll,
                $"当前余额：{(app.Unlimited ? "∞（无限模式）" : $"${app.Save.Balance:N0}")}",
                32, Theme.Accent, TextAnchor.UpperLeft, bold: true);
            UiFactory.Size(balance.gameObject, -1, 50);

            var tasks = TaskSystem.Load(app.Mode);

            if (tasks.Count == 0)
            {
                var empty = UiFactory.Label(scroll, "当前没有进行中的任务。", 30, Theme.Muted);
            }

            foreach (var task in tasks)
            {
                BuildTaskCard(scroll, task);
            }

            // 接取新任务
            var addRow = UiFactory.HorizontalLayout(scroll, 12, null, TextAnchor.MiddleCenter);
            UiFactory.Size(addRow.gameObject, -1, 116);

            var add = UiFactory.PrimaryButton(addRow.transform, "接取新任务", () =>
            {
                var t = TaskSystem.Generate(app.TierIndex, tasks);
                if (t != null)
                {
                    tasks.Add(t);
                    TaskSystem.Save(app.Mode, tasks);
                    app.Msg($"新任务：{t.Title}", "任务已接取");
                }
                app.ShowModeSelect();
            });
            UiFactory.Size(add.gameObject, 400, 110);
        }

        private static void BuildTaskCard(RectTransform parent, GameTask task)
        {
            var card = UiFactory.Card(parent,
                task.Completed ? new Color(0.91f, 0.96f, 0.93f) : Theme.Panel,
                task.Completed ? Theme.Success : Theme.PanelBorder);

            var layout = UiFactory.VerticalLayout(card.transform, 10,
                new RectOffset(30, 30, 24, 24), TextAnchor.UpperLeft);
            UiFactory.Stretch((RectTransform)layout.transform);

            var fit = card.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var head = UiFactory.Label(layout.transform,
                $"{(task.Completed ? "✓" : "●")}  {task.Title}",
                34, task.Completed ? Theme.Success : Theme.Ink,
                TextAnchor.UpperLeft, bold: true);
            head.horizontalOverflow = HorizontalWrapMode.Wrap;

            var detail = UiFactory.Label(layout.transform, task.Detail, 26, Theme.Muted);
            detail.lineSpacing = 1.25f;

            // 进度条
            var progressBg = UiFactory.Background(layout.transform, Theme.AccentSoft, "ProgressBg");
            UiFactory.Size(progressBg.gameObject, -1, 20);

            var ratio = task.Target > 0 ? Mathf.Clamp01(task.Progress / (float)task.Target) : 0f;
            var fill = UiFactory.Background(progressBg.transform, Theme.Accent, "Fill");
            var fr = fill.rectTransform;
            fr.anchorMin = new Vector2(0, 0);
            fr.anchorMax = new Vector2(ratio, 1);
            fr.offsetMin = Vector2.zero;
            fr.offsetMax = Vector2.zero;

            var progressText = UiFactory.Label(layout.transform,
                $"进度 {task.Progress} / {task.Target}    奖励 ${task.Reward:N0}",
                26, Theme.Muted);
            UiFactory.Size(progressText.gameObject, -1, 36);
        }
    }
}
