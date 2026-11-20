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
        public string KindName => Kind switch
        {
            ItemKind.Weapon => "武器",
            ItemKind.Ammo => "弹药",
            ItemKind.Medicine => "医疗品",
            ItemKind.Armor => "护甲",
            ItemKind.Valuable => "贵重物品",
            _ => "物品"
        };
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
