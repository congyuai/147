using UnityEngine;

namespace Dustline
{
    [DefaultExecutionOrder(-40)]
    public sealed class PlayerController : MonoBehaviour
    {
        public Camera View { get; private set; }
        public Health Health { get; private set; }
        public WeaponController Weapon { get; private set; }
        public WeaponVisual WeaponView { get; private set; }
        public OperatorVisual Operator { get; private set; }
        public float Stamina { get; private set; } = 100;
        public bool Crouched { get; private set; }
        public bool Prone { get; private set; }
        public bool Sprinting { get; private set; }
        public float MoveSpeed { get; private set; }
        public float Lean { get; private set; }
        public string InteractionHint { get; private set; }
        public LootContainer FocusedLoot { get; private set; }
        public LootContainer SearchingLoot { get; private set; }
        public float SearchProgress => SearchingLoot == null ? 0 : Mathf.Clamp01(searchElapsed / SearchingLoot.SearchSeconds);
        GameSession session;
        CharacterController motor;
        StepCadence steps;
        readonly Collider[] stanceHits = new Collider[16];
        readonly RaycastHit[] leanHits = new RaycastHit[8];
        float pitch, verticalSpeed, noiseAt, focusAt, searchElapsed;
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
            Health.ArmorReduction = () =>
            {
                var armor = session.Loadout.Equipped(EquipmentSlot.Armor);
                return armor == null ? 0 : session.Catalog.Find(armor.ItemId).DamageReduction;
            };
            Health.Died += _ => session.EndRaid(false, "撤离失败：你已阵亡");
            Operator = OperatorVisual.Create(transform, session.SelectedOperator, 0, gameObject.layer);
            Operator.ShadowsOnly();

            var cameraObject = new GameObject("First person camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(transform, false);
            cameraObject.transform.localPosition = Vector3.up * 1.6f;
            View = cameraObject.GetComponent<Camera>();
            View.nearClipPlane = 0.07f;
            View.farClipPlane = 300;
            View.allowDynamicResolution = true;
            View.fieldOfView = 75;
            WeaponView = WeaponVisual.Create(cameraObject.transform, true);
            Weapon = gameObject.AddComponent<WeaponController>();
            Weapon.Initialize(session, this);
        }

        public void SetWeaponView(WeaponDefinition definition)
        {
            bool melee = definition != null && definition.Melee;
            if (WeaponView == null || WeaponView.IsMelee != melee)
            {
                if (WeaponView != null) Destroy(WeaponView.gameObject);
                WeaponView = WeaponVisual.Create(View.transform, true, melee);
            }
            WeaponView.SetEquipped(definition != null);
        }

        void Update()
        {
            if (session.Phase != SessionPhase.Raid || !Health.Alive) return;
            var input = session.Input;
            if (input.Gameplay)
            {
                pitch = Mathf.Clamp(pitch - input.Look.y, -85, 85);
                transform.Rotate(0, input.Look.x, 0);
                if (input.CrouchPressed) ToggleCrouch();
                if (input.PronePressed) ToggleProne();
                if (input.HealPressed) session.UseMedicine();
            }
            bool lowProfile = Prone || Crouched || currentHeight < 1.7f;
            input.SprintAllowed = !sprintLocked && !lowProfile;

            bool grounded = motor.isGrounded;
            if (input.Gameplay && input.JumpPressed && grounded && !Prone)
            {
                if (!Crouched || CanFit(1.8f))
                {
                    Crouched = false;
                    verticalSpeed = 7.2f;
                }
            }
            bool sprint = input.Gameplay && input.Sprint && !sprintLocked && input.Move.y > 0.1f
                && Stamina > 0.01f && !lowProfile;
            Sprinting = sprint;
            float speed = Prone ? 1.25f : lowProfile ? 2f : sprint ? 6.5f : 3.8f;
            speed *= Mathf.Lerp(1, 0.65f, session.Loadout.Weight / session.Loadout.MaxWeight);
            Vector2 move = input.Gameplay ? input.Move : Vector2.zero;
            Vector3 movement = transform.right * move.x + transform.forward * move.y;
            MoveSpeed = movement.magnitude * speed;
            Stamina = Mathf.Clamp(Stamina + (sprint && movement.sqrMagnitude > 0.1f ? -18 : 12) * Time.deltaTime, 0, 100);
            if (Stamina <= 0.01f) sprintLocked = true;
            else if (Stamina >= 25f) sprintLocked = false;
            input.SprintAllowed = !sprintLocked && !lowProfile;

            UpdateStanceHeight(Time.deltaTime);
            if (motor.isGrounded && verticalSpeed < 0) verticalSpeed = -2;
            verticalSpeed -= 20 * Time.deltaTime;
            Vector3 before = transform.position;
            CollisionFlags collision = motor.Move((movement * speed + Vector3.up * verticalSpeed) * Time.deltaTime);
            if ((collision & CollisionFlags.Above) != 0 && verticalSpeed > 0) verticalSpeed = 0;
            Vector3 displacement = transform.position - before;
            if (steps.Advance(new Vector2(displacement.x, displacement.z).magnitude, motor.isGrounded,
                Prone ? 0.76f : lowProfile ? 0.91f : sprint ? 1.67f : 1.42f))
                session.EnvironmentAudio.PlayFootstep(transform.position,
                    Prone ? 0.22f : lowProfile ? 0.34f : sprint ? 0.97f : 0.72f, true);

            float leanTarget = input.Gameplay && !Prone
                ? (input.LeanRight ? 1 : input.LeanLeft ? -1 : 0) : 0;
            if (Mathf.Abs(leanTarget) > 0.5f && LeanBlocked(leanTarget)) leanTarget = 0;
            Lean = Mathf.Lerp(Lean, leanTarget, 1 - Mathf.Exp(-12 * Time.deltaTime));
            float cameraHeight = Prone ? 0.56f : Crouched ? 0.98f : Mathf.Min(currentHeight, 1.6f);
            Vector3 cameraTarget = new Vector3(Lean * 0.22f, cameraHeight, 0);
            View.transform.localPosition = Vector3.Lerp(View.transform.localPosition, cameraTarget, 1 - Mathf.Exp(-12 * Time.deltaTime));
            View.transform.localRotation = Quaternion.Euler(pitch, 0, -Lean * 6.5f);
            View.fieldOfView = Mathf.Lerp(View.fieldOfView, input.Gameplay && input.Aim ? 48 : 75, 1 - Mathf.Exp(-12 * Time.deltaTime));

            Operator.SetMotion(MoveSpeed, sprint, lowProfile, Time.deltaTime);
            Operator.SetProne(Prone, Time.deltaTime);
            bool moving = movement.sqrMagnitude > 0.1f;
            WeaponView.SetPose(input.Gameplay && input.Aim, MoveSpeed, sprint, lowProfile, Time.deltaTime);
            Operator.PoseArms(WeaponView.RightGrip, WeaponView.LeftGrip);
            if (moving && Time.time > noiseAt)
            {
                CombatNoise.Emit(transform.position, Prone ? 3 : lowProfile ? 4 : sprint ? 22 : 10, Health);
                noiseAt = Time.time + (sprint ? 0.32f : Prone ? 0.72f : 0.55f);
            }
            UpdateLoot(input, move.sqrMagnitude > 0.04f);
        }

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

        bool LeanBlocked(float side)
        {
            float eyeHeight = Prone ? 0.56f : Crouched ? 0.98f : Mathf.Min(currentHeight, 1.6f);
            Vector3 origin = transform.position + Vector3.up * eyeHeight;
            int count = Physics.SphereCastNonAlloc(origin, 0.17f, transform.right * side, leanHits, 0.27f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider collider = leanHits[i].collider;
                if (collider != null && collider != motor && !collider.transform.IsChildOf(transform)) return true;
            }
            return false;
        }

        void UpdateLoot(PlayerInputReader input, bool moving)
        {
            if (!input.Gameplay)
            {
                FocusedLoot = SearchingLoot = null;
                InteractionHint = string.Empty;
                return;
            }
            if (input.InteractPressed || Time.time >= focusAt)
            {
                focusAt = Time.time + 0.033f;
                FocusedLoot = Physics.Raycast(View.transform.position, View.transform.forward, out var hit, 3.2f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide)
                    ? hit.collider.GetComponentInParent<LootContainer>() : null;
                string verb = FocusedLoot == null ? "" : !FocusedLoot.Searched ? "搜索  " : FocusedLoot.Inventory.Items.Count == 0 ? "已搜空  " : "查看  ";
                InteractionHint = FocusedLoot == null ? string.Empty : (input.TouchMode ? "" : "[F] ") + verb + FocusedLoot.DisplayName;
            }
            if (moving || SearchingLoot != FocusedLoot) SearchingLoot = null;
            if (input.InteractPressed && FocusedLoot != null)
            {
                if (FocusedLoot.Searched || FocusedLoot.SearchSeconds <= 0)
                {
                    OpenLoot(FocusedLoot);
                    return;
                }
                if (!moving && SearchingLoot == null)
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

        public void Recoil(float amount) { pitch = Mathf.Clamp(pitch - amount, -85, 85); }
    }
}