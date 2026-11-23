using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Dustline
{
    internal sealed class InventoryDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        internal Action<PointerEventData> Begin, Move, End;
        public void OnBeginDrag(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) Begin?.Invoke(e); }
        public void OnDrag(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) Move?.Invoke(e); }
        public void OnEndDrag(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) End?.Invoke(e); }
    }

    internal sealed class InventoryTile : Button
    {
        internal Func<PointerEventData, bool> Place;
        public override void OnPointerClick(PointerEventData e)
        {
            if (TouchControl.TouchWasCancelled(e)) return;
            if (e.button == PointerEventData.InputButton.Left && IsActive() && IsInteractable() && (Place?.Invoke(e) ?? false)) return;
            base.OnPointerClick(e);
        }
    }

    internal sealed class InventoryScroll : ScrollRect
    {
        internal Func<InventoryScroll, bool> TryAcquire;
        internal Action<InventoryScroll> Released;
        internal Func<bool> AllowScroll;
        int pointer = int.MinValue;

        public override void OnInitializePotentialDrag(PointerEventData e)
        {
            if (pointer == int.MinValue && (AllowScroll?.Invoke() ?? true)) base.OnInitializePotentialDrag(e);
        }

        public override void OnBeginDrag(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || pointer != int.MinValue || !(TryAcquire?.Invoke(this) ?? true)) return;
            pointer = e.pointerId;
            base.OnBeginDrag(e);
        }

        public override void OnDrag(PointerEventData e)
        {
            if (pointer == e.pointerId) base.OnDrag(e);
        }

        public override void OnEndDrag(PointerEventData e)
        {
            if (pointer != e.pointerId) return;
            base.OnEndDrag(e);
            Release();
        }

        public override void OnScroll(PointerEventData e)
        {
            if (pointer == int.MinValue && (AllowScroll?.Invoke() ?? true)) base.OnScroll(e);
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
