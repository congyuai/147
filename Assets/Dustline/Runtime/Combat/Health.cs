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
        public bool Alive => Current > 0;
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
        }

        public void Damage(float amount, Health attacker)
        {
            if (!Alive || !(amount > 0)) return;
            if (attacker != null && attacker != this && attacker.Team == Team) return;
            float protection = Mathf.Clamp(ArmorReduction?.Invoke() ?? 0, 0, 0.8f);
            float remaining = Mathf.Max(0, Current - amount * (1 - protection));
            float lost = Current - remaining;
            if (!(lost > 0)) return;

            Current = remaining;
            Vector3 direction = attacker != null ? (attacker.transform.position - transform.position).normalized : Vector3.zero;
            Damaged?.Invoke(direction, lost);
            Hurt?.Invoke(attacker);
            if (remaining == 0) Died?.Invoke(attacker);
        }

        public void Heal(float amount)
        {
            if (Alive) Current = Mathf.Min(Maximum, Current + Mathf.Max(0, amount));
        }
    }
}
