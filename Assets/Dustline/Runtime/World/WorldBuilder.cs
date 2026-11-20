using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dustline
{
    public sealed class WorldBuilder : MonoBehaviour
    {
        enum LootKind { SupplyCrate, StorageLocker, Toolbox, MedicalCase, CombatCase }

        public Material DarkMaterial { get; private set; }
        public NavigationGrid Navigation { get; private set; }
        public ExtractionZone Exit { get; private set; }
        public IReadOnlyList<ExtractionZone> Exits { get; private set; } = Array.Empty<ExtractionZone>();
        public Light Sun { get; private set; }
        public Vector3 Spawn => PortLayout.Spawn;
        public readonly List<Vector3> PatrolPoints = new List<Vector3>();

        readonly List<UnityEngine.Object> generatedAssets = new List<UnityEngine.Object>();
        readonly List<ExtractionZone> exitZones = new List<ExtractionZone>();
        readonly List<Vector3> navigationSeeds = new List<Vector3>();
        GameSession session;
        System.Random random;
        PortMaterials portMaterials;
        AmbientSnapshot previousAmbient;
        bool ambientSaved;

        struct AmbientSnapshot
        {
            public AmbientMode Mode;
            public Color Ambient;
            public Color Sky;
            public Color Equator;
            public Color Ground;
            public bool Fog;
            public Color FogColor;
            public FogMode FogMode;
            public float FogStart;
            public float FogEnd;
            public Light Sun;
        }

        void Keep(UnityEngine.Object asset)
        {
            if (asset != null) generatedAssets.Add(asset);
        }

        public void Build(GameSession owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            ClearGeneratedWorld();
            session = owner;
            random = new System.Random(0xD05711 + Time.frameCount);
            SaveAmbient();
            ConfigureLighting();

            portMaterials = new PortMaterials(Keep);
            DarkMaterial = portMaterials.Dark;

            navigationSeeds.Clear();
            PatrolPoints.Clear();
            var scenery = new PortScenery(transform, portMaterials, Keep, navigationSeeds);
            scenery.Build();
            BuildExits();
            BuildFixedLoot();
            Physics.SyncTransforms();

            Navigation = new NavigationGrid(navigationSeeds, 68f, Physics.DefaultRaycastLayers, 0f);
            foreach (var point in Navigation.Points)
                if (PortLayout.CanPostBot(point)) PatrolPoints.Add(point);
            if (PatrolPoints.Count == 0) throw new InvalidOperationException("港区未生成可用巡逻路点。");
        }

        void SaveAmbient()
        {
            if (ambientSaved) return;
            previousAmbient = new AmbientSnapshot {
                Mode = RenderSettings.ambientMode,
                Ambient = RenderSettings.ambientLight,
                Sky = RenderSettings.ambientSkyColor,
                Equator = RenderSettings.ambientEquatorColor,
                Ground = RenderSettings.ambientGroundColor,
                Fog = RenderSettings.fog,
                FogColor = RenderSettings.fogColor,
                FogMode = RenderSettings.fogMode,
                FogStart = RenderSettings.fogStartDistance,
                FogEnd = RenderSettings.fogEndDistance,
                Sun = RenderSettings.sun
            };
            ambientSaved = true;
        }

        void ConfigureLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientLight = new Color(0.34f, 0.39f, 0.46f);
            RenderSettings.ambientSkyColor = new Color(0.22f, 0.32f, 0.43f);
            RenderSettings.ambientEquatorColor = new Color(0.39f, 0.43f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.13f, 0.14f, 0.14f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.27f, 0.34f, 0.39f);
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 170;
            RenderSettings.fogEndDistance = 275;

            var sunObject = new GameObject("Low angle harbor sun", typeof(Light));
            sunObject.transform.SetParent(transform, false);
            sunObject.transform.rotation = Quaternion.Euler(27, -38, 0);
            Sun = sunObject.GetComponent<Light>();
            Sun.type = LightType.Directional;
            Sun.color = new Color(1f, 0.78f, 0.59f);
            Sun.intensity = 1.15f;
            Sun.shadows = LightShadows.Soft;
            Sun.shadowStrength = 0.78f;
            Sun.shadowBias = 0.045f;
            Sun.shadowNormalBias = 0.35f;
            Sun.renderingLayerMask = 1;
            RenderSettings.sun = Sun;

            PointLight("Customs interior lamps", new Vector3(-198, 4.8f, 66), new Color(1f, 0.65f, 0.35f), 2.2f, 27);
            PointLight("Workshop interior lamps", new Vector3(83, 5.4f, -157), new Color(1f, 0.58f, 0.31f), 2.8f, 30);
            PointLight("Rail canopy lamps", new Vector3(-54, 5.1f, 273), new Color(0.74f, 0.84f, 1f), 2.5f, 30);
            PointLight("Quay service lamp", new Vector3(255, 5.1f, 117), new Color(0.82f, 0.94f, 1f), 2.1f, 24);
            PointLight("Gatehouse lamp", new Vector3(-350, 3.3f, -337), new Color(1f, 0.67f, 0.34f), 1.8f, 18);
        }

        void PointLight(string title, Vector3 position, Color color, float intensity, float range)
        {
            var go = new GameObject(title, typeof(Light));
            go.transform.SetParent(transform, false);
            go.transform.position = position;
            var light = go.GetComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;

        }

        void BuildExits()
        {
            exitZones.Clear();
            for (int i = 0; i < PortLayout.Exits.Count; i++)
            {
                var definition = PortLayout.Exits[i];
                var go = new GameObject("Extraction zone " + (i + 1));
                go.transform.SetParent(transform, false);
                go.transform.position = definition.Position;
                var zone = go.AddComponent<ExtractionZone>();
                zone.Initialize(session, definition);
                exitZones.Add(zone);
            }
            Exits = exitZones.AsReadOnly();
            Exit = exitZones[0];
        }

        void BuildFixedLoot()
        {
            var fixedVisuals = new PortMesh();
            var fixedPoints = new[] {
                new FixedLoot(-303, -255, LootKind.StorageLocker, "海关转运柜"),
                new FixedLoot(-277, -223, LootKind.SupplyCrate, "堆场物资箱"),
                new FixedLoot(-233, -153, LootKind.Toolbox, "堆场工具箱"),
                new FixedLoot(-149, -216, LootKind.SupplyCrate, "冷藏货箱"),
                new FixedLoot(-108, -118, LootKind.StorageLocker, "堆场储物柜"),
                new FixedLoot(-295, -104, LootKind.MedicalCase, "堆场医疗箱"),
                new FixedLoot(-194, -96, LootKind.SupplyCrate, "叉车旁物资箱"),
                new FixedLoot(-82, -201, LootKind.Toolbox, "维修通道工具箱"),

                new FixedLoot(-258, 16, LootKind.StorageLocker, "海关档案柜"),
                new FixedLoot(-238, 61, LootKind.MedicalCase, "值班医疗箱"),
                new FixedLoot(-209, 84, LootKind.SupplyCrate, "查验物资箱"),
                new FixedLoot(-177, 57, LootKind.Toolbox, "办公室工具箱"),
                new FixedLoot(-277, 117, LootKind.StorageLocker, "边门储物柜"),
                new FixedLoot(-108, 142, LootKind.SupplyCrate, "签发室物资箱"),
                new FixedLoot(-88, 153, LootKind.MedicalCase, "办公区医疗箱"),

                new FixedLoot(28, -238, LootKind.StorageLocker, "维修仓外柜"),
                new FixedLoot(52, -183, LootKind.Toolbox, "一号工位工具箱"),
                new FixedLoot(83, -177, LootKind.SupplyCrate, "维修仓货箱"),
                new FixedLoot(113, -177, LootKind.MedicalCase, "维修仓急救箱"),
                new FixedLoot(155, -235, LootKind.Toolbox, "泵房工具箱"),
                new FixedLoot(171, -197, LootKind.StorageLocker, "维修仓侧柜"),
                new FixedLoot(19, -92, LootKind.SupplyCrate, "备件物资箱"),
                new FixedLoot(181, -96, LootKind.Toolbox, "焊接工具箱"),
                new FixedLoot(132, -242, LootKind.MedicalCase, "夜班医疗箱"),

                new FixedLoot(-312, 215, LootKind.StorageLocker, "铁路信号柜"),
                new FixedLoot(-282, 273, LootKind.SupplyCrate, "西侧货台箱"),
                new FixedLoot(-228, 270, LootKind.Toolbox, "轨道工具箱"),
                new FixedLoot(-165, 314, LootKind.MedicalCase, "货台医疗箱"),
                new FixedLoot(-99, 218, LootKind.StorageLocker, "装卸储物柜"),
                new FixedLoot(-30, 310, LootKind.SupplyCrate, "雨棚物资箱"),
                new FixedLoot(45, 313, LootKind.Toolbox, "东侧扳手箱"),
                new FixedLoot(125, 318, LootKind.StorageLocker, "调度柜"),
                new FixedLoot(159, 212, LootKind.MedicalCase, "铁路急救箱"),

                new FixedLoot(237, -31, LootKind.SupplyCrate, "码头物资箱"),
                new FixedLoot(276, -13, LootKind.Toolbox, "缆绳工具箱"),
                new FixedLoot(239, 54, LootKind.StorageLocker, "泊位储物柜"),
                new FixedLoot(287, 72, LootKind.MedicalCase, "码头医疗箱"),
                new FixedLoot(236, 154, LootKind.SupplyCrate, "北岬货箱"),
                new FixedLoot(281, 176, LootKind.Toolbox, "吊机工具箱"),
                new FixedLoot(337, 120, LootKind.StorageLocker, "泊位柜"),
                new FixedLoot(337, 190, LootKind.MedicalCase, "值守医疗箱"),
                new FixedLoot(320, -27, LootKind.SupplyCrate, "装船物资箱")
            };
            for (int i = 0; i < fixedPoints.Length; i++)
            {
                FixedLoot point = fixedPoints[i];
                var container = CreateLootInternal(new Vector3(point.X, 0f, point.Z), point.Title,
                    point.Kind, SearchSeconds(point.Kind), fixedVisuals);
                FillFixedLoot(container, point.Kind, i);
            }

            var starter = CreateLootInternal(new Vector3(Spawn.x + 4, 0f, Spawn.z + 5), "共享初始补给",
                LootKind.MedicalCase, 1.1f, fixedVisuals);
            starter.Inventory.TryAdd("medkit", 1, out _);
            starter.Inventory.TryAdd("ammo", 30, out _);
            fixedVisuals.Bake(transform, "Fixed loot visuals", portMaterials.Structure, Keep, false);
        }

        void FillFixedLoot(LootContainer container, LootKind kind, int index)
        {
            session.LootTable.Fill(container.Inventory, new System.Random(0x5000 + index * 37));
            switch (kind)
            {
                case LootKind.StorageLocker:
                    container.Inventory.TryAdd(index % 3 == 0 ? "armor" : "circuit", 1, out _);
                    break;
                case LootKind.Toolbox:
                    container.Inventory.TryAdd("circuit", 1, out _);
                    container.Inventory.TryAdd("ammo", 12 + index % 3 * 6, out _);
                    break;
                case LootKind.MedicalCase:
                    container.Inventory.TryAdd("medkit", 1, out _);
                    break;
                case LootKind.SupplyCrate:
                    container.Inventory.TryAdd(index % 4 == 0 ? "relic" : "ammo", index % 4 == 0 ? 1 : 18, out _);
                    break;
            }
        }

        static float SearchSeconds(LootKind kind)
        {
            switch (kind)
            {
                case LootKind.StorageLocker: return 2.25f;
                case LootKind.Toolbox: return 1.7f;
                case LootKind.MedicalCase: return 1.2f;
                default: return 1.55f;
            }
        }

        public LootContainer CreateLoot(Vector3 point, string title)
        {
            return CreateLootInternal(point, title, LootKind.CombatCase, 2.2f, null);
        }

        LootContainer CreateLootInternal(Vector3 point, string title, LootKind kind, float searchSeconds, PortMesh visualBatch = null)
        {
            var root = new GameObject("Loot " + title);
            root.transform.SetParent(transform, false);
            root.transform.position = new Vector3(point.x, point.y, point.z);
            Vector3 dimensions = Dimensions(kind);
            var trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = Vector3.up * dimensions.y * 0.5f;
            trigger.size = dimensions;
            var container = root.AddComponent<LootContainer>();
            container.Initialize(title, session.Catalog, searchSeconds);
            BuildLootVisual(root.transform, kind, dimensions, visualBatch);
            return container;
        }

        static Vector3 Dimensions(LootKind kind)
        {
            switch (kind)
            {
                case LootKind.StorageLocker: return new Vector3(0.9f, 1.65f, 0.62f);
                case LootKind.Toolbox: return new Vector3(1.12f, 0.55f, 0.68f);
                case LootKind.MedicalCase: return new Vector3(0.95f, 0.6f, 0.72f);
                default: return new Vector3(0.96f, 0.76f, 0.78f);
            }
        }

        void BuildLootVisual(Transform parent, LootKind kind, Vector3 dimensions, PortMesh visualBatch)
        {
            var mesh = visualBatch ?? new PortMesh();
            Vector3 origin = visualBatch == null ? Vector3.zero : transform.InverseTransformPoint(parent.position);
            void Box(Vector3 center, Vector3 size, PortTone tone) => mesh.Box(origin + center, size, tone);
            switch (kind)
            {
                case LootKind.StorageLocker:
                    Box(Vector3.up * 0.825f, dimensions, PortTone.Slate);
                    Box(Vector3.up * 1.67f, new Vector3(1.02f, 0.09f, 0.7f), PortTone.Steel);
                    Box(new Vector3(0.28f, 0.86f, -0.34f), new Vector3(0.23f, 0.1f, 0.08f), PortTone.Ochre);
                    break;
                case LootKind.Toolbox:
                    Box(Vector3.up * 0.275f, dimensions, PortTone.Rust);
                    Box(new Vector3(0, 0.57f, 0), new Vector3(1.18f, 0.1f, 0.72f), PortTone.Steel);
                    Box(new Vector3(0, 0.78f, 0), new Vector3(0.45f, 0.08f, 0.1f), PortTone.Rust);
                    break;
                case LootKind.MedicalCase:
                    Box(Vector3.up * 0.3f, dimensions, PortTone.Red);
                    Box(new Vector3(0, 0.63f, 0), new Vector3(1f, 0.08f, 0.76f), PortTone.Steel);
                    Box(new Vector3(0, 0.69f, -0.405f), new Vector3(0.15f, 0.28f, 0.025f), PortTone.Paint);
                    Box(new Vector3(0, 0.69f, -0.405f), new Vector3(0.34f, 0.12f, 0.025f), PortTone.Paint);
                    break;
                default:
                    Box(Vector3.up * 0.38f, dimensions, PortTone.Ochre);
                    Box(new Vector3(0, 0.79f, 0), new Vector3(1.02f, 0.09f, 0.84f), PortTone.Steel);
                    for (int side = -1; side <= 1; side += 2)
                        Box(new Vector3(side * 0.31f, 0.41f, 0), new Vector3(0.08f, 0.68f, 0.84f), PortTone.Steel);
                    break;
            }
            if (visualBatch == null) mesh.Bake(parent, "Visual", portMaterials.Structure, Keep, false);
        }

        public void SpawnBots()
        {
            var candidates = new List<Vector3>();
            foreach (var post in PortLayout.BotPosts)
                if (PortLayout.CanPostBot(post) && Navigation.CanStandAt(post)) candidates.Add(post);
            foreach (var point in PatrolPoints)
                if (candidates.Count < 14 && PortLayout.CanPostBot(point) && !NearAny(point, candidates, 9f)) candidates.Add(point);
            for (int i = 0; i < 14 && candidates.Count > 0; i++)
            {
                int index = random.Next(candidates.Count);
                var bot = new GameObject("Local combatant " + (i + 1));
                bot.transform.SetParent(transform, false);
                bot.transform.position = candidates[index] + Vector3.up * 0.03f;
                candidates.RemoveAt(index);
                bot.AddComponent<BotController>().Initialize(session, i % 2 + 1, i);
            }
        }

        static bool NearAny(Vector3 point, List<Vector3> others, float distance)
        {
            foreach (var other in others)
                if (PortLayout.DistanceSquared(point, other) < distance * distance) return true;
            return false;
        }

        public ExtractionZone FindExit(Vector3 position)
        {
            foreach (var zone in exitZones)
                if (zone.Contains(position)) return zone;
            return null;
        }

        public string DistrictAt(Vector3 position)
        {
            int index = PortLayout.DistrictIndex(position);
            return index < 0 ? "港区道路" : PortLayout.Districts[index].Name;
        }

        public Vector3 PickPatrolPoint(Vector3 position, System.Random source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var nearby = new List<Vector3>();
            int current = NearestDistrict(position);
            foreach (var point in PatrolPoints)
            {
                if (!PortLayout.CanPostBot(point) || PortLayout.DistanceSquared(point, position) > 82f * 82f) continue;
                int district = NearestDistrict(point);
                if (PortLayout.Neighboring(current, district)) nearby.Add(point);
            }
            if (nearby.Count == 0)
            {
                foreach (var point in PatrolPoints)
                    if (PortLayout.CanPostBot(point) && PortLayout.Neighboring(current, NearestDistrict(point))) nearby.Add(point);
            }
            if (nearby.Count == 0) return position;
            return nearby[source.Next(nearby.Count)];
        }

        static int NearestDistrict(Vector3 position)
        {
            int exact = PortLayout.DistrictIndex(position);
            if (exact >= 0) return exact;
            int best = 0;
            float distance = float.PositiveInfinity;
            for (int i = 0; i < PortLayout.Districts.Count; i++)
            {
                float candidate = PortLayout.DistanceSquared(position, PortLayout.Districts[i].Center);
                if (candidate < distance) { distance = candidate; best = i; }
            }
            return best;
        }

        void ClearGeneratedWorld()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                DestroyImmediate(transform.GetChild(i).gameObject);
            for (int i = generatedAssets.Count - 1; i >= 0; i--)
                if (generatedAssets[i] != null) DestroyImmediate(generatedAssets[i]);
            generatedAssets.Clear();
            exitZones.Clear();
            Navigation = null;
            Exit = null;
            Exits = Array.Empty<ExtractionZone>();
            if (ambientSaved) RestoreAmbient();
        }

        void RestoreAmbient()
        {
            RenderSettings.ambientMode = previousAmbient.Mode;
            RenderSettings.ambientLight = previousAmbient.Ambient;
            RenderSettings.ambientSkyColor = previousAmbient.Sky;
            RenderSettings.ambientEquatorColor = previousAmbient.Equator;
            RenderSettings.ambientGroundColor = previousAmbient.Ground;
            RenderSettings.fog = previousAmbient.Fog;
            RenderSettings.fogColor = previousAmbient.FogColor;
            RenderSettings.fogMode = previousAmbient.FogMode;
            RenderSettings.fogStartDistance = previousAmbient.FogStart;
            RenderSettings.fogEndDistance = previousAmbient.FogEnd;
            RenderSettings.sun = previousAmbient.Sun;
            ambientSaved = false;
        }

        void OnDestroy()
        {
            if (ambientSaved) RestoreAmbient();
            for (int i = generatedAssets.Count - 1; i >= 0; i--)
                if (generatedAssets[i] != null) Destroy(generatedAssets[i]);
            generatedAssets.Clear();
        }

        readonly struct FixedLoot
        {
            internal readonly float X, Z;
            internal readonly LootKind Kind;
            internal readonly string Title;
            internal FixedLoot(float x, float z, LootKind kind, string title)
            {
                X = x; Z = z; Kind = kind; Title = title;
            }
        }
    }
}
