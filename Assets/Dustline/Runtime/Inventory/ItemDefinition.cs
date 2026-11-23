using UnityEngine;

namespace Dustline
{
    public enum ItemKind
    {
        Weapon,
        Ammo,
        Medicine,
        Armor,
        Valuable,
        Part, Helmet, Rig, Pack
    }

    public enum MedicineForm { Pouch, Roll, Case, Tourniquet, Splint, Tablets, Surgery }
    public enum TreatmentKind { Heal, LightBleed, HeavyBleed, Fracture, Pain, Surgery }
    public enum AmmoFamily { None, Amber, Silver, Slate, Copper }
    public enum PartSlot { Sight, Grip, Stock, Muzzle, Magazine }

    [System.Serializable]
    public struct StorageSpec
    {
        public int Columns, Rows, Split;
        public float CarryBonus, WeightLimit;
    }

    [System.Serializable]
    public struct PartSpec
    {
        public PartSlot Slot;
        public string Interface, DedicatedItem;
        public int Form, Capacity;
        public float Stability, ReloadFactor, AimFactor;
    }

    public enum EquipmentSlot
    {
        None,
        Weapon,
        Armor, Helmet, Rig, Pack
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
            ItemKind.Helmet => "头盔",
            ItemKind.Rig => "弹挂",
            ItemKind.Pack => "背包",
            ItemKind.Part => "配件",
            ItemKind.Valuable => "贵重物品",
            _ => "物品"
        };
        public int Width = 1;
        public int Height = 1;
        public int MaxStack = 1;
        public int Value;
        public float UnitWeight;
        public float HealAmount = 40f;
        public float UseSeconds = 2.1f;
        public MedicineForm MedicineVisual;
        public TreatmentKind Treatment;
        public int Uses = 1;
        public int Tier, Durability;
        public BodyCoverage Coverage;
        public StorageSpec Storage;
        public AmmoFamily AmmoFamily;
        public PartSpec Part;
        public float DamageReduction = 0.3f;
        public Color Tint;
        public WeaponDefinition Weapon;

        public EquipmentSlot Equipment => Kind switch
        {
            ItemKind.Weapon => EquipmentSlot.Weapon,
            ItemKind.Armor => EquipmentSlot.Armor,
            ItemKind.Helmet => EquipmentSlot.Helmet,
            ItemKind.Rig => EquipmentSlot.Rig,
            ItemKind.Pack => EquipmentSlot.Pack,
            _ => EquipmentSlot.None
        };

        public static Color GradeColor(int tier) => tier switch
        {
            1 => new Color32(226, 231, 231, 255), 2 => new Color32(100, 195, 127, 255),
            3 => new Color32(103, 169, 233, 255), 4 => new Color32(177, 135, 233, 255),
            5 => new Color32(232, 182, 84, 255), 6 => new Color32(230, 99, 95, 255),
            _ => Color.clear
        };
    }
}
