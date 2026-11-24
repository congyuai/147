using UnityEngine;
using Object = UnityEngine.Object;

namespace Dustline
{
    internal sealed class OperatorDisplay
    {
        readonly Transform pivot;
        readonly float facing, baseYaw;
        readonly bool showBoundBlade;
        OperatorVisual model;
        WeaponVisual hand, back;
        int handLook, backLook;
        WeaponDefinition handDefinition, backDefinition;
        float yaw;

        internal OperatorDisplay(Transform parent, Vector3 position, float facing, float baseYaw, bool showBoundBlade)
        {
            pivot = new GameObject("Operator turntable") { layer = parent.gameObject.layer }.transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = position;
            this.facing = facing;
            this.baseYaw = baseYaw;
            this.showBoundBlade = showBoundBlade;
            ApplyAngle();
        }

        internal OperatorVisual Model => model;
        internal Vector3 Position { set => pivot.localPosition = value; }

        internal void Select(OperatorStyle style)
        {
            if (model != null && model.Style == style) return;
            if (model != null) HideAndDestroy(model.gameObject);
            hand = back = null;
            handDefinition = backDefinition = null;
            model = OperatorVisual.Create(pivot, style, layer: pivot.gameObject.layer);
            model.PoseShowcase(null);
        }

        internal void SyncGear(GridInventory bag, WeaponDefinition boundBlade)
        {
            if (model == null) return;
            model.SyncGear(bag);
            var catalog = bag?.Catalog;
            var held = bag?.Equipped(EquipmentSlot.Weapon);
            var stowed = bag?.Equipped(EquipmentSlot.PrimaryTwo);
            if (showBoundBlade) held ??= bag?.Equipped(EquipmentSlot.Sidearm) ?? bag?.Equipped(EquipmentSlot.Melee);
            var heldRule = catalog?.Find(held?.ItemId)?.Weapon ?? (showBoundBlade ? boundBlade : null);
            var backRule = catalog?.Find(stowed?.ItemId)?.Weapon;
            if (backRule != null || back != null)
                Sync(ref back, ref backDefinition, ref backLook, model.CreateBackMount(), stowed, backRule, catalog, false);
            Sync(ref hand, ref handDefinition, ref handLook, model.WeaponMount, held, heldRule, catalog, true);
            model.PoseShowcase(hand);
        }

        static void Sync(ref WeaponVisual view, ref WeaponDefinition previous, ref int previousLook, Transform mount,
            ItemStack stack, WeaponDefinition rule, ItemCatalog catalog, bool held)
        {
            if (rule == null)
            {
                if (view != null) HideAndDestroy(view.gameObject);
                view = null;
                previous = null;
                return;
            }
            int look = WeaponRules.Look(stack, catalog);
            if (view != null && previous == rule && look == previousLook) return;
            if (view == null)
            {
                view = WeaponVisual.Create(mount, false, rule.Form);
                view.name = held ? "Equipped hand weapon" : "Equipped back weapon";
            }
            else view.SetForm(rule.Form);
            previous = rule;
            previousLook = look;
            view.SetConfiguration(stack, catalog, rule);
            if (!held)
            {
                view.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                view.transform.localScale = Vector3.one * .84f;
            }
        }

        internal void Tick(float dt)
        {
            if (model != null) model.SetMotion(0f, false, false, dt);
        }

        internal void Turn(float degrees)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees)) return;
            yaw = Mathf.Repeat(yaw + degrees, 360f);
            ApplyAngle();
        }

        internal void Flip()
        {
            yaw = yaw < 90f || yaw >= 270f ? 180f : 0f;
            ApplyAngle();
        }

        void ApplyAngle() => pivot.localRotation = Quaternion.Euler(0f, facing + baseYaw + yaw, 0f);

        static void HideAndDestroy(GameObject root)
        {
            root.SetActive(false);
            if (Application.isPlaying) Object.Destroy(root);
            else Object.DestroyImmediate(root);
        }
    }
}
