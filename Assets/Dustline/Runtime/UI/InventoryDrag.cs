using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Dustline
{
    internal sealed class InventoryDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        internal Action<PointerEventData> Begin, Move, End;
        public void OnBeginDrag(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) Begin?.Invoke(e); }
        public void OnDrag(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) Move?.Invoke(e); }
        public void OnEndDrag(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) End?.Invoke(e); }
    }

    internal sealed class GearDropSlot : MonoBehaviour
    {
        internal EquipmentSlot Slot;
    }
}
