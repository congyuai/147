using System.Collections;
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
        public Vector3 Spawn => new Vector3(-38, 0.15f, -38);
        public readonly List<Vector3> PatrolPoints = new List<Vector3>();
        readonly List<Material> materials = new List<Material>();
        Material ground, concrete, rust, loot, green, traceMaterial;
        GameSession session;
        System.Random random;

        Material MakeMaterial(string title, Color color)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.name = title;
            material.SetColor("_BaseColor", color);
            materials.Add(material);
            return material;
        }

        public void Build(GameSession owner)
        {
            session = owner;
            random = new System.Random(System.Environment.TickCount);
            ground = MakeMaterial("Dust", new Color(0.31f, 0.29f, 0.24f));
            concrete = MakeMaterial("Concrete", new Color(0.43f, 0.46f, 0.45f));
            rust = MakeMaterial("Oxide", new Color(0.45f, 0.22f, 0.12f));
            loot = MakeMaterial("Supply", new Color(0.65f, 0.52f, 0.18f));
            green = MakeMaterial("Exit", new Color(0.12f, 0.55f, 0.35f));
            DarkMaterial = MakeMaterial("Steel", new Color(0.09f, 0.12f, 0.14f));
            traceMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            traceMaterial.SetColor("_BaseColor", new Color(1, 0.65f, 0.25f));
            materials.Add(traceMaterial);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.58f, 0.62f, 0.68f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.46f, 0.51f, 0.53f);
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 55;
            RenderSettings.fogEndDistance = 150;
            var sun = new GameObject("Afternoon sun", typeof(Light));
            sun.transform.SetParent(transform);
            sun.transform.rotation = Quaternion.Euler(48, -32, 0);
            var light = sun.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            Box("Yard", new Vector3(0, -0.5f, 0), new Vector3(96, 1, 96), ground);
            Box("North fence", new Vector3(0, 2, 48), new Vector3(96, 4, 1), concrete);
            Box("South fence", new Vector3(0, 2, -48), new Vector3(96, 4, 1), concrete);
            Box("West fence", new Vector3(-48, 2, 0), new Vector3(1, 4, 96), concrete);
            Box("East fence", new Vector3(48, 2, 0), new Vector3(1, 4, 96), concrete);
            Warehouse(new Vector3(-20, 0, -13));
            Warehouse(new Vector3(20, 0, 17));
            for (int i = 0; i < 5; i++)
            {
                float x = -32 + i * 15;
                Box("Shipping container", new Vector3(x, 1.5f, i % 2 == 0 ? 29 : -27), new Vector3(8, 3, 4), rust);
                Box("Concrete barrier", new Vector3(x + 3, 0.65f, 2), new Vector3(3, 1.3f, 1), concrete);
            }
            Vector3[] supplies = {
                new Vector3(-33,0,-30), new Vector3(-24,0,-10), new Vector3(-22,0,-17),
                new Vector3(16,0,17), new Vector3(23,0,20), new Vector3(0,0,12),
                new Vector3(34,0,-8), new Vector3(-34,0,12), new Vector3(12,0,-12), new Vector3(31,0,34)
            };
            for (int i = 0; i < supplies.Length; i++)
            {
                var container = CreateLoot(supplies[i], "Supply crate " + (i + 1));
                session.LootTable.Fill(container.Inventory, random);
            }
            var loose = CreateLoot(new Vector3(-35, 0, -35), "Field supplies");
            loose.Inventory.TryAdd("medkit", 1, out _);
            loose.Inventory.TryAdd("ammo", 30, out _);
            var exitPad = Box("North-east extraction", new Vector3(39, 0.025f, 39), new Vector3(8, 0.05f, 8), green);
            Exit = exitPad.AddComponent<ExtractionZone>();
            Exit.Initialize(session);
            for (int i = -1; i <= 1; i += 2)
                Box("Extraction beacon", new Vector3(39 + i * 4, 2.5f, 43), new Vector3(0.4f, 5, 0.4f), green);
            Navigation = new NavigationGrid();
            for (int x = -36; x <= 36; x += 12)
                for (int z = -36; z <= 36; z += 12)
                {
                    var point = new Vector3(x, 0.1f, z);
                    if (!Physics.CheckSphere(point + Vector3.up, 0.8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) PatrolPoints.Add(point);
                }
        }

        void Warehouse(Vector3 center)
        {
            Box("Warehouse back", center + new Vector3(0, 2, 7), new Vector3(18, 4, 0.6f), concrete);
            Box("Warehouse left", center + new Vector3(-9, 2, 0), new Vector3(0.6f, 4, 14), concrete);
            Box("Warehouse right", center + new Vector3(9, 2, 0), new Vector3(0.6f, 4, 14), concrete);
            Box("Door left", center + new Vector3(-6, 2, -7), new Vector3(6, 4, 0.6f), concrete);
            Box("Door right", center + new Vector3(6, 2, -7), new Vector3(6, 4, 0.6f), concrete);
            Box("Roof", center + new Vector3(0, 4.3f, 0), new Vector3(19, 0.6f, 15), DarkMaterial);
            Box("Interior cover", center + new Vector3(0, 0.7f, 1), new Vector3(3, 1.4f, 2), rust);
        }

        public GameObject Box(string title, Vector3 position, Vector3 scale, Material material)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = title;
            box.transform.SetParent(transform);
            box.transform.position = position;
            box.transform.localScale = scale;
            box.GetComponent<Renderer>().sharedMaterial = material;
            return box;
        }

        public LootContainer CreateLoot(Vector3 point, string title)
        {
            var box = Box(title, point + Vector3.up * 0.35f, new Vector3(0.9f, 0.7f, 0.7f), loot);
            var container = box.AddComponent<LootContainer>();
            container.Initialize(title, session.Catalog);
            return container;
        }

        public void SpawnBots()
        {
            var candidates = new List<Vector3>();
            foreach (var point in PatrolPoints) if (Vector3.Distance(point, Spawn) > 28 && Vector3.Distance(point, Exit.transform.position) > 8) candidates.Add(point);
            for (int i = 0; i < 14 && candidates.Count > 0; i++)
            {
                int index = random.Next(candidates.Count);
                var bot = new GameObject("Local combatant " + (i + 1));
                bot.transform.SetParent(transform);
                bot.transform.position = candidates[index];
                candidates.RemoveAt(index);
                bot.AddComponent<BotController>().Initialize(session, i % 2 + 1, i);
            }
        }

        public void Trace(Vector3 start, Vector3 end, Color color) { StartCoroutine(ShowTrace(start, end, color)); }
        IEnumerator ShowTrace(Vector3 start, Vector3 end, Color color)
        {
            var go = new GameObject("Tracer", typeof(LineRenderer));
            go.transform.SetParent(transform);
            var line = go.GetComponent<LineRenderer>();
            line.sharedMaterial = traceMaterial;
            line.positionCount = 2;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
            line.startWidth = line.endWidth = 0.025f;
            line.startColor = line.endColor = color;
            line.shadowCastingMode = ShadowCastingMode.Off;
            yield return new WaitForSeconds(0.045f);
            Destroy(go);
        }
        void OnDestroy() { foreach (var material in materials) if (material != null) Destroy(material); }
    }
}
