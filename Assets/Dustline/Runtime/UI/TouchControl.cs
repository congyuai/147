using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using InputTouchPhase = UnityEngine.InputSystem.TouchPhase;
using Touchscreen = UnityEngine.InputSystem.Touchscreen;

namespace Dustline
{
    public sealed class TouchControl : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler, IInitializePotentialDragHandler, ICanvasRaycastFilter
    {
        public PlayerInputReader Input;
        public string Action;
        public RectTransform Knob;
        internal RectTransform SprintTarget;
        public Action<TouchControl> SelectForLayout;
        public Action<TouchControl, PointerEventData, Vector2> DragForLayout;
        internal Action<Vector2> GestureStart;
        internal Action<Vector2> GestureMove;
        internal Action<Vector2, bool> GestureEnd;
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

        internal void ShowLatched(bool active)
        {
            if (face != null) face.color = active || pointer != int.MinValue ? pressedColor : idleColor;
        }

        public void SetEditing(bool value)
        {
            if (editing == value) return;
            Release(true);
            editing = value;
        }

        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
            => editing || GestureStart == null || ContainsDialPoint(screenPoint, eventCamera);

        internal bool ContainsDialPoint(Vector2 point, Camera eventCamera)
        {
            var rect = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, point, eventCamera, out var local)) return false;
            float radius = Mathf.Min(rect.rect.width, rect.rect.height) * .5f;
            return (local - rect.rect.center).sqrMagnitude <= radius * radius;
        }

        public void OnInitializePotentialDrag(PointerEventData data)
        {
            if (editing || GestureStart != null || Action == "Move" || Action == "Look") data.useDragThreshold = false;
        }

        public void OnPointerDown(PointerEventData data)
        {
            if (pointer != int.MinValue || data.button != PointerEventData.InputButton.Left || (!editing && !Input.Gameplay)) return;
            pointer = data.pointerId;
            lastPosition = data.position;
            if (editing) { SelectForLayout?.Invoke(this); return; }
            if (face != null) face.color = pressedColor;
            if (GestureStart != null) { GestureStart(data.position); return; }
            Input.Hold(Action, true);
            if (Action != "Move" && Action != "Look") Input.Tap(Action);
            if (Action == "Move") SetStick(data, false);
        }
        public void OnDrag(PointerEventData data)
        {
            if (pointer != data.pointerId || data.button != PointerEventData.InputButton.Left) return;
            if (editing)
            {
                DragForLayout?.Invoke(this, data, lastPosition);
                lastPosition = data.position;
                return;
            }
            if (GestureMove != null) { GestureMove(data.position); lastPosition = data.position; return; }
            if (!Input.Gameplay) return;
            if (Action == "Look")
            {
                float scale = GetComponentInParent<Canvas>().scaleFactor;
                Input.AddLook((data.position - lastPosition) / Mathf.Max(0.1f, scale));
            }
            if (Action == "Move") SetStick(data, true);
            lastPosition = data.position;
        }
        void SetStick(PointerEventData data, bool dragging)
        {
            var rect = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, data.position, data.pressEventCamera, out var local)) return;
            var raw = (local - rect.rect.center) / (rect.rect.width * .35f);
            bool target = dragging && SprintTarget != null && SprintTarget.gameObject.activeInHierarchy
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(SprintTarget, data.position, data.pressEventCamera, out var hit)
                && hit.sqrMagnitude <= SprintTarget.rect.width * SprintTarget.rect.width * .25f;
            Input.MoveTouchStick(raw, target);
            if (Knob != null) Knob.anchoredPosition = Vector2.ClampMagnitude(raw, 1f) * rect.rect.width * .28f;
        }
        public void OnPointerUp(PointerEventData data)
        {
            if (pointer != data.pointerId || data.button != PointerEventData.InputButton.Left) return;
            lastPosition = data.position;
            Release(!editing && TouchWasCancelled(data));
        }

        internal static bool TouchWasCancelled(PointerEventData data)
        {
            if (data is not ExtendedPointerEventData extended || extended.device is not Touchscreen screen) return false;
            if (!screen.added || extended.touchId == 0) return true;
            foreach (var contact in screen.touches)
                if (contact.touchId.ReadValue() == extended.touchId) return contact.phase.ReadValue() != InputTouchPhase.Ended;
            return true;
        }
        void Release(bool cancel = false)
        {
            bool hadPointer = pointer != int.MinValue;
            pointer = int.MinValue;
            if (face != null && !editing) face.color = idleColor;
            if (Knob != null) Knob.anchoredPosition = Vector2.zero;
            if (Input == null || editing || !hadPointer) return;
            if (GestureEnd != null) { GestureEnd(lastPosition, cancel); return; }
            if (cancel) Input.CancelAction(Action);
            else Input.Hold(Action, false);
            if (Action == "Move") Input.ReleaseTouchStick(cancel);
        }
        internal void CancelPointer() => Release(true);

        void OnDisable() => CancelPointer();
        void OnApplicationFocus(bool focus) { if (!focus) CancelPointer(); }
        void OnApplicationPause(bool paused) { if (paused) CancelPointer(); }
    }
}
