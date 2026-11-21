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
        public IEnumerator SolidContainersRemainSearchableAndHighValueUsesItsOwnTable()
        {
            using (var built = new PortWorldFixture())
            {
                var world = built.World;
                LootContainer starter = null;
                int high = 0;
                foreach (var loot in world.GetComponentsInChildren<LootContainer>())
                {
                    Vector3 p = loot.transform.position;
                    Assert.That(loot.GetComponent<BoxCollider>().isTrigger, Is.False);
                    if (PortLayout.HighValueArea.Contains(new Vector2(p.x, p.z)))
                    {
                        high++;
                        Assert.That(loot.Inventory.Items.Count, Is.EqualTo(1));
                        Assert.That(loot.Inventory.Count(PortWorldFixture.HighItem), Is.EqualTo(1));
                    }
                    else Assert.That(loot.Inventory.Count(PortWorldFixture.HighItem), Is.Zero);
                    if (loot.DisplayName == "共享初始补给") starter = loot;
                }
                Assert.That(high, Is.EqualTo(4));
                Assert.That(starter, Is.Not.Null);
                Vector3 p0 = starter.transform.position - Vector3.forward * 2;
                Vector3 eye = p0 + Vector3.up * 1.6f;
                Assert.That(Physics.Raycast(eye, (starter.transform.position + Vector3.up * .39f - eye).normalized,
                    out var hit, 3.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide), Is.True);
                Assert.That(hit.collider.GetComponentInParent<LootContainer>(), Is.SameAs(starter));

                var actor = new GameObject("Container collision test actor", typeof(CharacterController));
                actor.transform.SetParent(world.transform, false);
                actor.transform.position = p0 + Vector3.up * .024f;
                var motor = actor.GetComponent<CharacterController>();
                motor.radius = .35f; motor.height = 1.8f; motor.center = Vector3.up * .9f; motor.stepOffset = .25f;
                Physics.SyncTransforms();
                Assert.That((motor.Move(Vector3.forward * 3) & CollisionFlags.Sides) != 0, Is.True);
                Assert.That(actor.transform.position.z, Is.LessThan(starter.transform.position.z - .3f));
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator FallenCombatantsDoNotMakeBridgeChokepointsImpassable()
        {
            using (var built = new PortWorldFixture())
            {
                var world = built.World;
                Vector3 bridge = PortWorldFixture.Floor(-334, -44);
                Vector3 from = PortWorldFixture.Floor(-334, -53), to = PortWorldFixture.Floor(-334, -35);
                Assert.That(world.Navigation.CanTraverse(from, to), Is.True);
                for (int i = 0; i < 5; i++)
                {
                    var bag = world.CreateLoot(bridge + Vector3.up * .17f, "落地背包 " + i);
                    Assert.That(bag.GetComponent<BoxCollider>().isTrigger, Is.True);
                    Assert.That(bag.transform.position.y, Is.EqualTo(bridge.y).Within(.05f));
                }
                Physics.SyncTransforms();
                Assert.That(world.Navigation.CanTraverse(from, to), Is.True);
                Assert.That(world.Navigation.Path(from, to).Count, Is.EqualTo(1));
            }
            yield return null;
        }

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
