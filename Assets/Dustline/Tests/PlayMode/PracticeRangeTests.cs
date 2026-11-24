using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Dustline.Tests
{
    public sealed class PracticeRangeTests
    {
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        GameObject host;
        ProfileStore store;
        GameSession session;
        byte[] originalBytes;
        int previousTarget, previousVSync;
        RenderPipelineAsset previousPipeline;

        [SetUp]
        public void CaptureSettings()
        {
            previousTarget = Application.targetFrameRate;
            previousVSync = QualitySettings.vSyncCount;
            previousPipeline = QualitySettings.renderPipeline;
        }

        [UnityTearDown]
        public IEnumerator CleanUp()
        {
            if (host != null)
            {
                host.SetActive(false);
                Object.Destroy(host);
            }
            if (store != null)
            {
                File.Delete(store.Path);
                File.Delete(store.LegacyBackupPath);
                File.Delete(store.UpgradeBackupPath);
                File.Delete(store.LoadoutUpgradeBackupPath);
                File.Delete(store.EconomyUpgradeBackupPath);
                File.Delete(store.CakeUpgradeBackupPath);
            }
            yield return null;
            QualitySettings.renderPipeline = previousPipeline;
            QualitySettings.vSyncCount = previousVSync;
            Application.targetFrameRate = previousTarget;
        }

        [UnityTest]
        public IEnumerator PracticeOnlyChangesTemporaryGearDuringUseRefillDeathAndExit()
        {
            var savedFog = RenderSettings.fog;
            var savedSun = RenderSettings.sun;
            yield return OpenPractice();
            var raidBag = session.Loadout;
            var savedRifle = raidBag.Equipped(EquipmentSlot.Weapon);
            Assert.That(savedRifle.LoadedAmmo, Is.EqualTo(3));
            var training = session.ActiveFieldBag;
            var player = session.Player;
            Assert.That(player.Weapon.Magazine.Rounds, Is.EqualTo(player.Weapon.Magazine.Capacity));
            Assert.That(player.Weapon.HasInfiniteReserve, Is.True);
            Assert.That(player.Weapon.Reserve, Is.EqualTo(int.MaxValue));
            var range = player.transform.parent;
            Assert.That(training, Is.Not.SameAs(raidBag));
            var trainingRifle = training.Equipped(EquipmentSlot.Weapon);
            Assert.That(trainingRifle.InstanceId, Is.Not.EqualTo(savedRifle.InstanceId));
            Assert.That(trainingRifle.Parts.Stock.InstanceId, Is.Not.EqualTo(savedRifle.Parts.Stock.InstanceId));
            int look = WeaponRules.Look(savedRifle, session.Catalog);
            Assert.That(look, Is.Not.Zero);
            Assert.That(player.WeaponView.AppearanceKey, Is.EqualTo(look));
            Assert.That(player.Weapon.Magazine.Capacity, Is.EqualTo(30));
            Assert.That(player.Weapon.Stats.ReloadSeconds, Is.EqualTo(WeaponTuning.Evaluate(player.Weapon.Definition, savedRifle, session.Catalog).ReloadSeconds));
            Assert.That(session.World, Is.Null);
            Assert.That(range.GetComponentsInChildren<BotController>(), Is.Empty);
            Assert.That(range.GetComponentsInChildren<ExtractionZone>(), Is.Empty);
            Assert.That(range.GetComponentsInChildren<LootContainer>(), Is.Empty);
            var plate = range.Find("Practice plate").GetComponent<Health>();
            Assert.That(plate, Is.Not.Null);

            // Keep the simulated input and ticks in one frame so no real timing or hardware is required.
            Set(session.Input, "Fire", true);
            Set(session.Input, "FirePressed", true);
            Invoke(player.Weapon, "Tick", Time.time + .5f, .02f);
            int afterShot = training.Equipped(EquipmentSlot.Weapon).LoadedAmmo;
            Assert.That(afterShot, Is.EqualTo(player.Weapon.Magazine.Capacity - 1));
            Set(session.Input, "Fire", false);
            Set(session.Input, "FirePressed", false);
            int reserve = training.Count("ammo");
            Assert.That(reserve, Is.Zero);
            Set(session.Input, "ReloadPressed", true);
            Invoke(player.Weapon, "Tick", Time.time + .8f, .02f);
            Set(session.Input, "ReloadPressed", false);
            Assert.That(player.Weapon.Magazine.Reloading, Is.True);
            Invoke(player.Weapon, "Tick", Time.time + 4f, 4f);
            Assert.That(player.Weapon.Magazine.Reloading, Is.False);
            Assert.That(player.Weapon.Magazine.Rounds, Is.EqualTo(player.Weapon.Magazine.Capacity));
            Assert.That(player.Weapon.Reserve, Is.EqualTo(int.MaxValue));
            Assert.That(training.Count("ammo"), Is.EqualTo(reserve));
            Assert.That(savedRifle.LoadedAmmo, Is.EqualTo(3));
            plate.Damage(plate.Maximum, player.Health);
            Assert.That(plate.gameObject.activeSelf, Is.False);
            var practice = session.GetType().GetProperty("Practice", Instance).GetValue(session);
            Assert.That(practice.GetType().GetProperty("Shots").GetValue(practice), Is.EqualTo(1));
            Assert.That(practice.GetType().GetProperty("Plates").GetValue(practice), Is.EqualTo(1));
            Invoke(practice, "ResetPlates");
            Assert.That(plate.Alive && plate.gameObject.activeSelf, Is.True);

            Invoke(session, "PracticeHealthSample");
            float beforeBandage = player.Health.Current;
            Assert.That(player.Health.Body.Injuries(BodyPart.LeftArm) & Injury.LightBleed, Is.EqualTo(Injury.LightBleed));
            var used = (ItemStack)Invoke(session, "QuickMedicine", new object[] { "bandage" });
            Assert.That(used, Is.Not.Null);
            session.UseMedicine(used.InstanceId);
            Assert.That(player.IsTreating, Is.True);
            Invoke(player, "TickTreatment", 4f);
            Assert.That(player.Health.Current, Is.EqualTo(beforeBandage).Within(.001f));
            Assert.That(player.Health.Body.Injuries(BodyPart.LeftArm) & Injury.LightBleed, Is.EqualTo(Injury.None));
            Assert.That(training.Count("bandage"), Is.EqualTo(4));
            Assert.That(raidBag.Count("bandage"), Is.Zero);

            Invoke(session, "RefillPractice");
            Assert.That(session.ActiveFieldBag, Is.Not.SameAs(training));
            Assert.That(session.ActiveFieldBag.Count("bandage"), Is.EqualTo(5));
            Assert.That(player.WeaponView.AppearanceKey, Is.EqualTo(look));
            Assert.That(player.Weapon.Magazine.Capacity, Is.EqualTo(30));
            Assert.That(player.Weapon.Magazine.Rounds, Is.EqualTo(player.Weapon.Magazine.Capacity));
            Assert.That(player.Weapon.Reserve, Is.EqualTo(int.MaxValue));
            session.UseMedicine();
            Assert.That(player.IsTreating, Is.True);
            Invoke(session, "OnApplicationPause", true);
            Invoke(session, "OnApplicationPause", false);
            Assert.That(player.IsTreating, Is.False);
            Assert.That(session.ActiveFieldBag.RemainingUses("medkit"), Is.EqualTo(6));
            session.UseMedicine();
            Assert.That(player.IsTreating, Is.True);
            player.Health.Damage(1f, null);
            Assert.That(player.IsTreating, Is.False);
            Assert.That(session.ActiveFieldBag.RemainingUses("medkit"), Is.EqualTo(6));
            player.Health.Damage(200f, null);
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.Practice));
            Assert.That(player.Health.Current, Is.EqualTo(100f));

            session.EndRaid(true, "This must not produce a raid result.");
            session.PersistSafeState();
            Assert.That(session.Profile.Raids, Is.EqualTo(4));
            Assert.That(session.Profile.Extractions, Is.EqualTo(2));
            Assert.That(session.Profile.Credits, Is.EqualTo(777));
            Assert.That(session.Profile.RaidInProgress, Is.False);
            Assert.That(session.Kills, Is.Zero);
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(store.Path));
            session.LeavePractice();
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.Menu));
            Assert.That(session.Player, Is.Null);
            Assert.That(range.gameObject.activeSelf, Is.False);
            Assert.That(session.ActiveFieldBag, Is.SameAs(raidBag));
            Assert.That(raidBag.Equipped(EquipmentSlot.Weapon), Is.SameAs(savedRifle));
            Assert.That(savedRifle.LoadedAmmo, Is.EqualTo(3));
            Assert.That(raidBag.Count("ammo"), Is.EqualTo(18));
            Assert.That(RenderSettings.fog, Is.EqualTo(savedFog));
            Assert.That(RenderSettings.sun, Is.SameAs(savedSun));
            session.Sell(training, training.Equipped(EquipmentSlot.Weapon).InstanceId);
            Assert.That(session.Profile.Credits, Is.EqualTo(777));
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(store.Path));
            Assert.That(File.Exists(store.LegacyBackupPath), Is.False);
            Assert.That(File.Exists(store.UpgradeBackupPath), Is.False);
            Assert.That(File.Exists(store.EconomyUpgradeBackupPath), Is.False);
            Assert.That(File.Exists(store.CakeUpgradeBackupPath), Is.False);
        }

        [UnityTest]
        public IEnumerator LongPressSelectsWithoutUseAndCancelledOrForeignTouchesCannotUseMedicine()
        {
            yield return OpenPractice();
            Invoke(session, "PracticeHealthSample");
            var ui = session.UI;
            var input = session.Input;
            var events = ui.GetComponentInChildren<EventSystem>();
            var touch = ui.transform.Find("Dustline UI/Safe area/Touch controls");
            var heal = touch.Find("Heal").GetComponent<TouchControl>();
            var look = touch.Find("Look pad").GetComponent<TouchControl>();
            Canvas.ForceUpdateCanvases();
            var centre = RectTransformUtility.WorldToScreenPoint(null, heal.transform.position);
            var owner = new PointerEventData(events) { pointerId = 1, position = centre };
            var foreign = new PointerEventData(events) { pointerId = 2, position = centre };
            var otherButton = new PointerEventData(events) { pointerId = 1, position = centre, button = PointerEventData.InputButton.Right };
            var bag = session.ActiveFieldBag;
            var player = session.Player;

            heal.OnPointerDown(owner);
            heal.OnPointerUp(foreign);
            heal.OnPointerUp(otherButton);
            owner.position = centre + Vector2.one * Mathf.Max(Screen.width, Screen.height);
            heal.OnPointerUp(owner);
            Invoke(input, "Update");
            Assert.That(input.HealPressed, Is.False);
            Assert.That(player.IsTreating, Is.False);

            owner.position = centre;
            heal.OnPointerDown(owner);
            look.OnPointerDown(foreign);
            Field(ui, "medicinePressAt").SetValue(ui, Time.unscaledTime - 1f);
            Invoke(ui, "UpdateMedicineGesture");
            Assert.That(ui.BlocksGameplay, Is.True);
            Assert.That(input.Gameplay, Is.False);
            Assert.That(Field(look, "pointer").GetValue(look), Is.EqualTo(int.MinValue));
            heal.OnPointerUp(foreign);
            Assert.That(ui.BlocksGameplay, Is.True);
            var hub = (RectTransform)Field(ui, "medicineHub").GetValue(ui);
            var options = (List<ItemDefinition>)Field(ui, "wheelItems").GetValue(ui);
            int index = options.FindIndex(item => item.Id == "bandage");
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            float angle = index * Mathf.PI * 2f / options.Count;
            Vector2 offset = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * 108f;
            owner.position = RectTransformUtility.WorldToScreenPoint(null, hub.TransformPoint(offset));
            heal.OnDrag(owner);
            heal.OnPointerUp(owner);
            Assert.That(ui.BlocksGameplay, Is.False);
            Assert.That(((ItemStack)Invoke(session, "QuickMedicine", new object[] { null })).ItemId, Is.EqualTo("bandage"));
            Assert.That(bag.Count("bandage"), Is.EqualTo(5));
            Assert.That(player.IsTreating, Is.False);

            owner.position = centre;
            heal.OnPointerDown(owner);
            Field(ui, "medicinePressAt").SetValue(ui, Time.unscaledTime - 1f);
            Invoke(ui, "UpdateMedicineGesture");
            Assert.That(ui.BlocksGameplay, Is.True);
            Invoke(session, "OnApplicationFocus", false);
            Invoke(session, "OnApplicationFocus", true);
            heal.OnPointerUp(owner);
            Invoke(input, "Update");
            Assert.That(input.HealPressed, Is.False);
            Assert.That(ui.BlocksGameplay, Is.False);
            Assert.That(player.IsTreating, Is.False);

            heal.OnPointerDown(owner);
            heal.OnPointerUp(owner);
            Invoke(input, "Update");
            Assert.That(input.HealPressed, Is.True);
            Invoke(player, "Tick", Time.time, .016f);
            Assert.That(player.TreatmentItem.Id, Is.EqualTo("bandage"));
            Assert.That(bag.Count("bandage"), Is.EqualTo(5));
            Invoke(player, "TickTreatment", 2f);
            Assert.That(bag.Count("bandage"), Is.EqualTo(4));
            session.LeavePractice();
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(store.Path));
        }

        [UnityTest]
        public IEnumerator PracticeAmmoPanelOffersSixCompatibleGradesWithoutChangingTheRaidLoadout()
        {
            yield return OpenPractice();
            var weapon = session.Player.Weapon;
            var copy = session.ActiveFieldBag.Equipped(EquipmentSlot.Weapon);
            var original = session.Loadout.Equipped(EquipmentSlot.Weapon);
            int rounds = weapon.Magazine.Rounds;
            Invoke(session.UI, "TogglePracticePanel");
            for (int grade = 1; grade <= 6; grade++)
            {
                ClickPractice(grade + " 级弹");
                var selected = session.Catalog.Find(weapon.ReadyAmmoType);
                Assert.That(selected, Is.Not.Null);
                Assert.That(selected.Tier, Is.EqualTo(grade));
                Assert.That(WeaponRules.CanLoad(weapon.Definition, selected, out _), Is.True);
                Assert.That(copy.LoadedAmmoId, Is.EqualTo(selected.Id));
                Assert.That(weapon.Magazine.Rounds, Is.EqualTo(rounds));
                Assert.That(weapon.Reserve, Is.EqualTo(int.MaxValue));
            }
            Assert.That(weapon.SelectPracticeAmmo("ammo_copper"), Is.False);
            Assert.That(session.Catalog.Find(weapon.ReadyAmmoType).Tier, Is.EqualTo(6));
            Assert.That((original.LoadedAmmoId, original.LoadedAmmo), Is.EqualTo(("ammo", 3)));
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(store.Path));
            ClickPractice("补充练习装备");
            Assert.That(weapon.ReadyAmmoType, Is.EqualTo("ammo"));
            Assert.That(weapon.Magazine.Rounds, Is.EqualTo(weapon.Magazine.Capacity));
            Invoke(session.UI, "TogglePracticePanel");
            ClickPractice("返回大厅");
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.Menu), session.Error);
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(store.Path));
        }

        [UnityTest]
        public IEnumerator PracticeDummiesKeepSeparateGearAndWearThroughIndividualAndGlobalResets()
        {
            yield return OpenPractice();
            var range = session.GetType().GetProperty("Practice", Instance).GetValue(session);
            var player = session.Player;
            var targets = player.transform.parent.Cast<Transform>().Where(node => node.name == "Practice plate")
                .Select(node => node.GetComponent<Health>()).ToArray();
            Assert.That(targets, Has.Length.EqualTo(6));
            Equip(0, EquipmentSlot.Helmet, "helmet_1");
            Equip(1, EquipmentSlot.Helmet, "helmet_3");
            Equip(0, EquipmentSlot.Armor, "vest_2");
            Equip(1, EquipmentSlot.Armor, "vest_2");
            var first = Dummy(0);
            var second = Dummy(1);
            Assert.That((first.helmet, first.armor, second.helmet, second.armor),
                Is.EqualTo(("helmet_1", "vest_2", "helmet_3", "vest_2")));
            Assert.That((first.helmetWear, first.armorWear, second.helmetWear, second.armorWear), Is.EqualTo((0, 0, 0, 0)));

            Hit(0, .445f);
            Hit(0, 0f);
            var firstHit = Dummy(0);
            Assert.That(firstHit.helmetWear, Is.GreaterThan(0));
            Assert.That(firstHit.armorWear, Is.GreaterThan(0));
            Assert.That(firstHit.hits, Is.EqualTo(2));
            Assert.That(firstHit.health, Is.LessThan(first.health));
            Assert.That(Dummy(1), Is.EqualTo(second));
            Hit(1, 0f);
            var secondHit = Dummy(1);
            Assert.That(secondHit.armorWear, Is.GreaterThan(0));
            Assert.That(secondHit.hits, Is.EqualTo(1));
            Assert.That(Dummy(0), Is.EqualTo(firstHit));

            Invoke(session.UI, "TogglePracticePanel");
            ClickPractice("假人 2");
            ClickPractice("复位此假人");
            Assert.That(Dummy(0), Is.EqualTo(firstHit));
            Assert.That(Dummy(1), Is.EqualTo(second));
            var stats = Invoke(range, "GetStats");
            Assert.That((Read<int>(stats, "Shots"), Read<int>(stats, "Hits")), Is.EqualTo((3, 3)));
            ClickPractice("重置标靶和计数");
            Assert.That(Dummy(0), Is.EqualTo(first));
            Assert.That(Dummy(1), Is.EqualTo(second));
            stats = Invoke(range, "GetStats");
            Assert.That((Read<int>(stats, "Shots"), Read<int>(stats, "Hits")), Is.EqualTo((0, 0)));
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(store.Path));
            ClickPractice("返回大厅");
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.Menu), session.Error);
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(store.Path));

            void Equip(int index, EquipmentSlot slot, string item)
            {
                Assert.That(Invoke(range, "TrySetDummyGear", index, slot, item, session.Catalog), Is.EqualTo(true));
            }

            (string helmet, string armor, int helmetWear, int armorWear, int hits, float health) Dummy(int index)
            {
                var args = new object[] { index, null };
                Assert.That(Invoke(range, "TryGetDummy", args), Is.EqualTo(true));
                var dummy = args[1];
                return (Read<string>(dummy, "HelmetId"), Read<string>(dummy, "ArmorId"), Read<int>(dummy, "HelmetWear"),
                    Read<int>(dummy, "ArmorWear"), Read<int>(dummy, "Hits"), Read<float>(dummy, "CurrentHealth"));
            }

            void Hit(int index, float height)
            {
                // Use fixed impacts so the isolation check does not depend on random spread.
                var target = targets[index];
                var point = target.transform.TransformPoint(Vector3.up * height);
                float before = target.Current;
                typeof(Health).GetMethod("DamageAt", Instance, null,
                    new[] { typeof(float), typeof(Health), typeof(Vector3), typeof(int) }, null)
                    .Invoke(target, new object[] { 8f, player.Health, point, WeaponRules.PenetrationForTier(1) });
                Invoke(range, "RecordShot");
                Assert.That(Invoke(range, "RecordHit", target, point, before - target.Current), Is.EqualTo(true));
            }
        }

        [UnityTest]
        public IEnumerator FailedRangeSetupLeavesTheLobbyProfileUntouched()
        {
            CreateSession();
            var full = session.Catalog;
            var incomplete = ScriptableObject.CreateInstance<ItemCatalog>();
            try
            {
                foreach (var item in full.Items)
                    if (item.Id != "bandage") incomplete.Items.Add(item);
                Set(session, "Catalog", incomplete);
                LogAssert.Expect(LogType.Exception, new Regex("InvalidDataException: 靶场练习装备未就绪"));
                session.StartPractice();
                for (int frame = 0; frame < 20 && session.Phase == SessionPhase.PracticeLoading; frame++) yield return null;
                Assert.That(session.Phase, Is.EqualTo(SessionPhase.Error));
                Assert.That(session.Player, Is.Null);
                Assert.That(session.ActiveFieldBag, Is.SameAs(session.Loadout));
                Assert.That(session.Profile.RaidInProgress, Is.False);
                Assert.That(session.Profile.Raids, Is.EqualTo(4));
                Assert.That(host.transform.Find("Menu camera").GetComponent<Camera>().enabled, Is.True);
                CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(store.Path));
            }
            finally
            {
                Set(session, "Catalog", full);
                Object.Destroy(incomplete);
            }
        }

        [UnityTest]
        public IEnumerator LegacySaveMigratesInPlaceAndKeepsSelectedAndPurchasedUpgrades()
        {
            CreateSession(legacy: true, birthdayGiftClaimed: false);
            Assert.That(session.Profile.Version, Is.EqualTo(ProfileData.CurrentVersion));
            Assert.That(session.Profile.Credits, Is.EqualTo(777));
            Assert.That(session.Profile.Raids, Is.EqualTo(4));
            Assert.That(session.BoundBladeId, Is.EqualTo(BladeCatalog.ShortBladeId));
            var rifle = session.Loadout.Equipped(EquipmentSlot.Weapon);
            Assert.That(rifle.LoadedAmmoId, Is.EqualTo("ammo"));
            Assert.That(rifle.LoadedAmmo, Is.EqualTo(3));
            Assert.That(rifle.Mods.Grip, Is.EqualTo(1));
            Assert.That(rifle.Mods.OwnedGrip, Is.EqualTo(2));
            Assert.That(rifle.Mods.Sight, Is.EqualTo(1));
            Assert.That(store.EconomyUpgradeBackupPath, Is.EqualTo(store.Path + ".pre-v4.bak"));
            Assert.That(store.CakeUpgradeBackupPath, Is.EqualTo(store.Path + ".pre-v5.bak"));
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(store.EconomyUpgradeBackupPath));
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(store.CakeUpgradeBackupPath));
            Assert.That(File.Exists(store.LoadoutUpgradeBackupPath), Is.False);
            Assert.That(session.Stash.Count("medkit"), Is.EqualTo(1));
            Assert.That(session.Stash.Count(LootTable.BirthdayCake), Is.EqualTo(1));
            Assert.That(store.Load().BirthdayGiftClaimed, Is.True);
            Assert.That(Invoke(session, "SelectWeaponPart", session.Loadout, rifle.InstanceId, WeaponModSlot.Grip, 2), Is.EqualTo(true));
            Assert.That(session.Profile.Credits, Is.EqualTo(777));
            var saved = store.Load();
            Assert.That(saved.Loadout.Find(x => x.InstanceId == rifle.InstanceId).Mods.Grip, Is.EqualTo(2));
            Assert.That(saved.Stash.Where(item => item.ItemId == LootTable.BirthdayCake).Sum(item => item.Quantity), Is.EqualTo(1));
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(store.EconomyUpgradeBackupPath));
            yield return null;
        }

        [UnityTest]
        public IEnumerator InterruptedSchemaFourRaidMigratesTheOldSecureCakeWithoutChangingCounters()
        {
            var fixture = new ProfileData { Version = 4, Credits = 712, Raids = 3, Extractions = 1,
                RaidInProgress = true, BirthdayGiftClaimed = true, BoundBladeId = BladeCatalog.ShortBladeId };
            fixture.Loadout.Add(new ItemStack { ItemId = LootTable.BirthdayCake, InstanceId = "protected-cake", Area = CargoArea.Secure, Rotated = true });
            fixture.Stash.Add(new ItemStack { ItemId = "ammo", InstanceId = "ordinary-ammo", X = 5, Y = 9, Quantity = 8 });
            CreateSession(fixture: fixture);
            var saved = store.Load();
            Assert.That((saved.Version, saved.Credits, saved.Raids, saved.Extractions, saved.RaidInProgress, saved.BirthdayGiftClaimed),
                Is.EqualTo((5, 712, 3, 1, false, true)));
            Assert.That(saved.BoundBladeId, Is.EqualTo(BladeCatalog.ShortBladeId));
            var cake = session.Loadout.Find("protected-cake");
            Assert.That((cake.Area, cake.X, cake.Y, cake.Quantity, cake.Rotated), Is.EqualTo((CargoArea.Legacy, 0, 0, 1, true)));
            Assert.That(saved.Loadout.Single().InstanceId, Is.EqualTo(cake.InstanceId));
            var ammo = saved.Stash.Single();
            Assert.That((ammo.InstanceId, ammo.X, ammo.Y, ammo.Quantity), Is.EqualTo(("ordinary-ammo", 5, 9, 8)));
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(store.CakeUpgradeBackupPath));
            Assert.That(File.Exists(store.EconomyUpgradeBackupPath), Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CorruptSchemaFourCakeLayoutIsNotWrittenBack()
        {
            var fixture = new ProfileData { Version = 4, BirthdayGiftClaimed = true };
            fixture.Stash.Add(new ItemStack { ItemId = LootTable.BirthdayCake, InstanceId = "overlapped-cake" });
            fixture.Stash.Add(new ItemStack { ItemId = "ammo", InstanceId = "overlapping-item", X = 1, Y = 1 });
            LogAssert.Expect(LogType.Exception, new Regex("InvalidDataException: 物品相互重叠或超出背包范围"));
            CreateSession(fixture: fixture, expectedPhase: SessionPhase.Error);
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(store.Path));
            Assert.That(File.Exists(store.CakeUpgradeBackupPath), Is.False);
            Assert.That(store.Load().Version, Is.EqualTo(4));
            yield return null;
        }

        [UnityTest]
        public IEnumerator MigratedSuppliesStayHomeDuringDeploymentAndSurviveAnUnsuccessfulRaid()
        {
            CreateSession(legacy: true);
            var medicine = new ItemStack { InstanceId = "home-only-medkit", ItemId = "medkit", Area = CargoArea.Legacy, X = 2, Y = 2, UsesSpent = 1 };
            var cake = new ItemStack { InstanceId = "home-only-cake", ItemId = LootTable.BirthdayCake, Area = CargoArea.Legacy, Y = 12, Rotated = true };
            var storedCake = new ItemStack { InstanceId = "warehouse-recovery-cake", ItemId = LootTable.BirthdayCake, Area = CargoArea.Legacy };
            var prepared = session.Loadout.Snapshot();
            prepared.Add(medicine);
            prepared.Add(cake);
            session.Loadout.Restore(prepared);
            var warehouse = session.Stash.Snapshot();
            warehouse.Add(storedCake);
            session.Stash.Restore(warehouse);
            session.PersistSafeState();
            var supplies = session.Loadout.Find(medicine.InstanceId);
            ConfirmDefaultDeployment();
            session.StartRaid();
            for (int i = 0; i < 60 && session.Phase == SessionPhase.Loading; i++) yield return null;
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.Raid), session.Error);
            Assert.That(session.ActiveFieldBag.Find(medicine.InstanceId), Is.Null);
            Assert.That(session.ActiveFieldBag.Find(cake.InstanceId), Is.Null);
            Assert.That(session.ActiveFieldBag.Find(storedCake.InstanceId), Is.Null);
            var home = store.Load();
            Assert.That(home.RaidInProgress, Is.True);
            Assert.That(home.Stash.Single(item => item.InstanceId == storedCake.InstanceId).Area, Is.EqualTo(CargoArea.Legacy));
            Assert.That(home.Loadout.Single(item => item.InstanceId == cake.InstanceId).Y, Is.EqualTo(12));
            Assert.That(home.Loadout.TrueForAll(item => item.Area == CargoArea.Legacy && item.Slot == EquipmentSlot.None), Is.True);
            var stored = home.Loadout.Find(item => item.InstanceId == medicine.InstanceId);
            Assert.That(stored, Is.Not.Null);
            Assert.That(stored.UsesSpent, Is.EqualTo(supplies.UsesSpent));

            session.EndRaid(false, "验收隔离档");
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.Results), session.Error);
            Assert.That(session.Loadout.Equipped(EquipmentSlot.Weapon), Is.Null);
            home = store.Load();
            Assert.That(home.RaidInProgress, Is.False);
            var recovered = home.Loadout.Find(item => item.InstanceId == medicine.InstanceId);
            Assert.That(recovered, Is.Not.Null);
            Assert.That((recovered.Area, recovered.X, recovered.Y, recovered.UsesSpent),
                Is.EqualTo((CargoArea.Legacy, medicine.X, medicine.Y, medicine.UsesSpent)));
            Assert.That(home.Loadout.Single(item => item.InstanceId == cake.InstanceId).Area, Is.EqualTo(CargoArea.Legacy));
            Assert.That(session.Loadout.Find(cake.InstanceId).Rotated, Is.True);
            Assert.That(home.Stash.Single(item => item.InstanceId == storedCake.InstanceId).Area, Is.EqualTo(CargoArea.Legacy));
            session.ReturnToMenu();
        }

        [UnityTest]
        public IEnumerator RaidUsesTheSameBuildAndExitOnlySettlesAfterConfirmation()
        {
            CreateSession();
            var gun = session.Loadout.Equipped(EquipmentSlot.Weapon);
            int appearance = WeaponRules.Look(gun, session.Catalog);
            Invoke(session.UI, "ShowGunsmith", session.Loadout, gun.InstanceId);
            var preview = host.GetComponentsInChildren<WeaponVisual>();
            Assert.That(preview.Length, Is.EqualTo(1));
            Assert.That(preview[0].AppearanceKey, Is.EqualTo(appearance));
            var geometry = Geometry(preview[0]);

            session.StartPractice();
            for (int frame = 0; frame < 30 && session.Phase == SessionPhase.PracticeLoading; frame++) yield return null;
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.Practice), session.Error);
            Assert.That(session.Player.WeaponView.AppearanceKey, Is.EqualTo(appearance));
            CollectionAssert.AreEqual(geometry, Geometry(session.Player.WeaponView));
            session.LeavePractice();
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(store.Path));

            ConfirmDefaultDeployment();
            session.StartRaid();
            for (int frame = 0; frame < 60 && session.Phase == SessionPhase.Loading; frame++) yield return null;
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.Raid), session.Error);
            Invoke(session, "OnApplicationFocus", true);
            var player = session.Player;
            Assert.That(player.WeaponView.AppearanceKey, Is.EqualTo(appearance));
            CollectionAssert.AreEqual(geometry, Geometry(player.WeaponView));
            Assert.That(player.Weapon.Magazine.Rounds, Is.EqualTo(3));
            Assert.That(player.Weapon.HasInfiniteReserve, Is.False);
            Assert.That(player.Weapon.Reserve, Is.EqualTo(18));
            var deployed = File.ReadAllBytes(store.Path);
            session.UI.BackOrSettings();
            Click("退出本局");
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.Raid));
            Assert.That(session.UI.BlocksGameplay, Is.True);
            Click("取消 · 继续本局");
            Assert.That(session.UI.BlocksGameplay, Is.False);
            Assert.That(session.Player, Is.SameAs(player));
            Assert.That(player.Weapon.Magazine.Rounds, Is.EqualTo(3));
            CollectionAssert.AreEqual(deployed, File.ReadAllBytes(store.Path));
            yield return null;
            session.UI.BackOrSettings();
            Click("退出本局");
            Click("确认退出");
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.Menu));
            Assert.That(session.Player, Is.Null);
            Assert.That(session.UI.GetComponentsInChildren<RectTransform>().Any(node => node.name == "Operations desk"), Is.True);
            Assert.That(session.Result, Does.Contain("主动退出"));
            var after = store.Load();
            Assert.That(after.RaidInProgress, Is.False);
            Assert.That(after.Loadout, Is.Empty);
            Assert.That(after.Stash.Count, Is.EqualTo(1));
            Assert.That(after.Credits, Is.EqualTo(777));
            Assert.That(after.BoundBladeId, Is.EqualTo(BladeCatalog.ShortBladeId));
            Assert.That(after.Extractions, Is.EqualTo(2));
            Assert.That(after.Raids, Is.EqualTo(5));

            void Click(string caption)
            {
                foreach (var button in session.UI.GetComponentsInChildren<Button>())
                    if (button.gameObject.name == caption) { button.onClick.Invoke(); return; }
                Assert.Fail("Missing action: " + caption);
            }
        }

        static (string path, string mesh, int vertices, Vector3 size, string material)[] Geometry(WeaponVisual view)
        {
            string[] paths = { "", "Part Sight", "Part Grip", "Part Stock", "Part Muzzle", "Magazine" };
            var actual = new (string, string, int, Vector3, string)[paths.Length];
            for (int i = 0; i < paths.Length; i++)
            {
                var piece = i == 0 ? view.transform : view.transform.Find(paths[i]);
                Assert.That(piece.gameObject.activeInHierarchy, Is.True, paths[i]);
                var mesh = piece.GetComponent<MeshFilter>().sharedMesh;
                var material = piece.GetComponent<MeshRenderer>().sharedMaterial;
                actual[i] = (paths[i], mesh.name, mesh.vertexCount, mesh.bounds.size, material.name);
            }
            return actual;
        }

        IEnumerator OpenPractice()
        {
            CreateSession();
            session.Input.ForceTouch = true;
            session.StartPractice();
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.PracticeLoading));
            session.PersistSafeState();
            for (int frame = 0; frame < 20 && session.Phase == SessionPhase.PracticeLoading; frame++) yield return null;
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.Practice), session.Error);
            Invoke(session, "OnApplicationFocus", true);
            Assert.That(session.Player, Is.Not.Null);
        }

        void ConfirmDefaultDeployment()
        {
            Assert.That(Invoke(session, "ConfirmDeployment", 0, 0), Is.EqualTo(true), "Confirm regular harbor deployment.");
        }

        void CreateSession(bool legacy = false, bool birthdayGiftClaimed = true, ProfileData fixture = null,
            SessionPhase expectedPhase = SessionPhase.Menu)
        {
            var catalog = Resources.Load<ItemCatalog>("Dustline/Items");
            Assert.That(catalog, Is.Not.Null, "Run Dustline > Prepare Project first.");
            store = new ProfileStore(Path.Combine(Application.temporaryCachePath, "dustline-practice-test-" + Guid.NewGuid().ToString("N") + ".json"));
            var stash = GridInventory.Warehouse(catalog);
            Assert.That(stash.TryAdd("medkit", 1, out _), Is.True);
            var rifle = new ItemStack { InstanceId = "raid-rifle", ItemId = "rifle", Slot = EquipmentSlot.Weapon, LoadedAmmo = 3 };
            rifle.Mods = new WeaponMods { Grip = 1, OwnedGrip = 2, Sight = 1, OwnedSight = 1 };
            if (!legacy)
            {
                rifle.LoadedAmmoId = "ammo";
                rifle.Parts.Sight = new PartLink { ItemId = "part_sight_prism", InstanceId = "raid-sight" };
                rifle.Parts.Stock = new PartLink { ItemId = "part_stock", InstanceId = "raid-stock" };
                rifle.Parts.Muzzle = new PartLink { ItemId = "part_muzzle", InstanceId = "raid-muzzle" };
                rifle.Parts.Magazine = new PartLink { ItemId = "part_mag_a", InstanceId = "raid-magazine" };
            }
            var carried = new List<ItemStack>
            {
                rifle,
                new ItemStack { InstanceId = "raid-ammo", ItemId = "ammo", Quantity = 18, Area = legacy ? CargoArea.Main : CargoArea.Pockets }
            };
            store.Save(fixture ?? new ProfileData { Version = legacy ? 1 : ProfileData.CurrentVersion, Credits = 777, Raids = 4, Extractions = 2, BoundBladeId = BladeCatalog.ShortBladeId,
                BirthdayGiftClaimed = birthdayGiftClaimed, Loadout = carried, Stash = stash.Snapshot() });
            originalBytes = File.ReadAllBytes(store.Path);
            host = new GameObject("Isolated practice test session");
            host.SetActive(false);
            session = host.AddComponent<GameSession>();
            Field(session, "store").SetValue(session, store);
            host.SetActive(true);
            Assert.That(session.Phase, Is.EqualTo(expectedPhase), session.Error);
        }

        void ClickPractice(string name)
        {
            var pages = session.UI.transform.Find("Dustline UI/Safe area/Pages");
            var panel = pages.GetComponentsInChildren<RectTransform>().SingleOrDefault(node => node.name == "Practice controls");
            Assert.That(panel, Is.Not.Null);
            var button = panel.GetComponentsInChildren<Button>().SingleOrDefault(candidate => candidate.name == name);
            Assert.That(button, Is.Not.Null, name);
            Assert.That(button.IsInteractable(), Is.True, name);
            button.onClick.Invoke();
        }

        static T Read<T>(object owner, string name) => (T)Field(owner, name).GetValue(owner);
        static FieldInfo Field(object owner, string name) => owner.GetType().GetField(name, Instance);
        static object Invoke(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Instance).Invoke(owner, args);
        static void Set(object owner, string name, object value) => owner.GetType().GetProperty(name, Instance).GetSetMethod(true).Invoke(owner, new[] { value });
    }
}
