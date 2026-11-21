using UnityEngine;
using UnityEngine.Rendering;

namespace Dustline
{
    public sealed class HarborAtmosphere : MonoBehaviour
    {
        GameSession session;
        Light sun;
        Material previousSky, sky;
        Texture2D clouds;

        static readonly int SunVector = Shader.PropertyToID("_SunVector");
        static readonly int Zenith = Shader.PropertyToID("_Zenith");
        static readonly int Horizon = Shader.PropertyToID("_Horizon");
        static readonly int SunTint = Shader.PropertyToID("_SunTint");
        static readonly int CloudMotion = Shader.PropertyToID("_CloudMotion");
        static readonly int CloudQuality = Shader.PropertyToID("_CloudQuality");

        internal void Initialize(GameSession owner, Light directional)
        {
            session = owner;
            sun = directional;
            previousSky = RenderSettings.skybox;
            var shader = Shader.Find("Dustline/Harbor Sky");
            if (shader != null && shader.isSupported)
            {
                sky = new Material(shader) { name = "North Cape sky", hideFlags = HideFlags.DontSave };
                clouds = MakeNoise();
                sky.SetTexture("_CloudNoise", clouds);
                RenderSettings.skybox = sky;
            }
            Apply(0f);
        }

        void Update()
        {
            if (session != null && session.Phase == SessionPhase.Raid)
                Apply(Mathf.Clamp01(1f - session.RaidRemaining / 900f));
        }

        void Apply(float progress)
        {
            float sunset = Mathf.SmoothStep(0f, 1f, (progress - 0.37f) / 0.63f);
            float elevation = Mathf.Lerp(38f, -2f, progress);
            float sunlight = Mathf.SmoothStep(0f, 1f, (elevation + 0.2f) / 7f);
            sun.transform.rotation = Quaternion.Euler(elevation, Mathf.Lerp(-59f, -31f, progress), 0f);
            var sunColor = Color.Lerp(new Color(1f, 0.93f, 0.83f), new Color(1f, 0.57f, 0.32f), sunset);
            sun.color = sunColor;
            sun.intensity = Mathf.Lerp(1.28f, 0.89f, sunset) * sunlight;
            sun.shadowStrength = 0.84f * sunlight;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(new Color(0.40f, 0.48f, 0.58f), new Color(0.25f, 0.30f, 0.43f), sunset);
            RenderSettings.ambientEquatorColor = Color.Lerp(new Color(0.31f, 0.36f, 0.40f), new Color(0.34f, 0.29f, 0.33f), sunset);
            RenderSettings.ambientGroundColor = Color.Lerp(new Color(0.17f, 0.18f, 0.17f), new Color(0.17f, 0.15f, 0.17f), sunset);
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fog = true;
            RenderSettings.fogStartDistance = 435f;
            RenderSettings.fogEndDistance = 1760f;
            var horizon = Color.Lerp(new Color(0.64f, 0.74f, 0.80f), new Color(0.57f, 0.43f, 0.40f), sunset);
            RenderSettings.fogColor = horizon;
            if (sky == null) return;
            Vector3 light = -sun.transform.forward;
            sky.SetVector(SunVector, new Vector4(light.x, light.y, light.z, Mathf.Lerp(1f, 0.53f, sunset)));
            sky.SetColor(Zenith, Color.Lerp(new Color(0.24f, 0.49f, 0.75f), new Color(0.12f, 0.21f, 0.40f), sunset));
            sky.SetColor(Horizon, horizon);
            sky.SetColor(SunTint, sunColor);
            sky.SetVector(CloudMotion, new Vector4(progress * 0.31f, progress * 0.13f, sunset, progress));
            sky.SetFloat(CloudQuality, session.QualityLevel);
        }

        static Texture2D MakeNoise()
        {
            const int resolution = 128;
            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, true, true)
            {
                name = "Harbor cloud weather", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear,
                hideFlags = HideFlags.DontSave
            };
            var pixels = new Color32[resolution * resolution];
            for (int y = 0; y < resolution; y++)
                for (int x = 0; x < resolution; x++)
                {
                    float u = x / (float)resolution, v = y / (float)resolution;
                    float macro = TileNoise(u, v, 6, 1219) * 0.66f + TileNoise(u, v, 12, 408) * 0.25f + TileNoise(u, v, 24, 661) * 0.09f;
                    float detail = TileNoise(u, v, 17, 813) * 0.69f + TileNoise(u, v, 34, 251) * 0.31f;
                    pixels[x + y * resolution] = new Color32((byte)Mathf.RoundToInt(macro * 255f), (byte)Mathf.RoundToInt(detail * 255f), 0, 255);
                }
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        static float TileNoise(float u, float v, int size, int seed)
        {
            float x = u * size, y = v * size;
            int a = Mathf.FloorToInt(x), b = Mathf.FloorToInt(y);
            float tx = x - a, ty = y - b;
            tx *= tx * (3f - 2f * tx);
            ty *= ty * (3f - 2f * ty);
            return Mathf.Lerp(Mathf.Lerp(Hash(a, b), Hash(a + 1, b), tx), Mathf.Lerp(Hash(a, b + 1), Hash(a + 1, b + 1), tx), ty);

            float Hash(int i, int j)
            {
                uint h = unchecked((uint)(i % size * 73856093 ^ j % size * 19349663 ^ seed * 83492791));
                h = unchecked((h ^ (h >> 14)) * 0x85ebca6bu);
                h = unchecked((h ^ (h >> 13)) * 0xc2b2ae35u);
                return ((h ^ (h >> 16)) & 65535u) / 65535f;
            }
        }

        void OnEnable()
        {
            if (sky != null) RenderSettings.skybox = sky;
        }

        void OnDisable()
        {
            if (sky != null && RenderSettings.skybox == sky) RenderSettings.skybox = previousSky;
        }

        void OnDestroy()
        {
            if (sky != null) Destroy(sky);
            if (clouds != null) Destroy(clouds);
        }
    }
}
