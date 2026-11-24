using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dustline
{
    internal static class QuarryLayout
    {
        internal const string Name = "石脊采石场";
        internal const string HighValueName = "筛分控制站";
        internal const float Size = 720f, HalfSize = Size * .5f, Boundary = 346f;
        internal static readonly Rect HighValueArea = Rect.MinMaxRect(174, 56, 252, 110);
        internal static readonly Rect CrusherFootprint = Rect.MinMaxRect(184, 25, 209, 43);
        internal static readonly Vector2 Pond = new Vector2(108, -273);
        internal static readonly Vector2 PondRadii = new Vector2(62, 44);

        internal static readonly HaulRoad[] Roads = {
            new HaulRoad(10, P(-304,-238,20), P(-211,-198,19)),
            new HaulRoad(11, P(-211,-198,19), P(-180,-108,21), P(-171,8,21), P(-158,101,22), P(-209,161,23), P(-261,223,24), P(-287,270,24)),
            new HaulRoad(11, P(-209,161,23), P(-50,222,25), P(74,224,26), P(164,212,24), P(228,154,23), P(229,48,20), P(228,-38,20), P(260,-119,20), P(296,-197,20), P(306,-254,20)),
            new HaulRoad(11, P(-211,-198,19), P(-110,-202,21), P(0,-193,22), P(101,-172,20), P(211,-154,20), P(260,-119,20)),
            new HaulRoad(10, P(-158,101,22), P(-76,130,11), P(18,115,-4), P(64,64,-18), P(54,4,-27), P(28,-15,-27), P(-44,-14,-26), P(-93,-29,-15), P(-137,-78,1), P(-180,-108,21)),
            new HaulRoad(10, P(229,48,20), P(162,61,17), P(115,99,2), P(64,64,-18))
        };

        internal static readonly Pad[] Pads = {
            new Pad(-324,-272,-245,-207,20), new Pad(-311,246,-257,290,24),
            new Pad(29,198,116,254,26), new Pad(174,16,258,111,20),
            new Pad(154,-182,214,-144,20), new Pad(276,-281,329,-231,20),
            new Pad(-36,-39,40,8,-27)
        };

        internal static readonly Shed[] Sheds = {
            new Shed("West dispatch shelter", -281,-247, 22,14, 5.8f, PortTone.Slate),
            new Shed("North ridge guardhouse", -299,255, 12,11, 4.2f, PortTone.Concrete),
            new Shed("Drill and service shed", 64,242, 38,18, 7.6f, PortTone.Slate),
            new Shed("Screening control house", 203,85, 38,19, 7.1f, PortTone.Slate),
            new Shed("Pump shed", 183,-173, 25,12, 5.4f, PortTone.Concrete),
            new Shed("South weigh station", 293,-263, 12,14, 4.9f, PortTone.Slate),
            new Shed("Cut face survey hut", -15,-28, 15,10, 4.3f, PortTone.Slate)
        };

        internal static Vector3 Spawn => QuarryTerrain.At(-304, -238) + Vector3.up * .12f;
        internal static Vector3 HighValueCenter => QuarryTerrain.At(203, 85);
        internal static IReadOnlyList<PortExit> Exits => Landmarks.Exits;
        internal static IReadOnlyList<PortDistrict> Districts => Landmarks.Districts;

        static class Landmarks
        {
            internal static readonly IReadOnlyList<PortExit> Exits = Array.AsReadOnly(new[] {
                new PortExit("北脊维修道", QuarryTerrain.At(-287, 270), 8f),
                new PortExit("南磅房卡口", QuarryTerrain.At(306, -254), 8f)
            });
            internal static readonly IReadOnlyList<PortDistrict> Districts = Array.AsReadOnly(new[] {
                new PortDistrict("西坡营地", QuarryTerrain.At(-266,-226), Rect.MinMaxRect(-332,-298,-211,-161)),
                new PortDistrict("北脊钻场", QuarryTerrain.At(70,216), Rect.MinMaxRect(-79,170,164,292)),
                new PortDistrict("阶梯采坑", QuarryTerrain.At(12,30), Rect.MinMaxRect(-131,-122,112,136)),
                new PortDistrict("碎石车间", QuarryTerrain.At(220,27), Rect.MinMaxRect(167,-47,277,141)),
                new PortDistrict("沉淀池泵房", QuarryTerrain.At(172,-203), Rect.MinMaxRect(40,-314,237,-131))
            });
        }

        internal static bool InHighValueArea(Vector3 p) => HighValueArea.Contains(new Vector2(p.x, p.z));

        internal static int DistrictIndex(Vector3 p)
        {
            var xz = new Vector2(p.x, p.z);
            for (int i = 0; i < Districts.Count; i++)
                if (Districts[i].Area.Contains(xz)) return i;
            return -1;
        }

        internal static bool CanPostBot(Vector3 p)
        {
            if (NavigationGrid.PlanarDistanceSquared(p, Spawn) < 78 * 78) return false;
            foreach (var exit in Exits)
                if (NavigationGrid.PlanarDistanceSquared(p, exit.Position) < 54 * 54) return false;
            return true;
        }

        static Vector3 P(float x, float z, float y) => new Vector3(x, y, z);

        internal sealed class HaulRoad
        {
            internal readonly float HalfWidth;
            internal readonly Vector3[] Points;
            internal HaulRoad(float width, params Vector3[] points) { HalfWidth = width; Points = points; }
        }

        internal readonly struct Pad
        {
            internal readonly Rect Area;
            internal readonly float Height;
            internal Pad(float x0, float z0, float x1, float z1, float height)
            { Area = Rect.MinMaxRect(x0, z0, x1, z1); Height = height; }

            internal float Distance(float x, float z)
            {
                float dx = Mathf.Max(0, Mathf.Max(Area.xMin - x, x - Area.xMax));
                float dz = Mathf.Max(0, Mathf.Max(Area.yMin - z, z - Area.yMax));
                return Mathf.Sqrt(dx * dx + dz * dz);
            }
        }

        internal readonly struct Shed
        {
            internal readonly string Name;
            internal readonly Rect Area;
            internal readonly float Height;
            internal readonly PortTone Wall;
            internal Shed(string name, float x, float z, float width, float depth, float height, PortTone wall)
            { Name = name; Area = new Rect(x - width * .5f, z - depth * .5f, width, depth); Height = height; Wall = wall; }
        }
    }

    internal static class QuarryTerrain
    {
        internal const int Step = 4, Cells = (int)QuarryLayout.Size / Step;
        internal const float WalkGradient = .60f, WaterY = 9f;
        const float HalfSize = QuarryLayout.HalfSize;

        internal readonly struct RoadSegment
        {
            internal readonly Vector3 A, B;
            internal readonly float HalfWidth;
            readonly float dx, dz, inverseLengthSq;
            internal RoadSegment(Vector3 a, Vector3 b, float width)
            {
                A = a; B = b; HalfWidth = width;
                dx = b.x - a.x; dz = b.z - a.z; inverseLengthSq = 1 / (dx * dx + dz * dz);
            }

            internal float DistanceSq(float x, float z, out float along)
            {
                float u = x - A.x, v = z - A.z;
                along = Mathf.Clamp01((u * dx + v * dz) * inverseLengthSq);
                u -= along * dx;
                v -= along * dz;
                return u * u + v * v;
            }
        }

        internal static readonly RoadSegment[] Segments = MakeSegments();
        static readonly float[,] heights = MakeHeights();

        static RoadSegment[] MakeSegments()
        {
            var segments = new List<RoadSegment>();
            foreach (var route in QuarryLayout.Roads)
                for (int i = 1; i < route.Points.Length; i++)
                    segments.Add(new RoadSegment(route.Points[i - 1], route.Points[i], route.HalfWidth));
            return segments.ToArray();
        }

        static float[,] MakeHeights()
        {
            var result = new float[Cells + 1, Cells + 1];
            for (int z = 0; z <= Cells; z++)
                for (int x = 0; x <= Cells; x++)
                    result[x, z] = Sculpt(x * Step - HalfSize, z * Step - HalfSize);
            return result;
        }

        static float Sculpt(float x, float z)
        {
            float u = x - 20, v = (z - 12) * 1.08f;
            float cut = Mathf.Sqrt(u * u + v * v) + 5.7f * Mathf.Sin(z * .033f) + 5.1f * Mathf.Sin(x * .024f + z * .036f);
            float rough = Mathf.PerlinNoise(x * .032f + 156, z * .028f + 92) - .5f;
            float y = -28f + 11f * Ease((cut - 49) / 10) + 13f * Ease((cut - 80) / 9)
                + 13f * Ease((cut - 109) / 10) + 13f * Ease((cut - 145) / 13);
            y += (1.9f + 4.8f * Ease((cut - 149) / 67)) * rough;
            y += 25f * Ridge(x, z, -295, 83, 91, 155) + 28f * Ridge(x, z, 109, 317, 118, 68)
                + 30f * Ridge(x, z, 304, 153, 75, 142) + 17f * Ridge(x, z, -167, -309, 142, 69);
            y += 15f * Ease((Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) - 306) / 54);

            float pondX = (x - QuarryLayout.Pond.x) / QuarryLayout.PondRadii.x;
            float pondZ = (z - QuarryLayout.Pond.y) / QuarryLayout.PondRadii.y;
            float pond = Mathf.Sqrt(pondX * pondX + pondZ * pondZ) + rough * .12f;
            y = Mathf.Lerp(4.8f + rough, y, Ease((pond - .65f) / .43f));

            float bestSq = float.PositiveInfinity, grade = 0, width = 0;
            foreach (var road in Segments)
            {
                float sq = road.DistanceSq(x, z, out float along);
                if (sq >= bestSq) continue;
                bestSq = sq;
                grade = Mathf.Lerp(road.A.y, road.B.y, along);
                width = road.HalfWidth;
            }
            y = Mathf.Lerp(y, grade, 1f - Ease((Mathf.Sqrt(bestSq) - width) / 20f));
            foreach (var pad in QuarryLayout.Pads) y = Mathf.Lerp(y, pad.Height, 1f - Ease(pad.Distance(x, z) / 18f));
            return y;
        }

        static float Ridge(float x, float z, float cx, float cz, float sx, float sz)
        {
            float u = (x - cx) / sx, v = (z - cz) / sz;
            return Mathf.Exp(-(u * u + v * v) * 1.55f);
        }

        static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        internal static float VertexHeight(int x, int z) => heights[x, z];
        internal static float Floor(float x, float z) => Sample(x, z, out _);
        internal static Vector3 At(float x, float z) => new Vector3(x, Floor(x, z), z);

        internal static float Sample(float x, float z, out Vector2 grade)
        {
            float u = Mathf.Clamp((x + HalfSize) / Step, 0, Cells), v = Mathf.Clamp((z + HalfSize) / Step, 0, Cells);
            int i = Mathf.Min((int)u, Cells - 1), j = Mathf.Min((int)v, Cells - 1);
            u -= i; v -= j;
            float a = heights[i, j], b = heights[i, j + 1], c = heights[i + 1, j + 1], d = heights[i + 1, j];
            float dx = v >= u ? c - b : d - a, dz = v >= u ? b - a : c - d;
            grade = new Vector2(dx / Step, dz / Step);
            return a + u * dx + v * dz;
        }

        internal static bool IsPond(float x, float z, float y) => y < WaterY
            && Mathf.Abs(x - QuarryLayout.Pond.x) < QuarryLayout.PondRadii.x
            && Mathf.Abs(z - QuarryLayout.Pond.y) < QuarryLayout.PondRadii.y;

        internal static bool TryWalk(float x, float z, out float y)
        {
            y = Sample(x, z, out var grade);
            return Mathf.Abs(x) < QuarryLayout.Boundary - .7f && Mathf.Abs(z) < QuarryLayout.Boundary - .7f
                && grade.sqrMagnitude <= WalkGradient * WalkGradient && !IsPond(x, z, y - .36f);
        }
    }
}
