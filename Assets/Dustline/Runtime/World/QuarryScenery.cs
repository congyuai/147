using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static Dustline.QuarryLayout;

namespace Dustline
{
    internal sealed class QuarryScenery
    {
        const int TileCells = 45;
        readonly Transform root;
        readonly PortMaterials materials;
        readonly Action<UnityEngine.Object> keep;
        readonly List<Vector3> nodes;
        readonly HashSet<Collider> floors;

        internal QuarryScenery(Transform root, PortMaterials materials, Action<UnityEngine.Object> keep,
            List<Vector3> nodes, HashSet<Collider> floors)
        { this.root = root; this.materials = materials; this.keep = keep; this.nodes = nodes; this.floors = floors; }

        internal void Build()
        {
            BuildGround();
            BuildRim();
            BuildPond();
            foreach (var shed in Sheds) BuildShed(shed);
            BuildCrusher();
            BuildCover();
            foreach (var exit in Exits) BuildExtractionBeacon(exit);
            SeedRoutes();
        }

        void BuildGround()
        {
            var ground = PortMesh.Child(root, "Stone Ridge terrain").transform;
            for (int z = 0; z < QuarryTerrain.Cells; z += TileCells)
                for (int x = 0; x < QuarryTerrain.Cells; x += TileCells) BuildTile(ground, x, z);
        }

        void BuildTile(Transform ground, int x0, int z0)
        {
            const int width = TileCells + 1;
            const float s = QuarryTerrain.Step;
            var vertices = new Vector3[width * width];
            var normals = new Vector3[vertices.Length];
            var tangents = new Vector4[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var detail = new List<Vector4>(vertices.Length);
            var triangles = new int[TileCells * TileCells * 6];
            for (int dz = 0; dz <= TileCells; dz++)
                for (int dx = 0; dx <= TileCells; dx++)
                {
                    int x = x0 + dx, z = z0 + dz, i = dz * width + dx;
                    float wx = x * s - HalfSize, wz = z * s - HalfSize;
                    vertices[i] = new Vector3(wx, QuarryTerrain.VertexHeight(x, z), wz);
                    float west = QuarryTerrain.VertexHeight(Mathf.Max(0, x - 1), z);
                    float east = QuarryTerrain.VertexHeight(Mathf.Min(QuarryTerrain.Cells, x + 1), z);
                    float south = QuarryTerrain.VertexHeight(x, Mathf.Max(0, z - 1));
                    float north = QuarryTerrain.VertexHeight(x, Mathf.Min(QuarryTerrain.Cells, z + 1));
                    normals[i] = new Vector3(west - east, s * 2, south - north).normalized;
                    Vector3 tangent = new Vector3(s * 2, east - west, 0).normalized;
                    tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, -1f);
                    uv[i] = new Vector2(x * s / Size, z * s / Size);
                    detail.Add(new Vector4(wx, wz, -1, 1));
                    if (dx == TileCells || dz == TileCells) continue;
                    int t = (dz * TileCells + dx) * 6;
                    triangles[t] = i; triangles[t + 1] = i + width; triangles[t + 2] = i + width + 1;
                    triangles[t + 3] = i; triangles[t + 4] = i + width + 1; triangles[t + 5] = i + 1;
                }
            var tile = PortMesh.Child(ground, "Bench tile " + x0 + " " + z0);
            tile.isStatic = true;
            var mesh = new Mesh { name = tile.name, vertices = vertices, normals = normals, tangents = tangents, uv = uv, triangles = triangles };
            mesh.SetUVs(1, detail);
            keep(mesh);
            tile.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = tile.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = materials.Ground;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var floor = tile.AddComponent<MeshCollider>();
            floor.sharedMesh = mesh;
            floors.Add(floor);
            tile.AddComponent<FootstepSurface>().Kind = SurfaceKind.Gravel;
            mesh.UploadMeshData(true);
        }

        void BuildRim()
        {
            for (int side = 0; side < 4; side++)
            {
                var boundary = new PortChunk(root, "Quarry perimeter " + side, materials, keep);
                var rotation = Quaternion.Euler(0, side * 90, 0);
                for (int k = 0; k < 44; k++)
                {
                    float from = Mathf.Lerp(-Boundary - 1, Boundary + 1, k / 44f);
                    float to = Mathf.Lerp(-Boundary - 1, Boundary + 1, (k + 1) / 44f);
                    Vector3 a = rotation * new Vector3(from, 0, Boundary), b = rotation * new Vector3(to, 0, Boundary);
                    a.y = QuarryTerrain.Floor(a.x, a.z);
                    b.y = QuarryTerrain.Floor(b.x, b.z);
                    Vector3 size = side % 2 == 0 ? new Vector3(to - from + .1f, 1, 1.2f) : new Vector3(1.2f, 1, to - from + .1f);
                    float bottom = Mathf.Min(a.y, b.y) - 4f, top = Mathf.Max(a.y, b.y) + 3.4f;
                    size.y = top - bottom;
                    Vector3 center = (a + b) * .5f; center.y = (bottom + top) * .5f;
                    boundary.Block(center, size, PortTone.Rock);
                    var cap = size; cap.y = .25f;
                    center.y = top;
                    boundary.Structures.Box(center, cap, PortTone.Concrete);
                }
                boundary.Bake();
            }
        }

        void BuildPond()
        {
            var edge = new PortChunk(root, "Settling pond safety rim", materials, keep);
            const int sides = 36;
            for (int i = 0; i < sides; i++)
            {
                Vector3 a = Edge(i), b = Edge(i + 1);
                var mid = (a + b) * .5f;
                var axis = b - a;
                var heading = Quaternion.LookRotation(new Vector3(axis.x, 0, axis.z));
                float bottom = Mathf.Min(a.y, b.y) - .75f, top = Mathf.Max(a.y, b.y) + 1.55f;
                mid.y = (bottom + top) * .5f;
                var size = new Vector3(.58f, top - bottom, new Vector2(axis.x, axis.z).magnitude + .16f);
                Solid(edge, mid, size, heading, PortTone.Concrete);
                mid.y = top;
                edge.Structures.Box(mid, new Vector3(.69f, .19f, size.z), PortTone.Ochre, heading);
            }
            edge.Bake();

            var water = new PortMesh();
            water.Floor(Pond.x - PondRadii.x, Pond.y - PondRadii.y, Pond.x + PondRadii.x, Pond.y + PondRadii.y,
                QuarryTerrain.WaterY, PortTone.WaterDark);
            water.Bake(root, "Settling water", materials.Sea, keep, false);
            var overflow = new PortChunk(root, "Settling pond culvert", materials, keep, QuarryTerrain.Floor(164, -271));
            overflow.Block(new Vector3(167, .62f, -271), new Vector3(4.5f, 1.4f, 7.6f), PortTone.Concrete);
            overflow.Structures.Cylinder(new Vector3(164.75f, .67f, -272.8f), .68f, .26f, PortTone.Steel,
                Quaternion.Euler(0, 0, 90), 12);
            overflow.Structures.Cylinder(new Vector3(164.75f, .67f, -269.2f), .68f, .26f, PortTone.Steel,
                Quaternion.Euler(0, 0, 90), 12);
            overflow.Bake();

            Vector3 Edge(int i)
            {
                float a = (i % sides) * (2 * Mathf.PI / sides);
                return QuarryTerrain.At(Pond.x + Mathf.Cos(a) * (PondRadii.x + 4f), Pond.y + Mathf.Sin(a) * (PondRadii.y + 4f));
            }
        }

        static void Solid(PortChunk chunk, Vector3 center, Vector3 size, Quaternion rotation, PortTone tone)
        {
            chunk.Structures.Box(center, size, tone, rotation);
            var solid = PortMesh.Child(chunk.Root.transform, "Structure collision");
            solid.isStatic = true;
            solid.transform.localPosition = center;
            solid.transform.localRotation = rotation;
            solid.AddComponent<BoxCollider>().size = size;
        }

        void BuildShed(Shed shed)
        {
            Rect r = shed.Area;
            float x = r.center.x, z = r.center.y, h = shed.Height;
            var building = new PortChunk(root, shed.Name, materials, keep, QuarryTerrain.Floor(x, z));
            const float t = .34f, door = 3.35f, lintel = 3.3f;
            foreach (int side in new[] { -1, 1 })
            {
                float endX = x + r.width * .5f * side, endZ = z + r.height * .5f * side;
                building.Block(new Vector3(endX, h * .5f, z), new Vector3(t, h, r.height), shed.Wall);
                float flank = (r.width - door * 2) * .5f;
                for (int direction = -1; direction <= 1; direction += 2)
                    building.Block(new Vector3(x + direction * (door + flank * .5f), h * .5f, endZ), new Vector3(flank, h, t), shed.Wall);
                building.Block(new Vector3(x, (h + lintel) * .5f, endZ), new Vector3(door * 2, h - lintel, t), shed.Wall);
                building.Structures.Box(new Vector3(x, lintel + .4f, endZ + side * .3f), new Vector3(2.3f, .52f, .2f), PortTone.Ochre);
                for (int column = -1; column <= 1; column += 2)
                {
                    float pillar = x + column * (door + .08f);
                    building.Structures.Box(new Vector3(pillar, lintel * .5f, endZ + side * .16f), new Vector3(.30f, lintel, .38f), PortTone.Zinc);
                }
                nodes.Add(QuarryTerrain.At(x, endZ + side * 3.3f));
            }
            nodes.Add(QuarryTerrain.At(x, z));
            const float apex = 1.65f;
            var backLeft = new Vector3(r.xMin - .65f, h, r.yMin - .65f);
            var frontLeft = new Vector3(r.xMin - .65f, h, r.yMax + .65f);
            var backRight = new Vector3(r.xMax + .65f, h, r.yMin - .65f);
            var frontRight = new Vector3(r.xMax + .65f, h, r.yMax + .65f);
            var ridgeA = new Vector3(backLeft.x, h + apex, z);
            var ridgeB = new Vector3(backRight.x, h + apex, z);
            building.Structures.Quad(backLeft, ridgeA, ridgeB, backRight, PortTone.Steel);
            building.Structures.Quad(ridgeA, frontLeft, frontRight, ridgeB, PortTone.Slate);
            building.Structures.Tri(backLeft, frontLeft, ridgeA, PortTone.Slate);
            building.Structures.Tri(frontRight, backRight, ridgeB, PortTone.Steel);
            building.Structures.Floor(r.xMin, r.yMin, r.xMax, r.yMax, .018f, PortTone.Concrete);
            building.Bake();
        }

        void BuildCrusher()
        {
            Rect area = CrusherFootprint;
            float x = area.center.x, z = area.center.y;
            var crusher = new PortChunk(root, "Screening hopper and crushers", materials, keep, QuarryTerrain.Floor(x, z));
            foreach (int side in new[] { -1, 1 })
                foreach (int end in new[] { -1, 1 })
                {
                    float a = x + side * 9.4f, b = z + end * 6.1f;
                    crusher.Block(new Vector3(a, .52f, b), new Vector3(2.35f, 1.04f, 2.35f), PortTone.Concrete);
                    crusher.Block(new Vector3(a, 4f, b), new Vector3(.85f, 8f, 1), PortTone.Ochre);
                }
            Vector3 deck = new Vector3(x, 8.1f, z);
            crusher.Block(deck, new Vector3(area.width, 2.4f, area.height), PortTone.Slate);
            crusher.Block(deck + Vector3.up * 2.35f, new Vector3(16, 2.9f, 12.3f), PortTone.Steel);
            crusher.Structures.Box(deck + Vector3.up * 4.2f, new Vector3(18.4f, .85f, 14.4f), PortTone.Zinc);
            var head = new Vector3(x, 8.35f, area.yMin + .6f);
            var tail = new Vector3(159, 1.9f, -1);
            Vector3 axis = tail - head;
            Solid(crusher, (head + tail) * .5f, new Vector3(4.1f, axis.magnitude, 1.28f),
                Quaternion.FromToRotation(Vector3.up, axis), PortTone.Slate);
            var footing = QuarryTerrain.At(tail.x, tail.z).y - crusher.Root.transform.localPosition.y;
            if (tail.y - footing > .25f)
                crusher.Block(new Vector3(tail.x, (footing + tail.y) * .5f, tail.z), new Vector3(3.5f, tail.y - footing, 3.7f), PortTone.Concrete);
            for (int i = 1; i <= 3; i++)
            {
                var p = Vector3.Lerp(head, tail, i * .24f);
                float y = QuarryTerrain.Floor(p.x, p.z) - crusher.Root.transform.localPosition.y;
                if (p.y - y > 1.2f) crusher.Block(new Vector3(p.x, (p.y + y) * .5f, p.z), new Vector3(.8f, p.y - y, 1.15f), PortTone.Ochre);
            }
            crusher.Bake();
        }

        void BuildCover()
        {
            BuildRows(-245, -227, 3, 8.6f, PortTone.Rust);
            BuildRows(51, 206, 4, 8.2f, PortTone.Ochre);
            BuildRows(232, 26, 3, 6.5f, PortTone.Slate);
            BuildRows(168, -160, 3, 7.8f, PortTone.Slate);
            BuildRows(-28, -12, 4, 10.4f, PortTone.Rock);
            BuildRows(211, 99, 3, 7.3f, PortTone.Steel);

            var random = new System.Random(0x4B5313);
            for (int k = 0; k < 4; k++)
            {
                var rock = new PortChunk(root, "Fractured quarry outcrops " + k, materials, keep);
                for (int i = 0; i < 105; i++)
                {
                    float x = (float)random.NextDouble() * 650 - 325, z = (float)random.NextDouble() * 650 - 325;
                    if ((x < 0 ? 0 : 1) + (z < 0 ? 0 : 2) != k || !QuarryTerrain.TryWalk(x, z, out float y)) continue;
                    if (NavigationGrid.PlanarDistanceSquared(Spawn, new Vector3(x, y, z)) < 26 * 26) continue;
                    if (Array.Exists(Pads, p => p.Distance(x, z) < 8f)) continue;
                    if (Array.Exists(QuarryTerrain.Segments, p => p.DistanceSq(x, z, out _) < (p.HalfWidth + 10) * (p.HalfWidth + 10))) continue;
                    if (Mathf.Abs(x - Pond.x) < PondRadii.x + 10 && Mathf.Abs(z - Pond.y) < PondRadii.y + 10) continue;
                    float w = 1.4f + (float)random.NextDouble() * 3.6f, h = 1.2f + (float)random.NextDouble() * 2.8f;
                    Vector3 p0 = new Vector3(x, y + h * .38f, z);
                    var tone = i % 3 == 0 ? PortTone.RockLichen : PortTone.Rock;
                    rock.Structures.Cylinder(p0, w * .63f, h, tone, Quaternion.Euler(8 + i % 15, i * 57, -13), 5 + i % 2);
                    rock.Collider(p0, new Vector3(w * .77f, h * .88f, w * .77f));
                }
                rock.Bake();
            }
        }

        void BuildRows(float x, float z, int count, float spacing, PortTone tone)
        {
            var cover = new PortChunk(root, "Quarry stock and barriers", materials, keep);
            for (int i = 0; i < count; i++)
            {
                float wx = x + spacing * i, wz = z + (i % 2) * 4;
                float y = QuarryTerrain.Floor(wx, wz);
                float h = i % 3 == 1 ? 1.8f : 1.25f;
                var center = new Vector3(wx, y + h * .46f, wz);
                var size = new Vector3(i % 2 == 1 ? 4.1f : 2.7f, h, 2.75f);
                cover.Block(center, size, tone);
                cover.Structures.Box(center + Vector3.up * (h * .52f), new Vector3(size.x - .2f, .15f, 2.52f), PortTone.Zinc);
                for (int rib = -1; rib <= 1; rib += 2)
                    cover.Structures.Box(center + new Vector3(rib * size.x * .34f, 0, 1.39f), new Vector3(.14f, h, .14f), PortTone.Steel);
            }
            cover.Bake();
        }

        void BuildExtractionBeacon(PortExit exit)
        {
            Vector3 p = exit.Position;
            var post = new PortChunk(root, exit.Name + " beacon", materials, keep, p.y);
            float outer = p.x + (p.x > 0 ? 1 : -1) * (exit.Radius + 1.2f);
            var basePt = new Vector3(outer, 0, p.z);
            post.Block(basePt + Vector3.up * 1.25f, new Vector3(.44f, 2.7f, .44f), PortTone.Steel);
            post.Structures.Box(basePt + Vector3.up * 2.9f, new Vector3(.79f, .48f, .79f), PortTone.Emerald);
            post.Structures.Box(basePt + Vector3.up * 3.18f, new Vector3(.96f, .16f, .96f), PortTone.Zinc);
            post.Bake();
        }

        void SeedRoutes()
        {
            for (int z = -309; z <= 309; z += 25)
                for (int x = -309; x <= 309; x += 25)
                    if (QuarryTerrain.TryWalk(x, z, out float y)) nodes.Add(new Vector3(x, y, z));
            foreach (var segment in QuarryTerrain.Segments)
            {
                float distance = Mathf.Sqrt(NavigationGrid.PlanarDistanceSquared(segment.A, segment.B));
                int count = Mathf.CeilToInt(distance / 12f);
                for (int step = 0; step <= count; step++)
                {
                    Vector3 point = Vector3.Lerp(segment.A, segment.B, step / (float)count);
                    nodes.Add(QuarryTerrain.At(point.x, point.z));
                }
            }
            nodes.Add(Spawn);
            foreach (var exit in Exits) nodes.Add(exit.Position);
        }
    }
}
