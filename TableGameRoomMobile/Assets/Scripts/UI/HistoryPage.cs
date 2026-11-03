using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TableGameRoom.Mobile.Core;
using TableGameRoom.Mobile.UI;

namespace TableGameRoom.Mobile
{
    /// <summary>一局历史记录。</summary>
    [Serializable]
    public sealed class HistoryEntry
    {
        public string Id { get; set; } = "";
        public DateTime Time { get; set; }
        public string GameKey { get; set; } = "";
        public string GameName { get; set; } = "";
        public int Stake { get; set; }
        public int Net { get; set; }
        public string Summary { get; set; } = "";
    }

    /// <summary>历史记录读写。</summary>
    public static class HistoryStore
    {
        private static string PathFor(string mode) =>
            Path.Combine(SaveStore.Dir, mode == "无限" ? "custom-history.json" : "history.json");

        public static List<HistoryEntry> Load(string mode)
        {
            try
            {
                var path = PathFor(mode);
                if (!File.Exists(path)) return new List<HistoryEntry>();

                var list = Json.DeserializeList<HistoryEntry>(File.ReadAllText(path));
                return list ?? new List<HistoryEntry>();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HistoryStore] 读取失败：{ex.Message}");
                return new List<HistoryEntry>();
            }
        }

        public static void Append(string mode, HistoryEntry entry)
        {
            try
            {
                var list = Load(mode);
                list.Add(entry);

                // 只保留最近 3 个月
                var cutoff = DateTime.Now.AddMonths(-3);
                list.RemoveAll(e => e.Time < cutoff);

                File.WriteAllText(PathFor(mode), Json.SerializeList(list, pretty: true));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HistoryStore] 写入失败：{ex.Message}");
            }
        }
    }

    /// <summary>历史记录页 —— 按日期分组显示每局输赢。</summary>
    public static class HistoryPage
    {
        public static void Build(RectTransform content)
        {
            var app = App.Instance;
            var stack = new VStack(content);

            stack.Button("← 返回", () => app.ShowModeSelect(), 88f);
            stack.Title("历史记录");

            var entries = HistoryStore.Load(app.Mode);

            if (entries.Count == 0)
            {
                stack.Text("还没有对局记录。去大厅玩一局吧。", Theme.FontBody, Theme.Muted);
                stack.Finish();
                return;
            }

            // 总计
            var totalNet = 0;
            foreach (var e in entries) totalNet += e.Net;

            var sign = totalNet >= 0 ? "赢" : "输";
            stack.Text($"共 {entries.Count} 局   ·   净{sign} ${Mathf.Abs(totalNet):N0}",
                Theme.FontHead, totalNet >= 0 ? Theme.Success : Theme.Danger, bold: true, minHeight: 52f);

            // 按日期倒序分组
            var byDate = new Dictionary<string, List<HistoryEntry>>();
            foreach (var e in entries)
            {
                var key = e.Time.ToString("yyyy-MM-dd");
                if (!byDate.ContainsKey(key)) byDate[key] = new List<HistoryEntry>();
                byDate[key].Add(e);
            }

            var dates = new List<string>(byDate.Keys);
            dates.Sort();
            dates.Reverse();

            foreach (var date in dates)
            {
                var dayEntries = byDate[date];
                var dayNet = 0;
                foreach (var e in dayEntries) dayNet += e.Net;

                stack.Head($"{date}   {dayEntries.Count} 局   {(dayNet >= 0 ? "+" : "")}{dayNet:N0}");

                dayEntries.Sort((a, b) => b.Time.CompareTo(a.Time));

                foreach (var e in dayEntries)
                    AddEntry(stack, e);
            }

            stack.Finish();
        }

        private static void AddEntry(VStack stack, HistoryEntry e)
        {
            stack.Card(inner =>
            {
                // 第一行：时间 + 游戏名 + 输赢
                inner.Text($"{e.Time:HH:mm}   {e.GameName}", Theme.FontBody, Theme.Ink, bold: true, minHeight: 42f);

                // 第二行：说明
                if (!string.IsNullOrEmpty(e.Summary))
                    inner.Text(e.Summary, Theme.FontSmall, Theme.Muted);

                // 第三行：净赢
                var netText = $"{(e.Net >= 0 ? "+" : "")}{e.Net:N0}";
                inner.Text(netText, Theme.FontHead, e.Net >= 0 ? Theme.Success : Theme.Danger, bold: true);
            }, Theme.Panel, Theme.PanelBorder, 22f, 8f);
        }
    }
}
