using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Dustline
{
    public sealed partial class GameUI
    {
        ItemPreviewStage gunsmithStage;
        RenderTexture gunsmithTexture;
        bool gunsmithDirty;

        readonly struct InventoryChoice
        {
            internal readonly GridInventory Bag;
            internal readonly string Id;
            internal InventoryChoice(GridInventory bag, string id) { Bag = bag; Id = id; }
            internal ItemStack Item => Bag?.Find(Id);
        }

        static string PartName(PartSlot slot) => slot switch
        {
            PartSlot.Sight => "瞄具", PartSlot.Grip => "握把", PartSlot.Stock => "枪托",
            PartSlot.Muzzle => "枪口件", PartSlot.Magazine => "弹匣", _ => "配件"
        };

        void ShowGunsmith(GridInventory selectedBag = null, string selectedId = null)
        {
            if (session.Phase != SessionPhase.Menu) return;
            var panel = Display(ScreenPage.Gunsmith, "Gunsmith");
            Header(panel, "枪械改装", "拖动枪模旋转 · 配件跟随本件武器 · 选件先预览、确认后安装");
            Button(panel, "返回仓库", 26, 615, 155, 49, ShowStash);
            Button(panel, "前往商店", 191, 615, 155, 49, () => ShowShop(2));
            Button(panel, "返回大厅", 1022, 615, 190, 49, ShowMenu, true);
            var guns = new List<InventoryChoice>();
            AddGuns(session.Loadout); AddGuns(session.Stash);
            if (guns.Count == 0)
            {
                Label(panel, "还没有可改装的枪械", 28, 26, 170, 1020, 47);
                Label(panel, "可在补给商店购买，或从港区带回。", 20, 26, 227, 1030, 64, color: Muted);
                return;
            }
            int index = guns.FindIndex(choice => choice.Bag == selectedBag && choice.Id == selectedId);
            if (index < 0) index = 0;
            PartSlot slot = PartSlot.Sight;
            var parts = new List<InventoryChoice>();
            int option = -1, optionPage = 0;
            string previewGun = null;
            bool showStock = false;
            int legacyLevel = -1;
            var left = At("Weapon preview", panel, 26, 132, 478, 462);
            var choices = At("Fittings", panel, 516, 132, 354, 462);
            var stats = At("Weapon comparisons", panel, 882, 132, 330, 462);
            Fill(left, Ink); Fill(choices, Ink); Fill(stats, Ink);
            var gunLabel = Label(left, "", 22, 94, 10, 365, 51);
            var art = At("Rotatable weapon", left, 12, 69, 454, 298).gameObject.AddComponent<RawImage>();
            gunsmithStage = new ItemPreviewStage(transform, 1);
            gunsmithTexture = ItemIconCache.Texture(544, 352, 24);
            art.texture = gunsmithTexture;
            art.color = Color.clear;
            var rotate = art.gameObject.AddComponent<InventoryDrag>();
            int owner = int.MinValue;
            rotate.Begin = e => { if (owner == int.MinValue) owner = e.pointerId; };
            rotate.Move = e =>
            {
                if (owner != e.pointerId) return;
                gunsmithStage.Rotate(e.delta / Mathf.Max(.2f, uiCanvas.scaleFactor));
                gunsmithDirty = true;
            };
            rotate.End = e => { if (owner == e.pointerId) owner = int.MinValue; };
            Label(left, "拖动旋转枪模", 16, 16, 374, 446, 30, TextAnchor.MiddleCenter, Muted);
            var pageLabel = Label(left, "", 16, 141, 414, 195, 35, TextAnchor.MiddleCenter, Muted);
            var iconRoot = At("Build thumbnail", left, 12, 12, 75, 50);
            var compare = At("Comparison values", stats, 0, 0, 330, 462);
            Button(left, "上一把", 12, 413, 119, 38, () => ChangeGun(-1)).interactable = guns.Count > 1;
            Button(left, "下一把", 347, 413, 119, 38, () => ChangeGun(1)).interactable = guns.Count > 1;
            Refresh();

            void AddGuns(GridInventory bag)
            {
                foreach (var it in bag.Items)
                {
                    var weapon = session.Catalog.Find(it.ItemId)?.Weapon;
                    if (weapon != null && !weapon.Melee) guns.Add(new InventoryChoice(bag, it.InstanceId));
                }
            }
            void ChangeGun(int step)
            {
                index = (index + step + guns.Count) % guns.Count;
                slot = PartSlot.Sight;
                option = legacyLevel = -1;
                optionPage = 0;
                showStock = false;
                Refresh();
            }
            void ChooseSlot(PartSlot value)
            {
                slot = value;
                option = legacyLevel = -1;
                optionPage = 0;
                showStock = false;
                Refresh();
            }
            void Refresh()
            {
                var choice = guns[index];
                var item = choice.Item;
                if (item == null) { ShowGunsmith(); return; }
                var def = session.Catalog.Find(item.ItemId);
                if (creditsReadout != null) creditsReadout.text = session.Profile.Credits + " 信用点";
                gunLabel.text = def.DisplayName;
                pageLabel.text = (index + 1) + " / " + guns.Count + (item.Slot == EquipmentSlot.Weapon ? " · 已装备" : "");
                Clear(iconRoot);
                DrawItemIcon(iconRoot, def, item, 0, 0, 75, 50);
                Clear(choices);
                for (int i = 0; i <= (int)PartSlot.Magazine; i++)
                {
                    PartSlot value = (PartSlot)i;
                    var button = Button(choices, PartName(value), 9 + i * 68, 8, 65, 36, () => ChooseSlot(value), value == slot);
                    button.interactable = WeaponRules.Socket(def.Weapon, value) != null;
                }
                var link = item.Parts.Get(slot);
                var fitted = session.Catalog.Find(link.ItemId);
                var legacy = (WeaponModSlot)slot;
                bool levels = (int)slot <= (int)PartSlot.Stock && WeaponTuning.SupportsUpgrades(def.Weapon) && item.ItemId == WeaponDefinition.RifleId;
                int owned = levels ? WeaponTuning.Owned(item.Mods, legacy) : 0;
                int installed = levels ? WeaponTuning.Level(item.Mods, legacy) : 0;
                Label(choices, "已装 · " + (fitted != null ? fitted.DisplayName : installed > 0 ? "原厂改装 " + installed : "原装"),
                    17, 13, 54, 328, 30, color: Aqua);
                parts.Clear(); AddLooseParts(session.Loadout); AddLooseParts(session.Stash);
                if (option >= parts.Count) option = -1;
                const int perPage = 3;
                int pages = Mathf.Max(1, (parts.Count + perPage - 1) / perPage);
                optionPage = Mathf.Clamp(optionPage, 0, pages - 1);
                var candidate = option >= 0 ? parts[option] : default;
                var part = session.Catalog.Find(candidate.Item?.ItemId);
                var proposed = item.Clone();
                if (showStock) proposed.Parts.Set(slot, default);
                else if (part != null) proposed.Parts.Set(slot, new PartLink { ItemId = part.Id, InstanceId = candidate.Id });
                else if (legacyLevel >= 0 && levels && WeaponTuning.TrySelect(item.Mods, legacy, legacyLevel, out var mods, out _)) proposed.Mods = mods;
                gunsmithStage.Set(def, proposed, session.Catalog, keepAngle: previewGun == choice.Id);
                previewGun = choice.Id;
                gunsmithDirty = true;
                art.color = Color.white;
                Label(choices, showStock ? "正在预览拆下" : parts.Count == 0 ? "暂无可安装的兼容配件" : "兼容配件 · " + parts.Count + " 件", 16, 13, 91, 223, 29, color: Muted);
                if (pages > 1)
                {
                    Button(choices, "‹", 237, 86, 44, 32, () => { option = -1; optionPage--; Refresh(); }).interactable = optionPage > 0;
                    Button(choices, "›", 294, 86, 44, 32, () => { option = -1; optionPage++; Refresh(); }).interactable = optionPage + 1 < pages;
                }
                for (int row = 0; row < perPage && optionPage * perPage + row < parts.Count; row++)
                {
                    int pick = optionPage * perPage + row;
                    var listing = parts[pick];
                    var loose = session.Catalog.Find(listing.Item.ItemId);
                    var cell = Button(choices, "", 12, 122 + row * 40, 329, 38, () =>
                    {
                        option = pick; legacyLevel = -1; showStock = false; Refresh();
                    }, option == pick);
                    DrawItemIcon(cell.transform, loose, listing.Item, 3, 2, 49, 34);
                    var tone = option == pick ? Ink : Pale;
                    Label(cell.transform, loose.DisplayName, 16, 54, 2, 203, 34, TextAnchor.MiddleLeft, tone);
                    Label(cell.transform, listing.Bag == session.Stash ? "仓库" : "随身", 13, 259, 2, 60, 34, TextAnchor.MiddleRight, tone);
                }
                Button(choices, "安装所选", 12, 245, 153, 42, () => Fit(candidate.Bag, candidate.Id), true).interactable = part != null && !showStock;
                Button(choices, showStock ? "确认拆下" : "预览拆下", 184, 245, 157, 42, () =>
                {
                    if (showStock) Fit(session.Stash, null);
                    else { showStock = true; option = legacyLevel = -1; Refresh(); }
                }).interactable = fitted != null;
                if (levels)
                {
                    Label(choices, "原厂改装 / 已购到 " + owned + " 级", 16, 13, 307, 328, 30, color: Amber);
                    for (int lv = 0; lv <= WeaponTuning.MaxUpgradeLevel; lv++)
                    {
                        int level = lv;
                        bool allowed = WeaponTuning.TrySelect(item.Mods, legacy, level, out _, out int cost);
                        string text = level == installed ? level + " · 使用中" : !allowed ? level + " · 待解锁" : cost == 0 ? level + " · 已购" : level + " · " + cost;
                        Button(choices, text, 12 + lv * 112, 341, 107, 38, () =>
                        {
                            showStock = false; option = -1; legacyLevel = level; Refresh();
                        }, legacyLevel == level).interactable = allowed && fitted == null && installed != level;
                    }
                    if (legacyLevel >= 0)
                        Button(choices, "应用原厂改装", 12, 391, 329, 38, () =>
                        {
                            if (session.SelectWeaponPart(choice.Bag, choice.Id, legacy, legacyLevel)) { legacyLevel = -1; Refresh(); }
                        }, true);
                    else if (fitted != null) Label(choices, "拆下新配件后可切回已购的原厂改装。", 15, 13, 389, 326, 52, color: Muted);
                }
                else Label(choices, "仅显示兼容接口及适用型号的配件。", 16, 13, 307, 328, 50, color: Muted);
                Compare(def.Weapon, item, proposed);

                void Fit(GridInventory source, string id)
                {
                    if (session.TryFitPart(choice.Bag, choice.Id, slot, source, id, out var error))
                    { option = legacyLevel = -1; showStock = false; Refresh(); }
                    else Notify(error ?? "未能更改配件。");
                }
                void AddLooseParts(GridInventory bag)
                {
                    foreach (var stack in bag.Items)
                        if (WeaponRules.CanMount(def, slot, session.Catalog.Find(stack.ItemId), out _))
                            parts.Add(new InventoryChoice(bag, stack.InstanceId));
                }
            }
            void Compare(WeaponDefinition def, ItemStack current, ItemStack proposed)
            {
                Clear(compare);
                Label(compare, "当前 → 装拆预览", 23, 13, 11, 302, 41, color: Amber);
                var now = WeaponTuning.Evaluate(def, current, session.Catalog);
                var after = WeaponTuning.Evaluate(def, proposed, session.Catalog);
                Stat("弹匣容量", WeaponRules.Capacity(def, current, session.Catalog), WeaponRules.Capacity(def, proposed, session.Catalog), "发", false, 65);
                Stat("换弹耗时", now.ReloadSeconds, after.ReloadSeconds, "秒", true, 113);
                Stat("后坐幅度", now.Recoil, after.Recoil, "", true, 161);
                Stat("腰射散布", now.HipSpread * 1000, after.HipSpread * 1000, "‰", true, 209);
                Stat("开镜散布", now.AimSpread * 1000, after.AimSpread * 1000, "‰", true, 257);
                Stat("开镜耗时", now.AimSeconds, after.AimSeconds, "秒", true, 305);
                Stat("开镜视角", now.AimFov, after.AimFov, "°", null, 353);
                Label(compare, "弹药 · " + AmmoName(def.AmmoFamily), 16, 13, 413, 301, 34, color: Muted);

                void Stat(string name, float a, float b, string unit, bool? lower, int y)
                {
                    Label(compare, name, 16, 13, y, 99, 29, color: Muted);
                    Color color = Mathf.Abs(b - a) < .001f || !lower.HasValue ? Pale : ((b < a) == lower.Value ? Aqua : MapDanger);
                    Label(compare, a.ToString("0.##") + " → " + b.ToString("0.##") + " " + unit, 16, 114, y, 201, 29, TextAnchor.UpperRight, color);
                    Fill(At("Comparison rule", compare, 13, y + 32, 303, 1), Border);
                }
            }
        }

        void RenderGunsmithPreview()
        {
            if (gunsmithTexture == null || (!gunsmithDirty && gunsmithTexture.IsCreated())) return;
            if (gunsmithStage.Render(gunsmithTexture, new Color32(17, 27, 33, 255))) gunsmithDirty = false;
        }

        void DisposeGunsmithPreview()
        {
            gunsmithDirty = false;
            gunsmithStage?.Dispose();
            gunsmithStage = null;
            ItemIconCache.Release(gunsmithTexture);
            gunsmithTexture = null;
        }
    }
}
