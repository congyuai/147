using System;
using UnityEngine;

namespace Dustline
{
    public enum BodyPart { Head, Chest, Abdomen, LeftArm, RightArm, LeftLeg, RightLeg }

    [Flags]
    public enum BodyCoverage
    {
        None = 0, Head = 1, Chest = 2, Abdomen = 4, LeftArm = 8, RightArm = 16, LeftLeg = 32, RightLeg = 64
    }

    [Flags]
    public enum Injury { None = 0, LightBleed = 1, HeavyBleed = 2, Fracture = 4, Pain = 8, Destroyed = 16 }

    public sealed class BodyCondition
    {
        public const int Count = 7;
        static readonly float[] Shares = { .16f, .31f, .16f, .09f, .09f, .095f, .095f };
        static readonly string[] Names = { "头部", "胸部", "腹部", "左臂", "右臂", "左腿", "右腿" };
        readonly float[] current = new float[Count], maximum = new float[Count], natural = new float[Count];
        readonly Injury[] injuries = new Injury[Count];
        float painRelief, bleedTime;
        public float Current(BodyPart part) => current[(int)part];
        public float Maximum(BodyPart part) => maximum[(int)part];
        public Injury Injuries(BodyPart part) => injuries[(int)part];
        public bool Alive => current[0] > 0f && current[1] > 0f;
        public bool PainRelieved => painRelief > 0;
        public float PainRemaining => painRelief;
        public float Total
        {
            get { float total = 0f; foreach (float value in current) total += value; return total; }
        }
        public float TotalMaximum
        {
            get { float total = 0f; foreach (float value in maximum) total += value; return total; }
        }
        public static string Name(BodyPart part) => Names[(int)part];
        public static BodyCoverage Mask(BodyPart part) => (BodyCoverage)(1 << (int)part);
        public static bool IsVital(BodyPart part) => part == BodyPart.Head || part == BodyPart.Chest;

        internal static float Protect(BodyPart part, float damage, ItemDefinition rule, ItemStack armor) =>
            Protect(part, damage, rule, armor, 0);

        internal static float Protect(BodyPart part, float damage, ItemDefinition rule, ItemStack armor, int penetration)
        {
            if (rule == null || armor == null || armor.Wear >= rule.Durability || (rule.Coverage & Mask(part)) == 0) return damage;
            float remaining = 1f - .45f * armor.Wear / rule.Durability;
            float resistance = 1f;
            if (penetration > 0 && rule.Tier > 0)
                resistance = Mathf.Clamp01(.60f + .04f * (WeaponRules.PenetrationForTier(rule.Tier)
                    - Math.Min(penetration, WeaponRules.PenetrationForTier(WeaponRules.AmmoTierCount))));
            float blocked = damage * Mathf.Clamp01(rule.DamageReduction * remaining * resistance);
            if (blocked > 0f) armor.Wear = (int)Mathf.Min(rule.Durability, armor.Wear + Mathf.Max(1f, Mathf.Ceil(blocked)));
            return damage - blocked;
        }

        public BodyCondition(float total) => Reset(total);

        public void Reset(float total)
        {
            for (int i = 0; i < Count; i++)
            {
                current[i] = maximum[i] = natural[i] = total * Shares[i];
                injuries[i] = Injury.None;
            }
            painRelief = bleedTime = 0f;
        }

        public void Damage(BodyPart part, float amount, bool mayInjure = true)
        {
            int i = (int)part;
            if (!Alive || i < 0 || i >= Count || !(amount > 0f) || float.IsInfinity(amount)) return;
            float overflow = Mathf.Max(0, amount - current[i]);
            Lose(i, amount);
            if (mayInjure && current[i] > 0f)
            {
                float severity = amount / natural[i];
                if (severity >= .60f) injuries[i] = (injuries[i] & ~Injury.LightBleed) | Injury.HeavyBleed;
                else if (severity >= .24f && (injuries[i] & Injury.HeavyBleed) == 0) injuries[i] |= Injury.LightBleed;
                if (i >= 3 && severity >= .48f) injuries[i] |= Injury.Fracture;
                if (severity >= .24f) injuries[i] |= Injury.Pain;
            }
            if (!Alive || overflow <= 0f) return;
            float weight = 0f;
            for (int j = 0; j < Count; j++) if (j != i && current[j] > 0f) weight += Shares[j];
            if (weight <= 0f) return;
            for (int j = 0; j < Count; j++)
                if (j != i && current[j] > 0f) Lose(j, overflow * (Shares[j] / weight));
        }

        void Lose(int i, float amount)
        {
            current[i] = Mathf.Max(0f, current[i] - amount);
            if (current[i] <= 0f) injuries[i] |= Injury.Destroyed | Injury.Pain;
        }

        public bool CanTreat(BodyPart part, TreatmentKind type)
        {
            int i = (int)part;
            if (!Alive || i < 0 || i >= Count) return false;
            var injury = injuries[i];
            return type switch
            {
                TreatmentKind.Heal => current[i] > 0f && current[i] < maximum[i] - .001f,
                TreatmentKind.LightBleed => (injury & Injury.LightBleed) != 0,
                TreatmentKind.HeavyBleed => (injury & Injury.HeavyBleed) != 0,
                TreatmentKind.Fracture => (injury & Injury.Fracture) != 0,
                TreatmentKind.Pain => !PainRelieved && (injury & Injury.Pain) != 0,
                TreatmentKind.Surgery => i >= 2 && (injury & Injury.Destroyed) != 0,
                _ => false
            };
        }

        public bool TryTreat(BodyPart part, TreatmentKind type, float strength)
        {
            if (!CanTreat(part, type) || (type == TreatmentKind.Heal || type == TreatmentKind.Pain)
                && (!(strength > 0f) || float.IsInfinity(strength))) return false;
            int i = (int)part;
            switch (type)
            {
                case TreatmentKind.Heal: current[i] = Mathf.Min(maximum[i], current[i] + Mathf.Max(0, strength)); break;
                case TreatmentKind.LightBleed: injuries[i] &= ~Injury.LightBleed; break;
                case TreatmentKind.HeavyBleed: injuries[i] &= ~Injury.HeavyBleed; break;
                case TreatmentKind.Fracture: injuries[i] &= ~Injury.Fracture; break;
                case TreatmentKind.Pain: painRelief = strength; break;
                case TreatmentKind.Surgery:
                    maximum[i] = Mathf.Max(1f, maximum[i] * .75f);
                    current[i] = Mathf.Min(1f, maximum[i]);
                    injuries[i] &= ~Injury.Destroyed;
                    break;
            }
            return true;
        }

        public BodyPart? Suggest(TreatmentKind kind)
        {
            int selected = -1;
            float deficit = -1f;
            for (int i = 0; i < Count; i++)
            {
                if (!CanTreat((BodyPart)i, kind)) continue;
                float value = kind == TreatmentKind.Heal ? maximum[i] - current[i] : 0f;
                if (value > deficit) { selected = i; deficit = value; }
            }
            return selected < 0 ? (BodyPart?)null : (BodyPart)selected;
        }

        internal void Sample(int sample)
        {
            current[3] = maximum[3] * .55f;
            current[4] = maximum[4] * .60f;
            current[5] = maximum[5] * .60f;
            injuries[3] = Injury.LightBleed | Injury.Pain;
            injuries[5] = Injury.Fracture | Injury.Pain;
            if (sample > 0)
            {
                injuries[4] = Injury.HeavyBleed | Injury.Pain;
                current[6] = 0f;
                injuries[6] = Injury.Destroyed | Injury.Pain;
            }
        }

        internal void Step(float dt)
        {
            if (!Alive || !(dt > 0f)) return;
            painRelief = Mathf.Max(0f, painRelief - dt);
            bleedTime += dt;
            if (bleedTime < 1f) return;
            bleedTime -= 1f;
            for (int i = 0; i < Count && Alive; i++)
            {
                var injury = injuries[i];
                float loss = (injury & Injury.HeavyBleed) != 0 ? 1.3f : (injury & Injury.LightBleed) != 0 ? .42f : 0f;
                if (loss > 0f) Damage((BodyPart)i, loss, false);
            }
        }

        float Impairment(int i)
        {
            var wound = injuries[i];
            float value = (wound & Injury.Destroyed) != 0 ? .24f : (wound & Injury.Fracture) != 0 ? .16f : 0f;
            return value + (!PainRelieved && (wound & Injury.Pain) != 0 ? .05f : 0f);
        }

        public float Mobility => Mathf.Max(.50f, 1f - Impairment(5) - Impairment(6));
        public float Handling => 1f + Impairment(3) + Impairment(4);
        public bool LegsAllowSprint => ((injuries[5] | injuries[6]) & (Injury.Destroyed | Injury.Fracture)) == 0;
    }
}
