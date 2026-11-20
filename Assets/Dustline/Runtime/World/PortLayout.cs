using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dustline
{
    public readonly struct PortDistrict
    {
        public string Name { get; }
        public Vector3 Center { get; }
        internal Rect Area { get; }

        internal PortDistrict(string name, Vector3 center, Rect area)
        {
            Name = name;
            Center = center;
            Area = area;
        }
    }

    public readonly struct PortExit
    {
        public string Name { get; }
        public Vector3 Position { get; }
        public float Radius { get; }

        internal PortExit(string name, Vector3 position, float radius)
        {
            Name = name;
            Position = position;
            Radius = radius;
        }
    }

    public static class PortLayout
    {
        public const float Size = 960f;
        internal const float HalfSize = Size * 0.5f;
        internal const float Shore = 356f;
        internal const float ExitKeepout = 82f;
        internal const float SpawnKeepout = 96f;
        public static readonly Vector3 Spawn = new Vector3(-366, 0.12f, -326);

        public static readonly IReadOnlyList<PortDistrict> Districts = Array.AsReadOnly(new[] {
            new PortDistrict("集装箱堆场", At(-183, -154), Rect.MinMaxRect(-318, -271, -55, -42)),
            new PortDistrict("海关办公区", At(-191, 67), Rect.MinMaxRect(-315, -13, -64, 180)),
            new PortDistrict("港机维修仓", At(93, -150), Rect.MinMaxRect(-10, -271, 208, -49)),
            new PortDistrict("铁路货台", At(-61, 258), Rect.MinMaxRect(-347, 181, 181, 354)),
            new PortDistrict("北岬码头", At(267, 63), Rect.MinMaxRect(185, -48, Shore, 220))
        });

        public static readonly IReadOnlyList<PortExit> Exits = Array.AsReadOnly(new[] {
            new PortExit("铁路西闸", At(-322, 315), 7.5f),
            new PortExit("东泊位接驳区", At(330, 165), 8f)
        });

        static readonly int[][] adjacent = {
            new[] { 1, 2 }, new[] { 0, 3, 4 }, new[] { 0, 4 },
            new[] { 1, 4 }, new[] { 1, 2, 3 }
        };

        internal static readonly Vector3[] BotPosts = {
            At(-246, -221), At(-179, -259), At(-121, -146),
            At(-270, 39), At(-179, 78), At(-84, 121),
            At(39, -210), At(104, -111), At(192, -174),
            At(-205, 207), At(-109, 279), At(92, 284),
            At(251, -33), At(285, 63)
        };

        internal static Vector3 At(float x, float z) => new Vector3(x, 0, z);

        internal static int DistrictIndex(Vector3 position)
        {
            var xz = new Vector2(position.x, position.z);
            for (int i = 0; i < Districts.Count; i++)
                if (Districts[i].Area.Contains(xz)) return i;
            return -1;
        }

        internal static bool Neighboring(int a, int b) => a < 0 || b < 0 || a == b || Array.IndexOf(adjacent[a], b) >= 0;

        internal static float DistanceSquared(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x, z = a.z - b.z;
            return x * x + z * z;
        }

        internal static bool CanPostBot(Vector3 point)
        {
            if (DistanceSquared(point, Spawn) < SpawnKeepout * SpawnKeepout) return false;
            foreach (var exit in Exits)
                if (DistanceSquared(point, exit.Position) < ExitKeepout * ExitKeepout) return false;
            return true;
        }
    }
}
