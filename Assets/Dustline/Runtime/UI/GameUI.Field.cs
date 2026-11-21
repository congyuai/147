using UnityEngine;
using UnityEngine.UI;

namespace Dustline
{
    public sealed partial class GameUI
    {
        RectTransform reticleAnchor;
        Image reticle, lifeBar, staminaBar, progressBar, fireGlyph, interactGlyph, sprintRing;
        GameObject aimButton, reloadButton, switchButton;
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
        float hudAt, hitUntil;

        void BuildHud()
        {
            var upperLeft = Anchor("Left HUD anchor", hud, 0, 1);
            var vitals = HudCard(upperLeft, "Vitals", 21, 20, 276, 136);
            healthReadout = Label(vitals, "", 20, 13, 7, 268, 29);
            lifeBar = Bar(vitals, 13, 38, 250, 6, new Color32(201, 104, 76, 255));
            staminaReadout = Label(vitals, "", 17, 13, 47, 268, 25, color: Muted);
            staminaBar = Bar(vitals, 13, 73, 250, 5, Aqua);
            ammoReadout = Label(vitals, "", 20, 13, 87, 250, 44, color: Amber);

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
                SetActive(switchButton, session.Loadout.Equipped(EquipmentSlot.Weapon) != null);
                var interactionIcon = icons[player.LookingAtDoor ? RaidIcon.InteractDoor : RaidIcon.Search];
                if (interactGlyph.sprite != interactionIcon) interactGlyph.sprite = interactionIcon;
                SetActive(sprintRing.gameObject, player.Sprinting);
            }
            bool obstructed = player.WeaponView.RifleObstructed;
            reticle.enabled = obstructed || (!session.Input.Aim && player.AimProgress < 0.08f);
            UpdateDamageCues(player.transform);
            bool marker = Time.unscaledTime < hitUntil;
            var sprite = icons[marker ? RaidIcon.Hit : RaidIcon.Reticle];
            if (reticle.sprite != sprite)
            {
                reticle.sprite = sprite;
                reticle.color = marker ? Amber : Pale;
            }
            if (!force && Time.unscaledTime < hudAt) return;
            hudAt = Time.unscaledTime + 0.09f;
            UpdateMiniMap();

            healthReadout.text = "生命  " + Mathf.CeilToInt(player.Health.Current);
            staminaReadout.text = "体力  " + Mathf.CeilToInt(player.Stamina);
            SetBar(lifeBar, player.Health.Current / player.Health.Maximum);
            SetBar(staminaBar, player.Stamina * 0.01f);
            ammoReadout.text = weapon.IsMelee ? (weapon.ActiveIsBound ? session.BoundBladeName + " / 随身刀" : "检修短棍 / 近战")
                : weapon.Magazine == null ? "未装备主武器" : weapon.Magazine.Reloading ? "正在换弹"
                : weapon.Magazine.Rounds + " / " + weapon.Reserve + (obstructed ? "   持枪受阻" : weapon.Automatic ? "   全自动" : "   半自动");

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
            else if (player.SearchingLoot != null)
            {
                prompt.text = "正在搜索  /  " + player.SearchingLoot.DisplayName;
                progress = player.SearchProgress;
            }
            else prompt.text = player.InteractionHint;
            bool show = !string.IsNullOrEmpty(prompt.text);
            if (hintCard.activeSelf != show) hintCard.SetActive(show);
            progressBar.transform.parent.gameObject.SetActive(inZone != null || player.SearchingLoot != null);
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
            AttachControl(look, "Look");
            AddTouchSlot("Move", "移动摇杆", RaidIcon.Knob, 0.094f, 0.170f, 155f);
            AddTouchSlot("Fire", "开火或挥击", RaidIcon.Fire, 0.925f, 0.265f, 107f, true);
            AddTouchSlot("Aim", "开镜", RaidIcon.Aim, 0.827f, 0.319f, 78f);
            AddTouchSlot("Reload", "换弹", RaidIcon.Reload, 0.922f, 0.104f, 74f);
            AddTouchSlot("Crouch", "蹲下", RaidIcon.Crouch, 0.720f, 0.111f, 72f);
            AddTouchSlot("Prone", "趴下", RaidIcon.Prone, 0.645f, 0.111f, 72f);
            AddTouchSlot("Jump", "跳跃", RaidIcon.Jump, 0.925f, 0.431f, 76f);
            AddTouchSlot("Interact", "搜索或开关门", RaidIcon.Search, 0.811f, 0.184f, 78f);
            AddTouchSlot("Heal", "治疗", RaidIcon.Heal, 0.722f, 0.259f, 72f);
            AddTouchSlot("Inventory", "背包", RaidIcon.Bag, 0.795f, 0.066f, 66f);
            AddTouchSlot("LeanLeft", "向左探头", RaidIcon.LeanLeft, 0.181f, 0.362f, 66f);
            AddTouchSlot("LeanRight", "向右探头", RaidIcon.LeanRight, 0.256f, 0.362f, 66f);
            AddTouchSlot("SwitchWeapon", "切换主武器和随身刀", RaidIcon.Switch, 0.811f, 0.458f, 72f);
            AddTouchSlot("Settings", "设置", RaidIcon.Gear, 0.944f, 0.545f, 65f);
            AddTouchSlot("Map", "大地图", RaidIcon.Map, 0.944f, 0.652f, 67f);
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
