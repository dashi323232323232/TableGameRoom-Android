using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TableGameRoom.Mobile
{
    /// <summary>
    /// 命令行构建脚本。用法：
    ///   Unity.exe -batchmode -quit -projectPath &lt;项目路径&gt; ^
    ///     -executeMethod TableGameRoom.Mobile.BuildScript.BuildAndroid ^
    ///     -logFile build.log
    ///
    /// 产物：Builds/TableGameRoom.apk
    /// </summary>
    public static class BuildScript
    {
        private const string OutputDir = "Builds";
        private const string ApkName = "TableGameRoom.apk";

        // 外部工具链路径（安装目录无写权限，工具链放在 D 盘）
        private static string ToolchainRoot =>
            Environment.GetEnvironmentVariable("TABLE_ANDROID_TOOLCHAIN")
            ?? @"D:\dsh\_android_sdk";

        /// <summary>配置 Android 工具链路径。</summary>
        public static void ConfigureAndroidTools()
        {
            var root = ToolchainRoot;
            var sdk = Path.Combine(root, "sdk");
            var ndk = Path.Combine(root, "ndk");
            var jdk = Path.Combine(root, "jdk");

            Debug.Log($"[BuildScript] 工具链: SDK={sdk}({Directory.Exists(sdk)}) NDK={ndk}({Directory.Exists(ndk)}) JDK={jdk}({Directory.Exists(jdk)})");

            if (Directory.Exists(sdk)) UnityEditor.Android.AndroidExternalToolsSettings.sdkRootPath = sdk;
            if (Directory.Exists(ndk)) UnityEditor.Android.AndroidExternalToolsSettings.ndkRootPath = ndk;
            if (Directory.Exists(jdk)) UnityEditor.Android.AndroidExternalToolsSettings.jdkRootPath = jdk;
        }

        [MenuItem("TABLE/构建 Android APK")]
        public static void BuildAndroidMenu() => BuildAndroid();

        public static void BuildAndroid()
        {
            try
            {
                ConfigureAndroidTools();

                var projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
                var outputDir = Path.Combine(projectRoot, OutputDir);
                Directory.CreateDirectory(outputDir);

                var apkPath = Path.Combine(outputDir, ApkName);

                // ---------- Player 设置 ----------
                PlayerSettings.companyName = "TABLE";
                PlayerSettings.productName = "TABLE 游戏厅";
                PlayerSettings.SetApplicationIdentifier(
                    BuildTargetGroup.Android, "com.table.gameroom");

                PlayerSettings.Android.bundleVersionCode = 1;
                PlayerSettings.bundleVersion = "1.0";

                // 竖屏锁定
                PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
                PlayerSettings.allowedAutorotateToPortrait = true;
                PlayerSettings.allowedAutorotateToLandscapeLeft = false;
                PlayerSettings.allowedAutorotateToLandscapeRight = false;

                // ARM64 + ARMv7（MuMu 是 x86_64，需要 ARM 转译；真机用 ARM64）
                PlayerSettings.Android.targetArchitectures =
                    AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;

                // 使用 IL2CPP（性能更好，且是 Play Store 要求）
                PlayerSettings.SetScriptingBackend(
                    BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);

                // 最小 API 24 (Android 7.0)，目标 API 34
                PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
                PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;

                // 图形 API：Vulkan + GLES3
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[]
                {
                    UnityEngine.Rendering.GraphicsDeviceType.Vulkan,
                    UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3
                });

                // ---------- 场景列表 ----------
                // 项目不依赖场景资源，用一个空场景即可（Bootstrap 会在运行时创建一切）
                var scenes = new[] { "Assets/Scenes/Main.unity" };
                if (!File.Exists(Path.Combine(projectRoot, scenes[0])))
                {
                    CreateEmptyScene(projectRoot, scenes[0]);
                }
                EditorBuildSettings.scenes = new[]
                {
                    new EditorBuildSettingsScene(scenes[0], true)
                };

                // ---------- 构建 ----------
                Debug.Log($"[BuildScript] 开始构建 → {apkPath}");

                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = apkPath,
                    target = BuildTarget.Android,
                    options = BuildOptions.None
                };

                var report = BuildPipeline.BuildPlayer(options);
                var summary = report.summary;

                Debug.Log($"[BuildScript] 结果: {summary.result}");
                Debug.Log($"[BuildScript] 大小: {summary.totalSize / 1024 / 1024} MB");
                Debug.Log($"[BuildScript] 耗时: {summary.totalTime}");
                Debug.Log($"[BuildScript] 错误数: {summary.totalErrors}");

                if (summary.result == BuildResult.Succeeded)
                {
                    Debug.Log($"[BuildScript] ✓ 构建成功: {apkPath}");
                    EditorApplication.Exit(0);
                }
                else
                {
                    Debug.LogError($"[BuildScript] ✗ 构建失败");
                    EditorApplication.Exit(1);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BuildScript] 异常: {ex}");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>创建一个空场景（只需一个空 GameObject 挂 Bootstrap）。</summary>
        private static void CreateEmptyScene(string projectRoot, string relativePath)
        {
            var full = Path.Combine(projectRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);

            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            var go = new GameObject("Bootstrap");
            go.AddComponent<Bootstrap>();

            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, relativePath);
            Debug.Log($"[BuildScript] 已创建场景: {relativePath}");
        }

        /// <summary>运行联机协议自检（命令行：-executeMethod TableGameRoom.Mobile.BuildScript.RunProtocolTest）。</summary>
        public static void RunProtocolTest()
        {
            var result = ProtocolSelfTest.Run();
            var logPath = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "protocol-test.txt");
            File.WriteAllText(logPath, result);
            Debug.Log($"[BuildScript] 协议自检结果已写入: {logPath}");
            EditorApplication.Exit(result.Contains("0 失败") ? 0 : 1);
        }

        /// <summary>仅用于验证许可证 / 环境是否就绪。</summary>
        public static void CheckEnvironment()
        {
            Debug.Log("[BuildScript] Unity 版本: " + Application.unityVersion);
            Debug.Log("[BuildScript] 项目路径: " + Directory.GetParent(Application.dataPath)!.FullName);
            Debug.Log("[BuildScript] 构建目标: " + EditorUserBuildSettings.activeBuildTarget);
            Debug.Log("[BuildScript] Android 模块: " +
                (Directory.Exists(Path.Combine(EditorApplication.applicationContentsPath,
                    "PlaybackEngines/AndroidPlayer")) ? "已安装" : "缺失"));

            ConfigureAndroidTools();
            Debug.Log("[BuildScript] SDK: " + UnityEditor.Android.AndroidExternalToolsSettings.sdkRootPath);
            Debug.Log("[BuildScript] NDK: " + UnityEditor.Android.AndroidExternalToolsSettings.ndkRootPath);
            Debug.Log("[BuildScript] JDK: " + UnityEditor.Android.AndroidExternalToolsSettings.jdkRootPath);

            EditorApplication.Exit(0);
        }
    }
}
