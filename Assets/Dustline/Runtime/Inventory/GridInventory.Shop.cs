using System;
using System.Collections.Generic;

namespace Dustline
{
    public sealed partial class GridInventory
    {
        internal int MaxAddable(string itemId, int limit)
        {
            var item = Catalog.Find(itemId);
            if (item == null || limit < 1 || !CanStore(item, out _)) return 0;
            int stackLimit = StackLimit(item);
            long upper = 0, footprint = (long)item.Width * item.Height;
            foreach (var area in CarryAreas)
            {
                var layout = Layout(area);
                upper += Math.Max(0, layout.Capacity - OccupiedCells(area)) / footprint * stackLimit;
            }
            foreach (var stack in items)
                if (stack.Slot == EquipmentSlot.None && stack.ItemId == itemId && Array.IndexOf(CarryAreas, stack.Area) >= 0)
                    upper += stackLimit - stack.Quantity;

            int high = (int)Math.Min(Math.Min(upper, limit), int.MaxValue - Count(itemId));
            if (high <= 0 || Fits(high)) return Math.Max(0, high);
            int low = 0;
            while (high - low > 1)
            {
                int next = low + (high - low) / 2;
                if (Fits(next)) low = next;
                else high = next;
            }
            return low;

            bool Fits(int quantity) => Draft().TryAdd(itemId, quantity, out _);
        }

        bool TryStageWarehouseAmmo(ItemDefinition definition, List<ItemStack> staged, int stackLimit, int remaining)
        {
            var occupied = new bool[baseWidth * baseHeight];
            foreach (var stack in staged)
            {
                if (stack.Slot != EquipmentSlot.None || stack.Area != CargoArea.Main) continue;
                var item = Catalog.Find(stack.ItemId);
                int width = stack.Rotated ? item.Height : item.Width;
                int height = stack.Rotated ? item.Width : item.Height;
                for (int y = stack.Y; y < stack.Y + height; y++)
                    for (int x = stack.X; x < stack.X + width; x++)
                        occupied[y * baseWidth + x] = true;
            }
            for (int cell = 0; remaining > 0 && cell < occupied.Length; cell++)
            {
                if (occupied[cell]) continue;
                int quantity = Math.Min(remaining, stackLimit);
                var stack = new ItemStack { ItemId = definition.Id, InstanceId = Guid.NewGuid().ToString("N"),
                    Quantity = quantity, X = cell % baseWidth, Y = cell / baseWidth };
                if (!Fits(stack, definition, staged, null)) return false;
                staged.Add(stack);
                remaining -= quantity;
            }
            return remaining == 0;
        }
    }
}
