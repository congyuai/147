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
            Assert.That(wearer.rect.width, Is.LessThan(storage.rect.width));
            Assert.That(Node(contents, "Equipment categories").rect.width, Is.LessThan(wearer.rect.width));
            var main = Node(wearer, "Equipment slot / Weapon");
            var secondary = Node(wearer, "Equipment slot / Sidearm");
            var mainTwo = Node(wearer, "Equipment slot / PrimaryTwo");
            var melee = Node(wearer, "Equipment slot / Melee");
            var helmet = Node(wearer, "Equipment slot / Helmet");
            var armor = Node(wearer, "Equipment slot / Armor");
            Assert.That(main.rect.width / secondary.rect.width, Is.EqualTo(2f).Within(.01f));
            Assert.That(main.anchoredPosition.y, Is.EqualTo(secondary.anchoredPosition.y));
            Assert.That(mainTwo.anchoredPosition.y, Is.EqualTo(melee.anchoredPosition.y).And.LessThan(main.anchoredPosition.y));
            Assert.That(helmet.rect.width, Is.EqualTo(armor.rect.width));
            Assert.That(helmet.Find("Empty equipment outline"), Is.Not.Null);
            Assert.That(armor.GetComponentInChildren<RawImage>(), Is.Not.Null);
            Assert.That(Node(wearer, "Capacity / Rig").GetComponent<Text>().text, Is.EqualTo("弹挂 1/12"));
            Assert.That(Node(wearer, "Capacity / Pockets").GetComponent<Text>().text, Is.EqualTo("口袋 0/5"));
            Assert.That(Node(wearer, "Capacity / Main").GetComponent<Text>().text, Is.EqualTo("背包 2/56"));
            Assert.That(Active(page, "Item details"), Is.False);

            var grid = Node(storage, "Item grid").GetComponent<InventoryBoard>();
            Assert.That(grid.GetComponent<CanvasRenderer>(), Is.Not.Null);
            float cell = ((RectTransform)grid.transform).rect.width / 12f;
            Assert.That(((RectTransform)grid.transform).rect.height, Is.EqualTo(30 * cell).Within(.02));
            Assert.That(((RectTransform)grid.transform.parent.parent).rect.width, Is.GreaterThanOrEqualTo(12 * cell));
            var stack = session.Stash.Items.Single(item => item.ItemId == "dust_marksman");
            var tile = Node(grid.transform, session.Catalog.Find(stack.ItemId).DisplayName);
            Assert.That(tile.rect.width, Is.GreaterThan(3f * cell));
            Assert.That(tile.GetComponentInChildren<RawImage>().name, Is.EqualTo("Item model / dust_marksman"));
            var helmetDefinition = session.Catalog.Find("helmet_6");
            var shellTiles = grid.GetComponentsInChildren<Button>().Where(button => button.name == helmetDefinition.DisplayName).ToArray();
            CollectionAssert.AreEquivalent(new[] { (helmetDefinition.Durability - 17) + "/" + helmetDefinition.Durability,
                (helmetDefinition.Durability - 5) + "/" + helmetDefinition.Durability },
                shellTiles.Select(button => Node(button.transform, "Item readout").GetComponent<Text>().text));
            yield return Capture("01-stash", page);
            var tap = Press(tile, ScreenPoint(tile), 41);
            ExecuteEvents.Execute(tile.gameObject, tap, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(tile.gameObject, tap, ExecuteEvents.pointerClickHandler);
            Assert.That(Active(page, "Item details"), Is.True);
            Click(page, "收起");
            Assert.That(Active(page, "Item details"), Is.False);

            var before = File.ReadAllBytes(store.Path);
            var rect = (RectTransform)grid.transform;
            var scroll = grid.GetComponentInParent<ScrollRect>();
            Vector2 press = CellPoint(rect, cell, stack.X, stack.Y);
            var swipe = Press(tile, press, 40);
            ExecuteEvents.Execute(tile.gameObject, swipe, ExecuteEvents.beginDragHandler);
            swipe.position += Vector2.up * 45f;
            ExecuteEvents.Execute(tile.gameObject, swipe, ExecuteEvents.dragHandler);
            Assert.That((int)Field(session.UI, "dragPointer"), Is.EqualTo(int.MinValue));
            if (scroll != null) Assert.That(scroll.content.anchoredPosition.y, Is.GreaterThan(0f));
            ExecuteEvents.Execute(tile.gameObject, swipe, ExecuteEvents.pointerUpHandler);
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;
            yield return null;
            Canvas.ForceUpdateCanvases();

            var drag = Press(tile, CellPoint(rect, cell, stack.X, stack.Y), 42);
            yield return new WaitForSecondsRealtime(.38f);
            Assert.That((int)Field(session.UI, "dragPointer"), Is.EqualTo(42));
            float scrollBefore = scroll == null ? 0f : scroll.content.anchoredPosition.y;
            var secondFinger = new PointerEventData(EventSystem.current) { pointerId = 43, button = PointerEventData.InputButton.Left, position = drag.position };
            if (scroll != null)
            {
                scroll.OnBeginDrag(secondFinger);
                secondFinger.position += Vector2.up * 40;
                scroll.OnDrag(secondFinger);
                scroll.OnEndDrag(secondFinger);
                Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(scrollBefore).Within(.001f));
            }
            drag.position = CellPoint(rect, cell, 7, 1);
            ExecuteEvents.Execute(tile.gameObject, drag, ExecuteEvents.dragHandler);
            var footprint = Node(grid.transform, "Drop footprint").GetComponent<Graphic>();
            Assert.That(footprint.GetComponent<CanvasRenderer>(), Is.Not.Null);
            Assert.That((int)Field(footprint, "right") - (int)Field(footprint, "left"), Is.EqualTo(4));
            Assert.That((int)Field(footprint, "bottom") - (int)Field(footprint, "top"), Is.EqualTo(2));
            Click(page, "旋转物品 90°");
            Assert.That((int)Field(footprint, "right") - (int)Field(footprint, "left"), Is.EqualTo(2));
            Assert.That((int)Field(footprint, "bottom") - (int)Field(footprint, "top"), Is.EqualTo(4));
            Assert.That((int)Field(footprint, "left"), Is.EqualTo(6), "Rotation must preserve the grabbed cell.");
            drag.position = CellPoint(rect, cell, 8, 1);
            ExecuteEvents.Execute(tile.gameObject, drag, ExecuteEvents.dragHandler);
            Assert.That((int)Field(footprint, "left"), Is.EqualTo(7));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(store.Path));
            ExecuteEvents.Execute(tile.gameObject, drag, ExecuteEvents.pointerUpHandler);
            Assert.That(stack.Rotated, Is.True);
            Assert.That((stack.X, stack.Y), Is.EqualTo((7, 1)));
            Assert.That(store.Load().Stash.Single(item => item.InstanceId == stack.InstanceId).Rotated, Is.True);
            Assert.That(Active(page, "Item details"), Is.False);
        }

        [UnityTest]
        public IEnumerator SelectingPlacementDoesNotReuseAnOlderPressOnAnEmptyCell()
        {
            RequireLandscape();
            OpenSession(0);
            session.UI.ShowStash();
            yield return null;
            var page = Panel("Stash");
            var grid = Node(Node(page, "Stash grid region"), "Item grid");
            var item = session.Stash.Items.Single(stack => stack.ItemId == "bandage");
            var target = CellPoint(grid, grid.rect.width / 12, 7, 1);
            byte[] before = File.ReadAllBytes(store.Path);
            var oldPress = Press(grid, target, 47);
            Click(grid, session.Catalog.Find(item.ItemId).DisplayName);
            Click(page, "选格放置");
            ReleaseCell(oldPress);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(store.Path));
            Assert.That(Active(page, "Select target cell"), Is.True);

            ReleaseCell(Press(grid, target, 48));
            Assert.That((item.X, item.Y), Is.EqualTo((7, 1)));
            Assert.That(Active(page, "Select target cell"), Is.False);
            Assert.That(store.Load().Stash.Single(saved => saved.InstanceId == item.InstanceId).X, Is.EqualTo(7));

            void ReleaseCell(PointerEventData data)
            {
                ExecuteEvents.Execute(grid.gameObject, data, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(grid.gameObject, data, ExecuteEvents.pointerClickHandler);
            }
        }

        [UnityTest]
        public IEnumerator BackpackEquipsThenAcceptsADropAndShopBackRestoresBothPanes()
        {
            RequireLandscape();
            OpenSession(1000, hasBackpack: false);
            session.UI.ShowStash();
            yield return null;
            var pages = session.UI.transform.Find(Root + "Pages");
            var stashPage = Panel("Stash");
            var storage = Node(stashPage, "Stash grid region");
            var pack = session.Stash.Items.Single(item => item.ItemId == "pack_compact");
            string packName = session.Catalog.Find(pack.ItemId).DisplayName;
            Click(stashPage, "Category / 背包");
            Assert.That(Node(stashPage, "Capacity / Main").GetComponent<Text>().text, Is.EqualTo("背包 0/0"));
            Assert.That(Node(stashPage, "Equipment slot / Pack").Find("Empty equipment outline"), Is.Not.Null);
            Click(storage, packName);
            var details = Node(stashPage, "Item details");
            BoundsWithin(details, stashPage);
            Assert.That(HasText(details, "容量 28 格"), Is.True);
            Click(details, "装备到背包");
            Assert.That(session.Stash.Find(pack.InstanceId), Is.Null);
            Assert.That(session.Loadout.Equipped(EquipmentSlot.Pack).InstanceId, Is.EqualTo(pack.InstanceId));
            Assert.That(Active(Node(stashPage, "Stash grid region"), packName), Is.False);
            Assert.That(Node(stashPage, "Capacity / Main").GetComponent<Text>().text, Is.EqualTo("背包 0/28"));
            Assert.That(Node(Node(stashPage, "Category / 背包"), "Category selection").GetComponent<Image>().enabled, Is.True);
            yield return Capture("08-backpack-equipped", stashPage);

            storage = Node(stashPage, "Stash grid region");
            var bandage = session.Stash.Items.Single(item => item.ItemId == "bandage");
            var loose = Node(storage, session.Catalog.Find(bandage.ItemId).DisplayName);
            var main = Node(stashPage, "Carried section / Main");
            var packGrid = Node(main, "Item grid");
            var drag = Press(loose, ScreenPoint(loose), 44);
            yield return new WaitForSecondsRealtime(.38f);
            Assert.That((int)Field(session.UI, "dragPointer"), Is.EqualTo(44));
            drag.position = CellPoint(packGrid, packGrid.rect.width / 7f, 1, 1);
            ExecuteEvents.Execute(loose.gameObject, drag, ExecuteEvents.dragHandler);
            ExecuteEvents.Execute(loose.gameObject, drag, ExecuteEvents.pointerUpHandler);
            var moved = session.Loadout.Find(bandage.InstanceId);
            Assert.That(moved, Is.Not.Null);
            Assert.That((moved.Area, moved.X, moved.Y), Is.EqualTo((CargoArea.Main, 1, 1)));
            Assert.That(Node(stashPage, "Capacity / Main").GetComponent<Text>().text, Is.EqualTo("背包 1/28"));

            var left = Node(Node(stashPage, "Carried gear"), "Grid viewport").GetComponent<ScrollRect>();
            var right = Node(Node(stashPage, "Stash grid region"), "Grid viewport").GetComponent<ScrollRect>();
            if (right != null) right.verticalNormalizedPosition = .37f;
            float rightY = right == null ? 0f : right.content.anchoredPosition.y;
            Click(stashPage, "Category / 弹挂");
            if (right != null) Assert.That(right.content.anchoredPosition.y, Is.EqualTo(rightY).Within(.01f));
            float leftY = left.content.anchoredPosition.y;
            Assert.That(Active(stashPage, "更多"), Is.False);
            Assert.That(Active(stashPage, "一键整理"), Is.True);
            Invoke(session.UI, "ShowShop", ItemKind.Rig);
            yield return null;
            var shop = Panel("Supply store");
            var product = shop.GetComponentsInChildren<Button>().First(button => button.name.StartsWith("Stock / "));
            product.onClick.Invoke();
            var shopDetails = Node(shop, "Item details");
            BoundsWithin(shopDetails, shop);
            Assert.That(shopDetails.GetComponentsInChildren<Button>().Any(button => button.name.StartsWith("购买 ")), Is.True);
            session.UI.BackOrSettings();
            Assert.That(Active(pages, "Supply store"), Is.True);
            Assert.That(Active(shop, "Item details"), Is.False);
            yield return null;
            Click(shop, "‹ 返回");
            stashPage = Panel("Stash");
            left = Node(Node(stashPage, "Carried gear"), "Grid viewport").GetComponent<ScrollRect>();
            right = Node(Node(stashPage, "Stash grid region"), "Grid viewport").GetComponent<ScrollRect>();
            Assert.That(left.content.anchoredPosition.y, Is.EqualTo(leftY).Within(.05f));
            if (right != null) Assert.That(right.content.anchoredPosition.y, Is.EqualTo(rightY).Within(.05f));
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
            var highlight = surface.Find("Selected model part");
            Assert.That(highlight, Is.Not.Null);
            Assert.That(highlight.GetComponent<CanvasRenderer>(), Is.Not.Null);
            Assert.That(highlight.gameObject.activeSelf, Is.False);
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
            int cost = session.Catalog.Find("part_mag_a").Value;
            Assert.That(option.GetComponentsInChildren<Text>().Any(label => label.text == "商店售价 " + cost), Is.True);
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
            Click(page, "‹ 返回");
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
            var bytes = File.ReadAllBytes(store.Path);
            session.UI.BackOrSettings();
            Click(Panel("Game settings"), "靶场控制");
            Click(Panel("Practice controls"), "模拟受伤");
            Assert.That(session.UI.BlocksGameplay, Is.False);
            Assert.That(session.Input.Gameplay, Is.True);
            Assert.That(Node(hud.parent, "Stamina display").GetComponent<CanvasGroup>().alpha, Is.Zero);
            Assert.That(Active(hud, "Total health"), Is.False);
            var summary = Node(hud, "Injury summary");
            Assert.That(Active(summary, "Injury / LightBleed"), Is.True);
            Assert.That(Active(summary, "Injury / Fracture"), Is.True);
            yield return Capture("05-practice-hud", hud.parent);
            yield return null;
            Click(hud, "Open body condition");
            var detail = Panel("Body condition");
            ReadVitals(detail);
            Assert.That(Active(Node(detail, "Region / LeftArm"), "Injury / LightBleed"), Is.True);
            Assert.That(Active(Node(detail, "Region / LeftLeg"), "Injury / Fracture"), Is.True);
            yield return Capture("09-health-detail", detail);
            Click(detail, "‹ 返回");
            Invoke(session, "PracticeSevereSample");
            var health = session.Player.Health;
            float maximum = health.Body.Maximum(BodyPart.RightLeg);
            Invoke(session.UI, "UpdateHud", true);
            Assert.That(Active(summary, "Injury / Destroyed"), Is.True);
            Assert.That(Invoke(health, "TryTreat", BodyPart.RightLeg, session.Catalog.Find("surgery")), Is.EqualTo(true));
            Assert.That(health.Body.Maximum(BodyPart.RightLeg), Is.LessThan(maximum));
            Invoke(session.UI, "UpdateHud", true);
            Assert.That(Active(summary, "Injury / Destroyed"), Is.False);
            Click(hud, "Open body condition");
            ReadVitals(Panel("Body condition"));
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(store.Path));

            void ReadVitals(Transform screen)
            {
                var body = session.Player.Health.Body;
                for (int i = 0; i < BodyCondition.Count; i++)
                {
                    var part = (BodyPart)i;
                    Assert.That(Node(screen, "Region health / " + part).GetComponent<Text>().text,
                        Is.EqualTo(Values(body.Current(part), body.Maximum(part))));
                }
                Assert.That(Node(screen, "Total health").GetComponent<Text>().text,
                    Is.EqualTo("总健康  " + Values(body.Total, body.TotalMaximum)));
            }
        }

        [UnityTest]
        public IEnumerator ShopDisplaysSeparatedGearTabsInQualityOrder()
        {
            OpenSession(0);
            session.UI.ShowStash();
            Invoke(session.UI, "ShowShop", ItemKind.Rig);
            yield return null;
            var page = Panel("Supply store");
            var tabs = Node(page, "Shop categories");
            var distinct = new[] { ("弹挂", ItemKind.Rig), ("背包", ItemKind.Pack), ("头盔", ItemKind.Helmet), ("护甲", ItemKind.Armor) };
            foreach (var (caption, kind) in distinct)
            {
                Click(tabs, "Category / " + caption);
                bool tiered = kind == ItemKind.Armor || kind == ItemKind.Helmet;
                if (tiered) Click(page, kind + " tier / 4");
                yield return null;
                tabs = Node(page, "Shop categories");
                var cards = Node(page, "Shop stock content").Cast<Transform>().Where(node => node.gameObject.activeInHierarchy && node.name.StartsWith("Stock / ")).ToArray();
                var expected = session.Catalog.Items.Where(item => item.Kind == kind && !item.NotForSale && (!tiered || item.Tier == 4))
                    .OrderBy(item => item.Tier > 0 ? item.Tier : int.MaxValue)
                    .ThenBy(item => item.Value).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
                Assert.That(cards.Length, Is.GreaterThanOrEqualTo(tiered ? 2 : 6));
                CollectionAssert.AreEqual(expected.Select(item => "Stock / " + item.Id), cards.Select(card => card.name));
                for (int i = 0; i < cards.Length; i++)
                {
                    var badge = Node(cards[i], "Stock quality").GetComponent<Text>();
                    Assert.That(badge.text, Is.EqualTo(expected[i].QualityName));
                    Assert.That(badge.color, Is.EqualTo(ItemDefinition.GradeColor(expected[i].Tier)));
                    string price = expected[i].Value + " 信用点";
                    Assert.That(cards[i].GetComponentsInChildren<Text>().Any(label => label.text == price), Is.True, expected[i].Id);
                }
            }
            yield return Capture("06-shop-armor", page);
        }

        [UnityTest]
        public IEnumerator RecoveredCakeUsesItsTrueFootprintAndTheSameBackdropInPackAndMarket()
        {
            RequireLandscape();
            OpenSession(1000, recoveredCake: true);
            session.UI.ShowStash();
            yield return null;
            var cake = session.Catalog.Find(LootTable.BirthdayCake);
            Assert.That((cake.Width, cake.Height), Is.EqualTo((3, 4)));
            Assert.That(cake.ResaleValue, Is.EqualTo(LootTable.BirthdayResale));
            var stashPage = Panel("Stash");
            var storage = Node(stashPage, "Stash grid region");
            Assert.That(Active(storage, cake.DisplayName), Is.False);
            var normal = Node(storage, "Grid viewport").GetComponent<ScrollRect>();
            normal.verticalNormalizedPosition = .56f;
            float normalOffset = normal.content.anchoredPosition.y;
            Click(storage, "恢复区");
            storage = Node(stashPage, "Stash grid region");
            var recovered = Node(storage, "Recovery grid");
            var cakeTile = Node(recovered, cake.DisplayName);
            float cell = recovered.rect.width / GridInventory.StashColumns;
            Assert.That((cakeTile.rect.width + 2f) / cell, Is.EqualTo(4).Within(.01f));
            Assert.That((cakeTile.rect.height + 2f) / cell, Is.EqualTo(3).Within(.01f));
            Assert.That(Vector2.Distance(cakeTile.anchoredPosition, new Vector2(cell * 4 + 1, -cell * 2 - 1)), Is.LessThan(.01f));
            Assert.That(Quaternion.Angle(Node(cakeTile, "Item name").localRotation, Quaternion.identity), Is.LessThan(.01f));
            var backdrop = Node(cakeTile, "Special item backdrop").GetComponent<Graphic>();
            Assert.That(backdrop.raycastTarget, Is.False);
            var before = File.ReadAllBytes(store.Path);
            Click(recovered, cake.DisplayName);
            Assert.That(HasText(Node(stashPage, "Item details"), "占格 4 × 3"), Is.True);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(store.Path));
            Click(stashPage, "转入随身栏");
            var item = session.Loadout.Find("recovered-cake");
            Assert.That((item.Area, item.Rotated), Is.EqualTo((CargoArea.Main, true)));
            storage = Node(stashPage, "Stash grid region");
            normal = Node(storage, "Grid viewport").GetComponent<ScrollRect>();
            Assert.That(normal.content.anchoredPosition.y, Is.EqualTo(normalOffset).Within(.05f));
            Assert.That(Active(storage, "恢复区"), Is.False);
            var pack = Node(stashPage, "Carried section / Main");
            Assert.That(Node(Node(pack, cake.DisplayName), "Special item backdrop").GetComponent<Graphic>().GetType(), Is.EqualTo(backdrop.GetType()));

            session.UI.ShowMarket();
            InjectMarketFixture();
            var market = Panel("Account market");
            Click(market, "出售");
            Assert.That(HasText(market, cake.DisplayName), Is.False);
            Click(market, "恢复区 · 1");
            var card = Node(market, "Market card");
            Assert.That(HasText(card, cake.DisplayName), Is.True);
            card.GetComponentInChildren<Button>().onClick.Invoke();
            var detail = Node(market, "Item details");
            Assert.That(HasText(detail, "恢复区物品"), Is.True);
            var marketBackdrop = Node(detail, "Special item backdrop").GetComponent<Graphic>();
            Assert.That(marketBackdrop.GetType(), Is.EqualTo(backdrop.GetType()));
            Assert.That(Active(detail, "确认上架"), Is.True);
            Click(market, "系统回收");
            Click(market, "恢复区 · 1");
            Node(market, "Market card").GetComponentInChildren<Button>().onClick.Invoke();
            Assert.That(Active(Node(market, "Item details"), "确认系统回收"), Is.True);
            Click(market, "出售");
            Node(market, "Market card").GetComponentInChildren<Button>().onClick.Invoke();
            detail = Node(market, "Item details");
            var price = Node(detail, "Market unit price").GetComponent<InputField>();
            Assert.That(price.text, Is.EqualTo("80000"));
            price.text = "1";
            Assert.That(Node(detail, "确认上架").GetComponent<Button>().interactable, Is.True);

            void InjectMarketFixture()
            {
                var client = Field(session.UI, "marketClient");
                var stateProperty = client.GetType().GetProperty("State", Instance);
                var state = Activator.CreateInstance(stateProperty.PropertyType, true);
                var type = state.GetType();
                type.GetField("Account").SetValue(state, "test-account");
                type.GetField("Items").SetValue(state, new[]
                {
                    new ItemStack { ItemId = cake.Id, InstanceId = "market-recovered", Area = CargoArea.Legacy },
                    new ItemStack { ItemId = "part_scope_6", InstanceId = "market-optic" }
                });
                foreach (var name in new[] { "Products", "References", "Listings", "History" })
                {
                    var field = type.GetField(name);
                    var element = field.FieldType.GetElementType();
                    var array = Array.CreateInstance(element, name == "Products" ? 1 : 0);
                    if (name == "Products")
                    {
                        var product = Activator.CreateInstance(element, true);
                        element.GetField("Id").SetValue(product, cake.Id);
                        element.GetField("Resale").SetValue(product, (long)cake.ResaleValue);
                        array.SetValue(product, 0);
                    }
                    field.SetValue(state, array);
                }
                stateProperty.GetSetMethod(true).Invoke(client, new object[] { state });
                client.GetType().GetField("token", Instance).SetValue(client, "fixture");
                Invoke(session.UI, "DrawMarket");
            }
        }

        [UnityTest]
        public IEnumerator MeleePreviewAndItsResetPlaceTheGripLeftOfTheTip()
        {
            OpenSession(0);
            var type = typeof(GameUI).Assembly.GetType("Dustline.ItemPreviewStage");
            var stage = Activator.CreateInstance(type, Instance, null, new object[] { host.transform, 0 }, CultureInfo.InvariantCulture);
            try
            {
                var root = (Transform)Field(stage, "Root");
                var camera = root.GetComponentInChildren<Camera>();
                foreach (var blade in new[] { WeaponForm.Tangdao, WeaponForm.ShortBlade, WeaponForm.Baton })
                {
                    Invoke(stage, "Set", null, null, session.Catalog, blade, false);
                    Check();
                    Invoke(stage, "Rotate", new Vector2(150, 0));
                    Invoke(stage, "ResetView");
                    Check();
                }
                void Check()
                {
                    Invoke(stage, "Frame", 2f);
                    var view = root.GetComponentInChildren<WeaponVisual>();
                    float grip = camera.WorldToViewportPoint(view.RightGrip).x;
                    float tip = camera.WorldToViewportPoint(view.Muzzle.position).x;
                    Assert.That(tip, Is.GreaterThan(grip + .1f));
                }
            }
            finally { ((IDisposable)stage).Dispose(); }
            yield return null;
        }

        void OpenSession(int credits, bool hasBackpack = true, bool recoveredCake = false)
        {
            var catalog = Resources.Load<ItemCatalog>("Dustline/Items");
            Assert.That(catalog, Is.Not.Null, "Run Dustline > Prepare Project first.");
            var stash = GridInventory.Warehouse(catalog);
            var loadout = GridInventory.Carried(catalog);
            var worn = new List<ItemStack>();
            foreach (var entry in new[] { ("rifle", EquipmentSlot.Weapon), ("vest_4", EquipmentSlot.Armor), ("rig_4", EquipmentSlot.Rig), ("pack_4", EquipmentSlot.Pack) })
                if (hasBackpack || entry.Item2 != EquipmentSlot.Pack)
                    worn.Add(new ItemStack { ItemId = entry.Item1, InstanceId = "worn-" + entry.Item1, Slot = entry.Item2 });
            loadout.Restore(worn);
            var rifle = Add(stash, "dust_marksman");
            Assert.That(stash.TryMove(rifle.InstanceId, 1, 1, false, out _), Is.True);
            Add(stash, "ammo_amber_range");
            Add(stash, "bandage");
            var shell = Add(stash, "helmet_6");
            Assert.That(stash.TryMove(shell.InstanceId, 9, 5, false, out _), Is.True);
            var spareShell = Add(stash, "helmet_6");
            Assert.That(stash.TryMove(spareShell.InstanceId, 5, 5, false, out _), Is.True);
            shell.Wear = 17;
            spareShell.Wear = 5;
            var kit = Add(stash, "medkit");
            Assert.That(stash.TryMove(kit.InstanceId, 10, 7, false, out _), Is.True);
            if (!hasBackpack)
            {
                var pack = Add(stash, "pack_compact");
                Assert.That(stash.TryMove(pack.InstanceId, 9, 0, false, out _), Is.True);
            }
            if (recoveredCake)
            {
                var saved = stash.Snapshot();
                saved.Add(new ItemStack { ItemId = LootTable.BirthdayCake, InstanceId = "recovered-cake", Area = CargoArea.Legacy, X = 4, Y = 2, Rotated = true });
                stash.Restore(saved);
            }
            var bandage = Add(loadout, "bandage");
            Assert.That(loadout.TryMove(bandage.InstanceId, CargoArea.Rig, 0, 0, false, out _), Is.True);
            if (hasBackpack) Add(loadout, "medkit");
            store = new ProfileStore(Path.Combine(Application.temporaryCachePath, "dustline-wide-ui-" + Guid.NewGuid().ToString("N") + ".json"));
            store.Save(new ProfileData { Version = ProfileData.CurrentVersion, Credits = credits, BirthdayGiftClaimed = true,
                Loadout = loadout.Snapshot(), Stash = stash.Snapshot() });
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
            yield return null;
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

        static PointerEventData Press(RectTransform tile, Vector2 point, int id)
        {
            var e = new PointerEventData(EventSystem.current) { pointerId = id, button = PointerEventData.InputButton.Left,
                pressPosition = point, position = point, eligibleForClick = true };
            ExecuteEvents.Execute(tile.gameObject, e, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(tile.gameObject, e, ExecuteEvents.initializePotentialDrag);
            return e;
        }

        static Vector2 CellPoint(RectTransform rect, float cell, int x, int y) => RectTransformUtility.WorldToScreenPoint(null,
            rect.TransformPoint(new Vector3(rect.rect.xMin + cell * (x + .5f), rect.rect.yMax - cell * (y + .5f))));

        static void BoundsWithin(RectTransform card, RectTransform panel)
        {
            var corners = new Vector3[4];
            card.GetWorldCorners(corners);
            foreach (var corner in corners)
                Assert.That(panel.rect.Contains((Vector2)panel.InverseTransformPoint(corner)), Is.True);
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
