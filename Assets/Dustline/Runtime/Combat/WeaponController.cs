using UnityEngine;

namespace Dustline
{
    public sealed class WeaponController : MonoBehaviour
    {
        public Magazine Magazine { get; private set; }
        public WeaponDefinition Definition { get; private set; }
        public int Reserve => Definition == null ? 0 : session.Loadout.Count(Definition.AmmoId);
        public bool Automatic { get; private set; }
        GameSession session;
        PlayerController player;
        string equippedId;
        ItemStack equipped;
        float nextShot;
        AudioSource audioSource;
        AudioClip shotClip;

        public void Initialize(GameSession owner, PlayerController actor)
        {
            session = owner;
            player = actor;
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            shotClip = AudioClip.Create("Synthetic shot", 2205, 1, 22050, false);
            var samples = new float[2205];
            var random = new System.Random(45);
            for (int i = 0; i < samples.Length; i++) samples[i] = (float)(random.NextDouble() * 2 - 1) * Mathf.Exp(-i / 330f) * 0.3f;
            shotClip.SetData(samples, 0);
            SyncEquipment();
        }

        public void SyncEquipment()
        {
            var item = session.Loadout.Equipped(EquipmentSlot.Weapon);
            if (item?.InstanceId == equippedId) return;
            equipped = item;
            equippedId = item?.InstanceId;
            Definition = item == null ? null : session.Catalog.Find(item.ItemId).Weapon;
            Magazine = Definition == null ? null : new Magazine(Definition.MagazineSize, Mathf.Max(0, item.LoadedAmmo));
            Automatic = Definition != null && Definition.Automatic;
            nextShot = Time.time + 0.2f;
        }

        public void ToggleFireMode() { Automatic = !Automatic; }

        void Update()
        {
            if (session.Phase != SessionPhase.Raid) return;
            SyncEquipment();
            if (Magazine == null) return;
            int loaded = Magazine.Step(Time.deltaTime, Reserve);
            if (loaded > 0)
            {
                session.Loadout.Consume(Definition.AmmoId, loaded);
                equipped.LoadedAmmo = Magazine.Rounds;
                session.Loadout.Touch();
                session.UI.RefreshInventory();
            }
            if (!session.Input.Gameplay) return;
            if (session.Input.ReloadPressed) Magazine.BeginReload(Reserve, Definition.ReloadSeconds);
            if (UnityEngine.InputSystem.Keyboard.current?.bKey.wasPressedThisFrame ?? false) ToggleFireMode();
            bool trigger = Automatic ? session.Input.Fire : session.Input.FirePressed;
            if (!trigger || Time.time < nextShot || !Magazine.Fire()) return;
            nextShot = Time.time + Definition.FireInterval;
            equipped.LoadedAmmo = Magazine.Rounds;
            session.Loadout.Touch();
            Vector3 start = player.View.transform.position;
            Vector3 direction = player.View.transform.forward;
            float spread = session.Input.Aim ? 0.002f : 0.012f;
            direction = (direction + player.View.transform.right * Random.Range(-spread, spread) + player.View.transform.up * Random.Range(-spread, spread)).normalized;
            Vector3 end = start + direction * Definition.Range;
            if (Physics.Raycast(start, direction, out var hit, Definition.Range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                end = hit.point;
                var target = hit.collider.GetComponentInParent<Health>();
                if (target != null)
                {
                    target.Damage(Definition.Damage, player.Health);
                    session.UI.HitMarker();
                }
            }
            session.World.Trace(start + direction * 0.5f, end, new Color(1, 0.75f, 0.25f));
            CombatNoise.Emit(transform.position, 65, player.Health);
            player.Recoil(Definition.Recoil * (session.Input.Aim ? 0.65f : 1));
            audioSource.PlayOneShot(shotClip);
        }
        void OnDestroy() { if (shotClip != null) Destroy(shotClip); }
    }
}
