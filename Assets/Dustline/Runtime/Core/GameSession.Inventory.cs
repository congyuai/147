using System;
using System.Collections.Generic;
using System.IO;

namespace Dustline
{
    public sealed partial class GameSession
    {
        void UpgradeInventory(ProfileData data)
        {
            FillLegacyAmmunition(data.Stash);
            FillLegacyAmmunition(data.Loadout);
            data.Version = 2;
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
            var next = new ProfileData { Version = 2, Credits = credits, Stash = stash.Snapshot(), Loadout = carried.Snapshot(),
                BoundBladeId = Profile.BoundBladeId, Raids = Profile.Raids, Extractions = Profile.Extractions, RaidInProgress = Profile.RaidInProgress };
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
                var stash = Stash.Draft();
                var carried = Loadout.Draft();
                if (!edit(source == Stash ? stash : carried, destination == Stash ? stash : carried, out error)) return false;
                return SaveMenuInventory(stash, carried, Profile.Credits);
            }
            if (Phase != SessionPhase.Raid)
            { error = "当前物品栏不可操作。"; return false; }
            if (!edit(source, destination, out error)) return false;
            Player?.Weapon.SyncEquipment();
            return true;
        }

        internal bool TryLoadAmmunition(GridInventory source, string id, out string error)
        {
            if (Phase != SessionPhase.Menu) { error = "请在整备时装填。"; return false; }
            return EditInventory(source, Loadout, (GridInventory ammoBag, GridInventory carried, out string why) =>
            {
                var stack = ammoBag.Find(id);
                var gun = carried.Equipped(EquipmentSlot.Weapon);
                var weapon = Catalog.Find(gun?.ItemId)?.Weapon;
                if (!WeaponRules.CanLoad(weapon, gun, Catalog.Find(stack?.ItemId), out why)) return false;
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
