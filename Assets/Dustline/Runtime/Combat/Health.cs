using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dustline
{
    public sealed class Health : MonoBehaviour
    {
        static readonly List<Health> actors = new List<Health>();
        public static IReadOnlyList<Health> Actors => actors;
        public float Maximum { get; private set; } = 100;
        public float Current { get; private set; } = 100;
        public int Team { get; private set; }
        public BodyCondition Body { get; private set; }
        public bool Alive => Body?.Alive ?? Current > 0f;
        internal Func<Vector3, BodyPart> HitPart;
        internal Func<BodyPart, float, float> ProtectPart;
        internal Func<BodyPart, float, int, float> ProtectAmmoPart;
        public float AimHeight = 1.2f;
        public Vector3 AimPoint => transform.position + Vector3.up * AimHeight;
        public Func<float> ArmorReduction;
        public event Action<Health> Hurt;
        public event Action<Health> Died;
        public event System.Action<Vector3, float> Damaged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { actors.Clear(); }
        void OnEnable() { actors.Add(this); }
        void OnDisable() { actors.Remove(this); }

        public void Initialize(float maximum, int team)
        {
            Maximum = maximum;
            Current = maximum;
            Team = team;
            Body?.Reset(maximum);
        }

        internal void EnableBody() => Body = new BodyCondition(Maximum);

        public void Damage(float amount, Health attacker) => Damage(amount, attacker, true);
        internal void DamageAt(float amount, Health attacker, Vector3 point) => DamageAt(amount, attacker, point, -1);

        internal void DamageAt(float amount, Health attacker, Vector3 point, int penetration) =>
            ApplyDamage(amount, attacker, HitPart?.Invoke(point) ?? BodyPart.Chest, true, penetration);

        internal void Damage(float amount, Health attacker, bool applyArmor) => ApplyDamage(amount, attacker, BodyPart.Chest, applyArmor);

        void ApplyDamage(float amount, Health attacker, BodyPart part, bool applyArmor, int penetration = -1)
        {
            if (!Alive || !(amount > 0) || float.IsInfinity(amount)) return;
            if (attacker != null && attacker != this && attacker.Team == Team) return;
            if (applyArmor)
            {
                if (penetration >= 0 && ProtectAmmoPart != null) amount = ProtectAmmoPart(part, amount, penetration);
                else if (ProtectPart != null) amount = ProtectPart(part, amount);
                else if (Body == null) amount *= 1f - Mathf.Clamp(ArmorReduction?.Invoke() ?? 0, 0, .8f);
            }
            if (!(amount > 0f) || float.IsInfinity(amount)) return;
            if (Body != null)
            {
                float before = Current;
                Body.Damage(part, amount);
                Current = Body.Total;
                Report(before - Current, attacker);
                return;
            }
            float remaining = Mathf.Max(0, Current - amount);
            float lost = Current - remaining;
            if (!(lost > 0)) return;

            Current = remaining;
            Report(lost, attacker);
        }

        void Report(float lost, Health attacker)
        {
            if (!(lost > 0f)) return;
            bool dead = !Alive;
            Vector3 direction = attacker != null ? (attacker.transform.position - transform.position).normalized : Vector3.zero;
            Damaged?.Invoke(direction, lost);
            Hurt?.Invoke(attacker);
            if (dead) Died?.Invoke(attacker);
        }

        internal void StepBody(float dt)
        {
            if (Body == null || !Alive) return;
            float before = Current;
            Body.Step(dt);
            Current = Body.Total;
            if (!Alive) Report(before - Current, null);
        }

        internal void ApplyPracticeSample(int sample)
        {
            if (Body == null || !Alive) return;
            Body.Reset(Maximum);
            Body.Sample(sample);
            Current = Body.Total;
        }

        internal bool TryTreat(BodyPart part, ItemDefinition item)
        {
            if (Body != null)
            {
                if (!Body.TryTreat(part, item.Treatment, item.HealAmount)) return false;
                Current = Body.Total;
                return true;
            }
            if (!Alive || item.Treatment != TreatmentKind.Heal || Current >= Maximum
                || !(item.HealAmount > 0f) || float.IsInfinity(item.HealAmount)) return false;
            Heal(item.HealAmount);
            return true;
        }

        public void Heal(float amount)
        {
            if (!Alive) return;
            if (Body == null) { Current = Mathf.Min(Maximum, Current + Mathf.Max(0, amount)); return; }
            var part = Body.Suggest(TreatmentKind.Heal);
            if (part.HasValue && Body.TryTreat(part.Value, TreatmentKind.Heal, amount)) Current = Body.Total;
        }
    }
}
