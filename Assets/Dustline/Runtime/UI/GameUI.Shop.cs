using UnityEngine;
using UnityEngine.UI;

namespace Dustline
{
    public sealed partial class GameUI
    {
        static readonly (ItemKind kind, string name)[] ShopTabs =
        {
            (ItemKind.Rig, "弹挂"), (ItemKind.Pack, "背包"), (ItemKind.Armor, "护甲"), (ItemKind.Helmet, "头盔"),
            (ItemKind.Weapon, "武器"), (ItemKind.Ammo, "弹药"), (ItemKind.Part, "配件"),
            (ItemKind.Medicine, "医疗"), (ItemKind.Valuable, "物资")
        };

        static readonly (WeaponForm? form, string name)[] ShopWeaponCategories =
        {
            (WeaponForm.Rifle, "突击步枪"), (WeaponForm.Smg, "冲锋枪"), (WeaponForm.Lmg, "轻机枪"),
            (WeaponForm.Marksman, "射手步枪"), (WeaponForm.Sniper, "狙击步枪"), (WeaponForm.Shotgun, "霰弹枪"),
            (WeaponForm.Pistol, "手枪"), (null, "近战武器")
        };

        void ShowShop(ItemKind kind = ItemKind.Rig) => BuildShop(kind, 0, 0f, kind == ItemKind.Weapon, -1);

        void BuildShop(ItemKind kind, int index, float categoryOffset, bool weaponsExpanded, int weaponCategory)
        {
            if (session.Phase != SessionPhase.Menu) return;
            var root = Display(ScreenPage.Shop, "Supply store", wide: true);
            RectTransform categoryContent = null;
            SetPageRestore(() => BuildShop(kind, index, categoryOffset, weaponsExpanded, weaponCategory), CaptureCategoryScroll);
            reflowWidePage = Build;
            Build();

            void CaptureCategoryScroll()
            {
                if (categoryContent != null) categoryOffset = categoryContent.anchoredPosition.y;
            }

            void Build()
            {
                CloseItemPopup();
                CaptureCategoryScroll();
                Clear(root);
                const float inset = 12f, gap = 8f, categoryHeight = 518f, mainRow = 42f, subRow = 38f;
                float w = root.rect.width, railWidth = Mathf.Clamp(w * .18f, 172f, 228f);
                float stockX = railWidth + 2 * inset, stockWidth = w - stockX - inset;
                WideHeader(root, "补给商店", "品质从低到高 · 同级按价格升序", true);
                var rail = At("Shop category rail", root, inset, 58, railWidth, 562);
                Fill(rail, Ink);
                Label(rail, "物品类别", 19, 10, 2, railWidth - 20, 32, TextAnchor.MiddleLeft, Aqua);
                float listWidth = railWidth - 23;
                float contentHeight = Mathf.Max(categoryHeight, mainRow * ShopTabs.Length + (weaponsExpanded ? subRow * ShopWeaponCategories.Length : 0f));
                var viewport = At("Shop categories", rail, 6, 36, listWidth, categoryHeight);
                Fill(viewport, Color.clear, true);
                viewport.gameObject.AddComponent<RectMask2D>();
                var content = categoryContent = At("Shop category content", viewport, 0, 0, listWidth, contentHeight);
                float scrollRange = contentHeight - categoryHeight;
                categoryOffset = Mathf.Clamp(categoryOffset, 0f, scrollRange);
                content.anchoredPosition = new Vector2(0, categoryOffset);
                if (scrollRange > 0f)
                {
                    var scroll = viewport.gameObject.AddComponent<InventoryScroll>();
                    scroll.viewport = viewport;
                    scroll.content = content;
                    scroll.horizontal = false;
                    scroll.inertia = false;
                    scroll.movementType = ScrollRect.MovementType.Clamped;
                    scroll.scrollSensitivity = 45f;
                    scroll.AllowScroll = CanAct;
                    scroll.TryAcquire = source =>
                    {
                        if (!CanAct()) return false;
                        inventoryScrollOwner = source;
                        CloseItemPopup();
                        return true;
                    };
                    scroll.Released = source => { if (inventoryScrollOwner == source) inventoryScrollOwner = null; };
                    scroll.WheelScroll = () => CloseItemPopup();

                    var track = At("Shop category scroll track", rail, railWidth - 12, 36, 6, categoryHeight);
                    Fill(track, Plate);
                    float thumbHeight = categoryHeight * categoryHeight / contentHeight;
                    var thumb = At("Shop category scroll thumb", track, 0, 0, 6, thumbHeight);
                    Fill(thumb, Muted);
                    SetThumb(categoryOffset / scrollRange);
                    scroll.onValueChanged.AddListener(value => SetThumb(1f - value.y));

                    void SetThumb(float fraction) => thumb.anchoredPosition = new Vector2(0, -Mathf.Clamp01(fraction) * (categoryHeight - thumbHeight));
                }

                float y = 0;
                string selection = "";
                foreach (var tab in ShopTabs)
                {
                    if (tab.kind == kind) selection = tab.name;
                    if (tab.kind != ItemKind.Weapon)
                    {
                        Button(content, tab.name, 0, y, listWidth, mainRow - 5, () => SelectCategory(tab.kind), tab.kind == kind)
                            .name = "Category / " + tab.name;
                        y += mainRow;
                        continue;
                    }

                    Button(content, (weaponsExpanded ? "−  " : "+  ") + tab.name, 0, y, listWidth, mainRow - 5, () =>
                    {
                        if (!CanAct()) return;
                        weaponsExpanded = !weaponsExpanded;
                        if (weaponsExpanded && kind != ItemKind.Weapon)
                        {
                            kind = ItemKind.Weapon;
                            index = 0;
                        }
                        Build();
                    }, kind == ItemKind.Weapon && (!weaponsExpanded || weaponCategory < 0)).name = "Category / 武器";
                    y += mainRow;
                    if (kind == ItemKind.Weapon && weaponCategory >= 0) selection = tab.name + " / " + ShopWeaponCategories[weaponCategory].name;
                    if (!weaponsExpanded) continue;
                    for (int i = 0; i < ShopWeaponCategories.Length; i++)
                    {
                        int category = i;
                        var entry = ShopWeaponCategories[i];
                        bool selected = kind == ItemKind.Weapon && weaponCategory == i;
                        Button(content, entry.name, 14, y, listWidth - 14, subRow - 3, () => SelectCategory(ItemKind.Weapon, category), selected)
                            .name = "Weapon category / " + (entry.form?.ToString() ?? "Melee");
                        y += subRow;
                    }
                }

                Label(root, selection, 22, stockX, 59, stockWidth, 43, TextAnchor.MiddleLeft).name = "Shop selection";
                bool chooseWeapon = kind == ItemKind.Weapon && weaponCategory < 0;
                var stock = chooseWeapon ? null : session.Catalog.Stock(kind);
                WeaponForm? form = weaponCategory < 0 ? null : ShopWeaponCategories[weaponCategory].form;
                // Stock already orders tiers and prices; filtering must precede pagination.
                stock?.RemoveAll(item => item.NotForSale || (kind == ItemKind.Weapon && !MatchesShopWeapon(item.Weapon, form)));
                const int pageSize = 8;
                int count = stock?.Count ?? 0;
                int pages = Mathf.Max(1, (count + pageSize - 1) / pageSize);
                index = Mathf.Clamp(index, 0, pages - 1);
                float cellWidth = (stockWidth - 3 * gap) * .25f;
                for (int i = 0; i < pageSize && index * pageSize + i < count; i++)
                {
                    var item = stock[index * pageSize + i];
                    var card = Button(root, "", stockX + (i % 4) * (cellWidth + gap), 108 + (i / 4) * 252, cellWidth, 239, null, background: Ink);
                    card.name = "Stock / " + item.Id;
                    var cell = (RectTransform)card.transform;
                    card.onClick.AddListener(() => { if (CanAct()) ShowProduct(cell, item); });
                    var grade = item.Tier > 0 ? ItemDefinition.GradeColor(item.Tier) : Muted;
                    Fill(At("Quality edge", cell, 0, 0, 3, 239), grade);
                    Fill(At("Quality badge", cell, 11, 10, 75, 27), Color.Lerp(Plate, grade, .22f));
                    var badge = Label(cell, item.Tier > 0 ? item.Tier + " 级" : "未分级", 15, 11, 10, 75, 27, TextAnchor.MiddleCenter, grade);
                    badge.name = "Stock quality";
                    DrawItemIcon(cell, item, null, 26, 31, cellWidth - 52, 87);
                    var title = Label(cell, item.DisplayName, 19, 11, 118, cellWidth - 22, 26, TextAnchor.MiddleCenter);
                    title.resizeTextForBestFit = true;
                    title.resizeTextMinSize = 13;
                    title.resizeTextMaxSize = 19;
                    Label(cell, ShopItemDetails(item), 14, 11, 148, cellWidth - 22, 43, TextAnchor.UpperCenter, Muted);
                    int amount = PurchaseAmount(item);
                    Fill(At("Price band", cell, 11, 196, cellWidth - 22, 33), Plate);
                    var price = Label(cell, (amount > 1 ? "×" + amount + " / " : "") + (long)item.Value * amount + " 信用点", 16,
                        18, 196, cellWidth - 92, 33, TextAnchor.MiddleLeft, Amber);
                    price.resizeTextForBestFit = true;
                    price.resizeTextMinSize = 11;
                    price.resizeTextMaxSize = 16;
                    Label(cell, "详情 ›", 14, cellWidth - 70, 196, 52, 33, TextAnchor.MiddleRight, Aqua);
                }
                if (count == 0)
                {
                    string message = chooseWeapon ? (weaponsExpanded ? "请选择武器类型" : "展开左侧武器分类") : "本类暂无商品";
                    Label(root, message, 26, stockX, 156, stockWidth, 62, TextAnchor.MiddleCenter, Muted).name = "Shop empty state";
                }
                Fill(At("Shop footer", root, 0, 630, w, 50), Ink);
                Button(root, "上一页", w - 357, 633, 102, 42, () => ChangePage(-1)).interactable = index > 0;
                Label(root, (index + 1) + " / " + pages, 18, w - 247, 633, 111, 42, TextAnchor.MiddleCenter).name = "Shop page count";
                Button(root, "下一页", w - 125, 633, 113, 42, () => ChangePage(1)).interactable = index + 1 < pages;

                bool CanAct() => rail != null && rail.gameObject.activeInHierarchy && session.Phase == SessionPhase.Menu
                    && !ProcessInventoryBackKey() && backHandledFrame != Time.frameCount && InventoryIdle();

                void SelectCategory(ItemKind choice, int category = -1)
                {
                    if (!CanAct() || (kind == choice && weaponCategory == category)) return;
                    kind = choice;
                    weaponCategory = category;
                    index = 0;
                    Build();
                }

                void ChangePage(int step)
                {
                    int next = index + step;
                    if (!CanAct() || next < 0 || next >= pages) return;
                    index = next;
                    Build();
                }

                void ShowProduct(RectTransform anchor, ItemDefinition item)
                {
                    var popup = ItemPopup(root, anchor, item, null, null);
                    int amount = PurchaseAmount(item);
                    Button(popup, "购买 ×" + amount + " / " + (long)item.Value * amount + " 信用点", 13, 164, popup.rect.width - 26, 42, () =>
                    {
                        if (popup == null || itemPopup != popup || !CanAct()) return;
                        session.Buy(item.Id, amount);
                        UpdateCredits();
                    }, true);
                }
            }
        }

        static bool MatchesShopWeapon(WeaponDefinition weapon, WeaponForm? form) => weapon != null
            && (form.HasValue ? !weapon.Melee && weapon.Form == form.Value : weapon.Melee);

        static int PurchaseAmount(ItemDefinition item) => item.Kind == ItemKind.Ammo ? Mathf.Min(20, item.MaxStack) : 1;

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
            float position, System.Action<float> changed)
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
