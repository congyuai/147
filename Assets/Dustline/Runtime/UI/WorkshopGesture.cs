using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Dustline
{
    internal sealed class WorkshopGesture : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler,
        IInitializePotentialDragHandler, IScrollHandler
    {
        internal Action<Vector2> Rotate, Tap;
        internal Action<float> Zoom;
        const int None = int.MinValue;
        int first = None, second = None;
        bool firstTouch, moved, suppressTap;
        Vector2 a, b, start;

        public void OnInitializePotentialDrag(PointerEventData e) => e.useDragThreshold = false;

        public void OnPointerDown(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || !Point(e, out var point)) return;
            Press(e.pointerId, point, e is ExtendedPointerEventData extended && extended.device is Touchscreen);
        }

        internal void Press(int id, Vector2 point, bool touch)
        {
            if (id == first || id == second) return;
            if (first == None)
            {
                first = id; firstTouch = touch; a = start = point;
                moved = suppressTap = false;
            }
            else if (second == None && firstTouch && touch)
            {
                second = id; b = point;
                moved = suppressTap = true;
            }
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left && Point(e, out var point)) Move(e.pointerId, point);
        }

        internal void Move(int id, Vector2 point)
        {
            if (id != first && id != second) return;
            if (second != None)
            {
                float before = Vector2.Distance(a, b);
                if (id == first) a = point; else b = point;
                float distance = Vector2.Distance(a, b);
                if (before >= 6f && distance >= 6f) Zoom?.Invoke(distance / before);
                return;
            }
            Vector2 previous = a;
            a = point;
            if (!moved && (point - start).sqrMagnitude < 49f) return;
            Rotate?.Invoke(point - (moved ? previous : start));
            moved = true;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            bool ok = Point(e, out var point);
            Release(e.pointerId, point, !ok || TouchControl.TouchWasCancelled(e));
        }

        internal void Release(int id, Vector2 point, bool cancelled)
        {
            if (id == second) { second = None; return; }
            if (id != first) return;
            if (second != None)
            {
                first = second; second = None; a = start = b;
                return;
            }
            first = None;
            if (!cancelled && !suppressTap && !moved && (point - start).sqrMagnitude < 49f) Tap?.Invoke(point);
        }

        public void OnScroll(PointerEventData e)
        {
            if (first == None) Zoom?.Invoke(Mathf.Exp(Mathf.Clamp(e.scrollDelta.y * .12f, -.6f, .6f)));
        }

        bool Point(PointerEventData e, out Vector2 result) => RectTransformUtility.ScreenPointToLocalPointInRectangle(
            (RectTransform)transform, e.position, e.pressEventCamera, out result);
        internal void ResetPointers() { first = second = None; }
        void OnDisable() => ResetPointers();
        void OnApplicationFocus(bool focus) { if (!focus) ResetPointers(); }
        void OnApplicationPause(bool paused) { if (paused) ResetPointers(); }
    }

    internal sealed class PartFrame : MaskableGraphic
    {
        protected override void OnEnable()
        {
            base.OnEnable();
            raycastTarget = false;
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var r = rectTransform.rect;
            float length = Mathf.Min(13, Mathf.Min(r.width, r.height) * .23f);
            var accent = new Color32(235, 185, 100, 225);
            Quad(r.xMin, r.yMin, r.xMax, r.yMax, new Color32(233, 169, 84, 24));
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 2; x++)
                {
                    float px = x == 0 ? r.xMin : r.xMax, py = y == 0 ? r.yMin : r.yMax;
                    float sx = x == 0 ? 1 : -1, sy = y == 0 ? 1 : -1;
                    Quad(px, py, px + length * sx, py + 2 * sy, accent);
                    Quad(px, py, px + 2 * sx, py + length * sy, accent);
                }

            void Quad(float x1, float y1, float x2, float y2, Color32 tint)
            {
                float left = Mathf.Min(x1, x2), right = Mathf.Max(x1, x2), bottom = Mathf.Min(y1, y2), top = Mathf.Max(y1, y2);
                int index = mesh.currentVertCount;
                mesh.AddVert(new Vector3(left, bottom), tint, Vector2.zero);
                mesh.AddVert(new Vector3(left, top), tint, Vector2.zero);
                mesh.AddVert(new Vector3(right, top), tint, Vector2.zero);
                mesh.AddVert(new Vector3(right, bottom), tint, Vector2.zero);
                mesh.AddTriangle(index, index + 1, index + 2);
                mesh.AddTriangle(index + 2, index + 3, index);
            }
        }
    }
}
