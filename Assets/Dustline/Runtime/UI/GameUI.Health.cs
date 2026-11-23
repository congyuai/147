using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Dustline
{
    public sealed partial class GameUI
    {
        static readonly Injury[] DisplayedInjuries = { Injury.Destroyed, Injury.HeavyBleed, Injury.LightBleed, Injury.Fracture, Injury.Pain };
        static readonly Color ConditionSelection = new Color32(46, 82, 81, 255);
        Action healthRefresh;
        float healthRefreshAt;

        internal void ToggleHealth()
        {
            if (!session.InField || MedicineWheelOpen || current == ScreenPage.ExitConfirm) return;
            if (current == ScreenPage.Health) BackOrSettings();
            else if (current == ScreenPage.Field || current == ScreenPage.FieldSettings || current == ScreenPage.FieldInventory || current == ScreenPage.PracticePanel)
                ShowHealth();
        }

        void ShowHealth(BodyPart? first = null, string preferred = null, Injury firstInjury = Injury.None)
        {
            var body = session.Player?.Health.Body;
            if (!session.InField || body == null || !body.Alive) return;
            var panel = Display(ScreenPage.Health, "Body condition");
            Header(panel, "分部位健康", session.Phase == SessionPhase.Practice ? "离线靶场 / 练习物资与正式装备独立" : "突袭仍在继续 / 选择部位与对应医疗品");
            var map = At("Condition map", panel, 26, 122, 1186, 390);
            var care = At("Treatment options", panel, 26, 523, 1186, 123);
            Fill(map, Ink); Fill(care, Ink);
            var total = Label(map, "", 16, 14, 5, 218, 31, TextAnchor.MiddleLeft, Muted);
            total.name = "Total health";
            BodyPart selected = first ?? body.Suggest(TreatmentKind.Heal) ?? BodyPart.Chest;
            Injury selectedInjury = firstInjury;
            SetPageRestore(() => ShowHealth(selected, preferred, selectedInjury));

            const float figureX = 513, figureY = 18, figureW = 162, figureH = 354;
            var regionButtons = new Button[BodyCondition.Count];
            var regionValues = new Text[BodyCondition.Count];
            var injuryButtons = new Button[BodyCondition.Count, DisplayedInjuries.Length];
            var figure = CreateBodyFigure(map, "Body detail diagram", figureX, figureY, figureW, figureH);
            var target = Label(care, "", 21, 13, 4, 1160, 28, color: Amber);
            target.name = "Treatment target";
            var choices = At("Available treatments", care, 13, 35, 1160, 83);
            var empty = Label(choices, "", 17, 0, 10, 490, 40, color: Muted);
            var options = new List<(ItemDefinition Item, RectTransform Row, Button Action, Text Caption, Text Detail)>();
            var footer = Label(panel, "", 14, 26, 649, 1186, 24, TextAnchor.MiddleLeft, Muted);

            Region(BodyPart.Head, 27, true);
            Region(BodyPart.Chest, 101, false);
            Region(BodyPart.LeftArm, 119, true);
            Region(BodyPart.RightArm, 175, false);
            Region(BodyPart.Abdomen, 209, true);
            Region(BodyPart.LeftLeg, 293, true);
            Region(BodyPart.RightLeg, 293, false);
            figure.transform.SetAsLastSibling();
            figure.Initialize(part => SelectRegion(part));

            foreach (var def in session.Catalog.Items)
            {
                if (def.Kind != ItemKind.Medicine) continue;
                var row = At("Treatment / " + def.Id, choices, 0, 0, 158, 83);
                var action = Button(row, "", 0, 0, 158, 83, () => StartTreatment(def));
                action.name = "开始";
                DrawItemIcon(row, def, null, 4, 4, 36, 46);
                Label(row, def.DisplayName, 15, 44, 0, 109, 21, TextAnchor.MiddleLeft);
                var detail = Label(row, "", 12, 44, 20, 109, 31, TextAnchor.MiddleLeft);
                Fill(At("Treatment divider", row, 5, 52, 148, 1), Border);
                var caption = Label(row, "", 15, 5, 54, 148, 25, TextAnchor.MiddleCenter);
                options.Add((def, row, action, caption, detail));
            }
            Refresh();
            healthRefresh = Refresh;

            void Region(BodyPart part, float row, bool left)
            {
                int index = (int)part;
                float x = left ? 308 : 700;
                var end = new Vector2(left ? x + 178 : x, row + 17);
                var bend = new Vector2(left ? figureX - 11 : figureX + figureW + 11, end.y);
                Stroke(map, "Body leader / " + part, end, bend, 1f, Border);
                Stroke(map, "Body leader / " + part, bend, BodyFigure.Pin(part, figureX, figureY, figureW, figureH), 1f, Border);
                var button = Button(map, "", x, row, 178, 66, () => SelectRegion(part));
                button.name = "Region / " + part;
                regionButtons[index] = button;
                Label(button.transform, BodyCondition.Name(part), 18, 8, 2, 72, 28, TextAnchor.MiddleLeft);
                var value = Label(button.transform, "", 16, 79, 2, 89, 28, TextAnchor.MiddleRight);
                value.name = "Region health / " + part;
                regionValues[index] = value;
                for (int j = 0; j < DisplayedInjuries.Length; j++)
                {
                    var injury = DisplayedInjuries[j];
                    injuryButtons[index, j] = InjuryButton(button.transform, injury, 8, 32, 28, 29, () => SelectRegion(part, injury));
                }
            }

            void SelectRegion(BodyPart part, Injury injury = Injury.None)
            {
                if (current != ScreenPage.Health || (injury != Injury.None && (VisibleInjuries(body, part) & injury) == 0)) return;
                selected = part;
                selectedInjury = injury;
                Refresh();
            }

            ItemStack MedicineStack(ItemDefinition item)
            {
                var stack = session.ActiveFieldBag?.Find(preferred);
                return stack != null && stack.ItemId == item.Id && stack.Quantity > 0 && stack.Slot == EquipmentSlot.None
                    ? stack : session.QuickMedicine(item.Id);
            }

            void StartTreatment(ItemDefinition item)
            {
                if (current != ScreenPage.Health || !Eligible(item)) return;
                var stack = MedicineStack(item);
                if (stack == null) { Refresh(); return; }
                EnterField();
                if (session.Input.Gameplay) session.Input.TapMedicine(stack.InstanceId, selected);
            }

            bool MatchesInjury(ItemDefinition item) => selectedInjury == Injury.None || item.Treatment == TreatmentForInjury(selectedInjury);
            bool Eligible(ItemDefinition item) => MatchesInjury(item) && body.CanTreat(selected, item.Treatment);

            void Refresh()
            {
                if ((VisibleInjuries(body, selected) & selectedInjury) == 0) selectedInjury = Injury.None;
                figure.SetCondition(body, selected);
                total.text = "总健康  " + VitalsValue(body.Total, body.TotalMaximum);
                for (int i = 0; i < BodyCondition.Count; i++)
                {
                    var part = (BodyPart)i;
                    var flags = VisibleInjuries(body, part);
                    regionValues[i].text = VitalsValue(body.Current(part), body.Maximum(part));
                    regionButtons[i].targetGraphic.color = part == selected ? ConditionSelection : Plate;
                    int column = 0;
                    for (int j = 0; j < DisplayedInjuries.Length; j++)
                    {
                        var injury = DisplayedInjuries[j];
                        var badge = injuryButtons[i, j];
                        bool visible = (flags & injury) != 0;
                        SetActive(badge.gameObject, visible);
                        if (!visible) continue;
                        ((RectTransform)badge.transform).anchoredPosition = new Vector2(8 + 32 * column++, -32);
                        badge.targetGraphic.color = part == selected && injury == selectedInjury ? ConditionSelection : Ink;
                    }
                }

                target.text = "治疗目标 / " + BodyCondition.Name(selected) + (selectedInjury == Injury.None ? "" : " · " + InjuryName(selectedInjury));
                int visibleOptions = 0;
                foreach (var option in options)
                {
                    var item = option.Item;
                    var stack = MatchesInjury(item) ? MedicineStack(item) : null;
                    SetActive(option.Row.gameObject, stack != null);
                    if (stack == null) continue;
                    option.Row.anchoredPosition = new Vector2(166 * visibleOptions++, 0);
                    bool eligible = Eligible(item);
                    option.Action.targetGraphic.color = stack.InstanceId == preferred ? ConditionSelection : Plate;
                    option.Action.interactable = eligible;
                    option.Caption.text = eligible ? "开始" : "不适用";
                    option.Caption.color = eligible ? Aqua : Muted;
                    option.Detail.color = eligible ? Aqua : Muted;
                    option.Detail.text = TreatmentName(item.Treatment) + "\n" + item.UseSeconds.ToString("0.#") + " 秒 / " + session.ActiveFieldBag.RemainingUses(item.Id) + " 次";
                }
                SetActive(empty.gameObject, visibleOptions == 0);
                empty.text = selectedInjury == Injury.None ? "随身栏没有医疗品。" : "随身栏没有适用于此伤情的医疗品。";
                footer.text = body.PainRelieved ? "疼痛已缓解 · " + Mathf.CeilToInt(body.PainRemaining) + " 秒" : "选择部位或伤情图标，再选择随身栏的医疗品。";
            }
        }

        BodyFigure CreateBodyFigure(Transform parent, string name, float x, float y, float w, float h)
        {
            var figure = At(name, parent, x, y, w, h).gameObject;
            figure.AddComponent<CanvasRenderer>();
            return figure.AddComponent<BodyFigure>();
        }

        Button InjuryButton(Transform parent, Injury injury, float x, float y, float width, float height, Action click)
        {
            var button = Button(parent, "", x, y, width, height, click, false, Ink);
            button.name = "Injury / " + injury;
            var rect = Anchor("Injury glyph", button.transform, .5f, .5f);
            rect.sizeDelta = Vector2.one * (height - 6f);
            rect.gameObject.AddComponent<CanvasRenderer>();
            var glyph = rect.gameObject.AddComponent<InjuryGlyph>();
            glyph.Initialize(injury, injury switch
            {
                Injury.Destroyed => Pale, Injury.HeavyBleed => new Color32(248, 106, 100, 255), Injury.LightBleed => new Color32(245, 151, 124, 255),
                Injury.Fracture => Amber, _ => new Color32(193, 174, 232, 255)
            });
            return button;
        }

        static Injury VisibleInjuries(BodyCondition body, BodyPart part) => body == null ? Injury.None
            : body.PainRelieved ? body.Injuries(part) & ~Injury.Pain : body.Injuries(part);

        static string InjuryName(Injury injury) => injury switch
        {
            Injury.Destroyed => "损毁", Injury.HeavyBleed => "重度出血", Injury.LightBleed => "轻度出血", Injury.Fracture => "骨折", Injury.Pain => "疼痛", _ => ""
        };

        static TreatmentKind TreatmentForInjury(Injury injury) => injury switch
        {
            Injury.Destroyed => TreatmentKind.Surgery, Injury.HeavyBleed => TreatmentKind.HeavyBleed,
            Injury.LightBleed => TreatmentKind.LightBleed, Injury.Fracture => TreatmentKind.Fracture, Injury.Pain => TreatmentKind.Pain, _ => TreatmentKind.Heal
        };

        static string TreatmentName(TreatmentKind type) => type switch
        {
            TreatmentKind.Heal => "恢复生命", TreatmentKind.LightBleed => "处理轻度出血", TreatmentKind.HeavyBleed => "处理重度出血",
            TreatmentKind.Fracture => "处理骨折", TreatmentKind.Pain => "缓解疼痛", TreatmentKind.Surgery => "恢复可治疗状态", _ => "治疗"
        };
    }

    internal sealed class InjuryGlyph : MaskableGraphic
    {
        Injury kind;

        internal void Initialize(Injury injury, Color tint)
        {
            kind = injury;
            color = tint;
            raycastTarget = false;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            float size = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height);
            Vector2 center = rectTransform.rect.center;
            switch (kind)
            {
                case Injury.LightBleed:
                    Drop(0f, 0f, 1f);
                    break;
                case Injury.HeavyBleed:
                    Drop(-.16f, .08f, .73f);
                    Drop(.20f, -.15f, .66f);
                    break;
                case Injury.Fracture:
                    Line(-.34f, -.29f, -.12f, -.08f); Line(-.12f, -.08f, -.20f, .07f);
                    Line(.34f, .29f, .12f, .08f); Line(.12f, .08f, .20f, -.07f);
                    Line(-.41f, -.21f, -.26f, -.36f); Line(.41f, .21f, .26f, .36f);
                    break;
                case Injury.Pain:
                    Face(new Vector2(.08f, .43f), new Vector2(-.30f, -.02f), new Vector2(.02f, -.02f), new Vector2(.21f, .43f));
                    Face(new Vector2(.30f, .11f), new Vector2(.03f, .11f), new Vector2(-.14f, -.43f));
                    break;
                case Injury.Destroyed:
                    Line(0f, .43f, .39f, 0f); Line(.39f, 0f, 0f, -.43f);
                    Line(0f, -.43f, -.39f, 0f); Line(-.39f, 0f, 0f, .43f);
                    Line(-.13f, -.13f, .13f, .13f); Line(-.13f, .13f, .13f, -.13f);
                    break;
            }

            void Drop(float x, float y, float scale)
            {
                var at = new Vector2(x, y);
                Face(at + Vector2.up * (.43f * scale), at + new Vector2(.26f, -.05f) * scale, at + new Vector2(.23f, -.24f) * scale,
                    at + new Vector2(0f, -.37f) * scale, at + new Vector2(-.23f, -.24f) * scale, at + new Vector2(-.26f, -.05f) * scale);
            }

            void Line(float x, float y, float toX, float toY)
            {
                var from = new Vector2(x, y);
                var to = new Vector2(toX, toY);
                var side = Vector2.Perpendicular(to - from).normalized * .053f;
                Face(from + side, to + side, to - side, from - side);
            }

            void Face(params Vector2[] points)
            {
                int first = mesh.currentVertCount;
                foreach (var point in points) mesh.AddVert(center + size * point, color, Vector2.zero);
                for (int i = 1; i < points.Length - 1; i++) mesh.AddTriangle(first, first + i, first + i + 1);
            }
        }
    }
}