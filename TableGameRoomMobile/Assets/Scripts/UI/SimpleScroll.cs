using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TableGameRoom.Mobile.UI
{
    /// <summary>
    /// 简易滚动容器 —— 不用 Unity 的 ScrollRect。
    ///
    /// 为什么自己写：
    /// ScrollRect 的 verticalNormalizedPosition 依赖 content.rect.height，
    /// 在 content 高度刚变化的那几帧会算出错误值，导致页面初始停在中间、
    /// 顶部内容被顶栏遮住。多次延迟/强制归位都无效。
    ///
    /// 实现要点：
    /// - RectMask2D 裁剪
    /// - 拖动改变 content.anchoredPosition.y
    /// - 坐标约定：content pivot 在顶部，y == 0 即顶部对齐
    /// - 范围 [0, -(contentH - viewportH)]
    /// </summary>
    public sealed class SimpleScroll : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private RectTransform _viewport;
        private RectTransform _content;

        private float _minY;
        private float _velocity;
        private bool _dragging;
        private float _lastPointerY;

        public RectTransform Content => _content;

        // ==================== 创建 ====================

        public static RectTransform Create(Transform parent, out SimpleScroll scroller)
        {
            var rootGo = new GameObject("ScrollRoot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            rootGo.transform.SetParent(parent, false);

            var rootRect = (RectTransform)rootGo.transform;
            UiFactory.Fill(rootRect);

            var rootImg = rootGo.GetComponent<Image>();
            rootImg.color = new Color(1f, 1f, 1f, 0f);
            rootImg.raycastTarget = true;

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

        // ==================== 刷新 ====================

        /// <summary>
        /// 内容高度变化后调用，重算可滚动范围。
        /// 因为 Canvas 布局要下一帧才生效，这里延迟两帧再算。
        /// </summary>
        public void Refresh(bool resetToTop = true)
        {
            if (_content == null || _viewport == null) return;

            // 立即算一次（此时 viewport 可能还是 0，但 content 高度是对的）
            ApplyRange(resetToTop);

            // 再延迟两帧算一次（等 Canvas 布局完成，viewport 尺寸就绪）
            StopAllCoroutines();
            StartCoroutine(RefreshRoutine(resetToTop));
        }

        /// <summary>立即应用可滚动范围。</summary>
        private void ApplyRange(bool resetToTop)
        {
            var contentH = _content.rect.height;
            var viewportH = _viewport.rect.height;

            if (contentH > 1f && viewportH > 1f)
            {
                _minY = Mathf.Min(0f, -(contentH - viewportH));
            }

            if (resetToTop)
            {
                _velocity = 0f;
                _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, 0f);
            }
        }

        private System.Collections.IEnumerator RefreshRoutine(bool resetToTop)
        {
            // 等两帧让 Canvas 完成布局（viewport 尺寸、content 高度）
            yield return null;
            yield return null;

            Canvas.ForceUpdateCanvases();

            var contentH = _content.rect.height;
            var viewportH = _viewport.rect.height;

            // viewport 还没算出来时再等一帧
            if (viewportH <= 1f)
            {
                yield return null;
                Canvas.ForceUpdateCanvases();
                viewportH = _viewport.rect.height;
                contentH = _content.rect.height;
            }

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

        // ==================== 拖拽 ====================

        public void OnBeginDrag(PointerEventData e)
        {
            _dragging = true;
            _velocity = 0f;
            _lastPointerY = e.position.y;
        }

        public void OnDrag(PointerEventData e)
        {
            if (!_dragging) return;

            var dy = e.position.y - _lastPointerY;
            _lastPointerY = e.position.y;

            // 手指上滑（dy < 0）→ 内容上移（y 减小），所以直接相加
            SetY(_content.anchoredPosition.y + dy);
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

            SetY(_content.anchoredPosition.y + _velocity * Time.unscaledDeltaTime);
            _velocity *= Mathf.Pow(0.02f, Time.unscaledDeltaTime);
        }
    }
}
