using UnityEngine;
using UnityEngine.UI;
using TableGameRoom.Mobile.Core;
using TableGameRoom.Mobile.UI;

namespace TableGameRoom.Mobile
{
    /// <summary>任务中心 —— 对应桌面版 Tasks.cs。</summary>
    public static class TasksPage
    {
        public static void Build(RectTransform content)
        {
            var app = App.Instance;
            var stack = new VStack(content);

            stack.Button("← 返回", () => app.ShowModeSelect(), 88f);
            stack.Title("任务中心");

            stack.Text("完成任务可以领取虚拟奖金。余额不足时，这里也是重新站起来的机会。",
                Theme.FontSmall, Theme.Muted);

            stack.Text($"当前余额：{(app.Unlimited ? "∞（无限模式）" : $"${app.Save.Balance:N0}")}",
                Theme.FontHead, Theme.Accent, bold: true, minHeight: 52f);

            var tasks = TaskSystem.Load(app.Mode);

            if (tasks.Count == 0)
            {
                stack.Text("当前没有进行中的任务。点下面的按钮接取一个。", Theme.FontBody, Theme.Muted);
            }
            else
            {
                foreach (var task in tasks)
                    AddTaskCard(stack, task);
            }

            stack.Primary("接取新任务", () =>
            {
                var list = TaskSystem.Load(app.Mode);
                var task = TaskSystem.Generate(app.TierIndex, list);

                if (task != null)
                {
                    list.Add(task);
                    TaskSystem.Save(app.Mode, list);
                    app.Msg($"新任务：{task.Title}", "任务已接取");
                }

                app.ShowTasks();
            });

            stack.Finish();
        }

        private static void AddTaskCard(VStack stack, GameTask task)
        {
            var fill = task.Completed ? new Color(0.91f, 0.96f, 0.93f) : Theme.Panel;
            var border = task.Completed ? Theme.Success : Theme.PanelBorder;

            stack.Card(inner =>
            {
                inner.Text($"{(task.Completed ? "✓" : "●")}  {task.Title}",
                    Theme.FontHead, task.Completed ? Theme.Success : Theme.Ink, bold: true, minHeight: 50f);

                inner.Text(task.Detail, Theme.FontSmall, Theme.Muted);

                // 进度条
                var barBg = UiFactory.Rect(inner.Host, Theme.AccentSoft, "BarBg");
                var barBgRect = barBg.rectTransform;
                inner.Add(barBgRect, 20f);

                var ratio = task.Target > 0 ? Mathf.Clamp01(task.Progress / (float)task.Target) : 0f;

                var fillGo = UiFactory.Rect(barBg.transform, Theme.Accent, "BarFill");
                var fr = fillGo.rectTransform;
                fr.anchorMin = new Vector2(0f, 0f);
                fr.anchorMax = new Vector2(ratio, 1f);
                fr.offsetMin = Vector2.zero;
                fr.offsetMax = Vector2.zero;

                inner.Text($"进度 {task.Progress} / {task.Target}     奖励 ${task.Reward:N0}",
                    Theme.FontSmall, Theme.Muted, minHeight: 36f);
            }, fill, border);
        }
    }
}
