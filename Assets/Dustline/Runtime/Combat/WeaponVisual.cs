using UnityEngine;
using UnityEngine.Rendering;

namespace Dustline
{
    public sealed class WeaponVisual : MonoBehaviour
    {
        const float ViewScale = 0.90f;
        const float BodyScale = 0.84f;
        const float Tau = 2f * Mathf.PI;
        const float ShotPoseSeconds = 0.143f;
        const float ShotPoseRelease = 0.081f;

        public Transform Muzzle { get; private set; }
        public WeaponForm Form { get; private set; }
        public bool IsMelee => Form != WeaponForm.Rifle;
        internal float VisibleAim => firstPerson && !IsMelee ? aimProgress : 0f;
        internal bool MedicineBusy => usingMedicine || medicineBlend > 0f;
        public Vector3 RightGrip => transform.TransformPoint(rightGrip);
        public Vector3 LeftGrip => IsMelee && Form != WeaponForm.Tangdao
            ? transform.parent.TransformPoint(FreeHand(firstPerson)) : transform.TransformPoint(leftGrip);
        internal Quaternion RightGlove => transform.rotation * rightGlove;
        internal Quaternion LeftGlove => IsMelee && Form != WeaponForm.Tangdao
            ? transform.parent.rotation * FreeGlove : transform.rotation * leftGlove;

        bool firstPerson;
        MeshFilter filter;
        MeshRenderer surface, magazineProp, medicineProp;
        MeshFilter medicineMesh;
        Ar20Mesh art;
        FirstPersonArms arms;
        PlayerController player;
        CharacterController motor;
        int sightLevel, swingStep;
        Vector3 rightGrip, leftGrip;
        Quaternion rightGlove, leftGlove;
        static readonly Quaternion FreeGlove = Quaternion.Euler(-42, -51, 26);
        float aimProgress, speed, run, stride, breath, crouch, prone;
        float kick, kickImpulse, shotPose;
        Vector3 shotAxis;
        float reloadTime, reloadDuration, reloadBlend;
        Magazine reloadSource;
        float swingTime, swingDuration, swingBlend;
        bool reloadCancelled, swingCancelled, usingMedicine;
        float medicineBlend, medicineProgress;
        Vector3 medicineAnchor;
        Quaternion medicineRotation;

        public static WeaponVisual Create(Transform parent, bool firstPerson, bool melee = false)
            => Create(parent, firstPerson, melee ? WeaponForm.Baton : WeaponForm.Rifle);

        public static WeaponVisual Create(Transform parent, bool firstPerson, WeaponForm form)
        {
            var root = new GameObject("Weapon view", typeof(MeshFilter), typeof(MeshRenderer));
            root.layer = firstPerson ? FirstPersonView.Layer : parent.gameObject.layer;
            root.transform.SetParent(parent, false);
            root.transform.localScale = Vector3.one * (firstPerson ? ViewScale : BodyScale);
            var visual = root.AddComponent<WeaponVisual>();
            visual.firstPerson = firstPerson;
            visual.filter = root.GetComponent<MeshFilter>();
            visual.surface = root.GetComponent<MeshRenderer>();
            visual.surface.shadowCastingMode = ShadowCastingMode.Off;
            visual.surface.receiveShadows = false;
            visual.surface.lightProbeUsage = LightProbeUsage.Off;
            visual.surface.reflectionProbeUsage = ReflectionProbeUsage.Off;

            var socket = new GameObject("Effects socket") { layer = root.layer };
            visual.Muzzle = socket.transform;
            visual.Muzzle.SetParent(root.transform, false);
            visual.player = firstPerson ? parent.GetComponentInParent<PlayerController>() : null;
            visual.motor = firstPerson ? null : parent.GetComponentInParent<CharacterController>();
            var body = firstPerson ? visual.player?.Operator : parent.GetComponentInParent<OperatorVisual>();
            body?.BindWeaponView(visual);
            visual.ConfigureForm(form);
            if (root.activeInHierarchy) visual.BindArt();
            else visual.ApplyPose();
            return visual;
        }

        void OnEnable()
        {
            if (Muzzle != null) BindArt();
        }

        void BindArt()
        {
            if (art != null) return;
            art = Ar20Mesh.Acquire();
            if (firstPerson)
            {
                art.WarmPlayerViews();
                arms = new FirstPersonArms(transform, player != null ? player.Operator.Style : OperatorStyle.Male, gameObject.layer);
                magazineProp = CreateProp("Magazine", out var magazineMesh);
                magazineMesh.sharedMesh = art.MagazineMesh;
                medicineProp = CreateProp("Medicine", out medicineMesh);
            }
            surface.sharedMaterial = art.Material;
            filter.sharedMesh = art.GetMesh(Form, sightLevel, firstPerson);
            ApplyPose();
        }

        MeshRenderer CreateProp(string name, out MeshFilter mesh)
        {
            var node = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)) { layer = gameObject.layer };
            node.transform.SetParent(transform, false);
            mesh = node.GetComponent<MeshFilter>();
            var renderer = node.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = art.Material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            node.SetActive(false);
            return renderer;
        }

        public void SetForm(WeaponForm form)
        {
            if (Form == form) return;
            ResetPose();
            ConfigureForm(form);
            if (art != null) filter.sharedMesh = art.GetMesh(Form, sightLevel, firstPerson);
            ApplyPose();
        }

        void ConfigureForm(WeaponForm form)
        {
            Form = form;
            sightLevel = 0;
            switch (Form)
            {
                case WeaponForm.Tangdao:
                    rightGrip = new Vector3(0.016f, -0.011f, 0.005f);
                    leftGrip = new Vector3(-0.012f, -0.012f, -0.142f);
                    rightGlove = Quaternion.Euler(-16, 66, -56);
                    leftGlove = Quaternion.Euler(-21, -65, 49);
                    Muzzle.localPosition = new Vector3(0, 0.057f, 0.861f);
                    break;
                case WeaponForm.ShortBlade:
                    rightGrip = new Vector3(0.017f, -0.012f, -0.052f);
                    rightGlove = Quaternion.Euler(-24, 70, -56);
                    Muzzle.localPosition = new Vector3(0, 0.039f, 0.411f);
                    break;
                case WeaponForm.Baton:
                    rightGrip = new Vector3(0.018f, -0.012f, -0.023f);
                    rightGlove = Quaternion.Euler(-14, 65, -50);
                    Muzzle.localPosition = new Vector3(0, 0, 0.453f);
                    break;
                default:
                    rightGrip = Ar20Mesh.RightPalm;
                    leftGrip = Ar20Mesh.LeftPalm;
                    rightGlove = Quaternion.Euler(-26f, -13f, -18f);
                    leftGlove = Quaternion.Euler(-72f, 12f, 45f);
                    Muzzle.localPosition = Ar20Mesh.Muzzle;
                    break;
            }
        }

        public void SetSightLevel(int level)
        {
            int value = IsMelee ? 0 : Mathf.Clamp(level, 0, 2);
            if (sightLevel == value) return;
            sightLevel = value;
            if (art != null) filter.sharedMesh = art.GetMesh(Form, sightLevel, firstPerson);
            ApplyPose();
        }

        public void SetEquipped(bool equipped)
        {
            ResetPose();
            gameObject.SetActive(equipped);
            if (equipped) ApplyPose();
        }

        void ResetPose()
        {
            aimProgress = speed = run = stride = breath = crouch = prone = kick = kickImpulse = shotPose = 0;
            reloadTime = reloadDuration = reloadBlend = swingTime = swingDuration = swingBlend = 0;
            reloadSource = null;
            medicineBlend = medicineProgress = 0f;
            reloadCancelled = swingCancelled = usingMedicine = false;
        }

        public void SetPose(bool aiming, float movementSpeed, bool sprinting, bool crouched, float delta, bool walkForFire = false)
        {
            if (!isActiveAndEnabled || delta <= 0) return;
            float dt = Mathf.Min(delta, 0.10f);
            if (motor != null && motor.enabled)
            {
                var velocity = motor.velocity;
                movementSpeed = new Vector2(velocity.x, velocity.z).magnitude;
            }
            speed = Mathf.Lerp(speed, Mathf.Clamp(movementSpeed, 0, 8), Damp(11, dt));
            crouch = Mathf.Lerp(crouch, crouched ? 1 : 0, Damp(11, dt));
            prone = Mathf.Lerp(prone, player != null && player.Prone ? 1 : 0, Damp(9, dt));
            run = walkForFire ? 0f : Mathf.Lerp(run, sprinting && !aiming ? Mathf.SmoothStep(0, 1, (speed - 1.2f) / 3.5f) : 0, Damp(10, dt));
            shotPose = Mathf.Max(0f, shotPose - delta);
            float stepDistance = Mathf.Lerp(Mathf.Lerp(1.38f, 2.10f, run), Mathf.Lerp(0.91f, 0.76f, prone), crouch);
            stride = Mathf.Repeat(stride + dt * speed / stepDistance * Mathf.PI, Tau);
            breath = Mathf.Repeat(breath + dt * 1.77f, Tau);
            float aimSeconds = player != null && player.Weapon != null ? player.Weapon.AimSeconds : 0.18f;
            medicineBlend = Mathf.MoveTowards(medicineBlend, usingMedicine ? 1f : 0f, 8f * dt);
            bool sighting = aiming && !IsMelee && !MedicineBusy && (reloadDuration <= 0 || reloadCancelled);
            aimProgress = Mathf.MoveTowards(aimProgress, sighting ? 1 : 0, delta / Mathf.Max(0.08f, aimSeconds));
            kick = Mathf.Lerp(kick, kickImpulse, Damp(38, dt));
            kickImpulse *= Mathf.Exp(-17 * dt);
            if (reloadSource != null) reloadTime = reloadDuration - reloadSource.Remaining;
            else AdvanceAction(ref reloadTime, ref reloadDuration, ref reloadBlend, reloadCancelled, delta);
            AdvanceAction(ref swingTime, ref swingDuration, ref swingBlend, swingCancelled, delta);
            ApplyPose();
        }

        static float Damp(float rate, float delta) => 1f - Mathf.Exp(-rate * delta);
        static float Ease(float t) => Mathf.SmoothStep(0, 1, t);

        static void AdvanceAction(ref float time, ref float duration, ref float blend, bool cancelled, float delta)
        {
            if (duration <= 0) return;
            if (cancelled) blend = Mathf.MoveTowards(blend, 0, 8 * delta);
            else time += delta;
            if (time >= duration || blend <= 0) time = duration = blend = 0;
        }

        public void Kick() => Kick(1.2f);

        public void Kick(float strength)
        {
            if (!isActiveAndEnabled) return;
            float impulse = Mathf.Clamp(strength * 0.74f, 0, 1.4f);
            kick = Mathf.Min(1.5f, kick + impulse * 0.26f);
            kickImpulse = Mathf.Min(2.1f, kickImpulse + impulse);
            if (!firstPerson) ApplyPose();
        }

        internal bool AlignShot(Vector3 direction, Vector3 end)
        {
            if (!firstPerson || IsMelee) return false;
            Vector3 toward = end - Muzzle.position;
            // An impact close to the eye can be behind the cosmetic muzzle.
            bool traceFromMuzzle = Vector3.Dot(toward, direction) > 1.25f;
            shotAxis = transform.parent.InverseTransformDirection(traceFromMuzzle ? toward.normalized : direction);
            shotPose = ShotPoseSeconds;
            ApplyPose();
            return traceFromMuzzle;
        }

        public void BeginReload(float seconds)
        {
            shotPose = 0f;
            reloadSource = null;
            reloadTime = 0;
            reloadDuration = Mathf.Max(0, seconds);
            reloadBlend = 1;
            reloadCancelled = false;
        }

        internal void BeginReload(Magazine source)
        {
            BeginReload(source.Remaining);
            reloadSource = source;
        }

        internal void CancelReload()
        {
            reloadSource = null;
            reloadCancelled = true;
        }

        internal void EndReload()
        {
            reloadSource = null;
            reloadTime = reloadDuration = reloadBlend = 0f;
            ApplyPose();
        }

        internal void BeginMedicine(MedicineForm form)
        {
            if (medicineMesh == null) return;
            usingMedicine = true;
            shotPose = 0f;
            medicineProgress = 0f;
            medicineMesh.sharedMesh = art.MedicineMesh(form);
        }

        internal void SetMedicineProgress(float progress)
        {
            if (usingMedicine) medicineProgress = progress;
        }

        internal void EndMedicine() => usingMedicine = false;

        public void Swing(float duration) => Swing(duration, 0);

        internal void Swing(float duration, int step)
        {
            swingTime = 0;
            swingDuration = Mathf.Max(0, duration);
            swingStep = step;
            swingBlend = 1;
            swingCancelled = false;
        }

        internal void CancelSwing() => swingCancelled = true;
        internal static float ContactPhase(int step) => step == 0 ? 0.40f : step == 1 ? 0.43f : 0.49f;

        void ApplyPose()
        {
            Vector3 position;
            Quaternion rotation;
            if (IsMelee)
            {
                MeleeLocalPose(firstPerson, out position, out var euler);
                rotation = Quaternion.Euler(euler);
            }
            else
            {
                RifleLocalPose(out position, out rotation);
                if (firstPerson && shotPose > 0f)
                {
                    Vector3 tip = Muzzle.localPosition * ViewScale;
                    Vector3 anchor = position + rotation * tip;
                    var facing = Quaternion.FromToRotation(rotation * Vector3.forward, shotAxis) * rotation;
                    rotation = Quaternion.Slerp(rotation, facing, Ease(shotPose / ShotPoseRelease));
                    position = anchor - rotation * tip;
                }
            }
            if (medicineBlend > 0f)
            {
                float lower = Ease(medicineBlend);
                position += new Vector3(0.158f, -0.36f, -0.092f) * lower;
                rotation = Quaternion.Slerp(rotation, Quaternion.Euler(32f, 34f, 24f), lower);
            }
            transform.SetLocalPositionAndRotation(position, rotation);
            if (arms == null) return;
            PoseProps(position, rotation);
            SampleArms(position, rotation, out var right, out var left);
            arms.Apply(position, rotation, right, left);
        }

        float ReloadPhase => reloadDuration > 0f ? Mathf.Clamp01(reloadTime / reloadDuration) : 0f;

        void PoseProps(Vector3 position, Quaternion rotation)
        {
            float phase = ReloadPhase;
            float carry = (Ease((phase - .16f) / .25f) - Ease((phase - .56f) / .25f)) * reloadBlend;
            float exchange = Ease((phase - .43f) / .12f);
            Vector3 path = Vector3.Lerp(new Vector3(-.064f, -.187f, .028f), new Vector3(-.117f, -.193f, .060f), exchange);
            magazineProp.transform.SetLocalPositionAndRotation(path * carry,
                Quaternion.Euler(18f * carry, Mathf.Lerp(-18f, 10f, exchange) * carry, -15f * carry));
            SetPropVisible(magazineProp, !IsMelee && !(phase >= .43f && phase < .55f && !reloadCancelled));

            float use = Ease(medicineBlend);
            bool showMedicine = use > .20f && medicineMesh.sharedMesh != null;
            SetPropVisible(medicineProp, showMedicine);
            if (medicineBlend <= 0f) return;
            float settle = Mathf.Sin(medicineProgress * Mathf.PI);
            medicineAnchor = new Vector3(-.123f + .024f * settle, Mathf.Lerp(-.437f, -.193f, use), .505f + .023f * settle);
            medicineRotation = Quaternion.Euler(7f - 19f * settle, -23f, 17f + 8f * settle);
            Quaternion inverse = Quaternion.Inverse(rotation);
            medicineProp.transform.SetLocalPositionAndRotation(inverse * (medicineAnchor - position) / ViewScale, inverse * medicineRotation);
        }

        static void SetPropVisible(MeshRenderer prop, bool visible)
        {
            if (prop.gameObject.activeSelf != visible) prop.gameObject.SetActive(visible);
        }

        void SampleArms(Vector3 position, Quaternion rotation,
            out FirstPersonArms.ArmPose rightArm, out FirstPersonArms.ArmPose leftArm)
        {
            Vector3 right = position + rotation * (rightGrip * ViewScale);
            Vector3 left = position + rotation * (leftGrip * ViewScale);
            Quaternion rightWrist = rotation * rightGlove;
            Quaternion leftWrist = rotation * leftGlove;
            if (IsMelee && Form != WeaponForm.Tangdao)
            {
                left = FreeHand(true);
                leftWrist = FreeGlove;
            }
            else if (!IsMelee && reloadDuration > 0)
            {
                float handling = ReloadWeight(ReloadPhase, reloadBlend);
                var mag = magazineProp.transform;
                Vector3 service = mag.localPosition + mag.localRotation * new Vector3(-.035f, -.138f, .067f);
                left = Vector3.Lerp(left, position + rotation * (service * ViewScale), handling);
                leftWrist = Quaternion.Slerp(leftWrist, rotation * mag.localRotation * Quaternion.Euler(-22f, -38f, 18f), handling);
            }
            if (medicineBlend > 0f)
            {
                float blend = Ease(medicineBlend);
                float motion = Mathf.Sin(medicineProgress * Mathf.PI * 3f) * .020f;
                left = Vector3.Lerp(left, medicineAnchor + medicineRotation * new Vector3(-.046f, -.044f, 0f), blend);
                right = Vector3.Lerp(right, medicineAnchor + medicineRotation * new Vector3(.065f, .026f + motion, -.012f), blend);
                leftWrist = Quaternion.Slerp(leftWrist, medicineRotation * Quaternion.Euler(-69f, 20f, 38f), blend);
                rightWrist = Quaternion.Slerp(rightWrist, medicineRotation * Quaternion.Euler(-16f, -31f, -34f), blend);
            }
            arms.Sample(right, rightWrist, left, leftWrist, run, crouch, prone, out rightArm, out leftArm);
        }

        void RifleLocalPose(out Vector3 position, out Quaternion rotation)
        {
            float aim = Ease(aimProgress);
            float bob = Gait() * (1 - aim);
            float horizontal = Mathf.Sin(stride) * bob;
            float vertical = Mathf.Sin(2 * stride) * bob;
            float idle = Mathf.Sin(breath) * (1 - aim);
            float progress = ReloadPhase;
            float reload = ReloadWeight(progress, reloadBlend);
            float roll = reload * Mathf.Sin(progress * Tau);
            if (firstPerson)
            {
                Vector3 hip = new Vector3(0.247f, -0.216f, 0.601f);
                Vector3 ads = new Vector3(0, -Ar20Mesh.SightHeight(sightLevel) * ViewScale, 0.608f);
                float carry = run * (1 - aim);
                position = Vector3.Lerp(hip, ads, aim) + new Vector3(0.013f * horizontal - 0.074f * reload + 0.047f * carry,
                    0.011f * vertical + 0.002f * idle + 0.072f * reload - 0.064f * carry - 0.018f * prone * (1 - aim),
                    -0.045f * kick + 0.087f * reload - 0.032f * carry);
                rotation = Quaternion.Euler(0.92f * vertical - Mathf.Lerp(3.2f, 1.15f, aim) * kick + 13 * reload + 16 * carry,
                    -7 * (1 - aim) + 1.45f * horizontal + 5 * reload - 17 * carry,
                    -8 * (1 - aim) - 1.8f * horizontal + 23 * reload + 9 * roll - 16 * carry);
            }
            else
            {
                position = new Vector3(0.062f + 0.005f * horizontal, 0.008f * vertical - 0.026f * reload, 0.066f - 0.021f * kick);
                rotation = Quaternion.Euler(7 * (1 - aim) - 2.8f * kick + 15 * reload + 8 * run,
                    0.8f * horizontal + 5 * reload, -1.2f * horizontal + 17 * reload + 4 * roll);
            }
        }

        void MeleeLocalPose(bool view, out Vector3 position, out Vector3 euler)
        {
            float gait = Gait();
            float x = gait * Mathf.Sin(stride), y = gait * Mathf.Sin(2 * stride);
            float idle = Mathf.Sin(breath);
            if (view)
            {
                switch (Form)
                {
                    case WeaponForm.Tangdao:
                        position = new Vector3(0.249f, -0.223f, 0.610f);
                        euler = new Vector3(-38, -11, -32);
                        break;
                    case WeaponForm.ShortBlade:
                        position = new Vector3(0.243f, -0.224f, 0.583f);
                        euler = new Vector3(-33, -17, -16);
                        break;
                    default:
                        position = new Vector3(0.261f, -0.240f, 0.602f);
                        euler = new Vector3(-31, -13, -14);
                        break;
                }
                position += new Vector3(x * 0.018f + run * 0.034f, y * 0.014f + idle * 0.003f - run * 0.047f - prone * 0.023f, -run * 0.043f);
                euler += new Vector3(2 * y + run * 23, 2 * x - run * 8, -3 * x - run * 7);
            }
            else
            {
                position = new Vector3(Form == WeaponForm.Tangdao ? 0.025f : 0.076f, -0.046f + y * 0.008f - run * 0.021f, 0.084f);
                euler = Form == WeaponForm.Tangdao ? new Vector3(-29, 17, -23) : new Vector3(-27, 14, -14);
                euler += new Vector3(2 * y + run * 14, 2 * x, -2 * x);
            }
            if (swingDuration <= 0) return;
            SampleSwing(out var shift, out var rotate);
            float scale = Form == WeaponForm.ShortBlade ? 0.79f : Form == WeaponForm.Tangdao ? 1.0f : 0.92f;
            position += shift * (swingBlend * scale * (view ? 1 : 0.55f));
            euler += rotate * (swingBlend * scale * (view ? 1 : 0.70f));
        }

        void SampleSwing(out Vector3 shift, out Vector3 rotate)
        {
            Vector3 takeBack, follow, loadEuler, finishEuler;
            switch (swingStep)
            {
                case 1:
                    takeBack = new Vector3(-0.23f, -0.034f, -0.043f);
                    follow = new Vector3(0.113f, 0.069f, 0.053f);
                    loadEuler = new Vector3(38, -61, 43);
                    finishEuler = new Vector3(9, 44, -39);
                    break;
                case 2:
                    bool blunt = Form == WeaponForm.Baton;
                    takeBack = blunt ? new Vector3(0.054f, 0.19f, -0.07f) : new Vector3(0.024f, -0.046f, -0.14f);
                    follow = blunt ? new Vector3(-0.088f, -0.079f, 0.10f) : new Vector3(-0.19f, 0.059f, 0.16f);
                    loadEuler = blunt ? new Vector3(-77, 20, -24) : new Vector3(10, 18, 54);
                    finishEuler = blunt ? new Vector3(74, -24, 8) : new Vector3(30, -14, 24);
                    break;
                default:
                    takeBack = new Vector3(0.063f, 0.093f, -0.072f);
                    follow = new Vector3(-0.315f, -0.025f, 0.080f);
                    loadEuler = new Vector3(-28, 52, -42);
                    finishEuler = new Vector3(65, -37, 36);
                    break;
            }
            float phase = swingTime / swingDuration;
            float contact = ContactPhase(swingStep);
            float wind = contact - 0.16f, finish = contact + 0.14f;
            if (phase < wind)
            {
                float weight = Ease(phase / wind);
                shift = takeBack * weight;
                rotate = loadEuler * weight;
            }
            else if (phase < finish)
            {
                float t = Ease((phase - wind) / (finish - wind));
                shift = Vector3.Lerp(takeBack, follow, t);
                rotate = Vector3.Lerp(loadEuler, finishEuler, t);
            }
            else
            {
                float weight = 1 - Ease((phase - finish) / (1 - finish));
                shift = follow * weight;
                rotate = finishEuler * weight;
            }
        }

        internal void ShadowGrips(Transform mount, out Vector3 right, out Quaternion rightRotation,
            out Vector3 left, out Quaternion leftRotation)
        {
            Vector3 position, euler;
            if (IsMelee) MeleeLocalPose(false, out position, out euler);
            else
            {
                position = new Vector3(0.062f, 0, 0.066f - 0.02f * kick);
                euler = new Vector3(6 + 8 * run - 2 * kick, 0, 0);
            }
            Quaternion carry = Quaternion.Euler(euler);
            right = mount.TransformPoint(position + carry * (rightGrip * BodyScale));
            rightRotation = mount.rotation * carry * rightGlove;
            bool free = IsMelee && Form != WeaponForm.Tangdao;
            left = mount.TransformPoint(free ? FreeHand(false) : position + carry * (leftGrip * BodyScale));
            leftRotation = mount.rotation * (free ? FreeGlove : carry * leftGlove);
        }

        Vector3 FreeHand(bool view)
        {
            float swing = swingDuration > 0 ? Mathf.Sin(Mathf.PI * swingTime / swingDuration) * swingBlend : 0;
            float side = swingStep == 1 ? 1 : -1;
            return view
                ? new Vector3(-0.235f + 0.045f * side * swing, -0.246f - 0.052f * run + 0.075f * swing, 0.524f - 0.051f * run - 0.04f * swing)
                : new Vector3(-0.226f + 0.028f * side * swing, -0.123f - 0.04f * run + 0.042f * swing, 0.026f - 0.018f * swing);
        }

        float Gait() => Mathf.SmoothStep(0, 1, speed / Mathf.Lerp(3.6f, 1.7f, crouch))
            * (1 + 0.56f * run) * (1 - 0.28f * crouch - 0.25f * prone);

        static float ReloadWeight(float progress, float blend) => blend * Ease(progress / 0.24f) * (1 - Ease((progress - 0.67f) / 0.33f));

        void OnDestroy()
        {
            arms?.Release();
            art?.Release();
        }
    }
}
