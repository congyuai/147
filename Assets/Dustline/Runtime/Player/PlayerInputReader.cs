using UnityEngine;
using UnityEngine.InputSystem;
using GyroDevice = UnityEngine.InputSystem.Gyroscope;

namespace Dustline
{
    [DefaultExecutionOrder(-200)]
    public sealed class PlayerInputReader : MonoBehaviour
    {
        const float StickDeadZone = .12f;
        public bool SprintAllowed { get; set; } = true;
        public float LookSensitivity { get; set; } = 1f;
        public float GyroSensitivity { get; set; } = 1f;
        public GyroMode GyroUsage { get; set; }
        public AimMode AimBehavior { get; private set; }
        public LeanMode LeanBehavior { get; private set; } = LeanMode.Hybrid;
        public bool QuietWalk { get; private set; }
        internal bool SwimUp => Gameplay && (touchSwimUp || Keyboard.current?.spaceKey.isPressed == true);
        internal bool SwimDown => Gameplay && (touchSwimDown || Keyboard.current?.cKey.isPressed == true);
        public bool HasGyroscope => Application.isMobilePlatform && GyroDevice.current != null;
        internal GameUI GestureUI;
        public bool Gameplay;
        public bool TouchMode => Application.isMobilePlatform || ForceTouch;
        public bool ForceTouch;
        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }
        public bool Fire { get; private set; }
        public bool FirePressed { get; private set; }
        public bool Aim { get; private set; }
        public bool Sprint { get; private set; }
        public bool SprintHeld { get; private set; }
        public bool HealthPressed { get; private set; }
        bool healthPage;
        public bool ReloadPressed { get; private set; }
        public bool CrouchPressed { get; private set; }
        public bool PronePressed { get; private set; }
        public bool JumpPressed { get; private set; }
        public bool LeanLeft { get; private set; }
        public bool LeanRight { get; private set; }
        public bool InteractPressed { get; private set; }
        public bool InventoryPressed { get; private set; }
        public bool SettingsPressed { get; private set; }
        public bool MapPressed { get; private set; }
        public bool SwitchWeaponPressed { get; private set; }
        internal bool WeaponSelectionPending => SwitchWeaponPressed || weaponSlot != EquipmentSlot.None;
        EquipmentSlot weaponSlot;
        public bool HealPressed { get; private set; }
        internal string HealInstance { get; private set; }
        string medicineTapInstance;
        BodyPart? medicineTapPart;
        internal BodyPart? HealPart { get; private set; }
        public bool EscapePressed { get; private set; }
        public Vector2 TouchMove;
        Vector2 touchLook, gyroSmoothed;
        GyroDevice gyro;
        bool ownsGyro, aimAvailable, mouseAimArmed;
        AimGesture aimGesture;
        LeanGesture leanGesture;
        bool touchFire, touchFireTap, touchSwimUp, touchSwimDown, autoSprint, wasFire, mouseArmed;
        bool sprintTargetHeld, quietTap;
        internal bool TouchSprintLocked => autoSprint;
        internal bool StickMoving => TouchMove.sqrMagnitude > StickDeadZone * StickDeadZone;
        bool reload, crouch, prone, jump, interact, inventory, heal, settings, map, weaponSwap;

        void Update()
        {
            GestureUI?.UpdateFieldGestures();
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            InventoryPressed = inventory || (keyboard?.tabKey.wasPressedThisFrame ?? false);
            EscapePressed = BackPressed();
            SettingsPressed = settings;
            HealthPressed = healthPage || (keyboard?.jKey.wasPressedThisFrame ?? false);
            healthPage = false;
            MapPressed = map || (keyboard?.mKey.wasPressedThisFrame ?? false);
            SwitchWeaponPressed = Gameplay && (weaponSwap || (keyboard?.xKey.wasPressedThisFrame ?? false));
            weaponSlot = !Gameplay || keyboard == null ? EquipmentSlot.None
                : keyboard.digit1Key.wasPressedThisFrame ? EquipmentSlot.Weapon
                : keyboard.digit2Key.wasPressedThisFrame ? EquipmentSlot.PrimaryTwo
                : keyboard.digit3Key.wasPressedThisFrame ? EquipmentSlot.Sidearm
                : keyboard.digit4Key.wasPressedThisFrame ? EquipmentSlot.Melee : EquipmentSlot.None;
            ReloadPressed = reload || (keyboard?.rKey.wasPressedThisFrame ?? false);
            CrouchPressed = crouch || (keyboard?.cKey.wasPressedThisFrame ?? false);
            PronePressed = prone || (keyboard?.zKey.wasPressedThisFrame ?? false);
            JumpPressed = jump || (keyboard?.spaceKey.wasPressedThisFrame ?? false);
            if (Gameplay && keyboard != null)
            {
                double t = Time.unscaledTimeAsDouble;
                if (keyboard.qKey.wasPressedThisFrame) leanGesture.Set(-1, true, LeanBehavior, t);
                if (keyboard.eKey.wasPressedThisFrame) leanGesture.Set(1, true, LeanBehavior, t);
                if (keyboard.qKey.wasReleasedThisFrame) leanGesture.Set(-1, false, LeanBehavior, t);
                if (keyboard.eKey.wasReleasedThisFrame) leanGesture.Set(1, false, LeanBehavior, t);
            }
            int lean = Gameplay ? leanGesture.Active(LeanBehavior) : 0;
            LeanLeft = lean < 0;
            LeanRight = lean > 0;
            if (Gameplay && (quietTap || keyboard?.leftAltKey.wasPressedThisFrame == true))
            {
                QuietWalk = !QuietWalk;
                if (QuietWalk) CancelTouchSprint();
            }
            quietTap = false;
            InteractPressed = interact || (keyboard?.fKey.wasPressedThisFrame ?? false);
            HealPressed = heal || (keyboard?.hKey.wasPressedThisFrame ?? false);
            HealInstance = heal ? medicineTapInstance : null;
            HealPart = heal ? medicineTapPart : null;
            medicineTapInstance = null;
            medicineTapPart = null;
            inventory = reload = crouch = prone = jump = interact = heal = settings = map = weaponSwap = false;
            Move = Vector2.zero;
            if (keyboard != null)
                Move = new Vector2((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                    (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
            Look = touchLook * (0.13f * LookSensitivity);
            bool mouseLook = !TouchMode && Cursor.lockState == CursorLockMode.Locked && mouse != null;
            if (mouseLook) Look += mouse.delta.ReadValue() * (0.08f * LookSensitivity);
            touchLook = Vector2.zero;
            bool mouseCanFire = mouseLook && mouseArmed;
            bool held = touchFire || (mouseCanFire && mouse.leftButton.isPressed);
            bool tapped = touchFireTap || (mouseCanFire && mouse.leftButton.wasPressedThisFrame);
            if (Gameplay && mouseLook && !mouse.leftButton.isPressed) mouseArmed = true;
            touchFireTap = false;
            Fire = Gameplay && (held || tapped);
            FirePressed = Gameplay && (tapped || held && !wasFire);
            if (mouseLook && Gameplay && aimAvailable)
            {
                var right = mouse.rightButton;
                double now = Time.unscaledTimeAsDouble;
                if (mouseAimArmed)
                {
                    if (right.wasPressedThisFrame) aimGesture.Set(true, AimBehavior, now);
                    aimGesture.Set(right.isPressed, AimBehavior, now);
                }
                else if (!right.isPressed) mouseAimArmed = true;
            }
            Aim = Gameplay && aimAvailable && aimGesture.Active(AimBehavior);
            Look += ReadGyro();
            if (!Gameplay || !TouchMode) autoSprint = false;
            SprintHeld = Gameplay && (TouchMode ? autoSprint : (keyboard?.leftShiftKey.isPressed ?? false));
            Sprint = SprintAllowed && !Aim && SprintHeld;
            Move = Vector2.ClampMagnitude(Move + TouchWalk(TouchMove, TouchMode && Sprint), 1f);
            if (!Gameplay) { Move = Look = Vector2.zero; Aim = Sprint = LeanLeft = LeanRight = false; }
            wasFire = Gameplay && held;
        }

        internal static bool BackPressed()
        {
            var keyboard = Keyboard.current;
            if (keyboard?.escapeKey.wasPressedThisFrame == true) return true;
            if (Application.platform == RuntimePlatform.Android)
                foreach (var candidate in InputSystem.devices)
                    if (candidate is Keyboard source && source != keyboard && source.escapeKey.wasPressedThisFrame) return true;
            return false;
        }

        public static Vector2 TouchWalk(Vector2 stick, bool lockedSprint)
        {
            if (lockedSprint) return new Vector2(Mathf.Clamp(stick.x, -1f, 1f), Mathf.Max(.75f, stick.y)).normalized;
            return stick.sqrMagnitude > StickDeadZone * StickDeadZone ? stick.normalized : Vector2.zero;
        }

        internal void MoveTouchStick(Vector2 raw, bool inSprintTarget)
        {
            TouchMove = raw;
            if (raw.y <= -.20f) CancelTouchSprint();
            else if (inSprintTarget && !sprintTargetHeld && Gameplay && TouchMode && SprintAllowed && !Aim && raw.y > StickDeadZone)
            {
                autoSprint = true;
                QuietWalk = false;
            }
            sprintTargetHeld = inSprintTarget;
        }

        internal void ReleaseTouchStick(bool cancelled)
        {
            TouchMove = Vector2.zero;
            sprintTargetHeld = false;
            if (cancelled) CancelTouchSprint();
        }

        internal void CancelTouchSprint()
        {
            autoSprint = false;
            if (TouchMode) Sprint = SprintHeld = false;
        }

        internal void EndQuietWalk() => QuietWalk = false;

        internal void SetLeanMode(LeanMode mode)
        {
            LeanBehavior = mode;
            leanGesture = default;
            LeanLeft = LeanRight = false;
        }

        public void SetAimMode(AimMode mode)
        {
            if (AimBehavior == mode) return;
            AimBehavior = mode;
            CancelAimGesture();
        }

        public void SetAimAvailable(bool available)
        {
            if (aimAvailable == available) return;
            aimAvailable = available;
            CancelAimGesture();
        }

        public void CancelAimGesture()
        {
            aimGesture = default;
            Aim = mouseAimArmed = false;
            gyroSmoothed = Vector2.zero;
        }

        Vector2 ReadGyro()
        {
            var device = Gameplay && GyroUsage != GyroMode.Off && HasGyroscope ? GyroDevice.current : null;
            if (device != gyro)
            {
                ReleaseGyro();
                gyro = device;
                if (gyro != null && !gyro.enabled)
                {
                    InputSystem.EnableDevice(gyro);
                    ownsGyro = true;
                }
            }
            if (gyro == null || GyroUsage == GyroMode.AimOnly && !Aim)
            {
                gyroSmoothed = Vector2.zero;
                return Vector2.zero;
            }
            Vector3 angular = gyro.angularVelocity.ReadValue();
            Vector2 axes = new Vector2(angular.x, angular.y);
            if (!InputSystem.settings.compensateForScreenOrientation)
            {
                if (Screen.orientation == ScreenOrientation.LandscapeLeft) axes = new Vector2(-axes.y, axes.x);
                else if (Screen.orientation == ScreenOrientation.LandscapeRight) axes = new Vector2(axes.y, -axes.x);
                else if (Screen.orientation == ScreenOrientation.PortraitUpsideDown) axes = -axes;
            }
            if (float.IsNaN(axes.x) || float.IsInfinity(axes.x) || float.IsNaN(axes.y) || float.IsInfinity(axes.y))
                return Vector2.zero;
            if (axes.sqrMagnitude < 0.000196f) axes = Vector2.zero;
            float delta = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            gyroSmoothed = Vector2.Lerp(gyroSmoothed, new Vector2(-axes.y, axes.x), 1 - Mathf.Exp(-32f * delta));
            return gyroSmoothed * (Mathf.Rad2Deg * delta * GyroSensitivity);
        }

        void ReleaseGyro()
        {
            if (gyro != null && gyro.added && ownsGyro) InputSystem.DisableDevice(gyro);
            gyro = null;
            ownsGyro = false;
            gyroSmoothed = Vector2.zero;
        }

        public void AddLook(Vector2 delta) { touchLook += delta; }
        public void Hold(string action, bool down)
        {
            if (action == "Fire")
            {
                touchFireTap |= down && !touchFire;
                touchFire = down;
            }
            if (action == "Aim" && Gameplay && aimAvailable)
                aimGesture.Set(down, AimBehavior, Time.unscaledTimeAsDouble);
            if (Gameplay && (action == "LeanLeft" || action == "LeanRight"))
                leanGesture.Set(action == "LeanLeft" ? -1 : 1, down, LeanBehavior, Time.unscaledTimeAsDouble);
            if (action == "Jump") touchSwimUp = down;
            if (action == "Crouch") touchSwimDown = down;
        }
        public void Tap(string action)
        {
            if (action == "Reload") reload = true;
            if (action == "Crouch") crouch = true;
            if (action == "Prone") prone = true;
            if (action == "Jump") jump = true;
            if (action == "Interact") interact = true;
            if (action == "Inventory") inventory = true;
            if (action == "Health") healthPage = true;
            if (action == "Settings") settings = true;
            if (action == "Map") map = true;
            if (action == "SwitchWeapon") weaponSwap = true;
            if (action == "Heal") TapMedicine();
            if (action == "Quiet") quietTap = true;
        }

        internal EquipmentSlot TakeWeaponSlot()
        {
            var selected = weaponSlot;
            weaponSlot = EquipmentSlot.None;
            return Gameplay ? selected : EquipmentSlot.None;
        }

        internal bool TakeWeaponSwap()
        {
            bool toggle = Gameplay && SwitchWeaponPressed;
            SwitchWeaponPressed = false;
            return toggle;
        }

        internal void CancelAction(string action)
        {
            if (action == "Aim") CancelAimGesture();
            else if (action == "LeanLeft" || action == "LeanRight") leanGesture.Cancel(action == "LeanLeft" ? -1 : 1);
            else Hold(action, false);
            switch (action)
            {
                case "Fire": touchFireTap = false; break;
                case "Reload": reload = false; break;
                case "Crouch": crouch = false; break;
                case "Prone": prone = false; break;
                case "Jump": jump = false; break;
                case "Interact": interact = false; break;
                case "Inventory": inventory = false; break;
                case "Settings": settings = false; break;
                case "Map": map = false; break;
                case "SwitchWeapon": weaponSwap = false; break;
                case "Health": healthPage = false; break;
                case "Quiet": quietTap = false; break;
            }
        }

        internal void TapMedicine(string instance = null, BodyPart? part = null)
        {
            heal = true;
            medicineTapInstance = instance;
            medicineTapPart = part;
        }

        public void ReleaseTouch()
        {
            TouchMove = touchLook = Move = Look = Vector2.zero;
            Fire = FirePressed = Sprint = SprintHeld = wasFire = mouseArmed = false;
            HealthPressed = healthPage = false;
            HealPart = medicineTapPart = null;
            touchFire = touchFireTap = touchSwimUp = touchSwimDown = autoSprint = sprintTargetHeld = quietTap = false;
            leanGesture = default;
            LeanLeft = LeanRight = SwitchWeaponPressed = false;
            weaponSlot = EquipmentSlot.None;
            ReloadPressed = CrouchPressed = PronePressed = JumpPressed = InteractPressed = HealPressed = false;
            HealInstance = medicineTapInstance = null;
            reload = crouch = prone = jump = interact = inventory = heal = settings = map = weaponSwap = false;
            CancelAimGesture();
            if (!Gameplay) ReleaseGyro();
        }
        void OnApplicationFocus(bool focus) { if (!focus) { ReleaseTouch(); ReleaseGyro(); } }
        void OnDisable() { ReleaseGyro(); }
    }
}
