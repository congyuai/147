using UnityEngine;

namespace Dustline
{
    public enum LootKind { SupplyCrate, StorageLocker, Toolbox, MedicalCase, FieldBag, EquipmentCase }

    public sealed class LootContainer : MonoBehaviour
    {
        public string DisplayName { get; private set; }
        public LootKind Kind { get; private set; }
        public GridInventory Inventory { get; private set; }
        public float SearchSeconds { get; private set; } = 1.5f;
        public bool Searched { get; private set; }

        public void Initialize(string title, ItemCatalog catalog, float searchSeconds = 1.5f, LootKind kind = LootKind.FieldBag)
        {
            if (Inventory != null) return;
            Kind = kind;
            Inventory = new GridInventory(6, 5, 100, catalog, Accepts);
            DisplayName = title;
            SearchSeconds = searchSeconds;
        }

        public bool Accepts(ItemDefinition item)
        {
            if (item == null) return false;
            return Kind switch
            {
                LootKind.MedicalCase => item.Kind == ItemKind.Medicine,
                LootKind.Toolbox or LootKind.EquipmentCase => item.Kind == ItemKind.Valuable,
                LootKind.StorageLocker => item.Kind == ItemKind.Armor || item.Kind == ItemKind.Valuable,
                LootKind.SupplyCrate => item.Kind == ItemKind.Ammo || item.Kind == ItemKind.Weapon,
                LootKind.FieldBag => true,
                _ => false
            };
        }

        public void Reveal()
        {
            if (Inventory != null) Searched = true;
        }
    }
}
