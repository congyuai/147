using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Dustline
{
    internal sealed class ItemPreviewStage : IDisposable
    {
        internal const int Layer = 29;
        internal const int LayerMask = 1 << Layer;
        internal readonly Transform Root;
        readonly Camera camera;
        Transform model;
        Bounds bounds;
        float yaw, pitch;
        bool disposed;

        internal ItemPreviewStage(Transform owner, int lane)
        {
            Root = new GameObject("Item preview studio") { layer = Layer }.transform;
            Root.SetParent(owner, false);
            Root.position = new Vector3(lane * 8f, -420f, 0f);
            var eye = new GameObject("Item preview camera", typeof(Camera)) { layer = Layer };
            eye.transform.SetParent(Root, false);
            camera = eye.GetComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.cullingMask = LayerMask;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.useOcclusionCulling = false;
            camera.nearClipPlane = .025f;
            camera.farClipPlane = 8f;
            var settings = camera.GetUniversalAdditionalCameraData();
            settings.SetRenderer(0);
            settings.renderShadows = false;
            settings.renderPostProcessing = false;
            settings.requiresColorOption = CameraOverrideOption.Off;
            settings.requiresDepthOption = CameraOverrideOption.Off;
            AddLamp(new Vector3(1.35f, 1.15f, .7f), 8f);
            AddLamp(new Vector3(-1.15f, .75f, -.35f), 5f);
            AddLamp(new Vector3(.55f, .37f, -1.45f), 4f);
        }

        void AddLamp(Vector3 position, float intensity)
        {
            var lamp = new GameObject("Studio light", typeof(Light)) { layer = Layer };
            lamp.transform.SetParent(Root, false);
            lamp.transform.localPosition = position;
            var light = lamp.GetComponent<Light>();
            light.type = LightType.Point;
            light.range = 5f;
            light.cullingMask = LayerMask;
            light.color = Color.white;
            light.shadows = LightShadows.None;
            light.intensity = intensity;
        }

        internal void Set(ItemDefinition item, ItemStack stack, ItemCatalog catalog, WeaponForm blade = WeaponForm.Tangdao, bool keepAngle = false)
        {
            keepAngle &= model != null;
            Clear();
            model = item != null ? ItemProp.Create(Root, item, stack, catalog) : ItemProp.CreateWeapon(Root, blade, null, catalog).transform;
            var renderers = model.GetComponentsInChildren<Renderer>();
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            bool weapon = item == null || item.Weapon != null;
            if (!keepAngle)
            {
                yaw = weapon ? 76f : item.Equipment != EquipmentSlot.None ? 152f : 50f;
                pitch = weapon ? 13f : 24f;
            }
        }

        internal void Rotate(Vector2 drag)
        {
            yaw += drag.x * .6f;
            pitch = Mathf.Clamp(pitch - drag.y * .45f, -75f, 75f);
        }

        internal bool Render(RenderTexture target, Color background)
        {
            if (disposed || model == null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null
                || !(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)) return false;
            if (!target.IsCreated() && !target.Create()) return false;
            float aspect = target.width / (float)target.height;
            Quaternion angle = Quaternion.Euler(pitch, yaw, 0f);
            var up = angle * Vector3.up;
            var right = angle * Vector3.right;
            Vector3 extent = bounds.extents;
            camera.orthographicSize = Mathf.Max(.017f, Mathf.Max(Project(up), Project(right) / aspect) * 1.12f);
            camera.aspect = aspect;
            camera.transform.SetPositionAndRotation(bounds.center - angle * Vector3.forward * 3.4f, angle);
            camera.backgroundColor = background;
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(camera, request)) return false;
            bool fog = RenderSettings.fog;
            var mode = RenderSettings.ambientMode;
            var sky = RenderSettings.ambientSkyColor;
            var previousTarget = RenderTexture.active;
            try
            {
                RenderSettings.fog = false;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.28f, .28f, .28f);
                RenderPipeline.SubmitRenderRequest(camera, request);
                return true;
            }
            finally
            {
                RenderSettings.ambientSkyColor = sky;
                RenderSettings.ambientMode = mode;
                RenderSettings.fog = fog;
                RenderTexture.active = previousTarget;
            }

            float Project(Vector3 axis) => Mathf.Abs(axis.x) * extent.x + Mathf.Abs(axis.y) * extent.y + Mathf.Abs(axis.z) * extent.z;
        }

        internal void Clear()
        {
            if (model == null) return;
            model.gameObject.SetActive(false);
            Object.Destroy(model.gameObject);
            model = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Root.gameObject.SetActive(false);
            Object.Destroy(Root.gameObject);
        }
    }
}
