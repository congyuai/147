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

        void ShowShop(ItemKind kind = ItemKind.Rig)
        {
            if (session.Phase != SessionPhase.Menu) return;
            var root = Display(ScreenPage.Shop, "Supply store", wide: true);
            int index = 0;
            float tabPosition = 0;
            bool centreTab = true;
            reflowWidePage = Build;
            Build();

            void Build()
            {
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
                for (int i = 0; i < ShopTabs.Length; i++)
                {
                    var tab = ShopTabs[i];
                    Button(tabs, tab.name, i * stride, 0, tabWidth, 42, () =>
                    {
                        kind = tab.kind; index = 0; Build();
                    }, kind == tab.kind);
                }

                var stock = session.Catalog.Stock(kind);
                const int pageSize = 8;
                int pages = Mathf.Max(1, (stock.Count + pageSize - 1) / pageSize);
                index = Mathf.Clamp(index, 0, pages - 1);
                float cellWidth = (usable - 3 * gap) * .25f;
                for (int i = 0; i < pageSize && index * pageSize + i < stock.Count; i++)
                {
                    var item = stock[index * pageSize + i];
                    var cell = At("Stock / " + item.Id, root, inset + (i % 4) * (cellWidth + gap), 119 + (i / 4) * 252, cellWidth, 239);
                    var grade = item.Tier > 0 ? ItemDefinition.GradeColor(item.Tier) : Muted;
                    Fill(cell, Ink);
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
                    int amount = item.Kind == ItemKind.Ammo ? Mathf.Min(20, item.MaxStack) : 1;
                    Button(cell, "购买 ×" + amount + " / " + item.Value * amount, 11, 191, cellWidth - 22, 38, () =>
                    {
                        session.Buy(item.Id, amount);
                        UpdateCredits();
                    }, true);
                }
                if (stock.Count == 0) Label(root, "本类暂无商品", 26, inset, 156, usable, 62, TextAnchor.MiddleCenter, Muted);
                Fill(At("Shop footer", root, 0, 630, w, 50), Ink);
                Button(root, "返回仓库", inset, 633, 132, 42, ShowStash);
                Button(root, "上一页", w - 357, 633, 102, 42, () => { index--; Build(); }).interactable = index > 0;
                Label(root, (index + 1) + " / " + pages, 18, w - 247, 633, 111, 42, TextAnchor.MiddleCenter);
                Button(root, "下一页", w - 125, 633, 113, 42, () => { index++; Build(); }).interactable = index + 1 < pages;
            }
        }

        string ShopItemDetails(ItemDefinition item)
        {
            if (item.Storage.Columns > 0)
                return item.Storage.Columns + " × " + item.Storage.Rows + " 格  /  " + item.Storage.WeightLimit + " 千克"
                    + (item.Storage.Split > 0 ? "\n分区 " + item.Storage.Split + " + " + (item.Storage.Columns - item.Storage.Split) + " 列" : "");
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
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
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
