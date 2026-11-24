using UnityEngine;

namespace Dustline
{
    internal static class QuarryRaster
    {
        internal const int ChartResolution = 2048;
        const int GroundResolution = 1024;
        static readonly Color32 Rock = new Color32(116, 108, 94, 224);
        static readonly Color32 Strata = new Color32(159, 143, 121, 224);
        static readonly Color32 Gravel = new Color32(140, 128, 104, 224);
        static readonly Color32 Bench = new Color32(111, 115, 99, 224);
        static readonly Color32 Road = new Color32(174, 156, 126, 224);
        static readonly Color32 Hardstand = new Color32(137, 136, 120, 96);
        static readonly Color32 Water = new Color32(49, 78, 82, 255);

        internal static Texture2D BakeChart() => Bake(true);
        internal static Texture2D BakeGround() => Bake(false);

        static Texture2D Bake(bool chart)
        {
            int size = chart ? ChartResolution : GroundResolution;
            float texel = QuarryLayout.Size / size;
            float first = -QuarryLayout.HalfSize + texel * .5f;
            var pixels = new Color32[size * size];
            for (int j = 0; j < size; j++)
            {
                float z = first + j * texel;
                for (int i = 0; i < size; i++)
                {
                    float x = first + i * texel;
                    float h = QuarryTerrain.Sample(x, z, out var grade);
                    float strata = Mathf.Abs(Mathf.Repeat(h + x * .031f - z * .023f, 4.3f) - 2.15f);
                    var color = Color32.Lerp(Rock, Strata, Mathf.Clamp01(.95f - strata * .35f));
                    float steep = grade.sqrMagnitude;
                    color = Color32.Lerp(Color32.Lerp(Gravel, Bench, Mathf.Clamp01((h + 4f) / 51f)), color, Mathf.Clamp01(steep * 2.7f));
                    float grain = Grain(i, j) * (chart ? 4 : 9);
                    float shade = grain + Mathf.Clamp((grade.x * .42f + grade.y * .36f) * (chart ? 29 : 11), -27, 25);
                    color = Offset(color, shade);
                    if (chart && steep > .013f)
                    {
                        float contour = Mathf.Abs(Mathf.Repeat(h + 3.5f, 7f) - 3.5f);
                        color = Color32.Lerp(color, new Color32(76, 68, 59, 255), Mathf.Clamp01(1f - contour / .48f) * .34f);
                    }
                    pixels[i + j * size] = QuarryTerrain.IsPond(x, z, h)
                        ? Color32.Lerp(Water, new Color32(76, 111, 104, 255), Mathf.Clamp01((h - 5f) / 4.4f)) : color;
                }
            }

            foreach (var road in QuarryTerrain.Segments)
            {
                float span = road.HalfWidth + 1.6f;
                float width = road.HalfWidth;
                Stamp(Rect.MinMaxRect(Mathf.Min(road.A.x, road.B.x) - span, Mathf.Min(road.A.z, road.B.z) - span,
                    Mathf.Max(road.A.x, road.B.x) + span, Mathf.Max(road.A.z, road.B.z) + span), (x, z, c) =>
                {
                    float d = Mathf.Sqrt(road.DistanceSq(x, z, out _));
                    if (d >= span) return c;
                    return Color32.Lerp(c, Color32.Lerp(Offset(Road, -24), Road, Mathf.Clamp01(width - d + .7f)),
                        Mathf.Clamp01((span - d) * .7f));
                });
            }
            foreach (var pad in QuarryLayout.Pads)
            {
                Rect a = pad.Area;
                Stamp(Rect.MinMaxRect(a.xMin - 1, a.yMin - 1, a.xMax + 1, a.yMax + 1), (x, z, c) =>
                {
                    float d = pad.Distance(x, z);
                    if (d >= 1f) return c;
                    return Color32.Lerp(c, Hardstand, 1f - d);
                });
            }

            foreach (var exit in QuarryLayout.Exits)
            {
                float x0 = exit.Position.x, z0 = exit.Position.z, r = exit.Radius;
                Stamp(Rect.MinMaxRect(x0-r-1, z0-r-1, x0+r+1, z0+r+1), (x, z, c) =>
                {
                    float d = Mathf.Sqrt((x-x0)*(x-x0) + (z-z0)*(z-z0));
                    var pad = Color32.Lerp(c, new Color32(57, 77, 71, 96), Mathf.Clamp01((r - d) / texel + .5f));
                    return Color32.Lerp(pad, new Color32(125, 199, 160, 96), Mathf.Clamp01(1f - Mathf.Abs(d - r + .6f) / .85f));
                });
            }

            if (chart)
            {
                foreach (var shed in QuarryLayout.Sheds) Roof(shed.Area);
                Roof(QuarryLayout.CrusherFootprint);
                for (int j = 0; j < size; j++)
                {
                    float gz = Mathf.Abs(Mathf.Repeat(first + j * texel + 45f, 90f) - 45f);
                    for (int i = 0; i < size; i++)
                    {
                        float gx = Mathf.Abs(Mathf.Repeat(first + i * texel + 45f, 90f) - 45f);
                        float line = Mathf.Clamp01(1f - Mathf.Min(gx, gz) * 1.45f);
                        if (line > 0) pixels[i + j * size] = Color32.Lerp(pixels[i + j * size], new Color32(186, 193, 179, 255), line * .22f);
                    }
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, false)
            {
                name = chart ? "Stone Ridge chart 2K" : "Stone Ridge ground",
                filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp, anisoLevel = chart ? 1 : 4
            };
            if (chart)
                for (int i = 0; i < pixels.Length; i++) pixels[i].a = 255;
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;

            void Roof(Rect a) => Stamp(a, (x, z, c) =>
            {
                float edge = Mathf.Min(Mathf.Min(x - a.xMin, a.xMax - x), Mathf.Min(z - a.yMin, a.yMax - z));
                return edge < .95f ? new Color32(65, 76, 78, 255) : new Color32(171, 183, 178, 255);
            });

            void Stamp(Rect a, System.Func<float, float, Color32, Color32> pixel)
            {
                int left = Mathf.Clamp(Mathf.CeilToInt((a.xMin - first) / texel), 0, size - 1);
                int right = Mathf.Clamp(Mathf.FloorToInt((a.xMax - first) / texel), 0, size - 1);
                int bottom = Mathf.Clamp(Mathf.CeilToInt((a.yMin - first) / texel), 0, size - 1);
                int top = Mathf.Clamp(Mathf.FloorToInt((a.yMax - first) / texel), 0, size - 1);
                for (int j = bottom; j <= top; j++)
                    for (int i = left; i <= right; i++)
                    {
                        int k = i + j * size;
                        pixels[k] = pixel(first + i * texel, first + j * texel, pixels[k]);
                    }
            }
        }

        static Color32 Offset(Color32 c, float delta) => new Color32(
            (byte)Mathf.Clamp(c.r + delta, 0, 255), (byte)Mathf.Clamp(c.g + delta, 0, 255),
            (byte)Mathf.Clamp(c.b + delta, 0, 255), c.a);

        static float Grain(int x, int z)
        {
            uint bits = unchecked((uint)x * 0x9E3779B9u ^ (uint)z * 0x85EBCA6Bu);
            bits ^= bits >> 15;
            bits = unchecked(bits * 0xC2B2AE35u);
            return ((bits ^ bits >> 16) & 255) / 255f - .5f;
        }
    }
}
