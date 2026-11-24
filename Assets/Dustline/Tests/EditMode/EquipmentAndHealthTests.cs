using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Dustline.Tests
{
    public sealed class EquipmentAndHealthTests
    {
        ItemCatalog catalog;

        [SetUp]
        public void LoadCatalog()
        {
            catalog = Resources.Load<ItemCatalog>("Dustline/Items");
            Assert.That(catalog, Is.Not.Null, "Run Dustline > Prepare Project first.");
        }

        [Test]
        public void SameSpecificationCanBeSharedButDoesNotPermitMixingOrDedicatedParts()
        {
            var pistol = catalog.Find("dust_pistol");
            var smg = catalog.Find("dust_smg");
            var copper = catalog.Find("ammo_copper");
            var magazine = catalog.Find("part_mag_c");
            Assert.That(WeaponRules.CanLoad(pistol.Weapon, copper, out _), Is.True);
            Assert.That(WeaponRules.CanLoad(smg.Weapon, copper, out _), Is.True);
            Assert.That(WeaponRules.CanMount(pistol, PartSlot.Magazine, magazine, out _), Is.True);
            Assert.That(WeaponRules.CanMount(smg, PartSlot.Magazine, magazine, out _), Is.True);
            Assert.That(WeaponRules.CanLoad(smg.Weapon, catalog.Find("ammo_slate"), out var wrongFamily), Is.False);
            Assert.That(wrongFamily, Is.Not.Empty);
            Assert.That(WeaponRules.CanMount(smg, PartSlot.Stock, catalog.Find("part_sight_dot"), out _), Is.False);
            Assert.That(WeaponRules.CanMount(smg, PartSlot.Muzzle, catalog.Find("part_muzzle_cap"), out var dedicated), Is.False);
            Assert.That(dedicated, Is.Not.Empty);
            Assert.That(WeaponRules.CanMount(catalog.Find("dust_sniper"), PartSlot.Muzzle, catalog.Find("part_muzzle_cap"), out _), Is.True);
            var rifle = catalog.Find("rifle").Weapon;
            var build = new ItemStack { LoadedAmmo = 2, LoadedAmmoId = "ammo" };
            var alternate = catalog.Find("ammo_amber_range");
            Assert.That(WeaponRules.CanLoad(rifle, alternate, out _), Is.True);
            Assert.That(WeaponRules.CanLoad(rifle, build, alternate, out _), Is.False);
            build.LoadedAmmo = 0;
            Assert.That(WeaponRules.CanLoad(rifle, build, alternate, out _), Is.True);
        }

        [TestCase(AmmoFamily.Amber, "ammo", "5.56 毫米")]
        [TestCase(AmmoFamily.Copper, "ammo_copper", "9 毫米")]
        [TestCase(AmmoFamily.Silver, "ammo_silver", "7.62 毫米")]
        [TestCase(AmmoFamily.Slate, "ammo_slate", "12 号")]
        public void UpdatedCaliberNamesKeepTheirIdsAndFamilies(AmmoFamily family, string id, string label)
        {
            Assert.That(ItemDefinition.CaliberName(family), Is.EqualTo(label));
            Assert.That(catalog.Find(id).AmmoFamily, Is.EqualTo(family));
            var stock = catalog.Stock(ItemKind.Ammo).Where(item => item.AmmoFamily == family).ToArray();
            Assert.That(stock.Select(item => item.Tier), Is.EqualTo(Enumerable.Range(1, WeaponRules.AmmoTierCount)));
            Assert.That(stock.All(item => item.DisplayName.StartsWith(label, System.StringComparison.Ordinal)), Is.True);
        }

        [TestCase(6, 80_000)]
        [TestCase(8, 100_000)]
        [TestCase(12, 180_000)]
        [TestCase(15, 240_000)]
        public void HighMagnificationSystemPriceIsExplicit(int magnification, int price)
        {
            var item = catalog.Find("part_scope_" + magnification);
            Assert.That(item.Part.Magnification, Is.EqualTo(magnification));
            Assert.That(item.Value, Is.EqualTo(price));
        }

        [Test]
        public void AmmoCapacityProbeDoesNotUseProtectedCellsOrNotifyListeners()
        {
            var bag = GridInventory.Carried(catalog);
            var ammo = catalog.Find("ammo_slate");
            var stock = new System.Collections.Generic.List<ItemStack>
            {
                new ItemStack { InstanceId = "loose", ItemId = ammo.Id, Area = CargoArea.Pockets, Quantity = ammo.MaxStack - 19 },
                new ItemStack { InstanceId = "protected", ItemId = ammo.Id, Area = CargoArea.Secure, Quantity = 1 },
                new ItemStack { InstanceId = "recovered", ItemId = ammo.Id, Area = CargoArea.Legacy, Quantity = 1 }
            };
            for (int x = 1; x < 5; x++)
                stock.Add(new ItemStack { InstanceId = "pocket-" + x, ItemId = "bandage", Area = CargoArea.Pockets, X = x });
            bag.Restore(stock);
            var probe = typeof(GridInventory).GetMethod("MaxAddable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var before = bag.Items.Select(item => (item.InstanceId, item.Area, item.Quantity)).ToArray();
            int events = 0;
            bag.Changed += () => events++;
            Assert.That(probe.Invoke(bag, new object[] { ammo.Id, 100 }), Is.EqualTo(19));
            Assert.That(probe.Invoke(bag, new object[] { ammo.Id, 1 }), Is.EqualTo(1));
            CollectionAssert.AreEqual(before, bag.Items.Select(item => (item.InstanceId, item.Area, item.Quantity)));
            Assert.That(events, Is.Zero);

            var weightLimited = new GridInventory(3, 3, ammo.UnitWeight * 21, catalog);
            Assert.That(probe.Invoke(weightLimited, new object[] { ammo.Id, 100 }), Is.EqualTo(21));
            Assert.That(weightLimited.Items, Is.Empty);
        }

        [Test]
        public void BulkWarehouseAmmoFillsOnlyOpenCellsAndRejectsOverflowAtomically()
        {
            var bag = GridInventory.Warehouse(catalog);
            var ammo = catalog.Find("ammo_slate");
            var item = new ItemStack { ItemId = "rifle", InstanceId = "rotated-rifle", Rotated = true };
            Assert.That(bag.TryInsert(item, out var error), Is.True, error);
            Assert.That(bag.TryInsert(new ItemStack { ItemId = ammo.Id, InstanceId = "partial", Quantity = ammo.MaxStack - 1 }, out error), Is.True, error);
            int limit = 1 + (GridInventory.StashColumns * GridInventory.StashRows - bag.OccupiedCells(CargoArea.Main)) * ammo.MaxStack;
            var probe = typeof(GridInventory).GetMethod("MaxAddable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(probe.Invoke(bag, new object[] { ammo.Id, int.MaxValue }), Is.EqualTo(limit));
            var before = bag.Snapshot();
            int notifications = 0;
            bag.Changed += () => notifications++;
            Assert.That(bag.TryAdd(ammo.Id, limit + 1, out _), Is.False);
            Assert.That(bag.Items.Count, Is.EqualTo(before.Count));
            Assert.That(bag.Find("partial").Quantity, Is.EqualTo(ammo.MaxStack - 1));
            Assert.That(notifications, Is.Zero);
            Assert.That(bag.TryAdd(ammo.Id, limit, out error), Is.True, error);
            Assert.That(bag.OccupiedCells(CargoArea.Main), Is.EqualTo(GridInventory.StashColumns * GridInventory.StashRows));
            var rifle = bag.Find(item.InstanceId);
            Assert.That((rifle.X, rifle.Y, rifle.Rotated), Is.EqualTo((before[0].X, before[0].Y, before[0].Rotated)));
            Assert.That(bag.Find("partial").Quantity, Is.EqualTo(ammo.MaxStack));
            Assert.That(notifications, Is.EqualTo(1));
            Assert.DoesNotThrow(() => GridInventory.Warehouse(catalog).Restore(bag.Snapshot()));
        }

        [Test]
        public void AmmoCapacityRespectsAnEquippedBagsOwnWeightLimit()
        {
            var testCatalog = ScriptableObject.CreateInstance<ItemCatalog>();
            var ammo = ScriptableObject.CreateInstance<ItemDefinition>();
            var pack = ScriptableObject.CreateInstance<ItemDefinition>();
            try
            {
                ammo.Id = "weighted-ammo";
                ammo.Kind = ItemKind.Ammo;
                ammo.MaxStack = 10;
                ammo.UnitWeight = 1;
                pack.Id = "restricted-pack";
                pack.Kind = ItemKind.Pack;
                pack.Storage = new StorageSpec { Columns = 2, Rows = 1, Split = 1, WeightLimit = 2.5f };
                testCatalog.Items.AddRange(new[] { ammo, pack, catalog.Find("bandage") });
                var bag = GridInventory.Carried(testCatalog);
                var items = new System.Collections.Generic.List<ItemStack>
                {
                    new ItemStack { InstanceId = "pack", ItemId = pack.Id, Slot = EquipmentSlot.Pack }
                };
                for (int x = 0; x < 5; x++)
                    items.Add(new ItemStack { InstanceId = "occupied-" + x, ItemId = "bandage", Area = CargoArea.Pockets, X = x });
                bag.Restore(items);
                var probe = typeof(GridInventory).GetMethod("MaxAddable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.That(probe.Invoke(bag, new object[] { ammo.Id, 100 }), Is.EqualTo(2));
                Assert.That(bag.Count(ammo.Id), Is.Zero);
                Assert.That(bag.TryAdd(ammo.Id, 3, out _), Is.False);
                Assert.That(bag.TryAdd(ammo.Id, 2, out _), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(pack);
                Object.DestroyImmediate(ammo);
                Object.DestroyImmediate(testCatalog);
            }
        }

        [TestCase(ItemKind.Rig, 5)]
        [TestCase(ItemKind.Pack, 6)]
        [TestCase(ItemKind.Armor, 6)]
        [TestCase(ItemKind.Helmet, 6)]
        public void GearShopListsOnlyTheRequestedKindInTierAndPriceOrder(ItemKind kind, int tiers)
        {
            var stock = catalog.Stock(kind);
            Assert.That(stock.All(item => item.Kind == kind && !item.NotForSale), Is.True);
            CollectionAssert.AreEqual(Enumerable.Range(1, tiers), stock.Select(item => item.Tier).Distinct());
            Assert.That(stock.GroupBy(item => item.Tier).All(level => level.Count() >= 2), Is.True);
            foreach (var row in stock.GroupBy(item => item.Tier))
                Assert.That(row.Select(item => item.Value), Is.Ordered.Ascending);
        }

        [Test]
        public void ShopSortIsGlobalAndRanksUnratedAfterEveryRatedItem()
        {
            var source = ScriptableObject.CreateInstance<ItemCatalog>();
            try
            {
                foreach (var entry in new[] { ("cheap", 0, 1), ("white", 1, 200), ("red-b", 6, 100), ("red-a", 6, 100), ("red-expensive", 6, 300) })
                {
                    var item = ScriptableObject.CreateInstance<ItemDefinition>();
                    item.Id = entry.Item1;
                    item.Tier = entry.Item2;
                    item.Value = entry.Item3;
                    item.Kind = ItemKind.Valuable;
                    source.Items.Add(item);
                }
                var sorted = source.Stock(ItemKind.Valuable).Select(item => item.Id).ToArray();
                CollectionAssert.AreEqual(new[] { "white", "red-a", "red-b", "red-expensive", "cheap" }, sorted);
                Assert.That(source.Items[0].Id, Is.EqualTo("cheap"));
            }
            finally
            {
                foreach (var item in source.Items) Object.DestroyImmediate(item);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void AmberMagazineComesFromTheFullCatalogWithNoLoosePart()
        {
            var rifle = catalog.Find("rifle");
            var stock = catalog.CompatibleParts(rifle, PartSlot.Magazine);
            CollectionAssert.AreEqual(new[] { "part_mag_a" }, stock.Select(item => item.Id).ToArray());
            Assert.That(catalog.CompatibleParts(catalog.Find("dust_rifle_jetty"), PartSlot.Magazine)
                .Any(item => item.Id == "part_mag_a_patrol"), Is.True);
            var expansion = stock.Single();
            var ordinary = new ItemStack { ItemId = rifle.Id };
            var proposed = ordinary.Clone();
            proposed.Parts.Magazine = new PartLink { ItemId = expansion.Id };
            Assert.That(ordinary.Parts.Magazine.Empty, Is.True);
            Assert.That(WeaponRules.Capacity(rifle.Weapon, ordinary, catalog), Is.EqualTo(20));
            Assert.That(WeaponRules.Capacity(rifle.Weapon, proposed, catalog), Is.EqualTo(30));
            Assert.That(WeaponRules.Look(proposed, catalog), Is.Not.EqualTo(WeaponRules.Look(ordinary, catalog)));
        }

        [Test]
        public void RotatedObjectsAndDividersAreValidatedBeforeMovingContents()
        {
            var bag = GridInventory.Carried(catalog);
            bag.Restore(new System.Collections.Generic.List<ItemStack>
            {
                new ItemStack { InstanceId = "equipped-rig", ItemId = "rig_4", Slot = EquipmentSlot.Rig },
                new ItemStack { InstanceId = "equipped-pack", ItemId = "pack_6", Slot = EquipmentSlot.Pack }
            });
            Assert.That(bag.Width, Is.EqualTo(catalog.Find("pack_6").Storage.Columns));
            Assert.That(bag.MaxWeight, Is.GreaterThan(30));
            var item = Add(bag, "part_stock");
            Assert.That(bag.TryMove(item, CargoArea.Rig, 1, 0, false, out _), Is.False);
            Assert.That(bag.TryMove(item, CargoArea.Rig, 1, 0, true, out _), Is.True);
            Assert.That(bag.TryMove(item, (CargoArea)99, 1, 0, true, out _), Is.False);
            Assert.That(bag.Find(item).Rotated, Is.True);
            Assert.That(bag.TryUnequip(EquipmentSlot.Rig, out _), Is.True);
            Assert.That(bag.Find(item).Area, Is.EqualTo(CargoArea.Main));
            Assert.That(bag.Columns(CargoArea.Rig), Is.Zero);
            Assert.DoesNotThrow(() => bag.Restore(bag.Snapshot()));
        }

        [Test]
        public void FailedBackpackRemovalPreservesAllContentsAndNotifiesNobody()
        {
            var bag = GridInventory.Carried(catalog);
            const string pack = "equipped-pack";
            bag.Restore(new System.Collections.Generic.List<ItemStack>
            {
                new ItemStack { InstanceId = pack, ItemId = "pack_6", Slot = EquipmentSlot.Pack }
            });
            Assert.That(bag.TryAdd("ammo_copper", bag.Width * bag.Height * catalog.Find("ammo_copper").MaxStack, out _) ||
                bag.TryAdd("tourniquet", bag.Width * bag.Height * catalog.Find("tourniquet").MaxStack, out _), Is.True);
            var before = bag.Items.Select(item => (item.InstanceId, item.Area, item.X, item.Y, item.Rotated, item.Quantity)).ToArray();
            var stash = new GridInventory(12, 10, 300, catalog);
            int events = 0;
            bag.Changed += () => events++;
            Assert.That(bag.TryTransferTo(pack, stash, out _), Is.False);
            Assert.That(bag.Equipped(EquipmentSlot.Pack).InstanceId, Is.EqualTo(pack));
            CollectionAssert.AreEqual(before, bag.Items.Select(item => (item.InstanceId, item.Area, item.X, item.Y, item.Rotated, item.Quantity)).ToArray());
            Assert.That(events, Is.Zero);
            Assert.That(stash.Items, Is.Empty);
        }

        [Test]
        public void BodyDamageDistinguishesBleedsFracturesVitalFailureAndOverflow()
        {
            var body = new BodyCondition(100f);
            body.Damage(BodyPart.LeftArm, 3f);
            body.Damage(BodyPart.RightArm, 6f);
            Assert.That(body.Handling, Is.GreaterThan(1f));
            Assert.That(body.TryTreat(BodyPart.RightArm, TreatmentKind.LightBleed, 0f), Is.False);
            Assert.That(body.TryTreat(BodyPart.LeftArm, TreatmentKind.LightBleed, 0f), Is.True);
            Assert.That(body.TryTreat(BodyPart.RightArm, TreatmentKind.HeavyBleed, 0f), Is.True);
            Assert.That(body.TryTreat(BodyPart.RightArm, TreatmentKind.Fracture, 0f), Is.True);
            Assert.That(body.TryTreat(BodyPart.LeftArm, TreatmentKind.Pain, 20f), Is.True);
            Assert.That(body.PainRelieved, Is.True);
            body.Damage(BodyPart.LeftLeg, 20f);
            Assert.That(body.Alive, Is.True);
            Assert.That(body.LegsAllowSprint, Is.False);
            Assert.That(body.Mobility, Is.LessThan(1f));
            Assert.That(body.Current(BodyPart.Head), Is.LessThan(body.Maximum(BodyPart.Head)));
            Assert.That(body.TryTreat(BodyPart.LeftLeg, TreatmentKind.Heal, 8f), Is.False);
            Assert.That(body.TryTreat(BodyPart.LeftLeg, TreatmentKind.Surgery, 0f), Is.True);
            Assert.That(body.TryTreat(BodyPart.LeftLeg, TreatmentKind.Heal, 8f), Is.True);
            body.Damage(BodyPart.Head, body.Current(BodyPart.Head));
            Assert.That(body.Alive, Is.False);
            Assert.That(body.TryTreat(BodyPart.Head, TreatmentKind.Surgery, 0f), Is.False);
        }

        string Add(GridInventory bag, string id)
        {
            Assert.That(bag.TryAdd(id, 1, out var error), Is.True, error);
            return bag.Items.First(item => item.ItemId == id).InstanceId;
        }
    }
}
