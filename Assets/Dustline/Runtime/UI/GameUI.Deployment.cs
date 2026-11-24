using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Dustline
{
    public sealed partial class GameUI
    {
        const string LastSiteKey = "Dustline.StrategySite.v1";
        const string LastTierKey = "Dustline.StrategyTier.v1";
        StrategyTerrain strategyTerrain;
        RectTransform strategyView, strategySidebar;
        RectTransform[] strategyMarkers;
        RawImage strategyPreview;
        float previewRetryAt;
        int strategySite = -1, strategyTier;
        bool strategyFocused;
        string expandedTask;
        Func<bool> closePreparationPicker;
        static readonly EquipmentSlot[] PreparationSlots =
        {
            EquipmentSlot.Weapon, EquipmentSlot.PrimaryTwo, EquipmentSlot.Sidearm, EquipmentSlot.Melee,
            EquipmentSlot.Helmet, EquipmentSlot.Armor, EquipmentSlot.Pack, EquipmentSlot.Rig
        };

        internal bool HasDeploymentResources(int site)
        {
            if (site != Deployment.Harbor && site != Deployment.Quarry) return false;
            if (site == Deployment.Harbor && (!Ready(PortRaster.Chart) || !Ready(PortRaster.Ground))) return false;
            foreach (PortRaster.Finish finish in Enum.GetValues(typeof(PortRaster.Finish)))
                if (!Ready(PortRaster.Surface(finish, false)) || !Ready(PortRaster.Surface(finish, true))) return false;
            return true;

            static bool Ready(string path)
            {
                var texture = Resources.Load<Texture2D>(path);
                return texture != null && texture.width == PortRaster.Resolution && texture.height == PortRaster.Resolution;
            }
        }

        static void ClearContent(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
        }

        void BuildPortrait(RectTransform portrait)
        {
            var art = Stretch("Full body portrait", portrait).gameObject.AddComponent<RawImage>();
            art.raycastTarget = false;
            operatorPreview = new OperatorPreview(transform, art, session.BoundBlade);
            operatorPreview.Select(session.SelectedOperator);
            operatorPreview.SyncGear(session.Loadout);
        }

        void DrawPreparedSummary(RectTransform root, float width)
        {
            var bag = session.Loadout;
            Label(root, "装备价值  " + bag.EquipmentValue.ToString("N0"), 18, 11, 55, width - 22, 28, color: Aqua);
            Label(root, "负重  " + bag.Weight.ToString("0.0") + " / " + bag.MaxWeight.ToString("0.#") + " 千克",
                16, 11, 85, width - 22, 24, color: Muted);
            for (int i = 0; i < LoadoutSlots.Hands.Length; i++)
            {
                var slot = LoadoutSlots.Hands[i];
                string name = ItemName(bag.Equipped(slot), slot == EquipmentSlot.Melee ? session.BoundBladeName : "空槽");
                Label(root, SlotName(slot), 14, 11, 122 + 51 * i, width - 22, 18, color: Muted);
                var title = Label(root, name, 17, 11, 142 + 51 * i, width - 22, 25, TextAnchor.MiddleLeft);
                title.resizeTextForBestFit = true;
                title.resizeTextMinSize = 12;
                title.resizeTextMaxSize = 17;
            }
        }

        void OpenStrategicMap()
        {
            int site = PlayerPrefs.GetInt(LastSiteKey, -1), tier = PlayerPrefs.GetInt(LastTierKey, 0);
            if (!Deployment.TryDifficulty(site, tier, out _))
            {
                tier = 0;
                if (!Deployment.TryDifficulty(site, tier, out _)) site = -1;
            }
            ShowStrategicMap(site, tier);
        }

        void ShowStrategicMap(int site, int tier)
        {
            if (session.Phase != SessionPhase.Menu) return;
            var root = Display(ScreenPage.Strategy, "Strategic map", wide: true);
            strategySite = site;
            strategyTier = tier;
            strategyFocused = site >= 0;
            expandedTask = null;
            SetPageRestore(() => ShowStrategicMap(site, tier));
            Build();
            reflowWidePage = RefreshStrategicDetails;

            void Build()
            {
                strategyView = Stretch("Strategic terrain", root);
                var image = strategyView.gameObject.AddComponent<RawImage>();
                image.raycastTarget = false;
                strategyTerrain = new StrategyTerrain(transform, image);
                strategyMarkers = new RectTransform[Deployment.Sites.Length];
                for (int i = 0; i < strategyMarkers.Length; i++)
                {
                    int index = i;
                    var marker = Anchor("Strategic marker / " + index, strategyView, .5f, .5f);
                    strategyMarkers[i] = marker;
                    var siteInfo = Deployment.Sites[i];
                    var ground = Button(marker, "", -42, -50, 84, 57, () => SelectStrategicSite(index), background: Color.clear);
                    ground.name = "Landmark / " + siteInfo.Name;
                    ground.transition = Selectable.Transition.None;
                    Button(marker, siteInfo.Name, -86, -97, 172, 36, () => SelectStrategicSite(index),
                        background: siteInfo.Open ? new Color32(27, 43, 54, 246) : new Color32(19, 27, 34, 245));
                }
                var header = At("Strategic header", root, 0, 0, 0, 51);
                header.anchorMax = new Vector2(1f, 1f);
                Fill(header, new Color32(11, 21, 30, 235));
                AddMenuBack(header);
                Label(header, "战略地图", 23, 127, 3, 180, 43, TextAnchor.MiddleLeft);
                var region = Label(header, "北境行动区", 15, -226, 4, 212, 43, TextAnchor.MiddleRight, Muted).rectTransform;
                region.anchorMin = region.anchorMax = Vector2.one;
                strategySidebar = Stretch("Strategic information", root);
                strategyTerrain.Focus(strategyFocused ? strategySite : -1);
                RefreshStrategicDetails();
                UpdateStrategicMap();
            }
        }

        void SelectStrategicSite(int site)
        {
            if (current != ScreenPage.Strategy || site < 0 || site >= Deployment.Sites.Length || ProcessInventoryBackKey()) return;
            if (strategySite != site) { strategyTier = 0; expandedTask = null; }
            strategySite = site;
            strategyFocused = true;
            strategyTerrain.Focus(site);
            RememberMap();
            RefreshStrategicDetails();
        }

        void RememberMap()
        {
            if (!Deployment.TryDifficulty(strategySite, strategyTier, out _)) return;
            PlayerPrefs.SetInt(LastSiteKey, strategySite);
            PlayerPrefs.SetInt(LastTierKey, strategyTier);
            PlayerPrefs.Save();
        }

        void FocusStrategicOverview()
        {
            strategyFocused = false;
            strategyTerrain.Focus(-1);
            RefreshStrategicDetails();
        }

        void UpdateStrategicMap()
        {
            if (strategyTerrain == null || strategyMarkers == null) return;
            strategyTerrain.Tick(Time.unscaledDeltaTime);
            for (int i = 0; i < strategyMarkers.Length; i++)
            {
                var pin = strategyTerrain.Pin(i);
                var marker = strategyMarkers[i];
                bool inView = pin.x > .035f && pin.x < .965f && pin.y > .075f && pin.y < .79f;
                SetActive(marker.gameObject, inView);
                if (inView) marker.anchorMin = marker.anchorMax = pin;
            }
            if (strategyPreview != null && Time.unscaledTime >= previewRetryAt
                && (strategyPreview.texture == null || strategyPreview.texture is RenderTexture frame && !frame.IsCreated()))
                RefreshStrategicPreview();
        }

        void RefreshStrategicPreview()
        {
            strategyPreview.texture = strategyTerrain.Preview(strategySite);
            strategyPreview.enabled = strategyPreview.texture != null;
            previewRetryAt = Time.unscaledTime + .75f;
        }

        void ReleaseStrategicMap()
        {
            strategyTerrain?.Dispose();
            strategyTerrain = null;
            strategyView = strategySidebar = null;
            strategyPreview = null;
            strategyMarkers = null;
            strategyFocused = false;
        }

        void RefreshStrategicDetails()
        {
            if (strategySidebar == null) return;
            strategyPreview = null;
            ClearContent(strategySidebar);
            foreach (var marker in strategyMarkers)
            {
                var old = marker.Find("Site highlight");
                if (old != null) { old.gameObject.SetActive(false); old.SetParent(null); Destroy(old.gameObject); }
            }
            float w = panelRoot.rect.width;
            if (!strategyFocused)
            {
                var mode = At("Action modes", strategySidebar, 12, 66, 139, 143);
                Fill(mode, Ink);
                Label(mode, "行动模式", 17, 8, 8, 123, 31, TextAnchor.MiddleCenter, Muted);
                Button(mode, "离线突袭", 8, 46, 123, 41, null, true);
                Button(mode, "离线靶场", 8, 94, 123, 41, session.StartPractice);
                return;
            }
            var site = Deployment.Sites[strategySite];
            var highlight = At("Site highlight", strategyMarkers[strategySite], -48, -47, 96, 73);
            var ring = Fill(At("Location ring", highlight, 7, 20, 82, 42), Aqua);
            ring.sprite = icons[RaidIcon.ControlRing];
            for (int i = 0; i < 4; i++)
            {
                var angle = i * 90f;
                var mark = Anchor("Location marker", highlight, .5f, .44f);
                mark.sizeDelta = Vector2.one * 12f;
                var offset = Quaternion.Euler(0f, 0f, angle) * new Vector3(0, 1, 0);
                mark.anchoredPosition = new Vector2(offset.x * 44f, offset.y * 25f);
                mark.localRotation = Quaternion.Euler(0, 0, angle + 180f);
                Fill(mark, Aqua).sprite = icons[RaidIcon.Heading];
            }
            if (!string.IsNullOrEmpty(site.LocalTime))
            {
                Label(highlight, site.LocalTime, 15, 23, -98, 66, 20, TextAnchor.MiddleCenter, Pale);
                var weather = Fill(At("Clear weather", highlight, 13, -98, 19, 19), Amber);
                weather.sprite = icons[RaidIcon.Sun];
            }
            if (site.Tasks.Length > 0)
            {
                var tasks = At("Location tasks", strategySidebar, 12, 68, 231, 242);
                Fill(tasks, Ink);
                Label(tasks, "地点任务", 20, 12, 6, 203, 36, TextAnchor.MiddleLeft, Aqua);
                float y = 48;
                foreach (string task in site.Tasks)
                {
                    Button(tasks, task, 8, y, 215, 41, () => { expandedTask = expandedTask == task ? null : task; RefreshStrategicDetails(); }, expandedTask == task);
                    y += 45;
                    if (expandedTask == task)
                    {
                        Label(tasks, task.StartsWith("撤离", StringComparison.Ordinal) ? "开启对局地图查看可用撤离点。" : "搜索该地标附近的物资容器。",
                            15, 12, y, 204, 56, color: Muted);
                        y += 58;
                    }
                }
            }
            const float panelW = 310;
            var card = At("Site details", strategySidebar, w - panelW - 12, 68, panelW, 594);
            Fill(card, new Color32(14, 25, 34, 250), true);
            Label(card, site.Name, 25, 12, 8, panelW - 24, 42, TextAnchor.MiddleLeft);
            if (!site.Open)
            {
                Label(card, site.DisabledReason, 20, 14, 78, panelW - 28, 84, color: Muted);
                return;
            }
            for (int i = 0; i < site.Difficulties.Length; i++)
            {
                int tier = i;
                Button(card, site.Difficulties[i].Name, 12 + i * 145, 54, 140, 41, () =>
                {
                    strategyTier = tier;
                    RememberMap();
                    RefreshStrategicDetails();
                }, i == strategyTier);
            }
            strategyPreview = At("Scene preview", card, 12, 108, panelW - 24, 163).gameObject.AddComponent<RawImage>();
            strategyPreview.raycastTarget = false;
            RefreshStrategicPreview();
            var diff = site.Difficulties[strategyTier];
            Label(card, site.Description, 16, 14, 283, panelW - 28, 69, color: Muted);
            Label(card, "行动难度    " + diff.Name + "\n最低装备价值    " + diff.RequiredValue.ToString("N0")
                + "\n等级要求    " + diff.RequiredLevel + "\n行动时限    " + (diff.Seconds / 60) + " 分钟",
                19, 14, 364, panelW - 28, 128, color: Pale);
            string blocked = Deployment.EntryError(strategySite, strategyTier, session.Loadout.EquipmentValue, session.PlayerLevel);
            if (blocked != null) Label(card, blocked, 16, 14, 498, panelW - 28, 32, color: Amber);
            Button(card, "开始行动", 12, 538, panelW - 24, 44, () =>
            {
                if (current != ScreenPage.Strategy || ProcessInventoryBackKey()) return;
                int selectedSite = strategySite, selectedTier = strategyTier;
                SetPageRestore(() => ShowStrategicMap(selectedSite, selectedTier));
                ShowPreparation(selectedSite, selectedTier);
            }, true);
        }

        void ShowPreparation(int site, int tier)
        {
            if (session.Phase != SessionPhase.Menu || !Deployment.TryDifficulty(site, tier, out var difficulty)) return;
            var root = Display(ScreenPage.Preparation, "Loadout preparation", wide: true);
            int selection = -1, pageIndex = 0;
            bool shop = false;
            RectTransform picker = null;
            SetPageRestore(() => ShowPreparation(site, tier));
            closePreparationPicker = () =>
            {
                if (selection < 0) return false;
                selection = -1;
                DrawPicker();
                return true;
            };
            reflowWidePage = Build;
            Build();

            void Build()
            {
                RemovePopup();
                operatorPreview?.Dispose();
                ClearContent(root);
                picker = null;
                float w = root.rect.width, leftW = Mathf.Min(w * .29f, 328f);
                WideHeader(root, "战备准备", Deployment.Sites[site].Name + " / " + difficulty.Name, true);
                var left = At("Preparation slots", root, 12, 59, leftW, 608);
                Fill(left, Ink);
                var bag = session.Loadout;
                Label(left, "装备价值 " + bag.EquipmentValue.ToString("N0"), 17, 8, 2, leftW - 16, 27, color: Aqua);
                Label(left, "负重 " + bag.Weight.ToString("0.0") + " / " + bag.MaxWeight.ToString("0.#") + " 千克",
                    16, 8, 28, leftW - 16, 25, color: Muted);
                for (int i = 0; i < PreparationSlots.Length; i++)
                {
                    int index = i;
                    var slot = PreparationSlots[i];
                    bool wide = i < 2;
                    float half = (leftW - 23) * .5f, sw = wide ? leftW - 16 : half;
                    float x = wide || i % 2 == 0 ? 8 : 15 + half;
                    float y = wide ? 59 + i * 111 : 281 + (i - 2) / 2 * 81;
                    float height = wide ? 104 : 74;
                    var button = Button(left, "", x, y, sw, height, () => Pick(index), background: selection == i ? Border : Plate);
                    button.name = "Prepare slot / " + slot;
                    Label(button.transform, SlotName(slot), 15, 6, 3, sw - 12, 19, TextAnchor.MiddleLeft, Muted);
                    var item = bag.Equipped(slot);
                    var def = session.Catalog.Find(item?.ItemId);
                    if (def != null) DrawItemIcon(button.transform, def, item, 7, 23, sw - 14, height - 47);
                    else if (slot == EquipmentSlot.Melee)
                        DrawItemIcon(button.transform, null, null, 7, 23, sw - 14, height - 47, blade: session.BoundBlade.Form);
                    else DrawSilhouette(button.transform, slot, sw * .22f, 23, sw * .56f, height - 48, Muted, "Empty equipment outline");
                    Label(button.transform, ItemName(item, slot == EquipmentSlot.Melee ? session.BoundBladeName : "空槽"),
                        13, 6, height - 23, sw - 12, 21, TextAnchor.MiddleLeft);
                }
                Button(left, "药品", 8, 535, (leftW - 23) * .5f, 45, () => Pick(8));
                Button(left, "弹药", (leftW + 7) * .5f, 535, (leftW - 23) * .5f, 45, () => Pick(9));
                float right = w - 240;
                float middleLeft = 24 + leftW, middleWidth = right - 12 - middleLeft;
                float portraitW = Mathf.Min(535f, middleWidth);
                var portrait = At("Prepared operator", root, middleLeft + (middleWidth - portraitW) * .5f, 57, portraitW, 600);
                BuildPortrait(portrait);
                Button(portrait, "切换正背面", (portraitW - 145) * .5f, 549, 145, 37, () => operatorPreview?.RotateView());
                Label(root, "整备要求", 24, right, 119, 219, 39, color: Aqua);
                Label(root, "最低装备价值  " + difficulty.RequiredValue.ToString("N0") + "\n等级要求  " + difficulty.RequiredLevel,
                    17, right, 163, 220, 76, color: Muted);
                Label(root, "确认后返回大厅，\n点出发才加载关卡。", 16, right, 535, 220, 57, color: Muted);
                Button(root, "确认配装", right, 605, 225, 54, () => Confirm(false), true);
                DrawPicker();
            }

            void Pick(int index)
            {
                if (root != panelRoot || ProcessInventoryBackKey()) return;
                selection = index; shop = false; pageIndex = 0;
                DrawPicker();
            }

            void DrawPicker()
            {
                RemovePopup();
                if (picker != null) { picker.gameObject.SetActive(false); Destroy(picker.gameObject); }
                if (selection < 0) return;
                float w = root.rect.width, px = Mathf.Min(w * .29f, 328f) + 24f;
                float panelW = Mathf.Min(352f, w - px - 255f);
                var slot = selection < PreparationSlots.Length ? PreparationSlots[selection] : EquipmentSlot.None;
                var supply = selection == 8 ? ItemKind.Medicine : ItemKind.Ammo;
                string title = slot == EquipmentSlot.None ? (supply == ItemKind.Medicine ? "补充药品" : "补充兼容弹药") : SlotName(slot);
                picker = At("Equipment picker", root, px, 74, panelW, 497);
                Fill(picker, new Color32(15, 25, 32, 250), true);
                Label(picker, title, 20, 10, 2, panelW - 68, 38, TextAnchor.MiddleLeft);
                Button(picker, "关闭", panelW - 60, 4, 52, 32, () => { selection = -1; DrawPicker(); });
                float bucket = (panelW - 27) * .5f;
                Button(picker, "仓库持有", 8, 47, bucket, 40, () => { shop = false; pageIndex = 0; DrawPicker(); }, !shop);
                Button(picker, "系统商店", 18 + bucket, 47, bucket, 40, () => { shop = true; pageIndex = 0; DrawPicker(); }, shop);
                var choices = new List<(ItemDefinition def, ItemStack item)>();
                if (shop)
                {
                    var stock = session.Catalog.Items.FindAll(def => !def.NotForSale && Compatible(def));
                    stock.Sort((a, b) => a.Tier != b.Tier ? a.Tier.CompareTo(b.Tier) : a.Value != b.Value ? a.Value.CompareTo(b.Value) : string.CompareOrdinal(a.Id, b.Id));
                    foreach (var def in stock) choices.Add((def, null));
                }
                else foreach (var item in session.Stash.Items)
                {
                    var def = session.Catalog.Find(item.ItemId);
                    if (item.Slot == EquipmentSlot.None && Compatible(def)) choices.Add((def, item));
                }
                const int perPage = 6;
                int pages = Mathf.Max(1, (choices.Count + perPage - 1) / perPage);
                pageIndex = Mathf.Clamp(pageIndex, 0, pages - 1);
                for (int i = 0; i < perPage && pageIndex * perPage + i < choices.Count; i++)
                {
                    var choice = choices[pageIndex * perPage + i];
                    ItemDefinition def = choice.def;
                    ItemStack item = choice.item;
                    var button = Button(picker, "", 8, 98 + 54 * i, panelW - 16, 50, null);
                    button.name = "Prepare option / " + (item?.InstanceId ?? def.Id);
                    DrawItemIcon(button.transform, def, item, 4, 3, 49, 43);
                    Label(button.transform, def.DisplayName, 15, 59, 1, panelW - 77, 24, TextAnchor.MiddleLeft);
                    int amount = item?.Quantity ?? (slot == EquipmentSlot.None ? PurchaseAmount(def) : 1);
                    string price = item == null ? ((long)def.Value * amount).ToString("N0") + " 信用点" : "仓库持有 ×" + amount;
                    Label(button.transform, price, 14, 59, 25, panelW - 77, 20, TextAnchor.MiddleLeft, Amber);
                    var anchor = (RectTransform)button.transform;
                    button.onClick.AddListener(() => ShowOption(anchor, def, item, amount, slot));
                }
                if (choices.Count == 0) Label(picker, "没有符合条件的物品。", 17, 12, 118, panelW - 24, 43, color: Muted);
                Button(picker, "上一页", 8, 446, 82, 37, () => { pageIndex--; DrawPicker(); }).interactable = pageIndex > 0;
                Label(picker, (pageIndex + 1) + "/" + pages, 15, 91, 446, panelW - 182, 37, TextAnchor.MiddleCenter);
                Button(picker, "下一页", panelW - 90, 446, 82, 37, () => { pageIndex++; DrawPicker(); }).interactable = pageIndex + 1 < pages;

                bool Compatible(ItemDefinition def)
                {
                    if (def == null) return false;
                    if (slot != EquipmentSlot.None)
                        return def.Equipment == slot || slot == EquipmentSlot.PrimaryTwo && def.Equipment == EquipmentSlot.Weapon;
                    if (def.Kind != supply) return false;
                    if (supply != ItemKind.Ammo) return true;
                    foreach (var hand in LoadoutSlots.Hands)
                    {
                        var gun = session.Loadout.Equipped(hand);
                        if (WeaponRules.CanLoad(session.Catalog.Find(gun?.ItemId)?.Weapon, def, out _)) return true;
                    }
                    return false;
                }
            }

            void ShowOption(RectTransform anchor, ItemDefinition def, ItemStack item, int amount, EquipmentSlot slot)
            {
                if (root != panelRoot || !anchor.gameObject.activeInHierarchy || ProcessInventoryBackKey()) return;
                var popup = ItemPopup(root, anchor, def, item, null);
                bool buying = item == null;
                int price = def.Value;
                if (buying) Label(popup, "价格 " + ((long)price * amount).ToString("N0") + " / 余额 " + session.Profile.Credits.ToString("N0"),
                    15, 12, 164, 304, 26, TextAnchor.MiddleLeft, Amber);
                popup.sizeDelta = new Vector2(330, buying ? 244 : 216);
                string text = slot == EquipmentSlot.None ? (buying ? "确认购买并携带" : "转入随身栏") : (buying ? "确认购买并穿戴" : "穿戴此物品");
                Button(popup, text, 12, buying ? 194 : 164, 306, 41, () =>
                {
                    if (itemPopup != popup || ProcessInventoryBackKey()) return;
                    string id = item?.InstanceId ?? def.Id;
                    string error;
                    bool ok = slot == EquipmentSlot.None ? session.TryPrepareSupply(id, amount, buying, price, out error)
                        : session.TryPrepareSlot(slot, id, buying, price, out error);
                    if (!ok) { Notify(error ?? "无法完成配装。"); return; }
                    Build();
                }, true);
                PlaceItemPopup(popup, root, anchor);
            }

            void Confirm(bool proceed)
            {
                if (root != panelRoot || ProcessInventoryBackKey()) return;
                var issue = Deployment.EntryError(site, tier, session.Loadout.EquipmentValue, session.PlayerLevel);
                if (issue != null) { Notify(issue); return; }
                bool ammo = NeedsAmmo(), medicine = session.QuickMedicine() == null;
                if (!proceed && (ammo || medicine))
                {
                    RemovePopup();
                    var box = At("Deployment reminders", root, (root.rect.width - 366) * .5f, 186, 366, 276);
                    itemPopup = box;
                    Fill(box, Ink, true);
                    Label(box, "入局提醒", 25, 13, 8, 340, 38, color: Amber);
                    float y = 54;
                    if (ammo) { Button(box, "未携带适配弹药 ›", 12, y, 342, 44, () => Pick(9)); y += 49; }
                    if (medicine) Button(box, "未携带可用药品 ›", 12, y, 342, 44, () => Pick(8));
                    Button(box, "继续确认", 12, 211, 165, 46, () => { if (itemPopup == box) Confirm(true); }, true);
                    Button(box, "返回配装", 189, 211, 165, 46, () => CloseItemPopup());
                    return;
                }
                if (session.ConfirmDeployment(site, tier)) ShowMenu();
            }

            bool NeedsAmmo()
            {
                foreach (var hand in LoadoutSlots.Hands)
                {
                    var gun = session.Loadout.Equipped(hand);
                    var def = session.Catalog.Find(gun?.ItemId)?.Weapon;
                    if (def == null || def.Melee) continue;
                    if (gun.LoadedAmmo > 0) continue;
                    bool hasReserve = false;
                    foreach (var stack in session.Loadout.Items)
                    {
                        if (!GridInventory.FieldSupply(stack) || !WeaponRules.CanLoad(def, session.Catalog.Find(stack.ItemId), out _)) continue;
                        hasReserve = true;
                        break;
                    }
                    if (!hasReserve) return true;
                }
                return false;
            }
        }
    }
}
