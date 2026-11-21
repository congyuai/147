using NUnit.Framework;
using UnityEngine;

namespace Dustline
{
    public sealed class WeaponTuningTests
    {
        [TestCase(WeaponModSlot.Sight)]
        [TestCase(WeaponModSlot.Grip)]
        [TestCase(WeaponModSlot.Stock)]
        public void SelectionOnlyPurchasesTheNextLevelAndOwnedLevelsAreFree(WeaponModSlot slot)
        {
            var mods = new WeaponMods();
            Assert.That(WeaponTuning.TrySelect(mods, slot, 2, out WeaponMods rejected, out int rejectedCost), Is.False);
            Assert.That(rejected, Is.EqualTo(mods));
            Assert.That(rejectedCost, Is.Zero);
            Assert.That(WeaponTuning.TrySelect(mods, slot, 1, out WeaponMods first, out int firstCost), Is.True);
            Assert.That(WeaponTuning.Level(first, slot), Is.EqualTo(1));
            Assert.That(WeaponTuning.Owned(first, slot), Is.EqualTo(1));
            Assert.That(firstCost, Is.Positive);
            Assert.That(WeaponTuning.TrySelect(first, slot, 2, out WeaponMods second, out int secondCost), Is.True);
            Assert.That(WeaponTuning.Level(second, slot), Is.EqualTo(2));
            Assert.That(WeaponTuning.Owned(second, slot), Is.EqualTo(2));
            Assert.That(secondCost, Is.GreaterThan(firstCost));
            Assert.That(WeaponTuning.TrySelect(second, slot, 0, out WeaponMods unequipped, out int removeCost), Is.True);
            Assert.That(removeCost, Is.Zero);
            Assert.That(WeaponTuning.Level(unequipped, slot), Is.Zero);
            Assert.That(WeaponTuning.Owned(unequipped, slot), Is.EqualTo(2));
            Assert.That(WeaponTuning.TrySelect(unequipped, slot, 2, out WeaponMods restored, out int reselectCost), Is.True);
            Assert.That(reselectCost, Is.Zero);
            Assert.That(restored, Is.EqualTo(second));
            Assert.That(mods, Is.EqualTo(default(WeaponMods)));
            foreach (WeaponModSlot other in new[] { WeaponModSlot.Sight, WeaponModSlot.Grip, WeaponModSlot.Stock })
                if (other != slot) Assert.That(WeaponTuning.Owned(restored, other), Is.Zero);
        }

        [Test]
        public void InvalidSelectionsDoNotChargeOrChangeAnySlot()
        {
            var mods = new WeaponMods { OwnedStock = 2, Stock = 1 };
            AssertRejected(mods, WeaponModSlot.Stock, -1);
            AssertRejected(mods, WeaponModSlot.Stock, WeaponTuning.MaxUpgradeLevel + 1);
            AssertRejected(mods, (WeaponModSlot)99, 1);
            mods.Grip = 1;
            AssertRejected(mods, WeaponModSlot.Stock, 0);
        }

        static void AssertRejected(WeaponMods current, WeaponModSlot slot, int level)
        {
            Assert.That(WeaponTuning.TrySelect(current, slot, level, out var selected, out int cost), Is.False);
            Assert.That(selected, Is.EqualTo(current));
            Assert.That(cost, Is.Zero);
        }

        [Test]
        public void InstalledModsHaveDistinctTradeoffsWithoutChangingDamageOrFireRate()
        {
            var rifle = ScriptableObject.CreateInstance<WeaponDefinition>();
            try
            {
                rifle.Damage = 26f;
                rifle.FireInterval = 0.14f;
                var baseline = WeaponTuning.Evaluate(rifle, default);
                Assert.That(baseline.HipSpread, Is.EqualTo(0.012f));
                Assert.That(baseline.AimSpread, Is.EqualTo(0.002f));
                Assert.That(baseline.AimFov, Is.EqualTo(48f));
                Assert.That(baseline.AimSeconds, Is.Positive);
                var sight = WeaponTuning.Evaluate(rifle, new WeaponMods { Sight = 2, OwnedSight = 2 });
                Assert.That(sight.AimFov, Is.InRange(32f, 45f));
                Assert.That(sight.AimSpread, Is.LessThan(baseline.AimSpread));
                Assert.That(sight.AimSeconds, Is.GreaterThan(baseline.AimSeconds));
                Assert.That(sight.Range, Is.GreaterThan(baseline.Range));
                var grip = WeaponTuning.Evaluate(rifle, new WeaponMods { Grip = 2, OwnedGrip = 2 });
                Assert.That(grip.HipSpread, Is.LessThan(baseline.HipSpread));
                Assert.That(grip.ReloadSeconds, Is.GreaterThan(baseline.ReloadSeconds));
                var stock = WeaponTuning.Evaluate(rifle, new WeaponMods { Stock = 2, OwnedStock = 2 });
                Assert.That(stock.Recoil, Is.LessThan(baseline.Recoil));
                Assert.That(stock.HipSpread, Is.GreaterThan(baseline.HipSpread));
                Assert.That(stock.AimSeconds, Is.InRange(0.10f, baseline.AimSeconds));
                foreach (var stats in new[] { sight, grip, stock })
                {
                    Assert.That(stats.Damage, Is.EqualTo(rifle.Damage));
                    Assert.That(stats.FireInterval, Is.EqualTo(rifle.FireInterval));
                }
                var ownedOnly = new WeaponMods { OwnedSight = 2, OwnedGrip = 2, OwnedStock = 2 };
                Assert.That(WeaponTuning.Evaluate(rifle, ownedOnly), Is.EqualTo(baseline));
                rifle.Form = WeaponForm.Baton;
                rifle.Melee = true;
                var invalid = new WeaponMods { Sight = 2, OwnedSight = 2 };
                Assert.That(WeaponTuning.SupportsUpgrades(rifle), Is.False);
                Assert.That(WeaponTuning.Evaluate(rifle, invalid), Is.EqualTo(WeaponTuning.Evaluate(rifle, default)));
            }
            finally { Object.DestroyImmediate(rifle); }
        }
    }
}
