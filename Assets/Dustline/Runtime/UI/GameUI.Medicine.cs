using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Dustline
{
    public sealed partial class GameUI
    {
        const float MedicineHoldSeconds = .36f;
        internal const float WheelRadius = 155f;
        internal const float WheelInnerRadius = 53f;
        readonly List<ItemDefinition> wheelItems = new List<ItemDefinition>(3);
        RectTransform medicineWheel, medicineHub;
        TouchControl medicineControl;
        MedicineWheelRing medicineRing;
        Image medicineGlyph, medicineDialProgress;
        RawImage medicineModel;
        string shownMedicine;
        Text medicineCount, medicineLabel;
        Vector2 medicinePointer, medicineStart;
        float medicinePressAt;
        bool medicineHeld, medicineLong;
        bool MedicineWheelOpen => medicineWheel != null;

        void BindMedicine(TouchControl control, Image glyph)
        {
            medicineControl = control;
            medicineGlyph = glyph;
            medicineGlyph.preserveAspect = true;
            var root = (RectTransform)control.transform;
            var picture = Stretch("Selected medicine model", root);
            picture.anchorMin = new Vector2(.17f, .17f);
            picture.anchorMax = new Vector2(.83f, .83f);
            medicineModel = picture.gameObject.AddComponent<RawImage>();
            medicineModel.color = Color.clear;
            medicineModel.raycastTarget = false;
            medicineCount = Label(root, "", 15, 0, 0, 25, 20, TextAnchor.MiddleCenter, Pale);
            var countRect = medicineCount.rectTransform;
            countRect.anchorMin = countRect.anchorMax = new Vector2(.68f, .26f);
            countRect.pivot = Vector2.one * .5f;
            countRect.anchoredPosition = Vector2.zero;
            var badge = medicineCount.gameObject.AddComponent<Outline>();
            badge.effectColor = Ink;
            badge.effectDistance = new Vector2(1.3f, -1.3f);
            medicineDialProgress = Fill(Stretch("Treatment progress", root), Aqua);
            medicineDialProgress.sprite = icons[RaidIcon.ControlRing];
            medicineDialProgress.type = Image.Type.Filled;
            medicineDialProgress.fillMethod = Image.FillMethod.Radial360;
            medicineDialProgress.fillOrigin = (int)Image.Origin360.Top;
            medicineDialProgress.fillAmount = 0f;
            control.GestureStart = BeginMedicineGesture;
            control.GestureMove = MoveMedicineGesture;
            control.GestureEnd = EndMedicineGesture;
        }

        void UpdateMedicineButton(PlayerController player)
        {
            if (!session.Input.TouchMode) return;
            var stack = session.QuickMedicine();
            var definition = player.TreatmentItem ?? (stack != null ? session.Catalog.Find(stack.ItemId) : null);
            medicineGlyph.color = Muted;
            if (definition == null) { itemIcons.Unbind(medicineModel); shownMedicine = null; }
            else if (shownMedicine != definition.Id || medicineModel.texture == null)
            {
                shownMedicine = definition.Id;
                var rect = medicineModel.rectTransform;
                float aspect = Mathf.Clamp(definition.Width / (float)definition.Height, .25f, 4f);
                float width = .66f * Mathf.Min(1f, aspect), height = .66f * Mathf.Min(1f, 1f / aspect);
                rect.anchorMin = new Vector2(.5f - width * .5f, .5f - height * .5f);
                rect.anchorMax = new Vector2(.5f + width * .5f, .5f + height * .5f);
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                itemIcons.Bind(medicineModel, definition, stack, session.Catalog);
            }
            medicineGlyph.enabled = medicineModel.texture == null || medicineModel.texture is RenderTexture target && !target.IsCreated();
            int count = definition == null ? 0 : session.ActiveFieldBag.RemainingUses(definition.Id);
            medicineCount.text = count > 0 ? count.ToString() : "";
            medicineDialProgress.fillAmount = player.TreatmentProgress;
        }

        void BeginMedicineGesture(Vector2 point)
        {
            if (!session.InField || !session.Input.Gameplay || !session.Player.Health.Alive || session.Player.WeaponView.MedicineBusy) return;
            if (!medicineControl.ContainsDialPoint(point, null)) return;
            medicineStart = medicinePointer = point;
            medicinePressAt = Time.unscaledTime;
            medicineHeld = true;
            medicineLong = false;
        }

        void MoveMedicineGesture(Vector2 point)
        {
            medicinePointer = point;
            UpdateMedicineHighlight();
        }

        void UpdateMedicineGesture()
        {
            if (!medicineHeld || medicineLong) return;
            if (!session.Input.Gameplay || session.Player.WeaponView.MedicineBusy)
            {
                CloseMedicineWheel(false);
                return;
            }
            if (Time.unscaledTime - medicinePressAt >= MedicineHoldSeconds) OpenMedicineWheel();
        }

        void EndMedicineGesture(Vector2 point, bool cancelled)
        {
            if (!medicineHeld) return;
            if (cancelled) { CloseMedicineWheel(true); return; }
            if (!medicineLong && Time.unscaledTime - medicinePressAt >= MedicineHoldSeconds)
            {
                medicinePointer = point;
                if (session.Input.Gameplay && !session.Player.WeaponView.MedicineBusy) OpenMedicineWheel();
                else medicineLong = true;
            }
            bool quick = !medicineLong && medicineControl.ContainsDialPoint(point, null);
            if (MedicineWheelOpen)
            {
                int index = MedicineIndex(point);
                if (index >= 0) session.SelectMedicine(wheelItems[index].Id);
            }
            CloseMedicineWheel(true);
            if (quick && session.Input.Gameplay) session.Input.TapMedicine();
        }

        void OpenMedicineWheel()
        {
            medicineLong = true;
            var bag = session.ActiveFieldBag;
            wheelItems.Clear();
            foreach (var item in session.Catalog.Items)
                if (item.Kind == ItemKind.Medicine && bag.Count(item.Id) > 0) wheelItems.Add(item);
            if (wheelItems.Count == 0) { Notify("没有可用的医疗品。"); return; }

            medicineWheel = Stretch("Medicine selection", safe);
            Fill(medicineWheel, new Color32(6, 12, 16, 65), true);
            medicineWheel.SetSiblingIndex(page.GetSiblingIndex());
            medicineHub = Anchor("Medicine wheel centre", medicineWheel, .5f, .5f);
            medicineHub.sizeDelta = Vector2.one * 2f * WheelRadius;
            float half = WheelRadius + 12f;
            float scale = Mathf.Min(1f, Mathf.Min(safe.rect.width, safe.rect.height) / (half * 2f));
            medicineHub.localScale = Vector3.one * Mathf.Max(0.01f, scale);
            Vector2 p = safe.InverseTransformPoint(medicineControl.transform.position);
            Vector2 center = p - safe.rect.center;
            Vector2 bounds = Vector2.Max(Vector2.zero, safe.rect.size * .5f - Vector2.one * (half * scale));
            medicineHub.anchoredPosition = new Vector2(Mathf.Clamp(center.x, -bounds.x, bounds.x), Mathf.Clamp(center.y, -bounds.y, bounds.y));
            medicineRing = medicineHub.gameObject.AddComponent<MedicineWheelRing>();
            medicineRing.raycastTarget = false;
            medicineRing.Count = wheelItems.Count;
            var centre = Anchor("Wheel deadzone", medicineHub, .5f, .5f);
            centre.sizeDelta = Vector2.one * 103f;
            Fill(centre, new Color32(23, 40, 46, 230)).sprite = icons[RaidIcon.Dial];
            medicineLabel = Label(centre, "", 17, 6, 14, 91, 72, TextAnchor.MiddleCenter, Pale);
            for (int i = 0; i < wheelItems.Count; i++)
            {
                var item = wheelItems[i];
                float angle = i * Mathf.PI * 2f / wheelItems.Count;
                var slot = Anchor("Medicine option", medicineHub, .5f, .5f);
                slot.anchoredPosition = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * 104f;
                slot.sizeDelta = new Vector2(82f, 64f);
                DrawItemIcon(slot, item, null, 19, 1, 44, 37);
                Label(slot, item.DisplayName + " ×" + bag.RemainingUses(item.Id), 12, 0, 38, 82, 26, TextAnchor.UpperCenter, Pale);
            }
            ReleaseOtherFieldPointers();
            session.SyncControls();
            UpdateMedicineHighlight();
        }

        void UpdateMedicineHighlight()
        {
            if (!MedicineWheelOpen) return;
            int index = MedicineIndex(medicinePointer);
            medicineRing.Active = index;
            string text = index < 0 ? "滑动选药\n松手切换" : wheelItems[index].DisplayName;
            if (medicineLabel.text != text) medicineLabel.text = text;
        }

        int MedicineIndex(Vector2 screen)
        {
            float slop = 18f * uiCanvas.scaleFactor * medicineHub.localScale.x;
            if ((screen - medicineStart).sqrMagnitude <= slop * slop
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(medicineHub, screen, null, out var offset)) return -1;
            return WheelIndex(offset, wheelItems.Count);
        }

        internal static int WheelIndex(Vector2 offset, int count)
        {
            if (count <= 0 || offset.sqrMagnitude < WheelInnerRadius * WheelInnerRadius || offset.sqrMagnitude > WheelRadius * WheelRadius) return -1;
            float angle = Mathf.Repeat(Mathf.Atan2(offset.x, offset.y), Mathf.PI * 2f);
            return Mathf.FloorToInt(angle * count / (Mathf.PI * 2f) + .5f) % count;
        }

        void CloseMedicineWheel(bool resume)
        {
            bool held = medicineHeld;
            medicineHeld = medicineLong = false;
            if (held) medicineControl.CancelPointer();
            if (!MedicineWheelOpen) return;
            medicineWheel.gameObject.SetActive(false);
            Destroy(medicineWheel.gameObject);
            medicineWheel = null;
            if (resume) session.SyncControls();
        }

        void ReleaseOtherFieldPointers()
        {
            lookControl.CancelPointer();
            foreach (var slot in touchSlots)
                if (slot.Live != medicineControl) slot.Live.CancelPointer();
        }

        internal void CancelFieldGestures()
        {
            CloseMedicineWheel(false);
            medicineControl.CancelPointer();
            ReleaseOtherFieldPointers();
        }
    }

    sealed class MedicineWheelRing : MaskableGraphic
    {
        internal int Count;
        int active = -1;
        internal int Active
        {
            set { if (active == value) return; active = value; SetVerticesDirty(); }
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (Count == 0) return;
            float span = 2f * Mathf.PI / Count;
            const int steps = 24;
            for (int i = 0; i < Count; i++)
            {
                var tint = i == active ? new Color32(125, 94, 46, 237) : new Color32(33, 61, 69, 199);
                float start = (i - .5f) * span + .022f;
                float step = (span - .044f) / steps;
                int first = mesh.currentVertCount;
                for (int j = 0; j <= steps; j++)
                {
                    float theta = start + j * step;
                    var axis = new Vector2(Mathf.Sin(theta), Mathf.Cos(theta));
                    mesh.AddVert(axis * GameUI.WheelInnerRadius, tint, Vector2.zero);
                    mesh.AddVert(axis * GameUI.WheelRadius, tint, Vector2.zero);
                    if (j == 0) continue;
                    int at = first + j * 2;
                    mesh.AddTriangle(at - 2, at - 1, at + 1);
                    mesh.AddTriangle(at - 2, at + 1, at);
                }
            }
        }
    }
}
