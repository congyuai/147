using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Dustline
{
    public sealed partial class GameUI
    {
        ItemPreviewStage gunsmithStage;
        RenderTexture gunsmithTexture;
        RawImage gunsmithImage;
        Button workshopConfirm;
        Text workshopRenderMessage;
        WorkshopGesture workshopGesture;
        Action projectWorkshopPins;
        bool gunsmithDirty;
        static readonly Vector2[] PinOffsets =
        {
            new Vector2(0, -49), new Vector2(67, 36), new Vector2(52, -39), new Vector2(-49, -39), new Vector2(-57, 42),
            new Vector2(0, 53), new Vector2(82, -56), new Vector2(-82, -56), new Vector2(82, 56), new Vector2(-82, 56),
            new Vector2(110, 0), new Vector2(-110, 0)
        };

        readonly struct InventoryChoice
        {
            internal readonly GridInventory Bag;
            internal readonly string Id;
            internal InventoryChoice(GridInventory bag, string id) { Bag = bag; Id = id; }
            internal ItemStack Item => Bag?.Find(Id);
        }

        sealed class WorkshopOption
        {
            internal ItemDefinition Part;
            internal int Level, Cost, Count;
            internal bool Installed;
            internal bool Available => Part == null || !Part.NotForSale || Count > 0;
            internal string Name;
        }

        sealed class WorkshopState
        {
            internal PartSlot? Slot;
            internal WorkshopOption Preview;
            internal bool Expanded;
            internal float CardScroll, ViewZoom = 1f;
            internal Vector2 ViewDrag;

            internal void ResetView()
            {
                ViewDrag = Vector2.zero;
                ViewZoom = 1f;
            }
        }

        sealed class WorkshopPin
        {
            internal RectTransform Dot, Card, Line;
            internal Image Face;
            internal Text Caption;
            internal Rect Hit, Region;
            internal Vector2 Target;
            internal bool Visible;
        }

        readonly struct WorkshopMetric
        {
            internal readonly string Name, Value, Unit;
            internal readonly bool Changed;
            internal WorkshopMetric(string name, float before, float after, string unit)
            {
                Name = name; Unit = unit;
                string a = Format(before), b = Format(after);
                Changed = Mathf.Abs(before - after) > .0005f && a != b;
                Value = Changed ? a + " → " + b : b;
            }

            static string Format(float value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            internal string Readout => Name + " " + Value + (Unit.Length == 0 ? "" : " " + Unit);
        }

        static string PartName(PartSlot slot) => slot switch
        {
            PartSlot.Sight => "瞄具", PartSlot.Grip => "握把", PartSlot.Stock => "枪托",
            PartSlot.Muzzle => "枪口件", PartSlot.Magazine => "弹匣", _ => "配件"
        };

        static string FactoryName(PartSlot slot, int level)
        {
            if (level == 0) return "原装" + PartName(slot);
            return (slot, level) switch
            {
                (PartSlot.Sight, 1) => "原厂导光瞄具", (PartSlot.Sight, _) => "原厂远望瞄具",
                (PartSlot.Grip, 1) => "原厂纹理握把", (PartSlot.Grip, _) => "原厂包覆握把",
                (PartSlot.Stock, 1) => "原厂轻架枪托", _ => "原厂稳肩枪托"
            };
        }

        void ShowGunsmith(GridInventory selectedBag = null, string selectedId = null) => BuildGunsmith(selectedBag, selectedId, null);

        void BuildGunsmith(GridInventory selectedBag, string selectedId, WorkshopState state)
        {
            if (session.Phase != SessionPhase.Menu) return;
            var panel = Display(ScreenPage.Gunsmith, "Gunsmith", wide: true);
            var guns = new List<InventoryChoice>();
            AddGuns(session.Loadout); AddGuns(session.Stash);
            int index = guns.FindIndex(choice => choice.Bag == selectedBag && choice.Id == selectedId);
            if (index < 0 && selectedId != null) index = guns.FindIndex(choice => choice.Id == selectedId);
            if (index < 0) { index = 0; state = null; }
            state ??= new WorkshopState();
            string renderedId = null;
            RectTransform surface = null, tray = null, statsPanel = null, partFrame = null, confirmation = null;
            ScrollRect candidatesScroll = null;
            Text title = null, total = null, note = null, previewHeader = null;
            Button cancel = null, statsButton = null;
            var pins = new WorkshopPin[(int)PartSlot.Magazine + 1];
            SetPageRestore(() =>
            {
                if (guns.Count == 0) ShowGunsmith();
                else
                {
                    var choice = guns[index];
                    BuildGunsmith(choice.Bag, choice.Id, state);
                }
            }, CaptureCardScroll);
            reflowWidePage = Build;
            Build();

            void CaptureCardScroll()
            {
                if (candidatesScroll != null) state.CardScroll = Mathf.Clamp01(candidatesScroll.horizontalNormalizedPosition);
            }

            void AddGuns(GridInventory bag)
            {
                foreach (var slot in LoadoutSlots.Hands)
                {
                    var equipped = bag.Equipped(slot);
                    if (equipped != null) Add(equipped);
                }
                foreach (var item in bag.Items) if (!LoadoutSlots.IsHand(item.Slot)) Add(item);

                void Add(ItemStack item)
                {
                    var weapon = session.Catalog.Find(item.ItemId)?.Weapon;
                    if (weapon != null && !weapon.Melee) guns.Add(new InventoryChoice(bag, item.InstanceId));
                }
            }

            void Build()
            {
                CaptureCardScroll();
                candidatesScroll = null;
                if (statsPanel != null && !statsPanel.gameObject.activeSelf) Destroy(statsPanel.gameObject);
                Clear(panel);
                float width = panel.rect.width;
                WideHeader(panel, "枪械改装", "单指旋转 · 双指缩放 · 点枪模选槽", true);
                Fill(At("Workshop footer", panel, 0, 630, width, 50), Ink);
                Button(panel, "配件商店", 12, 634, 127, 41, () => ShowShop(ItemKind.Part));
                if (guns.Count == 0)
                {
                    Label(panel, "还没有可改装的枪械", 26, 13, 205, width - 26, 58, TextAnchor.MiddleCenter);
                    Label(panel, "可在补给商店购买，或从港区带回。", 19, 13, 270, width - 26, 43, TextAnchor.MiddleCenter, Muted);
                    return;
                }

                float viewWidth = width - 24f;
                const float viewHeight = 423f;
                surface = At("Workshop model viewport", panel, 12, 57, viewWidth, viewHeight);
                Fill(surface, new Color32(31, 46, 53, 255), true);
                surface.gameObject.AddComponent<RectMask2D>();
                var art = Stretch("Rotatable weapon", surface);
                gunsmithImage = art.gameObject.AddComponent<RawImage>();
                gunsmithImage.color = Color.clear;
                gunsmithImage.raycastTarget = false;
                partFrame = At("Selected model part", surface, 0, 0, 0, 0);
                partFrame.gameObject.AddComponent<CanvasRenderer>();
                partFrame.gameObject.AddComponent<PartFrame>();
                partFrame.gameObject.SetActive(false);
                if (gunsmithStage == null) gunsmithStage = new ItemPreviewStage(transform, 1);
                int textureWidth = Mathf.RoundToInt(Mathf.Min(1600f, viewWidth));
                int textureHeight = Mathf.RoundToInt(textureWidth * viewHeight / viewWidth);
                if (gunsmithTexture == null || gunsmithTexture.width != textureWidth || gunsmithTexture.height != textureHeight)
                {
                    ItemIconCache.Release(gunsmithTexture);
                    gunsmithTexture = ItemIconCache.Texture(textureWidth, textureHeight, 24);
                }
                gunsmithImage.texture = gunsmithTexture;
                workshopGesture = surface.gameObject.AddComponent<WorkshopGesture>();
                workshopGesture.Rotate = delta =>
                {
                    // Store gesture totals relative to the preview stage's neutral view.
                    state.ViewDrag.x = Mathf.Repeat(state.ViewDrag.x + delta.x, 360f / .55f);
                    state.ViewDrag.y = Mathf.Clamp(state.ViewDrag.y + delta.y, (7f - 75f) / .44f, (7f + 75f) / .44f);
                    gunsmithStage.Rotate(delta);
                    gunsmithDirty = true;
                };
                workshopGesture.Zoom = scale =>
                {
                    state.ViewZoom = Mathf.Clamp(state.ViewZoom * scale, .76f, 1.58f);
                    gunsmithStage.Zoom(scale);
                    gunsmithDirty = true;
                };
                workshopGesture.Tap = PickPin;
                for (int n = 0; n < pins.Length; n++) pins[n] = CreatePin((PartSlot)n);
                foreach (var pin in pins) pin.Card.SetAsLastSibling();
                projectWorkshopPins = ProjectPins;
                workshopRenderMessage = Label(surface, "", 22, 12, 167, viewWidth - 24f, 50, TextAnchor.MiddleCenter, Muted);
                Fill(At("Model frame top", surface, 0, 0, viewWidth, 2), Border);
                Fill(At("Model frame bottom", surface, 0, viewHeight - 2, viewWidth, 2), Border);
                title = Label(surface, "", 25, 14, 7, viewWidth * .38f - 16, 38, TextAnchor.MiddleLeft);
                title.resizeTextForBestFit = true;
                title.resizeTextMinSize = 17;
                title.resizeTextMaxSize = 25;
                total = Label(surface, "", 16, 15, 50, 208, 26, TextAnchor.MiddleLeft, Muted);
                previewHeader = Label(surface, "", 16, viewWidth * .39f, 9, viewWidth * .61f - 234, 33, TextAnchor.MiddleCenter, Aqua);
                Button(surface, "上一把", viewWidth - 225, 11, 103, 37, () => ChangeGun(-1)).interactable = guns.Count > 1;
                Button(surface, "下一把", viewWidth - 114, 11, 103, 37, () => ChangeGun(1)).interactable = guns.Count > 1;
                Button(surface, "重置视角", viewWidth - 114, viewHeight - 47, 103, 36, () =>
                {
                    state.ResetView();
                    gunsmithStage.ResetView();
                    gunsmithDirty = true;
                });

                float compareWidth = width - 156f;
                note = Label(panel, "", 21, 14, 481, compareWidth - 10, 41, TextAnchor.MiddleLeft, Aqua);
                note.name = "Workshop comparison";
                note.resizeTextForBestFit = true;
                note.resizeTextMinSize = 15;
                note.resizeTextMaxSize = 21;
                statsButton = Button(panel, "完整参数", width - 130, 483, 118, 36, () => { state.Expanded = !state.Expanded; DrawMetrics(); });
                tray = At("Workshop candidates", panel, 12, 525, viewWidth, 100);
                Fill(tray, Ink);
                statsPanel = At("Workshop parameters", panel, width - 343, 111, 320, 340);
                cancel = Button(panel, "取消预览", width - 351, 634, 130, 41, () => { CaptureCardScroll(); state.Preview = null; Refresh(); });
                confirmation = At("Installation confirmation", panel, width - 209, 634, 197, 41);
                Refresh();
            }

            WorkshopPin CreatePin(PartSlot slot)
            {
                var line = At("Part leader / " + slot, surface, 0, 0, 0, 1.4f);
                Fill(line, Aqua);
                var dot = Anchor("Part hotspot / " + slot, surface, 0, 1);
                dot.sizeDelta = new Vector2(32, 32);
                var ring = Fill(dot, Aqua);
                ring.sprite = icons[RaidIcon.ControlRing];
                var centre = Anchor("Hotspot pin", dot, .5f, .5f);
                centre.sizeDelta = new Vector2(5, 5);
                Fill(centre, Pale);
                var card = At("Part caption / " + slot, surface, 0, 0, 69, 29);
                Fill(card, new Color32(17, 29, 35, 244));
                var caption = Label(card, PartName(slot), 15, 1, 0, 67, 29, TextAnchor.MiddleCenter, Pale);
                SetActive(dot.gameObject, false);
                SetActive(card.gameObject, false);
                SetActive(line.gameObject, false);
                return new WorkshopPin { Dot = dot, Card = card, Line = line, Face = ring, Caption = caption };
            }

            void ProjectPins()
            {
                var gun = guns[index].Item;
                var definition = session.Catalog.Find(gun?.ItemId)?.Weapon;
                float w = surface.rect.width, h = surface.rect.height;
                bool frameVisible = false;
                for (int n = 0; n < pins.Length; n++)
                {
                    var slot = (PartSlot)n;
                    var marker = pins[n];
                    Vector2 uv = default;
                    Rect region = default;
                    bool visible = WeaponRules.Socket(definition, slot) != null && gunsmithStage.Project(slot, out uv, out region);
                    marker.Visible = visible;
                    SetActive(marker.Dot.gameObject, visible);
                    SetActive(marker.Card.gameObject, visible);
                    SetActive(marker.Line.gameObject, visible);
                    if (!visible) continue;
                    bool selected = slot == state.Slot;
                    var target = new Vector2(uv.x * w, (1 - uv.y) * h);
                    marker.Target = target;
                    marker.Region = new Rect(region.xMin * w, (1f - region.yMax) * h, region.width * w, region.height * h);
                    if (selected)
                    {
                        const float margin = 6f;
                        partFrame.anchoredPosition = new Vector2(marker.Region.xMin - margin, -marker.Region.yMin + margin);
                        partFrame.sizeDelta = marker.Region.size + Vector2.one * (2 * margin);
                        frameVisible = true;
                    }
                    marker.Dot.anchoredPosition = new Vector2(target.x, -target.y);
                    marker.Dot.sizeDelta = Vector2.one * (selected ? 46 : 32);
                    marker.Face.color = selected ? Amber : Aqua;
                    marker.Caption.color = selected ? Amber : Pale;
                }
                for (int n = 0; n < pins.Length; n++)
                {
                    var marker = pins[n];
                    if (!marker.Visible) continue;
                    var target = marker.Target;
                    var center = CaptionPosition(n, w, h);
                    marker.Hit = new Rect(center.x - 34.5f, center.y - 14.5f, 69, 29);
                    marker.Card.anchoredPosition = new Vector2(marker.Hit.x, -marker.Hit.y);
                    var d = center - target;
                    marker.Line.anchoredPosition = new Vector2(target.x, -target.y);
                    marker.Line.localRotation = Quaternion.Euler(0, 0, -Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                    marker.Line.sizeDelta = new Vector2(Mathf.Max(0, d.magnitude - 24), 1.4f);
                    marker.Line.GetComponent<Image>().color = (PartSlot)n == state.Slot ? Amber : Aqua;
                }
                SetActive(partFrame.gameObject, frameVisible);
            }

            Vector2 CaptionPosition(int index, float width, float height)
            {
                Vector2 wanted = pins[index].Target + PinOffsets[index];
                Vector2 best = wanted;
                float score = float.PositiveInfinity;
                for (int i = -1; i < PinOffsets.Length; i++)
                {
                    Vector2 at = i < 0 ? wanted : pins[index].Target + PinOffsets[i];
                    at.x = Mathf.Clamp(at.x, 40f, width - 40f);
                    at.y = Mathf.Clamp(at.y, 85f, height - 63f);
                    var box = new Rect(at.x - 38f, at.y - 18f, 76f, 36f);
                    float cost = (at - wanted).sqrMagnitude;
                    for (int n = 0; n < pins.Length; n++)
                    {
                        if (!pins[n].Visible) continue;
                        Vector2 t = pins[n].Target;
                        if (box.Overlaps(new Rect(t.x - 24, t.y - 24, 48, 48))) cost += 220000f;
                        if (n < index && box.Overlaps(pins[n].Hit)) cost += 220000f;
                    }
                    if (cost < score) { score = cost; best = at; }
                }
                return best;
            }

            void PickPin(Vector2 local)
            {
                if (!surface.rect.Contains(local)) return;
                var point = new Vector2(local.x, -local.y);
                for (int n = pins.Length - 1; n >= 0; n--)
                    if (pins[n].Visible && pins[n].Hit.Contains(point)) { ChooseSlot((PartSlot)n); return; }
                PartSlot? closest = null;
                float nearest = float.PositiveInfinity;
                for (int n = 0; n < pins.Length; n++)
                {
                    var marker = pins[n];
                    if (!marker.Visible) continue;
                    float distance = (marker.Target - point).sqrMagnitude;
                    if (distance <= 22f * 22f && distance < nearest) { closest = (PartSlot)n; nearest = distance; }
                }
                if (closest.HasValue) { ChooseSlot(closest.Value); return; }
                for (int n = 0; n < pins.Length; n++)
                {
                    var marker = pins[n];
                    if (!marker.Visible) continue;
                    float distance = (marker.Target - point).sqrMagnitude;
                    if (marker.Region.Contains(point) && distance < nearest) { closest = (PartSlot)n; nearest = distance; }
                }
                if (closest.HasValue) ChooseSlot(closest.Value);
            }

            void ChooseSlot(PartSlot slot)
            {
                if (state.Slot == slot) return;
                state.Slot = slot;
                state.Preview = null;
                state.CardScroll = 0;
                Refresh();
            }

            void ChangeGun(int step)
            {
                index = (index + step + guns.Count) % guns.Count;
                state.Slot = null;
                state.Preview = null;
                state.CardScroll = 0;
                state.ResetView();
                Refresh();
            }

            void Refresh()
            {
                var gun = guns[index].Item;
                if (gun == null) { ShowGunsmith(); return; }
                var def = session.Catalog.Find(gun.ItemId);
                var options = state.Slot.HasValue ? OptionsFor(gun, def, state.Slot.Value) : null;
                var prior = state.Preview;
                if (prior != null) state.Preview = options?.Find(option => !option.Installed && option.Part == prior.Part && option.Level == prior.Level);
                title.text = def.DisplayName;
                total.text = (index + 1) + " / " + guns.Count + (LoadoutSlots.IsHand(gun.Slot) ? "  · 已装备" : "  · 未装备");
                previewHeader.text = state.Preview != null ? "预览 · " + state.Preview.Name : "当前装配";
                var next = Proposed(gun, def);
                bool sameGun = renderedId == gun.InstanceId;
                gunsmithStage.Set(def, next, session.Catalog, keepAngle: sameGun);
                if (!sameGun)
                {
                    gunsmithStage.Rotate(state.ViewDrag);
                    gunsmithStage.Zoom(state.ViewZoom);
                }
                gunsmithStage.Focus(state.Slot);
                renderedId = gun.InstanceId;
                gunsmithDirty = true;
                UpdateCredits();
                DrawOptions(gun, def, options);
                DrawMetrics();
                DrawConfirmation();
            }

            void DrawConfirmation()
            {
                Clear(confirmation);
                var option = state.Preview;
                var slot = state.Slot;
                var choice = guns[index];
                var button = Button(confirmation, "确认安装", 0, 0, 197, 41, null, true);
                button.interactable = false;
                cancel.interactable = option != null;
                workshopConfirm = option != null && option.Available ? button : null;
                if (option == null || !slot.HasValue) return;
                if (!option.Available) { button.GetComponentInChildren<Text>().text = "需持有配件"; return; }
                if (option.Cost > 0) button.GetComponentInChildren<Text>().text = "确认安装 · " + option.Cost;
                button.onClick.AddListener(() =>
                {
                    // An earlier touch must not confirm a different or not-yet-rendered quote.
                    if (button != workshopConfirm || !button.IsActive() || !button.IsInteractable()) return;
                    Apply(choice, slot.Value, option);
                });
            }

            ItemStack Proposed(ItemStack gun, ItemDefinition def)
            {
                var preview = state.Preview;
                if (preview == null || !state.Slot.HasValue) return gun;
                var next = gun.Clone();
                var slot = state.Slot.Value;
                next.Parts.Set(slot, preview.Part != null ? new PartLink { ItemId = preview.Part.Id } : default);
                if (preview.Part == null && UsesFactory(def, slot))
                    WeaponTuning.TrySelect(gun.Mods, (WeaponModSlot)slot, preview.Level, out next.Mods, out _);
                return next;
            }

            List<WorkshopOption> OptionsFor(ItemStack gun, ItemDefinition def, PartSlot slot)
            {
                var installed = gun.Parts.Get(slot);
                bool factory = UsesFactory(def, slot);
                var options = new List<WorkshopOption>();
                foreach (var item in session.Catalog.CompatibleParts(def, slot))
                {
                    int count = session.Stash.Count(item.Id) + session.Loadout.Count(item.Id);
                    string name = item.DisplayName;
                    if (slot == PartSlot.Sight && item.Part.Magnification > 0) name += " · " + item.Part.Magnification + "×";
                    options.Add(new WorkshopOption { Part = item, Cost = count == 0 && !item.NotForSale ? item.Value : 0, Count = count,
                        Installed = installed.ItemId == item.Id, Name = name });
                }
                for (int level = 0; level <= (factory ? WeaponTuning.MaxUpgradeLevel : 0); level++)
                {
                    int cost = 0;
                    if (factory && !WeaponTuning.TrySelect(gun.Mods, (WeaponModSlot)slot, level, out _, out cost)) continue;
                    options.Add(new WorkshopOption { Level = level, Cost = cost, Name = FactoryName(slot, level),
                        Installed = installed.Empty && (!factory || level == WeaponTuning.Level(gun.Mods, (WeaponModSlot)slot)) });
                }
                return options;
            }

            void DrawOptions(ItemStack gun, ItemDefinition def, List<WorkshopOption> options)
            {
                Clear(tray);
                candidatesScroll = null;
                float width = tray.rect.width;
                if (options == null)
                {
                    Label(tray, "点枪上的标记，查看兼容配件。试装不会扣费或改动当前装备。", 21, 12, 7, width - 24, 84,
                        TextAnchor.MiddleCenter, Muted);
                    return;
                }
                var slot = state.Slot.Value;
                bool factory = UsesFactory(def, slot);
                const float stride = 226f;
                var content = HorizontalStrip(tray, "Compatible catalog / " + slot, 0, 1, width, 98,
                    options.Count * stride - 8f, state.CardScroll, value => state.CardScroll = value);
                candidatesScroll = content.parent.GetComponent<ScrollRect>();
                for (int i = 0; i < options.Count; i++)
                {
                    var option = options[i];
                    bool selected = state.Preview == option;
                    var card = Button(content, "", i * stride, 0, stride - 8f, 91, () =>
                    {
                        CaptureCardScroll();
                        state.Preview = option.Installed ? null : option;
                        Refresh();
                    }, false, selected ? Color.Lerp(Plate, Aqua, .31f) : Plate);
                    card.name = "Candidate / " + (option.Part != null ? option.Part.Id : "factory-" + option.Level);
                    Fill(At("Candidate rule", card.transform, 0, 0, 218, 2), selected ? Amber : option.Installed ? Aqua : Border);
                    if (option.Part != null) DrawItemIcon(card.transform, option.Part, null, 6, 11, 76, 75);
                    else if (option.Level > 0)
                    {
                        var icon = ItemIcon(card.transform, "Factory model / " + option.Level, 1f, 6, 11, 76, 75, false);
                        itemIcons.BindLegacy(icon, slot, option.Level);
                    }
                    else
                    {
                        var original = gun.Clone();
                        original.Parts.Set(slot, default);
                        if (factory) WeaponTuning.TrySelect(gun.Mods, (WeaponModSlot)slot, 0, out original.Mods, out _);
                        DrawItemIcon(card.transform, def, original, 6, 11, 76, 75);
                    }
                    var name = Label(card.transform, option.Name, 17, 87, 9, 125, 48, TextAnchor.MiddleLeft);
                    name.resizeTextForBestFit = true;
                    name.resizeTextMinSize = 13;
                    name.resizeTextMaxSize = 17;
                    string text = option.Installed ? "已安装" : option.Count > 0 ? "持有 × " + option.Count + " 件"
                        : !option.Available ? "不在系统商店出售"
                        : option.Part != null ? "商店售价 " + option.Cost : option.Cost > 0 ? "原厂售价 " + option.Cost
                        : option.Level > 0 ? "已购原厂" : "原装 · 无需购买";
                    Label(card.transform, text, 14, 87, 58, 125, 30, TextAnchor.MiddleLeft, option.Installed ? Aqua : Muted);
                }
            }

            void DrawMetrics()
            {
                var gun = guns[index].Item;
                var def = session.Catalog.Find(gun.ItemId);
                var after = Proposed(gun, def);
                var metrics = Metrics(def.Weapon, gun, after);
                string headline = "";
                int changes = 0;
                foreach (var row in metrics)
                {
                    if (!row.Changed) continue;
                    if (changes < 3) headline += (changes == 0 ? "" : "   ·   ") + row.Readout;
                    changes++;
                }
                bool previewing = state.Preview != null;
                note.text = !previewing ? metrics[0].Readout + "   ·   " + metrics[1].Readout + "   ·   "
                    + (state.Slot.HasValue ? "选择" + PartName(state.Slot.Value) : "当前配置")
                    : changes == 0 ? "仅外观变化 · 参数不变" : headline;
                note.color = previewing ? Aqua : Pale;
                statsButton.GetComponentInChildren<Text>().text = state.Expanded ? "收起参数" : "完整参数";
                SetActive(statsPanel.gameObject, state.Expanded);
                if (!state.Expanded) return;
                Clear(statsPanel);
                var backing = At("Parameter backdrop", statsPanel, 0, 0, 320, 340);
                Fill(backing, new Color32(12, 22, 29, 249), true);
                Fill(At("Parameter accent", statsPanel, 0, 0, 320, 2), Aqua);
                Label(statsPanel, previewing ? "当前 → 预览" : "当前参数", 19, 12, 3, 298, 31, TextAnchor.MiddleLeft);
                int rowIndex = 0;
                foreach (var row in metrics)
                {
                    Label(statsPanel, row.Readout, 16, 12, 33 + rowIndex * 22, 296, 22, TextAnchor.MiddleLeft, row.Changed ? Aqua : Muted);
                    rowIndex++;
                }
                Label(statsPanel, "弹药 · " + AmmoName(def.Weapon.AmmoFamily), 14, 12, 309, 296, 25, TextAnchor.MiddleLeft, Muted);
            }

            WorkshopMetric[] Metrics(WeaponDefinition def, ItemStack old, ItemStack next)
            {
                var a = WeaponTuning.Evaluate(def, old, session.Catalog);
                var b = WeaponTuning.Evaluate(def, next, session.Catalog);
                return new[]
                {
                    new WorkshopMetric("容量", WeaponRules.Capacity(def, old, session.Catalog), WeaponRules.Capacity(def, next, session.Catalog), "发"),
                    new WorkshopMetric("腰射散布", a.Spread(false) * 1000f, b.Spread(false) * 1000f, "‰"),
                    new WorkshopMetric("瞄具倍率", a.Magnification, b.Magnification, "×"),
                    new WorkshopMetric("开镜视角", a.AimFov, b.AimFov, "°"),
                    new WorkshopMetric("开镜散布", a.Spread(true) * 1000f, b.Spread(true) * 1000f, "‰"),
                    new WorkshopMetric("开镜耗时", a.AimSeconds, b.AimSeconds, "秒"),
                    new WorkshopMetric("换弹耗时", a.ReloadSeconds, b.ReloadSeconds, "秒"),
                    new WorkshopMetric("后坐幅度", a.Recoil, b.Recoil, ""),
                    new WorkshopMetric("游戏伤害", a.Damage, b.Damage, ""),
                    new WorkshopMetric("开火间隔", a.FireInterval, b.FireInterval, "秒"),
                    new WorkshopMetric("有效距离", a.Range, b.Range, "米"),
                    new WorkshopMetric("移动系数", def.MoveFactor, def.MoveFactor, "")
                };
            }

            void Apply(InventoryChoice choice, PartSlot slot, WorkshopOption option)
            {
                string error;
                bool ok = option.Part != null
                    ? session.TryFitCatalogPart(choice.Bag, choice.Id, slot, option.Part.Id, option.Cost, out error)
                    : session.TryFitWorkshopOriginal(choice.Bag, choice.Id, slot, option.Level, option.Cost, out error);
                if (current != ScreenPage.Gunsmith) return;
                if (ok) { CaptureCardScroll(); Notify("配件配置已保存。"); state.Preview = null; Refresh(); }
                else Notify(error ?? "未能更改配件。");
            }
        }

        static bool UsesFactory(ItemDefinition definition, PartSlot slot) => definition.Id == WeaponDefinition.RifleId
            && (int)slot <= (int)PartSlot.Stock && WeaponTuning.SupportsUpgrades(definition.Weapon);

        void RenderGunsmithPreview()
        {
            if (gunsmithTexture == null || (!gunsmithDirty && gunsmithTexture.IsCreated())) return;
            if (!gunsmithStage.Render(gunsmithTexture, new Color32(31, 46, 53, 255)))
            {
                gunsmithImage.color = Color.clear;
                if (workshopConfirm != null) workshopConfirm.interactable = false;
                if (workshopRenderMessage.text.Length == 0) workshopRenderMessage.text = "模型预览不可用";
                return;
            }
            gunsmithDirty = false;
            if (workshopRenderMessage.text.Length > 0) workshopRenderMessage.text = "";
            gunsmithImage.color = Color.white;
            projectWorkshopPins?.Invoke();
            if (workshopConfirm != null) workshopConfirm.interactable = true;
        }

        void DisposeGunsmithPreview()
        {
            gunsmithDirty = false;
            projectWorkshopPins = null;
            gunsmithImage = null;
            workshopConfirm = null;
            workshopRenderMessage = null;
            workshopGesture = null;
            gunsmithStage?.Dispose();
            gunsmithStage = null;
            ItemIconCache.Release(gunsmithTexture);
            gunsmithTexture = null;
        }
    }
}
