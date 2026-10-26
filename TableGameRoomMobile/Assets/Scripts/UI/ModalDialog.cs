using System;
using UnityEngine;
using UnityEngine.UI;
using TableGameRoom.Mobile.UI;

namespace TableGameRoom.Mobile
{
    /// <summary>
    /// 模态对话框 —— 对应桌面版的 ThemeDialog。
    /// 半透明遮罩 + 居中圆角卡片 + 标题 + 正文 + 按钮。
    /// </summary>
    public sealed class ModalDialog : MonoBehaviour
    {
        private static ModalDialog _current;

        private Action _onOk;
        private Action _onCancel;

        public static ModalDialog Show(
            Transform canvas,
            string title,
            string body,
            string okText = "确定",
            string cancelText = null,
            Action onOk = null,
            Action onCancel = null)
        {
            // 同一时刻只允许一个模态框
            if (_current != null) Destroy(_current.gameObject);

            var go = new GameObject("ModalDialog", typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            var rt = (RectTransform)go.transform;
            UiFactory.Stretch(rt);

            var dlg = go.AddComponent<ModalDialog>();
            dlg._onOk = onOk;
            dlg._onCancel = onCancel;
            _current = dlg;

            // 遮罩（点击空白不关闭，避免误触）
            var overlay = UiFactory.Background(go.transform, Theme.Overlay, "Overlay");
            UiFactory.Stretch(overlay.rectTransform);

            // 卡片
            var card = UiFactory.Card(go.transform, Color.white, Theme.PanelBorder, "DialogCard");
            var cardRect = card.rectTransform;
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(880, 0);

            var layout = UiFactory.VerticalLayout(card.transform, 22, new RectOffset(48, 48, 44, 40));
            UiFactory.Stretch((RectTransform)layout.transform);

            var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // 标题
            var titleText = UiFactory.Label(layout.transform, title, 42, Theme.Ink, TextAnchor.UpperLeft, bold: true);
            titleText.horizontalOverflow = HorizontalWrapMode.Wrap;

            // 正文
            var bodyText = UiFactory.Label(layout.transform, body, 34, Theme.Muted);
            bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            bodyText.lineSpacing = 1.25f;

            // 按钮行
            var btnRow = UiFactory.HorizontalLayout(layout.transform, 18, null, TextAnchor.MiddleRight);
            UiFactory.Size(btnRow.gameObject, -1, 96);

            if (!string.IsNullOrEmpty(cancelText))
            {
                var cancel = UiFactory.Button(btnRow.transform, cancelText, dlg.Cancel);
                UiFactory.Size(cancel.gameObject, 220, 88);
            }

            var ok = UiFactory.PrimaryButton(btnRow.transform, okText, dlg.Ok);
            UiFactory.Size(ok.gameObject, 220, 88);

            return dlg;
        }

        private void Ok()
        {
            var cb = _onOk;
            Close();
            cb?.Invoke();
        }

        private void Cancel()
        {
            var cb = _onCancel;
            Close();
            cb?.Invoke();
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
