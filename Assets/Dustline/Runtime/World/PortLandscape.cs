using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static Dustline.PortLayout;

namespace Dustline
{
    internal sealed class PortLandscape
    {
        const int TileCells = 48;
        readonly Transform root;
        readonly PortMaterials materials;
        readonly Action<UnityEngine.Object> keep;
        readonly List<Vector3> nodes;
        readonly System.Random plants = new System.Random(0x2c560a);

        internal PortLandscape(Transform root, PortMaterials materials, Action<UnityEngine.Object> keep, List<Vector3> nodes)
        { this.root = root; this.materials = materials; this.keep = keep; this.nodes = nodes; }

        internal void Build()
        {
            BuildGround();
            BuildWater();
            BuildBanks();
            BuildBoundary();
            BuildFlora();
            BuildRoadNetwork();
        }

        void BuildGround()
        {
            var terrain = PortMesh.Child(root, "North Cape terrain");
            terrain.isStatic = true;
            for (int z = 0; z < PortTerrain.Cells; z += TileCells)
                for (int x = 0; x < PortTerrain.SeaColumn; x += TileCells)
                {
                    int nx = Mathf.Min(TileCells, PortTerrain.SeaColumn - x), nz = Mathf.Min(TileCells, PortTerrain.Cells - z);
                    var tile = PortMesh.Child(terrain.transform, "Grade " + x + " " + z);
                    tile.isStatic = true;
                    BuildGrade(tile, x, z, nx, nz);
                }
            var seabed = PortMesh.Child(terrain.transform, "Tidal basin seabed");
            seabed.isStatic = true;
            BuildGrade(seabed, PortTerrain.SeaColumn, 0, 1, 1, PortTerrain.Cells - PortTerrain.SeaColumn, PortTerrain.Cells);
        }

        void BuildGrade(GameObject tile, int x, int z, int nx, int nz, int strideX = 1, int strideZ = 1)
        {
            int width = nx + 1;
            var vertices = new Vector3[width * (nz + 1)];
            var normals = new Vector3[vertices.Length];
            var tangents = new Vector4[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var detail = new List<Vector4>(vertices.Length);
            var triangles = new int[nx * nz * 6];
            const float s = PortTerrain.Step;
            for (int dz = 0; dz <= nz; dz++)
                for (int dx = 0; dx <= nx; dx++)
                {
                    int i = x + dx * strideX, j = z + dz * strideZ, v = dz * width + dx;
                    vertices[v] = GradePoint(i, j);
                    uv[v] = new Vector2(i * s / Size, j * s / Size);
                    float west = strideX > 1 ? vertices[v].y : PortTerrain.VertexHeight(Mathf.Max(0, i - 1), j);
                    float east = PortTerrain.VertexHeight(Mathf.Min(PortTerrain.Cells, i + 1), j);
                    float south = PortTerrain.VertexHeight(i, Mathf.Max(0, j - 1));
                    float north = PortTerrain.VertexHeight(i, Mathf.Min(PortTerrain.Cells, j + 1));
                    normals[v] = new Vector3(west - east, s * 2, south - north).normalized;
                    Vector3 tangent = new Vector3(s * 2, east - west, 0f).normalized;
                    tangents[v] = new Vector4(tangent.x, tangent.y, tangent.z, -1f);
                    detail.Add(new Vector4(vertices[v].x, vertices[v].z, -1f, 1f));
                    if (dx == nx || dz == nz) continue;
                    int t = (dz * nx + dx) * 6;
                    triangles[t] = v; triangles[t + 1] = v + width; triangles[t + 2] = v + width + 1;
                    triangles[t + 3] = v; triangles[t + 4] = v + width + 1; triangles[t + 5] = v + 1;
                }
            var mesh = new Mesh { name = tile.name, vertices = vertices, normals = normals, tangents = tangents, uv = uv, triangles = triangles };
            mesh.SetUVs(1, detail);
            keep(mesh);
            tile.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = tile.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = materials.Ground;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            tile.AddComponent<MeshCollider>().sharedMesh = mesh;
            mesh.UploadMeshData(true);
            tile.AddComponent<FootstepSurface>().UsesPortGround = true;
        }

        void BuildWater()
        {
            materials.Sea.SetFloat("_Cull", (float)CullMode.Off);
            for (int start = 0; start < PortTerrain.SeaColumn; start += TileCells)
            {
                var water = new PortMesh();
                int end = Mathf.Min(PortTerrain.SeaColumn, start + TileCells);
                for (int x = start; x < end; x++)
                    for (int z = 0; z < PortTerrain.Cells; z++)
                    {
                        Vector3 a = GradePoint(x, z), b = GradePoint(x, z + 1);
                        Vector3 c = GradePoint(x + 1, z + 1), d = GradePoint(x + 1, z);
                        // The same diagonal as the collider keeps each waterline on the bare grade.
                        WetTriangle(water, a, b, c);
                        WetTriangle(water, a, c, d);
                    }
                water.Bake(root, "Tideline water " + start, materials.Sea, keep, false);
            }
            for (int i = 0; i < 6; i++)
            {
                var sea = new PortMesh();
                float z = -HalfSize + i * 160;
                sea.Floor(PortTerrain.SeaBedStart, z, HalfSize + 96, z + 160, PortTerrain.SeaY, PortTone.Water);
                sea.Bake(root, "Tidal basin " + i, materials.Sea, keep, false);
            }
        }

        static Vector3 GradePoint(int x, int z) => new Vector3(x * PortTerrain.Step - HalfSize,
            PortTerrain.VertexHeight(x, z), z * PortTerrain.Step - HalfSize);

        static void WetTriangle(PortMesh water, Vector3 a, Vector3 b, Vector3 c)
        {
            const float level = PortTerrain.SeaY;
            if (a.y >= level && b.y >= level && c.y >= level) return;
            if (a.y >= level)
                (a, b, c) = b.y < level ? (b, c, a) : (c, a, b);

            bool wetB = b.y <= level, wetC = c.y <= level;
            Vector3 p = wetB ? b : Waterline(a, b), q = wetC ? c : Waterline(c, a);
            a.y = p.y = q.y = level;
            if (wetB == wetC || b.y == level || c.y == level) water.Tri(a, p, q, PortTone.Water);
            else water.Quad(a, p, Waterline(b, c), q, PortTone.Water);

            static Vector3 Waterline(Vector3 from, Vector3 to)
            {
                var point = Vector3.LerpUnclamped(from, to, (PortTerrain.SeaY - from.y) / (to.y - from.y));
                point.y = PortTerrain.SeaY;
                return point;
            }
        }

        void BuildBanks()
        {
            foreach (var span in PortTerrain.Bridges)
            {
                const float halfWidth = PortTerrain.Bridge.HalfWidth;
                var c = new PortChunk(root, "Service bridge " + span.X, materials, keep);
                const float thickness = PortTerrain.Bridge.DeckThickness;
                Vector3 deckCenter = new Vector3(span.X, span.DeckY - thickness * .5f, span.Z);
                Vector3 deckSize = new Vector3(halfWidth * 2, thickness, span.HalfLength * 2);
                c.Structures.Box(deckCenter, deckSize, PortTone.Slate);
                var surface = PortMesh.Child(c.Root.transform, "Bridge steel deck");
                surface.isStatic = true;
                var floor = surface.AddComponent<BoxCollider>();
                floor.center = deckCenter; floor.size = deckSize;
                var bridgeSurface = surface.AddComponent<FootstepSurface>();
                bridgeSurface.Kind = SurfaceKind.Metal;
                bridgeSurface.IsBridgeDeck = true;
                foreach (int side in new[] { -1, 1 })
                {
                    float x = span.X + side * (halfWidth - .19f);
                    c.Block(new Vector3(x, span.DeckY + .75f, span.Z), new Vector3(.37f, 1.50f, span.HalfLength * 2), PortTone.Concrete);
                    c.Block(new Vector3(x, span.DeckY + 1.59f, span.Z), new Vector3(.43f, .18f, span.HalfLength * 2), PortTone.Ochre);
                    for (int pier = -1; pier <= 1; pier++)
                    {
                        float z = span.Z + pier * span.HalfLength * .71f;
                        float top = span.DeckY - thickness;
                        float bottom = Mathf.Min(top - .4f, PortTerrain.Ground(x, z) - .7f);
                        c.Block(new Vector3(x, (top + bottom) * .5f, z), new Vector3(1.6f, top - bottom, 2.1f), PortTone.Concrete);
                    }
                    float paint = span.X + side * (halfWidth - 1.02f);
                    c.Deck.Floor(paint - .23f, span.Z - span.HalfLength + .7f, paint + .23f, span.Z + span.HalfLength - .7f,
                        span.DeckY + .045f, PortTone.Paint);
                }
                for (float z = span.Z - span.HalfLength - 9; z <= span.Z + span.HalfLength + 9; z += 5)
                    nodes.Add(At(span.X, z));
                nodes.Add(At(span.X, span.Z + span.HalfLength + 2));
                nodes.Add(At(span.X, span.Z - span.HalfLength - 2));
                c.Bake();
            }
            for (int side = -1; side <= 1; side += 2)
            {
                var bank = new PortChunk(root, "Canal retaining wall " + side, materials, keep);
                for (float from = PortTerrain.RetainingWallStart(side); from < PortTerrain.QuayEdge; from += 6)
                {
                    float to = Mathf.Min(PortTerrain.QuayEdge, from + 6);
                    float z0 = PortTerrain.RiverZ(from) + side * PortTerrain.RiverRail;
                    float z1 = PortTerrain.RiverZ(to) + side * PortTerrain.RiverRail;
                    Wall(bank, from, z0, to, z1, 1.67f, .49f, PortTone.Concrete, bankSide: side,
                        minimumBase: PortTerrain.SeaBedY - .8f);
                }
                bank.Bake();
            }
        }

        void BuildBoundary()
        {
            const float edge = PortTerrain.Boundary, quay = PortTerrain.QuayEdge;
            const float deepBase = PortTerrain.SeaBedY - .8f;
            const float parapet = 1.67f, quayTop = PortTerrain.QuayY + parapet;
            const float width = edge / 4;
            float mouth = PortTerrain.RiverZ(quay);
            for (int i = 0; i < 8; i++)
            {
                var c = new PortChunk(root, "Breakwater and ridge wall " + i, materials, keep);
                float a = -edge + i * width;
                for (float p = a; p < a + width; p += 9)
                {
                    float next = Mathf.Min(p + 9, a + width);
                    Wall(c, -edge, p, -edge, next, 2.8f, 1.2f, PortTone.Concrete, deepBase,
                        minimumTop: PortTerrain.SeaY + 2.1f);
                    // Cover the entire sloped mesh cell under the vertical quay face.
                    if (p < mouth - PortTerrain.RiverRail)
                        Wall(c, quay, p, quay, Mathf.Min(next, mouth - PortTerrain.RiverRail), parapet, PortTerrain.Step,
                            PortTone.ConcreteLight, deepBase, minimumTop: quayTop);
                    if (next > mouth + PortTerrain.RiverRail)
                        Wall(c, quay, Mathf.Max(p, mouth + PortTerrain.RiverRail), quay, next, parapet, PortTerrain.Step,
                            PortTone.ConcreteLight, deepBase, minimumTop: quayTop);
                    if (p < quay)
                    {
                        float end = Mathf.Min(next, quay);
                        Wall(c, p, -edge, end, -edge, 2.8f, 1.23f, PortTone.Concrete);
                        Wall(c, p, edge, end, edge, 2.8f, 1.23f, PortTone.Concrete);
                    }
                }
                c.Bake();
            }
            var basin = new PortChunk(root, "Tidal basin breakwater", materials, keep);
            const float rise = PortTerrain.SeaY - PortTerrain.SeaBedY + 1.6f;
            Wall(basin, PortTerrain.SeaBedStart, -edge, edge, -edge, rise, 1.23f, PortTone.Concrete);
            Wall(basin, PortTerrain.SeaBedStart, edge, edge, edge, rise, 1.23f, PortTone.Concrete);
            Wall(basin, edge, -edge, edge, edge, rise, 1.23f, PortTone.Concrete);
            // The quay toe is shallower than the flat seabed; close the two corners beneath the waterline.
            Wall(basin, quay, -edge, PortTerrain.SeaBedStart, -edge, 1.6f, 1.23f, PortTone.Concrete, deepBase, minimumTop: .2f);
            Wall(basin, quay, edge, PortTerrain.SeaBedStart, edge, 1.6f, 1.23f, PortTone.Concrete, deepBase, minimumTop: .2f);
            basin.Bake();
        }

        static void Wall(PortChunk c, float x0, float z0, float x1, float z1, float height, float width, PortTone tone,
            float minimumBase = float.PositiveInfinity, int bankSide = 0, float minimumTop = float.NegativeInfinity)
        {
            Vector3 a = At(x0, z0), b = At(x1, z1);
            Vector3 line = b - a; line.y = 0;
            Vector3 side = Vector3.Cross(line.normalized, Vector3.up) * (width * .5f);
            Vector3 lo = Vector3.up * .65f;
            Vector3 topA = a + Vector3.up * height, topB = b + Vector3.up * height;
            topA.y = Mathf.Max(topA.y, minimumTop); topB.y = Mathf.Max(topB.y, minimumTop);
            if (bankSide != 0)
            {
                topA.y = Mathf.Max(topA.y, PortTerrain.Ground(x0, z0 + bankSide * 2.1f) + height);
                topB.y = Mathf.Max(topB.y, PortTerrain.Ground(x1, z1 + bankSide * 2.1f) + height);
            }
            Vector3 baseA = a - lo, baseB = b - lo;
            baseA.y = Mathf.Min(baseA.y, minimumBase); baseB.y = Mathf.Min(baseB.y, minimumBase);
            c.Structures.Quad(baseA + side, baseB + side, topB + side, topA + side, tone);
            c.Structures.Quad(baseB - side, baseA - side, topA - side, topB - side, tone);
            c.Structures.Quad(topA - side, topA + side, topB + side, topB - side, PortTone.Slate);
            float bottom = Mathf.Min(baseA.y, baseB.y), top = Mathf.Max(topA.y, topB.y);
            Vector3 midpoint = (a + b) * .5f;
            midpoint.y = (top + bottom) * .5f;
            var size = new Vector3(width, top - bottom, line.magnitude + .15f);
            if (Mathf.Abs(line.x) < .001f) c.Collider(midpoint, size);
            else if (Mathf.Abs(line.z) < .001f) c.Collider(midpoint, new Vector3(size.z, size.y, size.x));
            else
            {
                var piece = PortMesh.Child(c.Root.transform, "Wall segment");
                piece.isStatic = true;
                piece.transform.SetPositionAndRotation(midpoint, Quaternion.LookRotation(line));
                piece.AddComponent<BoxCollider>().size = size;
            }
        }

        void BuildRoadNetwork()
        {
            foreach (var road in PortTerrain.Roads)
            {
                float length = Vector2.Distance(road.A, road.B);
                int count = Mathf.CeilToInt(length / 22);
                for (int i = 0; i <= count; i++)
                {
                    Vector2 p = Vector2.Lerp(road.A, road.B, i / (float)count);
                    nodes.Add(At(p.x, p.y));
                }
            }
            var roadside = new PortMesh();
            foreach (float x in new[] { -334f, -40f, 221f })
                foreach (float z in new[] { -300f, -195f, 51f, 181f, 350f })
                    PortScenery.Pole(roadside, x + 10, z + 4, 8.4f, -1, PortTerrain.Ground(x + 10, z + 4));
            roadside.Bake(root, "Street lamps", materials.Structure, keep, true);
        }

        void BuildFlora()
        {
            for (int j = 0; j < 8; j++)
                for (int i = 0; i < 7; i++)
                {
                    var natural = new PortChunk(root, "Headland vegetation " + i + " " + j, materials, keep);
                    for (int n = 0; n < 18; n++)
                    {
                        float x = -464 + i * 116 + Next(2, 114), z = -469 + j * 117 + Next(2, 115);
                        if (x > 342 || !PortTerrain.IsNatural(x, z)) continue;
                        Vector3 p = At(x, z);
                        if (p.y < PortTerrain.SeaY + .4f || DistanceSquared(p, Spawn) < 15 * 15) continue;
                        float kind = Next(0, 1);
                        if (kind < .23f && PortTerrain.TryWalk(x, z, out _))
                            Tree(natural, p, Next(3.7f, 10.2f), kind < .165f);
                        else if (kind < .54f) Stone(natural, p, Next(.42f, p.y > 20 ? 3.5f : 1.25f));
                        else Shrubs(natural.Deck, p, kind > .81f);
                    }
                    natural.Bake();
                }
        }

        void Tree(PortChunk c, Vector3 ground, float height, bool pine)
        {
            float trunk = height * (pine ? .027f : .036f);
            c.Structures.Cylinder(ground + Vector3.up * (height * .38f), trunk, height * .79f, PortTone.Bark, 6);
            c.Collider(ground + Vector3.up * (height * .38f), new Vector3(trunk * 1.8f, height * .78f, trunk * 1.8f));
            float sway = Next(0, Mathf.PI);
            if (pine)
            {
                for (int tier = 0; tier < 4; tier++)
                {
                    float radius = height * (.229f - .044f * tier) * Next(.87f, 1.15f);
                    Vector3 p = ground + Vector3.up * (height * (.21f + .175f * tier));
                    Crown(c.Structures, p, radius, .33f * height, sway + tier, PortTone.Needles, PortTone.NeedlesLight);
                }
            }
            else
            {
                for (int branch = 0; branch < 4; branch++)
                {
                    float angle = sway + branch * 1.57f;
                    Vector3 spread = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (height * .17f);
                    Vector3 top = ground + spread + Vector3.up * (height * Next(.55f, .73f));
                    c.Structures.Beam(ground + Vector3.up * (height * .42f), top + Vector3.up * .38f, trunk, trunk, PortTone.Bark);
                    Crown(c.Structures, top, Next(.16f, .28f) * height, height * .3f, angle, PortTone.Leaves, PortTone.NeedlesLight);
                }
            }
        }

        static void Crown(PortMesh mesh, Vector3 p, float radius, float height, float yaw, PortTone a, PortTone b)
        {
            const int sides = 7;
            Vector3 tip = p + new Vector3(radius * .16f, height, 0), basePoint = p + Vector3.up * (height * -.32f);
            for (int i = 0; i < sides; i++)
            {
                float from = yaw + i * (2 * Mathf.PI / sides), to = yaw + (i + 1) * (2 * Mathf.PI / sides);
                Vector3 v0 = p + new Vector3(Mathf.Sin(from) * radius, 0, Mathf.Cos(from) * radius);
                Vector3 v1 = p + new Vector3(Mathf.Sin(to) * radius, 0, Mathf.Cos(to) * radius);
                mesh.Tri(v0, v1, tip, i % 3 == 0 ? b : a);
                mesh.Tri(v0, basePoint, v1, a);
            }
        }

        void Stone(PortChunk c, Vector3 p, float size)
        {
            Crown(c.Structures, p + Vector3.up * (size * .18f), size, size * .54f, Next(0, Mathf.PI), PortTone.Rock, PortTone.RockLichen);
            if (size > .9f)
                c.Collider(p + Vector3.up * (size * .17f), new Vector3(size * 1.42f, size * .51f, size * 1.42f));
        }

        void Shrubs(PortMesh mesh, Vector3 p, bool flowers)
        {
            for (int i = 0; i < 4; i++)
            {
                float x = p.x + Next(-1.2f, 1.2f), z = p.z + Next(-1.2f, 1.2f), h = Next(.22f, .63f);
                Vector3 b = At(x, z) + Vector3.up * .025f;
                Crown(mesh, b + Vector3.up * .13f, h * .71f, h, Next(0, 7), PortTone.DryGrass, PortTone.Leaves);
                if (flowers) Crown(mesh, b + Vector3.up * (h + .09f), h * .25f, .085f, 0, PortTone.FlowerPale, PortTone.FlowerOchre);
            }
        }

        float Next(float min, float max) => Mathf.Lerp(min, max, (float)plants.NextDouble());
    }
}
