using System;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Dustline
{
    sealed class OperatorPreview : IDisposable
    {
        internal const int Layer = 30;
        internal const int LayerMask = 1 << Layer;
        readonly GameObject stage;
        readonly Camera camera;
        readonly RenderTexture texture;
        readonly WeaponDefinition boundBlade;
        OperatorVisual model;
        WeaponVisual hand, back;
        Transform backMount;
        OperatorStyle selected;
        bool rear, disposed;

        public OperatorPreview(Transform parent, RawImage image, WeaponDefinition loadout)
        {
            boundBlade = loadout;
            stage = new GameObject("Operator preview stage") { layer = Layer };
            stage.transform.SetParent(parent, false);
            stage.transform.position = new Vector3(0, -300, 0);
            float aspect = Mathf.Clamp(image.rectTransform.rect.width / Mathf.Max(1f, image.rectTransform.rect.height), .65f, 2f);
            texture = new RenderTexture(Mathf.RoundToInt(512f * aspect), 512, 24, RenderTextureFormat.ARGB32) { name = "Operator portrait" };
            image.texture = texture;
            var eye = new GameObject("Portrait camera", typeof(Camera));
            eye.transform.SetParent(stage.transform, false);
            eye.transform.localPosition = new Vector3(1.3f, 1.32f, 3.2f);
            eye.transform.LookAt(stage.transform.position + Vector3.up * 0.87f);
            camera = eye.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 1.03f;
            camera.nearClipPlane = 0.10f;
            camera.farClipPlane = 9;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(27, 43, 47, 255);
            camera.cullingMask = LayerMask;
            camera.aspect = aspect;
            camera.targetTexture = texture;
            camera.allowMSAA = false;
            camera.allowHDR = false;
            Lamp("Key", new Vector3(0.1f, 2.8f, 2.9f), new Color(1, 0.82f, 0.64f), 10);
            Lamp("Fill", new Vector3(-1.8f, 1.15f, 1.8f), new Color(0.60f, 0.83f, 1), 7);
        }

        void Lamp(string name, Vector3 position, Color color, float intensity)
        {
            var lamp = new GameObject(name, typeof(Light));
            lamp.transform.SetParent(stage.transform, false);
            lamp.transform.localPosition = position;
            var light = lamp.GetComponent<Light>();
            light.type = LightType.Point;
            light.intensity = intensity;
            light.range = 9;
            light.color = color;
            light.cullingMask = 1 << Layer;
            light.shadows = LightShadows.None;
        }

        public void Select(OperatorStyle style)
        {
            if (model != null && selected == style) return;
            if (model != null)
            {
                model.gameObject.SetActive(false);
                Object.Destroy(model.gameObject);
            }
            selected = style;
            model = OperatorVisual.Create(stage.transform, style, 0, Layer);
            model.transform.localRotation = Quaternion.Euler(0, -16, 0);
            model.SetMotion(0, false, false, 0.016f);
            backMount = model.CreateBackMount();
            hand = null;
            back = null;
        }

        internal void SyncGear(GridInventory bag)
        {
            if (model == null || bag == null) return;
            model.SyncGear(bag);
            var primary = bag.Equipped(EquipmentSlot.Weapon);
            var second = bag.Equipped(EquipmentSlot.PrimaryTwo);
            var held = primary ?? bag.Equipped(EquipmentSlot.Sidearm) ?? bag.Equipped(EquipmentSlot.Melee);
            var definition = bag.Catalog.Find(held?.ItemId)?.Weapon ?? boundBlade;
            Sync(ref back, backMount, second, bag.Catalog.Find(second?.ItemId)?.Weapon, bag.Catalog, false);
            Sync(ref hand, model.WeaponMount, held, definition, bag.Catalog, true);
            model.BindWeaponView(hand);
            if (hand != null) model.PoseArms(hand.RightGrip, hand.LeftGrip);
        }

        static void Sync(ref WeaponVisual visual, Transform mount, ItemStack item, WeaponDefinition definition, ItemCatalog catalog, bool held)
        {
            if (definition == null)
            {
                if (visual != null) { visual.gameObject.SetActive(false); Object.Destroy(visual.gameObject); }
                visual = null;
                return;
            }
            if (visual == null)
            {
                visual = WeaponVisual.Create(mount, false, definition.Form);
                visual.name = held ? "Equipped hand weapon" : "Equipped back weapon";
            }
            else visual.SetForm(definition.Form);
            visual.SetConfiguration(item, catalog, definition);
            if (!held)
            {
                visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                visual.transform.localScale = Vector3.one * .75f;
            }
        }

        internal void RotateView()
        {
            rear = !rear;
            camera.transform.localPosition = rear ? new Vector3(-1.45f, 1.24f, -3.2f) : new Vector3(1.3f, 1.32f, 3.2f);
            camera.transform.LookAt(stage.transform.position + Vector3.up * .88f);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (camera != null) camera.targetTexture = null;
            if (stage != null) stage.SetActive(false);
            ItemIconCache.Release(texture);
            if (stage != null) Object.Destroy(stage);
        }
    }
}
