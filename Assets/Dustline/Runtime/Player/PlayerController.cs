using UnityEngine;

namespace Dustline
{
    public sealed class PlayerController : MonoBehaviour
    {
        public Camera View { get; private set; }
        public Health Health { get; private set; }
        public WeaponController Weapon { get; private set; }
        public float Stamina { get; private set; } = 100;
        public bool Crouched { get; private set; }
        public string InteractionHint { get; private set; }
        public LootContainer FocusedLoot { get; private set; }
        GameSession session;
        CharacterController motor;
        float pitch, verticalSpeed, noiseAt;
        Transform gun;

        public void Initialize(GameSession owner)
        {
            session = owner;
            gameObject.layer = 2;
            motor = gameObject.AddComponent<CharacterController>();
            motor.height = 1.8f;
            motor.radius = 0.35f;
            motor.center = Vector3.up * 0.9f;
            motor.stepOffset = 0.25f;
            Health = gameObject.AddComponent<Health>();
            Health.Initialize(100, 0);
            Health.ArmorReduction = () =>
            {
                var armor = session.Loadout.Equipped(EquipmentSlot.Armor);
                return armor == null ? 0 : session.Catalog.Find(armor.ItemId).DamageReduction;
            };
            Health.Died += _ => session.EndRaid(false, "Killed in action");
            var cameraObject = new GameObject("First person camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(transform, false);
            cameraObject.transform.localPosition = Vector3.up * 1.6f;
            View = cameraObject.GetComponent<Camera>();
            View.nearClipPlane = 0.07f;
            View.farClipPlane = 180;
            View.fieldOfView = 75;
            var model = GameObject.CreatePrimitive(PrimitiveType.Cube);
            model.name = "Weapon silhouette";
            model.layer = 2;
            Destroy(model.GetComponent<Collider>());
            gun = model.transform;
            gun.SetParent(cameraObject.transform, false);
            gun.localPosition = new Vector3(0.24f, -0.22f, 0.48f);
            gun.localScale = new Vector3(0.11f, 0.12f, 0.55f);
            model.GetComponent<Renderer>().sharedMaterial = owner.World.DarkMaterial;
            Weapon = gameObject.AddComponent<WeaponController>();
            Weapon.Initialize(session, this);
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
                if (input.HealPressed) session.UseMedicine();
                if (input.InteractPressed && FocusedLoot != null) session.UI.OpenLoot(FocusedLoot);
            }
            bool sprint = input.Gameplay && input.Sprint && input.Move.y > 0.1f && Stamina > 5 && !Crouched;
            float speed = Crouched ? 2 : sprint ? 6.5f : 3.8f;
            speed *= Mathf.Lerp(1, 0.65f, session.Loadout.Weight / session.Loadout.MaxWeight);
            Vector2 move = input.Gameplay ? input.Move : Vector2.zero;
            Vector3 movement = transform.right * move.x + transform.forward * move.y;
            Stamina = Mathf.Clamp(Stamina + (sprint && movement.sqrMagnitude > 0.1f ? -18 : 12) * Time.deltaTime, 0, 100);
            if (motor.isGrounded && verticalSpeed < 0) verticalSpeed = -2;
            verticalSpeed -= 20 * Time.deltaTime;
            motor.Move((movement * speed + Vector3.up * verticalSpeed) * Time.deltaTime);
            View.transform.localPosition = Vector3.Lerp(View.transform.localPosition, Vector3.up * (Crouched ? 1 : 1.6f), 12 * Time.deltaTime);
            View.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
            View.fieldOfView = Mathf.Lerp(View.fieldOfView, input.Aim ? 48 : 75, 12 * Time.deltaTime);
            gun.localPosition = Vector3.Lerp(gun.localPosition, input.Aim ? new Vector3(0, -0.14f, 0.55f) : new Vector3(0.24f, -0.22f, 0.48f), 12 * Time.deltaTime);
            gun.gameObject.SetActive(session.Loadout.Equipped(EquipmentSlot.Weapon) != null);
            if (movement.sqrMagnitude > 0.1f && Time.time > noiseAt)
            {
                CombatNoise.Emit(transform.position, Crouched ? 4 : sprint ? 22 : 10, Health);
                noiseAt = Time.time + (sprint ? 0.32f : 0.55f);
            }
            FocusedLoot = null;
            InteractionHint = "";
            if (Physics.Raycast(View.transform.position, View.transform.forward, out var hit, 3.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                FocusedLoot = hit.collider.GetComponentInParent<LootContainer>();
                if (FocusedLoot != null) InteractionHint = "[E / USE] " + FocusedLoot.DisplayName;
            }
        }

        void ToggleCrouch()
        {
            if (Crouched && Physics.CheckSphere(transform.position + Vector3.up * 1.55f, 0.34f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return;
            Crouched = !Crouched;
            motor.height = Crouched ? 1.2f : 1.8f;
            motor.center = Vector3.up * motor.height * 0.5f;
            Health.AimHeight = motor.height * 0.65f;
        }
        public void Recoil(float amount) { pitch = Mathf.Clamp(pitch - amount, -85, 85); }
    }
}
