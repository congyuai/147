using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Dustline.Tests
{
    public sealed class WeaponSystemsV060Tests
    {
        ItemCatalog catalog;

        [SetUp]
        public void LoadCatalog()
        {
            catalog = Resources.Load<ItemCatalog>("Dustline/Items");
            Assert.That(catalog, Is.Not.Null, "Run Dustline > Prepare Project first.");
        }

        [Test]
        public void EachWeaponClassHasTwoDistinctReceiversAndValidMounts()
        {
            var groups = catalog.Items.Where(item => item.Weapon != null && !item.Weapon.Melee).GroupBy(item => item.Weapon.Form).ToArray();
            Assert.That(groups.Length, Is.EqualTo(7));
            foreach (var group in groups)
            {
                Assert.That(group.Count(), Is.GreaterThanOrEqualTo(2), group.Key.ToString());
                var introduced = group.Single(item => item.Weapon.AlternateFrame);
                var original = group.First(item => !item.Weapon.AlternateFrame);
                Assert.That(introduced.Weapon.Damage, Is.Not.EqualTo(original.Weapon.Damage), introduced.Id);
                foreach (var gun in group)
                {
                    Assert.That(WeaponRules.CanLoad(gun.Weapon, catalog.Find(gun.Weapon.AmmoId), out _), Is.True, gun.Id);
                    foreach (PartSlot slot in Enum.GetValues(typeof(PartSlot)))
                    {
                        if (WeaponRules.Socket(gun.Weapon, slot) == null) continue;
                        Assert.That(catalog.CompatibleParts(gun, slot), Is.Not.Empty, gun.Id + ": " + slot);
                    }
                }
            }
            var lantern = catalog.Find("dust_sniper_lantern");
            var cap = catalog.Find("part_muzzle_cap");
            Assert.That(WeaponRules.CanMount(lantern, PartSlot.Muzzle, cap, out _), Is.False);
            Assert.That(WeaponRules.CanMount(lantern, PartSlot.Magazine, catalog.Find("part_mag_n_lantern"), out _), Is.True);
        }

        [TestCase(AmmoFamily.Amber)]
        [TestCase(AmmoFamily.Copper)]
        [TestCase(AmmoFamily.Silver)]
        [TestCase(AmmoFamily.Slate)]
        public void EveryAmmoFamilyHasSixSelectablePenetrationScores(AmmoFamily family)
        {
            var ammo = catalog.Items.Where(item => item.Kind == ItemKind.Ammo && item.AmmoFamily == family).OrderBy(item => item.Tier).ToArray();
            Assert.That(ammo.Select(item => item.Tier), Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6 }));
            Assert.That(ammo.Select(WeaponRules.AmmoPenetration), Is.EqualTo(new[] { 10, 20, 30, 40, 50, 60 }));
            Assert.That(catalog.Items.Any(item => item.Weapon != null && ammo.All(round => WeaponRules.CanLoad(item.Weapon, round, out _))), Is.True);
        }

        [Test]
        public void OpticFactorsMatchPerspectiveProjectionAndUniqueIconKeys()
        {
            var gun = catalog.Find("rifle");
            var optics = catalog.CompatibleParts(gun, PartSlot.Sight).OrderBy(item => item.Part.Magnification).ToArray();
            Assert.That(optics.Select(item => item.Part.Magnification), Is.EqualTo(new[] { 1, 2, 3, 4, 6, 8, 12, 15 }));
            var item = new ItemStack { ItemId = gun.Id };
            var keys = new System.Collections.Generic.HashSet<int>();
            float previous = float.PositiveInfinity;
            foreach (var optic in optics)
            {
                item.Parts.Sight = new PartLink { ItemId = optic.Id, InstanceId = "test-optic" };
                var stats = WeaponTuning.Evaluate(gun.Weapon, item, catalog);
                double power = Math.Tan(75d * Math.PI / 360d) / Math.Tan(stats.AimFov * Math.PI / 360d);
                Assert.That(power, Is.EqualTo(optic.Part.Magnification).Within(.0001d), optic.Id);
                Assert.That(stats.Magnification, Is.EqualTo(optic.Part.Magnification).Within(.0001f));
                Assert.That(stats.AimFov, Is.LessThan(previous));
                Assert.That(keys.Add(WeaponRules.Look(item, catalog)), Is.True, optic.Id);
                previous = stats.AimFov;
            }
        }

        [Test]
        public void PelletAndSingleRayWeaponsShareTheStatsUsedForFiring()
        {
            var shotgun = catalog.Find("dust_shotgun_brine");
            var item = new ItemStack { ItemId = shotgun.Id };
            var plain = WeaponTuning.Evaluate(shotgun.Weapon, item, catalog);
            item.Parts.Grip = new PartLink { ItemId = "part_grip", InstanceId = "test-grip" };
            var grip = catalog.Find(item.Parts.Grip.ItemId);
            Assert.That(WeaponRules.CanMount(shotgun, PartSlot.Grip, grip, out _), Is.True);
            var fitted = WeaponTuning.Evaluate(shotgun.Weapon, item, catalog);
            Assert.That(plain.Spread(false), Is.EqualTo(shotgun.Weapon.HipSpread));
            Assert.That(plain.Spread(true), Is.EqualTo(shotgun.Weapon.AimSpread));
            Assert.That(fitted.Spread(false), Is.EqualTo(plain.Spread(false) * grip.Part.Stability).Within(.000001f));
            Assert.That(fitted.Spread(true), Is.EqualTo(plain.Spread(true) * grip.Part.Stability).Within(.000001f));
            Assert.That(WeaponTuning.Evaluate(catalog.Find("dust_shotgun").Weapon, default(WeaponMods)).Spread(false), Is.EqualTo(.035f));
            Assert.That(WeaponTuning.Evaluate(catalog.Find("rifle").Weapon, default(WeaponMods)).Spread(false), Is.EqualTo(.012f));
        }

        [Test]
        public void PenetrationDefeatsProtectionByTierWithoutIgnoringCoverageOrWear()
        {
            var method = typeof(BodyCondition).GetMethod("Protect", BindingFlags.Static | BindingFlags.NonPublic, null,
                new[] { typeof(BodyPart), typeof(float), typeof(ItemDefinition), typeof(ItemStack), typeof(int) }, null);
            Assert.That(method, Is.Not.Null);
            float Protect(BodyPart part, ItemDefinition rule, ItemStack worn, int power) => (float)method.Invoke(null, new object[] { part, 25f, rule, worn, power });

            var armor = ScriptableObject.CreateInstance<ItemDefinition>();
            try
            {
                armor.Kind = ItemKind.Armor;
                armor.Coverage = BodyCoverage.Chest;
                armor.DamageReduction = .5f;
                armor.Durability = 60;
                for (int tier = 1; tier <= 6; tier++)
                {
                    armor.Tier = tier;
                    int equivalent = WeaponRules.PenetrationForTier(tier);
                    var wear = new ItemStack();
                    float stopped = Protect(BodyPart.Chest, armor, wear, 0);
                    Assert.That(stopped, Is.EqualTo(12.5f));
                    Assert.That(wear.Wear, Is.Positive);
                    Assert.That(Protect(BodyPart.Chest, armor, wear, 0), Is.GreaterThan(stopped));
                    float even = Protect(BodyPart.Chest, armor, new ItemStack(), equivalent);
                    Assert.That(even, Is.GreaterThan(stopped).And.LessThan(25f));
                    if (tier < 6) Assert.That(Protect(BodyPart.Chest, armor, new ItemStack(), equivalent + 10), Is.GreaterThan(even));
                    if (tier > 1) Assert.That(Protect(BodyPart.Chest, armor, new ItemStack(), equivalent - 10), Is.LessThan(even));
                }
                var covered = new ItemStack();
                Assert.That(Protect(BodyPart.Head, armor, covered, 10), Is.EqualTo(25f));
                Assert.That(covered.Wear, Is.Zero);
                covered.Wear = armor.Durability;
                Assert.That(Protect(BodyPart.Chest, armor, covered, 10), Is.EqualTo(25f));
                Assert.That(covered.Wear, Is.EqualTo(armor.Durability));
            }
            finally { Object.DestroyImmediate(armor); }
        }
    }
}