using UnityEngine;
using UnityEngine.UI;

namespace Dustline
{
    public sealed partial class GameUI
    {
        RectTransform reticleAnchor;
        Image reticle, lifeBar, staminaBar, progressBar, fireGlyph, sprintRing;
        GameObject aimButton, reloadButton;
        GameObject hintCard;
        Text healthReadout, staminaReadout, ammoReadout, statusReadout, prompt;
        float hudAt, hitUntil;

        void BuildHud()
        {
            var upperLeft = Anchor("Left HUD anchor", hud, 0, 1);
            var vitals = HudCard(upperLeft, "Vitals", 21, 20, 298, 138);
            healthReadout = Label(vitals, "", 20, 13, 7, 268, 29);
            lifeBar = Bar(vitals, 13, 38, 272, 6, new Color32(201, 104, 76, 255));
            staminaReadout = Label(vitals, "", 17, 13, 47, 268, 25, color: Muted);
            staminaBar = Bar(vitals, 13, 73, 272, 5, Aqua);
            ammoReadout = Label(vitals, "", 21, 13, 87, 272, 44, color: Amber);

            var upperRight = Anchor("Right HUD anchor", hud, 1, 1);
            var status = HudCard(upperRight, "Raid status", -369, 20, 348, 126);
            statusReadout = Label(status, "", 18, 13, 6, 322, 113, TextAnchor.MiddleRight);
            reticleAnchor = Anchor("Screen centre", hud, 0.5f, 0.5f);
            reticle = Fill(At("Reticle", reticleAnchor, -22, -22, 44, 44), Pale);
            reticle.sprite = icons[RaidIcon.Reticle];

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
                SetActive(sprintRing.gameObject, player.Sprinting);
            }
            bool marker = Time.unscaledTime < hitUntil;
            var sprite = icons[marker ? RaidIcon.Hit : RaidIcon.Reticle];
            if (reticle.sprite != sprite)
            {
                reticle.sprite = sprite;
                reticle.color = marker ? Amber : Pale;
            }
            if (!force && Time.unscaledTime < hudAt) return;
            hudAt = Time.unscaledTime + 0.09f;

            healthReadout.text = "生命  " + Mathf.CeilToInt(player.Health.Current);
            staminaReadout.text = "体力  " + Mathf.CeilToInt(player.Stamina);
            SetBar(lifeBar, player.Health.Current / player.Health.Maximum);
            SetBar(staminaBar, player.Stamina * 0.01f);
            ammoReadout.text = weapon.IsMelee ? "检修短棍 / 近战" : weapon.Magazine == null ? "未装备武器" : weapon.Magazine.Reloading ? "正在换弹"
                : weapon.Magazine.Rounds + " / " + weapon.Reserve + (weapon.Automatic ? "   全自动" : "   半自动");

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

        void BuildTouch()
        {
            var look = Stretch("Look pad", touch);
            look.anchorMin = new Vector2(0.40f, 0);
            look.anchorMax = new Vector2(1, 0.86f);
            Fill(look, Color.clear, true);
            AttachControl(look, "Look");
            var left = Anchor("Left touch anchor", touch, 0, 0);
            var right = Anchor("Right touch anchor", touch, 1, 0);
            var topRight = Anchor("Top touch anchor", touch, 1, 1);
            var stick = At("Move", left, 30, -211, 184, 184);
            var stickFace = Fill(stick, new Color32(85, 120, 124, 138), true);
            stickFace.sprite = icons[RaidIcon.Dial];
            var knob = Anchor("Move knob", stick, 0.5f, 0.5f);
            knob.sizeDelta = new Vector2(69, 69);
            Fill(knob, new Color32(203, 221, 210, 174)).sprite = icons[RaidIcon.Knob];
            var move = AttachControl(stick, "Move");
            move.SetFace(stickFace, new Color32(104, 155, 146, 179));
            move.Knob = knob;
            var halo = Stretch("Sprint indicator", stick);
            halo.SetAsFirstSibling();
            sprintRing = Fill(halo, new Color32(229, 174, 91, 181));
            sprintRing.sprite = icons[RaidIcon.Dial];
            sprintRing.gameObject.SetActive(false);
            fireGlyph = TouchButton(right, "Fire", RaidIcon.Fire, -145, -195, 117, true);
            aimButton = TouchButton(right, "Aim", RaidIcon.Aim, -250, -175, 88).GetComponentInParent<TouchControl>().gameObject;
            reloadButton = TouchButton(right, "Reload", RaidIcon.Reload, -119, -284, 82).GetComponentInParent<TouchControl>().gameObject;
            TouchButton(right, "Crouch", RaidIcon.Crouch, -352, -118, 83);
            TouchButton(right, "Prone", RaidIcon.Prone, -448, -118, 83);
            TouchButton(right, "Jump", RaidIcon.Jump, -448, -210, 83);
            TouchButton(right, "Interact", RaidIcon.Search, -258, -277, 90);
            TouchButton(right, "Heal", RaidIcon.Heal, -352, -210, 83);
            TouchButton(right, "Inventory", RaidIcon.Bag, -124, -377, 85);
            TouchButton(left, "LeanLeft", RaidIcon.Lean, 219, -109, 72);
            TouchButton(left, "LeanRight", RaidIcon.Lean, 298, -109, 72);
            TouchButton(topRight, "Settings", RaidIcon.Gear, -103, 157, 77);
        }

        static void SetActive(GameObject target, bool active)
        {
            if (target.activeSelf != active) target.SetActive(active);
        }

        Image TouchButton(Transform parent, string action, RaidIcon icon, float x, float y, float size, bool primary = false)
        {
            var rect = At(action, parent, x, y, size, size);
            var face = Fill(rect, primary ? new Color32(151, 101, 52, 191) : new Color32(66, 91, 99, 169), true);
            face.sprite = icons[RaidIcon.Dial];
            var glyph = Anchor("Icon", rect, 0.5f, 0.5f);
            glyph.sizeDelta = Vector2.one * (size * (primary ? 0.68f : 0.65f));
            var image = Fill(glyph, Pale);
            image.sprite = icons[icon];
            AttachControl(rect, action).SetFace(face, primary ? Amber : new Color32(108, 166, 156, 236));
            return image;
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
