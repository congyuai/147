using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Dustline
{
    public sealed class TouchControl : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler, IInitializePotentialDragHandler
    {
        public PlayerInputReader Input;
        public string Action;
        public RectTransform Knob;
        public Action<TouchControl> SelectForLayout;
        public Action<TouchControl, PointerEventData, Vector2> DragForLayout;
        bool editing;
        int pointer = int.MinValue;
        Vector2 lastPosition;
        Image face;
        Color idleColor, pressedColor;

        public void SetFace(Image image, Color pressed)
        {
            face = image;
            idleColor = image.color;
            pressedColor = pressed;
        }

        public void SetEditing(bool value)
        {
            if (editing == value) return;
            Release(true);
            editing = value;
        }

        public void OnInitializePotentialDrag(PointerEventData data)
        {
            if (editing || Action == "Move" || Action == "Look") data.useDragThreshold = false;
        }

        public void OnPointerDown(PointerEventData data)
        {
            if (pointer != int.MinValue || data.button != PointerEventData.InputButton.Left || (!editing && !Input.Gameplay)) return;
            pointer = data.pointerId;
            lastPosition = data.position;
            if (editing) { SelectForLayout?.Invoke(this); return; }
            if (face != null) face.color = pressedColor;
            Input.Hold(Action, true);
            if (Action != "Move" && Action != "Look") Input.Tap(Action);
            if (Action == "Move") SetStick(data);
        }
        public void OnDrag(PointerEventData data)
        {
            if (pointer != data.pointerId) return;
            if (editing)
            {
                DragForLayout?.Invoke(this, data, lastPosition);
                lastPosition = data.position;
                return;
            }
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
        void Release(bool cancel = false)
        {
            bool hadPointer = pointer != int.MinValue;
            pointer = int.MinValue;
            if (face != null) face.color = idleColor;
            if (Knob != null) Knob.anchoredPosition = Vector2.zero;
            if (Input == null || editing || !hadPointer) return;
            if (cancel && Action == "Aim") Input.CancelAimGesture();
            else Input.Hold(Action, false);
            if (Action == "Move") Input.TouchMove = Vector2.zero;
        }
        void OnDisable() { Release(true); }
        void OnApplicationFocus(bool focus) { if (!focus) Release(true); }
        void OnApplicationPause(bool paused) { if (paused) Release(true); }
    }
}
