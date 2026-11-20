using System.Collections.Generic;
using UnityEngine;
using Bone = Dustline.OperatorVisual.Bone;

namespace Dustline
{
    internal static class OperatorMesh
    {
        internal const float BackDepth = 0.250f;

        enum Finish
        {
            Canvas, CanvasDark, Seam, Twill, TwillDark, Shell, ShellRim, Webbing,
            Ink, Rubber, Leather, Metal, Amber, Chalk, Team, TeamDark,
            Skin, SkinShade, Hair, HairLine, Eye
        }

        static readonly Mesh[] shapes = new Mesh[2];
        static readonly int[] shapeUsers = new int[2];
        static readonly Material[] materials = new Material[3];
        static readonly Texture2D[] palettes = new Texture2D[3];
        static readonly int[] paletteUsers = new int[3];

        public static void Acquire(OperatorStyle style, int team, out Mesh mesh, out Material material)
        {
            int shape = (int)style;
            if (materials[team] == null) CreatePalette(team);
            if (shapes[shape] == null) shapes[shape] = Build(style);
            shapeUsers[shape]++;
            paletteUsers[team]++;
            mesh = shapes[shape];
            material = materials[team];
        }

        public static void Release(OperatorStyle style, int team)
        {
            int shape = (int)style;
            if (--shapeUsers[shape] == 0)
            {
                Dispose(shapes[shape]);
                shapes[shape] = null;
            }
            if (--paletteUsers[team] == 0)
            {
                Dispose(materials[team]);
                Dispose(palettes[team]);
                materials[team] = null;
                palettes[team] = null;
            }
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

            int signal = team == 1 ? 0xBD503F : team == 2 ? 0xA8B057 : 0x50A6B3;
            int backing = team == 1 ? 0x693A34 : team == 2 ? 0x565D39 : 0x315B67;
            // Each UV addresses one solid swatch: all trim, silhouette, and facial relief are geometry.
            int[] colors = {
                0x416166, 0x31464C, 0x678086, 0x57615B, 0x3D4947, 0x41525C, 0x6A7779, 0x3F4240,
                0x253035, 0x22282B, 0x4A4340, 0x95A49F, 0xE2A14C, 0xDAD2AA, signal, backing,
                0xB78C74, 0x986854, 0x322A26, 0x524239, 0x3B3331
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
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.22f);
            palettes[team] = palette;
            materials[team] = material;
        }

        static Mesh Build(OperatorStyle style)
        {
            var b = new Sculpt();
            bool f = style == OperatorStyle.Female;
            float w = f ? 0.150f : 0.174f;
            float h = OperatorVisual.Rest(Bone.Hips, style).y;
            b.Loft(Finish.TwillDark, 12, 0.80f,
                R(Bone.Hips, h - 0.070f, 0f, -0.009f, f ? 0.151f : 0.143f, 0.090f, 0.093f),
                R(Bone.Hips, h - 0.023f, 0f, -0.013f, f ? 0.187f : 0.173f, 0.109f, 0.111f),
                R(Bone.Hips, h + 0.045f, 0f, -0.012f, f ? 0.173f : 0.163f, 0.109f, 0.111f),
                R(Bone.Hips, 1.024f, 0f, -0.007f, w - 0.006f, 0.099f, 0.092f));
            b.Loft(Finish.Canvas, 14, 0.79f,
                R(Bone.Hips, 0.991f, 0f, -0.006f, w + 0.001f, 0.100f, 0.095f),
                R(Bone.Hips, 1.057f, 0f, -0.008f, w - 0.015f, 0.105f, 0.095f, Bone.Chest, 0.20f),
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

            Gear(b, w);
            Head(b, f);
            Arm(b, style, 1f, Bone.RightUpper, Bone.RightLower, Bone.RightHand);
            Arm(b, style, -1f, Bone.LeftUpper, Bone.LeftLower, Bone.LeftHand);
            Leg(b, style, 1f, Bone.RightThigh, Bone.RightShin, Bone.RightBoot);
            Leg(b, style, -1f, Bone.LeftThigh, Bone.LeftShin, Bone.LeftBoot);
            return b.ToMesh(style);
        }

        static void Gear(Sculpt b, float w)
        {
            b.Loft(Finish.Webbing, 12, 0.70f,
                R(Bone.Hips, 0.994f, 0f, -0.005f, w + 0.010f, 0.111f, 0.100f),
                R(Bone.Hips, 1.041f, 0f, -0.006f, w + 0.003f, 0.113f, 0.106f, Bone.Chest, 0.12f));
            b.Tile(Finish.Metal, Bone.Hips, V(0f, 1.015f, 0.119f), V(0.060f, 0.039f, 0.018f), 0.006f);
            b.Tile(Finish.Ink, Bone.Hips, V(0f, 1.015f, 0.129f), V(0.034f, 0.017f, 0.006f), 0.003f);

            b.Loft(Finish.Webbing, 12, 0.65f,
                R(Bone.Chest, 1.080f, 0f, 0.009f, w - 0.003f, 0.124f, 0.114f, Bone.Hips, 0.18f),
                R(Bone.Chest, 1.161f, 0f, 0.005f, w + 0.001f, 0.126f, 0.118f),
                R(Bone.Chest, 1.312f, 0f, 0.003f, w + 0.005f, 0.125f, 0.112f),
                R(Bone.Chest, 1.408f, 0f, 0.004f, w - 0.013f, 0.108f, 0.106f));
            b.Panel(Finish.Shell, Bone.Chest, V(0f, 1.266f, 0.131f), 0.050f,
                P(-w + 0.035f, -0.132f), P(w - 0.035f, -0.132f), P(w - 0.016f, -0.097f),
                P(w - 0.019f, 0.078f), P(w - 0.064f, 0.127f), P(-w + 0.064f, 0.127f),
                P(-w + 0.019f, 0.078f), P(-w + 0.016f, -0.097f));
            b.Tile(Finish.TeamDark, Bone.Chest, V(0f, 1.324f, 0.160f), V(w * 1.48f, 0.046f, 0.014f), 0.011f);
            b.Tile(Finish.Team, Bone.Chest, V(-0.036f, 1.324f, 0.169f), V(0.085f, 0.025f, 0.008f), 0.006f);
            b.Tile(Finish.Chalk, Bone.Chest, V(0.049f, 1.323f, 0.169f), V(0.029f, 0.009f, 0.008f), 0.003f, V(0f, 0f, 19f));
            for (int i = -1; i <= 1; i++)
            {
                float x = i * (w - 0.050f) * 0.69f;
                b.Tile(Finish.CanvasDark, Bone.Chest, V(x, 1.186f, 0.162f), V(0.060f, 0.094f, 0.047f), 0.009f);
                b.Tile(Finish.Seam, Bone.Chest, V(x, 1.220f, 0.185f), V(0.059f, 0.025f, 0.012f), 0.006f);
            }
            b.Tile(Finish.Ink, Bone.Chest, V(0f, 1.258f, -0.158f), V(w * 1.80f, 0.320f, 0.176f), 0.027f);
            b.Tile(Finish.Shell, Bone.Chest, V(0f, 1.268f, -0.221f), V(w * 1.53f, 0.244f, 0.048f), 0.022f);
            b.Tile(Finish.Team, Bone.Chest, V(0f, 1.343f, 0.005f - BackDepth), V(w * 1.16f, 0.043f, 0.010f), 0.008f);
            b.Tile(Finish.ShellRim, Bone.Chest, V(0f, 1.245f, 0.005f - BackDepth), V(w * 1.16f, 0.013f, 0.010f), 0.004f);
            b.Tile(Finish.ShellRim, Bone.Chest, V(0f, 1.211f, 0.005f - BackDepth), V(w * 0.94f, 0.012f, 0.010f), 0.004f);
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * (w - 0.057f);
                b.Tile(Finish.Webbing, Bone.Chest, V(x, 1.400f, 0.082f), V(0.038f, 0.116f, 0.025f), 0.007f, V(-34f, 0f, s * 9f));
                b.Tile(Finish.Webbing, Bone.Chest, V(x, 1.417f, -0.078f), V(0.039f, 0.112f, 0.032f), 0.008f, V(42f, 0f, 0f));
                b.Tile(Finish.Metal, Bone.Chest, V(x, 1.382f, 0.114f), V(0.039f, 0.023f, 0.013f), 0.004f, V(0f, 0f, s * 9f));
                b.Tile(Finish.CanvasDark, Bone.Hips, V(s * w, 0.986f, 0.034f), V(0.080f, 0.105f, 0.046f), 0.012f,
                    V(-4f, s * 55f, s * 10f));
            }
            b.Tile(Finish.Amber, Bone.Chest, V(w - 0.038f, 1.252f, 0.158f), V(0.018f, 0.040f, 0.011f), 0.005f, V(0f, 0f, 8f));
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
                    R(head, 1.621f, 0f, -0.096f, 0.026f, 0.024f, 0.024f),
                    R(head, 1.651f, 0f, -0.100f, 0.037f, 0.026f, 0.029f),
                    R(head, 1.678f, 0f, -0.099f, 0.031f, 0.024f, 0.028f));
                b.Tile(Finish.Webbing, head, V(0f, 1.657f, -0.122f), V(0.060f, 0.021f, 0.020f), 0.007f, V(-12f, 0f, -6f));
                b.Loft(Finish.Hair, 10, 0.86f,
                    R(head, 1.619f, 0f, -0.128f, 0.022f, 0.022f, 0.023f),
                    R(head, 1.646f, 0.004f, -0.135f, 0.043f, 0.037f, 0.033f),
                    R(head, 1.686f, 0.005f, -0.135f, 0.042f, 0.036f, 0.035f),
                    R(head, 1.710f, 0.005f, -0.122f, 0.023f, 0.023f, 0.022f));
                b.Tile(Finish.HairLine, head, V(0.011f, 1.673f, -0.169f), V(0.054f, 0.012f, 0.009f), 0.005f, V(0f, -2f, -22f));
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
            b.Loft(Finish.Canvas, 10, 0.86f,
                R(elbow, h.y + 0.005f, x, z, 0.038f * k, 0.032f * k, 0.035f * k),
                R(elbow, h.y + 0.049f, x, z, 0.041f * k, 0.038f * k, 0.041f * k),
                R(elbow, e.y - 0.071f, x, z - 0.005f, 0.052f * k, 0.044f * k, 0.050f * k),
                R(elbow, e.y - 0.027f, x, z, 0.054f * k, 0.049f * k, 0.052f * k, upper, 0.21f),
                R(upper, e.y + 0.010f, x, z, 0.054f * k, 0.050f * k, 0.052f * k, elbow, 0.46f),
                R(upper, e.y + 0.057f, x, z, 0.060f * k, 0.052f * k, 0.051f * k, elbow, 0.12f),
                R(upper, a.y - 0.065f, x, z, 0.069f * k, 0.063f * k, 0.061f * k),
                R(upper, a.y + 0.010f, x - s * 0.010f, z - 0.002f, 0.073f * k, 0.064f * k, 0.067f * k, Bone.Chest, 0.20f),
                R(upper, a.y + 0.052f, x - s * 0.020f, z - 0.004f, 0.050f * k, 0.043f * k, 0.044f * k, Bone.Chest, 0.46f));
            b.Tile(Finish.ShellRim, upper, V(x + s * 0.046f * k, a.y - 0.033f, z + 0.034f),
                V(0.103f * k, 0.132f, 0.051f), 0.019f, V(0f, s * 54f, s * 10f));
            b.Tile(Finish.TeamDark, upper, V(x + s * 0.067f * k, a.y - 0.038f, z + 0.052f),
                V(0.080f * k, 0.074f, 0.023f), 0.014f, V(0f, s * 54f, s * 10f));
            b.Tile(Finish.Team, upper, V(x + s * 0.076f * k, a.y - 0.039f, z + 0.059f),
                V(0.062f * k, 0.035f, 0.007f), 0.008f, V(0f, s * 54f, s * 10f));
            b.Tile(Finish.Shell, elbow, V(x + s * 0.015f, h.y + 0.092f, z - 0.036f),
                V(0.068f * k, 0.122f, 0.026f), 0.015f, V(0f, -s * 28f, s * 4f));
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
            b.Tile(Finish.Ink, hand, V(x, h.y - 0.049f, z - 0.019f), V(0.054f * k, 0.041f, 0.015f), 0.009f);
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
            b.Loft(Finish.Twill, 12, 0.82f,
                R(knee, 0.147f, x, -0.002f, 0.041f, 0.044f, 0.044f, foot, 0.25f),
                R(knee, 0.211f, x, -0.005f, 0.048f, 0.052f, 0.054f),
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
            b.Tile(Finish.Webbing, knee, V(x, n.y - 0.035f, 0.043f), V(0.133f, 0.122f, 0.077f), 0.019f, V(-7f, 0f, 0f));
            b.Panel(Finish.Shell, knee, V(x, n.y - 0.003f, 0.079f), 0.038f,
                P(-0.038f, -0.060f), P(0.037f, -0.060f), P(0.056f, -0.033f), P(0.058f, 0.026f),
                P(0.040f, 0.045f), P(-0.039f, 0.045f), P(-0.056f, 0.027f), P(-0.054f, -0.034f));
            b.Tile(Finish.ShellRim, knee, V(x, n.y + 0.012f, 0.100f), V(0.080f, 0.016f, 0.012f), 0.005f);
            b.Tile(Finish.Amber, knee, V(x + s * 0.028f, n.y - 0.032f, 0.100f), V(0.026f, 0.016f, 0.008f), 0.005f);

            float boot = style == OperatorStyle.Female ? 0.060f : 0.064f;
            b.Loft(Finish.Rubber, 12, 0.68f,
                R(foot, 0f, x, 0.045f, boot - 0.004f, 0.126f, 0.121f),
                R(foot, 0.014f, x, 0.047f, boot + 0.002f, 0.137f, 0.126f),
                R(foot, 0.037f, x, 0.048f, boot + 0.002f, 0.135f, 0.123f),
                R(foot, 0.049f, x, 0.047f, boot - 0.002f, 0.126f, 0.116f));
            b.Loft(Finish.Leather, 12, 0.76f,
                R(foot, 0.039f, x, 0.047f, boot, 0.130f, 0.117f),
                R(foot, 0.062f, x, 0.045f, boot - 0.002f, 0.130f, 0.115f),
                R(foot, 0.081f, x, 0.032f, boot - 0.005f, 0.131f, 0.095f),
                R(foot, 0.109f, x, 0.010f, boot - 0.009f, 0.101f, 0.069f),
                R(foot, 0.148f, x, -0.001f, boot - 0.014f, 0.062f, 0.053f, knee, 0.25f),
                R(foot, 0.197f, x, -0.001f, boot - 0.011f, 0.052f, 0.049f, knee, 0.74f));
            b.Tile(Finish.Ink, foot, V(x, 0.060f, 0.143f), V(boot * 1.69f, 0.032f, 0.068f), 0.010f, V(-6f, 0f, 0f));
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
            readonly List<Vector3> positions = new List<Vector3>(4096);
            readonly List<Vector2> uv = new List<Vector2>(4096);
            readonly List<BoneWeight> weights = new List<BoneWeight>(4096);
            readonly List<int> indices = new List<int>(12288);

            public void Loft(Finish finish, int sides, float shape, params Ring[] rings)
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
                        Add(r.Center + V(r.Width * dx, r.Tilt * dz, dz * (dz >= 0f ? r.Front : r.Back)), r.Weight, finish);
                    }
                    if (i > 0) Bridge(first + sides * (i - 1), sides);
                }
                Cap(first, sides, false, finish);
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

            public Mesh ToMesh(OperatorStyle style)
            {
                var bind = new Matrix4x4[(int)Bone.Count];
                for (int i = 0; i < bind.Length; i++) bind[i] = Matrix4x4.Translate(-OperatorVisual.Rest((Bone)i, style));
                var mesh = new Mesh { name = "Operator mesh / " + style, hideFlags = HideFlags.DontSave };
                mesh.SetVertices(positions);
                mesh.SetUVs(0, uv);
                mesh.SetTriangles(indices, 0);
                mesh.bindposes = bind;
                mesh.boneWeights = weights.ToArray();
                mesh.RecalculateNormals();
                return mesh;
            }
        }
    }
}
