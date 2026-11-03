using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TableGameRoom.Mobile.UI;

namespace TableGameRoom.Mobile.UI
{
    /// <summary>
    /// 模态对话框 —— 提示 / 确认 / 单选列表三合一。
    ///
    /// 桌面版对应 ThemeDialog（提示/确认）。这里加了列表选择，
    /// 用于档位、桌人数这类需要多选一的场景。
    /// </summary>
    public sealed class Dialog : MonoBehaviour
    {
        private static Dialog _current;

        private Action _onOk;
        private Action _onCancel;
        private bool _closed;

        // ==================== 提示 / 确认 ====================

        /// <summary>显示提示框（cancelText 为 null 时只有一个确定按钮）。</summary>
        public static Dialog Show(
            Transform canvas,
            string title,
            string body,
            string okText = "确定",
            string cancelText = null,
            Action onOk = null,
            Action onCancel = null)
        {
            var dlg = Create(canvas);
            dlg._onOk = onOk;
            dlg._onCancel = onCancel;

            var stack = dlg.BuildFrame(canvas, title);

            // 正文
            stack.Text(body, Theme.FontBody, Theme.Muted);

            // 按钮行
            var buttons = new List<Button>();
            if (!string.IsNullOrEmpty(cancelText))
            {
                buttons.Add(UiFactory.Button(dlg.BodyHost, cancelText, dlg.Cancel));
            }
            buttons.Add(UiFactory.Primary(dlg.BodyHost, okText, dlg.Ok));
            stack.RowOf(buttons, Theme.ButtonHeight);

            dlg.Commit();
            return dlg;
        }

        // ==================== 单选列表 ====================

        /// <summary>显示选项列表，选中后回调。</summary>
        public static Dialog Pick(
            Transform canvas,
            string title,
            IReadOnlyList<string> options,
            int selectedIndex,
            Action<int> onPick)
        {
            var dlg = Create(canvas);
            dlg._onCancel = null;

            var stack = dlg.BuildFrame(canvas, title);

            for (var i = 0; i < options.Count; i++)
            {
                var idx = i;
                var picked = i == selectedIndex;

                var btn = UiFactory.Button(dlg.BodyHost, options[i], () =>
                {
                    onPick?.Invoke(idx);
                    dlg.Close();
                }, picked ? Theme.AccentSoft : Theme.Panel);

                if (picked)
                {
                    var txt = btn.GetComponentInChildren<Text>();
                    if (txt != null) txt.fontStyle = FontStyle.Bold;
                }

                stack.Add(btn.GetComponent<RectTransform>(), Theme.ButtonHeightSmall + 6f);
            }

            var cancel = UiFactory.Button(dlg.BodyHost, "取消", dlg.Close);
            stack.Add(cancel.GetComponent<RectTransform>(), Theme.ButtonHeightSmall);

            dlg.Commit();
            return dlg;
        }

        // ==================== 内部 ====================

        private RectTransform _card;
        private RectTransform _bodyHost;
        private VStack _stack;

        /// <summary>卡片内部可放内容的容器。</summary>
        private RectTransform BodyHost => _bodyHost;

        private static Dialog Create(Transform canvas)
        {
            if (_current != null) Destroy(_current.gameObject);

            var go = new GameObject("Dialog", typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            var rt = (RectTransform)go.transform;
            UiFactory.Fill(rt);

            var dlg = go.AddComponent<Dialog>();
            _current = dlg;

            // 半透明遮罩（拦截点击）
            var overlay = UiFactory.Rect(go.transform, new Color(0f, 0f, 0f, 0.55f), "Overlay");
            UiFactory.Fill(overlay.rectTransform);
            overlay.raycastTarget = true;

            var overlayBtn = overlay.gameObject.AddComponent<Button>();
            overlayBtn.transition = Selectable.Transition.None;
            overlayBtn.onClick.AddListener(() => { /* 点遮罩不关闭，防误触 */ });

            return dlg;
        }

        /// <summary>建好卡片和标题，返回可继续添加内容的 VStack。</summary>
        private VStack BuildFrame(Transform canvas, string title)
        {
            // 卡片容器
            var cardGo = new GameObject("Card", typeof(RectTransform));
            cardGo.transform.SetParent(transform, false);
            _card = (RectTransform)cardGo.transform;

            _card.anchorMin = _card.anchorMax = new Vector2(0.5f, 0.5f);
            _card.pivot = new Vector2(0.5f, 0.5f);
            _card.sizeDelta = new Vector2(880f, 100f);

            var cardImg = cardGo.AddComponent<Image>();
            cardImg.sprite = UiFactory.RoundSprite;
            cardImg.type = Image.Type.Sliced;
            cardImg.color = Color.white;
            cardImg.pixelsPerUnitMultiplier = 1f;

            var outline = cardGo.AddComponent<Outline>();
            outline.effectColor = Theme.PanelBorder;
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            // 内容容器
            var bodyGo = new GameObject("Body", typeof(RectTransform));
            bodyGo.transform.SetParent(_card, false);
            _bodyHost = (RectTransform)bodyGo.transform;
            UiFactory.Fill(_bodyHost);

            _stack = new VStack(_bodyHost, 40f, 20f, 36f);

            // 标题
            _stack.Text(title, Theme.FontTitle, Theme.Ink, bold: true, minHeight: 60f);

            return _stack;
        }

        /// <summary>按内容高度调整卡片尺寸并居中。</summary>
        private void Commit()
        {
            var h = _stack.Height;
            _card.sizeDelta = new Vector2(880f, h);
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
            if (_closed) return;
            _closed = true;

            if (_current == this) _current = null;
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_current == this) _current = null;
        }
    }
}
