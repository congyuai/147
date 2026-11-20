using System.Collections;
using NUnit.Framework;
using UnityEngine;
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
                Assert.That(exit.Position.y, Is.EqualTo(0));
                Assert.That((exit.Position - PortLayout.Spawn).sqrMagnitude, Is.GreaterThan(120f * 120f));
            }
            Assert.That((PortLayout.Exits[0].Position - PortLayout.Exits[1].Position).sqrMagnitude,
                Is.GreaterThan(120f * 120f));
            yield return null;
        }
    }
}
