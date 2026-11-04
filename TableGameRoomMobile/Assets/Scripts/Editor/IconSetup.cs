using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace TableGameRoom.Mobile
{
    /// <summary>
    /// 图标与开屏配置。
    ///
    /// 安卓图标不能只改 ProjectSettings.asset —— Unity 构建时会用编辑器内存里的
    /// PlayerSettings 覆盖回去，必须在构建前用 API 设置。
    ///
    /// 三层图标：
    ///   Adaptive（API 26+）  前景 = 白圆+双环+T，背景 = 纯青色铺满
    ///   Round（API 25）      完整圆形图标
    ///   Legacy               完整方形图标
    ///
    /// 开屏：深色背景 + 居中大 logo，直接显示游戏品牌而不是 Unity 默认黑屏。
    /// </summary>
    public static class IconSetup
    {
        private const string IconDir = "Assets/Icons";

        /// <summary>配置全部图标与开屏。构建前调用。</summary>
        public static void Apply()
        {
            EnsureImportSettings();

            var legacy = Load("app_icon");
            var round = Load("app_icon");
            var fg = Load("app_icon_fg");
            var bg = Load("app_icon_bg");
            var splash = Load("splash_logo");

            if (legacy == null || round == null || fg == null || bg == null || splash == null)
            {
                Debug.LogError("[IconSetup] 图标资源缺失，跳过图标配置。");
                return;
            }

            ApplyPlatformIcons(NamedBuildTarget.Android, legacy, round, fg, bg);
            ApplySplash(splash);

            AssetDatabase.SaveAssets();
            Debug.Log("[IconSetup] 图标与开屏已配置。");
        }

        // ==================== 图标 ====================

        private static void ApplyPlatformIcons(
            NamedBuildTarget target,
            Texture2D legacy, Texture2D round, Texture2D fg, Texture2D bg)
        {
            var kinds = PlayerSettings.GetSupportedIconKinds(target);

            foreach (var kind in kinds)
            {
                var icons = PlayerSettings.GetPlatformIcons(target, kind);
                var name = kind.ToString();

                for (var i = 0; i < icons.Length; i++)
                {
                    var icon = icons[i];

                    if (name.StartsWith("Adaptive", StringComparison.Ordinal))
                    {
                        // 自适应图标固定两层，顺序是 [背景, 前景] —— 第 0 层在最底下。
                        // 传反了的话纯色背景会把图形整个盖住，桌面上只剩一块青色。
                        icon.SetTextures(new[] { bg, fg });
                    }
                    else if (name.StartsWith("Round", StringComparison.Ordinal))
                    {
                        icon.SetTextures(new[] { round });
                    }
                    else
                    {
                        icon.SetTextures(new[] { legacy });
                    }

                    icons[i] = icon;
                }

                PlayerSettings.SetPlatformIcons(target, kind, icons);
                Debug.Log($"[IconSetup] {name}: {icons.Length} 个尺寸已设置");
            }
        }

        // ==================== 开屏 ====================

        private static void ApplySplash(Texture2D logo)
        {
            PlayerSettings.SplashScreen.show = true;
            PlayerSettings.SplashScreen.showUnityLogo = false;   // 去掉 Unity 默认 logo

            // 背景用顶栏同款深色，logo 是白底圆盘，对比强烈
            PlayerSettings.SplashScreen.backgroundColor = new Color(0.125f, 0.165f, 0.188f, 1f);

            PlayerSettings.SplashScreen.drawMode = PlayerSettings.SplashScreen.DrawMode.AllSequential;
            PlayerSettings.SplashScreen.animationMode = PlayerSettings.SplashScreen.AnimationMode.Static;
            PlayerSettings.SplashScreen.logos = new[]
            {
                PlayerSettings.SplashScreenLogo.Create(2.0f, SpriteOf(logo))
            };

            Debug.Log("[IconSetup] 开屏已配置：品牌 logo 2 秒，深色背景，无 Unity logo");
        }

        private static Sprite SpriteOf(Texture2D tex)
        {
            var path = AssetDatabase.GetAssetPath(tex);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) return sprite;

            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }

        // ==================== 资源导入 ====================

        /// <summary>
        /// 确保图标按「Sprite + 不压缩 + mipmap 关」导入。
        /// 默认的纹理压缩会把纯色环压出脏边，1024 的图必须保持无损。
        /// </summary>
        private static void EnsureImportSettings()
        {
            if (!Directory.Exists(IconDir))
            {
                Debug.LogError($"[IconSetup] 目录不存在: {IconDir}");
                return;
            }

            foreach (var file in Directory.GetFiles(IconDir, "*.png"))
            {
                var path = file.Replace('\\', '/');
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                var dirty = false;

                if (importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    dirty = true;
                }
                if (importer.spriteImportMode != SpriteImportMode.Single)
                {
                    importer.spriteImportMode = SpriteImportMode.Single;
                    dirty = true;
                }
                if (importer.textureCompression != TextureImporterCompression.Uncompressed)
                {
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    dirty = true;
                }
                if (importer.mipmapEnabled)
                {
                    importer.mipmapEnabled = false;
                    dirty = true;
                }
                if (importer.maxTextureSize < 1024)
                {
                    importer.maxTextureSize = 1024;
                    dirty = true;
                }
                if (importer.filterMode != FilterMode.Bilinear)
                {
                    importer.filterMode = FilterMode.Bilinear;
                    dirty = true;
                }
                if (importer.alphaIsTransparency == false)
                {
                    importer.alphaIsTransparency = true;
                    dirty = true;
                }

                if (dirty)
                {
                    importer.SaveAndReimport();
                    Debug.Log($"[IconSetup] 导入设置已修正: {path}");
                }
            }
        }

        private static Texture2D Load(string name)
        {
            var path = $"{IconDir}/{name}.png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null) Debug.LogError($"[IconSetup] 找不到 {path}");
            return tex;
        }
    }
}
