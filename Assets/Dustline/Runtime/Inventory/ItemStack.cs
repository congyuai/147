using System;

namespace Dustline
{
    [Serializable]
    public struct PartLink
    {
        public string ItemId, InstanceId;
        public bool Empty => string.IsNullOrEmpty(ItemId) && string.IsNullOrEmpty(InstanceId);
    }

    [Serializable]
    public struct WeaponParts
    {
        public PartLink Sight, Grip, Stock, Muzzle, Magazine;
        public PartLink Get(PartSlot slot) => slot switch
        {
            PartSlot.Sight => Sight, PartSlot.Grip => Grip, PartSlot.Stock => Stock,
            PartSlot.Muzzle => Muzzle, PartSlot.Magazine => Magazine, _ => default
        };

        public void Set(PartSlot slot, PartLink item)
        {
            switch (slot)
            {
                case PartSlot.Sight: Sight = item; break;
                case PartSlot.Grip: Grip = item; break;
                case PartSlot.Stock: Stock = item; break;
                case PartSlot.Muzzle: Muzzle = item; break;
                case PartSlot.Magazine: Magazine = item; break;
            }
        }

        public bool Empty => Sight.Empty && Grip.Empty && Stock.Empty && Muzzle.Empty && Magazine.Empty;
    }

    public enum CargoArea { Main, Rig }

    [Serializable]
    public sealed class ItemStack
    {
        public string InstanceId;
        public string ItemId;
        public int Quantity = 1;
        public int X;
        public int Y;
        public bool Rotated;
        public CargoArea Area;
        public EquipmentSlot Slot;
        public int LoadedAmmo = -1;
        public WeaponMods Mods;
        public WeaponParts Parts;
        public string LoadedAmmoId = "";
        public int UsesSpent, Wear;

        public ItemStack Clone()
        {
            return (ItemStack)MemberwiseClone();
        }
    }
}
