using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Dustline
{
    public sealed partial class GameUI
    {
        RectTransform inventoryBody, dragGhost;
        Text creditsReadout;
        LootContainer loot;
        GridInventory selectedBag, dragBag;
        string selectedId, dragId;
        bool placing, rotated, dragRotated;
        float carryScroll = 1f, stashScroll = 1f, lootScroll = 1f;
        int dragPointer = int.MinValue, grabX, grabY;
        Vector2 dragScreenPoint;
        readonly List<ScrollRect> inventoryScrollViews = new List<ScrollRect>();
        readonly List<RaycastResult> dropHits = new List<RaycastResult>();
        ItemIconCache itemIcons;
        static readonly EquipmentSlot[] GearSlots = { EquipmentSlot.Weapon, EquipmentSlot.Helmet, EquipmentSlot.Armor, EquipmentSlot.Rig, EquipmentSlot.Pack };

        void ResetInventoryView()
        {
            CancelInventoryDrag();
            inventoryScrollViews.Clear();
            inventoryBody = null;
            creditsReadout = null;
            loot = null;
            selectedBag = null;
            selectedId = null;
            placing = rotated = false;
            carryScroll = stashScroll = lootScroll = 1f;
        }

        public void ShowStash()
        {
            if (session.Phase != SessionPhase.Menu) return;
            var root = Display(ScreenPage.Stash, "Stash");
            Header(root, "仓库与整备", "点击查看或转移 · 拖到格子或装备槽 · 可旋转放置");
            inventoryBody = At("Stash contents", root, 26, 131, 1186, 463);
            Button(root, "补给商店", 26, 615, 149, 49, () => ShowShop());
            Button(root, "枪械改装", 185, 615, 149, 49, () => ShowGunsmith());
            Button(root, "应急装备", 344, 615, 149, 49, () => { if (dragPointer == int.MinValue) session.EmergencyKit(); });
            Button(root, "返回大厅", 1014, 615, 198, 49, ShowMenu, true);
            RefreshInventory();
        }

        void ShowShop(int group = 0, int index = 0)
        {
            if (session.Phase != SessionPhase.Menu) return;
            var root = Display(ScreenPage.Shop, "Supply store");
            Header(root, "补给商店", "购买后存入仓库，可在整备与改枪界面使用");
            string[] groups = { "武器", "弹药", "配件", "防护", "携行", "医疗", "物资" };
            for (int i = 0; i < groups.Length; i++)
            {
                int selected = i;
                Button(root, groups[i], 26 + 167 * i, 121, 153, 42, () => ShowShop(selected), i == group);
            }
            var stock = new List<ItemDefinition>();
            foreach (var item in session.Catalog.Items)
            {
                int type = item.Kind switch
                {
                    ItemKind.Weapon => 0, ItemKind.Ammo => 1, ItemKind.Part => 2,
                    ItemKind.Armor or ItemKind.Helmet => 3, ItemKind.Pack or ItemKind.Rig => 4, ItemKind.Medicine => 5, _ => 6
                };
                if (type == group) stock.Add(item);
            }
            const int pageSize = 8;
            int pages = Mathf.Max(1, (stock.Count + pageSize - 1) / pageSize);
            index = Mathf.Clamp(index, 0, pages - 1);
            for (int i = 0; i < pageSize && index * pageSize + i < stock.Count; i++)
            {
                var item = stock[index * pageSize + i];
                var cell = At("Stock / " + item.Id, root, 26 + i % 4 * 298, 177 + i / 4 * 209, 285, 194);
                Fill(cell, Ink);
                DrawItemIcon(cell, item, null, 9, 5, 111, 128);
                Label(cell, item.DisplayName, 19, 126, 11, 149, 65);
                Label(cell, ItemDetails(item, null), 14, 126, 77, 149, 66, color: Muted);
                var grade = ItemDefinition.GradeColor(item.Tier);
                if (item.Tier > 0) Fill(At("Grade", cell, 0, 0, 3, 194), grade);
                int count = item.Kind == ItemKind.Ammo ? Mathf.Min(20, item.MaxStack) : 1;
                Button(cell, "购买 ×" + count + " / " + item.Value * count, 13, 145, 259, 37, () =>
                {
                    session.Buy(item.Id, count);
                    if (creditsReadout != null) creditsReadout.text = session.Profile.Credits + " 信用点";
                }, true);
            }
            Button(root, "返回仓库", 26, 615, 157, 49, ShowStash);
            Button(root, "上一页", 802, 615, 124, 49, () => ShowShop(group, index - 1)).interactable = index > 0;
            Label(root, (index + 1) + " / " + pages, 18, 935, 615, 116, 49, TextAnchor.MiddleCenter);
            Button(root, "下一页", 1060, 615, 152, 49, () => ShowShop(group, index + 1)).interactable = index + 1 < pages;
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
            Header(root, container == null ? "战地背包" : container.DisplayName, "突袭仍在继续 / 打开背包不会暂停战斗");
            inventoryBody = At("Field inventory contents", root, 26, 131, 1186, 463);
            Button(root, "分部位健康", 26, 615, 155, 49, () => ShowHealth());
            Button(root, "切换射击模式", 191, 615, 174, 49, () =>
            {
                var weapon = session.Player?.Weapon;
                if (weapon?.Definition == null || weapon.IsMelee) { Notify("当前武器不能切换射击模式。"); return; }
                weapon.ToggleFireMode();
                Notify(weapon.Automatic ? "已切换为全自动" : "已切换为半自动");
            });
            Button(root, "设置", 908, 615, 95, 49, () => ShowSettings(true));
            Button(root, "返回突袭", 1014, 615, 198, 49, EnterField, true);
            RefreshInventory();
        }

        public void RefreshInventory()
        {
            bool stash = current == ScreenPage.Stash && session.Phase == SessionPhase.Menu;
            if (inventoryBody == null || dragPointer != int.MinValue || (!stash && (!InventoryOpen || session.Phase != SessionPhase.Raid))) return;
            if (creditsReadout != null) creditsReadout.text = session.Profile.Credits + " 信用点";
            inventoryScrollViews.Clear();
            Clear(inventoryBody);
            var bag = InventoryPlate("Carried gear", 0, 331);
            var storage = InventoryPlate("Storage", 343, 500);
            var details = InventoryPlate("Item actions", 855, 331);
            var carried = session.Loadout;
            Label(bag, "装备与随身物资", 22, 14, 8, 294, 35);
            Label(bag, "负重 " + carried.Weight.ToString("0.0") + " / " + carried.MaxWeight.ToString("0.#") + " 千克", 15, 14, 39, 298, 23, color: Muted);
            for (int i = 0; i < GearSlots.Length; i++) DrawEquipment(bag, carried, GearSlots[i], 14 + 61 * i);
            var rig = carried.Equipped(EquipmentSlot.Rig);
            var pack = carried.Equipped(EquipmentSlot.Pack);
            DrawGrid(bag, carried, CargoArea.Rig, ItemName(rig, "弹挂未装备"), 13, 131, 299, 64);
            DrawGrid(bag, carried, CargoArea.Main, ItemName(pack, "基础携行区"), 13, 233, 299, 190);
            if (stash) DrawGrid(storage, session.Stash, CargoArea.Main, "仓库储备", 16, 14, 468, 403);
            else if (loot != null)
            {
                DrawGrid(storage, loot.Inventory, CargoArea.Main, loot.DisplayName, 16, 14, 468, 383);
                if (loot.Inventory.Items.Count == 0) Label(storage, "已搜空", 21, 17, 406, 389, 41, color: Muted);
            }
            else
            {
                Label(storage, "搜刮指引", 24, 17, 12, 432, 39, color: Amber);
                Label(storage, "对准容器并搜索后，即可在此查看和转移物品。\n\n浏览界面不会暂停突袭。", 20, 17, 76, 447, 225, color: Muted);
            }
            ShowSelection(details, stash);
        }

        RectTransform InventoryPlate(string title, float x, float width)
        {
            var rect = At(title, inventoryBody, x, 0, width, 463);
            Fill(rect, Ink);
            Fill(At("Section line", rect, 0, 0, width, 2), Border);
            return rect;
        }

        void DrawGrid(RectTransform root, GridInventory bag, CargoArea area, string title, float left, float top, float width, float height)
        {
            int columns = bag.Columns(area), rows = bag.Rows(area);
            string shape = columns == 0 ? title : title + "  " + columns + " × " + rows;
            if (columns > 0 && bag == session.Loadout) shape += "  /  " + bag.CargoWeight(area).ToString("0.#") + " / " + bag.CargoLimit(area).ToString("0.#") + " 千克";
            Label(root, shape, 15, left, top, width, 27, color: columns == 0 ? Muted : Pale);
            if (columns == 0 || rows == 0) return;
            float fitted = Mathf.Min(width / columns, height / rows);
            bool scrollable = fitted < 32f && area == CargoArea.Main && width / columns >= 32f;
            float cell = scrollable ? Mathf.Min(39f, (width - 27f) / columns) : fitted;
            scrollable &= rows * cell > height;
            RectTransform host = root;
            if (scrollable)
            {
                host = At("Grid viewport", root, left, top + 28, width - 22f, height);
                Fill(host, Color.clear, true);
                host.gameObject.AddComponent<RectMask2D>();
            }
            var board = At("Item grid", host, scrollable ? 0f : left, scrollable ? 0f : top + 28, columns * cell, rows * cell);
            if (scrollable)
            {
                var scroll = host.gameObject.AddComponent<ScrollRect>();
                inventoryScrollViews.Add(scroll);
                scroll.viewport = host;
                scroll.content = board;
                scroll.horizontal = false;
                scroll.inertia = false;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.scrollSensitivity = cell;
                var track = At("Grid scroll", root, left + width - 20f, top + 28, 20f, height);
                Fill(track, Plate, true);
                var space = At("Grid scroll lane", track, 2, 2, 16, height - 4f);
                var handle = Fill(Stretch("Grid scroll handle", space), Muted, true);
                var scrollbar = track.gameObject.AddComponent<Scrollbar>();
                scrollbar.targetGraphic = handle;
                scrollbar.handleRect = handle.rectTransform;
                scrollbar.direction = Scrollbar.Direction.BottomToTop;
                scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
                scroll.verticalScrollbar = scrollbar;
                scroll.verticalNormalizedPosition = bag == session.Loadout ? carryScroll : bag == session.Stash ? stashScroll : lootScroll;
                scroll.onValueChanged.AddListener(value =>
                {
                    if (bag == session.Loadout) carryScroll = value.y;
                    else if (bag == session.Stash) stashScroll = value.y;
                    else lootScroll = value.y;
                });
            }
            var drops = board.gameObject.AddComponent<InventoryBoard>();
            drops.Bag = bag;
            drops.Area = area;
            drops.Initialize(columns, rows, cell, (x, y) => PlaceAt(bag, area, x, y), bag.Split(area));
            foreach (var item in bag.Items)
            {
                if (item.Slot != EquipmentSlot.None || item.Area != area) continue;
                var def = session.Catalog.Find(item.ItemId);
                float w = (item.Rotated ? def.Height : def.Width) * cell - 2,
                    h = (item.Rotated ? def.Width : def.Height) * cell - 2;
                string id = item.InstanceId;
                bool selected = selectedBag == bag && selectedId == id;
                var grade = ItemDefinition.GradeColor(def.Tier);
                var tile = Button(board, "", item.X * cell + 1, item.Y * cell + 1, w, h, () => SelectItem(bag, id), selected,
                    selected ? Color.Lerp(Plate, Aqua, .34f) : def.Tier > 0 ? Color.Lerp(Plate, grade, .23f) : Plate);
                tile.name = def.DisplayName;
                if (def.Tier > 0 || selected) Fill(At("Grade", tile.transform, 0, 0, 2, h), def.Tier > 0 ? grade : Aqua);
                DrawItemIcon(tile.transform, def, item, 2, 1, w - 4f, h - 2f, item.Rotated);
                if (item.Quantity > 1 || (def.Kind == ItemKind.Medicine && def.Uses > 1))
                {
                    string count = def.Kind == ItemKind.Medicine && def.Uses > 1 ? (item.Quantity * def.Uses - item.UsesSpent).ToString() : item.Quantity.ToString();
                    var number = Label(tile.transform, count, cell < 35f ? 11 : 14, 1, 1, w - 4f, 20f, TextAnchor.UpperRight);
                    number.gameObject.AddComponent<Outline>().effectColor = Ink;
                }
                BindDrag(tile.transform, bag, id, drops);
            }
        }

        void DrawEquipment(RectTransform root, GridInventory carried, EquipmentSlot slot, float x)
        {
            var stack = carried.Equipped(slot);
            string id = stack?.InstanceId;
            var box = Button(root, "", x, 67, 57, 57, () => { if (id != null) SelectItem(carried, id); }, false,
                selectedBag == carried && selectedId == id && id != null ? Color.Lerp(Plate, Aqua, .35f) : Plate);
            box.gameObject.AddComponent<GearDropSlot>().Slot = slot;
            Label(box.transform, SlotName(slot), 12, 1, 40, 55, 17, TextAnchor.MiddleCenter);
            if (stack == null) { Label(box.transform, "—", 17, 1, 6, 55, 34, TextAnchor.MiddleCenter, Muted); return; }
            var item = session.Catalog.Find(stack.ItemId);
            if (item.Tier > 0) Fill(At("Grade", box.transform, 0, 0, 2, 57), ItemDefinition.GradeColor(item.Tier));
            DrawItemIcon(box.transform, item, stack, 2, 2, 53, 38);
            BindDrag(box.transform, carried, id, null);
        }

        static string SlotName(EquipmentSlot slot) => slot switch
        {
            EquipmentSlot.Weapon => "武器", EquipmentSlot.Armor => "防弹衣", EquipmentSlot.Helmet => "头盔",
            EquipmentSlot.Rig => "弹挂", EquipmentSlot.Pack => "背包", _ => "物品"
        };

        void SelectItem(GridInventory bag, string id)
        {
            if (dragPointer != int.MinValue) return;
            var item = bag.Find(id);
            if (item == null) return;
            if (placing && item.Slot == EquipmentSlot.None && selectedId != id)
            { PlaceAt(bag, item.Area, item.X, item.Y); return; }
            selectedBag = bag;
            selectedId = id;
            rotated = item.Rotated;
            placing = false;
            RefreshInventory();
        }

        void PlaceAt(GridInventory bag, CargoArea area, int x, int y)
        {
            if (dragPointer != int.MinValue || !placing || selectedBag == null || selectedId == null) return;
            MoveInventory(selectedBag, selectedId, bag, area, x, y, rotated);
        }

        void MoveInventory(GridInventory source, string id, GridInventory target, CargoArea area, int x, int y, bool rotate)
        {
            bool ok = session.EditInventory(source, target, (GridInventory from, GridInventory to, out string reason) =>
            {
                if (from != to) return from.TryTransferAt(id, to, area, x, y, rotate, out reason);
                var item = from.Find(id);
                return item != null && item.Slot != EquipmentSlot.None
                    ? from.TryUnequipAt(item.Slot, area, x, y, rotate, out reason) : from.TryMove(id, area, x, y, rotate, out reason);
            }, out var error);
            if (ok) { selectedBag = target; selectedId = id; placing = false; rotated = rotate; }
            else Notify(error ?? "无法放入目标格。");
            RefreshInventory();
        }

        void BindDrag(Transform node, GridInventory bag, string id, InventoryBoard board)
        {
            var slide = node.gameObject.AddComponent<InventoryDrag>();
            slide.Begin = e =>
            {
                if (dragPointer != int.MinValue) return;
                var stack = bag.Find(id);
                if (stack == null) return;
                var item = session.Catalog.Find(stack.ItemId);
                dragPointer = e.pointerId;
                dragBag = bag;
                dragId = id;
                dragRotated = placing && selectedBag == bag && selectedId == id ? rotated : stack.Rotated;
                grabX = grabY = 0;
                if (board != null && dragRotated == stack.Rotated && board.TryCell(e.pressPosition, e.pressEventCamera, out int x, out int y))
                { grabX = x - stack.X; grabY = y - stack.Y; }
                dragGhost = Anchor("Dragged item", safe, .5f, .5f);
                dragGhost.SetAsLastSibling();
                dragGhost.sizeDelta = new Vector2(110f, 83f);
                Fill(dragGhost, new Color32(21, 35, 42, 232));
                DrawItemIcon(dragGhost, item, stack, 5, 5, 100, 73, dragRotated);
                MoveGhost(e);
            };
            slide.Move = MoveGhost;
            slide.End = FinishDrag;
        }

        void MoveGhost(PointerEventData e)
        {
            if (dragGhost != null && dragPointer == e.pointerId && RectTransformUtility.ScreenPointToLocalPointInRectangle(safe, e.position, null, out var local))
            {
                dragScreenPoint = e.position;
                Vector2 target = local - safe.rect.center + new Vector2(43f, 38f);
                Vector2 edges = Vector2.Max(Vector2.zero, (safe.rect.size - dragGhost.sizeDelta) * .5f);
                dragGhost.anchoredPosition = new Vector2(Mathf.Clamp(target.x, -edges.x, edges.x), Mathf.Clamp(target.y, -edges.y, edges.y));
            }
        }

        void ScrollDragEdge()
        {
            if (dragPointer == int.MinValue) return;
            foreach (var scroll in inventoryScrollViews)
            {
                var view = scroll.viewport;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(view, dragScreenPoint, null, out var local)
                    || !view.rect.Contains(local)) continue;
                float band = Mathf.Min(34f, view.rect.height * .18f);
                float steer = Mathf.Clamp01(1f - (view.rect.yMax - local.y) / band)
                    - Mathf.Clamp01(1f - (local.y - view.rect.yMin) / band);
                float overflow = scroll.content.rect.height - view.rect.height;
                if (overflow > 1f && Mathf.Abs(steer) > .01f)
                    scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition + steer * 270f * Time.unscaledDeltaTime / overflow);
                break;
            }
        }

        void FinishDrag(PointerEventData e)
        {
            if (dragPointer != e.pointerId) return;
            var from = dragBag;
            string id = dragId;
            bool rotate = dragRotated;
            int dx = grabX, dy = grabY;
            CancelInventoryDrag();
            if (inventoryBody == null || EventSystem.current == null || TouchControl.TouchWasCancelled(e)) return;
            dropHits.Clear();
            EventSystem.current.RaycastAll(e, dropHits);
            foreach (var hit in dropHits)
            {
                var gear = hit.gameObject.GetComponentInParent<GearDropSlot>();
                if (gear != null)
                {
                    if (session.EditInventory(from, session.Loadout, (GridInventory a, GridInventory b, out string why) => b.TryWearFrom(a, id, gear.Slot, out why), out var error))
                    { selectedBag = session.Loadout; selectedId = id; placing = false; rotated = selectedBag.Find(id)?.Rotated ?? false; }
                    else Notify(error ?? "不能装备到该栏位。");
                    RefreshInventory();
                    break;
                }
                var board = hit.gameObject.GetComponentInParent<InventoryBoard>();
                if (board != null && board.TryCell(e.position, e.pressEventCamera, out int x, out int y))
                { MoveInventory(from, id, board.Bag, board.Area, x - dx, y - dy, rotate); break; }
                if (hit.gameObject == inventoryBody.gameObject || !hit.gameObject.transform.IsChildOf(inventoryBody)) break;
            }
            dropHits.Clear();
        }

        void CancelInventoryDrag()
        {
            dragPointer = int.MinValue;
            dragId = null;
            dragBag = null;
            if (dragGhost == null) return;
            dragGhost.gameObject.SetActive(false);
            Destroy(dragGhost.gameObject);
            dragGhost = null;
        }

        void ShowSelection(RectTransform root, bool stash)
        {
            var item = selectedBag?.Find(selectedId);
            if (item == null)
            {
                Label(root, "物品详情", 23, 15, 10, 298, 39);
                Label(root, "点按物品查看详情，使用按钮快速转移；也可拖到目标格或装备槽。", 18, 15, 67, 296, 160, color: Muted);
                return;
            }
            var source = selectedBag;
            string id = item.InstanceId;
            var def = session.Catalog.Find(item.ItemId);
            DrawItemIcon(root, def, item, 11, 7, 61, 57, item.Rotated);
            Label(root, def.DisplayName, 21, 83, 8, 232, 50);
            Label(root, ItemDetails(def, item), 15, 14, 68, 304, 83, color: Muted);
            float y = 155;
            var target = stash ? (source == session.Stash ? session.Loadout : session.Stash)
                : loot == null ? null : source == loot.Inventory ? session.Loadout : loot.Inventory;
            if (target != null)
                ActionButton(root, target == session.Loadout ? "转入随身栏" : stash ? "转入仓库" : "放入容器", ref y, () =>
                {
                    if (session.EditInventory(source, target, (GridInventory from, GridInventory to, out string e) => from.TryTransferTo(id, to, out e), out var error))
                    { selectedBag = target; selectedId = id; placing = false; rotated = target.Find(id)?.Rotated ?? false; }
                    else Notify(error ?? "无法转移物品。");
                    RefreshInventory();
                });
            if (item.Slot == EquipmentSlot.None)
            {
                ActionButton(root, placing ? "取消放置" : "选格放置", ref y, () =>
                {
                    placing = !placing;
                    rotated = item.Rotated;
                    RefreshInventory();
                });
                ActionButton(root, rotated ? "旋转到 0°" : "旋转到 90°", ref y, () =>
                {
                    bool rotate = !rotated;
                    if (session.EditInventory(source, source, (GridInventory from, GridInventory to, out string e) => from.TryMove(id, item.Area, item.X, item.Y, rotate, out e), out _))
                        placing = false;
                    else { placing = true; Notify("请选择可放下旋转物品的格子。"); }
                    rotated = rotate;
                    RefreshInventory();
                });
            }
            if (def.Equipment != EquipmentSlot.None)
                ActionButton(root, item.Slot == EquipmentSlot.None ? "装备 / 换装" : "卸下装备", ref y, () =>
                {
                    if (!session.EditInventory(source, session.Loadout, (GridInventory from, GridInventory to, out string e) =>
                        item.Slot != EquipmentSlot.None ? from.TryUnequip(item.Slot, out e) : to.TryWearFrom(from, id, def.Equipment, out e), out var error))
                        Notify(error ?? "无法变更装备。");
                    else { selectedBag = session.Loadout; selectedId = id; placing = false; rotated = selectedBag.Find(id)?.Rotated ?? false; }
                    RefreshInventory();
                });
            if (stash && def.Weapon != null && !def.Weapon.Melee)
            {
                ActionButton(root, "检查与改装", ref y, () => ShowGunsmith(source, id));
                if (item.LoadedAmmo > 0)
                    ActionButton(root, "卸弹到仓库", ref y, () =>
                    {
                        if (!session.TryUnloadAmmunition(source, id, out var error)) Notify(error ?? "卸弹失败。");
                        RefreshInventory();
                    });
            }
            if (stash && def.Kind == ItemKind.Ammo)
                ActionButton(root, "装填至已装备武器", ref y, () =>
                {
                    if (!session.TryLoadAmmunition(source, id, out var error)) Notify(error ?? "装填失败。");
                    RefreshInventory();
                });
            if (stash)
            {
                string sale = (!item.Parts.Empty || item.LoadedAmmo > 0) ? "出售枪身 · 退回配件和弹药" : "出售";
                ActionButton(root, sale + " / " + Mathf.Max(1, def.Value / 2) * item.Quantity, ref y, () => session.Sell(source, id));
            }
            else if (source == session.Loadout && def.Kind == ItemKind.Medicine) ActionButton(root, "选择治疗部位", ref y, () => ShowHealth(null, id));
            if (placing) Label(root, "选择目标格", 14, 14, 438, 288, 21, color: Amber);
        }

        string ItemDetails(ItemDefinition item, ItemStack stack)
        {
            string result = item.KindName + (item.Tier > 0 ? " / " + item.Tier + " 级" : "") + " / " + (item.UnitWeight * (stack?.Quantity ?? 1)).ToString("0.##") + " 千克";
            if (item.Storage.Columns > 0)
                result += "\n" + item.Storage.Columns + " × " + item.Storage.Rows + " 格 / 承重 +" + item.Storage.CarryBonus + " / 内载 " + item.Storage.WeightLimit + (item.Storage.Split > 0 ? " / 分区" : "");
            if (item.Durability > 0) result += "\n耐久 " + (item.Durability - (stack?.Wear ?? 0)) + " / " + item.Durability + " · " + CoverageName(item.Coverage);
            if (item.Kind == ItemKind.Medicine) result += "\n" + TreatmentName(item.Treatment) + " / " + item.UseSeconds.ToString("0.#") + " 秒 / " + ((stack?.Quantity ?? 1) * item.Uses - (stack?.UsesSpent ?? 0)) + " 次";
            if (item.Weapon != null && !item.Weapon.Melee)
                result += "\n弹匣 " + Mathf.Max(0, stack?.LoadedAmmo ?? 0) + " / " + WeaponRules.Capacity(item.Weapon, stack, session.Catalog)
                    + " · " + AmmoName(item.Weapon.AmmoFamily);
            if (item.Kind == ItemKind.Ammo) result += "\n" + AmmoName(item.AmmoFamily);
            if (item.Kind == ItemKind.Part) result += "\n" + PartName(item.Part.Slot) + " · " + (string.IsNullOrEmpty(item.Part.DedicatedItem)
                ? item.Part.Interface : session.Catalog.Find(item.Part.DedicatedItem)?.DisplayName ?? "指定型号");
            return result;
        }

        static string CoverageName(BodyCoverage coverage)
        {
            if ((coverage & BodyCoverage.Head) != 0) return "头部";
            string value = "胸部";
            if ((coverage & BodyCoverage.Abdomen) != 0) value += "、腹部";
            if ((coverage & (BodyCoverage.LeftArm | BodyCoverage.RightArm)) != 0) value += "、手臂";
            return value;
        }

        static string AmmoName(AmmoFamily family) => family switch
        {
            AmmoFamily.Amber => "琥珀规格", AmmoFamily.Silver => "银标规格", AmmoFamily.Slate => "岩纹规格",
            AmmoFamily.Copper => "铜线规格", _ => "原装规格"
        };

        RawImage DrawItemIcon(Transform parent, ItemDefinition item, ItemStack stack, float x, float y, float w, float h, bool turn = false,
            WeaponForm blade = WeaponForm.Tangdao)
        {
            var rect = Anchor("Item model / " + (item?.Id ?? blade.ToString()), parent, 0, 1);
            rect.anchoredPosition = new Vector2(x + w * .5f, -y - h * .5f);
            rect.localRotation = Quaternion.Euler(0, 0, turn ? -90f : 0f);
            float aspect = Mathf.Clamp(item == null ? 2f : item.Width / (float)item.Height, .25f, 4f);
            float fit = Mathf.Min((turn ? h : w) / aspect, turn ? w : h);
            rect.sizeDelta = new Vector2(aspect * fit, fit);
            var image = rect.gameObject.AddComponent<RawImage>();
            image.raycastTarget = false;
            itemIcons.Bind(image, item, stack, session.Catalog, blade);
            return image;
        }

        void ActionButton(Transform parent, string title, ref float y, System.Action action)
        {
            Button(parent, title, 14, y, 303, 34, () => { if (dragPointer == int.MinValue) action(); });
            y += 39;
        }
    }
}
