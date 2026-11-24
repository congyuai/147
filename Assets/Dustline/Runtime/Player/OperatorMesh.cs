using System.Collections.Generic;
using UnityEngine;
using Bone = Dustline.OperatorVisual.Bone;

namespace Dustline
{
    internal static partial class OperatorMesh
    {
        internal const float BackDepth = 0.250f;

        enum Finish
        {
            Canvas, CanvasDark, Seam, Twill, TwillDark, Shell, ShellRim, Webbing,
            Ink, Rubber, Leather, Metal, Amber, Chalk, Team, TeamDark,
            Skin, SkinShade, Hair, HairLine, Eye, Stitch, CoatTrim, GlovePlate
        }

        static readonly Mesh[] shapes = new Mesh[4];
        static readonly Mesh[] sleeves = new Mesh[2];
        static readonly int[] shapeUsers = new int[4];
        static readonly int[] sleeveUsers = new int[2];
        static readonly Material[] materials = new Material[2];
        static readonly Texture2D[] palettes = new Texture2D[2];
        static readonly Texture2D[] finishes = new Texture2D[2];
        static readonly int[] paletteUsers = new int[2];

        public static void Acquire(OperatorStyle style, int team, out Mesh mesh, out Material material)
        {
            int shape = (int)style + 2 * team;
            if (materials[team] == null) CreatePalette(team);
            if (shapes[shape] == null) shapes[shape] = Build(style, team != 0);
            shapeUsers[shape]++;
            paletteUsers[team]++;
            mesh = shapes[shape];
            material = materials[team];
        }

        public static void Release(OperatorStyle style, int team)
        {
            int shape = (int)style + 2 * team;
            if (--shapeUsers[shape] == 0)
            {
                Dispose(shapes[shape]);
                shapes[shape] = null;
            }
            ReleasePalette(team);
        }

        public static void AcquireArms(OperatorStyle style, out Mesh mesh, out Material material)
        {
            int shape = (int)style;
            if (materials[0] == null) CreatePalette(0);
            if (sleeves[shape] == null)
            {
                var b = new Sculpt(1500);
                Arm(b, style, 1f, Bone.RightUpper, Bone.RightLower, Bone.RightHand);
                Arm(b, style, -1f, Bone.LeftUpper, Bone.LeftLower, Bone.LeftHand);
                sleeves[shape] = b.ToMesh(style, armsOnly: true);
            }
            sleeveUsers[shape]++;
            paletteUsers[0]++;
            mesh = sleeves[shape];
            material = materials[0];
        }

        public static void ReleaseArms(OperatorStyle style)
        {
            int shape = (int)style;
            if (--sleeveUsers[shape] == 0)
            {
                Dispose(sleeves[shape]);
                sleeves[shape] = null;
            }
            ReleasePalette(0);
        }

        static void ReleasePalette(int team)
        {
            if (--paletteUsers[team] != 0) return;
            Dispose(materials[team]);
            Dispose(palettes[team]);
            Dispose(finishes[team]);
            materials[team] = null;
            palettes[team] = finishes[team] = null;
        }

        static void Dispose(Object asset)
        {
            if (Application.isPlaying) Object.Destroy(asset);
            else Object.DestroyImmediate(asset);
        }

        const int PaletteWidth = 32;

        static void CreatePalette(int team)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new System.InvalidOperationException("Operator visuals need URP Lit or Unlit in the shader library.");

            int[] colors = team == 0 ? new[] {
                0x3D6267, 0x324C52, 0x6D8A87, 0x525C53, 0x3F4742, 0x485856, 0x75847D, 0x3C4642,
                0x263238, 0x282D31, 0x534843, 0x95A49F, 0xC78D48, 0xEAE5D4, 0xD3A362, 0x33555C,
                0xB78C74, 0x986854, 0x322A26, 0x524239, 0x3B3331, 0x96A59C, 0x5C797B, 0x556160
            } : new[] {
                0xBCC0BC, 0x939B99, 0xD8DAD4, 0x868F91, 0x646D72, 0x8D9698, 0xBEC3C0, 0x555E62,
                0x343B41, 0x303639, 0x565859, 0xA5AEAE, 0xB6BCB7, 0xF1EFDF, 0xEFEDE2, 0x414D56,
                0xAC826D, 0x825C4C, 0x362F2D, 0x554640, 0x3B3331, 0xA3ADAB, 0xD8DBD1, 0x686F73
            };
            var pixels = new Color32[PaletteWidth];
            for (int i = 0; i < colors.Length; i++)
            {
                int c = colors[i];
                pixels[i] = new Color32((byte)(c >> 16), (byte)((c >> 8) & 255), (byte)(c & 255), 255);
            }
            var palette = new Texture2D(PaletteWidth, 1, TextureFormat.RGBA32, false, false)
            {
                name = "Operator swatches " + team,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            palette.SetPixels32(pixels);
            palette.Apply(false, true);
            var material = new Material(shader) { name = "Operator fabric " + team, hideFlags = HideFlags.DontSave };
            material.SetTexture("_BaseMap", palette);
            material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_MetallicGlossMap"))
            {
                var finish = new Texture2D(PaletteWidth, 1, TextureFormat.RGBA32, false, true) {
                    name = "Operator surface finishes " + team, filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave
                };
                var gloss = new Color32[PaletteWidth];
                for (int i = 0; i < gloss.Length; i++)
                {
                    switch ((Finish)i)
                    {
                        case Finish.Metal: gloss[i] = new Color32(138, 0, 0, 149); break;
                        case Finish.Shell: case Finish.ShellRim: case Finish.GlovePlate:
                            gloss[i] = new Color32(18, 0, 0, 107); break;
                        case Finish.Skin: case Finish.SkinShade: case Finish.Leather:
                            gloss[i] = new Color32(0, 0, 0, 76); break;
                        default: gloss[i] = new Color32(0, 0, 0, 34); break;
                    }
                }
                finish.SetPixels32(gloss);
                finish.Apply(false, true);
                material.SetTexture("_MetallicGlossMap", finish);
                material.SetFloat("_Smoothness", 1f);
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
                finishes[team] = finish;
            }
            palettes[team] = palette;
            materials[team] = material;
        }

        static Mesh Build(OperatorStyle style, bool faction)
        {
            var b = new Sculpt();
            bool f = style == OperatorStyle.Female;
            float w = TorsoWidth(style);
            float h = OperatorVisual.Rest(Bone.Hips, style).y;
            b.Loft(Finish.TwillDark, 12, 0.80f,
                R(Bone.Hips, h - 0.070f, 0f, -0.009f, f ? 0.151f : 0.143f, 0.090f, 0.093f),
                R(Bone.Hips, h - 0.023f, 0f, -0.013f, f ? 0.187f : 0.173f, 0.109f, 0.111f),
                R(Bone.Hips, h + 0.045f, 0f, -0.012f, f ? 0.173f : 0.163f, 0.109f, 0.111f),
                R(Bone.Hips, 1.024f, 0f, -0.007f, w - 0.006f, 0.099f, 0.092f));
            b.Cloth(Finish.Canvas, 18, 0.84f, .0038f,
                R(Bone.Hips, 0.991f, 0f, -0.006f, w + 0.001f, 0.100f, 0.095f),
                R(Bone.Hips, 1.036f, 0f, -0.006f, w - 0.005f, 0.101f, 0.094f),
                R(Bone.Hips, 1.068f, 0f, -0.008f, w - 0.015f, 0.105f, 0.095f, Bone.Chest, 0.20f),
                R(Bone.Chest, 1.151f, 0f, 0.002f, w - 0.010f, 0.110f, 0.096f, Bone.Hips, 0.26f),
                R(Bone.Chest, 1.274f, 0f, 0.006f, w + 0.015f, f ? 0.114f : 0.109f, 0.099f),
                R(Bone.Chest, 1.386f, 0f, 0.003f, w + 0.023f, 0.100f, 0.100f),
                R(Bone.Chest, 1.440f, 0f, 0.003f, w + 0.009f, 0.080f, 0.079f),
                R(Bone.Chest, 1.477f, 0f, 0.010f, 0.091f, 0.065f, 0.055f),
                R(Bone.Chest, 1.492f, 0f, 0.010f, 0.066f, 0.055f, 0.047f));
            b.Loft(Finish.CanvasDark, 10, 1f,
                R(Bone.Chest, 1.465f, 0f, 0.010f, 0.074f, 0.068f, 0.057f),
                R(Bone.Chest, 1.499f, 0f, 0.012f, 0.069f, 0.064f, 0.059f, Bone.Head, 0.22f),
                R(Bone.Head, 1.529f, 0f, 0.013f, 0.056f, 0.051f, 0.048f, Bone.Chest, 0.39f));
            b.Loft(Finish.Skin, 10, 1f,
                R(Bone.Head, 1.506f, 0f, 0.012f, 0.052f, 0.047f, 0.045f, Bone.Chest, 0.22f),
                R(Bone.Head, 1.554f, 0f, 0.011f, 0.053f, 0.047f, 0.047f),
                R(Bone.Head, 1.582f, 0f, 0.011f, 0.062f, 0.052f, 0.055f));

            Uniform(b, w, faction);
            Head(b, f);
            Arm(b, style, 1f, Bone.RightUpper, Bone.RightLower, Bone.RightHand);
            Arm(b, style, -1f, Bone.LeftUpper, Bone.LeftLower, Bone.LeftHand);
            Leg(b, style, 1f, Bone.RightThigh, Bone.RightShin, Bone.RightBoot);
            Leg(b, style, -1f, Bone.LeftThigh, Bone.LeftShin, Bone.LeftBoot);
            return b.ToMesh(style);
        }

        static float TorsoWidth(OperatorStyle style) => style == OperatorStyle.Female ? .150f : .174f;

        static void Uniform(Sculpt b, float w, bool faction)
        {
            const Bone chest = Bone.Chest, hips = Bone.Hips;
            b.Loft(Finish.Webbing, 16, .80f,
                R(hips, .997f, 0f, -.006f, w + .011f, .109f, .104f),
                R(hips, 1.030f, 0f, -.008f, w + .003f, .109f, .100f, chest, .12f));
            b.Tile(Finish.Metal, hips, V(0f, 1.013f, .108f), V(.054f, .028f, .012f), .006f);
            for (int i = -1; i <= 1; i += 2)
            {
                float x = i * w * .51f;
                b.Tile(Finish.CanvasDark, chest, V(x, 1.283f, .092f), V(w * .45f, .104f, .018f), .011f, V(0, i * 31f, i * 5f));
                b.Tile(Finish.Seam, chest, V(x, 1.315f, .104f), V(w * .44f, .016f, .009f), .004f, V(0, i * 31f, i * 5f));
                if (!faction) b.Tile(Finish.CoatTrim, chest, V(i * w * .52f, 1.444f, .056f),
                    V(.067f, .042f, .016f), .009f, V(-21f, i * 11f, i * 23f));
            }
            if (!faction)
                b.Cloth(Finish.CoatTrim, 14, .85f, .002f,
                    R(chest, 1.430f, 0f, -.028f, w * .55f, .040f, .057f, tilt: .019f),
                    R(chest, 1.463f, 0f, -.023f, w * .47f, .043f, .052f, tilt: .011f),
                    R(chest, 1.493f, 0f, -.018f, .060f, .050f, .049f, Bone.Head, .12f));
            b.Tile(faction ? Finish.Seam : Finish.Stitch, chest, V(0f, 1.295f, .114f), V(.010f, .263f, .008f), .003f);
            b.Tile(Finish.CanvasDark, chest, V(0f, 1.138f, .110f), V(.011f, .067f, .007f), .003f);
            Mark(b, chest, V(.074f, 1.361f, .092f), 21f, .73f);
        }

        static void Mark(Sculpt b, Bone bone, Vector3 center, float yaw, float scale)
        {
            var euler = V(0f, yaw, 0f);
            var turn = Quaternion.Euler(euler);
            b.Tile(Finish.TeamDark, bone, center, V(.078f, .045f, .010f) * scale, .006f, euler);
            for (int i = -1; i <= 1; i++)
                b.Tile(Finish.Team, bone, center + turn * (V(.016f * i, -.002f + .004f * (1 - Mathf.Abs(i)), .006f) * scale),
                    V(.009f, .024f - .003f * Mathf.Abs(i), .004f) * scale, .0015f, V(0f, yaw, -24f));
        }

        static void Head(Sculpt b, bool f)
        {
            float w = f ? 0.077f : 0.085f;
            const Bone head = Bone.Head;
            b.Loft(Finish.Skin, 16, f ? 0.95f : 0.82f,
                R(head, 1.535f, 0f, 0.022f, w * 0.41f, 0.044f, 0.048f),
                R(head, 1.556f, 0f, 0.020f, w * (f ? 0.67f : 0.77f), 0.065f, 0.058f),
                R(head, 1.585f, 0f, 0.013f, w * 0.93f, 0.080f, 0.076f),
                R(head, 1.620f, 0f, 0.012f, w, 0.082f, 0.087f),
                R(head, 1.651f, 0f, 0.008f, w * 0.99f, 0.082f, 0.092f),
                R(head, 1.688f, 0f, 0.004f, w * 0.96f, 0.083f, 0.087f),
                R(head, 1.719f, 0f, 0.002f, w * 0.80f, 0.072f, 0.073f),
                R(head, 1.741f, 0f, 0.002f, w * 0.45f, 0.041f, 0.040f));
            b.Loft(Finish.Hair, 16, 0.86f,
                R(head, 1.661f, 0f, 0.005f, w + 0.006f, 0.087f, 0.095f, tilt: f ? 0.037f : 0.031f),
                R(head, 1.710f, f ? -0.004f : 0.004f, 0f, w + 0.003f, 0.087f, 0.091f),
                R(head, 1.741f, f ? -0.006f : 0.006f, -0.003f, w * 0.81f, 0.067f, 0.071f),
                R(head, f ? 1.759f : 1.758f, f ? -0.005f : 0.008f, -0.008f, w * 0.38f, 0.031f, 0.038f));
            if (f)
            {
                b.Loft(Finish.HairLine, 10, 1f,
                    R(head, 1.578f, 0f, -0.067f, 0.026f, 0.023f, 0.023f),
                    R(head, 1.607f, 0f, -0.087f, 0.034f, 0.025f, 0.027f),
                    R(head, 1.640f, 0f, -0.090f, 0.031f, 0.023f, 0.025f));
                b.Tile(Finish.Webbing, head, V(0f, 1.604f, -0.111f), V(0.054f, 0.019f, 0.018f), 0.006f, V(-12f, 0f, -6f));
                b.Loft(Finish.Hair, 10, 0.86f,
                    R(head, 1.562f, 0f, -0.116f, 0.021f, 0.022f, 0.022f),
                    R(head, 1.584f, 0.004f, -0.119f, 0.040f, 0.035f, 0.032f),
                    R(head, 1.617f, 0.005f, -0.117f, 0.037f, 0.033f, 0.032f),
                    R(head, 1.640f, 0.005f, -0.109f, 0.021f, 0.024f, 0.021f));
                b.Tile(Finish.HairLine, head, V(0.010f, 1.601f, -0.149f), V(0.048f, 0.011f, 0.008f), 0.004f, V(0f, -2f, -22f));
            }
            else
            {
                b.Tile(Finish.HairLine, head, V(0.028f, 1.718f, 0.077f), V(0.054f, 0.015f, 0.016f), 0.006f, V(-19f, 27f, 11f));
                b.Tile(Finish.HairLine, head, V(-0.022f, 1.734f, 0.064f), V(0.056f, 0.013f, 0.013f), 0.005f, V(-32f, -23f, -4f));
                b.Tile(Finish.Hair, head, V(-0.058f, 1.710f, 0.066f), V(0.030f, 0.042f, 0.022f), 0.009f, V(0f, -36f, -27f));
            }

            b.Loft(Finish.Skin, 8, 0.90f,
                R(head, 1.596f, 0f, 0.084f, f ? 0.014f : 0.017f, 0.024f, 0.010f),
                R(head, 1.608f, 0f, 0.092f, f ? 0.015f : 0.018f, f ? 0.026f : 0.030f, 0.016f),
                R(head, 1.632f, 0f, 0.087f, 0.012f, 0.016f, 0.012f),
                R(head, 1.653f, 0f, 0.079f, 0.010f, 0.015f, 0.010f));
            b.Tile(Finish.SkinShade, head, V(0f, 1.575f, 0.093f), V(f ? 0.038f : 0.043f, 0.006f, 0.010f), 0.002f, V(-6f, 0f, 0f));
            for (int s = -1; s <= 1; s += 2)
            {
                float ex = s * (f ? 0.033f : 0.036f);
                float ear = s * (w - 0.002f);
                Vector3 slope = V(0f, s * 22f, s * 4f);
                b.Tile(Finish.SkinShade, head, V(ex, 1.634f, 0.089f), V(0.034f, 0.014f, 0.017f), 0.005f, slope);
                b.Tile(Finish.Chalk, head, V(ex, 1.633f, 0.098f), V(0.024f, 0.007f, 0.003f), 0.003f, slope);
                b.Tile(Finish.Eye, head, V(ex - s * 0.003f, 1.633f, 0.100f), V(0.009f, 0.007f, 0.004f), 0.002f, slope);
                b.Tile(Finish.Hair, head, V(ex, 1.647f, 0.090f), V(0.035f, f ? 0.006f : 0.009f, 0.009f), 0.003f,
                    V(0f, s * 23f, s * 8f));
                b.Loft(Finish.Skin, 8, 1f,
                    R(head, 1.588f, ear, 0.004f, 0.008f, 0.012f, 0.010f),
                    R(head, 1.608f, ear + s * 0.006f, 0.003f, 0.011f, 0.019f, 0.014f),
                    R(head, 1.637f, ear + s * 0.004f, -0.001f, 0.010f, 0.016f, 0.010f),
                    R(head, 1.650f, ear, -0.003f, 0.006f, 0.009f, 0.008f));
            }
            b.Tile(Finish.Ink, head, V(w + 0.011f, 1.609f, -0.012f), V(0.033f, 0.034f, 0.018f), 0.008f, V(0f, 76f, -8f));
            b.Tile(Finish.Amber, head, V(w + 0.020f, 1.615f, -0.017f), V(0.014f, 0.011f, 0.005f), 0.004f, V(0f, 81f, -8f));
        }

        static void Arm(Sculpt b, OperatorStyle style, float s, Bone upper, Bone elbow, Bone hand)
        {
            Vector3 a = OperatorVisual.Rest(upper, style);
            Vector3 e = OperatorVisual.Rest(elbow, style);
            Vector3 h = OperatorVisual.Rest(hand, style);
            float x = a.x, z = a.z;
            float k = style == OperatorStyle.Female ? 0.91f : 1f;
            b.Cloth(Finish.Canvas, 12, 0.90f, .0030f * k,
                R(elbow, h.y + 0.005f, x, z, 0.034f * k, 0.032f * k, 0.033f * k),
                R(elbow, h.y + 0.041f, x, z, 0.038f * k, 0.037f * k, 0.040f * k),
                R(elbow, e.y - 0.071f, x, z - 0.005f, 0.052f * k, 0.044f * k, 0.050f * k),
                R(elbow, e.y - 0.027f, x, z, 0.054f * k, 0.049f * k, 0.052f * k, upper, 0.21f),
                R(upper, e.y + 0.010f, x, z, 0.054f * k, 0.050f * k, 0.052f * k, elbow, 0.46f),
                R(upper, e.y + 0.057f, x, z, 0.060f * k, 0.052f * k, 0.051f * k, elbow, 0.12f),
                R(upper, a.y - 0.065f, x, z, 0.069f * k, 0.063f * k, 0.061f * k),
                R(upper, a.y + 0.010f, x - s * 0.010f, z - 0.002f, 0.073f * k, 0.064f * k, 0.067f * k, Bone.Chest, 0.20f),
                R(upper, a.y + 0.052f, x - s * 0.020f, z - 0.004f, 0.050f * k, 0.043f * k, 0.044f * k, Bone.Chest, 0.46f));
            Mark(b, upper, V(x + s * .051f * k, a.y - .055f, z + .048f * k), s * 49f, .86f * k);
            b.Tile(Finish.CanvasDark, elbow, V(x + s * 0.014f, h.y + 0.090f, z - 0.038f),
                V(0.066f * k, 0.108f, 0.014f), 0.015f, V(0f, -s * 27f, s * 4f));
            b.Loft(Finish.CanvasDark, 8, 0.81f,
                R(elbow, h.y - 0.013f, x, z, 0.036f * k, 0.034f * k, 0.034f * k, hand, 0.18f),
                R(elbow, h.y + 0.029f, x, z, 0.043f * k, 0.039f * k, 0.042f * k));

            b.Loft(Finish.Leather, 10, 0.65f,
                R(hand, h.y - 0.096f, x + s * 0.004f, z + 0.012f, 0.023f * k, 0.014f, 0.012f),
                R(hand, h.y - 0.074f, x + s * 0.003f, z + 0.005f, 0.032f * k, 0.017f, 0.017f),
                R(hand, h.y - 0.047f, x, z, 0.034f * k, 0.021f, 0.021f),
                R(hand, h.y - 0.021f, x, z, 0.030f * k, 0.021f, 0.021f),
                R(hand, h.y + 0.015f, x, z, 0.032f * k, 0.031f, 0.031f));
            b.Loft(Finish.Leather, 8, 0.86f,
                R(hand, h.y - 0.066f, x - s * 0.035f * k, z + 0.018f, 0.010f * k, 0.013f, 0.014f),
                R(hand, h.y - 0.048f, x - s * 0.036f * k, z + 0.006f, 0.015f * k, 0.016f, 0.017f),
                R(hand, h.y - 0.013f, x - s * 0.023f * k, z, 0.013f * k, 0.016f, 0.017f));
            b.Tile(Finish.Stitch, elbow, V(x + s * 0.039f * k, h.y + 0.039f, z - 0.008f),
                V(0.022f, 0.006f, 0.008f), 0.003f, V(0f, s * 75f, 0f));
            b.Tile(Finish.CoatTrim, elbow, V(x + s * 0.037f * k, h.y + 0.025f, z - 0.009f),
                V(0.026f, 0.017f, 0.010f), 0.004f, V(0f, s * 78f, 0f));
            b.Tile(Finish.GlovePlate, hand, V(x, h.y - 0.049f, z - 0.019f), V(0.054f * k, 0.041f, 0.015f), 0.009f);
            for (int i = 0; i < 4; i++)
            {
                float fx = x + s * (i - 1.5f) * (0.014f * k);
                b.Tile(Finish.Rubber, hand, V(fx, h.y - 0.080f + 0.002f * i, z + 0.017f), V(0.013f * k, 0.026f, 0.014f),
                    0.004f, V(23f, 0f, 0f));
            }
        }

        static void Leg(Sculpt b, OperatorStyle style, float s, Bone thigh, Bone knee, Bone foot)
        {
            Vector3 u = OperatorVisual.Rest(thigh, style);
            Vector3 n = OperatorVisual.Rest(knee, style);
            float x = u.x;
            float w = style == OperatorStyle.Female ? 0.077f : 0.084f;
            b.Cloth(Finish.Twill, 14, 0.87f, .004f,
                R(knee, 0.147f, x, -0.002f, 0.041f, 0.044f, 0.044f, foot, 0.25f),
                R(knee, 0.186f, x, -0.004f, 0.047f, 0.048f, 0.049f),
                R(knee, 0.213f, x, -0.005f, 0.048f, 0.052f, 0.054f),
                R(knee, n.y - 0.147f, x, -0.013f, 0.060f, 0.057f, 0.065f),
                R(knee, n.y - 0.047f, x, -0.005f, 0.061f, 0.062f, 0.061f, thigh, 0.17f),
                R(knee, n.y + 0.006f, x, 0.002f, 0.064f, 0.065f, 0.066f, thigh, 0.47f),
                R(thigh, n.y + 0.056f, x, 0.003f, 0.066f, 0.067f, 0.071f, knee, 0.18f),
                R(thigh, u.y - 0.163f, x, 0.001f, w, 0.084f, 0.086f),
                R(thigh, u.y - 0.062f, x - s * 0.005f, -0.005f, w + 0.009f, 0.102f, 0.097f, Bone.Hips, 0.24f),
                R(thigh, u.y + 0.041f, x - s * 0.014f, -0.012f, w + 0.004f, 0.095f, 0.097f, Bone.Hips, 0.74f));
            b.Tile(Finish.TwillDark, thigh, V(x + s * 0.061f, u.y - 0.151f, 0.039f), V(0.105f, 0.139f, 0.038f), 0.017f,
                V(0f, s * 54f, s * 5f));
            b.Tile(Finish.Seam, thigh, V(x + s * 0.076f, u.y - 0.103f, 0.053f), V(0.098f, 0.024f, 0.016f), 0.006f,
                V(0f, s * 54f, s * 5f));
            b.Tile(Finish.Chalk, thigh, V(x + s * 0.067f, u.y - 0.209f, 0.057f), V(0.070f, 0.012f, 0.013f), 0.004f,
                V(0f, s * 43f, s * 5f));
            b.Tile(Finish.TwillDark, knee, V(x, n.y - 0.011f, 0.044f), V(0.119f, 0.136f, 0.054f), 0.024f, V(-7f, 0f, 0f));
            b.Tile(Finish.Stitch, knee, V(x, n.y - 0.041f, 0.073f), V(0.075f, 0.006f, 0.006f), 0.002f, V(-7f, 0f, 0f));

            float boot = style == OperatorStyle.Female ? 0.060f : 0.064f;
            b.Loft(Finish.Rubber, 12, 0.68f,
                R(foot, 0f, x, 0.035f, boot - 0.005f, 0.108f, 0.094f),
                R(foot, 0.011f, x, 0.038f, boot, 0.116f, 0.098f),
                R(foot, 0.033f, x, 0.040f, boot + 0.001f, 0.117f, 0.098f),
                R(foot, 0.044f, x, 0.039f, boot - 0.002f, 0.112f, 0.095f));
            b.Loft(Finish.Leather, 12, 0.76f,
                R(foot, 0.035f, x, 0.039f, boot - 0.001f, 0.114f, 0.096f),
                R(foot, 0.055f, x, 0.037f, boot - 0.002f, 0.115f, 0.094f),
                R(foot, 0.078f, x, 0.027f, boot - 0.005f, 0.113f, 0.086f),
                R(foot, 0.109f, x, 0.010f, boot - 0.009f, 0.101f, 0.069f),
                R(foot, 0.148f, x, -0.001f, boot - 0.014f, 0.062f, 0.053f, knee, 0.25f),
                R(foot, 0.197f, x, -0.001f, boot - 0.011f, 0.052f, 0.049f, knee, 0.74f));
            b.Tile(Finish.Ink, foot, V(x, 0.052f, 0.123f), V(boot * 1.55f, 0.023f, 0.053f), 0.009f, V(-6f, 0f, 0f));
            for (int i = 0; i < 3; i++)
            {
                b.Tile(Finish.Webbing, foot, V(x, 0.138f - 0.013f * i, 0.058f + 0.024f * i),
                    V(0.056f, 0.009f, 0.015f), 0.003f, V(-43f, 0f, i == 1 ? -9f : 9f));
                b.Tile(Finish.Ink, foot, V(x + s * (boot + 0.001f), 0.020f, -0.015f + 0.062f * i),
                    V(0.024f, 0.028f, 0.011f), 0.005f, V(0f, s * 76f, 0f));
            }
        }

        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);
        static Vector2 P(float x, float y) => new Vector2(x, y);

        readonly struct Ring
        {
            public readonly Vector3 Center;
            public readonly float Width, Front, Back, Tilt;
            public readonly BoneWeight Weight;

            public Ring(Vector3 center, float width, float front, float back, float tilt, BoneWeight weight)
            {
                Center = center;
                Width = width;
                Front = front;
                Back = back;
                Tilt = tilt;
                Weight = weight;
            }
        }

        static Ring R(Bone primary, float y, float x, float z, float width, float front, float back,
            Bone secondary = Bone.Count, float blend = 0f, float tilt = 0f)
        {
            return new Ring(V(x, y, z), width, front, back, tilt, Weight(primary, secondary, blend));
        }

        static BoneWeight Weight(Bone a, Bone b = Bone.Count, float blend = 0f)
        {
            if (blend > 0.5f) { Bone swap = a; a = b; b = swap; blend = 1f - blend; }
            return new BoneWeight { boneIndex0 = (int)a, boneIndex1 = b == Bone.Count ? (int)a : (int)b,
                weight0 = 1f - blend, weight1 = blend };
        }

        sealed class Sculpt
        {
            readonly List<Vector3> positions;
            readonly List<Vector2> uv;
            readonly List<BoneWeight> weights;
            readonly List<int> indices;

            public Sculpt(int capacity = 4096)
            {
                positions = new List<Vector3>(capacity);
                uv = new List<Vector2>(capacity);
                weights = new List<BoneWeight>(capacity);
                indices = new List<int>(capacity * 3);
            }

            public void Loft(Finish finish, int sides, float shape, params Ring[] rings) => Surface(finish, sides, shape, 0f, true, rings);
            public void Cloth(Finish finish, int sides, float shape, float fold, params Ring[] rings) => Surface(finish, sides, shape, fold, true, rings);
            public void Shell(Finish finish, int sides, float shape, params Ring[] rings) => Surface(finish, sides, shape, 0f, false, rings);

            void Surface(Finish finish, int sides, float shape, float fold, bool closeBase, Ring[] rings)
            {
                int first = positions.Count;
                for (int i = 0; i < rings.Length; i++)
                {
                    Ring r = rings[i];
                    for (int j = 0; j < sides; j++)
                    {
                        float angle = 2f * Mathf.PI * j / sides;
                        float dx = Curve(Mathf.Sin(angle), shape);
                        float dz = Curve(Mathf.Cos(angle), shape);
                        float ripple = i == 0 || i == rings.Length - 1 ? 0f : fold *
                            (.68f * Mathf.Sin(3f * angle + i * 1.93f) + .32f * Mathf.Cos(5f * angle - i * 1.16f));
                        Add(r.Center + V((r.Width + ripple) * dx, r.Tilt * dz, dz * ((dz >= 0f ? r.Front : r.Back) + ripple)), r.Weight, finish);
                    }
                    if (i > 0) Bridge(first + sides * (i - 1), sides);
                }
                if (closeBase) Cap(first, sides, false, finish);
                Cap(first + sides * (rings.Length - 1), sides, true, finish);
            }

            static float Curve(float v, float shape) => Mathf.Sign(v) * Mathf.Pow(Mathf.Abs(v), shape);

            public void Tile(Finish finish, Bone bone, Vector3 center, Vector3 size, float bevel, Vector3 euler = default)
            {
                float w = size.x * 0.5f, h = size.y * 0.5f;
                float c = Mathf.Min(bevel, Mathf.Min(w, h) * 0.79f);
                Extrude(finish, bone, center, size.z, Quaternion.Euler(euler),
                    P(-w + c, -h), P(w - c, -h), P(w, -h + c), P(w, h - c),
                    P(w - c, h), P(-w + c, h), P(-w, h - c), P(-w, -h + c));
            }

            public void Panel(Finish finish, Bone bone, Vector3 center, float depth, params Vector2[] outline)
            {
                Extrude(finish, bone, center, depth, Quaternion.identity, outline);
            }

            void Extrude(Finish finish, Bone bone, Vector3 center, float depth, Quaternion rotation, params Vector2[] outline)
            {
                int first = positions.Count;
                BoneWeight weight = Weight(bone);
                for (int face = 0; face <= 1; face++)
                {
                    float inset = face == 0 ? 1f : 0.90f;
                    for (int j = 0; j < outline.Length; j++)
                    {
                        Vector2 p = outline[j] * inset;
                        Add(center + rotation * V(p.x, p.y, (face - 0.5f) * depth), weight, finish);
                    }
                }
                Bridge(first, outline.Length);
                Cap(first, outline.Length, false, finish);
                Cap(first + outline.Length, outline.Length, true, finish);
            }

            void Add(Vector3 p, BoneWeight weight, Finish color)
            {
                positions.Add(p);
                uv.Add(P(((int)color + 0.5f) / PaletteWidth, 0.5f));
                weights.Add(weight);
            }

            void Bridge(int first, int sides)
            {
                for (int j = 0; j < sides; j++)
                {
                    int a = first + j, b = first + (j + 1) % sides;
                    Tri(a, b, b + sides);
                    Tri(a, b + sides, a + sides);
                }
            }

            void Cap(int source, int sides, bool forward, Finish finish)
            {
                int start = positions.Count;
                for (int j = 0; j < sides; j++) Add(positions[source + j], weights[source + j], finish);
                for (int j = 1; j < sides - 1; j++)
                {
                    if (forward) Tri(start, start + j, start + j + 1);
                    else Tri(start, start + j + 1, start + j);
                }
            }

            void Tri(int a, int b, int c)
            {
                indices.Add(a);
                indices.Add(b);
                indices.Add(c);
            }

            public Mesh ToMesh(OperatorStyle style, bool armsOnly = false, string label = null)
            {
                const int firstArm = (int)Bone.RightUpper;
                int first = armsOnly ? firstArm : 0;
                var bind = new Matrix4x4[armsOnly ? 6 : (int)Bone.Count];
                for (int i = 0; i < bind.Length; i++) bind[i] = Matrix4x4.Translate(-OperatorVisual.Rest((Bone)(i + first), style));
                var skinWeights = weights.ToArray();
                if (armsOnly)
                {
                    for (int i = 0; i < skinWeights.Length; i++)
                    {
                        ref var weight = ref skinWeights[i];
                        int shoulder = positions[i].x >= 0 ? 0 : 3;
                        weight.boneIndex0 = weight.boneIndex0 < firstArm ? shoulder : weight.boneIndex0 - firstArm;
                        weight.boneIndex1 = weight.boneIndex1 < firstArm ? shoulder : weight.boneIndex1 - firstArm;
                        if (weight.boneIndex0 == weight.boneIndex1) { weight.weight0 = 1; weight.weight1 = 0; }
                    }
                }
                var mesh = new Mesh { name = label ?? (armsOnly ? "Operator sleeves / " : "Operator mesh / ") + style, hideFlags = HideFlags.DontSave };
                mesh.SetVertices(positions);
                mesh.SetUVs(0, uv);
                mesh.SetTriangles(indices, 0);
                mesh.bindposes = bind;
                mesh.boneWeights = skinWeights;
                mesh.RecalculateNormals();
                return mesh;
            }
        }
    }
}
