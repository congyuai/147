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
        readonly RawImage image;
        readonly OperatorDrag drag;
        readonly WeaponDefinition boundBlade;
        readonly OperatorDisplay display;
        bool disposed;

        public OperatorPreview(Transform parent, RawImage image, WeaponDefinition loadout)
        {
            this.image = image;
            boundBlade = loadout;
            stage = new GameObject("Operator preview stage") { layer = Layer };
            stage.transform.SetParent(parent, false);
            stage.transform.position = new Vector3(0, -300, 0);
            float aspect = Mathf.Clamp(image.rectTransform.rect.width / Mathf.Max(1f, image.rectTransform.rect.height), .65f, 2f);
            texture = new RenderTexture(Mathf.RoundToInt(512f * aspect), 512, 24, RenderTextureFormat.ARGB32) { name = "Operator portrait" };
            image.texture = texture;
            var eye = new GameObject("Portrait camera", typeof(Camera)) { layer = Layer };
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
            display = new OperatorDisplay(stage.transform, Vector3.zero, 0f, -11f, true);
            image.raycastTarget = true;
            drag = image.gameObject.AddComponent<OperatorDrag>();
            drag.Turn = display.Turn;
        }

        void Lamp(string name, Vector3 position, Color color, float intensity)
        {
            var lamp = new GameObject(name, typeof(Light)) { layer = Layer };
            lamp.transform.SetParent(stage.transform, false);
            lamp.transform.localPosition = position;
            var light = lamp.GetComponent<Light>();
            light.type = LightType.Point;
            light.intensity = intensity;
            light.range = 9;
            light.color = color;
            light.cullingMask = LayerMask;
            light.shadows = LightShadows.None;
        }

        public void Select(OperatorStyle style) { if (!disposed) display.Select(style); }
        internal void SyncGear(GridInventory bag) { if (!disposed) display.SyncGear(bag, boundBlade); }
        internal void RotateView() { if (!disposed) display.Flip(); }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (camera != null) { camera.enabled = false; camera.targetTexture = null; }
            if (stage != null) stage.SetActive(false);
            if (drag != null) { drag.Turn = null; drag.enabled = false; }
            if (image != null && image.texture == texture) { image.texture = null; image.raycastTarget = false; }
            ItemIconCache.Release(texture);
            if (stage != null) Object.Destroy(stage);
        }
    }
}
