using UnityEngine;

namespace Dustline
{
    [DefaultExecutionOrder(-40)]
    public sealed class PlayerController : MonoBehaviour
    {
        public Camera View { get; private set; }
        internal Camera Foreground { get; private set; }
        public Health Health { get; private set; }
        public WeaponController Weapon { get; private set; }
        public WeaponVisual WeaponView { get; private set; }
        public OperatorVisual Operator { get; private set; }
        public float Stamina { get; private set; } = 100;
        public bool Crouched { get; private set; }
        public bool Prone { get; private set; }
        public bool Sprinting { get; private set; }
        public bool SprintExhausted => sprintLocked;
        public float MoveSpeed { get; private set; }
        public float Lean { get; private set; }
        public float AimProgress => WeaponView != null ? WeaponView.VisibleAim : 0f;
        public string InteractionHint { get; private set; }
        public LootContainer FocusedLoot { get; private set; }
        public bool LookingAtDoor => focusedDoor != null;
        public LootContainer SearchingLoot { get; private set; }
        public float SearchProgress => SearchingLoot == null ? 0 : Mathf.Clamp01(searchElapsed / SearchingLoot.SearchSeconds);
        public bool IsTreating => treatmentBag != null;
        public float TreatmentProgress => IsTreating ? Mathf.Clamp01(1f - treatmentLeft / treatmentDuration) : 0f;
        public ItemDefinition TreatmentItem => treatmentItem;
        public BodyPart TreatmentPart => treatmentPart;
        BodyPart treatmentPart;
        GridInventory treatmentBag;
        ItemDefinition treatmentItem;
        string treatmentInstance;
        float treatmentLeft, treatmentDuration;
        GameSession session;
        CharacterController motor;
        StepCadence steps;
        readonly Collider[] stanceHits = new Collider[16];
        float pitch, verticalSpeed, noiseAt, focusAt, searchElapsed, staminaResumeAt, firingWalkUntil;
        Vector2 recoilImpulse, recoilView;
        int recoilPattern;
        HarborDoor focusedDoor;
        bool sprintLocked;
        float currentHeight = 1.8f;

        public void Initialize(GameSession owner)
        {
            session = owner;
            gameObject.layer = 2;
            motor = gameObject.AddComponent<CharacterController>();
            motor.height = currentHeight;
            motor.radius = 0.35f;
            motor.center = Vector3.up * currentHeight * 0.5f;
            motor.stepOffset = 0.25f;
            Health = gameObject.AddComponent<Health>();
            Health.Initialize(100, 0);
            Health.EnableBody();
            Health.HitPart = ResolveBodyPart;
            Health.ProtectPart = (part, damage) => ApplyArmor(part, damage, 0);
            Health.ProtectAmmoPart = ApplyArmor;
            Health.Damaged += session.UI.ShowDamage;
            Health.Hurt += _ => CancelTreatment();
            Health.Died += _ => session.PlayerDown();
            Operator = OperatorVisual.Create(transform, session.SelectedOperator, 0, gameObject.layer);
            Operator.ShadowsOnly();

            var cameraObject = new GameObject("First person camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(transform, false);
            cameraObject.transform.localPosition = Vector3.up * 1.6f;
            View = cameraObject.GetComponent<Camera>();
            View.nearClipPlane = 0.09f;
            View.farClipPlane = 1450f;
            View.clearFlags = CameraClearFlags.Skybox;
            View.fieldOfView = 75;
            Foreground = FirstPersonView.Create(View);
            Weapon = gameObject.AddComponent<WeaponController>();
            Weapon.Initialize(session, this);
        }

        BodyPart ResolveBodyPart(Vector3 point)
        {
            return Operator.HitRegion(point);
        }

        float ApplyArmor(BodyPart part, float damage, int penetration)
        {
            var bag = session.ActiveFieldBag;
            var armor = bag.Equipped(part == BodyPart.Head ? EquipmentSlot.Helmet : EquipmentSlot.Armor);
            var rule = session.Catalog.Find(armor?.ItemId);
            float remaining = BodyCondition.Protect(part, damage, rule, armor, penetration);
            if (remaining < damage) bag.Touch();
            return remaining;
        }

        public void SetWeaponView(WeaponDefinition definition)
        {
            WeaponForm form = definition == null ? WeaponForm.Rifle : definition.Form;
            if (WeaponView == null) WeaponView = WeaponVisual.Create(View.transform, true, form);
            else WeaponView.SetForm(form);
            WeaponView.SetEquipped(definition != null);
            SetViewFov(75f);
        }

        void SetViewFov(float value)
        {
            View.fieldOfView = value;
            if (Foreground != null) Foreground.fieldOfView = value;
        }

        internal bool BeginTreatment(GridInventory bag, string instanceId, ItemDefinition item, BodyPart part)
        {
            if (IsTreating) return false;
            treatmentBag = bag;
            treatmentInstance = instanceId;
            treatmentItem = item;
            treatmentPart = part;
            treatmentLeft = treatmentDuration = Mathf.Clamp(item.UseSeconds, 0.4f, 10f);
            Weapon.BeginTreatment();
            WeaponView.BeginMedicine(item.MedicineVisual);
            return true;
        }

        internal void CancelTreatment()
        {
            if (!IsTreating) return;
            treatmentBag = null;
            treatmentItem = null;
            treatmentInstance = null;
            WeaponView.EndMedicine();
        }

        void TickTreatment(float delta)
        {
            if (!IsTreating) return;
            var stack = treatmentBag.Find(treatmentInstance);
            if (!session.Input.Gameplay || session.Input.WeaponSelectionPending || !Health.Alive
                || treatmentBag != session.ActiveFieldBag || stack == null || stack.Quantity <= 0 || stack.ItemId != treatmentItem.Id
                || !GridInventory.FieldSupply(stack) || (Health.Body != null ? !Health.Body.CanTreat(treatmentPart, treatmentItem.Treatment)
                    : Health.Current >= Health.Maximum))
            {
                CancelTreatment();
                return;
            }
            treatmentLeft -= Mathf.Max(0f, delta);
            if (treatmentLeft > 0f) return;
            var bag = treatmentBag;
            string id = treatmentInstance;
            var item = treatmentItem;
            var part = treatmentPart;
            CancelTreatment();
            if (Health.TryTreat(part, item)) bag.ConsumeInstance(id);
            session.UI?.RefreshInventory();
        }

        bool WalkForFire(float now, bool gameplay, bool firearm, bool wantsFire)
        {
            if (!gameplay || !firearm)
            {
                firingWalkUntil = 0f;
                return false;
            }
            if (wantsFire) firingWalkUntil = now + 0.17f;
            return now < firingWalkUntil;
        }

        void Update() => Tick(Time.time, Time.deltaTime);

        void Tick(float now, float frame)
        {
            if (!session.InField || !Health.Alive) return;
            Health.StepBody(frame);
            if (!Health.Alive || !session.InField) return;
            var input = session.Input;
            if (input.Gameplay)
            {
                pitch = Mathf.Clamp(pitch - input.Look.y, -85, 85);
                transform.Rotate(0, input.Look.x, 0);
                if (input.CrouchPressed) ToggleCrouch();
                if (input.PronePressed) ToggleProne();
                if (input.HealPressed && !input.WeaponSelectionPending && !input.InteractPressed) session.TryUseMedicine(input.HealInstance, input.HealPart);
            }
            Weapon.SyncHandForMovement(now);
            if (Stamina <= .01f && !sprintLocked)
            {
                sprintLocked = true;
                input.CancelTouchSprint();
                staminaResumeAt = now + .35f;
            }
            bool lowProfile = Prone || Crouched || currentHeight < 1.7f;
            input.SprintAllowed = !sprintLocked && !lowProfile && !WeaponView.MedicineBusy && (Health.Body?.LegsAllowSprint ?? true);

            bool grounded = motor.isGrounded;
            if (input.Gameplay && input.JumpPressed && grounded && !Prone)
            {
                if (!Crouched || CanFit(1.8f))
                {
                    Crouched = false;
                    verticalSpeed = 7.2f;
                }
            }
            bool walkForFire = WalkForFire(now, input.Gameplay, Weapon.Magazine != null, input.Fire);
            if (sprintLocked && input.Gameplay && !input.SprintHeld && Stamina >= 25f) sprintLocked = false;
            bool sprint = input.Gameplay && input.Sprint && !walkForFire && input.SprintAllowed && !sprintLocked && input.Move.y > .1f
                && Stamina > .01f && !lowProfile;
            if (sprint && Stamina - 9.5f * frame <= .01f)
            {
                Stamina = 0f; sprintLocked = true; sprint = false;
                input.CancelTouchSprint();
                staminaResumeAt = now + .35f;
            }
            var bag = session.ActiveFieldBag;
            float speed = MovementSpeed(Prone, lowProfile, sprint, bag.Weight, bag.MaxWeight, Weapon.MoveFactor, Health.Body?.Mobility ?? 1f);
            Vector2 move = input.Gameplay ? input.Move : Vector2.zero;
            Vector3 movement = transform.right * move.x + transform.forward * move.y;
            UpdateStanceHeight(frame);
            if (motor.isGrounded && verticalSpeed < 0) verticalSpeed = -2;
            verticalSpeed -= 20 * frame;
            Vector3 before = transform.position;
            CollisionFlags collision = motor.Move((movement * speed + Vector3.up * verticalSpeed) * frame);
            if ((collision & CollisionFlags.Above) != 0 && verticalSpeed > 0) verticalSpeed = 0;
            Vector3 displacement = transform.position - before;
            float travel = new Vector2(displacement.x, displacement.z).magnitude;
            MoveSpeed = frame > 0f ? travel / frame : 0f;
            bool running = sprint && MoveSpeed > 1.5f;
            Sprinting = running;
            if (running)
            {
                Stamina = Mathf.Max(0f, Stamina - 9.5f * frame);
                staminaResumeAt = now + 0.35f;
            }
            else if (now >= staminaResumeAt) Stamina = Mathf.Min(100f, Stamina + 15f * frame);
            if (Stamina <= 0.01f) sprintLocked = true;
            input.SprintAllowed = !sprintLocked && !lowProfile && !WeaponView.MedicineBusy && (Health.Body?.LegsAllowSprint ?? true);
            if (steps.Advance(travel, motor.isGrounded,
                Prone ? 0.76f : lowProfile ? 0.91f : running ? 1.67f : 1.42f))
                session.EnvironmentAudio?.PlayFootstep(transform.position,
                    Prone ? 0.30f : lowProfile ? 0.46f : running ? 1f : 0.83f, true);

            float leanTarget = input.Gameplay && !Prone
                ? (input.LeanRight ? 1 : input.LeanLeft ? -1 : 0) : 0;
            float cameraEase = 1f - Mathf.Exp(-12f * frame);
            float cameraHeight = Prone ? 0.56f : Crouched ? 0.98f : Mathf.Min(currentHeight, 1.6f);
            float eyeY = Mathf.Min(currentHeight - 0.14f, Mathf.Lerp(View.transform.localPosition.y, cameraHeight, cameraEase));
            float offset = FitLean(eyeY, Mathf.Lerp(Lean, leanTarget, cameraEase) * 0.22f);
            Lean = offset / 0.22f;
            View.transform.localPosition = new Vector3(offset, eyeY, 0f);
            recoilView = Vector2.Lerp(recoilView, recoilImpulse, 1f - Mathf.Exp(-34f * frame));
            recoilImpulse *= Mathf.Exp(-9f * frame);
            View.transform.localRotation = Quaternion.Euler(Mathf.Clamp(pitch - recoilView.x, -87f, 87f), recoilView.y, -Lean * 6.5f);
            if (session.Phase == SessionPhase.Raid) UpdateLoot(input, move.sqrMagnitude > 0.04f || IsTreating);
            bool aiming = input.Gameplay && input.Aim && Weapon.Magazine != null && !WeaponView.MedicineBusy;
            WeaponView.SetMedicineProgress(TreatmentProgress);
            WeaponView.SetPose(aiming, MoveSpeed, running, lowProfile, frame, sprintLocked || walkForFire || Weapon.Magazine?.Reloading == true);
            SetViewFov(Mathf.Lerp(75f, Weapon.AimFov, Mathf.SmoothStep(0f, 1f, AimProgress)));

            Operator.SetMotion(MoveSpeed, running, lowProfile, frame, sprintLocked);
            Operator.SyncGear(session.ActiveFieldBag);
            Operator.SetProne(Prone, frame);
            Operator.PoseArms(WeaponView.RightGrip, WeaponView.LeftGrip);
            bool moving = MoveSpeed > 0.35f;
            if (moving && now > noiseAt)
            {
                CombatNoise.Emit(transform.position, Prone ? 3 : lowProfile ? 4 : running ? 22 : 10, Health);
                noiseAt = now + (running ? 0.32f : Prone ? 0.72f : 0.55f);
            }
        }

        void LateUpdate()
        {
            if (session != null && session.InField && Health.Alive) TickTreatment(Time.deltaTime);
        }

        public static float MovementSpeed(bool prone, bool low, bool sprint, float weight, float limit, float weapon, float legs)
            => (prone ? 1.25f : low ? 2f : sprint ? 6.5f : 3.8f) * Mathf.Lerp(1f, .65f, limit > 0 ? weight / limit : 0f) * weapon * legs;

        void UpdateStanceHeight(float delta)
        {
            float target = Prone ? 0.78f : Crouched ? 1.2f : 1.8f;
            if (target > currentHeight && !CanFit(target)) target = currentHeight;
            currentHeight = Mathf.MoveTowards(currentHeight, target, delta * 5.5f);
            motor.height = currentHeight;
            motor.center = Vector3.up * currentHeight * 0.5f;
            Health.AimHeight = currentHeight * 0.65f;
        }

        bool CanFit(float height)
        {
            float radius = motor.radius * 0.92f;
            Vector3 bottom = transform.position + Vector3.up * (radius + 0.02f);
            Vector3 top = transform.position + Vector3.up * Mathf.Max(radius + 0.02f, height - radius);
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, stanceHits,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider collider = stanceHits[i];
                if (collider == null || collider == motor || collider.transform.IsChildOf(transform)) continue;
                return false;
            }
            return true;
        }

        float FitLean(float eyeHeight, float offset)
        {
            float distance = Mathf.Abs(offset);
            if (distance < 0.001f) return 0f;
            Vector3 origin = transform.TransformPoint(new Vector3(0f, eyeHeight, 0f));
            const float radius = 0.17f;
            const int mask = Physics.DefaultRaycastLayers;
            const QueryTriggerInteraction triggers = QueryTriggerInteraction.Ignore;
            if (Physics.CheckSphere(origin, radius, mask, triggers)) return 0f;
            float sign = Mathf.Sign(offset);
            if (Physics.SphereCast(origin, radius, transform.right * sign, out var hit, distance, mask, triggers))
                return sign * Mathf.Max(0f, hit.distance - 0.009f);
            return offset;
        }

        void UpdateLoot(PlayerInputReader input, bool moving)
        {
            if (!input.Gameplay)
            {
                focusedDoor = null;
                FocusedLoot = SearchingLoot = null;
                InteractionHint = string.Empty;
                return;
            }
            if (input.InteractPressed || Time.time >= focusAt)
            {
                focusAt = Time.time + 0.033f;
                bool found = Physics.Raycast(View.transform.position, View.transform.forward, out var hit, 3.2f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
                focusedDoor = found ? hit.collider.GetComponentInParent<HarborDoor>() : null;
                FocusedLoot = found && focusedDoor == null ? hit.collider.GetComponentInParent<LootContainer>() : null;
                string key = input.TouchMode ? "" : "[F] ";
                if (focusedDoor != null) InteractionHint = key + (focusedDoor.IsOpen ? "关闭门" : "打开门");
                else if (FocusedLoot == null) InteractionHint = string.Empty;
                else InteractionHint = key + (FocusedLoot.Searched ? (FocusedLoot.Inventory.Items.Count == 0 ? "已搜空  " : "查看  ") : "搜索  ") + FocusedLoot.DisplayName;
            }
            if (moving || !motor.isGrounded || SearchingLoot != FocusedLoot) SearchingLoot = null;
            if (input.InteractPressed && focusedDoor != null)
            {
                if (!focusedDoor.TryToggle()) session.UI.Notify("门的通道被挡住了。");
                focusAt = 0f;
                return;
            }
            if (input.InteractPressed && FocusedLoot != null && !IsTreating)
            {
                if (FocusedLoot.Searched || FocusedLoot.SearchSeconds <= 0)
                {
                    OpenLoot(FocusedLoot);
                    return;
                }
                if (!moving && motor.isGrounded && SearchingLoot == null)
                {
                    SearchingLoot = FocusedLoot;
                    searchElapsed = 0;
                }
                else if (moving) session.UI.Notify("停下并对准容器，即可搜索。");
            }
            if (SearchingLoot == null) return;
            searchElapsed += Time.deltaTime;
            if (searchElapsed >= SearchingLoot.SearchSeconds) OpenLoot(SearchingLoot);
        }

        void OpenLoot(LootContainer container)
        {
            SearchingLoot = null;
            container.Reveal();
            session.UI.OpenLoot(container);
        }

        void ToggleCrouch()
        {
            if (Prone)
            {
                ExitProne();
                return;
            }
            if (Crouched && !CanFit(1.8f)) return;
            Crouched = !Crouched;
        }

        void ToggleProne()
        {
            if (!motor.isGrounded) return;
            if (Prone)
            {
                ExitProne();
                return;
            }
            Prone = true;
            Crouched = false;
            Lean = 0;
        }

        void ExitProne()
        {
            if (CanFit(1.8f))
            {
                Prone = false;
                Crouched = false;
            }
            else if (CanFit(1.2f))
            {
                Prone = false;
                Crouched = true;
            }
        }

        public void Recoil(float amount)
        {
            recoilPattern = (recoilPattern + 1) & 31;
            recoilImpulse.x = Mathf.Min(9f, recoilImpulse.x + amount);
            recoilImpulse.y = Mathf.Clamp(recoilImpulse.y + Mathf.Sin(recoilPattern * 1.73f) * amount * 0.17f, -2f, 2f);
        }
    }
}