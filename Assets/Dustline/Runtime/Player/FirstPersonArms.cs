using UnityEngine;
using UnityEngine.Rendering;
using Bone = Dustline.OperatorVisual.Bone;

namespace Dustline
{
    internal sealed class FirstPersonArms
    {
        const float PalmOffset = 0.044f;
        readonly Transform root;
        readonly SkinnedMeshRenderer skin;
        readonly float inverseWeaponScale;
        readonly Transform[] joints = new Transform[6];
        readonly OperatorStyle style;
        readonly float upper, lower;

        public FirstPersonArms(Transform weapon, OperatorStyle style, int layer)
        {
            this.style = style;
            root = new GameObject("First person sleeves") { layer = layer }.transform;
            root.SetParent(weapon, false);
            inverseWeaponScale = 1f / weapon.localScale.x;
            root.localScale = Vector3.one * inverseWeaponScale;
            for (int i = 0; i < joints.Length; i++)
            {
                var joint = new GameObject(((Bone)((int)Bone.RightUpper + i)).ToString()) { layer = layer };
                joints[i] = joint.transform;
                joints[i].SetParent(i % 3 == 0 ? root : joints[i - 1], false);
            }
            upper = Vector3.Distance(OperatorVisual.Rest(Bone.RightUpper, style), OperatorVisual.Rest(Bone.RightLower, style));
            lower = Vector3.Distance(OperatorVisual.Rest(Bone.RightLower, style), OperatorVisual.Rest(Bone.RightHand, style));
            skin = root.gameObject.AddComponent<SkinnedMeshRenderer>();
            OperatorMesh.AcquireArms(style, out var mesh, out var material);
            skin.sharedMesh = mesh;
            skin.sharedMaterial = material;
            skin.bones = joints;
            skin.rootBone = root;
            skin.localBounds = new Bounds(new Vector3(0, -0.23f, 0.46f), new Vector3(1.45f, 1.24f, 1.42f));
            skin.quality = SkinQuality.Bone2;
            skin.shadowCastingMode = ShadowCastingMode.Off;
            skin.receiveShadows = false;
            skin.skinnedMotionVectors = false;
            skin.lightProbeUsage = LightProbeUsage.Off;
            skin.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        public void SetVisible(bool visible) => skin.enabled = visible;

        internal readonly struct ArmPose
        {
            internal readonly Vector3 Shoulder, Elbow, Wrist;
            internal readonly Quaternion Glove;
            Vector3 Fingers => Wrist + Glove * (Vector3.down * 0.084f);

            internal ArmPose(Vector3 shoulder, Vector3 elbow, Vector3 wrist, Quaternion glove)
            { Shoulder = shoulder; Elbow = elbow; Wrist = wrist; Glove = glove; }

            internal void Enclose(ref Bounds bounds)
            {
                Expand(ref bounds, Shoulder, 0.115f);
                Expand(ref bounds, Elbow, 0.115f);
                Expand(ref bounds, Wrist, 0.064f);
                Expand(ref bounds, Fingers, 0.047f);
            }

            static void Expand(ref Bounds bounds, Vector3 point, float skin)
            {
                bounds.Encapsulate(point - Vector3.one * skin);
                bounds.Encapsulate(point + Vector3.one * skin);
            }

            internal bool Overlaps(Transform eye) => Intersects(Shoulder, Elbow, 0.115f, eye)
                || Intersects(Elbow, Wrist, 0.064f, eye) || Intersects(Wrist, Fingers, 0.047f, eye);

            static bool Intersects(Vector3 a, Vector3 b, float radius, Transform eye) =>
                Physics.CheckCapsule(eye.TransformPoint(a), eye.TransformPoint(b), radius,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        public void Sample(Vector3 right, Quaternion rightRotation, Vector3 left, Quaternion leftRotation,
            float run, float duck, float prone, float tuck, out ArmPose rightArm, out ArmPose leftArm)
        {
            rightArm = Solve(1f, right, rightRotation, new Vector3(0.238f, -0.401f - 0.037f * run + 0.095f * prone,
                Mathf.Lerp(0.139f + 0.066f * prone, 0.105f, tuck)), prone);
            leftArm = Solve(-1f, left, leftRotation, new Vector3(-0.196f, -0.414f + 0.027f * duck + 0.095f * prone,
                Mathf.Lerp(0.247f + 0.035f * prone, 0.107f, tuck)), prone);
        }

        ArmPose Solve(float side, Vector3 palm, Quaternion glove, Vector3 shoulder, float prone)
        {
            Vector3 wrist = palm + glove * (Vector3.up * PalmOffset);
            Vector3 reach = wrist - shoulder;
            float length = reach.magnitude;
            float limit = (upper + lower) * 0.93f;
            if (length > limit) shoulder += reach * ((length - limit) / length);
            var pole = new Vector3(side * Mathf.Lerp(0.75f, 1f, prone), Mathf.Lerp(-1f, -0.16f, prone), -0.41f);
            OperatorVisual.Solve(shoulder, wrist, pole, upper, lower, out var elbow, out var end);
            return new ArmPose(shoulder, elbow, end, glove);
        }

        public void Apply(Vector3 weaponPosition, Quaternion weaponRotation, ArmPose right, ArmPose left)
        {
            // Undo the parent pose so the solved joints remain in camera space.
            Quaternion inverse = Quaternion.Inverse(weaponRotation);
            root.SetLocalPositionAndRotation(inverse * (-weaponPosition * inverseWeaponScale), inverse);
            Place(0, 1f, right);
            Place(3, -1f, left);
        }

        void Place(int index, float side, ArmPose pose)
        {
            var roll = new Vector3(side * 0.17f, 0.41f, 0.89f);
            Quaternion top = OperatorVisual.DownAxis(pose.Elbow - pose.Shoulder, roll);
            Quaternion mid = OperatorVisual.DownAxis(pose.Wrist - pose.Elbow, roll);
            joints[index].SetLocalPositionAndRotation(pose.Shoulder, top);
            joints[index + 1].SetLocalPositionAndRotation(Vector3.down * upper, Quaternion.Inverse(top) * mid);
            joints[index + 2].SetLocalPositionAndRotation(Vector3.down * lower, Quaternion.Inverse(mid) * pose.Glove);
        }

        public void Release() => OperatorMesh.ReleaseArms(style);
    }
}
