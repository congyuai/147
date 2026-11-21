using System;

namespace Dustline
{
    [Serializable]
    public sealed class ItemStack
    {
        public string InstanceId;
        public string ItemId;
        public int Quantity = 1;
        public int X;
        public int Y;
        public bool Rotated;
        public EquipmentSlot Slot;
        public int LoadedAmmo = -1;
        public WeaponMods Mods;

        public ItemStack Clone()
        {
            return (ItemStack)MemberwiseClone();
        }
    }
}
