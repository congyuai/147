using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Dustline.Tests
{
    public sealed class WarehouseAndEconomyTests
    {
        ItemCatalog catalog;

        [SetUp]
        public void LoadCatalog()
        {
            catalog = Resources.Load<ItemCatalog>("Dustline/Items");
            Assert.That(catalog, Is.Not.Null, "Run Dustline > Prepare Project first.");
        }

        [Test]
        public void WarehouseExpansionRestoresOldCoordinatesAndAllowsUseOfTheLastRow()
        {
            var old = new GridInventory(12, 10, 300, catalog);
            var warehouse = GridInventory.Warehouse(catalog);
            Assert.That((warehouse.Width, warehouse.Height), Is.EqualTo((12, 30)));
            Assert.That(old.TryInsert(new ItemStack { ItemId = "ammo", InstanceId = "original-rounds", Quantity = 7 }, out _), Is.True);
            Assert.That(old.TryMove("original-rounds", 11, 9, false, out _), Is.True);
            warehouse.Restore(old.Snapshot());
            Assert.That((warehouse.Find("original-rounds").X, warehouse.Find("original-rounds").Y, warehouse.Find("original-rounds").Quantity),
                Is.EqualTo((11, 9, 7)));
            Assert.That(warehouse.TryMove("original-rounds", 11, 29, false, out _), Is.True);
            Assert.That(warehouse.TryMove("original-rounds", 11, 30, false, out _), Is.False);
            Assert.That(warehouse.Find("original-rounds").Y, Is.EqualTo(29));
        }

        [TestCase("secure_2x2", 2, 2)]
        [TestCase("secure_2x3", 2, 3)]
        [TestCase("secure_3x3", 3, 3)]
        [TestCase("secure_3x4", 3, 4)]
        public void CaseDefinitionsDoNotUnlockLargerCapacity(string id, int columns, int rows)
        {
            var definition = catalog.Find(id);
            Assert.That(definition.Kind, Is.EqualTo(ItemKind.SecureCase));
            Assert.That(definition.NotForSale, Is.True);
            Assert.That((definition.Storage.Columns, definition.Storage.Rows), Is.EqualTo((columns, rows)));
            var carrying = GridInventory.Carried(catalog).Layout(CargoArea.Secure);
            Assert.That((carrying.Columns, carrying.Rows, carrying.Capacity), Is.EqualTo((2, 2, 4)));
            Assert.That(GridInventory.Warehouse(catalog).Layout(CargoArea.Secure).Capacity, Is.Zero);
        }

        [Test]
        public void SecureCaseIsExplicitStorageAndDoesNotProvideFieldSupplies()
        {
            var stash = GridInventory.Warehouse(catalog);
            var carried = GridInventory.Carried(catalog);
            Assert.That(stash.TryInsert(new ItemStack { ItemId = "medkit", InstanceId = "protected-kit" }, out _), Is.True);
            Assert.That(stash.TryTransferAt("protected-kit", carried, CargoArea.Secure, 0, 0, false, out var error), Is.True, error);
            Assert.That(stash.TryInsert(new ItemStack { ItemId = "ammo", InstanceId = "protected-rounds", Quantity = 5 }, out _), Is.True);
            Assert.That(stash.TryTransferAt("protected-rounds", carried, CargoArea.Secure, 1, 0, false, out error), Is.True, error);
            Assert.That(carried.Consume("ammo", 1), Is.False);
            Assert.That(carried.RemainingUses("medkit"), Is.Zero);
            Assert.That(carried.CargoWeight(CargoArea.Secure), Is.EqualTo(carried.Weight).Within(.0001f));
            Assert.That(carried.Find("protected-rounds").Quantity, Is.EqualTo(5));
            Assert.That(carried.TryMove("protected-rounds", CargoArea.Pockets, 2, 0, false, out error), Is.True, error);
            Assert.That(carried.Consume("ammo", 1), Is.True);
            Assert.That(carried.Find("protected-rounds").Quantity, Is.EqualTo(4));
            Assert.That(carried.TryTransferTo("protected-kit", stash, out error), Is.True, error);
            Assert.That(stash.Find("protected-kit").Area, Is.EqualTo(CargoArea.Main));
        }

        [Test]
        public void SchemaFourJournalsProtectedCargoButRejectsUnprotectedRaidGear()
        {
            string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "-dustline-secure");
            try
            {
                var store = new ProfileStore(Path.Combine(dir, "profile.json"));
                var old = new ProfileData { Version = 3, Credits = 667, Raids = 1 };
                store.Save(old);
                var bytes = File.ReadAllBytes(store.Path);
                var next = store.Load();
                next.Version = ProfileData.CurrentVersion;
                next.RaidInProgress = true;
                next.Loadout.Add(new ItemStack { ItemId = "medkit", InstanceId = "secured-medical", Area = CargoArea.Secure, X = 1, UsesSpent = 1 });
                store.Save(next);
                CollectionAssert.AreEqual(bytes, File.ReadAllBytes(store.EconomyUpgradeBackupPath));
                var kept = store.Load();
                Assert.That(kept.Credits, Is.EqualTo(667));
                Assert.That(kept.Loadout.Single().UsesSpent, Is.EqualTo(1));
                Assert.That(kept.Loadout.Single().Area, Is.EqualTo(CargoArea.Secure));
                var before = File.ReadAllBytes(store.Path);
                kept.Loadout[0].Area = CargoArea.Pockets;
                Assert.Throws<InvalidDataException>(() => store.Save(kept));
                CollectionAssert.AreEqual(before, File.ReadAllBytes(store.Path));
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Test]
        public void BirthdayCakeHasAnIndependentRareRollAndFixedResaleOutsideOrdinaryEconomy()
        {
            var cake = catalog.Find(LootTable.BirthdayCake);
            Assert.That(cake.DisplayName, Is.EqualTo("生日蛋糕"));
            Assert.That(cake.Special && cake.NotForSale, Is.True);
            Assert.That(cake.Value, Is.Zero);
            Assert.That(cake.ResaleValue, Is.EqualTo(52_013_140));
            Assert.That(catalog.Stock(ItemKind.Valuable).Contains(cake), Is.False);
            Assert.That(catalog.Find("circuit").ResaleValue, Is.EqualTo(catalog.Find("circuit").Value / 2));
            var table = ScriptableObject.CreateInstance<LootTable>();
            try
            {
                table.Rolls = 1;
                table.Entries.Add(new LootEntry { ItemId = "circuit" });
                table.Entries.Add(new LootEntry { ItemId = cake.Id, Weight = int.MaxValue });
                var yes = GridInventory.Warehouse(catalog);
                var no = GridInventory.Warehouse(catalog);
                var win = new CakeRoll(0);
                table.Fill(yes, win);
                var miss = new CakeRoll(1);
                table.Fill(no, miss);
                Assert.That(win.Bound, Is.EqualTo(100_000));
                Assert.That(miss.Bound, Is.EqualTo(100_000));
                Assert.That(yes.Count(cake.Id), Is.EqualTo(1));
                Assert.That(no.Count(cake.Id), Is.Zero);
                Assert.That(no.Count("circuit"), Is.EqualTo(1));
            }
            finally { UnityEngine.Object.DestroyImmediate(table); }
        }

        [TestCase(0, 0, 0L, 1, true)]
        [TestCase(1, 1, 75000L, 2, true)]
        [TestCase(1, 1, 74999L, 2, false)]
        [TestCase(1, 1, 75000L, 1, false)]
        [TestCase(2, 0, 999999L, 99, false)]
        public void DeploymentChecksBothRequirementsAndRejectsTheClosedSite(int site, int tier, long value, int level, bool allowed)
        {
            var rules = typeof(GameSession).Assembly.GetType("Dustline.Deployment", true);
            var error = rules.GetMethod("EntryError", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(error.Invoke(null, new object[] { site, tier, value, level }) == null, Is.EqualTo(allowed));
        }

        sealed class CakeRoll : System.Random
        {
            readonly int chosen;
            public int Bound;
            public CakeRoll(int chosen) { this.chosen = chosen; }
            public override int Next(int maxValue) { Bound = maxValue; return chosen; }
            public override double NextDouble() => .8d;
        }
    }
}
