using UnityEngine;
using UnityEngine.UI;

namespace Dustline
{
    internal sealed class BodyFigure : MaskableGraphic
    {
        static readonly Vector2[][] Shape =
        {
            Polygon(.42f,.04f, .58f,.04f, .65f,.08f, .65f,.17f, .58f,.22f, .42f,.22f, .35f,.17f, .35f,.08f),
            Polygon(.36f,.24f, .64f,.24f, .75f,.28f, .65f,.46f, .35f,.46f, .25f,.28f),
            Polygon(.35f,.48f, .65f,.48f, .68f,.60f, .59f,.66f, .41f,.66f, .32f,.60f),
            Polygon(.23f,.29f, .30f,.36f, .21f,.56f, .13f,.62f, .05f,.60f, .14f,.44f),
            Polygon(.77f,.29f, .86f,.44f, .95f,.60f, .87f,.62f, .79f,.56f, .70f,.36f),
            Polygon(.32f,.62f, .46f,.67f, .43f,.84f, .38f,.97f, .23f,.97f, .27f,.85f),
            Polygon(.68f,.62f, .73f,.85f, .77f,.97f, .62f,.97f, .57f,.84f, .54f,.67f)
        };
        static readonly Vector2[] Pins =
        {
            new Vector2(.50f,.13f), new Vector2(.50f,.34f), new Vector2(.50f,.55f),
            new Vector2(.17f,.48f), new Vector2(.83f,.48f), new Vector2(.36f,.79f), new Vector2(.64f,.79f)
        };
        readonly Color32[] tones = new Color32[BodyCondition.Count];
        Color32 rim;

        internal void Initialize(Color fill, Color outline)
        {
            raycastTarget = false;
            rim = outline;
            for (int i = 0; i < tones.Length; i++) tones[i] = fill;
            SetVerticesDirty();
        }

        internal void SetTone(BodyPart part, Color value)
        {
            int index = (int)part;
            Color32 next = value;
            if (tones[index].Equals(next)) return;
            tones[index] = next;
            SetVerticesDirty();
        }

        internal static Vector2 Pin(BodyPart part, float x, float y, float w, float h)
        {
            var point = Pins[(int)part];
            return new Vector2(x + point.x * w, y + point.y * h);
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect box = rectTransform.rect;
            for (int i = 0; i < Shape.Length; i++)
            {
                Face(Shape[i], Pins[i], 1f, rim);
                Face(Shape[i], Pins[i], .83f, tones[i]);
            }

            void Face(Vector2[] outline, Vector2 center, float scale, Color32 tint)
            {
                int index = mesh.currentVertCount;
                foreach (var point in outline)
                {
                    Vector2 p = center + (point - center) * scale;
                    mesh.AddVert(new Vector3(box.xMin + p.x * box.width, box.yMax - p.y * box.height), tint, Vector2.zero);
                }
                for (int j = 1; j < outline.Length - 1; j++) mesh.AddTriangle(index, index + j, index + j + 1);
            }
        }

        static Vector2[] Polygon(params float[] points)
        {
            var poly = new Vector2[points.Length / 2];
            for (int i = 0; i < poly.Length; i++) poly[i] = new Vector2(points[i * 2], points[i * 2 + 1]);
            return poly;
        }
    }
}
