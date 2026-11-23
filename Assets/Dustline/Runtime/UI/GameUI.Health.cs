using System;
using UnityEngine;
using UnityEngine.UI;

namespace Dustline
{
    public sealed partial class GameUI
    {
        Action healthRefresh;
        float healthRefreshAt;

        internal void ToggleHealth()
        {
            if (!session.InField || MedicineWheelOpen || current == ScreenPage.ExitConfirm) return;
            if (current == ScreenPage.Health) EnterField();
            else if (current == ScreenPage.Field || current == ScreenPage.FieldSettings || current == ScreenPage.FieldInventory || current == ScreenPage.PracticePanel)
                ShowHealth();
        }

        void ShowHealth(BodyPart? first = null, string preferred = null)
        {
            var body = session.Player?.Health.Body;
            if (!session.InField || body == null || !body.Alive) return;
            var panel = Display(ScreenPage.Health, "Body condition");
            Header(panel, "分部位健康", session.Phase == SessionPhase.Practice ? "离线靶场 / 练习物资与正式装备独立" : "突袭仍在继续 / 选择部位与对应医疗品");
            var map = At("Condition map", panel, 26, 131, 451, 460);
            var care = At("Treatment options", panel, 489, 131, 723, 460);
            Fill(map, Ink); Fill(care, Ink);
            BodyPart selected = first ?? body.Suggest(TreatmentKind.Heal) ?? BodyPart.Chest;
            string med = preferred;
            var options = new System.Collections.Generic.List<(ItemDefinition Item, Button Action, Text Caption, Text Detail)>();
            var states = new Text[BodyCondition.Count];
            var bars = new Image[BodyCondition.Count];
            var buttons = new Button[BodyCondition.Count];
            var choices = At("Available treatments", care, 0, 0, 723, 460);
            var footer = Label(panel, "", 16, 26, 617, 966, 41, TextAnchor.MiddleLeft, Muted);
            for (int i = 0; i < buttons.Length; i++)
            {
                var part = (BodyPart)i;
                float row = 8 + i * 62;
                buttons[i] = Button(map, "", 12, row, 427, 55, () =>
                {
                    selected = part;
                    RefreshOptions(); RefreshRows();
                });
                Label(buttons[i].transform, BodyCondition.Name(part), 19, 11, 8, 79, 37, TextAnchor.MiddleLeft);
                states[i] = Label(buttons[i].transform, "", 15, 91, 7, 321, 31, TextAnchor.MiddleLeft);
                bars[i] = Bar(buttons[i].transform, 91, 41, 304, 4, Aqua);
            }
            Button(panel, "返回行动", 1014, 615, 198, 49, EnterField, true);
            RefreshOptions(); RefreshRows();
            healthRefresh = RefreshRows;

            void RefreshRows()
            {
                for (int i = 0; i < buttons.Length; i++)
                {
                    var part = (BodyPart)i;
                    var flags = body.Injuries(part);
                    float hp = body.Current(part), max = body.Maximum(part);
                    buttons[i].targetGraphic.color = part == selected ? new Color32(48, 80, 80, 255) : Plate;
                    bars[i].color = hp <= .001f ? MapDanger : flags != Injury.None ? Amber : Aqua;
                    SetBar(bars[i], hp / max);
                    states[i].text = hp.ToString("0.#") + " / " + max.ToString("0.#") + "   " + WoundName(flags, body.PainRelieved);
                }
                foreach (var option in options)
                {
                    bool eligible = body.CanTreat(selected, option.Item.Treatment);
                    option.Action.interactable = eligible;
                    option.Caption.text = eligible ? "开始" : "不适用";
                    option.Detail.color = eligible ? Aqua : Muted;
                }
                footer.text = body.PainRelieved ? "疼痛已缓解 · " + Mathf.CeilToInt(body.PainRemaining) + " 秒" : "头或胸归零即失败。其他部位的溢出按基础生命比例一次分给仍有生命的部位。";
            }
            void RefreshOptions()
            {
                Clear(choices);
                options.Clear();
                Label(choices, "治疗目标 / " + BodyCondition.Name(selected), 23, 13, 7, 697, 36, color: Amber);
                int i = 0;
                var bag = session.ActiveFieldBag;
                foreach (var def in session.Catalog.Items)
                {
                    if (def.Kind != ItemKind.Medicine) continue;
                    var stack = bag.Find(med);
                    if (stack == null || stack.ItemId != def.Id) stack = session.QuickMedicine(def.Id);
                    if (stack == null) continue;
                    string id = stack.InstanceId;
                    bool eligible = body.CanTreat(selected, def.Treatment);
                    int charges = bag.RemainingUses(def.Id);
                    var row = At("Treatment / " + def.Id, choices, 13, 54 + i++ * 51, 697, 46);
                    Fill(row, id == med ? new Color32(36, 68, 72, 255) : Plate);
                    DrawItemIcon(row, def, stack, 5, 1, 51, 44);
                    Label(row, def.DisplayName, 17, 63, 5, 211, 35, TextAnchor.MiddleLeft);
                    var details = Label(row, TreatmentName(def.Treatment) + "  " + def.UseSeconds.ToString("0.#") + " 秒 / " + charges + " 次", 15, 280, 5, 283, 35,
                        TextAnchor.MiddleLeft, eligible ? Aqua : Muted);
                    var action = Button(row, eligible ? "开始" : "不适用", 576, 4, 109, 38, () =>
                    {
                        if (!body.CanTreat(selected, def.Treatment)) { Notify("当前部位不适用此医疗品。"); return; }
                        EnterField();
                        if (session.Input.Gameplay) session.Input.TapMedicine(id, selected);
                    });
                    options.Add((def, action, action.GetComponentInChildren<Text>(), details));
                }
                if (i == 0) Label(choices, "随身栏没有医疗品。", 18, 13, 65, 420, 55, color: Muted);
            }
        }

        static string WoundName(Injury flags, bool relief)
        {
            if (flags == Injury.None) return "正常";
            string text = "";
            if ((flags & Injury.Destroyed) != 0) text += "损毁 ";
            if ((flags & Injury.HeavyBleed) != 0) text += "重度出血 ";
            else if ((flags & Injury.LightBleed) != 0) text += "轻度出血 ";
            if ((flags & Injury.Fracture) != 0) text += "骨折 ";
            if ((flags & Injury.Pain) != 0) text += relief ? "疼痛缓解" : "疼痛";
            return text;
        }

        static string TreatmentName(TreatmentKind type) => type switch
        {
            TreatmentKind.Heal => "恢复生命", TreatmentKind.LightBleed => "处理轻度出血", TreatmentKind.HeavyBleed => "处理重度出血",
            TreatmentKind.Fracture => "处理骨折", TreatmentKind.Pain => "缓解疼痛", TreatmentKind.Surgery => "恢复可治疗状态", _ => "治疗"
        };
    }
}
