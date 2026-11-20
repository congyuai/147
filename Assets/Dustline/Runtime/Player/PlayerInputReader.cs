using UnityEngine;
using UnityEngine.InputSystem;

namespace Dustline
{
    [DefaultExecutionOrder(-200)]
    public sealed class PlayerInputReader : MonoBehaviour
    {
        public const int DefaultSprintThreshold = 86;
        public const int MinSprintThreshold = 55;
        public const int MaxSprintThreshold = 99;
        public int SprintThreshold { get; set; } = DefaultSprintThreshold;
        public bool SprintAllowed { get; set; } = true;
        public bool Gameplay;
        public bool TouchMode => Application.isMobilePlatform || ForceTouch;
        public bool ForceTouch;
        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }
        public bool Fire { get; private set; }
        public bool FirePressed { get; private set; }
        public bool Aim { get; private set; }
        public bool Sprint { get; private set; }
        public bool ReloadPressed { get; private set; }
        public bool CrouchPressed { get; private set; }
        public bool PronePressed { get; private set; }
        public bool JumpPressed { get; private set; }
        public bool LeanLeft { get; private set; }
        public bool LeanRight { get; private set; }
        public bool InteractPressed { get; private set; }
        public bool InventoryPressed { get; private set; }
        public bool SettingsPressed { get; private set; }
        public bool HealPressed { get; private set; }
        public bool EscapePressed { get; private set; }
        public Vector2 TouchMove;
        Vector2 touchLook;
        bool touchFire, touchFireTap, touchAim, touchLeanLeft, touchLeanRight, autoSprint, wasFire, mouseArmed;
        bool reload, crouch, prone, jump, interact, inventory, heal, settings;

        void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            InventoryPressed = inventory || (keyboard?.tabKey.wasPressedThisFrame ?? false);
            EscapePressed = keyboard?.escapeKey.wasPressedThisFrame ?? false;
            SettingsPressed = settings;
            ReloadPressed = reload || (keyboard?.rKey.wasPressedThisFrame ?? false);
            CrouchPressed = crouch || (keyboard?.cKey.wasPressedThisFrame ?? false);
            PronePressed = prone || (keyboard?.zKey.wasPressedThisFrame ?? false);
            JumpPressed = jump || (keyboard?.spaceKey.wasPressedThisFrame ?? false);
            LeanLeft = touchLeanLeft || (keyboard?.qKey.isPressed ?? false);
            LeanRight = touchLeanRight || (keyboard?.eKey.isPressed ?? false);
            InteractPressed = interact || (keyboard?.fKey.wasPressedThisFrame ?? false);
            HealPressed = heal || (keyboard?.hKey.wasPressedThisFrame ?? false);
            inventory = reload = crouch = prone = jump = interact = heal = settings = false;
            Move = Vector2.zero;
            if (keyboard != null)
                Move = new Vector2((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                    (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
            Move = Vector2.ClampMagnitude(Move + TouchMove, 1);
            Look = touchLook * 0.13f;
            bool mouseLook = !TouchMode && Cursor.lockState == CursorLockMode.Locked && mouse != null;
            if (mouseLook) Look += mouse.delta.ReadValue() * 0.08f;
            touchLook = Vector2.zero;
            bool mouseCanFire = mouseLook && mouseArmed;
            bool held = touchFire || (mouseCanFire && mouse.leftButton.isPressed);
            bool tapped = touchFireTap || (mouseCanFire && mouse.leftButton.wasPressedThisFrame);
            if (Gameplay && mouseLook && !mouse.leftButton.isPressed) mouseArmed = true;
            touchFireTap = false;
            Fire = Gameplay && (held || tapped);
            FirePressed = Gameplay && (tapped || held && !wasFire);
            Aim = touchAim || (mouseLook && mouse.rightButton.isPressed);
            if (!SprintAllowed) autoSprint = false;
            else autoSprint = Gameplay && TouchMode && !Aim && AutoSprintForward(TouchMove.y, autoSprint, SprintThreshold);
            Sprint = SprintAllowed && (TouchMode ? autoSprint : (keyboard?.leftShiftKey.isPressed ?? false));
            if (!Gameplay) { Move = Look = Vector2.zero; Aim = Sprint = LeanLeft = LeanRight = false; }
            wasFire = Gameplay && held;
        }

        public static int ClampSprintThreshold(int percent) => Mathf.Clamp(percent, MinSprintThreshold, MaxSprintThreshold);

        public static bool AutoSprintForward(float forward, bool alreadySprinting, int percent = DefaultSprintThreshold)
        {
            // A lower release point keeps small joystick movements from switching the gait repeatedly.
            int threshold = ClampSprintThreshold(percent) - (alreadySprinting ? 14 : 0);
            return forward >= threshold / 100f;
        }

        public void AddLook(Vector2 delta) { touchLook += delta; }
        public void Hold(string action, bool down)
        {
            if (action == "Fire")
            {
                touchFireTap |= down && !touchFire;
                touchFire = down;
            }
            if (action == "Aim") touchAim = down;
            if (action == "LeanLeft") touchLeanLeft = down;
            if (action == "LeanRight") touchLeanRight = down;
        }
        public void Tap(string action)
        {
            if (action == "Reload") reload = true;
            if (action == "Crouch") crouch = true;
            if (action == "Prone") prone = true;
            if (action == "Jump") jump = true;
            if (action == "Interact") interact = true;
            if (action == "Inventory") inventory = true;
            if (action == "Settings") settings = true;
            if (action == "Heal") heal = true;
        }
        public void ReleaseTouch()
        {
            TouchMove = touchLook = Move = Look = Vector2.zero;
            Fire = FirePressed = Aim = Sprint = wasFire = mouseArmed = false;
            touchFire = touchFireTap = touchAim = touchLeanLeft = touchLeanRight = autoSprint = false;
            LeanLeft = LeanRight = false;
            reload = crouch = prone = jump = interact = inventory = heal = settings = false;
        }
        void OnApplicationFocus(bool focus) { if (!focus) ReleaseTouch(); }
    }
}
