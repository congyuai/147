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
    public sealed class FieldHudTests
    {
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const string SafePath = "Dustline UI/Safe area";
        const string VitalsPath = "HUD/Left HUD anchor/Vitals";
        const string DetailPath = "Pages/Body condition";
        GameObject host;
        GameSession session;
        ProfileStore store;
        byte[] savedProfile;
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
        public IEnumerator BodyHudColorsEachRegionAndOpensCenteredDetail()
        {
            yield return OpenPractice();
            var vitals = Node(Safe, VitalsPath);
            var figure = Node(vitals, "Body diagram");
            var body = session.Player.Health.Body;
            float[] fractions = { 1f, .85f, .65f, .4f, .12f, .1f, 0f };
            for (int i = 0; i < fractions.Length; i++)
            {
                var part = (BodyPart)i;
                body.Damage(part, body.Maximum(part) * (1f - fractions[i]), false);
            }
            RefreshHud();
            var strip = Node(Safe, "Weapon bar/Weapon slots");
            var stamina = Node(strip, "Stamina display");
            Assert.That(ScreenPoint(stamina).x, Is.EqualTo(ScreenPoint(strip).x).Within(1f));
            Assert.That(ScreenPoint(stamina).y, Is.GreaterThan(ScreenPoint(strip).y));
            var staminaGroup = stamina.GetComponent<CanvasGroup>();
            Assert.That(staminaGroup.alpha, Is.Zero);
            Assert.That(staminaGroup.blocksRaycasts, Is.False);
            Assert.That(LabelAt(stamina, "Stamina value"), Is.EqualTo("体力  100 / 100"));
            Assert.That(vitals.GetComponentsInChildren<RectTransform>().Any(node => node.name == "Bar track"), Is.False);
            Assert.That(Node(stamina, "Bar track"), Is.Not.Null);
            var setStamina = typeof(PlayerController).GetProperty(nameof(PlayerController.Stamina)).GetSetMethod(true);
            setStamina.Invoke(session.Player, new object[] { 64f });
            RefreshHud();
            Assert.That(staminaGroup.alpha, Is.EqualTo(1f));
            Assert.That(LabelAt(stamina, "Stamina value"), Is.EqualTo("体力  64 / 100"));
            setStamina.Invoke(session.Player, new object[] { 100f });
            RefreshHud();
            Assert.That(staminaGroup.alpha, Is.Zero);
            Assert.That(vitals.GetComponentsInChildren<Text>().Any(text => text.text.StartsWith("总健康") || text.name.StartsWith("Region health / ")), Is.False);
            var sidearm = Slot(Node(Safe, "Weapon bar/Weapon slots"), EquipmentSlot.Sidearm);
            Assert.That(sidearm.GetComponent<Button>().interactable, Is.False);
            var emptySlotHits = Hits(ScreenPoint(sidearm));
            Assert.That(emptySlotHits.Any(hit => hit.gameObject == sidearm.gameObject), Is.False);
            Assert.That(emptySlotHits.Any(hit => hit.gameObject == Node(Safe, "Touch controls/Look pad").gameObject), Is.True);
            Assert.That(LabelAt(sidearm, "Weapon ammunition"), Is.Empty);
            Assert.That(Node(vitals, "Injury summary/Injury / Destroyed").gameObject.activeInHierarchy, Is.True);
            AssertBodyMesh(figure.GetComponent<Graphic>());

            Vector2 head = RectTransformUtility.WorldToScreenPoint(null,
                figure.TransformPoint(new Vector2(figure.rect.center.x, figure.rect.yMax - .13f * figure.rect.height)));
            ClickAt(figure.gameObject, head);
            var detail = Node(Safe, DetailPath);
            var map = Node(detail, "Condition map");
            var large = Node(map, "Body detail diagram");
            Assert.That(ScreenPoint(large).x, Is.EqualTo(ScreenPoint(detail).x).Within(1f));
            Assert.That(large.rect.height, Is.GreaterThan(2f * figure.rect.height));
            Assert.That(LabelAt(map, "Total health"), Is.EqualTo("总健康  " + Values(body.Total, body.TotalMaximum)));
            Assert.That(LabelAt(detail, "Treatment options/Treatment target"), Does.Contain("头部"));
            for (int i = 0; i < BodyCondition.Count; i++)
            {
                var part = (BodyPart)i;
                Assert.That(LabelAt(map, "Region / " + part + "/Region health / " + part), Is.EqualTo(Values(body.Current(part), body.Maximum(part))));
            }
            yield return null;
            Click(Node(detail, "‹ 返回"));
            Assert.That(session.UI.BlocksGameplay, Is.False);
            Assert.That(figure.gameObject.activeInHierarchy, Is.True);
        }

        [UnityTest]
        public IEnumerator InjuryBadgeCountsRegionsAndQueuesOnlyMatchingTreatment()
        {
            yield return OpenPractice();
            var player = session.Player;
            var body = player.Health.Body;
            foreach (var leg in new[] { BodyPart.LeftLeg, BodyPart.RightLeg }) body.Damage(leg, .5f * body.Maximum(leg));
            RefreshHud();
            var summary = Node(Safe, VitalsPath + "/Injury summary");
            var fracture = Node(summary, "Injury / Fracture");
            Assert.That(LabelAt(fracture, "Injury count"), Is.EqualTo("2"));
            yield return null;
            Click(fracture);
            yield return null;
            var detail = Node(Safe, DetailPath);
            Assert.That(LabelAt(detail, "Treatment options/Treatment target"), Is.EqualTo("治疗目标 / 左腿 · 骨折"));
            var right = Node(detail, "Condition map/Region / RightLeg/Injury / Fracture");
            Assert.That(Node(right, "Injury glyph").GetComponent<Graphic>().GetType(), Is.EqualTo(Node(fracture, "Injury glyph").GetComponent<Graphic>().GetType()));
            Click(right);
            Assert.That(LabelAt(detail, "Treatment options/Treatment target"), Is.EqualTo("治疗目标 / 右腿 · 骨折"));
            yield return null;
            var options = Node(detail, "Treatment options/Available treatments");
            Assert.That(Node(options, "Treatment / bandage").gameObject.activeSelf, Is.False);
            var splint = Node(options, "Treatment / splint/开始");
            Assert.That(splint.GetComponent<Button>().interactable, Is.True);
            int stock = session.ActiveFieldBag.RemainingUses("splint");
            Click(splint);
            Assert.That(session.Input.Gameplay, Is.True);
            Assert.That(session.ActiveFieldBag.RemainingUses("splint"), Is.EqualTo(stock), "Opening or selecting in the UI must not consume an item.");
            Invoke(session.Input, "Update");
            Invoke(player, "Tick", Time.time, .01f);
            Assert.That(player.IsTreating, Is.True);
            Assert.That(player.TreatmentPart, Is.EqualTo(BodyPart.RightLeg));
            Invoke(player, "TickTreatment", 3f);
            Assert.That(player.IsTreating, Is.False);
            Assert.That(session.ActiveFieldBag.RemainingUses("splint"), Is.EqualTo(stock - 1));
            Assert.That(body.Injuries(BodyPart.RightLeg) & Injury.Fracture, Is.EqualTo(Injury.None));
            Assert.That(body.Injuries(BodyPart.LeftLeg) & Injury.Fracture, Is.EqualTo(Injury.Fracture));
            RefreshHud();
            Assert.That(fracture.gameObject.activeInHierarchy, Is.True);
            Assert.That(LabelAt(fracture, "Injury count"), Is.Empty);

            yield return null;
            Click(fracture);
            detail = Node(Safe, DetailPath);
            Assert.That(Node(detail, "Condition map/Region / RightLeg/Injury / Fracture").gameObject.activeSelf, Is.False);
            Assert.That(LabelAt(detail, "Treatment options/Treatment target"), Does.Contain("左腿"));
            CollectionAssert.AreEqual(savedProfile, File.ReadAllBytes(store.Path));
        }

        [UnityTest]
        public IEnumerator WeaponBarSelectsAllEquippedSlotsAboveLookAndHidesOnPages()
        {
            yield return OpenPractice(true);
            var weapon = session.Player.Weapon;
            var bag = session.ActiveFieldBag;
            var strip = Node(Safe, "Weapon bar/Weapon slots");
            var look = Node(Safe, "Touch controls/Look pad");
            var legacy = Node(Safe, "Touch controls/SwitchWeapon");
            var slots = new[] { EquipmentSlot.Weapon, EquipmentSlot.PrimaryTwo, EquipmentSlot.Sidearm, EquipmentSlot.Melee };
            var models = new List<RawImage>();
            Assert.That(strip.GetComponentsInChildren<Button>().Length, Is.EqualTo(4));
            Assert.That(legacy.gameObject.activeInHierarchy, Is.True);
            foreach (var slot in slots)
            {
                var node = Slot(strip, slot);
                var item = bag.Equipped(slot);
                Assert.That(node.GetComponent<Button>().interactable, Is.True);
                Assert.That(Node(node, "Active slot").gameObject.activeSelf, Is.EqualTo(slot == EquipmentSlot.Weapon));
                if (item == null)
                {
                    Assert.That(slot, Is.EqualTo(EquipmentSlot.Melee));
                    Assert.That(LabelAt(node, "Weapon ammunition"), Is.EqualTo("绑定"));
                    models.Add(Node(node, "Item model / " + session.BoundBlade.Form).GetComponent<RawImage>());
                    continue;
                }
                int rounds = slot == weapon.ActiveSlot ? weapon.Magazine.Rounds : Mathf.Max(0, item.LoadedAmmo);
                Assert.That(LabelAt(node, "Weapon ammunition"), Is.EqualTo(rounds + " / 无限"));
                models.Add(Node(node, "Item model / " + item.ItemId).GetComponent<RawImage>());
            }
            Assert.That(models.All(model => model != null && !model.raycastTarget), Is.True);
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null && GraphicsSettings.currentRenderPipeline != null)
            {
                for (int frame = 0; frame < 60 && models.Any(model => model.texture == null); frame++) yield return null;
                Assert.That(models.All(model => model.texture is RenderTexture target && target.IsCreated()), Is.True);
            }

            foreach (var slot in new[] { EquipmentSlot.Sidearm, EquipmentSlot.PrimaryTwo, EquipmentSlot.Melee, EquipmentSlot.Weapon })
            {
                var node = Slot(strip, slot);
                var point = ScreenPoint(node);
                var hits = Hits(point);
                Assert.That(hits.Any(hit => hit.gameObject == look.gameObject), Is.True, "Test point must overlap the transparent Look pad: " + slot);
                ClickAt(node.gameObject, point);
                Assert.That(weapon.ActiveSlot, Is.EqualTo(slot));
                Assert.That(Node(node, "Active slot").gameObject.activeSelf, Is.True);
            }
            Assert.That(bag.Equipped(EquipmentSlot.Melee), Is.Null);
            Assert.That(bag.TryAdd("baton", 1, out var error), Is.True, error);
            Assert.That(bag.TryEquip(bag.Items.Single(item => item.ItemId == "baton").InstanceId, EquipmentSlot.Melee, out error), Is.True, error);
            RefreshHud();
            var melee = Slot(strip, EquipmentSlot.Melee);
            Assert.That(Node(melee, "Item model / baton").GetComponent<RawImage>(), Is.Not.Null);
            Click(melee);
            Assert.That(weapon.ActiveIsBound, Is.False);

            var sidearm = Slot(strip, EquipmentSlot.Sidearm);
            var pistol = bag.Equipped(EquipmentSlot.Sidearm);
            Assert.That(bag.Count("ammo_copper"), Is.Zero);
            pistol.LoadedAmmo = 3;
            RefreshHud();
            Assert.That(LabelAt(sidearm, "Weapon ammunition"), Is.EqualTo("3 / 无限"));
            pistol.LoadedAmmo = -1;
            RefreshHud();
            Assert.That(LabelAt(sidearm, "Weapon ammunition"), Is.EqualTo("0 / 无限"));

            var legacyPosition = legacy.position;
            legacy.position = sidearm.TransformPoint(sidearm.rect.center);
            RefreshHud();
            Assert.That(legacy.gameObject.activeSelf, Is.False);
            legacy.position = legacyPosition;
            RefreshHud();
            Assert.That(legacy.gameObject.activeSelf, Is.True);

            session.UI.BackOrSettings();
            Assert.That(strip.gameObject.activeInHierarchy, Is.False);
            Assert.That(Hits(ScreenPoint(sidearm)).Any(hit => hit.gameObject == sidearm.gameObject), Is.False);
            sidearm.GetComponent<Button>().onClick.Invoke();
            Assert.That(weapon.ActiveSlot, Is.EqualTo(EquipmentSlot.Melee));
            yield return null;
            Click(Node(Safe, "Pages/Game settings/‹ 返回"));
            Assert.That(strip.gameObject.activeInHierarchy, Is.True);
            Assert.That(session.Input.Gameplay, Is.True);
            CollectionAssert.AreEqual(savedProfile, File.ReadAllBytes(store.Path));
        }

        [UnityTest]
        public IEnumerator HipReticleMarksProjectedSpreadWithFixedStrokeSizes()
        {
            yield return OpenPractice();
            var center = Node(Safe, "HUD/Screen centre");
            var view = session.Player.View;
            var arms = Enumerable.Range(0, 4).Select(i => Node(center, "Hip spread arm " + i)).ToArray();
            var spread = typeof(WeaponController).GetProperty("CurrentShotSpread", Instance).GetSetMethod(true);
            foreach (float value in new[] { .008f, .02f, .045f })
            {
                spread.Invoke(session.Player.Weapon, new object[] { value });
                RefreshHud();
                Assert.That(Vector2.Distance(ScreenPoint(center), view.pixelRect.center), Is.LessThan(1f));
                var expected = FieldHandling.ScreenRadius(value, view.pixelRect, view.projectionMatrix);
                for (int i = 0; i < arms.Length; i++)
                {
                    var line = arms[i];
                    Assert.That(line.GetComponent<Image>().enabled, Is.True);
                    bool horizontal = i < 2;
                    Assert.That(line.sizeDelta, Is.EqualTo(horizontal ? new Vector2(12, 2) : new Vector2(2, 12)));
                    float sign = (i & 1) == 0 ? 1f : -1f;
                    Vector2 inner = horizontal ? new Vector2(-sign * line.rect.width / 2f, 0f)
                        : new Vector2(0f, -sign * line.rect.height / 2f);
                    var actual = RectTransformUtility.WorldToScreenPoint(null, line.TransformPoint(inner)) - view.pixelRect.center;
                    Assert.That(Vector2.Distance(actual, horizontal ? new Vector2(sign * expected.x, 0f) : new Vector2(0f, sign * expected.y)), Is.LessThan(1f));
                }
            }
            typeof(PlayerInputReader).GetProperty(nameof(PlayerInputReader.Aim)).GetSetMethod(true).Invoke(session.Input, new object[] { true });
            RefreshHud();
            Assert.That(arms.All(arm => !arm.GetComponent<Image>().enabled), Is.True);
        }

        [UnityTest]
        public IEnumerator OnlyDraggingIntoTheSprintIconLocksTheStick()
        {
            yield return OpenPractice();
            var move = Node(Safe, "Touch controls/Move");
            var control = move.GetComponent<TouchControl>();
            var icon = Node(move, "Sprint lock");
            var input = session.Input;
            var e = new PointerEventData(EventSystem.current) { pointerId = 31, button = PointerEventData.InputButton.Left, position = ScreenPoint(move) };
            control.OnPointerDown(e);
            e.position = RectTransformUtility.WorldToScreenPoint(null, move.TransformPoint(Vector2.up * move.rect.width * .32f));
            control.OnDrag(e);
            Invoke(input, "Update");
            Assert.That(input.Move.y, Is.EqualTo(1f).Within(.001f));
            Assert.That(input.Sprint, Is.False);
            RefreshHud();
            Canvas.ForceUpdateCanvases();
            Assert.That(icon.gameObject.activeInHierarchy, Is.True);
            e.position = ScreenPoint(icon);
            control.OnDrag(e);
            control.OnPointerUp(e);
            Invoke(input, "Update");
            Assert.That(input.Sprint && input.SprintHeld, Is.True);
            Assert.That(input.Move, Is.EqualTo(Vector2.up));
            e.position = ScreenPoint(move);
            control.OnPointerDown(e);
            e.position = RectTransformUtility.WorldToScreenPoint(null, move.TransformPoint(Vector2.down * move.rect.width * .31f));
            control.OnDrag(e);
            Invoke(input, "Update");
            Assert.That(input.SprintHeld, Is.False);
            Assert.That(input.Move.y, Is.EqualTo(-1f).Within(.001f));
            control.OnPointerUp(e);
        }

        IEnumerator OpenPractice(bool allFirearms = false)
        {
            if (Screen.width < Screen.height || Screen.height < 1) Assert.Ignore("Requires a landscape Game view.");
            var catalog = Resources.Load<ItemCatalog>("Dustline/Items");
            Assert.That(catalog, Is.Not.Null, "Run Dustline > Prepare Project first.");
            var loadout = new GridInventory(8, 7, 70f, catalog);
            Equip("rifle", EquipmentSlot.Weapon);
            if (allFirearms)
            {
                Equip("dust_marksman", EquipmentSlot.PrimaryTwo);
                Equip("dust_pistol", EquipmentSlot.Sidearm);
            }
            store = new ProfileStore(Path.Combine(Application.temporaryCachePath, "dustline-field-hud-" + Guid.NewGuid().ToString("N") + ".json"));
            store.Save(new ProfileData { Version = ProfileData.CurrentVersion, BirthdayGiftClaimed = true, Loadout = loadout.Snapshot() });
            host = new GameObject("Field HUD test session");
            host.SetActive(false);
            session = host.AddComponent<GameSession>();
            typeof(GameSession).GetField("store", Instance).SetValue(session, store);
            host.SetActive(true);
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.Menu), session.Error);
            savedProfile = File.ReadAllBytes(store.Path);
            session.Input.ForceTouch = true;
            session.StartPractice();
            for (int frame = 0; frame < 45 && session.Phase == SessionPhase.PracticeLoading; frame++) yield return null;
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.Practice), session.Error);
            Invoke(session, "OnApplicationFocus", true);
            Assert.That(session.Input.Gameplay, Is.True);

            void Equip(string id, EquipmentSlot slot)
            {
                Assert.That(loadout.TryAdd(id, 1, out var error), Is.True, error);
                Assert.That(loadout.TryEquip(loadout.Items.Single(item => item.ItemId == id).InstanceId, slot, out error), Is.True, error);
            }
        }

        static void AssertBodyMesh(Graphic graphic)
        {
            Assert.That(graphic.GetComponent<CanvasRenderer>(), Is.Not.Null);
            var shapes = (Vector2[][])graphic.GetType().GetField("Shape", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            Color32[] fills = { new Color32(115, 164, 137, 255), new Color32(237, 219, 142, 255), new Color32(231, 151, 73, 255),
                new Color32(218, 77, 72, 255), new Color32(126, 39, 49, 255), new Color32(126, 39, 49, 255), new Color32(8, 10, 11, 255) };
            using (var mesh = new VertexHelper())
            {
                graphic.GetType().GetMethod("OnPopulateMesh", Instance, null, new[] { typeof(VertexHelper) }, null).Invoke(graphic, new object[] { mesh });
                var vertex = default(UIVertex);
                int offset = 0;
                for (int i = 0; i < BodyCondition.Count; i++)
                {
                    mesh.PopulateUIVertex(ref vertex, offset);
                    var rim = i == (int)BodyPart.RightLeg ? new Color32(239, 240, 229, 191) : new Color32(52, 73, 79, 191);
                    Assert.That(vertex.color, Is.EqualTo(rim), (BodyPart)i + " outline");
                    mesh.PopulateUIVertex(ref vertex, offset + shapes[i].Length);
                    var fill = fills[i];
                    fill.a = 191;
                    Assert.That(vertex.color, Is.EqualTo(fill), (BodyPart)i + " fill");
                    offset += 2 * shapes[i].Length;
                }
            }
        }

        RectTransform Safe => Node(session.UI.transform, SafePath);
        void RefreshHud() => Invoke(session.UI, "UpdateHud", true);
        static RectTransform Slot(Transform root, EquipmentSlot slot) => Node(root, "Weapon slot / " + slot);
        static string LabelAt(Transform root, string path) => Node(root, path).GetComponent<Text>().text;
        static string Values(float hp, float max) => hp.ToString("0.#", CultureInfo.InvariantCulture) + " / " + max.ToString("0.#", CultureInfo.InvariantCulture);
        static object Invoke(object owner, string name, params object[] values) => owner.GetType().GetMethod(name, Instance).Invoke(owner, values);
        static Vector2 ScreenPoint(RectTransform rect) => RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        static RectTransform Node(Transform root, string path)
        {
            // Unity treats '/' in a literal object name as a path separator in Transform.Find.
            var node = root;
            foreach (var name in path.Replace(" / ", "\u001f").Split('/'))
            {
                string child = name.Replace("\u001f", " / ");
                node = node.Cast<Transform>().LastOrDefault(candidate => candidate.name == child);
                Assert.That(node, Is.Not.Null, path);
            }
            return (RectTransform)node;
        }

        static List<RaycastResult> Hits(Vector2 position)
        {
            Canvas.ForceUpdateCanvases();
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, position = position }, hits);
            return hits;
        }

        static void Click(RectTransform rect) => ClickAt(rect.gameObject, ScreenPoint(rect));
        static void ClickAt(GameObject expected, Vector2 position)
        {
            var hits = Hits(position);
            Assert.That(hits.Count, Is.GreaterThan(0), expected.name);
            var handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject);
            Assert.That(handler, Is.SameAs(expected));
            var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, position = position, pointerId = -1 };
            ExecuteEvents.Execute(handler, data, ExecuteEvents.pointerClickHandler);
        }
    }
}