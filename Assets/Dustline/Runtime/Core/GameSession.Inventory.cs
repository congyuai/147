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
                var weapon = Catalog.Find(gun?.ItemId);
                if (weapon?.Weapon == null || weapon.Weapon.Melee || WeaponRules.Socket(weapon.Weapon, slot) == null)
                { why = "当前没有可改装的枪械。"; return false; }
                var incoming = partBag.Find(partId);
                if (partId != null && (incoming == null || !WeaponRules.CanMount(weapon, slot, Catalog.Find(incoming.ItemId), out _)))
                {
                    if (incoming == null) why = "未找到配件。";
                    else WeaponRules.CanMount(weapon, slot, Catalog.Find(incoming.ItemId), out why);
                    return false;
                }
                var previous = gun.Parts.Get(slot);
                if (partId == null && previous.Empty) { why = "此配件槽已是原装。"; return false; }
                if (incoming != null && !partBag.Remove(incoming.InstanceId)) { why = "未能取出配件。"; return false; }
                gun.Parts.Set(slot, incoming != null ? new PartLink { InstanceId = incoming.InstanceId, ItemId = incoming.ItemId } : default);
                if (!previous.Empty && !partBag.TryInsert(new ItemStack { InstanceId = previous.InstanceId, ItemId = previous.ItemId }, out why)) return false;
                int capacity = WeaponRules.Capacity(weapon.Weapon, gun, Catalog);
                if (gun.LoadedAmmo > capacity)
                {
                    int excess = gun.LoadedAmmo - capacity;
                    if (!partBag.TryAdd(WeaponRules.LoadedType(weapon.Weapon, gun), excess, out why)) return false;
                    gun.LoadedAmmo = capacity;
                }
                try
                {
                    gunBag.Restore(gunBag.Snapshot());
                    if (!ReferenceEquals(gunBag, partBag)) partBag.Restore(partBag.Snapshot());
                }
                catch (InvalidDataException e) { why = e.Message; return false; }
                why = null;
                return true;
            }, out error);
        }
    }
}
