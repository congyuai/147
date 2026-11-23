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
        Transform display;
        string shown;

        public void Initialize(string title, ItemCatalog catalog, float searchSeconds = 1.5f, LootKind kind = LootKind.FieldBag)
        {
            if (Inventory != null) return;
            Kind = kind;
            Inventory = new GridInventory(6, 5, 100, catalog, Accepts);
            DisplayName = title;
            SearchSeconds = searchSeconds;
            Inventory.Changed += UpdateDisplay;
        }

        public bool Accepts(ItemDefinition item)
        {
            if (item == null) return false;
            return Kind switch
            {
                LootKind.MedicalCase => item.Kind == ItemKind.Medicine,
                LootKind.Toolbox => item.Kind == ItemKind.Valuable || item.Kind == ItemKind.Part,
                LootKind.EquipmentCase => item.Kind == ItemKind.Valuable || item.Kind == ItemKind.Part,
                LootKind.StorageLocker => item.Kind == ItemKind.Armor || item.Kind == ItemKind.Helmet || item.Kind == ItemKind.Rig
                    || item.Kind == ItemKind.Pack || item.Kind == ItemKind.Valuable,
                LootKind.SupplyCrate => item.Kind == ItemKind.Ammo || item.Kind == ItemKind.Weapon,
                LootKind.FieldBag => true,
                _ => false
            };
        }

        public void Reveal()
        {
            if (Inventory == null || Searched) return;
            Searched = true;
            UpdateDisplay();
        }

        void UpdateDisplay()
        {
            if (!Searched || !gameObject.activeInHierarchy) return;
            var stack = Inventory.Items.Count == 0 ? null : Inventory.Items[0];
            var def = Inventory.Catalog.Find(stack?.ItemId);
            string key = stack?.InstanceId;
            if (key == shown) return;
            shown = key;
            if (display != null) { display.gameObject.SetActive(false); Destroy(display.gameObject); }
            if (def == null) { display = null; return; }
            display = ItemProp.Create(transform, def, stack, Inventory.Catalog);
            var shell = GetComponent<BoxCollider>();
            float top = shell != null ? shell.center.y + shell.size.y * .5f : .1f;
            display.localPosition = new Vector3(0, top + .105f, -.035f);
            display.localRotation = Quaternion.Euler(def.Weapon != null ? 1f : -13f, 62f, def.Weapon != null ? 71f : 8f);
            display.localScale = Vector3.one * (def.Weapon != null ? .53f : 1.05f);
        }

        void OnDestroy()
        {
            if (Inventory != null) Inventory.Changed -= UpdateDisplay;
        }
    }
}
