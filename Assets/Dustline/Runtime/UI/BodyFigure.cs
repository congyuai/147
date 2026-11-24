using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Dustline
{
    internal sealed class BodyFigure : MaskableGraphic, IPointerClickHandler
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
        static readonly Color32 Healthy = new Color32(115, 164, 137, 255);
        static readonly Color32 LightYellow = new Color32(237, 219, 142, 255);
        static readonly Color32 Orange = new Color32(231, 151, 73, 255);
        static readonly Color32 Red = new Color32(218, 77, 72, 255);
        static readonly Color32 DeepRed = new Color32(126, 39, 49, 255);
        static readonly Color32 Black = new Color32(8, 10, 11, 255);
        static readonly Color32 Edge = new Color32(52, 73, 79, 255);
        static readonly Color32 DestroyedEdge = new Color32(239, 240, 229, 255);
        static readonly Color32 SelectedEdge = new Color32(147, 240, 222, 255);
        readonly Color32[] tones = new Color32[BodyCondition.Count];
        readonly bool[] destroyed = new bool[BodyCondition.Count];
        BodyPart? selected;
        Action<BodyPart> select;
        float opacity = 1f;

        internal void Initialize(Action<BodyPart> onSelect, float alpha = 1f)
        {
            select = onSelect;
            opacity = alpha;
            SetVerticesDirty();
            raycastTarget = select != null;
            SetCondition(null);
        }

        internal void SetCondition(BodyCondition body, BodyPart? selection = null)
        {
            bool dirty = selected != selection;
            selected = selection;
            for (int i = 0; i < tones.Length; i++)
            {
                var part = (BodyPart)i;
                float maximum = body?.Maximum(part) ?? 0f;
                float fraction = maximum > 0f ? body.Current(part) / maximum : 0f;
                var tone = fraction >= .999f ? Healthy : fraction >= .75f ? LightYellow : fraction >= .50f ? Orange
                    : fraction >= .25f ? Red : fraction > 0f ? DeepRed : Black;
                bool lost = body != null && (body.Injuries(part) & Injury.Destroyed) != 0;
                if (tones[i].Equals(tone) && destroyed[i] == lost) continue;
                tones[i] = tone;
                destroyed[i] = lost;
                dirty = true;
            }
            if (dirty) SetVerticesDirty();
        }

        internal static Vector2 Pin(BodyPart part, float x, float y, float w, float h)
        {
            var point = Pins[(int)part];
            return new Vector2(x + point.x * w, y + point.y * h);
        }

        public override bool Raycast(Vector2 screenPoint, Camera eventCamera) =>
            base.Raycast(screenPoint, eventCamera) && TryHitPart(screenPoint, eventCamera, out _);

        public void OnPointerClick(PointerEventData data)
        {
            if (select != null && data.button == PointerEventData.InputButton.Left && !TouchControl.TouchWasCancelled(data)
                && TryHitPart(data.position, data.pressEventCamera, out var part)) select(part);
        }

        bool TryHitPart(Vector2 screenPoint, Camera eventCamera, out BodyPart part)
        {
            part = default;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPoint, eventCamera, out var local)) return false;
            Rect box = rectTransform.rect;
            if (!box.Contains(local) || box.width <= 0f || box.height <= 0f) return false;
            var p = new Vector2((local.x - box.xMin) / box.width, (box.yMax - local.y) / box.height);
            for (int i = 0; i < Shape.Length; i++)
            {
                var polygon = Shape[i];
                bool inside = false;
                for (int j = 0, previous = polygon.Length - 1; j < polygon.Length; previous = j++)
                {
                    var a = polygon[previous];
                    var b = polygon[j];
                    if ((a.y > p.y) != (b.y > p.y) && p.x < a.x + (p.y - a.y) * (b.x - a.x) / (b.y - a.y)) inside = !inside;
                }
                if (inside) { part = (BodyPart)i; return true; }
            }
            return false;
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect box = rectTransform.rect;
            for (int i = 0; i < Shape.Length; i++)
            {
                bool active = selected == (BodyPart)i;
                Face(Shape[i], Pins[i], 1f, destroyed[i] ? DestroyedEdge : active ? SelectedEdge : Edge);
                Face(Shape[i], Pins[i], active ? .79f : destroyed[i] ? .84f : .91f, tones[i]);
            }

            void Face(Vector2[] outline, Vector2 center, float scale, Color32 tint)
            {
                int index = mesh.currentVertCount;
                tint.a = (byte)(tint.a * opacity);
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
