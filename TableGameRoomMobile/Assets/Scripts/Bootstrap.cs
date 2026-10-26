using UnityEngine;

namespace TableGameRoom.Mobile
{
    /// <summary>
    /// 场景入口。挂在一个空 GameObject 上即可启动整个游戏。
    ///
    /// 因为不依赖任何 prefab / 场景资源，这个脚本可以在任意空场景里跑起来：
    /// 1. 新建场景
    /// 2. 建一个空 GameObject
    /// 3. 挂上 Bootstrap
    /// 4. 直接运行
    /// </summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        private void Awake()
        {
            // 全局配置
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            // 创建 App 单例（它会自动搭建 Canvas 并进入模式选择页）
            var go = new GameObject("App");
            go.AddComponent<App>();
        }
    }
}
