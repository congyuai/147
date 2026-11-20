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
        public IEnumerator NavigationDoesNotCrossThinWallsBetweenGridCells()
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = new Vector3(1, 1, 0);
            wall.transform.localScale = new Vector3(0.3f, 2, 6);
            try
            {
                var navigation = new NavigationGrid();
                var route = navigation.Path(new Vector3(-2, 0, 0), new Vector3(4, 0, 0));
                Assert.That(route.Count, Is.GreaterThan(0));
                for (int i = 1; i < route.Count; i++)
                    Assert.That(Physics.Linecast(route[i - 1] + Vector3.up, route[i] + Vector3.up), Is.False);
            }
            finally { Object.Destroy(wall); }
            yield return null;
        }
    }
}
