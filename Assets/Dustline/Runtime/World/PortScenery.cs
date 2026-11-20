using System;
using System.Collections.Generic;
using UnityEngine;
using static Dustline.PortLayout;

namespace Dustline
{
    internal sealed class PortScenery
    {
        readonly Transform root;
        readonly PortMaterials materials;
        readonly Action<UnityEngine.Object> keep;
        readonly List<Vector3> nodes;
        static readonly float[] arterials = { -334, -40, 221 };
        static readonly float[] crossStreets = { -306, -31, 181, 350 };

        internal PortScenery(Transform root, PortMaterials materials, Action<UnityEngine.Object> keep, List<Vector3> nodes)
        {
            this.root = root;
            this.materials = materials;
            this.keep = keep;
            this.nodes = nodes;
        }

        internal void Build()
        {
            Terrain();
            Streets();
            Gatehouse();
            Yard();
            Customs();
            Workshop();
            Railhead();
            Quay();
            ExtractPads();
            const int spacing = 48;
            for (float z = -446; z <= 446; z += spacing)
                for (float x = -440; x < Shore - 8; x += spacing)
                    Node(x, z);
            nodes.Add(Spawn);
            nodes.AddRange(BotPosts);
            foreach (var exit in Exits) nodes.Add(exit.Position);
        }

        PortChunk Chunk(string name) => new PortChunk(root, name, materials, keep);
        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);
        void Node(float x, float z) => nodes.Add(At(x, z));

        void Terrain()
        {
            var colliders = Chunk("Site perimeter and ground");
            colliders.Collider(V((Shore - HalfSize) * 0.5f, -0.55f, 0), V(Shore + HalfSize, 1.1f, Size));
            colliders.Collider(V(-479, 1.6f, 0), V(2, 3.2f, Size));
            colliders.Collider(V(-62, 1.6f, -479), V(836, 3.2f, 2));
            colliders.Collider(V(-62, 1.6f, 479), V(836, 3.2f, 2));
            colliders.Collider(V(355, 0.95f, 0), V(1.1f, 1.9f, Size));

            for (int row = 0; row < 4; row++)
            {
                float z0 = -HalfSize + row * 240, z1 = z0 + 240;
                for (int col = 0; col < 4; col++)
                {
                    float x0 = -HalfSize + col * (Shore + HalfSize) / 4, x1 = x0 + (Shore + HalfSize) / 4;
                    var block = Chunk("Ground cell " + col + "-" + row);
                    for (int iz = 0; iz < 5; iz++)
                        for (int ix = 0; ix < 5; ix++)
                        {
                            float tx = Mathf.Lerp(x0, x1, ix / 5f), tz = Mathf.Lerp(z0, z1, iz / 5f);
                            var tone = (row * 7 + col * 3 + ix * 5 + iz * 2) % 11 < 4 ? PortTone.SandDark : PortTone.Sand;
                            block.Deck.Floor(tx, tz, Mathf.Lerp(x0, x1, (ix + 1) / 5f),
                                Mathf.Lerp(z0, z1, (iz + 1) / 5f), 0, tone);
                        }
                    if (col == 0) WallStrip(block.Structures, -479, z0, z1, true);
                    if (row == 0) WallStrip(block.Structures, -479, x0, x1, false);
                    if (row == 3) WallStrip(block.Structures, 479, x0, x1, false);
                    if (col == 3)
                    {
                        block.Structures.Box(V(355, 0.84f, (z0 + z1) * 0.5f), V(1.1f, 2.12f, 240), PortTone.Concrete);
                        block.Structures.Box(V(355, 1.89f, (z0 + z1) * 0.5f), V(1.25f, 0.14f, 240), PortTone.Yellow);
                        for (float z = z0 + 10; z < z1; z += 20)
                            block.Structures.Box(V(355.92f, -0.34f, z), V(0.4f, 1.7f, 2.3f), PortTone.Steel);
                    }
                    block.Bake();
                }

                var sea = new PortMesh();
                for (int i = 0; i < 6; i++)
                    for (int j = 0; j < 8; j++)
                    {
                        float x = Mathf.Lerp(Shore, HalfSize, i / 6f), z = z0 + j * 30;
                        sea.Floor(x, z, Mathf.Lerp(Shore, HalfSize, (i + 1) / 6f), z + 30, -1.16f,
                            (i * 3 + j * 7 + row) % 7 < 2 ? PortTone.WaterDark : PortTone.Water);
                        float shift = (i * 13 + j * 9 + row) % 13;
                        sea.Floor(x + 3 + shift * 0.5f, z + shift + 5, x + 19, z + shift + 5.27f, -1.12f, PortTone.WaterLight);
                    }
                sea.Bake(root, "Outer basin " + row, materials.Sea, keep, false);
            }
        }

        static void WallStrip(PortMesh mesh, float fixedAxis, float from, float to, bool alongZ)
        {
            Vector3 P(float line, float y) => alongZ ? V(fixedAxis, y, line) : V(line, y, fixedAxis);
            Vector3 D(float width, float height, float length) => alongZ ? V(width, height, length) : V(length, height, width);
            mesh.Box(P((from + to) * 0.5f, 1.46f), D(1.6f, 2.92f, to - from), PortTone.Concrete);
            mesh.Box(P((from + to) * 0.5f, 3.04f), D(1.84f, 0.24f, to - from), PortTone.Slate);
            for (float p = from + 4; p < to; p += 20)
                mesh.Box(P(p, 1.6f), D(2, 3.2f, 0.9f), PortTone.ConcreteLight);
        }

        void Streets()
        {
            var streets = Chunk("Port service roads");
            foreach (float x in arterials)
            {
                streets.Surface(V(x, 0.045f, 0), V(16, 0.09f, 888), SurfaceKind.Asphalt);
                for (int z = -444; z < 443; z += 32)
                    streets.Deck.Floor(x - 8, z, x + 8, Mathf.Min(z + 32, 444), 0.023f, PortTone.Asphalt);
                for (int z = -431; z < 441; z += 14)
                {
                    if (NearCrossing(z, crossStreets)) continue;
                    streets.Deck.Floor(x - 0.13f, z, x + 0.13f, z + 5, 0.078f, PortTone.Yellow);
                    streets.Deck.Floor(x - 7, z, x - 6.78f, z + 8, 0.078f, PortTone.Paint);
                    streets.Deck.Floor(x + 6.78f, z, x + 7, z + 8, 0.078f, PortTone.Paint);
                }
                for (int z = -438; z <= 438; z += 54) Node(x, z);
            }
            foreach (float z in crossStreets)
            {
                streets.Surface(V(-53.5f, 0.045f, z), V(801, 0.09f, 15), SurfaceKind.Asphalt);
                for (int x = -454; x < 347; x += 32)
                    streets.Deck.Floor(x, z - 7.5f, Mathf.Min(x + 32, 347), z + 7.5f, 0.027f, PortTone.Asphalt);
                for (int x = -447; x < 343; x += 15)
                {
                    if (NearCrossing(x, arterials)) continue;
                    streets.Deck.Floor(x, z - 0.14f, x + 5, z + 0.14f, 0.078f, PortTone.Yellow);
                    streets.Deck.Floor(x, z + 6.13f, x + 9, z + 6.33f, 0.078f, PortTone.Paint);
                    streets.Deck.Floor(x, z - 6.33f, x + 9, z - 6.13f, 0.078f, PortTone.Paint);
                }
                foreach (float x in arterials)
                {
                    for (int i = -3; i <= 3; i++)
                        streets.Deck.Floor(x + i * 1.42f, z - 11.5f, x + i * 1.42f + 0.74f, z - 8.75f, 0.075f, PortTone.Paint);
                    Node(x, z);
                    Pole(streets.Structures, x + 11.5f, z + 11, 8.5f, x < 0 ? -1 : 1);
                }
                for (int x = -445; x <= 343; x += 54) Node(x, z);
            }
            streets.Bake();
        }

        static bool NearCrossing(float v, float[] streets)
        {
            foreach (float street in streets) if (Mathf.Abs(v - street) < 20) return true;
            return false;
        }

        static void Pad(PortMesh mesh, float x0, float z0, float x1, float z1, PortTone tone)
        {
            int nx = Mathf.CeilToInt((x1 - x0) / 24), nz = Mathf.CeilToInt((z1 - z0) / 24);
            for (int x = 0; x < nx; x++)
                for (int z = 0; z < nz; z++)
                {
                    PortTone tile = tone == PortTone.Concrete && (x + z * 3) % 9 == 5 ? PortTone.ConcreteLight : tone;
                    float ax = Mathf.Lerp(x0, x1, x / (float)nx), az = Mathf.Lerp(z0, z1, z / (float)nz);
                    mesh.Floor(ax, az, Mathf.Lerp(x0, x1, (x + 1f) / nx) - 0.035f,
                        Mathf.Lerp(z0, z1, (z + 1f) / nz) - 0.035f, 0.043f, tile);
                }
        }

        static void Pole(PortMesh mesh, float x, float z, float height, int arm)
        {
            mesh.Box(V(x, 0.2f, z), V(0.65f, 0.4f, 0.65f), PortTone.ConcreteLight);
            mesh.Box(V(x, height * 0.5f, z), V(0.21f, height, 0.21f), PortTone.Slate);
            mesh.Beam(V(x, height - 0.66f, z), V(x + arm * 1.55f, height + 0.16f, z), 0.15f, 0.19f, PortTone.Slate);
            mesh.Box(V(x + arm * 1.62f, height + 0.16f, z), V(0.73f, 0.17f, 0.43f), PortTone.Paint);
        }

        void Gatehouse()
        {
            var c = Chunk("Southwest service entrance");
            Pad(c.Deck, -393, -356, -343, -313, PortTone.Concrete);
            Hall(c, -350, -337, 13, 10, 3.6f, new[] { -351f }, new[] { -350f }, 2.2f, PortTone.Plaster, PortTone.Teal);
            AbiBarrier(c, -382, -317, 0);
            AbiBarrier(c, -348, -315, 0);
            Pole(c.Structures, -375, -318, 6.4f, 1);
            c.Block(V(-367.5f, 3.4f, -314), V(0.5f, 6.8f, 0.55f), PortTone.Steel);
            c.Block(V(-347.5f, 3.4f, -314), V(0.5f, 6.8f, 0.55f), PortTone.Steel);
            c.Structures.Box(V(-357.5f, 6.75f, -314), V(21, 1.25f, 0.55f), PortTone.DeepBlue);
            for (int i = 0; i < 7; i++)
                c.Structures.Box(V(-365 + i * 2.5f, 6.74f, -314.29f), V(0.62f, 0.58f, 0.04f), i % 2 == 0 ? PortTone.Paint : PortTone.Teal);
            Arrow(c.Deck, -357, -322, 1, 1, 4, PortTone.Yellow);
            Node(-357, -321);
            Node(-361, -300);
            c.Bake();
        }

        void Yard()
        {
            var c = Chunk("01 container yard");
            Pad(c.Deck, -313, -264, -62, -61, PortTone.Concrete);
            Hall(c, -292, -235, 14, 10, 3.8f, new[] { -291f }, new[] { -291f }, 2.5f, PortTone.Plaster, PortTone.Teal);
            float[] rows = { -214, -161, -106 };
            float[] bays = { -269, -232, -192, -149, -106 };
            PortTone[] freight = { PortTone.Rust, PortTone.Slate, PortTone.Teal, PortTone.Terra, PortTone.Ochre };
            for (int r = 0; r < rows.Length; r++)
                for (int b = 0; b < bays.Length; b++)
                {
                    float x = bays[b], z = rows[r];
                    Freight(c, x, z - 6, 12.2f, 1 + (r + b) % 3, freight[(b + r) % freight.Length]);
                    if ((b + r) % 3 != 1) Freight(c, x + 5.5f, z + 5.6f, 12.2f, 1 + (b + r) % 2, freight[(b + r + 2) % freight.Length]);
                    if (r == 0 || r == 2) Pallets(c, x + 12, z - 6);
                    Node(x - 10, z);
                    Node(x - 10, z - 16);
                    Node(x + 10, z + 15);
                    c.Deck.Floor(x - 8.5f, z + 11, x + 13, z + 11.25f, 0.079f, PortTone.Yellow);
                    for (int i = -1; i <= 1; i++)
                        c.Deck.Floor(x + i * 9, z + 10, x + i * 9 + 0.25f, z + 11, 0.079f, PortTone.Yellow);
                }
            Gantry(c, -192, -160);
            for (int i = 0; i < 5; i++)
            {
                int x = -287 + i * 48;
                AbiBarrier(c, x, -72, 0);
                Pole(c.Structures, x, -66, 9, 1);
            }
            Arrow(c.Deck, -301, -188, 0, 1, 6, PortTone.Yellow);
            Arrow(c.Deck, -83, -139, 0, -1, 6, PortTone.Yellow);
            c.Bake();
        }

        void Customs()
        {
            var c = Chunk("02 customs administration");
            Pad(c.Deck, -307, 3, -77, 164, PortTone.Concrete);
            Hall(c, -198, 66, 72, 38, 8, new[] { -218f, -178f }, new[] { -198f }, 4.3f, PortTone.Plaster, PortTone.Teal);
            Hall(c, -277, 95, 26, 19, 4.5f, new[] { -277f }, new[] { -277f }, 3.6f, PortTone.Concrete, PortTone.SignalBlue);
            Hall(c, -108, 125, 24, 16, 4.2f, new[] { -108f }, new[] { -108f }, 3.3f, PortTone.Plaster, PortTone.Teal);
            SegmentedWall(c, 66, -233.0f, -163.0f, 2.5f, 3f, 1.4f, new[] { -218f, -198f, -178f }, PortTone.ConcreteLight);
            c.Deck.Floor(-201, 48, -195, 84, 0.055f, PortTone.Teal);
            for (int i = 0; i < 4; i++)
            {
                float x = -230 + i * 19;
                Desk(c, x, 57);
                Desk(c, x + 3, 75);
            }
            c.Block(V(-240, 0.82f, 34), V(15, 1.64f, 2.4f), PortTone.Concrete);
            c.Block(V(-156, 0.82f, 35), V(15, 1.64f, 2.4f), PortTone.Concrete);
            for (int i = 0; i < 5; i++)
            {
                float x = -228 + i * 15;
                c.Structures.Box(V(x, 3.61f, 44.2f), V(0.22f, 0.19f, 6.2f), PortTone.Steel);
                c.Structures.Box(V(x + 2.85f, 3.8f, 44.2f), V(5.7f, 0.22f, 6.2f), PortTone.Teal);
                c.Deck.Floor(x - 0.1f, 18, x + 0.1f, 31, 0.079f, PortTone.Paint);
                c.Deck.Floor(x, 18, x + 14.9f, 18.2f, 0.079f, PortTone.Paint);
            }
            for (int i = 0; i < 3; i++)
            {
                float z = 24 + i * 26;
                c.Block(V(-257, 0.42f, z), V(4.2f, 0.84f, 13), PortTone.ConcreteLight);
                c.Structures.Box(V(-257, 0.85f, z), V(3.8f, 0.18f, 12.6f), PortTone.SandDark);
                Pole(c.Structures, -257, z, 6.5f, 1);
            }
            for (int i = 0; i < 4; i++)
            {
                float x = -253 + i * 29;
                c.Block(V(x, 0.54f, 139), V(7.3f, 1.08f, 2.2f), PortTone.Teal);
                c.Structures.Box(V(x, 1.15f, 139), V(6.85f, 0.14f, 1.8f), PortTone.SandDark);
            }
            Node(-198, 61); Node(-198, 71); Node(-218, 61); Node(-178, 72);
            Arrow(c.Deck, -198, 34, 0, 1, 5, PortTone.Teal);
            c.Bake();
        }

        void Workshop()
        {
            var c = Chunk("03 harbor machinery workshop");
            Pad(c.Deck, 1, -264, 202, -62, PortTone.Concrete);
            Hall(c, 83, -157, 96, 52, 10.8f, new[] { 53f, 83f, 113f }, new[] { 62f, 105f }, 8.2f, PortTone.Slate, PortTone.Ochre, true);
            Hall(c, 155, -216, 24, 20, 4.6f, new[] { 155f }, new[] { 155f }, 3.9f, PortTone.Concrete, PortTone.Rust);
            for (int bay = 0; bay < 3; bay++)
            {
                float x = 53 + bay * 30;
                for (int side = -1; side <= 1; side += 2)
                {
                    float post = x + side * 4.6f;
                    c.Block(V(post, 1.55f, -167), V(0.62f, 3.1f, 0.67f), PortTone.Ochre);
                    c.Structures.Box(V(post, 0.19f, -167), V(1.8f, 0.36f, 1.3f), PortTone.Steel);
                    for (int i = 0; i < 5; i++)
                        c.Deck.Floor(post - 0.2f, -177 + i * 5, post + 0.2f, -173 + i * 5, 0.083f, PortTone.Yellow);
                }
                c.Structures.Box(V(x, 3.18f, -167), V(9.7f, 0.29f, 0.55f), PortTone.Steel);
                Desk(c, x + 4, -141);
                AbiBarrier(c, x + 7, -190, 0);
                Arrow(c.Deck, x, -201, 0, 1, 6, PortTone.Yellow);
                Node(x, -168); Node(x, -151);
            }
            for (int i = 0; i < 3; i++)
            {
                float z = -146 + i * 9.7f;
                var rotation = Quaternion.Euler(0, 0, 90);
                c.Structures.Cylinder(V(167, 2.95f, z), 2.15f, 13, i % 2 == 0 ? PortTone.Zinc : PortTone.Ochre, rotation, 12);
                c.Collider(V(167, 2.55f, z), V(13, 5.1f, 3.85f));
                for (int side = -1; side <= 1; side += 2)
                {
                    c.Structures.Box(V(167 + side * 3.6f, 0.56f, z), V(1.3f, 1.12f, 3.35f), PortTone.Slate);
                    c.Structures.Cylinder(V(167 + side * 4.6f, 2.95f, z), 2.21f, 0.35f, PortTone.Steel, rotation, 12);
                }
            }
            for (int i = 0; i < 4; i++)
            {
                Pallets(c, 22 + i * 15, -110);
                Pole(c.Structures, 22 + i * 28, -240, 9, -1);
            }
            c.Bake();
        }

        void Railhead()
        {
            var c = Chunk("04 rail freight platforms");
            Pad(c.Deck, -346, 206, 175, 340, PortTone.Gravel);
            c.Surface(V(-85.5f, 0.035f, 216.5f), V(521, 0.07f, 21), SurfaceKind.Gravel);
            c.Surface(V(-85.5f, 0.035f, 245), V(521, 0.07f, 10), SurfaceKind.Gravel);
            c.Surface(V(-85.5f, 0.035f, 273), V(521, 0.07f, 26), SurfaceKind.Gravel);
            c.Surface(V(-85.5f, 0.035f, 319), V(521, 0.07f, 40), SurfaceKind.Gravel);
            foreach (float z in new[] { 233f, 253f, 294f })
            {
                c.Surface(V(-85.5f, 0.065f, z), V(521, 0.05f, 2.8f), SurfaceKind.Wood);
                c.Deck.Floor(-344, z - 2.3f, 174, z + 2.3f, 0.053f, PortTone.Joint);
                for (int i = -1; i <= 1; i += 2)
                {
                    c.Deck.Floor(-344, z + i * 0.86f - 0.069f, 174, z + i * 0.86f + 0.069f, 0.084f, PortTone.Zinc);
                    c.Surface(V(-85.5f, 0.09f, z + i * 0.86f), V(521, 0.07f, 0.14f), SurfaceKind.Metal);
                }
                for (int x = -343; x <= 173; x += 3)
                    c.Deck.Floor(x, z - 1.4f, x + 0.25f, z + 1.4f, 0.064f, PortTone.Timber);
            }
            for (int i = 0; i < 4; i++)
            {
                Wagon(c, -195 + i * 32, 253, i % 2 == 0 ? PortTone.Rust : PortTone.Slate, i != 2);
                Wagon(c, -96 + i * 32, 294, i % 2 == 0 ? PortTone.Teal : PortTone.Terra, i != 1);
                if (i < 3) Wagon(c, 53 + i * 33, 233, PortTone.Ochre, i == 1);
                Node(-179 + i * 32, 253);
                Node(-80 + i * 32, 294);
            }
            Hall(c, -195, 313, 34, 18, 5.2f, new[] { -195f }, new[] { -195f }, 4f, PortTone.Plaster, PortTone.Rust);
            Pad(c.Deck, -127, 263, 19, 283, PortTone.ConcreteLight);
            c.Surface(V(-54, 0.085f, 273), V(143, 0.07f, 17), SurfaceKind.Metal);
            c.Deck.Floor(-126, 264, 18, 264.35f, 0.083f, PortTone.Yellow);
            c.Deck.Floor(-126, 281.65f, 18, 282, 0.083f, PortTone.Yellow);
            for (int i = 0; i < 7; i++)
            {
                float x = -120 + 22 * i;
                foreach (float z in new[] { 266f, 280f })
                {
                    c.Block(V(x, 2.8f, z), V(0.46f, 5.6f, 0.46f), PortTone.Steel);
                    c.Structures.Beam(V(x, 4.32f, z), V(x, 5.34f, 273), 0.23f, 0.22f, PortTone.Zinc);
                }
                if (i % 2 == 0) Pallets(c, x + 5.5f, 270);
                Node(x + 5.5f, 276);
            }
            Roof(c.Structures, -54, 273, 143, 17, 5.53f, 1.8f, PortTone.Slate);
            c.Collider(V(-54, 5.62f, 273), V(143, 0.26f, 17));
            for (int i = 0; i < 3; i++)
            {
                float x = -279 + i * 189;
                c.Block(V(x, 2.9f, 239), V(0.26f, 5.8f, 0.26f), PortTone.Slate);
                c.Structures.Box(V(x, 5.72f, 239), V(0.77f, 1.5f, 0.51f), PortTone.Steel);
                c.Structures.Box(V(x, 5.94f, 238.71f), V(0.36f, 0.35f, 0.09f), PortTone.Red);
                c.Structures.Box(V(x, 5.45f, 238.71f), V(0.36f, 0.35f, 0.09f), PortTone.Yellow);
            }
            c.Bake();
        }

        void Quay()
        {
            var c = Chunk("05 north cape quay");
            Pad(c.Deck, 235, -42, 354, 214, PortTone.Concrete);
            c.Surface(V(337, 0.085f, 84), V(22, 0.07f, 240), SurfaceKind.Metal);
            c.Deck.Floor(348, 100, 378, 104, 0.12f, PortTone.Timber);
            c.Surface(V(363, 0.10f, 102), V(30, 0.05f, 4), SurfaceKind.Wood);
            Hall(c, 255, 117, 31, 24, 6.7f, new[] { 255f }, new[] { 255f }, 5.45f, PortTone.DeepBlue, PortTone.SignalBlue);
            Freight(c, 253, -2, 12.2f, 2, PortTone.DeepBlue);
            Freight(c, 270, 9, 12.2f, 1, PortTone.Teal);
            Freight(c, 252, 91, 12.2f, 2, PortTone.Terra);
            Freight(c, 274, 137, 12.2f, 1, PortTone.Slate);
            Freight(c, 253, -28, 12.2f, 1, PortTone.Rust);
            for (int i = 0; i < 7; i++)
            {
                float z = -27 + i * 36;
                c.Structures.Cylinder(V(350.2f, 0.42f, z), 0.52f, 0.84f, PortTone.Steel);
                c.Structures.Cylinder(V(350.2f, 0.87f, z), 0.74f, 0.19f, PortTone.Ochre);
                c.Collider(V(350.2f, 0.46f, z), V(1.05f, 0.92f, 1.05f));
                if (i != 5) Pallets(c, 274, z);
                c.Deck.Floor(344, z, 344.22f, z + 15, 0.081f, PortTone.Yellow);
                Node(339, z);
            }
            for (float z = -37; z < 142; z += 12)
            {
                c.Deck.Floor(287.6f, z, 288.36f, z + 6, 0.082f, PortTone.Joint);
                c.Deck.Floor(326.6f, z, 327.36f, z + 6, 0.082f, PortTone.Joint);
            }
            QuayCrane(c, 19, true);
            QuayCrane(c, 93, false);
            Vessel(c.Structures);
            Node(309, 22); Node(306, 55); Node(308, 100); Node(312, 139); Node(325, 190);
            c.Bake();
        }

        void ExtractPads()
        {
            for (int i = 0; i < Exits.Count; i++)
            {
                var exit = Exits[i];
                var c = Chunk("Extraction pad " + (i + 1));
                float x = exit.Position.x, z = exit.Position.z, radius = exit.Radius;
                for (int slice = 0; slice < 24; slice++)
                {
                    float a = slice * Mathf.PI / 12, b = (slice + 1) * Mathf.PI / 12;
                    Vector3 Radial(float angle, float r, float y) => V(x + Mathf.Sin(angle) * r, y, z + Mathf.Cos(angle) * r);
                    c.Deck.Tri(V(x, 0.087f, z), Radial(a, radius, 0.087f), Radial(b, radius, 0.087f), PortTone.DeepBlue);
                    c.Deck.Quad(Radial(a, radius - 0.62f, 0.094f), Radial(a, radius, 0.094f),
                        Radial(b, radius, 0.094f), Radial(b, radius - 0.62f, 0.094f), slice % 4 == 0 ? PortTone.Paint : PortTone.Emerald);
                }
                Arrow(c.Deck, x, z - 2.9f, 0, 1, 6.4f, PortTone.Emerald, 0.101f);
                for (int side = -1; side <= 1; side += 2)
                {
                    float px = x + side * (radius + 1.1f);
                    c.Block(V(px, 2.75f, z + 3), V(0.65f, 5.5f, 0.65f), PortTone.Steel);
                    c.Structures.Box(V(px, 5.15f, z + 3), V(1.02f, 1.15f, 1.02f), PortTone.Emerald);
                    c.Structures.Box(V(px, 5.79f, z + 3), V(1.24f, 0.21f, 1.24f), PortTone.Slate);
                }
                Node(x, z - radius - 5);
                Node(x, z + radius + 5);
                c.Bake();
            }
        }

        static void Arrow(PortMesh mesh, float x, float z, int dx, int dz, float length, PortTone tone, float height = 0.08f)
        {
            Vector3 f = V(dx, 0, dz).normalized, side = Vector3.Cross(Vector3.up, f);
            Vector3 p = V(x, height, z), h = p + f * (length * 0.64f);
            float width = length * 0.075f;
            mesh.Quad(p - side * width, h - side * width, h + side * width, p + side * width, tone);
            mesh.Tri(h - side * (width * 2.9f), p + f * length, h + side * (width * 2.9f), tone);
        }

        void Hall(PortChunk c, float x, float z, float width, float depth, float eave,
            float[] southDoors, float[] northDoors, float doorWidth, PortTone wall, PortTone trim, bool industrial = false)
        {
            float x0 = x - width * 0.5f, x1 = x + width * 0.5f, z0 = z - depth * 0.5f, z1 = z + depth * 0.5f;
            float clearHeight = industrial ? 5.8f : Mathf.Min(eave - 0.32f, 3.2f);
            SegmentedWall(c, z0, x0, x1, eave, doorWidth, clearHeight, southDoors, wall);
            SegmentedWall(c, z1, x0, x1, eave, doorWidth, clearHeight, northDoors, wall);
            c.Block(V(x0, eave * 0.5f, z), V(0.44f, eave, depth), wall);
            c.Block(V(x1, eave * 0.5f, z), V(0.44f, eave, depth), wall);
            if (industrial)
            {
                Roof(c.Structures, x, z, width + 1.3f, depth + 1.6f, eave, 6.4f, PortTone.Steel);
                c.Structures.Tri(V(x0 - 0.65f, eave, z1 + 0.8f), V(x0 - 0.65f, eave, z0 - 0.8f), V(x0 - 0.65f, eave + 6.4f, z), wall);
                c.Structures.Tri(V(x1 + 0.65f, eave, z0 - 0.8f), V(x1 + 0.65f, eave, z1 + 0.8f), V(x1 + 0.65f, eave + 6.4f, z), wall);
                c.Structures.Box(V(x, eave + 6.45f, z), V(width + 1.1f, 0.46f, 2.4f), PortTone.Zinc);
                c.Collider(V(x, eave + 0.09f, z), V(width + 1.3f, 0.18f, depth + 1.6f));
            }
            else
            {
                c.Block(V(x, eave + 0.18f, z), V(width + 1.5f, 0.36f, depth + 1.5f), PortTone.Slate);
                foreach (int s in new[] { -1, 1 })
                    c.Structures.Box(V(x, eave + 0.65f, z + s * depth * 0.5f), V(width + 0.66f, 0.58f, 0.41f), trim);
                c.Structures.Box(V(x + width * 0.27f, eave + 0.75f, z + 1.8f), V(width * 0.19f, 0.91f, 2.5f), PortTone.Zinc);
            }
            foreach (float face in new[] { z0 - 0.25f, z1 + 0.25f })
            {
                c.Structures.Box(V(x, eave - 0.23f, face), V(width, 0.43f, 0.07f), trim);
                if (eave >= 4.2f)
                {
                    int windows = Mathf.Max(2, Mathf.FloorToInt(width / 5.4f));
                    for (int i = 0; i < windows; i++)
                    {
                        float wx = Mathf.Lerp(x0 + 2.3f, x1 - 2.3f, i / (windows - 1f)), y = eave * 0.76f;
                        c.Structures.Box(V(wx, y, face), V(2.15f, 0.94f, 0.08f), PortTone.Steel);
                        c.Structures.Box(V(wx - 0.51f, y, face + Mathf.Sign(face - z) * 0.061f), V(0.92f, 0.76f, 0.045f), PortTone.Glass);
                        c.Structures.Box(V(wx + 0.51f, y, face + Mathf.Sign(face - z) * 0.061f), V(0.92f, 0.76f, 0.045f), PortTone.SignalBlue);
                    }
                }
            }
            Pad(c.Deck, x0 + 0.24f, z0 + 0.24f, x1 - 0.24f, z1 - 0.24f, PortTone.Gravel);
            DoorDetails(southDoors, z0, -1);
            DoorDetails(northDoors, z1, 1);
            Node(x, z);
            Node(x0 + 3, z); Node(x1 - 3, z);
            Node(x0 - 3, z); Node(x1 + 3, z);

            void DoorDetails(float[] doors, float wallZ, int normal)
            {
                foreach (float door in doors)
                {
                    Node(door, wallZ + normal * 4.2f);
                    Node(door, wallZ - normal * 3.3f);
                    c.Structures.Box(V(door, clearHeight + 0.14f, wallZ + normal * 0.29f), V(doorWidth + 0.54f, 0.27f, 0.25f), trim);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float px = door + side * (doorWidth * 0.5f + 0.19f);
                        c.Structures.Box(V(px, clearHeight * 0.5f, wallZ + normal * 0.27f), V(0.28f, clearHeight, 0.18f), PortTone.Steel);
                        for (int j = 0; j < 4; j++)
                            c.Structures.Box(V(px, 0.34f + j * 0.57f, wallZ + normal * 0.38f), V(0.24f, 0.27f, 0.06f), PortTone.Yellow);
                    }
                }
            }
        }

        static void SegmentedWall(PortChunk c, float z, float from, float to, float height,
            float doorWidth, float head, float[] doors, PortTone tone)
        {
            float cursor = from;
            foreach (float x in doors)
            {
                float a = x - doorWidth * 0.5f, b = x + doorWidth * 0.5f;
                Span(cursor, a, 0, height);
                Span(a, b, head, height);
                cursor = b;
            }
            Span(cursor, to, 0, height);

            void Span(float a, float b, float bottom, float top)
            {
                if (b > a) c.Block(V((a + b) * 0.5f, (bottom + top) * 0.5f, z), V(b - a, top - bottom, 0.46f), tone);
            }
        }

        static void Roof(PortMesh mesh, float x, float z, float width, float depth, float height, float rise, PortTone tone)
        {
            float a = x - width * 0.5f, b = x + width * 0.5f, s = z - depth * 0.5f, n = z + depth * 0.5f;
            for (int i = 0; i < 12; i++)
            {
                float left = Mathf.Lerp(a, b, i / 12f), right = Mathf.Lerp(a, b, (i + 1) / 12f);
                PortTone panel = i % 5 == 0 ? PortTone.Slate : tone;
                mesh.Quad(V(left, height, s), V(left, height + rise, z), V(right, height + rise, z), V(right, height, s), panel);
                mesh.Quad(V(left, height + rise, z), V(left, height, n), V(right, height, n), V(right, height + rise, z), panel);
            }
            mesh.Box(V(x, height - 0.06f, z), V(width, 0.13f, depth), PortTone.Slate);
        }

        static void Freight(PortChunk c, float x, float z, float length, int stack, PortTone tone)
        {
            const float width = 2.52f, height = 2.68f;
            c.Collider(V(x, height * stack * 0.5f, z), V(length, height * stack, width));
            for (int i = 0; i < stack; i++)
            {
                float y = i * height;
                PortTone shell = i == 1 ? PortTone.Slate : i == 2 ? PortTone.Rust : tone;
                c.Structures.Box(V(x, y + height * 0.5f, z), V(length, height, width), shell);
                foreach (int side in new[] { -1, 1 })
                {
                    for (float rx = -length * 0.5f + 0.65f; rx < length * 0.5f - 0.4f; rx += 0.61f)
                        c.Structures.Box(V(x + rx, y + height * 0.5f, z + side * (width * 0.5f + 0.025f)), V(0.135f, height - 0.3f, 0.085f), shell);
                    c.Structures.Box(V(x, y + height - 0.11f, z + side * width * 0.5f), V(length, 0.15f, 0.12f), PortTone.Steel);
                    c.Structures.Box(V(x + side * (length * 0.5f - 0.08f), y + height * 0.5f, z + side * width * 0.5f), V(0.17f, height, 0.16f), PortTone.Steel);
                    c.Structures.Box(V(x + length * 0.5f + 0.047f, y + 1.39f, z + side * 0.58f), V(0.08f, 2.39f, 1.08f), shell);
                    c.Structures.Box(V(x + length * 0.5f + 0.125f, y + 1.39f, z + side * 0.46f), V(0.065f, 2.21f, 0.07f), PortTone.Zinc);
                    c.Structures.Box(V(x + length * 0.5f + 0.085f, y + 1.18f, z + side * 0.63f), V(0.09f, 0.07f, 0.36f), PortTone.Steel);
                }
                c.Structures.Box(V(x - length * 0.3f, y + 1.91f, z - width * 0.5f - 0.08f), V(1.35f, 0.4f, 0.04f), PortTone.Paint);
            }
        }

        static void Pallets(PortChunk c, float x, float z)
        {
            c.Collider(V(x, 0.56f, z), V(2.3f, 1.12f, 1.56f));
            for (int i = -1; i <= 1; i++)
                c.Structures.Box(V(x + i * 1.03f, 0.105f, z), V(0.17f, 0.21f, 1.55f), PortTone.Timber);
            c.Structures.Box(V(x, 0.215f, z), V(2.3f, 0.12f, 1.58f), PortTone.Timber);
            c.Structures.Box(V(x - 0.54f, 0.69f, z + 0.03f), V(1.07f, 0.82f, 1.43f), PortTone.Paper);
            c.Structures.Box(V(x + 0.59f, 0.57f, z), V(1.12f, 0.61f, 1.35f), PortTone.Ochre);
            c.Structures.Box(V(x + 0.61f, 0.91f, z), V(1.06f, 0.15f, 1.33f), PortTone.Paper);
            c.Structures.Box(V(x - 0.55f, 1.108f, z + 0.03f), V(0.11f, 0.019f, 1.43f), PortTone.Paint);
            c.Structures.Box(V(x - 0.5f, 0.83f, z - 0.694f), V(0.33f, 0.21f, 0.018f), PortTone.Paint);
        }

        static void Desk(PortChunk c, float x, float z)
        {
            c.Collider(V(x, 0.56f, z), V(3.6f, 1.12f, 1.45f));
            c.Structures.Box(V(x, 1.01f, z), V(3.6f, 0.18f, 1.45f), PortTone.Timber);
            c.Structures.Box(V(x - 1.34f, 0.46f, z), V(0.62f, 0.92f, 1.23f), PortTone.Slate);
            c.Structures.Box(V(x + 1.35f, 0.46f, z), V(0.68f, 0.92f, 1.23f), PortTone.Slate);
            c.Structures.Box(V(x + 0.68f, 1.31f, z + 0.38f), V(0.73f, 0.42f, 0.22f), PortTone.Steel);
            c.Structures.Box(V(x + 0.68f, 1.33f, z + 0.255f), V(0.59f, 0.32f, 0.025f), PortTone.Glass);
            c.Structures.Box(V(x - 0.3f, 1.11f, z - 0.2f), V(0.55f, 0.027f, 0.34f), PortTone.Paper);
        }

        static void AbiBarrier(PortChunk c, float x, float z, float yaw)
        {
            c.Structures.Box(V(x, 0.16f, z), V(4.3f, 0.32f, 1.3f), PortTone.Concrete, yaw);
            c.Structures.Box(V(x, 0.68f, z), V(4.3f, 0.87f, 0.67f), PortTone.ConcreteLight, yaw);
            c.Collider(V(x, 0.55f, z), V(4.3f, 1.1f, 1.3f));
            for (int i = -2; i <= 2; i++)
                c.Structures.Box(V(x + i * 0.77f, 0.79f, z - 0.35f), V(0.36f, 0.29f, 0.03f), PortTone.Ochre);
        }

        static void Gantry(PortChunk c, float x, float z)
        {
            foreach (int side in new[] { -1, 1 })
            {
                float px = x + side * 26;
                foreach (int end in new[] { -1, 1 })
                {
                    float pz = z + end * 11;
                    c.Block(V(px, 0.9f, pz), V(2.75f, 1.8f, 2.88f), PortTone.Steel);
                    c.Block(V(px, 9.3f, pz), V(1.5f, 17.2f, 1.4f), PortTone.Ochre);
                    c.Structures.Beam(V(px, 8, pz), V(x + side * 17, 17.7f, pz), 0.81f, 0.8f, PortTone.Rust);
                }
                c.Structures.Box(V(px, 13.8f, z), V(1.21f, 1.1f, 23.8f), PortTone.Steel);
            }
            foreach (int end in new[] { -1, 1 })
            {
                float zs = z + end * 11;
                c.Structures.Box(V(x, 18.25f, zs), V(57, 1.24f, 1.4f), PortTone.Ochre);
                c.Structures.Box(V(x, 21.55f, zs), V(57, 0.55f, 0.76f), PortTone.Ochre);
                for (int i = 0; i < 8; i++)
                    c.Structures.Beam(V(x - 27 + i * 6.8f, 18.4f, zs), V(x - 27 + (i + (i % 2 == 0 ? 1 : 0)) * 6.8f, 21.55f, zs), 0.41f, 0.46f, PortTone.Rust);
            }
            c.Structures.Box(V(x + 7.4f, 17.7f, z), V(3.6f, 1.4f, 20.9f), PortTone.Slate);
            for (int side = -1; side <= 1; side += 2)
                c.Structures.Beam(V(x + 7.4f + side * 1.1f, 17.6f, z), V(x + 7.4f + side * 1.1f, 10.6f, z), 0.08f, 0.08f, PortTone.Steel);
            c.Structures.Box(V(x + 7.4f, 10.5f, z), V(4.6f, 0.38f, 1.25f), PortTone.Ochre);
        }

        static void Wagon(PortChunk c, float x, float z, PortTone tone, bool enclosed)
        {
            c.Collider(V(x, enclosed ? 2.15f : 0.95f, z), V(21, enclosed ? 4.3f : 1.9f, 3.35f));
            c.Structures.Box(V(x, 1.04f, z), V(21.4f, 0.59f, 3.4f), PortTone.Steel);
            for (int side = -1; side <= 1; side += 2)
            {
                float px = x + side * 8.1f;
                c.Structures.Box(V(px, 0.57f, z), V(3.45f, 0.58f, 2.44f), PortTone.Steel);
                c.Structures.Box(V(x + side * 11.1f, 0.98f, z), V(1.3f, 0.31f, 0.37f), PortTone.Slate);
                for (int dz = -1; dz <= 1; dz += 2)
                    for (int dx = -1; dx <= 1; dx += 2)
                        c.Structures.Cylinder(V(px + dx * 0.76f, 0.55f, z + dz * 1.11f), 0.52f, 0.26f,
                            PortTone.Steel, Quaternion.Euler(90, 0, 0));
            }
            if (enclosed)
            {
                c.Structures.Box(V(x, 2.76f, z), V(20.8f, 2.89f, 3.23f), tone);
                c.Structures.Box(V(x, 4.24f, z), V(21.15f, 0.27f, 3.55f), PortTone.Slate);
                foreach (int face in new[] { -1, 1 })
                {
                    for (int i = -5; i <= 5; i++)
                        c.Structures.Box(V(x + i * 1.76f, 2.75f, z + face * 1.64f), V(0.17f, 2.71f, 0.11f), PortTone.Slate);
                    c.Structures.Box(V(x, 2.72f, z + face * 1.739f), V(4.64f, 2.46f, 0.16f), tone);
                    c.Structures.Box(V(x + 0.2f, 2.81f, z + face * 1.841f), V(1.2f, 0.27f, 0.06f), PortTone.Paint);
                }
            }
            else
            {
                for (int i = -4; i <= 4; i++)
                    c.Structures.Box(V(x + i * 2.23f, 1.52f, z), V(0.34f, 0.39f, 3.65f), PortTone.Ochre);
            }
        }

        static void QuayCrane(PortChunk c, float z, bool longBoom)
        {
            var mesh = c.Structures;
            float tip = longBoom ? 420 : 401;
            foreach (int side in new[] { -1, 1 })
            {
                float row = z + side * 8.2f;
                for (int i = 0; i < 2; i++)
                {
                    float x = 288 + i * 39;
                    c.Block(V(x, 0.78f, row), V(4.6f, 1.56f, 3.1f), PortTone.Steel);
                    c.Block(V(x, 11.9f, row), V(1.8f, 22.7f, 1.8f), PortTone.Ochre);
                    mesh.Beam(V(x, 13.4f, row), V(x + (i == 0 ? 8 : -8), 23.8f, row), 0.87f, 0.82f, PortTone.Rust);
                }
                mesh.Box(V(307.5f, 23.2f, row), V(40.4f, 1.31f, 1.65f), PortTone.Ochre);
            }
            mesh.Box(V(322, 25.1f, z), V(8.8f, 2.97f, 8.7f), PortTone.Slate);
            mesh.Box(V(326.3f, 25.42f, z), V(0.24f, 1.24f, 5.88f), PortTone.Glass);
            mesh.Beam(V(319, 24.8f, z), V(311, 47, z), 1.5f, 1.45f, PortTone.Ochre);
            mesh.Beam(V(311, 47, z), V(tip, 33.4f, z), 0.22f, 0.25f, PortTone.Slate);
            mesh.Beam(V(311, 47, z), V(282, 31, z), 0.26f, 0.25f, PortTone.Slate);
            mesh.Beam(V(282, 31, z), V(tip, 33.4f, z), 1.2f, 3.8f, PortTone.Ochre);
            for (int beam = 0; beam < 11; beam++)
            {
                float left = Mathf.Lerp(284, tip - 1, beam / 11f), right = Mathf.Lerp(284, tip - 1, (beam + 1) / 11f);
                float y = 31 + (left - 282) * 2.4f / (tip - 282);
                mesh.Beam(V(left, y, z), V(right, y - 2.3f, z), 0.26f, 0.36f, PortTone.Rust);
            }
            mesh.Beam(V(282, 28.7f, z), V(tip, 31.1f, z), 0.49f, 1.05f, PortTone.Slate);
            mesh.Box(V(tip - 13, 31, z), V(2.5f, 1.11f, 4.43f), PortTone.Slate);
            mesh.Beam(V(tip - 13, 30.5f, z), V(tip - 13, 13, z), 0.11f, 0.11f, PortTone.Slate);
            mesh.Box(V(tip - 13, 12.84f, z), V(4.4f, 0.44f, 1.34f), PortTone.Ochre);
        }

        static void Vessel(PortMesh mesh)
        {
            Vector3[] deck = { V(404, 2.9f, 113), V(415, 2.9f, 105), V(417, 2.9f, 21), V(411, 2.9f, 14),
                V(392, 2.9f, 14), V(388, 2.9f, 22), V(389, 2.9f, 98), V(396, 2.9f, 108) };
            var center = V(403, 2.9f, 58);
            for (int i = 0; i < deck.Length; i++)
            {
                Vector3 a = deck[i], b = deck[(i + 1) % deck.Length];
                Vector3 aa = Vector3.Lerp(a, center, 0.12f), bb = Vector3.Lerp(b, center, 0.12f);
                aa.y = bb.y = -0.25f;
                mesh.Quad(aa, a, b, bb, PortTone.DeepBlue);
                mesh.Quad(aa - V(0, 2.2f, 0), aa, bb, bb - V(0, 2.2f, 0), PortTone.Rust);
                mesh.Tri(center, a, b, PortTone.Slate);
                mesh.Beam(a + V(0, 0.34f, 0), b + V(0, 0.34f, 0), 0.26f, 0.26f, PortTone.Zinc);
            }
            mesh.Box(V(403, 4.6f, 31), V(19.8f, 3.4f, 16), PortTone.Plaster);
            mesh.Box(V(403, 8.1f, 32), V(17.3f, 3.3f, 11), PortTone.Plaster);
            mesh.Box(V(403, 9.79f, 32), V(18.3f, 0.38f, 12.2f), PortTone.DeepBlue);
            for (int i = -3; i <= 3; i++)
                mesh.Box(V(403 + i * 2.25f, 8.47f, 37.57f), V(1.9f, 0.91f, 0.16f), PortTone.Glass);
            mesh.Cylinder(V(406, 11.1f, 26), 1.18f, 2.65f, PortTone.Rust);
            mesh.Cylinder(V(406, 12.53f, 26), 1.23f, 0.29f, PortTone.Steel);
            for (int i = 0; i < 3; i++)
            {
                mesh.Box(V(399, 4.23f, 53 + i * 16), V(7.1f, 2.66f, 10.5f), PortTone.Teal);
                mesh.Box(V(408, 4.23f, 53 + i * 16), V(7.35f, 2.66f, 10.5f), PortTone.Terra);
            }
            mesh.Beam(V(390, 2.72f, 31), V(355.4f, 0.63f, 17), 0.12f, 0.12f, PortTone.Steel);
            mesh.Beam(V(390, 2.72f, 96), V(355.4f, 0.63f, 102), 0.12f, 0.12f, PortTone.Steel);
        }
    }
}
