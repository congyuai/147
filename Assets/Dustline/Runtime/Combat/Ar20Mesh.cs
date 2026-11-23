using System.Collections.Generic;
using UnityEngine;

namespace Dustline
{
    sealed partial class Ar20Mesh
    {
        public static readonly Vector3 Muzzle = new Vector3(0f, 0.032f, 0.518f);
        public static readonly Vector3 RightPalm = new Vector3(0.037f, -0.113f, -0.165f);
        public static readonly Vector3 LeftPalm = new Vector3(-0.045f, -0.031f, 0.224f);
        enum Finish
        {
            Ink, Blue, Porcelain, Amber, Edge, Warm, Signal, Deep, Steel, Jade, Wrap, Optic,
            Anodized, Graphite, Parkerized, Polymer, Rubber, Machined, Recess, Stencil, Coating, OpticDot
        }
        const int Columns = 8, Rows = 4;
        static Ar20Mesh shared;
        int users;
        readonly Texture2D colorMap, finishMap, lightMap;
        readonly Dictionary<int, Mesh> variants = new Dictionary<int, Mesh>();
        readonly Dictionary<int, Mesh> parts = new Dictionary<int, Mesh>();
        readonly Dictionary<string, Mesh> objects = new Dictionary<string, Mesh>(System.StringComparer.Ordinal);
        readonly Mesh[] medicine = new Mesh[(int)MedicineForm.Surgery + 1];
        Mesh magazineMesh;
        public readonly Material Material;

        public static Ar20Mesh Acquire()
        {
            if (shared == null) shared = new Ar20Mesh();
            shared.users++;
            return shared;
        }

        public Mesh GetMesh(WeaponForm form, int sight, bool customGrip = false, bool customStock = false)
        {
            sight = Mathf.Clamp(sight, 0, 4);
            int index = ((int)form << 5) | (sight << 2) | (customGrip ? 2 : 0) | (customStock ? 1 : 0);
            if (variants.TryGetValue(index, out var mesh)) return mesh;
            switch (form)
            {
                case WeaponForm.Baton: mesh = BuildBaton(); break;
                case WeaponForm.Tangdao: mesh = BuildTangdao(); break;
                case WeaponForm.ShortBlade: mesh = BuildShortBlade(); break;
                case WeaponForm.Rifle: mesh = BuildRifle(sight, customGrip, customStock); break;
                default: mesh = BuildFrame(form, sight, customGrip, customStock); break;
            }
            mesh.name = "Dustline / " + form + " / " + index;
            return variants[index] = mesh;
        }

        public Mesh MagazineMesh
        {
            get
            {
                if (magazineMesh != null) return magazineMesh;
                var b = new Sculpt();
                AddMagazine(b);
                magazineMesh = b.FinishMesh();
                magazineMesh.name = "AR-20 magazine prop";
                return magazineMesh;
            }
        }

        public Mesh MedicineMesh(MedicineForm form)
        {
            int index = (int)form;
            if (medicine[index] == null) medicine[index] = BuildMedicine(form);
            return medicine[index];
        }

        public void WarmPlayerViews()
        {
            GetMesh(WeaponForm.Rifle, 0);
            GetMesh(WeaponForm.Baton, 0);
            GetMesh(WeaponForm.Tangdao, 0);
            GetMesh(WeaponForm.ShortBlade, 0);
            for (int i = 0; i < medicine.Length; i++) MedicineMesh((MedicineForm)i);
        }

        public void Release()
        {
            if (--users != 0) return;
            foreach (var mesh in variants.Values) Dispose(mesh);
            foreach (var mesh in parts.Values) Dispose(mesh);
            foreach (var mesh in objects.Values) Dispose(mesh);
            foreach (var mesh in medicine) Dispose(mesh);
            Dispose(magazineMesh);
            Dispose(Material);
            Dispose(colorMap);
            Dispose(finishMap);
            Dispose(lightMap);
            shared = null;
        }

        static void Dispose(Object asset)
        {
            if (asset == null) return;
            if (Application.isPlaying) Object.Destroy(asset);
            else Object.DestroyImmediate(asset);
        }

        Ar20Mesh()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new System.InvalidOperationException("Missing URP Lit shader for weapon visuals.");
            colorMap = Palette("Dustline / weapon colors", false, new[] {
                Rgb(0x29333C), Rgb(0x436577), Rgb(0xBDC5BD), Rgb(0xDE9347),
                Rgb(0x778C91), Rgb(0xECE1C5), Rgb(0x68D9D7), Rgb(0x172129),
                Rgb(0xB4CCD0), Rgb(0x328F85), Rgb(0x453839), Rgb(0x87EFE1),
                Rgb(0x51575B), Rgb(0x3D4143), Rgb(0x444844), Rgb(0x56594E), Rgb(0x2C3030),
                Rgb(0x838786), Rgb(0x232625), Rgb(0x9A9B8F), Rgb(0x364849), Rgb(0xCD533C)
            }, 64);
            finishMap = Palette("Dustline / weapon finishes", true, new[] {
                Gloss(30, 98), Gloss(48, 115), Gloss(16, 114), Gloss(40, 128),
                Gloss(72, 150), Gloss(20, 103), Gloss(23, 178), Gloss(4, 85),
                Gloss(150, 208), Gloss(67, 150), Gloss(6, 62), Gloss(20, 165),
                Gloss(87, 121), Gloss(41, 100), Gloss(83, 94), Gloss(2, 73), Gloss(0, 50),
                Gloss(150, 151), Gloss(1, 54), Gloss(4, 94), Gloss(63, 155), Gloss(1, 127)
            }, 64);
            lightMap = Palette("Dustline / weapon indicators", false, new[] {
                Rgb(0), Rgb(0), Rgb(0), Rgb(0), Rgb(0), Rgb(0), Rgb(0x55C8C7), Rgb(0),
                Rgb(0), Rgb(0), Rgb(0), Rgb(0x83FFFF), Rgb(0), Rgb(0), Rgb(0), Rgb(0),
                Rgb(0), Rgb(0), Rgb(0), Rgb(0), Rgb(0), Rgb(0xFF6844)
            }, 4);
            Material = new Material(shader) { name = "Dustline / weapon surface", hideFlags = HideFlags.DontSave };
            Material.SetColor("_BaseColor", Color.white);
            Material.SetTexture("_BaseMap", colorMap);
            Material.SetTexture("_MetallicGlossMap", finishMap);
            Material.SetTexture("_EmissionMap", lightMap);
            Material.SetFloat("_Smoothness", 1);
            Material.SetFloat("_Metallic", 0.16f);
            Material.SetColor("_EmissionColor", new Color(0.73f, 0.73f, 0.73f));
            Material.EnableKeyword("_METALLICSPECGLOSSMAP");
            Material.EnableKeyword("_EMISSION");
            Material.enableInstancing = true;
        }

        static Color32 Rgb(int rgb) => new Color32((byte)(rgb >> 16), (byte)((rgb >> 8) & 255), (byte)(rgb & 255), 255);
        static Color32 Gloss(byte metallic, byte smoothness) => new Color32(metallic, 0, 0, smoothness);

        static Texture2D Palette(string name, bool linear, Color32[] colors, int tile)
        {
            bool grain = tile > 4;
            var texture = new Texture2D(tile * Columns, tile * Rows, TextureFormat.RGBA32, grain ? 5 : 1, linear)
            {
                name = name, filterMode = grain ? FilterMode.Trilinear : FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp, anisoLevel = grain ? 2 : 1, hideFlags = HideFlags.DontSave
            };
            var pixels = new Color32[texture.width * texture.height];
            for (int y = 0; y < texture.height; y++)
                for (int x = 0; x < texture.width; x++)
                {
                    int slot = x / tile + y / tile * Columns;
                    var color = slot < colors.Length ? colors[slot] : colors[0];
                    if (grain && slot >= (int)Finish.Anodized && slot <= (int)Finish.Coating)
                    {
                        int u = x % tile, v = y % tile;
                        float mottling = Mathf.PerlinNoise(u * 0.106f + slot * 3.7f, v * 0.139f);
                        float brushed = Mathf.PerlinNoise(u * 0.043f + slot * 2.1f, v * 0.61f);
                        float speckle = ((u * 71 ^ v * 131 ^ u * v * 313) & 31) / 31f - 0.5f;
                        bool polymer = slot == (int)Finish.Polymer || slot == (int)Finish.Rubber;
                        float variation = (mottling - 0.5f) * 9f + speckle * (polymer ? 15f : 8f);
                        if (!polymer) variation += (brushed - 0.5f) * 12f;
                        if (linear) color.a = Channel(color.a + variation * 1.25f);
                        else color = new Color32(Channel(color.r + variation), Channel(color.g + variation), Channel(color.b + variation), 255);
                    }
                    pixels[y * texture.width + x] = color;
                }
            texture.SetPixels32(pixels);
            texture.Apply(grain, true);
            return texture;
        }

        static byte Channel(float value) => (byte)Mathf.Clamp(Mathf.RoundToInt(value), 0, 255);

        public static float SightHeight(int level) => level == 2 || level == 4 ? 0.164f : level > 0 ? 0.148f : 0.132f;

        static Mesh BuildRifle(int sight, bool customGrip, bool customStock)
        {
            var b = new Sculpt();
            if (!customStock)
            {
            b.Loft(Finish.Polymer, S(-0.411f, -0.005f, 0.035f, 0.065f, 0.010f),
                S(-0.364f, 0.001f, 0.036f, 0.058f, 0.010f), S(-0.303f, 0.019f, 0.033f, 0.036f, 0.010f),
                S(-0.235f, 0.032f, 0.029f, 0.026f, 0.008f));
            b.Loft(Finish.Rubber, S(-0.430f, -0.008f, 0.031f, 0.069f, 0.010f),
                S(-0.424f, -0.007f, 0.036f, 0.073f, 0.009f), S(-0.408f, -0.005f, 0.037f, 0.068f, 0.009f));
            b.Loft(Finish.Graphite, S(-0.399f, 0.045f, 0.025f, 0.013f, 0.006f),
                S(-0.367f, 0.059f, 0.033f, 0.012f, 0.006f), S(-0.303f, 0.058f, 0.031f, 0.012f, 0.006f),
                S(-0.231f, 0.042f, 0.026f, 0.011f, 0.005f));

            }
            b.Loft(Finish.Anodized, S(-0.248f, 0.039f, 0.027f, 0.033f, 0.007f),
                S(-0.218f, 0.040f, 0.038f, 0.047f, 0.008f), S(0.126f, 0.040f, 0.037f, 0.047f, 0.008f),
                S(0.165f, 0.034f, 0.034f, 0.040f, 0.007f));
            b.Loft(Finish.Graphite, S(-0.213f, -0.014f, 0.030f, 0.018f, 0.007f),
                S(-0.182f, -0.014f, 0.034f, 0.023f, 0.006f), S(0.084f, -0.019f, 0.040f, 0.029f, 0.006f),
                S(0.137f, -0.011f, 0.034f, 0.020f, 0.006f));
            b.Loft(Finish.Parkerized, S(0.130f, 0.027f, 0.034f, 0.047f, 0.010f),
                S(0.153f, 0.027f, 0.042f, 0.048f, 0.010f), S(0.343f, 0.029f, 0.037f, 0.039f, 0.009f),
                S(0.378f, 0.031f, 0.028f, 0.031f, 0.008f));
            b.Loft(Finish.Graphite, S(-0.197f, 0.086f, 0.022f, 0.005f, 0.002f),
                S(-0.177f, 0.087f, 0.024f, 0.008f, 0.003f), S(0.168f, 0.084f, 0.023f, 0.008f, 0.003f),
                S(0.313f, 0.074f, 0.019f, 0.005f, 0.002f));
            for (int i = 0; i < 17; i++)
            {
                float z = -0.179f + 0.029f * i;
                float y = Mathf.Lerp(0.096f, 0.080f, Mathf.InverseLerp(0.11f, 0.31f, z));
                b.Box(Finish.Anodized, 0f, y, z, 0.043f, 0.006f, 0.017f);
            }

            Quaternion down = Quaternion.Euler(90f, 0f, 0f);
            var grip = new Vector3(0f, -0.024f, -0.140f);
            if (!customGrip)
            {
            b.Loft(Finish.Polymer, grip, down, S(0f, 0f, 0.024f, 0.032f, 0.008f),
                S(0.027f, -0.006f, 0.028f, 0.033f, 0.009f), S(0.092f, -0.026f, 0.025f, 0.027f, 0.007f),
                S(0.137f, -0.040f, 0.022f, 0.023f, 0.007f));
            b.Loft(Finish.Rubber, grip, down, S(0.124f, -0.037f, 0.025f, 0.027f, 0.007f),
                S(0.142f, -0.042f, 0.025f, 0.026f, 0.007f), S(0.151f, -0.043f, 0.022f, 0.024f, 0.006f));
            }
            b.Loft(Finish.Graphite, S(-0.117f, -0.049f, 0.026f, 0.030f, 0.008f),
                S(-0.079f, -0.081f, 0.019f, 0.005f, 0.002f), S(-0.033f, -0.074f, 0.020f, 0.005f, 0.002f),
                S(-0.007f, -0.046f, 0.022f, 0.024f, 0.007f));

            b.Loft(Finish.Parkerized, S(0.347f, 0.032f, 0.018f, 0.018f, 0.006f),
                S(0.395f, 0.032f, 0.016f, 0.016f, 0.005f), S(0.475f, 0.032f, 0.013f, 0.013f, 0.005f));
            b.Aperture(Finish.Graphite, Finish.Machined, Finish.Recess, 0.456f, 0.478f, Muzzle.z - 0.005f, Muzzle.y);

            for (int side = -1; side <= 1; side += 2)
            {
                b.Plate(Finish.Anodized, side, 0.035f, 0.043f, 0.075f,
                    P(-0.195f, 0.018f), P(-0.153f, 0.005f), P(0.099f, 0.005f), P(0.126f, 0.032f),
                    P(0.103f, 0.065f), P(-0.171f, 0.067f));
                b.Plate(side > 0 ? Finish.Recess : Finish.Parkerized, side, 0.042f, 0.045f, 0.10f,
                    P(-0.115f, 0.038f), P(0.050f, 0.037f), P(0.068f, 0.051f), P(0.050f, 0.061f), P(-0.106f, 0.061f));
                b.Plate(Finish.Machined, side, 0.044f, 0.046f, 0.14f,
                    P(-0.094f, 0.044f), P(0.031f, 0.043f), P(0.044f, 0.050f), P(-0.086f, 0.052f));
                b.Marking(side, 0.044f, -0.039f, 0.022f);
                b.Plate(Finish.Polymer, side, 0.033f, 0.046f, 0.13f,
                    P(0.151f, 0.004f), P(0.181f, -0.009f), P(0.311f, 0.000f), P(0.356f, 0.020f),
                    P(0.328f, 0.045f), P(0.169f, 0.048f));
                for (int i = 0; i < 4; i++)
                {
                    float z = 0.175f + 0.041f * i;
                    float y = 0.017f + 0.003f * i;
                    b.Plate(Finish.Recess, side, 0.045f, 0.0475f, 0.10f,
                        P(z, y), P(z + 0.023f, y), P(z + 0.032f, y + 0.012f), P(z + 0.007f, y + 0.012f));
                }
                if (!customStock) b.Plate(Finish.Parkerized, side, 0.029f, 0.037f, 0.13f,
                    P(-0.388f, -0.032f), P(-0.356f, -0.030f), P(-0.307f, -0.006f), P(-0.338f, 0.006f), P(-0.389f, -0.005f));
                b.Box(Finish.Graphite, side * 0.040f, -0.011f, -0.138f, 0.008f, 0.016f, 0.027f);
                b.Box(Finish.Anodized, side * 0.039f, -0.017f, 0.071f, 0.006f, 0.019f, 0.028f);
            }
            if (sight == 0) BuildSights(b, 0);
            return b.FinishMesh();
        }

        static void AddMagazine(Sculpt b)
        {
            var mount = new Vector3(0f, -0.032f, 0.048f);
            var rotation = Quaternion.Euler(90f, 0f, 0f);
            b.Loft(Finish.Graphite, mount, rotation, S(0f, 0f, 0.024f, 0.042f, 0.008f),
                S(0.059f, -0.004f, 0.026f, 0.046f, 0.008f), S(0.124f, 0.011f, 0.027f, 0.046f, 0.009f),
                S(0.180f, 0.034f, 0.026f, 0.045f, 0.009f));
            b.Loft(Finish.Polymer, mount, rotation, S(0.167f, 0.028f, 0.027f, 0.046f, 0.008f),
                S(0.177f, 0.033f, 0.030f, 0.048f, 0.009f), S(0.192f, 0.040f, 0.027f, 0.046f, 0.009f));
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 3; i++)
                {
                    float z = 0.024f + 0.021f * i;
                    b.Plate(Finish.Parkerized, side, 0.024f, 0.028f, 0.16f,
                        P(z - 0.004f, -0.081f), P(z + 0.005f, -0.081f), P(z + 0.038f, -0.190f), P(z + 0.026f, -0.193f));
                }
        }

        static Mesh BuildMedicine(MedicineForm form)
        {
            var b = new Sculpt();
            if (form == MedicineForm.Roll)
            {
                b.Loft(Finish.Warm, S(-0.055f, 0, 0.036f, 0.036f, 0.020f),
                    S(-0.043f, 0, 0.040f, 0.040f, 0.023f), S(0.047f, 0, 0.040f, 0.040f, 0.023f),
                    S(0.058f, 0, 0.033f, 0.033f, 0.016f));
                b.Box(Finish.Jade, 0, 0.022f, 0f, 0.070f, 0.044f, 0.035f);
            }
            else if (form == MedicineForm.Splint)
            {
                b.Box(Finish.Warm, 0, 0f, 0, .072f, .021f, .158f);
                b.Box(Finish.Blue, 0, .012f, -.042f, .077f, .013f, .033f);
                b.Box(Finish.Blue, 0, .012f, .042f, .077f, .013f, .033f);
            }
            else if (form == MedicineForm.Tourniquet)
            {
                b.Box(Finish.Wrap, 0, 0, 0, .108f, .037f, .072f);
                b.Box(Finish.Amber, .033f, .017f, 0, .024f, .020f, .067f);
                b.Box(Finish.Ink, -.008f, .023f, 0, .028f, .018f, .102f);
            }
            else if (form == MedicineForm.Tablets)
            {
                b.Loft(Finish.Porcelain, S(-.047f, 0, .034f, .033f, .017f), S(.037f, 0, .036f, .034f, .017f));
                b.Loft(Finish.Blue, S(.035f, 0, .039f, .037f, .018f), S(.052f, 0, .038f, .037f, .018f));
            }
            else
            {
                bool kit = form == MedicineForm.Case || form == MedicineForm.Surgery;
                float width = form == MedicineForm.Surgery ? .205f : kit ? 0.18f : 0.15f;
                b.Box(form == MedicineForm.Surgery ? Finish.Blue : kit ? Finish.Amber : Finish.Jade, 0, 0, 0, width, 0.063f, 0.122f);
                b.Box(Finish.Graphite, 0, 0.019f, 0.056f, width * 0.91f, 0.047f, 0.024f);
                b.Box(Finish.Porcelain, 0, 0.033f, 0f, 0.051f, 0.005f, 0.014f);
                b.Box(Finish.Porcelain, 0, 0.034f, 0f, 0.015f, 0.004f, 0.055f);
            }
            var mesh = b.FinishMesh();
            mesh.name = "Treatment prop / " + form;
            return mesh;
        }

        static void BuildSights(Sculpt b, int sight)
        {
            float y = SightHeight(sight);
            if (sight == 0)
            {
                b.Box(Finish.Graphite, 0, 0.103f, -0.166f, 0.047f, 0.018f, 0.054f);
                b.Window(Finish.Anodized, Finish.Recess, -0.185f, -0.155f, y, 0.022f, 0.023f, 0.013f, 0.014f);
                b.Box(Finish.Parkerized, 0, 0.091f, 0.307f, 0.047f, 0.027f, 0.042f);
                for (int side = -1; side <= 1; side += 2)
                    b.Box(Finish.Graphite, side * 0.018f, 0.121f, 0.307f, 0.009f, 0.041f, 0.029f);
                b.Box(Finish.Graphite, 0, y - 0.014f, 0.307f, 0.008f, 0.028f, 0.012f);
                b.Box(Finish.Stencil, 0, y - 0.001f, 0.300f, 0.004f, 0.003f, 0.003f);
                return;
            }

            bool enclosed = sight == 2 || sight == 4;
            bool aftermarket = sight >= 3;
            float rear = enclosed ? -0.142f : -0.117f;
            float front = enclosed ? (aftermarket ? .062f : .037f) : (aftermarket ? -.040f : -.066f);
            float width = enclosed ? 0.034f : 0.038f, height = enclosed ? 0.032f : 0.028f;
            float holeWidth = enclosed ? 0.025f : 0.030f, holeHeight = enclosed ? 0.024f : 0.021f;
            b.Box(Finish.Graphite, 0, 0.103f, (rear + front) * 0.5f, 0.056f, 0.018f, front - rear + 0.023f);
            float foot = y - height;
            b.Box(Finish.Parkerized, 0, (foot + 0.107f) * 0.5f, (rear + front) * 0.5f,
                0.046f, foot - 0.107f + 0.008f, (front - rear) * 0.72f);
            b.Window(Finish.Anodized, Finish.Coating, rear - 0.009f, front, y, width, height, holeWidth, holeHeight);
            b.Box(Finish.Graphite, width + 0.002f, y, (rear + front) * 0.5f, 0.012f, 0.019f, 0.033f);
            float dotZ = front - 0.009f;
            // Coat the rim without covering the game's sight picture.
            b.Box(Finish.Optic, 0f, y - holeHeight + .0035f, dotZ, holeWidth * 1.38f, .005f, .0024f);
            b.Box(Finish.Optic, -holeWidth + .003f, y - .007f, dotZ, .004f, holeHeight * .91f, .0024f);
            b.Box(Finish.Recess, 0, y - holeHeight * 0.5f, dotZ, 0.002f, holeHeight + 0.002f, 0.003f);
            b.Box(Finish.OpticDot, 0, y, dotZ, 0.0032f, 0.0032f, 0.005f);
            if (aftermarket)
            {
                b.Box(Finish.Jade, -width - .004f, y - .011f, (rear + front) * .5f, .009f, .017f, .049f);
                b.Box(Finish.Steel, width + .006f, y - .019f, rear + .017f, .016f, .020f, .030f);
            }
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

        static Mesh BuildTangdao()
        {
            var b = new Sculpt();
            b.Loft(Finish.Wrap, S(-0.245f, -0.006f, 0.026f, 0.031f, 0.011f),
                S(-0.211f, -0.004f, 0.030f, 0.034f, 0.011f), S(0.053f, 0, 0.028f, 0.031f, 0.011f));
            for (int i = 0; i < 9; i++)
            {
                float z = -0.211f + i * 0.028f;
                b.Loft(i % 3 == 0 ? Finish.Ink : Finish.Deep, S(z, -0.003f, 0.029f, 0.034f, 0.011f),
                    S(z + 0.009f, -0.003f, 0.033f, 0.037f, 0.012f), S(z + 0.017f, -0.003f, 0.029f, 0.032f, 0.011f));
            }
            b.Loft(Finish.Amber, S(-0.251f, -0.005f, 0.024f, 0.028f, 0.009f),
                S(-0.234f, -0.005f, 0.035f, 0.033f, 0.011f), S(-0.216f, -0.004f, 0.029f, 0.034f, 0.010f));
            b.Loft(Finish.Ink, S(0.035f, 0.004f, 0.043f, 0.045f, 0.021f),
                S(0.054f, 0.004f, 0.072f, 0.057f, 0.031f), S(0.071f, 0.004f, 0.060f, 0.051f, 0.027f));
            b.Loft(Finish.Amber, S(0.067f, 0.004f, 0.060f, 0.051f, 0.027f),
                S(0.076f, 0.004f, 0.058f, 0.050f, 0.026f), S(0.088f, 0.004f, 0.030f, 0.035f, 0.014f));
            b.Blade(Finish.Porcelain, Finish.Steel, new Vector4(0.081f, 0.006f, 0.035f, 0.013f),
                new Vector4(0.173f, 0.013f, 0.040f, 0.013f), new Vector4(0.490f, 0.022f, 0.038f, 0.011f),
                new Vector4(0.690f, 0.023f, 0.032f, 0.008f), new Vector4(0.777f, 0.027f, 0.028f, 0.005f),
                new Vector4(0.861f, 0.057f, 0.001f, 0.001f));
            for (int side = -1; side <= 1; side += 2)
                b.Plate(Finish.Jade, side, 0.011f, 0.014f, 0.16f,
                    P(0.084f, 0.008f), P(0.122f, -0.004f), P(0.243f, 0.006f), P(0.185f, 0.019f), P(0.101f, 0.022f));
            return b.FinishMesh();
        }

        static Mesh BuildShortBlade()
        {
            var b = new Sculpt();
            b.Loft(Finish.Jade, S(-0.154f, -0.012f, 0.022f, 0.025f, 0.009f),
                S(-0.135f, -0.010f, 0.031f, 0.032f, 0.012f), S(-0.026f, -0.004f, 0.029f, 0.031f, 0.011f),
                S(0.058f, 0, 0.036f, 0.035f, 0.014f));
            for (int i = 0; i < 4; i++)
            {
                float z = -0.119f + i * 0.041f;
                b.Loft(Finish.Deep, S(z, -0.006f, 0.029f, 0.031f, 0.010f),
                    S(z + 0.010f, -0.006f, 0.033f, 0.034f, 0.012f), S(z + 0.018f, -0.006f, 0.029f, 0.031f, 0.011f));
            }
            b.Loft(Finish.Ink, S(0.039f, -0.004f, 0.034f, 0.036f, 0.013f),
                S(0.070f, -0.011f, 0.050f, 0.061f, 0.022f), S(0.102f, -0.004f, 0.032f, 0.039f, 0.015f));
            b.Blade(Finish.Blue, Finish.Steel, new Vector4(0.079f, 0.005f, 0.043f, 0.015f),
                new Vector4(0.144f, 0.007f, 0.046f, 0.014f), new Vector4(0.187f, -0.012f, 0.062f, 0.011f),
                new Vector4(0.280f, -0.010f, 0.059f, 0.009f), new Vector4(0.344f, 0.008f, 0.037f, 0.008f),
                new Vector4(0.411f, 0.039f, 0.0015f, 0.001f));
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 3; i++)
                {
                    float z = 0.093f + i * 0.032f;
                    b.Plate(Finish.Jade, side, 0.010f, 0.016f, 0.1f, P(z, 0.015f),
                        P(z + 0.015f, 0.002f), P(z + 0.026f, 0.008f), P(z + 0.012f, 0.024f));
                }
            return b.FinishMesh();
        }

        static Vector2 P(float z, float y) => new Vector2(z, y);
        static Section S(float z, float y, float width, float height, float bevel) => new Section(z, y, width, height, bevel);

        readonly struct Section
        {
            public readonly Vector3 Center;
            readonly float width, height, bevel;
            public Section(float z, float y, float width, float height, float bevel, float x = 0)
            {
                Center = new Vector3(x, y, z);
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
                float length = n.magnitude;
                n = length > 0 ? n / length : Vector3.up;
                points.Add(a); points.Add(b); points.Add(c);
                normals.Add(n); normals.Add(n); normals.Add(n);
                uv.Add(SurfaceUV(a, n, finish)); uv.Add(SurfaceUV(b, n, finish)); uv.Add(SurfaceUV(c, n, finish));
                indices.Add(start); indices.Add(start + 1); indices.Add(start + 2);
                return start;
            }

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Finish finish, bool flip = false)
            {
                if (flip) { var t = b; b = d; d = t; }
                int start = Face(a, b, c, finish);
                points.Add(d); normals.Add(normals[start]); uv.Add(SurfaceUV(d, normals[start], finish));
                indices.Add(start); indices.Add(start + 2); indices.Add(start + 3);
            }

            static Vector2 SurfaceUV(Vector3 point, Vector3 normal, Finish finish)
            {
                int slot = (int)finish;
                float u = 0.5f, v = 0.5f;
                if (slot >= (int)Finish.Anodized && slot <= (int)Finish.Coating)
                {
                    bool facing = Mathf.Abs(normal.z) > 0.65f;
                    u = Mathf.Lerp(0.21f, 0.79f, facing ? 0.5f + point.x * 6f : (point.z + 0.44f) / 0.97f);
                    v = Mathf.Lerp(0.21f, 0.79f, Mathf.Abs(normal.y) > 0.65f ? 0.5f + point.x * 6f : (point.y + 0.24f) / 0.46f);
                }
                return new Vector2((slot % Columns + u) / Columns, (slot / Columns + v) / Rows);
            }

            public void Loft(Finish finish, params Section[] sections) => Loft(finish, Vector3.zero, Quaternion.identity, sections);

            public void Loft(Finish finish, Vector3 offset, Quaternion rotation, params Section[] sections)
            {
                int last = sections.Length - 1;
                for (int i = 0; i < 8; i++)
                {
                    int next = (i + 1) % 8;
                    Face(offset + rotation * sections[0].Center, Point(0, next), Point(0, i), finish);
                    Face(offset + rotation * sections[last].Center, Point(last, i), Point(last, next), finish);
                    for (int j = 1; j < sections.Length; j++)
                        Quad(Point(j - 1, i), Point(j - 1, next), Point(j, next), Point(j, i), finish);
                }

                Vector3 Point(int section, int corner) => offset + rotation * sections[section].Point(corner);
            }

            public void Plate(Finish finish, int side, float inner, float outer, float bevel, params Vector2[] contour)
            {
                Vector2 center = Vector2.zero;
                float area = 0f;
                for (int i = 0; i < contour.Length; i++)
                {
                    Vector2 a = contour[i], b = contour[(i + 1) % contour.Length];
                    area += a.x * b.y - a.y * b.x;
                    center += a;
                }
                center /= contour.Length;
                Vector3 centerFace = AtSide(side * outer, center);
                bool flip = (side > 0) == (area > 0f);
                for (int i = 0; i < contour.Length; i++)
                {
                    Vector2 a = contour[i], b = contour[(i + 1) % contour.Length];
                    Vector3 frontA = AtSide(side * outer, Vector2.Lerp(a, center, bevel));
                    Vector3 frontB = AtSide(side * outer, Vector2.Lerp(b, center, bevel));
                    Quad(AtSide(side * inner, a), AtSide(side * inner, b), frontB, frontA, finish, flip);
                    if (!flip) Face(centerFace, frontA, frontB, finish);
                    else Face(centerFace, frontB, frontA, finish);
                }
            }

            public void Aperture(Finish rim, Finish lip, Finish inset, float rear, float shoulder, float front, float y)
            {
                const int sides = 16;
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
                    Vector3 outerA = face + va * 0.018f, outerB = face + vb * 0.018f;
                    Vector3 innerA = face + va * 0.0075f, innerB = face + vb * 0.0075f;
                    Quad(back + va * 0.014f, back + vb * 0.014f, ridge + vb * 0.020f, ridge + va * 0.020f, rim);
                    Quad(ridge + va * 0.020f, ridge + vb * 0.020f, outerB, outerA, rim);
                    Quad(outerA, outerB, innerB, innerA, lip);
                    Quad(innerA, innerB, inside + vb * 0.007f, inside + va * 0.007f, inset);
                    Face(inside, inside + va * 0.007f, inside + vb * 0.007f, inset);
                }
            }

            public void Box(Finish finish, float x, float y, float z, float width, float height, float depth)
            {
                float w = width * 0.5f, h = height * 0.5f;
                float bevel = Mathf.Min(w, h) * 0.35f;
                Section rear = new Section(z - depth * 0.5f, y, w, h, bevel, x);
                Section front = new Section(z + depth * 0.5f, y, w, h, bevel, x);
                Loft(finish, rear, front);
            }

            public void Window(Finish shell, Finish inner, float rear, float front, float y,
                float outerWidth, float outerHeight, float innerWidth, float innerHeight)
            {
                const int sides = 12;
                var back = new Vector3(0, y, rear);
                var face = new Vector3(0, y, front);
                var bevel = Vector3.forward * Mathf.Min(0.005f, (front - rear) * 0.17f);
                for (int i = 0; i < sides; i++)
                {
                    float a = i * 2 * Mathf.PI / sides, b = (i + 1) * 2 * Mathf.PI / sides;
                    Vector3 oa = new Vector3(Mathf.Cos(a) * outerWidth, Mathf.Sin(a) * outerHeight, 0);
                    Vector3 ob = new Vector3(Mathf.Cos(b) * outerWidth, Mathf.Sin(b) * outerHeight, 0);
                    Vector3 ia = new Vector3(Mathf.Cos(a) * innerWidth, Mathf.Sin(a) * innerHeight, 0);
                    Vector3 ib = new Vector3(Mathf.Cos(b) * innerWidth, Mathf.Sin(b) * innerHeight, 0);
                    Quad(back + bevel + oa, back + bevel + ob, face - bevel + ob, face - bevel + oa, shell);
                    Quad(back + oa * 0.92f, back + ob * 0.92f, back + bevel + ob, back + bevel + oa, shell);
                    Quad(face - bevel + oa, face - bevel + ob, face + ob * 0.92f, face + oa * 0.92f, shell);
                    Quad(face + oa * 0.92f, face + ob * 0.92f, face + ib, face + ia, shell);
                    Quad(back + oa * 0.92f, back + ia, back + ib, back + ob * 0.92f, inner);
                    Quad(back + ia, face + ia, face + ib, back + ib, inner);
                }
            }

            public void Blade(Finish face, Finish bevel, params Vector4[] stations)
            {
                for (int j = 0; j < 6; j++)
                {
                    int next = (j + 1) % 6;
                    Finish color = j == 2 || j == 5 ? face : bevel;
                    Vector4 first = stations[0], last = stations[stations.Length - 1];
                    Face(new Vector3(0, first.y, first.x), BladePoint(first, next), BladePoint(first, j), face);
                    Face(new Vector3(0, last.y, last.x), BladePoint(last, j), BladePoint(last, next), bevel);
                    for (int i = 1; i < stations.Length; i++)
                        Quad(BladePoint(stations[i - 1], j), BladePoint(stations[i - 1], next),
                            BladePoint(stations[i], next), BladePoint(stations[i], j), color);
                }
            }

            static Vector3 BladePoint(Vector4 s, int side)
            {
                switch (side)
                {
                    case 0: return new Vector3(s.w * 0.88f, s.y + s.z * 0.65f, s.x);
                    case 1: return new Vector3(0, s.y + s.z, s.x);
                    case 2: return new Vector3(-s.w * 0.88f, s.y + s.z * 0.65f, s.x);
                    case 3: return new Vector3(-s.w, s.y - s.z * 0.28f, s.x);
                    case 4: return new Vector3(0, s.y - s.z, s.x);
                    default: return new Vector3(s.w, s.y - s.z * 0.28f, s.x);
                }
            }

            public void Marking(int side, float x, float z, float y)
            {
                int[] glyphs = { 0b010_101_111_101_101, 0b110_101_110_101_101, 0b000_000_111_000_000,
                    0b110_001_010_100_111, 0b111_101_101_101_111 };
                const float step = 0.00265f, half = 0.00119f;
                for (int l = 0; l < glyphs.Length; l++)
                    for (int row = 0; row < 5; row++)
                        for (int col = 0; col < 3; col++)
                        {
                            if ((glyphs[l] & (1 << (14 - row * 3 - col))) == 0) continue;
                            float u = z + side * (4 * l + col - 9) * step, v = y + (2 - row) * step;
                            float at = x * side;
                            Quad(new Vector3(at, v - half, u - half), new Vector3(at, v - half, u + half),
                                new Vector3(at, v + half, u + half), new Vector3(at, v + half, u - half), Finish.Stencil, side > 0);
                        }
            }

            static Vector3 AtSide(float x, Vector2 zy) => new Vector3(x, zy.y, zy.x);

            public Mesh FinishMesh()
            {
                var mesh = new Mesh { hideFlags = HideFlags.DontSave };
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
