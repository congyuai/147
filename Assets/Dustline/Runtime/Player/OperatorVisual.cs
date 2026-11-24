using UnityEngine;
using UnityEngine.Rendering;

namespace Dustline
{
    public enum OperatorStyle { Male, Female }

    public sealed class OperatorVisual : MonoBehaviour
    {
        internal enum Bone
        {
            Hips, Chest, Head,
            RightUpper, RightLower, RightHand, LeftUpper, LeftLower, LeftHand,
            RightThigh, RightShin, RightBoot, LeftThigh, LeftShin, LeftBoot, Count
        }

        const float Tau = 2f * Mathf.PI;
        const float PalmOffset = 0.044f;
        static readonly Vector3 CarryPosition = new Vector3(0.106f, 0.116f, 0.160f);
        static readonly Vector3 RightRestGrip = new Vector3(0.060f, -0.105f, -0.045f);
        static readonly Vector3 LeftRestGrip = new Vector3(0.037f, -0.058f, 0.276f);
        static readonly Quaternion RightGlove = Quaternion.Euler(-23f, -7f, -11f);
        static readonly Quaternion LeftGlove = Quaternion.Euler(-70f, 15f, 34f);

        public Transform WeaponMount { get; private set; }
        internal OperatorStyle Style => style;

        Transform[] bones;
        SkinnedMeshRenderer skin;
        WeaponVisual carriedWeapon;
        CharacterController motor;
        OperatorStyle style;
        int team;
        bool artBound, externalGrip, fallen, basePoseCaptured;
        readonly Transform[] gear = new Transform[4];
        readonly string[] gearIds = new string[4];
        float speed, run, duck, pronePose, cycle, breath;
        Quaternion baseLocalRotation;
        Vector3 baseLocalPosition;
        Vector3 restHips;
        float thigh, shin, upper, lower, ankleHeight;

        public static OperatorVisual Create(Transform parent, OperatorStyle style, int team = 0, int layer = 0)
        {
            layer = Mathf.Clamp(layer, 0, 31);
            var root = new GameObject(style == OperatorStyle.Female ? "Operator | Female" : "Operator | Male") { layer = layer };
            root.transform.SetParent(parent, false);
            var visual = root.AddComponent<OperatorVisual>();
            visual.Build(style == OperatorStyle.Female ? style : OperatorStyle.Male, Mathf.Clamp(team, 0, 2), layer);
            return visual;
        }

        void Build(OperatorStyle shape, int palette, int layer)
        {
            style = shape;
            team = palette;
            motor = GetComponentInParent<CharacterController>();
            bones = new Transform[(int)Bone.Count];
            for (int i = 0; i < bones.Length; i++)
            {
                var id = (Bone)i;
                Bone parent = ParentOf(id);
                var joint = new GameObject(id.ToString()) { layer = layer };
                Transform t = joint.transform;
                t.SetParent(parent == Bone.Count ? transform : Joint(parent), false);
                t.localPosition = Rest(id, style) - (parent == Bone.Count ? Vector3.zero : Rest(parent, style));
                bones[i] = t;
            }
            WeaponMount = new GameObject("Weapon mount") { layer = layer }.transform;
            WeaponMount.SetParent(Joint(Bone.Chest), false);

            restHips = Rest(Bone.Hips, style);
            ankleHeight = Rest(Bone.RightBoot, style).y;
            thigh = Joint(Bone.RightShin).localPosition.magnitude;
            shin = Joint(Bone.RightBoot).localPosition.magnitude;
            upper = Joint(Bone.RightLower).localPosition.magnitude;
            lower = Joint(Bone.RightHand).localPosition.magnitude;

            skin = gameObject.AddComponent<SkinnedMeshRenderer>();
            skin.bones = bones;
            skin.rootBone = transform;
            skin.quality = SkinQuality.Bone2;
            skin.skinnedMotionVectors = false;
            skin.updateWhenOffscreen = false;
            if (isActiveAndEnabled) BindArt();
            ApplyMotion();
        }

        void OnEnable()
        {
            if (skin != null && !artBound) BindArt();
        }

        void BindArt()
        {
            OperatorMesh.Acquire(style, team, out Mesh mesh, out Material material);
            artBound = true;
            skin.sharedMaterial = material;
            skin.sharedMesh = mesh;
            UpdateBounds();
        }

        void UpdateBounds()
        {
            skin.localBounds = fallen
                ? new Bounds(new Vector3(0f, 0.25f, -0.87f), new Vector3(1.75f, 0.90f, 2.10f))
                : new Bounds(new Vector3(0f, 0.92f, 0.025f), new Vector3(1.75f, 2.24f, 1.70f));
        }

        void OnDestroy()
        {
            if (artBound) OperatorMesh.Release(style, team);
        }

        public void SetMotion(float speed, bool sprinting, bool crouched, float delta, bool walkNow = false)
        {
            if (fallen || bones == null || !Finite(delta) || delta <= 0f) return;
            CaptureBasePose();
            float dt = Mathf.Min(delta, 0.10f);
            if (motor != null && motor.enabled)
            {
                Vector3 velocity = motor.velocity;
                speed = new Vector2(velocity.x, velocity.z).magnitude;
            }
            float target = Finite(speed) ? Mathf.Clamp(speed, 0f, 8f) : 0f;
            this.speed = Mathf.Lerp(this.speed, target, 1f - Mathf.Exp(-11f * dt));
            duck = Mathf.Lerp(duck, crouched ? 1f : 0f, 1f - Mathf.Exp(-10f * dt));
            float sprint = sprinting && !crouched ? Mathf.SmoothStep(0f, 1f, (target - 0.7f) / 2.8f) : 0f;
            run = walkNow ? 0f : Mathf.Lerp(run, sprint, 1f - Mathf.Exp(-9f * dt));
            cycle = Mathf.Repeat(cycle + (this.speed / Stride()) * dt, 1f);
            breath = Mathf.Repeat(breath + dt * 1.77f, Tau);
            ApplyMotion();
        }

        float Stride()
        {
            float walk = Mathf.Lerp(0.75f, 1.52f, speed / 3.8f);
            float sprint = Mathf.Lerp(1.58f, 2.27f, (speed - 3.3f) / 3.2f);
            return Mathf.Lerp(Mathf.Lerp(walk, sprint, run), Mathf.Lerp(0.65f, 1.15f, speed / 2.2f), duck);
        }

        public void SetProne(bool prone, float delta)
        {
            if (fallen || bones == null || !Finite(delta) || delta <= 0f) return;
            float target = prone ? 1f : 0f;
            if (pronePose == target) return;
            CaptureBasePose();
            pronePose = Mathf.Lerp(pronePose, target, 1f - Mathf.Exp(-8f * Mathf.Min(delta, 0.10f)));
            if (Mathf.Abs(pronePose - target) < 0.001f) pronePose = target;
            ApplyMotion();
        }

        void CaptureBasePose()
        {
            if (basePoseCaptured) return;
            baseLocalRotation = transform.localRotation;
            baseLocalPosition = transform.localPosition;
            basePoseCaptured = true;
        }

        void ApplyMotion()
        {
            if (basePoseCaptured)
            {
                transform.localRotation = baseLocalRotation * Quaternion.Euler(90f * pronePose, 0f, 0f);
                transform.localPosition = baseLocalPosition + baseLocalRotation * (Vector3.up * (OperatorMesh.BackDepth * pronePose));
            }
            float gait = Mathf.SmoothStep(0f, 1f, speed / 0.95f);
            float bend = duck * (1f - pronePose);
            float stance = Mathf.Lerp(Mathf.Lerp(0.61f, 0.43f, run), 0.66f, bend);
            float span = Stride() * stance * gait * (1f - 0.68f * pronePose);
            float lift = Mathf.Lerp(Mathf.Lerp(0.077f, 0.154f, run), 0.057f, bend) * gait * (1f - 0.61f * pronePose);
            Vector3 r = FootTarget(1f, cycle, stance, span, lift, out float rPitch);
            Vector3 l = FootTarget(-1f, Mathf.Repeat(cycle + 0.5f, 1f), stance, span, lift, out float lPitch);
            float wave = Mathf.Sin(Tau * cycle) * gait;
            float rise = 0.5f - 0.5f * Mathf.Cos(2f * Tau * cycle);
            float breathing = Mathf.Sin(breath);
            float hipZ = -0.13f * bend + 0.033f * run;
            float hipY = restHips.y - Mathf.Lerp(0.016f, 0.036f, gait) - 0.006f * run - 0.446f * bend;
            hipY += (rise - 0.45f) * gait * Mathf.Lerp(0.035f, 0.066f, run) + 0.003f * breathing;
            hipY = SoftMin(SoftMin(hipY, HipLimit(r, hipZ)), HipLimit(l, hipZ));

            Transform hips = Joint(Bone.Hips);
            hips.localPosition = new Vector3(0.021f * wave, hipY, hipZ);
            hips.localRotation = Quaternion.Euler(3f * bend, 3.8f * wave, -2.4f * wave);
            Joint(Bone.Chest).localRotation = Quaternion.Euler(0.6f * breathing + 9f * run + 21f * bend - 9f * pronePose,
                -6.5f * wave, 2.9f * wave);
            Joint(Bone.Head).localRotation = Quaternion.Euler(-7f * run - 18f * bend - 31f * pronePose, 1.6f * wave, -1.2f * wave);

            PlaceLeg(Bone.RightThigh, Bone.RightShin, Bone.RightBoot, 1f, r, rPitch);
            PlaceLeg(Bone.LeftThigh, Bone.LeftShin, Bone.LeftBoot, -1f, l, lPitch);

            WeaponMount.localPosition = CarryPosition + new Vector3(0.008f * wave + 0.010f * run,
                0.003f * breathing - 0.033f * run, 0.023f * run + 0.017f * bend);
            WeaponMount.localRotation = Quaternion.Euler(1.1f * wave + 12f * run - 17f * bend - 14f * pronePose, -11f - 2f * wave - 10f * run,
                1.9f * wave - 11f * run);
            if (!externalGrip) ApplyArms(WeaponMount.TransformPoint(RightRestGrip), WeaponMount.TransformPoint(LeftRestGrip));
        }

        float HipLimit(Vector3 ankle, float hipZ)
        {
            float reach = thigh + shin - 0.011f;
            float dz = ankle.z - hipZ;
            return ankle.y + Mathf.Sqrt(Mathf.Max(0f, reach * reach - dz * dz - 0.005f));
        }

        static float SoftMin(float a, float b)
        {
            const float blend = 0.024f;
            float h = Mathf.Max(0f, blend - Mathf.Abs(a - b)) / blend;
            return Mathf.Min(a, b) - 0.25f * blend * h * h;
        }

        Vector3 FootTarget(float side, float phase, float stance, float span, float lift, out float pitch)
        {
            float x = Rest(Bone.RightBoot, style).x + 0.018f * duck;
            float z = 0.022f + side * 0.031f * duck;
            pitch = 0f;
            if (phase < stance)
                return new Vector3(side * x, ankleHeight, z + span * (0.5f - phase / stance));

            float t = (phase - stance) / (1f - stance);
            float smooth = t * t * (3f - 2f * t);
            // Match stance velocity at both ends of swing, so the sole does not snap at contact.
            float tangent = -(1f - stance) / stance * (2f * t - 1f) * t * (t - 1f);
            float arc = Mathf.Sin(Mathf.PI * t);
            arc *= arc;
            pitch = -145f * lift * arc * Mathf.Sin(Tau * t);
            return new Vector3(side * (x + 0.13f * lift * arc), ankleHeight + lift * arc, z + span * (smooth - 0.5f + tangent));
        }

        void PlaceLeg(Bone top, Bone mid, Bone foot, float side, Vector3 ankle, float pitch)
        {
            Transform hip = Joint(top);
            Vector3 origin = transform.InverseTransformPoint(hip.position);
            Solve(origin, ankle, new Vector3(side * (0.13f + 0.15f * duck), 0f, 1f), thigh, shin,
                out Vector3 knee, out Vector3 end);
            Quaternion a = LegAxis(knee - origin);
            Quaternion b = LegAxis(end - knee);
            hip.rotation = transform.rotation * a;
            Joint(mid).localRotation = Quaternion.Inverse(a) * b;
            Joint(foot).localRotation = Quaternion.Inverse(b) * Quaternion.Euler(pitch, side * (4f + 6f * duck), 0f);
        }

        internal BodyPart HitRegion(Vector3 point)
        {
            Vector3 head = Joint(Bone.Head).TransformPoint(Vector3.up * .12f);
            Vector3 chest = Joint(Bone.Chest).TransformPoint(Vector3.up * .095f);
            Vector3 hips = Joint(Bone.Hips).TransformPoint(Vector3.up * .09f);
            float best = float.PositiveInfinity;
            BodyPart part = BodyPart.Chest;
            Score(BodyPart.Head, head, head, .135f);
            Score(BodyPart.Chest, chest, chest + (hips - chest) * .42f, .225f);
            Score(BodyPart.Abdomen, hips, chest + (hips - chest) * .46f, .18f);
            Chain(BodyPart.LeftArm, Bone.LeftUpper, Bone.LeftLower, Bone.LeftHand, .087f);
            Chain(BodyPart.RightArm, Bone.RightUpper, Bone.RightLower, Bone.RightHand, .087f);
            Chain(BodyPart.LeftLeg, Bone.LeftThigh, Bone.LeftShin, Bone.LeftBoot, .11f);
            Chain(BodyPart.RightLeg, Bone.RightThigh, Bone.RightShin, Bone.RightBoot, .11f);
            return part;

            void Chain(BodyPart id, Bone a, Bone b, Bone c, float radius)
            {
                Score(id, Joint(a).position, Joint(b).position, radius);
                Score(id, Joint(b).position, Joint(c).position, radius * .83f);
            }
            void Score(BodyPart id, Vector3 a, Vector3 b, float radius)
            {
                Vector3 d = b - a;
                float t = Mathf.Clamp01(Vector3.Dot(point - a, d) / Mathf.Max(d.sqrMagnitude, .00001f));
                float distance = (point - a - d * t).magnitude - radius;
                if (distance < best) { best = distance; part = id; }
            }
        }

        internal void SyncGear(GridInventory bag)
        {
            Set(0, EquipmentSlot.Helmet, Bone.Head, new Vector3(0,.126f,.005f), .90f);
            Set(1, EquipmentSlot.Armor, Bone.Chest, new Vector3(0,.032f,.113f), .96f);
            Set(2, EquipmentSlot.Rig, Bone.Chest, new Vector3(0,-.158f,.160f), .83f);
            Set(3, EquipmentSlot.Pack, Bone.Chest, new Vector3(0,-.055f,-.190f), .90f);

            void Set(int index, EquipmentSlot slot, Bone at, Vector3 offset, float scale)
            {
                var stack = bag?.Equipped(slot);
                if (gearIds[index] == stack?.ItemId) return;
                gearIds[index] = stack?.ItemId;
                if (gear[index] != null) { gear[index].gameObject.SetActive(false); Destroy(gear[index].gameObject); }
                if (stack == null) { gear[index] = null; return; }
                gear[index] = ItemProp.Create(Joint(at), bag.Catalog.Find(stack.ItemId), stack, bag.Catalog);
                gear[index].localPosition = offset;
                if (slot == EquipmentSlot.Pack) gear[index].localRotation = Quaternion.Euler(0f, 180f, 0f);
                gear[index].localScale = Vector3.one * scale;
                var field = gear[index].GetComponent<MeshRenderer>();
                if (field != null && skin.shadowCastingMode == ShadowCastingMode.ShadowsOnly) field.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            }
        }

        internal Transform CreateBackMount()
        {
            var mount = new GameObject("Back weapon mount") { layer = gameObject.layer }.transform;
            mount.SetParent(Joint(Bone.Chest), false);
            mount.SetLocalPositionAndRotation(new Vector3(.14f, .08f, -.38f), Quaternion.Euler(-69f, 180f, -24f));
            return mount;
        }

        internal void BindWeaponView(WeaponVisual weapon) => carriedWeapon = weapon;

        public void PoseArms(Vector3 rightHand, Vector3 leftHand)
        {
            if (fallen || bones == null) return;
            externalGrip = true;
            ApplyArms(rightHand, leftHand);
        }

        void ApplyArms(Vector3 right, Vector3 left)
        {
            Quaternion rightRotation = WeaponMount.rotation * RightGlove;
            Quaternion leftRotation = WeaponMount.rotation * LeftGlove;
            if (carriedWeapon != null && carriedWeapon.isActiveAndEnabled)
            {
                if (skin.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
                    carriedWeapon.ShadowGrips(WeaponMount, out right, out rightRotation, out left, out leftRotation);
                else
                {
                    rightRotation = carriedWeapon.RightGlove;
                    leftRotation = carriedWeapon.LeftGlove;
                }
            }
            Quaternion body = Quaternion.Inverse(transform.rotation);
            PlaceArm(Bone.RightUpper, Bone.RightLower, Bone.RightHand, 1f,
                Finite(right) ? right : WeaponMount.TransformPoint(RightRestGrip), body * rightRotation);
            PlaceArm(Bone.LeftUpper, Bone.LeftLower, Bone.LeftHand, -1f,
                Finite(left) ? left : WeaponMount.TransformPoint(LeftRestGrip), body * leftRotation);
        }

        void PlaceArm(Bone top, Bone mid, Bone hand, float side, Vector3 palm, Quaternion glove)
        {
            Transform shoulder = Joint(top);
            Vector3 origin = transform.InverseTransformPoint(shoulder.position);
            Vector3 wrist = transform.InverseTransformPoint(palm) + glove * (Vector3.up * PalmOffset);
            Vector3 pole = new Vector3(0.80f * side, -1f, -0.23f);
            Solve(origin, wrist, pole, upper, lower, out Vector3 elbow, out Vector3 end);
            Vector3 roll = new Vector3(0.16f * side, 0.46f, 0.87f);
            Quaternion a = DownAxis(elbow - origin, roll);
            Quaternion b = DownAxis(end - elbow, roll);
            shoulder.rotation = transform.rotation * a;
            Joint(mid).localRotation = Quaternion.Inverse(a) * b;
            Joint(hand).localRotation = Quaternion.Inverse(b) * glove;
        }

        internal static void Solve(Vector3 origin, Vector3 target, Vector3 pole, float a, float b, out Vector3 joint, out Vector3 end)
        {
            Vector3 ray = target - origin;
            float length = ray.magnitude;
            Vector3 direction = length > 0.00001f ? ray / length : Vector3.down;
            float d = Mathf.Clamp(length, Mathf.Abs(a - b) + 0.009f, a + b - 0.003f);
            Vector3 bend = Vector3.ProjectOnPlane(pole, direction);
            if (bend.sqrMagnitude < 0.00001f)
                bend = Vector3.ProjectOnPlane(Mathf.Abs(direction.z) < 0.9f ? Vector3.forward : Vector3.right, direction);
            float along = (a * a + d * d - b * b) / (2f * d);
            joint = origin + direction * along + bend.normalized * Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
            end = origin + direction * d;
        }

        static Quaternion LegAxis(Vector3 down) => Quaternion.LookRotation(Vector3.Cross(down, Vector3.right), -down);

        internal static Quaternion DownAxis(Vector3 down, Vector3 roll)
        {
            Vector3 up = -down.normalized;
            Vector3 forward = Vector3.ProjectOnPlane(roll, up);
            if (forward.sqrMagnitude < 0.00001f)
                forward = Vector3.ProjectOnPlane(Mathf.Abs(up.x) < 0.9f ? Vector3.right : Vector3.forward, up);
            return Quaternion.LookRotation(forward.normalized, up);
        }

        public void Fall()
        {
            if (fallen || bones == null) return;
            fallen = true;
            pronePose = 0f;
            CaptureBasePose();
            transform.localPosition = baseLocalPosition;
            transform.localRotation = baseLocalRotation;
            Quaternion lay = Quaternion.Euler(-90f, 0f, 0f);
            Transform hips = Joint(Bone.Hips);
            hips.localPosition = lay * restHips + Vector3.up * OperatorMesh.BackDepth;
            hips.localRotation = lay;
            Joint(Bone.Chest).localRotation = Quaternion.identity;
            Joint(Bone.Head).localRotation = Quaternion.Euler(-8f, 18f, 8f);
            RestChain(Bone.RightUpper, Bone.RightLower, Bone.RightHand, new Vector3(4f, -11f, 31f),
                new Vector3(-15f, 0f, -18f), new Vector3(0f, 18f, -8f));
            RestChain(Bone.LeftUpper, Bone.LeftLower, Bone.LeftHand, new Vector3(2f, 16f, -28f),
                new Vector3(-25f, 0f, 16f), new Vector3(-7f, -19f, 3f));
            RestChain(Bone.RightThigh, Bone.RightShin, Bone.RightBoot, new Vector3(8f, 0f, 9f),
                new Vector3(8f, 0f, 0f), new Vector3(-16f, 8f, 0f));
            RestChain(Bone.LeftThigh, Bone.LeftShin, Bone.LeftBoot, new Vector3(5f, 0f, -7f),
                new Vector3(14f, 0f, 0f), new Vector3(-19f, -7f, 0f));
            WeaponMount.localPosition = CarryPosition;
            WeaponMount.localRotation = Quaternion.Euler(7f, -23f, -18f);
            UpdateBounds();
        }

        void RestChain(Bone a, Bone b, Bone c, Vector3 x, Vector3 y, Vector3 z)
        {
            Joint(a).localRotation = Quaternion.Euler(x);
            Joint(b).localRotation = Quaternion.Euler(y);
            Joint(c).localRotation = Quaternion.Euler(z);
        }

        public void ShadowsOnly()
        {
            if (skin != null) skin.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        }

        Transform Joint(Bone b) => bones[(int)b];
        static bool Finite(float n) => !float.IsNaN(n) && !float.IsInfinity(n);
        static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);

        internal static Bone ParentOf(Bone id)
        {
            switch (id)
            {
                case Bone.Hips: return Bone.Count;
                case Bone.Chest: case Bone.RightThigh: case Bone.LeftThigh: return Bone.Hips;
                case Bone.Head: case Bone.RightUpper: case Bone.LeftUpper: return Bone.Chest;
                case Bone.RightLower: return Bone.RightUpper;
                case Bone.LeftLower: return Bone.LeftUpper;
                case Bone.RightHand: return Bone.RightLower;
                case Bone.LeftHand: return Bone.LeftLower;
                case Bone.RightShin: return Bone.RightThigh;
                case Bone.LeftShin: return Bone.LeftThigh;
                case Bone.RightBoot: return Bone.RightShin;
                default: return Bone.LeftShin;
            }
        }

        internal static Vector3 Rest(Bone id, OperatorStyle style)
        {
            bool female = style == OperatorStyle.Female;
            float armX = female ? 0.177f : 0.204f;
            float armY = female ? 1.393f : 1.408f;
            float upper = female ? 0.259f : 0.273f;
            float lower = female ? 0.241f : 0.249f;
            float legX = female ? 0.104f : 0.096f;
            float pelvis = female ? 0.914f : 0.907f;
            float knee = female ? 0.512f : 0.506f;
            switch (id)
            {
                case Bone.Hips: return new Vector3(0f, pelvis, 0f);
                case Bone.Chest: return new Vector3(0f, 1.198f, 0.002f);
                case Bone.Head: return new Vector3(0f, 1.537f, 0.012f);
                case Bone.RightUpper: return new Vector3(armX, armY, 0.015f);
                case Bone.LeftUpper: return new Vector3(-armX, armY, 0.015f);
                case Bone.RightLower: return new Vector3(armX, armY - upper, 0.015f);
                case Bone.LeftLower: return new Vector3(-armX, armY - upper, 0.015f);
                case Bone.RightHand: return new Vector3(armX, armY - upper - lower, 0.015f);
                case Bone.LeftHand: return new Vector3(-armX, armY - upper - lower, 0.015f);
                case Bone.RightThigh: return new Vector3(legX, pelvis, 0f);
                case Bone.LeftThigh: return new Vector3(-legX, pelvis, 0f);
                case Bone.RightShin: return new Vector3(legX, knee, 0f);
                case Bone.LeftShin: return new Vector3(-legX, knee, 0f);
                case Bone.RightBoot: return new Vector3(legX, 0.115f, 0f);
                default: return new Vector3(-legX, 0.115f, 0f);
            }
        }
    }
}
