using UnityEngine;
using UnityEngine.InputSystem;

namespace Dustline
{
    public sealed class WeaponController : MonoBehaviour
    {
        public Magazine Magazine { get; private set; }
        public WeaponDefinition Definition { get; private set; }
        public bool IsMelee => Definition != null && Definition.Melee;
        public bool ActiveIsBound { get; private set; }
        public int Reserve => Magazine == null ? 0 : CountPocketAmmo(session.Loadout, Definition.AmmoId);
        public bool Automatic => Magazine != null && mainAutomatic;
        public float AimFov => Magazine != null ? stats.AimFov : 75f;
        public float AimSeconds => Magazine != null ? stats.AimSeconds : 0.18f;

        GameSession session;
        PlayerController player;
        GridInventory activeBag;
        WeaponDefinition mainDefinition;
        string activeId, activeItemId, mainId;
        WeaponMods appliedMods;
        WeaponStats stats;
        bool preferBound, mainAutomatic, emptyClicked, queuedSwing;
        float nextAttack, contactAt = -1, chainUntil = -1;
        int comboStep, contactStep;

        public void Initialize(GameSession owner, PlayerController actor)
        {
            session = owner;
            player = actor;
            player.Health.Hurt += OnHurt;
            SyncEquipment();
        }

        public void SyncEquipment() => ResolveEquipment(false, Time.time);

        void ResolveEquipment(bool toggle, float now)
        {
            var bag = session.Loadout;
            var item = bag.Equipped(EquipmentSlot.Weapon);
            string itemId = item?.ItemId;
            var main = item == null ? null : session.Catalog.Find(itemId)?.Weapon;
            if (mainId != item?.InstanceId || mainDefinition != main || !ReferenceEquals(bag, activeBag))
            {
                mainId = item?.InstanceId;
                mainDefinition = main;
                mainAutomatic = main != null && main.Automatic;
            }
            if (main == null) preferBound = false;
            else if (toggle) preferBound = !preferBound;
            bool useBound = main == null || preferBound;
            var definition = useBound ? session.BoundBlade : main;
            string id = useBound ? null : item.InstanceId;
            var mods = useBound ? default : item.Mods;
            bool changed = Definition != definition || ActiveIsBound != useBound || activeId != id
                || activeItemId != (useBound ? null : itemId) || !ReferenceEquals(bag, activeBag);
            if (changed)
            {
                CancelActions();
                if (player.WeaponView != null) session.Effects?.CancelMuzzle(player.WeaponView.Muzzle);
                session.Input.SetAimAvailable(false);
                activeBag = bag;
                activeId = id;
                activeItemId = useBound ? null : itemId;
                Definition = definition;
                ActiveIsBound = useBound;
                Magazine = useBound || definition.Melee ? null : new Magazine(definition.MagazineSize, item.LoadedAmmo);
                nextAttack = now + 0.18f;
                emptyClicked = false;
                player.SetWeaponView(definition);
                session.Input.SetAimAvailable(Magazine != null);
            }
            else if (Magazine != null && Magazine.Rounds != Mathf.Clamp(item.LoadedAmmo, 0, Magazine.Capacity))
            {
                // Restore/transactions can replace both the object and its loaded count.
                CancelReload(Mathf.Max(0, item.LoadedAmmo));
            }

            if (changed || mods.Sight != appliedMods.Sight || mods.Grip != appliedMods.Grip || mods.Stock != appliedMods.Stock)
            {
                appliedMods = mods;
                stats = WeaponTuning.Evaluate(definition, mods);
                player.WeaponView.SetSightLevel(Magazine != null ? mods.Sight : 0);
            }
        }

        public void ToggleFireMode()
        {
            if (Magazine != null && Definition.Automatic) mainAutomatic = !mainAutomatic;
        }

        void Update() => Tick(Time.time, Time.deltaTime);

        void Tick(float now, float delta)
        {
            if (session == null) return;
            var input = session.Input;
            if (session.Phase != SessionPhase.Raid || !player.Health.Alive)
            {
                CancelActions();
                return;
            }

            // Switching or inventory replacement must run before completing a pending action.
            ResolveEquipment(input.Gameplay && input.SwitchWeaponPressed, now);
            if (!input.Gameplay)
            {
                CancelActions();
                emptyClicked = false;
                return;
            }
            if (IsMelee)
            {
                UpdateMelee(now, input.FirePressed);
                return;
            }
            if (Magazine == null) return;
            if (!input.Fire) emptyClicked = false;
            if (Keyboard.current?.bKey.wasPressedThisFrame ?? false) ToggleFireMode();
            if (Magazine.Reloading)
            {
                CompleteReload(delta);
                return;
            }
            if (input.ReloadPressed && TryBeginReload()) return;
            if (now < nextAttack) return;
            if (Magazine.Rounds == 0 && TryBeginReload()) return;
            if (!player.WeaponView.RifleObstructed && (Automatic ? input.Fire : input.FirePressed)) Fire(now, input.Aim);
        }

        bool TryBeginReload()
        {
            if (!Magazine.BeginReload(Reserve, stats.ReloadSeconds)) return false;
            emptyClicked = false;
            session.Input.SetAimAvailable(false);
            var visual = player.WeaponView;
            visual.BeginReload(stats.ReloadSeconds);
            session.Effects?.Reload(visual.Muzzle.position, stats.ReloadSeconds);
            return true;
        }

        void CompleteReload(float delta)
        {
            var mag = Magazine;
            if (delta < mag.Remaining)
            {
                mag.Step(delta, 0);
                return;
            }
            var bag = activeBag;
            var item = FindActiveItem();
            if (item == null) { SyncEquipment(); return; }
            string ammoId = Definition.AmmoId;
            int previous = item.LoadedAmmo;
            int loaded = mag.Step(delta, CountPocketAmmo(bag, ammoId));
            if (mag.Reloading) return;
            session.Input.SetAimAvailable(true);
            if (loaded == 0) return;

            // Consume notifies Changed synchronously; observers must see the new loaded count alongside the new reserve.
            item.LoadedAmmo = mag.Rounds;
            if (!bag.Consume(ammoId, loaded))
            {
                item.LoadedAmmo = previous;
                CancelReload(previous);
                return;
            }
            session.UI?.RefreshInventory();
        }

        ItemStack FindActiveItem()
        {
            if (ActiveIsBound || !ReferenceEquals(session.Loadout, activeBag)) return null;
            var item = activeBag.Equipped(EquipmentSlot.Weapon);
            return item != null && item.InstanceId == activeId && item.ItemId == activeItemId ? item : null;
        }

        static int CountPocketAmmo(GridInventory bag, string ammoId)
        {
            int count = 0;
            var items = bag.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Slot == EquipmentSlot.None && item.ItemId == ammoId) count += item.Quantity;
            }
            return count;
        }

        void Fire(float now, bool aiming)
        {
            var visual = player.WeaponView;
            var item = FindActiveItem();
            if (item == null) { SyncEquipment(); return; }
            if (!Magazine.Fire())
            {
                if (!emptyClicked)
                {
                    emptyClicked = true;
                    session.Effects?.DryFire(visual.Muzzle.position);
                }
                return;
            }

            nextAttack = now + stats.FireInterval;
            item.LoadedAmmo = Magazine.Rounds;
            var bag = activeBag;
            var view = player.View.transform;
            Vector3 start = view.position;
            float spread = aiming ? stats.AimSpread : stats.HipSpread;
            Vector2 offset = Random.insideUnitCircle * spread;
            Vector3 direction = (view.forward + view.right * offset.x + view.up * offset.y).normalized;
            bool struck = Physics.Raycast(start, direction, out var hit, stats.Range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Vector3 end = struck ? hit.point : start + direction * stats.Range;
            var target = struck ? hit.collider.GetComponentInParent<Health>() : null;
            DealDamage(target);
            CombatNoise.Emit(transform.position, 65, player.Health);
            float recoil = stats.Recoil * (aiming ? 0.65f : 1f);
            player.Recoil(recoil);
            visual.Kick(recoil);
            session.Effects?.Shot(visual.Muzzle, end, in hit, target != null);
            bag.Touch();
        }

        void UpdateMelee(float now, bool pressed)
        {
            if (contactAt >= 0 && now >= contactAt)
            {
                contactAt = -1;
                MeleeContact(contactStep);
            }
            if (now < nextAttack)
            {
                if (pressed && chainUntil > nextAttack && nextAttack - now <= 0.24f) queuedSwing = true;
                return;
            }
            if (!pressed && !queuedSwing) return;
            if (now > chainUntil)
            {
                comboStep = 0;
                if (!pressed) { queuedSwing = false; return; }
            }
            queuedSwing = false;
            contactStep = comboStep;
            comboStep = (comboStep + 1) % 3;
            nextAttack = now + stats.FireInterval;
            chainUntil = nextAttack + 0.55f;
            contactAt = now + stats.FireInterval * WeaponVisual.ContactPhase(contactStep);
            player.WeaponView.Swing(stats.FireInterval, contactStep);
            session.EnvironmentAudio?.PlayMelee(player.WeaponView.Muzzle.position, MeleeSound.Swing);
        }

        void MeleeContact(int step)
        {
            var view = player.View.transform;
            float radius = step == 2 ? 0.13f : Definition.Form == WeaponForm.ShortBlade ? 0.17f : 0.22f;
            if (!Physics.SphereCast(view.position, radius, view.forward, out var hit, stats.Range,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && !Physics.Raycast(view.position, view.forward, out hit, stats.Range,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return;
            Vector3 toContact = hit.point - view.position;
            if (toContact.sqrMagnitude > 0.0001f && Physics.Linecast(view.position, hit.point + 0.012f * toContact.normalized,
                out var blocker, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) hit = blocker;
            var target = hit.collider.GetComponentInParent<Health>();
            DealDamage(target);
            session.EnvironmentAudio?.PlayMelee(hit.point, target != null ? MeleeSound.Contact : MeleeSound.Solid);
            Vector3 swipe = step == 0 ? -view.right - view.up * 0.60f : step == 1 ? view.right + view.up * 0.38f : -view.up;
            session.Effects?.MeleeImpact(in hit, target != null, Definition.Form, swipe);
        }

        void DealDamage(Health target)
        {
            if (target == null || !target.Alive) return;
            float before = target.Current;
            target.Damage(stats.Damage, player.Health);
            if (target.Current < before) session.UI?.HitMarker();
        }

        void OnHurt(Health attacker)
        {
            if (contactAt >= 0) CancelSwing();
        }

        void CancelSwing()
        {
            contactAt = chainUntil = -1;
            comboStep = 0;
            queuedSwing = false;
            player.WeaponView?.CancelSwing();
        }

        void CancelReload(int rounds)
        {
            // Close the timer with zero reserve; cancellation cannot transfer ammo.
            if (Magazine.Reloading) Magazine.Step(Magazine.Remaining, 0);
            rounds = Mathf.Clamp(rounds, 0, Magazine.Capacity);
            if (Magazine.Rounds != rounds) Magazine = new Magazine(Magazine.Capacity, rounds);
            session.Effects?.CancelReload();
            player.WeaponView?.CancelReload();
            session.Input.SetAimAvailable(true);
        }

        void CancelActions()
        {
            if (Magazine != null && Magazine.Reloading) CancelReload(Magazine.Rounds);
            if (chainUntil >= 0) CancelSwing();
        }

        void OnDisable()
        {
            if (player != null) CancelActions();
        }

        void OnDestroy()
        {
            if (player != null && player.Health != null) player.Health.Hurt -= OnHurt;
        }
    }
}
