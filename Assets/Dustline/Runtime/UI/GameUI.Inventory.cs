using System;
using UnityEngine;
using UnityEngine.UI;

namespace Dustline
{
    public sealed partial class GameUI
    {
        RectTransform inventoryBody;
        Text creditsReadout;
        LootContainer loot;
        GridInventory selectedBag;
        string selectedId;
        bool placing, rotated;

        void ResetInventoryView()
        {
            inventoryBody = null;
            creditsReadout = null;
            loot = null;
            selectedBag = null;
            selectedId = null;
            placing = rotated = false;
        }

        public void ShowStash()
        {
            if (session.Phase != SessionPhase.Menu) return;
            var root = Display(ScreenPage.Stash, "Stash");
            Header(root, "仓库与整备", "行动大厅 / 仓库");
            inventoryBody = At("Stash contents", root, 26, 130, 1186, 461);
            ShopButton(root, "步枪", "rifle", 1, 26, 120);
            ShopButton(root, "弹药 ×30", "ammo", 30, 156, 148);
            ShopButton(root, "医疗包", "medkit", 1, 314, 125);
            ShopButton(root, "防弹衣", "armor", 1, 449, 125);
            ShopButton(root, "短棍", WeaponDefinition.BatonId, 1, 584, 125);
            Button(root, "应急装备", 719, 615, 142, 49, session.EmergencyKit);
            Button(root, "返回大厅", 1014, 615, 198, 49, ShowMenu, true);
            RefreshInventory();
        }

        void ShopButton(Transform root, string name, string id, int count, float x, float width)
        {
            int cost = session.Catalog.Find(id).Value * count;
            Button(root, name + " / " + cost, x, 615, width, 49, () => session.Buy(id, count));
        }

        public void ToggleInventory()
        {
            if (session.Phase != SessionPhase.Raid) return;
            if (current == ScreenPage.Field) ShowFieldInventory(null);
            else if (InventoryOpen) EnterField();
        }

        public void OpenLoot(LootContainer container)
        {
            if (session.Phase == SessionPhase.Raid && current == ScreenPage.Field && container != null && container.Searched)
                ShowFieldInventory(container);
        }

        void ShowFieldInventory(LootContainer container)
        {
            var root = Display(ScreenPage.FieldInventory, "Field inventory");
            loot = container;
            Header(root, container == null ? "战地背包" : "物资搜刮", "突袭仍在继续 / 打开背包不会暂停战斗");
            inventoryBody = At("Field inventory contents", root, 26, 130, 1186, 461);
            Button(root, session.Input.TouchMode ? "使用医疗品" : "使用医疗品 [H]", 26, 615, 182, 49, () => session.UseMedicine());
            Button(root, "切换射击模式", 219, 615, 179, 49, () =>
            {
                var weapon = session.Player.Weapon;
                if (weapon.Definition == null) { Notify("未装备武器。"); return; }
                if (weapon.IsMelee) { Notify("近战武器不需要切换射击模式。"); return; }
                weapon.ToggleFireMode();
                Notify(weapon.Automatic ? "已切换为全自动" : "已切换为半自动");
            });
            Button(root, "设置", 908, 615, 95, 49, () => ShowSettings(true));
            Button(root, session.Input.TouchMode ? "返回突袭" : "返回突袭 [Tab]", 1014, 615, 198, 49, EnterField, true);
            RefreshInventory();
        }

        public void RefreshInventory()
        {
            bool stash = current == ScreenPage.Stash && session.Phase == SessionPhase.Menu;
            if (inventoryBody == null || (!stash && (!InventoryOpen || session.Phase != SessionPhase.Raid))) return;
            if (creditsReadout != null) creditsReadout.text = session.Profile.Credits + " 信用点";
            Clear(inventoryBody);
            var storage = InventoryPlate("Storage", 0, 423);
            var bag = InventoryPlate("Carried gear", 439, 376);
            var details = InventoryPlate("Item actions", 831, 355);
            if (stash) DrawGrid(storage, session.Stash, "仓库储备", 32);
            else if (loot != null)
            {
                DrawGrid(storage, loot.Inventory, loot.DisplayName, 51);
                if (loot.Inventory.Items.Count == 0)
                    Label(storage, "已搜空", 21, 17, 368, 389, 40, color: Muted);
            }
            else
            {
                Label(storage, "搜刮指引", 24, 17, 12, 390, 39, color: Amber);
                Label(storage, "靠近物资箱、工具箱、医疗箱或储物柜。对准后保持站定，完成搜索即可转移物品。\n\n撤离点以界面上的方位和距离为准，进入后连续停留 8 秒。", 20, 17, 80, 385, 301, color: Muted);
            }
            DrawGrid(bag, session.Loadout, stash ? "整备背包" : "随身背包", 49);
            DrawEquipment(bag, EquipmentSlot.Weapon, 369);
            DrawEquipment(bag, EquipmentSlot.Armor, 414);
            ShowSelection(details, stash);
        }

        RectTransform InventoryPlate(string title, float x, float width)
        {
            var rect = At(title, inventoryBody, x, 0, width, 461);
            Fill(rect, Ink);
            Fill(At("Section line", rect, 0, 0, width, 2), Border);
            return rect;
        }

        void DrawGrid(RectTransform root, GridInventory bag, string title, float cell)
        {
            Label(root, title, 23, 17, 9, 340, 32);
            Label(root, "负重 " + bag.Weight.ToString("0.0") + " / " + bag.MaxWeight.ToString("0") + " 千克", 15, 17, 42, 340, 23, color: Muted);
            var board = At("Item grid", root, 17, 69, bag.Width * cell, bag.Height * cell);
            board.gameObject.AddComponent<InventoryBoard>().Initialize(bag.Width, bag.Height, cell, (x, y) => PlaceAt(bag, x, y));
            foreach (var item in bag.Items)
            {
                if (item.Slot != EquipmentSlot.None) continue;
                var definition = session.Catalog.Find(item.ItemId);
                int width = item.Rotated ? definition.Height : definition.Width;
                int height = item.Rotated ? definition.Width : definition.Height;
                string id = item.InstanceId;
                bool selected = selectedBag == bag && selectedId == id;
                string label = definition.DisplayName;
                if (definition.Kind == ItemKind.Ammo) label = cell < 40 ? "弹" : "弹药\n" + item.Quantity;
                else if (definition.Kind == ItemKind.Medicine && width == 1) label = cell < 40 ? "医\n疗" : "医疗包";
                else if (width == 1) label = definition.KindName;
                else if (item.Quantity > 1 && cell >= 40) label += " ×" + item.Quantity;
                var button = Button(board, label, item.X * cell + 1, item.Y * cell + 1, width * cell - 2, height * cell - 2,
                    () => SelectItem(bag, id), selected, selected ? Amber : Color.Lerp(Plate, definition.Tint, 0.37f));
                Fill(At("Item stripe", button.transform, 1, 1, 2, height * cell - 4), selected ? Ink : definition.Tint);
            }
        }

        void DrawEquipment(RectTransform root, EquipmentSlot slot, float y)
        {
            var item = session.Loadout.Equipped(slot);
            string name = slot == EquipmentSlot.Weapon ? "武器" : "护甲";
            Button(root, name + " / " + ItemName(item, "未装备"), 17, y, 342, 39, () =>
            {
                if (item != null) SelectItem(session.Loadout, item.InstanceId);
            }, item != null && selectedBag == session.Loadout && selectedId == item.InstanceId);
        }

        void SelectItem(GridInventory bag, string id)
        {
            var item = bag.Find(id);
            if (item == null) return;
            if (placing && bag == selectedBag && item.Slot == EquipmentSlot.None)
            {
                PlaceAt(bag, item.X, item.Y);
                return;
            }
            selectedBag = bag;
            selectedId = id;
            rotated = item.Rotated;
            placing = false;
            RefreshInventory();
        }

        void PlaceAt(GridInventory bag, int x, int y)
        {
            if (!placing || bag != selectedBag || selectedId == null) return;
            if (!bag.TryMove(selectedId, x, y, rotated, out var error)) { Notify(error); return; }
            placing = false;
            session.InventoryChanged();
            RefreshInventory();
        }

        void ShowSelection(RectTransform root, bool stash)
        {
            var item = selectedBag?.Find(selectedId);
            if (item == null)
            {
                Label(root, "物品详情", 23, 17, 9, 321, 39);
                Label(root, "点击物品查看详情。可按当前物品和位置执行转移、旋转、装备或出售。", 19, 17, 72, 321, 235, color: Muted);
                return;
            }
            var definition = session.Catalog.Find(item.ItemId);
            Label(root, definition.DisplayName, 23, 17, 9, 321, 57);
            string info = definition.KindName + "  /  数量 " + item.Quantity + "  /  " + (definition.UnitWeight * item.Quantity).ToString("0.00") + " 千克";
            if (definition.Kind == ItemKind.Weapon)
                info += definition.Weapon != null && definition.Weapon.Melee ? "\n近战武器 / 无需弹药" : "\n弹匣 " + Mathf.Max(0, item.LoadedAmmo);
            Label(root, info, 17, 17, 65, 321, 61, color: Muted);
            float y = 133;
            GridInventory target = stash ? (selectedBag == session.Stash ? session.Loadout : session.Stash)
                : loot == null ? null : selectedBag == loot.Inventory ? session.Loadout : loot.Inventory;
            if (target != null)
            {
                string verb = target == session.Loadout ? "转入背包" : stash ? "转入仓库" : "放入容器";
                ActionButton(root, verb, ref y, () =>
                {
                    if (!selectedBag.TryTransferTo(selectedId, target, out var error)) Notify(error);
                    else { selectedId = null; placing = false; session.InventoryChanged(); }
                    RefreshInventory();
                });
            }
            if (item.Slot == EquipmentSlot.None)
            {
                ActionButton(root, placing ? "取消移动" : "移动", ref y, () => { placing = !placing; RefreshInventory(); });
                ActionButton(root, rotated ? "旋转 / 90 度" : "旋转 / 0 度", ref y, () => { rotated = !rotated; placing = true; RefreshInventory(); });
            }
            if (selectedBag == session.Loadout && (definition.Kind == ItemKind.Weapon || definition.Kind == ItemKind.Armor))
            {
                ActionButton(root, item.Slot == EquipmentSlot.None ? "装备" : "卸下", ref y, () =>
                {
                    string error;
                    bool ok = item.Slot == EquipmentSlot.None
                        ? selectedBag.TryEquip(item.InstanceId, definition.Kind == ItemKind.Weapon ? EquipmentSlot.Weapon : EquipmentSlot.Armor, out error)
                        : selectedBag.TryUnequip(item.Slot, out error);
                    if (!ok) Notify(error);
                    else session.InventoryChanged();
                    placing = false;
                    RefreshInventory();
                });
            }
            if (stash)
                ActionButton(root, "出售 / " + Mathf.Max(1, definition.Value / 2) * item.Quantity, ref y, () => session.Sell(selectedBag, selectedId));
            else if (selectedBag == session.Loadout && definition.Kind == ItemKind.Medicine)
                ActionButton(root, "使用医疗品", ref y, () => session.UseMedicine(selectedId));
            if (placing) Label(root, "请选择同一物品栏中的目标格。", 16, 17, y + 6, 320, 54, color: Amber);
        }

        void ActionButton(Transform parent, string title, ref float y, Action action)
        {
            Button(parent, title, 17, y, 321, 39, action);
            y += 46;
        }
    }
}
