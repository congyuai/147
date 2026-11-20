using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Dustline
{
    public sealed class TouchControl : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        public PlayerInputReader Input;
        public string Action;
        public RectTransform Knob;
        int pointer = int.MinValue;
        Vector2 lastPosition;

        public void OnPointerDown(PointerEventData data)
        {
            if (pointer != int.MinValue) return;
            pointer = data.pointerId;
            lastPosition = data.position;
            Input.Hold(Action, true);
            if (Action != "Move" && Action != "Look") Input.Tap(Action);
            if (Action == "Move") SetStick(data);
        }
        public void OnDrag(PointerEventData data)
        {
            if (pointer != data.pointerId) return;
            if (Action == "Look")
            {
                float scale = GetComponentInParent<Canvas>().scaleFactor;
                Input.AddLook((data.position - lastPosition) / Mathf.Max(0.1f, scale));
            }
            if (Action == "Move") SetStick(data);
            lastPosition = data.position;
        }
        void SetStick(PointerEventData data)
        {
            var rect = (RectTransform)transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, data.position, data.pressEventCamera, out var local);
            var value = Vector2.ClampMagnitude((local - rect.rect.center) / (rect.rect.width * 0.35f), 1);
            Input.TouchMove = value;
            if (Knob != null) Knob.anchoredPosition = value * rect.rect.width * 0.28f;
        }
        public void OnPointerUp(PointerEventData data)
        {
            if (pointer != data.pointerId) return;
            Release();
        }
        void Release()
        {
            pointer = int.MinValue;
            if (Input == null) return;
            Input.Hold(Action, false);
            if (Action == "Move") Input.TouchMove = Vector2.zero;
            if (Knob != null) Knob.anchoredPosition = Vector2.zero;
        }
        void OnDisable() { Release(); }
        void OnApplicationFocus(bool focus) { if (!focus) Release(); }
        void OnApplicationPause(bool paused) { if (paused) Release(); }
    }
}
