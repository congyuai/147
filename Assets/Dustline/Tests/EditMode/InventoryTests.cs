using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Dustline
{
    public sealed class InventoryTests
    {
        private readonly List<ScriptableObject> assets = new List<ScriptableObject>();
        private ItemCatalog catalog;

        [SetUp]
        public void SetUp()
        {
            catalog = Create<ItemCatalog>();
            ItemDefinition rifle = Define("rifle", ItemKind.Weapon, 2, 1, 50, 2f);
            rifle.Weapon = Create<WeaponDefinition>();
            rifle.Weapon.AmmoId = "ammo";
            Define("armor", ItemKind.Armor, 1, 1, 50, 3f);
            Define("ammo", ItemKind.Ammo, 1, 1, 10, 0.25f);
            Define("medicine", ItemKind.Medicine, 1, 1, 3, 0.5f);
            Define("bar", ItemKind.Valuable, 2, 1, 1, 1f);
            Define("coin", ItemKind.Valuable, 1, 1, 1, 0f);
            catalog.RebuildIndex();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = assets.Count - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(assets[i]);
            assets.Clear();
        }

        [Test]
        public void MoveChecksCollisionRotationAndBoundsAtomically()
        {
            var inventory = new GridInventory(3, 2, 20f, catalog);
            Assert.That(inventory.TryAdd("bar", 1, out _), Is.True);
            ItemStack bar = inventory.Items[0];
            Assert.That(inventory.TryAdd("coin", 1, out _), Is.True);
            ItemStack coin = inventory.Items[1];
            int changed = 0;
            inventory.Changed += () => changed++;

            Assert.That(inventory.TryMove(coin.InstanceId, 1, 0, false, out string error), Is.False);
            Assert.That(error, Is.Not.Empty);
            Assert.That(coin.X, Is.EqualTo(2));
            Assert.That(inventory.TryMove(bar.InstanceId, -1, 0, false, out _), Is.False);
            Assert.That(inventory.TryMove(bar.InstanceId, int.MaxValue, 0, false, out _), Is.False);
            Assert.That(changed, Is.Zero);

            Assert.That(inventory.TryMove(bar.InstanceId, 0, 0, true, out _), Is.True);
            Assert.That(bar.Rotated, Is.True);
            Assert.That(inventory.TryMove(coin.InstanceId, 1, 0, false, out _), Is.True);
            Assert.That(inventory.TryMove(bar.InstanceId, 0, 0, false, out _), Is.False);
            Assert.That(inventory.TryMove(bar.InstanceId, 0, 1, true, out _), Is.False);
            Assert.That(bar.Rotated, Is.True);
            Assert.That(bar.Y, Is.Zero);
            Assert.That(changed, Is.EqualTo(2));
        }

        [Test]
        public void AddAutomaticallyRotatesToFit()
        {
            var inventory = new GridInventory(1, 2, 20f, catalog);
            Assert.That(inventory.TryAdd("bar", 1, out string error), Is.True, error);
            Assert.That(inventory.Items[0].Rotated, Is.True);
            Assert.That(inventory.TryAdd("coin", 1, out _), Is.False);
            Assert.That(inventory.Items.Count, Is.EqualTo(1));
        }

        [Test]
        public void StackingFillsExistingThenCreatesStacksAndPreservesReferences()
        {
            var inventory = new GridInventory(2, 1, 20f, catalog);
            Assert.That(inventory.TryAdd("ammo", 8, out _), Is.True);
            ItemStack original = inventory.Items[0];
            IReadOnlyList<ItemStack> view = inventory.Items;
            int changed = 0;
            inventory.Changed += () => changed++;

            Assert.That(inventory.TryAdd("ammo", 5, out string error), Is.True, error);
            Assert.That(inventory.Find(original.InstanceId), Is.SameAs(original));
            Assert.That(original.Quantity, Is.EqualTo(10));
            Assert.That(view, Is.SameAs(inventory.Items));
            Assert.That(view.Count, Is.EqualTo(2));
            Assert.That(view[1].Quantity, Is.EqualTo(3));
            Assert.That(inventory.Count("ammo"), Is.EqualTo(13));
            Assert.That(changed, Is.EqualTo(1));
        }

        [Test]
        public void FailedAddDoesNotPartiallyMergeOrNotify()
        {
            var inventory = new GridInventory(1, 1, 20f, catalog);
            Assert.That(inventory.TryAdd("ammo", 8, out _), Is.True);
            ItemStack original = inventory.Items[0];
            int changed = 0;
            inventory.Changed += () => changed++;

            Assert.That(inventory.TryAdd("ammo", 3, out string error), Is.False);
            Assert.That(error, Is.EqualTo("背包空间不足。"));
            Assert.That(inventory.TryAdd("unknown", 1, out _), Is.False);
            Assert.That(inventory.TryAdd("ammo", 0, out _), Is.False);
            Assert.That(inventory.TryAdd("ammo", -1, out _), Is.False);
            Assert.That(inventory.Items.Count, Is.EqualTo(1));
            Assert.That(inventory.Items[0], Is.SameAs(original));
            Assert.That(original.Quantity, Is.EqualTo(8));
            Assert.That(changed, Is.Zero);
        }

        [Test]
        public void WeightLimitIncludesEquipmentButNotLoadedAmmunition()
        {
            var inventory = new GridInventory(4, 2, 3f, catalog);
            Assert.That(inventory.TryAdd("rifle", 1, out _), Is.True);
            ItemStack rifle = inventory.Items[0];
            Assert.That(inventory.TryEquip(rifle.InstanceId, EquipmentSlot.Weapon, out _), Is.True);
            rifle.LoadedAmmo = 20;
            inventory.Touch();
            Assert.That(inventory.Weight, Is.EqualTo(2f));
            Assert.That(inventory.TryAdd("ammo", 4, out _), Is.True);
            Assert.That(inventory.Weight, Is.EqualTo(3f));
            int changed = 0;
            inventory.Changed += () => changed++;

            Assert.That(inventory.TryAdd("ammo", 1, out string error), Is.False);
            Assert.That(error, Is.EqualTo("已超出负重上限。"));
            Assert.That(inventory.Count("ammo"), Is.EqualTo(4));
            Assert.That(inventory.Weight, Is.EqualTo(3f));
            Assert.That(changed, Is.Zero);
        }

        [Test]
        public void EquipmentMatchesKindAndNeverOverwritesOccupiedSlots()
        {
            var inventory = new GridInventory(4, 2, 30f, catalog);
            Assert.That(inventory.TryAdd("rifle", 2, out _), Is.True);
            ItemStack first = inventory.Items[0];
            ItemStack second = inventory.Items[1];
            Assert.That(first.Quantity, Is.EqualTo(1));
            Assert.That(second.Quantity, Is.EqualTo(1));
            Assert.That(inventory.TryAdd("armor", 2, out _), Is.True);
            ItemStack armor = inventory.Items[2];
            Assert.That(armor.Quantity, Is.EqualTo(1));
            Assert.That(inventory.Items[3].Quantity, Is.EqualTo(1));

            Assert.That(inventory.TryEquip(first.InstanceId, EquipmentSlot.Armor, out _), Is.False);
            Assert.That(inventory.TryEquip(first.InstanceId, EquipmentSlot.None, out _), Is.False);
            Assert.That(inventory.TryEquip(first.InstanceId, EquipmentSlot.Weapon, out _), Is.True);
            Assert.That(inventory.TryEquip(second.InstanceId, EquipmentSlot.Weapon, out string error), Is.False);
            Assert.That(error, Is.EqualTo("请先卸下已装备的物品。"));
            Assert.That(inventory.Equipped(EquipmentSlot.Weapon), Is.SameAs(first));
            Assert.That(second.Slot, Is.EqualTo(EquipmentSlot.None));
            Assert.That(inventory.TryMove(first.InstanceId, 0, 1, false, out _), Is.False);
            Assert.That(inventory.TryEquip(armor.InstanceId, EquipmentSlot.Armor, out _), Is.True);
            Assert.That(inventory.Equipped(EquipmentSlot.Armor), Is.SameAs(armor));
            Assert.That(inventory.Equipped(EquipmentSlot.None), Is.Null);
            Assert.That(inventory.TryUnequip(EquipmentSlot.Weapon, out _), Is.True);
            Assert.That(inventory.Equipped(EquipmentSlot.Weapon), Is.Null);
            Assert.That(first.Slot, Is.EqualTo(EquipmentSlot.None));
            Assert.That(inventory.TryEquip(second.InstanceId, EquipmentSlot.Weapon, out _), Is.True);
        }

        [Test]
        public void UnequipRequiresRoomAndDoesNotLoseEquipmentOnFailure()
        {
            var inventory = new GridInventory(2, 1, 20f, catalog);
            Assert.That(inventory.TryAdd("rifle", 1, out _), Is.True);
            ItemStack rifle = inventory.Items[0];
            Assert.That(inventory.TryEquip(rifle.InstanceId, EquipmentSlot.Weapon, out _), Is.True);
            Assert.That(inventory.TryAdd("bar", 1, out _), Is.True);
            ItemStack bar = inventory.Items[1];
            int changed = 0;
            inventory.Changed += () => changed++;

            Assert.That(inventory.TryUnequip(EquipmentSlot.Weapon, out _), Is.False);
            Assert.That(inventory.TryUnequip(EquipmentSlot.None, out _), Is.False);
            Assert.That(inventory.Equipped(EquipmentSlot.Weapon), Is.SameAs(rifle));
            Assert.That(changed, Is.Zero);
            Assert.That(inventory.Remove(bar.InstanceId), Is.True);
            Assert.That(inventory.TryUnequip(EquipmentSlot.Weapon, out _), Is.True);
            Assert.That(rifle.Slot, Is.EqualTo(EquipmentSlot.None));
            Assert.That(changed, Is.EqualTo(2));
        }

        [Test]
        public void InsertClonesInputIgnoresLocationAndPreservesMagazine()
        {
            var inventory = new GridInventory(1, 2, 20f, catalog);
            var input = new ItemStack
            {
                InstanceId = "fixed-instance",
                ItemId = "rifle",
                LoadedAmmo = 7,
                X = int.MinValue,
                Y = int.MaxValue,
                Slot = EquipmentSlot.Armor
            };
            Assert.That(inventory.TryInsert(input, out string error), Is.True, error);
            ItemStack inserted = inventory.Find(input.InstanceId);
            Assert.That(inserted, Is.Not.SameAs(input));
            Assert.That(inserted.LoadedAmmo, Is.EqualTo(7));
            Assert.That(inserted.Slot, Is.EqualTo(EquipmentSlot.None));
            Assert.That(inserted.Rotated, Is.True);
            Assert.That(input.Slot, Is.EqualTo(EquipmentSlot.Armor));
            Assert.That(input.X, Is.EqualTo(int.MinValue));
            Assert.That(inventory.TryInsert(input, out _), Is.False);
            Assert.That(inventory.Items.Count, Is.EqualTo(1));
        }

        [Test]
        public void TransferIsAtomicAndPreservesInstanceAndLoadedAmmo()
        {
            var source = new GridInventory(2, 1, 20f, catalog);
            var destination = new GridInventory(2, 1, 20f, catalog);
            Assert.That(source.TryAdd("rifle", 1, out _), Is.True);
            ItemStack rifle = source.Items[0];
            rifle.LoadedAmmo = 9;
            Assert.That(source.TryEquip(rifle.InstanceId, EquipmentSlot.Weapon, out _), Is.True);
            Assert.That(destination.TryAdd("bar", 1, out _), Is.True);
            string blocker = destination.Items[0].InstanceId;
            int sourceChanges = 0;
            int destinationChanges = 0;
            source.Changed += () =>
            {
                sourceChanges++;
                Assert.That(source.Find(rifle.InstanceId), Is.Null);
                Assert.That(destination.Find(rifle.InstanceId), Is.Not.Null);
            };
            destination.Changed += () => destinationChanges++;

            Assert.That(source.TryTransferTo(rifle.InstanceId, destination, out string error), Is.False);
            Assert.That(error, Is.Not.Empty);
            Assert.That(source.Equipped(EquipmentSlot.Weapon), Is.SameAs(rifle));
            Assert.That(rifle.LoadedAmmo, Is.EqualTo(9));
            Assert.That(source.TryTransferTo(rifle.InstanceId, source, out _), Is.False);
            Assert.That(source.TryTransferTo(rifle.InstanceId, null, out _), Is.False);
            Assert.That(sourceChanges, Is.Zero);
            Assert.That(destinationChanges, Is.Zero);

            Assert.That(destination.Remove(blocker), Is.True);
            destinationChanges = 0;
            Assert.That(source.TryTransferTo(rifle.InstanceId, destination, out error), Is.True, error);
            Assert.That(source.Items, Is.Empty);
            ItemStack received = destination.Find(rifle.InstanceId);
            Assert.That(received.ItemId, Is.EqualTo("rifle"));
            Assert.That(received.Quantity, Is.EqualTo(1));
            Assert.That(received.LoadedAmmo, Is.EqualTo(9));
            Assert.That(received.Slot, Is.EqualTo(EquipmentSlot.None));
            Assert.That(sourceChanges, Is.EqualTo(1));
            Assert.That(destinationChanges, Is.EqualTo(1));
        }

        [Test]
        public void TransferRejectsWeightAndDuplicateInstancesWithoutRemovingSource()
        {
            var source = new GridInventory(2, 1, 20f, catalog);
            var light = new GridInventory(2, 1, 1f, catalog);
            var duplicate = new GridInventory(4, 1, 20f, catalog);
            Assert.That(source.TryAdd("rifle", 1, out _), Is.True);
            ItemStack rifle = source.Items[0];
            Assert.That(source.TryTransferTo(rifle.InstanceId, light, out _), Is.False);
            Assert.That(duplicate.TryInsert(rifle, out _), Is.True);
            Assert.That(source.TryTransferTo(rifle.InstanceId, duplicate, out _), Is.False);
            Assert.That(source.Find(rifle.InstanceId), Is.SameAs(rifle));
            Assert.That(light.Items, Is.Empty);
            Assert.That(duplicate.Items.Count, Is.EqualTo(1));
        }

        [Test]
        public void TransferKeepsAFullStackSeparateFromExistingStacks()
        {
            var source = new GridInventory(1, 1, 20f, catalog);
            var destination = new GridInventory(2, 1, 20f, catalog);
            Assert.That(source.TryAdd("ammo", 3, out _), Is.True);
            string instanceId = source.Items[0].InstanceId;
            Assert.That(destination.TryAdd("ammo", 5, out _), Is.True);
            Assert.That(source.TryTransferTo(instanceId, destination, out _), Is.True);
            Assert.That(destination.Items.Count, Is.EqualTo(2));
            Assert.That(destination.Find(instanceId).Quantity, Is.EqualTo(3));
            Assert.That(destination.Count("ammo"), Is.EqualTo(8));
        }

        [Test]
        public void ConsumeIsAtomicAndSkipsEquipment()
        {
            var inventory = new GridInventory(4, 2, 30f, catalog);
            Assert.That(inventory.TryAdd("rifle", 2, out _), Is.True);
            string equippedId = inventory.Items[0].InstanceId;
            Assert.That(inventory.TryEquip(equippedId, EquipmentSlot.Weapon, out _), Is.True);
            Assert.That(inventory.TryAdd("ammo", 12, out _), Is.True);
            int changed = 0;
            inventory.Changed += () => changed++;

            Assert.That(inventory.Consume("ammo", 13), Is.False);
            Assert.That(inventory.Consume("ammo", 0), Is.False);
            Assert.That(inventory.Consume("ammo", -1), Is.False);
            Assert.That(inventory.Consume("rifle", 2), Is.False);
            Assert.That(inventory.Count("ammo"), Is.EqualTo(12));
            Assert.That(inventory.Count("rifle"), Is.EqualTo(2));
            Assert.That(changed, Is.Zero);
            Assert.That(inventory.Consume("rifle", 1), Is.True);
            Assert.That(inventory.Equipped(EquipmentSlot.Weapon).InstanceId, Is.EqualTo(equippedId));
            Assert.That(inventory.Consume("rifle", 1), Is.False);
            Assert.That(inventory.Consume("ammo", 11), Is.True);
            Assert.That(inventory.Count("ammo"), Is.EqualTo(1));
            Assert.That(inventory.Consume("ammo", 1), Is.True);
            Assert.That(inventory.Count("ammo"), Is.Zero);
            Assert.That(changed, Is.EqualTo(3));
        }

        [Test]
        public void SnapshotAndRestoreDoNotAliasCallerDataAndKeepExistingReferences()
        {
            var inventory = new GridInventory(4, 2, 20f, catalog);
            Assert.That(inventory.TryAdd("rifle", 1, out _), Is.True);
            ItemStack rifle = inventory.Items[0];
            Assert.That(inventory.TryEquip(rifle.InstanceId, EquipmentSlot.Weapon, out _), Is.True);
            rifle.LoadedAmmo = 8;
            Assert.That(inventory.TryAdd("ammo", 4, out _), Is.True);
            Assert.That(inventory.Equipped(EquipmentSlot.Weapon), Is.SameAs(rifle));
            List<ItemStack> snapshot = inventory.Snapshot();
            Assert.That(snapshot[0], Is.Not.SameAs(rifle));
            snapshot[0].LoadedAmmo = 3;
            Assert.That(rifle.LoadedAmmo, Is.EqualTo(8));
            int changed = 0;
            inventory.Changed += () => changed++;

            inventory.Restore(snapshot);
            Assert.That(inventory.Equipped(EquipmentSlot.Weapon), Is.SameAs(rifle));
            Assert.That(rifle.LoadedAmmo, Is.EqualTo(3));
            snapshot[0].LoadedAmmo = 1;
            snapshot[1].Quantity = 1;
            Assert.That(rifle.LoadedAmmo, Is.EqualTo(3));
            Assert.That(inventory.Count("ammo"), Is.EqualTo(4));
            Assert.That(changed, Is.EqualTo(1));

            var restored = new GridInventory(4, 2, 20f, catalog);
            List<ItemStack> saved = inventory.Snapshot();
            restored.Restore(saved);
            saved.Clear();
            Assert.That(restored.Equipped(EquipmentSlot.Weapon).LoadedAmmo, Is.EqualTo(3));
            Assert.That(restored.Count("ammo"), Is.EqualTo(4));
            Assert.That(restored.Weight, Is.EqualTo(3f));
        }

        [TestCase("null-list")]
        [TestCase("null-item")]
        [TestCase("empty-instance")]
        [TestCase("empty-item")]
        [TestCase("unknown-item")]
        [TestCase("duplicate-instance")]
        [TestCase("zero-quantity")]
        [TestCase("negative-quantity")]
        [TestCase("too-many")]
        [TestCase("weapon-stack")]
        [TestCase("armor-stack")]
        [TestCase("overlap")]
        [TestCase("negative-coordinate")]
        [TestCase("outside")]
        [TestCase("rotated-outside")]
        [TestCase("overweight")]
        [TestCase("wrong-slot")]
        [TestCase("invalid-slot")]
        [TestCase("duplicate-slot")]
        [TestCase("negative-loaded")]
        [TestCase("overfilled-magazine")]
        [TestCase("nonweapon-loaded")]
        public void RestoreRejectsInvalidInputWithoutChangingExistingItems(string scenario)
        {
            var inventory = new GridInventory(3, 2, 3f, catalog);
            Assert.That(inventory.TryAdd("coin", 1, out _), Is.True);
            ItemStack original = inventory.Items[0];
            var input = new List<ItemStack>
            {
                new ItemStack { InstanceId = "restored", ItemId = "ammo", Quantity = 2 }
            };
            switch (scenario)
            {
                case "null-list": input = null; break;
                case "null-item": input[0] = null; break;
                case "empty-instance": input[0].InstanceId = " "; break;
                case "empty-item": input[0].ItemId = ""; break;
                case "unknown-item": input[0].ItemId = "missing"; break;
                case "duplicate-instance":
                    input.Add(input[0].Clone());
                    input[1].X = 1;
                    break;
                case "zero-quantity": input[0].Quantity = 0; break;
                case "negative-quantity": input[0].Quantity = -1; break;
                case "too-many": input[0].Quantity = 11; break;
                case "weapon-stack": input[0].ItemId = "rifle"; break;
                case "armor-stack": input[0].ItemId = "armor"; break;
                case "overlap":
                    input.Add(new ItemStack { InstanceId = "second", ItemId = "coin" });
                    break;
                case "negative-coordinate": input[0].X = -1; break;
                case "outside": input[0].X = int.MaxValue; break;
                case "rotated-outside":
                    input[0].ItemId = "bar";
                    input[0].Quantity = 1;
                    input[0].Rotated = true;
                    input[0].Y = 1;
                    break;
                case "overweight":
                    input[0].Quantity = 10;
                    input.Add(new ItemStack { InstanceId = "second", ItemId = "ammo", Quantity = 3, X = 1 });
                    break;
                case "wrong-slot": input[0].Slot = EquipmentSlot.Weapon; break;
                case "invalid-slot": input[0].Slot = (EquipmentSlot)99; break;
                case "duplicate-slot":
                    input[0].ItemId = "rifle";
                    input[0].Quantity = 1;
                    input[0].Slot = EquipmentSlot.Weapon;
                    input.Add(input[0].Clone());
                    input[1].InstanceId = "second";
                    break;
                case "negative-loaded": input[0].LoadedAmmo = -2; break;
                case "overfilled-magazine":
                    input[0].ItemId = "rifle";
                    input[0].Quantity = 1;
                    input[0].LoadedAmmo = 21;
                    break;
                case "nonweapon-loaded": input[0].LoadedAmmo = 0; break;
            }
            int changed = 0;
            inventory.Changed += () => changed++;

            if (scenario == "null-list")
                Assert.Throws<ArgumentNullException>(() => inventory.Restore(input));
            else
                Assert.Throws<InvalidDataException>(() => inventory.Restore(input));
            Assert.That(inventory.Items.Count, Is.EqualTo(1));
            Assert.That(inventory.Items[0], Is.SameAs(original));
            Assert.That(original.ItemId, Is.EqualTo("coin"));
            Assert.That(changed, Is.Zero);
        }

        [Test]
        public void RestoreEmptyAndTouchNotifyOnceEach()
        {
            var inventory = new GridInventory(2, 1, 20f, catalog);
            Assert.That(inventory.TryAdd("coin", 1, out _), Is.True);
            int changed = 0;
            inventory.Changed += () => changed++;
            inventory.Touch();
            Assert.That(changed, Is.EqualTo(1));
            inventory.Restore(new List<ItemStack>());
            Assert.That(inventory.Items, Is.Empty);
            Assert.That(changed, Is.EqualTo(2));
        }

        [Test]
        public void CatalogRejectsDuplicateIdsAndRebuildsAfterEditing()
        {
            Define("ammo", ItemKind.Valuable, 1, 1, 1, 0f);
            Assert.Throws<ArgumentException>(() => catalog.RebuildIndex());
            Assert.Throws<ArgumentException>(() => catalog.Find("ammo"));
            catalog.Items.RemoveAt(catalog.Items.Count - 1);
            catalog.RebuildIndex();
            Assert.That(catalog.Find("ammo").Kind, Is.EqualTo(ItemKind.Ammo));
            Assert.That(catalog.Find("missing"), Is.Null);
        }

        [Test]
        public void LootRollsResolveThroughTargetCatalogAndSkipInvalidEntries()
        {
            LootTable loot = Create<LootTable>();
            loot.Rolls = 3;
            loot.Entries.Add(new LootEntry { ItemId = "missing", Weight = 100 });
            loot.Entries.Add(new LootEntry { ItemId = "coin", Weight = 0 });
            loot.Entries.Add(new LootEntry { ItemId = "coin", Min = 2, Max = 1 });
            loot.Entries.Add(new LootEntry { ItemId = "ammo", Min = 2, Max = 2, Weight = 1 });
            var inventory = new GridInventory(1, 1, 20f, catalog);
            loot.Fill(inventory, new System.Random(17));
            Assert.That(inventory.Count("ammo"), Is.EqualTo(6));
            loot.Fill(inventory, new System.Random(17));
            Assert.That(inventory.Count("ammo"), Is.EqualTo(10));
        }

        private T Create<T>() where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            assets.Add(asset);
            return asset;
        }

        private ItemDefinition Define(string id, ItemKind kind, int width, int height, int maxStack, float weight)
        {
            ItemDefinition definition = Create<ItemDefinition>();
            definition.Id = id;
            definition.DisplayName = id;
            definition.Kind = kind;
            definition.Width = width;
            definition.Height = height;
            definition.MaxStack = maxStack;
            definition.UnitWeight = weight;
            catalog.Items.Add(definition);
            return definition;
        }
    }
}
