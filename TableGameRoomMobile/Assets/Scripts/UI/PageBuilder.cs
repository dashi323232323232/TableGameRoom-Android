using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace TableGameRoom.Mobile.UI
{
    /// <summary>
    /// 页面构建延迟器。
    ///
    /// UGUI 的 RectTransform 宽度要等 Canvas 完成一次布局后才有正确值，
    /// 如果 Awake 里立刻构建页面，FlowLayout 拿到的宽度是 0，会导致元素宽度错乱。
    ///
    /// 用法：App.BeginPage() 返回的容器交给 <see cref="PageBuilder.Run"/>，
    /// 它会等一帧（Canvas 布局完成）后再执行构建回调。
    /// </summary>
    public sealed class PageBuilder : MonoBehaviour
    {
        private Action<RectTransform> _build;

        public static void Run(RectTransform content, Action<RectTransform> build)
        {
            var go = new GameObject("PageBuilder");
            go.transform.SetParent(content, false);
            var pb = go.AddComponent<PageBuilder>();
            pb._build = build;
            pb.StartCoroutine(pb.DelayedBuild(content));
        }

        private IEnumerator DelayedBuild(RectTransform content)
        {
            // 等两帧：第一帧 Canvas 完成尺寸计算，第二帧 LayoutRebuilder 应用
            yield return null;
            yield return null;

            Canvas.ForceUpdateCanvases();

            try
            {
                _build?.Invoke(content);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PageBuilder] 构建页面失败: {ex}");
            }

            // 构建完再强制刷新一次
            Canvas.ForceUpdateCanvases();

            Destroy(gameObject);
        }
    }
}
