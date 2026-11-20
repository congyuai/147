using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Dustline.Tests
{
    public sealed class LootDropTests
    {
        [UnityTest]
        public IEnumerator ContainerLootTransfersAndKeepsMagazine()
        {
            var catalog = ScriptableObject.CreateInstance<ItemCatalog>();
            var rifle = ScriptableObject.CreateInstance<ItemDefinition>();
            var weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
            var table = ScriptableObject.CreateInstance<LootTable>();
            var crate = new GameObject("Drop", typeof(LootContainer));
            try
            {
                rifle.Id = "rifle";
                rifle.Kind = ItemKind.Weapon;
                rifle.Width = 3;
                rifle.Height = 2;
                rifle.Weapon = weapon;
                weapon.MagazineSize = 20;
                catalog.Items.Add(rifle);
                catalog.RebuildIndex();
                var container = crate.GetComponent<LootContainer>();
                container.Initialize("Fallen combatant", catalog);
                table.Rolls = 1;
                table.Entries = new List<LootEntry> { new LootEntry { ItemId = "rifle", Min = 1, Max = 1, Weight = 1 } };
                table.Fill(container.Inventory, new System.Random(1));
                Assert.That(container.Searched, Is.False);
                container.Reveal();
                Assert.That(container.Searched, Is.True);
                Assert.That(container.Inventory.Count("rifle"), Is.EqualTo(1));
                var dropped = container.Inventory.Items[0];
                dropped.LoadedAmmo = 7;
                var bag = new GridInventory(6, 6, 30, catalog);
                Assert.That(container.Inventory.TryTransferTo(dropped.InstanceId, bag, out var error), Is.True, error);
                var inventory = container.Inventory;
                container.Reveal();
                container.Initialize("Repeated search", catalog);
                Assert.That(container.Searched, Is.True);
                Assert.That(container.Inventory, Is.SameAs(inventory));
                Assert.That(container.Inventory.Items.Count, Is.Zero);
                Assert.That(bag.Find(dropped.InstanceId).LoadedAmmo, Is.EqualTo(7));
            }
            finally
            {
                Object.Destroy(crate);
                Object.Destroy(table);
                Object.Destroy(catalog);
                Object.Destroy(rifle);
                Object.Destroy(weapon);
            }
            yield return null;
        }
    }
}
