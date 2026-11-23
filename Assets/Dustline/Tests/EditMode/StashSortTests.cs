using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Dustline
{
    public sealed class StashSortTests
    {
        static readonly MethodInfo sort = typeof(GridInventory).GetMethod("TrySortStash", BindingFlags.Instance | BindingFlags.NonPublic);
        readonly List<ScriptableObject> assets = new List<ScriptableObject>();
        ItemCatalog catalog;

        [SetUp]
        public void SetUp() => catalog = Create<ItemCatalog>();

        [TearDown]
        public void TearDown()
        {
            for (int i = assets.Count - 1; i >= 0; i--) Object.DestroyImmediate(assets[i]);
            assets.Clear();
        }

        [Test]
        public void ConsolidatesLooseRoundsAndUnopenedMedicineWithoutChangingInstanceState()
        {
            var rifle = Define(WeaponDefinition.RifleId, ItemKind.Weapon, 3, 2, 20);
            rifle.Durability = 100;
            rifle.Weapon = Create<WeaponDefinition>();
            rifle.Weapon.AmmoId = "ammo";
            rifle.Weapon.AmmoFamily = AmmoFamily.Amber;
            rifle.Weapon.FrameInterface = "test-frame";
            Define("ammo", ItemKind.Ammo, maxStack: 10).AmmoFamily = AmmoFamily.Amber;
            Define("alternate", ItemKind.Ammo, maxStack: 10).AmmoFamily = AmmoFamily.Amber;
            Define("magazine", ItemKind.Part, maxStack: 10).Part = new PartSpec
            { Slot = PartSlot.Magazine, Interface = "test-frame", Form = 1, Capacity = 30 };
            Define("kit", ItemKind.Medicine, maxStack: 5).Uses = 4;
            Define("durable", ItemKind.Valuable, maxStack: 5).Durability = 20;
            Define("unique", ItemKind.Valuable);

            var rifleA = Stack("rifle-a", rifle.Id, 8, 6);
            rifleA.Parts.Magazine = new PartLink { ItemId = "magazine", InstanceId = "mounted-a" };
            rifleA.Mods = new WeaponMods { Sight = 1, OwnedSight = 2, OwnedGrip = 1 };
            rifleA.LoadedAmmo = 22;
            rifleA.LoadedAmmoId = "alternate";
            rifleA.Wear = 17;
            var rifleB = Stack("rifle-b", rifle.Id, 2, 7);
            rifleB.Parts.Magazine = new PartLink { ItemId = "magazine", InstanceId = "mounted-b" };
            rifleB.Mods = new WeaponMods { Grip = 2, OwnedGrip = 2, OwnedStock = 1 };
            rifleB.LoadedAmmo = 7;
            rifleB.LoadedAmmoId = "ammo";
            rifleB.Wear = 3;

            var worn = Stack("durable-b", "durable", 1, 9);
            worn.Wear = 5;
            var openedA = Stack("opened-a", "kit", 3, 9, 2);
            openedA.UsesSpent = 3;
            var openedB = Stack("opened-b", "kit", 4, 9);
            openedB.UsesSpent = 1;
            var unchanged = new[]
            {
                rifleA, rifleB, Stack("durable-a", "durable", 0, 9), worn, Stack("durable-c", "durable", 2, 9),
                openedA, openedB, Stack("loose-mag-a", "magazine", 7, 9), Stack("loose-mag-b", "magazine", 8, 9),
                Stack("unique-a", "unique", 9, 9), Stack("unique-b", "unique", 10, 9), Stack("alt", "alternate", 3, 5, 3)
            };
            var mergeable = new[]
            {
                Stack("ammo-a", "ammo", 0, 5, 4), Stack("ammo-b", "ammo", 1, 5, 8), Stack("ammo-c", "ammo", 2, 5, 8),
                Stack("sealed-a", "kit", 5, 9, 2), Stack("sealed-b", "kit", 6, 9, 3)
            };
            var stash = NewStash();
            stash.Restore(unchanged.Concat(mergeable).Reverse().ToList());
            var references = stash.Items.ToDictionary(item => item.InstanceId);
            float weight = stash.Weight;
            int changes = 0;
            stash.Changed += () => changes++;

            Assert.That(TrySort(stash, out var error), Is.True, error);
            Assert.That(error, Is.Null);
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(stash.Weight, Is.EqualTo(weight));
            Assert.That(stash.Items.Count, Is.EqualTo(unchanged.Length + 3));
            Assert.That(stash.Count("ammo"), Is.EqualTo(20));
            Assert.That(stash.Find("ammo-a").Quantity, Is.EqualTo(10));
            Assert.That(stash.Find("ammo-b").Quantity, Is.EqualTo(10));
            Assert.That(stash.Find("ammo-c"), Is.Null);
            Assert.That(stash.Count("kit"), Is.EqualTo(8));
            Assert.That(stash.RemainingUses("kit"), Is.EqualTo(28));
            Assert.That(stash.Find("sealed-a").Quantity, Is.EqualTo(5));
            Assert.That(stash.Find("sealed-a").UsesSpent, Is.Zero);
            Assert.That(stash.Find("sealed-b"), Is.Null);
            foreach (var before in unchanged)
            {
                var after = stash.Find(before.InstanceId);
                Assert.That(after, Is.SameAs(references[before.InstanceId]), before.InstanceId);
                Assert.That(State(after), Is.EqualTo(State(before)), before.InstanceId);
            }
            Assert.DoesNotThrow(() => NewStash().Restore(stash.Snapshot()));
        }

        [Test]
        public void PlacesLargeItemsFirstRotatesIntoNarrowSpaceAndFillsHolesDeterministically()
        {
            Define("slab", ItemKind.Valuable, 10, 9);
            Define("strip", ItemKind.Valuable, 3, 2);
            Define("filler", ItemKind.Valuable);
            var input = new List<ItemStack>
            {
                Stack("filler-b", "filler", 1, 8), Stack("strip", "strip", 0, 4),
                Stack("filler-a", "filler", 1, 9), Stack("slab", "slab", 3, 0, rotated: true)
            };
            var first = NewStash();
            var second = NewStash();
            first.Restore(input);
            input.Reverse();
            second.Restore(input);

            Assert.That(TrySort(first, out var error), Is.True, error);
            Assert.That(TrySort(second, out error), Is.True, error);
            CollectionAssert.AreEqual(Layout(first), Layout(second));
            Assert.That(Position(first.Find("slab")), Is.EqualTo((0, 0, false)));
            Assert.That(Position(first.Find("strip")), Is.EqualTo((10, 0, true)));
            Assert.That(Position(first.Find("filler-a")), Is.EqualTo((10, 3, false)));
            Assert.That(Position(first.Find("filler-b")), Is.EqualTo((11, 3, false)));
            Assert.That(first.Items.Sum(item => item.Quantity), Is.EqualTo(4));
        }

        [Test]
        public void UsesOriginalPositionsForMergedStacksWhenGreedyPlacementCannotFit()
        {
            Define("block", ItemKind.Valuable, 10, 7);
            Define("wide", ItemKind.Valuable, 8, 4);
            Define("ammo", ItemKind.Ammo, maxStack: 10);
            var stash = NewStash();
            stash.Restore(new List<ItemStack>
            {
                Stack("block", "block", 1, 0, rotated: true), Stack("wide", "wide", 8, 1, rotated: true),
                Stack("ammo-a", "ammo", 0, 7, 2), Stack("ammo-b", "ammo", 0, 9, 3)
            });
            int changes = 0;
            stash.Changed += () => changes++;

            Assert.That(TrySort(stash, out var error), Is.True, error);
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(stash.Items.Count, Is.EqualTo(3));
            Assert.That(stash.Count("ammo"), Is.EqualTo(5));
            Assert.That(stash.Find("ammo-a").Quantity, Is.EqualTo(5));
            Assert.That(Position(stash.Find("ammo-a")), Is.EqualTo((0, 7, false)));
            Assert.That(Position(stash.Find("block")), Is.EqualTo((1, 0, true)));
            Assert.That(Position(stash.Find("wide")), Is.EqualTo((8, 1, true)));
            Assert.DoesNotThrow(() => NewStash().Restore(stash.Snapshot()));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void RejectsCarriedInventoryAndLootSizedGrid(bool carried)
        {
            Define("ammo", ItemKind.Ammo, maxStack: 10);
            var inventory = carried ? GridInventory.Carried(catalog) : new GridInventory(6, 5, 100f, catalog);
            Assert.That(inventory.TryInsert(Stack("ammo-a", "ammo", 0, 0, 3), out var error), Is.True, error);
            Assert.That(inventory.TryInsert(Stack("ammo-b", "ammo", 0, 0, 2), out error), Is.True, error);
            AssertRejectedUnchanged(inventory);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void RejectsEquippedAndNonMainCargoInStashSizedGrid(bool equipped)
        {
            Define("armor", ItemKind.Armor);
            var stash = NewStash();
            stash.Restore(new List<ItemStack> { Stack("armor", "armor", 1, 1) });
            if (equipped) Assert.That(stash.TryEquip("armor", EquipmentSlot.Armor, out var error), Is.True, error);
            else stash.Find("armor").Area = CargoArea.Rig;
            AssertRejectedUnchanged(stash);
        }

        [Test]
        public void InvalidExistingLayoutRejectsWithoutMergingOrNotifying()
        {
            Define("ammo", ItemKind.Ammo, maxStack: 10);
            var stash = NewStash();
            stash.Restore(new List<ItemStack> { Stack("ammo-a", "ammo", 3, 0, 2), Stack("ammo-b", "ammo", 4, 0, 3) });
            stash.Find("ammo-b").X = 3;
            AssertRejectedUnchanged(stash);
        }

        static void AssertRejectedUnchanged(GridInventory inventory)
        {
            var before = Layout(inventory);
            var references = inventory.Items.ToArray();
            int changes = 0;
            inventory.Changed += () => changes++;
            Assert.That(TrySort(inventory, out var error), Is.False);
            Assert.That(error, Is.Not.Empty);
            Assert.That(changes, Is.Zero);
            CollectionAssert.AreEqual(before, Layout(inventory));
            for (int i = 0; i < references.Length; i++) Assert.That(inventory.Items[i], Is.SameAs(references[i]));
        }

        static bool TrySort(GridInventory inventory, out string error)
        {
            Assert.That(sort, Is.Not.Null);
            var args = new object[1];
            bool result = (bool)sort.Invoke(inventory, args);
            error = (string)args[0];
            return result;
        }

        static (int, int, bool) Position(ItemStack item) => (item.X, item.Y, item.Rotated);
        static (string, string, int, EquipmentSlot, CargoArea, int, string, WeaponParts, WeaponMods, int, int) State(ItemStack item)
            => (item.InstanceId, item.ItemId, item.Quantity, item.Slot, item.Area, item.LoadedAmmo,
                item.LoadedAmmoId, item.Parts, item.Mods, item.UsesSpent, item.Wear);
        static object[] Layout(GridInventory inventory) => inventory.Items.Select(item => (object)(State(item), Position(item))).ToArray();

        GridInventory NewStash() => new GridInventory(12, 10, 300f, catalog);

        static ItemStack Stack(string id, string itemId, int x, int y, int quantity = 1, bool rotated = false)
            => new ItemStack { InstanceId = id, ItemId = itemId, X = x, Y = y, Quantity = quantity, Rotated = rotated };

        ItemDefinition Define(string id, ItemKind kind, int width = 1, int height = 1, int maxStack = 1)
        {
            var item = Create<ItemDefinition>();
            item.Id = id;
            item.Kind = kind;
            item.Width = width;
            item.Height = height;
            item.MaxStack = maxStack;
            item.UnitWeight = 0.25f;
            catalog.Items.Add(item);
            return item;
        }

        T Create<T>() where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            assets.Add(asset);
            return asset;
        }
    }
}
