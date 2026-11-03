using System;
using System.Collections.Generic;
using UnityEngine;

namespace TableGameRoom.Mobile
{
    /// <summary>
    /// 主线程调度器 —— 网络回调在后台线程触发，
    /// 而 Unity 的 UI 只能在主线程操作，所以统一投递到这里。
    ///
    /// 实现：一个线程安全的队列 + 每帧在 Update 里排空。
    /// </summary>
    public sealed class UnityMainThread : MonoBehaviour
    {
        private static readonly Queue<Action> Queue = new Queue<Action>();
        private static readonly object Gate = new object();

        private static UnityMainThread _instance;

        /// <summary>由 Bootstrap 在启动时创建。</summary>
        public static void Ensure()
        {
            if (_instance != null) return;

            var go = new GameObject("UnityMainThread");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<UnityMainThread>();
        }

        /// <summary>把回调投递到主线程执行。</summary>
        public static void Run(Action action)
        {
            if (action == null) return;

            lock (Gate) Queue.Enqueue(action);
        }

        private void Awake()
        {
            _instance = this;
        }

        private void Update()
        {
            while (true)
            {
                Action next;
                lock (Gate)
                {
                    if (Queue.Count == 0) break;
                    next = Queue.Dequeue();
                }

                try { next?.Invoke(); }
                catch (Exception ex) { Debug.LogError($"[UnityMainThread] 回调异常: {ex}"); }
            }
        }
    }
}
