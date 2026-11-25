#if !UNITY_SERVER
using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;
using Point = System.Numerics.Vector3;

namespace Dustline
{
    internal sealed class PilotView : IDisposable
    {
        readonly Transform owner;
        readonly Camera eye, foreground;
        readonly AudioSource localVoice;
        readonly List<Material> materials = new List<Material>();
        readonly Avatar[] avatars = new Avatar[4];
        readonly MeshRenderer[] crates = new MeshRenderer[PilotMap.Crates.Length];
        readonly AudioClip shot;
        readonly GameObject cameraNode;
        readonly Material source;
        Material emptyCrate;
        GameObject arena;
        WeaponVisual hands;
        bool viewStarted, reloading;
        uint localShots;

        sealed class Avatar
        {
            public GameObject Node;
            public OperatorVisual Body;
            public WeaponVisual Weapon;
            public AudioSource Voice;
            public uint Shots;
        }

        public PilotView(Transform owner)
        {
            this.owner = owner;
            source = Resources.Load<Material>("Dustline/Rendering/LitBase");
            if (source == null) throw new InvalidOperationException("Pilot material not prepared.");
            cameraNode = new GameObject("Pilot camera", typeof(Camera), typeof(AudioListener), typeof(AudioSource));
            cameraNode.transform.SetParent(owner, false);
            try
            {
                eye = cameraNode.GetComponent<Camera>();
                eye.clearFlags = CameraClearFlags.SolidColor;
                eye.backgroundColor = new Color32(31, 41, 48, 255);
                eye.nearClipPlane = .06f;
                eye.farClipPlane = 160f;
                eye.fieldOfView = WeaponTuning.DefaultViewFov;
                foreground = FirstPersonView.Create(eye);
                localVoice = cameraNode.GetComponent<AudioSource>();
                localVoice.playOnAwake = false;
                shot = CombatFoley.Shot(0);
            }
            catch { cameraNode.SetActive(false); Object.Destroy(cameraNode); throw; }
        }

        public void Show(in PilotFrame frame, float yaw, float pitch, bool aiming, float dt)
        {
            if (arena == null) Build();
            var local = frame.Actor(frame.Seat);
            float blend = 1f - Mathf.Exp(-16f * Mathf.Min(.1f, dt));
            Vector3 destination = ToUnity(local.Position) + Vector3.up * (local.Crouched ? 1.02f : 1.58f);
            eye.transform.position = viewStarted ? Vector3.Lerp(eye.transform.position, destination, blend) : destination;
            eye.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            hands.gameObject.SetActive(local.Visible);
            if (local.Visible)
            {
                if (frame.Reloading != reloading)
                {
                    if (frame.Reloading) hands.BeginReload(PilotMatch.ReloadSeconds);
                    else hands.EndReload();
                }
                reloading = frame.Reloading;
                hands.SetPose(aiming, local.Speed, local.Running, local.Crouched, dt, reloading);
                if (viewStarted && localShots != local.Shots)
                {
                    hands.Kick();
                    localVoice.PlayOneShot(shot, .76f);
                }
            }
            localShots = local.Shots;
            float visibleAim = local.Visible ? hands.VisibleAim : 0f;
            eye.fieldOfView = foreground.fieldOfView = Mathf.Lerp(WeaponTuning.DefaultViewFov, 48f, visibleAim);
            for (int i = 0; i < crates.Length; i++)
                if ((frame.Claimed & (1 << i)) != 0 && crates[i].sharedMaterial != emptyCrate) crates[i].sharedMaterial = emptyCrate;
            for (int i = 0; i < avatars.Length; i++)
            {
                var actor = frame.Actor(i);
                var avatar = avatars[i];
                bool visible = i != frame.Seat && actor.Visible;
                bool existed = avatar.Node.activeSelf;
                if (existed != visible) avatar.Node.SetActive(visible);
                if (visible)
                {
                    var t = avatar.Node.transform;
                    Vector3 p = ToUnity(actor.Position);
                    t.SetPositionAndRotation(existed ? Vector3.Lerp(t.position, p, blend) : p,
                        Quaternion.Euler(0f, existed ? Mathf.LerpAngle(t.eulerAngles.y, actor.Yaw, blend) : actor.Yaw, 0f));
                    avatar.Body.SetMotion(actor.Speed, actor.Running, actor.Crouched, dt);
                    avatar.Weapon.SetPose(false, actor.Speed, actor.Running, actor.Crouched, dt);
                    avatar.Body.PoseArms(avatar.Weapon.RightGrip, avatar.Weapon.LeftGrip);
                    if (existed && avatar.Shots != actor.Shots)
                    {
                        avatar.Weapon.Kick();
                        avatar.Voice.PlayOneShot(shot, .68f);
                    }
                }
                avatar.Shots = actor.Shots;
            }
            viewStarted = true;
        }

        void Build()
        {
            arena = new GameObject("West slope pilot field");
            arena.transform.SetParent(owner, false);
            eye.backgroundColor = new Color32(128, 152, 160, 255);
            var sun = new GameObject("Pilot daylight", typeof(Light));
            sun.transform.SetParent(arena.transform, false);
            sun.transform.rotation = Quaternion.Euler(49f, -35f, 0f);
            var light = sun.GetComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color32(255, 239, 215, 255);
            light.intensity = 1.32f;
            light.shadows = LightShadows.Soft;

            var dirt = Paint(new Color32(144, 127, 101, 255));
            var road = Paint(new Color32(111, 111, 100, 255));
            var dark = Paint(new Color32(94, 102, 109, 255));
            var wall = Paint(new Color32(149, 149, 137, 255));
            var rock = Paint(new Color32(104, 102, 90, 255));
            var crate = Paint(new Color32(224, 161, 69, 255));
            emptyCrate = Paint(new Color32(100, 98, 87, 255));
            var exit = Paint(new Color32(73, 176, 147, 255));
            Cube("Quarry grade", new PilotBox(0, -.35f, 0, 100, .7f, 105), dirt);
            Cube("Haul road", new PilotBox(0, .005f, 0, 8, .01f, 85), road);
            Cube("Dispatch apron", new PilotBox(0, .015f, -26, 34, .01f, 17), road);
            for (int i = 0; i < PilotMap.Walls.Length; i++)
                Cube("Quarry cover", PilotMap.Walls[i], i < 4 || i >= 14 ? rock : i == 7 || (i & 1) == 0 ? dark : wall);
            for (int i = 0; i < crates.Length; i++) crates[i] = Cube("Supply container " + (i + 1), PilotMap.Crates[i], crate);
            for (int i = 0; i < 24; i++)
            {
                float a = i * 15f;
                var p = PilotMap.Exit + PilotMap.Facing(a) * PilotMap.ExitRadius;
                var marker = Cube("Extraction ring", new PilotBox(p.X, .03f, p.Z, .84f, .06f, .21f), exit);
                marker.transform.rotation = Quaternion.Euler(0f, a, 0f);
            }
            Cube("Exit marker", new PilotBox(0, .78f, 38.9f, .24f, 1.56f, .24f), exit);
            Cube("Exit signal", new PilotBox(0, 1.63f, 38.9f, .56f, .32f, .29f), exit);
            var ridge = new System.Random(227);
            for (int i = 0; i < 24; i++)
            {
                float z = -56f + i * 4.8f, y = 4 + (float)ridge.NextDouble() * 8;
                Cube("Far ridge", new PilotBox(i % 2 == 0 ? 48f : -48f, y - 2, z, 13, 2 * y, 14), rock);
            }
            for (int i = 0; i < avatars.Length; i++)
            {
                var node = new GameObject("Pilot actor " + i);
                node.transform.SetParent(arena.transform, false);
                node.SetActive(false);
                var op = OperatorVisual.Create(node.transform, (OperatorStyle)(i % 2), i < 2 ? 0 : 1);
                var arm = WeaponVisual.Create(op.WeaponMount, false, WeaponForm.Rifle);
                var voice = node.AddComponent<AudioSource>();
                voice.playOnAwake = false;
                voice.spatialBlend = 1f;
                voice.minDistance = 4f;
                voice.maxDistance = 45f;
                avatars[i] = new Avatar { Node = node, Body = op, Weapon = arm, Voice = voice };
            }
            hands = WeaponVisual.Create(eye.transform, true, WeaponForm.Rifle);
        }

        Material Paint(Color color)
        {
            var m = new Material(source);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", .13f);
            materials.Add(m);
            return m;
        }

        MeshRenderer Cube(string name, PilotBox box, Material material)
        {
            var node = GameObject.CreatePrimitive(PrimitiveType.Cube);
            node.name = name;
            node.transform.SetParent(arena.transform, false);
            node.transform.SetPositionAndRotation(ToUnity(box.Center), Quaternion.identity);
            node.transform.localScale = ToUnity(box.Size);
            node.GetComponent<Collider>().enabled = false;
            var renderer = node.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        static Vector3 ToUnity(Point point) => new Vector3(point.X, point.Y, point.Z);

        public void Clear()
        {
            if (hands != null) { hands.gameObject.SetActive(false); Object.Destroy(hands.gameObject); hands = null; }
            if (arena != null) { arena.SetActive(false); Object.Destroy(arena); arena = null; }
            foreach (var material in materials) Object.Destroy(material);
            materials.Clear();
            viewStarted = reloading = false;
            eye.fieldOfView = foreground.fieldOfView = WeaponTuning.DefaultViewFov;
            eye.backgroundColor = new Color32(31, 41, 48, 255);
        }

        public void Dispose()
        {
            Clear();
            Object.Destroy(shot);
            cameraNode.SetActive(false);
            Object.Destroy(cameraNode);
        }
    }
}
#endif
