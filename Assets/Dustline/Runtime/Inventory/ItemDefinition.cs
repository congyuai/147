using UnityEngine;

namespace Dustline
{
    public enum ItemKind
    {
        Weapon,
        Ammo,
        Medicine,
        Armor,
        Valuable
    }

    public enum EquipmentSlot
    {
        None,
        Weapon,
        Armor
    }

    [CreateAssetMenu(menuName = "Dustline/Item Definition")]
    public sealed class ItemDefinition : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        public ItemKind Kind;
        public int Width = 1;
        public int Height = 1;
        public int MaxStack = 1;
        public int Value;
        public float UnitWeight;
        public float HealAmount = 40f;
        public float DamageReduction = 0.3f;
        public Color Tint;
        public WeaponDefinition Weapon;
    }
}
