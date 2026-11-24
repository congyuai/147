using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Dustline
{
    internal sealed class StrategyTerrain : IDisposable
    {
        internal const int Layer = 27;
        internal const int LayerMask = 1 << Layer;
        const int Cols = 150, Rows = 112;
        const float Left = -125, Right = 125, Near = -87, Far = 104;
        const float EyeDistance = 178f;
        static readonly Vector2[] Sites = { new Vector2(-46, 5), new Vector2(0, -18), new Vector2(51, 25) };
        static readonly Color32 Backdrop = new Color32(16, 25, 34, 255);
        static readonly Vector3 Key = new Vector3(-.51f, .76f, -.40f).normalized;
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");

        readonly GameObject stage;
        readonly Camera eye;
        readonly RawImage image;
        readonly Texture previousTexture;
        readonly float[,] relief = new float[Cols + 1, Rows + 1];
        readonly Vector3[] pins = new Vector3[Sites.Length];
        readonly List<Object> assets = new List<Object>();
        readonly RenderTexture[] previews = new RenderTexture[Sites.Length];
        RenderTexture target;
        Vector3 center;
        Quaternion angle;
        float extent, aspect, retryMainAt;
        int focus = -1;
        bool dirty, mainRendered, disposed;

        internal StrategyTerrain(Transform owner, RawImage image)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (image == null) throw new ArgumentNullException(nameof(image));
            this.image = image;
            previousTexture = image.texture;
            stage = new GameObject("Regional relief stage") { layer = Layer, hideFlags = HideFlags.DontSave };
            stage.SetActive(false);
            stage.transform.position = new Vector3(-4096, -1300, 4096);
            stage.transform.SetParent(owner, true);
            eye = Child("Relief camera").AddComponent<Camera>();
            eye.enabled = false;
            eye.orthographic = true;
            eye.nearClipPlane = 1;
            eye.farClipPlane = 430;
            eye.clearFlags = CameraClearFlags.SolidColor;
            eye.backgroundColor = Backdrop;
            eye.cullingMask = LayerMask;
            eye.allowMSAA = false;
            eye.allowHDR = false;
            eye.useOcclusionCulling = false;
            var options = eye.GetUniversalAdditionalCameraData();
            options.SetRenderer(0);
            options.renderShadows = false;
            options.renderPostProcessing = false;
            options.requiresColorOption = CameraOverrideOption.Off;
            options.requiresDepthOption = CameraOverrideOption.Off;

            try
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) throw new InvalidOperationException("无法加载战略地形材质。");
                BuildRelief(shader);
                var props = MakeMaterial(shader, "Regional structures", BakePalette());
                BuildHarbor(props);
                BuildQuarry(props);
                BuildSluice(props);
                BuildConnections(props);
                for (int i = 0; i < pins.Length; i++)
                    pins[i] = OnGround(Sites[i].x, Sites[i].y, i == 0 ? 1.1f : .35f);
                TakeView(-1, ImageAspect(), out center, out angle, out extent);
                Tick(0f);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal void Focus(int index)
        {
            if (!disposed && index >= -1 && index < Sites.Length) focus = index;
        }

        internal void Tick(float dt)
        {
            if (disposed || stage == null || eye == null || image == null) return;
            float a = ImageAspect();
            if (target == null || Mathf.Abs(aspect - a) > .006f)
            {
                int width = Mathf.Min(1024, Mathf.RoundToInt(640 * a));
                int height = Mathf.Max(1, Mathf.RoundToInt(width / a));
                ReleaseTarget(target);
                target = NewTarget(width, height, "Regional relief view");
                image.texture = target;
                aspect = a;
                dirty = true;
                mainRendered = false;
                retryMainAt = 0f;
            }
            TakeView(focus, aspect, out var goal, out var rotation, out float zoom);
            if ((goal - center).sqrMagnitude > 0f || Mathf.Abs(extent - zoom) > 0f || Quaternion.Angle(angle, rotation) > .001f)
            {
                float blend = 1f - Mathf.Exp(-6.4f * Mathf.Clamp(dt, 0f, .1f));
                center = Vector3.Lerp(center, goal, blend);
                angle = Quaternion.Slerp(angle, rotation, blend);
                extent = Mathf.Lerp(extent, zoom, blend);
                if ((center - goal).sqrMagnitude < .0004f && Mathf.Abs(extent - zoom) < .02f && Quaternion.Angle(angle, rotation) < .08f)
                {
                    center = goal;
                    angle = rotation;
                    extent = zoom;
                }
                dirty = true;
            }
            ApplyView(center, angle, extent, aspect);
            if (CanRender && Time.unscaledTime >= retryMainAt && (dirty || !mainRendered || !target.IsCreated()))
            {
                mainRendered = Render(target);
                retryMainAt = mainRendered ? 0f : Time.unscaledTime + .75f;
                dirty = !mainRendered;
            }
            image.enabled = mainRendered && target.IsCreated();
        }

        // Viewport points are not clamped: a focused view can move other landmarks off-screen.
        internal Vector2 Pin(int index)
        {
            if (disposed || stage == null || eye == null || index < 0 || index >= pins.Length) return Vector2.one * .5f;
            var p = eye.WorldToViewportPoint(stage.transform.TransformPoint(pins[index]));
            return new Vector2(p.x, p.y);
        }

        internal Texture Preview(int index)
        {
            if (disposed || stage == null || eye == null || !CanRender || index < 0 || index >= previews.Length) return null;
            var frame = previews[index];
            if (frame != null && frame.IsCreated()) return frame;
            if (frame == null) frame = NewTarget(512, 292, "Regional landmark view " + index);
            previews[index] = frame;
            try
            {
                var look = pins[index] + Vector3.up * (index == 0 ? 4f : 1.8f);
                var rotation = Quaternion.Euler(index == 1 ? 42 : 29, index == 2 ? 28 : -24, 0);
                ApplyView(look, rotation, index == 0 ? 11.8f : 14.5f, frame.width / (float)frame.height);
                if (Render(frame)) return frame;
                ReleaseTarget(frame);
                previews[index] = null;
                return null;
            }
            finally { ApplyView(center, angle, extent, aspect); }
        }

        float ImageAspect()
        {
            var rect = image.rectTransform.rect;
            return rect.width > 0 && rect.height > 0 ? Mathf.Clamp(rect.width / rect.height, .4f, 5f) : 1.8f;
        }

        void TakeView(int index, float ratio, out Vector3 look, out Quaternion rotation, out float size)
        {
            if (index < 0)
            {
                look = new Vector3(0, 3, 4);
                rotation = Quaternion.Euler(34f, -10f, 0);
                size = Mathf.Max(32f, 77f / ratio);
                return;
            }
            rotation = Quaternion.Euler(31f, index == 2 ? 13f : -12f, 0);
            size = Mathf.Max(index == 1 ? 15.5f : 13.5f, 24f / ratio);
            look = pins[index] + rotation * new Vector3(.044f * size * ratio, .115f * size, 0);
        }

        void ApplyView(Vector3 look, Quaternion rotation, float size, float ratio)
        {
            var origin = stage.transform;
            eye.transform.SetPositionAndRotation(origin.TransformPoint(look - rotation * Vector3.forward * EyeDistance), origin.rotation * rotation);
            eye.orthographicSize = size * Mathf.Abs(origin.lossyScale.y);
            eye.aspect = ratio;
        }

        static bool CanRender => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null
            && GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset;

        bool Render(RenderTexture frame)
        {
            if (!frame.IsCreated() && !frame.Create()) return false;
            bool fog = RenderSettings.fog;
            var previous = RenderTexture.active;
            // The stage exists in the gameplay scene but is enabled only during this camera's request.
            stage.SetActive(true);
            try
            {
                var request = new RenderPipeline.StandardRequest { destination = frame };
                if (!RenderPipeline.SupportsRenderRequest(eye, request)) return false;
                RenderSettings.fog = false;
                RenderPipeline.SubmitRenderRequest(eye, request);
                return true;
            }
            finally
            {
                stage.SetActive(false);
                RenderSettings.fog = fog;
                RenderTexture.active = previous;
            }
        }

        static RenderTexture NewTarget(int width, int height, string name) => new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
        {
            name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave
        };

        GameObject Child(string name)
        {
            var go = new GameObject(name) { layer = Layer };
            go.transform.SetParent(stage.transform, false);
            return go;
        }

        Material MakeMaterial(Shader shader, string name, Texture2D texture)
        {
            assets.Add(texture);
            var material = new Material(shader) { name = name, hideFlags = HideFlags.DontSave };
            assets.Add(material);
            material.SetTexture(BaseMap, texture);
            return material;
        }

        void AddMesh(Mesh mesh, string name, Material material)
        {
            mesh.name = name;
            assets.Add(mesh);
            var go = Child(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mesh.UploadMeshData(true);
        }

        void BuildRelief(Shader shader)
        {
            const int stride = Cols + 1;
            var vertices = new Vector3[stride * (Rows + 1)];
            var uv = new Vector2[vertices.Length];
            var faces = new int[Cols * Rows * 6];
            for (int j = 0; j <= Rows; j++)
                for (int i = 0; i <= Cols; i++)
                {
                    int v = i + j * stride;
                    float u = i / (float)Cols, t = j / (float)Rows;
                    float x = Mathf.Lerp(Left, Right, u), z = Mathf.Lerp(Near, Far, t);
                    relief[i, j] = Sculpt(x, z);
                    vertices[v] = new Vector3(x, relief[i, j], z);
                    uv[v] = new Vector2(u, t);
                    if (i == Cols || j == Rows) continue;
                    int k = (i + j * Cols) * 6;
                    faces[k] = v; faces[k + 1] = v + stride; faces[k + 2] = v + stride + 1;
                    faces[k + 3] = v; faces[k + 4] = v + stride + 1; faces[k + 5] = v + 1;
                }
            var material = MakeMaterial(shader, "Slate relief and waterways", BakeRelief());
            AddMesh(new Mesh { vertices = vertices, uv = uv, triangles = faces }, "Continuous regional terrain", material);
            AddMesh(new Mesh {
                vertices = new[] { new Vector3(Left, .015f, Near), new Vector3(Left, .015f, Far), new Vector3(Right, .015f, Far), new Vector3(Right, .015f, Near) },
                uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right }, triangles = new[] { 0, 1, 2, 0, 2, 3 }
            }, "Estuary and sluice water", material);
        }

        static float Sculpt(float x, float z)
        {
            float grain = Mathf.PerlinNoise(x * .067f + 22.4f, z * .085f + 48.1f);
            float y = 2.7f + grain * 4.1f + 2.3f * Mathf.Sin(x * .061f + z * .033f);
            y += 16f * Hill(x, z, -35, 60, 25, 40) + 21f * Hill(x, z, 8, 49, 35, 26)
                + 18f * Hill(x, z, 86, 43, 26, 54) + 13f * Hill(x, z, -28, -60, 34, 24)
                + 13f * Hill(x, z, 42, -47, 22, 30) + 10f * Hill(x, z, -90, 58, 37, 29);
            float shore = -54 + 7 * Mathf.Sin((z + 4) * .054f) + 3 * Mathf.Sin(z * .118f + 1f);
            if (z > 35) shore -= 13f * Ease((z - 35) / 26);
            y = Mathf.Lerp(-3.6f + grain, y, Ease((x - shore + 4.5f) / 10f));
            float apron = Mathf.Max(Mathf.Abs(x + 44f) - 16f, Mathf.Abs(z - 4) - 13f);
            y = Mathf.Lerp(y, .52f, 1f - Ease(apron / 4.3f));

            float qx = x - Sites[1].x, qz = z - Sites[1].y;
            float quarry = 1f - Ease((Mathf.Max(Mathf.Abs(qx), Mathf.Abs(qz)) - 19f) / 9f);
            if (quarry > 0f) y = Mathf.Lerp(y, 4.8f + QuarryTerrain.Floor(qx / .063f, qz / .063f) * .14f, quarry);

            float dx = Mathf.Abs(x - Channel(z));
            float gulley = (1f - Ease((dx - 3.1f) / 8.1f)) * Ease((z + 22) / 15f);
            y = Mathf.Lerp(y, -1.85f + grain * .35f, gulley);
            return y;
        }

        static float Channel(float z) => Sites[2].x + 5.4f * Mathf.Sin((z - Sites[2].y) * .057f);
        static float Hill(float x, float z, float cx, float cz, float sx, float sz)
        {
            float a = (x - cx) / sx, b = (z - cz) / sz;
            return Mathf.Exp(-(a * a + b * b) * 1.58f);
        }

        static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        float Sample(float x, float z, out Vector3 normal)
        {
            float u = Mathf.Clamp01((x - Left) / (Right - Left)) * Cols;
            float v = Mathf.Clamp01((z - Near) / (Far - Near)) * Rows;
            int i = Mathf.Min((int)u, Cols - 1), j = Mathf.Min((int)v, Rows - 1);
            u -= i; v -= j;
            float a = relief[i, j], b = relief[i, j + 1], c = relief[i + 1, j + 1], d = relief[i + 1, j];
            float dx = v >= u ? c - b : d - a, dz = v >= u ? b - a : c - d;
            normal = new Vector3(-dx * (Cols / (Right - Left)), 1f, -dz * (Rows / (Far - Near))).normalized;
            return a + u * dx + v * dz;
        }

        Vector3 OnGround(float x, float z, float offset = 0f) => new Vector3(x, Sample(x, z, out _) + offset, z);

        Texture2D BakeRelief()
        {
            const int w = 768, h = 576;
            var pixels = new Color32[w * h];
            var low = new Color32(37, 52, 65, 255);
            var high = new Color32(71, 86, 97, 255);
            var quarry = new Color32(92, 102, 105, 255);
            var deep = new Color32(18, 34, 49, 255);
            var shallow = new Color32(39, 69, 80, 255);
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    float x = Mathf.Lerp(Left, Right, (i + .5f) / w), z = Mathf.Lerp(Near, Far, (j + .5f) / h);
                    float y = Sample(x, z, out var normal);
                    float mottling = Mathf.PerlinNoise(x * .49f + 83.7f, z * .43f + 7.7f) - .5f;
                    float strata = 1f - Mathf.Clamp01(Mathf.Abs(Mathf.Repeat(y + 1.1f, 2.2f) - 1.1f) * 3.15f);
                    float rock = Mathf.Clamp01((.8f - normal.y) * 3.6f);
                    var tone = Color32.Lerp(low, high, Mathf.Clamp01(y / 24f + mottling * .23f));
                    float cut = 1f - Ease((Vector2.Distance(new Vector2(x, z), Sites[1]) - 13f) / 13f);
                    tone = Color32.Lerp(tone, quarry, cut * (.2f + rock * .4f));
                    float light = Vector3.Dot(normal, Key) - .70f;
                    tone = Tint(tone, 1f + light * .55f + mottling * .23f - strata * (.08f + rock * .08f));
                    var water = Color32.Lerp(deep, shallow, Ease((y + 2.8f) / 2.8f));
                    float ripple = Mathf.Sin(z * 9.1f + Mathf.Sin(x * .48f) * 1.6f);
                    water = Tint(water, 1f + mottling * .115f + ripple * .034f);
                    pixels[i + j * w] = Color32.Lerp(tone, water, Ease((.22f - y) / .52f));
                }
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, true, false)
            {
                name = "Regional relief albedo", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        void BuildHarbor(Material material)
        {
            var b = new Batch();
            b.Box(new Vector3(-44.8f, .61f, 4), new Vector3(29.4f, .68f, 25.2f), Tone.Concrete);
            b.Box(new Vector3(-59.5f, .34f, 4), new Vector3(.64f, 1.22f, 24.9f), Tone.Zinc);
            foreach (float z in new[] { -5.3f, 10.3f })
            {
                b.Box(new Vector3(-64.1f, .38f, z), new Vector3(13.4f, .67f, 3.9f), Tone.Slate);
                b.Box(new Vector3(-70.45f, .46f, z), new Vector3(.53f, .88f, 4.1f), Tone.Concrete);
                for (int i = 0; i < 5; i++)
                    b.Box(new Vector3(-60 - i * 2.35f, .57f, z + 1.87f), new Vector3(.38f, 1.26f, .34f), Tone.Dark);
            }
            Crane(-54.8f, -2.6f, 1.0f);
            Crane(-55.7f, 10.7f, .87f);
            for (int row = 0; row < 4; row++)
                for (int col = 0; col < 3; col++)
                {
                    if (row == 2 && col == 1) continue;
                    float x = -44.3f + col * 4.4f, z = -4.25f + row * 5.4f;
                    var tone = ((row + col * 2) % 4) switch { 0 => Tone.Rust, 1 => Tone.Blue, 2 => Tone.Teal, _ => Tone.Ochre };
                    Container(x, z, .97f, tone);
                    if ((row + col) % 3 == 0) Container(x, z, 2.18f, tone == Tone.Blue ? Tone.Teal : Tone.Slate);
                }
            b.Box(new Vector3(-35.1f, 2.38f, 13.4f), new Vector3(8.4f, 2.8f, 4.7f), Tone.Slate);
            b.Box(new Vector3(-35.1f, 3.87f, 13.4f), new Vector3(8.9f, .33f, 5.2f), Tone.Zinc);
            b.Box(new Vector3(-64.9f, .30f, 23.2f), new Vector3(4.3f, .61f, 14.8f), Tone.Dark);
            b.Box(new Vector3(-64.9f, .66f, 23.2f), new Vector3(3.95f, .16f, 14.1f), Tone.Slate);
            b.Box(new Vector3(-64.9f, 1.46f, 27.6f), new Vector3(3.6f, 1.45f, 2.3f), Tone.Zinc);
            AddMesh(b.Mesh(), "North Cape cranes, quay and freight stacks", material);

            void Crane(float x, float z, float s)
            {
                Vector3 P(float u, float v, float w) => new Vector3(x + u * s, .96f + v * s, z + w * s);
                foreach (int side in new[] { -1, 1 })
                {
                    b.Beam(P(2, 0, side * 1.85f), P(.9f, 8.6f, side * 1.3f), .56f, .52f, Tone.Ochre);
                    b.Beam(P(-1.8f, 0, side * 1.85f), P(-.9f, 8.6f, side * 1.3f), .55f, .52f, Tone.Ochre);
                    b.Beam(P(1.7f, 2.4f, side * 1.75f), P(-1.02f, 7.6f, side * 1.35f), .25f, .25f, Tone.Zinc);
                    b.Beam(P(-11, 8.35f, side * .65f), P(4.4f, 8.35f, side * .65f), .38f, .36f, Tone.Ochre);
                    b.Beam(P(-11, 9.65f, side * .65f), P(4.4f, 9.65f, side * .65f), .3f, .31f, Tone.Ochre);
                    for (int n = 0; n < 7; n++)
                        b.Beam(P(-11 + n * 2.2f, 8.35f, side * .65f), P(-8.8f + n * 2.2f, 9.65f, side * .65f), .19f, .18f, Tone.Ochre);
                }
                b.Beam(P(0, 8.5f, 0), P(1.0f, 14, 0), .52f, .52f, Tone.Zinc);
                b.Beam(P(1.0f, 14, 0), P(-10.7f, 9.65f, 0), .21f, .20f, Tone.Zinc);
                b.Beam(P(1.0f, 14, 0), P(4.4f, 9.65f, 0), .19f, .20f, Tone.Zinc);
                b.Box(P(3.9f, 8.35f, 0), new Vector3(1.9f, 1.05f, 1.9f) * s, Tone.Slate);
                b.Beam(P(-6.4f, 8.17f, 0), P(-6.4f, 3.15f, 0), .13f, .13f, Tone.Zinc);
                b.Box(P(-6.4f, 3.07f, 0), new Vector3(1.6f, .24f, .69f) * s, Tone.Ochre);
            }

            void Container(float x, float z, float y, Tone tone)
            {
                b.Box(new Vector3(x, y + .59f, z), new Vector3(2.65f, 1.19f, 4.3f), tone);
                b.Box(new Vector3(x, y + 1.2f, z), new Vector3(2.72f, .105f, 4.34f), tone);
                for (int rib = 0; rib < 7; rib++)
                    b.Box(new Vector3(x + 1.35f, y + .59f, z - 1.79f + rib * .6f), new Vector3(.09f, 1.06f, .11f), tone);
            }
        }

        void BuildQuarry(Material material)
        {
            var b = new Batch();
            Vector2 Convert(Vector3 world) => Sites[1] + new Vector2(world.x, world.z) * .063f;
            foreach (var route in QuarryLayout.Roads)
                for (int i = 1; i < route.Points.Length; i++)
                    Strip(b, Convert(route.Points[i - 1]), Convert(route.Points[i]), route.HalfWidth * .043f, Tone.Gravel);
            foreach (var shed in QuarryLayout.Sheds)
            {
                var p = Convert(new Vector3(shed.Area.center.x, 0f, shed.Area.center.y));
                var at = OnGround(p.x, p.y);
                float w = shed.Area.width * .124f, l = shed.Area.height * .126f, h = shed.Height * .26f;
                b.Box(at + Vector3.up * (h * .43f), new Vector3(w, h, l), Tone.Concrete);
                b.Box(at + Vector3.up * h, new Vector3(w + .37f, .24f, l + .33f), Tone.Slate);
                b.Box(at + new Vector3(0, h * .32f, -l * .503f), new Vector3(w * .43f, h * .68f, .10f), Tone.Dark);
            }
            var crusher = Convert(new Vector3(QuarryLayout.CrusherFootprint.center.x, 0, QuarryLayout.CrusherFootprint.center.y));
            var head = OnGround(crusher.x, crusher.y, 4.65f);
            var tail = OnGround(crusher.x - 7.4f, crusher.y - 6.2f, .82f);
            foreach (int side in new[] { -1, 1 })
                foreach (int end in new[] { -1, 1 })
                {
                    var p = head + new Vector3(side * 1.54f, -.55f, end * 1.19f);
                    b.Beam(OnGround(p.x, p.z, -.2f), p, .28f, .28f, Tone.Ochre);
                }
            b.Box(head, new Vector3(3.9f, 1.3f, 2.95f), Tone.Slate);
            b.Box(head + Vector3.up * .8f, new Vector3(3.4f, .78f, 2.5f), Tone.Zinc);
            b.Beam(head, tail, 1.3f, .50f, Tone.Dark);
            b.Beam(head + new Vector3(.15f,.27f,0), tail + new Vector3(.15f,.27f,0), 1.08f, .24f, Tone.Ochre);
            for (int i = 1; i <= 3; i++)
            {
                var p = Vector3.Lerp(head, tail, i * .24f);
                b.Beam(OnGround(p.x, p.z, -.1f), p, .22f, .22f, Tone.Slate);
            }
            b.Mound(OnGround(-2.7f, -18.7f, -.12f), 3.8f, 1.56f, Tone.Rock);
            b.Mound(OnGround(4.3f, -23.4f, -.13f), 3.1f, 1.75f, Tone.Gravel);
            b.Mound(OnGround(13.3f, -21, -.22f), 2.2f, 1.32f, Tone.Rock);
            AddMesh(b.Mesh(), "Stone Ridge quarry, haul ramps and crusher", material);
        }

        void BuildSluice(Material material)
        {
            var b = new Batch();
            float x = Sites[2].x, z = Sites[2].y;
            var p = new Vector3(x, 0, z);
            b.Box(p + Vector3.up * .72f, new Vector3(15.5f, 2.9f, 3.1f), Tone.Concrete);
            b.Box(p + Vector3.up * 3.7f, new Vector3(17.8f, .64f, 2.65f), Tone.Slate);
            b.Box(p + new Vector3(0, 4.14f, -1.21f), new Vector3(17.7f, .33f, .16f), Tone.Zinc);
            b.Box(p + new Vector3(0, 4.14f, 1.21f), new Vector3(17.7f, .33f, .16f), Tone.Zinc);
            for (int i = -2; i <= 2; i++)
            {
                b.Box(p + new Vector3(i * 2.94f, 1.5f, -.22f), new Vector3(.73f, 4.35f, 5.3f), Tone.Zinc);
                if (i == 2) continue;
                var gate = p + new Vector3((i + .5f) * 2.94f, 1.82f, -1.63f);
                b.Box(gate, new Vector3(2.21f, 2.9f, .54f), Tone.Dark);
                for (int bar = 0; bar < 4; bar++)
                    b.Box(gate + new Vector3(0, -.91f + .61f * bar, -.29f), new Vector3(2.16f, .16f, .17f), Tone.Blue);
            }
            foreach (int side in new[] { -1, 1 })
            {
                var end = p + Vector3.right * (side * 7.95f);
                b.Box(end + Vector3.up * 1.9f, new Vector3(2.7f, 5.1f, 4.4f), Tone.Concrete);
                b.Box(end + Vector3.up * 4.6f, new Vector3(2.95f, .60f, 4.64f), Tone.Dark);
                b.Box(end + new Vector3(0, 3.28f, -2.21f), new Vector3(1.67f, 1.16f, .14f), Tone.Blue);
                b.Box(end + Vector3.up * 5.15f, new Vector3(.35f, .54f, .35f), Tone.Rust);
                Strip(b, new Vector2(end.x, z), new Vector2(end.x + side * 8.5f, z + 4.5f), .95f, Tone.Slate);
            }
            AddMesh(b.Mesh(), "Grey Bay locked sluice and service bridge", material);
        }

        void BuildConnections(Material material)
        {
            var b = new Batch();
            Road(new Vector2(-31, 5), new Vector2(-25, .6f), new Vector2(-20, -1.2f), new Vector2(-16, -6.1f));
            Road(new Vector2(18, -1.5f), new Vector2(24, 4), new Vector2(26, 14), new Vector2(33, 22), new Vector2(43, 25));
            Road(new Vector2(-37, 15), new Vector2(-25, 30), new Vector2(-7, 34), new Vector2(12, 32), new Vector2(31, 25));
            AddMesh(b.Mesh(), "Regional service tracks", material);

            void Road(params Vector2[] points)
            {
                for (int i = 1; i < points.Length; i++) Strip(b, points[i - 1], points[i], .55f, Tone.Slate);
            }
        }

        void Strip(Batch b, Vector2 from, Vector2 to, float halfWidth, Tone tone)
        {
            Vector2 d = to - from;
            Vector2 side = new Vector2(d.y, -d.x).normalized * halfWidth;
            int count = Mathf.Max(1, Mathf.CeilToInt(d.magnitude / .76f));
            for (int i = 0; i < count; i++)
            {
                Vector2 a = Vector2.Lerp(from, to, i / (float)count), c = Vector2.Lerp(from, to, (i + 1f) / count);
                b.Quad(At(a-side), At(c-side), At(c+side), At(a+side), tone);
            }
            Vector3 At(Vector2 p) => OnGround(p.x, p.y, .14f);
        }

        enum Tone { Dark, Slate, Concrete, Zinc, Blue, Teal, Rust, Ochre, Rock, Gravel }
        const int ShadeBands = 24;
        static readonly Color32[] Tones = {
            new Color32(33,48,61,255), new Color32(76,94,108,255), new Color32(114,128,134,255),
            new Color32(144,159,166,255), new Color32(65,107,133,255), new Color32(69,124,126,255),
            new Color32(146,100,78,255), new Color32(173,145,95,255), new Color32(106,116,117,255), new Color32(122,132,131,255)
        };

        static Texture2D BakePalette()
        {
            const int width = 32, height = 16;
            var atlas = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                name = "Relief structure palette", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point
            };
            var pixels = new Color32[width * height];
            for (int j = 0; j < height; j++)
                for (int i = 0; i < width; i++)
                    pixels[i + j * width] = Tint(Tones[Mathf.Min(j, Tones.Length - 1)], .52f + .58f * Mathf.Clamp01(i / (ShadeBands - 1f)));
            atlas.SetPixels32(pixels);
            atlas.Apply(false, true);
            return atlas;
        }

        static Color32 Tint(Color32 c, float light) => new Color32(
            (byte)Mathf.Clamp(c.r * light, 0, 255), (byte)Mathf.Clamp(c.g * light, 0, 255), (byte)Mathf.Clamp(c.b * light, 0, 255), 255);

        sealed class Batch
        {
            readonly List<Vector3> points = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int> triangles = new List<int>();

            void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Tone tone, bool quad)
            {
                var facing = Vector3.Cross(b - a, c - a).normalized;
                float light = Mathf.Clamp01(.18f + .78f * Mathf.Max(0, Vector3.Dot(facing, Key)));
                var swatch = new Vector2((Mathf.RoundToInt(light * (ShadeBands - 1)) + .5f) / 32, ((int)tone + .5f) / 16);
                int i = points.Count;
                points.Add(a); points.Add(b); points.Add(c);
                uv.Add(swatch); uv.Add(swatch); uv.Add(swatch);
                triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + 2);
                if (!quad) return;
                points.Add(d); uv.Add(swatch);
                triangles.Add(i); triangles.Add(i + 2); triangles.Add(i + 3);
            }

            internal void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Tone tone) => Face(a, b, c, d, tone, true);
            internal void Box(Vector3 center, Vector3 size, Tone tone) => Box(center, size, tone, Quaternion.identity);

            void Box(Vector3 center, Vector3 size, Tone tone, Quaternion rotation)
            {
                Vector3 half = size * .5f;
                Vector3 P(int x, int y, int z) => center + rotation * new Vector3(x * half.x, y * half.y, z * half.z);
                var a = P(-1,-1,-1); var b = P(1,-1,-1); var c = P(1,1,-1); var d = P(-1,1,-1);
                var e = P(-1,-1,1); var f = P(1,-1,1); var g = P(1,1,1); var h = P(-1,1,1);
                Quad(b,a,d,c,tone); Quad(e,f,g,h,tone); Quad(a,e,h,d,tone);
                Quad(f,b,c,g,tone); Quad(d,h,g,c,tone); Quad(a,b,f,e,tone);
            }

            internal void Beam(Vector3 from, Vector3 to, float width, float depth, Tone tone)
            {
                var delta = to - from;
                if (delta.sqrMagnitude < .0001f) return;
                Box((from + to) * .5f, new Vector3(width, delta.magnitude, depth), tone, Quaternion.FromToRotation(Vector3.up, delta));
            }

            internal void Mound(Vector3 foot, float radius, float height, Tone tone)
            {
                var top = foot + new Vector3(radius * .15f, height, -radius * .09f);
                for (int i = 0; i < 8; i++)
                    Face(Edge(i), Edge(i + 1), top, default, tone, false);
                Vector3 Edge(int i)
                {
                    int n = i % 8;
                    float a = n * Mathf.PI / 4f, r = radius * (.81f + n % 3 * .09f);
                    return foot + new Vector3(Mathf.Sin(a) * r, 0, Mathf.Cos(a) * r);
                }
            }

            internal Mesh Mesh()
            {
                var mesh = new Mesh();
                mesh.SetVertices(points);
                mesh.SetUVs(0, uv);
                mesh.SetTriangles(triangles, 0);
                return mesh;
            }
        }

        static void ReleaseTarget(RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            Destroy(texture);
        }

        static void Destroy(Object item)
        {
            if (item == null) return;
            if (Application.isPlaying) Object.Destroy(item);
            else Object.DestroyImmediate(item);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (image != null && image.texture == target) image.texture = previousTexture;
            if (stage != null) stage.SetActive(false);
            if (eye != null) eye.targetTexture = null;
            ReleaseTarget(target);
            foreach (var preview in previews) ReleaseTarget(preview);
            Destroy(stage);
            for (int i = assets.Count - 1; i >= 0; i--) Destroy(assets[i]);
            assets.Clear();
        }
    }
}
