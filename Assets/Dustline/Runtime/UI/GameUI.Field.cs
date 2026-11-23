using UnityEngine;
using UnityEngine.UI;

namespace Dustline
{
    public sealed partial class GameUI
    {
        RectTransform reticleAnchor;
        TouchControl lookControl;
        Image reticle, staminaBar, progressBar, fireGlyph, interactGlyph, sprintRing;
        GameObject aimButton, reloadButton, switchButton, interactButton;
        readonly DamageCue[] damageCues = new DamageCue[4];
        int damageCursor;

        struct DamageCue
        {
            internal Image Image;
            internal Vector3 Bearing;
            internal float Born, Strength;
        }
        GameObject hintCard;
        Text staminaReadout, statusReadout, prompt, weaponMode;
        BodyFigure bodyHud;
        RectTransform weaponBar, weaponStrip;
        readonly Button[] injuryBadges = new Button[DisplayedInjuries.Length];
        readonly Text[] injuryCounts = new Text[DisplayedInjuries.Length];
        readonly RectTransform[] injuryGlyphs = new RectTransform[DisplayedInjuries.Length];
        readonly int[] shownInjuryCounts = new int[DisplayedInjuries.Length];
        float hudAt, hitUntil;

        static readonly (EquipmentSlot Slot, string Caption)[] FieldWeapons =
        {
            (EquipmentSlot.Weapon, "主武器 1"), (EquipmentSlot.PrimaryTwo, "主武器 2"), (EquipmentSlot.Sidearm, "副武器"), (EquipmentSlot.Melee, "近战")
        };
        const float WeaponCellWidth = 80, WeaponCellGap = 5, WeaponBarHeight = 76;
        const float WeaponBarWidth = 4 * WeaponCellWidth + 3 * WeaponCellGap;
        const float WeaponModelWidth = WeaponCellWidth - 8, WeaponModelHeight = 33;
        readonly WeaponSlotView[] weaponSlots = new WeaponSlotView[FieldWeapons.Length];

        sealed class WeaponSlotView
        {
            internal Button Button;
            internal Image Highlight;
            internal RawImage Model;
            internal Text Empty, Ammo;
            internal string ModelId;
            internal int Look;
        }

        void BuildHud()
        {
            var upperLeft = Anchor("Left HUD anchor", hud, 0, 1);
            var vitals = HudCard(upperLeft, "Vitals", 13, 12, 139, 172);
            var sheet = Button(vitals, "", 2, 0, 137, 137, () => { if (!BlocksGameplay) ShowHealth(); }, false, Color.clear);
            sheet.name = "Open body condition";
            sheet.transition = Selectable.Transition.None;
            bodyHud = CreateBodyFigure(vitals, "Body diagram", 38, 4, 63, 131);
            bodyHud.Initialize(part => { if (!BlocksGameplay) ShowHealth(part); });
            var summary = At("Injury summary", vitals, 90, 102, DisplayedInjuries.Length * 38, 30);
            for (int i = 0; i < DisplayedInjuries.Length; i++)
            {
                var injury = DisplayedInjuries[i];
                var badge = InjuryButton(summary, injury, 0, 0, 34, 30, () => SelectInjuryFromHud(injury));
                injuryBadges[i] = badge;
                injuryGlyphs[i] = (RectTransform)badge.transform.GetChild(0);
                var count = Label(badge.transform, "", 14, 20, 7, 13, 19, TextAnchor.MiddleCenter, Pale);
                count.name = "Injury count";
                injuryCounts[i] = count;
                badge.gameObject.SetActive(false);
            }
            Fill(At("Stamina divider", vitals, 8, 138, 123, 1), Border);
            staminaReadout = Label(vitals, "", 14, 8, 140, 123, 20, TextAnchor.MiddleLeft, Muted);
            staminaReadout.name = "Stamina value";
            staminaBar = Bar(vitals, 8, 164, 123, 4, Aqua);

            var upperRight = Anchor("Right HUD anchor", hud, 1, 1);
            miniMap = CreateMap(upperRight, -197, 18, 179, true, false);
            var status = HudCard(upperRight, "Raid status", -471, 18, 262, 120);
            statusReadout = Label(status, "", 16, 11, 6, 239, 106, TextAnchor.MiddleRight);
            reticleAnchor = Anchor("Screen centre", hud, 0.5f, 0.5f);
            reticle = Fill(At("Reticle", reticleAnchor, -22, -22, 44, 44), Pale);
            reticle.sprite = icons[RaidIcon.Reticle];
            for (int i = 0; i < damageCues.Length; i++)
            {
                var cue = Anchor("Incoming damage", reticleAnchor, 0.5f, 0.5f);
                cue.sizeDelta = new Vector2(100f, 80f);
                damageCues[i].Image = Fill(cue, new Color32(255, 88, 67, 220));
                damageCues[i].Image.sprite = icons[RaidIcon.DamageArc];
                cue.gameObject.SetActive(false);
            }

            var bottom = Anchor("Interaction anchor", hud, 0.5f, 0);
            var interaction = HudCard(bottom, "Interaction status", -234, -259, 468, 67);
            prompt = Label(interaction, "", 21, 12, 7, 444, 45, TextAnchor.MiddleCenter);
            progressBar = Bar(interaction, 14, 53, 440, 5, Aqua);
            hintCard = interaction.gameObject;
            hintCard.SetActive(false);
        }

        void BuildWeaponBar()
        {
            weaponBar = Stretch("Weapon bar", safe);
            weaponStrip = Anchor("Weapon slots", weaponBar, .5f, 0);
            weaponStrip.pivot = new Vector2(.5f, 0);
            weaponStrip.anchoredPosition = new Vector2(0, 10);
            weaponStrip.sizeDelta = new Vector2(WeaponBarWidth, WeaponBarHeight);
            weaponMode = Label(weaponStrip, "", 13, 0, -22, WeaponBarWidth, 20, TextAnchor.MiddleCenter, Pale);
            weaponMode.name = "Weapon mode";
            weaponMode.gameObject.AddComponent<Outline>().effectColor = Ink;
            for (int i = 0; i < FieldWeapons.Length; i++)
            {
                var entry = FieldWeapons[i];
                var button = Button(weaponStrip, "", i * (WeaponCellWidth + WeaponCellGap), 0, WeaponCellWidth, WeaponBarHeight,
                    () => SelectFieldWeapon(entry.Slot));
                button.name = "Weapon slot / " + entry.Slot;
                Label(button.transform, entry.Caption, 13, 3, 3, WeaponCellWidth - 6, 18, TextAnchor.MiddleCenter, Pale);
                var highlight = Fill(At("Active slot", button.transform, 0, 0, WeaponCellWidth, 2), Amber);
                var model = ItemIcon(button.transform, "Item model", 1, 4, 22, WeaponModelWidth, WeaponModelHeight, false);
                SetActive(model.gameObject, false);
                var empty = Label(button.transform, "空槽", 15, 3, 22, WeaponCellWidth - 6, WeaponModelHeight, TextAnchor.MiddleCenter, Muted);
                var ammo = Label(button.transform, "", 15, 3, 55, WeaponCellWidth - 6, 18, TextAnchor.MiddleCenter);
                ammo.name = "Weapon ammunition";
                weaponSlots[i] = new WeaponSlotView { Button = button, Highlight = highlight, Model = model, Empty = empty, Ammo = ammo };
            }
        }

        void SelectFieldWeapon(EquipmentSlot slot)
        {
            if (!BlocksGameplay && session.Player?.Weapon.SelectSlot(slot) == true) UpdateHud(true);
        }

        void UpdateWeaponBar(WeaponController weapon)
        {
            var bag = session.ActiveFieldBag;
            var catalog = session.Catalog;
            int selectable = 0;
            string activeName = "";
            for (int i = 0; i < weaponSlots.Length; i++)
            {
                var slot = FieldWeapons[i].Slot;
                var view = weaponSlots[i];
                var stack = bag.Equipped(slot);
                var definition = catalog.Find(stack?.ItemId);
                var blade = slot == EquipmentSlot.Melee && stack == null ? session.BoundBlade : null;
                bool available = definition?.Weapon != null || blade != null;
                bool active = available && weapon.ActiveSlot == slot;
                if (available) selectable++;
                if (active) activeName = definition?.DisplayName ?? session.BoundBladeName;
                view.Button.interactable = available;
                view.Button.targetGraphic.color = active ? new Color32(82, 64, 37, 247)
                    : available ? new Color32(24, 37, 42, 235) : new Color32(15, 24, 28, 196);
                SetActive(view.Highlight.gameObject, active);
                SetActive(view.Empty.gameObject, !available);
                SetActive(view.Model.gameObject, available);
                string modelId = available ? definition?.Id ?? blade.Form.ToString() : null;
                int look = available ? WeaponRules.Look(stack, catalog) : 0;
                if (view.ModelId != modelId || view.Look != look)
                {
                    view.ModelId = modelId;
                    view.Look = look;
                    if (!available) itemIcons.Unbind(view.Model);
                    else
                    {
                        float aspect = ItemIconCache.Aspect(definition);
                        float size = Mathf.Min(WeaponModelWidth / aspect, WeaponModelHeight);
                        view.Model.rectTransform.sizeDelta = new Vector2(size * aspect, size);
                        view.Model.name = "Item model / " + modelId;
                        itemIcons.Bind(view.Model, definition, stack, catalog, blade != null ? blade.Form : default);
                    }
                }
                view.Ammo.text = !available ? "" : definition?.Weapon != null && !definition.Weapon.Melee
                    ? (active && weapon.Magazine != null ? weapon.Magazine.Rounds : Mathf.Max(0, stack.LoadedAmmo)) + " / " + weapon.ReserveFor(stack)
                    : blade != null ? "绑定" : "近战";
                view.Ammo.color = active ? Amber : Pale;
            }
            weaponMode.text = weapon.Magazine == null ? activeName : activeName + " · "
                + (weapon.Magazine.Reloading ? "正在换弹" : weapon.Automatic ? "自动" : "单发")
                + " · " + (catalog.Find(weapon.ReadyAmmoType)?.DisplayName ?? "无兼容备弹");
            float fit = session.Input.TouchMode ? Mathf.Clamp((safe.rect.width * .29f - 86f) / WeaponBarWidth, .55f, 1f) : 1f;
            weaponStrip.localScale = Vector3.one * fit;
            if (session.Input.TouchMode)
            {
                var legacy = (RectTransform)switchButton.transform;
                Vector2 legacyOrigin = safe.InverseTransformPoint(legacy.TransformPoint(legacy.rect.min));
                Vector2 barOrigin = safe.InverseTransformPoint(weaponStrip.TransformPoint(weaponStrip.rect.min));
                var barBounds = new Rect(barOrigin, new Vector2(WeaponBarWidth, WeaponBarHeight + 22f) * fit);
                bool overlap = new Rect(legacyOrigin, legacy.rect.size).Overlaps(barBounds);
                SetActive(switchButton, selectable > 1 && !overlap);
            }
        }

        RectTransform HudCard(Transform parent, string title, float x, float y, float w, float h)
        {
            var root = At(title, parent, x, y, w, h);
            Fill(root, new Color32(12, 21, 26, 209));
            Fill(At("HUD rule", root, 0, 0, 2, h), new Color32(111, 195, 177, 197));
            return root;
        }

        static Image Bar(Transform parent, float x, float y, float w, float h, Color color)
        {
            var track = At("Bar track", parent, x, y, w, h);
            Fill(track, Border);
            var fill = Stretch("Bar fill", track);
            fill.pivot = new Vector2(0, 0.5f);
            return Fill(fill, color);
        }

        static void SetBar(Image bar, float fraction)
        {
            bar.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1);
        }

        void UpdateHud(bool force)
        {
            var player = session.Player;
            if (player == null) return;
            var weapon = player.Weapon;
            if (session.Input.TouchMode)
            {
                var activeIcon = icons[weapon.IsMelee ? RaidIcon.Strike : RaidIcon.Fire];
                if (fireGlyph.sprite != activeIcon) fireGlyph.sprite = activeIcon;
                bool firearm = weapon.Magazine != null;
                SetActive(aimButton, firearm);
                SetActive(reloadButton, firearm);
                SetActive(interactButton, session.Phase == SessionPhase.Raid);
                var interactionIcon = icons[player.LookingAtDoor ? RaidIcon.InteractDoor : RaidIcon.Search];
                if (interactGlyph.sprite != interactionIcon) interactGlyph.sprite = interactionIcon;
                SetActive(sprintRing.gameObject, player.Sprinting);
            }
            bool marker = Time.unscaledTime < hitUntil;
            reticle.enabled = !player.WeaponView.MedicineBusy && !MedicineWheelOpen && (marker || !session.Input.Aim && player.AimProgress < 0.08f);
            UpdateDamageCues(player.transform);
            var sprite = icons[marker ? RaidIcon.Hit : RaidIcon.Reticle];
            if (reticle.sprite != sprite)
            {
                reticle.sprite = sprite;
                reticle.color = marker ? Amber : Pale;
            }
            if (session.Input.TouchMode) medicineDialProgress.fillAmount = player.TreatmentProgress;
            if (!force && Time.unscaledTime < hudAt) return;
            hudAt = Time.unscaledTime + 0.09f;
            UpdateMedicineButton(player);

            UpdateBodyHud(player.Health.Body);
            UpdateWeaponBar(weapon);
            staminaReadout.text = "体力  " + Mathf.CeilToInt(player.Stamina) + " / 100" + (player.SprintExhausted ? " 步行" : "");
            SetBar(staminaBar, player.Stamina * 0.01f);

            if (session.Phase == SessionPhase.Practice)
            {
                var range = session.Practice;
                statusReadout.text = "离线靶场 / " + Mathf.RoundToInt(sampledFps) + " 帧\n开火 " + range.Shots + " / 命中 " + range.Hits + "\n击倒靶板 " + range.Plates;
                string hint = session.Input.TouchMode ? "点地图或背包打开靶场控制" : "Tab 或 M 打开靶场控制";
                SetFieldPrompt(player.IsTreating ? TreatmentPrompt(player) : hint, player.IsTreating, player.TreatmentProgress);
                return;
            }

            UpdateMiniMap();
            var position = player.transform.position;
            var inZone = session.World.FindExit(position);
            ExtractionZone nearest = null;
            float distSq = float.PositiveInfinity;
            foreach (var exit in session.World.Exits)
            {
                float candidate = (exit.transform.position - position).sqrMagnitude;
                if (candidate >= distSq) continue;
                nearest = exit;
                distSq = candidate;
            }
            int seconds = Mathf.CeilToInt(session.RaidRemaining);
            var bearing = nearest == null ? Vector3.forward : nearest.transform.position - position;
            float heading = Mathf.Atan2(bearing.x, bearing.z) * Mathf.Rad2Deg;
            float relative = Mathf.DeltaAngle(player.transform.eulerAngles.y, heading);
            string direction = Mathf.Abs(relative) < 16 ? "前方" : Mathf.Abs(relative) > 158 ? "后方" : relative > 0 ? "右侧" : "左侧";
            statusReadout.text = session.World.DistrictAt(position) + "  /  " + (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00")
                + (nearest == null ? "" : "\n" + nearest.DisplayName + "  " + Mathf.RoundToInt(Mathf.Sqrt(distSq)) + " 米 / " + direction)
                + "\n" + Mathf.RoundToInt(sampledFps) + " 帧  /  击败 " + session.Kills;

            float progress = 0;
            if (inZone != null)
            {
                var timer = inZone.Timer;
                prompt.text = "正在撤离  /  " + timer.Remaining.ToString("0.0") + " 秒";
                progress = timer.Elapsed / timer.RequiredSeconds;
            }
            else if (player.IsTreating)
            {
                prompt.text = TreatmentPrompt(player);
                progress = player.TreatmentProgress;
            }
            else if (player.SearchingLoot != null)
            {
                prompt.text = "正在搜索  /  " + player.SearchingLoot.DisplayName;
                progress = player.SearchProgress;
            }
            else prompt.text = player.InteractionHint;
            SetFieldPrompt(prompt.text, inZone != null || player.IsTreating || player.SearchingLoot != null, progress);
        }

        void UpdateBodyHud(BodyCondition body)
        {
            bodyHud.SetCondition(body);
            int column = 0;
            for (int i = 0; i < DisplayedInjuries.Length; i++)
            {
                var injury = DisplayedInjuries[i];
                int count = 0;
                for (int j = 0; j < BodyCondition.Count; j++)
                    if ((VisibleInjuries(body, (BodyPart)j) & injury) != 0) count++;
                var badge = injuryBadges[i];
                SetActive(badge.gameObject, count > 0);
                if (count > 0) ((RectTransform)badge.transform).anchoredPosition = new Vector2(38 * column++, 0);
                if (shownInjuryCounts[i] == count) continue;
                shownInjuryCounts[i] = count;
                injuryCounts[i].text = count > 1 ? count.ToString() : "";
                injuryGlyphs[i].anchoredPosition = count > 1 ? new Vector2(-5, 0) : Vector2.zero;
            }
        }

        void SelectInjuryFromHud(Injury injury)
        {
            if (BlocksGameplay) return;
            var body = session.Player?.Health.Body;
            for (int i = 0; i < BodyCondition.Count; i++)
            {
                var part = (BodyPart)i;
                if ((VisibleInjuries(body, part) & injury) == 0) continue;
                ShowHealth(part, firstInjury: injury);
                break;
            }
        }

        static string VitalsValue(float current, float max) => current.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)
            + " / " + max.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

        static string TreatmentPrompt(PlayerController player) => player.TreatmentItem.DisplayName + " / " + BodyCondition.Name(player.TreatmentPart);

        void SetFieldPrompt(string message, bool showProgress, float progress)
        {
            prompt.text = message ?? "";
            SetActive(hintCard, !string.IsNullOrEmpty(message));
            SetActive(progressBar.transform.parent.gameObject, showProgress);
            SetBar(progressBar, progress);
        }

        public void HitMarker()
        {
            hitUntil = Time.unscaledTime + 0.13f;
        }

        public void ShowDamage(Vector3 bearing, float damage)
        {
            bearing.y = 0f;
            if (damage <= 0f || bearing.sqrMagnitude < 0.001f) return;
            ref DamageCue cue = ref damageCues[damageCursor++ % damageCues.Length];
            cue.Bearing = bearing.normalized;
            cue.Born = Time.unscaledTime;
            cue.Strength = Mathf.Clamp(0.58f + damage * 0.024f, 0f, 1f);
            cue.Image.gameObject.SetActive(true);
        }

        void UpdateDamageCues(Transform actor)
        {
            for (int i = 0; i < damageCues.Length; i++)
            {
                ref DamageCue cue = ref damageCues[i];
                if (!cue.Image.gameObject.activeSelf) continue;
                float age = Time.unscaledTime - cue.Born;
                if (age >= 1.3f) { cue.Image.gameObject.SetActive(false); continue; }
                var local = actor.InverseTransformDirection(cue.Bearing);
                var rect = cue.Image.rectTransform;
                rect.anchoredPosition = new Vector2(local.x, local.z) * 119f;
                rect.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg);
                cue.Image.color = new Color(1f, 0.31f, 0.24f, cue.Strength * (1f - Mathf.SmoothStep(0f, 1f, (age - 0.22f) / 1.08f)));
            }
        }

        void ResetDamageCues()
        {
            damageCursor = 0;
            hitUntil = 0f;
            for (int i = 0; i < damageCues.Length; i++) damageCues[i].Image.gameObject.SetActive(false);
        }

        void BuildTouch()
        {
            var look = Stretch("Look pad", touch);
            look.anchorMin = new Vector2(0.34f, 0f);
            look.anchorMax = new Vector2(1f, 0.90f);
            Fill(look, Color.clear, true);
            lookControl = AttachControl(look, "Look");
            AddTouchSlot("Move", "移动摇杆", RaidIcon.Knob, 0.094f, 0.170f, 155f);
            AddTouchSlot("Fire", "开火或挥击", RaidIcon.Fire, 0.925f, 0.265f, 107f, true);
            AddTouchSlot("Aim", "开镜", RaidIcon.Aim, 0.827f, 0.319f, 78f);
            AddTouchSlot("Reload", "换弹", RaidIcon.Reload, 0.922f, 0.104f, 74f);
            AddTouchSlot("Crouch", "蹲下", RaidIcon.Crouch, 0.720f, 0.111f, 72f);
            AddTouchSlot("Prone", "趴下", RaidIcon.Prone, 0.645f, 0.111f, 72f);
            AddTouchSlot("Jump", "跳跃", RaidIcon.Jump, 0.925f, 0.431f, 76f);
            AddTouchSlot("Interact", "搜索或开关门", RaidIcon.Search, 0.811f, 0.184f, 78f);
            AddTouchSlot("Heal", "快捷用药 / 长按选药", RaidIcon.Heal, 0.722f, 0.259f, 72f);
            AddTouchSlot("Inventory", "背包", RaidIcon.Bag, 0.795f, 0.066f, 66f);
            AddTouchSlot("LeanLeft", "向左探头", RaidIcon.LeanLeft, 0.181f, 0.362f, 66f);
            AddTouchSlot("LeanRight", "向右探头", RaidIcon.LeanRight, 0.256f, 0.362f, 66f);
            AddTouchSlot("SwitchWeapon", "轮换武器", RaidIcon.Switch, 0.811f, 0.458f, 72f);
            AddTouchSlot("Settings", "设置", RaidIcon.Gear, 0.944f, 0.545f, 65f);
            AddTouchSlot("Map", "大地图", RaidIcon.Map, 0.944f, 0.652f, 67f);
            AddTouchSlot("Health", "分部位健康", RaidIcon.Health, 0.077f, 0.514f, 68f);
            LoadTouchLayout();
        }

        static void SetActive(GameObject target, bool active)
        {
            if (target.activeSelf != active) target.SetActive(active);
        }

        TouchControl AttachControl(RectTransform rect, string action)
        {
            var control = rect.gameObject.AddComponent<TouchControl>();
            control.Input = session.Input;
            control.Action = action;
            return control;
        }
    }
}
