using UnityEngine;

namespace Dustline
{
    public sealed class LootContainer : MonoBehaviour
    {
        public string DisplayName { get; private set; }
        public GridInventory Inventory { get; private set; }
        public float SearchSeconds { get; private set; } = 1.5f;
        public bool Searched { get; private set; }

        public void Initialize(string title, ItemCatalog catalog) => Initialize(title, catalog, 1.5f);

        internal void Initialize(string title, ItemCatalog catalog, float searchSeconds)
        {
            if (Inventory != null) return;
            Inventory = new GridInventory(6, 5, 100, catalog);
            DisplayName = title;
            SearchSeconds = searchSeconds;
        }

        public void Reveal()
        {
            if (Inventory != null) Searched = true;
        }
    }
}
