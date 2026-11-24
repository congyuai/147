using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Dustline.Tests
{
    public sealed class CombatWorldTests
    {
        [UnityTest]
        public IEnumerator DamageAppliesArmorAndDeathOnlyOnce()
        {
            var targetObject = new GameObject("Target");
            var enemyObject = new GameObject("Enemy");
            try
            {
                var target = targetObject.AddComponent<Health>();
                var enemy = enemyObject.AddComponent<Health>();
                target.Initialize(100, 0);
                enemy.Initialize(100, 1);
                target.ArmorReduction = () => 0.5f;
                int deaths = 0;
                target.Died += _ => deaths++;
                target.Damage(40, enemy);
                Assert.That(target.Current, Is.EqualTo(80));
                target.Heal(10);
                Assert.That(target.Current, Is.EqualTo(90));
                target.Damage(200, enemy);
                target.Damage(200, enemy);
                target.Heal(100);
                Assert.That(target.Alive, Is.False);
                Assert.That(deaths, Is.EqualTo(1));
            }
            finally
            {
                Object.Destroy(targetObject);
                Object.Destroy(enemyObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator AlliesDoNotDamageEachOther()
        {
            var first = new GameObject("Ally A");
            var second = new GameObject("Ally B");
            try
            {
                var a = first.AddComponent<Health>();
                var b = second.AddComponent<Health>();
                a.Initialize(100, 1);
                b.Initialize(100, 1);
                a.Damage(50, b);
                Assert.That(a.Current, Is.EqualTo(100));
            }
            finally { Object.Destroy(first); Object.Destroy(second); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator NavigationDoesNotCrossThinWallsBetweenWaypoints()
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Thin wall obstacle";
            wall.transform.position = new Vector3(1, 1, 0);
            wall.transform.localScale = new Vector3(0.3f, 2, 6);
            var waypoints = new[] {
                new Vector3(-2, 0, 0), new Vector3(-2, 0, -4), new Vector3(2, 0, -4),
                new Vector3(4, 0, -4), new Vector3(4, 0, 0),
                new Vector3(-2, 0, 4), new Vector3(2, 0, 4), new Vector3(4, 0, 4)
            };
            try
            {
                var navigation = new NavigationGrid(waypoints, 6f);
                var start = new Vector3(-2, 0, 0);
                var route = navigation.Path(start, new Vector3(4, 0, 0));
                Assert.That(route.Count, Is.GreaterThan(0));
                var previous = start;
                foreach (var point in route)
                {
                    Assert.That(Physics.Linecast(previous + Vector3.up, point + Vector3.up), Is.False,
                        "A returned waypoint segment crossed the thin wall.");
                    previous = point;
                }
            }
            finally
            {
                Object.Destroy(wall);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator PortLayoutFitsScaleAndSeparatesExtractionRoutes()
        {
            Assert.That(PortLayout.Size, Is.EqualTo(960f));
            Assert.That(PortLayout.Districts.Count, Is.GreaterThanOrEqualTo(5));
            Assert.That(PortLayout.Exits.Count, Is.EqualTo(2));
            Assert.That(PortLayout.Spawn.x, Is.InRange(-480f, 480f));
            Assert.That(PortLayout.Spawn.z, Is.InRange(-480f, 480f));
            for (int i = 0; i < PortLayout.Districts.Count; i++)
            {
                var district = PortLayout.Districts[i];
                Assert.That(string.IsNullOrEmpty(district.Name), Is.False);
                Assert.That(district.Center.x, Is.InRange(-480f, 480f));
                Assert.That(district.Center.z, Is.InRange(-480f, 480f));
                for (int j = i + 1; j < PortLayout.Districts.Count; j++)
                    Assert.That((district.Center - PortLayout.Districts[j].Center).sqrMagnitude, Is.GreaterThan(1));
            }
            foreach (var exit in PortLayout.Exits)
            {
                Assert.That(string.IsNullOrEmpty(exit.Name), Is.False);
                Assert.That(exit.Position.x, Is.InRange(-480f, 480f));
                Assert.That(exit.Position.z, Is.InRange(-480f, 480f));
                Assert.That(exit.Position.y, Is.GreaterThan(0));
                Assert.That((exit.Position - PortLayout.Spawn).sqrMagnitude, Is.GreaterThan(120f * 120f));
            }
            Assert.That((PortLayout.Exits[0].Position - PortLayout.Exits[1].Position).sqrMagnitude,
                Is.GreaterThan(120f * 120f));
            Assert.That(PortLayout.Exits[0].Position.x, Is.EqualTo(-322));
            Assert.That(PortLayout.Exits[0].Position.z, Is.EqualTo(315));
            Assert.That(PortLayout.Exits[1].Position.x, Is.EqualTo(330));
            Assert.That(PortLayout.Exits[1].Position.z, Is.EqualTo(165));
            var station = PortLayout.HighValueCenter;
            Assert.That(PortLayout.HighValueArea.Contains(new Vector2(station.x, station.z)), Is.True);
            Assert.That(PortLayout.HighValueName, Is.Not.Empty);
            Assert.That(PortLayout.MapColor(new Vector2(-334, -44)), Is.Not.EqualTo(PortLayout.MapColor(new Vector2(-279, -41))));
            Assert.That(PortLayout.MapColor(new Vector2(-334, -220)), Is.Not.EqualTo(PortLayout.MapColor(new Vector2(-305, -253))));
            yield return null;
        }

        [UnityTest]
        public IEnumerator PortRoutesReachOrdinaryLootBothExitsAndTheGuardedStation()
        {
            using (var built = new PortWorldFixture())
            {
                var world = built.World;
                var nav = world.Navigation;
                AssertRoute(nav, world.Spawn, PortWorldFixture.Floor(-261.2f, -217.9f));
                AssertRoute(nav, world.Spawn, world.Exits[0].transform.position);
                AssertRoute(nav, world.Spawn, world.Exits[1].transform.position);
                AssertRoute(nav, world.Spawn, PortLayout.HighValueCenter);
                Assert.That(nav.CanTraverse(PortWorldFixture.Floor(-334, -73), PortWorldFixture.Floor(-334, -15)), Is.True,
                    "The service road should continue over the west bridge.");
                Assert.That(nav.CanTraverse(PortWorldFixture.Floor(-149, -64), PortWorldFixture.Floor(-149, -5)), Is.False,
                    "The canal is not a shortcut across the uncovered channel.");
                Assert.That(PortWorldFixture.Floor(-452, 126).y - world.Spawn.y, Is.GreaterThan(20));
                foreach (var exit in world.Exits) Assert.That(world.FindExit(exit.transform.position), Is.SameAs(exit));
                AssertDetailedSurfaces(world);

                world.SpawnBots();
                int regular = 0, elite = 0, boss = 0;
                var bots = world.GetComponentsInChildren<BotController>();
                AssertFaction(bots);
                foreach (var bot in bots)
                {
                    Vector3 post = bot.transform.position;
                    bool inStation = PortLayout.HighValueArea.Contains(new Vector2(post.x, post.z));
                    Assert.That(inStation, Is.EqualTo(bot.Rank != BotRank.Regular));
                    if (bot.Rank == BotRank.Regular)
                    {
                        regular++;
                        Assert.That((world.Spawn - post).sqrMagnitude, Is.GreaterThan(95 * 95));
                    }
                    else
                    {
                        if (bot.Rank == BotRank.Boss) boss++; else elite++;
                        var destination = world.PickGuardPoint(post, new System.Random(7));
                        Assert.That(PortLayout.HighValueArea.Contains(new Vector2(destination.x, destination.z)), Is.True);
                    }
                }
                Assert.That(regular, Is.EqualTo(10));
                Assert.That(elite, Is.EqualTo(3));
                Assert.That(boss, Is.EqualTo(1));
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator QuarryRetainsFourteenRegularPostsWithTheSameFaction()
        {
            using (var built = new PortWorldFixture(quarry: true))
            {
                var world = built.World;
                world.SpawnBots();
                var bots = world.GetComponentsInChildren<BotController>();
                Assert.That(bots.Length, Is.EqualTo(14));
                Assert.That(bots.All(bot => bot.Rank == BotRank.Regular), Is.True);
                AssertFaction(bots);
                foreach (var exit in world.Exits)
                    Assert.That(world.Navigation.Path(world.Spawn, exit.transform.position), Is.Not.Empty);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator WornKitProtectsAndTransfersTheSameInstancesOnlyOnce()
        {
            using (var built = new PortWorldFixture())
            {
                var world = built.World;
                world.SpawnBots();
                var bot = world.GetComponentsInChildren<BotController>().Single(actor => actor.Rank == BotRank.Boss);
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var kit = (GridInventory)typeof(BotController).GetProperty("Equipment", flags).GetValue(bot);
                Assert.That(kit.Items.Count, Is.EqualTo(5));
                var body = bot.GetComponentInChildren<OperatorVisual>();
                foreach (var slot in new[] { EquipmentSlot.Helmet, EquipmentSlot.Armor, EquipmentSlot.Rig, EquipmentSlot.Pack })
                {
                    var item = kit.Equipped(slot);
                    string name = "Worn " + slot + " / " + item.ItemId;
                    var view = body.transform.Cast<Transform>().SingleOrDefault(child => child.name == name);
                    Assert.That(view, Is.Not.Null, name);
                    Assert.That(view.GetComponent<SkinnedMeshRenderer>().sharedMesh.vertexCount, Is.GreaterThan(64));
                }
                var weapon = kit.Equipped(EquipmentSlot.Weapon);
                Assert.That(body.WeaponMount.GetComponentInChildren<WeaponVisual>().Form, Is.EqualTo(kit.Catalog.Find(weapon.ItemId).Weapon.Form));
                Assert.That(weapon.LoadedAmmo, Is.EqualTo(20));
                weapon.LoadedAmmo = 7;
                built.ForceLargeSupplyRoll();
                var armor = kit.Equipped(EquipmentSlot.Armor);
                var health = bot.GetComponent<Health>();
                int initialWear = armor.Wear;
                health.Damage(14f, null);
                Assert.That(armor.Wear, Is.GreaterThan(initialWear));
                Assert.That(health.Current, Is.GreaterThan(health.Maximum - 14f));
                health.Damage(999f, null);
                health.Damage(999f, null);
                Assert.That(armor.Wear, Is.EqualTo(kit.Catalog.Find(armor.ItemId).Durability));
                Assert.That(kit.Items, Is.Empty);
                Assert.That(bot.GetComponent<CharacterController>().enabled, Is.False);
                Assert.That(body.WeaponMount.GetComponentInChildren<WeaponVisual>(), Is.Null);
                Assert.That(body.GetComponentsInChildren<SkinnedMeshRenderer>().Length, Is.EqualTo(1));
                var drop = world.GetComponentsInChildren<LootContainer>().Single(box => box.DisplayName == "首领物资");
                Assert.That(drop.Inventory.Find(armor.InstanceId).Wear, Is.EqualTo(armor.Wear));
                Assert.That(drop.Inventory.Find(weapon.InstanceId).LoadedAmmo, Is.EqualTo(7));
                Assert.That(drop.Inventory.Find(weapon.InstanceId).LoadedAmmoId, Is.EqualTo(weapon.LoadedAmmoId));
                Assert.That(drop.Inventory.Find(weapon.InstanceId).Slot, Is.EqualTo(EquipmentSlot.None));
                var supplies = world.GetComponentsInChildren<LootContainer>().Single(box => box.DisplayName == "首领物资 · 补给");
                Assert.That(supplies.Inventory.Count("test-large-supply"), Is.EqualTo(1));
            }
            yield return null;
        }

        static void AssertFaction(BotController[] bots)
        {
            Assert.That(bots, Is.Not.Empty);
            var actors = bots.Select(bot => bot.GetComponent<Health>()).ToArray();
            Assert.That(actors.Select(actor => actor.Team).Distinct().ToArray(), Is.EqualTo(new[] { 1 }));
            var uniforms = bots.Select(bot => bot.GetComponentInChildren<OperatorVisual>().GetComponent<SkinnedMeshRenderer>().sharedMaterial);
            Assert.That(uniforms.Distinct().Count(), Is.EqualTo(1));
            var canEngage = typeof(BotController).GetMethod("CanEngage", BindingFlags.Instance | BindingFlags.NonPublic);
            for (int i = 0; i < bots.Length; i++)
                for (int j = 0; j < actors.Length; j++)
                    Assert.That(canEngage.Invoke(bots[i], new object[] { actors[j] }), Is.EqualTo(false));
            float remaining = actors[0].Current;
            actors[0].Damage(13f, actors[actors.Length - 1]);
            Assert.That(actors[0].Current, Is.EqualTo(remaining));
        }

        [UnityTest]
        public IEnumerator LocalRoutingYieldsToAnActorOnThePlayerLayerWithoutLosingTheGlobalRoute()
        {
            var other = new GameObject("Local test actor", typeof(CharacterController));
            try
            {
                var start = new Vector3(1000, 0, 1000);
                var end = start + Vector3.forward * 6;
                var navigation = new NavigationGrid(new[] { start, end }, 8);
                other.layer = 2;
                other.transform.position = start + Vector3.forward * 1.1f;
                var motor = other.GetComponent<CharacterController>();
                motor.radius = .35f; motor.height = 1.8f; motor.center = Vector3.up * .9f;
                Physics.SyncTransforms();
                yield return new WaitForFixedUpdate();
                Assert.That(navigation.Path(start, end).Count, Is.EqualTo(1));
                if (navigation.TryLocalStep(start, end, null, out var route))
                    Assert.That(Mathf.Abs(route.x), Is.GreaterThan(.3f), "The actor should not push straight into the blocking capsule.");
                else Assert.That(route, Is.EqualTo(Vector3.zero));
                motor.enabled = false;
                Assert.That(navigation.TryLocalStep(start, end, null, out route), Is.True);
                Assert.That(route, Is.EqualTo(Vector3.forward));
            }
            finally { Object.Destroy(other); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator AShutterUpdatesCollisionAndRoutingAndDoesNotCloseOnThePlayerLayer()
        {
            using (var built = new PortWorldFixture())
            {
                var world = built.World;
                HarborDoor shutter = null;
                foreach (var door in world.GetComponentsInChildren<HarborDoor>())
                    if (Vector2.Distance(new Vector2(door.transform.position.x, door.transform.position.z), new Vector2(255, 105)) < .2f)
                        shutter = door;
                Assert.That(shutter, Is.Not.Null);
                Vector3 from = PortWorldFixture.Floor(255, 102), to = PortWorldFixture.Floor(255, 110);
                Assert.That(shutter.IsOpen, Is.False);
                Assert.That(world.Navigation.CanTraverse(from, to), Is.False);
                AssertRoute(world.Navigation, from, to);
                Assert.That(shutter.TryToggle(), Is.True);
                Assert.That(world.Navigation.CanTraverse(from, to), Is.True);
                Assert.That(world.Navigation.Path(from, to).Count, Is.EqualTo(1));

                var blocker = new GameObject("Player in shutter opening", typeof(CharacterController));
                blocker.transform.SetParent(world.transform, false);
                blocker.layer = 2;
                var motor = blocker.GetComponent<CharacterController>();
                motor.height = 1.8f; motor.radius = .35f; motor.center = Vector3.up * .9f;
                blocker.transform.position = shutter.transform.position;
                Physics.SyncTransforms();
                yield return new WaitForFixedUpdate();
                Assert.That(shutter.TryToggle(), Is.False);
                Assert.That(shutter.IsOpen, Is.True);
                motor.enabled = false;

                var bag = world.CreateLoot(PortWorldFixture.Floor(255, 106.3f), "Shutter sightline");
                Physics.SyncTransforms();
                Assert.That(Physics.Raycast(new Vector3(from.x, from.y + .18f, 104), Vector3.forward, out var hit, 3.2f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide), Is.True);
                Assert.That(hit.collider.GetComponentInParent<LootContainer>(), Is.SameAs(bag));
                Assert.That(shutter.TryToggle(), Is.True);
                Assert.That(world.Navigation.CanTraverse(from, to), Is.False);
                AssertRoute(world.Navigation, from, to);
            }
            yield return null;
        }

        static void AssertDetailedSurfaces(WorldBuilder world)
        {
            MeshFilter ground = null, bridge = null;
            foreach (var part in world.GetComponentsInChildren<MeshFilter>())
            {
                if (part.name.StartsWith("Grade ", System.StringComparison.Ordinal)) ground = part;
                if (part.name == "Structures" && part.transform.parent.name.StartsWith("Service bridge ", System.StringComparison.Ordinal)) bridge = part;
            }
            Assert.That(ground, Is.Not.Null);
            Assert.That(bridge, Is.Not.Null);
            foreach (var part in new[] { ground, bridge })
            {
                var material = part.GetComponent<MeshRenderer>().sharedMaterial;
                Assert.That(material.shader.name, Is.EqualTo(PortRaster.SurfaceShader));
                Assert.That(part.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord1), Is.True);
                Assert.That(part.sharedMesh.HasVertexAttribute(VertexAttribute.Tangent), Is.True);
                foreach (var finish in new[] { "Asphalt", "Concrete", "Steel", "Aggregate" })
                    foreach (var channel in new[] { "Albedo", "Normal" })
                    {
                        var texture = material.GetTexture("_" + finish + channel);
                        Assert.That(texture, Is.Not.Null, finish + channel);
                        Assert.That(texture.width, Is.EqualTo(2048));
                        Assert.That(texture.height, Is.EqualTo(2048));
                    }
            }
            Assert.That(ground.GetComponent<MeshRenderer>().sharedMaterial.mainTexture,
                Is.SameAs(Resources.Load<Texture2D>(PortRaster.Ground)));
        }

        static void AssertRoute(NavigationGrid nav, Vector3 start, Vector3 finish)
        {
            var route = nav.Path(start, finish);
            Assert.That(route.Count, Is.GreaterThan(0), $"No route from {start} to {finish}");
            Assert.That(Vector3.Distance(route[route.Count - 1], finish), Is.LessThan(.18f));
            var previous = start;
            foreach (var point in route)
            {
                Assert.That(nav.CanTraverse(previous, point), Is.True, $"Blocked route leg {previous} -> {point}");
                Assert.That(PortWorldFixture.Floor(point.x, point.z).y, Is.EqualTo(point.y).Within(.075f));
                previous = point;
            }
        }
    }

    internal sealed class PortWorldFixture : System.IDisposable
    {
        internal const string HighItem = "test-station-item";
        readonly GameObject owner = new GameObject("Inactive raid fixture");
        readonly GameObject world = new GameObject("Port fixture");
        readonly ItemCatalog catalog = ScriptableObject.CreateInstance<ItemCatalog>();
        readonly LootTable regular = ScriptableObject.CreateInstance<LootTable>();
        readonly LootTable high = ScriptableObject.CreateInstance<LootTable>();
        internal WorldBuilder World { get; }

        internal PortWorldFixture(bool quarry = false)
        {
            try
            {
                owner.SetActive(false);
                var session = owner.AddComponent<GameSession>();
                AddItem("ammo", ItemKind.Ammo);
                AddItem("medkit", ItemKind.Medicine);
                AddItem("bandage", ItemKind.Medicine);
                AddItem("armor", ItemKind.Armor);
                var rifle = AddItem("rifle", ItemKind.Weapon);
                rifle.Width = 3;
                rifle.Height = 2;
                rifle.Weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
                rifle.Weapon.AmmoId = "ammo";
                for (int i = 1; i <= 5; i += 2)
                {
                    AddItem("helmet_" + i, ItemKind.Helmet);
                    AddItem("vest_" + i, ItemKind.Armor);
                    AddItem("pack_" + i, ItemKind.Pack);
                    AddItem("rig_" + i, ItemKind.Rig);
                }
                AddItem("circuit", ItemKind.Valuable);
                AddItem(HighItem, ItemKind.Valuable);
                catalog.RebuildIndex();
                regular.Rolls = high.Rolls = 1;
                regular.Entries = Entries("ammo", "medkit", "bandage", "armor", "circuit");
                high.Entries = Entries(HighItem);
                World = world.AddComponent<WorldBuilder>();
                Assign(session, "Catalog", catalog);
                Assign(session, "LootTable", regular);
                Assign(session, "HighValueLootTable", high);
                Assign(session, "World", World);
                if (quarry)
                    typeof(WorldBuilder).GetMethod("Build", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(World, new object[] { session, true });
                else World.Build(session);
            }
            catch { Dispose(); throw; }
        }

        internal void ForceLargeSupplyRoll()
        {
            var supply = AddItem("test-large-supply", ItemKind.Valuable);
            supply.Width = 3;
            supply.Height = 4;
            catalog.RebuildIndex();
            regular.Entries = Entries(supply.Id);
        }

        internal static Vector3 Floor(float x, float z)
        {
            Vector3 p = new Vector3(x, 190, z);
            var hits = Physics.RaycastAll(p, Vector3.down, 220, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float y = float.NegativeInfinity;
            foreach (var hit in hits)
                if (hit.collider.TryGetComponent<FootstepSurface>(out _) && hit.collider.GetComponent<LootContainer>() == null)
                    y = Mathf.Max(y, hit.point.y);
            Assert.That(float.IsNegativeInfinity(y), Is.False, $"Missing grade at ({x}, {z})");
            return new Vector3(x, y, z);
        }

        ItemDefinition AddItem(string id, ItemKind kind)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.Id = id;
            item.Kind = kind;
            item.MaxStack = item.Equipment == EquipmentSlot.None ? 99 : 1;
            if (item.Equipment != EquipmentSlot.None) item.Width = item.Height = 2;
            if (kind == ItemKind.Armor || kind == ItemKind.Pack) item.Height = 3;
            if (kind == ItemKind.Pack || kind == ItemKind.Rig)
                item.Storage = new StorageSpec { Columns = 4, Rows = 3 };
            if (kind == ItemKind.Armor || kind == ItemKind.Helmet)
            {
                item.Coverage = kind == ItemKind.Armor ? BodyCoverage.Chest : BodyCoverage.Head;
                item.Durability = 60;
                item.Tier = 2;
            }
            catalog.Items.Add(item);
            return item;
        }

        static List<LootEntry> Entries(params string[] ids)
        {
            var entries = new List<LootEntry>(ids.Length);
            foreach (string id in ids) entries.Add(new LootEntry { ItemId = id, Weight = 1, Min = 1, Max = 1 });
            return entries;
        }

        static void Assign(GameSession session, string name, object value) => typeof(GameSession).GetProperty(name)
            .GetSetMethod(true).Invoke(session, new[] { value });

        public void Dispose()
        {
            world.SetActive(false);
            Object.Destroy(world);
            Object.Destroy(owner);
            foreach (var item in catalog.Items)
            {
                if (item.Weapon != null) Object.Destroy(item.Weapon);
                Object.Destroy(item);
            }
            Object.Destroy(catalog);
            Object.Destroy(regular);
            Object.Destroy(high);
        }
    }
}
