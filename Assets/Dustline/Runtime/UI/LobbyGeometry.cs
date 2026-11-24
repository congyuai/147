using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Dustline
{
    internal sealed class LobbyGeometry
    {
        const int PaletteWidth = 256;
        readonly Transform root;
        readonly Shader shader;
        readonly Action<Object> keep;
        readonly List<Color32> swatches = new List<Color32>();
        readonly Dictionary<Color32, int> palette = new Dictionary<Color32, int>();
        readonly Batch scene = new Batch();

        internal LobbyGeometry(Transform root, Action<Object> keep)
        {
            this.root = root;
            this.keep = keep;
            shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new InvalidOperationException("Lobby scenery needs URP Unlit.");
        }

        internal Material Build()
        {
            Sky();
            Ridge(118, 24, 21, 13, 0x657E8A, 0x80929A);
            Ridge(178, 31, 26, 27, 0x7A909B, 0x96A6AC);
            Ridge(253, 39, 43, 41, 0x91A4AC, 0xA5B6B8);
            var water = Estuary();
            Platform();
            Quay(-11.4f, 36f, 11.8f, 0xA68155, 6.0f);
            Quay(17.2f, 57f, 15.6f, 0x7C8B82, 8.0f);
            var data = new Color32[PaletteWidth];
            swatches.CopyTo(data);
            var texture = new Texture2D(PaletteWidth, 1, TextureFormat.RGBA32, false) {
                name = "Lobby scenery swatches", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            keep(texture);
            texture.SetPixels32(data);
            texture.Apply(false, true);
            Render("Shore, ridges and dock machinery", scene, Surface("Lobby scenery", texture));
            return water;
        }

        internal Transform Contact(string name, Material material)
        {
            var b = new Batch();
            b.Quad(V(-1, 0, -1), V(-1, 0, 1), V(1, 0, 1), V(1, 0, -1),
                Vector2.zero, Vector2.up, Vector2.one, Vector2.right);
            return Render(name, b, material);
        }

        internal Material ContactMaterial()
        {
            var mask = Texture("Lobby soft contact", 48, 48, false, (u, v) =>
            {
                float distance = new Vector2(2f * u - 1f, 2f * v - 1f).magnitude;
                float fade = 1f - Ease(.08f, 1f, distance);
                return new Color(.11f, .15f, .16f, .72f * fade * fade);
            });
            var material = Surface("Lobby contact", mask);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetShaderPassEnabled("DepthOnly", false);
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        void Sky()
        {
            var sky = Texture("Lobby late daylight", 512, 256, false, (u, v) =>
            {
                var horizon = Hue(0xC3C5B5);
                var zenith = Hue(0x4F758F);
                var air = Color.Lerp(horizon, zenith, Ease(.26f, .83f, v));
                const float sunU = .448f, sunV = .578f;
                float glow = Mathf.Exp(-26f * ((u - sunU) * (u - sunU) * 17f + (v - sunV) * (v - sunV)));
                air = Color.Lerp(air, Hue(0xE8D9B6), glow * .65f);
                float d = new Vector2((u - sunU) * 5.4f, v - sunV).magnitude;
                air = Color.Lerp(air, Hue(0xF7E4BF), .86f * (1f - Ease(.017f, .023f, d)));
                float wisps = Noise(12f * u + v * 1.7f, 39f * v, 41) * .66f + Noise(33f * u, 97f * v, 9) * .34f;
                float streak = Ease(.48f, .77f, wisps) * Ease(.24f, .51f, v);
                return Color.Lerp(air, Hue(0xDFDDCF), streak * .52f);
            });
            var b = new Batch();
            b.Quad(V(-520, -54, 321), V(-520, 112, 321), V(520, 112, 321), V(520, -54, 321),
                Vector2.zero, Vector2.up, Vector2.one, Vector2.right);
            Render("Sky and low cloud", b, Surface("Lobby sky", sky));
        }

        void Ridge(float z, float crest, float variation, int seed, int slope, int lit)
        {
            const int steps = 68;
            var foot = new Vector3[steps + 1];
            var face = new Vector3[steps + 1];
            var top = new Vector3[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float x = (i - steps * .5f) * (z / 34f) * 1.60f;
                float h = crest * (.53f + .48f * Noise(i * .21f, 2, seed)) + variation * (.64f * Noise(i * .72f, 0, seed) + .36f * Hash(i, 1, seed) - .55f);
                foot[i] = V(x, -2.2f, z - 18f - 9f * Hash(i, 0, seed));
                face[i] = V(x + (Hash(i, 2, seed) - .5f) * 4.8f, h * .57f, z - 11f + Hash(i, 5, seed) * 7f);
                top[i] = V(x, h, z + 5f + Hash(i, 4, seed) * 18f);
                if (i == 0) continue;
                Color32 grass = Hue(i % 4 == 1 ? lit : slope);
                Color32 stone = Hue(i % 3 == 1 ? lit : slope, i % 3 == 2 ? .94f : 1f);
                Flat(foot[i - 1], face[i - 1], foot[i], Hue(slope, .95f));
                Flat(foot[i], face[i - 1], face[i], grass);
                Flat(face[i - 1], top[i - 1], top[i], stone);
                Flat(face[i - 1], top[i], face[i], Hue(slope, i % 5 == 0 ? .93f : 1.02f));
            }
        }

        Material Estuary()
        {
            const int steps = 88;
            const float waterY = -1.45f;
            var water = new Batch();
            var near = V(-176, waterY, Near(-176));
            var far = V(-176, waterY, Far(-176));
            for (int i = 1; i <= steps; i++)
            {
                float x = -176 + 352f * i / steps;
                Vector3 a = V(x, waterY, Near(x)), b = V(x, waterY, Far(x));
                Vector3 a0 = V(near.x, -.20f, 3.2f), a1 = V(a.x, -.20f, 3.2f);
                Vector3 bank0 = V(far.x, 1.08f + .52f * Hash(i - 1, 2, 12), far.z + 4f);
                Vector3 bank1 = V(b.x, 1.08f + .52f * Hash(i, 2, 12), b.z + 4f);
                water.Quad(near, far, b, a, WaterUv(near), WaterUv(far), WaterUv(b), WaterUv(a));
                Flat(a0, near, a, Hue(i % 6 < 2 ? 0x59695D : 0x686F64));
                Flat(a0, a, a1, Hue(0x70776B, i % 3 == 0 ? .94f : 1.03f));
                Flat(far, bank0, bank1, Hue(i % 4 == 0 ? 0x73796A : 0x687369));
                Flat(far, bank1, b, Hue(0x656D62));
                Flat(bank0, bank0 + V(0, 1.1f, 42f), bank1 + V(0, 1.1f, 42f), Hue(0x77877E));
                Flat(bank0, bank1 + V(0, 1.1f, 42f), bank1, Hue(i % 5 == 0 ? 0x7C8A7D : 0x748277));
                near = a; far = b;
            }
            var texture = Texture("Lobby water ripples", 256, 128, true, (u, v) =>
            {
                float angle = 2f * Mathf.PI;
                float wash = .50f + .23f * Mathf.Sin(angle * (12f * v + .27f * Mathf.Sin(angle * 3f * u)))
                    + .15f * Mathf.Sin(angle * (31f * v - 2f * u + .25f * Mathf.Sin(angle * 2f * u)));
                float light = Mathf.Pow(Mathf.Clamp01(wash + .09f), 1.9f);
                return Color.Lerp(Hue(0x3F5E66), Hue(0x8BA6A7), light);
            });
            var surface = Surface("Moving river surface", texture);
            Render("River bend", water, surface);
            return surface;

            static Vector2 WaterUv(Vector3 p) => new Vector2(p.x * .054f, p.z * .192f);
            static float Near(float x) => Mathf.Max(3.45f, 7.7f + 4.0f * Mathf.Sin(.068f * x - .3f) + 1.5f * Mathf.Cos(.15f * x));
            static float Far(float x) => 48f + 8f * Mathf.Sin(.075f * x + 1f) + 24f * Mathf.Exp(-.0014f * (x - 32f) * (x - 32f));
        }

        void Platform()
        {
            var texture = Texture("Lobby weathered concrete", 192, 192, true, (u, v) =>
            {
                float grain = Noise(23f * u, 25f * v, 12) * .24f + Hash((int)(u * 192), (int)(v * 192), 8) * .15f;
                float cloud = .21f * Noise(u * 4f, v * 5f, 37) + .18f;
                return Color.Lerp(Hue(0x535E5D), Hue(0x92988E), grain + cloud);
            });
            var b = new Batch();
            b.Quad(V(-42, 0, -9), V(-42, 0, 3.28f), V(42, 0, 3.28f), V(42, 0, -9),
                V2(0, 0), V2(0, 5.1f), V2(34, 5.1f), V2(34, 0));
            Render("Concrete apron", b, Surface("Lobby floor", texture));
            Box(V(0, -.17f, 3.25f), V(84, .34f, .23f), 0x566365);
            Box(V(0, .012f, 3.13f), V(84, .024f, .14f), 0xA0A69D);
            for (int i = -9; i <= 9; i++)
            {
                Box(V(i * 2.7f + .85f, .0015f, -.65f), V(.011f, .003f, 7.35f), 0x4A5755);
                if (i == 0) continue;
                Box(V(i * 1.08f, .0018f, 2.72f), V(.60f, .0036f, .067f), 0xA29B72);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 5; i++)
                    Box(V(side * (3.2f + 1.2f * i), .47f, 3.12f), V(.045f, .94f, .056f), 0x546361);
                for (int i = 0; i < 2; i++)
                    Box(V(side * 5.6f, .41f + .43f * i, 3.12f), V(4.8f, .039f, .043f), 0x89948A);
            }
        }

        void Quay(float x, float z, float span, int paint, float height)
        {
            Vector3 origin = V(x, .20f, z);
            Box(origin + V(0, -.92f, .96f), V(span + 4f, 1.98f, 6.9f), 0x667777);
            Box(origin + V(0, .099f, .96f), V(span + 4.1f, .22f, 6.96f), 0x8A8B7F);
            Box(origin + V(0, -.20f, -2.53f), V(span + 4.1f, .15f, .16f), 0x46585C);
            for (int i = 0; i < 4; i++)
            {
                float side = (i - 1.6f) * 1.75f;
                Box(origin + V(side, .56f, 2.08f), V(1.64f, .94f, 2.49f), i % 2 == 0 ? 0x667F7D : 0x86745E);
                if (i < 2) Box(origin + V(side, 1.47f, 1.98f), V(1.64f, .9f, 2.45f), 0x758984);
                for (int j = 0; j < 4; j++)
                    Box(origin + V(side + (j - 1.5f) * .32f, .60f, .82f), V(.043f, .69f, .027f), 0x8A9183);
            }
            float half = span * .39f;
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 low = origin + V(s * half, .28f, -.59f);
                Vector3 high = origin + V(s * half * .88f, height, -.20f);
                Box(low, V(.90f, .22f, 1.25f), 0x505C60);
                Beam(low, high, .31f, .43f, paint);
                Beam(low + V(0, 0, 1.76f), high, .25f, .32f, paint);
                Beam(low + V(0, 1.0f, .08f), high + V(-s * 1.45f, -.82f, .11f), .14f, .19f, paint);
            }
            Vector3 left = origin + V(-span * .58f, height, -.22f);
            Vector3 right = origin + V(span * .58f, height, -.22f);
            Beam(left, right, .21f, .60f, paint);
            Beam(left + Vector3.up * .75f, right + Vector3.up * .75f, .15f, .44f, paint);
            for (int i = 0; i < 8; i++)
            {
                Vector3 a = Vector3.Lerp(left, right, i / 8f), b = Vector3.Lerp(left, right, (i + 1) / 8f);
                Beam(a, b + Vector3.up * .75f, .105f, .18f, paint);
                Beam(a, a + Vector3.up * .75f, .075f, .18f, paint);
            }
            Vector3 trolley = origin + V(-span * .14f, height - .18f, -.46f);
            Box(trolley, V(1.27f, .49f, 1.00f), 0x526264);
            for (int s = -1; s <= 1; s += 2)
                Beam(trolley + V(s * .17f, -.22f, 0), trolley + V(s * .12f, -2.28f, 0), .035f, .035f, 0x46565A);
            Box(trolley + V(0, -2.34f, 0), V(.47f, .21f, .24f), paint);
        }

        void Beam(Vector3 from, Vector3 to, float width, float depth, int tone) => Box((from + to) * .5f,
            V(width, (to - from).magnitude, depth), tone, Quaternion.FromToRotation(Vector3.up, to - from));

        void Box(Vector3 center, Vector3 size, int tone, Quaternion? rotation = null)
        {
            Quaternion q = rotation ?? Quaternion.identity;
            for (int axis = 0; axis < 3; axis++)
                for (int side = -1; side <= 1; side += 2)
                {
                    var n = Vector3.zero; n[axis] = side;
                    var u = Vector3.zero; u[(axis + 1) % 3] = side;
                    var v = Vector3.zero; v[(axis + 2) % 3] = 1f;
                    Vector3 t = center + q * Vector3.Scale(size * .5f, n);
                    u = q * Vector3.Scale(size * .5f, u);
                    v = q * Vector3.Scale(size * .5f, v);
                    Vector3 normal = q * n;
                    float light = Vector3.Dot(normal, V(-.55f, .74f, -.39f));
                    float shade = light > .55f ? 1.12f : light > .1f ? 1.02f : light > -.4f ? .92f : .77f;
                    Vector2 uv = Swatch(Hue(tone, shade));
                    scene.Quad(t - u - v, t + u - v, t + u + v, t - u + v, uv, uv, uv, uv);
                }
        }

        void Flat(Vector3 a, Vector3 b, Vector3 c, Color32 color) => scene.Triangle(a, b, c, Swatch(color));
        Vector2 Swatch(Color32 color)
        {
            if (!palette.TryGetValue(color, out int index))
            {
                index = swatches.Count;
                palette.Add(color, index);
                swatches.Add(color);
            }
            return V2((index + .5f) / PaletteWidth, .5f);
        }

        Transform Render(string name, Batch batch, Material material)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)) { layer = root.gameObject.layer };
            go.transform.SetParent(root, false);
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            keep(mesh);
            mesh.SetVertices(batch.Points);
            mesh.SetUVs(0, batch.Uvs);
            mesh.SetTriangles(batch.Indices, 0);
            mesh.UploadMeshData(true);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go.transform;
        }

        Material Surface(string name, Texture2D texture)
        {
            var material = new Material(shader) { name = name, hideFlags = HideFlags.DontSave };
            keep(material);
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            return material;
        }

        Texture2D Texture(string name, int width, int height, bool repeat, Func<float, float, Color> shade)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, repeat) {
                name = name, filterMode = repeat ? FilterMode.Trilinear : FilterMode.Bilinear,
                wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp, anisoLevel = repeat ? 4 : 1,
                hideFlags = HideFlags.DontSave
            };
            keep(texture);
            var data = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    data[y * width + x] = shade((x + .5f) / width, (y + .5f) / height);
            texture.SetPixels32(data);
            texture.Apply(repeat, true);
            return texture;
        }

        static float Ease(float from, float to, float value) => Mathf.SmoothStep(0f, 1f, (value - from) / (to - from));
        static Color Hue(int value, float shade = 1f) => new Color(((value >> 16) & 255) * shade / 255f,
            ((value >> 8) & 255) * shade / 255f, (value & 255) * shade / 255f);
        static Vector2 V2(float x, float y) => new Vector2(x, y);
        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);
        static float Noise(float x, float y, int seed)
        {
            int i = Mathf.FloorToInt(x), j = Mathf.FloorToInt(y);
            float u = x - i, v = y - j;
            u *= u * (3f - 2f * u);
            v *= v * (3f - 2f * v);
            return Mathf.Lerp(Mathf.Lerp(Hash(i, j, seed), Hash(i + 1, j, seed), u),
                Mathf.Lerp(Hash(i, j + 1, seed), Hash(i + 1, j + 1, seed), u), v);
        }
        static float Hash(int x, int y, int seed)
        {
            uint h = unchecked((uint)(x * 73856093 ^ y * 19349663 ^ seed * 83492791));
            h = unchecked((h ^ (h >> 16)) * 0x7feb352du);
            return ((h ^ (h >> 15)) & 65535u) / 65535f;
        }

        sealed class Batch
        {
            internal readonly List<Vector3> Points = new List<Vector3>();
            internal readonly List<Vector2> Uvs = new List<Vector2>();
            internal readonly List<int> Indices = new List<int>();
            internal void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector2 uv)
            {
                Indices.Add(Points.Count); Indices.Add(Points.Count + 1); Indices.Add(Points.Count + 2);
                Add(a, uv); Add(b, uv); Add(c, uv);
            }
            internal void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                int i = Points.Count;
                Add(a, ua); Add(b, ub); Add(c, uc); Add(d, ud);
                Indices.Add(i); Indices.Add(i + 1); Indices.Add(i + 2);
                Indices.Add(i); Indices.Add(i + 2); Indices.Add(i + 3);
            }
            void Add(Vector3 point, Vector2 uv) { Points.Add(point); Uvs.Add(uv); }
        }
    }
}
