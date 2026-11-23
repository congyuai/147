using System;

namespace Dustline
{
    public static class WeaponRules
    {
        public static string Socket(WeaponDefinition weapon, PartSlot slot)
        {
            if (weapon == null || weapon.Melee || string.IsNullOrEmpty(weapon.FrameInterface)) return null;
            bool compact = weapon.Form == WeaponForm.Pistol;
            return slot switch
            {
                PartSlot.Sight => compact ? "glass-mini" : "glass-line",
                PartSlot.Grip => compact ? "hand-mini" : "hand-line",
                PartSlot.Stock => compact ? null : "tail-line",
                PartSlot.Muzzle => compact ? "front-mini" : "front-line",
                PartSlot.Magazine => weapon.FrameInterface,
                _ => null
            };
        }

        public static bool CanMount(ItemDefinition weapon, PartSlot slot, ItemDefinition part, out string error)
        {
            string socket = Socket(weapon?.Weapon, slot);
            if (string.IsNullOrEmpty(socket)) { error = "该枪型没有这个配件槽。"; return false; }
            if (part == null || part.Kind != ItemKind.Part || part.Part.Slot != slot || part.Part.Interface != socket)
            { error = "配件接口不匹配。"; return false; }
            if (!string.IsNullOrEmpty(part.Part.DedicatedItem) && part.Part.DedicatedItem != weapon.Id)
            { error = "该配件仅适用于指定型号。"; return false; }
            error = null;
            return true;
        }

        public static bool CanLoad(WeaponDefinition weapon, ItemDefinition ammo, out string error)
        {
            if (weapon == null || weapon.Melee) { error = "当前武器不能装填弹药。"; return false; }
            if (ammo == null || ammo.Kind != ItemKind.Ammo || (weapon.AmmoFamily == AmmoFamily.None
                    ? ammo.Id != weapon.AmmoId : ammo.AmmoFamily != weapon.AmmoFamily))
            { error = "弹药规格与当前武器不符。"; return false; }
            error = null;
            return true;
        }

        public static bool CanLoad(WeaponDefinition weapon, ItemStack stack, ItemDefinition ammo, out string error)
        {
            if (!CanLoad(weapon, ammo, out error)) return false;
            if (stack != null && stack.LoadedAmmo > 0 && LoadedType(weapon, stack) != ammo.Id)
            { error = "不能混装弹种，请先卸下原有弹药。"; return false; }
            return true;
        }

        public static string LoadedType(WeaponDefinition weapon, ItemStack stack) =>
            string.IsNullOrEmpty(stack?.LoadedAmmoId) ? weapon?.AmmoId : stack.LoadedAmmoId;

        public static int Capacity(WeaponDefinition weapon, ItemStack stack, ItemCatalog catalog)
        {
            if (weapon == null || weapon.Melee) return 0;
            var part = stack == null ? null : catalog?.Find(stack.Parts.Magazine.ItemId);
            return part != null && part.Part.Capacity > 0 ? part.Part.Capacity : weapon.MagazineSize;
        }

        internal static int PartLook(ItemDefinition item)
        {
            var part = item.Part;
            return (int)part.Slot <= (int)PartSlot.Stock && part.Form <= 2 ? part.Form + 2 : part.Form;
        }

        public static int VisualLevel(ItemStack stack, ItemCatalog catalog, PartSlot slot)
        {
            if (stack == null) return 0;
            var part = catalog?.Find(stack.Parts.Get(slot).ItemId);
            if (part != null) return PartLook(part);
            return (int)slot <= (int)PartSlot.Stock ? WeaponTuning.Level(stack.Mods, (WeaponModSlot)slot) : 0;
        }

        public static int Look(ItemStack stack, ItemCatalog catalog)
        {
            int key = 0;
            for (int i = 0; i <= (int)PartSlot.Magazine; i++) key |= (VisualLevel(stack, catalog, (PartSlot)i) & 7) << (i * 3);
            return key;
        }

        internal static bool SameParts(WeaponParts a, WeaponParts b)
        {
            for (int i = 0; i <= (int)PartSlot.Magazine; i++)
                if (a.Get((PartSlot)i).ItemId != b.Get((PartSlot)i).ItemId) return false;
            return true;
        }

        internal static bool VerifyLinks(ItemStack stack, ItemDefinition definition, ItemCatalog catalog, out string error)
        {
            error = null;
            for (int i = 0; i <= (int)PartSlot.Magazine; i++)
            {
                var slot = (PartSlot)i;
                PartLink link = stack.Parts.Get(slot);
                if (link.Empty) continue;
                if (string.IsNullOrWhiteSpace(link.InstanceId) || string.IsNullOrWhiteSpace(link.ItemId))
                { error = "配件实例无效。"; return false; }
                if (!CanMount(definition, slot, catalog.Find(link.ItemId), out error)) return false;
            }
            return true;
        }
    }
}
