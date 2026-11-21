using System;
using UnityEngine;
using static Dustline.PortLayout;

namespace Dustline
{
    internal static class PortTerrain
    {
        internal const int Step = 3;
        internal const int Cells = (int)Size / Step;
        internal const float SeaY = -1.4f;
        internal const float GateY = 0, YardY = 2.3f, CustomsY = 8.2f, WorkshopY = 4.3f, RailY = 5.8f, QuayY = 2.1f;
        internal const float RiverRail = 15.5f;
        internal const float WalkGradient = 0.64f;

        internal readonly struct Bridge
        {
            internal readonly float X, Z, HalfLength, DeckY;
            internal const float HalfWidth = 7.5f;
            internal Bridge(float x, float y, float halfLength)
            { X = x; Z = RiverZ(x); DeckY = y; HalfLength = halfLength; }

            internal bool Covers(float x, float z, float margin = 0) =>
                Mathf.Abs(x - X) <= HalfWidth - margin && Mathf.Abs(z - Z) <= HalfLength;
        }

        internal readonly struct Road
        {
            internal readonly Vector2 A, B;
            internal readonly float HalfWidth, Length;
            internal readonly Vector2 Along;
            readonly Vector2 delta;
            readonly float inverseLengthSqr;
            internal Road(float x0, float z0, float x1, float z1, float halfWidth)
            {
                A = new Vector2(x0, z0); B = new Vector2(x1, z1);
                HalfWidth = halfWidth; delta = B - A;
                Length = delta.magnitude; Along = delta / Length; inverseLengthSqr = 1 / delta.sqrMagnitude;
            }
            internal float Distance(Vector2 point)
            {
                Vector2 from = point - A;
                return (from - delta * Mathf.Clamp01(Vector2.Dot(from, delta) * inverseLengthSqr)).magnitude;
            }
        }

        readonly struct Lot
        {
            internal readonly Rect Area;
            internal readonly float Grade, Shoulder;
            internal readonly SurfaceKind Surface;
            internal Lot(float x0, float z0, float x1, float z1, float grade, float shoulder, SurfaceKind surface)
            { Area = Rect.MinMaxRect(x0, z0, x1, z1); Grade = grade; Shoulder = shoulder; Surface = surface; }

            internal float Outside(float x, float z)
            {
                float dx = Mathf.Max(0, Mathf.Max(Area.xMin - x, x - Area.xMax));
                float dz = Mathf.Max(0, Mathf.Max(Area.yMin - z, z - Area.yMax));
                return Mathf.Sqrt(dx * dx + dz * dz);
            }
        }

        static readonly Vector2[] channel = {
            new Vector2(-480, -61), new Vector2(-404, -48), new Vector2(-334, -44),
            new Vector2(-235, -38), new Vector2(-137, -35), new Vector2(-40, -17),
            new Vector2(50, 6), new Vector2(117, 6), new Vector2(175, -15),
            new Vector2(221, -45), new Vector2(265, -74), new Vector2(318, -77), new Vector2(356, -75)
        };

        internal static readonly Bridge[] Bridges = { new Bridge(-334, 2.55f, 23), new Bridge(-40, 5.55f, 24), new Bridge(221, 3.75f, 26) };
        internal static readonly Road[] Roads = {
            new Road(-334, -426, -334, 424, 7.5f), new Road(-40, -409, -40, 398, 7.5f),
            new Road(221, -317, 221, 350, 7.5f), new Road(-431, -306, 341, -306, 7.5f),
            new Road(-427, 181, 341, 181, 7.5f), new Road(-413, 350, 341, 350, 7.5f),
            new Road(-417, -11, -254, -11, 6.0f), new Road(-254, -11, -104, 10, 6.0f),
            new Road(-104, 10, -57, 46, 6.0f),
            new Road(-57, 46, 341, 46, 6.5f)
        };

        static readonly Lot[] lots = {
            new Lot(-399, -362, -339, -309, GateY, 26, SurfaceKind.Concrete),
            new Lot(-317, -270, -55, -58, YardY, 31, SurfaceKind.Concrete),
            new Lot(-313, -2, -70, 173, CustomsY, 25, SurfaceKind.Concrete),
            new Lot(-4, -270, 206, -57, WorkshopY, 27, SurfaceKind.Concrete),
            new Lot(-349, 203, 181, 344, RailY, 24, SurfaceKind.Gravel),
            new Lot(230, -42, 355, 222, QuayY, 24, SurfaceKind.Concrete)
        };

        static readonly Rect[] roofs = {
            Rect.MinMaxRect(-356.5f, -342, -343.5f, -332), Rect.MinMaxRect(-299, -240, -285, -230),
            Rect.MinMaxRect(-234, 47, -162, 85), Rect.MinMaxRect(-290, 85.5f, -264, 104.5f),
            Rect.MinMaxRect(-120, 117, -96, 133), Rect.MinMaxRect(35, -183, 131, -131),
            Rect.MinMaxRect(143, -226, 167, -206), Rect.MinMaxRect(-212, 304, -178, 322),
            Rect.MinMaxRect(-125.5f, 264.5f, 17.5f, 281.5f), Rect.MinMaxRect(239.5f, 105, 270.5f, 129)
        };

        static readonly float[,] heights = MakeHeights();

        static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3 - 2 * t); }

        internal static float RiverZ(float x)
        {
            for (int i = 1; i < channel.Length; i++)
            {
                if (x > channel[i].x) continue;
                Vector2 a = channel[i - 1], b = channel[i];
                float u = Mathf.Clamp01((x - a.x) / (b.x - a.x));
                return Mathf.Lerp(a.y, b.y, u) + Mathf.Sin(u * Mathf.PI * 2) * 1.2f;
            }
            return channel[channel.Length - 1].y;
        }

        static float BankDistance(float x, float z) => Mathf.Abs(z - RiverZ(x));

        internal static float RoadClearance(Vector2 point)
        {
            float d = float.PositiveInfinity;
            foreach (var road in Roads) d = Mathf.Min(d, road.Distance(point) - road.HalfWidth);
            return d;
        }

        static float[,] MakeHeights()
        {
            var sample = new float[Cells + 1, Cells + 1];
            for (int z = 0; z <= Cells; z++)
                for (int x = 0; x <= Cells; x++) sample[x, z] = Sculpt(-HalfSize + x * Step, -HalfSize + z * Step);
            return sample;
        }

        static float Hill(float x, float z, float cx, float cz, float sx, float sz)
        {
            float dx = (x - cx) / sx, dz = (z - cz) / sz;
            return Mathf.Exp(-(dx * dx + dz * dz) * 1.65f);
        }

        static float Sculpt(float x, float z)
        {
            float swells = 4.3f + 2.3f * Mathf.Sin((z + x * .37f) * .017f) + 1.55f * Mathf.Cos(x * .026f - z * .009f);
            float y = swells + 6.2f * Mathf.PerlinNoise(x * .014f + 144.7f, z * .014f + 75.9f) - 2.8f;
            y += 48 * Hill(x, z, -443, 64, 106, 203) + 29 * Hill(x, z, -427, -223, 87, 118)
                + 32 * Hill(x, z, -263, -419, 128, 76) + 41 * Hill(x, z, 37, 428, 174, 95)
                + 27 * Hill(x, z, -271, 435, 136, 81) + 16 * Hill(x, z, 154, -361, 125, 94)
                + 16 * Hill(x, z, 120, 120, 120, 86) + 8 * Hill(x, z, -101, -395, 67, 86);
            float rim = Mathf.Max(Ease((-420 - x) / 56), Ease((Mathf.Abs(z) - 425) / 54));
            y += rim * (14 + 11 * Mathf.PerlinNoise(x * .031f + 92, z * .027f + 93));

            // The same gentle grade field feeds every road at a junction.
            float grade = 4.8f + 2.1f * Mathf.Sin((z + 115) * .008f) + 1.2f * Mathf.Cos((x + 135) * .009f);
            float road = 1 - Ease((RoadClearance(new Vector2(x, z)) - 2.5f) / 27);
            y = Mathf.Lerp(y, grade, road);
            foreach (var lot in lots) y = Mathf.Lerp(y, lot.Grade, 1 - Ease((lot.Outside(x, z) - 3f) / lot.Shoulder));

            if (x > 345)
            {
                y = Mathf.Lerp(y, QuayY, Ease((x - 345) / 9));
                y = Mathf.Lerp(y, -5.6f, Ease((x - 354) / 3));
            }

            float bank = BankDistance(x, z);
            if (x < Shore + 4)
                y = Mathf.Lerp(-5.8f + .35f * Mathf.Sin(x * .053f), y, Ease((bank - 6.8f) / 14.2f));

            foreach (var bridge in Bridges)
            {
                float lateral = 1 - Ease((Mathf.Abs(x - bridge.X) - 8.5f) / 12);
                float end = Mathf.Abs(z - bridge.Z) - bridge.HalfLength;
                if (lateral <= 0 || end < -9 || end > 30) continue;
                float approach = end <= 0 ? Ease((end + 9) / 4) : 1 - Ease((end - 2) / 28);
                y = Mathf.Lerp(y, bridge.DeckY - .08f, approach * lateral);
            }
            return y;
        }

        internal static float VertexHeight(int x, int z) => heights[x, z];

        internal static float Ground(float x, float z) => Sample(x, z, out _);

        static float Sample(float x, float z, out float gradient)
        {
            float u = Mathf.Clamp((x + HalfSize) / Step, 0, Cells);
            float v = Mathf.Clamp((z + HalfSize) / Step, 0, Cells);
            int i = Mathf.Min((int)u, Cells - 1), j = Mathf.Min((int)v, Cells - 1);
            u -= i; v -= j;
            float a = heights[i, j], b = heights[i, j + 1], c = heights[i + 1, j + 1], d = heights[i + 1, j];
            float dx = v >= u ? c - b : d - a, dz = v >= u ? b - a : c - d;
            gradient = (dx * dx + dz * dz) / (Step * Step);
            return a + u * dx + v * dz;
        }

        internal static float Floor(float x, float z)
        {
            foreach (var bridge in Bridges)
                if (bridge.Covers(x, z)) return bridge.DeckY;
            return Ground(x, z);
        }

        internal static bool TryWalk(float x, float z, out float y)
        {
            y = 0;
            if (x <= -469 || x >= 354 || Mathf.Abs(z) >= 469) return false;
            foreach (var bridge in Bridges)
                if (bridge.Covers(x, z, .5f)) { y = bridge.DeckY; return true; }
            if (BankDistance(x, z) < RiverRail + .45f) return false;
            y = Sample(x, z, out float gradient);
            return y > SeaY + .45f && gradient <= WalkGradient * WalkGradient;
        }

        internal static bool IsWater(float x, float z) => (x >= 354 || BankDistance(x, z) < 19) && Ground(x, z) < SeaY;

        internal static bool IsNatural(float x, float z)
        {
            if (x >= 344 || RoadClearance(new Vector2(x, z)) < 4 || BankDistance(x, z) < RiverRail + 2) return false;
            foreach (var lot in lots)
                if (lot.Outside(x, z) < 2) return false;
            return true;
        }

        internal static SurfaceKind Surface(float x, float z)
        {
            foreach (var bridge in Bridges)
                if (bridge.Covers(x, z)) return SurfaceKind.Metal;
            Vector2 p = new Vector2(x, z);
            float road = RoadClearance(p);
            if (road <= 0) return SurfaceKind.Asphalt;
            if (x > -126 && x < 18 && z > 264 && z < 282 || x > 326 && x < 348 && z > -37 && z < 203)
                return SurfaceKind.Metal;
            if (x > -344 && x < 174)
            {
                float track = TrackDistance(z);
                if (track < 1.65f) return Mathf.Abs(track - .86f) < .18f ? SurfaceKind.Metal : SurfaceKind.Wood;
            }
            foreach (var lot in lots)
                if (lot.Area.Contains(p)) return lot.Surface;
            if (BankDistance(x, z) < 18) return SurfaceKind.Dirt;
            if (road < 1.7f || x > 342) return SurfaceKind.Gravel;
            float patch = Mathf.PerlinNoise(x * .038f + 163.8f, z * .038f + 13.2f);
            return patch >= .48f ? SurfaceKind.Grass : SurfaceKind.Dirt;
        }

        static float TrackDistance(float z) => Mathf.Min(Mathf.Abs(z - 233), Mathf.Min(Mathf.Abs(z - 253), Mathf.Abs(z - 294)));

        internal static Color32 ColorAt(Vector2 p, bool map) => ColorAt(p, map, out _);

        static Color32 ColorAt(Vector2 p, bool map, out SurfaceKind surface)
        {
            float x = p.x, z = p.y;
            surface = SurfaceKind.Dirt;
            bool bridge = false;
            foreach (var span in Bridges)
                if (span.Covers(x, z)) { bridge = true; break; }
            if (!bridge && IsWater(x, z)) return new Color32(48, 77, 92, 255);
            if (map)
                foreach (var roof in roofs)
                    if (roof.Contains(p))
                    {
                        float edge = Mathf.Min(Mathf.Min(x - roof.xMin, roof.xMax - x), Mathf.Min(z - roof.yMin, roof.yMax - z));
                        return edge < 0.9f ? new Color32(79, 105, 113, 255) : new Color32(140, 157, 159, 255);
                    }

            float noise = Mathf.PerlinNoise(x * .27f + 101.1f, z * .27f + 44.2f) - .5f;
            float patch = Mathf.PerlinNoise(x * .038f + 163.8f, z * .038f + 13.2f);
            Color c;
            surface = Surface(x, z);
            switch (surface)
            {
                case SurfaceKind.Asphalt: c = map ? new Color(.24f, .29f, .31f) : new Color(.295f, .315f, .318f); break;
                case SurfaceKind.Concrete: c = new Color(.535f, .55f, .526f); break;
                case SurfaceKind.Gravel: c = new Color(.436f, .428f, .383f); break;
                case SurfaceKind.Metal: c = new Color(.41f, .45f, .46f); break;
                case SurfaceKind.Wood: c = new Color(.39f, .33f, .26f); break;
                default:
                    float green = Ease((patch - .35f) / .27f) * Ease((BankDistance(x, z) - 14) / 9);
                    c = Color.Lerp(new Color(.43f, .366f, .286f), new Color(.29f, .39f, .27f), green);
                    float rock = Ease((Ground(x, z) - 33f) / 33) * Ease((noise + .31f) / .34f);
                    c = Color.Lerp(c, new Color(.45f, .455f, .426f), rock);
                    break;
            }
            float shade = noise * (map ? .035f : .115f) + (patch - .5f) * .105f;
            if (map)
            {
                float floor = Floor(x, z);
                shade += Mathf.Clamp(floor * .0015f, -.012f, .12f);
                if (surface == SurfaceKind.Grass || surface == SurfaceKind.Dirt)
                    shade -= Mathf.Clamp01(1f - Mathf.Abs(Mathf.Repeat(floor + 6f, 12f) - 6f) * 1.9f) * .045f;
            }
            return new Color(c.r + shade, c.g + shade, c.b + shade, 1);
        }

        internal static Color32 PaintedColor(Vector2 p, float texel, out SurfaceKind surface)
        {
            Color32 baseColor = ColorAt(p, false, out surface);
            for (int i = 0; i < Exits.Count; i++)
            {
                var exit = Exits[i];
                float x = p.x - exit.Position.x, z = p.y - exit.Position.z;
                if (Mathf.Abs(x) > exit.Radius + texel || Mathf.Abs(z) > exit.Radius + texel) continue;
                float radial = Mathf.Sqrt(x * x + z * z);
                float pad = Mathf.Clamp01(.5f + (exit.Radius - radial) / texel);
                float ring = Paint(radial - exit.Radius + .53f, .58f, texel);
                Color32 padColor = Color32.Lerp(baseColor, new Color32(38, 66, 74, 255), pad);
                return Color32.Lerp(padColor, new Color32(93, 190, 146, 255), ring);
            }
            if (p.x > -344 && p.x < 174 && TrackDistance(p.y) < 2.5f)
            {
                float track = TrackDistance(p.y);
                var timber = new Color32(110, 87, 65, 255);
                return Color32.Lerp(baseColor, timber, Paint(track, 1.48f, texel) * .73f);
            }
            float strongest = 0;
            bool edge = false;
            int nearby = 0;
            foreach (var road in Roads)
            {
                Vector2 along = road.Along, from = p - road.A;
                float t = Vector2.Dot(along, from);
                if (t < 3 || t > road.Length - 3) continue;
                float offset = Mathf.Abs(along.x * from.y - along.y * from.x);
                if (offset > road.HalfWidth + 1.5f) continue;
                if (++nearby > 1) return baseColor;
                float shoulder = Paint(offset - (road.HalfWidth - 1.1f), .29f, texel);
                float dash = Paint(offset, .28f, texel) * Paint(Mathf.Repeat(t, 15) - 3.5f, 2.8f, texel);
                strongest = Mathf.Max(shoulder * .73f, dash);
                edge = shoulder * .73f > dash;
            }
            var paint = edge ? new Color32(192, 193, 175, 255) : new Color32(205, 173, 89, 255);
            return Color32.Lerp(baseColor, paint, strongest);
        }

        static float Paint(float distance, float halfWidth, float texel)
        {
            float halfPixel = texel * .68f;
            float a = Mathf.Min(distance + halfPixel, halfWidth);
            float b = Mathf.Max(distance - halfPixel, -halfWidth);
            return Mathf.Clamp01((a - b) / (2 * halfPixel));
        }
    }
}
