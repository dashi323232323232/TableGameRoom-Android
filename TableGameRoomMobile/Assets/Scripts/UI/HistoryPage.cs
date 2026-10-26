using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TableGameRoom.Mobile.Core;
using TableGameRoom.Mobile.UI;

namespace TableGameRoom.Mobile
{
    /// <summary>一局的历史记录。</summary>
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

    /// <summary>历史记录读写 —— 对应桌面版 CalendarHistory.cs 的存储部分。</summary>
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

                var json = File.ReadAllText(path);
                var list = Json.DeserializeList<HistoryEntry>(json);
                return list;
            }
            catch
            {
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
        public static void Build(RectTransform root)
        {
            var app = App.Instance;
            var scroll = UiFactory.ScrollView(root, out var sr);
            

            var back = UiFactory.Button(scroll, "← 返回", () => app.ShowModeSelect());
            UiFactory.Size(back.gameObject, 240, 92);

            var title = UiFactory.Label(scroll, "历史记录", 48, Theme.Ink, TextAnchor.UpperLeft, bold: true);
            UiFactory.Size(title.gameObject, -1, 68);

            var entries = HistoryStore.Load(app.Mode);

            if (entries.Count == 0)
            {
                var empty = UiFactory.Label(scroll, "还没有对局记录。去大厅玩一局吧。", 30, Theme.Muted);
                return;
            }

            // 总计
            var totalNet = 0;
            foreach (var e in entries) totalNet += e.Net;

            var summary = UiFactory.Label(scroll,
                $"共 {entries.Count} 局  ·  净{(totalNet >= 0 ? "赢" : "输")} ${Mathf.Abs(totalNet):N0}",
                32, totalNet >= 0 ? Theme.Success : Theme.Danger, TextAnchor.UpperLeft, bold: true);
            UiFactory.Size(summary.gameObject, -1, 50);

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
                var dayNet = 0;
                foreach (var e in byDate[date]) dayNet += e.Net;

                var dayHeader = UiFactory.Label(scroll,
                    $"{date}   {byDate[date].Count} 局   {(dayNet >= 0 ? "+" : "")}{dayNet:N0}",
                    32, Theme.Ink, TextAnchor.UpperLeft, bold: true);
                UiFactory.Size(dayHeader.gameObject, -1, 52);

                var list = byDate[date];
                list.Sort((a, b) => b.Time.CompareTo(a.Time));

                foreach (var e in list)
                {
                    var card = UiFactory.Card(scroll, Theme.Panel, Theme.PanelBorder);
                    var layout = UiFactory.HorizontalLayout(card.transform, 14,
                        new RectOffset(24, 24, 18, 18), TextAnchor.MiddleLeft);
                    UiFactory.Stretch((RectTransform)layout.transform);
                    var fit = card.gameObject.AddComponent<ContentSizeFitter>();
                    fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                    var time = UiFactory.Label(layout.transform, e.Time.ToString("HH:mm"),
                        26, Theme.Muted, TextAnchor.MiddleLeft);
                    UiFactory.Size(time.gameObject, 100, 40);

                    var col = UiFactory.VerticalLayout(layout.transform, 4, null, TextAnchor.MiddleLeft);
                    var le = col.gameObject.AddComponent<LayoutElement>();
                    le.flexibleWidth = 1;

                    var g = UiFactory.Label(col.transform, e.GameName, 30, Theme.Ink, TextAnchor.MiddleLeft, bold: true);
                    UiFactory.Size(g.gameObject, -1, 40);

                    var s = UiFactory.Label(col.transform, e.Summary, 24, Theme.Muted, TextAnchor.MiddleLeft);
                    UiFactory.Size(s.gameObject, -1, 32);

                    var net = UiFactory.Label(layout.transform,
                        $"{(e.Net >= 0 ? "+" : "")}{e.Net:N0}",
                        32, e.Net >= 0 ? Theme.Success : Theme.Danger,
                        TextAnchor.MiddleRight, bold: true);
                    UiFactory.Size(net.gameObject, 180, 60);
                }
            }
        }
    }
}
