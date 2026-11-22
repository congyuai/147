using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Dustline
{
    public sealed partial class GameUI
    {
        readonly struct RifleChoice
        {
            internal readonly GridInventory Bag;
            internal readonly string Id;
            internal RifleChoice(GridInventory bag, string id) { Bag = bag; Id = id; }
        }

        void ShowGunsmith(GridInventory selectedBag = null, string selectedId = null)
        {
            if (session.Phase != SessionPhase.Menu) return;
            var panel = Display(ScreenPage.Gunsmith, "Gunsmith");
            Header(panel, "枪械改装", "整备 · 配件跟随这件武器，撤离后可继续调整");
            Button(panel, "前往仓库", 26, 615, 143, 49, ShowStash);
            Button(panel, "返回大厅", 1014, 615, 198, 49, ShowMenu, true);
            var rifles = new List<RifleChoice>();
            AddRifles(session.Loadout);
            AddRifles(session.Stash);
            if (rifles.Count == 0)
            {
                Label(panel, "还没有可改装的枪械", 28, 26, 170, 1020, 47);
                Label(panel, "可在仓库购买或在港区搜到 AR-20。", 20, 26, 227, 1030, 64, color: Muted);
                return;
            }
            int index = rifles.FindIndex(choice => choice.Bag == selectedBag && choice.Id == selectedId);
            if (index < 0) index = 0;
            var left = At("Rifle selection", panel, 26, 132, 273, 465);
            var parts = At("Part choices", panel, 312, 132, 470, 465);
            var stats = At("Weapon statistics", panel, 795, 132, 417, 465);
            Fill(left, Ink); Fill(parts, Ink); Fill(stats, Ink);
            Fill(At("Part accent", parts, 0, 0, 470, 2), Amber);
            Fill(At("Stats accent", stats, 0, 0, 417, 2), Aqua);
            var selection = At("Rifle data", left, 0, 0, 273, 355);
            var options = At("Upgrades", parts, 0, 0, 470, 465);
            var values = At("Current attributes", stats, 0, 0, 417, 465);
            var pageText = Label(left, "", 17, 17, 354, 239, 29, TextAnchor.MiddleCenter, Muted);
            Button(left, "上一件", 17, 397, 113, 47, () => SelectRifle(-1)).interactable = rifles.Count > 1;
            Button(left, "下一件", 141, 397, 113, 47, () => SelectRifle(1)).interactable = rifles.Count > 1;
            Refresh();

            void AddRifles(GridInventory bag)
            {
                foreach (var item in bag.Items)
                    if (item.ItemId == WeaponDefinition.RifleId && WeaponTuning.SupportsUpgrades(session.Catalog.Find(item.ItemId).Weapon))
                        rifles.Add(new RifleChoice(bag, item.InstanceId));
            }

            void SelectRifle(int offset)
            {
                index = (index + offset + rifles.Count) % rifles.Count;
                Refresh();
            }

            void Refresh()
            {
                var choice = rifles[index];
                var item = choice.Bag.Find(choice.Id);
                if (item == null) { ShowGunsmith(); return; }
                var definition = session.Catalog.Find(item.ItemId);
                Clear(selection); Clear(options); Clear(values);
                if (creditsReadout != null) creditsReadout.text = session.Profile.Credits + " 信用点";
                pageText.text = (index + 1) + " / " + rifles.Count;
                Label(selection, definition.DisplayName, 24, 17, 21, 239, 58, color: Amber);
                Label(selection, choice.Bag == session.Loadout ? (item.Slot == EquipmentSlot.Weapon ? "整备背包 / 已装备" : "整备背包") : "仓库储备", 17, 17, 88, 239, 38, color: Aqua);
                Label(selection, "弹匣容量  " + definition.Weapon.MagazineSize + "\n伤害  " + definition.Weapon.Damage.ToString("0.#") + "\n射速  " + Mathf.RoundToInt(60f / definition.Weapon.FireInterval) + " 发/分", 18, 17, 152, 239, 111);
                DrawItemSymbol(selection, ItemSymbol(definition), 170, 103, 75, 66, Aqua);
                Label(selection, "逐级解锁；切换已解锁配件不收费。", 16, 17, 277, 239, 78, color: Muted);
                Part(WeaponModSlot.Sight, "瞄具", "机瞄", "反射瞄具", "紧凑瞄具", "提高开镜精度；较高等级开镜更慢。", 15);
                Part(WeaponModSlot.Grip, "握把", "原装", "防滑握把", "稳定握把", "减少腰射散布与后坐；换弹略慢。", 162);
                Part(WeaponModSlot.Stock, "枪托", "原装", "轻量枪托", "平衡枪托", "减少后坐、加快换弹；腰射散布略大。", 309);
                var standard = WeaponTuning.Evaluate(definition.Weapon, default);
                var actual = WeaponTuning.Evaluate(definition.Weapon, item.Mods);
                Label(values, "当前属性 / 相对原装", 23, 18, 16, 378, 47);
                Stat("有效距离", "米", actual.Range, standard.Range, false, 75);
                Stat("换弹时间", "秒", actual.ReloadSeconds, standard.ReloadSeconds, true, 125);
                Stat("后坐幅度", "", actual.Recoil, standard.Recoil, true, 175);
                Stat("腰射散布", "‰", actual.HipSpread * 1000f, standard.HipSpread * 1000f, true, 225);
                Stat("开镜散布", "‰", actual.AimSpread * 1000f, standard.AimSpread * 1000f, true, 275);
                Stat("开镜耗时", "秒", actual.AimSeconds, standard.AimSeconds, true, 325);
                Stat("开镜视角", "°", actual.AimFov, standard.AimFov, null, 375);

                void Part(WeaponModSlot slot, string title, string first, string second, string third, string hint, int y)
                {
                    Label(options, title, 21, 17, y, 429, 31);
                    var labels = new[] { first, second, third };
                    int installed = WeaponTuning.Level(item.Mods, slot);
                    for (int i = 0; i < labels.Length; i++)
                    {
                        int level = i;
                        bool allowed = WeaponTuning.TrySelect(item.Mods, slot, level, out _, out int cost);
                        string state = level == installed ? "已使用" : !allowed ? "待解锁" : cost == 0 ? "装备" : "购买 " + cost;
                        var button = Button(options, labels[i] + "\n" + state, 17 + i * 146, y + 36, 141, 55, () =>
                        {
                            if (session.SelectWeaponPart(choice.Bag, choice.Id, slot, level)) Refresh();
                        }, level == installed);
                        button.interactable = allowed && level != installed;
                    }
                    Label(options, hint, 16, 17, y + 99, 429, 33, color: Muted);
                    if (y + 135 < 455) Fill(At("Part divider", options, 17, y + 135, 434, 1), Border);
                }

                void Stat(string title, string unit, float value, float baseline, bool? lower, int y)
                {
                    float diff = value - baseline;
                    Label(values, title, 17, 18, y, 153, 40, color: Muted);
                    Label(values, value.ToString("0.##") + " " + unit, 19, 166, y, 123, 40, TextAnchor.UpperRight);
                    if (Mathf.Abs(diff) < 0.001f) return;
                    Color tint = lower.HasValue ? ((diff < 0f) == lower.Value ? Aqua : MapDanger) : Pale;
                    Label(values, diff.ToString("+0.##;-0.##"), 18, 297, y, 97, 40, TextAnchor.UpperRight, tint);
                }
            }
        }
    }
}
