using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dustline
{
    sealed class CombatFxBatch
    {
        public enum Ink { Glow, Vapor, Tongue, Core, Dust, Signal, Streak, CoreEdge, Hole, Scrape, Ding }
        const int QuadsPerBurst = 18;
        const int Levels = 16;
        const int Tile = 32;
        const int Swatches = (int)Ink.Ding + 1;
        const int AtlasWidth = Swatches * Tile;
        const int AtlasHeight = Levels * Tile;
        const MeshUpdateFlags WriteFlags = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds;

        [StructLayout(LayoutKind.Sequential)]
        struct Vertex
        {
            public Vector3 Position;
            public Vector2 Uv;
        }

        readonly Mesh mesh;
        readonly MeshRenderer renderer;
        readonly Material material;
        readonly Texture2D atlas;
        readonly Vertex[] vertices;
        Matrix4x4 toLocal;
        Vector3 eye, right, up, minimum, maximum;
        int cursor;

        public CombatFxBatch(GameObject node, int bursts, int marks)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new System.InvalidOperationException("Missing URP Unlit shader for raid combat effects.");
            renderer = node.AddComponent<MeshRenderer>();
            renderer.enabled = false;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            atlas = MakeAtlas();
            material = new Material(shader) { name = "Dustline / transient FX" };
            material.SetTexture("_BaseMap", atlas);
            material.SetFloat("_Surface", 1);
            material.SetFloat("_Blend", 0);
            material.SetFloat("_ZWrite", 0);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetShaderPassEnabled("DepthOnly", false);
            material.renderQueue = (int)RenderQueue.Transparent;
            renderer.sharedMaterial = material;

            int quads = bursts * QuadsPerBurst + marks;
            vertices = new Vertex[quads * 4];
            var indices = new ushort[quads * 6];
            for (int i = 0; i < quads; i++)
            {
                ushort at = (ushort)(i * 4);
                int index = i * 6;
                indices[index] = at; indices[index + 1] = (ushort)(at + 1); indices[index + 2] = (ushort)(at + 2);
                indices[index + 3] = at; indices[index + 4] = (ushort)(at + 2); indices[index + 5] = (ushort)(at + 3);
            }
            mesh = new Mesh { name = "Dustline / streaming FX buffer" };
            mesh.MarkDynamic();
            mesh.SetVertexBufferParams(vertices.Length,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2));
            mesh.SetIndexBufferParams(indices.Length, IndexFormat.UInt16);
            mesh.SetIndexBufferData(indices, 0, 0, indices.Length, WriteFlags);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, 0, MeshTopology.Triangles), WriteFlags);
            node.AddComponent<MeshFilter>().sharedMesh = mesh;
        }

        public void Begin(Transform camera)
        {
            cursor = 0;
            toLocal = renderer.transform.worldToLocalMatrix;
            eye = camera.position;
            right = camera.right;
            up = camera.up;
            minimum = Vector3.one * float.PositiveInfinity;
            maximum = Vector3.one * float.NegativeInfinity;
        }

        public void Billboard(Vector3 center, float radius, float spin, Ink ink, float fade)
        {
            float s = Mathf.Sin(spin), c = Mathf.Cos(spin);
            Vector3 a = (right * c + up * s) * radius;
            Vector3 b = (up * c - right * s) * radius;
            Quad(center - a - b, center + a - b, center + a + b, center - a + b, ink, fade);
        }

        public void Ribbon(Vector3 from, Vector3 to, float width, Ink ink, float fade)
        {
            Vector3 across = Vector3.Cross(to - from, eye - (from + to) * 0.5f);
            across = (across.sqrMagnitude > 0.000001f ? across.normalized : right) * width;
            Quad(from - across, from + across, to + across, to - across, ink, fade);
        }

        public void Ridge(Vector3 rear, Vector3 front, Vector3 edgeA, Vector3 edgeB, Ink ink, float fade)
        {
            Quad(edgeA, rear, edgeB, front, ink, fade);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Ink ink, float fade)
        {
            int opacity = Mathf.RoundToInt(Mathf.Clamp01(fade) * Levels);
            if (opacity == 0 || cursor >= vertices.Length) return;
            float u0 = ((int)ink * Tile + 1.5f) / AtlasWidth;
            float u1 = ((int)ink * Tile + Tile - 1.5f) / AtlasWidth;
            float v0 = ((opacity - 1) * Tile + 1.5f) / AtlasHeight;
            float v1 = ((opacity - 1) * Tile + Tile - 1.5f) / AtlasHeight;
            Add(a, u0, v0); Add(b, u1, v0); Add(c, u1, v1); Add(d, u0, v1);
        }

        void Add(Vector3 world, float u, float v)
        {
            Vector3 p = toLocal.MultiplyPoint3x4(world);
            vertices[cursor++] = new Vertex { Position = p, Uv = new Vector2(u, v) };
            minimum = Vector3.Min(minimum, p);
            maximum = Vector3.Max(maximum, p);
        }

        public void Upload()
        {
            if (cursor == 0) { Hide(); return; }
            var bounds = new Bounds();
            bounds.SetMinMax(minimum, maximum);
            bounds.Expand(0.1f);
            mesh.SetVertexBufferData(vertices, 0, 0, cursor, 0, WriteFlags);
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, cursor / 4 * 6, MeshTopology.Triangles) {
                vertexCount = cursor, bounds = bounds
            }, WriteFlags);
            mesh.bounds = bounds;
            if (!renderer.enabled) renderer.enabled = true;
        }

        public void Hide()
        {
            if (renderer != null && renderer.enabled) renderer.enabled = false;
        }

        public void Release()
        {
            Object.Destroy(mesh);
            Object.Destroy(material);
            Object.Destroy(atlas);
        }

        static Texture2D MakeAtlas()
        {
            var result = new Texture2D(AtlasWidth, AtlasHeight, TextureFormat.RGBA32, false, false) {
                name = "Dustline / hand-shaped FX atlas", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            // Opacity rows let the stock URP Unlit shader fade one batch without vertex colors or per-effect materials.
            var pixels = new Color32[AtlasWidth * AtlasHeight];
            for (int type = 0; type < Swatches; type++)
                for (int y = 0; y < Tile; y++)
                    for (int x = 0; x < Tile; x++)
                    {
                        float u = Mathf.Clamp01((x - 1f) / (Tile - 3f)) * 2 - 1;
                        float v = Mathf.Clamp01((y - 1f) / (Tile - 3f));
                        Color color = Swatch((Ink)type, u, v);
                        Color32 rgba = color;
                        for (int a = 0; a < Levels; a++)
                        {
                            rgba.a = (byte)Mathf.RoundToInt(255 * color.a * (a + 1) / Levels);
                            pixels[(a * Tile + y) * AtlasWidth + type * Tile + x] = rgba;
                        }
                    }
            result.SetPixels32(pixels);
            result.Apply(false, true);
            return result;
        }

        static Color Swatch(Ink ink, float x, float v)
        {
            float y = 2 * v - 1;
            float r = Mathf.Sqrt(x * x + y * y);
            float width = Mathf.Abs(x);
            switch (ink)
            {
                case Ink.Tongue:
                {
                    float spread = Mathf.Lerp(0.83f, 0.046f, v) * (0.87f + 0.13f * Mathf.Sin(17 * v));
                    float edge = 1 - Mathf.SmoothStep(0, 1, (width / spread - 0.35f) / 0.65f);
                    float cap = Mathf.SmoothStep(0, 1, v * 16) * (1 - Mathf.SmoothStep(0, 1, (v - 0.39f) / 0.61f));
                    var tint = Color.Lerp(new Color(1, 0.92f, 0.62f), new Color(1, 0.38f, 0.09f), v * 0.66f + (1 - edge) * 0.34f);
                    tint.a = edge * cap;
                    return tint;
                }
                case Ink.Core:
                    return new Color(1, 0.96f, 0.74f, 0.96f);
                case Ink.CoreEdge:
                    return new Color(1, 0.72f, 0.31f, 0.88f);
                case Ink.Vapor:
                case Ink.Dust:
                {
                    float waves = Mathf.Sin(8 * x + 5 * y) * Mathf.Cos(7 * y - 3 * x);
                    float grain = 0.5f + 0.5f * Mathf.Sin(19 * x + 12 * y) * Mathf.Sin(14 * y - 5 * x);
                    float fade = 1 - Mathf.SmoothStep(0, 1, r / (0.84f + 0.16f * waves));
                    var tint = ink == Ink.Dust ? Color.Lerp(new Color(0.44f, 0.34f, 0.24f), new Color(0.78f, 0.64f, 0.45f), grain)
                        : Color.Lerp(new Color(0.39f, 0.45f, 0.48f), new Color(0.73f, 0.71f, 0.64f), grain);
                    tint.a = fade * (0.64f + 0.36f * grain);
                    return tint;
                }
                case Ink.Streak:
                {
                    float taper = 1 - Mathf.SmoothStep(0, 1, width);
                    float cap = Mathf.SmoothStep(0, 1, v * 6) * (1 - Mathf.SmoothStep(0, 1, (v - 0.76f) / 0.24f));
                    return new Color(1, 0.53f + 0.35f * taper, 0.22f + 0.45f * taper, taper * cap);
                }
                case Ink.Signal:
                {
                    float core = 1 - Mathf.SmoothStep(0, 1, r);
                    return new Color(0.58f + 0.4f * core, 0.86f + 0.13f * core, 1, core * core);
                }
                case Ink.Hole:
                case Ink.Ding:
                {
                    float angle = Mathf.Atan2(y, x);
                    float chipped = r * (1 + 0.12f * Mathf.Sin(angle * 7) + 0.075f * Mathf.Cos(angle * 13));
                    float core = 1 - Mathf.SmoothStep(0, 1, (chipped - 0.22f) / 0.25f);
                    float rim = 1 - Mathf.SmoothStep(0, 1, Mathf.Abs(chipped - 0.51f) / 0.14f);
                    float crack = Mathf.Pow(Mathf.Max(0, Mathf.Sin(angle * 6 + r * 2)), 14) * (1 - r * r);
                    var tint = Color.Lerp(new Color(0.075f, 0.066f, 0.055f), new Color(0.55f, 0.47f, 0.37f), rim * (1 - core));
                    tint.a = (core + 0.66f * rim * (1 - core) + 0.23f * crack) * (1 - Mathf.SmoothStep(0.72f, 1, r));
                    if (ink == Ink.Ding) tint.a *= 0.66f;
                    return tint;
                }
                case Ink.Scrape:
                {
                    float taper = 1 - Mathf.SmoothStep(0, 1, (Mathf.Abs(y) - 0.30f) / 0.70f);
                    float edge = Mathf.Abs(x + y * 0.17f - Mathf.Sin(8 * y) * 0.06f) / (0.19f + 0.49f * taper);
                    float mask = (1 - Mathf.SmoothStep(0, 1, edge)) * taper;
                    var tint = Color.Lerp(new Color(0.18f, 0.15f, 0.12f), new Color(0.68f, 0.60f, 0.48f), Mathf.Clamp01(edge * 1.5f));
                    tint.a = mask * 0.91f;
                    return tint;
                }
                default:
                {
                    float core = 1 - Mathf.SmoothStep(0, 1, r);
                    return new Color(1, 0.38f + 0.5f * core, 0.12f + 0.43f * core, core * core);
                }
            }
        }
    }
}
