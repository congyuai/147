using System.Collections.Generic;
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
        public EquipmentSlot ActiveSlot { get; private set; }
        public int Reserve => Magazine == null ? 0 : AvailableReserve(activeBag, SelectReloadType());
        public bool HasInfiniteReserve => InPractice && ReadyAmmoType != null;
        public WeaponStats Stats => stats;
        public float MoveFactor => IsMelee ? 1.13f : Mathf.Clamp(Definition != null ? Definition.MoveFactor : 1f, .65f, 1.2f);
        public string ReadyAmmoType => Magazine == null ? null : SelectReloadType();
        public bool Automatic => Magazine != null && mainAutomatic;
        public float AimFov => Magazine != null ? stats.AimFov : WeaponTuning.DefaultViewFov;
        public float AimSeconds => Magazine != null ? stats.AimSeconds : 0.18f;

        GameSession session;
        PlayerController player;
        GridInventory activeBag;
        readonly Dictionary<string, bool> fireModes = new Dictionary<string, bool>(System.StringComparer.Ordinal);
        readonly RaycastHit[] shotHits = new RaycastHit[9];
        bool InPractice => session != null && session.Phase == SessionPhase.Practice;
        string activeId, activeItemId;
        WeaponMods appliedMods;
        WeaponParts appliedParts;
        string appliedAmmo, reloadAmmo;
        WeaponStats stats;
        bool mainAutomatic, emptyClicked, queuedSwing;
        float nextAttack, contactAt = -1, chainUntil = -1;
        int comboStep, contactStep;

        public void Initialize(GameSession owner, PlayerController actor)
        {
            session = owner;
            player = actor;
            player.Health.Hurt += OnHurt;
            SyncEquipment();
        }

        public void SyncEquipment() => ResolveEquipment(ActiveSlot, Time.time);

        public bool SelectSlot(EquipmentSlot slot)
        {
            if (session == null || !session.InField || !session.Input.Gameplay || !player.Health.Alive || !Available(slot, session.ActiveFieldBag)) return false;
            ResolveEquipment(slot, Time.time);
            return true;
        }

        public bool SelectPracticeAmmo(string itemId)
        {
            if (!InPractice || Magazine == null || !player.Health.Alive) return false;
            var ammo = session.Catalog.Find(itemId);
            if (!WeaponRules.CanLoad(Definition, ammo, out _)) return false;
            var item = FindActiveItem();
            if (item == null) return false;
            if (WeaponRules.LoadedType(Definition, item) == itemId) return true;
            if (Magazine.Reloading) CancelReload(Magazine.Rounds);
            // The practice copy has one selected ammo type, even with a partially filled magazine.
            appliedAmmo = item.LoadedAmmoId = itemId;
            activeBag.Touch();
            return true;
        }

        internal void SyncHandForMovement(float now) => ReadSwitch(now);

        void ReadSwitch(float now)
        {
            var bag = session.ActiveFieldBag;
            var requested = session.Input.TakeWeaponSlot();
            bool cycle = session.Input.TakeWeaponSwap();
            if (requested != EquipmentSlot.None)
            {
                if (Available(requested, bag)) ResolveEquipment(requested, now);
            }
            else if (cycle)
            {
                int start = System.Array.IndexOf(LoadoutSlots.Hands, ActiveSlot);
                for (int offset = 1; offset <= LoadoutSlots.Hands.Length; offset++)
                {
                    var slot = LoadoutSlots.Hands[(start + offset) % LoadoutSlots.Hands.Length];
                    if (!Available(slot, bag)) continue;
                    ResolveEquipment(slot, now);
                    break;
                }
            }
        }

        bool Available(EquipmentSlot slot, GridInventory bag) => LoadoutSlots.IsHand(slot)
            && (slot == EquipmentSlot.Melee || session.Catalog.Find(bag?.Equipped(slot)?.ItemId)?.Weapon != null);

        void ResolveEquipment(EquipmentSlot slot, float now)
        {
            var bag = session.ActiveFieldBag;
            if (!Available(slot, bag))
            {
                foreach (var hand in LoadoutSlots.Hands)
                    if (Available(hand, bag)) { slot = hand; break; }
            }
            var item = bag?.Equipped(slot);
            string itemId = item?.ItemId;
            bool useBound = slot == EquipmentSlot.Melee && item == null;
            var definition = useBound ? session.BoundBlade : session.Catalog.Find(itemId)?.Weapon;
            if (definition == null) return;
            string id = item?.InstanceId;
            var mods = item?.Mods ?? default;
            var parts = item?.Parts ?? default;
            int capacity = WeaponRules.Capacity(definition, item, session.Catalog);
            string ammo = WeaponRules.LoadedType(definition, item);
            bool changed = Definition != definition || ActiveSlot != slot || ActiveIsBound != useBound || activeId != id
                || activeItemId != (useBound ? null : itemId) || !ReferenceEquals(bag, activeBag)
                || capacity != (Magazine?.Capacity ?? 0);
            if (changed)
            {
                player.CancelTreatment();
                CancelActions();
                if (player.WeaponView != null) session.Effects?.CancelMuzzle(player.WeaponView.Muzzle);
                session.Input.SetAimAvailable(false);
                if (!ReferenceEquals(bag, activeBag)) fireModes.Clear();
                activeBag = bag;
                activeId = id;
                activeItemId = itemId;
                Definition = definition;
                ActiveSlot = slot;
                ActiveIsBound = useBound;
                mainAutomatic = !definition.Melee && definition.Automatic && (!fireModes.TryGetValue(id, out bool mode) || mode);
                Magazine = useBound || definition.Melee ? null : new Magazine(capacity, item.LoadedAmmo);
                nextAttack = now + 0.18f;
                emptyClicked = false;
                player.SetWeaponView(definition);
                session.Input.SetAimAvailable(Magazine != null);
            }
            else if (Magazine != null && (Magazine.Rounds != Mathf.Clamp(item.LoadedAmmo, 0, Magazine.Capacity) || appliedAmmo != ammo))
            {
                // Restore/transactions can replace both the object and its loaded count.
                CancelReload(Mathf.Max(0, item.LoadedAmmo));
            }

            appliedAmmo = ammo;
            if (changed || mods.Sight != appliedMods.Sight || mods.Grip != appliedMods.Grip || mods.Stock != appliedMods.Stock
                || !WeaponRules.SameParts(parts, appliedParts))
            {
                appliedMods = mods;
                appliedParts = parts;
                stats = WeaponTuning.Evaluate(definition, useBound ? null : item, session.Catalog);
                player.WeaponView.SetConfiguration(useBound ? null : item, session.Catalog);
            }
        }

        public void ToggleFireMode()
        {
            if (Magazine != null && Definition.Automatic) fireModes[activeId] = mainAutomatic = !mainAutomatic;
        }

        void Update() => Tick(Time.time, Time.deltaTime);

        void Tick(float now, float delta)
        {
            if (session == null) return;
            var input = session.Input;
            if (!session.InField || !player.Health.Alive)
            {
                CancelActions();
                return;
            }

            // Switching or inventory replacement must run before completing a pending action.
            ReadSwitch(now);
            ResolveEquipment(ActiveSlot, now);
            if (!input.Gameplay)
            {
                CancelActions();
                emptyClicked = false;
                return;
            }
            bool treating = player.WeaponView.MedicineBusy;
            session.Input.SetAimAvailable(!treating && Magazine != null && !Magazine.Reloading);
            if (treating) return;
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
            if (input.ReloadPressed)
            {
                if (TryBeginReload()) return;
                session.UI?.Notify(Magazine.Rounds == Magazine.Capacity ? "弹匣已满。"
                    : Magazine.Rounds > 0 ? "没有同种备弹；局内不能混装弹种。" : "没有可用于当前武器的兼容备弹。");
            }
            if (now < nextAttack) return;
            if (Magazine.Rounds == 0 && TryBeginReload()) return;
            if (Automatic ? input.Fire : input.FirePressed) Fire(now, input.Aim);
        }

        internal void BeginTreatment()
        {
            CancelActions();
            session.Effects?.CancelMuzzle(player.WeaponView.Muzzle);
            session.Input.SetAimAvailable(false);
        }

        bool TryBeginReload()
        {
            string ammo = SelectReloadType();
            float duration = stats.ReloadSeconds * (player.Health.Body?.Handling ?? 1f);
            if (!Magazine.BeginReload(AvailableReserve(activeBag, ammo), duration)) return false;
            reloadAmmo = ammo;
            emptyClicked = false;
            session.Input.SetAimAvailable(false);
            var visual = player.WeaponView;
            visual.BeginReload(Magazine);
            session.Effects?.Reload(visual.Muzzle.position, duration);
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
            string ammoId = reloadAmmo;
            int previous = item.LoadedAmmo;
            string previousType = item.LoadedAmmoId;
            int loaded = mag.Step(delta, AvailableReserve(bag, ammoId));
            if (mag.Reloading) return;
            session.Input.SetAimAvailable(true);
            player.WeaponView.EndReload();
            if (loaded == 0) return;

            // Consume notifies Changed synchronously; observers must see the new loaded count alongside the new reserve.
            item.LoadedAmmo = mag.Rounds;
            appliedAmmo = item.LoadedAmmoId = ammoId;
            if (InPractice) bag.Touch();
            else if (!bag.Consume(ammoId, loaded))
            {
                item.LoadedAmmo = previous;
                item.LoadedAmmoId = previousType;
                appliedAmmo = WeaponRules.LoadedType(Definition, item);
                CancelReload(previous);
                return;
            }
            session.UI?.RefreshInventory();
        }

        ItemStack FindActiveItem()
        {
            if (ActiveIsBound || !ReferenceEquals(session.ActiveFieldBag, activeBag)) return null;
            var item = activeBag?.Equipped(ActiveSlot);
            return item != null && item.InstanceId == activeId && item.ItemId == activeItemId ? item : null;
        }

        internal int ReserveFor(ItemStack item)
        {
            if (item == null) return 0;
            if (item.InstanceId == activeId && item.Slot == ActiveSlot) return Reserve;
            var bag = session.ActiveFieldBag;
            var definition = session.Catalog.Find(item.ItemId)?.Weapon;
            return AvailableReserve(bag, ReloadType(bag, definition, item));
        }

        string SelectReloadType()
        {
            if (Magazine == null || activeBag == null) return null;
            return Magazine.Reloading ? reloadAmmo : ReloadType(activeBag, Definition, FindActiveItem());
        }

        string ReloadType(GridInventory bag, WeaponDefinition definition, ItemStack item)
        {
            if (bag == null || definition == null || definition.Melee || item == null) return null;
            string prior = WeaponRules.LoadedType(definition, item);
            if (WeaponRules.CanLoad(definition, item, session.Catalog.Find(prior), out _)
                && (item.LoadedAmmo > 0 || AvailableReserve(bag, prior) > 0)) return prior;
            if (InPractice)
            {
                foreach (var ammo in session.Catalog.Items)
                    if (WeaponRules.CanLoad(definition, item, ammo, out _)) return ammo.Id;
                return null;
            }
            foreach (var stack in bag.Items)
                if (GridInventory.FieldSupply(stack) && WeaponRules.CanLoad(definition, item, session.Catalog.Find(stack.ItemId), out _)) return stack.ItemId;
            return null;
        }

        int AvailableReserve(GridInventory bag, string ammoId)
        {
            if (bag == null || ammoId == null) return 0;
            if (InPractice) return int.MaxValue;
            int count = 0;
            var items = bag.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (GridInventory.FieldSupply(item) && item.ItemId == ammoId) count += item.Quantity;
            }
            return count;
        }

        void Fire(float now, bool aiming)
        {
            var visual = player.WeaponView;
            var item = FindActiveItem();
            if (item == null) { SyncEquipment(); return; }
            var view = player.View.transform;
            Vector3 start = view.position;
            if (Physics.CheckSphere(start, 0.024f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return;
            if (!Magazine.Fire())
            {
                if (!emptyClicked)
                {
                    emptyClicked = true;
                    session.Effects?.DryFire(visual.Muzzle.position);
                }
                return;
            }

            var practice = session.Practice;
            practice?.RecordShot();
            nextAttack = now + stats.FireInterval;
            item.LoadedAmmo = Magazine.Rounds;
            var bag = activeBag;
            int penetration = WeaponRules.AmmoPenetration(session.Catalog.Find(WeaponRules.LoadedType(Definition, item)));
            float handling = player.Health.Body?.Handling ?? 1f;
            float spread = stats.Spread(aiming) * handling;
            int rays = Mathf.Clamp(Definition.Rays, 1, shotHits.Length);
            Vector3 direction = view.forward;
            // Resolve the whole volley before a hit can remove a dummy or other collider.
            for (int i = 0; i < rays; i++)
            {
                Vector2 scatter = Random.insideUnitCircle * spread;
                Vector3 ray = (view.forward + view.right * scatter.x + view.up * scatter.y).normalized;
                Physics.Raycast(start, ray, out shotHits[i], stats.Range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                practice?.RecordRay(Mathf.Atan(scatter.magnitude) * Mathf.Rad2Deg);
                if (i == 0) direction = ray;
            }
            var hit = shotHits[0];
            Vector3 end = hit.collider != null ? hit.point : start + direction * stats.Range;
            bool traceFromMuzzle = visual.AlignShot(direction, end);
            bool struckHealth = false, hitDummy = false;
            for (int i = 0; i < rays; i++)
            {
                var impact = shotHits[i];
                var target = impact.collider != null ? impact.collider.GetComponentInParent<Health>(true) : null;
                if (i == 0) struckHealth = target != null;
                float lost = DealDamage(target, stats.Damage, impact.point, penetration);
                if (practice != null && practice.RecordHit(target, impact.point, lost)) hitDummy = true;
            }
            if (hitDummy) practice.RecordHitShot();
            CombatNoise.Emit(transform.position, 65, player.Health);
            float recoil = stats.Recoil * handling * (aiming ? 0.65f : 1f);
            player.Recoil(recoil);
            visual.Kick(recoil);
            if (session.Effects != null)
            {
                Vector3 muzzle = visual.Muzzle.position;
                const int mask = Physics.DefaultRaycastLayers;
                const QueryTriggerInteraction triggers = QueryTriggerInteraction.Ignore;
                bool clearTracer = traceFromMuzzle && !Physics.Linecast(start, muzzle, mask, triggers)
                    && !Physics.Linecast(muzzle, end - visual.Muzzle.forward * 0.025f, mask, triggers);
                session.Effects.Shot(visual.Muzzle, end, in hit, struckHealth, clearTracer);
            }
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
            if (Physics.CheckSphere(view.position, 0.024f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return;
            float radius = step == 2 ? 0.13f : Definition.Form == WeaponForm.ShortBlade ? 0.17f : 0.22f;
            if (!Physics.SphereCast(view.position, radius, view.forward, out var hit, stats.Range,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && !Physics.Raycast(view.position, view.forward, out hit, stats.Range,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return;
            Vector3 toContact = hit.point - view.position;
            if (toContact.sqrMagnitude > 0.0001f && Physics.Linecast(view.position, hit.point + 0.012f * toContact.normalized,
                out var blocker, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) hit = blocker;
            var target = hit.collider.GetComponentInParent<Health>();
            DealDamage(target, stats.Damage, hit.point);
            session.EnvironmentAudio?.PlayMelee(hit.point, target != null ? MeleeSound.Contact : MeleeSound.Solid);
            Vector3 swipe = step == 0 ? -view.right - view.up * 0.60f : step == 1 ? view.right + view.up * 0.38f : -view.up;
            session.Effects?.MeleeImpact(in hit, target != null, Definition.Form, swipe);
        }

        float DealDamage(Health target, float amount, Vector3 point, int penetration = -1)
        {
            if (target == null || !target.Alive) return 0f;
            float before = target.Current;
            target.DamageAt(amount, player.Health, point, penetration);
            float lost = Mathf.Max(0f, before - target.Current);
            if (lost > 0f) session.UI?.HitMarker();
            return lost;
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

        internal void CancelActions()
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
