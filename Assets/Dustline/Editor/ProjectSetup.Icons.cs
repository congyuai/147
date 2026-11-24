using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Dustline.Editor
{
    public static partial class ProjectSetup
    {
        const string LauncherArt = "Assets/Dustline/Branding/";

        static void ConfigureLauncherIcons(bool required)
        {
            if (!AssignLauncherIcons() && required)
                throw new BuildFailedException("安卓启动图标未配置，请安装 Android Build Support、切换到 Android 平台并检查 Branding 资源。");
        }

        static bool AssignLauncherIcons()
        {
            var background = ImportLauncherTexture("AdaptiveBackground", 432);
            var foreground = ImportLauncherTexture("AdaptiveForeground", 432);
            var round = ImportLauncherTexture("Round", 192);
            var legacy = ImportLauncherTexture("Legacy", 192);
            var platform = NamedBuildTarget.Android;
            var kinds = PlayerSettings.GetSupportedIconKinds(platform);
            if (kinds.Length == 0 || background == null || foreground == null || round == null || legacy == null) return false;
            return Set("Adaptive", background, foreground) & Set("Round", round) & Set("Legacy", legacy);

            bool Set(string name, params Texture2D[] textures)
            {
                var kind = Array.Find(kinds, candidate => candidate.ToString().StartsWith(name, StringComparison.OrdinalIgnoreCase));
                if (kind == null) return false;
                var icons = PlayerSettings.GetPlatformIcons(platform, kind);
                if (icons.Length == 0) return false;
                bool changed = false;
                foreach (var icon in icons)
                {
                    if (textures.Length < icon.minLayerCount || textures.Length > icon.maxLayerCount) return false;
                    if (Matches(icon)) continue;
                    icon.SetTextures(textures);
                    changed = true;
                }
                if (!changed) return true;
                PlayerSettings.SetPlatformIcons(platform, kind, icons);
                var assigned = PlayerSettings.GetPlatformIcons(platform, kind);
                return assigned.Length == icons.Length && Array.TrueForAll(assigned, Matches);

                bool Matches(PlatformIcon icon)
                {
                    if (icon.layerCount != textures.Length) return false;
                    for (int i = 0; i < textures.Length; i++)
                        if (icon.GetTexture(i) != textures[i]) return false;
                    return true;
                }
            }
        }

        static Texture2D ImportLauncherTexture(string name, int size)
        {
            string path = LauncherArt + name + ".png";
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return null;
            if (importer.textureType != TextureImporterType.Default || importer.npotScale != TextureImporterNPOTScale.None
                || importer.maxTextureSize != 512 || importer.mipmapEnabled || importer.wrapMode != TextureWrapMode.Clamp
                || importer.textureCompression != TextureImporterCompression.Uncompressed
                || importer.alphaSource != TextureImporterAlphaSource.FromInput || !importer.alphaIsTransparency)
            {
                importer.textureType = TextureImporterType.Default;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = 512;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            return texture != null && texture.width == size && texture.height == size && texture.mipmapCount == 1 ? texture : null;
        }
    }
}
