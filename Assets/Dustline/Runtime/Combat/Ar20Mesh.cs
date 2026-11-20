using System.Collections.Generic;
using UnityEngine;

namespace Dustline
{
    sealed class Ar20Mesh
    {
        public const float FaceZ = 0.551f;
        enum Finish { Ink, Blue, Porcelain, Amber, Edge, Warm, Signal, Deep }
        const int Swatches = 8;
        static Ar20Mesh shared, baton;
        readonly bool melee;
        int users;
        readonly Texture2D colorMap, finishMap, lightMap;
        public readonly Mesh Mesh;
        public readonly Material Material;

        public static Ar20Mesh Acquire(bool melee)
        {
            if (melee)
            {
                if (baton == null) baton = new Ar20Mesh(true);
                baton.users++;
                return baton;
            }
            if (shared == null) shared = new Ar20Mesh(false);
            shared.users++;
            return shared;
        }

        public void Release()
        {
            if (--users != 0) return;
            Object.Destroy(Mesh);
            Object.Destroy(Material);
            Object.Destroy(colorMap);
            Object.Destroy(finishMap);
            Object.Destroy(lightMap);
            if (melee) baton = null;
            else shared = null;
        }

        Ar20Mesh(bool melee)
        {
            this.melee = melee;
            string name = melee ? "Service baton" : "AR-20";
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new System.InvalidOperationException("Missing URP Lit shader for AR-20.");
            colorMap = Palette("AR-20 / color", false, new[] {
                Rgb(0x29333C), Rgb(0x436577), Rgb(0xBDC5BD), Rgb(0xDE9347),
                Rgb(0x778C91), Rgb(0xECE1C5), Rgb(0x68D9D7), Rgb(0x172129)
            });
            finishMap = Palette("AR-20 / finish", true, new[] {
                Gloss(30, 98), Gloss(48, 115), Gloss(16, 114), Gloss(40, 128),
                Gloss(72, 150), Gloss(20, 103), Gloss(23, 178), Gloss(4, 85)
            });
            lightMap = Palette("AR-20 / indicators", false, new[] {
                Rgb(0), Rgb(0), Rgb(0), Rgb(0), Rgb(0), Rgb(0), Rgb(0x55C8C7), Rgb(0)
            });
            Material = new Material(shader) { name = name + " / shared surface" };
            Material.SetTexture("_BaseMap", colorMap);
            Material.SetTexture("_MetallicGlossMap", finishMap);
            Material.SetTexture("_EmissionMap", lightMap);
            Material.SetFloat("_Smoothness", 1);
            Material.SetFloat("_Metallic", 0.16f);
            Material.SetColor("_EmissionColor", new Color(0.54f, 0.79f, 0.76f));
            Material.EnableKeyword("_METALLICSPECGLOSSMAP");
            Material.EnableKeyword("_EMISSION");
            Material.enableInstancing = true;
            Mesh = melee ? BuildBaton() : Build();
            Mesh.name = name + " / sculpt";
        }

        static Color32 Rgb(int rgb) => new Color32((byte)(rgb >> 16), (byte)((rgb >> 8) & 255), (byte)(rgb & 255), 255);
        static Color32 Gloss(byte metallic, byte smoothness) => new Color32(metallic, 0, 0, smoothness);

        static Texture2D Palette(string name, bool linear, Color32[] colors)
        {
            const int span = 4;
            var texture = new Texture2D(Swatches * span, span, TextureFormat.RGBA32, false, linear) {
                name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[Swatches * span * span];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = colors[i / span % Swatches];
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        static Mesh Build()
        {
            var b = new Sculpt();
            b.Loft(Finish.Ink, S(-0.246f, 0.009f, 0.041f, 0.048f, 0.012f),
                S(-0.184f, 0.014f, 0.067f, 0.070f, 0.016f), S(0.068f, 0.017f, 0.072f, 0.074f, 0.017f),
                S(0.154f, 0.025f, 0.062f, 0.065f, 0.017f), S(0.345f, 0.038f, 0.048f, 0.047f, 0.012f),
                S(0.443f, 0.031f, 0.036f, 0.035f, 0.012f));
            b.Loft(Finish.Blue, S(-0.275f, 0.064f, 0.031f, 0.017f, 0.007f),
                S(-0.205f, 0.084f, 0.043f, 0.035f, 0.010f), S(0.075f, 0.084f, 0.047f, 0.034f, 0.012f),
                S(0.148f, 0.087f, 0.043f, 0.024f, 0.009f), S(0.286f, 0.077f, 0.036f, 0.018f, 0.007f),
                S(0.390f, 0.060f, 0.029f, 0.015f, 0.006f));
            b.Loft(Finish.Edge, S(-0.177f, 0.115f, 0.029f, 0.006f, 0.003f),
                S(-0.135f, 0.121f, 0.027f, 0.008f, 0.004f), S(0.133f, 0.118f, 0.027f, 0.007f, 0.004f),
                S(0.246f, 0.096f, 0.022f, 0.007f, 0.003f));
            b.Loft(Finish.Deep, S(-0.113f, 0.125f, 0.019f, 0.005f, 0.003f),
                S(-0.086f, 0.131f, 0.023f, 0.005f, 0.003f), S(0.054f, 0.131f, 0.023f, 0.006f, 0.003f),
                S(0.083f, 0.124f, 0.015f, 0.005f, 0.003f));
            for (int i = 0; i < 3; i++)
            {
                float mark = -0.070f + 0.044f * i;
                b.Loft(i == 2 ? Finish.Amber : Finish.Edge, S(mark, 0.135f, 0.014f, 0.004f, 0.002f),
                    S(mark + 0.008f, 0.138f, 0.018f, 0.004f, 0.003f), S(mark + 0.022f, 0.135f, 0.015f, 0.004f, 0.002f));
            }

            b.Loft(Finish.Ink, S(-0.396f, 0.002f, 0.025f, 0.052f, 0.009f),
                S(-0.378f, 0.009f, 0.040f, 0.066f, 0.012f), S(-0.346f, 0.016f, 0.038f, 0.059f, 0.010f));
            b.Loft(Finish.Blue, S(-0.362f, 0.062f, 0.031f, 0.014f, 0.006f),
                S(-0.297f, 0.067f, 0.033f, 0.013f, 0.006f), S(-0.200f, 0.031f, 0.047f, 0.031f, 0.010f));
            b.Loft(Finish.Blue, S(-0.356f, -0.039f, 0.029f, 0.014f, 0.006f),
                S(-0.303f, -0.037f, 0.028f, 0.013f, 0.006f), S(-0.223f, -0.008f, 0.043f, 0.018f, 0.007f));
            b.Loft(Finish.Amber, S(-0.397f, -0.003f, 0.027f, 0.034f, 0.009f),
                S(-0.392f, -0.003f, 0.029f, 0.037f, 0.010f), S(-0.385f, -0.003f, 0.027f, 0.036f, 0.009f));

            b.Loft(Finish.Ink, S(-0.040f, -0.105f, 0.034f, 0.044f, 0.010f),
                S(-0.012f, -0.134f, 0.050f, 0.081f, 0.013f), S(0.056f, -0.124f, 0.057f, 0.084f, 0.013f),
                S(0.123f, -0.088f, 0.045f, 0.046f, 0.012f), S(0.151f, -0.047f, 0.033f, 0.024f, 0.009f));
            b.Loft(Finish.Ink, S(-0.214f, -0.110f, 0.023f, 0.035f, 0.009f),
                S(-0.181f, -0.104f, 0.035f, 0.089f, 0.012f), S(-0.148f, -0.080f, 0.031f, 0.074f, 0.009f),
                S(-0.118f, -0.061f, 0.026f, 0.037f, 0.008f));

            b.Loft(Finish.Blue, S(0.370f, 0.029f, 0.047f, 0.050f, 0.014f),
                S(0.408f, 0.028f, 0.054f, 0.048f, 0.013f), S(0.470f, 0.028f, 0.050f, 0.044f, 0.013f));
            b.Aperture(Finish.Ink, Finish.Amber, Finish.Deep, 0.451f, 0.503f, FaceZ - 0.008f, 0.028f);

            for (int side = -1; side <= 1; side += 2)
            {
                b.Plate(Finish.Porcelain, side, 0.036f, 0.077f, 0.105f,
                    P(-0.194f, 0.006f), P(-0.170f, -0.037f), P(0.055f, -0.046f), P(0.133f, -0.016f),
                    P(0.170f, 0.047f), P(0.099f, 0.081f), P(-0.128f, 0.079f));
                b.Plate(Finish.Blue, side, 0.056f, 0.082f, 0.14f,
                    P(-0.155f, 0.006f), P(-0.127f, -0.022f), P(0.021f, -0.027f), P(0.072f, 0.013f),
                    P(0.050f, 0.051f), P(-0.121f, 0.055f));
                b.Plate(Finish.Amber, side, 0.062f, 0.082f, 0.12f,
                    P(0.079f, -0.009f), P(0.116f, 0.000f), P(0.147f, 0.043f), P(0.113f, 0.058f));
                b.Plate(Finish.Edge, side, 0.038f, 0.060f, 0.11f,
                    P(0.147f, -0.018f), P(0.189f, -0.025f), P(0.343f, -0.009f), P(0.382f, 0.030f),
                    P(0.331f, 0.068f), P(0.194f, 0.054f));
                b.Plate(Finish.Deep, side, 0.048f, 0.063f, 0.10f,
                    P(0.182f, -0.013f), P(0.325f, 0.003f), P(0.353f, 0.029f), P(0.322f, 0.049f), P(0.201f, 0.040f));
                for (int i = 0; i < 4; i++)
                {
                    float z = 0.196f + 0.033f * i;
                    b.Plate(i == 3 ? Finish.Amber : Finish.Blue, side, 0.058f, 0.068f, 0.15f,
                        P(z, -0.011f + 0.003f * i), P(z + 0.013f, -0.009f + 0.003f * i),
                        P(z + 0.029f, 0.043f + 0.001f * i), P(z + 0.013f, 0.041f + 0.001f * i));
                }
                b.Plate(Finish.Amber, side, 0.027f, 0.047f, 0.18f,
                    P(-0.351f, 0.054f), P(-0.237f, 0.021f), P(-0.238f, 0.051f), P(-0.297f, 0.077f));
                b.Plate(Finish.Blue, side, 0.028f, 0.060f, 0.11f,
                    P(-0.018f, -0.119f), P(0.004f, -0.193f), P(0.045f, -0.201f), P(0.091f, -0.159f),
                    P(0.105f, -0.090f), P(0.042f, -0.072f));
                b.Plate(Finish.Warm, side, 0.052f, 0.063f, 0.16f,
                    P(0.002f, -0.158f), P(0.027f, -0.168f), P(0.079f, -0.134f), P(0.080f, -0.116f));
                b.Plate(Finish.Amber, side, 0.043f, 0.060f, 0.15f,
                    P(0.003f, -0.191f), P(0.043f, -0.201f), P(0.061f, -0.185f), P(0.009f, -0.176f));
                b.Plate(Finish.Blue, side, 0.016f, 0.037f, 0.15f,
                    P(-0.196f, -0.142f), P(-0.172f, -0.177f), P(-0.152f, -0.161f), P(-0.135f, -0.082f), P(-0.161f, -0.096f));
                b.Plate(Finish.Ink, side, 0.012f, 0.036f, 0.18f,
                    P(-0.213f, 0.096f), P(-0.154f, 0.100f), P(-0.167f, 0.145f), P(-0.203f, 0.139f));
                b.Plate(Finish.Signal, side, 0.020f, 0.038f, 0.12f,
                    P(-0.213f, 0.125f), P(-0.200f, 0.127f), P(-0.198f, 0.137f), P(-0.207f, 0.138f));
                b.Plate(Finish.Ink, side, 0.006f, 0.020f, 0.12f,
                    P(0.313f, 0.074f), P(0.367f, 0.073f), P(0.354f, 0.111f), P(0.332f, 0.112f));
                b.Plate(Finish.Signal, side, 0.011f, 0.022f, 0.10f,
                    P(0.324f, 0.096f), P(0.353f, 0.096f), P(0.350f, 0.107f), P(0.332f, 0.107f));
                b.Plate(Finish.Signal, side, 0.076f, 0.084f, 0.10f,
                    P(-0.110f, 0.031f), P(-0.066f, 0.031f), P(-0.061f, 0.039f), P(-0.104f, 0.039f));
                b.Marking(side, 0.083f, -0.043f, 0.007f);
            }
            return b.FinishMesh();
        }

        static Mesh BuildBaton()
        {
            var b = new Sculpt();
            b.Loft(Finish.Blue, S(-0.12f, 0, 0.027f, 0.029f, 0.008f),
                S(-0.09f, 0, 0.035f, 0.037f, 0.012f), S(0.052f, 0, 0.034f, 0.035f, 0.012f),
                S(0.14f, 0, 0.028f, 0.035f, 0.011f), S(0.40f, 0.005f, 0.041f, 0.047f, 0.015f),
                S(0.44f, 0.001f, 0.035f, 0.04f, 0.015f));
            for (int i = 0; i < 7; i++)
            {
                float z = -0.085f + i * 0.024f;
                b.Loft(Finish.Ink, S(z, 0, 0.036f, 0.038f, 0.011f),
                    S(z + 0.005f, 0, 0.041f, 0.045f, 0.013f), S(z + 0.013f, 0, 0.036f, 0.038f, 0.011f));
            }
            b.Loft(Finish.Amber, S(0.11f, 0, 0.035f, 0.042f, 0.012f),
                S(0.121f, 0, 0.053f, 0.056f, 0.016f), S(0.151f, 0, 0.039f, 0.045f, 0.013f));
            b.Loft(Finish.Edge, S(0.393f, 0.003f, 0.042f, 0.050f, 0.015f),
                S(0.43f, 0.002f, 0.047f, 0.047f, 0.015f), S(0.453f, 0, 0.033f, 0.038f, 0.013f));
            for (int side = -1; side <= 1; side += 2)
                b.Plate(Finish.Amber, side, 0.026f, 0.041f, 0.17f,
                    P(0.162f, -0.020f), P(0.288f, -0.027f), P(0.368f, 0.013f), P(0.209f, 0.027f));
            return b.FinishMesh();
        }

        static Vector2 P(float z, float y) => new Vector2(z, y);
        static Section S(float z, float y, float width, float height, float bevel) => new Section(z, y, width, height, bevel);

        readonly struct Section
        {
            public readonly Vector3 Center;
            readonly float width, height, bevel;
            public Section(float z, float y, float width, float height, float bevel)
            {
                Center = new Vector3(0, y, z);
                this.width = width;
                this.height = height;
                this.bevel = bevel;
            }
            public Vector3 Point(int i)
            {
                float a = width - bevel, b = height - bevel;
                switch (i)
                {
                    case 0: return Center + new Vector3(a, height, 0);
                    case 1: return Center + new Vector3(-a, height, 0);
                    case 2: return Center + new Vector3(-width, b, 0);
                    case 3: return Center + new Vector3(-width, -b, 0);
                    case 4: return Center + new Vector3(-a, -height, 0);
                    case 5: return Center + new Vector3(a, -height, 0);
                    case 6: return Center + new Vector3(width, -b, 0);
                    default: return Center + new Vector3(width, b, 0);
                }
            }
        }

        sealed class Sculpt
        {
            readonly List<Vector3> points = new List<Vector3>(2600);
            readonly List<Vector3> normals = new List<Vector3>(2600);
            readonly List<Vector2> uv = new List<Vector2>(2600);
            readonly List<int> indices = new List<int>(3600);

            int Face(Vector3 a, Vector3 b, Vector3 c, Finish finish)
            {
                int start = points.Count;
                Vector3 n = Vector3.Cross(b - a, c - a);
                n /= Mathf.Sqrt(n.sqrMagnitude);
                var tint = new Vector2(((int)finish + 0.5f) / Swatches, 0.5f);
                points.Add(a); points.Add(b); points.Add(c);
                normals.Add(n); normals.Add(n); normals.Add(n);
                uv.Add(tint); uv.Add(tint); uv.Add(tint);
                indices.Add(start); indices.Add(start + 1); indices.Add(start + 2);
                return start;
            }

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Finish finish, bool flip = false)
            {
                if (flip) { var t = b; b = d; d = t; }
                int start = Face(a, b, c, finish);
                points.Add(d); normals.Add(normals[start]); uv.Add(uv[start]);
                indices.Add(start); indices.Add(start + 2); indices.Add(start + 3);
            }

            public void Loft(Finish finish, params Section[] sections)
            {
                var first = sections[0];
                var last = sections[sections.Length - 1];
                for (int i = 0; i < 8; i++)
                {
                    int next = (i + 1) % 8;
                    Face(first.Center, first.Point(next), first.Point(i), finish);
                    Face(last.Center, last.Point(i), last.Point(next), finish);
                    for (int j = 1; j < sections.Length; j++)
                        Quad(sections[j - 1].Point(i), sections[j - 1].Point(next), sections[j].Point(next), sections[j].Point(i), finish);
                }
            }

            public void Plate(Finish finish, int side, float inner, float outer, float bevel, params Vector2[] contour)
            {
                Vector2 center = Vector2.zero;
                foreach (var p in contour) center += p;
                center /= contour.Length;
                Vector3 centerFace = AtSide(side * outer, center);
                for (int i = 0; i < contour.Length; i++)
                {
                    Vector2 a = contour[i], b = contour[(i + 1) % contour.Length];
                    Vector3 frontA = AtSide(side * outer, Vector2.Lerp(a, center, bevel));
                    Vector3 frontB = AtSide(side * outer, Vector2.Lerp(b, center, bevel));
                    Quad(AtSide(side * inner, a), AtSide(side * inner, b), frontB, frontA, finish, side > 0);
                    if (side < 0) Face(centerFace, frontA, frontB, finish);
                    else Face(centerFace, frontB, frontA, finish);
                }
            }

            public void Aperture(Finish rim, Finish lip, Finish inset, float rear, float shoulder, float front, float y)
            {
                const int sides = 12;
                for (int i = 0; i < sides; i++)
                {
                    float a = (i + 0.5f) * 2 * Mathf.PI / sides;
                    float b = (i + 1.5f) * 2 * Mathf.PI / sides;
                    var va = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                    var vb = new Vector3(Mathf.Cos(b), Mathf.Sin(b), 0);
                    var back = new Vector3(0, y, rear);
                    var ridge = new Vector3(0, y, shoulder);
                    var face = new Vector3(0, y, front);
                    var inside = new Vector3(0, y, shoulder + 0.006f);
                    Vector3 outerA = face + va * 0.037f, outerB = face + vb * 0.037f;
                    Vector3 innerA = face + va * 0.022f, innerB = face + vb * 0.022f;
                    Quad(back + va * 0.037f, back + vb * 0.037f, ridge + vb * 0.054f, ridge + va * 0.054f, rim);
                    Quad(ridge + va * 0.054f, ridge + vb * 0.054f, outerB, outerA, i % 3 == 1 ? lip : rim);
                    Quad(outerA, outerB, innerB, innerA, i % 3 == 1 ? lip : Finish.Edge);
                    Quad(innerA, innerB, inside + vb * 0.019f, inside + va * 0.019f, inset);
                    Face(inside, inside + va * 0.019f, inside + vb * 0.019f, inset);
                }
            }

            public void Marking(int side, float x, float z, float y)
            {
                int[] glyphs = { 0b010_101_111_101_101, 0b110_101_110_101_101, 0b000_000_111_000_000,
                    0b110_001_010_100_111, 0b111_101_101_101_111 };
                const float step = 0.0054f, half = 0.00255f;
                for (int l = 0; l < glyphs.Length; l++)
                    for (int row = 0; row < 5; row++)
                        for (int col = 0; col < 3; col++)
                        {
                            if ((glyphs[l] & (1 << (14 - row * 3 - col))) == 0) continue;
                            float u = z + side * (4 * l + col - 9) * step, v = y + (2 - row) * step;
                            float at = x * side;
                            Quad(new Vector3(at, v - half, u - half), new Vector3(at, v - half, u + half),
                                new Vector3(at, v + half, u + half), new Vector3(at, v + half, u - half), Finish.Warm, side > 0);
                        }
            }

            static Vector3 AtSide(float x, Vector2 zy) => new Vector3(x, zy.y, zy.x);

            public Mesh FinishMesh()
            {
                var mesh = new Mesh { name = "AR-20 / sculpt" };
                mesh.SetVertices(points);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uv);
                mesh.SetTriangles(indices, 0);
                mesh.UploadMeshData(true);
                return mesh;
            }
        }
    }
}
