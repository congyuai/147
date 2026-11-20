using System;
using UnityEngine;

namespace Dustline
{
    enum RaidIcon { Dial, Knob, Aim, Fire, Reload, Crouch, Search, Heal, Bag, Gear, Reticle, Hit, Strike, Prone, Jump, Lean }

    sealed class RaidUiIcons : IDisposable
    {
        const int Tile = 64, Columns = 8;
        readonly Texture2D atlas;
        readonly Sprite[] sprites;

        public RaidUiIcons()
        {
            int count = Enum.GetValues(typeof(RaidIcon)).Length;
            int atlasWidth = Columns * Tile;
            int atlasHeight = Tile * ((count + Columns - 1) / Columns);
            atlas = new Texture2D(atlasWidth, atlasHeight, TextureFormat.RGBA32, false)
            {
                name = "Dustline UI atlas",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            sprites = new Sprite[count];
            var pixels = new Color32[atlasWidth * atlasHeight];
            for (int i = 0; i < count; i++)
            {
                int ox = i % Columns * Tile, oy = i / Columns * Tile;
                for (int y = 1; y < Tile - 1; y++)
                    for (int x = 1; x < Tile - 1; x++)
                    {
                        var p = new Vector2(x + 0.5f - Tile / 2, y + 0.5f - Tile / 2);
                        float alpha = Paint((RaidIcon)i, p);
                        pixels[ox + x + (oy + y) * atlasWidth] = new Color32(255, 255, 255, (byte)(255 * alpha));
                    }
            }
            atlas.SetPixels32(pixels);
            for (int i = 0; i < count; i++)
            {
                var sprite = Sprite.Create(atlas, new Rect(i % Columns * Tile, i / Columns * Tile, Tile, Tile), new Vector2(0.5f, 0.5f), Tile, 0, SpriteMeshType.FullRect);
                sprite.name = ((RaidIcon)i).ToString();
                sprites[i] = sprite;
            }
            atlas.Apply(false, true);
        }

        public Sprite this[RaidIcon icon] => sprites[(int)icon];

        static float Paint(RaidIcon icon, Vector2 p)
        {
            switch (icon)
            {
                case RaidIcon.Dial:
                    return Mathf.Max(Cover(Ring(p, 0, 0, 28, 1.3f)), Cover(p.magnitude - 27) * 0.76f);
                case RaidIcon.Knob:
                    return Mathf.Max(Cover(Ring(p, 0, 0, 26.5f, 1.4f)), Cover(p.magnitude - 26) * 0.57f);
                case RaidIcon.Aim:
                    return Cover(Mathf.Min(Ring(p, 0, 0, 16.5f, 1.9f), Cross(p, 9, 24, 1.9f)));
                case RaidIcon.Fire:
                    return Cover(Mathf.Min(Cross(p, 10, 25, 2.15f), Mathf.Min(Ring(p, 0, 0, 12, 1.7f), p.magnitude - 4)));
                case RaidIcon.Strike:
                    return Cover(Mathf.Min(Segment(p, -16, -17, 15, 15, 4.0f), Mathf.Min(
                        Segment(p, 7, 20, 15, 20, 21, 14), Segment(p, 16, 0, 23, 7, 24, 13))));
                case RaidIcon.Prone:
                    return Cover(Mathf.Min(Segment(p, -19, 6, 18, 6, 5), Mathf.Min(
                        Segment(p, -13, -3, 13, -3, 4), Circle(p, -16, 6, 3.8f))));
                case RaidIcon.Jump:
                    return Cover(Mathf.Min(Segment(p, -19, 4, -5, 16, 4), Mathf.Min(
                        Segment(p, -5, 16, 10, 4, 4), Segment(p, 10, 4, 20, 14, 4))));
                case RaidIcon.Lean:
                    return Cover(Mathf.Min(Segment(p, -12, 18, 12, 18, 4), Mathf.Min(
                        Segment(p, 12, 18, 8, -15, 4), Segment(p, 8, -15, 18, -7, 4))));
                case RaidIcon.Reload:
                    float arc = Ring(p, 0, 0, 19, 2.1f);
                    if (p.x > 5 && p.y > 3) arc = 50;
                    return Cover(Mathf.Min(arc, Mathf.Min(
                        Segment(p, -6, 12, 5, 18, -4, 25), Box(p, 0, -1, 4.5f, 9))));
                case RaidIcon.Crouch:
                    return Cover(Mathf.Min(Circle(p, 13, 13, 4.5f), Mathf.Min(Segment(p, 6, 8, -3, 1, -12, -7),
                        Mathf.Min(Segment(p, -12, -7, 3, -7, 11, -16), Segment(p, 6, 6, 13, -2, 21, -2)))));
                case RaidIcon.Search:
                    return Cover(Mathf.Min(Ring(p, -4, 4, 12.5f, 2.5f), Segment(p, 6, -6, 20, -20, 2.8f)));
                case RaidIcon.Heal:
                    return Cover(Mathf.Min(Outline(p, 0, 0, 19, 17, 2.2f), Cross(p, 0, 10.5f, 2.7f)));
                case RaidIcon.Bag:
                    return Cover(Mathf.Min(Mathf.Min(Outline(p, 0, -4, 16, 17, 2.1f), Outline(p, 0, 17, 6, 4, 1.9f)),
                        Mathf.Min(Segment(p, -15, 3, 15, 3, 1.85f), Box(p, 0, -3, 3.5f, 3.5f))));
                case RaidIcon.Gear:
                    float teeth = Ring(p, 0, 0, 17, 2.2f);
                    for (int tooth = 0; tooth < 8; tooth++)
                    {
                        float a = tooth * Mathf.PI / 4;
                        float c = Mathf.Cos(a), s = Mathf.Sin(a);
                        teeth = Mathf.Min(teeth, Segment(p, c * 17, s * 17, c * 23, s * 23, 2.65f));
                    }
                    return Cover(Mathf.Min(teeth, Ring(p, 0, 0, 7, 2.2f)));
                case RaidIcon.Reticle:
                    return Cover(Mathf.Min(Cross(p, 8, 25, 1.55f), p.magnitude - 1.5f));
                case RaidIcon.Hit:
                    var diagonal = new Vector2((p.x + p.y) * 0.7071f, (p.x - p.y) * 0.7071f);
                    return Cover(Cross(diagonal, 6, 22, 2.8f));
                default:
                    return 0;
            }
        }

        static float Cover(float d) => Mathf.Clamp01(0.5f - d);
        static float Circle(Vector2 p, float x, float y, float r) => (p - new Vector2(x, y)).magnitude - r;
        static float Ring(Vector2 p, float x, float y, float r, float stroke) => Mathf.Abs(Circle(p, x, y, r)) - stroke;
        static float Box(Vector2 p, float x, float y, float w, float h) => Mathf.Max(Mathf.Abs(p.x - x) - w, Mathf.Abs(p.y - y) - h);
        static float Outline(Vector2 p, float x, float y, float w, float h, float stroke) => Mathf.Abs(Box(p, x, y, w, h)) - stroke;

        static float Cross(Vector2 p, float gap, float end, float width)
        {
            p = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y));
            return Mathf.Min(Segment(p, gap, 0, end, 0, width), Segment(p, 0, gap, 0, end, width));
        }

        static float Segment(Vector2 p, float ax, float ay, float bx, float by, float width)
        {
            var a = new Vector2(ax, ay);
            var d = new Vector2(bx, by) - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, d) / d.sqrMagnitude);
            return (p - a - d * t).magnitude - width;
        }

        static float Segment(Vector2 p, float ax, float ay, float bx, float by, float cx, float cy) =>
            Mathf.Min(Segment(p, ax, ay, bx, by, 2.7f), Segment(p, bx, by, cx, cy, 2.7f));

        public void Dispose()
        {
            foreach (var sprite in sprites) UnityEngine.Object.Destroy(sprite);
            UnityEngine.Object.Destroy(atlas);
        }
    }
}
