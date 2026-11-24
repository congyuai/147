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

        internal PortScenery(Transform root, PortMaterials materials, Action<UnityEngine.Object> keep, List<Vector3> nodes)
        { this.root = root; this.materials = materials; this.keep = keep; this.nodes = nodes; }

        internal void Build()
        {
            new PortLandscape(root, materials, keep, nodes).Build();
            Gatehouse();
            Yard();
            Customs();
            Workshop();
            Railhead();
            Quay();
            ExtractPads();
            for (float z = -441; z <= 449; z += 39)
                for (float x = -444; x < 348; x += 39) Node(x, z);
            nodes.Add(Spawn);
            nodes.AddRange(BotPosts);
            nodes.AddRange(GuardPosts);
            foreach (var exit in Exits) nodes.Add(exit.Position);
        }

        PortChunk Chunk(string name, float x, float z) => new PortChunk(root, name, materials, keep, PortTerrain.Ground(x, z));
        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);
        void Node(float x, float z) => nodes.Add(At(x, z));

        void Gatehouse()
        {
            var c = Chunk("Southwest service entrance", -350, -337);
            Hall(c, -350, -337, 13, 10, 3.6f, new[] { -351f }, new[] { -350f }, 2.2f, PortTone.Plaster, PortTone.Teal);
            Desk(c, -354, -337);
            Barrier(c, -382, -317);
            Barrier(c, -348, -315);
            Pole(c.Structures, -375, -318, 6.4f, 1);
            c.Block(V(-367.5f, 3.4f, -314), V(.5f, 6.8f, .55f), PortTone.Steel);
            c.Block(V(-347.5f, 3.4f, -314), V(.5f, 6.8f, .55f), PortTone.Steel);
            c.Structures.Box(V(-357.5f, 6.75f, -314), V(21, 1.25f, .58f), PortTone.DeepBlue);
            Arrow(c.Deck, -357, -322, 0, 1, 4, PortTone.Yellow);
            Node(-357, -321); Node(-361, -300);
            c.Bake();
        }

        void Yard()
        {
            var c = Chunk("01 container yard", -192, -160);
            Hall(c, -292, -235, 14, 10, 3.8f, new[] { -291f }, new[] { -291f }, 2.5f, PortTone.Plaster, PortTone.Teal);
            float[] rows = { -214, -161, -106 };
            float[] bays = { -269, -232, -192, -149, -106 };
            PortTone[] freight = { PortTone.Rust, PortTone.Slate, PortTone.Teal, PortTone.Terra, PortTone.Ochre };
            for (int r = 0; r < rows.Length; r++)
                for (int b = 0; b < bays.Length; b++)
                {
                    float x = bays[b], z = rows[r];
                    Freight(c, x, z - 6, 1 + (r + b) % 3, freight[(b + r) % freight.Length]);
                    if ((b + r) % 3 != 1) Freight(c, x + 5.5f, z + 5.6f, 1 + (b + r) % 2, freight[(b + r + 2) % freight.Length]);
                    if (r == 0 || r == 2) Pallets(c, x + 12, z - 6);
                    Node(x - 10, z); Node(x - 10, z - 16); Node(x + 10, z + 15);
                    c.Deck.Floor(x - 8, z + 11, x + 13, z + 11.38f, .038f, PortTone.Yellow);
                }
            Gantry(c, -192, -160);
            for (int i = 0; i < 5; i++)
            {
                int x = -287 + i * 48;
                Barrier(c, x, -72);
                Pole(c.Structures, x, -66, 9, 1, PortTerrain.Ground(x, -66) - c.Root.transform.localPosition.y);
            }
            Arrow(c.Deck, -301, -188, 0, 1, 6, PortTone.Yellow);
            Arrow(c.Deck, -83, -139, 0, -1, 6, PortTone.Yellow);
            c.Bake();
        }

        void Customs()
        {
            var c = Chunk("02 customs administration", -198, 66);
            Hall(c, -198, 66, 72, 38, 8, new[] { -218f, -178f }, new[] { -198f }, 4.3f, PortTone.Plaster, PortTone.Teal);
            Hall(c, -277, 95, 26, 19, 4.5f, new[] { -277f }, new[] { -277f }, 3.6f, PortTone.Concrete, PortTone.SignalBlue);
            Hall(c, -108, 125, 24, 16, 4.2f, new[] { -108f }, new[] { -108f }, 3.3f, PortTone.Plaster, PortTone.Teal);
            Wall(c, 66, -233, -163, 2.9f, 1.65f, 2.45f, new[] { -218f, -198f, -178f }, PortTone.ConcreteLight, false);
            for (int i = 0; i < 4; i++)
            {
                float x = -230 + i * 19;
                Desk(c, x, 57);
                Desk(c, x + 3, 75);
            }
            c.Block(V(-240, .82f, 34), V(15, 1.64f, 2.4f), PortTone.Concrete);
            c.Block(V(-156, .82f, 35), V(15, 1.64f, 2.4f), PortTone.Concrete);
            for (int i = 0; i < 4; i++)
            {
                float x = -253 + i * 29;
                c.Block(V(x, .54f, 139), V(7.3f, 1.08f, 2.2f), PortTone.Teal);
                c.Structures.Box(V(x, 1.17f, 139), V(6.85f, .17f, 1.8f), PortTone.SandDark);
                c.Structures.Cylinder(V(x, 2.01f, 139), .14f, 1.56f, PortTone.Bark, 6);
                c.Structures.Cylinder(V(x, 3.1f, 139), 1.14f, 1.64f, PortTone.Leaves, 7);
            }
            Node(-198, 61); Node(-198, 71); Node(-218, 61); Node(-178, 72);
            Arrow(c.Deck, -198, 34, 0, 1, 5, PortTone.Teal);
            c.Bake();
        }

        void Workshop()
        {
            var c = Chunk("03 harbor machinery workshop", 83, -157);
            Hall(c, 83, -157, 96, 52, 10.8f, new[] { 53f, 83f, 113f }, new[] { 62f, 105f }, 7.6f, PortTone.Slate, PortTone.Ochre, true);
            Hall(c, 155, -216, 24, 20, 4.6f, new[] { 155f }, new[] { 155f }, 3.9f, PortTone.Concrete, PortTone.Rust);
            for (int bay = 0; bay < 3; bay++)
            {
                float x = 53 + bay * 30;
                foreach (int sign in new[] { -1, 1 })
                {
                    float post = x + sign * 4.6f;
                    c.Block(V(post, 1.55f, -167), V(.62f, 3.1f, .67f), PortTone.Ochre);
                    c.Structures.Box(V(post, .19f, -167), V(1.8f, .36f, 1.3f), PortTone.Steel);
                    c.Deck.Floor(post - .25f, -175, post + .25f, -164, .041f, PortTone.Yellow);
                }
                c.Structures.Box(V(x, 3.18f, -167), V(9.7f, .29f, .55f), PortTone.Steel);
                Desk(c, x + 4, -141);
                Barrier(c, x + 7, -190);
                Arrow(c.Deck, x, -201, 0, 1, 6, PortTone.Yellow);
                Node(x, -168); Node(x, -151);
            }
            for (int i = 0; i < 3; i++)
            {
                float z = -146 + i * 9.7f;
                var rotation = Quaternion.Euler(0, 0, 90);
                c.Structures.Cylinder(V(167, 2.95f, z), 2.15f, 13, i % 2 == 0 ? PortTone.Zinc : PortTone.Ochre, rotation, 12);
                c.Collider(V(167, 2.55f, z), V(13, 5.1f, 3.85f));
                c.Block(V(163.4f, .56f, z), V(1.3f, 1.12f, 3.35f), PortTone.Slate);
                c.Block(V(170.6f, .56f, z), V(1.3f, 1.12f, 3.35f), PortTone.Slate);
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
            var c = Chunk("04 rail freight platforms", -54, 273);
            foreach (float z in new[] { 233f, 253f, 294f })
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 center = V(-85, .034f, z + side * .86f), size = V(518, .068f, .18f);
                    c.Structures.Box(center, size, PortTone.Steel);
                    c.Surface(center, size, SurfaceKind.Metal);
                }
            }
            for (int i = 0; i < 4; i++)
            {
                Wagon(c, -195 + i * 32, 253, i % 2 == 0 ? PortTone.Rust : PortTone.Slate, i != 2);
                Wagon(c, -96 + i * 32, 294, i % 2 == 0 ? PortTone.Teal : PortTone.Terra, i != 1);
                if (i < 3) Wagon(c, 53 + i * 33, 233, PortTone.Ochre, i == 1);
                Node(-179 + i * 32, 253); Node(-80 + i * 32, 294);
            }
            Hall(c, -195, 313, 34, 18, 5.2f, new[] { -195f }, new[] { -195f }, 4, PortTone.Plaster, PortTone.Rust);
            c.Deck.Floor(-126, 264, 18, 264.38f, .049f, PortTone.Yellow);
            c.Deck.Floor(-126, 281.62f, 18, 282, .049f, PortTone.Yellow);
            for (int i = 0; i < 7; i++)
            {
                float x = -120 + 22 * i;
                foreach (float z in new[] { 266f, 280f })
                {
                    c.Block(V(x, 2.8f, z), V(.46f, 5.6f, .46f), PortTone.Steel);
                    c.Structures.Beam(V(x, 4.32f, z), V(x, 5.34f, 273), .23f, .22f, PortTone.Zinc);
                }
                if (i % 2 == 0) Pallets(c, x + 5.5f, 270);
                Node(x + 5.5f, 276);
            }
            Roof(c.Structures, -54, 273, 143, 17, 5.53f, 1.8f, PortTone.Slate);
            c.Collider(V(-54, 5.55f, 273), V(143, .26f, 17));
            for (int i = 0; i < 3; i++)
            {
                float x = -279 + i * 189;
                c.Block(V(x, 2.9f, 239), V(.26f, 5.8f, .26f), PortTone.Slate);
                c.Structures.Box(V(x, 5.72f, 239), V(.77f, 1.5f, .51f), PortTone.Steel);
                c.Structures.Box(V(x, 5.94f, 238.71f), V(.36f, .35f, .09f), PortTone.Red);
                c.Structures.Box(V(x, 5.45f, 238.71f), V(.36f, .35f, .09f), PortTone.Yellow);
            }
            c.Bake();
        }

        void Quay()
        {
            var c = Chunk("05 north cape quay", 267, 63);
            Hall(c, 255, 117, 31, 24, 6.7f, new[] { 255f }, new[] { 255f }, 4.6f, PortTone.DeepBlue, PortTone.Ochre, secure: true);
            Wall(c, 119, 239.72f, 249.9f, 3.15f, 1.55f, 2.35f, new[] { 245.2f }, PortTone.ConcreteLight, false);
            c.Block(V(249.9f, 1.575f, 123.8f), V(.27f, 3.15f, 9.6f), PortTone.ConcreteLight);
            Desk(c, 243.1f, 124.7f);
            c.Block(V(265.2f, .72f, 118.4f), V(6.6f, 1.44f, 1.55f), PortTone.Steel);
            c.Structures.Box(V(265.2f, 1.49f, 118.4f), V(6.65f, .095f, 1.6f), PortTone.Timber);
            c.Structures.Box(V(265.3f, 1.77f, 118.72f), V(.75f, .44f, .38f), PortTone.Ochre);
            Desk(c, 262.8f, 109.6f);
            StationMark(c);
            Freight(c, 253, -2, 2, PortTone.DeepBlue);
            Freight(c, 270, 9, 1, PortTone.Teal);
            Freight(c, 252, 91, 2, PortTone.Terra);
            Freight(c, 274, 137, 1, PortTone.Slate);
            Freight(c, 253, -28, 1, PortTone.Rust);
            for (int i = 0; i < 7; i++)
            {
                float z = -27 + i * 36;
                c.Structures.Cylinder(V(350.2f, .42f, z), .52f, .84f, PortTone.Steel);
                c.Structures.Cylinder(V(350.2f, .87f, z), .74f, .19f, PortTone.Ochre);
                c.Collider(V(350.2f, .46f, z), V(1.05f, .92f, 1.05f));
                if (i != 5) Pallets(c, 274, z);
                c.Deck.Floor(343.75f, z, 344.25f, z + 15, .043f, PortTone.Yellow);
                Node(339, z);
            }
            QuayCrane(c, 19, true);
            QuayCrane(c, 93, false);
            Node(309, 22); Node(306, 55); Node(308, 100); Node(312, 139); Node(325, 190);
            Node(243, 116); Node(245.2f, 121); Node(246, 126); Node(260, 116); Node(262, 125);
            c.Bake();
            var vessel = new PortMesh();
            var hull = Vessel(vessel);
            keep(hull);
            var shell = PortMesh.Child(root, "Pilot vessel hull");
            shell.isStatic = true;
            shell.AddComponent<MeshCollider>().sharedMesh = hull;
            vessel.Bake(root, "North Cape pilot vessel", materials.Structure, keep, true);
        }

        void StationMark(PortChunk c)
        {
            for (int sign = -1; sign <= 1; sign += 2)
            {
                float z = 117 + sign * 12.40f;
                c.Structures.Box(V(255, 5.85f, z), V(7.7f, 1.12f, .29f), PortTone.Ochre);
                c.Structures.Box(V(255, 5.85f, z + sign * .16f), V(6.95f, .86f, .074f), PortTone.DeepBlue);
                const string code = "C07";
                for (int i = 0; i < code.Length; i++)
                    Stencil(c.Structures, code[i], V(253.75f - sign * i * 1.24f + (sign > 0 ? 2.48f : 0), 5.86f,
                        z + sign * .21f));
            }
            for (int i = 0; i < 5; i++)
            {
                float x = 247 + 3.2f * i;
                c.Deck.Floor(x, 101, x + 1.56f, 102.7f, .047f, PortTone.Ochre);
            }
            Arrow(c.Deck, 266, 99.5f, 0, 1, 4.5f, PortTone.Yellow);
            var markers = new PortMesh();
            foreach (float x in new[] { 252f, 258f })
                markers.Box(V(x, 5.86f, 104.37f), V(.34f, .23f, .18f), PortTone.Amber);
            markers.Bake(c.Root.transform, "C07 inspection beacons", materials.Lamp, keep, false);
        }

        static void Stencil(PortMesh mesh, char code, Vector3 p)
        {
            void Bar(float x, float y, float w, float h) => mesh.Box(p + V(x, y, 0), V(w, h, .024f), PortTone.Paint);
            Bar(0, .29f, .74f, .12f);
            if (code != '7') { Bar(0, -.29f, .74f, .12f); Bar(-.31f, 0, .12f, .46f); }
            if (code != 'C') Bar(.31f, 0, .12f, .46f);
        }

        void ExtractPads()
        {
            for (int i = 0; i < Exits.Count; i++)
            {
                var exit = Exits[i];
                float x = exit.Position.x, z = exit.Position.z, radius = exit.Radius;
                var c = Chunk("Extraction pad " + (i + 1), x, z);
                var lamps = new PortMesh();
                for (int side = -1; side <= 1; side += 2)
                {
                    float px = x + side * (radius + 1.1f);
                    c.Block(V(px, 2.75f, z + 3), V(.65f, 5.5f, .65f), PortTone.Steel);
                    c.Structures.Box(V(px, 5.15f, z + 3), V(1.02f, 1.15f, 1.02f), PortTone.Emerald);
                    lamps.Box(V(px, 5.79f, z + 3), V(.73f, .21f, .73f), PortTone.Amber);
                }
                lamps.Bake(c.Root.transform, "Exit beacons", materials.Lamp, keep, false);
                Node(x, z - radius - 5); Node(x, z + radius + 5);
                c.Bake();
            }
        }

        void Hall(PortChunk c, float x, float z, float width, float depth, float eave,
            float[] south, float[] north, float doorWidth, PortTone wall, PortTone trim, bool industrial = false, bool secure = false)
        {
            float x0 = x - width * .5f, x1 = x + width * .5f, z0 = z - depth * .5f, z1 = z + depth * .5f;
            float clear = industrial ? 5.8f : Mathf.Min(eave - .32f, 3.2f);
            Wall(c, z0, x0, x1, eave, doorWidth, clear, south, wall);
            Wall(c, z1, x0, x1, eave, doorWidth, clear, north, wall);
            c.Block(V(x0, eave * .5f - .11f, z), V(.44f, eave + .22f, depth - .46f), wall);
            c.Block(V(x1, eave * .5f - .11f, z), V(.44f, eave + .22f, depth - .46f), wall);
            if (industrial) Roof(c.Structures, x, z, width + 1.2f, depth + 1.6f, eave, 6.4f, PortTone.Steel);
            c.Block(V(x, eave + (industrial ? -.15f : .14f), z), V(width + 1.5f, .25f, depth + 1.5f), PortTone.Slate);
            if (!industrial)
            {
                c.Structures.Box(V(x, eave + .52f, z0), V(width + .66f, .43f, .4f), trim);
                c.Structures.Box(V(x, eave + .52f, z1), V(width + .66f, .43f, .4f), trim);
                c.Structures.Box(V(x + width * .27f, eave + .68f, z + 1.8f), V(width * .19f, .85f, 2.5f), PortTone.Zinc);
            }
            var lights = new PortMesh();
            for (int side = -1; side <= 1; side += 2)
            {
                float face = side < 0 ? z0 : z1;
                float[] entrances = side < 0 ? south : north;
                int windows = Mathf.Max(2, Mathf.FloorToInt(width / (industrial ? 7 : 5.4f)));
                for (int i = 0; i < windows; i++)
                {
                    float px = Mathf.Lerp(x0 + 2, x1 - 2, i / (windows - 1f));
                    float wy = eave >= 5 ? eave * .73f : 1.79f;
                    bool doorway = false;
                    foreach (float dx in entrances)
                        if (Mathf.Abs(px - dx) < doorWidth * .5f + 1.48f && wy < clear + .9f) { doorway = true; break; }
                    if (doorway) continue;
                    float f = face + side * .234f;
                    c.Structures.Box(V(px, wy, f), V(2.0f, 1.18f, .158f), PortTone.Steel);
                    c.Structures.Box(V(px - .46f, wy, f + side * .091f), V(.81f, .87f, .046f), PortTone.Glass);
                    c.Structures.Box(V(px + .46f, wy, f + side * .091f), V(.81f, .87f, .046f), i % 3 == 0 ? PortTone.SignalBlue : PortTone.Glass);
                    c.Structures.Box(V(px, wy - .61f, f + side * .1f), V(2.12f, .12f, .29f), PortTone.Zinc);
                }
                for (int i = 0; i < entrances.Length; i++)
                {
                    float dx = entrances[i];
                    bool initiallyOpen = !secure ? side < 0 || i != 0 : side > 0;
                    Shutter(c, dx, face, doorWidth, clear, initiallyOpen, trim);
                    Node(dx, face + side * 3.2f); Node(dx, face - side * 3.2f);
                    float ly = Mathf.Min(eave - .31f, clear + .64f);
                    float lx = eave - clear < .66f ? dx + doorWidth * .5f + .64f : dx;
                    c.Structures.Box(V(lx, ly + .16f, face + side * .38f), V(.71f, .18f, .53f), PortTone.Steel);
                    lights.Box(V(lx, ly, face + side * .46f), V(.49f, .14f, .27f), PortTone.Amber);
                }
            }
            lights.Bake(c.Root.transform, "Door luminaires " + x, materials.Lamp, keep, false);
            Node(x, z); Node(x0 + 3, z); Node(x1 - 3, z); Node(x0 - 3, z); Node(x1 + 3, z);
            Node(x0 - 2.1f, z0 - 2.1f); Node(x1 + 2.1f, z0 - 2.1f);
            Node(x0 - 2.1f, z1 + 2.1f); Node(x1 + 2.1f, z1 + 2.1f);
        }

        void Shutter(PortChunk c, float x, float z, float width, float height, bool open, PortTone trim)
        {
            var holder = PortMesh.Child(c.Root.transform, "Harbor shutter " + x + " / " + z);
            holder.transform.localPosition = V(x, 0, z);
            var door = holder.AddComponent<HarborDoor>();
            var closed = PortMesh.Child(holder.transform, "Closed shutter leaf");
            var collider = closed.AddComponent<BoxCollider>();
            collider.center = V(0, height * .5f + .012f, 0);
            collider.size = V(width - .014f, height - .02f, .20f);
            var panel = new PortMesh();
            panel.Box(collider.center, collider.size, PortTone.Slate);
            for (int i = 1; i < Mathf.FloorToInt(height / .39f); i++)
            {
                float y = .39f * i;
                panel.Box(V(0, y, -.103f), V(width - .052f, .046f, .024f), PortTone.Steel);
                panel.Box(V(0, y, .103f), V(width - .052f, .046f, .024f), PortTone.Steel);
            }
            panel.Box(V(0, .91f, 0), V(.42f, .15f, .31f), PortTone.Ochre);
            panel.Bake(closed.transform, "Galvanized shutters", materials.Structure, keep, true);
            var rolled = PortMesh.Child(holder.transform, "Raised shutter leaf");
            var roll = new PortMesh();
            roll.Box(V(0, height + .065f, 0), V(width, .25f, .31f), trim);
            roll.Bake(rolled.transform, "Shutter drum", materials.Structure, keep, true);
            var frame = new PortMesh();
            for (int side = -1; side <= 1; side += 2)
            {
                float edge = side * (width * .5f + .11f);
                frame.Box(V(edge, height * .5f, 0), V(.22f, height, .59f), PortTone.Steel);
                frame.Box(V(edge, .37f, 0), V(.26f, .51f, .64f), PortTone.Ochre);
                float buttonX = side * (width * .5f + .57f);
                Vector3 button = V(buttonX, 1.42f, side * .297f);
                frame.Box(button, V(.28f, .37f, .14f), PortTone.Steel);
                frame.Box(button + V(0, .041f, side * .084f), V(.14f, .12f, .048f), PortTone.Emerald);
                var control = holder.AddComponent<BoxCollider>();
                control.center = button;
                control.size = V(.40f, .50f, .24f);
                control.isTrigger = true;
            }
            frame.Bake(holder.transform, "Jambs and shutter controls", materials.Structure, keep, true);
            door.Initialize(collider, rolled, open);
        }

        static void Wall(PortChunk c, float z, float from, float to, float height,
            float doorWidth, float head, float[] doors, PortTone tone, bool foundation = true)
        {
            float cursor = from;
            foreach (float x in doors)
            {
                float a = x - doorWidth * .5f, b = x + doorWidth * .5f;
                Span(cursor, a, foundation ? -.25f : 0, height);
                Span(a, b, head, height);
                cursor = b;
            }
            Span(cursor, to, foundation ? -.25f : 0, height);
            void Span(float a, float b, float bottom, float top)
            {
                if (b > a) c.Block(V((a + b) * .5f, (bottom + top) * .5f, z), V(b - a, top - bottom, .46f), tone);
            }
        }

        static void Roof(PortMesh mesh, float x, float z, float width, float depth, float height, float rise, PortTone tone)
        {
            float a = x - width * .5f, b = x + width * .5f, s = z - depth * .5f, n = z + depth * .5f;
            for (int i = 0; i < 10; i++)
            {
                float left = Mathf.Lerp(a, b, i / 10f), right = Mathf.Lerp(a, b, (i + 1) / 10f);
                PortTone panel = i % 5 == 0 ? PortTone.Slate : tone;
                mesh.Quad(V(left, height, s), V(left, height + rise, z), V(right, height + rise, z), V(right, height, s), panel);
                mesh.Quad(V(left, height + rise, z), V(left, height, n), V(right, height, n), V(right, height + rise, z), panel);
            }
            mesh.Tri(V(a, height, s), V(a, height, n), V(a, height + rise, z), PortTone.Slate);
            mesh.Tri(V(b, height, n), V(b, height, s), V(b, height + rise, z), PortTone.Slate);
            mesh.Box(V(x, height + rise, z), V(width + .06f, .16f, .68f), PortTone.Zinc);
        }

        static void Arrow(PortMesh mesh, float x, float z, int dx, int dz, float length, PortTone tone, float height = .047f)
        {
            Vector3 f = V(dx, 0, dz).normalized, side = Vector3.Cross(Vector3.up, f);
            Vector3 p = V(x, height, z), h = p + f * (length * .64f);
            float width = length * .075f;
            mesh.Quad(p - side * width, h - side * width, h + side * width, p + side * width, tone);
            mesh.Tri(h - side * (width * 2.9f), p + f * length, h + side * (width * 2.9f), tone);
        }

        internal static void Pole(PortMesh mesh, float x, float z, float height, int arm, float y = 0)
        {
            mesh.Box(V(x, y + .2f, z), V(.65f, .4f, .65f), PortTone.ConcreteLight);
            mesh.Cylinder(V(x, y + height * .5f, z), .126f, height, PortTone.Slate, 6);
            mesh.Beam(V(x, y + height - .66f, z), V(x + arm * 1.55f, y + height + .16f, z), .15f, .19f, PortTone.Slate);
            mesh.Box(V(x + arm * 1.62f, y + height + .16f, z), V(.73f, .17f, .43f), PortTone.Amber);
        }

        static void Freight(PortChunk c, float x, float z, int stack, PortTone tone)
        {
            const float width = 2.52f, height = 2.68f, length = 12.2f;
            c.Collider(V(x, height * stack * .5f, z), V(length, height * stack, width));
            for (int i = 0; i < stack; i++)
            {
                float y = i * height;
                PortTone shell = i == 1 ? PortTone.Slate : i == 2 ? PortTone.Rust : tone;
                c.Structures.Box(V(x, y + height * .5f, z), V(length, height, width), shell);
                for (int side = -1; side <= 1; side += 2)
                {
                    for (float rx = -5.35f; rx <= 5.4f; rx += 1.06f)
                        c.Structures.Box(V(x + rx, y + 1.33f, z + side * 1.28f), V(.25f, 2.24f, .085f), shell);
                    c.Structures.Box(V(x, y + height - .11f, z + side * 1.267f), V(length, .15f, .13f), PortTone.Steel);
                    c.Structures.Box(V(x + 6.147f, y + 1.39f, z + side * .58f), V(.08f, 2.39f, 1.08f), shell);
                    c.Structures.Box(V(x + 6.215f, y + 1.39f, z + side * .46f), V(.066f, 2.21f, .077f), PortTone.Zinc);
                }
                c.Structures.Box(V(x - 3.7f, y + 1.93f, z - 1.34f), V(1.52f, .42f, .037f), PortTone.Paint);
            }
        }

        static void Pallets(PortChunk c, float x, float z)
        {
            c.Collider(V(x, .56f, z), V(2.3f, 1.12f, 1.56f));
            for (int i = -1; i <= 1; i++) c.Structures.Box(V(x + i * 1.03f, .105f, z), V(.17f, .21f, 1.55f), PortTone.Timber);
            c.Structures.Box(V(x, .215f, z), V(2.3f, .12f, 1.58f), PortTone.Timber);
            c.Structures.Box(V(x - .54f, .68f, z + .03f), V(1.07f, .82f, 1.43f), PortTone.Paper);
            c.Structures.Box(V(x + .59f, .60f, z), V(1.12f, .66f, 1.35f), PortTone.Ochre);
            c.Structures.Box(V(x - .55f, 1.097f, z + .03f), V(.13f, .017f, 1.42f), PortTone.Paint);
        }

        static void Desk(PortChunk c, float x, float z)
        {
            c.Collider(V(x, .56f, z), V(3.6f, 1.12f, 1.45f));
            c.Structures.Box(V(x, 1.01f, z), V(3.6f, .18f, 1.45f), PortTone.Timber);
            c.Structures.Box(V(x - 1.34f, .46f, z), V(.62f, .92f, 1.23f), PortTone.Slate);
            c.Structures.Box(V(x + 1.35f, .46f, z), V(.68f, .92f, 1.23f), PortTone.Slate);
            c.Structures.Box(V(x + .68f, 1.31f, z + .38f), V(.73f, .42f, .22f), PortTone.Steel);
            c.Structures.Box(V(x + .68f, 1.33f, z + .255f), V(.59f, .32f, .025f), PortTone.Glass);
            c.Structures.Box(V(x - .3f, 1.11f, z - .2f), V(.55f, .027f, .34f), PortTone.Paper);
        }

        static void Barrier(PortChunk c, float x, float z)
        {
            c.Structures.Box(V(x, .16f, z), V(4.3f, .32f, 1.3f), PortTone.Concrete);
            c.Structures.Box(V(x, .68f, z), V(4.3f, .87f, .67f), PortTone.ConcreteLight);
            c.Collider(V(x, .55f, z), V(4.3f, 1.1f, 1.3f));
            for (int i = -2; i <= 2; i++) c.Structures.Box(V(x + i * .77f, .79f, z - .35f), V(.36f, .29f, .03f), PortTone.Ochre);
        }

        static void Gantry(PortChunk c, float x, float z)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                float px = x + side * 26;
                for (int end = -1; end <= 1; end += 2)
                {
                    float pz = z + end * 11;
                    c.Block(V(px, .9f, pz), V(2.75f, 1.8f, 2.88f), PortTone.Steel);
                    c.Block(V(px, 9.8f, pz), V(1.5f, 16.0f, 1.4f), PortTone.Ochre);
                    c.Structures.Beam(V(px, 8, pz), V(x + side * 17, 17.7f, pz), .81f, .8f, PortTone.Rust);
                }
                c.Structures.Box(V(px, 13.8f, z), V(1.21f, 1.1f, 23.8f), PortTone.Steel);
            }
            for (int end = -1; end <= 1; end += 2)
            {
                float zs = z + end * 11;
                c.Structures.Box(V(x, 18.25f, zs), V(57, 1.24f, 1.4f), PortTone.Ochre);
                c.Structures.Box(V(x, 21.55f, zs), V(57, .55f, .76f), PortTone.Ochre);
                for (int i = 0; i < 8; i++)
                    c.Structures.Beam(V(x - 27 + i * 6.8f, 18.4f, zs), V(x - 27 + (i + 1) * 6.8f, 21.55f, zs), .41f, .46f, PortTone.Rust);
            }
            c.Structures.Box(V(x + 7.4f, 17.7f, z), V(3.6f, 1.4f, 20.9f), PortTone.Slate);
            c.Structures.Beam(V(x + 7.4f, 17.6f, z), V(x + 7.4f, 10.6f, z), .09f, .09f, PortTone.Steel);
            c.Structures.Box(V(x + 7.4f, 10.5f, z), V(4.6f, .38f, 1.25f), PortTone.Ochre);
        }

        static void Wagon(PortChunk c, float x, float z, PortTone tone, bool enclosed)
        {
            c.Collider(V(x, enclosed ? 2.15f : .95f, z), V(21, enclosed ? 4.3f : 1.9f, 3.35f));
            c.Structures.Box(V(x, 1.04f, z), V(21.4f, .59f, 3.4f), PortTone.Steel);
            for (int side = -1; side <= 1; side += 2)
            {
                float px = x + side * 8.1f;
                c.Structures.Box(V(px, .57f, z), V(3.45f, .58f, 2.44f), PortTone.Steel);
                for (int dz = -1; dz <= 1; dz += 2)
                    for (int dx = -1; dx <= 1; dx += 2)
                        c.Structures.Cylinder(V(px + dx * .76f, .55f, z + dz * 1.11f), .52f, .26f, PortTone.Steel, Quaternion.Euler(90, 0, 0));
            }
            if (!enclosed)
            {
                for (int i = -4; i <= 4; i++) c.Structures.Box(V(x + i * 2.23f, 1.52f, z), V(.34f, .39f, 3.65f), PortTone.Ochre);
                return;
            }
            c.Structures.Box(V(x, 2.76f, z), V(20.8f, 2.89f, 3.23f), tone);
            c.Structures.Box(V(x, 4.24f, z), V(21.15f, .27f, 3.55f), PortTone.Slate);
            for (int face = -1; face <= 1; face += 2)
            {
                for (int i = -4; i <= 4; i++)
                    c.Structures.Box(V(x + i * 2.14f, 2.75f, z + face * 1.64f), V(.20f, 2.71f, .11f), PortTone.Slate);
                c.Structures.Box(V(x, 2.72f, z + face * 1.74f), V(4.64f, 2.46f, .16f), tone);
                c.Structures.Box(V(x + .2f, 2.81f, z + face * 1.84f), V(1.2f, .27f, .06f), PortTone.Paint);
            }
        }

        static void QuayCrane(PortChunk c, float z, bool longBoom)
        {
            var m = c.Structures;
            float tip = longBoom ? 420 : 401;
            for (int side = -1; side <= 1; side += 2)
            {
                float row = z + side * 8.2f;
                for (int i = 0; i < 2; i++)
                {
                    float x = 288 + i * 39;
                    c.Block(V(x, .78f, row), V(4.6f, 1.56f, 3.1f), PortTone.Steel);
                    c.Block(V(x, 12.13f, row), V(1.8f, 21.18f, 1.8f), PortTone.Ochre);
                    m.Beam(V(x, 13.4f, row), V(x + (i == 0 ? 8 : -8), 23.8f, row), .87f, .82f, PortTone.Rust);
                }
                m.Box(V(307.5f, 23.2f, row), V(40.4f, 1.31f, 1.65f), PortTone.Ochre);
            }
            m.Box(V(322, 25.1f, z), V(8.8f, 2.97f, 8.7f), PortTone.Slate);
            m.Box(V(326.41f, 25.42f, z), V(.24f, 1.24f, 5.88f), PortTone.Glass);
            m.Beam(V(319, 24.8f, z), V(311, 47, z), 1.5f, 1.45f, PortTone.Ochre);
            m.Beam(V(311, 47, z), V(tip, 33.4f, z), .27f, .31f, PortTone.Slate);
            m.Beam(V(311, 47, z), V(282, 31, z), .28f, .28f, PortTone.Slate);
            m.Beam(V(282, 31, z), V(tip, 33.4f, z), 1.2f, 3.8f, PortTone.Ochre);
            m.Beam(V(282, 28.7f, z), V(tip, 31.1f, z), .49f, 1.05f, PortTone.Slate);
            m.Box(V(tip - 13, 31, z), V(2.5f, 1.11f, 4.43f), PortTone.Slate);
            m.Beam(V(tip - 13, 30.5f, z), V(tip - 13, 13, z), .11f, .11f, PortTone.Steel);
            m.Box(V(tip - 13, 12.84f, z), V(4.4f, .44f, 1.34f), PortTone.Ochre);
        }

        static Mesh Vessel(PortMesh m)
        {
            Vector3[] deck = { V(404, 2.9f, 113), V(415, 2.9f, 105), V(417, 2.9f, 21), V(411, 2.9f, 14),
                V(392, 2.9f, 14), V(388, 2.9f, 22), V(389, 2.9f, 98), V(396, 2.9f, 108) };
            var center = V(403, 2.9f, 58);
            int count = deck.Length;
            var vertices = new Vector3[count * 3 + 2];
            var triangles = new List<int>(count * 18);
            vertices[count * 3] = center;
            vertices[count * 3 + 1] = V(center.x, -2.45f, center.z);
            for (int i = 0; i < count; i++)
            {
                vertices[i] = deck[i];
                vertices[i + count] = Vector3.Lerp(deck[i], center, .12f);
                vertices[i + count].y = -.25f;
                vertices[i + 2 * count] = vertices[i + count] - V(0, 2.2f, 0);
            }
            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                Band(i + count, j + count, j, i, PortTone.DeepBlue);
                Band(i + 2 * count, j + 2 * count, j + count, i + count, PortTone.Rust);
                Face(count * 3, i, j, PortTone.Slate);
                Face(count * 3 + 1, j + 2 * count, i + 2 * count, PortTone.Rust);
                m.Beam(deck[i] + V(0, .34f, 0), deck[j] + V(0, .34f, 0), .26f, .26f, PortTone.Zinc);
            }

            void Band(int a, int b, int c, int d, PortTone tone)
            {
                m.Quad(vertices[a], vertices[b], vertices[c], vertices[d], tone);
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(a); triangles.Add(c); triangles.Add(d);
            }
            void Face(int a, int b, int c, PortTone tone)
            {
                m.Tri(vertices[a], vertices[b], vertices[c], tone);
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
            }
            m.Box(V(403, 4.6f, 31), V(19.8f, 3.4f, 16), PortTone.Plaster);
            m.Box(V(403, 8.1f, 32), V(17.3f, 3.3f, 11), PortTone.Plaster);
            m.Box(V(403, 9.79f, 32), V(18.3f, .38f, 12.2f), PortTone.DeepBlue);
            for (int i = -3; i <= 3; i++)
                m.Box(V(403 + i * 2.25f, 8.47f, 37.57f), V(1.9f, .91f, .16f), PortTone.Glass);
            m.Cylinder(V(406, 11.1f, 26), 1.18f, 2.65f, PortTone.Rust);
            m.Cylinder(V(406, 12.53f, 26), 1.23f, .29f, PortTone.Steel);
            for (int i = 0; i < 3; i++)
            {
                m.Box(V(399, 4.23f, 53 + i * 16), V(7.1f, 2.66f, 10.5f), PortTone.Teal);
                m.Box(V(408, 4.23f, 53 + i * 16), V(7.35f, 2.66f, 10.5f), PortTone.Terra);
            }
            var hull = new Mesh { name = "Pilot vessel hull", vertices = vertices };
            hull.SetTriangles(triangles, 0);
            return hull;
        }
    }
}
