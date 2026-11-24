using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dustline
{
    public sealed class WorldBuilder : MonoBehaviour
    {
        public Material DarkMaterial { get; private set; }
        public NavigationGrid Navigation { get; private set; }
        public ExtractionZone Exit { get; private set; }
        public IReadOnlyList<ExtractionZone> Exits { get; private set; } = Array.Empty<ExtractionZone>();
        public Light Sun { get; private set; }
        public Vector3 Spawn => IsQuarry ? QuarryLayout.Spawn : PortLayout.Spawn;
        internal bool IsQuarry { get; private set; }
        internal Texture2D QuarryChart { get; private set; }
        public readonly List<Vector3> PatrolPoints = new List<Vector3>();

        readonly List<UnityEngine.Object> generatedAssets = new List<UnityEngine.Object>();
        readonly List<ExtractionZone> exitZones = new List<ExtractionZone>();
        readonly List<Vector3> navigationSeeds = new List<Vector3>();
        readonly HashSet<Collider> quarryFloors = new HashSet<Collider>();
        readonly List<Vector3> guardPoints = new List<Vector3>();
        readonly List<Vector3> candidates = new List<Vector3>();
        readonly Collider[] placementHits = new Collider[16];
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

        public void Build(GameSession owner) => Build(owner, false);

        internal void Build(GameSession owner, bool quarry)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            ClearGeneratedWorld();
            IsQuarry = quarry;
            session = owner;
            random = new System.Random(0xD05711 + Time.frameCount);
            SaveAmbient();
            ConfigureLighting();

            Texture2D ground = quarry ? QuarryRaster.BakeGround() : null;
            Keep(ground);
            portMaterials = new PortMaterials(Keep, ground);
            DarkMaterial = portMaterials.Dark;
            if (quarry)
            {
                BuildQuarry();
                return;
            }

            var scenery = new PortScenery(transform, portMaterials, Keep, navigationSeeds);
            scenery.Build();
            BuildExits(PortLayout.Exits);
            BuildPortLoot();
            Physics.SyncTransforms();

            Navigation = new NavigationGrid(navigationSeeds, 57f, GetComponentsInChildren<HarborDoor>());
            foreach (var point in Navigation.Points)
            {
                if (PortLayout.InHighValueArea(point)) guardPoints.Add(point);
                else if (PortLayout.CanPostBot(point)) PatrolPoints.Add(point);
            }
            if (PatrolPoints.Count == 0) throw new InvalidOperationException("港区未生成可用巡逻路点。");
        }

        void BuildQuarry()
        {
            new QuarryScenery(transform, portMaterials, Keep, navigationSeeds, quarryFloors).Build();
            BuildExits(QuarryLayout.Exits);
            BuildQuarryLoot();
            Physics.SyncTransforms();
            Navigation = new NavigationGrid(navigationSeeds, 39f, quarryFloors);
            foreach (var point in Navigation.ReachablePoints(Spawn))
                if (QuarryLayout.CanPostBot(point)) PatrolPoints.Add(point);
            foreach (var exit in QuarryLayout.Exits)
                if (Navigation.Path(Spawn, exit.Position).Count == 0)
                    throw new InvalidOperationException("采石场撤离路线不通：" + exit.Name);
            if (PatrolPoints.Count < 14) throw new InvalidOperationException("采石场没有足够的可达巡逻路点。");
            QuarryChart = QuarryRaster.BakeChart();
            Keep(QuarryChart);
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
            RenderSettings.fogStartDistance = 250;
            RenderSettings.fogEndDistance = 1620;

            if (IsQuarry)
            {
                RenderSettings.ambientSkyColor = new Color(.57f, .63f, .69f);
                RenderSettings.ambientEquatorColor = new Color(.39f, .38f, .35f);
                RenderSettings.ambientGroundColor = new Color(.21f, .18f, .16f);
                RenderSettings.fogColor = new Color(.54f, .57f, .60f);
                RenderSettings.fogStartDistance = 295f;
                RenderSettings.fogEndDistance = 1120f;
            }
            var sunObject = new GameObject(IsQuarry ? "Stone Ridge daylight" : "Low angle harbor sun", typeof(Light));
            sunObject.transform.SetParent(transform, false);
            sunObject.transform.rotation = IsQuarry ? Quaternion.Euler(46, -62, 0) : Quaternion.Euler(27, -38, 0);
            Sun = sunObject.GetComponent<Light>();
            Sun.type = LightType.Directional;
            Sun.cullingMask &= ~ItemPreviewStage.LayerMask;
            Sun.color = IsQuarry ? new Color(1f, .92f, .77f) : new Color(1f, .78f, .59f);
            Sun.intensity = 1.15f;
            Sun.shadows = LightShadows.Soft;
            Sun.shadowStrength = 0.78f;
            Sun.shadowBias = 0.045f;
            Sun.shadowNormalBias = 0.35f;
            Sun.renderingLayerMask = 1;
            RenderSettings.sun = Sun;
            if (IsQuarry)
            {
                PointLight("Screening control house lamps", QuarryLayout.HighValueCenter + Vector3.up * 4.4f, new Color(1f, .72f, .46f), 2.5f, 19f);
                PointLight("Quarry workshop lamps", QuarryTerrain.At(64, 242) + Vector3.up * 4.7f, new Color(.81f, .88f, 1f), 2.1f, 22f);
                foreach (var exit in QuarryLayout.Exits)
                    PointLight("Ridge exit signal", exit.Position + Vector3.up * 2.65f, new Color(.55f, 1f, .72f), 1.35f, 12f);
                return;
            }

            PointLight("Customs interior lamps", PortLayout.At(-198, 62) + Vector3.up * 4.8f, new Color(1f, .65f, .35f), 2.2f, 25);
            PointLight("Workshop interior lamps", PortLayout.At(83, -157) + Vector3.up * 5.4f, new Color(1f, .58f, .31f), 2.8f, 28);
            PointLight("Rail canopy lamps", PortLayout.At(-54, 273) + Vector3.up * 5.1f, new Color(.74f, .84f, 1f), 2.5f, 25);
            PointLight("Quay service lamp", PortLayout.HighValueCenter + Vector3.up * 5.1f, new Color(1f, .74f, .45f), 2.1f, 17);
            PointLight("Gatehouse lamp", PortLayout.At(-350, -337) + Vector3.up * 3.3f, new Color(1f, .67f, .34f), 1.8f, 12);
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

        void BuildExits(IReadOnlyList<PortExit> definitions)
        {
            for (int i = 0; i < definitions.Count; i++)
            {
                var definition = definitions[i];
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

        void BuildPortLoot() => BuildFixedLoot(new[] {
                new FixedLoot(-297.6f, -236.6f, LootKind.StorageLocker, "堆场值班柜"),
                new FixedLoot(-261.2f, -220.1f, LootKind.SupplyCrate, "堆场物资箱"),
                new FixedLoot(-231, -164.8f, LootKind.Toolbox, "堆场工具箱"),
                new FixedLoot(-145.6f, -218.1f, LootKind.SupplyCrate, "冷藏货箱"),
                new FixedLoot(-102.5f, -109.8f, LootKind.StorageLocker, "堆场储物柜"),
                new FixedLoot(-287.6f, -237.1f, LootKind.MedicalCase, "堆场医疗箱"),
                new FixedLoot(-184, -111.8f, LootKind.SupplyCrate, "托盘旁物资箱"),
                new FixedLoot(-96.6f, -220.3f, LootKind.Toolbox, "装卸工具箱"),

                new FixedLoot(-232.2f, 77.8f, LootKind.StorageLocker, "海关档案柜"),
                new FixedLoot(-227.5f, 57.2f, LootKind.MedicalCase, "值班医疗箱"),
                new FixedLoot(-209, 83.5f, LootKind.SupplyCrate, "查验物资箱"),
                new FixedLoot(-170.4f, 58.2f, LootKind.Toolbox, "办公室工具箱"),
                new FixedLoot(-287.6f, 99.5f, LootKind.StorageLocker, "边门储物柜"),
                new FixedLoot(-98.1f, 129.2f, LootKind.SupplyCrate, "签发室物资箱"),
                new FixedLoot(-117.8f, 120.1f, LootKind.MedicalCase, "办公区医疗箱"),

                new FixedLoot(34.2f, -175.8f, LootKind.StorageLocker, "维修仓外柜"),
                new FixedLoot(47.2f, -168.5f, LootKind.Toolbox, "一号工位工具箱"),
                new FixedLoot(74.4f, -175.6f, LootKind.SupplyCrate, "维修仓货箱"),
                new FixedLoot(119, -167.7f, LootKind.MedicalCase, "维修仓急救箱"),
                new FixedLoot(147.4f, -222.3f, LootKind.Toolbox, "泵房工具箱"),
                new FixedLoot(164.6f, -211.6f, LootKind.StorageLocker, "泵房备件柜"),
                new FixedLoot(19.5f, -110, LootKind.SupplyCrate, "备件物资箱"),
                new FixedLoot(170.4f, -143.1f, LootKind.Toolbox, "罐旁检修工具箱"),
                new FixedLoot(121.1f, -137.9f, LootKind.MedicalCase, "夜班医疗箱"),

                new FixedLoot(-312, 215, LootKind.StorageLocker, "铁路信号柜"),
                new FixedLoot(-282, 273, LootKind.SupplyCrate, "西侧货台箱"),
                new FixedLoot(-228, 270, LootKind.Toolbox, "轨道工具箱"),
                new FixedLoot(-165, 314, LootKind.MedicalCase, "货台医疗箱"),
                new FixedLoot(-99, 218, LootKind.StorageLocker, "装卸储物柜"),
                new FixedLoot(-30, 310, LootKind.SupplyCrate, "雨棚物资箱"),
                new FixedLoot(45, 313, LootKind.Toolbox, "东侧扳手箱"),
                new FixedLoot(125, 318, LootKind.StorageLocker, "调度柜"),
                new FixedLoot(159, 212, LootKind.MedicalCase, "铁路急救箱"),

                new FixedLoot(250, -25.8f, LootKind.SupplyCrate, "码头物资箱"),
                new FixedLoot(276.1f, 9.4f, LootKind.Toolbox, "缆绳工具箱"),
                new FixedLoot(247.3f, 87.2f, LootKind.StorageLocker, "急件保管柜"),
                new FixedLoot(270.8f, 82.1f, LootKind.MedicalCase, "码头医疗箱"),
                new FixedLoot(277.6f, 153, LootKind.SupplyCrate, "北岬货箱"),
                new FixedLoot(328.4f, 104, LootKind.Toolbox, "吊机工具箱"),
                new FixedLoot(347.7f, 116.3f, LootKind.StorageLocker, "泊位柜"),
                new FixedLoot(347.5f, 188.9f, LootKind.MedicalCase, "值守医疗箱"),
                new FixedLoot(276.1f, -26.5f, LootKind.SupplyCrate, "装船物资箱"),

                new FixedLoot(246.5f, 126.4f, LootKind.StorageLocker, "海事站 · 保管柜"),
                new FixedLoot(259.1f, 125.8f, LootKind.EquipmentCase, "海事站 · 精密设备箱"),
                new FixedLoot(265.5f, 112.4f, LootKind.Toolbox, "海事站 · 校准工具箱"),
                new FixedLoot(242.2f, 112.2f, LootKind.StorageLocker, "海事站 · 岸电备件柜")
            });

        void BuildQuarryLoot() => BuildFixedLoot(new[] {
            new FixedLoot(-288,-247, LootKind.StorageLocker, "调度棚值班柜"),
            new FixedLoot(-274,-250, LootKind.Toolbox, "西坡测量工具箱"),
            new FixedLoot(-269,-231, LootKind.SupplyCrate, "营地补给箱"),
            new FixedLoot(-265,-214, LootKind.MedicalCase, "营地急救箱"),
            new FixedLoot(-303,254, LootKind.Toolbox, "北脊道班备件箱"),
            new FixedLoot(-296,257, LootKind.StorageLocker, "护坡巡检柜"),
            new FixedLoot(53,242, LootKind.Toolbox, "钻头检修工具箱"),
            new FixedLoot(77,247, LootKind.StorageLocker, "钻场工装柜"),
            new FixedLoot(74,238, LootKind.MedicalCase, "钻场医疗箱"),
            new FixedLoot(77,208, LootKind.SupplyCrate, "北脊料台货箱"),
            new FixedLoot(-20,-29, LootKind.Toolbox, "坑底测绘工具箱"),
            new FixedLoot(-10,-30, LootKind.MedicalCase, "勘探棚医疗箱"),
            new FixedLoot(-18,-5, LootKind.SupplyCrate, "采坑巡检供给箱"),
            new FixedLoot(35,-31, LootKind.StorageLocker, "坑底机务柜"),
            new FixedLoot(188,82, LootKind.EquipmentCase, "控制站 · 筛分诊断设备箱"),
            new FixedLoot(216,89, LootKind.EquipmentCase, "控制站 · 精密测量设备箱"),
            new FixedLoot(189,90, LootKind.StorageLocker, "控制站 · 检测样品柜"),
            new FixedLoot(220,77, LootKind.Toolbox, "控制站 · 校准工具箱"),
            new FixedLoot(216,46, LootKind.SupplyCrate, "输送廊补给箱"),
            new FixedLoot(241,47, LootKind.StorageLocker, "破碎机维修柜"),
            new FixedLoot(174,-173, LootKind.Toolbox, "泵房过滤器工具箱"),
            new FixedLoot(192,-173, LootKind.MedicalCase, "泵站医疗箱"),
            new FixedLoot(193,-158, LootKind.StorageLocker, "沉淀池样品柜"),
            new FixedLoot(172,-150, LootKind.SupplyCrate, "泵站供给箱"),
            new FixedLoot(289,-264, LootKind.StorageLocker, "磅房票据柜"),
            new FixedLoot(287,-249, LootKind.Toolbox, "称重校正工具箱")
        });

        void BuildFixedLoot(FixedLoot[] fixedPoints)
        {
            Physics.SyncTransforms();
            var fixedVisuals = new PortMesh();
            int stations = 0;
            for (int i = 0; i < fixedPoints.Length; i++)
            {
                FixedLoot point = fixedPoints[i];
                Vector3 requested = GroundPoint(point.X, point.Z);
                bool high = InHighValueArea(requested);
                if (!TryLootSite(requested, Dimensions(point.Kind), false, out var site)) continue;
                var container = CreateLootInternal(site, point.Title, point.Kind, SearchSeconds(point.Kind), fixedVisuals);
                var table = high ? session.HighValueLootTable : session.LootTable;
                int rolls = high ? table.Rolls : point.Kind == LootKind.MedicalCase || point.Kind == LootKind.Toolbox ? 1 : 2;
                table.FillContainer(container, random, rolls);
                if (high) stations++;
                Physics.SyncTransforms();
            }
            if (stations == 0) throw new InvalidOperationException((IsQuarry ? QuarryLayout.HighValueName : PortLayout.HighValueName) + "没有可搜索的物资箱。");

            var starter = CreateLootInternal(GroundPoint(Spawn.x + 4, Spawn.z + 5), "起点医疗箱",
                LootKind.MedicalCase, 1.1f, fixedVisuals);
            starter.Inventory.TryAdd("medkit", 1, out _);
            starter.Inventory.TryAdd("bandage", 2, out _);
            Physics.SyncTransforms();
            var ammoPoint = GroundPoint(Spawn.x + 6.3f, Spawn.z + 5.7f);
            if (TryLootSite(ammoPoint, Dimensions(LootKind.SupplyCrate), false, out var ammoSite))
                CreateLootInternal(ammoSite, "起点弹药箱", LootKind.SupplyCrate, 1.2f, fixedVisuals).Inventory.TryAdd("ammo", 30, out _);
            fixedVisuals.Bake(transform, "Fixed loot visuals", portMaterials.Structure, Keep, false);
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
            Physics.SyncTransforms();
            if (!TryLootSite(point, Dimensions(LootKind.FieldBag), true, out var site))
                site = GroundPoint(point.x, point.z);
            return CreateLootInternal(site, title, LootKind.FieldBag, 2.2f);
        }

        float GroundHeight(float x, float z) => IsQuarry ? QuarryTerrain.Floor(x, z) : PortTerrain.Floor(x, z);
        Vector3 GroundPoint(float x, float z) => new Vector3(x, GroundHeight(x, z), z);
        bool InHighValueArea(Vector3 point) => IsQuarry ? QuarryLayout.InHighValueArea(point) : PortLayout.InHighValueArea(point);
        bool TryWalk(float x, float z, out float y) => IsQuarry ? QuarryTerrain.TryWalk(x, z, out y) : PortTerrain.TryWalk(x, z, out y);

        bool TryLootSite(Vector3 point, Vector3 size, bool dropped, out Vector3 site)
        {
            bool high = InHighValueArea(point);
            bool onWalkable = TryWalk(point.x, point.z, out float floor);
            if (TryPlace(point.x, point.z, out site)) return true;
            for (int ring = 1; ring <= (dropped ? 5 : 3); ring++)
                for (int i = 0; i < 8; i++)
                {
                    float a = (i + (ring % 2) * .5f) * Mathf.PI / 4, radius = ring * 1.28f;
                    if (TryPlace(point.x + Mathf.Sin(a) * radius, point.z + Mathf.Cos(a) * radius, out site)) return true;
                }
            return false;

            bool TryPlace(float x, float z, out Vector3 position)
            {
                position = new Vector3(x, 0, z);
                if (!TryWalk(x, z, out float y) || (!dropped && InHighValueArea(position) != high)) return false;
                position.y = y;
                if (dropped && onWalkable && Physics.Linecast(new Vector3(point.x, floor + .78f, point.z),
                    position + Vector3.up * .78f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;
                float spread = Mathf.Max(size.x, size.z) * .5f;
                if (Mathf.Abs(y - GroundHeight(x - spread, z)) > .22f || Mathf.Abs(y - GroundHeight(x + spread, z)) > .22f
                    || Mathf.Abs(y - GroundHeight(x, z + spread)) > .22f || Mathf.Abs(y - GroundHeight(x, z - spread)) > .22f) return false;
                var center = position + Vector3.up * ((size.y + .13f) * .5f);
                var extents = size * .5f + new Vector3(.17f, -.065f, .17f);
                int count = Physics.OverlapBoxNonAlloc(center, extents, placementHits, Quaternion.identity,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
                for (int j = 0; j < count; j++)
                {
                    var hit = placementHits[j];
                    if (IsQuarry ? quarryFloors.Contains(hit)
                        : hit.TryGetComponent<FootstepSurface>(out var surface) && surface.IsNavigationFloor) continue;
                    return false;
                }
                return count < placementHits.Length;
            }
        }

        LootContainer CreateLootInternal(Vector3 point, string title, LootKind kind, float searchSeconds, PortMesh visualBatch = null)
        {
            var root = new GameObject("Loot " + title);
            root.transform.SetParent(transform, false);
            root.transform.position = point;
            Vector3 dimensions = Dimensions(kind);
            // The solid shell also serves as the interaction target, so it never hides a separate loot trigger.
            var shell = root.AddComponent<BoxCollider>();
            shell.isTrigger = kind == LootKind.FieldBag;
            shell.center = Vector3.up * dimensions.y * .5f;
            shell.size = dimensions;
            if (!shell.isTrigger) root.AddComponent<FootstepSurface>().Kind = SurfaceKind.Metal;
            root.isStatic = kind != LootKind.FieldBag;
            var container = root.AddComponent<LootContainer>();
            container.Initialize(title, session.Catalog, searchSeconds, kind);
            BuildLootVisual(root.transform, kind, dimensions, visualBatch);
            return container;
        }

        static Vector3 Dimensions(LootKind kind)
        {
            switch (kind)
            {
                case LootKind.StorageLocker: return new Vector3(.96f, 1.7f, .67f);
                case LootKind.Toolbox: return new Vector3(1.18f, .63f, .72f);
                case LootKind.MedicalCase: return new Vector3(.96f, .67f, .76f);
                case LootKind.FieldBag: return new Vector3(.79f, .30f, .52f);
                default: return new Vector3(1.03f, .84f, .86f);
            }
        }

        void BuildLootVisual(Transform parent, LootKind kind, Vector3 dimensions, PortMesh visualBatch)
        {
            var mesh = visualBatch ?? new PortMesh();
            Vector3 origin = visualBatch == null ? Vector3.zero : transform.InverseTransformPoint(parent.position);
            void Box(Vector3 center, Vector3 size, PortTone tone) => mesh.Box(origin + center, size, tone);
            switch (kind)
            {
                case LootKind.FieldBag:
                    Box(new Vector3(0, .115f, 0), new Vector3(.74f, .22f, .5f), PortTone.Canvas);
                    Box(new Vector3(0, .23f, 0), new Vector3(.62f, .08f, .42f), PortTone.Canvas);
                    Box(new Vector3(0, .277f, .045f), new Vector3(.56f, .028f, .072f), PortTone.Slate);
                    for (int i = -1; i <= 1; i += 2)
                        Box(new Vector3(i * .23f, .14f, -.253f), new Vector3(.066f, .23f, .029f), PortTone.Steel);
                    break;
                case LootKind.StorageLocker:
                    Box(Vector3.up * .807f, new Vector3(dimensions.x, 1.61f, dimensions.z), PortTone.Slate);
                    Box(Vector3.up * 1.65f, new Vector3(.96f, .10f, .67f), PortTone.Steel);
                    for (int i = 0; i < 3; i++)
                        Box(new Vector3(-.20f, 1.42f - i * .11f, -.342f), new Vector3(.38f, .037f, .018f), PortTone.Steel);
                    Box(new Vector3(0.28f, 0.86f, -0.34f), new Vector3(0.23f, 0.1f, 0.08f), PortTone.Ochre);
                    break;
                case LootKind.Toolbox:
                    Box(Vector3.up * .273f, new Vector3(1.12f, .546f, .68f), PortTone.Rust);
                    Box(new Vector3(0, .574f, 0), new Vector3(1.18f, .112f, .72f), PortTone.Steel);
                    Box(new Vector3(0, .661f, 0), new Vector3(.43f, .069f, .075f), PortTone.Rust);
                    for (int i = -1; i <= 1; i += 2)
                        Box(new Vector3(i * .29f, .53f, -.368f), new Vector3(.13f, .095f, .038f), PortTone.Zinc);
                    break;
                case LootKind.MedicalCase:
                    Box(Vector3.up * .30f, new Vector3(.94f, .596f, .733f), PortTone.Red);
                    Box(new Vector3(0, .623f, 0), new Vector3(.96f, .094f, .76f), PortTone.Steel);
                    Box(new Vector3(0, .308f, -.371f), new Vector3(.10f, .28f, .016f), PortTone.Paint);
                    Box(new Vector3(-.095f, .308f, -.371f), new Vector3(.09f, .096f, .016f), PortTone.Paint);
                    Box(new Vector3(.095f, .308f, -.371f), new Vector3(.09f, .096f, .016f), PortTone.Paint);
                    break;
                default:
                    Box(Vector3.up * .365f, new Vector3(.98f, .73f, .806f), PortTone.Ochre);
                    Box(new Vector3(0, .788f, 0), new Vector3(1.03f, .104f, .86f), PortTone.Steel);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Box(new Vector3(side * .33f, .41f, 0), new Vector3(.087f, .68f, .84f), PortTone.Steel);
                        Box(new Vector3(side * .33f, .69f, -.429f), new Vector3(.13f, .11f, .05f), PortTone.Zinc);
                    }
                    Box(new Vector3(0, .49f, -.412f), new Vector3(.34f, .16f, .025f), PortTone.Canvas);
                    break;
            }
            if (visualBatch == null) mesh.Bake(parent, "Visual", portMaterials.Structure, Keep, false);
        }

        public void SpawnBots()
        {
            if (IsQuarry)
            {
                SpawnQuarryBots();
                return;
            }
            var taken = new List<Vector3>(14);
            for (int district = 0; district < PortLayout.Districts.Count; district++)
                for (int slot = 0; slot < 2; slot++)
                {
                    candidates.Clear();
                    foreach (var p in PortLayout.BotPosts)
                        if (PortLayout.DistrictIndex(p) == district && PortLayout.CanPostBot(p) && !NearAny(p, taken, 21) && Navigation.CanStandAt(p))
                            candidates.Add(p);
                    if (candidates.Count == 0)
                        foreach (var p in PatrolPoints)
                            if (NearestDistrict(p) == district && !NearAny(p, taken, 21)) candidates.Add(p);
                    if (candidates.Count == 0)
                        foreach (var p in PatrolPoints)
                            if (!NearAny(p, taken, 12)) candidates.Add(p);
                    Vector3 post = ChoosePost();
                    Register(post, (district + slot) % 2 + 1, BotRank.Regular);
                }

            var boss = PortLayout.HighValueCenter;
            if (!Navigation.CanStandAt(boss)) boss = PickGuardPoint(boss, random);
            Register(boss, 3, BotRank.Boss);
            for (int i = 0; i < 3; i++)
            {
                candidates.Clear();
                foreach (var p in PortLayout.GuardPosts)
                    if (!NearAny(p, taken, 11) && Navigation.CanStandAt(p)) candidates.Add(p);
                if (candidates.Count == 0)
                    foreach (var p in guardPoints)
                        if (!NearAny(p, taken, 7) && Navigation.CanStandAt(p)) candidates.Add(p);
                Register(ChoosePost(), 3, BotRank.Elite);
            }

            Vector3 ChoosePost()
            {
                if (candidates.Count == 0) throw new InvalidOperationException("港区没有足够的安全出生点。");
                return candidates[random.Next(candidates.Count)];
            }
            void Register(Vector3 point, int patrolGroup, BotRank rank)
            {
                taken.Add(point);
                AddBot(point, patrolGroup, taken.Count - 1, rank);
            }
        }

        void SpawnQuarryBots()
        {
            const int count = 14;
            var taken = new List<Vector3>(count);
            int stations = QuarryLayout.Districts.Count;
            for (int slot = 0; slot < count; slot++)
            {
                int district = slot % stations;
                candidates.Clear();
                foreach (var p in PatrolPoints)
                    if (QuarryLayout.DistrictIndex(p) == district && !NearAny(p, taken, 28f) && Navigation.CanStandAt(p))
                        candidates.Add(p);
                if (candidates.Count == 0)
                    foreach (var p in PatrolPoints)
                        if (!NearAny(p, taken, 18f) && Navigation.CanStandAt(p)) candidates.Add(p);
                if (candidates.Count == 0) throw new InvalidOperationException("采石场没有足够的安全出生点。");
                var point = candidates[random.Next(candidates.Count)];
                taken.Add(point);
                AddBot(point, (district + slot / stations) % 2 + 1, slot, BotRank.Regular);
            }
        }

        void AddBot(Vector3 point, int patrolGroup, int index, BotRank rank)
        {
            var bot = new GameObject("Local combatant " + (index + 1) + " " + rank);
            bot.transform.SetParent(transform, false);
            bot.transform.position = point + Vector3.up * .035f;
            // Preserve each post's patrol random stream independently of the shared faction.
            int patrolSeed = unchecked((index + 1) * 486187739 ^ patrolGroup * 16777619);
            bot.AddComponent<BotController>().Initialize(session, index, rank, patrolSeed);
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
            if (IsQuarry)
            {
                if (QuarryLayout.InHighValueArea(position)) return QuarryLayout.HighValueName;
                int quarryDistrict = QuarryLayout.DistrictIndex(position);
                return quarryDistrict < 0 ? "采石运输道" : QuarryLayout.Districts[quarryDistrict].Name;
            }
            int index = PortLayout.DistrictIndex(position);
            if (PortLayout.InHighValueArea(position)) return PortLayout.HighValueName;
            return index < 0 ? "港区道路" : PortLayout.Districts[index].Name;
        }

        public Vector3 PickPatrolPoint(Vector3 position, System.Random source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (IsQuarry)
            {
                candidates.Clear();
                foreach (var point in PatrolPoints)
                {
                    float sq = NavigationGrid.PlanarDistanceSquared(point, position);
                    if (sq >= 6 * 6 && sq <= 104 * 104) candidates.Add(point);
                }
                if (candidates.Count == 0) candidates.AddRange(PatrolPoints);
                return PickReachable(position, source);
            }
            candidates.Clear();
            int current = NearestDistrict(position);
            foreach (var point in PatrolPoints)
            {
                float sq = PortLayout.DistanceSquared(point, position);
                if (sq < 4 * 4 || sq > 82 * 82) continue;
                if (PortLayout.Neighboring(current, NearestDistrict(point))) candidates.Add(point);
            }
            if (candidates.Count == 0)
                foreach (var point in PatrolPoints)
                    if (PortLayout.Neighboring(current, NearestDistrict(point))) candidates.Add(point);
            return PickReachable(position, source);
        }

        public Vector3 PickGuardPoint(Vector3 position, System.Random rng)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            candidates.Clear();
            foreach (var point in guardPoints)
            {
                float sq = PortLayout.DistanceSquared(point, position);
                if (sq > 4 * 4 && sq < 55 * 55) candidates.Add(point);
            }
            if (candidates.Count == 0) candidates.AddRange(guardPoints);
            return PickReachable(position, rng);
        }

        Vector3 PickReachable(Vector3 position, System.Random source)
        {
            if (candidates.Count == 0) return position;
            int start = source.Next(candidates.Count);
            // Prefer a direct local leg; the retained alternate is still planned through Navigation.Path.
            for (int i = 0; i < Mathf.Min(7, candidates.Count); i++)
            {
                var point = candidates[(start + i) % candidates.Count];
                if (Navigation.CanTraverse(position, point)) return point;
            }
            return candidates[start];
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
            navigationSeeds.Clear();
            guardPoints.Clear();
            PatrolPoints.Clear();
            quarryFloors.Clear();
            candidates.Clear();
            QuarryChart = null;
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
