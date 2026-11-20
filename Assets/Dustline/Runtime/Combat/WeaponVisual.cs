using UnityEngine;
using UnityEngine.Rendering;

namespace Dustline
{
    public sealed class WeaponVisual : MonoBehaviour
    {
        public Transform Muzzle => muzzle;
        public bool IsMelee { get; private set; }
        public Vector3 RightGrip => transform.TransformPoint(IsMelee ? new Vector3(0, -0.018f, -0.016f) : new Vector3(0.01f, -0.103f, -0.16f));
        public Vector3 LeftGrip => transform.TransformPoint(IsMelee ? new Vector3(-0.17f, -0.16f, -0.32f) : new Vector3(-0.043f, -0.021f, 0.22f));
        Transform muzzle;
        bool firstPerson;
        Ar20Mesh art;
        float aim, gait, sprint, stride, recoil, crouch;
        float reloadTime, reloadDuration, swingTime, swingDuration;

        public static WeaponVisual Create(Transform parent, bool firstPerson, bool melee = false)
        {
            var root = new GameObject(melee ? "Service baton" : "AR-20 | Dustline", typeof(MeshFilter), typeof(MeshRenderer));
            root.layer = firstPerson ? 2 : parent.gameObject.layer;
            root.transform.SetParent(parent, false);
            root.transform.localScale = Vector3.one * (firstPerson ? 0.9f : 0.84f);
            var renderer = root.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            var socket = new GameObject("Effects socket") { layer = root.layer };
            socket.transform.SetParent(root.transform, false);
            socket.transform.localPosition = melee ? new Vector3(0, 0.01f, 0.45f) : new Vector3(0, 0.028f, Ar20Mesh.FaceZ);
            var visual = root.AddComponent<WeaponVisual>();
            visual.firstPerson = firstPerson;
            visual.IsMelee = melee;
            visual.muzzle = socket.transform;
            if (root.activeInHierarchy) visual.BindArt();
            visual.ApplyPose();
            return visual;
        }

        void OnEnable()
        {
            if (muzzle != null && art == null) BindArt();
        }

        void BindArt()
        {
            art = Ar20Mesh.Acquire(IsMelee);
            GetComponent<MeshFilter>().sharedMesh = art.Mesh;
            GetComponent<MeshRenderer>().sharedMaterial = art.Material;
        }

        public void SetEquipped(bool equipped)
        {
            aim = gait = sprint = stride = recoil = crouch = reloadTime = reloadDuration = swingTime = swingDuration = 0;
            ApplyPose();
            gameObject.SetActive(equipped);
        }

        public void SetPose(bool aiming, float movementSpeed, bool sprinting, bool crouched, float delta)
        {
            if (!isActiveAndEnabled) return;
            float movement = Mathf.Clamp01(movementSpeed / (crouched ? 2f : 3.6f));
            aim = Mathf.Lerp(aim, aiming && !IsMelee ? 1 : 0, 1 - Mathf.Exp(-14 * delta));
            gait = Mathf.Lerp(gait, movement, 1 - Mathf.Exp(-9 * delta));
            sprint = Mathf.Lerp(sprint, sprinting && !aiming ? movement : 0, 1 - Mathf.Exp(-8 * delta));
            crouch = Mathf.Lerp(crouch, crouched ? 1 : 0, 1 - Mathf.Exp(-10 * delta));
            stride = Mathf.Repeat(stride + delta * Mathf.Lerp(4, 13, gait) * Mathf.Lerp(1, 0.68f, crouch), 2 * Mathf.PI);
            recoil *= Mathf.Exp(-15 * delta);
            reloadTime = Mathf.Min(reloadTime + delta, reloadDuration);
            swingTime = Mathf.Min(swingTime + delta, swingDuration);
            ApplyPose();
        }

        public void Kick()
        {
            if (!isActiveAndEnabled) return;
            recoil = Mathf.Min(1.35f, recoil + 0.86f);
            ApplyPose();
        }

        public void BeginReload(float seconds)
        {
            reloadTime = 0;
            reloadDuration = Mathf.Max(0, seconds);
        }

        public void Swing(float duration)
        {
            swingTime = 0;
            swingDuration = duration;
            ApplyPose();
        }

        void ApplyPose()
        {
            if (IsMelee)
            {
                float phase = swingDuration > 0 ? swingTime / swingDuration : 1;
                float draw = Mathf.SmoothStep(0, 1, phase / 0.31f) * (1 - Mathf.SmoothStep(0, 1, (phase - 0.30f) / 0.16f));
                float sweep = Mathf.SmoothStep(0, 1, (phase - 0.26f) / 0.25f) * (1 - Mathf.SmoothStep(0, 1, (phase - 0.51f) / 0.49f));
                float bobY = Mathf.Sin(stride * 2) * gait;
                transform.localPosition = new Vector3(0.27f + 0.09f * draw - 0.35f * sweep, -0.27f + 0.11f * sweep + 0.013f * bobY - 0.037f * sprint,
                    0.55f - 0.11f * draw + 0.11f * sweep);
                transform.localRotation = Quaternion.Euler(-18 - 28 * draw + 14 * sweep + 18 * sprint, -11 + 40 * draw - 70 * sweep,
                    -17 - 49 * draw + 38 * sweep);
                return;
            }
            float progress = reloadDuration > 0 ? reloadTime / reloadDuration : 0;
            float reload = Mathf.SmoothStep(0, 1, progress / 0.22f) *
                (1 - Mathf.SmoothStep(0, 1, (progress - 0.64f) / 0.36f));
            float roll = reload * Mathf.Sin(progress * 2 * Mathf.PI);
            float bob = gait * (1 - 0.72f * aim) * (1 + 0.55f * sprint) * (1 - 0.38f * crouch);
            float horizontal = Mathf.Sin(stride) * bob;
            float vertical = Mathf.Sin(2 * stride) * bob;
            if (firstPerson)
            {
                var position = Vector3.Lerp(new Vector3(0.255f, -0.214f, 0.589f), new Vector3(0, -0.142f, 0.63f), aim);
                transform.localPosition = position + new Vector3(0.009f * horizontal + 0.05f * reload + 0.044f * sprint,
                    0.008f * vertical - 0.062f * reload - 0.038f * sprint, -0.029f * recoil - 0.044f * reload);
                transform.localRotation = Quaternion.Euler(0.65f * vertical - 3.4f * recoil + 13 * reload + 11 * sprint,
                    Mathf.Lerp(-7, 0, aim) + 1.1f * horizontal + 8 * reload - 14 * sprint,
                    Mathf.Lerp(-8, 0, aim) - 1.4f * horizontal + 19 * reload + 7 * roll - 13 * sprint);
            }
            else
            {
                transform.localPosition = new Vector3(0.063f + 0.005f * horizontal, 0.008f * vertical - 0.028f * reload, 0.075f - 0.019f * recoil);
                transform.localRotation = Quaternion.Euler(Mathf.Lerp(7, 0, aim) - 2.8f * recoil + 15 * reload + 8 * sprint,
                    0.8f * horizontal + 5 * reload, -1.2f * horizontal + 17 * reload + 4 * roll);
            }
        }

        void OnDestroy()
        {
            if (art != null) art.Release();
        }
    }
}
