using System;
using System.Collections.Generic;
using System.IO;

namespace Dustline
{
    public sealed partial class GameSession
    {
        void UpgradeInventory(ProfileData data)
        {
            if (data.Version == 1)
            {
                FillLegacyAmmunition(data.Stash);
                FillLegacyAmmunition(data.Loadout);
            }
            if (data.Version < 3)
            {
                var carried = GridInventory.Carried(Catalog);
                carried.MigrateCarried(data.Loadout);
                data.Loadout = carried.Snapshot();
            }
            data.Version = ProfileData.CurrentVersion;
        }

        void FillLegacyAmmunition(List<ItemStack> items)
        {
            foreach (var item in items)
            {
                if (item.LoadedAmmo > 0 && string.IsNullOrEmpty(item.LoadedAmmoId))
                    item.LoadedAmmoId = Catalog.Find(item.ItemId)?.Weapon?.AmmoId ?? "";
                if (item.Slot != EquipmentSlot.None) { item.Area = CargoArea.Main; item.X = item.Y = 0; }
            }
        }

        bool SaveMenuInventory(GridInventory stash, GridInventory carried, int credits)
        {
            var next = Profile.WithInventory(stash, carried, credits);
            next.Version = ProfileData.CurrentVersion;
            if (!next.BirthdayGiftClaimed && Catalog.Find(Dustline.LootTable.BirthdayCake) != null)
            {
                var gift = ReferenceEquals(stash, Stash) ? stash.Draft() : stash;
                if (gift.TryAdd(Dustline.LootTable.BirthdayCake, 1, out _))
                {
                    stash = gift;
                    next.Stash = stash.Snapshot();
                    next.BirthdayGiftClaimed = true;
                }
            }
            try { store.Save(next); }
            catch (Exception error) { Fail(error); return false; }
            bool stashChanged = !ReferenceEquals(Stash, stash), carriedChanged = !ReferenceEquals(Loadout, carried);
            if (stashChanged) Stash.ApplyDraft(stash);
            if (carriedChanged) Loadout.ApplyDraft(carried);
            Profile = next;
            if (stashChanged) Stash.Touch();
            if (carriedChanged) Loadout.Touch();
            return true;
        }

        internal delegate bool InventoryEdit(GridInventory source, GridInventory destination, out string error);

        internal bool EditInventory(GridInventory source, GridInventory destination, InventoryEdit edit, out string error)
        {
            if (Phase == SessionPhase.Menu)
            {
                if ((source != Stash && source != Loadout) || (destination != Stash && destination != Loadout))
                { error = "当前物品栏不可操作。"; return false; }
                var stash = source == Stash || destination == Stash ? Stash.Draft() : Stash;
                var carried = source == Loadout || destination == Loadout ? Loadout.Draft() : Loadout;
                if (!edit(source == Stash ? stash : carried, destination == Stash ? stash : carried, out error)) return false;
                return SaveMenuInventory(stash, carried, Profile.Credits);
            }
            if (Phase != SessionPhase.Raid || source == Stash || destination == Stash)
            { error = "当前物品栏不可操作。"; return false; }
            var from = source.Draft();
            var to = source == destination ? from : destination.Draft();
            if (!edit(from, to, out error)) return false;
            var field = source == Loadout ? from : destination == Loadout ? to : null;
            if (field != null && !SaveProtectedCargo(field, out error)) return false;
            source.ApplyDraft(from);
            if (source != destination) destination.ApplyDraft(to);
            source.Touch();
            if (source != destination) destination.Touch();
            Player?.Weapon.SyncEquipment();
            return true;
        }

        bool SaveProtectedCargo(GridInventory bag, out string error)
        {
            error = null;
            var retained = RetainedCargo(Profile.Loadout, bag);
            if (SameRetainedCargo(Profile.Loadout, retained)) return true;
            var next = Profile.WithInventory(Stash, bag, Profile.Credits);
            next.Loadout = retained;
            try { store.Save(next); }
            catch (Exception ex) { Fail(ex); error = "安全箱未能保存。"; return false; }
            Profile = next;
            return true;
        }

        internal static List<ItemStack> RetainedCargo(List<ItemStack> home, GridInventory carried)
        {
            var retained = home.FindAll(item => item.Area == CargoArea.Legacy);
            retained.AddRange(carried.Snapshot().FindAll(item => item.Area == CargoArea.Secure));
            return retained;
        }

        GridInventory StageReturn(bool extracted, GridInventory stash)
        {
            var carried = extracted ? Loadout.Draft() : GridInventory.Carried(Catalog);
            if (extracted)
            {
                var recovered = carried.Snapshot();
                foreach (var item in recovered)
                    if (item.Area != CargoArea.Secure && item.Slot != EquipmentSlot.Rig && item.Slot != EquipmentSlot.Pack)
                        carried.TryTransferTo(item.InstanceId, stash, out _);
                foreach (var item in recovered)
                    if (item.Slot == EquipmentSlot.Rig || item.Slot == EquipmentSlot.Pack)
                        carried.TryTransferTo(item.InstanceId, stash, out _);
            }
            var returned = extracted ? carried.Snapshot() : RetainedCargo(Profile.Loadout, Loadout);
            if (extracted) returned.AddRange(Profile.Loadout.FindAll(item => item.Area == CargoArea.Legacy));
            carried.Restore(returned);
            return carried;
        }

        static bool SameRetainedCargo(List<ItemStack> a, List<ItemStack> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                var x = a[i]; var y = b[i];
                if (x.InstanceId != y.InstanceId || x.ItemId != y.ItemId || x.Area != y.Area || x.X != y.X || x.Y != y.Y
                    || x.Rotated != y.Rotated || x.Quantity != y.Quantity || x.Wear != y.Wear || x.UsesSpent != y.UsesSpent
                    || x.LoadedAmmo != y.LoadedAmmo || x.LoadedAmmoId != y.LoadedAmmoId || !x.Parts.Equals(y.Parts) || !x.Mods.Equals(y.Mods)) return false;
            }
            return true;
        }

        internal bool SortStash(out string error)
        {
            if (Phase != SessionPhase.Menu) { error = "只能在整备时整理仓库。"; return false; }
            var stash = Stash.Draft();
            return stash.TrySortStash(out error) && SaveMenuInventory(stash, Loadout, Profile.Credits);
        }

        internal bool TryPrepareSlot(EquipmentSlot slot, string id, bool buy, int quote, out string error)
        {
            error = "当前无法调整配装。";
            if (Phase != SessionPhase.Menu) return false;
            var stash = Stash.Draft();
            var bag = Loadout.Draft();
            var source = stash;
            int cost = 0;
            if (buy)
            {
                var definition = Catalog.Find(id);
                if (definition == null || definition.NotForSale || definition.Value != quote)
                { error = "商品或价格已变化。"; return false; }
                cost = definition.Value;
                if (Profile.Credits < cost) { error = "信用点不足。"; return false; }
                source = new GridInventory(12, 10, 100, Catalog);
                var newItem = new ItemStack { ItemId = id, InstanceId = Guid.NewGuid().ToString("N") };
                if (!source.TryInsert(newItem, out error)) return false;
                id = newItem.InstanceId;
            }
            if (!bag.TryWearFrom(source, id, slot, out error, stash)) return false;
            return SaveMenuInventory(stash, bag, Profile.Credits - cost);
        }

        internal bool TryPrepareSupply(string id, int quantity, bool buy, int quote, out string error)
        {
            error = "当前无法补充物资。";
            if (Phase != SessionPhase.Menu || quantity < 1) return false;
            if (!buy)
                return EditInventory(Stash, Loadout, (GridInventory a, GridInventory b, out string reason) =>
                {
                    var stack = a.Find(id);
                    var definition = Catalog.Find(stack?.ItemId);
                    if (stack?.Quantity != quantity || (definition?.Kind != ItemKind.Ammo && definition?.Kind != ItemKind.Medicine))
                    { reason = "仓库物资已变化。"; return false; }
                    return a.TryTransferTo(id, b, out reason);
                }, out error);
            var item = Catalog.Find(id);
            if (item == null || item.NotForSale || (item.Kind != ItemKind.Ammo && item.Kind != ItemKind.Medicine) || item.Value != quote)
            { error = "商品或价格已变化。"; return false; }
            long cost = (long)item.Value * quantity;
            if (cost > Profile.Credits) { error = "信用点不足。"; return false; }
            var bag = Loadout.Draft();
            if (!bag.TryAdd(id, quantity, out error)) return false;
            return SaveMenuInventory(Stash, bag, Profile.Credits - (int)cost);
        }

        internal bool TryLoadAmmunition(GridInventory source, string id, EquipmentSlot slot, out string error)
        {
            if (Phase != SessionPhase.Menu || !LoadoutSlots.IsHand(slot) || slot == EquipmentSlot.Melee)
            { error = "请在整备时选择枪械装填。"; return false; }
            return EditInventory(source, Loadout, (GridInventory ammoBag, GridInventory carried, out string why) =>
            {
                var stack = ammoBag.Find(id);
                var gun = carried.Equipped(slot);
                var weapon = Catalog.Find(gun?.ItemId)?.Weapon;
                if (gun == null || stack == null || stack.Slot != EquipmentSlot.None)
                { why = "未找到选中的枪械或备弹。"; return false; }
                if (!WeaponRules.CanLoad(weapon, gun, Catalog.Find(stack.ItemId), out why)) return false;
                int loaded = Math.Max(0, gun.LoadedAmmo);
                int amount = Math.Min(stack.Quantity, WeaponRules.Capacity(weapon, gun, Catalog) - loaded);
                if (amount <= 0) { why = "弹匣已满。"; return false; }
                gun.LoadedAmmoId = stack.ItemId;
                gun.LoadedAmmo = loaded + amount;
                if (ammoBag.ConsumeStack(id, amount)) return true;
                why = "备弹数量不足。";
                return false;
            }, out error);
        }

        internal bool TryUnloadAmmunition(GridInventory source, string id, out string error)
        {
            if (Phase != SessionPhase.Menu) { error = "请在整备时卸弹。"; return false; }
            return EditInventory(source, Stash, (GridInventory guns, GridInventory stash, out string why) =>
            {
                var gun = guns.Find(id);
                var weapon = Catalog.Find(gun?.ItemId)?.Weapon;
                if (weapon == null || gun.LoadedAmmo <= 0) { why = "没有可卸下的弹药。"; return false; }
                if (!stash.TryAdd(WeaponRules.LoadedType(weapon, gun), gun.LoadedAmmo, out why)) return false;
                gun.LoadedAmmo = 0;
                gun.LoadedAmmoId = "";
                return true;
            }, out error);
        }

        bool PrepareSale(GridInventory source, string id, out GridInventory stash, out GridInventory carried, out string error)
        {
            stash = Stash.Draft();
            carried = source == Loadout ? Loadout.Draft() : Loadout;
            var from = source == Loadout ? carried : stash;
            var item = from.Find(id);
            error = null;
            if (!from.Remove(id)) { error = "请先转移该容器中的物资。"; return false; }
            for (int i = 0; i <= (int)PartSlot.Magazine; i++)
            {
                var part = item.Parts.Get((PartSlot)i);
                if (!part.Empty && !stash.TryInsert(new ItemStack { ItemId = part.ItemId, InstanceId = part.InstanceId }, out error)) return false;
            }
            if (item.LoadedAmmo > 0 && !stash.TryAdd(WeaponRules.LoadedType(Catalog.Find(item.ItemId).Weapon, item), item.LoadedAmmo, out error)) return false;
            return true;
        }

        internal bool TryFitPart(GridInventory guns, string gunId, PartSlot slot, GridInventory parts, string partId, out string error)
        {
            error = null;
            if (Phase != SessionPhase.Menu) return false;
            return EditInventory(guns, parts, (GridInventory gunBag, GridInventory partBag, out string why) =>
            {
                var gun = gunBag.Find(gunId);
                if (!CheckSocket(gun, slot, out var weapon, out why)) return false;
                var incoming = partBag.Find(partId);
                if (partId != null)
                {
                    if (incoming == null || incoming.Slot != EquipmentSlot.None) { why = "未找到配件。"; return false; }
                    if (!WeaponRules.CanMount(weapon, slot, Catalog.Find(incoming.ItemId), out why)) return false;
                    if (!partBag.Remove(incoming.InstanceId)) { why = "未能取出配件。"; return false; }
                }
                else if (gun.Parts.Get(slot).Empty) { why = "此配件槽已是原装。"; return false; }
                return StageFit(gunBag, gun, weapon, slot, incoming, partBag, out why);
            }, out error);
        }

        internal bool TryFitCatalogPart(GridInventory guns, string gunId, PartSlot slot, string partId, int quoted, out string error)
        {
            error = "请在整备时改装。";
            if (Phase != SessionPhase.Menu || (guns != Stash && guns != Loadout)) return false;
            var gun = guns.Find(gunId);
            if (!CheckSocket(gun, slot, out var weapon, out error)) return false;
            var part = Catalog.Find(partId);
            if (!WeaponRules.CanMount(weapon, slot, part, out error)) return false;
            if (gun.Parts.Get(slot).ItemId == partId) { error = "已安装此配件。"; return false; }
            var other = guns == Stash ? Loadout : Stash;
            bool owned = guns.Count(partId) > 0 || other.Count(partId) > 0;
            if (!owned && part.NotForSale) { error = "此配件不在系统商店出售。"; return false; }
            int cost = owned ? 0 : part.Value;
            if (cost != quoted) { error = "持有量或报价已变，请重新预览。"; return false; }
            if (cost > Profile.Credits) { error = "信用点不足。"; return false; }
            if (!owned)
                return Stage(Stash, null, out var boughtStash, out var boughtCarried, out error)
                    && SaveMenuInventory(boughtStash, boughtCarried, Profile.Credits - cost);

            error = "未找到可用配件。";
            foreach (var (bag, item) in AvailableParts())
                if (Stage(bag, item.InstanceId, out var stash, out var carried, out error))
                    return SaveMenuInventory(stash, carried, Profile.Credits);
            return false;

            IEnumerable<(GridInventory bag, ItemStack item)> AvailableParts()
            {
                if (gun.Slot == EquipmentSlot.None)
                {
                    // Taking the part from the gun's own pocket does not increase that pocket's load.
                    foreach (var item in guns.Items)
                        if (Loose(item) && item.Area == gun.Area) yield return (guns, item);
                }
                foreach (var item in guns.Items)
                    if (Loose(item) && (gun.Slot != EquipmentSlot.None || item.Area != gun.Area)) yield return (guns, item);
                foreach (var item in other.Items)
                    if (Loose(item)) yield return (other, item);
            }

            bool Loose(ItemStack item) => item.Slot == EquipmentSlot.None && item.ItemId == partId;

            bool Stage(GridInventory stock, string instance, out GridInventory nextStash, out GridInventory nextCarried, out string why)
            {
                nextStash = Stash.Draft();
                nextCarried = Loadout.Draft();
                var gunBag = guns == Stash ? nextStash : nextCarried;
                ItemStack incoming;
                if (instance == null) incoming = new ItemStack { ItemId = partId, InstanceId = Guid.NewGuid().ToString("N") };
                else
                {
                    var partBag = stock == Stash ? nextStash : nextCarried;
                    incoming = partBag.Find(instance);
                    if (!partBag.Remove(instance)) { why = "未能取出配件。"; return false; }
                }
                return StageFit(gunBag, gunBag.Find(gunId), weapon, slot, incoming, nextStash, out why);
            }
        }

        internal bool TryFitWorkshopOriginal(GridInventory guns, string gunId, PartSlot slot, int level, int quoted, out string error)
        {
            error = "请在整备时改装。";
            if (Phase != SessionPhase.Menu || (guns != Stash && guns != Loadout)) return false;
            var gun = guns.Find(gunId);
            if (!CheckSocket(gun, slot, out var weapon, out error)) return false;
            bool legacy = weapon.Id == WeaponDefinition.RifleId && (int)slot <= (int)PartSlot.Stock;
            WeaponMods mods = gun.Mods;
            int cost = 0;
            if ((level != 0 || legacy) && (!legacy || !WeaponTuning.TrySelect(gun.Mods, (WeaponModSlot)slot, level, out mods, out cost)))
            { error = "请先解锁前一级原厂改装。"; return false; }
            if (gun.Parts.Get(slot).Empty && (!legacy || level == WeaponTuning.Level(gun.Mods, (WeaponModSlot)slot)))
            { error = "已是此配置。"; return false; }
            if (cost != quoted) { error = "改装报价已变，请重新预览。"; return false; }
            if (cost > Profile.Credits) { error = "信用点不足。"; return false; }
            var stash = Stash.Draft();
            var carried = guns == Stash ? Loadout : Loadout.Draft();
            var gunBag = guns == Stash ? stash : carried;
            var staged = gunBag.Find(gunId);
            staged.Mods = mods;
            if (!StageFit(gunBag, staged, weapon, slot, null, stash, out error)) return false;
            return SaveMenuInventory(stash, carried, Profile.Credits - cost);
        }

        bool CheckSocket(ItemStack gun, PartSlot slot, out ItemDefinition weapon, out string error)
        {
            weapon = Catalog.Find(gun?.ItemId);
            error = WeaponRules.Socket(weapon?.Weapon, slot) == null ? "该枪型没有这个配件槽。" : null;
            return error == null;
        }

        bool StageFit(GridInventory gunBag, ItemStack gun, ItemDefinition weapon, PartSlot slot, ItemStack incoming, GridInventory returns, out string error)
        {
            var previous = gun.Parts.Get(slot);
            gun.Parts.Set(slot, incoming != null ? new PartLink { InstanceId = incoming.InstanceId, ItemId = incoming.ItemId } : default);
            if (!previous.Empty && !returns.TryInsert(new ItemStack { InstanceId = previous.InstanceId, ItemId = previous.ItemId }, out error)) return false;
            int capacity = WeaponRules.Capacity(weapon.Weapon, gun, Catalog);
            if (gun.LoadedAmmo > capacity)
            {
                int excess = gun.LoadedAmmo - capacity;
                gun.LoadedAmmo = capacity;
                if (!returns.TryAdd(WeaponRules.LoadedType(weapon.Weapon, gun), excess, out error)) return false;
            }
            try
            {
                gunBag.Restore(gunBag.Snapshot());
                if (!ReferenceEquals(gunBag, returns)) returns.Restore(returns.Snapshot());
            }
            catch (InvalidDataException e) { error = e.Message; return false; }
            error = null;
            return true;
        }
    }
}
