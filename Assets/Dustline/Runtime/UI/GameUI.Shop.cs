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

        void ShowShop(ItemKind kind = ItemKind.Rig) => BuildShop(kind, 0, 0f, true);

        void BuildShop(ItemKind kind, int index, float tabPosition, bool centreTab)
        {
            if (session.Phase != SessionPhase.Menu) return;
            var root = Display(ScreenPage.Shop, "Supply store", wide: true);
            InventoryScroll tabScroll = null;
            ItemDefinition shown = null;
            SetPageRestore(() => BuildShop(kind, index, tabPosition, false), CaptureTabScroll);
            reflowWidePage = Build;
            Build();

            void CaptureTabScroll()
            {
                if (tabScroll != null) tabPosition = Mathf.Clamp01(tabScroll.horizontalNormalizedPosition);
            }

            void Build()
            {
                var reopen = shown;
                CloseItemPopup();
                CaptureTabScroll();
                Clear(root);
                const float inset = 12f, tabMin = 122f, gap = 8f;
                float w = root.rect.width, usable = w - 2 * inset, stride = Mathf.Max(tabMin + gap, (usable + gap) / ShopTabs.Length);
                float tabWidth = stride - gap, tabTotal = stride * ShopTabs.Length - gap;
                if (centreTab)
                {
                    int active = System.Array.FindIndex(ShopTabs, entry => entry.kind == kind);
                    tabPosition = Mathf.Clamp01((stride * (active + .5f) - usable * .5f) / Mathf.Max(1f, tabTotal - usable));
                    centreTab = false;
                }
                WideHeader(root, "补给商店", "品质从高到低 · 同级按价格升序", true);
                var tabs = HorizontalStrip(root, "Shop categories", inset, 58, usable, 48, tabTotal, tabPosition, value => tabPosition = value);
                tabScroll = tabs.parent.GetComponent<InventoryScroll>();
                for (int i = 0; i < ShopTabs.Length; i++)
                {
                    var tab = ShopTabs[i];
                    Button(tabs, tab.name, i * stride, 0, tabWidth, 42, () =>
                    {
                        if (tabScroll?.Dragging == true || kind == tab.kind || ProcessInventoryBackKey()) return;
                        kind = tab.kind; index = 0; Build();
                    }, kind == tab.kind);
                }

                var stock = session.Catalog.Stock(kind);
                const int pageSize = 8;
                int pages = Mathf.Max(1, (stock.Count + pageSize - 1) / pageSize);
                index = Mathf.Clamp(index, 0, pages - 1);
                float cellWidth = (usable - 3 * gap) * .25f;
                RectTransform restoredAnchor = null;
                for (int i = 0; i < pageSize && index * pageSize + i < stock.Count; i++)
                {
                    var item = stock[index * pageSize + i];
                    var card = Button(root, "", inset + (i % 4) * (cellWidth + gap), 119 + (i / 4) * 252, cellWidth, 239, null, background: Ink);
                    card.name = "Stock / " + item.Id;
                    var cell = (RectTransform)card.transform;
                    card.onClick.AddListener(() =>
                    {
                        if (tabScroll?.Dragging != true && !ProcessInventoryBackKey()) ShowProduct(cell, item);
                    });
                    if (item == reopen) restoredAnchor = cell;
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
                    Label(cell, (amount > 1 ? "×" + amount + " / " : "") + (long)item.Value * amount + " 信用点", 16,
                        18, 196, cellWidth - 92, 33, TextAnchor.MiddleLeft, Amber);
                    Label(cell, "详情 ›", 14, cellWidth - 70, 196, 52, 33, TextAnchor.MiddleRight, Aqua);
                }
                if (stock.Count == 0) Label(root, "本类暂无商品", 26, inset, 156, usable, 62, TextAnchor.MiddleCenter, Muted);
                Fill(At("Shop footer", root, 0, 630, w, 50), Ink);
                Button(root, "上一页", w - 357, 633, 102, 42, () => ChangePage(-1)).interactable = index > 0;
                Label(root, (index + 1) + " / " + pages, 18, w - 247, 633, 111, 42, TextAnchor.MiddleCenter);
                Button(root, "下一页", w - 125, 633, 113, 42, () => ChangePage(1)).interactable = index + 1 < pages;
                if (restoredAnchor != null) ShowProduct(restoredAnchor, reopen);
            }

            void ChangePage(int step)
            {
                if (tabScroll?.Dragging == true || ProcessInventoryBackKey()) return;
                index += step;
                Build();
            }

            void ShowProduct(RectTransform anchor, ItemDefinition item)
            {
                var popup = ItemPopup(root, anchor, item, null, () => shown = null);
                shown = item;
                int amount = PurchaseAmount(item);
                Button(popup, "购买 ×" + amount + " / " + (long)item.Value * amount + " 信用点", 13, 164, popup.rect.width - 26, 42, () =>
                {
                    if (itemPopup != popup || tabScroll?.Dragging == true || ProcessInventoryBackKey()) return;
                    session.Buy(item.Id, amount);
                    UpdateCredits();
                }, true);
            }
        }

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
