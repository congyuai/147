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
            new[] {.23f,.34f, .33f,.23f, .42f,.23f, .43f,.12f, .59f,.12f, .60f,.23f, .70f,.23f, .79f,.37f, .78f,.79f, .67f,.90f, .31f,.90f, .22f,.77f}
        };
        EquipmentSlot slot;

        internal void Initialize(EquipmentSlot value, Color tint)
        {
            slot = value;
            color = tint;
            raycastTarget = false;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (slot == EquipmentSlot.None) return;
            var shape = Shapes[slot switch
            {
                EquipmentSlot.Weapon => 0, EquipmentSlot.Helmet => 1, EquipmentSlot.Armor => 2, EquipmentSlot.Rig => 3, _ => 4
            }];
            for (int i = 0; i < shape.Length; i += 2) Line(shape[i], shape[i + 1], shape[(i + 2) % shape.Length], shape[(i + 3) % shape.Length]);
            if (slot == EquipmentSlot.Rig || slot == EquipmentSlot.Pack)
            {
                Line(.31f, .55f, .69f, .55f);
                Line(.31f, .72f, .69f, .72f);
                if (slot == EquipmentSlot.Rig) Line(.50f, .55f, .50f, .72f);
            }

            void Line(float x1, float y1, float x2, float y2)
            {
                Rect box = rectTransform.rect;
                var a = new Vector2(box.xMin + x1 * box.width, box.yMax - y1 * box.height);
                var b = new Vector2(box.xMin + x2 * box.width, box.yMax - y2 * box.height);
                var d = (b - a).normalized;
                var n = new Vector2(-d.y, d.x) * 1.1f;
                int i = mesh.currentVertCount;
                mesh.AddVert(a - n, color, Vector2.zero);
                mesh.AddVert(a + n, color, Vector2.zero);
                mesh.AddVert(b + n, color, Vector2.zero);
                mesh.AddVert(b - n, color, Vector2.zero);
                mesh.AddTriangle(i, i + 1, i + 2);
                mesh.AddTriangle(i + 2, i + 3, i);
            }
        }
    }
}
