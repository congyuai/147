using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Dustline
{
    public sealed partial class GameUI
    {
        const string TouchLayoutKey = "Dustline.TouchLayout.v1";
        readonly List<TouchSlot> touchSlots = new List<TouchSlot>();
        RectTransform layoutField;
        TouchSlot layoutSelection;
        Slider buttonSize, buttonOpacity;
        Text layoutDetail;

        sealed class TouchSlot
        {
            internal string Id, Caption;
            internal RaidIcon Icon;
            internal Vector2 Initial;
            internal float Diameter;
            internal bool Primary;
            internal LayoutEntry Value, Draft;
            internal TouchControl Live, Preview;
        }

        [Serializable]
        sealed class LayoutSettings { public LayoutEntry[] Entries; }

        [Serializable]
        struct LayoutEntry
        {
            public string Id;
            public Vector2 Position;
            public float Scale, Opacity;
        }

        void AddTouchSlot(string action, string caption, RaidIcon icon, float x, float y, float size, bool primary = false)
        {
            var slot = new TouchSlot { Id = action, Caption = caption, Icon = icon, Initial = new Vector2(x, y), Diameter = size, Primary = primary };
            slot.Value = DefaultLayout(slot);
            slot.Live = CreateTouchButton(slot, touch, false, out var glyph);
            touchSlots.Add(slot);
            switch (action)
            {
                case "Fire": fireGlyph = glyph; break;
                case "Aim": aimButton = slot.Live.gameObject; break;
                case "Reload": reloadButton = slot.Live.gameObject; break;
                case "Interact": interactGlyph = glyph; interactButton = slot.Live.gameObject; break;
                case "SwitchWeapon": switchButton = slot.Live.gameObject; break;
                case "Heal": BindMedicine(slot.Live, glyph); break;
            }
        }

        TouchControl CreateTouchButton(TouchSlot slot, Transform parent, bool editing, out Image glyph)
        {
            var root = Anchor(slot.Id, parent, slot.Initial.x, slot.Initial.y);
            var face = Fill(root, slot.Primary ? new Color32(168, 111, 48, 214) : new Color32(39, 63, 70, 204), true);
            face.sprite = icons[RaidIcon.Dial];
            var mark = Stretch("Control symbol", root);
            mark.anchorMin = Vector2.one * 0.15f;
            mark.anchorMax = Vector2.one * 0.85f;
            glyph = Fill(mark, Pale);
            glyph.sprite = icons[slot.Icon];
            root.gameObject.AddComponent<CanvasGroup>();
            var control = AttachControl(root, slot.Id);
            control.SetFace(face, slot.Primary ? Amber : Aqua);
            if (slot.Id == "Move")
            {
                mark.anchorMin = mark.anchorMax = Vector2.one * 0.5f;
                mark.sizeDelta = Vector2.one * 63f;
                control.Knob = mark;
                if (!editing)
                {
                    var ring = Stretch("Sprint ring", root);
                    sprintRing = Fill(ring, Amber);
                    sprintRing.sprite = icons[RaidIcon.ControlRing];
                    sprintRing.gameObject.SetActive(false);
                    mark.SetAsLastSibling();
                }
            }
            if (editing)
            {
                control.SetEditing(true);
                control.SelectForLayout = _ => SelectLayoutSlot(slot);
                control.DragForLayout = (_, data, previous) => MoveLayoutSlot(slot, data, previous);
            }
            return control;
        }

        static LayoutEntry DefaultLayout(TouchSlot slot) => new LayoutEntry { Id = slot.Id, Position = slot.Initial, Scale = 1f, Opacity = 1f };
        static bool Finite(float n) => !float.IsNaN(n) && !float.IsInfinity(n);

        void LoadTouchLayout()
        {
            string json = PlayerPrefs.GetString(TouchLayoutKey, "");
            if (json.Length == 0 || json.Length > 12000) return;
            try
            {
                var state = JsonUtility.FromJson<LayoutSettings>(json);
                if (state?.Entries == null) return;
                foreach (var entry in state.Entries)
                {
                    if (!Finite(entry.Position.x) || !Finite(entry.Position.y) || !Finite(entry.Scale) || !Finite(entry.Opacity)) continue;
                    var slot = touchSlots.Find(candidate => candidate.Id == entry.Id);
                    if (slot == null) continue;
                    slot.Value = entry;
                    slot.Value.Scale = Mathf.Clamp(entry.Scale, 0.7f, 1.55f);
                    slot.Value.Opacity = Mathf.Clamp(entry.Opacity, 0.25f, 1f);
                    slot.Value.Position = new Vector2(Mathf.Clamp01(entry.Position.x), Mathf.Clamp01(entry.Position.y));
                }
            }
            catch (ArgumentException) { }
        }

        static LayoutEntry ApplySlot(TouchSlot slot, LayoutEntry value, TouchControl control, RectTransform space, float factor)
        {
            var rect = (RectTransform)control.transform;
            float size = slot.Diameter * value.Scale * factor;
            float rx = Mathf.Clamp((size * 0.5f + 3f * factor) / Mathf.Max(1f, space.rect.width), 0f, 0.5f);
            float ry = Mathf.Clamp((size * 0.5f + 3f * factor) / Mathf.Max(1f, space.rect.height), 0f, 0.5f);
            value.Position = new Vector2(Mathf.Clamp(value.Position.x, rx, 1f - rx), Mathf.Clamp(value.Position.y, ry, 1f - ry));
            rect.anchorMin = rect.anchorMax = value.Position;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.one * size;
            control.GetComponent<CanvasGroup>().alpha = value.Opacity;
            if (control.Knob != null) control.Knob.sizeDelta = Vector2.one * (size * 0.35f);
            return value;
        }

        void ApplyTouchLayout()
        {
            foreach (var slot in touchSlots) ApplySlot(slot, slot.Value, slot.Live, touch, 1f);
        }

        void ShowTouchLayout(bool field)
        {
            var panel = Display(ScreenPage.TouchLayout, "Touch layout");
            Header(panel, "自定义键位", "拖动图标到合适位置，再调整大小和透明度");
            float ratio = safe.rect.width / Mathf.Max(1f, safe.rect.height);
            float width = Mathf.Min(860f, 442f * Mathf.Clamp(ratio, 1.2f, 3.4f));
            float height = Mathf.Min(442f, width / Mathf.Clamp(ratio, 1.2f, 3.4f));
            layoutField = At("Touch preview", panel, 26 + (860f - width) * 0.5f, 131 + (449f - height) * 0.5f, width, height);
            Fill(layoutField, new Color32(28, 44, 48, 255));
            Label(layoutField, "预览 · 按下图标不会触发操作", 17, 10, 10, width - 20, 28, TextAnchor.UpperCenter, Muted);
            foreach (var slot in touchSlots)
            {
                slot.Draft = slot.Value;
                slot.Preview = CreateTouchButton(slot, layoutField, true, out _);
            }
            var options = At("Control adjustments", panel, 904, 130, 308, 448);
            Fill(options, Ink);
            layoutDetail = Label(options, "", 24, 17, 19, 270, 67, color: Amber);
            buttonSize = SettingsSlider(options, "大小", 17, 98, 274, 70, 155, 100, _ => ChangeLayoutSlot());
            buttonOpacity = SettingsSlider(options, "透明度", 17, 195, 274, 25, 100, 100, _ => ChangeLayoutSlot());
            Button(options, "恢复默认", 17, 332, 274, 46, () =>
            {
                foreach (var slot in touchSlots) slot.Draft = DefaultLayout(slot);
                SelectLayoutSlot(layoutSelection);
            });
            SelectLayoutSlot(touchSlots[0]);
            Button(panel, "取消", 26, 615, 132, 49, () => ShowSettings(field, true));
            Button(panel, "保存并返回", 1014, 615, 198, 49, () =>
            {
                var values = new LayoutEntry[touchSlots.Count];
                for (int i = 0; i < values.Length; i++) values[i] = touchSlots[i].Value = touchSlots[i].Draft;
                PlayerPrefs.SetString(TouchLayoutKey, JsonUtility.ToJson(new LayoutSettings { Entries = values }));
                PlayerPrefs.Save();
                ApplyTouchLayout();
                ShowSettings(field, true);
            }, true);
        }

        void SelectLayoutSlot(TouchSlot slot)
        {
            if (slot == null) return;
            layoutSelection = slot;
            buttonSize.SetValueWithoutNotify(Mathf.RoundToInt(slot.Draft.Scale * 100f));
            buttonOpacity.SetValueWithoutNotify(Mathf.RoundToInt(slot.Draft.Opacity * 100f));
            UpdateLayoutPreview();
        }

        void MoveLayoutSlot(TouchSlot slot, PointerEventData data, Vector2 previous)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(layoutField, data.position, data.pressEventCamera, out var point)
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(layoutField, previous, data.pressEventCamera, out var before)) return;
            slot.Draft.Position += new Vector2((point.x - before.x) / layoutField.rect.width, (point.y - before.y) / layoutField.rect.height);
            UpdateLayoutPreview();
        }

        void ChangeLayoutSlot()
        {
            if (layoutSelection == null) return;
            layoutSelection.Draft.Scale = buttonSize.value * 0.01f;
            layoutSelection.Draft.Opacity = buttonOpacity.value * 0.01f;
            UpdateLayoutPreview();
        }

        void UpdateLayoutPreview()
        {
            float factor = layoutField.rect.width / Mathf.Max(1f, safe.rect.width);
            foreach (var slot in touchSlots)
            {
                slot.Draft = ApplySlot(slot, slot.Draft, slot.Preview, layoutField, factor);
                slot.Preview.GetComponent<Image>().color = slot == layoutSelection ? new Color32(200, 142, 64, 255) : Plate;
            }
            layoutSelection.Preview.transform.SetAsLastSibling();
            layoutDetail.text = layoutSelection.Caption + "\n" + Mathf.RoundToInt(layoutSelection.Draft.Scale * 100f) + "%  /  " + Mathf.RoundToInt(layoutSelection.Draft.Opacity * 100f) + "%";
        }
    }
}
