using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Dustline
{
    public sealed partial class GameUI
    {
        RectTransform inventoryBody, inventoryDetails, inventoryMode, dragGhost;
        Button rotateDragButton;
        RawImage ghostIcon;
        Text creditsReadout;
        LootContainer loot;
        GridInventory selectedBag, dragBag;
        string selectedId, dragId;
        bool detailsRequested, placing, rotated, dragRotated;
        float carryScroll = 1f, stashScroll = 1f, lootScroll = 1f;
        int dragPointer = int.MinValue, grabX, grabY;
        Vector2 dragScreenPoint;
        InventoryBoard paintedBoard;
        GearDropSlot paintedGear;
        Color gearOriginal;
        (int x, int y, bool turned, int from, int to) paintedPlacement;
        PointerEventData dropProbe;
        readonly List<InventoryScroll> inventoryScrollViews = new List<InventoryScroll>();
        InventoryScroll inventoryScrollOwner;
        readonly List<RaycastResult> dropHits = new List<RaycastResult>();
        ItemIconCache itemIcons;

        void ResetInventoryView()
        {
            CancelInventoryInput();
            inventoryScrollViews.Clear();
            inventoryBody = inventoryDetails = inventoryMode = null;
            rotateDragButton = null;
            creditsReadout = null;
            loot = null;
            ClearSelection();
            carryScroll = stashScroll = lootScroll = 1f;
        }

        public void ShowStash()
        {
            if (session.Phase != SessionPhase.Menu) return;
            var root = Display(ScreenPage.Stash, "Stash", wide: true);
            reflowWidePage = () => BuildInventoryPage(root, true);
            BuildInventoryPage(root, true);
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
            var root = Display(ScreenPage.FieldInventory, "Field inventory", wide: true);
            loot = container;
            reflowWidePage = () => BuildInventoryPage(root, false);
            BuildInventoryPage(root, false);
        }

        void BuildInventoryPage(RectTransform root, bool stash)
        {
            CancelInventoryInput();
            Clear(root);
            float w = root.rect.width;
            string title = stash ? "仓库与整备" : loot == null ? "战地背包" : loot.DisplayName;
            WideHeader(root, title, stash ? "轻点查看 · 按住拖柄移动" : "浏览物品不会暂停突袭", stash);
            inventoryBody = At(stash ? "Stash contents" : "Field inventory contents", root, 12, 57, w - 24, 565);
            var footer = At("Inventory footer", root, 0, 630, w, 50);
            Fill(footer, Ink);
            inventoryMode = At("Placement controls", footer, Mathf.Max(435f, w * .5f - 163f), 3, 333, 42);
            if (stash)
            {
                Button(root, "补给商店", 12, 633, 135, 42, () => { if (dragPointer == int.MinValue) ShowShop(); });
                Button(root, "枪械改装", 155, 633, 135, 42, () => { if (dragPointer == int.MinValue) ShowGunsmith(selectedBag, selectedId); });
                Button(root, "应急装备", 298, 633, 128, 42, () => { if (dragPointer == int.MinValue) session.EmergencyKit(); });
            }
            else
            {
                Button(root, "分部位健康", 12, 633, 135, 42, () => { if (dragPointer == int.MinValue) ShowHealth(); });
                Button(root, "切换射击模式", 155, 633, 155, 42, () =>
                {
                    if (dragPointer != int.MinValue) return;
                    var weapon = session.Player?.Weapon;
                    if (weapon?.Definition == null || weapon.IsMelee) { Notify("当前武器不能切换射击模式。"); return; }
                    weapon.ToggleFireMode();
                    Notify(weapon.Automatic ? "已切换为全自动" : "已切换为半自动");
                });
                Button(root, "设置", w - 245, 633, 81, 42, () => { if (dragPointer == int.MinValue) ShowSettings(true); });
            }
            rotateDragButton = Button(footer, "旋转物品 90°", Mathf.Max(435f, w * .5f - 76), 3, 152, 42, RotateDraggedItem, true);
            rotateDragButton.gameObject.SetActive(false);
            Button(root, stash ? "返回大厅" : "返回突袭", w - 155, 633, 143, 42,
                () => { if (dragPointer == int.MinValue) { if (stash) ShowMenu(); else EnterField(); } }, true);
            RefreshInventory();
        }

        public void RefreshInventory()
        {
            bool stash = current == ScreenPage.Stash && session.Phase == SessionPhase.Menu;
            if (inventoryBody == null || dragPointer != int.MinValue || (!stash && (!InventoryOpen || session.Phase != SessionPhase.Raid))) return;
            UpdateCredits();
            if (selectedBag?.Find(selectedId) == null) ClearSelection();
            if (inventoryScrollOwner != null) inventoryScrollOwner.Cancel();
            inventoryScrollViews.Clear();
            Clear(inventoryBody);
            inventoryDetails = null;
            float width = inventoryBody.rect.width;
            float h = inventoryBody.rect.height;
            const float gap = 12f;
            float left = Mathf.Floor((width - gap) * .45f), right = width - gap - left;
            var carried = session.Loadout;
            var gear = InventoryPlate("Carried gear", 0, left, h);
            var storage = InventoryPlate(stash ? "Stash grid region" : "Loot grid region", left + gap, right, h);
            DrawGear(gear, carried, left);
            DrawCarried(gear, carried, left, h);

            var target = stash ? session.Stash : loot?.Inventory;
            Label(storage, stash ? "仓库储备" : loot == null ? "搜刮区" : "容器物资", 21, 12, 3, right * .5f, 31, TextAnchor.MiddleLeft);
            if (target != null)
            {
                Label(storage, target.Width + " 列 × " + target.Height + " 行", 17, right * .5f, 3, right * .5f - 14, 31,
                    TextAnchor.MiddleRight, Muted);
                if (!stash && target.Items.Count == 0)
                    Label(storage, "已搜空", 17, 14, h - 39, right - 28, 31, TextAnchor.MiddleRight, Muted);
                float cell = Mathf.Min((right - 48) / target.Width, stash ? 95f : 62f);
                float height = cell * target.Height;
                float leftInset = stash ? 12f : Mathf.Floor((right - cell * target.Width - 25f) * .5f);
                var viewport = VerticalInventory(storage, leftInset, 38, right - leftInset - 12f, h - 48f, height,
                    stash ? stashScroll : lootScroll, value => { if (stash) stashScroll = value; else lootScroll = value; });
                DrawGrid(viewport, target, CargoArea.Main, 0, 0, cell);
            }
            else Label(storage, "对准容器并搜索后，可在这里查看和转移物品。", 19, 14, 53, right - 28, 70, color: Muted);
            DrawPlacementControls();
            if (detailsRequested && !placing && selectedId != null)
            {
                const float cardWidth = 330f;
                float x = selectedBag == carried ? width - cardWidth - 11 : (left - cardWidth) * .5f;
                inventoryDetails = At("Item details", inventoryBody, x, 43, cardWidth, 445);
                ShowSelection(inventoryDetails, stash);
            }
        }

        RectTransform InventoryPlate(string title, float x, float width, float h)
        {
            var rect = At(title, inventoryBody, x, 0, width, h);
            Fill(rect, Ink);
            Fill(At("Section line", rect, 0, 0, width, 2), Border);
            return rect;
        }

        void DrawGear(RectTransform root, GridInventory carried, float width)
        {
            Label(root, "角色穿戴", 21, 12, 3, 161, 31, TextAnchor.MiddleLeft);
            Label(root, "负重 " + carried.Weight.ToString("0.0") + " / " + carried.MaxWeight.ToString("0.#") + " 千克",
                17, 177, 3, width - 190, 31, TextAnchor.MiddleRight, Muted);
            float bodyX = width * .5f - 52, outer = Mathf.Min(246f, Mathf.Floor((width - 134) * .5f));
            var figure = At("Equipment silhouette", root, bodyX, 38, 104, 183).gameObject.AddComponent<BodyFigure>();
            figure.Initialize(Plate, new Color32(75, 106, 109, 255));
            float r = width - outer - 12;
            Draw(EquipmentSlot.Helmet, 12, 42, outer, 60, BodyPart.Head, true);
            Draw(EquipmentSlot.Weapon, 12, 113, outer, 99, BodyPart.LeftArm, true);
            Draw(EquipmentSlot.Armor, r, 55, outer, 51, BodyPart.Chest, false);
            Draw(EquipmentSlot.Rig, r, 111, outer, 51, BodyPart.Abdomen, false);
            Draw(EquipmentSlot.Pack, r, 167, outer, 52, BodyPart.RightArm, false);
            Fill(At("Gear divider", root, 12, 225, width - 24, 1), Border);

            void Draw(EquipmentSlot slot, float x, float y, float w, float h, BodyPart part, bool fromLeft)
            {
                Stroke(root, "Equipment leader", new Vector2(fromLeft ? x + w : x, y + h * .5f),
                    BodyFigure.Pin(part, bodyX, 38, 104, 183), 1, Border);
                DrawEquipment(root, carried, slot, x, y, w, h);
            }
        }

        void DrawCarried(RectTransform root, GridInventory carried, float width, float h)
        {
            var rig = carried.Equipped(EquipmentSlot.Rig);
            var pack = carried.Equipped(EquipmentSlot.Pack);
            const float head = 30, empty = 26, inset = 6, gutter = 10;
            float internalWidth = width - 42f;
            float rigCell = carried.Columns(CargoArea.Rig) == 0 ? 0f : Mathf.Min(38f, (internalWidth - 2 * inset) / carried.Columns(CargoArea.Rig));
            float packCell = Mathf.Min(42f, (internalWidth - 2 * inset) / carried.Width);
            float rigHeight = rig == null || rigCell <= 0f ? empty : head + carried.Rows(CargoArea.Rig) * rigCell + gutter;
            float packHeight = (pack == null ? empty : 0) + head + carried.Height * packCell + gutter;
            var content = VerticalInventory(root, 12, 232, width - 24, h - 242, rigHeight + packHeight,
                carryScroll, value => carryScroll = value);
            if (rig == null || rigCell <= 0f) Label(content, "弹挂未装备", 16, inset, 0, internalWidth, empty, TextAnchor.MiddleLeft, Muted);
            else
            {
                ContainerHeader(CargoArea.Rig, ItemName(rig, "弹挂"), 0);
                DrawGrid(content, carried, CargoArea.Rig, inset, head, rigCell);
            }
            float packTop = rigHeight;
            if (pack == null)
            {
                Label(content, "背包未装备", 16, inset, packTop, internalWidth, empty, TextAnchor.MiddleLeft, Muted);
                packTop += empty;
            }
            ContainerHeader(CargoArea.Main, ItemName(pack, "基础携行区"), packTop);
            DrawGrid(content, carried, CargoArea.Main, inset, packTop + head, packCell);

            void ContainerHeader(CargoArea area, string name, float y)
            {
                int columns = carried.Columns(area), rows = carried.Rows(area);
                int split = carried.Split(area);
                string layout = split > 0 ? split + "+" + (columns - split) + " 列" : columns + " 列";
                var label = Label(content, name + "   " + layout + " × " + rows + " 行  /  " + columns * rows + " 格", 16,
                    inset, y, internalWidth - 133, head, TextAnchor.MiddleLeft);
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 13;
                label.resizeTextMaxSize = 16;
                Label(content, carried.CargoWeight(area).ToString("0.#") + " / " + carried.CargoLimit(area).ToString("0.#") + " 千克",
                    15, internalWidth - 128, y, 128, head, TextAnchor.MiddleRight, Muted);
            }
        }

        RectTransform VerticalInventory(Transform parent, float x, float y, float w, float h, float contentHeight, float position, System.Action<float> changed)
        {
            const float trackWidth = 18f;
            var viewport = At("Grid viewport", parent, x, y, w - trackWidth - 4, h);
            Fill(viewport, Color.clear, true);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = At("Grid content", viewport, 0, 0, w - trackWidth - 4, contentHeight);
            if (contentHeight <= h + .01f) return content;
            var scroll = viewport.gameObject.AddComponent<InventoryScroll>();
            inventoryScrollViews.Add(scroll);
            scroll.TryAcquire = source =>
            {
                if (dragPointer != int.MinValue || inventoryScrollOwner != null) return false;
                inventoryScrollOwner = source;
                return true;
            };
            scroll.Released = source => { if (inventoryScrollOwner == source) inventoryScrollOwner = null; };
            scroll.AllowScroll = () => dragPointer == int.MinValue && inventoryScrollOwner == null;
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.inertia = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 45f;
            var track = At("Grid scroll", parent, x + w - trackWidth, y, trackWidth, h);
            Fill(track, Plate);
            float thumbHeight = (h - 4f) * h / contentHeight;
            var thumb = At("Grid scroll thumb", track, 2, 2, trackWidth - 4, thumbHeight);
            Fill(thumb, Muted);
            content.anchoredPosition = new Vector2(0, (contentHeight - h) * (1 - Mathf.Clamp01(position)));
            ShowPosition(position);
            scroll.onValueChanged.AddListener(value => { ShowPosition(value.y); changed(Mathf.Clamp01(value.y)); });
            void ShowPosition(float value) => thumb.anchoredPosition = new Vector2(2, -2 - (h - 4f - thumbHeight) * (1 - Mathf.Clamp01(value)));
            return content;
        }

        void DrawGrid(Transform root, GridInventory bag, CargoArea area, float left, float top, float cell)
        {
            int columns = bag.Columns(area), rows = bag.Rows(area);
            var board = At("Item grid", root, left, top, columns * cell, rows * cell);
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
                var tileRect = At(def.DisplayName, board, item.X * cell + 1, item.Y * cell + 1, w, h);
                var tile = tileRect.gameObject.AddComponent<InventoryTile>();
                tile.targetGraphic = Fill(tileRect, Plate, true);
                tile.navigation = new Navigation { mode = Navigation.Mode.None };
                StyleButton(tile, selected, selected ? Color.Lerp(Plate, Aqua, .34f) : def.Tier > 0 ? Color.Lerp(Plate, grade, .23f) : Plate);
                tile.onClick.AddListener(() => SelectItem(bag, id));
                tile.Place = e =>
                {
                    if (!placing) return false;
                    if (drops.TryCell(e.position, e.pressEventCamera, out int x, out int y)) PlaceAt(bag, area, x, y);
                    return true;
                };
                if (def.Tier > 0 || selected) Fill(At("Grade", tile.transform, 0, 0, 2, h), def.Tier > 0 ? grade : Aqua);
                DrawItemIcon(tile.transform, def, item, 1, 1, w - 2f, h - 2f, item.Rotated);
                if (item.Quantity > 1 || (def.Kind == ItemKind.Medicine && def.Uses > 1))
                {
                    string count = def.Kind == ItemKind.Medicine && def.Uses > 1 ? (item.Quantity * def.Uses - item.UsesSpent).ToString() : item.Quantity.ToString();
                    var number = Label(tile.transform, count, cell < 35f ? 11 : 14, 1, 1, w - 4f, 20f, TextAnchor.UpperRight);
                    number.gameObject.AddComponent<Outline>().effectColor = Ink;
                }
                float grip = Mathf.Min(34f, cell * .73f);
                BindDrag(DragHandle(tile.transform, 1, h - grip - 1, grip), bag, id, drops);
            }
            int split = bag.Split(area);
            if (split > 0) Fill(At("Pocket divider", board, split * cell - 1.5f, 0, 3, rows * cell), new Color32(76, 101, 105, 255));
        }

        RectTransform DragHandle(Transform parent, float x, float y, float size)
        {
            var grip = At("Item drag handle", parent, x, y, size, size);
            Fill(grip, Color.clear, true);
            float mark = Mathf.Min(18f, size * .72f);
            var face = At("Drag grip", grip, 0, size - mark, mark, mark);
            Fill(face, new Color32(13, 20, 24, 190));
            for (int row = 0; row < 3; row++)
                Fill(At("Grip line", face, mark * .23f, mark * (.27f + row * .18f), mark * .54f, 1.3f), Muted);
            return grip;
        }

        void DrawEquipment(RectTransform root, GridInventory carried, EquipmentSlot slot, float x, float y, float w, float h)
        {
            var stack = carried.Equipped(slot);
            string id = stack?.InstanceId;
            var item = stack != null ? session.Catalog.Find(stack.ItemId) : null;
            var box = Button(root, "", x, y, w, h, () => { if (id != null) SelectItem(carried, id); }, false,
                selectedBag == carried && selectedId == id && id != null ? Color.Lerp(Plate, Aqua, .35f) : Plate);
            box.name = "Equipment slot / " + slot;
            box.gameObject.AddComponent<GearDropSlot>().Slot = slot;
            float textArea = slot == EquipmentSlot.Weapon ? w - 12f : w - Mathf.Min(78, w * .44f) - 8f;
            Label(box.transform, SlotName(slot), 17, 7, 3, textArea, 24, TextAnchor.MiddleLeft);
            if (item == null)
            {
                Label(box.transform, "空槽", 14, 7, slot == EquipmentSlot.Weapon ? 71 : 26, textArea, 22, TextAnchor.MiddleLeft, Muted);
                var empty = At("Empty equipment outline", box.transform, slot == EquipmentSlot.Weapon ? w * .27f : w - 59,
                    slot == EquipmentSlot.Weapon ? 29 : 7, slot == EquipmentSlot.Weapon ? w * .57f : 53, slot == EquipmentSlot.Weapon ? 54 : h - 14);
                empty.gameObject.AddComponent<GearOutline>().Initialize(slot, new Color32(114, 148, 150, 255));
                return;
            }
            if (item.Tier > 0) Fill(At("Grade", box.transform, 0, 0, 2, h), ItemDefinition.GradeColor(item.Tier));
            if (slot == EquipmentSlot.Weapon) DrawItemIcon(box.transform, item, stack, 11, 26, w - 22, 49);
            else DrawItemIcon(box.transform, item, stack, textArea + 8, 1, w - textArea - 22, h - 2);
            var caption = Label(box.transform, item.DisplayName, 14, 7, slot == EquipmentSlot.Weapon ? 75 : 27,
                slot == EquipmentSlot.Weapon ? w - 39 : textArea, 24, TextAnchor.MiddleLeft, Aqua);
            caption.resizeTextForBestFit = true;
            caption.resizeTextMinSize = 11;
            caption.resizeTextMaxSize = 14;
            BindDrag(DragHandle(box.transform, w - 26, h - 25, 24), carried, id, null);
        }

        static string SlotName(EquipmentSlot slot) => slot switch
        {
            EquipmentSlot.Weapon => "主武器", EquipmentSlot.Armor => "护甲", EquipmentSlot.Helmet => "头盔",
            EquipmentSlot.Rig => "弹挂", EquipmentSlot.Pack => "背包", _ => "物品"
        };

        void SelectItem(GridInventory bag, string id)
        {
            if (dragPointer != int.MinValue) return;
            var item = bag.Find(id);
            if (item == null) return;
            if (placing || inventoryScrollOwner != null) return;
            detailsRequested = true;
            selectedBag = bag;
            selectedId = id;
            rotated = item.Rotated;
            placing = false;
            RefreshInventory();
        }

        void CloseItemDetails()
        {
            if (dragPointer != int.MinValue) return;
            ClearSelection();
            RefreshInventory();
        }

        void ClearSelection()
        {
            selectedBag = null;
            selectedId = null;
            detailsRequested = placing = rotated = false;
        }

        void DrawPlacementControls()
        {
            Clear(inventoryMode);
            if (!placing) return;
            var toolbar = At("Select target cell", inventoryMode, 0, 0, 333, 42);
            Label(toolbar, "点选目标格", 15, 2, 0, 101, 42, TextAnchor.MiddleLeft, Amber);
            Button(toolbar, rotated ? "转回 0°" : "旋转 90°", 105, 0, 112, 42,
                () => { rotated = !rotated; DrawPlacementControls(); });
            Button(toolbar, "取消放置", 222, 0, 108, 42, () => { placing = false; RefreshInventory(); });
        }

        void PlaceAt(GridInventory bag, CargoArea area, int x, int y)
        {
            if (dragPointer != int.MinValue || inventoryScrollOwner != null || !placing || selectedBag == null || selectedId == null) return;
            MoveInventory(selectedBag, selectedId, bag, area, x, y, rotated);
        }

        static bool PutAt(GridInventory from, string id, GridInventory to, CargoArea area, int x, int y, bool turn, out string error)
        {
            if (from != to) return from.TryTransferAt(id, to, area, x, y, turn, out error);
            var item = from.Find(id);
            return item != null && item.Slot != EquipmentSlot.None
                ? from.TryUnequipAt(item.Slot, area, x, y, turn, out error) : from.TryMove(id, area, x, y, turn, out error);
        }

        void MoveInventory(GridInventory source, string id, GridInventory target, CargoArea area, int x, int y, bool rotate)
        {
            var item = source.Find(id);
            if (item != null && source == target && item.Slot == EquipmentSlot.None && item.Area == area
                && item.X == x && item.Y == y && item.Rotated == rotate)
            {
                if (placing) { placing = false; RefreshInventory(); }
                return;
            }
            bool ok = session.EditInventory(source, target, (GridInventory from, GridInventory to, out string reason) =>
                PutAt(from, id, to, area, x, y, rotate, out reason), out var error);
            if (ok)
            {
                detailsRequested &= selectedBag == source && selectedId == id;
                selectedBag = target; selectedId = id; placing = false; rotated = rotate;
            }
            else Notify(error ?? "无法放入目标格。");
            RefreshInventory();
        }

        void BindDrag(Transform node, GridInventory bag, string id, InventoryBoard board)
        {
            var slide = node.gameObject.AddComponent<InventoryDrag>();
            slide.Begin = e =>
            {
                if (dragPointer != int.MinValue || inventoryScrollOwner != null || EventSystem.current == null) return;
                var stack = bag.Find(id);
                if (stack == null) return;
                var item = session.Catalog.Find(stack.ItemId);
                dragPointer = e.pointerId;
                dragBag = bag;
                dragId = id;
                dragRotated = stack.Rotated;
                grabX = grabY = 0;
                if (board != null && board.TryCell(e.pressPosition, e.pressEventCamera, out int x, out int y))
                { grabX = x - stack.X; grabY = y - stack.Y; }
                if (placing && selectedBag == bag && selectedId == id && rotated != dragRotated) Turn(item);
                dropProbe = new PointerEventData(EventSystem.current);
                if (inventoryDetails != null) inventoryDetails.gameObject.SetActive(false);
                inventoryMode.gameObject.SetActive(false);
                rotateDragButton.gameObject.SetActive(true);
                dragGhost = Anchor("Dragged item", safe, .5f, .5f);
                dragGhost.SetAsLastSibling();
                Fill(dragGhost, new Color32(21, 35, 42, 220));
                DrawGhost();
                MoveGhost(e);
            };
            slide.Move = MoveGhost;
            slide.End = FinishDrag;
        }

        void Turn(ItemDefinition def)
        {
            (grabX, grabY) = dragRotated ? (grabY, def.Height - 1 - grabX) : (def.Height - 1 - grabY, grabX);
            dragRotated = !dragRotated;
        }

        void RotateDraggedItem()
        {
            var stack = dragBag?.Find(dragId);
            if (stack == null) return;
            Turn(session.Catalog.Find(stack.ItemId));
            DrawGhost();
            UpdateDropPreview();
        }

        void DrawGhost()
        {
            var stack = dragBag.Find(dragId);
            var item = session.Catalog.Find(stack.ItemId);
            if (ghostIcon != null)
            {
                itemIcons.Unbind(ghostIcon);
                ghostIcon.gameObject.SetActive(false);
                Destroy(ghostIcon.gameObject);
            }
            dragGhost.sizeDelta = new Vector2((dragRotated ? item.Height : item.Width) * 33f + 8f,
                (dragRotated ? item.Width : item.Height) * 33f + 8f);
            ghostIcon = DrawItemIcon(dragGhost, item, stack, 2, 2, dragGhost.sizeDelta.x - 4, dragGhost.sizeDelta.y - 4, dragRotated);
            PositionGhost();
        }

        void MoveGhost(PointerEventData e)
        {
            if (dragGhost == null || dragPointer != e.pointerId) return;
            dragScreenPoint = e.position;
            PositionGhost();
            UpdateDropPreview();
        }

        void PositionGhost()
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(safe, dragScreenPoint, null, out var local)) return;
            Vector2 target = local - safe.rect.center + new Vector2(dragGhost.sizeDelta.x * .40f + 13, dragGhost.sizeDelta.y * .50f + 16);
            Vector2 edges = Vector2.Max(Vector2.zero, (safe.rect.size - dragGhost.sizeDelta) * .5f);
            dragGhost.anchoredPosition = new Vector2(Mathf.Clamp(target.x, -edges.x, edges.x), Mathf.Clamp(target.y, -edges.y, edges.y));
        }

        void UpdateInventoryDrag()
        {
            if (dragPointer == int.MinValue) return;
            if (Keyboard.current?.rKey.wasPressedThisFrame ?? false) RotateDraggedItem();
            foreach (var scroll in inventoryScrollViews)
            {
                var view = scroll.viewport;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(view, dragScreenPoint, null, out var local)
                    || !view.rect.Contains(local)) continue;
                float band = Mathf.Min(38f, view.rect.height * .18f);
                float steer = Mathf.Clamp01(1f - (view.rect.yMax - local.y) / band)
                    - Mathf.Clamp01(1f - (local.y - view.rect.yMin) / band);
                float overflow = scroll.content.rect.height - view.rect.height;
                if (overflow > 1f && Mathf.Abs(steer) > .01f)
                    scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition + steer * 315f * Time.unscaledDeltaTime / overflow);
                break;
            }
            UpdateDropPreview();
        }

        void FindDrop(out InventoryBoard board, out GearDropSlot gear, out int x, out int y)
        {
            board = null; gear = null; x = y = -1;
            if (dropProbe == null || inventoryBody == null || EventSystem.current == null) return;
            dropProbe.position = dragScreenPoint;
            dropHits.Clear();
            EventSystem.current.RaycastAll(dropProbe, dropHits);
            if (dropHits.Count == 0) return;
            var hit = dropHits[0];
            dropHits.Clear();
            if (!hit.gameObject.transform.IsChildOf(inventoryBody)) return;
            gear = hit.gameObject.GetComponentInParent<GearDropSlot>();
            if (gear != null) return;
            board = hit.gameObject.GetComponentInParent<InventoryBoard>();
            if (board == null || !board.TryCell(dragScreenPoint, hit.module.eventCamera, out x, out y)) { board = null; return; }
            x -= grabX; y -= grabY;
        }

        void UpdateDropPreview()
        {
            FindDrop(out var board, out var gear, out int x, out int y);
            GridInventory target = board != null ? board.Bag : gear != null ? session.Loadout : null;
            var key = (x, y, dragRotated, dragBag.Revision, target?.Revision ?? 0);
            if (board == paintedBoard && gear == paintedGear && key == paintedPlacement) return;
            ClearDropPreview();
            paintedPlacement = key;
            var stack = dragBag.Find(dragId);
            if (target == null || stack == null) return;
            var from = dragBag.Draft();
            var to = target == dragBag ? from : target.Draft();
            if (board != null)
            {
                bool ok = PutAt(from, dragId, to, board.Area, x, y, dragRotated, out _);
                var def = session.Catalog.Find(stack.ItemId);
                board.Highlight(x, y, dragRotated ? def.Height : def.Width, dragRotated ? def.Width : def.Height, ok);
                paintedBoard = board;
            }
            else
            {
                paintedGear = gear;
                var targetGraphic = gear.GetComponent<Button>().targetGraphic;
                gearOriginal = targetGraphic.color;
                targetGraphic.color = Color.Lerp(gearOriginal, to.TryWearFrom(from, dragId, gear.Slot, out _) ? Aqua : MapDanger, .6f);
            }
        }

        void FinishDrag(PointerEventData e)
        {
            if (dragPointer != e.pointerId) return;
            dragScreenPoint = e.position;
            FindDrop(out var board, out var gear, out int x, out int y);
            GridInventory from = dragBag;
            string id = dragId;
            bool turn = dragRotated;
            CancelInventoryDrag();
            if (TouchControl.TouchWasCancelled(e)) return;
            if (gear != null)
            {
                if (session.EditInventory(from, session.Loadout, (GridInventory a, GridInventory b, out string why) => b.TryWearFrom(a, id, gear.Slot, out why), out var error))
                {
                    detailsRequested &= selectedBag == from && selectedId == id;
                    selectedBag = session.Loadout; selectedId = id; placing = false; rotated = selectedBag.Find(id)?.Rotated ?? false;
                }
                else Notify(error ?? "不能装备到该栏位。");
                RefreshInventory();
            }
            else if (board != null) MoveInventory(from, id, board.Bag, board.Area, x, y, turn);
        }

        void ClearDropPreview()
        {
            if (paintedBoard != null) paintedBoard.ClearHighlight();
            if (paintedGear != null) paintedGear.GetComponent<Button>().targetGraphic.color = gearOriginal;
            paintedBoard = null;
            paintedGear = null;
        }

        void CancelInventoryInput()
        {
            CancelInventoryDrag();
            if (inventoryScrollOwner != null) inventoryScrollOwner.Cancel();
        }

        void CancelInventoryDrag()
        {
            dragPointer = int.MinValue;
            dragId = null;
            dragBag = null;
            dropProbe = null;
            ClearDropPreview();
            if (rotateDragButton != null) rotateDragButton.gameObject.SetActive(false);
            if (inventoryMode != null) inventoryMode.gameObject.SetActive(true);
            if (inventoryDetails != null) inventoryDetails.gameObject.SetActive(true);
            if (dragGhost == null) return;
            if (ghostIcon != null) itemIcons.Unbind(ghostIcon);
            ghostIcon = null;
            dragGhost.gameObject.SetActive(false);
            Destroy(dragGhost.gameObject);
            dragGhost = null;
        }

        void ShowSelection(RectTransform root, bool stash)
        {
            var source = selectedBag;
            var item = source.Find(selectedId);
            string id = item.InstanceId;
            var def = session.Catalog.Find(item.ItemId);
            Fill(root, new Color32(16, 27, 33, 252), true);
            var border = def.Tier > 0 ? ItemDefinition.GradeColor(def.Tier) : Aqua;
            Fill(At("Details rule", root, 0, 0, 330, 2), border);
            DrawItemIcon(root, def, item, 10, 12, 63, 61, item.Rotated);
            Label(root, def.DisplayName, 20, 81, 10, 185, 60, TextAnchor.MiddleLeft);
            Button(root, "收起", 272, 9, 49, 35, CloseItemDetails);
            Label(root, ItemDetails(def, item), 15, 14, 75, 302, 76, color: Muted);
            float y = 154;
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
                ActionButton(root, "选格放置", ref y, () =>
                {
                    placing = true;
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
            root.sizeDelta = new Vector2(330, Mathf.Max(209f, y + 7));
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
            var image = ItemIcon(parent, "Item model / " + (item?.Id ?? blade.ToString()), ItemIconCache.Aspect(item), x, y, w, h, turn);
            itemIcons.Bind(image, item, stack, session.Catalog, blade);
            return image;
        }

        RawImage ItemIcon(Transform parent, string name, float aspect, float x, float y, float w, float h, bool turn)
        {
            var rect = Anchor(name, parent, 0, 1);
            rect.anchoredPosition = new Vector2(x + w * .5f, -y - h * .5f);
            rect.localRotation = Quaternion.Euler(0, 0, turn ? -90f : 0f);
            float fit = Mathf.Min((turn ? h : w) / aspect, turn ? w : h);
            rect.sizeDelta = new Vector2(aspect * fit, fit);
            var image = rect.gameObject.AddComponent<CachedItemImage>();
            image.raycastTarget = false;
            return image;
        }

        void ActionButton(Transform parent, string title, ref float y, System.Action action)
        {
            Button(parent, title, 13, y, 304, 36, () => { if (dragPointer == int.MinValue) action(); });
            y += 40;
        }
    }
}
