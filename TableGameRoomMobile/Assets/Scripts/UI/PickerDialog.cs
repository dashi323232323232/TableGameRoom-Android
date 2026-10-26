using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TableGameRoom.Mobile.Core;
using TableGameRoom.Mobile.UI;

namespace TableGameRoom.Mobile
{
    /// <summary>选项选择弹窗（档位 / 桌人数共用）。</summary>
    public sealed class PickerDialog : MonoBehaviour
    {
        private static PickerDialog _current;

        public static void Show(
            Transform canvas,
            string title,
            IReadOnlyList<string> options,
            int selectedIndex,
            Action<int> onPick)
        {
            if (_current != null) Destroy(_current.gameObject);

            var go = new GameObject("PickerDialog", typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            UiFactory.Stretch((RectTransform)go.transform);

            var dlg = go.AddComponent<PickerDialog>();
            _current = dlg;

            var overlay = UiFactory.Background(go.transform, Theme.Overlay, "Overlay");
            UiFactory.Stretch(overlay.rectTransform);

            var card = UiFactory.Card(go.transform, Color.white, Theme.PanelBorder, "Card");
            var cr = card.rectTransform;
            cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 0.5f);
            cr.pivot = new Vector2(0.5f, 0.5f);
            cr.sizeDelta = new Vector2(880, 0);

            var layout = UiFactory.VerticalLayout(card.transform, 12, new RectOffset(40, 40, 36, 32));
            UiFactory.Stretch((RectTransform)layout.transform);

            var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var t = UiFactory.Label(layout.transform, title, 40, Theme.Ink, TextAnchor.UpperLeft, bold: true);
            UiFactory.Size(t.gameObject, -1, 56);

            for (var i = 0; i < options.Count; i++)
            {
                var idx = i;
                var picked = i == selectedIndex;

                var btn = UiFactory.Button(layout.transform, options[i], () =>
                {
                    onPick?.Invoke(idx);
                    dlg.Close();
                }, picked ? Theme.AccentSoft : Theme.Panel);

                UiFactory.Size(btn.gameObject, -1, 92);

                var txt = btn.GetComponentInChildren<Text>();
                if (picked && txt != null) txt.fontStyle = FontStyle.Bold;
            }

            var cancel = UiFactory.Button(layout.transform, "取消", dlg.Close);
            UiFactory.Size(cancel.gameObject, -1, 88);
        }

        private void Close()
        {
            if (_current == this) _current = null;
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_current == this) _current = null;
        }
    }
}
