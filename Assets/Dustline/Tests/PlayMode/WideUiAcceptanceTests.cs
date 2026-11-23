using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Dustline.Tests
{
    public sealed class WideUiAcceptanceTests
    {
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const string Root = "Dustline UI/Safe area/";
        GameObject host;
        GameSession session;
        ProfileStore store;
        int previousLimit, previousSync;
        RenderPipelineAsset previousPipeline;

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
            if (host != null) { host.SetActive(false); Object.Destroy(host); }
            if (store != null) File.Delete(store.Path);
            yield return null;
            QualitySettings.renderPipeline = previousPipeline;
            QualitySettings.vSyncCount = previousSync;
            Application.targetFrameRate = previousLimit;
        }

        [UnityTest]
        public IEnumerator StashFitsTwelveColumnsAndRotatesTheRealDropFootprint()
        {
            RequireLandscape();
            OpenSession(0);
            session.UI.ShowStash();
            yield return null;
            var page = Panel("Stash");
            var contents = Node(page, "Stash contents");
            var wearer = Node(contents, "Carried gear");
            var storage = Node(contents, "Stash grid region");
            Assert.That(wearer.rect.width / (wearer.rect.width + storage.rect.width), Is.EqualTo(.45f).Within(.01));
            Assert.That(Node(wearer, "Equipment slot / Helmet").Find("Empty equipment outline"), Is.Not.Null);
            Assert.That(Node(wearer, "Equipment slot / Armor").GetComponentInChildren<RawImage>(), Is.Not.Null);
            Assert.That(wearer.GetComponentsInChildren<Text>().Any(label => label.text.Contains("2+4 列") && label.text.Contains("12 格")), Is.True);
            Assert.That(wearer.GetComponentsInChildren<Text>().Any(label => label.text.Contains("3+4 列") && label.text.Contains("56 格")), Is.True);
            Assert.That(Active(contents, "Item details"), Is.False);

            var grid = Node(storage, "Item grid").GetComponent<InventoryBoard>();
            float cell = ((RectTransform)grid.transform).rect.width / 12f;
            Assert.That(((RectTransform)grid.transform).rect.height, Is.EqualTo(10 * cell).Within(.02));
            Assert.That(((RectTransform)grid.transform.parent.parent).rect.width, Is.GreaterThanOrEqualTo(12 * cell));
            var stack = session.Stash.Items.Single(item => item.ItemId == "dust_marksman");
            var tile = Node(grid.transform, session.Catalog.Find(stack.ItemId).DisplayName);
            Assert.That(tile.rect.width, Is.GreaterThan(3f * cell));
            Assert.That(tile.GetComponentInChildren<RawImage>().name, Is.EqualTo("Item model / dust_marksman"));
            yield return Capture("01-stash", page);
            tile.GetComponent<Button>().onClick.Invoke();
            Assert.That(Active(contents, "Item details"), Is.True);
            Click(contents, "收起");
            Assert.That(Active(contents, "Item details"), Is.False);
            yield return null;

            grid = Node(Node(contents, "Stash grid region"), "Item grid").GetComponent<InventoryBoard>();
            tile = Node(grid.transform, session.Catalog.Find(stack.ItemId).DisplayName);
            var grab = Node(tile, "Item drag handle");
            Assert.That(ExecuteEvents.GetEventHandler<IDragHandler>(grab.gameObject), Is.SameAs(grab.gameObject));
            var scroll = grid.GetComponentInParent<ScrollRect>();
            if (scroll != null) Assert.That(ExecuteEvents.GetEventHandler<IDragHandler>(tile.gameObject), Is.SameAs(scroll.gameObject));
            var before = File.ReadAllBytes(store.Path);
            var rect = (RectTransform)grid.transform;
            Vector2 press = ScreenPoint(grab), destination = RectTransformUtility.WorldToScreenPoint(null,
                rect.TransformPoint(new Vector3(rect.rect.xMin + cell * 7.5f, rect.rect.yMax - cell * 1.5f)));
            var drag = new PointerEventData(EventSystem.current) { pointerId = 42, button = PointerEventData.InputButton.Left, pressPosition = press, position = press };
            ExecuteEvents.Execute(grab.gameObject, drag, ExecuteEvents.beginDragHandler);
            drag.position = destination;
            ExecuteEvents.Execute(grab.gameObject, drag, ExecuteEvents.dragHandler);
            var footprint = Node(grid.transform, "Drop footprint").GetComponent<Graphic>();
            Assert.That((int)Field(footprint, "right") - (int)Field(footprint, "left"), Is.EqualTo(4));
            Assert.That((int)Field(footprint, "bottom") - (int)Field(footprint, "top"), Is.EqualTo(2));
            Click(page, "旋转物品 90°");
            Assert.That((int)Field(footprint, "right") - (int)Field(footprint, "left"), Is.EqualTo(2));
            Assert.That((int)Field(footprint, "bottom") - (int)Field(footprint, "top"), Is.EqualTo(4));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(store.Path));
            ExecuteEvents.Execute(grab.gameObject, drag, ExecuteEvents.endDragHandler);
            Assert.That(stack.Rotated, Is.True);
            Assert.That((stack.X, stack.Y), Is.EqualTo((7, 1)));
            Assert.That(store.Load().Stash.Single(item => item.InstanceId == stack.InstanceId).Rotated, Is.True);
            Assert.That(Active(contents, "Item details"), Is.False);
        }

        [UnityTest]
        public IEnumerator WorkshopHotspotPreviewsAStorePartAtZeroCreditsAndCommitsOnlyOnConfirmation()
        {
            RequireLandscape();
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("The workshop requires an actual graphics device.");
            OpenSession(0);
            var gun = session.Loadout.Equipped(EquipmentSlot.Weapon);
            var before = File.ReadAllBytes(store.Path);
            session.UI.ShowStash();
            var stockIcon = Node(Panel("Stash"), "Equipment slot / Weapon").GetComponentInChildren<RawImage>();
            for (int i = 0; i < 20 && stockIcon.texture == null; i++) yield return null;
            Texture stockTexture = stockIcon.texture;
            Assert.That(stockTexture, Is.Not.Null);
            Invoke(session.UI, "ShowGunsmith", session.Loadout, gun.InstanceId);
            yield return null;
            var page = Panel("Gunsmith");
            var surface = Node(page, "Workshop model viewport");
            var marker = Node(surface, "Part hotspot / Magazine");
            var art = Node(surface, "Rotatable weapon").GetComponent<RawImage>();
            Assert.That(art.texture is RenderTexture target && target.IsCreated(), Is.True);
            var position = marker.anchoredPosition;
            var gesture = surface.GetComponents<MonoBehaviour>().Single(item => item.GetType().Name == "WorkshopGesture");
            Invoke(gesture, "Press", 1, new Vector2(200, -170), true);
            Invoke(gesture, "Move", 1, new Vector2(272, -153));
            Invoke(gesture, "Release", 1, new Vector2(272, -153), false);
            yield return null;
            Assert.That(Vector2.Distance(position, marker.anchoredPosition), Is.GreaterThan(4f));
            position = marker.anchoredPosition;
            Invoke(gesture, "Press", 2, new Vector2(220, -150), true);
            Invoke(gesture, "Press", 3, new Vector2(340, -150), true);
            Invoke(gesture, "Move", 3, new Vector2(374, -150));
            Invoke(gesture, "Release", 3, new Vector2(374, -150), false);
            Invoke(gesture, "Release", 2, new Vector2(220, -150), false);
            yield return null;
            Assert.That(Vector2.Distance(position, marker.anchoredPosition), Is.GreaterThan(2f));
            Assert.That(Active(page, "Candidate / part_mag_a"), Is.False, "A pinch must not select a slot.");
            Click(surface, "重置视角");
            yield return null;
            TapHotspot(surface, marker);
            yield return null;
            Assert.That(Active(surface, "Selected model part"), Is.True);
            var option = Node(page, "Candidate / part_mag_a");
            Assert.That(option.GetComponentsInChildren<Text>().Any(label => label.text.StartsWith("商店售价 ")), Is.True);
            Assert.That(HasText(page, "容量 20 发"), Is.True);
            var oldMesh = PreviewMagazine();
            yield return Capture("02-workshop-stock", page);

            Click(page, "Candidate / part_mag_a");
            yield return null;
            Assert.That(HasText(page, "容量 20 → 30 发"), Is.True);
            Assert.That(PreviewMagazine(), Is.Not.SameAs(oldMesh));
            Assert.That(gun.Parts.Magazine.Empty, Is.True);
            Assert.That(session.Profile.Credits, Is.Zero);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(store.Path));
            yield return Capture("03-workshop-amber-preview", page);
            Click(page, "确认安装");
            Assert.That(gun.Parts.Magazine.Empty, Is.True);
            Assert.That(session.Stash.Count("part_mag_a"), Is.Zero);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(store.Path));
            Click(page, "取消预览");
            Assert.That(HasText(page, "容量 20 → 30 发"), Is.False);
            Assert.That(PreviewMagazine(), Is.SameAs(oldMesh));

            int cost = session.Catalog.Find("part_mag_a").Value;
            session.Profile.Credits = cost + 10;
            session.PersistSafeState();
            var funded = File.ReadAllBytes(store.Path);
            Click(page, "Candidate / part_mag_a");
            var stale = Node(page, "确认安装").GetComponent<Button>();
            Assert.That(stale.interactable, Is.False, "Only a drawn preview can be confirmed.");
            stale.onClick.Invoke();
            CollectionAssert.AreEqual(funded, File.ReadAllBytes(store.Path));
            yield return null;
            Assert.That(stale.interactable, Is.True);
            Click(page, "取消预览");
            Click(page, "Candidate / part_mag_a");
            var secondConfirm = Node(page, "确认安装").GetComponent<Button>();
            secondConfirm.onClick.Invoke();
            CollectionAssert.AreEqual(funded, File.ReadAllBytes(store.Path));
            yield return null;
            Click(page, "确认安装");
            Assert.That(session.Profile.Credits, Is.EqualTo(10));
            Assert.That(gun.Parts.Magazine.ItemId, Is.EqualTo("part_mag_a"));
            Assert.That(store.Load().Loadout.Single(x => x.InstanceId == gun.InstanceId).Parts.Magazine.InstanceId, Is.EqualTo(gun.Parts.Magazine.InstanceId));
            Assert.That(HasText(page, "已安装"), Is.True);
            Assert.That(HasText(page, "容量 30 发"), Is.True);
            Assert.That(PreviewMagazine(), Is.Not.SameAs(oldMesh));
            if (CaptureRequested()) yield return new WaitForSecondsRealtime(4.6f);
            yield return Capture("04-workshop-installed", page);

            string expectedMesh = PreviewMagazine().name;
            byte[] settled = File.ReadAllBytes(store.Path);
            Click(page, "返回仓库");
            var fittedIcon = Node(Panel("Stash"), "Equipment slot / Weapon").GetComponentInChildren<RawImage>();
            for (int i = 0; i < 20 && fittedIcon.texture == null; i++) yield return null;
            Assert.That(fittedIcon.texture, Is.Not.Null.And.Not.SameAs(stockTexture));
            session.StartPractice();
            for (int i = 0; i < 45 && session.Phase == SessionPhase.PracticeLoading; i++) yield return null;
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.Practice), session.Error);
            Assert.That(session.Player.WeaponView.transform.Find("Magazine").GetComponent<MeshFilter>().sharedMesh.name, Is.EqualTo(expectedMesh));
            Assert.That(session.Player.Weapon.Magazine.Capacity, Is.EqualTo(30));
            session.LeavePractice();
            CollectionAssert.AreEqual(settled, File.ReadAllBytes(store.Path));

            Mesh PreviewMagazine() => host.GetComponentsInChildren<WeaponVisual>().Single(view => view.transform.parent.name == "Item preview studio")
                .transform.Find("Magazine").GetComponent<MeshFilter>().sharedMesh;
        }

        [UnityTest]
        public IEnumerator PracticeReturnsToTheSevenRegionHudAndReflectsSurgery()
        {
            OpenSession(0);
            session.StartPractice();
            for (int i = 0; i < 45 && session.Phase == SessionPhase.PracticeLoading; i++) yield return null;
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.Practice), session.Error);
            Invoke(session, "OnApplicationFocus", true);
            Assert.That(session.Input.Gameplay, Is.True);
            var hud = (RectTransform)session.UI.transform.Find(Root + "HUD");
            ReadVitals();
            var bytes = File.ReadAllBytes(store.Path);
            session.UI.BackOrSettings();
            Click(Panel("Game settings"), "靶场控制");
            Click(Panel("Practice controls"), "模拟受伤");
            Assert.That(session.UI.BlocksGameplay, Is.False);
            Assert.That(session.Input.Gameplay, Is.True);
            ReadVitals();
            Assert.That(Node(hud, "Region injury / LeftArm").GetComponent<Text>().text, Is.EqualTo("轻度出血"));
            Assert.That(Node(hud, "Region injury / LeftLeg").GetComponent<Text>().text, Is.EqualTo("骨折"));
            yield return Capture("05-practice-hud", hud);
            Click(hud, "Open body condition");
            Assert.That(Panel("Body condition"), Is.Not.Null);
            Click(Panel("Body condition"), "返回行动");
            Invoke(session, "PracticeSevereSample");
            var health = session.Player.Health;
            float maximum = health.Body.TotalMaximum;
            Assert.That(Invoke(health, "TryTreat", BodyPart.RightLeg, session.Catalog.Find("surgery")), Is.EqualTo(true));
            Assert.That(health.Body.TotalMaximum, Is.LessThan(maximum));
            Invoke(session.UI, "UpdateHud", true);
            ReadVitals();
            Assert.That(Node(hud, "Region injury / RightLeg").GetComponent<Text>().text, Does.Not.Contain("损毁"));
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(store.Path));

            void ReadVitals()
            {
                var body = session.Player.Health.Body;
                for (int i = 0; i < BodyCondition.Count; i++)
                {
                    var part = (BodyPart)i;
                    Assert.That(Node(hud, "Region health / " + part).GetComponent<Text>().text,
                        Is.EqualTo(BodyCondition.Name(part) + " " + Values(body.Current(part), body.Maximum(part))));
                }
                Assert.That(((Text)Field(session.UI, "healthReadout")).text, Is.EqualTo("总健康  " + Values(body.Total, body.TotalMaximum)));
                Assert.That(((Text)Field(session.UI, "staminaReadout")).text, Does.StartWith("体力"));
            }
        }

        [UnityTest]
        public IEnumerator ShopDisplaysSeparatedGearTabsInQualityOrder()
        {
            OpenSession(0);
            Invoke(session.UI, "ShowShop", ItemKind.Rig);
            yield return null;
            var page = Panel("Supply store");
            var tabs = Node(page, "Shop categories");
            var distinct = new[] { ("弹挂", ItemKind.Rig, 5), ("背包", ItemKind.Pack, 6), ("头盔", ItemKind.Helmet, 6), ("护甲", ItemKind.Armor, 6) };
            foreach (var tab in distinct)
            {
                Click(tabs, tab.Item1);
                yield return null;
                tabs = Node(page, "Shop categories");
                var cards = page.Cast<Transform>().Where(node => node.gameObject.activeInHierarchy && node.name.StartsWith("Stock / ")).ToArray();
                Assert.That(cards.Length, Is.GreaterThanOrEqualTo(6));
                CollectionAssert.AreEqual(session.Catalog.Stock(tab.Item2).Take(8).Select(item => "Stock / " + item.Id), cards.Select(card => card.name));
                var badge = Node(cards[0], "Stock quality").GetComponent<Text>();
                Assert.That(badge.text, Is.EqualTo(tab.Item3 + " 级"));
                Assert.That(badge.color, Is.EqualTo(ItemDefinition.GradeColor(tab.Item3)));
            }
            yield return Capture("06-shop-armor", page);
        }

        void OpenSession(int credits)
        {
            var catalog = Resources.Load<ItemCatalog>("Dustline/Items");
            Assert.That(catalog, Is.Not.Null, "Run Dustline > Prepare Project first.");
            var stash = new GridInventory(12, 10, 300f, catalog);
            var loadout = GridInventory.Carried(catalog);
            foreach (var entry in new[] { ("rifle", EquipmentSlot.Weapon), ("vest_4", EquipmentSlot.Armor), ("rig_4", EquipmentSlot.Rig), ("pack_4", EquipmentSlot.Pack) })
                Assert.That(loadout.TryEquip(Add(loadout, entry.Item1).InstanceId, entry.Item2, out _), Is.True);
            var rifle = Add(stash, "dust_marksman");
            Assert.That(stash.TryMove(rifle.InstanceId, 1, 1, false, out _), Is.True);
            Add(stash, "ammo_amber_range"); Add(stash, "bandage"); Add(stash, "helmet_6"); Add(stash, "medkit");
            var bandage = Add(loadout, "bandage");
            Assert.That(loadout.TryMove(bandage.InstanceId, CargoArea.Rig, 0, 0, false, out _), Is.True);
            Add(loadout, "medkit");
            store = new ProfileStore(Path.Combine(Application.temporaryCachePath, "dustline-wide-ui-" + Guid.NewGuid().ToString("N") + ".json"));
            store.Save(new ProfileData { Version = 2, Credits = credits, Loadout = loadout.Snapshot(), Stash = stash.Snapshot() });
            host = new GameObject("Wide UI test session");
            host.SetActive(false);
            session = host.AddComponent<GameSession>();
            typeof(GameSession).GetField("store", Instance).SetValue(session, store);
            host.SetActive(true);
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.Menu), session.Error);
            session.Input.ForceTouch = true;
        }

        static void RequireLandscape()
        {
            if (Screen.width < 650 || Screen.width < Screen.height) Assert.Ignore("Use a landscape Game view at 1536 × 707 or a similar size.");
        }

        static bool CaptureRequested() => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DUSTLINE_UI_CAPTURE_DIR"));

        static IEnumerator Capture(string name, Transform visibleRoot)
        {
            string target = Environment.GetEnvironmentVariable("DUSTLINE_UI_CAPTURE_DIR");
            if (string.IsNullOrWhiteSpace(target)) yield break;
            Assert.That(Application.isEditor && !Application.isBatchMode && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, Is.True,
                "Capture requires an interactive Unity Game view with graphics.");
            Assert.That(Screen.width / (float)Screen.height, Is.InRange(2.10f, 2.25f), "Use Game view 1536 × 707 before capture.");
            bool ready = false;
            for (int i = 0; i < 50 && !ready; i++)
            {
                yield return null;
                ready = visibleRoot.GetComponentsInChildren<RawImage>().All(image => !image.IsActive() || image.canvasRenderer.cull
                    || image.texture != null && (image.texture is not RenderTexture rt || rt.IsCreated()));
            }
            Assert.That(ready, Is.True, "Visible model previews did not finish rendering.");
            yield return new WaitForEndOfFrame();
            var previous = RenderTexture.active;
            var screenshot = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = null;
                screenshot.ReadPixels(new Rect(0, 0, screenshot.width, screenshot.height), 0, 0, false);
                screenshot.Apply(false, false);
                Directory.CreateDirectory(target);
                string path = Path.Combine(target, name + ".png");
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                {
                    byte[] bytes = screenshot.EncodeToPNG();
                    stream.Write(bytes, 0, bytes.Length);
                }
                Debug.Log("Dustline Unity Play Mode: " + screenshot.width + " × " + screenshot.height + " / " + path);
            }
            finally { RenderTexture.active = previous; Object.Destroy(screenshot); }
        }

        static void TapHotspot(RectTransform surface, RectTransform marker)
        {
            var e = new PointerEventData(EventSystem.current) { pointerId = -3, button = PointerEventData.InputButton.Left, position = ScreenPoint(marker) };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(e, hits);
            Assert.That(hits.Count, Is.GreaterThan(0));
            Assert.That(ExecuteEvents.GetEventHandler<IPointerDownHandler>(hits[0].gameObject), Is.SameAs(surface.gameObject));
            ExecuteEvents.Execute(surface.gameObject, e, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(surface.gameObject, e, ExecuteEvents.pointerUpHandler);
        }

        static Vector2 ScreenPoint(RectTransform rect) => RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        static ItemStack Add(GridInventory inventory, string id)
        {
            Assert.That(inventory.TryAdd(id, 1, out var error), Is.True, error);
            return inventory.Items.Last(item => item.ItemId == id);
        }

        RectTransform Panel(string name) => Node(session.UI.transform.Find(Root + "Pages"), name);
        static RectTransform Node(Transform owner, string name)
        {
            var node = owner.GetComponentsInChildren<RectTransform>().FirstOrDefault(candidate => candidate.name == name);
            Assert.That(node, Is.Not.Null, name);
            return node;
        }

        static bool Active(Transform owner, string name) => owner.GetComponentsInChildren<RectTransform>().Any(node => node.name == name);
        static bool HasText(Transform owner, string text) => owner.GetComponentsInChildren<Text>().Any(label => label.text.Contains(text));
        static void Click(Transform owner, string name) => Node(owner, name).GetComponent<Button>().onClick.Invoke();
        static object Field(object owner, string name) => owner.GetType().GetField(name, Instance).GetValue(owner);
        static object Invoke(object owner, string name, params object[] arguments) => owner.GetType().GetMethod(name, Instance).Invoke(owner, arguments);
        static string Values(float current, float maximum) => current.ToString("0.#", CultureInfo.InvariantCulture) + " / " + maximum.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
