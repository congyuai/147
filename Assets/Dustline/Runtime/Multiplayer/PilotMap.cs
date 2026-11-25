using System;
using System.Numerics;

namespace Dustline
{
    internal readonly struct PilotBox
    {
        public readonly Vector3 Center, Size;
        public PilotBox(float x, float y, float z, float w, float h, float d)
        {
            Center = new Vector3(x, y, z);
            Size = new Vector3(w, h, d);
        }

        public bool Intersect(Vector3 origin, Vector3 ray, float limit, out float distance)
        {
            var min = Center - Size * .5f;
            var max = Center + Size * .5f;
            distance = 0;
            return Clip(origin.X, ray.X, min.X, max.X, ref distance, ref limit)
                && Clip(origin.Y, ray.Y, min.Y, max.Y, ref distance, ref limit)
                && Clip(origin.Z, ray.Z, min.Z, max.Z, ref distance, ref limit);
        }

        static bool Clip(float origin, float ray, float min, float max, ref float near, ref float far)
        {
            if (Math.Abs(ray) < .00001f) return origin >= min && origin <= max;
            float a = (min - origin) / ray, b = (max - origin) / ray;
            near = Math.Max(near, Math.Min(a, b));
            far = Math.Min(far, Math.Max(a, b));
            return near <= far;
        }
    }

    internal static class PilotMap
    {
        public const float HalfWidth = 39, HalfDepth = 43, PlayerRadius = .34f;
        public const float ExitRadius = 4f;
        public static readonly Vector3 Exit = new Vector3(0, 0, 35);
        public static readonly Vector3[] Spawns = { new Vector3(-3, 0, -33), new Vector3(3, 0, -33) };
        public static readonly PilotBox[] Walls =
        {
            new PilotBox(-39.5f, 1.6f, 0, 1, 3.2f, 88), new PilotBox(39.5f, 1.6f, 0, 1, 3.2f, 88),
            new PilotBox(0, 1.6f, -43.5f, 78, 3.2f, 1), new PilotBox(0, 1.6f, 43.5f, 78, 3.2f, 1),
            new PilotBox(-17, 1.8f, -15, 15, 3.6f, 2), new PilotBox(-23.5f, 1.8f, -8.5f, 2, 3.6f, 11),
            new PilotBox(-17, 1.8f, -3, 15, 3.6f, 2), new PilotBox(-17, 3.8f, -9, 15, .4f, 13),
            new PilotBox(15, 1.4f, -16, 9, 2.8f, 4), new PilotBox(15, 1.4f, -2, 9, 2.8f, 4),
            new PilotBox(-9, .65f, 10, 7, 1.3f, 3), new PilotBox(9, .68f, 10, 7, 1.36f, 3),
            new PilotBox(-16, 1.4f, 23, 5, 2.8f, 9), new PilotBox(16, 1.6f, 23, 6, 3.2f, 9),
            new PilotBox(-32, 2.2f, 16, 7, 4.4f, 15), new PilotBox(32, 2.4f, 17, 7, 4.8f, 17),
            new PilotBox(-32, 1.8f, -22, 6, 3.6f, 11), new PilotBox(32, 1.8f, -21, 6, 3.6f, 11)
        };
        public static readonly PilotBox[] Crates =
        {
            new PilotBox(-5, .45f, -20, 1.5f, .9f, 1.1f), new PilotBox(5, .45f, -15, 1.5f, .9f, 1.1f),
            new PilotBox(-8, .45f, 16, 1.5f, .9f, 1.1f), new PilotBox(6, .45f, 26, 1.5f, .9f, 1.1f)
        };
        public static readonly Vector3[,] Patrol =
        {
            { new Vector3(-18,0,6), new Vector3(-18,0,15), new Vector3(-5,0,15), new Vector3(-5,0,6) },
            { new Vector3(18,0,5), new Vector3(18,0,14), new Vector3(5,0,14), new Vector3(5,0,5) }
        };

        public static Vector3 Move(Vector3 position, Vector3 delta, float radius = PlayerRadius)
        {
            position.X = Slide(position, delta.X, true, radius);
            position.Z = Slide(position, delta.Z, false, radius);
            return position;
        }

        static float Slide(Vector3 p, float delta, bool x, float r)
        {
            float origin = x ? p.X : p.Z, cross = x ? p.Z : p.X;
            float end = origin + delta;
            foreach (var box in Walls) Clamp(box);
            foreach (var box in Crates) Clamp(box);
            float limit = (x ? HalfWidth : HalfDepth) - r;
            return Math.Clamp(end, -limit, limit);

            void Clamp(PilotBox box)
            {
                if (box.Center.Y - box.Size.Y * .5f > 1.85f) return;
                float center = x ? box.Center.Z : box.Center.X;
                float width = (x ? box.Size.Z : box.Size.X) * .5f + r;
                if (cross <= center - width || cross >= center + width) return;
                center = x ? box.Center.X : box.Center.Z;
                width = (x ? box.Size.X : box.Size.Z) * .5f + r;
                if (delta > 0 && origin <= center - width) end = Math.Min(end, center - width);
                else if (delta < 0 && origin >= center + width) end = Math.Max(end, center + width);
            }
        }

        public static float Obstacle(Vector3 origin, Vector3 ray, float limit, bool crates = true)
        {
            foreach (var wall in Walls)
                if (wall.Intersect(origin, ray, limit, out float hit)) limit = hit;
            if (crates)
                foreach (var box in Crates)
                    if (box.Intersect(origin, ray, limit, out float hit)) limit = hit;
            return limit;
        }

        public static int SearchTarget(Vector3 origin, Vector3 ray)
        {
            float limit = Obstacle(origin, ray, 3.2f, crates: false);
            int target = -1;
            for (int i = 0; i < Crates.Length; i++)
                if (Crates[i].Intersect(origin, ray, limit, out float hit)) { target = i; limit = hit; }
            return target;
        }

        public static Vector3 Facing(float yaw, float pitch = 0)
        {
            const float rad = (float)(Math.PI / 180);
            yaw *= rad; pitch *= rad;
            float tilt = (float)Math.Cos(pitch);
            return new Vector3((float)Math.Sin(yaw) * tilt, -(float)Math.Sin(pitch), (float)Math.Cos(yaw) * tilt);
        }

        public static float Yaw(Vector3 delta) => (float)(Math.Atan2(delta.X, delta.Z) * 180 / Math.PI);
        public static Vector3 Plane(Vector3 p) => new Vector3(p.X, 0, p.Z);
        public static Vector3 Unit(Vector3 p) => p.LengthSquared() > .000001f ? Vector3.Normalize(p) : Vector3.Zero;
    }
}
