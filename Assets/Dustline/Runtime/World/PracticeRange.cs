using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dustline
{
    internal sealed class PracticeRange : MonoBehaviour
    {
        readonly List<Object> generated = new List<Object>();
        readonly List<Target> targets = new List<Target>();
        public Vector3 Spawn => transform.TransformPoint(0f, 0.045f, -18f);
        public int Shots { get; private set; }
        public int Hits { get; private set; }
        public int Plates { get; private set; }
        AmbientMode previousMode;
        Color previousSky, previousEquator, previousGround;
        bool previousFog, lighting;
        Light previousSun;

        sealed class Target
        {
            internal GameObject Plate;
            internal Health Health;
            internal Vector3 Origin;
            internal float Travel, RestoreAt;
        }

        internal void Build()
        {
            var materials = new PortMaterials(generated.Add);
            var deck = new PortChunk(transform, "Practice decks", materials, generated.Add);
            var floor = new Vector3(0, -0.25f, 8);
            var floorSize = new Vector3(42, 0.5f, 64);
            deck.Structures.Box(floor, floorSize, PortTone.Concrete);
            deck.Surface(floor, floorSize, SurfaceKind.Concrete);
            deck.Block(new Vector3(0, 2.5f, 40), new Vector3(42, 5, 1.2f), PortTone.Slate);
            deck.Block(new Vector3(0, 1.8f, -24), new Vector3(42, 3.6f, .5f), PortTone.Concrete);
            for (int sign = -1; sign <= 1; sign += 2)
            {
                deck.Block(new Vector3(sign * 21, 1.8f, 8), new Vector3(.55f, 3.6f, 64), PortTone.Concrete);
                deck.Deck.Floor(sign * 7 - .1f, -21, sign * 7 + .1f, 37, .01f, PortTone.Yellow);
                deck.Block(new Vector3(sign * 11, .65f, -2), new Vector3(3.7f, 1.3f, 1.15f), PortTone.Rust);
                deck.Block(new Vector3(sign * 4, .46f, -8.1f), new Vector3(2.2f, .92f, .72f), PortTone.ConcreteLight);
            }
            for (int i = 0; i < 5; i++)
                deck.Deck.Floor(-18f, -15f + i * 10f, 18f, -14.8f + i * 10f, .011f, PortTone.Paint);
            deck.Bake();
            AddTarget(new Vector3(-11f, 1.12f, 5f), 0f, materials);
            AddTarget(new Vector3(-5f, 1.27f, -6f), 0f, materials);
            AddTarget(new Vector3(0f, 1.45f, 13f), 0f, materials);
            AddTarget(new Vector3(10f, 1.43f, 23f), 3.7f, materials);
            AddTarget(new Vector3(-10f, 1.65f, 32f), 4.1f, materials);
            AddTarget(new Vector3(3f, 1.25f, 34f), 0f, materials);
            SetupLight();
            Physics.SyncTransforms();
        }

        void AddTarget(Vector3 position, float travel, PortMaterials materials)
        {
            var node = PortMesh.Child(transform, "Practice plate");
            node.transform.localPosition = position;
            var mesh = new PortMesh();
            var axis = Quaternion.Euler(90f, 0f, 0f);
            mesh.Cylinder(Vector3.zero, .43f, .11f, PortTone.Slate, axis, 24);
            mesh.Cylinder(new Vector3(0, 0, -.065f), .34f, .028f, PortTone.Paint, axis, 24);
            mesh.Cylinder(new Vector3(0, 0, -.085f), .22f, .022f, PortTone.Terra, axis, 18);
            mesh.Cylinder(new Vector3(0, 0, -.102f), .103f, .02f, PortTone.Yellow, axis, 14);
            mesh.Box(new Vector3(0, -position.y * .5f, .09f), new Vector3(.09f, position.y, .16f), PortTone.Steel);
            mesh.Bake(node.transform, "Painted target", materials.Structure, generated.Add, false);
            var shell = node.AddComponent<BoxCollider>();
            shell.size = new Vector3(.83f, .83f, .21f);
            shell.center = new Vector3(0, 0, -.022f);
            var health = node.AddComponent<Health>();
            health.Initialize(50f, 4);
            var target = new Target { Plate = node, Health = health, Origin = position, Travel = travel };
            health.Damaged += (direction, lost) => Hits++;
            health.Died += _ =>
            {
                Plates++;
                target.RestoreAt = Time.time + 1.4f;
                node.SetActive(false);
            };
            targets.Add(target);
        }

        internal void RecordShot() => Shots++;

        internal void ResetPlates()
        {
            Shots = Hits = Plates = 0;
            foreach (var target in targets)
            {
                target.Plate.transform.localPosition = target.Origin;
                Restore(target);
            }
            Physics.SyncTransforms();
        }

        void FixedUpdate()
        {
            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (target.RestoreAt != 0f && Time.time >= target.RestoreAt) Restore(target);
                if (target.Travel > 0f && target.Plate.activeSelf)
                    target.Plate.transform.localPosition = target.Origin + Vector3.right * (target.Travel * Mathf.Sin(Time.time * .76f + i));
            }
        }

        static void Restore(Target target)
        {
            target.Health.Initialize(50f, 4);
            target.RestoreAt = 0f;
            target.Plate.SetActive(true);
        }

        void SetupLight()
        {
            previousMode = RenderSettings.ambientMode;
            previousSky = RenderSettings.ambientSkyColor;
            previousEquator = RenderSettings.ambientEquatorColor;
            previousGround = RenderSettings.ambientGroundColor;
            previousFog = RenderSettings.fog;
            previousSun = RenderSettings.sun;
            lighting = true;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.44f, .54f, .62f);
            RenderSettings.ambientEquatorColor = new Color(.32f, .38f, .41f);
            RenderSettings.ambientGroundColor = new Color(.20f, .20f, .19f);
            RenderSettings.fog = false;
            var key = PortMesh.Child(transform, "Practice lighting");
            key.transform.localRotation = Quaternion.Euler(43f, -36f, 0f);
            var light = key.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, .92f, .82f);
            light.intensity = 1.22f;
            light.shadows = LightShadows.Soft;
            RenderSettings.sun = light;
        }

        void OnDisable()
        {
            if (!lighting) return;
            lighting = false;
            RenderSettings.ambientMode = previousMode;
            RenderSettings.ambientSkyColor = previousSky;
            RenderSettings.ambientEquatorColor = previousEquator;
            RenderSettings.ambientGroundColor = previousGround;
            RenderSettings.fog = previousFog;
            RenderSettings.sun = previousSun;
        }

        void OnDestroy()
        {
            foreach (var asset in generated) Destroy(asset);
        }
    }
}
