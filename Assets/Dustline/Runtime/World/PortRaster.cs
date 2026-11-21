using UnityEngine;

namespace Dustline
{
    public static class PortRaster
    {
        public const int Resolution = 2048;
        public const string Chart = "Dustline/Maps/NorthCape_2k";
        public const string Ground = "Dustline/Maps/NorthCapeGround_2k";
        public const string SurfaceShader = "Dustline/Harbor Surface";

        public enum Finish { Asphalt, Concrete, Steel, Aggregate }

        public static string Surface(Finish finish, bool normal) =>
            "Dustline/Surfaces/" + finish + (normal ? "_normal_2k" : "_albedo_2k");

#if UNITY_EDITOR
        public static Texture2D Bake(bool chart)
        {
            const float texel = PortLayout.Size / Resolution;
            const float first = -PortLayout.Size * 0.5f + texel * 0.5f;
            var pixels = new Color32[Resolution * Resolution];
            for (int y = 0; y < Resolution; y++)
            {
                float z = first + y * texel;
                for (int x = 0; x < Resolution; x++)
                {
                    var point = new Vector2(first + x * texel, z);
                    pixels[x + y * Resolution] = chart ? ChartPixel(point) : GroundPixel(point, texel);
                }
            }
            var texture = new Texture2D(Resolution, Resolution, chart ? TextureFormat.RGB24 : TextureFormat.RGBA32, false, false);
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        static Color32 ChartPixel(Vector2 point)
        {
            var color = PortLayout.MapColor(point);
            float x = Mathf.Abs(Mathf.Repeat(point.x + 60f, 120f) - 60f);
            float z = Mathf.Abs(Mathf.Repeat(point.y + 60f, 120f) - 60f);
            float grid = Mathf.Clamp01(1f - Mathf.Min(x, z) * 1.35f);
            return Color32.Lerp(color, new Color32(155, 176, 174, 255), 0.20f * grid);
        }

        static Color32 GroundPixel(Vector2 point, float texel)
        {
            var color = PortTerrain.PaintedColor(point, texel, out var kind);
            Finish finish = kind switch
            {
                SurfaceKind.Asphalt => Finish.Asphalt,
                SurfaceKind.Concrete => Finish.Concrete,
                SurfaceKind.Metal => Finish.Steel,
                _ => Finish.Aggregate
            };
            // Class centers stay away from thresholds after texture compression.
            color.a = (byte)(32 + (int)finish * 64);
            return color;
        }
#endif
    }
}
