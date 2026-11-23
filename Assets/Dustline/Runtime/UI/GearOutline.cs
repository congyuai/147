using System;
using UnityEngine;
using UnityEngine.UI;

namespace Dustline
{
    internal sealed class GearOutline : MaskableGraphic
    {
        static readonly float[][] Shapes =
        {
            new[] {.06f,.36f, .27f,.42f, .39f,.35f, .65f,.35f, .68f,.28f, .76f,.28f, .77f,.37f, .97f,.37f, .97f,.46f, .75f,.46f, .68f,.53f, .60f,.53f, .57f,.72f, .49f,.72f, .48f,.53f, .41f,.54f, .37f,.68f, .29f,.68f, .32f,.50f, .24f,.49f, .08f,.64f},
            new[] {.12f,.74f, .14f,.40f, .31f,.17f, .64f,.15f, .83f,.34f, .91f,.64f, .96f,.70f, .71f,.75f, .58f,.73f, .52f,.84f, .28f,.83f, .29f,.67f},
            new[] {.16f,.39f, .26f,.12f, .40f,.17f, .42f,.31f, .59f,.31f, .61f,.17f, .75f,.12f, .84f,.39f, .80f,.86f, .67f,.93f, .32f,.93f, .19f,.86f},
            new[] {.10f,.38f, .33f,.36f, .41f,.22f, .58f,.22f, .67f,.36f, .90f,.38f, .88f,.76f, .70f,.84f, .29f,.84f, .12f,.76f},
            new[] {.23f,.34f, .33f,.23f, .42f,.23f, .43f,.12f, .59f,.12f, .60f,.23f, .70f,.23f, .79f,.37f, .78f,.79f, .67f,.90f, .31f,.90f, .22f,.77f},
            new[] {.19f,.19f, .79f,.19f, .88f,.27f, .88f,.42f, .63f,.42f, .59f,.60f, .43f,.60f, .38f,.51f, .29f,.86f, .12f,.86f, .24f,.42f, .13f,.42f, .13f,.27f},
            new[] {.09f,.70f, .38f,.49f, .35f,.41f, .44f,.35f, .49f,.41f, .78f,.19f, .93f,.17f, .86f,.36f, .56f,.59f, .58f,.67f, .48f,.73f, .44f,.66f, .14f,.85f}
        };
        EquipmentSlot slot;

        internal void Initialize(EquipmentSlot value, Color tint)
        {
            slot = value;
            color = tint;
            raycastTarget = false;
            SetVerticesDirty();
        }

        static readonly System.Collections.Generic.IComparer<Vector3> Compare =
            System.Collections.Generic.Comparer<Vector3>.Create((a, b) => a.x.CompareTo(b.x));

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (slot == EquipmentSlot.None) return;
            var shape = Shapes[slot switch
            {
                EquipmentSlot.Weapon or EquipmentSlot.PrimaryTwo => 0, EquipmentSlot.Helmet => 1, EquipmentSlot.Armor => 2,
                EquipmentSlot.Rig => 3, EquipmentSlot.Sidearm => 5, EquipmentSlot.Melee => 6, _ => 4
            }];
            Rect bounds = rectTransform.rect;
            var levels = new float[shape.Length / 2];
            var intersections = new Vector3[levels.Length];
            for (int i = 0; i < levels.Length; i++) levels[i] = shape[2 * i + 1];
            Array.Sort(levels);
            for (int k = 1; k < levels.Length; k++)
            {
                float top = levels[k - 1], bottom = levels[k], middle = (top + bottom) * .5f;
                if (bottom <= top) continue;
                int count = 0;
                for (int i = 0; i < shape.Length; i += 2)
                {
                    int j = (i + 2) % shape.Length;
                    float a = shape[i + 1], b = shape[j + 1];
                    if ((a > middle) == (b > middle)) continue;
                    float x1 = shape[i] + (top - a) / (b - a) * (shape[j] - shape[i]);
                    float x2 = shape[i] + (bottom - a) / (b - a) * (shape[j] - shape[i]);
                    intersections[count++] = new Vector3((x1 + x2) * .5f, x1, x2);
                }
                Array.Sort(intersections, 0, count, Compare);
                for (int i = 0; i + 1 < count; i += 2)
                {
                    var left = intersections[i]; var right = intersections[i + 1];
                    int first = mesh.currentVertCount;
                    Emit(left.y, top); Emit(right.y, top); Emit(right.z, bottom); Emit(left.z, bottom);
                    mesh.AddTriangle(first, first + 1, first + 2);
                    mesh.AddTriangle(first + 2, first + 3, first);
                }
            }

            void Emit(float x, float y) => mesh.AddVert(new Vector3(bounds.xMin + x * bounds.width, bounds.yMax - y * bounds.height), color, Vector2.zero);
        }
    }
}
