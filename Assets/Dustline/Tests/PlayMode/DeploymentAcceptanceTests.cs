using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Dustline.Tests
{
    public sealed class DeploymentAcceptanceTests
    {
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
        const int Harbor = 0, Sluice = 2, Regular = 0;
        const string SiteKey = "Dustline.StrategySite.v1", TierKey = "Dustline.StrategyTier.v1";
        const string MedkitId = "test-medkit", AmmoId = "test-ammo", RifleId = "test-rifle", PackId = "test-pack";
        delegate bool InventoryEdit(GridInventory source, GridInventory destination, out string error);

        GameObject host;
        GameSession session;
        ProfileStore store;
        int previousLimit, previousSync;
        RenderPipelineAsset previousPipeline;
        bool restoreMapPreferences;
        int? previousSite, previousTier;

        [SetUp]
        public void RememberDisplay()
        {
            previousLimit = Application.targetFrameRate;
            previousSync = QualitySettings.vSyncCount;
            previousPipeline = QualitySettings.renderPipeline;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            CloseSession();
            if (store != null) File.Delete(store.Path);
            if (restoreMapPreferences)
            {
                RestorePreference(SiteKey, previousSite);
                RestorePreference(TierKey, previousTier);
                PlayerPrefs.Save();
                restoreMapPreferences = false;
            }
            yield return null;
            QualitySettings.renderPipeline = previousPipeline;
            QualitySettings.vSyncCount = previousSync;
            Application.targetFrameRate = previousLimit;
        }

        [UnityTest]
        public IEnumerator OnlyConfirmedOpenSiteCanLaunchFromTheLobby()
        {
            OpenSession();
            previousSite = PlayerPrefs.HasKey(SiteKey) ? (int?)PlayerPrefs.GetInt(SiteKey) : null;
            previousTier = PlayerPrefs.HasKey(TierKey) ? (int?)PlayerPrefs.GetInt(TierKey) : null;
            restoreMapPreferences = true;
            PlayerPrefs.SetInt(SiteKey, -1);
            PlayerPrefs.SetInt(TierKey, Regular);
            var original = File.ReadAllBytes(store.Path);
            session.StartRaid();
            AssertNotDeployed(original);
            Assert.That(FindButton(Panel("Operations desk"), "出发"), Is.Null);

            Click(Panel("Operations desk"), "开始游戏");
            var map = Panel("Strategic map");
            for (int i = 0; i < 120 && FindButton(map, "灰湾水闸") == null; i++) yield return null;
            Click(map, "灰湾水闸");
            var locked = Node(map, "Site details");
            Assert.That(locked.GetComponentsInChildren<Text>().Any(label => label.text == "该地点尚未开放。"), Is.True);
            Assert.That(FindButton(map, "开始行动"), Is.Null);
            Assert.That(ConfirmDeployment(Sluice), Is.False);
            session.StartRaid();
            AssertNotDeployed(original);

            Click(map, "‹ 返回");
            for (int i = 0; i < 120 && FindButton(map, "石脊采石场") == null; i++) yield return null;
            Click(map, "石脊采石场");
            Click(map, "开始行动");
            var preparation = Panel("Loadout preparation");
            session.StartRaid();
            AssertNotDeployed(original);
            Click(preparation, "确认配装");
            var reminders = Node(preparation, "Deployment reminders");
            AssertNotDeployed(original);
            Click(reminders, "继续确认");

            var lobby = Panel("Operations desk");
            Assert.That(FindButton(lobby, "石脊采石场 / 常规"), Is.Not.Null);
            Assert.That(lobby.GetComponentsInChildren<Text>().Any(label => label.text == "30:00 · 可空装"), Is.True);
            AssertNotDeployed(original);
            Click(lobby, "出发");
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.Loading), session.Error);
            Assert.That(Panel("Loading district").GetComponentsInChildren<Text>().Any(label => label.text == "石脊采石场"), Is.True);
            for (int i = 0; i < 60 && session.Phase == SessionPhase.Loading; i++) yield return null;
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.Raid), session.Error);
            Assert.That(session.RaidRemaining, Is.InRange(1798f, 1800f));
            Assert.That(store.Load().RaidInProgress, Is.True);
        }

        [UnityTest]
        public IEnumerator SecureCargoRoundTripsAndFailureKeepsOnlyProtectedItems()
        {
            OpenSession();
            Move(session.Stash, session.Loadout, MedkitId, CargoArea.Secure, 0, 1, rotated: true);
            AssertSavedMedkit(CargoArea.Secure, 0, 1, rotated: true);
            Move(session.Loadout, session.Stash, MedkitId, CargoArea.Main, 4, 2);
            AssertSavedMedkit(CargoArea.Main, 4, 2, inStash: true);
            Move(session.Stash, session.Loadout, MedkitId, CargoArea.Secure, 0, 0);

            yield return OpenRaid();
            AssertSavedMedkit(CargoArea.Secure, 0, 0);
            var bag = session.ActiveFieldBag;
            Move(bag, bag, MedkitId, CargoArea.Main, 2, 1);
            Assert.That(store.Load().Loadout, Is.Empty, "Taking an item out must remove it from the protected save.");
            Assert.That(bag.Find(MedkitId).Area, Is.EqualTo(CargoArea.Main));
            Move(bag, bag, MedkitId, CargoArea.Secure, 0, 1, rotated: true);
            AssertSavedMedkit(CargoArea.Secure, 0, 1, rotated: true);

            session.EndRaid(false, "测试：撤离失败");
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.Results), session.Error);
            AssertSettled(extracted: false);
        }

        [UnityTest]
        public IEnumerator SecureCargoAddedInRaidSurvivesAnInterruptedSessionExactlyOnce()
        {
            OpenSession();
            Move(session.Stash, session.Loadout, MedkitId, CargoArea.Main, 2, 1);
            yield return OpenRaid();
            Assert.That(store.Load().Loadout, Is.Empty);
            var bag = session.ActiveFieldBag;
            Move(bag, bag, MedkitId, CargoArea.Secure, 0, 1, rotated: true);
            AssertSavedMedkit(CargoArea.Secure, 0, 1, rotated: true);
            var interrupted = File.ReadAllBytes(store.Path);

            CloseSession();
            yield return null;
            CollectionAssert.AreEqual(interrupted, File.ReadAllBytes(store.Path));
            StartSession();
            Assert.That(session.Notice, Does.Contain("上次突袭中断"));
            AssertSettled(extracted: false);
            var recovered = File.ReadAllBytes(store.Path);

            CloseSession();
            yield return null;
            StartSession();
            AssertSettled(extracted: false);
            CollectionAssert.AreEqual(recovered, File.ReadAllBytes(store.Path));
        }

        [UnityTest]
        public IEnumerator SuccessfulRaidKeepsProtectedCargoAndBanksUnprotectedCargoExactlyOnce()
        {
            OpenSession();
            Move(session.Stash, session.Loadout, MedkitId, CargoArea.Main, 2, 1);
            yield return OpenRaid();
            var bag = session.ActiveFieldBag;
            Move(bag, bag, MedkitId, CargoArea.Secure, 0, 1, rotated: true);
            AssertSavedMedkit(CargoArea.Secure, 0, 1, rotated: true);

            session.EndRaid(true, "测试：撤离成功");
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.Results), session.Error);
            AssertSettled(extracted: true);
            var settled = File.ReadAllBytes(store.Path);
            session.EndRaid(true, "重复结算不应写档");
            CollectionAssert.AreEqual(settled, File.ReadAllBytes(store.Path));
        }

        IEnumerator OpenRaid()
        {
            Assert.That(ConfirmDeployment(Harbor), Is.True);
            session.StartRaid();
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.Loading), session.Error);
            for (int i = 0; i < 60 && session.Phase == SessionPhase.Loading; i++) yield return null;
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.Raid), session.Error);
            Assert.That(session.RaidRemaining, Is.InRange(1798f, 1800f));
            Assert.That(store.Load().RaidInProgress, Is.True);
        }

        void AssertNotDeployed(byte[] original)
        {
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.Menu), session.Error);
            Assert.That(session.World, Is.Null);
            Assert.That(session.Player, Is.Null);
            Assert.That(session.Profile.RaidInProgress, Is.False);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(store.Path));
        }

        void AssertSettled(bool extracted)
        {
            var saved = store.Load();
            Assert.That(saved.RaidInProgress, Is.False);
            Assert.That(saved.Raids, Is.EqualTo(1));
            Assert.That(saved.Extractions, Is.EqualTo(extracted ? 1 : 0));
            Assert.That(saved.BoundBladeId, Is.EqualTo(BladeCatalog.ShortBladeId));
            CollectionAssert.AreEqual(new[] { MedkitId }, saved.Loadout.Select(item => item.InstanceId));
            CollectionAssert.AreEquivalent(extracted ? new[] { AmmoId, RifleId, PackId } : Array.Empty<string>(),
                saved.Stash.Select(item => item.InstanceId));
            AssertMedkit(saved.Stash, saved.Loadout, CargoArea.Secure, 0, 1, rotated: true, inStash: false);
            AssertMedkit(session.Stash.Items, session.Loadout.Items, CargoArea.Secure, 0, 1, rotated: true, inStash: false);
        }

        void AssertSavedMedkit(CargoArea area, int x, int y, bool rotated = false, bool inStash = false)
        {
            var saved = store.Load();
            AssertMedkit(saved.Stash, saved.Loadout, area, x, y, rotated, inStash);
            AssertMedkit(session.Stash.Items, session.Loadout.Items, area, x, y, rotated, inStash);
        }

        static void AssertMedkit(IReadOnlyList<ItemStack> stash, IReadOnlyList<ItemStack> carried,
            CargoArea area, int x, int y, bool rotated, bool inStash)
        {
            var matches = stash.Concat(carried).Where(item => item.ItemId == "medkit").ToArray();
            Assert.That(matches, Has.Length.EqualTo(1));
            var stack = matches[0];
            Assert.That((inStash ? stash : carried).Any(item => item.InstanceId == MedkitId), Is.True);
            Assert.That((stack.InstanceId, stack.Area, stack.X, stack.Y, stack.Rotated, stack.Quantity, stack.UsesSpent),
                Is.EqualTo((MedkitId, area, x, y, rotated, 1, 1)));
        }

        bool ConfirmDeployment(int site) => (bool)typeof(GameSession).GetMethod("ConfirmDeployment", Instance)
            .Invoke(session, new object[] { site, Regular });

        void Move(GridInventory source, GridInventory destination, string id, CargoArea area, int x, int y, bool rotated = false)
        {
            var edit = typeof(GameSession).GetMethod("EditInventory", Instance);
            InventoryEdit operation = (GridInventory from, GridInventory to, out string error) => from == to
                ? from.TryMove(id, area, x, y, rotated, out error)
                : from.TryTransferAt(id, to, area, x, y, rotated, out error);
            var callback = Delegate.CreateDelegate(edit.GetParameters()[2].ParameterType, operation.Target, operation.Method);
            var args = new object[] { source, destination, callback, null };
            Assert.That((bool)edit.Invoke(session, args), Is.True, args[3] as string);
        }

        void OpenSession()
        {
            Assert.That(Resources.Load<ItemCatalog>("Dustline/Items"), Is.Not.Null, "Run Dustline > Prepare Project first.");
            store = new ProfileStore(Path.Combine(Application.temporaryCachePath, "dustline-deployment-" + Guid.NewGuid().ToString("N") + ".json"));
            store.Save(new ProfileData
            {
                Version = ProfileData.CurrentVersion, BirthdayGiftClaimed = true, BoundBladeId = BladeCatalog.ShortBladeId,
                Loadout = new List<ItemStack>
                {
                    new ItemStack { ItemId = "rifle", InstanceId = RifleId, Slot = EquipmentSlot.Weapon, LoadedAmmo = 3, LoadedAmmoId = "ammo" },
                    new ItemStack { ItemId = "pack_compact", InstanceId = PackId, Slot = EquipmentSlot.Pack },
                    new ItemStack { ItemId = "ammo", InstanceId = AmmoId, Area = CargoArea.Pockets, Quantity = 18 }
                },
                Stash = new List<ItemStack> { new ItemStack { ItemId = "medkit", InstanceId = MedkitId, UsesSpent = 1 } }
            });
            StartSession();
        }

        void StartSession()
        {
            host = new GameObject("Deployment acceptance session");
            host.SetActive(false);
            session = host.AddComponent<GameSession>();
            typeof(GameSession).GetField("store", Instance).SetValue(session, store);
            host.SetActive(true);
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.Menu), session.Error);
            session.Input.ForceTouch = true;
        }

        void CloseSession()
        {
            if (host == null) return;
            host.SetActive(false);
            Object.Destroy(host);
            host = null;
        }

        Transform Panel(string name) => Node(session.UI.transform.Find("Dustline UI/Safe area/Pages"), name);
        static Transform Node(Transform root, string name)
        {
            var node = root.GetComponentsInChildren<RectTransform>().SingleOrDefault(candidate => candidate.name == name);
            Assert.That(node, Is.Not.Null, name);
            return node;
        }

        static Button FindButton(Transform root, string name) => root.GetComponentsInChildren<Button>().SingleOrDefault(button => button.name == name);
        static void Click(Transform root, string name)
        {
            var button = FindButton(root, name);
            Assert.That(button, Is.Not.Null, name);
            Assert.That(button.IsInteractable(), Is.True, name);
            button.onClick.Invoke();
        }

        static void RestorePreference(string key, int? previous)
        {
            if (previous.HasValue) PlayerPrefs.SetInt(key, previous.Value);
            else PlayerPrefs.DeleteKey(key);
        }
    }
}
