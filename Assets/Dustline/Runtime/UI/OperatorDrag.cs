using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Dustline
{
    internal sealed class OperatorDrag : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IInitializePotentialDragHandler, IDragHandler, IEndDragHandler
    {
        const int None = int.MinValue;
        const float Slop = 7f;
        int pointer = None;
        Vector2 origin, previous;
        bool turning;
        internal Action<float> Turn;

        public void OnInitializePotentialDrag(PointerEventData data) => data.useDragThreshold = false;

        public void OnPointerDown(PointerEventData data)
        {
            if (pointer != None || data.button != PointerEventData.InputButton.Left || !Local(data, out var point)) return;
            if (!((RectTransform)transform).rect.Contains(point)) return;
            pointer = data.pointerId;
            previous = origin = point;
            turning = false;
        }

        public void OnDrag(PointerEventData data)
        {
            if (pointer != data.pointerId || data.button != PointerEventData.InputButton.Left || !Local(data, out var point)) return;
            if (!turning)
            {
                var traveled = point - origin;
                if (Mathf.Abs(traveled.x) < Slop || Mathf.Abs(traveled.x) < Mathf.Abs(traveled.y)) return;
                turning = true;
            }
            float width = ((RectTransform)transform).rect.width;
            if (width > 1f) Turn?.Invoke((point.x - previous.x) * (360f / width));
            previous = point;
        }

        public void OnPointerUp(PointerEventData data)
        {
            if (pointer == data.pointerId && data.button == PointerEventData.InputButton.Left) ResetPointer();
        }

        public void OnEndDrag(PointerEventData data) => OnPointerUp(data);

        bool Local(PointerEventData data, out Vector2 point) => RectTransformUtility.ScreenPointToLocalPointInRectangle(
            (RectTransform)transform, data.position, data.pressEventCamera, out point);

        void ResetPointer() { pointer = None; turning = false; }
        void OnDisable() => ResetPointer();
        void OnApplicationFocus(bool focused) { if (!focused) ResetPointer(); }
        void OnApplicationPause(bool paused) { if (paused) ResetPointer(); }
        void OnRectTransformDimensionsChange() => ResetPointer();
    }
}
