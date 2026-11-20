using System;
using System.Collections.Generic;
using System.IO;

namespace Dustline
{
    public sealed class GridInventory
    {
        private readonly List<ItemStack> items = new List<ItemStack>();
        private readonly IReadOnlyList<ItemStack> view;

        public GridInventory(int width, int height, float maxWeight, ItemCatalog catalog)
        {
            if (width < 1)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height < 1)
                throw new ArgumentOutOfRangeException(nameof(height));
            if (float.IsNaN(maxWeight) || float.IsInfinity(maxWeight) || maxWeight < 0f)
                throw new ArgumentOutOfRangeException(nameof(maxWeight));
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

            catalog.RebuildIndex();
            Width = width;
            Height = height;
            MaxWeight = maxWeight;
            Catalog = catalog;
            view = items.AsReadOnly();
        }

        public int Width { get; }
        public int Height { get; }
        public float MaxWeight { get; }
        public float Weight => (float)CalculateWeight(items);
        public ItemCatalog Catalog { get; }
        public IReadOnlyList<ItemStack> Items => view;
        public event Action Changed;

        public bool TryAdd(string itemId, int quantity, out string error)
        {
            error = null;
            if (quantity < 1)
            {
                error = "Quantity must be positive.";
                return false;
            }

            ItemDefinition definition = Catalog.Find(itemId);
            if (definition == null)
            {
                error = "Unknown item ID: " + itemId;
                return false;
            }
            if (!CanCarry(CalculateWeight(items) + (double)definition.UnitWeight * quantity))
            {
                error = "Weight limit exceeded.";
                return false;
            }
            if ((long)Count(itemId) + quantity > int.MaxValue)
            {
                error = "Item count limit exceeded.";
                return false;
            }

            List<ItemStack> staged = Snapshot();
            int remaining = quantity;
            int limit = StackLimit(definition);
            foreach (ItemStack stack in staged)
            {
                if (stack.Slot != EquipmentSlot.None || stack.ItemId != itemId)
                    continue;

                int added = Math.Min(remaining, limit - stack.Quantity);
                stack.Quantity += added;
                remaining -= added;
                if (remaining == 0)
                    break;
            }

            while (remaining > 0)
            {
                int amount = Math.Min(remaining, limit);
                var stack = new ItemStack
                {
                    InstanceId = Guid.NewGuid().ToString("N"),
                    ItemId = itemId,
                    Quantity = amount
                };
                if (!TryPlace(stack, definition, staged))
                {
                    error = "Not enough backpack space.";
                    return false;
                }
                staged.Add(stack);
                remaining -= amount;
            }

            Commit(staged);
            Touch();
            return true;
        }

        public bool TryInsert(ItemStack item, out string error)
        {
            if (!TryPrepareInsert(item, out ItemStack inserted, out error))
                return false;

            items.Add(inserted);
            Touch();
            return true;
        }

        public bool TryMove(string instanceId, int x, int y, bool rotated, out string error)
        {
            error = null;
            ItemStack stack = Find(instanceId);
            if (stack == null)
            {
                error = "Item not found.";
                return false;
            }
            if (stack.Slot != EquipmentSlot.None)
            {
                error = "Unequip the item before moving it.";
                return false;
            }

            ItemStack moved = stack.Clone();
            moved.X = x;
            moved.Y = y;
            moved.Rotated = rotated;
            if (!Fits(moved, Catalog.Find(stack.ItemId), items, stack.InstanceId))
            {
                error = "The position is occupied or outside the backpack.";
                return false;
            }

            stack.X = x;
            stack.Y = y;
            stack.Rotated = rotated;
            Touch();
            return true;
        }

        public bool TryEquip(string instanceId, EquipmentSlot slot, out string error)
        {
            error = null;
            ItemStack stack = Find(instanceId);
            if (stack == null)
            {
                error = "Item not found.";
                return false;
            }
            if (!MatchesSlot(Catalog.Find(stack.ItemId), slot))
            {
                error = "This item does not match the equipment slot.";
                return false;
            }
            if (stack.Slot != EquipmentSlot.None || Equipped(slot) != null)
            {
                error = "Unequip the existing item first.";
                return false;
            }

            stack.Slot = slot;
            stack.X = 0;
            stack.Y = 0;
            Touch();
            return true;
        }

        public bool TryUnequip(EquipmentSlot slot, out string error)
        {
            error = null;
            ItemStack stack = Equipped(slot);
            if (stack == null)
            {
                error = "Equipment slot is empty or invalid.";
                return false;
            }

            ItemStack moved = stack.Clone();
            moved.Slot = EquipmentSlot.None;
            if (!TryPlace(moved, Catalog.Find(stack.ItemId), items))
            {
                error = "Not enough backpack space.";
                return false;
            }

            stack.X = moved.X;
            stack.Y = moved.Y;
            stack.Rotated = moved.Rotated;
            stack.Slot = EquipmentSlot.None;
            Touch();
            return true;
        }

        public bool TryTransferTo(string instanceId, GridInventory destination, out string error)
        {
            error = null;
            if (destination == null || ReferenceEquals(this, destination))
            {
                error = "A different destination inventory is required.";
                return false;
            }

            ItemStack stack = Find(instanceId);
            if (stack == null)
            {
                error = "Item not found.";
                return false;
            }
            if (!destination.TryPrepareInsert(stack, out ItemStack inserted, out error))
                return false;

            destination.items.Add(inserted);
            items.Remove(stack);
            try
            {
                Touch();
            }
            finally
            {
                destination.Touch();
            }
            return true;
        }

        public bool Remove(string instanceId)
        {
            ItemStack stack = Find(instanceId);
            if (stack == null)
                return false;

            items.Remove(stack);
            Touch();
            return true;
        }

        public int Count(string itemId)
        {
            int count = 0;
            foreach (ItemStack stack in items)
            {
                if (stack.ItemId == itemId)
                    count += stack.Quantity;
            }
            return count;
        }

        public bool Consume(string itemId, int quantity)
        {
            if (quantity < 1)
                return false;

            int available = 0;
            foreach (ItemStack stack in items)
            {
                if (stack.Slot == EquipmentSlot.None && stack.ItemId == itemId)
                    available += stack.Quantity;
            }
            if (available < quantity)
                return false;

            for (int i = 0; i < items.Count && quantity > 0;)
            {
                ItemStack stack = items[i];
                if (stack.Slot != EquipmentSlot.None || stack.ItemId != itemId)
                {
                    i++;
                    continue;
                }

                int consumed = Math.Min(quantity, stack.Quantity);
                stack.Quantity -= consumed;
                quantity -= consumed;
                if (stack.Quantity == 0)
                    items.RemoveAt(i);
                else
                    i++;
            }

            Touch();
            return true;
        }

        public ItemStack Equipped(EquipmentSlot slot)
        {
            if (slot != EquipmentSlot.Weapon && slot != EquipmentSlot.Armor)
                return null;

            foreach (ItemStack stack in items)
            {
                if (stack.Slot == slot)
                    return stack;
            }
            return null;
        }

        public ItemStack Find(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
                return null;

            foreach (ItemStack stack in items)
            {
                if (stack.InstanceId == instanceId)
                    return stack;
            }
            return null;
        }

        public List<ItemStack> Snapshot()
        {
            var snapshot = new List<ItemStack>(items.Count);
            foreach (ItemStack stack in items)
                snapshot.Add(stack.Clone());
            return snapshot;
        }

        public void Restore(List<ItemStack> items)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));

            var restored = new List<ItemStack>(items.Count);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var slots = new HashSet<EquipmentSlot>();
            var counts = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (ItemStack input in items)
            {
                if (!ValidateStack(input, out ItemDefinition definition, out string error))
                    throw new InvalidDataException(error);
                if (!ids.Add(input.InstanceId))
                    throw new InvalidDataException("Duplicate instance ID: " + input.InstanceId);
                if (input.X < 0 || input.Y < 0 || input.X >= Width || input.Y >= Height)
                    throw new InvalidDataException("Invalid item coordinates.");

                ItemStack stack = input.Clone();
                if (stack.Slot == EquipmentSlot.None)
                {
                    if (!Fits(stack, definition, restored, null))
                        throw new InvalidDataException("Items overlap or extend outside the backpack.");
                }
                else if (!MatchesSlot(definition, stack.Slot) || !slots.Add(stack.Slot))
                {
                    throw new InvalidDataException("Invalid or duplicate equipment slot.");
                }

                counts.TryGetValue(stack.ItemId, out long count);
                count += stack.Quantity;
                if (count > int.MaxValue)
                    throw new InvalidDataException("Item count limit exceeded.");
                counts[stack.ItemId] = count;
                restored.Add(stack);
            }

            if (!CanCarry(CalculateWeight(restored)))
                throw new InvalidDataException("Weight limit exceeded.");

            Commit(restored);
            Touch();
        }

        public void Touch()
        {
            Changed?.Invoke();
        }

        private bool TryPrepareInsert(ItemStack input, out ItemStack inserted, out string error)
        {
            inserted = null;
            if (!ValidateStack(input, out ItemDefinition definition, out error))
                return false;
            if (Find(input.InstanceId) != null)
            {
                error = "Duplicate instance ID: " + input.InstanceId;
                return false;
            }
            if ((long)Count(input.ItemId) + input.Quantity > int.MaxValue)
            {
                error = "Item count limit exceeded.";
                return false;
            }
            if (!CanCarry(CalculateWeight(items) + (double)definition.UnitWeight * input.Quantity))
            {
                error = "Weight limit exceeded.";
                return false;
            }

            ItemStack candidate = input.Clone();
            candidate.Slot = EquipmentSlot.None;
            if (!TryPlace(candidate, definition, items))
            {
                error = "Not enough backpack space.";
                return false;
            }

            inserted = candidate;
            return true;
        }

        private bool ValidateStack(ItemStack stack, out ItemDefinition definition, out string error)
        {
            definition = null;
            error = null;
            if (stack == null || string.IsNullOrWhiteSpace(stack.InstanceId)
                || string.IsNullOrWhiteSpace(stack.ItemId))
            {
                error = "An item needs non-empty instance and definition IDs.";
                return false;
            }

            definition = Catalog.Find(stack.ItemId);
            if (definition == null)
            {
                error = "Unknown item ID: " + stack.ItemId;
                return false;
            }
            if (stack.Quantity < 1 || stack.Quantity > StackLimit(definition))
            {
                error = "Invalid stack quantity.";
                return false;
            }
            if (stack.LoadedAmmo < -1
                || (definition.Kind != ItemKind.Weapon && stack.LoadedAmmo != -1)
                || (definition.Kind == ItemKind.Weapon && definition.Weapon != null
                    && stack.LoadedAmmo > definition.Weapon.MagazineSize))
            {
                error = "Invalid magazine ammunition count.";
                return false;
            }
            return true;
        }

        private bool TryPlace(ItemStack stack, ItemDefinition definition, List<ItemStack> occupied)
        {
            bool preferred = stack.Rotated;
            for (int orientation = 0; orientation < 2; orientation++)
            {
                stack.Rotated = orientation == 0 ? preferred : !preferred;
                int width = stack.Rotated ? definition.Height : definition.Width;
                int height = stack.Rotated ? definition.Width : definition.Height;
                for (int y = 0; y <= Height - height; y++)
                {
                    for (int x = 0; x <= Width - width; x++)
                    {
                        stack.X = x;
                        stack.Y = y;
                        if (Fits(stack, definition, occupied, null))
                            return true;
                    }
                }
                if (definition.Width == definition.Height)
                    break;
            }
            return false;
        }

        private bool Fits(ItemStack candidate, ItemDefinition definition, List<ItemStack> occupied,
            string ignoredInstanceId)
        {
            int width = candidate.Rotated ? definition.Height : definition.Width;
            int height = candidate.Rotated ? definition.Width : definition.Height;
            if (candidate.X < 0 || candidate.Y < 0 || candidate.X > Width - width
                || candidate.Y > Height - height)
                return false;

            foreach (ItemStack other in occupied)
            {
                if (other.Slot != EquipmentSlot.None || other.InstanceId == ignoredInstanceId)
                    continue;

                ItemDefinition otherDefinition = Catalog.Find(other.ItemId);
                int otherWidth = other.Rotated ? otherDefinition.Height : otherDefinition.Width;
                int otherHeight = other.Rotated ? otherDefinition.Width : otherDefinition.Height;
                if (candidate.X < (long)other.X + otherWidth && (long)candidate.X + width > other.X
                    && candidate.Y < (long)other.Y + otherHeight && (long)candidate.Y + height > other.Y)
                    return false;
            }
            return true;
        }

        private double CalculateWeight(List<ItemStack> stacks)
        {
            double weight = 0d;
            foreach (ItemStack stack in stacks)
                weight += (double)Catalog.Find(stack.ItemId).UnitWeight * stack.Quantity;
            return weight;
        }

        private bool CanCarry(double weight)
        {
            return (float)weight <= MaxWeight;
        }

        private static int StackLimit(ItemDefinition definition)
        {
            return definition.Kind == ItemKind.Weapon || definition.Kind == ItemKind.Armor
                ? 1 : definition.MaxStack;
        }

        private static bool MatchesSlot(ItemDefinition definition, EquipmentSlot slot)
        {
            return (slot == EquipmentSlot.Weapon && definition.Kind == ItemKind.Weapon)
                || (slot == EquipmentSlot.Armor && definition.Kind == ItemKind.Armor);
        }

        private void Commit(List<ItemStack> replacement)
        {
            var existing = new Dictionary<string, ItemStack>(StringComparer.Ordinal);
            foreach (ItemStack stack in items)
                existing.Add(stack.InstanceId, stack);

            for (int i = 0; i < replacement.Count; i++)
            {
                ItemStack update = replacement[i];
                if (!existing.TryGetValue(update.InstanceId, out ItemStack current))
                    continue;

                current.ItemId = update.ItemId;
                current.Quantity = update.Quantity;
                current.X = update.X;
                current.Y = update.Y;
                current.Rotated = update.Rotated;
                current.Slot = update.Slot;
                current.LoadedAmmo = update.LoadedAmmo;
                replacement[i] = current;
            }

            items.Clear();
            items.AddRange(replacement);
        }
    }
}
