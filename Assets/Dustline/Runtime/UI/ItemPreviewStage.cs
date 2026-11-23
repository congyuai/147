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
        WeaponVisual weaponView;
        MeshFilter[] geometry;
        float yaw, pitch, zoom = 1f;
        float referenceAspect, referenceSize;
        readonly bool workshop;
        bool disposed;

        internal ItemPreviewStage(Transform owner, int lane)
        {
            workshop = lane == 1;
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
            AddLamp(new Vector3(-1.07f, 1.10f, -.38f), 13f, new Color(1f, .95f, .88f));
            AddLamp(new Vector3(1.04f, .86f, .63f), 10.5f, new Color(.89f, .96f, 1f));
            AddLamp(new Vector3(.11f, -.68f, -.32f), 3.6f, Color.white);
        }

        void AddLamp(Vector3 position, float intensity, Color tone)
        {
            var lamp = new GameObject("Studio light", typeof(Light)) { layer = Layer };
            lamp.transform.SetParent(Root, false);
            lamp.transform.localPosition = position;
            var light = lamp.GetComponent<Light>();
            light.type = LightType.Point;
            light.range = 4.2f;
            light.cullingMask = LayerMask;
            light.color = tone;
            light.shadows = LightShadows.None;
            light.intensity = intensity;
        }

        internal void Set(ItemDefinition item, ItemStack stack, ItemCatalog catalog, WeaponForm blade = WeaponForm.Tangdao, bool keepAngle = false)
        {
            keepAngle &= model != null;
            Clear();
            model = item != null ? ItemProp.Create(Root, item, stack, catalog) : ItemProp.CreateWeapon(Root, blade, null, catalog).transform;
            Measure();
            if (!keepAngle)
            {
                bool weapon = item == null || item.Weapon != null;
                yaw = weapon ? 82f : item.Equipment != EquipmentSlot.None ? 151f : item.Kind == ItemKind.Part ? 63f : 42f;
                pitch = weapon ? 7f : 22f;
                zoom = 1f;
                referenceSize = 0f;
            }
        }

        internal void SetLegacyPart(PartSlot slot, int level)
        {
            Clear();
            model = ItemProp.CreateLegacyPart(Root, slot, level);
            Measure();
            yaw = 72f; pitch = 18f; zoom = 1f;
        }

        void Measure()
        {
            weaponView = model.GetComponent<WeaponVisual>();
            geometry = model.GetComponentsInChildren<MeshFilter>();
        }

        internal void Focus(PartSlot? slot) => weaponView?.SetPreviewFocus(slot);

        internal bool Project(PartSlot slot, out Vector2 uv, out Rect region)
        {
            uv = default; region = default;
            if (weaponView == null) return false;
            var bounds = weaponView.PreviewPartBounds(slot, out var space);
            Vector3 point = camera.WorldToViewportPoint(space.TransformPoint(bounds.center));
            uv = new Vector2(point.x, point.y);
            if (point.z <= 0f || point.x < 0f || point.x > 1f || point.y < 0f || point.y > 1f) return false;
            Vector2 min = Vector2.one, max = Vector2.zero;
            for (int i = 0; i < 8; i++)
            {
                var corner = camera.WorldToViewportPoint(space.TransformPoint(Corner(bounds, i)));
                min = Vector2.Min(min, corner); max = Vector2.Max(max, corner);
            }
            region = Rect.MinMaxRect(Mathf.Clamp01(min.x), Mathf.Clamp01(min.y), Mathf.Clamp01(max.x), Mathf.Clamp01(max.y));
            return true;
        }

        internal void Rotate(Vector2 drag)
        {
            yaw = Mathf.Repeat(yaw + drag.x * .55f, 360f);
            pitch = Mathf.Clamp(pitch - drag.y * .44f, -75f, 75f);
        }

        internal void Zoom(float multiplier) => zoom = Mathf.Clamp(zoom * multiplier, .76f, 1.58f);

        internal void ResetView()
        {
            yaw = 82f;
            pitch = 7f;
            zoom = 1f;
        }

        internal bool Render(RenderTexture target, Color background)
        {
            if (disposed || model == null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null
                || !(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)) return false;
            if (!target.IsCreated() && !target.Create()) return false;
            float aspect = target.width / (float)target.height;
            Frame(aspect);
            camera.backgroundColor = background;
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(camera, request)) return false;
            bool fog = RenderSettings.fog;
            var mode = RenderSettings.ambientMode;
            var sky = RenderSettings.ambientSkyColor;
            var ambient = RenderSettings.ambientLight;
            var previousTarget = RenderTexture.active;
            try
            {
                RenderSettings.fog = false;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.41f, .44f, .45f);
                RenderPipeline.SubmitRenderRequest(camera, request);
                return true;
            }
            finally
            {
                RenderSettings.ambientSkyColor = sky;
                RenderSettings.ambientLight = ambient;
                RenderSettings.ambientMode = mode;
                RenderSettings.fog = fog;
                RenderTexture.active = previousTarget;
            }
        }

        void Frame(float aspect)
        {
            Quaternion angle = Quaternion.Euler(pitch, yaw, 0f);
            var box = ProjectedBounds(angle);
            if (workshop)
            {
                if (referenceSize <= 0f || Mathf.Abs(referenceAspect - aspect) > .005f)
                {
                    var initial = ProjectedBounds(Quaternion.Euler(7f, 82f, 0f)).extents;
                    referenceAspect = aspect;
                    referenceSize = Mathf.Max(initial.y / .91f, initial.x / (aspect * .69f));
                }
            }
            else if (weaponView != null && !weaponView.IsMelee && weaponView.Form != WeaponForm.Pistol && aspect >= 2.25f)
            {
                // A higher angle preserves the whole silhouette in unusually wide tiles.
                for (float tilt = 16f; tilt <= 88f && HorizontalFill(box.extents) < .91f; tilt += 8f)
                {
                    var candidate = Quaternion.Euler(tilt, 87f, 0f);
                    var projected = ProjectedBounds(candidate);
                    if (HorizontalFill(projected.extents) <= HorizontalFill(box.extents)) continue;
                    angle = candidate;
                    box = projected;
                }
            }
            Vector3 half = box.extents, centre = model.position + angle * box.center;
            float fit = Mathf.Max(half.y / .94f, half.x / (aspect * .94f));
            if (workshop) fit = Mathf.Max(referenceSize, fit);
            camera.orthographicSize = Mathf.Max(.012f, fit / zoom);
            camera.aspect = aspect;
            camera.transform.SetPositionAndRotation(centre - angle * Vector3.forward * 3.4f, angle);

            float HorizontalFill(Vector3 extent) => Mathf.Min(.94f, extent.x * .94f / Mathf.Max(.0001f, aspect * extent.y));
        }

        Bounds ProjectedBounds(Quaternion angle)
        {
            var inverse = Quaternion.Inverse(angle);
            var origin = model.position;
            Vector3 min = Vector3.one * float.PositiveInfinity, max = Vector3.one * float.NegativeInfinity;
            foreach (var mesh in geometry)
            {
                var bounds = mesh.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var projected = inverse * (mesh.transform.TransformPoint(Corner(bounds, i)) - origin);
                    min = Vector3.Min(min, projected); max = Vector3.Max(max, projected);
                }
            }
            var result = new Bounds();
            result.SetMinMax(min, max);
            return result;
        }

        static Vector3 Corner(Bounds bounds, int i)
        {
            var size = bounds.extents;
            return bounds.center + new Vector3((i & 1) == 0 ? -size.x : size.x, (i & 2) == 0 ? -size.y : size.y, (i & 4) == 0 ? -size.z : size.z);
        }

        internal void Clear()
        {
            if (model == null) return;
            model.gameObject.SetActive(false);
            Object.Destroy(model.gameObject);
            model = null;
            weaponView = null;
            geometry = null;
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
