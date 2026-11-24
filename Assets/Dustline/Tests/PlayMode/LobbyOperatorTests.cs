using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Dustline.Tests
{
    public sealed class LobbyOperatorTests
    {
        [UnityTest]
        public IEnumerator LobbyCameraIsFullBleedWhileOnlyTheOperatorRegionTurns()
        {
            using (var fixture = new LobbyFixture())
            {
                var ui = fixture.UI;
                var desk = fixture.Desk;
                var camera = fixture.Scenery.GetComponentInChildren<Camera>();
                Assert.That(camera.rect, Is.EqualTo(new Rect(0f, 0f, 1f, 1f)));
                Assert.That(camera.targetTexture, Is.Null);
                Assert.That(desk.GetComponentsInChildren<RawImage>(), Is.Empty);
                Assert.That(desk.GetComponentsInChildren<Text>().Any(t => t.text == "切换正背面"), Is.False);
                foreach (string title in new[] { "仓库", "商店", "市场", "枪械改装", "离线靶场", "角色", "设置", "开始游戏" })
                    Assert.That(FindButton(desk, title), Is.Not.Null, title);
                foreach (var view in desk.GetComponentsInChildren<Button>())
                    Assert.That(Screen.safeArea.Contains(CenterScreenPoint((RectTransform)view.transform)), Is.True, view.name);
                foreach (string part in new[] { "River bend", "Sky and low cloud", "Concrete apron", "Shore, ridges and dock machinery" })
                    Assert.That(fixture.Scenery.Find(part).GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.GreaterThan(0), part);

                var pivot = fixture.Operator.transform.parent;
                Quaternion start = pivot.localRotation;
                var drag = (RectTransform)desk.Find("Operator drag region");
                var eventSystem = ui.GetComponentInChildren<EventSystem>();
                var raycaster = desk.GetComponentInParent<GraphicRaycaster>();
                Canvas.ForceUpdateCanvases();

                var menu = (RectTransform)FindButton(desk, "仓库").transform;
                var menuPoint = CenterScreenPoint(menu);
                Assert.That(TopHit(menuPoint), Is.SameAs(menu.gameObject));
                Swipe(TopHit(menuPoint), menuPoint, CenterScreenPoint(drag));
                Assert.That(Quaternion.Angle(start, pivot.localRotation), Is.LessThan(.01f));

                var center = drag.rect.center;
                var touch = ScreenPoint(drag, center);
                Assert.That(TopHit(touch), Is.SameAs(drag.gameObject));
                Swipe(drag.gameObject, touch, ScreenPoint(drag, center + Vector2.up * .25f * drag.rect.height));
                Assert.That(Quaternion.Angle(start, pivot.localRotation), Is.LessThan(.01f));
                Swipe(drag.gameObject, touch, ScreenPoint(drag, center + Vector2.right * .25f * drag.rect.width));
                Assert.That(Quaternion.Angle(start, pivot.localRotation), Is.EqualTo(90f).Within(.25f));
                Swipe(drag.gameObject, touch, ScreenPoint(drag, center + Vector2.right * .75f * drag.rect.width));
                Assert.That(Quaternion.Angle(start, pivot.localRotation), Is.LessThan(.25f));

                var shell = fixture.Scenery.Find("River bend").GetComponent<MeshFilter>().sharedMesh;
                FindButton(desk, "设置").onClick.Invoke();
                Assert.That(camera.isActiveAndEnabled, Is.False);
                yield return null;
                Assert.That(shell == null, Is.True, "Page changes must release the procedural assets.");
                ui.BackOrSettings();
                Assert.That(fixture.Desk.gameObject.activeInHierarchy, Is.True);
                Assert.That(ui.GetComponentsInChildren<Camera>().Count(eye => eye.name == "Lobby scenery camera"), Is.EqualTo(1));

                GameObject TopHit(Vector2 p)
                {
                    var hits = new List<RaycastResult>();
                    raycaster.Raycast(new PointerEventData(eventSystem) { position = p }, hits);
                    Assert.That(hits, Is.Not.Empty);
                    return hits[0].gameObject;
                }
                void Swipe(GameObject startAt, Vector2 from, Vector2 to)
                {
                    var e = new PointerEventData(eventSystem) { pointerId = 7, button = PointerEventData.InputButton.Left, position = from };
                    var press = ExecuteEvents.ExecuteHierarchy(startAt, e, ExecuteEvents.pointerDownHandler);
                    var capture = ExecuteEvents.GetEventHandler<IDragHandler>(startAt);
                    e.position = to;
                    e.delta = to - from;
                    if (capture != null) ExecuteEvents.Execute(capture, e, ExecuteEvents.dragHandler);
                    if (press != null) ExecuteEvents.Execute(press, e, ExecuteEvents.pointerUpHandler);
                }
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator LobbyMirrorsSlotsAndRemovesTheirMeshesWhenUnequipped()
        {
            using (var fixture = new LobbyFixture())
            {
                var body = fixture.Operator;
                var worn = Worn(body);
                Assert.That(worn.Length, Is.EqualTo(4));
                Assert.That(worn.Select(mesh => mesh.sharedMesh.name), Is.EquivalentTo(new[] {
                    "Worn / helmet", "Worn / armor", "Worn / rig", "Worn / pack"
                }));
                Assert.That(worn.All(mesh => mesh.rootBone == body.transform), Is.True);
                var hand = body.WeaponMount.GetComponentInChildren<WeaponVisual>();
                var backMount = body.transform.Find("Hips/Chest/Back weapon mount");
                Assert.That(hand.Form, Is.EqualTo(WeaponForm.Rifle));
                Assert.That(backMount.GetComponentInChildren<WeaponVisual>().Form, Is.EqualTo(WeaponForm.Smg));
                float backZ = backMount.localPosition.z;
                var sole = fixture.Scenery.Find("Right sole contact");
                var foot = body.transform.Find("Hips/RightThigh/RightShin/RightBoot");
                Assert.That(Mathf.Abs(sole.position.x - foot.position.x), Is.LessThan(.08f));
                Assert.That(Mathf.Abs(sole.position.z - foot.position.z), Is.LessThan(.08f));

                Assert.That(fixture.Bag.Remove("pack"), Is.True);
                Assert.That(fixture.Bag.Remove("gun1"), Is.True);
                yield return null;
                yield return null;
                Assert.That(fixture.Operator, Is.SameAs(body));
                Assert.That(Worn(body).Length, Is.EqualTo(3));
                Assert.That(backMount.localPosition.z, Is.GreaterThan(backZ));
                Assert.That(body.WeaponMount.GetComponentInChildren<WeaponVisual>(), Is.Null, "No fallback weapon in a cleared primary slot.");
                Assert.That(backMount.GetComponentInChildren<WeaponVisual>().Form, Is.EqualTo(WeaponForm.Smg));
                fixture.Bag.Restore(new List<ItemStack>());
                yield return null;
                yield return null;
                Assert.That(Worn(body), Is.Empty);
                Assert.That(body.GetComponentsInChildren<WeaponVisual>(), Is.Empty);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator PreparationPortraitCanStillFlipAfterADrag()
        {
            var host = new GameObject("Portrait fixture");
            IDisposable preview = null;
            try
            {
                var node = new GameObject("Portrait target", typeof(RectTransform), typeof(RawImage));
                node.transform.SetParent(host.transform, false);
                var image = node.GetComponent<RawImage>();
                image.rectTransform.sizeDelta = new Vector2(400, 500);
                var type = typeof(GameUI).Assembly.GetType("Dustline.OperatorPreview", true);
                preview = (IDisposable)Activator.CreateInstance(type, new object[] { host.transform, image, null });
                type.GetMethod("Select").Invoke(preview, new object[] { OperatorStyle.Female });
                var pivot = host.GetComponentInChildren<OperatorVisual>().transform.parent;
                Quaternion front = pivot.localRotation;
                var drag = node.GetComponent<IDragHandler>();
                Assert.That(drag, Is.Not.Null);
                var pointer = new PointerEventData(EventSystem.current) {
                    pointerId = 3, button = PointerEventData.InputButton.Left, position = CenterScreenPoint(image.rectTransform)
                };
                ((IPointerDownHandler)drag).OnPointerDown(pointer);
                pointer.position += new Vector2(40f, 0f);
                drag.OnDrag(pointer);
                ((IPointerUpHandler)drag).OnPointerUp(pointer);
                Assert.That(Quaternion.Angle(front, pivot.localRotation), Is.EqualTo(36f).Within(.1f));
                var rotate = type.GetMethod("RotateView", BindingFlags.Instance | BindingFlags.NonPublic);
                rotate.Invoke(preview, null);
                Assert.That(Quaternion.Angle(front, pivot.localRotation), Is.EqualTo(180f).Within(.1f));
                rotate.Invoke(preview, null);
                Assert.That(Quaternion.Angle(front, pivot.localRotation), Is.LessThan(.1f));
            }
            finally
            {
                preview?.Dispose();
                Object.Destroy(host);
            }
            yield return null;
        }

        static Vector2 CenterScreenPoint(RectTransform rect) => ScreenPoint(rect, rect.rect.center);
        static Vector2 ScreenPoint(RectTransform rect, Vector2 p) => RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(p));
        static Button FindButton(Transform desk, string title) => desk.GetComponentsInChildren<Button>()
            .SingleOrDefault(button => button.GetComponentInChildren<Text>().text == title);
        static SkinnedMeshRenderer[] Worn(OperatorVisual body) => body.GetComponentsInChildren<SkinnedMeshRenderer>()
            .Where(part => part.transform != body.transform).ToArray();

        sealed class LobbyFixture : IDisposable
        {
            readonly GameObject owner = new GameObject("Inactive lobby session fixture");
            readonly GameObject host = new GameObject("Lobby fixture UI");
            readonly ItemCatalog catalog = ScriptableObject.CreateInstance<ItemCatalog>();
            readonly BladeCatalog blades = ScriptableObject.CreateInstance<BladeCatalog>();
            public GameUI UI { get; }
            public GridInventory Bag { get; }
            public Transform Desk => UI.transform.Find("Dustline UI/Safe area/Pages/Operations desk");
            public Transform Scenery => UI.transform.Find("Lobby river overlook");
            public OperatorVisual Operator => Scenery.GetComponentInChildren<OperatorVisual>();

            public LobbyFixture()
            {
                try
                {
                    owner.SetActive(false);
                    var session = owner.AddComponent<GameSession>();
                    UI = host.AddComponent<GameUI>();
                    blades.Blades = Array.Empty<BladeChoice>();
                    var items = new List<ItemStack>();
                    Gear("gun1", ItemKind.Weapon, EquipmentSlot.Weapon, WeaponForm.Rifle);
                    Gear("gun2", ItemKind.Weapon, EquipmentSlot.PrimaryTwo, WeaponForm.Smg);
                    Gear("armor", ItemKind.Armor, EquipmentSlot.Armor);
                    Gear("helmet", ItemKind.Helmet, EquipmentSlot.Helmet);
                    Gear("rig", ItemKind.Rig, EquipmentSlot.Rig);
                    Gear("pack", ItemKind.Pack, EquipmentSlot.Pack);
                    Bag = GridInventory.Carried(catalog);
                    Bag.Restore(items);
                    Set("Catalog", catalog);
                    Set("Blades", blades);
                    Set("Loadout", Bag);
                    Set("Profile", new ProfileData());
                    Set("Input", owner.AddComponent<PlayerInputReader>());
                    Set("UI", UI);
                    UI.Initialize(session);
                    UI.ShowMenu();
                    Canvas.ForceUpdateCanvases();
                    Assert.That(Desk, Is.Not.Null);
                    Assert.That(Scenery, Is.Not.Null);

                    void Set(string key, object value) => typeof(GameSession).GetProperty(key).GetSetMethod(true).Invoke(session, new[] { value });
                    void Gear(string id, ItemKind kind, EquipmentSlot slot, WeaponForm form = default)
                    {
                        var item = ScriptableObject.CreateInstance<ItemDefinition>();
                        item.Kind = kind;
                        item.Id = item.DisplayName = id;
                        if (kind == ItemKind.Weapon)
                        {
                            item.Weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
                            item.Weapon.Form = form;
                        }
                        if (kind == ItemKind.Pack || kind == ItemKind.Rig)
                            item.Storage = new StorageSpec { Columns = 4, Rows = 3 };
                        catalog.Items.Add(item);
                        items.Add(new ItemStack { InstanceId = id, ItemId = id, Slot = slot });
                    }
                }
                catch { Dispose(); throw; }
            }

            public void Dispose()
            {
                host.SetActive(false);
                Object.Destroy(host);
                Object.Destroy(owner);
                foreach (var item in catalog.Items)
                {
                    if (item.Weapon != null) Object.Destroy(item.Weapon);
                    Object.Destroy(item);
                }
                Object.Destroy(catalog);
                Object.Destroy(blades);
            }
        }
    }
}
