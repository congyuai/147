using System;

namespace Dustline
{
    public enum WeaponModSlot { Sight, Grip, Stock }

    [Serializable]
    public struct WeaponMods
    {
        public int Sight, Grip, Stock;
        public int OwnedSight, OwnedGrip, OwnedStock;

        internal bool IsEmpty => Sight == 0 && Grip == 0 && Stock == 0
            && OwnedSight == 0 && OwnedGrip == 0 && OwnedStock == 0;
    }

    public readonly struct WeaponStats
    {
        public readonly float Damage, FireInterval, Range, ReloadSeconds, Recoil;
        public readonly float HipSpread, AimSpread, AimFov, AimSeconds;

        internal WeaponStats(float damage, float fireInterval, float range, float reloadSeconds, float recoil,
            float hipSpread, float aimSpread, float aimFov, float aimSeconds)
        {
            Damage = damage;
            FireInterval = fireInterval;
            Range = range;
            ReloadSeconds = reloadSeconds;
            Recoil = recoil;
            HipSpread = hipSpread;
            AimSpread = aimSpread;
            AimFov = aimFov;
            AimSeconds = aimSeconds;
        }
    }

    public static class WeaponTuning
    {
        public const int MaxUpgradeLevel = 2;

        public static int Level(WeaponMods mods, WeaponModSlot slot) => slot switch
        {
            WeaponModSlot.Sight => mods.Sight,
            WeaponModSlot.Grip => mods.Grip,
            WeaponModSlot.Stock => mods.Stock,
            _ => 0
        };

        public static int Owned(WeaponMods mods, WeaponModSlot slot) => slot switch
        {
            WeaponModSlot.Sight => mods.OwnedSight,
            WeaponModSlot.Grip => mods.OwnedGrip,
            WeaponModSlot.Stock => mods.OwnedStock,
            _ => 0
        };

        public static bool TrySelect(WeaponMods current, WeaponModSlot slot, int level, out WeaponMods selected, out int cost)
        {
            selected = current;
            cost = 0;
            if (level < 0 || level > MaxUpgradeLevel || !IsValid(current)) return false;
            int owned = Owned(current, slot);
            if (level > owned + 1) return false;
            int newOwned = Math.Max(owned, level);
            switch (slot)
            {
                case WeaponModSlot.Sight: selected.Sight = level; selected.OwnedSight = newOwned; break;
                case WeaponModSlot.Grip: selected.Grip = level; selected.OwnedGrip = newOwned; break;
                case WeaponModSlot.Stock: selected.Stock = level; selected.OwnedStock = newOwned; break;
                default: return false;
            }
            if (newOwned > owned) cost = 100 * newOwned;
            return true;
        }

        public static WeaponStats Evaluate(WeaponDefinition definition, WeaponMods mods)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (!SupportsUpgrades(definition) || !IsValid(mods)) mods = default;
            return new WeaponStats(definition.Damage, definition.FireInterval,
                definition.Range * (1f + 0.04f * mods.Sight),
                definition.ReloadSeconds * (1f + 0.06f * mods.Grip) * (1f - 0.06f * mods.Stock),
                definition.Recoil * (1f - 0.08f * mods.Grip) * (1f - 0.13f * mods.Stock),
                0.012f * (1f - 0.17f * mods.Grip) * (1f + 0.09f * mods.Stock),
                0.002f * (1f - 0.20f * mods.Sight),
                48f - 6f * mods.Sight,
                0.18f + 0.043f * mods.Sight - 0.018f * mods.Stock);
        }

        public static bool SupportsUpgrades(WeaponDefinition definition) =>
            definition != null && definition.Form == WeaponForm.Rifle && !definition.Melee;

        internal static bool IsValidForItem(string itemId, WeaponMods mods) =>
            mods.IsEmpty || itemId == WeaponDefinition.RifleId && IsValid(mods);

        internal static bool IsValid(WeaponMods mods) =>
            ValidPair(mods.Sight, mods.OwnedSight) && ValidPair(mods.Grip, mods.OwnedGrip)
            && ValidPair(mods.Stock, mods.OwnedStock);

        private static bool ValidPair(int level, int owned) => level >= 0 && level <= owned && owned <= MaxUpgradeLevel;
    }
}
