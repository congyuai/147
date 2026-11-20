using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dustline
{
    [CreateAssetMenu(menuName = "Dustline/Item Catalog")]
    public sealed class ItemCatalog : ScriptableObject
    {
        public List<ItemDefinition> Items = new List<ItemDefinition>();

        private Dictionary<string, ItemDefinition> index;

        public ItemDefinition Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;

            if (index == null)
                RebuildIndex();

            return index.TryGetValue(id, out ItemDefinition definition) ? definition : null;
        }

        public void RebuildIndex()
        {
            index = null;
            if (Items == null)
                throw new ArgumentException("Catalog items cannot be null.", nameof(Items));

            var rebuilt = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
            foreach (ItemDefinition definition in Items)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
                    throw new ArgumentException("Every catalog item needs a non-empty ID.", nameof(Items));
                if (!Enum.IsDefined(typeof(ItemKind), definition.Kind)
                    || definition.Width < 1 || definition.Height < 1 || definition.MaxStack < 1
                    || float.IsNaN(definition.UnitWeight) || float.IsInfinity(definition.UnitWeight)
                    || definition.UnitWeight < 0f)
                    throw new ArgumentException("Invalid inventory definition: " + definition.Id, nameof(Items));
                if (definition.Weapon != null && !definition.Weapon.Melee && definition.Weapon.MagazineSize < 1)
                    throw new ArgumentException("Invalid magazine size: " + definition.Id, nameof(Items));
                if (rebuilt.ContainsKey(definition.Id))
                    throw new ArgumentException("Duplicate item ID: " + definition.Id, nameof(Items));

                rebuilt.Add(definition.Id, definition);
            }

            index = rebuilt;
        }

        private void OnValidate()
        {
            index = null;
        }
    }
}
