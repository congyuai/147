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
                    || definition.UnitWeight < 0f || definition.Value < 0)
                    throw new ArgumentException("Invalid inventory definition: " + definition.Id, nameof(Items));
                if (definition.Weapon != null && !definition.Weapon.Melee && definition.Weapon.MagazineSize < 1)
                    throw new ArgumentException("Invalid magazine size: " + definition.Id, nameof(Items));
                if ((definition.Kind == ItemKind.Rig || definition.Kind == ItemKind.Pack) && (definition.Storage.Columns < 1
                    || definition.Storage.Columns > 12 || definition.Storage.Rows < 1 || definition.Storage.Rows > 12
                    || definition.Storage.Split < 0 || definition.Storage.Split >= definition.Storage.Columns
                    || !(definition.Storage.CarryBonus >= 0f) || float.IsInfinity(definition.Storage.CarryBonus)
                    || !(definition.Storage.WeightLimit >= 0f) || float.IsInfinity(definition.Storage.WeightLimit)))
                    throw new ArgumentException("Invalid cargo capacity: " + definition.Id, nameof(Items));
                if (definition.Kind == ItemKind.Part && (!Enum.IsDefined(typeof(PartSlot), definition.Part.Slot)
                    || definition.Part.Form < 1 || definition.Part.Form > 3 || string.IsNullOrWhiteSpace(definition.Part.Interface)
                    || definition.Part.Slot == PartSlot.Magazine && definition.Part.Capacity < 1))
                    throw new ArgumentException("Invalid part fitting: " + definition.Id, nameof(Items));
                if (definition.Kind == ItemKind.Medicine && (definition.Uses < 1 || definition.Uses > 1000
                    || !Enum.IsDefined(typeof(MedicineForm), definition.MedicineVisual)
                    || !Enum.IsDefined(typeof(TreatmentKind), definition.Treatment)))
                    throw new ArgumentException("Invalid treatment item: " + definition.Id, nameof(Items));
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
