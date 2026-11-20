using UnityEngine;
using UnityEngine.InputSystem;

namespace Dustline
{
    [DefaultExecutionOrder(-200)]
    public sealed class PlayerInputReader : MonoBehaviour
    {
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
        public bool InteractPressed { get; private set; }
        public bool InventoryPressed { get; private set; }
        public bool HealPressed { get; private set; }
        public bool EscapePressed { get; private set; }
        public Vector2 TouchMove;
        Vector2 touchLook;
        bool touchFire, touchAim, touchSprint, wasFire;
        bool reload, crouch, interact, inventory, heal;

        void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            InventoryPressed = inventory || (keyboard?.tabKey.wasPressedThisFrame ?? false);
            EscapePressed = keyboard?.escapeKey.wasPressedThisFrame ?? false;
            ReloadPressed = reload || (keyboard?.rKey.wasPressedThisFrame ?? false);
            CrouchPressed = crouch || (keyboard?.cKey.wasPressedThisFrame ?? false);
            InteractPressed = interact || (keyboard?.eKey.wasPressedThisFrame ?? false);
            HealPressed = heal || (keyboard?.hKey.wasPressedThisFrame ?? false);
            inventory = reload = crouch = interact = heal = false;
            Move = Vector2.zero;
            if (keyboard != null)
                Move = new Vector2((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                    (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
            Move = Vector2.ClampMagnitude(Move + TouchMove, 1);
            Look = touchLook * 0.13f;
            if (Cursor.lockState == CursorLockMode.Locked && mouse != null)
                Look += mouse.delta.ReadValue() * 0.08f;
            touchLook = Vector2.zero;
            Fire = touchFire || (!TouchMode && Cursor.lockState == CursorLockMode.Locked && (mouse?.leftButton.isPressed ?? false));
            Aim = touchAim || (!TouchMode && Cursor.lockState == CursorLockMode.Locked && (mouse?.rightButton.isPressed ?? false));
            Sprint = touchSprint || (keyboard?.leftShiftKey.isPressed ?? false);
            if (!Gameplay) { Move = Look = Vector2.zero; Fire = Aim = Sprint = false; }
            FirePressed = Fire && !wasFire;
            wasFire = Fire;
        }

        public void AddLook(Vector2 delta) { touchLook += delta; }
        public void Hold(string action, bool down)
        {
            if (action == "Fire") touchFire = down;
            if (action == "Aim") touchAim = down;
            if (action == "Sprint") touchSprint = down;
        }
        public void Tap(string action)
        {
            if (action == "Reload") reload = true;
            if (action == "Crouch") crouch = true;
            if (action == "Interact") interact = true;
            if (action == "Inventory") inventory = true;
            if (action == "Heal") heal = true;
        }
        public void ReleaseTouch()
        {
            TouchMove = touchLook = Vector2.zero;
            touchFire = touchAim = touchSprint = false;
            reload = crouch = interact = inventory = heal = false;
        }
        void OnApplicationFocus(bool focus) { if (!focus) ReleaseTouch(); }
    }
}
