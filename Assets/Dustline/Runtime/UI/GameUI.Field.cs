using UnityEngine;
using UnityEngine.UI;

namespace Dustline
{
    public sealed partial class GameUI
    {
        RectTransform reticleAnchor;
        TouchControl lookControl;
        Image reticle, lifeBar, staminaBar, progressBar, fireGlyph, interactGlyph, sprintRing;
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
        Text healthReadout, staminaReadout, ammoReadout, statusReadout, prompt;
        BodyFigure bodyHud;
        readonly Text[] partHealth = new Text[BodyCondition.Count], partWound = new Text[BodyCondition.Count];
        readonly float[] shownHp = new float[BodyCondition.Count], shownMax = new float[BodyCondition.Count];
        readonly Injury[] shownInjury = new Injury[BodyCondition.Count];
        BodyCondition shownBody;
        float shownTotal = -1, shownTotalMax = -1;
        float hudAt, hitUntil;

        void BuildHud()
        {
            var upperLeft = Anchor("Left HUD anchor", hud, 0, 1);
            var vitals = HudCard(upperLeft, "Vitals", 13, 12, 322, 233);
            const float x = 125, y = 39, w = 72, h = 149;
            var sheet = Button(vitals, "", x - 5, y - 1, w + 10, h + 2, OpenBody);
            sheet.name = "Open body condition";
            sheet.targetGraphic.color = Color.clear;
            sheet.transition = Selectable.Transition.None;
            healthReadout = Label(vitals, "", 19, 10, 2, 230, 27, TextAnchor.MiddleLeft);
            var open = Button(vitals, "治疗 ›", 235, 2, 77, 27, OpenBody, false, new Color32(31, 54, 59, 242));
            open.GetComponentInChildren<Text>().color = Aqua;
            lifeBar = Bar(vitals, 10, 31, 302, 4, new Color32(221, 115, 82, 255));
            bodyHud = At("Body diagram", vitals, x, y, w, h).gameObject.AddComponent<BodyFigure>();
            bodyHud.Initialize(new Color32(43, 94, 90, 255), new Color32(120, 187, 177, 255));
            Row(BodyPart.Head, 210, 37, false);
            Row(BodyPart.Chest, 9, 37, true);
            Row(BodyPart.Abdomen, 9, 77, true);
            Row(BodyPart.LeftArm, 9, 117, true);
            Row(BodyPart.RightArm, 210, 117, false);
            Row(BodyPart.LeftLeg, 9, 157, true);
            Row(BodyPart.RightLeg, 210, 157, false);
            Fill(At("Stamina divider", vitals, 10, 196, 302, 1), Border);
            staminaReadout = Label(vitals, "", 17, 10, 197, 176, 28, TextAnchor.MiddleLeft, Muted);
            staminaBar = Bar(vitals, 185, 209, 127, 6, Aqua);
            var ammo = HudCard(upperLeft, "Ammunition status", 13, 251, 322, 49);
            ammoReadout = Label(ammo, "", 17, 10, 2, 302, 44, TextAnchor.MiddleLeft, Amber);

            void OpenBody() { if (!BlocksGameplay) ShowHealth(); }

            void Row(BodyPart part, float left, float top, bool onLeft)
            {
                var pin = BodyFigure.Pin(part, x, y, w, h);
                var end = new Vector2(onLeft ? left + 104 : left - 3, top + 12);
                var bend = new Vector2(onLeft ? x - 6 : x + w + 6, top + 12);
                Stroke(vitals, "Body leader / " + part, end, bend, 1f, Border);
                Stroke(vitals, "Body leader / " + part, bend, pin, 1f, Border);
                int index = (int)part;
                partHealth[index] = Label(vitals, "", 15, left, top, 110, 23, TextAnchor.MiddleLeft);
                partHealth[index].name = "Region health / " + part;
                partWound[index] = Label(vitals, "", 13, left, top + 21, 110, 16, TextAnchor.MiddleLeft, Amber);
                partWound[index].verticalOverflow = VerticalWrapMode.Overflow;
                partWound[index].name = "Region injury / " + part;
            }

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
            var interaction = HudCard(bottom, "Interaction status", -234, -315, 468, 67);
            prompt = Label(interaction, "", 21, 12, 7, 444, 45, TextAnchor.MiddleCenter);
            progressBar = Bar(interaction, 14, 53, 440, 5, Aqua);
            hintCard = interaction.gameObject;
            hintCard.SetActive(false);
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
                SetActive(switchButton, session.ActiveFieldBag.Equipped(EquipmentSlot.Weapon) != null);
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

            UpdateBodyHud(player.Health, force);
            staminaReadout.text = "体力  " + Mathf.CeilToInt(player.Stamina) + " / 100" + (player.SprintExhausted ? " 步行" : "");
            SetBar(staminaBar, player.Stamina * 0.01f);
            ammoReadout.text = weapon.IsMelee ? (weapon.ActiveIsBound ? session.BoundBladeName + " / 随身刀" : "检修短棍 / 近战")
                : weapon.Magazine == null ? "未装备主武器" : weapon.Magazine.Reloading ? "正在换弹"
                : weapon.Magazine.Rounds + " / " + weapon.Reserve + (weapon.Automatic ? "  自动" : "  单发")
                    + "\n" + (session.Catalog.Find(weapon.ReadyAmmoType)?.DisplayName ?? "无兼容备弹");

            if (session.Phase == SessionPhase.Practice)
            {
                var range = session.Practice;
                statusReadout.text = "离线靶场 / " + Mathf.RoundToInt(sampledFps) + " 帧\n开火 " + range.Shots + " / 命中 " + range.Hits + "\n击倒靶板 " + range.Plates;
                SetFieldPrompt(player.IsTreating ? TreatmentPrompt(player) : null, player.IsTreating, player.TreatmentProgress);
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

        void UpdateBodyHud(Health health, bool force)
        {
            var body = health.Body;
            bool refresh = force || body != shownBody;
            shownBody = body;
            float total = body?.Total ?? health.Current, max = body?.TotalMaximum ?? health.Maximum;
            if (refresh || total != shownTotal || max != shownTotalMax)
            {
                healthReadout.text = "总健康  " + VitalsValue(total, max);
                shownTotal = total; shownTotalMax = max;
                SetBar(lifeBar, max > 0f ? total / max : 0f);
            }
            for (int i = 0; i < BodyCondition.Count; i++)
            {
                var part = (BodyPart)i;
                float hp = body?.Current(part) ?? 0f, cap = body?.Maximum(part) ?? 0f;
                var flags = body?.Injuries(part) ?? Injury.None;
                if (!refresh && shownHp[i] == hp && shownMax[i] == cap && shownInjury[i] == flags) continue;
                shownHp[i] = hp; shownMax[i] = cap; shownInjury[i] = flags;
                partHealth[i].text = BodyCondition.Name(part) + " " + (body == null ? "— / —" : VitalsValue(hp, cap));
                string wound = FieldWound(flags);
                partWound[i].text = wound;
                bool broken = (flags & Injury.Destroyed) != 0;
                bool critical = broken || (flags & Injury.HeavyBleed) != 0;
                bool injured = body != null && (hp < cap - .001f || flags != Injury.None);
                bodyHud.SetTone(part, broken ? new Color32(104, 35, 36, 255) : critical ? new Color32(199, 75, 61, 255)
                    : injured ? new Color32(178, 116, 47, 255) : new Color32(43, 94, 90, 255));
                partHealth[i].color = critical ? new Color32(255, 148, 129, 255) : injured ? new Color32(255, 207, 136, 255) : Pale;
                partWound[i].color = critical ? new Color32(255, 148, 129, 255) : Amber;
            }
        }

        static string VitalsValue(float current, float max) => current.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)
            + " / " + max.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

        static string FieldWound(Injury flags)
        {
            bool destroyed = (flags & Injury.Destroyed) != 0;
            bool fractured = (flags & Injury.Fracture) != 0;
            string bleed = (flags & Injury.HeavyBleed) != 0 ? "重度出血" : (flags & Injury.LightBleed) != 0 ? "轻度出血" : "";
            if (destroyed && fractured && bleed.Length > 0) return (bleed == "重度出血" ? "重血" : "轻血") + " 骨折 损毁";
            string text = destroyed ? "损毁" : "";
            if (bleed.Length > 0) text += (text.Length > 0 ? " " : "") + bleed;
            if (fractured) text += (text.Length > 0 ? " " : "") + "骨折";
            return text;
        }

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
            AddTouchSlot("SwitchWeapon", "切换主武器和随身刀", RaidIcon.Switch, 0.811f, 0.458f, 72f);
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
