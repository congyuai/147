using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dustline
{
    internal sealed class PracticeRange : MonoBehaviour
    {
        readonly List<Object> generated = new List<Object>();
        readonly List<Target> targets = new List<Target>();
        readonly Dictionary<Health, Target> byHealth = new Dictionary<Health, Target>();
        public Vector3 Spawn => transform.TransformPoint(0f, 0.045f, -18f);
        public int Shots { get; private set; }
        public int Hits { get; private set; }
        public int Plates { get; private set; }
        internal int DummyCount => targets.Count;
        int hitShots, rays;
        double squaredAngles;
        AmbientMode previousMode;
        Color previousSky, previousEquator, previousGround;
        bool previousFog, lighting;
        Light previousSun;

        internal readonly struct RangeSnapshot
        {
            internal readonly int Shots, HitShots, Rays, Hits, Plates;
            internal readonly float Accuracy, SpreadRmsDegrees;

            internal RangeSnapshot(PracticeRange range)
            {
                Shots = range.Shots; HitShots = range.hitShots; Rays = range.rays; Hits = range.Hits; Plates = range.Plates;
                Accuracy = Shots > 0 ? HitShots / (float)Shots : 0f;
                SpreadRmsDegrees = Rays > 0 ? (float)System.Math.Sqrt(range.squaredAngles / Rays) : 0f;
            }
        }

        internal readonly struct DummySnapshot
        {
            internal readonly int Index, HelmetWear, ArmorWear, HelmetDurability, ArmorDurability, Hits, Knockdowns;
            internal readonly string Name, HelmetId, ArmorId;
            internal readonly float CurrentHealth, MaxHealth, TotalDamage, LastDamage, SpreadRms;
            internal readonly bool Alive;
            internal readonly BodyPart? LastHitPart;

            internal DummySnapshot(int index, Target target)
            {
                Index = index; Name = target.Name;
                HelmetId = target.Helmet?.ItemId; ArmorId = target.Armor?.ItemId;
                HelmetWear = target.Helmet?.Wear ?? 0; ArmorWear = target.Armor?.Wear ?? 0;
                HelmetDurability = target.HelmetRule?.Durability ?? 0; ArmorDurability = target.ArmorRule?.Durability ?? 0;
                CurrentHealth = target.Health.Current; MaxHealth = target.Health.Maximum; Alive = target.Health.Alive;
                Hits = target.Hits; Knockdowns = target.Knockdowns; TotalDamage = target.TotalDamage;
                LastDamage = target.LastDamage; LastHitPart = target.LastPart;
                SpreadRms = target.Spread;
            }
        }

        internal sealed class Target
        {
            internal string Name;
            internal GameObject Plate;
            internal Health Health;
            internal Vector3 Origin;
            internal float Travel, RestoreAt, StartMoving;
            internal ItemDefinition HelmetRule, ArmorRule;
            internal ItemStack Helmet, Armor;
            internal MeshRenderer HelmetModel, ArmorModel;
            internal int Hits, Knockdowns;
            internal float TotalDamage, LastDamage;
            internal BodyPart? LastPart;
            double sumX, sumY, sumSquares;

            internal float Spread => Hits < 2 ? 0f : (float)System.Math.Sqrt(System.Math.Max(0d,
                sumSquares / Hits - (sumX * sumX + sumY * sumY) / ((double)Hits * Hits)));

            internal void Impact(Vector3 point, float damage)
            {
                var local = Plate.transform.InverseTransformPoint(point);
                Hits++;
                TotalDamage += damage;
                LastDamage = damage;
                LastPart = Region(local);
                sumX += local.x; sumY += local.y;
                sumSquares += (double)local.x * local.x + (double)local.y * local.y;
            }

            internal void ClearHits()
            {
                Hits = Knockdowns = 0;
                TotalDamage = LastDamage = 0f;
                LastPart = null;
                sumX = sumY = sumSquares = 0d;
            }
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
            HitBox(0f, .445f, .31f, .30f, PortTone.ConcreteLight);
            HitBox(0f, 0f, .54f, .54f, PortTone.Slate);
            HitBox(0f, -.396f, .43f, .24f, PortTone.Slate);
            for (int side = -1; side <= 1; side += 2)
            {
                HitBox(side * .372f, -.048f, .18f, .48f, PortTone.ConcreteLight);
                HitBox(side * .127f, -.710f, .17f, .36f, PortTone.Slate);
            }
            var axis = Quaternion.Euler(90f, 0f, 0f);
            mesh.Cylinder(new Vector3(0, 0, -.088f), .207f, .025f, PortTone.Paint, axis, 24);
            mesh.Cylinder(new Vector3(0, 0, -.104f), .132f, .020f, PortTone.Terra, axis, 18);
            mesh.Cylinder(new Vector3(0, 0, -.117f), .062f, .015f, PortTone.Yellow, axis, 14);
            mesh.Cylinder(new Vector3(0, .445f, -.087f), .093f, .022f, PortTone.Terra, axis, 14);
            mesh.Box(new Vector3(0, -position.y * .5f, .105f), new Vector3(.085f, position.y, .16f), PortTone.Steel);
            mesh.Box(new Vector3(0, -position.y + .045f, .11f), new Vector3(.56f, .085f, .37f), PortTone.Steel);
            mesh.Bake(node.transform, "Painted dummy", materials.Structure, generated.Add, false);

            var helmet = new PortMesh();
            helmet.Box(new Vector3(0f, .574f, -.055f), new Vector3(.35f, .083f, .12f), PortTone.Slate);
            for (int side = -1; side <= 1; side += 2)
                helmet.Box(new Vector3(side * .153f, .471f, -.056f), new Vector3(.044f, .158f, .12f), PortTone.Slate);
            helmet.Bake(node.transform, "Helmet trim", materials.Dark, generated.Add, false);
            var vest = new PortMesh();
            for (int side = -1; side <= 1; side += 2)
            {
                vest.Box(new Vector3(side * .178f, .194f, -.110f), new Vector3(.14f, .096f, .053f), PortTone.Slate);
                vest.Box(new Vector3(side * .220f, -.068f, -.106f), new Vector3(.078f, .28f, .050f), PortTone.Slate);
            }
            vest.Box(new Vector3(0f, -.217f, -.105f), new Vector3(.43f, .048f, .053f), PortTone.Slate);
            vest.Bake(node.transform, "Vest trim", materials.Dark, generated.Add, false);

            var health = node.AddComponent<Health>();
            health.Initialize(50f, 4);
            health.AimHeight = 0f;
            var target = new Target { Plate = node, Health = health, Origin = position, Travel = travel,
                Name = "假人 " + (targets.Count + 1), StartMoving = Time.time,
                HelmetModel = node.transform.Find("Helmet trim").GetComponent<MeshRenderer>(),
                ArmorModel = node.transform.Find("Vest trim").GetComponent<MeshRenderer>() };
            target.HelmetModel.enabled = target.ArmorModel.enabled = false;
            health.HitPart = point => Region(node.transform.InverseTransformPoint(point));
            health.ProtectPart = (part, damage) => Protect(target, part, damage, 0);
            health.ProtectAmmoPart = (part, damage, penetration) => Protect(target, part, damage, penetration);
            health.Died += _ =>
            {
                Plates++;
                target.Knockdowns++;
                target.RestoreAt = Time.time + 1.4f;
                node.SetActive(false);
            };
            targets.Add(target);
            byHealth.Add(health, target);

            void HitBox(float x, float y, float width, float height, PortTone tone)
            {
                var center = new Vector3(x, y, 0f);
                var size = new Vector3(width, height, .16f);
                mesh.Box(center, size, tone);
                var collider = node.AddComponent<BoxCollider>();
                collider.center = center;
                collider.size = size + new Vector3(0f, 0f, .08f);
            }
        }

        static BodyPart Region(Vector3 local)
        {
            if (local.y > .282f) return BodyPart.Head;
            if (Mathf.Abs(local.x) > .274f) return local.x < 0f ? BodyPart.LeftArm : BodyPart.RightArm;
            if (local.y < -.520f) return local.x < 0f ? BodyPart.LeftLeg : BodyPart.RightLeg;
            return local.y < -.272f ? BodyPart.Abdomen : BodyPart.Chest;
        }

        static float Protect(Target target, BodyPart part, float damage, int penetration) => part == BodyPart.Head
            ? BodyCondition.Protect(part, damage, target.HelmetRule, target.Helmet, penetration)
            : BodyCondition.Protect(part, damage, target.ArmorRule, target.Armor, penetration);

        internal RangeSnapshot GetStats() => new RangeSnapshot(this);

        internal bool TryGetDummy(int index, out DummySnapshot dummy)
        {
            if ((uint)index >= (uint)targets.Count) { dummy = default; return false; }
            dummy = new DummySnapshot(index, targets[index]);
            return true;
        }

        internal bool TrySetDummyGear(int index, EquipmentSlot slot, string itemId, ItemCatalog catalog)
        {
            if ((uint)index >= (uint)targets.Count || (slot != EquipmentSlot.Helmet && slot != EquipmentSlot.Armor)) return false;
            var rule = catalog?.Find(itemId);
            if (!string.IsNullOrEmpty(itemId) && (rule == null || rule.Equipment != slot)) return false;
            var target = targets[index];
            bool helmet = slot == EquipmentSlot.Helmet;
            if ((helmet ? target.HelmetRule : target.ArmorRule) == rule) return true;
            var item = rule != null ? new ItemStack { ItemId = rule.Id, Slot = slot } : null;
            if (helmet) { target.HelmetRule = rule; target.Helmet = item; }
            else { target.ArmorRule = rule; target.Armor = item; }
            var trim = helmet ? target.HelmetModel : target.ArmorModel;
            trim.enabled = rule != null;
            if (rule != null)
            {
                var paint = new MaterialPropertyBlock();
                paint.SetColor("_BaseColor", rule.Tier > 0 ? ItemDefinition.GradeColor(rule.Tier) : rule.Tint);
                trim.SetPropertyBlock(paint);
            }
            return true;
        }

        internal void RecordShot() => Shots++;
        internal void RecordHitShot() => hitShots++;
        internal void RecordRay(float angle) { rays++; squaredAngles += (double)angle * angle; }

        internal bool RecordHit(Health struck, Vector3 point, float damage)
        {
            if (struck == null || !byHealth.TryGetValue(struck, out var dummy)) return false;
            Hits++;
            dummy.Impact(point, damage);
            return true;
        }

        internal bool ResetDummy(int index)
        {
            if ((uint)index >= (uint)targets.Count) return false;
            ResetTarget(targets[index]);
            Physics.SyncTransforms();
            return true;
        }

        internal void ResetPlates()
        {
            Shots = Hits = Plates = rays = hitShots = 0;
            squaredAngles = 0d;
            foreach (var target in targets) ResetTarget(target);
            Physics.SyncTransforms();
        }

        static void ResetTarget(Target target)
        {
            if (target.Helmet != null) target.Helmet.Wear = 0;
            if (target.Armor != null) target.Armor.Wear = 0;
            target.ClearHits();
            Restore(target);
        }

        void FixedUpdate()
        {
            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (target.RestoreAt != 0f && Time.time >= target.RestoreAt) Restore(target);
                if (target.Travel > 0f && target.Plate.activeSelf)
                    target.Plate.transform.localPosition = target.Origin + Vector3.right * (target.Travel
                        * Mathf.Sin((Time.time - target.StartMoving) * .76f) * (i % 2 == 0 ? 1f : -1f));
            }
        }

        static void Restore(Target target)
        {
            // Automatic reappearance preserves wear and grouping; a panel reset clears them explicitly.
            target.Health.Initialize(50f, 4);
            target.RestoreAt = 0f;
            target.StartMoving = Time.time;
            target.Plate.transform.localPosition = target.Origin;
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
            light.cullingMask &= ~ItemPreviewStage.LayerMask;
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
