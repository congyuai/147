using System.Collections.Generic;
using UnityEngine;
using Bone = Dustline.OperatorVisual.Bone;

namespace Dustline
{
    internal static partial class OperatorMesh
    {
        sealed class Wearable
        {
            public Mesh Mesh;
            public int Users;
        }

        static readonly Dictionary<(OperatorStyle, ItemDefinition), Wearable> wearables = new Dictionary<(OperatorStyle, ItemDefinition), Wearable>();

        internal static Mesh AcquireWearable(OperatorStyle style, ItemDefinition item)
        {
            var key = (style, item);
            if (!wearables.TryGetValue(key, out var fit))
            {
                var b = new Sculpt(1024);
                float w = TorsoWidth(style);
                switch (item.Kind)
                {
                    case ItemKind.Helmet: Helmet(b, style, item); break;
                    case ItemKind.Armor: Vest(b, style, w, item); break;
                    case ItemKind.Rig: Rig(b, w, item); break;
                    case ItemKind.Pack: Pack(b, w, item); break;
                }
                fit = new Wearable { Mesh = b.ToMesh(style, label: "Worn / " + item.Id) };
                wearables.Add(key, fit);
            }
            fit.Users++;
            return fit.Mesh;
        }

        internal static void ReleaseWearable(OperatorStyle style, ItemDefinition item)
        {
            var key = (style, item);
            var fit = wearables[key];
            if (--fit.Users != 0) return;
            wearables.Remove(key);
            Dispose(fit.Mesh);
        }

        static void Helmet(Sculpt b, OperatorStyle style, ItemDefinition item)
        {
            const Bone head = Bone.Head;
            float w = style == OperatorStyle.Female ? .092f : .099f;
            int tier = Mathf.Clamp(item.Tier, 1, 6);
            float cut = item.Id == "helmet_scout" ? .014f : .0015f * (6 - tier);
            b.Shell(Finish.Shell, 20, .91f,
                R(head, 1.645f + cut, 0f, .002f, w * .98f, .103f, .106f, tilt: .044f),
                R(head, 1.695f, 0f, 0f, w * 1.06f, .106f, .112f, tilt: .025f),
                R(head, 1.744f, 0f, -.004f, w * .96f, .095f, .104f, tilt: .010f),
                R(head, 1.778f, 0f, -.007f, w * .68f, .067f, .078f),
                R(head, 1.790f, 0f, -.008f, w * .37f, .040f, .050f));
            b.Shell(Finish.ShellRim, 20, .91f,
                R(head, 1.641f + cut, 0f, .002f, w * .975f, .103f, .105f, tilt: .044f),
                R(head, 1.651f + cut, 0f, .002f, w * 1.008f, .107f, .109f, tilt: .044f));
            b.Tile(Finish.Webbing, head, V(0f, 1.720f, .096f), V(.057f, .034f, .014f), .006f, V(-18f, 0f, 0f));
            for (int s = -1; s <= 1; s += 2)
            {
                b.Tile(Finish.Webbing, head, V(s * w * .81f, 1.603f, .033f), V(.012f, .096f, .009f), .003f, V(23f, s * 72f, s * 14f));
                if (tier >= 3)
                    b.Tile(Finish.Ink, head, V(s * w, 1.670f + cut, -.010f), V(.059f, .020f, .013f), .005f, V(0f, s * 92f, 0f));
            }
        }

        static void Vest(Sculpt b, OperatorStyle style, float w, ItemDefinition item)
        {
            const Bone chest = Bone.Chest;
            float plate = w - .012f, thickness = .006f + .0028f * Mathf.Clamp(item.Tier, 1, 6);
            b.Loft(Finish.Webbing, 16, .76f,
                R(chest, 1.126f, 0f, 0f, w + .003f, .114f, .109f),
                R(chest, 1.178f, 0f, .002f, w + .010f, .121f, .112f),
                R(chest, 1.300f, 0f, .003f, w + .022f, .127f, .113f),
                R(chest, 1.357f, 0f, .002f, w + .017f, .121f, .110f));
            b.Panel(Finish.Shell, chest, V(0, 1.270f, .117f + thickness * .38f), thickness * 1.8f,
                P(-plate * .76f, -.117f), P(plate * .76f, -.117f), P(plate, -.082f),
                P(plate * .89f, .053f), P(plate * .63f, .110f), P(-plate * .63f, .110f), P(-plate * .89f, .053f), P(-plate, -.082f));
            b.Tile(Finish.Shell, chest, V(0f, 1.260f, -.105f - thickness * .43f), V(plate * 1.66f, .220f, thickness * 1.65f), .034f,
                V(0f, 180f, 0f));
            Harness(b, w, .122f, .108f, .109f, .043f);
            Mark(b, chest, V(.048f, 1.333f, .120f + thickness * 1.3f), 8f, .79f);
            for (int i = 0; i < 3; i++)
                b.Tile(Finish.CanvasDark, chest, V(0f, 1.191f + .028f * i, .119f + thickness * 1.3f), V(plate * 1.45f, .009f, .006f), .003f);
            if ((item.Coverage & BodyCoverage.Abdomen) != 0)
                b.Tile(Finish.CanvasDark, chest, V(0f, 1.117f, .108f), V(w * 1.29f, .098f, .025f), .023f, V(-10f, 0f, 0f));
            if ((item.Coverage & (BodyCoverage.LeftArm | BodyCoverage.RightArm)) != 0)
                for (int s = -1; s <= 1; s += 2)
                {
                    Bone arm = s == 1 ? Bone.RightUpper : Bone.LeftUpper;
                    var a = OperatorVisual.Rest(arm, style);
                    b.Tile(Finish.Shell, arm, a + V(s * .048f, -.017f, -.026f), V(.059f, .095f, .032f), .015f, V(0f, s * 108f, s * 9f));
                }
        }

        static void Rig(Sculpt b, float w, ItemDefinition item)
        {
            const Bone chest = Bone.Chest;
            int count = Mathf.Clamp((item.Storage.Columns + 1) / 2, 2, 4);
            float width = 1.60f * w;
            float height = .081f + .012f * Mathf.Clamp(item.Storage.Rows, 1, 3);
            float depth = .039f + .003f * Mathf.Clamp(item.Tier, 1, 5);
            b.Tile(Finish.Webbing, chest, V(0f, 1.159f, .099f), V(width * 1.16f, height, .025f), .018f);
            for (int i = 0; i < count; i++)
            {
                float x = ((i + .5f) / count - .5f) * width;
                float z = .135f - .10f * Mathf.Abs(x);
                var turn = V(-5f, x / w * 24f, 0f);
                b.Tile(i == count - 1 && count > 2 ? Finish.CanvasDark : Finish.Canvas, chest, V(x, 1.153f, z),
                    V(width / count - .006f, height, depth), .011f, turn);
                b.Tile(Finish.Seam, chest, V(x, 1.153f + height * .37f, z + depth * .44f), V(width / count - .007f, .027f, .012f), .006f, turn);
                b.Tile(Finish.Webbing, chest, V(x, 1.151f, z + depth * .49f), V(.013f, .043f, .007f), .003f, turn);
            }
            for (int s = -1; s <= 1; s += 2)
                b.Tile(Finish.Webbing, chest, V(s * w * .88f, 1.174f, .036f), V(.13f, .023f, .013f), .005f, V(0f, s * 61f, 0f));
        }

        internal static float PackDepth(ItemDefinition item) => .091f + .009f * Mathf.Clamp(item.Tier, 1, 6) + .003f * item.Storage.Rows;

        static void Pack(Sculpt b, float w, ItemDefinition item)
        {
            const Bone chest = Bone.Chest;
            float depth = PackDepth(item), wide = w * (.62f + .025f * Mathf.Clamp(item.Storage.Columns, 1, 9));
            float tall = .242f + .015f * Mathf.Clamp(item.Storage.Rows, 1, 9);
            float y = 1.273f, z = -.102f - depth * .5f;
            b.Cloth(Finish.CanvasDark, 16, .62f, .002f,
                R(chest, y - tall * .48f, 0f, z + .009f, wide * .77f, depth * .40f, depth * .36f),
                R(chest, y - tall * .35f, 0f, z + .001f, wide * .98f, depth * .50f, depth * .49f),
                R(chest, y + tall * .16f, 0f, z, wide, depth * .53f, depth * .50f),
                R(chest, y + tall * .39f, 0f, z + .008f, wide * .94f, depth * .47f, depth * .46f),
                R(chest, y + tall * .48f, 0f, z + .013f, wide * .71f, depth * .37f, depth * .31f));
            b.Tile(Finish.Canvas, chest, V(0f, y - tall * .13f, -.106f - depth), V(wide * 1.62f, tall * .56f, .027f), .032f, V(0f, 180f, 0f));
            b.Tile(Finish.Seam, chest, V(0f, y - tall * .04f, -.119f - depth), V(wide * 1.47f, .011f, .009f), .004f, V(0f, 180f, -4f));
            b.Tile(Finish.Webbing, chest, V(0f, y + tall * .49f, -.144f), V(.070f, .022f, .023f), .006f);
            for (int s = -1; s <= 1; s += 2)
                b.Tile(Finish.Webbing, chest, V(s * wide * .84f, y + .029f, -.145f - depth * .61f), V(.025f, .032f, depth * .91f),
                    .006f, V(0f, s * 18f, 0f));
            Mark(b, chest, V(-wide * .30f, y + tall * .20f, -.103f - depth), 180f, .71f);
            Harness(b, w, .139f, .116f, .134f, .027f, .005f);
        }

        static void Harness(Sculpt b, float w, float front, float shoulder, float back, float strap, float lift = 0f)
        {
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * w * .68f;
                b.Tile(Finish.Webbing, Bone.Chest, V(x, 1.371f, front - .010f), V(strap, .165f, .019f), .006f, V(-26f, s * 9f, s * 8f));
                b.Tile(Finish.Webbing, Bone.Chest, V(x * .85f, 1.435f + lift, .016f), V(strap, .019f, shoulder * 1.80f), .006f, V(0f, s * 8f, s * 11f));
                b.Tile(Finish.Webbing, Bone.Chest, V(x, 1.383f, -back + .025f), V(strap, .143f, .020f), .006f, V(32f, 0f, -s * 6f));
                b.Tile(Finish.Metal, Bone.Chest, V(x, 1.343f, front + .012f), V(strap * .82f, .019f, .009f), .003f);
            }
        }
    }
}
