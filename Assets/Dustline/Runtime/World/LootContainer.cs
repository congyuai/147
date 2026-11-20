using UnityEngine;

namespace Dustline
{
    public sealed class LootContainer : MonoBehaviour
    {
        public string DisplayName { get; private set; }
        public GridInventory Inventory { get; private set; }
        public void Initialize(string title, ItemCatalog catalog)
        {
            DisplayName = title;
            Inventory = new GridInventory(6, 5, 100, catalog);
        }
    }
}
