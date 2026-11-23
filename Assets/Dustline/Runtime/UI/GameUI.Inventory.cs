using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Dustline
{
    public sealed partial class GameUI
    {
        RectTransform inventoryBody, itemPopup, inventoryMore, inventoryMode, dragGhost;
        System.Action popupClosed;
        System.Action<int> jumpToSection;
        readonly Dictionary<(GridInventory bag, string id), RectTransform> itemAnchors = new Dictionary<(GridInventory, string), RectTransform>();
        Button rotateDragButton;
        RawImage ghostIcon;
        Text creditsReadout;
        LootContainer loot;
        GridInventory selectedBag, dragBag;
        string selectedId, dragId;
        bool detailsRequested, placing, rotated, dragRotated;
        float carryOffset, fieldCarryOffset, stashScroll = 1f, lootScroll = 1f;
        PlayerController lastInventoryPlayer;
        LootContainer lastLoot;
        int dragPointer = int.MinValue, grabX, grabY, placementStamp, rotatedByKeyFrame = -1;
        Vector2 dragScreenPoint;
        InventoryBoard paintedBoard;
        GearDropSlot paintedGear;
        Color gearOriginal;
        (int x, int y, bool turned, int from, int to) paintedPlacement;
        PointerEventData dropProbe;
        readonly List<InventoryScroll> inventoryScrollViews = new List<InventoryScroll>();
        readonly List<InventoryTile> inventoryTiles = new List<InventoryTile>();
        readonly List<InventoryBoard> inventoryBoards = new List<InventoryBoard>();
        readonly Vector3[] popupCorners = new Vector3[4];
        InventoryScroll inventoryScrollOwner, carriedView, storageView;
        readonly List<RaycastResult> dropHits = new List<RaycastResult>();
        ItemIconCache itemIcons;

        void ResetInventoryView()
        {
            CaptureInventoryScroll();
            CancelInventoryInput();
            carriedView = storageView = null;
            inventoryScrollViews.Clear();
            inventoryTiles.Clear();
            inventoryBoards.Clear();
            RemovePopup();
            CloseInventoryMore();
            inventoryBody = inventoryMode = null;
            rotateDragButton = null;
            creditsReadout = null;
            loot = null;
            jumpToSection = null;
            itemAnchors.Clear();
            ClearSelection();
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
            else if (InventoryOpen) BackOrSettings();
        }

        public void OpenLoot(LootContainer container)
        {
            if (session.Phase == SessionPhase.Raid && current == ScreenPage.Field && container != null && container.Searched)
                ShowFieldInventory(container);
        }

        void ShowFieldInventory(LootContainer container)
        {
            if (lastInventoryPlayer != session.Player)
            {
                lastInventoryPlayer = session.Player;
                fieldCarryOffset = 0;
                lastLoot = null;
                lootScroll = 1f;
            }
            if (lastLoot != container) lootScroll = 1f;
            lastLoot = container;
            var root = Display(ScreenPage.FieldInventory, "Field inventory", wide: true);
            loot = container;
            reflowWidePage = () => BuildInventoryPage(root, false);
            BuildInventoryPage(root, false);
        }

        void BuildInventoryPage(RectTransform root, bool stash)
        {
            CaptureInventoryScroll();
            CancelInventoryInput();
            carriedView = storageView = null;
            RemovePopup();
            CloseInventoryMore();
            Clear(root);
            float w = root.rect.width;
            string title = stash ? "仓库" : loot == null ? "战地背包" : loot.DisplayName;
            Fill(At("Inventory heading", root, 0, 0, w, 49), Ink);
            Fill(At("Inventory heading line", root, 0, 0, w, 2), Border);
            AddMenuBack(root);
            Label(root, title, 24, 116, 4, 256, 40, TextAnchor.MiddleLeft);
            inventoryBody = At(stash ? "Stash contents" : "Field inventory contents", root, 8, 53, w - 16, 620);
            Button(root, "更多", w - (stash ? 245 : 90), 6, 76, 35, () => ToggleInventoryMore(root, stash));
            if (stash)
            {
                creditsReadout = Label(root, "", 19, w - 164, 4, 150, 40, TextAnchor.MiddleRight, Aqua);
                UpdateCredits();
            }
            inventoryMode = At("Placement controls", root, w * .5f - 166f, 4, 333, 41);
            RefreshInventory();
        }

        public void RefreshInventory()
        {
            bool stash = current == ScreenPage.Stash && session.Phase == SessionPhase.Menu;
            if (inventoryBody == null || dragPointer != int.MinValue || (!stash && (!InventoryOpen || session.Phase != SessionPhase.Raid))) return;
            UpdateCredits();
            if (selectedBag != null && selectedBag.Find(selectedId) == null) ClearSelection();
            CaptureInventoryScroll();
            CancelInventoryInput();
            carriedView = storageView = null;
            inventoryScrollViews.Clear();
            inventoryTiles.Clear();
            inventoryBoards.Clear();
            itemAnchors.Clear();
            RemovePopup();
            Clear(inventoryBody);
            float width = inventoryBody.rect.width, h = inventoryBody.rect.height;
            const float railWidth = 56f, gap = 7f;
            float left = Mathf.Floor((width - railWidth - 2 * gap) * .43f), right = width - railWidth - 2 * gap - left;
            var carried = session.Loadout;
            var rail = InventoryPlate("Equipment categories", 0, railWidth, h);
            var gear = InventoryPlate("Carried gear", railWidth + gap, left, h);
            var storage = InventoryPlate(stash ? "Stash grid region" : "Loot grid region", railWidth + gap + left + gap, right, h);
            DrawCarried(rail, gear, carried, left, h, stash);

            var target = stash ? session.Stash : loot?.Inventory;
            Label(storage, stash ? "仓库储备" : loot == null ? "搜刮区" : "容器物资", 20, 9, 2, right * .48f, 32, TextAnchor.MiddleLeft);
            if (stash) Button(storage, "一键整理", right - 113, 3, 105, 31, () =>
            {
                if (!InventoryIdle() || ProcessInventoryBackKey()) return;
                CloseItemPopup();
                if (session.SortStash(out var error)) { RefreshInventory(); Notify("仓库已整理。"); }
                else Notify(error ?? "未能保存整理结果。");
            });
            if (target != null)
            {
                if (!stash && target.Items.Count == 0)
                    Label(storage, "已搜空", 17, 14, h - 39, right - 28, 31, TextAnchor.MiddleRight, Muted);
                float cell = Mathf.Min((right - 27f) / target.Width, stash ? 104f : 62f);
                float height = cell * target.Height;
                float leftInset = stash ? 6f : Mathf.Max(6f, Mathf.Floor((right - cell * target.Width - 15f) * .5f));
                var viewport = VerticalInventory(storage, leftInset, 37, right - leftInset - 6f, h - 42f, height,
                    stash ? stashScroll : lootScroll, value => { if (stash) stashScroll = value; else lootScroll = value; }, out storageView);
                DrawGrid(viewport, target, CargoArea.Main, 0, 0, cell);
            }
            else Label(storage, "对准容器并搜索后，可在这里查看和转移物品。", 19, 14, 53, right - 28, 70, color: Muted);
            DrawPlacementControls();
            if (detailsRequested && !placing && selectedBag != null && itemAnchors.TryGetValue((selectedBag, selectedId), out var anchor))
                OpenSelection(anchor, stash);
        }

        void CaptureInventoryScroll()
        {
            if (current != ScreenPage.Stash && current != ScreenPage.FieldInventory) return;
            bool stash = current == ScreenPage.Stash;
            if (carriedView != null && carriedView.isActiveAndEnabled)
            {
                float offset = Mathf.Max(0, carriedView.content.anchoredPosition.y);
                if (stash) carryOffset = offset; else fieldCarryOffset = offset;
            }
            if (storageView != null && storageView.isActiveAndEnabled)
            {
                float overflow = storageView.content.rect.height - storageView.viewport.rect.height;
                if (overflow <= .01f) return;
                float position = 1f - Mathf.Clamp01(storageView.content.anchoredPosition.y / overflow);
                if (stash) stashScroll = position; else lootScroll = position;
            }
        }

        RectTransform InventoryPlate(string title, float x, float width, float h)
        {
            var rect = At(title, inventoryBody, x, 0, width, h);
            Fill(rect, Ink);
            Fill(At("Section line", rect, 0, 0, width, 2), Border);
            return rect;
        }

        void DrawCarried(RectTransform rail, RectTransform root, GridInventory carried, float width, float h, bool stash)
        {
            const float head = 30f, gutter = 6f, trackSpace = 15f;
            float inner = width - 10f - trackSpace, iconWidth = Mathf.Min(118f, inner * .27f);
            float gearRow = (h - 48f - 3 * gutter) / 3f;
            float guardTop = 42f + 2 * (gearRow + gutter);
            bool legacy = carried.OccupiedCells(CargoArea.Legacy) > 0;
            var sections = new[] { "武器", "防具", "弹挂", "口袋", "背包", "迁移物品" };
            var symbols = new[] { EquipmentSlot.Weapon, EquipmentSlot.Armor, EquipmentSlot.Rig, EquipmentSlot.None, EquipmentSlot.Pack, EquipmentSlot.Pack };
            var area = new[] { CargoArea.Rig, CargoArea.Pockets, CargoArea.Main, CargoArea.Legacy };
            var cells = new float[area.Length];
            var heights = new float[area.Length];
            var tops = new float[6];
            tops[1] = guardTop;
            float end = h;
            int count = legacy ? 6 : 5;
            for (int i = 0; i < count - 2; i++)
            {
                var shape = carried.Layout(area[i]);
                bool hasGear = i == 0 || i == 2;
                float free = inner - 2 * gutter - (hasGear ? iconWidth + gutter : 0f);
                cells[i] = Mathf.Min(hasGear ? 52f : 58f, free / Mathf.Max(1, shape.Columns));
                heights[i] = head + Mathf.Max(hasGear ? 88f : 0f, shape.Rows * cells[i]) + gutter;
                tops[i + 2] = end;
                end += heights[i];
            }
            float overflow = Mathf.Max(end - h, tops[count - 1]);
            var markers = new Image[count];
            float saved = Mathf.Clamp(stash ? carryOffset : fieldCarryOffset, 0f, overflow);
            var content = VerticalInventory(root, 5, 0, width - 10, h, h + overflow, 1f - saved / overflow,
                value => Track((1f - value) * overflow), out carriedView);
            var scroll = carriedView;
            var hands = At("Weapon equipment", content, 0, 0, inner, guardTop);
            Label(hands, "装备价值 " + carried.EquipmentValue, 17, 7, 3, inner * .46f, 36, TextAnchor.MiddleLeft, Aqua);
            Label(hands, "负重 " + carried.Weight.ToString("0.0") + " / " + carried.MaxWeight.ToString("0.#") + " 千克",
                15, inner * .46f, 3, inner * .54f - 7, 36, TextAnchor.MiddleRight, Muted);
            float longSlot = (inner - gutter) * (2f / 3f), shortSlot = inner - longSlot - gutter;
            DrawEquipment(hands, carried, EquipmentSlot.Weapon, 0, 42, longSlot, gearRow);
            DrawEquipment(hands, carried, EquipmentSlot.Sidearm, longSlot + gutter, 42, shortSlot, gearRow);
            DrawEquipment(hands, carried, EquipmentSlot.PrimaryTwo, 0, 42 + gearRow + gutter, longSlot, gearRow);
            DrawEquipment(hands, carried, EquipmentSlot.Melee, longSlot + gutter, 42 + gearRow + gutter, shortSlot, gearRow);
            float half = (inner - gutter) * .5f;
            DrawEquipment(content, carried, EquipmentSlot.Helmet, 0, guardTop, half, gearRow);
            DrawEquipment(content, carried, EquipmentSlot.Armor, half + gutter, guardTop, half, gearRow);
            for (int i = 0; i < count - 2; i++)
            {
                var section = At("Carried section / " + area[i], content, 0, tops[i + 2], inner, heights[i]);
                Fill(At("Container heading", section, 0, 0, inner, head - 3f), Surface);
                var shape = carried.Layout(area[i]);
                string name = sections[i + 2];
                string title = area[i] == CargoArea.Legacy ? name + " · 只可移出"
                    : name + " " + carried.OccupiedCells(area[i]) + "/" + shape.Capacity;
                var caption = Label(section, title, 18, 8, 0, inner - 16, head - 3f, TextAnchor.MiddleLeft, Aqua);
                caption.name = "Capacity / " + area[i];
                if (area[i] == CargoArea.Legacy)
                    Label(section, "不带入突袭", 14, 8, 0, inner - 16, head - 3f, TextAnchor.MiddleRight, Muted);
                var gearSlot = i == 0 ? EquipmentSlot.Rig : i == 2 ? EquipmentSlot.Pack : EquipmentSlot.None;
                float x = gutter;
                if (gearSlot != EquipmentSlot.None)
                {
                    DrawEquipment(section, carried, gearSlot, 0, head, iconWidth, Mathf.Max(88f, shape.Rows * cells[i]));
                    x = iconWidth + gutter;
                }
                if (shape.Columns > 0) DrawGrid(section, carried, area[i], x, head, cells[i]);
                else Label(section, "拖入对应装备", 15, x, head, inner - x, 82, TextAnchor.MiddleCenter, Muted);
            }

            for (int i = 0; i < count; i++)
            {
                int sectionIndex = i;
                var tab = Button(rail, "", 3, 8 + i * 61, 50, 55, () => jumpToSection?.Invoke(sectionIndex));
                tab.name = "Category / " + sections[i];
                var tint = new Color32(150, 177, 175, 255);
                if (symbols[i] != EquipmentSlot.None)
                    DrawSilhouette(tab.transform, symbols[i], 9, 7, 32, 24, tint, "Category icon");
                else
                    for (int x = 0; x < 3; x++) Fill(At("Pocket symbol", tab.transform, 9 + x * 11, 14, 8, 8), tint);
                Label(tab.transform, sections[i], i == 5 ? 11 : 13, 1, 33, 48, 16, TextAnchor.MiddleCenter, Muted);
                markers[i] = Fill(At("Category selection", tab.transform, 0, 0, 2, 55), Aqua);
            }
            jumpToSection = index =>
            {
                if (!InventoryIdle() || index < 0 || index >= count) return;
                CloseItemPopup();
                CloseInventoryMore();
                scroll.StopMovement();
                content.anchoredPosition = new Vector2(0, tops[index]);
                Track(tops[index]);
            };
            Track(saved);

            void Track(float offset)
            {
                if (stash) carryOffset = offset; else fieldCarryOffset = offset;
                int active = 0;
                for (int i = 1; i < count; i++) if (offset + 2f >= tops[i]) active = i;
                for (int i = 0; i < count; i++) if (markers[i] != null) markers[i].enabled = i == active;
            }
        }

        RectTransform VerticalInventory(Transform parent, float x, float y, float w, float h, float contentHeight, float position,
            System.Action<float> changed, out InventoryScroll view)
        {
            const float trackWidth = 11f;
            view = null;
            var viewport = At("Grid viewport", parent, x, y, w - trackWidth - 4, h);
            Fill(viewport, Color.clear, true);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = At("Grid content", viewport, 0, 0, w - trackWidth - 4, contentHeight);
            if (contentHeight <= h + .01f) return content;
            var scroll = viewport.gameObject.AddComponent<InventoryScroll>();
            view = scroll;
            inventoryScrollViews.Add(scroll);
            scroll.TryAcquire = source =>
            {
                if (dragPointer != int.MinValue || inventoryScrollOwner != null) return false;
                inventoryScrollOwner = source;
                if (!placing) CloseItemPopup();
                CloseInventoryMore();
                return true;
            };
            scroll.Released = source => { if (inventoryScrollOwner == source) inventoryScrollOwner = null; };
            scroll.AllowScroll = InventoryIdle;
            scroll.WheelScroll = () =>
            {
                if (!placing) CloseItemPopup();
                CloseInventoryMore();
            };
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
            scroll.onValueChanged.AddListener(value =>
            {
                float normalized = Mathf.Clamp01(value.y);
                ShowPosition(normalized);
                changed(normalized);
            });
            void ShowPosition(float value) => thumb.anchoredPosition = new Vector2(2, -2 - (h - 4f - thumbHeight) * (1 - Mathf.Clamp01(value)));
            return content;
        }

        void DrawGrid(Transform root, GridInventory bag, CargoArea area, float left, float top, float cell)
        {
            var layout = bag.Layout(area);
            var board = At("Item grid", root, left, top, layout.Columns * cell, layout.Rows * cell);
            board.gameObject.AddComponent<CanvasRenderer>();
            var drops = board.gameObject.AddComponent<InventoryBoard>();
            drops.Bag = bag;
            drops.Area = area;
            drops.Initialize(layout, cell, () => placing && InventoryIdle() ? placementStamp : -1, (x, y) => PlaceAt(bag, area, x, y));
            inventoryBoards.Add(drops);
            foreach (var item in bag.Items)
            {
                if (item.Slot != EquipmentSlot.None || item.Area != area) continue;
                var def = session.Catalog.Find(item.ItemId);
                float w = (item.Rotated ? def.Height : def.Width) * cell - 2,
                    h = (item.Rotated ? def.Width : def.Height) * cell - 2;
                string id = item.InstanceId;
                var grade = ItemDefinition.GradeColor(def.Tier);
                float margin = layout.Pockets != null && layout.Pockets.Length > 0 || layout.Split > 0 ? 3f : 1f;
                w -= 2f * (margin - 1f); h -= 2f * (margin - 1f);
                var tileRect = At(def.DisplayName, board, item.X * cell + margin, item.Y * cell + margin, w, h);
                var tile = CreateItemTile(tileRect, def.Tier > 0 ? Color.Lerp(Plate, grade, .15f) : Plate);
                itemAnchors[(bag, id)] = tileRect;
                tile.onClick.AddListener(() => SelectItem(bag, id));
                tile.Place = e =>
                {
                    if (!placing) return false;
                    if (drops.TryCell(e.position, e.pressEventCamera, out int x, out int y)) PlaceAt(bag, area, x, y);
                    return true;
                };
                if (def.Tier > 0) Fill(At("Grade", tileRect, 0, 0, 2, h), grade);
                float modelInset = Mathf.Min(11f, h * .22f);
                DrawItemIcon(tileRect, def, item, 2, modelInset, w - 4f, h - 2f * modelInset, item.Rotated);
                TileCaption(tileRect, def.DisplayName, "Item name", 3, 2, w - 6, 13, TextAnchor.UpperLeft, 12);
                string badge = ItemBadge(def, item);
                if (!string.IsNullOrEmpty(badge)) TileCaption(tileRect, badge, "Item readout", 2, h - 14, w - 5, 13, TextAnchor.LowerRight, 12);
                BindDrag(tile, bag, id, drops);
            }
        }

        InventoryTile CreateItemTile(RectTransform rect, Color tint)
        {
            var tile = rect.gameObject.AddComponent<InventoryTile>();
            tile.targetGraphic = Fill(rect, tint, true);
            tile.navigation = new Navigation { mode = Navigation.Mode.None };
            StyleButton(tile, false, tint);
            tile.CanPress = InventoryIdle;
            tile.SelectionStamp = () => placing ? placementStamp : -1;
            inventoryTiles.Add(tile);
            return tile;
        }

        Text TileCaption(Transform root, string value, string name, float x, float y, float w, float h, TextAnchor side, int size)
        {
            var label = Label(root, value, size, x, y, w, h, side);
            label.name = name;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 9;
            label.resizeTextMaxSize = size;
            label.gameObject.AddComponent<Outline>().effectColor = Ink;
            return label;
        }

        string ItemBadge(ItemDefinition item, ItemStack stack)
        {
            if (item.Durability > 0) return (item.Durability - stack.Wear) + "/" + item.Durability;
            if (item.Weapon != null && !item.Weapon.Melee)
                return Mathf.Max(0, stack.LoadedAmmo) + "/" + WeaponRules.Capacity(item.Weapon, stack, session.Catalog);
            if (item.Kind == ItemKind.Medicine) return ((long)stack.Quantity * item.Uses - stack.UsesSpent) + " 次";
            return item.MaxStack > 1 ? "×" + stack.Quantity : "";
        }

        void DrawEquipment(RectTransform root, GridInventory carried, EquipmentSlot slot, float x, float y, float w, float h)
        {
            var stack = carried.Equipped(slot);
            string id = stack?.InstanceId;
            var item = stack != null ? session.Catalog.Find(stack.ItemId) : null;
            var box = At("Equipment slot / " + slot, root, x, y, w, h);
            var tile = CreateItemTile(box, new Color32(22, 32, 38, 255));
            box.gameObject.AddComponent<GearDropSlot>().Slot = slot;
            Label(box, SlotName(slot), 16, 7, 3, w - 14, 23, TextAnchor.MiddleLeft, Muted);
            var blade = slot == EquipmentSlot.Melee && item == null ? session.Blades?.Resolve(session.Profile?.BoundBladeId) : null;
            if (item == null && blade == null)
            {
                float silhouetteWidth = w * .68f, silhouetteHeight = Mathf.Min(h * .60f, silhouetteWidth * .8f);
                DrawSilhouette(box, slot, (w - silhouetteWidth) * .5f, (h - silhouetteHeight) * .5f,
                    silhouetteWidth, silhouetteHeight, new Color32(47, 64, 69, 255), "Empty equipment outline");
                return;
            }
            if (item != null && item.Tier > 0) Fill(At("Grade", box, 0, 0, 2, h), ItemDefinition.GradeColor(item.Tier));
            DrawItemIcon(box, item, stack, 5, 26, w - 10, h - 49, blade: blade != null ? blade.Form : WeaponForm.Tangdao);
            TileCaption(box, item != null ? item.DisplayName : session.BoundBladeName, "Item name", 6, h - 22,
                item != null && (item.Durability > 0 || item.Weapon != null && !item.Weapon.Melee) ? w * .69f : w - 12,
                18, TextAnchor.MiddleLeft, 13);
            if (item == null)
            {
                tile.onClick.AddListener(() =>
                {
                    if (InventoryIdle() && !placing)
                    {
                        CloseInventoryMore();
                        ItemPopup(panelRoot, box, null, null, null, blade);
                    }
                });
                return;
            }
            string badge = ItemBadge(item, stack);
            if (!string.IsNullOrEmpty(badge)) TileCaption(box, badge, "Item readout", w * .63f, h - 21, w * .37f - 5, 17, TextAnchor.MiddleRight, 12);
            itemAnchors[(carried, id)] = box;
            tile.onClick.AddListener(() => SelectItem(carried, id));
            BindDrag(tile, carried, id, null);
        }

        static void DrawSilhouette(Transform parent, EquipmentSlot slot, float x, float y, float w, float h, Color tint, string name)
        {
            var rect = At(name, parent, x, y, w, h);
            rect.gameObject.AddComponent<CanvasRenderer>();
            rect.gameObject.AddComponent<GearOutline>().Initialize(slot, tint);
        }

        static string SlotName(EquipmentSlot slot) => slot switch
        {
            EquipmentSlot.Weapon => "主武器 1", EquipmentSlot.PrimaryTwo => "主武器 2", EquipmentSlot.Sidearm => "副武器", EquipmentSlot.Melee => "近战武器",
            EquipmentSlot.Armor => "护甲", EquipmentSlot.Helmet => "头盔", EquipmentSlot.Rig => "弹挂", EquipmentSlot.Pack => "背包", _ => "物品"
        };

        bool InventoryIdle() => dragPointer == int.MinValue && inventoryScrollOwner == null;

        void SelectItem(GridInventory bag, string id)
        {
            if (!InventoryIdle() || ProcessInventoryBackKey() || placing || !itemAnchors.TryGetValue((bag, id), out var anchor)) return;
            var item = bag.Find(id);
            if (item == null) return;
            CloseInventoryMore();
            CloseItemPopup();
            detailsRequested = true;
            selectedBag = bag;
            selectedId = id;
            rotated = item.Rotated;
            OpenSelection(anchor, current == ScreenPage.Stash);
        }

        bool CloseInventoryOverlay()
        {
            if (CloseItemPopup()) return true;
            if (CloseInventoryMore()) return true;
            if (dragPointer != int.MinValue) { CancelInventoryInput(); return true; }
            if (!placing) return false;
            ClearSelection();
            DrawPlacementControls();
            return true;
        }

        void ToggleInventoryMore(RectTransform root, bool stash)
        {
            if (!InventoryIdle() || ProcessInventoryBackKey() || CloseInventoryMore()) return;
            CloseItemPopup();
            float width = root.rect.width;
            inventoryMore = At("Inventory more", root, width - (stash ? 340 : 198), 48, 190, stash ? 92 : 134);
            Fill(inventoryMore, Ink, true);
            float y = 6;
            if (stash)
            {
                Add("补给商店", () => ShowShop());
                Add("应急装备", session.EmergencyKit);
            }
            else
            {
                Add("分部位健康", () => ShowHealth());
                Add("设置", () => ShowSettings(true));
                Add("切换射击模式", () =>
                {
                    var weapon = session.Player?.Weapon;
                    if (weapon?.Magazine == null) { Notify("当前武器不能切换射击模式。"); return; }
                    weapon.ToggleFireMode();
                    Notify(weapon.Automatic ? "已切换为全自动" : "已切换为半自动");
                });
            }

            void Add(string title, System.Action action)
            {
                var menu = inventoryMore;
                Button(menu, title, 6, y, 178, 37, () =>
                {
                    if (inventoryMore != menu || !InventoryIdle() || ProcessInventoryBackKey()) return;
                    CloseInventoryMore();
                    action();
                });
                y += 42;
            }
        }

        bool CloseInventoryMore()
        {
            if (inventoryMore == null) return false;
            inventoryMore.gameObject.SetActive(false);
            Destroy(inventoryMore.gameObject);
            inventoryMore = null;
            return true;
        }

        bool CloseItemPopup()
        {
            if (itemPopup == null) return false;
            var closed = popupClosed;
            RemovePopup();
            closed?.Invoke();
            return true;
        }

        void RemovePopup()
        {
            if (itemPopup != null)
            {
                itemPopup.gameObject.SetActive(false);
                Destroy(itemPopup.gameObject);
            }
            itemPopup = null;
            popupClosed = null;
        }

        RectTransform ItemPopup(RectTransform panel, RectTransform anchor, ItemDefinition def, ItemStack stack, System.Action closed, WeaponDefinition blade = null)
        {
            CloseItemPopup();
            itemPopup = At("Item details", panel, 0, 0, 330, 216);
            popupClosed = closed;
            Fill(itemPopup, new Color32(16, 27, 33, 252), true);
            var border = def != null && def.Tier > 0 ? ItemDefinition.GradeColor(def.Tier) : Aqua;
            Fill(At("Details rule", itemPopup, 0, 0, 330, 2), border);
            DrawItemIcon(itemPopup, def, stack, 8, 11, 87, 61, blade: blade != null ? blade.Form : WeaponForm.Tangdao);
            Label(itemPopup, def != null ? def.DisplayName : session.BoundBladeName, 19, 99, 12, 166, 57, TextAnchor.MiddleLeft);
            var popup = itemPopup;
            Button(popup, "收起", 272, 9, 49, 35, () => { if (itemPopup == popup) BackOrSettings(); });
            Label(itemPopup, def != null ? ItemDetails(def, stack) : "绑定近战 · 不占储物格\n随身携带，不因阵亡丢失。", 15,
                14, 76, 302, 84, color: Muted);
            if (def == null) itemPopup.sizeDelta = new Vector2(330, 160);
            PlaceItemPopup(itemPopup, panel, anchor);
            return itemPopup;
        }

        void PlaceItemPopup(RectTransform popup, RectTransform panel, RectTransform anchor)
        {
            anchor.GetWorldCorners(popupCorners);
            var a = panel.InverseTransformPoint(popupCorners[0]);
            var b = panel.InverseTransformPoint(popupCorners[2]);
            var frame = panel.rect;
            const float edge = 8f;
            float width = popup.rect.width, height = popup.rect.height;
            float right = b.x + edge, left = a.x - edge - width;
            float x = right + width <= frame.xMax - edge ? right : left >= frame.xMin + edge ? left
                : frame.xMax - b.x >= a.x - frame.xMin ? right : left;
            x = Mathf.Clamp(x, frame.xMin + edge, frame.xMax - width - edge);
            float y = Mathf.Clamp(b.y, frame.yMin + height + edge, frame.yMax - 50f);
            popup.anchoredPosition = new Vector2(x - frame.xMin, y - frame.yMax);
        }

        void OpenSelection(RectTransform anchor, bool stash)
        {
            var stack = selectedBag.Find(selectedId);
            var def = session.Catalog.Find(stack.ItemId);
            var popup = ItemPopup(panelRoot, anchor, def, stack, ClearSelection);
            ShowSelection(popup, stash);
            PlaceItemPopup(popup, panelRoot, anchor);
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
                () => { rotated = !rotated; placementStamp++; DrawPlacementControls(); });
            Button(toolbar, "取消放置", 222, 0, 108, 42, () => { placing = false; RefreshInventory(); });
        }

        void PlaceAt(GridInventory bag, CargoArea area, int x, int y)
        {
            if (!InventoryIdle() || !placing || selectedBag == null || selectedId == null || ProcessInventoryBackKey()) return;
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

        void BindDrag(InventoryTile slide, GridInventory bag, string id, InventoryBoard board)
        {
            slide.Begin = (pointer, origin, eventCamera, position) =>
            {
                if (!InventoryIdle() || EventSystem.current == null) return false;
                var stack = bag.Find(id);
                if (stack == null) return false;
                var item = session.Catalog.Find(stack.ItemId);
                dragPointer = pointer;
                dragScreenPoint = position;
                dragBag = bag;
                dragId = id;
                dragRotated = stack.Rotated;
                grabX = grabY = 0;
                if (board != null && board.TryCell(origin, eventCamera, out int x, out int y))
                {
                    grabX = Mathf.Clamp(x - stack.X, 0, (dragRotated ? item.Height : item.Width) - 1);
                    grabY = Mathf.Clamp(y - stack.Y, 0, (dragRotated ? item.Width : item.Height) - 1);
                }
                if (placing && selectedBag == bag && selectedId == id && rotated != dragRotated) Turn(item);
                dropProbe = new PointerEventData(EventSystem.current);
                CloseInventoryMore();
                CloseItemPopup();
                ClearSelection();
                DrawPlacementControls();
                inventoryMode.gameObject.SetActive(false);
                Button rotate = null;
                rotate = Button(panelRoot, "旋转物品 90°", panelRoot.rect.width * .5f - 76, 5, 152, 38, () =>
                {
                    if (rotateDragButton == rotate) RotateDraggedItem();
                }, true);
                rotateDragButton = rotate;
                dragGhost = Anchor("Dragged item", safe, .5f, .5f);
                dragGhost.SetAsLastSibling();
                Fill(dragGhost, new Color32(21, 35, 42, 220));
                DrawGhost();
                UpdateDropPreview();
                return true;
            };
            slide.Move = MoveGhost;
            slide.End = FinishDrag;
            slide.Cancel = pointer => { if (pointer == dragPointer) CancelInventoryDrag(); };
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
            RotateFromKeyboard();
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

        void RotateFromKeyboard()
        {
            if (rotatedByKeyFrame == Time.frameCount || Keyboard.current?.rKey.wasPressedThisFrame != true) return;
            rotatedByKeyFrame = Time.frameCount;
            RotateDraggedItem();
        }

        bool ProcessInventoryBackKey()
        {
            if (!PlayerInputReader.BackPressed() && !(InventoryOpen && Keyboard.current?.tabKey.wasPressedThisFrame == true)) return false;
            BackOrSettings();
            return true;
        }

        void FinishDrag(PointerEventData e)
        {
            if (dragPointer != e.pointerId || ProcessInventoryBackKey()) return;
            if (TouchControl.TouchWasCancelled(e)) { CancelInventoryDrag(); return; }
            dragScreenPoint = e.position;
            RotateFromKeyboard();
            FindDrop(out var board, out var gear, out int x, out int y);
            GridInventory from = dragBag;
            string id = dragId;
            bool turn = dragRotated;
            CancelInventoryDrag();
            if (gear != null) EquipInventoryItem(from, id, gear.Slot);
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
            foreach (var tile in inventoryTiles) if (tile != null) tile.CancelGesture();
            foreach (var board in inventoryBoards) if (board != null) board.CancelPress();
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
            if (rotateDragButton != null)
            {
                var retired = rotateDragButton;
                rotateDragButton = null;
                retired.gameObject.SetActive(false);
                Destroy(retired.gameObject);
            }
            if (inventoryMode != null) inventoryMode.gameObject.SetActive(true);
            if (dragGhost == null) return;
            if (ghostIcon != null) itemIcons.Unbind(ghostIcon);
            ghostIcon = null;
            dragGhost.gameObject.SetActive(false);
            Destroy(dragGhost.gameObject);
            dragGhost = null;
        }

        void EquipInventoryItem(GridInventory source, string id, EquipmentSlot slot)
        {
            if (!InventoryIdle() || source == session.Loadout && source.Find(id)?.Slot == slot) return;
            if (!session.EditInventory(source, session.Loadout, (GridInventory from, GridInventory to, out string why) =>
                to.TryWearFrom(from, id, slot, out why), out var error))
            {
                Notify(error ?? "不能装备到该栏位。");
                return;
            }
            CloseItemPopup();
            ClearSelection();
            RefreshInventory();
            if (slot == EquipmentSlot.Rig) jumpToSection?.Invoke(2);
            else if (slot == EquipmentSlot.Pack) jumpToSection?.Invoke(4);
        }

        void ShowSelection(RectTransform root, bool stash)
        {
            var source = selectedBag;
            var item = source.Find(selectedId);
            string id = item.InstanceId;
            var def = session.Catalog.Find(item.ItemId);
            float y = 164;
            var target = stash ? (source == session.Stash ? session.Loadout : session.Stash)
                : loot == null ? null : source == loot.Inventory ? session.Loadout : loot.Inventory;
            if (target != null)
                ActionButton(root, target == session.Loadout ? "转入随身栏" : stash ? "转入仓库" : "放入容器", ref y, () =>
                {
                    if (!session.EditInventory(source, target, (GridInventory from, GridInventory to, out string e) => from.TryTransferTo(id, to, out e), out var error))
                    { Notify(error ?? "无法转移物品。"); return; }
                    CloseItemPopup();
                    RefreshInventory();
                });
            ActionButton(root, "选格放置", ref y, () => StartPlacement(item.Rotated));
            if (item.Slot == EquipmentSlot.None && item.Area != CargoArea.Legacy && def.Width != def.Height)
                ActionButton(root, rotated ? "旋转到 0°" : "旋转到 90°", ref y, () =>
                {
                    bool turn = !item.Rotated;
                    if (!session.EditInventory(source, source, (GridInventory from, GridInventory to, out string e) =>
                        from.TryMove(id, item.Area, item.X, item.Y, turn, out e), out _))
                    {
                        StartPlacement(turn);
                        Notify("请选择可放下旋转物品的格子。");
                        return;
                    }
                    rotated = turn;
                    RefreshInventory();
                });
            if (item.Slot != EquipmentSlot.None)
                ActionButton(root, "卸下装备", ref y, () =>
                {
                    if (!session.EditInventory(source, source, (GridInventory from, GridInventory to, out string e) =>
                        from.TryUnequip(item.Slot, out e), out var error)) { Notify(error ?? "无法卸下装备。"); return; }
                    CloseItemPopup();
                    RefreshInventory();
                });
            else if (def.Equipment != EquipmentSlot.None)
            {
                var slot = def.Equipment;
                ActionButton(root, "装备到" + SlotName(slot), ref y, () => EquipInventoryItem(source, id, slot));
                if (slot == EquipmentSlot.Weapon)
                    ActionButton(root, "装备到主武器 2", ref y, () => EquipInventoryItem(source, id, EquipmentSlot.PrimaryTwo));
            }
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
                foreach (var slot in LoadoutSlots.Hands)
                {
                    var gun = session.Loadout.Equipped(slot);
                    if (!WeaponRules.CanLoad(session.Catalog.Find(gun?.ItemId)?.Weapon, gun, def, out _)) continue;
                    ActionButton(root, "装填至" + SlotName(slot), ref y, () =>
                    {
                        if (!session.TryLoadAmmunition(source, id, slot, out var error)) Notify(error ?? "装填失败。");
                        RefreshInventory();
                    });
                }
            if (stash)
            {
                string sale = (!item.Parts.Empty || item.LoadedAmmo > 0) ? "出售枪身 · 退回配件和弹药" : "出售";
                ActionButton(root, sale + " / " + (long)Mathf.Max(1, def.Value / 2) * item.Quantity, ref y, () => session.Sell(source, id));
            }
            else if (source == session.Loadout && def.Kind == ItemKind.Medicine) ActionButton(root, "选择治疗部位", ref y, () => ShowHealth(null, id));
            root.sizeDelta = new Vector2(330, Mathf.Max(209f, y + 7));

            void StartPlacement(bool turn)
            {
                RemovePopup();
                placing = true;
                placementStamp++;
                rotated = turn;
                DrawPlacementControls();
            }
        }

        string ItemDetails(ItemDefinition item, ItemStack stack)
        {
            double weight = (double)item.UnitWeight * (stack?.Quantity ?? 1);
            if (stack != null && !stack.Parts.Empty)
                for (int i = 0; i <= (int)PartSlot.Magazine; i++)
                    weight += session.Catalog.Find(stack.Parts.Get((PartSlot)i).ItemId)?.UnitWeight ?? 0f;
            int w = stack?.Rotated == true ? item.Height : item.Width, h = stack?.Rotated == true ? item.Width : item.Height;
            string result = item.KindName + " · " + (item.Tier > 0 ? item.Tier + " 级" : "未分级")
                + "\n占格 " + w + " × " + h + " / " + weight.ToString("0.##") + " 千克";
            if (item.Storage.Columns > 0)
                result += "\n容量 " + item.Storage.Capacity + " 格 / 内载 " + item.Storage.WeightLimit + " 千克";
            if (item.Durability > 0) result += "\n耐久 " + (item.Durability - (stack?.Wear ?? 0)) + " / " + item.Durability + " · " + CoverageName(item.Coverage);
            if (item.Kind == ItemKind.Medicine) result += "\n" + TreatmentName(item.Treatment) + " / " + item.UseSeconds.ToString("0.#") + " 秒 / " + ((long)(stack?.Quantity ?? 1) * item.Uses - (stack?.UsesSpent ?? 0)) + " 次";
            if (item.Weapon != null && !item.Weapon.Melee)
                result += "\n弹匣 " + Mathf.Max(0, stack?.LoadedAmmo ?? 0) + " / " + WeaponRules.Capacity(item.Weapon, stack, session.Catalog)
                    + " · " + AmmoName(item.Weapon.AmmoFamily);
            if (item.Kind == ItemKind.Ammo) result += "\n" + AmmoName(item.AmmoFamily);
            if (item.Kind == ItemKind.Part)
            {
                result += "\n" + PartName(item.Part.Slot) + " · " + (string.IsNullOrEmpty(item.Part.DedicatedItem)
                    ? item.Part.Interface : session.Catalog.Find(item.Part.DedicatedItem)?.DisplayName ?? "指定型号");
                if (item.Part.Slot == PartSlot.Magazine) result += "\n容量 " + item.Part.Capacity + " 发";
            }
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
            rect.gameObject.AddComponent<CanvasRenderer>();
            var image = rect.gameObject.AddComponent<CachedItemImage>();
            image.raycastTarget = false;
            return image;
        }

        void ActionButton(Transform parent, string title, ref float y, System.Action action)
        {
            Button(parent, title, 13, y, 304, 36, () =>
            {
                if (itemPopup == parent && InventoryIdle() && !ProcessInventoryBackKey()) action();
            });
            y += 40;
        }
    }
}
