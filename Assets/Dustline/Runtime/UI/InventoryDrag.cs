using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Dustline
{
    internal sealed class InventoryTile : Button, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        internal const float HoldSeconds = .32f;
        internal Func<bool> CanPress;
        internal Func<int> SelectionStamp;
        internal Func<int, Vector2, Camera, Vector2, bool> Begin;
        internal Func<PointerEventData, bool> Place;
        internal Action<PointerEventData> Move, End;
        internal Action<int> Cancel;
        PointerEventData press;
        InventoryScroll scroll;
        Vector2 scrollAtPress, origin;
        Camera pressCamera;
        float deadline;
        int owner = int.MinValue, selectionAtPress;
        bool moving, draggingItem;
        int click = int.MinValue;

        public override void OnPointerDown(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || press != null || !IsInteractable() || !(CanPress?.Invoke() ?? true)) return;
            click = int.MinValue;
            press = e;
            owner = e.pointerId;
            selectionAtPress = SelectionStamp?.Invoke() ?? -1;
            origin = e.position;
            pressCamera = e.pressEventCamera;
            scroll = GetComponentInParent<InventoryScroll>();
            scrollAtPress = scroll != null ? scroll.content.anchoredPosition : Vector2.zero;
            deadline = Time.unscaledTime + HoldSeconds;
            base.OnPointerDown(e);
        }

        public void OnInitializePotentialDrag(PointerEventData e)
        {
            if (Owns(e)) scroll?.OnInitializePotentialDrag(e);
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (!Owns(e) || moving) return;
            if (draggingItem || Time.unscaledTime >= deadline && StartItemDrag(e.position))
            {
                e.eligibleForClick = false;
                return;
            }
            moving = true;
            e.eligibleForClick = false;
            scroll?.OnBeginDrag(e);
        }

        public void OnDrag(PointerEventData e)
        {
            if (!Owns(e)) return;
            if (draggingItem) Move?.Invoke(e);
            else if (moving) scroll?.OnDrag(e);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (Owns(e)) OnPointerUp(e);
        }

        public override void OnPointerUp(PointerEventData e)
        {
            if (!Owns(e)) return;
            bool item = draggingItem, pan = moving, stable = ScrollStable;
            bool held = Begin != null && Time.unscaledTime >= deadline;
            var view = scroll;
            ResetGesture();
            click = !item && !pan && !held && stable ? e.pointerId : int.MinValue;
            if (click == int.MinValue) e.eligibleForClick = false;
            base.OnPointerUp(e);
            if (item) End?.Invoke(e);
            else if (pan) view?.OnEndDrag(e);
        }

        public override void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || click != e.pointerId || TouchControl.TouchWasCancelled(e)
                || !SelectionStable || !(CanPress?.Invoke() ?? true)) return;
            click = int.MinValue;
            if (IsActive() && IsInteractable() && (Place?.Invoke(e) ?? false)) return;
            base.OnPointerClick(e);
        }

        void Update()
        {
            if (press != null && !moving && !draggingItem && Begin != null && Time.unscaledTime >= deadline)
            {
                if (!StartItemDrag(press.position)) CancelGesture();
            }
        }

        bool ScrollStable => scroll == null || (scroll.content.anchoredPosition - scrollAtPress).sqrMagnitude <= .25f;
        bool SelectionStable => (SelectionStamp?.Invoke() ?? -1) == selectionAtPress;

        bool StartItemDrag(Vector2 position)
        {
            if (!ScrollStable || !SelectionStable || !(Begin?.Invoke(owner, origin, pressCamera, position) ?? false)) return false;
            draggingItem = true;
            return true;
        }

        bool Owns(PointerEventData e) => press != null && e.button == PointerEventData.InputButton.Left && owner == e.pointerId;

        internal void CancelGesture()
        {
            if (press != null)
            {
                if (moving) scroll?.Cancel();
                if (draggingItem) Cancel?.Invoke(owner);
                base.OnPointerUp(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
            }
            ResetGesture();
            click = int.MinValue;
        }

        void ResetGesture()
        {
            press = null;
            owner = int.MinValue;
            pressCamera = null;
            scroll = null;
            moving = draggingItem = false;
        }

        protected override void OnDisable()
        {
            CancelGesture();
            base.OnDisable();
        }
    }

    internal sealed class InventoryScroll : ScrollRect
    {
        internal Func<InventoryScroll, bool> TryAcquire;
        internal Action<InventoryScroll> Released;
        internal Action WheelScroll;
        internal Func<bool> AllowScroll;
        int pointer = int.MinValue;
        internal bool Dragging => pointer != int.MinValue;

        public override void OnInitializePotentialDrag(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left && pointer == int.MinValue && (AllowScroll?.Invoke() ?? true))
                base.OnInitializePotentialDrag(e);
        }

        public override void OnBeginDrag(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || pointer != int.MinValue || !(TryAcquire?.Invoke(this) ?? true)) return;
            pointer = e.pointerId;
            Vector2 atThreshold = e.position;
            e.position = e.pressPosition;
            try { base.OnBeginDrag(e); }
            finally { e.position = atThreshold; }
        }

        public override void OnDrag(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left && pointer == e.pointerId) base.OnDrag(e);
        }

        public override void OnEndDrag(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || pointer != e.pointerId) return;
            base.OnEndDrag(e);
            Release();
        }

        public override void OnScroll(PointerEventData e)
        {
            if (pointer != int.MinValue || !(AllowScroll?.Invoke() ?? true)) return;
            WheelScroll?.Invoke();
            base.OnScroll(e);
        }

        protected override void OnDisable()
        {
            Release();
            base.OnDisable();
        }

        internal void Cancel()
        {
            if (pointer == int.MinValue) return;
            base.OnEndDrag(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, pointerId = pointer });
            Release();
        }

        void Release()
        {
            if (pointer == int.MinValue) return;
            pointer = int.MinValue;
            Released?.Invoke(this);
        }
    }

    internal sealed class GearDropSlot : MonoBehaviour
    {
        internal EquipmentSlot Slot;
    }
}
