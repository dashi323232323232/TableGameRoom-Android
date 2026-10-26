using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TableGameRoom.Mobile.UI
{
    /// <summary>
    /// 简易滚动容器 —— 不用 Unity 的 ScrollRect。
    ///
    /// 为什么自己写：
    /// ScrollRect 的 content 尺寸在构建过程中会变化多次，它的 verticalNormalizedPosition
    /// 计算依赖 content.rect.height，在高度刚变化的那几帧里会算出错误的值，导致
    /// 页面初始停在中间、顶部内容被顶栏遮住。
    ///
    /// 这里只做两件事：
    ///   1. 裁剪超出区域（RectMask2D）
    ///   2. 拖动时改变 content 的 anchoredPosition.y，并限制在 [0, contentHeight - viewportHeight]
    ///
    /// 坐标约定：content 的 anchorMin/Max = (0,1)/(1,1)，pivot = (0.5,1)
    ///   → anchoredPosition.y == 0 表示顶部对齐
    ///   → anchoredPosition.y == -(contentH - viewportH) 表示滚到底
    /// </summary>
    public sealed class SimpleScroll : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private RectTransform _viewport;
        private RectTransform _content;
        private float _minY;      // 允许的最小 y（负数）
        private float _velocity;
        private bool _dragging;

        /// <summary>内容高度（由 VStack.Finish 设置后同步过来）。</summary>
        public float ContentHeight => _content != null ? _content.rect.height : 0f;

        public RectTransform Content => _content;

        /// <summary>
        /// 创建一个滚动容器。返回 content 容器（配合 VStack 使用）。
        /// </summary>
        public static RectTransform Create(Transform parent, out SimpleScroll scroller)
        {
            // 根节点（铺满父级，负责接收拖拽）
            var rootGo = new GameObject("ScrollRoot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            rootGo.transform.SetParent(parent, false);

            var rootRect = (RectTransform)rootGo.transform;
            UiFactory.Fill(rootRect);

            var rootImg = rootGo.GetComponent<Image>();
            rootImg.color = new Color(1f, 1f, 1f, 0f);
            rootImg.raycastTarget = true;   // 必须能接收拖拽

            scroller = rootGo.AddComponent<SimpleScroll>();

            // Viewport（裁剪）
            var vpGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            vpGo.transform.SetParent(rootGo.transform, false);

            var vpRect = (RectTransform)vpGo.transform;
            UiFactory.Fill(vpRect);

            // Content
            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(vpGo.transform, false);

            var contentRect = (RectTransform)contentGo.transform;
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, 100f);

            scroller._viewport = vpRect;
            scroller._content = contentRect;

            return contentRect;
        }

        /// <summary>内容高度变化后调用，重算可滚动范围并归位到顶部。</summary>
        public void Refresh(bool resetToTop = true)
        {
            if (_content == null || _viewport == null) return;

            Canvas.ForceUpdateCanvases();

            var contentH = _content.rect.height;
            var viewportH = _viewport.rect.height;

            _minY = Mathf.Min(0f, -(contentH - viewportH));

            if (resetToTop)
            {
                _velocity = 0f;
                SetY(0f);
            }
            else
            {
                SetY(_content.anchoredPosition.y);
            }
        }

        private void SetY(float y)
        {
            y = Mathf.Clamp(y, _minY, 0f);
            _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, y);
        }

        // ---------- 拖拽 ----------

        public void OnBeginDrag(PointerEventData e)
        {
            _dragging = true;
            _velocity = 0f;
        }

        public void OnDrag(PointerEventData e)
        {
            if (!_dragging) return;
            SetY(_content.anchoredPosition.y + e.delta.y);
        }

        public void OnEndDrag(PointerEventData e)
        {
            _dragging = false;
            _velocity = e.delta.y / Mathf.Max(0.001f, Time.unscaledDeltaTime);
        }

        private void Update()
        {
            if (_dragging) return;
            if (Mathf.Abs(_velocity) < 1f) return;

            // 惯性滑动
            SetY(_content.anchoredPosition.y + _velocity * Time.unscaledDeltaTime);
            _velocity *= Mathf.Pow(0.02f, Time.unscaledDeltaTime);
        }
    }
}
