using System;
using System.Collections.Generic;
using System.IO;

namespace Dustline
{
    public sealed class GridInventory
    {
        private readonly List<ItemStack> items = new List<ItemStack>();
        private readonly IReadOnlyList<ItemStack> view;
        private readonly Predicate<ItemDefinition> accepts;
        readonly int baseWidth, baseHeight;
        readonly float baseWeight;
        readonly bool adaptive;

        public static GridInventory Carried(ItemCatalog catalog) => new GridInventory(6, 6, 30, catalog, null, true);

        public GridInventory(int width, int height, float maxWeight, ItemCatalog catalog) : this(width, height, maxWeight, catalog, null) { }

        internal GridInventory(int width, int height, float maxWeight, ItemCatalog catalog, Predicate<ItemDefinition> accepts, bool adaptive = false,
            bool rebuildCatalog = true)
        {
            if (width < 1)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height < 1)
                throw new ArgumentOutOfRangeException(nameof(height));
            if (float.IsNaN(maxWeight) || float.IsInfinity(maxWeight) || maxWeight < 0f)
                throw new ArgumentOutOfRangeException(nameof(maxWeight));
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

            if (rebuildCatalog) catalog.RebuildIndex();
            baseWidth = width;
            baseHeight = height;
            baseWeight = maxWeight;
            this.adaptive = adaptive;
            Catalog = catalog;
            this.accepts = accepts;
            view = items.AsReadOnly();
        }

        public int Width => Columns(CargoArea.Main);
        public int Height => Rows(CargoArea.Main);
        public float MaxWeight => Limit(items);
        public int Columns(CargoArea area) => Dimensions(area, items).Columns;
        public int Rows(CargoArea area) => Dimensions(area, items).Rows;
        public int Split(CargoArea area) => Dimensions(area, items).Split;
        public float CargoWeight(CargoArea area) => (float)AreaWeight(area, items);
        public float CargoLimit(CargoArea area) => AreaLimit(Dimensions(area, items));

        float AreaLimit(StorageSpec spec) => spec.Columns <= 0 ? 0f : spec.WeightLimit > 0f ? spec.WeightLimit : baseWeight;

        double AreaWeight(CargoArea area, List<ItemStack> load)
        {
            double weight = 0d;
            foreach (var stack in load)
                if (stack.Slot == EquipmentSlot.None && stack.Area == area) weight += StackWeight(stack);
            return weight;
        }

        StorageSpec Dimensions(CargoArea area, List<ItemStack> load)
        {
            if (area != CargoArea.Main && area != CargoArea.Rig) return default;
            if (adaptive)
            {
                var gear = Gear(area == CargoArea.Main ? EquipmentSlot.Pack : EquipmentSlot.Rig, load);
                if (gear != null && gear.Storage.Columns > 0) return gear.Storage;
            }
            return area == CargoArea.Main ? new StorageSpec { Columns = baseWidth, Rows = baseHeight } : default;
        }

        ItemDefinition Gear(EquipmentSlot slot, List<ItemStack> load)
        {
            foreach (var item in load)
                if (item.Slot == slot) return Catalog.Find(item.ItemId);
            return null;
        }

        float Limit(List<ItemStack> load) => !adaptive ? baseWeight : baseWeight
            + (Gear(EquipmentSlot.Pack, load)?.Storage.CarryBonus ?? 0f)
            + (Gear(EquipmentSlot.Rig, load)?.Storage.CarryBonus ?? 0f);
        public float Weight => (float)CalculateWeight(items);
        public ItemCatalog Catalog { get; }
        public IReadOnlyList<ItemStack> Items => view;
        public event Action Changed;
        internal int Revision { get; private set; }

        public bool TryAdd(string itemId, int quantity, out string error)
        {
            error = null;
            if (quantity < 1)
            {
                error = "物品数量必须大于零。";
                return false;
            }

            ItemDefinition definition = Catalog.Find(itemId);
            if (definition == null)
            {
                error = "未知物品 ID：" + itemId;
                return false;
            }
            if (!CanStore(definition, out error)) return false;
            if (!CanCarry(CalculateWeight(items) + (double)definition.UnitWeight * quantity))
            {
                error = "已超出负重上限。";
                return false;
            }
            if ((long)Count(itemId) + quantity > int.MaxValue)
            {
                error = "已超出物品数量上限。";
                return false;
            }

            List<ItemStack> staged = Snapshot();
            int remaining = quantity;
            int limit = StackLimit(definition);
            foreach (ItemStack stack in staged)
            {
                if (stack.Slot != EquipmentSlot.None || stack.ItemId != itemId)
                    continue;

                int added = FitQuantity(staged, stack.Area, definition, Math.Min(remaining, limit - stack.Quantity));
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
                if (!TryPlace(stack, definition, staged) && !TryPlacePartial(stack, definition, staged, amount))
                {
                    error = adaptive ? "格子或分区承重不足。" : "背包空间不足。";
                    return false;
                }
                staged.Add(stack);
                remaining -= stack.Quantity;
            }

            Commit(staged);
            Touch();
            return true;
        }

        int FitQuantity(List<ItemStack> current, CargoArea area, ItemDefinition definition, int requested)
        {
            if (requested <= 0 || definition.UnitWeight == 0f) return requested;
            double limit = AreaLimit(Dimensions(area, current));
            double used = AreaWeight(area, current);
            if ((float)(used + (double)requested * definition.UnitWeight) <= limit) return requested;
            return (int)Math.Max(0, Math.Floor((limit - used) / definition.UnitWeight));
        }

        bool TryPlacePartial(ItemStack stack, ItemDefinition definition, List<ItemStack> occupied, int amount)
        {
            if (!adaptive || amount <= 1) return false;
            bool orientation = stack.Rotated;
            for (int i = 0; i < 2; i++)
            {
                var area = (CargoArea)i;
                stack.Area = area;
                stack.Rotated = orientation;
                stack.Quantity = FitQuantity(occupied, area, definition, amount);
                if (stack.Quantity > 0 && TryPlace(stack, definition, occupied, onlyArea: true)) return true;
            }
            return false;
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
            => TryMove(instanceId, Find(instanceId)?.Area ?? CargoArea.Main, x, y, rotated, out error);

        public bool TryMove(string instanceId, CargoArea area, int x, int y, bool rotated, out string error)
        {
            error = null;
            ItemStack stack = Find(instanceId);
            if (stack == null)
            {
                error = "未找到该物品。";
                return false;
            }
            if (stack.Slot != EquipmentSlot.None)
            {
                error = "请先卸下该物品，再移动。";
                return false;
            }

            ItemStack moved = stack.Clone();
            moved.Area = area;
            moved.X = x;
            moved.Y = y;
            moved.Rotated = rotated;
            if (!Fits(moved, Catalog.Find(stack.ItemId), items, stack.InstanceId))
            {
                error = "该位置已被占用或超出背包范围。";
                return false;
            }

            stack.Area = area;
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
                error = "未找到该物品。";
                return false;
            }
            if (!MatchesSlot(Catalog.Find(stack.ItemId), slot))
            {
                error = "该物品不适用于此装备栏位。";
                return false;
            }
            if (stack.Slot != EquipmentSlot.None || Equipped(slot) != null)
            {
                error = "请先卸下已装备的物品。";
                return false;
            }

            if (adaptive && CapacitySlot(slot))
            {
                var next = Snapshot();
                var staged = next.Find(item => item.InstanceId == instanceId);
                SetEquipped(staged, slot);
                if (!Repack(next, out error)) return false;
                Commit(next);
            }
            else SetEquipped(stack, slot);
            Touch();
            return true;
        }

        static void SetEquipped(ItemStack stack, EquipmentSlot slot)
        {
            stack.Slot = slot;
            stack.Area = CargoArea.Main;
            stack.X = stack.Y = 0;
        }

        static bool CapacitySlot(EquipmentSlot slot) => slot == EquipmentSlot.Pack || slot == EquipmentSlot.Rig;

        internal bool TryWearFrom(GridInventory source, string id, EquipmentSlot slot, out string error)
        {
            error = null;
            var input = source.Find(id);
            if (input == null || input.Slot != EquipmentSlot.None || !ValidateStack(input, out var definition, out error)
                || !MatchesSlot(definition, slot))
            { error ??= "该物品不适用于此装备栏位。"; return false; }
            bool same = ReferenceEquals(source, this);
            if (!same && !CanInsertIdentity(input)) { error = "物品或配件实例重复。"; return false; }
            var next = Snapshot();
            var old = next.Find(item => item.Slot == slot);
            if (old != null)
            {
                old.Slot = EquipmentSlot.None;
                old.X = -1;
            }
            var incoming = same ? next.Find(item => item.InstanceId == id) : input.Clone();
            SetEquipped(incoming, slot);
            if (!same) next.Add(incoming);
            if (!Repack(next, out error)) return false;
            if (!same && !source.RemoveStage(input, out error)) return false;
            Commit(next);
            if (!same) source.Touch();
            Touch();
            return true;
        }

        public bool TryUnequip(EquipmentSlot slot, out string error)
        {
            error = null;
            ItemStack stack = Equipped(slot);
            if (stack == null)
            {
                error = "装备栏位为空或无效。";
                return false;
            }

            if (adaptive && CapacitySlot(slot))
            {
                var next = Snapshot();
                var staged = next.Find(item => item.InstanceId == stack.InstanceId);
                staged.Slot = EquipmentSlot.None;
                if (!Repack(next, out error)) return false;
                Commit(next);
                Touch();
                return true;
            }
            ItemStack moved = stack.Clone();
            moved.Slot = EquipmentSlot.None;
            if (!TryPlace(moved, Catalog.Find(stack.ItemId), items))
            {
                error = "背包空间不足。";
                return false;
            }

            stack.Area = moved.Area;
            stack.X = moved.X;
            stack.Y = moved.Y;
            stack.Rotated = moved.Rotated;
            stack.Slot = EquipmentSlot.None;
            Touch();
            return true;
        }

        internal bool TryUnequipAt(EquipmentSlot slot, CargoArea area, int x, int y, bool rotated, out string error)
        {
            var next = Snapshot();
            var gear = next.Find(item => item.Slot == slot && slot != EquipmentSlot.None);
            if (gear == null) { error = "未装备该物品。"; return false; }
            gear.Slot = EquipmentSlot.None;
            gear.Area = area;
            gear.X = x; gear.Y = y; gear.Rotated = rotated;
            var def = Catalog.Find(gear.ItemId);
            if (!Fits(gear, def, next, gear.InstanceId, checkCargoWeight: false)
                || (float)StackWeight(gear) > AreaLimit(Dimensions(area, next)))
            { error = "无法放入目标格，请检查容量和负重。"; return false; }
            if (!Repack(next, out error, gear)) return false;
            Commit(next);
            Touch();
            return true;
        }

        public bool TryTransferTo(string instanceId, GridInventory destination, out string error)
            => Transfer(instanceId, destination, null, 0, 0, false, out error);

        public bool TryTransferAt(string instanceId, GridInventory destination, CargoArea area, int x, int y, bool rotated, out string error)
            => Transfer(instanceId, destination, area, x, y, rotated, out error);

        bool Transfer(string instanceId, GridInventory destination, CargoArea? area, int x, int y, bool rotated, out string error)
        {
            error = null;
            if (destination == null || ReferenceEquals(this, destination))
            {
                error = "请选择另一个物品栏作为转移目标。";
                return false;
            }

            ItemStack stack = Find(instanceId);
            if (stack == null)
            {
                error = "未找到该物品。";
                return false;
            }
            if (!destination.TryPrepareInsert(stack, out ItemStack inserted, out error, area, x, y, rotated))
                return false;
            if (!RemoveStage(stack, out error)) return false;
            destination.items.Add(inserted);
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

            if (!RemoveStage(stack, out _)) return false;
            Touch();
            return true;
        }

        bool RemoveStage(ItemStack stack, out string error)
        {
            error = null;
            if (adaptive && CapacitySlot(stack.Slot))
            {
                var next = Snapshot();
                next.RemoveAll(item => item.InstanceId == stack.InstanceId);
                if (!Repack(next, out error)) return false;
                Commit(next);
            }
            else items.Remove(stack);
            return true;
        }

        bool Repack(List<ItemStack> next, out string error, ItemStack pinned = null)
        {
            if ((float)CalculateWeight(next) > Limit(next))
            { error = "换装后负重超限，请先转移物资。"; return false; }
            var placed = next.FindAll(item => item.Slot != EquipmentSlot.None);
            if (pinned != null) placed.Add(pinned);
            var displaced = new List<ItemStack>();
            foreach (var item in next)
            {
                if (item.Slot != EquipmentSlot.None || item == pinned) continue;
                if (Fits(item, Catalog.Find(item.ItemId), placed, null)) placed.Add(item);
                else displaced.Add(item);
            }
            foreach (var item in displaced)
            {
                if (!TryPlace(item, Catalog.Find(item.ItemId), placed))
                { error = "换装后格子不足，请先转移物资。"; return false; }
                placed.Add(item);
            }
            error = null;
            return true;
        }

        internal GridInventory Draft()
        {
            var draft = new GridInventory(baseWidth, baseHeight, baseWeight, Catalog, accepts, adaptive, rebuildCatalog: false);
            draft.items.AddRange(Snapshot());
            return draft;
        }

        internal void ApplyDraft(GridInventory draft) => Commit(draft.Snapshot());

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

        public int RemainingUses(string itemId)
        {
            long count = 0;
            var item = Catalog.Find(itemId);
            if (item?.Kind != ItemKind.Medicine) return 0;
            foreach (var stack in items)
                if (stack.ItemId == itemId) count += (long)stack.Quantity * item.Uses - stack.UsesSpent;
            return (int)Math.Min(count, int.MaxValue);
        }

        internal bool ConsumeInstance(string instanceId)
        {
            var stack = Find(instanceId);
            if (stack == null || stack.Slot != EquipmentSlot.None || stack.Quantity <= 0) return false;
            var item = Catalog.Find(stack.ItemId);
            if (item.Kind == ItemKind.Medicine && stack.UsesSpent + 1 < Math.Max(1, item.Uses)) stack.UsesSpent++;
            else
            {
                stack.UsesSpent = 0;
                if (--stack.Quantity == 0) items.Remove(stack);
            }
            Touch();
            return true;
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
            if (slot == EquipmentSlot.None) return null;

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
                    throw new InvalidDataException("物品实例 ID 重复：" + input.InstanceId);
                foreach (PartSlot part in (PartSlot[])Enum.GetValues(typeof(PartSlot)))
                {
                    var link = input.Parts.Get(part);
                    if (!link.Empty && !ids.Add(link.InstanceId)) throw new InvalidDataException("配件实例 ID 重复。");
                }
                if (input.X < 0 || input.Y < 0 || !Enum.IsDefined(typeof(CargoArea), input.Area))
                    throw new InvalidDataException("物品位置坐标无效。");

                ItemStack stack = input.Clone();
                if (stack.Slot != EquipmentSlot.None && (stack.Area != CargoArea.Main || !MatchesSlot(definition, stack.Slot) || !slots.Add(stack.Slot)))
                {
                    throw new InvalidDataException("装备栏位无效或被重复占用。");
                }

                if (stack.Slot != EquipmentSlot.None) SetEquipped(stack, stack.Slot);
                counts.TryGetValue(stack.ItemId, out long count);
                count += stack.Quantity;
                if (count > int.MaxValue)
                    throw new InvalidDataException("已超出物品数量上限。");
                counts[stack.ItemId] = count;
                restored.Add(stack);
            }

            foreach (var stack in restored)
            {
                var area = Dimensions(stack.Area, restored);
                if (stack.X >= area.Columns || stack.Y >= area.Rows || stack.Slot == EquipmentSlot.None
                    && !Fits(stack, Catalog.Find(stack.ItemId), restored, stack.InstanceId))
                    throw new InvalidDataException("物品相互重叠或超出背包范围。");
            }
            if ((float)CalculateWeight(restored) > Limit(restored))
                throw new InvalidDataException("已超出负重上限。");

            Commit(restored);
            Touch();
        }

        public void Touch()
        {
            unchecked { Revision++; }
            Changed?.Invoke();
        }

        private bool TryPrepareInsert(ItemStack input, out ItemStack inserted, out string error, CargoArea? area = null, int x = 0, int y = 0, bool rotated = false)
        {
            inserted = null;
            if (!ValidateStack(input, out ItemDefinition definition, out error))
                return false;
            if (!CanInsertIdentity(input))
            {
                error = "物品或配件实例 ID 重复。";
                return false;
            }
            if ((long)Count(input.ItemId) + input.Quantity > int.MaxValue)
            {
                error = "已超出物品数量上限。";
                return false;
            }
            if (!CanCarry(CalculateWeight(items) + StackWeight(input)))
            {
                error = "已超出负重上限。";
                return false;
            }

            ItemStack candidate = input.Clone();
            candidate.Slot = EquipmentSlot.None;
            if (area.HasValue)
            {
                candidate.Area = area.Value;
                candidate.X = x; candidate.Y = y; candidate.Rotated = rotated;
            }
            if (area.HasValue ? !Fits(candidate, definition, items, null) : !TryPlace(candidate, definition, items))
            {
                error = "背包空间不足。";
                return false;
            }

            inserted = candidate;
            return true;
        }

        bool CanInsertIdentity(ItemStack candidate)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var existing in items)
            {
                ids.Add(existing.InstanceId);
                for (int i = 0; i <= (int)PartSlot.Magazine; i++)
                {
                    var link = existing.Parts.Get((PartSlot)i);
                    if (!link.Empty) ids.Add(link.InstanceId);
                }
            }
            if (!ids.Add(candidate.InstanceId)) return false;
            for (int i = 0; i <= (int)PartSlot.Magazine; i++)
            {
                var link = candidate.Parts.Get((PartSlot)i);
                if (!link.Empty && !ids.Add(link.InstanceId)) return false;
            }
            return true;
        }

        internal bool ConsumeStack(string instanceId, int quantity)
        {
            var stack = Find(instanceId);
            if (stack == null || stack.Slot != EquipmentSlot.None || quantity < 1 || stack.Quantity < quantity) return false;
            stack.Quantity -= quantity;
            if (stack.Quantity == 0) items.Remove(stack);
            Touch();
            return true;
        }

        private bool ValidateStack(ItemStack stack, out ItemDefinition definition, out string error)
        {
            definition = null;
            error = null;
            if (stack == null || string.IsNullOrWhiteSpace(stack.InstanceId)
                || string.IsNullOrWhiteSpace(stack.ItemId))
            {
                error = "物品的实例 ID 和定义 ID 不能为空。";
                return false;
            }

            definition = Catalog.Find(stack.ItemId);
            if (definition == null)
            {
                error = "未知物品 ID：" + stack.ItemId;
                return false;
            }
            if (!CanStore(definition, out error)) return false;
            if (stack.Quantity < 1 || stack.Quantity > StackLimit(definition))
            {
                error = "物品堆叠数量无效。";
                return false;
            }
            if (stack.LoadedAmmo < -1
                || ((definition.Kind != ItemKind.Weapon || definition.Weapon != null && definition.Weapon.Melee) && stack.LoadedAmmo != -1)
                || (definition.Kind == ItemKind.Weapon && definition.Weapon != null
                    && stack.LoadedAmmo > WeaponRules.Capacity(definition.Weapon, stack, Catalog)))
            {
                error = "弹匣内的弹药数量无效。";
                return false;
            }
            if ((!string.IsNullOrEmpty(stack.LoadedAmmoId) && !WeaponRules.CanLoad(definition.Weapon, Catalog.Find(stack.LoadedAmmoId), out error))
                || !WeaponRules.VerifyLinks(stack, definition, Catalog, out error)) return false;
            if (stack.UsesSpent < 0 || stack.UsesSpent >= Math.Max(1, definition.Uses)
                || (definition.Kind != ItemKind.Medicine && stack.UsesSpent != 0) || stack.Wear < 0 || stack.Wear > definition.Durability)
            { error = "物品剩余次数或耐久无效。"; return false; }
            if (!WeaponTuning.IsValidForItem(stack.ItemId, stack.Mods)
                || (!stack.Mods.IsEmpty && (definition.Kind != ItemKind.Weapon || !WeaponTuning.SupportsUpgrades(definition.Weapon))))
            {
                error = "武器改装等级无效。";
                return false;
            }
            return true;
        }

        private bool CanStore(ItemDefinition definition, out string error)
        {
            error = accepts != null && !accepts(definition) ? "不适合放入此容器。" : null;
            return error == null;
        }

        private bool TryPlace(ItemStack stack, ItemDefinition definition, List<ItemStack> occupied, bool onlyArea = false)
        {
            bool preferred = stack.Rotated;
            var first = adaptive ? stack.Area : CargoArea.Main;
            for (int area = 0; area < (adaptive && !onlyArea ? 2 : 1); area++)
            {
                stack.Area = area == 0 ? first : first == CargoArea.Main ? CargoArea.Rig : CargoArea.Main;
                var shape = Dimensions(stack.Area, occupied);
                for (int orientation = 0; orientation < 2; orientation++)
                {
                    stack.Rotated = orientation == 0 ? preferred : !preferred;
                    int w = stack.Rotated ? definition.Height : definition.Width;
                    int h = stack.Rotated ? definition.Width : definition.Height;
                    for (int y = 0; y <= shape.Rows - h; y++)
                        for (int x = 0; x <= shape.Columns - w; x++)
                        {
                            stack.X = x; stack.Y = y;
                            if (Fits(stack, definition, occupied, null)) return true;
                        }
                    if (definition.Width == definition.Height) break;
                }
            }
            return false;
        }

        private bool Fits(ItemStack candidate, ItemDefinition definition, List<ItemStack> occupied,
            string ignoredInstanceId, bool checkCargoWeight = true)
        {
            int width = candidate.Rotated ? definition.Height : definition.Width;
            int height = candidate.Rotated ? definition.Width : definition.Height;
            if (candidate.Area != CargoArea.Main && candidate.Area != CargoArea.Rig) return false;
            var shape = Dimensions(candidate.Area, occupied);
            if (candidate.X < 0 || candidate.Y < 0 || candidate.X > shape.Columns - width
                || candidate.Y > shape.Rows - height || (shape.Split > 0 && candidate.X < shape.Split && candidate.X + width > shape.Split))
                return false;

            double mass = checkCargoWeight ? StackWeight(candidate) : 0d;
            foreach (ItemStack other in occupied)
            {
                if (other.Slot != EquipmentSlot.None || other.Area != candidate.Area || other.InstanceId == ignoredInstanceId)
                    continue;

                if (checkCargoWeight) mass += StackWeight(other);
                ItemDefinition otherDefinition = Catalog.Find(other.ItemId);
                int otherWidth = other.Rotated ? otherDefinition.Height : otherDefinition.Width;
                int otherHeight = other.Rotated ? otherDefinition.Width : otherDefinition.Height;
                if (candidate.X < (long)other.X + otherWidth && (long)candidate.X + width > other.X
                    && candidate.Y < (long)other.Y + otherHeight && (long)candidate.Y + height > other.Y)
                    return false;
            }
            return !checkCargoWeight || (float)mass <= AreaLimit(shape);
        }

        private double CalculateWeight(List<ItemStack> stacks)
        {
            double weight = 0d;
            foreach (ItemStack stack in stacks)
                weight += StackWeight(stack);
            return weight;
        }

        double StackWeight(ItemStack stack)
        {
            var definition = Catalog.Find(stack.ItemId);
            double weight = (double)definition.UnitWeight * stack.Quantity;
            if (!stack.Parts.Empty)
                for (int i = 0; i <= (int)PartSlot.Magazine; i++)
                    weight += Catalog.Find(stack.Parts.Get((PartSlot)i).ItemId)?.UnitWeight ?? 0f;
            return weight;
        }

        private bool CanCarry(double weight)
        {
            return (float)weight <= MaxWeight;
        }

        private static int StackLimit(ItemDefinition definition)
        {
            return definition.Equipment != EquipmentSlot.None || definition.Kind == ItemKind.Part ? 1 : definition.MaxStack;
        }

        private static bool MatchesSlot(ItemDefinition definition, EquipmentSlot slot)
        {
            return slot != EquipmentSlot.None && definition.Equipment == slot;
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
                current.Area = update.Area;
                current.LoadedAmmo = update.LoadedAmmo;
                current.LoadedAmmoId = update.LoadedAmmoId;
                current.Mods = update.Mods;
                current.Parts = update.Parts;
                current.UsesSpent = update.UsesSpent;
                current.Wear = update.Wear;
                replacement[i] = current;
            }

            items.Clear();
            items.AddRange(replacement);
        }
    }
}
