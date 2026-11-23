using System;
using System.Collections.Generic;

namespace Dustline
{
    public sealed partial class GridInventory
    {
        internal bool TrySortStash(out string error)
        {
            if (adaptive || accepts != null || baseWidth != 12 || baseHeight != 10)
            {
                error = "仅可整理仓库。";
                return false;
            }

            var originals = new Dictionary<string, ItemStack>(StringComparer.Ordinal);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var stack in items)
            {
                if (!ValidateStack(stack, out _, out error)) return false;
                if (stack.Slot != EquipmentSlot.None || stack.Area != CargoArea.Main)
                {
                    error = "仅可整理普通仓库格子。";
                    return false;
                }
                if (!ids.Add(stack.InstanceId))
                {
                    error = "仓库物品实例 ID 重复。";
                    return false;
                }
                for (int i = 0; i <= (int)PartSlot.Magazine; i++)
                {
                    var link = stack.Parts.Get((PartSlot)i);
                    if (!link.Empty && !ids.Add(link.InstanceId))
                    {
                        error = "仓库配件实例 ID 重复。";
                        return false;
                    }
                }
                originals.Add(stack.InstanceId, stack);
            }
            if (!ValidStashLayout(items))
            {
                error = "仓库布局或负重无效。";
                return false;
            }

            var merged = Snapshot();
            // The placement order also groups identical definitions for the merge pass.
            merged.Sort(CompareStashStacks);
            ItemStack target = null;
            foreach (var stack in merged)
            {
                var definition = Catalog.Find(stack.ItemId);
                if (!CanMergeStashStack(stack, definition)) continue;
                int limit = StackLimit(definition);
                if (stack.Quantity == limit) continue;
                if (target != null && target.ItemId == stack.ItemId)
                {
                    int moved = Math.Min(limit - target.Quantity, stack.Quantity);
                    target.Quantity += moved;
                    stack.Quantity -= moved;
                    if (target.Quantity == limit) target = null;
                }
                if (stack.Quantity != 0) target = stack;
            }
            merged.RemoveAll(stack => stack.Quantity == 0);

            var packed = new List<ItemStack>(merged.Count);
            foreach (var stack in merged)
            {
                // Keep the original footprint available if the greedy layout runs out of room.
                var moved = stack.Clone();
                moved.Rotated = false;
                if (!TryPlace(moved, Catalog.Find(moved.ItemId), packed))
                {
                    packed = merged;
                    break;
                }
                packed.Add(moved);
            }
            if (!PreservesStashContents(packed, originals) || !ValidStashLayout(packed))
            {
                error = "仓库整理校验失败，未改动仓库。";
                return false;
            }

            Commit(packed);
            Touch();
            error = null;
            return true;
        }

        int CompareStashStacks(ItemStack a, ItemStack b)
        {
            var left = Catalog.Find(a.ItemId);
            var right = Catalog.Find(b.ItemId);
            int order = ((long)right.Width * right.Height).CompareTo((long)left.Width * left.Height);
            if (order == 0) order = string.CompareOrdinal(a.ItemId, b.ItemId);
            return order != 0 ? order : string.CompareOrdinal(a.InstanceId, b.InstanceId);
        }

        static bool CanMergeStashStack(ItemStack stack, ItemDefinition definition)
        {
            // UsesSpent describes one opened unit, so keep its containing stack intact.
            return StackLimit(definition) > 1 && definition.Weapon == null && definition.Durability == 0
                && stack.Wear == 0 && stack.UsesSpent == 0 && stack.Parts.Empty && stack.Mods.IsEmpty
                && stack.LoadedAmmo == -1 && string.IsNullOrEmpty(stack.LoadedAmmoId);
        }

        bool ValidStashLayout(List<ItemStack> layout)
        {
            foreach (var stack in layout)
                if (!Fits(stack, Catalog.Find(stack.ItemId), layout, stack.InstanceId)) return false;
            return true;
        }

        bool PreservesStashContents(List<ItemStack> packed, Dictionary<string, ItemStack> unmatched)
        {
            var balance = new Dictionary<string, (long Units, long Uses)>(StringComparer.Ordinal);
            foreach (var before in unmatched.Values)
            {
                Account(before, Catalog.Find(before.ItemId), 1);
                if (balance[before.ItemId].Units > int.MaxValue) return false;
            }
            foreach (var stack in packed)
            {
                if (!unmatched.TryGetValue(stack.InstanceId, out var before) || stack.ItemId != before.ItemId
                    || stack.Slot != before.Slot || stack.Area != before.Area
                    || stack.LoadedAmmo != before.LoadedAmmo || stack.LoadedAmmoId != before.LoadedAmmoId
                    || !stack.Parts.Equals(before.Parts) || !stack.Mods.Equals(before.Mods)
                    || stack.Wear != before.Wear || stack.UsesSpent != before.UsesSpent
                    || !ValidateStack(stack, out var definition, out _)
                    || (stack.Quantity != before.Quantity && !CanMergeStashStack(before, definition)))
                    return false;
                Account(stack, definition, -1);
                unmatched.Remove(stack.InstanceId);
            }
            foreach (var removed in unmatched.Values)
                if (!CanMergeStashStack(removed, Catalog.Find(removed.ItemId))) return false;
            foreach (var total in balance.Values)
                if (total.Units != 0 || total.Uses != 0) return false;
            return true;

            void Account(ItemStack stack, ItemDefinition definition, int direction)
            {
                balance.TryGetValue(stack.ItemId, out var total);
                long uses = definition.Kind == ItemKind.Medicine ? (long)stack.Quantity * definition.Uses - stack.UsesSpent : 0L;
                balance[stack.ItemId] = (total.Units + (long)direction * stack.Quantity, total.Uses + direction * uses);
            }
        }
    }
}
