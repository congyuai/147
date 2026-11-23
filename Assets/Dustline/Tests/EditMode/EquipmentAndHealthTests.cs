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

        [TestCase(ItemKind.Rig, "rig_5 rig_4 rig_watch rig_3 rig_2 rig_1")]
        [TestCase(ItemKind.Pack, "pack_6 pack_5 pack_patrol pack_4 pack_3 pack_compact pack_2 pack_1")]
        [TestCase(ItemKind.Armor, "vest_6 vest_5 vest_flex vest_4 vest_3 armor vest_2 vest_1")]
        [TestCase(ItemKind.Helmet, "helmet_6 helmet_5 helmet_4 helmet_scout helmet_3 helmet_2 helmet_1")]
        public void GearShopListsOnlyTheRequestedKindInTierAndPriceOrder(ItemKind kind, string expected)
        {
            var stock = catalog.Stock(kind);
            Assert.That(stock.All(item => item.Kind == kind), Is.True);
            CollectionAssert.AreEqual(expected.Split(' '), stock.Select(item => item.Id).ToArray());
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
                CollectionAssert.AreEqual(new[] { "red-a", "red-b", "red-expensive", "white", "cheap" }, sorted);
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
            var ordinary = new ItemStack { ItemId = rifle.Id };
            var proposed = ordinary.Clone();
            proposed.Parts.Magazine = new PartLink { ItemId = stock[0].Id };
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
