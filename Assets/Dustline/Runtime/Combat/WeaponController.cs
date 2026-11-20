using UnityEngine;

namespace Dustline
{
    public sealed class WeaponController : MonoBehaviour
    {
        public Magazine Magazine { get; private set; }
        public WeaponDefinition Definition { get; private set; }
        public bool IsMelee => Definition != null && Definition.Melee;
        public int Reserve => Magazine == null ? 0 : session.Loadout.Count(Definition.AmmoId);
        public bool Automatic { get; private set; }
        GameSession session;
        PlayerController player;
        string equippedId;
        ItemStack equipped;
        float nextShot, contactAt = -1;
        bool emptyClicked;

        public void Initialize(GameSession owner, PlayerController actor)
        {
            session = owner;
            player = actor;
            player.WeaponView.SetEquipped(false);
            SyncEquipment();
        }

        public void SyncEquipment()
        {
            var item = session.Loadout.Equipped(EquipmentSlot.Weapon);
            if (item?.InstanceId == equippedId) return;
            session.Effects.CancelReload();
            equipped = item;
            equippedId = item?.InstanceId;
            Definition = item == null ? null : session.Catalog.Find(item.ItemId).Weapon;
            Magazine = Definition == null || IsMelee ? null : new Magazine(Definition.MagazineSize, Mathf.Max(0, item.LoadedAmmo));
            Automatic = Magazine != null && Definition.Automatic;
            nextShot = Time.time + 0.2f;
            contactAt = -1;
            emptyClicked = false;
            player.SetWeaponView(Definition);
        }

        public void ToggleFireMode() { if (Magazine != null) Automatic = !Automatic; }

        void Update()
        {
            if (session.Phase != SessionPhase.Raid) return;
            SyncEquipment();
            if (IsMelee) { UpdateMelee(); return; }
            if (Magazine == null) return;
            int loaded = Magazine.Step(Time.deltaTime, Reserve);
            if (loaded > 0)
            {
                session.Loadout.Consume(Definition.AmmoId, loaded);
                equipped.LoadedAmmo = Magazine.Rounds;
                session.Loadout.Touch();
                session.UI.RefreshInventory();
            }
            var input = session.Input;
            if (!input.Gameplay || !input.Fire) emptyClicked = false;
            if (!input.Gameplay) return;
            var visual = player.WeaponView;
            if (input.ReloadPressed && Magazine.BeginReload(Reserve, Definition.ReloadSeconds))
            {
                emptyClicked = false;
                visual.BeginReload(Definition.ReloadSeconds);
                session.Effects.Reload(visual.Muzzle.position, Definition.ReloadSeconds);
            }
            if (UnityEngine.InputSystem.Keyboard.current?.bKey.wasPressedThisFrame ?? false) ToggleFireMode();
            bool trigger = Automatic ? input.Fire : input.FirePressed;
            if (!trigger || Time.time < nextShot) return;
            if (!Magazine.Fire())
            {
                if (!Magazine.Reloading && !emptyClicked)
                {
                    emptyClicked = true;
                    session.Effects.DryFire(visual.Muzzle.position);
                }
                return;
            }
            nextShot = Time.time + Definition.FireInterval;
            equipped.LoadedAmmo = Magazine.Rounds;
            session.Loadout.Touch();
            Vector3 start = player.View.transform.position;
            Vector3 direction = player.View.transform.forward;
            float spread = session.Input.Aim ? 0.002f : 0.012f;
            direction = (direction + player.View.transform.right * Random.Range(-spread, spread) + player.View.transform.up * Random.Range(-spread, spread)).normalized;
            Vector3 end = start + direction * Definition.Range;
            bool hitSomething = Physics.Raycast(start, direction, out var hit, Definition.Range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Health target = null;
            if (hitSomething)
            {
                end = hit.point;
                target = hit.collider.GetComponentInParent<Health>();
                if (target != null)
                {
                    target.Damage(Definition.Damage, player.Health);
                    session.UI.HitMarker();
                }
            }
            CombatNoise.Emit(transform.position, 65, player.Health);
            player.Recoil(Definition.Recoil * (session.Input.Aim ? 0.65f : 1));
            visual.Kick();
            session.Effects.Shot(visual.Muzzle, end, hitSomething ? hit.normal : Vector3.zero, hitSomething, target != null);
        }

        void UpdateMelee()
        {
            if (!session.Input.Gameplay)
            {
                contactAt = -1;
                return;
            }
            if (contactAt >= 0 && Time.time >= contactAt)
            {
                contactAt = -1;
                var view = player.View.transform;
                bool hitSomething = Physics.SphereCast(view.position, 0.20f, view.forward, out var hit, Definition.Range,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                var target = hitSomething ? hit.collider.GetComponentInParent<Health>() : null;
                if (target != null && target.Alive)
                {
                    target.Damage(Definition.Damage, player.Health);
                    session.UI.HitMarker();
                }
                session.EnvironmentAudio.PlayMelee(hitSomething ? hit.point : player.WeaponView.Muzzle.position,
                    hitSomething && target == null ? MeleeSound.Solid : target != null ? MeleeSound.Contact : MeleeSound.Swing);
            }
            if (!session.Input.FirePressed || Time.time < nextShot) return;
            nextShot = Time.time + Definition.FireInterval;
            contactAt = Time.time + Definition.FireInterval * 0.37f;
            player.WeaponView.Swing(Definition.FireInterval);
        }
    }
}
