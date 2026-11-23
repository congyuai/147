using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace Dustline.Tests
{
    public sealed class LoadoutLayoutTests
    {
        static readonly MethodInfo wear = typeof(GridInventory).GetMethod("TryWearFrom", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly MethodInfo migrate = typeof(GridInventory).GetMethod("MigrateCarried", BindingFlags.Instance | BindingFlags.NonPublic);
        ItemCatalog catalog;

        [SetUp]
        public void LoadCatalog()
        {
            catalog = Resources.Load<ItemCatalog>("Dustline/Items");
            Assert.That(catalog, Is.Not.Null, "Run Dustline > Prepare Project first.");
        }

        [Test]
        public void PersonalPocketsHoldFiveSingleCellsButRejectSpanningItems()
        {
            var carried = GridInventory.Carried(catalog);
            var stash = NewStash();
            var layout = carried.Layout(CargoArea.Pockets);
            Assert.That((layout.Columns, layout.Rows, layout.Capacity), Is.EqualTo((5, 1, 5)));
            Assert.That(Enumerable.Range(0, 5).Select(x => layout.GroupAt(x, 0)), Is.EquivalentTo(Enumerable.Range(0, 5)));

            Assert.That(stash.TryInsert(new ItemStack { ItemId = "part_stock", InstanceId = "stock" }, out var error), Is.True, error);
            Assert.That(stash.TryTransferAt("stock", carried, CargoArea.Pockets, 0, 0, false, out _), Is.False);
            Assert.That(carried.TryAdd("part_sight_dot", 5, out error), Is.True, error);
            Assert.That(carried.OccupiedCells(CargoArea.Pockets), Is.EqualTo(5));
            Assert.That(carried.TryAdd("part_sight_dot", 1, out _), Is.False);
        }

        [Test]
        public void RigAllowsRotatedStockInOnePocketButNotAcrossTwo()
        {
            var carried = GridInventory.Carried(catalog);
            carried.Restore(new List<ItemStack> { new ItemStack { ItemId = "rig_watch", InstanceId = "rig", Slot = EquipmentSlot.Rig } });
            var stash = NewStash();
            Assert.That(carried.Layout(CargoArea.Rig).Capacity, Is.EqualTo(12));
            Assert.That(stash.TryInsert(new ItemStack { ItemId = "part_stock", InstanceId = "stock" }, out var error), Is.True, error);
            Assert.That(stash.TryTransferAt("stock", carried, CargoArea.Rig, 0, 0, false, out _), Is.False);
            Assert.That(stash.TryTransferAt("stock", carried, CargoArea.Rig, 2, 0, false, out _), Is.False);
            Assert.That(stash.TryTransferAt("stock", carried, CargoArea.Rig, 0, 0, true, out error), Is.True, error);
            Assert.That(Position(carried.Find("stock")), Is.EqualTo((CargoArea.Rig, 0, 0, true)));
        }

        [Test]
        public void WearingCompactPackFreesStashCellsAndAllowsExactDropsImmediately()
        {
            var carried = GridInventory.Carried(catalog);
            var stash = NewStash();
            stash.Restore(new List<ItemStack>
            {
                new ItemStack { ItemId = "pack_compact", InstanceId = "pack", X = 4, Y = 2 },
                new ItemStack { ItemId = "part_stock", InstanceId = "stock" }
            });
            Assert.That(carried.TryInsert(new ItemStack { ItemId = "ammo_copper", InstanceId = "rounds" }, out var error), Is.True, error);
            Assert.That(carried.TryTransferAt("rounds", stash, CargoArea.Main, 5, 4, false, out _), Is.False);

            Assert.That(TryWear(carried, stash, "pack", EquipmentSlot.Pack, out error), Is.True, error);
            Assert.That(carried.TryTransferAt("rounds", stash, CargoArea.Main, 5, 4, false, out error), Is.True, error);
            Assert.That(Position(stash.Find("rounds")), Is.EqualTo((CargoArea.Main, 5, 4, false)));
            Assert.That(stash.Find("pack"), Is.Null);
            Assert.That(carried.Equipped(EquipmentSlot.Pack)?.InstanceId, Is.EqualTo("pack"));
            var layout = carried.Layout(CargoArea.Main);
            Assert.That((layout.Columns, layout.Rows, layout.Capacity), Is.EqualTo((7, 4, 28)));
            Assert.That(stash.TryTransferAt("stock", carried, CargoArea.Main, 5, 3, false, out error), Is.True, error);
            Assert.That(Position(carried.Find("stock")), Is.EqualTo((CargoArea.Main, 5, 3, false)));
            Assert.That(carried.OccupiedCells(CargoArea.Main), Is.EqualTo(2));
        }

        [Test]
        public void SwappingPrimariesKeepsEachBuildAndSidearmRejectsTheRifle()
        {
            var first = new ItemStack { ItemId = WeaponDefinition.RifleId, InstanceId = "first", Slot = EquipmentSlot.Weapon,
                LoadedAmmo = 28, LoadedAmmoId = "ammo_amber_range" };
            first.Parts.Magazine = new PartLink { ItemId = "part_mag_a", InstanceId = "magazine" };
            var second = new ItemStack { ItemId = WeaponDefinition.RifleId, InstanceId = "second", Slot = EquipmentSlot.PrimaryTwo,
                LoadedAmmo = 7, LoadedAmmoId = "ammo" };
            second.Parts.Sight = new PartLink { ItemId = "part_sight_dot", InstanceId = "sight" };
            var firstBuild = Build(first);
            var secondBuild = Build(second);
            var carried = GridInventory.Carried(catalog);
            carried.Restore(new List<ItemStack> { first, second });

            Assert.That(TryWear(carried, carried, "first", EquipmentSlot.PrimaryTwo, out var error), Is.True, error);
            Assert.That(Build(carried.Equipped(EquipmentSlot.Weapon)), Is.EqualTo(secondBuild));
            Assert.That(Build(carried.Equipped(EquipmentSlot.PrimaryTwo)), Is.EqualTo(firstBuild));

            Assert.That(TryWear(carried, carried, "first", EquipmentSlot.Sidearm, out _), Is.False);
            Assert.That(carried.Equipped(EquipmentSlot.Sidearm), Is.Null);
            Assert.That(Build(carried.Equipped(EquipmentSlot.PrimaryTwo)), Is.EqualTo(firstBuild));

            var stash = NewStash();
            Assert.That(stash.TryInsert(new ItemStack { ItemId = "dust_pistol", InstanceId = "pistol" }, out error), Is.True, error);
            Assert.That(stash.TryInsert(new ItemStack { ItemId = WeaponDefinition.BatonId, InstanceId = "baton" }, out error), Is.True, error);
            Assert.That(TryWear(carried, stash, "pistol", EquipmentSlot.Sidearm, out error), Is.True, error);
            Assert.That(TryWear(carried, stash, "baton", EquipmentSlot.Melee, out error), Is.True, error);
            Assert.That(carried.Equipped(EquipmentSlot.Sidearm)?.InstanceId, Is.EqualTo("pistol"));
            Assert.That(carried.Equipped(EquipmentSlot.Melee)?.InstanceId, Is.EqualTo("baton"));
            Assert.That(Build(carried.Equipped(EquipmentSlot.Weapon)), Is.EqualTo(secondBuild));
            Assert.That(Build(carried.Equipped(EquipmentSlot.PrimaryTwo)), Is.EqualTo(firstBuild));
        }

        [TestCase("dust_pistol", EquipmentSlot.Sidearm)]
        [TestCase(WeaponDefinition.BatonId, EquipmentSlot.Melee)]
        public void SchemaTwoMigratesOldHandSlotAndKeepsOverflowInOneWayLegacy(string itemId, EquipmentSlot slot)
        {
            var older = new ProfileData { Version = 2, Loadout = new List<ItemStack>
            {
                new ItemStack { ItemId = itemId, InstanceId = "hand", Slot = EquipmentSlot.Weapon },
                new ItemStack { ItemId = "rig_watch", InstanceId = "rig", Slot = EquipmentSlot.Rig },
                // A 2x3 vest fits the old continuous rig, but none of its new pockets.
                new ItemStack { ItemId = "armor", InstanceId = "overflow", Area = CargoArea.Rig, X = 1 }
            } };
            string directory = Path.Combine(Path.GetTempPath(), "dustline-loadout-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var store = new ProfileStore(Path.Combine(directory, "profile.json"));
                File.WriteAllText(store.Path, "\r\n" + JsonUtility.ToJson(older, true) + "\r\n", new UTF8Encoding(true));
                byte[] original = File.ReadAllBytes(store.Path);
                var upgraded = store.Load();
                Assert.That(upgraded.Version, Is.EqualTo(2));
                var carried = GridInventory.Carried(catalog);
                Assert.That(migrate, Is.Not.Null);
                migrate.Invoke(carried, new object[] { upgraded.Loadout });
                upgraded.Version = ProfileData.CurrentVersion;
                upgraded.Loadout = carried.Snapshot();
                store.Save(upgraded);

                var saved = store.Load();
                Assert.That(saved.Version, Is.EqualTo(3));
                byte[] savedBytes = File.ReadAllBytes(store.Path);
                Assert.Throws<InvalidDataException>(() => store.Save(older));
                Assert.That(File.ReadAllBytes(store.Path), Is.EqualTo(savedBytes));
                Assert.That(File.ReadAllBytes(store.Path + ".pre-v3.bak"), Is.EqualTo(original));

                var mislabeled = new ProfileStore(Path.Combine(directory, "mislabeled.json"));
                Assert.Throws<InvalidDataException>(() => mislabeled.Save(new ProfileData { Version = 2, Loadout = saved.Loadout }));
                Assert.That(File.Exists(mislabeled.Path), Is.False);

                carried.Restore(saved.Loadout);
                Assert.That(carried.Equipped(EquipmentSlot.Weapon), Is.Null);
                Assert.That(carried.Equipped(slot)?.InstanceId, Is.EqualTo("hand"));
                Assert.That(carried.Equipped(EquipmentSlot.Rig)?.InstanceId, Is.EqualTo("rig"));
                var overflow = carried.Find("overflow");
                Assert.That(overflow?.Area, Is.EqualTo(CargoArea.Legacy));
                var stash = NewStash();
                Assert.That(carried.TryTransferTo("overflow", stash, out var error), Is.True, error);
                Assert.That(stash.Find("overflow")?.ItemId, Is.EqualTo("armor"));
                Assert.That(stash.TryTransferAt("overflow", carried, CargoArea.Legacy, overflow.X, overflow.Y, overflow.Rotated, out _), Is.False);
                Assert.That(stash.TryTransferTo("overflow", carried, out _), Is.False);
                Assert.That(carried.Find("overflow"), Is.Null);
                Assert.That(stash.Find("overflow"), Is.Not.Null);
            }
            finally { Directory.Delete(directory, true); }
        }

        GridInventory NewStash() => new GridInventory(12, 10, 300f, catalog);

        static (CargoArea, int, int, bool) Position(ItemStack stack) => (stack.Area, stack.X, stack.Y, stack.Rotated);

        static (string, string, WeaponParts, int, string) Build(ItemStack stack)
            => (stack.InstanceId, stack.ItemId, stack.Parts, stack.LoadedAmmo, stack.LoadedAmmoId);

        static bool TryWear(GridInventory carried, GridInventory source, string id, EquipmentSlot slot, out string error)
        {
            Assert.That(wear, Is.Not.Null);
            var args = new object[] { source, id, slot, null };
            bool result = (bool)wear.Invoke(carried, args);
            error = (string)args[3];
            return result;
        }
    }
}
