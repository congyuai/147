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
        public IEnumerator WeaponTreeStartsUnselectedAndFiltersAllEightBranchesBeforePaging()
        {
            OpenSession();
            var catalog = ScriptableObject.CreateInstance<ItemCatalog>();
            fixtures.Add(catalog);
            catalog.Items.AddRange(session.Catalog.Items);
            var rifle = session.Catalog.Find(WeaponDefinition.RifleId);
            for (int i = 0; i < 10; i++) AddRifle("shop-test-rifle-" + i, i % 3 + 1, 120 - i * 3, false);
            AddRifle("shop-test-ungraded", 0, 10, false);
            AddRifle("shop-test-not-for-sale", 1, 1, true);
            catalog.RebuildIndex();
            typeof(GameSession).GetProperty(nameof(GameSession.Catalog)).GetSetMethod(true).Invoke(session, new object[] { catalog });

            var shop = OpenShop();
            yield return null;
            var gear = new[] { (ItemKind.Rig, "弹挂"), (ItemKind.Pack, "背包"), (ItemKind.Armor, "护甲"), (ItemKind.Helmet, "头盔") };
            foreach (var entry in gear)
            {
                Click(Node(shop, "Shop categories"), "Category / " + entry.Item2);
                Assert.That(TextOf(shop, "Shop selection"), Is.EqualTo(entry.Item2));
                var stock = Ordered(entry.Item1, _ => true);
                Assert.That(stock, Is.Not.Empty);
                AssertPage(shop, stock, 0);
            }

            Assert.That(WeaponRows(shop), Is.Empty);
            Click(shop, WeaponRow);
            Assert.That(Cards(shop), Is.Empty, "Expanding the branch is not a weapon filter selection.");
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
                Assert.That(stock, Is.Not.Empty, entry.label);
                Assert.That(TextOf(shop, "Shop selection"), Is.EqualTo("武器 / " + entry.label));
                AssertPage(shop, stock, 0);
                if (entry.form == WeaponForm.Rifle)
                {
                    Click(shop, "下一页");
                    AssertPage(shop, stock, 1);
                    Click(shop, WeaponRow);
                    Assert.That(WeaponRows(shop), Is.Empty);
                    AssertPage(shop, stock, 1);
                    Click(shop, WeaponRow);
                    AssertPage(shop, stock, 1);
                }
            }
            Assert.That(Cards(shop), Does.Contain(StockPrefix + WeaponDefinition.BatonId));

            void AddRifle(string id, int tier, int value, bool notForSale)
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

        [UnityTest]
        public IEnumerator ShopKeepsFilterPageAndScrollWhileClosingDetailsBeforeBackAndReflow()
        {
            OpenSession();
            session.UI.ShowStash();
            var shop = OpenShop();
            yield return null;
            var retiredCard = Node(shop, Cards(shop)[0]).GetComponent<Button>();
            retiredCard.onClick.Invoke();
            var retiredBuy = BuyButton(shop);
            int credits = session.Profile.Credits;
            Click(shop, WeaponRow);
            retiredBuy.onClick.Invoke();
            retiredCard.onClick.Invoke();
            Assert.That(Details(shop), Is.Null);
            Assert.That(session.Profile.Credits, Is.EqualTo(credits));

            var viewport = Node(shop, "Shop categories");
            var scroll = viewport.GetComponent<ScrollRect>();
            Assert.That(viewport.GetComponent<RectMask2D>(), Is.Not.Null);
            Assert.That(scroll.vertical && !scroll.horizontal, Is.True);
            Canvas.ForceUpdateCanvases();
            var point = RectTransformUtility.WorldToScreenPoint(null, viewport.TransformPoint(viewport.rect.center));
            var drag = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left, pointerId = 62, pressPosition = point, position = point
            };
            scroll.OnInitializePotentialDrag(drag);
            scroll.OnBeginDrag(drag);
            drag.position += Vector2.up * 100;
            scroll.OnDrag(drag);
            Assert.That(scroll.content.anchoredPosition.y, Is.GreaterThan(0f));
            Click(shop, WeaponNode(WeaponForm.Rifle));
            Assert.That(Cards(shop), Is.Empty, "A category tap during the scroll must not change the filter.");
            scroll.OnEndDrag(drag);
            scroll.verticalNormalizedPosition = .4f;
            float offset = scroll.content.anchoredPosition.y;

            Click(shop, "Category / 弹药");
            var ammo = Ordered(ItemKind.Ammo, _ => true);
            AssertPage(shop, ammo, 0);
            Click(shop, "下一页");
            AssertAmmoPage();
            ItemDefinition item = ammo[8];
            string card = StockPrefix + item.Id;
            Click(shop, card);
            var details = Details(shop);
            Assert.That(details, Is.Not.Null);
            var detailsRect = (RectTransform)details;
            var corners = new Vector3[4];
            detailsRect.GetWorldCorners(corners);
            foreach (var corner in corners)
                Assert.That(shop.rect.Contains((Vector2)shop.InverseTransformPoint(corner)), Is.True);
            var buy = BuyButton(shop);
            var specs = details.GetComponentsInChildren<Text>().First(text => text.text.Contains("占格 ")).rectTransform;
            Assert.That(((RectTransform)buy.transform).anchoredPosition.y, Is.EqualTo(-164f));
            Assert.That(-specs.anchoredPosition.y + specs.rect.height, Is.LessThanOrEqualTo(164f));
            ((Action)typeof(GameUI).GetField("reflowWidePage", Instance).GetValue(session.UI)).Invoke();
            buy.onClick.Invoke();
            Assert.That(session.Profile.Credits, Is.EqualTo(credits));
            Assert.That(Details(shop), Is.Null);
            AssertAmmoPage();

            session.UI.ShowStash();
            session.UI.BackOrSettings();
            shop = Panel("Supply store");
            AssertAmmoPage();
            yield return null;
            Click(shop, card);
            buy = BuyButton(shop);
            var purchase = buy.onClick;
            int amount = Mathf.Min(20, item.MaxStack);
            purchase.Invoke();
            int remaining = credits - item.Value * amount;
            Assert.That(session.Profile.Credits, Is.EqualTo(remaining));
            Assert.That(session.Stash.Count(item.Id), Is.EqualTo(amount));
            Assert.That(store.Load().Credits, Is.EqualTo(remaining));
            Assert.That(shop.GetComponentsInChildren<Text>().Any(text => text.text == remaining + " 信用点"), Is.True);
            session.UI.BackOrSettings();
            purchase.Invoke();
            Click(shop, card);
            Assert.That(session.Profile.Credits, Is.EqualTo(remaining));
            Assert.That(Details(shop), Is.Null);
            AssertAmmoPage();
            yield return null;
            purchase.Invoke();
            Assert.That(session.Profile.Credits, Is.EqualTo(remaining));
            Click(shop, "‹ 返回");
            Assert.That(Panel("Stash"), Is.Not.Null);

            void AssertAmmoPage()
            {
                Assert.That(TextOf(shop, "Shop selection"), Is.EqualTo("弹药"));
                AssertPage(shop, ammo, 1);
                Assert.That(Node(shop, "Shop categories").GetComponent<ScrollRect>().content.anchoredPosition.y,
                    Is.EqualTo(offset).Within(.05f));
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

        RectTransform OpenShop()
        {
            typeof(GameUI).GetMethod("ShowShop", Instance).Invoke(session.UI, new object[] { ItemKind.Rig });
            return Panel("Supply store");
        }

        ItemDefinition[] Ordered(ItemKind kind, Func<ItemDefinition, bool> filter) => session.Catalog.Items
            .Where(item => item.Kind == kind && !item.NotForSale && filter(item))
            .OrderBy(item => item.Tier > 0 ? item.Tier : int.MaxValue).ThenBy(item => item.Value).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();

        static void AssertPage(RectTransform shop, ItemDefinition[] stock, int page)
        {
            CollectionAssert.AreEqual(stock.Skip(page * 8).Take(8).Select(item => StockPrefix + item.Id), Cards(shop));
            Assert.That(TextOf(shop, "Shop page count"), Is.EqualTo((page + 1) + " / " + Math.Max(1, (stock.Length + 7) / 8)));
        }

        RectTransform Panel(string name) => Node(session.UI.transform.Find("Dustline UI/Safe area/Pages"), name);
        static RectTransform Node(Transform owner, string name)
        {
            var node = owner.GetComponentsInChildren<RectTransform>().FirstOrDefault(rect => rect.name == name);
            Assert.That(node, Is.Not.Null, name);
            return node;
        }

        static Transform Details(Transform shop) => shop.Cast<Transform>().FirstOrDefault(node => node.name == "Item details" && node.gameObject.activeInHierarchy);
        static Button BuyButton(Transform shop) => Details(shop).GetComponentsInChildren<Button>().Single(button => button.name.StartsWith("购买 ", StringComparison.Ordinal));
        static string WeaponNode(WeaponForm? form) => WeaponPrefix + (form?.ToString() ?? "Melee");
        static Button[] WeaponRows(Transform shop) => Node(shop, "Shop categories").GetComponentsInChildren<Button>()
            .Where(button => button.name.StartsWith(WeaponPrefix, StringComparison.Ordinal)).ToArray();
        static string[] Cards(Transform shop) => shop.Cast<Transform>().Where(node => node.gameObject.activeInHierarchy && node.name.StartsWith(StockPrefix, StringComparison.Ordinal))
            .Select(node => node.name).ToArray();
        static string TextOf(Transform owner, string name) => Node(owner, name).GetComponent<Text>().text;
        static void Click(Transform owner, string name)
        {
            var button = Node(owner, name).GetComponent<Button>();
            Assert.That(button.IsInteractable(), Is.True, name);
            button.onClick.Invoke();
        }
    }
}
