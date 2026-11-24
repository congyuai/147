using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Dustline
{
    public sealed partial class GameUI
    {
        static readonly (ItemKind kind, string name)[] ShopTabs =
        {
            (ItemKind.Rig, "弹挂"), (ItemKind.Pack, "背包"), (ItemKind.Armor, "护甲"), (ItemKind.Helmet, "头盔"),
            (ItemKind.Weapon, "武器"), (ItemKind.Ammo, "弹药"), (ItemKind.Part, "配件"), (ItemKind.Medicine, "医疗"), (ItemKind.Valuable, "物资")
        };

        static readonly (WeaponForm? form, string name)[] ShopWeaponCategories =
        {
            (WeaponForm.Rifle, "突击步枪"), (WeaponForm.Smg, "冲锋枪"), (WeaponForm.Lmg, "轻机枪"),
            (WeaponForm.Marksman, "射手步枪"), (WeaponForm.Sniper, "狙击步枪"), (WeaponForm.Shotgun, "霰弹枪"),
            (WeaponForm.Pistol, "手枪"), (null, "近战武器")
        };

        static readonly AmmoFamily[] ShopCalibers = { AmmoFamily.Amber, AmmoFamily.Copper, AmmoFamily.Silver, AmmoFamily.Slate };

        sealed class ShopBrowse
        {
            internal ItemKind Kind;
            internal float CategoryOffset;
            internal readonly HashSet<ItemKind> Expanded = new HashSet<ItemKind>();
            internal readonly Dictionary<ItemKind, int> Filters = new Dictionary<ItemKind, int>();
            internal readonly Dictionary<(ItemKind, int), float> StockOffsets = new Dictionary<(ItemKind, int), float>();
        }

        void ShowShop(ItemKind kind = ItemKind.Rig)
        {
            var browse = new ShopBrowse { Kind = kind };
            if (ShopBranchCount(kind) > 0) browse.Expanded.Add(kind);
            BuildShop(browse);
        }

        void BuildShop(ShopBrowse browse)
        {
            if (session.Phase != SessionPhase.Menu) return;
            var root = Display(ScreenPage.Shop, "Supply store", wide: true);
            Action capture = null;
            SetPageRestore(() => BuildShop(browse), () => capture?.Invoke());
            reflowWidePage = Build;
            Build();

            void Build()
            {
                capture?.Invoke();
                CancelInventoryInput();
                inventoryTiles.Clear();
                CloseItemPopup();
                Clear(root);
                const float inset = 12f, gap = 8f, categoryHeight = 518f, mainRow = 42f, subRow = 38f;
                float w = root.rect.width, railWidth = Mathf.Clamp(w * .18f, 172f, 228f);
                float stockX = railWidth + 2 * inset, stockWidth = w - stockX - inset;
                ItemKind kind = browse.Kind;
                int choice = browse.Filters.TryGetValue(kind, out var filter) ? filter : -1;
                var key = (kind, choice);
                browse.StockOffsets.TryGetValue(key, out float stockOffset);
                int stamp = 0, lastScrollFrame = -1;
                WideHeader(root, "补给商店", "品质从低到高 · 同级按价格升序", true);
                var rail = At("Shop category rail", root, inset, 58, railWidth, 562);
                Fill(rail, Ink);
                Label(rail, "物品类别", 19, 10, 2, railWidth - 20, 32, TextAnchor.MiddleLeft, Aqua);
                float height = mainRow * ShopTabs.Length;
                foreach (var entry in ShopTabs)
                    if (browse.Expanded.Contains(entry.kind)) height += subRow * ShopBranchCount(entry.kind);
                var categoryContent = Scroller(rail, "Shop categories", "Shop category content", 6, 36, railWidth - 12, categoryHeight, height, browse.CategoryOffset);
                float listWidth = categoryContent.rect.width, y = 0f;
                string selection = "";
                foreach (var tab in ShopTabs)
                {
                    var category = tab.kind;
                    int branches = ShopBranchCount(category);
                    bool expanded = browse.Expanded.Contains(category);
                    if (category == kind) selection = tab.name + (choice < 0 ? "" : " / " + ShopBranchName(category, choice));
                    string title = branches == 0 ? tab.name : (expanded ? "−  " : "+  ") + tab.name;
                    Tap(categoryContent, "Category / " + tab.name, title, 0, y, listWidth, mainRow - 5, () =>
                    {
                        if (expanded) browse.Expanded.Remove(category);
                        else
                        {
                            if (branches == 0 && category == browse.Kind) return;
                            browse.Kind = category;
                            if (branches > 0) browse.Expanded.Add(category);
                        }
                        Build();
                    }, category == kind && (!expanded || choice < 0));
                    y += mainRow;
                    if (!expanded) continue;
                    for (int i = 0; i < branches; i++)
                    {
                        int index = i;
                        Tap(categoryContent, ShopBranchNode(category, index), ShopBranchName(category, index), 14, y, listWidth - 14, subRow - 3, () =>
                        {
                            if (kind == category && choice == index) return;
                            browse.Kind = category;
                            browse.Filters[category] = index;
                            Build();
                        }, kind == category && choice == index);
                        y += subRow;
                    }
                }

                Label(root, selection, 22, stockX, 59, stockWidth, 43, TextAnchor.MiddleLeft).name = "Shop selection";
                bool chooseBranch = ShopBranchCount(kind) > 0 && choice < 0;
                var stock = chooseBranch ? null : session.Catalog.Stock(kind);
                if (choice >= 0) stock.RemoveAll(item => !MatchesShopBranch(item, choice));
                int count = stock?.Count ?? 0;
                const int columns = 4;
                const float stockHeight = 517f, cardHeight = 239f, rowHeight = cardHeight + 13f;
                int rows = (count + columns - 1) / columns;
                var stockContent = Scroller(root, "Shop stock viewport", "Shop stock content", stockX, 108, stockWidth, stockHeight,
                    rows * rowHeight, stockOffset);
                float cellWidth = (stockContent.rect.width - (columns - 1) * gap) / columns;
                capture = () =>
                {
                    browse.CategoryOffset = categoryContent.anchoredPosition.y;
                    browse.StockOffsets[key] = stockContent.anchoredPosition.y;
                };

                for (int i = 0; i < count; i++)
                {
                    var item = stock[i];
                    var cell = At("Stock / " + item.Id, stockContent, (i % columns) * (cellWidth + gap), (i / columns) * rowHeight, cellWidth, cardHeight);
                    BindTap(cell, Ink, false, () => ShowProduct(cell, item));
                    var grade = item.Tier > 0 || item.Special ? item.QualityColor : Muted;
                    Fill(At("Quality edge", cell, 0, 0, 3, cardHeight), grade);
                    Fill(At("Quality badge", cell, 11, 10, 75, 27), Color.Lerp(Plate, grade, .22f));
                    Label(cell, item.QualityName, 15, 11, 10, 75, 27, TextAnchor.MiddleCenter, grade).name = "Stock quality";
                    DrawItemIcon(cell, item, null, 26, 31, cellWidth - 52, 87);
                    var title = Label(cell, item.DisplayName, 19, 11, 118, cellWidth - 22, 26, TextAnchor.MiddleCenter);
                    title.resizeTextForBestFit = true;
                    title.resizeTextMinSize = 13;
                    title.resizeTextMaxSize = 19;
                    Label(cell, ShopItemDetails(item), 14, 11, 148, cellWidth - 22, 43, TextAnchor.UpperCenter, Muted);
                    Fill(At("Price band", cell, 11, 196, cellWidth - 22, 33), Plate);
                    string unit = item.Kind == ItemKind.Ammo ? " / 发" : "";
                    var price = Label(cell, item.Value + " 信用点" + unit, 16, 18, 196, cellWidth - 92, 33, TextAnchor.MiddleLeft, Amber);
                    price.resizeTextForBestFit = true;
                    price.resizeTextMinSize = 11;
                    price.resizeTextMaxSize = 16;
                    Label(cell, "详情 ›", 14, cellWidth - 70, 196, 52, 33, TextAnchor.MiddleRight, Aqua);
                }
                if (count == 0)
                {
                    string branchHint = kind == ItemKind.Weapon ? "武器类型" : kind == ItemKind.Ammo ? "口径" : "等级";
                    string message = chooseBranch ? "请选择" + branchHint : "本类暂无商品";
                    Label(stockContent, message, 26, 0, 48, stockContent.rect.width, 62, TextAnchor.MiddleCenter, Muted).name = "Shop empty state";
                }
                Fill(At("Shop footer", root, 0, 630, w, 50), Ink);
                Label(root, count + " 件商品", 17, stockX, 633, stockWidth - 5, 42, TextAnchor.MiddleRight, Muted).name = "Shop stock count";

                bool CanScroll() => rail != null && rail.gameObject.activeInHierarchy && session.Phase == SessionPhase.Menu
                    && InventoryIdle() && !ProcessInventoryBackKey() && backHandledFrame != Time.frameCount;

                bool CanAct() => CanScroll() && lastScrollFrame != Time.frameCount;

                void Scrolling()
                {
                    stamp++;
                    lastScrollFrame = Time.frameCount;
                    CloseItemPopup();
                }

                RectTransform Scroller(Transform parent, string name, string contentName, float x, float top, float width, float visible, float full, float offset)
                {
                    const float trackWidth = 6f, trackGap = 4f;
                    float inner = width - trackWidth - trackGap;
                    var viewport = At(name, parent, x, top, inner, visible);
                    Fill(viewport, Color.clear, true);
                    viewport.gameObject.AddComponent<RectMask2D>();
                    var content = At(contentName, viewport, 0, 0, inner, Mathf.Max(visible, full));
                    float range = full - visible;
                    if (range <= 0f) return content;
                    var scroll = viewport.gameObject.AddComponent<InventoryScroll>();
                    scroll.viewport = viewport;
                    scroll.content = content;
                    scroll.horizontal = false;
                    scroll.inertia = false;
                    scroll.movementType = ScrollRect.MovementType.Clamped;
                    scroll.scrollSensitivity = 45f;
                    scroll.AllowScroll = CanScroll;
                    scroll.TryAcquire = source =>
                    {
                        if (!CanScroll()) return false;
                        inventoryScrollOwner = source;
                        Scrolling();
                        return true;
                    };
                    scroll.Released = source =>
                    {
                        if (inventoryScrollOwner != source) return;
                        inventoryScrollOwner = null;
                        lastScrollFrame = Time.frameCount;
                    };
                    scroll.WheelScroll = Scrolling;
                    var track = At(name + " scroll track", parent, x + width - trackWidth, top, trackWidth, visible);
                    Fill(track, Plate);
                    float thumbHeight = visible * visible / full;
                    var thumb = At(name + " scroll thumb", track, 0, 0, trackWidth, thumbHeight);
                    Fill(thumb, Muted);
                    offset = Mathf.Clamp(offset, 0f, range);
                    content.anchoredPosition = new Vector2(0, offset);
                    SetThumb(offset / range);
                    scroll.onValueChanged.AddListener(value => SetThumb(1f - value.y));
                    return content;

                    void SetThumb(float fraction) => thumb.anchoredPosition = new Vector2(0, -Mathf.Clamp01(fraction) * (visible - thumbHeight));
                }

                void Tap(Transform parent, string name, string title, float x, float top, float width, float h, Action action, bool selected)
                {
                    var rect = At(name, parent, x, top, width, h);
                    var text = Label(rect, title, 18, 5, 2, width - 10, h - 4, TextAnchor.MiddleCenter);
                    text.resizeTextForBestFit = true;
                    text.resizeTextMinSize = 12;
                    text.resizeTextMaxSize = 19;
                    BindTap(rect, selected ? Amber : Plate, selected, action);
                }

                void BindTap(RectTransform rect, Color tint, bool selected, Action action)
                {
                    var tile = CreateItemTile(rect, tint);
                    if (selected) StyleButton(tile, true, tint);
                    tile.CanPress = CanAct;
                    tile.SelectionStamp = () => stamp;
                    tile.onClick.AddListener(() => { if (rect != null && rect.gameObject.activeInHierarchy && CanAct()) action(); });
                }

                void ShowProduct(RectTransform anchor, ItemDefinition item)
                {
                    stamp++;
                    var popup = ItemPopup(root, anchor, item, null, null);
                    bool Ready() => popup != null && itemPopup == popup && CanAct();
                    bool Purchase(int amount, int price, out string error)
                        => session.TryBuyShopItem(item.Id, amount, price, out error);
                    void Purchased()
                    {
                        CloseItemPopup();
                        UpdateCredits();
                    }

                    if (item.Kind == ItemKind.Ammo)
                    {
                        AddAmmoPurchaseControls(popup, item, session.Stash, "购买弹药", Ready, (amount, price) =>
                        {
                            bool ok = Purchase(amount, price, out var error);
                            return (ok, error);
                        }, Purchased);
                        PlaceItemPopup(popup, root, anchor);
                    }
                    else
                    {
                        inventoryTiles.RemoveAll(tile => tile == null || !tile.gameObject.activeInHierarchy);
                        int quote = item.Value;
                        string title = "购买 ×1 / " + quote + " 信用点";
                        Tap(popup, title, title, 13, 164, popup.rect.width - 26, 42, () =>
                        {
                            if (!Ready()) return;
                            if (Purchase(1, quote, out var error)) Purchased();
                            else Notify(error);
                        }, true);
                    }
                }
            }
        }

        static int ShopBranchCount(ItemKind kind) => kind switch
        {
            ItemKind.Weapon => ShopWeaponCategories.Length,
            ItemKind.Ammo => ShopCalibers.Length,
            ItemKind.Armor or ItemKind.Helmet => 6,
            _ => 0
        };

        static string ShopBranchName(ItemKind kind, int index) => kind switch
        {
            ItemKind.Weapon => ShopWeaponCategories[index].name,
            ItemKind.Ammo => AmmoName(ShopCalibers[index]),
            _ => (index + 1) + " 级"
        };

        static string ShopBranchNode(ItemKind kind, int index) => kind switch
        {
            ItemKind.Weapon => "Weapon category / " + (ShopWeaponCategories[index].form?.ToString() ?? "Melee"),
            ItemKind.Ammo => "Ammo category / " + ShopCalibers[index],
            _ => kind + " tier / " + (index + 1)
        };

        static bool MatchesShopBranch(ItemDefinition item, int index) => item.Kind switch
        {
            ItemKind.Weapon => item.Weapon != null && (ShopWeaponCategories[index].form is WeaponForm form
                ? !item.Weapon.Melee && item.Weapon.Form == form : item.Weapon.Melee),
            ItemKind.Ammo => item.AmmoFamily == ShopCalibers[index],
            _ => item.Tier == index + 1
        };

        static int PurchaseAmount(ItemDefinition item) => 1;

        string ShopItemDetails(ItemDefinition item)
        {
            if (item.Storage.Columns > 0)
                return "容量 " + item.Storage.Capacity + " 格  /  " + item.Storage.WeightLimit + " 千克"
                    + (item.Storage.Pockets is { Length: > 0 } ? "\n" + item.Storage.Pockets.Length + " 组口袋"
                        : item.Storage.Split > 0 ? "\n双分区" : "");
            if (item.Durability > 0) return "耐久 " + item.Durability + " · " + CoverageName(item.Coverage);
            if (item.Kind == ItemKind.Part && item.Part.Slot == PartSlot.Magazine) return "容量 " + item.Part.Capacity + " 发";
            return ItemDetails(item, null);
        }

        RectTransform HorizontalStrip(Transform parent, string name, float x, float y, float w, float h, float contentWidth,
            float position, Action<float> changed)
        {
            var viewport = At(name, parent, x, y, w, h);
            Fill(viewport, Color.clear, true);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = At("Strip content", viewport, 0, 0, Mathf.Max(w, contentWidth), h);
            if (contentWidth <= w + .1f) return content;
            var scroll = viewport.gameObject.AddComponent<InventoryScroll>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 35f;
            content.anchoredPosition = new Vector2((w - contentWidth) * Mathf.Clamp01(position), 0);
            const float trackHeight = 5;
            var rail = At("Strip scroll position", parent, x, y + h - trackHeight, w, trackHeight);
            Fill(rail, Border);
            var handle = Fill(At("Strip scroll handle", rail, 0, 0, w * w / contentWidth, trackHeight), Muted);
            var tab = handle.rectTransform;
            SetPosition(position);
            scroll.onValueChanged.AddListener(value => { SetPosition(value.x); changed?.Invoke(Mathf.Clamp01(value.x)); });
            return content;

            void SetPosition(float fraction) => tab.anchoredPosition = new Vector2(Mathf.Clamp01(fraction) * (w - tab.rect.width), 0f);
        }
    }
}
