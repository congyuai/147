using System;
using UnityEngine;

namespace Dustline
{
    enum RaidIcon { Dial, Knob, Aim, Fire, Reload, Crouch, Search, Heal, Bag, Gear, Reticle, Hit, Strike, Prone, Jump, LeanLeft, LeanRight,
        InteractDoor, Map, Heading, Danger, Exit, Switch, ControlRing, DamageArc, Health, Sort, Secure, Sun, Sprint, Quiet }

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
                case RaidIcon.Sort:
                    return Cover(Mathf.Min(Segment(p, -18, -15, -18, 17, 2), Mathf.Min(
                        Segment(p, -25, -8, -18, -16, -11, -8), Mathf.Min(Box(p, 7, 13, 15, 2),
                        Mathf.Min(Box(p, 3, 2, 11, 2), Box(p, -1, -9, 7, 2))))));
                case RaidIcon.Secure:
                    return Cover(Mathf.Min(Outline(p, 0, -5, 24, 16, 2.3f), Mathf.Min(
                        Outline(p, 0, 13, 9, 5, 2), Mathf.Min(Box(p, -13, -3, 2, 6), Box(p, 13, -3, 2, 6)))));
                case RaidIcon.Sun:
                    float light = Circle(p, 0, 0, 13);
                    for (int ray = 0; ray < 8; ray++)
                    {
                        float a = ray * Mathf.PI / 4;
                        float x = Mathf.Cos(a), y = Mathf.Sin(a);
                        light = Mathf.Min(light, Segment(p, x * 17, y * 17, x * 24, y * 24, 1.7f));
                    }
                    return Cover(light);
                case RaidIcon.Health:
                    const float limb = 3f;
                    p.x = Mathf.Abs(p.x);
                    return Cover(Mathf.Min(Circle(p, 0, 20, 5.2f), Mathf.Min(Box(p, 0, 1, 6, 11),
                        Mathf.Min(Segment(p, 4, 8, 17, -5, limb), Segment(p, 3, -6, 10, -23, limb)))));
                case RaidIcon.Dial:
                    return Mathf.Max(Cover(Ring(p, 0, 0, 28, 1.3f)), Cover(p.magnitude - 27) * 0.76f);
                case RaidIcon.Knob:
                    return Mathf.Max(Cover(Ring(p, 0, 0, 26.5f, 1.4f)), Cover(p.magnitude - 26) * 0.57f);
                case RaidIcon.Aim:
                    return Cover(Mathf.Min(Ring(p, 0, 0, 16.5f, 1.9f), Cross(p, 9, 24, 1.9f)));
                case RaidIcon.Fire:
                    return Cover(Mathf.Min(Box(p, -1, 4, 16, 5), Mathf.Min(Box(p, 16, 5, 8, 2.4f),
                        Segment(p, -8, 2, -14, -15, 4.5f))));
                case RaidIcon.Strike:
                    return Cover(Mathf.Min(Segment(p, -16, -17, 15, 15, 4.0f), Mathf.Min(
                        Segment(p, 7, 20, 15, 20, 21, 14), Segment(p, 16, 0, 23, 7, 24, 13))));
                case RaidIcon.Prone:
                    return Cover(Mathf.Min(Circle(p, 16, 7, 4.6f), Mathf.Min(Segment(p, -19, -6, 8, 1, 4.5f),
                        Mathf.Min(Segment(p, 6, 2, 21, -6, 2.7f), Segment(p, -22, -14, 24, -14, 1.3f)))));
                case RaidIcon.Jump:
                    return Cover(Mathf.Min(Segment(p, 0, -11, 0, 20, 3.3f), Mathf.Min(
                        Segment(p, -13, 7, 0, 21, 13, 7), Segment(p, -20, -19, 20, -19, 2))));
                case RaidIcon.LeanLeft:
                case RaidIcon.LeanRight:
                    if (icon == RaidIcon.LeanRight) p.x = -p.x;
                    return Cover(Mathf.Min(Circle(p, 7, 15, 4.2f), Mathf.Min(Segment(p, 17, -19, 8, 7, 3.4f),
                        Segment(p, -6, 7, -19, -2, -6, -11))));
                case RaidIcon.InteractDoor:
                    return Cover(Mathf.Min(Outline(p, 0, 0, 16, 22, 2), Mathf.Min(Ring(p, 7, 0, 2.7f, 1.4f), Segment(p, -25, -23, 24, -23, 1.7f))));
                case RaidIcon.Map:
                    return Cover(Mathf.Min(Outline(p, 0, 0, 22, 17, 1.9f), Mathf.Min(
                        Segment(p, -7, -15, -7, 15, 1.8f), Segment(p, 8, -15, 8, 15, 1.8f))));
                case RaidIcon.Heading:
                    float headingWidth = Mathf.Max(0f, (20f - p.y) * 0.41f);
                    return Cover(Mathf.Max(Mathf.Max(Mathf.Abs(p.x) - headingWidth, p.y - 20f), -11f - p.y + Mathf.Max(0f, 7f - Mathf.Abs(p.x))));
                case RaidIcon.Danger:
                    return Cover(Mathf.Min(Mathf.Abs(Mathf.Abs(p.x) + Mathf.Abs(p.y) - 22f) - 2.7f,
                        Mathf.Min(Segment(p, 0, -1, 0, 10, 2.3f), Circle(p, 0, -8, 2.4f))));
                case RaidIcon.Exit:
                    return Cover(Mathf.Min(Outline(p, 1, 0, 22, 20, 2.2f), Mathf.Min(Segment(p, -10, 0, 13, 0, 2.8f),
                        Segment(p, 3, 10, 13, 0, 3, -10))));
                case RaidIcon.Switch:
                    return Cover(Mathf.Min(Mathf.Min(Segment(p, -20, 8, 19, 8, 2.8f), Segment(p, 10, 17, 19, 8, 10, -1)),
                        Mathf.Min(Segment(p, -19, -11, 20, -11, 2.8f), Segment(p, -10, -2, -19, -11, -10, -20))));
                case RaidIcon.ControlRing:
                    return Cover(Ring(p, 0, 0, 29, 1.45f));
                case RaidIcon.DamageArc:
                    float arcY = 16f - p.x * p.x * 0.016f;
                    return Cover(Mathf.Max(Mathf.Abs(p.y - arcY) - 3.25f, Mathf.Abs(p.x) - 26f));
                case RaidIcon.Reload:
                    float arc = Ring(p, 0, 0, 19, 2.1f);
                    if (p.x > 5 && p.y > 3) arc = 50;
                    return Cover(Mathf.Min(arc, Mathf.Min(
                        Segment(p, -6, 12, 5, 18, -4, 25), Box(p, 0, -1, 4.5f, 9))));
                case RaidIcon.Crouch:
                    return Cover(Mathf.Min(Circle(p, 13, 13, 4.5f), Mathf.Min(Segment(p, 6, 8, -3, 1, -12, -7),
                        Mathf.Min(Segment(p, -12, -7, 3, -7, 11, -16), Segment(p, 6, 6, 13, -2, 21, -2)))));
                case RaidIcon.Sprint:
                    return Cover(Mathf.Min(Segment(p, -14, 2, 0, 16, 14, 2), Segment(p, -14, -12, 0, 2, 14, -12)));
                case RaidIcon.Quiet:
                    return Cover(Mathf.Min(Box(p, -14, 0, 5, 6), Mathf.Min(Segment(p, -9, 4, 1, 13, 1, -13),
                        Segment(p, 8, -9, 21, 9, 2.7f))));
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
