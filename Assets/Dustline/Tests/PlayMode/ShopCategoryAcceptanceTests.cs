using System;
using System.Collections;
using System.Collections.Generic;
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
    public sealed class ShopCategoryAcceptanceTests
    {
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
        const string WeaponRow = "Category / 武器", WeaponPrefix = "Weapon category / ", StockPrefix = "Stock / ";
        GameObject host;
        GameSession session;
        ProfileStore store;
        readonly List<Object> fixtures = new List<Object>();
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
            foreach (var fixture in fixtures) Object.Destroy(fixture);
            fixtures.Clear();
            if (store != null) File.Delete(store.Path);
            yield return null;
            QualitySettings.renderPipeline = previousPipeline;
            QualitySettings.vSyncCount = previousSync;
            Application.targetFrameRate = previousLimit;
        }

        [UnityTest]
        public IEnumerator CategoriesExpandInPlaceAndShowEveryMatchingItemInQualityOrder()
        {
            OpenSession();
            AddExtraRifles();
            var shop = OpenShop();
            yield return null;
            AssertStock(shop, Ordered(ItemKind.Rig));
            Click(shop, "Category / 背包");
            AssertStock(shop, Ordered(ItemKind.Pack));
            foreach (var (kind, label) in new[] { (ItemKind.Armor, "护甲"), (ItemKind.Helmet, "头盔") })
            {
                Click(shop, "Category / " + label);
                Assert.That(Cards(shop), Is.Empty);
                for (int tier = 1; tier <= 6; tier++)
                {
                    Click(shop, kind + " tier / " + tier);
                    Assert.That(TextOf(shop, "Shop selection"), Is.EqualTo(label + " / " + tier + " 级"));
                    AssertStock(shop, Ordered(kind, item => item.Tier == tier));
                }
            }
            Click(shop, "Category / 弹药");
            Assert.That(TextOf(shop, "Shop empty state"), Is.EqualTo("请选择口径"));
            foreach (var (family, label) in new[] { (AmmoFamily.Amber, "5.56 毫米"), (AmmoFamily.Copper, "9 毫米"),
                (AmmoFamily.Silver, "7.62 毫米"), (AmmoFamily.Slate, "12 号") })
            {
                Click(shop, "Ammo category / " + family);
                Assert.That(TextOf(shop, "Shop selection"), Is.EqualTo("弹药 / " + label));
                AssertStock(shop, Ordered(ItemKind.Ammo, item => item.AmmoFamily == family));
            }

            Assert.That(WeaponRows(shop), Is.Empty);
            Click(shop, WeaponRow);
            Assert.That(Cards(shop), Is.Empty, "Expanding the branch does not select a weapon subtype.");
            Assert.That(TextOf(shop, "Shop empty state"), Is.EqualTo("请选择武器类型"));
            var types = new (WeaponForm? form, string label)[]
            {
                (WeaponForm.Rifle, "突击步枪"), (WeaponForm.Smg, "冲锋枪"), (WeaponForm.Lmg, "轻机枪"),
                (WeaponForm.Marksman, "射手步枪"), (WeaponForm.Sniper, "狙击步枪"), (WeaponForm.Shotgun, "霰弹枪"),
                (WeaponForm.Pistol, "手枪"), (null, "近战武器")
            };
            CollectionAssert.AreEqual(types.Select(entry => WeaponNode(entry.form)), WeaponRows(shop).Select(button => button.name));
            CollectionAssert.AreEqual(types.Select(entry => entry.label), WeaponRows(shop).Select(button => button.GetComponentInChildren<Text>().text));
            foreach (var entry in types)
            {
                Click(shop, WeaponNode(entry.form));
                var stock = Ordered(ItemKind.Weapon, item => item.Weapon != null && (entry.form.HasValue
                    ? !item.Weapon.Melee && item.Weapon.Form == entry.form.Value : item.Weapon.Melee));
                Assert.That(TextOf(shop, "Shop selection"), Is.EqualTo("武器 / " + entry.label));
                AssertStock(shop, stock);
                if (entry.form != WeaponForm.Rifle) continue;
                Assert.That(stock.Length, Is.GreaterThan(8));
                Click(shop, WeaponRow);
                Assert.That(WeaponRows(shop), Is.Empty);
                AssertStock(shop, stock);
                Click(shop, WeaponRow);
                AssertStock(shop, stock);
            }
            Assert.That(Cards(shop), Does.Contain(StockPrefix + WeaponDefinition.BatonId));
            Assert.That(shop.GetComponentsInChildren<Button>().Any(button => button.name == "上一页" || button.name == "下一页"), Is.False);
        }

        [UnityTest]
        public IEnumerator LeftAndRightScrollStayIndependentAcrossGesturesReflowAndBack()
        {
            OpenSession();
            AddExtraRifles();
            session.UI.ShowStash();
            var shop = OpenShop();
            yield return null;
            var retiredCard = Node(shop, Cards(shop)[0]).GetComponent<Button>();
            retiredCard.onClick.Invoke();
            var retiredBuy = Details(shop).GetComponentsInChildren<Button>().Single(button => button.name.StartsWith("购买 ", StringComparison.Ordinal));
            int credits = session.Profile.Credits;
            Click(shop, WeaponRow);
            retiredBuy.onClick.Invoke();
            retiredCard.onClick.Invoke();
            Assert.That(Details(shop), Is.Null);
            Assert.That(session.Profile.Credits, Is.EqualTo(credits));

            var rail = Vertical(shop, "Shop categories");
            Canvas.ForceUpdateCanvases();
            var drag = Press(rail.viewport, 62);
            rail.OnBeginDrag(drag);
            drag.position += Vector2.up * 100;
            rail.OnDrag(drag);
            Assert.That(rail.content.anchoredPosition.y, Is.GreaterThan(0f));
            Click(shop, WeaponNode(WeaponForm.Rifle));
            Assert.That(Cards(shop), Is.Empty, "Category selection is blocked while a finger owns the scroll.");
            rail.OnEndDrag(drag);
            rail.verticalNormalizedPosition = .65f;
            float leftOffset = rail.content.anchoredPosition.y;
            yield return null;
            Click(shop, WeaponNode(WeaponForm.Rifle));
            Canvas.ForceUpdateCanvases();
            var stock = Vertical(shop, "Shop stock viewport");
            var tile = Node(stock.content, Cards(shop)[0]);
            var swipe = Press(tile, 63);
            ExecuteEvents.Execute(tile.gameObject, swipe, ExecuteEvents.beginDragHandler);
            swipe.position += Vector2.up * 100;
            ExecuteEvents.Execute(tile.gameObject, swipe, ExecuteEvents.dragHandler);
            ExecuteEvents.Execute(tile.gameObject, swipe, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(tile.gameObject, swipe, ExecuteEvents.pointerClickHandler);
            Assert.That(Details(shop), Is.Null, "A swipe starting on a product may not open it on release.");
            Assert.That(stock.content.anchoredPosition.y, Is.GreaterThan(0f));
            Assert.That(Vertical(shop, "Shop categories").content.anchoredPosition.y, Is.EqualTo(leftOffset).Within(.05f));
            stock.verticalNormalizedPosition = .33f;
            float rightOffset = stock.content.anchoredPosition.y;
            yield return null;

            Click(shop, "Category / 配件");
            Click(shop, WeaponNode(WeaponForm.Rifle));
            AssertOffsets();
            Click(shop, Cards(shop)[4]);
            Assert.That(Details(shop), Is.Not.Null);
            ((Action)typeof(GameUI).GetField("reflowWidePage", Instance).GetValue(session.UI)).Invoke();
            Assert.That(Details(shop), Is.Null);
            AssertOffsets();
            session.UI.ShowStash();
            session.UI.BackOrSettings();
            shop = Panel("Supply store");
            AssertOffsets();
            yield return null;
            string card = Cards(shop)[4];
            Click(shop, card);
            session.UI.BackOrSettings();
            Click(shop, card);
            Assert.That(Details(shop), Is.Null, "The release that dismisses details may not open another product.");
            AssertOffsets();
            yield return null;
            Click(shop, "‹ 返回");
            Assert.That(Panel("Stash"), Is.Not.Null);

            void AssertOffsets()
            {
                Assert.That(TextOf(shop, "Shop selection"), Is.EqualTo("武器 / 突击步枪"));
                Assert.That(Vertical(shop, "Shop categories").content.anchoredPosition.y, Is.EqualTo(leftOffset).Within(.05f));
                Assert.That(Vertical(shop, "Shop stock viewport").content.anchoredPosition.y, Is.EqualTo(rightOffset).Within(.05f));
            }
        }

        [UnityTest]
        public IEnumerator AmmoStartsAtOneAndPurchasesOneNineteenAndTwentyOneExactlyOnce()
        {
            OpenSession();
            var item = session.Catalog.Find("ammo_slate");
            var shop = OpenShop(ItemKind.Ammo);
            yield return null;
            Click(shop, "Ammo category / Slate");
            int additions = 0, purchases = 0, total = 0;
            session.Stash.Changed += () => additions++;
            int credits = session.Profile.Credits;
            foreach (int units in new[] { 1, 19, 21 })
            {
                byte[] before = File.ReadAllBytes(store.Path);
                Click(shop, StockPrefix + item.Id);
                var popup = Details(shop);
                var slider = Node(popup, "Ammo quantity").GetComponent<Slider>();
                Assert.That(slider.wholeNumbers, Is.True);
                Assert.That(slider.value, Is.EqualTo(1f));
                Assert.That(slider.minValue, Is.EqualTo(1f));
                Assert.That(Node(popup, "Ammo minus").GetComponent<Button>().interactable, Is.False);
                if (units == 19)
                {
                    var confirm = Node(popup, "Ammo confirm");
                    var oldPress = Press(confirm, 64);
                    slider.value = 18.9f;
                    ExecuteEvents.Execute(confirm.gameObject, oldPress, ExecuteEvents.pointerUpHandler);
                    ExecuteEvents.Execute(confirm.gameObject, oldPress, ExecuteEvents.pointerClickHandler);
                    Assert.That(Details(shop), Is.SameAs(popup), "A confirmation pressed before the quantity changed must not execute.");
                }
                if (units == 21)
                {
                    slider.value = 21;
                    Click(popup, "Ammo minus");
                    Click(popup, "Ammo plus");
                }
                Assert.That(slider.value, Is.EqualTo(units));
                Assert.That(TextOf(popup, "Ammo amount"), Does.StartWith("数量 " + units + " 发"));
                Assert.That(TextOf(popup, "Ammo unit price"), Does.Contain(item.Value.ToString("N0")));
                Assert.That(TextOf(popup, "Ammo balance"), Is.EqualTo("余额 " + (credits - total * item.Value).ToString("N0")));
                Assert.That(TextOf(popup, "Ammo total"), Is.EqualTo("总价 " + (units * item.Value).ToString("N0") + " 信用点"));
                AssertWithin((RectTransform)popup, shop);
                Assert.That(additions, Is.EqualTo(purchases));
                CollectionAssert.AreEqual(before, File.ReadAllBytes(store.Path));
                var purchase = Node(popup, "Ammo confirm").GetComponent<Button>().onClick;
                purchase.Invoke();
                purchase.Invoke();
                total += units;
                Assert.That(additions, Is.EqualTo(++purchases));
                Assert.That(session.Stash.Count(item.Id), Is.EqualTo(total));
                Assert.That(session.Profile.Credits, Is.EqualTo(credits - total * item.Value));
                Assert.That(store.Load().Stash.Where(saved => saved.ItemId == item.Id).Sum(saved => saved.Quantity), Is.EqualTo(total));
                Assert.That(store.Load().Credits, Is.EqualTo(session.Profile.Credits));
            }
            Assert.That(additions, Is.EqualTo(3));
            Assert.That(session.Stash.Items.Count(stack => stack.ItemId == item.Id), Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator AmmoMaximumUsesActualStackRoomAndRejectsAStaleConfirmationWithoutSaving()
        {
            OpenSession();
            var item = session.Catalog.Find("ammo_slate");
            var stash = session.Stash;
            var stock = new List<ItemStack>
            {
                new ItemStack { InstanceId = "existing-shells", ItemId = item.Id, Quantity = item.MaxStack - 21 }
            };
            for (int i = 1; i < stash.Width * stash.Height; i++)
                stock.Add(new ItemStack { InstanceId = "occupied-" + i, ItemId = "bandage", X = i % stash.Width, Y = i / stash.Width });
            stash.Restore(stock);
            session.Profile.Credits = item.Value * 19;
            session.PersistSafeState();
            var shop = OpenShop(ItemKind.Ammo);
            yield return null;
            Click(shop, "Ammo category / Slate");
            Click(shop, StockPrefix + item.Id);
            var popup = Details(shop);
            var slider = Node(popup, "Ammo quantity").GetComponent<Slider>();
            Assert.That(slider.maxValue, Is.EqualTo(19));
            Click(popup, "Ammo max");
            Assert.That(slider.value, Is.EqualTo(19));

            session.Profile.Credits = item.Value * 50;
            Click(popup, "Ammo max");
            Assert.That(slider.value, Is.EqualTo(21), "The only room is in the existing stack, not a new stack or a protected slot.");
            Assert.That(stash.TryAdd(item.Id, 1, out var error), Is.True, error);
            session.PersistSafeState();
            byte[] baseline = File.ReadAllBytes(store.Path);
            int previous = stash.Count(item.Id), credits = session.Profile.Credits, commits = 0;
            stash.Changed += () => commits++;
            Click(popup, "Ammo confirm");
            Assert.That(slider.maxValue, Is.EqualTo(20));
            Assert.That(stash.Count(item.Id), Is.EqualTo(previous));
            Assert.That(session.Profile.Credits, Is.EqualTo(credits));
            Assert.That(commits, Is.Zero);
            CollectionAssert.AreEqual(baseline, File.ReadAllBytes(store.Path));
        }

        void AddExtraRifles()
        {
            var catalog = ScriptableObject.CreateInstance<ItemCatalog>();
            fixtures.Add(catalog);
            catalog.Items.AddRange(session.Catalog.Items);
            var rifle = session.Catalog.Find(WeaponDefinition.RifleId);
            for (int i = 0; i < 10; i++) Add("shop-test-rifle-" + i, i % 3 + 1, 120 - i * 3, false);
            Add("shop-test-ungraded", 0, 10, false);
            Add("shop-test-not-for-sale", 1, 1, true);
            catalog.RebuildIndex();
            typeof(GameSession).GetProperty(nameof(GameSession.Catalog)).GetSetMethod(true).Invoke(session, new object[] { catalog });

            void Add(string id, int tier, int value, bool notForSale)
            {
                var item = Object.Instantiate(rifle);
                fixtures.Add(item);
                item.Id = id;
                item.Tier = tier;
                item.Value = value;
                item.NotForSale = notForSale;
                catalog.Items.Add(item);
            }
        }

        void OpenSession()
        {
            Assert.That(Resources.Load<ItemCatalog>("Dustline/Items"), Is.Not.Null, "Run Dustline > Prepare Project first.");
            store = new ProfileStore(Path.Combine(Application.temporaryCachePath, "dustline-shop-" + Guid.NewGuid().ToString("N") + ".json"));
            store.Save(new ProfileData { Version = ProfileData.CurrentVersion, Credits = 10000, BirthdayGiftClaimed = true });
            host = new GameObject("Shop category test session");
            host.SetActive(false);
            session = host.AddComponent<GameSession>();
            typeof(GameSession).GetField("store", Instance).SetValue(session, store);
            host.SetActive(true);
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.Menu), session.Error);
        }

        RectTransform OpenShop(ItemKind kind = ItemKind.Rig)
        {
            typeof(GameUI).GetMethod("ShowShop", Instance).Invoke(session.UI, new object[] { kind });
            return Panel("Supply store");
        }

        ItemDefinition[] Ordered(ItemKind kind, Func<ItemDefinition, bool> filter = null) => session.Catalog.Items
            .Where(item => item.Kind == kind && !item.NotForSale && (filter == null || filter(item)))
            .OrderBy(item => item.Tier > 0 ? item.Tier : int.MaxValue).ThenBy(item => item.Value).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();

        static void AssertStock(RectTransform shop, ItemDefinition[] stock)
        {
            Assert.That(stock, Is.Not.Empty);
            CollectionAssert.AreEqual(stock.Select(item => StockPrefix + item.Id), Cards(shop));
            Assert.That(TextOf(shop, "Shop stock count"), Is.EqualTo(stock.Length + " 件商品"));
        }

        RectTransform Panel(string name) => Node(session.UI.transform.Find("Dustline UI/Safe area/Pages"), name);
        static RectTransform Node(Transform owner, string name)
        {
            var node = owner.GetComponentsInChildren<RectTransform>().FirstOrDefault(rect => rect.name == name);
            Assert.That(node, Is.Not.Null, name);
            return node;
        }

        static ScrollRect Vertical(Transform owner, string name)
        {
            var viewport = Node(owner, name);
            var scroll = viewport.GetComponent<ScrollRect>();
            Assert.That(viewport.GetComponent<RectMask2D>(), Is.Not.Null);
            Assert.That(scroll, Is.Not.Null);
            Assert.That(scroll.vertical && !scroll.horizontal, Is.True);
            return scroll;
        }

        static PointerEventData Press(RectTransform tile, int pointer)
        {
            var point = RectTransformUtility.WorldToScreenPoint(null, tile.TransformPoint(tile.rect.center));
            var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, pointerId = pointer,
                pressPosition = point, position = point, eligibleForClick = true };
            ExecuteEvents.Execute(tile.gameObject, data, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(tile.gameObject, data, ExecuteEvents.initializePotentialDrag);
            return data;
        }

        static void AssertWithin(RectTransform popup, RectTransform shop)
        {
            var corners = new Vector3[4];
            popup.GetWorldCorners(corners);
            foreach (var corner in corners) Assert.That(shop.rect.Contains((Vector2)shop.InverseTransformPoint(corner)), Is.True);
        }

        static Transform Details(Transform shop) => shop.Cast<Transform>().FirstOrDefault(node => node.name == "Item details" && node.gameObject.activeInHierarchy);
        static string WeaponNode(WeaponForm? form) => WeaponPrefix + (form?.ToString() ?? "Melee");
        static Button[] WeaponRows(Transform shop) => Node(shop, "Shop categories").GetComponentsInChildren<Button>()
            .Where(button => button.name.StartsWith(WeaponPrefix, StringComparison.Ordinal)).ToArray();
        static string[] Cards(Transform shop) => Node(shop, "Shop stock content").GetComponentsInChildren<Button>()
            .Where(button => button.name.StartsWith(StockPrefix, StringComparison.Ordinal)).Select(button => button.name).ToArray();
        static string TextOf(Transform owner, string name) => Node(owner, name).GetComponent<Text>().text;
        static void Click(Transform owner, string name)
        {
            var button = Node(owner, name).GetComponent<Button>();
            Assert.That(button.IsInteractable(), Is.True, name);
            button.onClick.Invoke();
        }
    }
}
