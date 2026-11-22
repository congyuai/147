using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
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
            var range = player.transform.parent;
            Assert.That(training, Is.Not.SameAs(raidBag));
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
            plate.Damage(plate.Maximum, player.Health);
            Assert.That(plate.gameObject.activeSelf, Is.False);
            var practice = session.GetType().GetProperty("Practice", Instance).GetValue(session);
            Assert.That(practice.GetType().GetProperty("Shots").GetValue(practice), Is.EqualTo(1));
            Assert.That(practice.GetType().GetProperty("Plates").GetValue(practice), Is.EqualTo(1));
            Invoke(practice, "ResetPlates");
            Assert.That(plate.Alive && plate.gameObject.activeSelf, Is.True);

            Invoke(session, "PracticeHealthSample");
            Assert.That(player.Health.Current, Is.EqualTo(40f).Within(.001f));
            var used = (ItemStack)Invoke(session, "QuickMedicine", new object[] { "bandage" });
            Assert.That(used, Is.Not.Null);
            session.UseMedicine(used.InstanceId);
            Assert.That(player.IsTreating, Is.True);
            Invoke(player, "TickTreatment", 4f);
            Assert.That(player.Health.Current, Is.EqualTo(62f).Within(.001f));
            Assert.That(training.Count("bandage"), Is.EqualTo(4));
            Assert.That(raidBag.Count("bandage"), Is.Zero);

            Invoke(session, "RefillPractice");
            Assert.That(session.ActiveFieldBag, Is.Not.SameAs(training));
            Assert.That(session.ActiveFieldBag.Count("bandage"), Is.EqualTo(5));
            Assert.That(player.Weapon.Magazine.Rounds, Is.EqualTo(player.Weapon.Magazine.Capacity));
            session.UseMedicine();
            Assert.That(player.IsTreating, Is.True);
            Invoke(session, "OnApplicationPause", true);
            Invoke(session, "OnApplicationPause", false);
            Assert.That(player.IsTreating, Is.False);
            Assert.That(session.ActiveFieldBag.Count("medkit"), Is.EqualTo(2));
            session.UseMedicine();
            Assert.That(player.IsTreating, Is.True);
            player.Health.Damage(1f, null);
            Assert.That(player.IsTreating, Is.False);
            Assert.That(session.ActiveFieldBag.Count("medkit"), Is.EqualTo(2));
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

        void CreateSession()
        {
            var catalog = Resources.Load<ItemCatalog>("Dustline/Items");
            Assert.That(catalog, Is.Not.Null, "Run Dustline > Prepare Project first.");
            store = new ProfileStore(Path.Combine(Application.temporaryCachePath, "dustline-practice-test-" + Guid.NewGuid().ToString("N") + ".json"));
            var stash = new GridInventory(12, 10, 300f, catalog);
            var loadout = new GridInventory(6, 6, 30f, catalog);
            Assert.That(stash.TryAdd("medkit", 1, out _), Is.True);
            Assert.That(loadout.TryAdd("rifle", 1, out _), Is.True);
            var rifle = loadout.Items[0];
            Assert.That(loadout.TryEquip(rifle.InstanceId, EquipmentSlot.Weapon, out _), Is.True);
            rifle.LoadedAmmo = 3;
            Assert.That(loadout.TryAdd("ammo", 18, out _), Is.True);
            store.Save(new ProfileData { Credits = 777, Raids = 4, Extractions = 2, Loadout = loadout.Snapshot(), Stash = stash.Snapshot() });
            originalBytes = File.ReadAllBytes(store.Path);
            host = new GameObject("Isolated practice test session");
            host.SetActive(false);
            session = host.AddComponent<GameSession>();
            Field(session, "store").SetValue(session, store);
            host.SetActive(true);
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.Menu), session.Error);
        }

        static FieldInfo Field(object owner, string name) => owner.GetType().GetField(name, Instance);
        static object Invoke(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Instance).Invoke(owner, args);
        static void Set(object owner, string name, object value) => owner.GetType().GetProperty(name, Instance).GetSetMethod(true).Invoke(owner, new[] { value });
    }
}
