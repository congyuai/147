using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

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
                LootContainer starter = null, starterAmmo = null;
                int high = 0, medicine = 0;
                foreach (var loot in world.GetComponentsInChildren<LootContainer>())
                {
                    Vector3 p = loot.transform.position;
                    Assert.That(loot.GetComponent<BoxCollider>().isTrigger, Is.False);
                    Assert.That(loot.Inventory.Items, Is.Not.Empty, loot.DisplayName);
                    foreach (var item in loot.Inventory.Items)
                        Assert.That(loot.Accepts(loot.Inventory.Catalog.Find(item.ItemId)), Is.True, loot.DisplayName + ": " + item.ItemId);
                    if (loot.Kind == LootKind.MedicalCase)
                    {
                        medicine++;
                        foreach (var item in loot.Inventory.Items)
                            Assert.That(loot.Inventory.Catalog.Find(item.ItemId).Kind, Is.EqualTo(ItemKind.Medicine), loot.DisplayName);
                    }
                    if (PortLayout.HighValueArea.Contains(new Vector2(p.x, p.z)))
                    {
                        high++;
                        Assert.That(loot.Inventory.Items.Count, Is.EqualTo(1));
                        Assert.That(loot.Inventory.Count(PortWorldFixture.HighItem), Is.EqualTo(1));
                    }
                    else Assert.That(loot.Inventory.Count(PortWorldFixture.HighItem), Is.Zero);
                    if (loot.DisplayName == "起点医疗箱") starter = loot;
                    if (loot.DisplayName == "起点弹药箱") starterAmmo = loot;
                }
                Assert.That(high, Is.EqualTo(4));
                Assert.That(medicine, Is.GreaterThan(1));
                Assert.That(starter, Is.Not.Null);
                Assert.That(starterAmmo, Is.Not.Null);
                Assert.That(starter.Inventory.Count("medkit"), Is.EqualTo(1));
                Assert.That(starter.Inventory.Count("bandage"), Is.EqualTo(2));
                Assert.That(starter.Inventory.Count("ammo"), Is.Zero);
                Assert.That(starterAmmo.Inventory.Count("ammo"), Is.EqualTo(30));
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
                yield return new WaitForFixedUpdate();
                Assert.That((motor.Move(Vector3.forward * 3) & CollisionFlags.Sides) != 0, Is.True);
                Assert.That(actor.transform.position.z, Is.LessThan(starter.transform.position.z - .3f));
            }
            yield return null;
        }

        [TestCase(LootKind.MedicalCase)]
        [TestCase(LootKind.SupplyCrate)]
        [TestCase(LootKind.Toolbox)]
        [TestCase(LootKind.StorageLocker)]
        [TestCase(LootKind.EquipmentCase)]
        [TestCase(LootKind.FieldBag)]
        public void WeightedDropsAndDepositsRespectTheContainerKind(LootKind kind)
        {
            var catalog = Resources.Load<ItemCatalog>("Dustline/Items");
            var table = Resources.Load<LootTable>("Dustline/Loot");
            Assert.That(catalog, Is.Not.Null, "Run Dustline > Prepare Project first.");
            Assert.That(table, Is.Not.Null);
            var crate = new GameObject("Typed container");
            try
            {
                var container = crate.AddComponent<LootContainer>();
                container.Initialize("Typed container", catalog, kind: kind);
                var bag = container.Inventory;
                var seen = new HashSet<string>();
                for (int seed = 0; seed < 80; seed++)
                {
                    table.FillContainer(container, new System.Random(seed), 2);
                    Assert.That(bag.Items, Is.Not.Empty);
                    foreach (var item in bag.Items)
                    {
                        Assert.That(IsCompatible(kind, catalog.Find(item.ItemId).Kind), Is.True, item.ItemId);
                        seen.Add(item.ItemId);
                    }
                    bag.Restore(new List<ItemStack>());
                }
                if (kind == LootKind.MedicalCase)
                {
                    Assert.That(seen, Is.EquivalentTo(new[] { "medkit", "bandage", "trauma_case", "tourniquet", "splint", "pain_tabs", "surgery" }));
                    Assert.That(bag.TryAdd("ammo", 1, out _), Is.False);
                    var source = new GridInventory(2, 2, 20, catalog);
                    Assert.That(source.TryAdd("ammo", 1, out var error), Is.True, error);
                    Assert.That(source.TryTransferTo(source.Items[0].InstanceId, bag, out _), Is.False);
                    Assert.That(source.Count("ammo"), Is.EqualTo(1));
                    Assert.That(bag.Items, Is.Empty);
                }
            }
            finally { Object.DestroyImmediate(crate); }
        }

        [UnityTest]
        public IEnumerator LootScreenNamesTheContainerAndShowsOnlyCarriedWeight()
        {
            var owner = new GameObject("Loot UI fixture");
            var box = new GameObject("Loot UI container");
            var uiNode = new GameObject("Loot UI");
            try
            {
                owner.SetActive(false);
                var session = owner.AddComponent<GameSession>();
                var ui = uiNode.AddComponent<GameUI>();
                var input = owner.AddComponent<PlayerInputReader>();
                var catalog = Resources.Load<ItemCatalog>("Dustline/Items");
                Assert.That(catalog, Is.Not.Null, "Run Dustline > Prepare Project first.");
                var loadout = GridInventory.Carried(catalog);
                Assert.That(loadout.TryAdd("bandage", 2, out _), Is.True);
                Assign("Phase", SessionPhase.Raid);
                Assign("Loadout", loadout);
                Assign("Catalog", catalog);
                Assign("Input", input);
                Assign("UI", ui);
                ui.Initialize(session);
                var loot = box.AddComponent<LootContainer>();
                loot.Initialize("靶场测试医疗箱", catalog, kind: LootKind.MedicalCase);
                Assert.That(loot.Inventory.TryAdd("medkit", 1, out _), Is.True);
                Assert.That(loot.Inventory.TryMove(loot.Inventory.Items[0].InstanceId, 0, 0, true, out _), Is.True);
                loot.Reveal();
                typeof(GameUI).GetMethod("ShowFieldInventory", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ui, new object[] { loot });

                var panel = ui.transform.Find("Dustline UI/Safe area/Pages/Field inventory");
                Assert.That(panel, Is.Not.Null);
                int weightLabels = 0;
                bool named = false;
                foreach (var label in panel.GetComponentsInChildren<Text>())
                {
                    if (label.transform.parent == panel && label.text == loot.DisplayName) named = true;
                    if (label.text.StartsWith("负重", System.StringComparison.Ordinal)) weightLabels++;
                }
                Assert.That(named, Is.True);
                Assert.That(weightLabels, Is.EqualTo(1));
                var glyphs = new HashSet<string>();
                RawImage rotated = null;
                foreach (var image in panel.GetComponentsInChildren<RawImage>())
                {
                    if (image.gameObject.name.StartsWith("Item model / ", System.StringComparison.Ordinal)) glyphs.Add(image.gameObject.name);
                    if (image.gameObject.name == "Item model / medkit") rotated = image;
                }
                Assert.That(glyphs, Is.EquivalentTo(new[] { "Item model / medkit", "Item model / bandage" }));
                Assert.That(rotated, Is.Not.Null);
                var icon = rotated.rectTransform;
                var tile = (RectTransform)icon.parent;
                Assert.That(tile.rect.width, Is.GreaterThan(tile.rect.height));
                Assert.That(Quaternion.Angle(icon.localRotation, Quaternion.Euler(0, 0, -90f)), Is.LessThan(.01f));
                Assert.That(icon.rect.width / icon.rect.height, Is.EqualTo(.5f).Within(.001f));
                Assert.That(icon.rect.width, Is.LessThan(tile.rect.height));
                Assert.That(icon.rect.height, Is.LessThan(tile.rect.width));
                Assert.That(icon.anchoredPosition.x, Is.EqualTo(tile.rect.width * .5f).Within(.001f));
                Assert.That(icon.anchoredPosition.y, Is.EqualTo(-tile.rect.height * .5f).Within(.001f));

                void Assign(string name, object value) => typeof(GameSession).GetProperty(name).GetSetMethod(true).Invoke(session, new[] { value });
            }
            finally
            {
                Object.Destroy(uiNode);
                Object.Destroy(box);
                Object.Destroy(owner);
            }
            yield return null;
        }

        static bool IsCompatible(LootKind kind, ItemKind item) => kind switch
        {
            LootKind.MedicalCase => item == ItemKind.Medicine,
            LootKind.SupplyCrate => item == ItemKind.Weapon || item == ItemKind.Ammo,
            LootKind.StorageLocker => item == ItemKind.Armor || item == ItemKind.Helmet || item == ItemKind.Rig || item == ItemKind.Pack || item == ItemKind.Valuable,
            LootKind.Toolbox or LootKind.EquipmentCase => item == ItemKind.Valuable || item == ItemKind.Part,
            _ => true
        };

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
