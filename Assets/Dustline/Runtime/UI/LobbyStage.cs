using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Dustline
{
    internal sealed class LobbyStage : MonoBehaviour, IDisposable
    {
        const int Layer = OperatorPreview.Layer;
        const int LayerMask = OperatorPreview.LayerMask;
        const float Tilt = 5f, VerticalFov = 43f;
        readonly List<Object> assets = new List<Object>();
        readonly Transform[] soles = new Transform[2];
        GameObject root;
        Camera eye;
        OperatorDisplay display;
        Transform ambientContact;
        Material water;
        GameSession session;
        GridInventory shown;
        RectTransform swipe;
        Action redrawHud, refreshHud;
        Vector2 lastHudSize;
        Vector2Int lastResolution;
        Rect lastArea;
        OperatorStyle lastStyle;
        int lastRevision, lastCredits;
        bool disposed;
        float currentX, ripple;

        internal void Initialize(Transform owner, GameSession session, RectTransform swipe, Action redrawHud, Action refreshHud)
        {
            this.session = session;
            this.swipe = swipe;
            this.redrawHud = redrawHud;
            this.refreshHud = refreshHud;
            root = new GameObject("Lobby river overlook") { layer = Layer, hideFlags = HideFlags.DontSave };
            root.SetActive(false);
            root.transform.SetParent(owner, false);
            // Share the preview-only layer, but keep the two stages outside each other's camera and lamp ranges.
            root.transform.position = new Vector3(0f, -768f, 0f);
            try
            {
                eye = Child("Lobby scenery camera").AddComponent<Camera>();
                eye.transform.localRotation = Quaternion.Euler(Tilt, 0f, 0f);
                eye.cullingMask = LayerMask;
                eye.clearFlags = CameraClearFlags.SolidColor;
                eye.backgroundColor = new Color32(134, 155, 157, 255);
                eye.depth = 2f;
                eye.rect = new Rect(0f, 0f, 1f, 1f);
                eye.fieldOfView = VerticalFov;
                eye.nearClipPlane = .08f;
                eye.farClipPlane = 350f;
                eye.allowHDR = false;
                eye.allowMSAA = false;
                eye.useOcclusionCulling = false;
                var settings = eye.GetUniversalAdditionalCameraData();
                settings.SetRenderer(0);
                settings.renderPostProcessing = false;
                settings.renderShadows = false;
                settings.requiresColorOption = CameraOverrideOption.Off;
                settings.requiresDepthOption = CameraOverrideOption.Off;

                var decor = new LobbyGeometry(root.transform, assets.Add);
                water = decor.Build();
                var shadow = decor.ContactMaterial();
                ambientContact = decor.Contact("Operator ground shadow", shadow);
                ambientContact.localScale = new Vector3(.47f, 1f, .28f);
                for (int i = 0; i < soles.Length; i++)
                    soles[i] = decor.Contact(i == 0 ? "Right sole contact" : "Left sole contact", shadow);
                Lamp("Warm daylight", new Vector3(-1.55f, 2.75f, -2.25f), new Color(1f, .88f, .75f), 11f);
                Lamp("River fill", new Vector3(2.75f, 1.75f, -1.6f), new Color(.73f, .87f, 1f), 7.5f);
                Lamp("Sky rim", new Vector3(.81f, 2.7f, 3.0f), new Color(.90f, .96f, 1f), 9.5f);
                display = new OperatorDisplay(root.transform, Vector3.zero, 180f, 16f, false);
                root.SetActive(true);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal void Refresh()
        {
            if (disposed || display == null) return;
            var size = ((RectTransform)transform).rect.size;
            var resolution = new Vector2Int(Screen.width, Screen.height);
            var area = Screen.safeArea;
            bool reflow = size != lastHudSize || resolution != lastResolution || area != lastArea;
            if (reflow)
            {
                lastHudSize = size;
                lastResolution = resolution;
                lastArea = area;
                if (area.width > 1f && area.height > 1f && size.x > 1f && size.y > 1f)
                {
                    var bounds = Frame(area, resolution);
                    float horizontal = area.width * .046f, vertical = area.height * .028f;
                    swipe.anchorMin = new Vector2(Mathf.Max(.21f, (bounds.xMin - horizontal - area.xMin) / area.width),
                        Mathf.Max(.075f, (bounds.yMin - vertical - area.yMin) / area.height));
                    swipe.anchorMax = new Vector2(Mathf.Min(.74f, (bounds.xMax + horizontal - area.xMin) / area.width),
                        Mathf.Min(.87f, (bounds.yMax + vertical - area.yMin) / area.height));
                    swipe.offsetMin = swipe.offsetMax = Vector2.zero;
                    redrawHud();
                }
            }
            var bag = session.Loadout;
            int credits = session.Profile?.Credits ?? 0;
            bool changed = shown != bag || lastRevision != bag.Revision || lastStyle != session.SelectedOperator;
            if (changed)
            {
                shown = bag;
                lastRevision = bag.Revision;
                lastStyle = session.SelectedOperator;
                display.Select(lastStyle);
                display.SyncGear(bag, null);
                PlaceContact();
            }
            if (changed || reflow || lastCredits != credits) refreshHud();
            lastCredits = credits;
        }

        internal void Turn(float degrees)
        {
            if (disposed) return;
            display.Turn(degrees);
            PlaceContact();
        }

        Rect Frame(Rect safe, Vector2Int resolution)
        {
            // Keep the 3D image full-bleed. Only the stand point and frame scale follow the HUD safe area.
            float screenHeight = Mathf.Max(1, resolution.y), screenWidth = Mathf.Max(1, resolution.x);
            float distance = 3.25f / Mathf.Max(.68f, safe.height / screenHeight);
            float halfHeight = distance * Mathf.Tan(VerticalFov * .5f * Mathf.Deg2Rad);
            eye.aspect = screenWidth / screenHeight;
            eye.transform.localPosition = new Vector3(0f,
                .893f + distance * Mathf.Tan(Tilt * Mathf.Deg2Rad) - 2f * halfHeight * (safe.center.y / screenHeight - .5f), -distance);
            var ray = eye.ViewportPointToRay(new Vector3((safe.xMin + safe.width * .47f) / screenWidth, .5f, 0f));
            currentX = (ray.origin + ray.direction * ((root.transform.position.z - ray.origin.z) / ray.direction.z)).x - root.transform.position.x;
            display.Position = new Vector3(currentX, 0f, 0f);
            Vector3 low = eye.WorldToScreenPoint(root.transform.TransformPoint(new Vector3(currentX - .55f, -.09f, 0f)));
            Vector3 high = eye.WorldToScreenPoint(root.transform.TransformPoint(new Vector3(currentX + .55f, 1.89f, 0f)));
            PlaceContact();
            return Rect.MinMaxRect(low.x, low.y, high.x, high.y);
        }

        void LateUpdate()
        {
            if (disposed || display == null) return;
            Refresh();
            float dt = Time.unscaledDeltaTime;
            ripple = Mathf.Repeat(ripple + Mathf.Min(dt, .1f) * .035f, 1f);
            water.SetTextureOffset("_BaseMap", new Vector2(ripple, 0f));
            display.Tick(dt);
            PlaceContact();
        }

        void PlaceContact()
        {
            if (display.Model == null) return;
            ambientContact.localPosition = new Vector3(currentX + .09f, .003f, .069f);
            for (int i = 0; i < soles.Length; i++)
            {
                var point = root.transform.InverseTransformPoint(display.Model.FootContact(i == 0));
                soles[i].localPosition = new Vector3(point.x, .0045f, point.z);
                soles[i].localRotation = display.Model.transform.localRotation * display.Model.transform.parent.localRotation;
                soles[i].localScale = new Vector3(.129f, 1f, .202f);
            }
        }

        GameObject Child(string name)
        {
            var go = new GameObject(name) { layer = Layer };
            go.transform.SetParent(root.transform, false);
            return go;
        }

        void Lamp(string name, Vector3 position, Color tint, float power)
        {
            var light = Child(name).AddComponent<Light>();
            light.type = LightType.Point;
            light.transform.localPosition = position;
            light.range = 9f;
            light.color = tint;
            light.intensity = power;
            light.cullingMask = LayerMask;
            light.shadows = LightShadows.None;
        }

        void OnEnable() { if (!disposed && root != null) root.SetActive(true); }
        void OnDisable() { if (root != null) root.SetActive(false); }
        void OnDestroy() => Dispose();

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (root != null)
            {
                root.SetActive(false);
                Release(root);
            }
            for (int i = assets.Count - 1; i >= 0; i--) Release(assets[i]);
            assets.Clear();
            redrawHud = refreshHud = null;
            shown = null;
        }

        static void Release(Object asset)
        {
            if (Application.isPlaying) Object.Destroy(asset);
            else Object.DestroyImmediate(asset);
        }
    }
}
