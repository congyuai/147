using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Dustline.Editor
{
    sealed class PortTextureSetup : AssetPostprocessor
    {
        const string Root = "Assets/Dustline/Resources/";
        const int Size = PortRaster.Resolution;
        const string Android = "Android";
        static string ChartPath => Root + PortRaster.Chart + ".png";
        static string GroundPath => Root + PortRaster.Ground + ".png";

        void OnPreprocessTexture()
        {
            bool isSurface = assetPath.StartsWith(Root + "Dustline/Surfaces/", StringComparison.Ordinal)
                && assetPath.EndsWith("_2k.jpg", StringComparison.Ordinal);
            if (!isSurface && assetPath != GroundPath && assetPath != ChartPath) return;
            Configure((TextureImporter)assetImporter, assetPath);
        }

        internal static bool IsPrepared()
        {
            if (!IsReady(ChartPath) || !IsReady(GroundPath)) return false;
            foreach (PortRaster.Finish finish in Enum.GetValues(typeof(PortRaster.Finish)))
                if (!IsReady(TexturePath(finish, false)) || !IsReady(TexturePath(finish, true))) return false;
            return true;
        }

        internal static void Prepare()
        {
            foreach (PortRaster.Finish finish in Enum.GetValues(typeof(PortRaster.Finish)))
            {
                Import(TexturePath(finish, false));
                Import(TexturePath(finish, true));
            }
            BakeMap(ChartPath, true);
            BakeMap(GroundPath, false);
        }

        static string TexturePath(PortRaster.Finish finish, bool normal) => Root + PortRaster.Surface(finish, normal) + ".jpg";

        static bool IsNormal(string path) => path.EndsWith("_normal_2k.jpg", StringComparison.Ordinal);

        static TextureImporterFormat AndroidFormat(string path) => IsNormal(path) || path == GroundPath
            ? TextureImporterFormat.ETC2_RGBA8 : TextureImporterFormat.ETC2_RGB4;

        static bool IsReady(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return false;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var settings = importer.GetPlatformTextureSettings(Android);
            bool surface = path.EndsWith(".jpg", StringComparison.Ordinal);
            if (texture == null || texture.width != Size || texture.height != Size || texture.mipmapCount != 12) return false;
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            return width == Size && height == Size
                && !importer.isReadable && importer.ignoreMipmapLimit && !importer.streamingMipmaps
                && importer.filterMode == FilterMode.Trilinear && importer.maxTextureSize == Size
                && importer.wrapMode == (surface ? TextureWrapMode.Repeat : TextureWrapMode.Clamp)
                && importer.anisoLevel == (path == ChartPath ? 1 : 8)
                && importer.sRGBTexture != IsNormal(path) && !importer.convertToNormalmap && !importer.flipGreenChannel
                && importer.textureType == (IsNormal(path) ? TextureImporterType.NormalMap : TextureImporterType.Default)
                && importer.alphaSource == (path == GroundPath ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None)
                && !importer.alphaIsTransparency
                && settings.overridden && settings.maxTextureSize == Size && settings.format == AndroidFormat(path);
        }

        static void Import(string path)
        {
            if (!File.Exists(path)) throw new BuildFailedException("缺少 2K 港区贴图：" + path);
            if (IsReady(path)) return;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            if (!IsReady(path)) throw new BuildFailedException("无法按 2048×2048 导入港区贴图：" + path);
        }

        static void BakeMap(string path, bool chart)
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var source = PortRaster.Bake(chart);
                try { File.WriteAllBytes(path, source.EncodeToPNG()); }
                finally { UnityEngine.Object.DestroyImmediate(source); }
            }
            Import(path);
        }

        static void Configure(TextureImporter importer, string path)
        {
            bool normal = IsNormal(path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal;
            importer.convertToNormalmap = false;
            importer.flipGreenChannel = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.alphaSource = path == GroundPath ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = false;
            importer.maxTextureSize = Size;
            importer.isReadable = false;
            importer.mipmapEnabled = true;
            importer.ignoreMipmapLimit = true;
            importer.streamingMipmaps = false;
            importer.filterMode = FilterMode.Trilinear;
            importer.wrapMode = path.EndsWith(".jpg", StringComparison.Ordinal) ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            importer.anisoLevel = path == ChartPath ? 1 : 8;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.crunchedCompression = false;
            var android = importer.GetPlatformTextureSettings(Android);
            android.overridden = true;
            android.maxTextureSize = Size;
            android.format = AndroidFormat(path);
            android.compressionQuality = 90;
            importer.SetPlatformTextureSettings(android);
        }
    }
}
