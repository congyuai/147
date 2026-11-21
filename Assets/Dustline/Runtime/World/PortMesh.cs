using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dustline
{
    internal enum PortTone
    {
        Sand, SandDark, Gravel, Asphalt, Concrete, ConcreteLight, Joint, Plaster,
        Steel, Slate, Zinc, DeepBlue, Glass, SignalBlue, Teal, Emerald,
        Rust, Terra, Ochre, Yellow, Paint, Paper, Canvas, Red,
        Water, WaterLight, WaterDark, Timber,
        Rock, RockLichen, Bark, Needles, NeedlesLight, Leaves, DryGrass, FlowerPale, FlowerOchre, Amber
    }

    internal sealed class PortMaterials
    {
        static readonly Color32[] colors = {
            new Color32(141,129,106,255), new Color32(116,108,96,255), new Color32(113,112,104,255),
            new Color32(75,83,86,255), new Color32(126,136,135,255), new Color32(167,172,163,255),
            new Color32(75,80,77,255), new Color32(186,179,153,255),
            new Color32(44,57,65,255), new Color32(67,84,94,255), new Color32(138,155,155,255),
            new Color32(30,50,66,255), new Color32(56,87,100,255), new Color32(59,105,134,255),
            new Color32(53,108,106,255), new Color32(93,215,162,255),
            new Color32(135,70,47,255), new Color32(164,91,67,255), new Color32(174,127,56,255),
            new Color32(216,173,71,255), new Color32(211,217,194,255), new Color32(205,177,126,255),
            new Color32(114,118,80,255), new Color32(184,65,58,255),
            new Color32(55,88,104,255), new Color32(77,114,123,255), new Color32(39,68,86,255),
            new Color32(128,99,69,255), new Color32(103,106,103,255), new Color32(126,130,112,255),
            new Color32(77,65,52,255), new Color32(49,74,62,255), new Color32(73,94,69,255),
            new Color32(89,107,68,255), new Color32(134,132,90,255), new Color32(205,187,153,255),
            new Color32(199,150,72,255), new Color32(231,180,94,255)
        };
        const int Tile = 32, Columns = 8, Rows = 8;
        internal readonly Material Structure, Pavement, Sea, Dark, Ground, Lamp;

        internal PortMaterials(Action<UnityEngine.Object> keep)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var texture = new Texture2D(Columns * Tile, Rows * Tile, TextureFormat.RGBA32, 5, false)
            {
                name = "Port swatches", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 2
            };
            keep(texture);
            var pixels = new Color32[texture.width * texture.height];
            for (int y = 0; y < texture.height; y++)
                for (int x = 0; x < texture.width; x++)
                {
                    int swatch = (y / Tile) * Columns + x / Tile;
                    Color32 c = colors[swatch % colors.Length];
                    float weather = Mathf.PerlinNoise((x % Tile) * .28f + swatch * 1.09f, (y % Tile) * .21f);
                    int grit = Mathf.RoundToInt((weather - .5f) * 14);
                    pixels[x + y * texture.width] = new Color32((byte)(c.r + grit), (byte)(c.g + grit), (byte)(c.b + grit), 255);
                }
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            Structure = Make("Port structures", texture, 0.18f);
            Pavement = Make("Port pavement", texture, 0.07f);
            Sea = Make("Port water", texture, 0.24f);
            Ground = Make("Harbor grade atlas", null, 0.065f);
            Lamp = Make("Weatherproof amber luminaires", texture, 0.16f);
            Lamp.EnableKeyword("_EMISSION");
            Lamp.SetColor("_EmissionColor", new Color(.91f, .49f, .17f) * 1.15f);
            Dark = Make("Steel", null, 0.2f);
            Dark.SetColor("_BaseColor", new Color(0.09f, 0.12f, 0.14f));

            Material Make(string name, Texture2D atlas, float smoothness)
            {
                var material = new Material(shader) { name = name };
                keep(material);
                material.mainTexture = atlas;
                material.SetFloat("_Smoothness", smoothness);
                return material;
            }
        }

        internal static Vector2 UV(PortTone tone, float u, float v)
        {
            int index = (int)tone;
            return new Vector2((index % Columns + 0.25f + 0.5f * u) / Columns,
                (index / Columns + 0.25f + 0.5f * v) / Rows);
        }
    }

    internal sealed class PortMesh
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int> triangles = new List<int>();

        internal void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, PortTone tone)
        {
            int i = vertices.Count;
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            Vertex(a, normal, tone, 0, 0);
            Vertex(b, normal, tone, 1, 0);
            Vertex(c, normal, tone, 1, 1);
            Vertex(d, normal, tone, 0, 1);
            triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + 2);
            triangles.Add(i); triangles.Add(i + 2); triangles.Add(i + 3);
        }

        internal void Tri(Vector3 a, Vector3 b, Vector3 c, PortTone tone)
        {
            int i = vertices.Count;
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            Vertex(a, normal, tone, 0, 0);
            Vertex(b, normal, tone, 1, 0);
            Vertex(c, normal, tone, 0.5f, 1);
            triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + 2);
        }

        void Vertex(Vector3 point, Vector3 normal, PortTone tone, float u, float v)
        {
            vertices.Add(point);
            normals.Add(normal);
            uv.Add(PortMaterials.UV(tone, u, v));
        }

        internal void Floor(float x0, float z0, float x1, float z1, float y, PortTone tone)
        {
            Quad(new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0), tone);
        }

        internal void Box(Vector3 center, Vector3 size, PortTone tone, float yaw = 0) =>
            Box(center, size, tone, Quaternion.Euler(0, yaw, 0));

        internal void Box(Vector3 center, Vector3 size, PortTone tone, Quaternion rotation)
        {
            float x = size.x * 0.5f, y = size.y * 0.5f, z = size.z * 0.5f;
            var a = center + rotation * new Vector3(-x, -y, -z);
            var b = center + rotation * new Vector3(x, -y, -z);
            var c = center + rotation * new Vector3(x, y, -z);
            var d = center + rotation * new Vector3(-x, y, -z);
            var e = center + rotation * new Vector3(-x, -y, z);
            var f = center + rotation * new Vector3(x, -y, z);
            var g = center + rotation * new Vector3(x, y, z);
            var h = center + rotation * new Vector3(-x, y, z);
            Quad(b, a, d, c, tone);
            Quad(e, f, g, h, tone);
            Quad(a, e, h, d, tone);
            Quad(f, b, c, g, tone);
            Quad(d, h, g, c, tone);
            Quad(a, b, f, e, tone);
        }

        internal void Beam(Vector3 start, Vector3 end, float width, float depth, PortTone tone)
        {
            var delta = end - start;
            Box((start + end) * 0.5f, new Vector3(width, delta.magnitude, depth), tone,
                Quaternion.FromToRotation(Vector3.up, delta));
        }

        internal void Cylinder(Vector3 center, float radius, float height, PortTone tone, int sides = 8) =>
            Cylinder(center, radius, height, tone, Quaternion.identity, sides);

        internal void Cylinder(Vector3 center, float radius, float height, PortTone tone, Quaternion rotation, int sides = 8)
        {
            Vector3 up = rotation * Vector3.up * (height * 0.5f);
            Vector3 previous = rotation * Vector3.forward * radius;
            for (int i = 1; i <= sides; i++)
            {
                float angle = i * (2 * Mathf.PI / sides);
                var next = rotation * new Vector3(Mathf.Sin(angle) * radius, 0, Mathf.Cos(angle) * radius);
                Vector3 a = center + previous, b = center + next;
                Quad(a - up, b - up, b + up, a + up, tone);
                Tri(center + up, a + up, b + up, tone);
                Tri(center - up, b - up, a - up, tone);
                previous = next;
            }
        }

        internal void Bake(Transform parent, string name, Material material, Action<UnityEngine.Object> keep, bool shadow)
        {
            if (vertices.Count == 0) return;
            var mesh = new Mesh { name = name, indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            keep(mesh);
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.UploadMeshData(true);
            var go = Child(parent, name);
            go.isStatic = parent.gameObject.isStatic;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        internal static GameObject Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }
    }

    internal sealed class PortChunk
    {
        internal readonly GameObject Root;
        internal readonly PortMesh Deck = new PortMesh();
        internal readonly PortMesh Structures = new PortMesh();
        readonly PortMaterials materials;
        readonly Action<UnityEngine.Object> keep;

        internal PortChunk(Transform parent, string name, PortMaterials materials, Action<UnityEngine.Object> keep, float grade = 0)
        {
            Root = PortMesh.Child(parent, name);
            Root.transform.localPosition = Vector3.up * grade;
            Root.isStatic = true;
            this.materials = materials;
            this.keep = keep;
        }

        internal BoxCollider Collider(Vector3 center, Vector3 size)
        {
            var box = Root.AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;
            return box;
        }

        internal void Block(Vector3 center, Vector3 size, PortTone tone)
        {
            Structures.Box(center, size, tone);
            Collider(center, size);
        }

        internal void Surface(Vector3 center, Vector3 size, SurfaceKind kind)
        {
            var surface = PortMesh.Child(Root.transform, "Walkable " + kind);
            surface.isStatic = true;
            surface.transform.localPosition = center;
            var collider = surface.AddComponent<BoxCollider>();
            collider.size = size;
            surface.AddComponent<FootstepSurface>().Kind = kind;
        }

        internal void Bake()
        {
            Deck.Bake(Root.transform, "Pavement", materials.Pavement, keep, false);
            Structures.Bake(Root.transform, "Structures", materials.Structure, keep, true);
        }
    }
}
