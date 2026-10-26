using UnityEngine;
using UnityEngine.EventSystems;

namespace TableGameRoom.Mobile.UI
{
    /// <summary>
    /// 让按钮的拖拽事件冒泡到父级滚动容器。
    ///
    /// UGUI 的 Button 只处理 Click，拖拽事件本来会冒泡；
    /// 但如果按钮上有 Image 且 raycastTarget=true，事件会被它吃掉。
    /// 这里显式把拖拽转发给父级。
    /// </summary>
    public sealed class DragPassThrough : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private SimpleScroll _scroll;

        /// <summary>动态查找（不能缓存：Awake 时父级链可能还没建好）。</summary>
        private SimpleScroll Scroll
        {
            get
            {
                if (_scroll == null) _scroll = GetComponentInParent<SimpleScroll>();
                return _scroll;
            }
        }

        public void OnBeginDrag(PointerEventData e) => Scroll?.OnBeginDrag(e);

        public void OnDrag(PointerEventData e) => Scroll?.OnDrag(e);

        public void OnEndDrag(PointerEventData e) => Scroll?.OnEndDrag(e);
    }
}
