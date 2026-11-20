using UnityEngine;

namespace Dustline
{
    static class CombatFoley
    {
        const int Rate = 32000;
        const float Tau = 2 * Mathf.PI;
        public const float ReloadReference = 2;

        public static AudioClip Shot(int variant)
        {
            var samples = new float[Rate * 36 / 100];
            uint seed = 0xb3279cf1u + (uint)variant * 13379u;
            float top = 0, low = 0, air = 0, phase = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)Rate;
                float n = Noise(ref seed);
                top += 0.45f * (n - top);
                low += 0.095f * (n - low);
                air += 0.016f * (n - air);
                float split = t - (0.0074f + 0.0017f * variant);
                float crack = (0.68f * (n - top) + 0.72f * (top - low)) *
                    (Pulse(t, 0.00045f, 0.009f) + 0.35f * Pulse(split, 0.0009f, 0.008f));
                phase += Tau / Rate * (93 + 8 * variant + 94 * Mathf.Exp(-t * 36));
                float punch = (0.73f * Mathf.Sin(phase) + 0.31f * Mathf.Sin(1.9f * phase) + 0.11f * Mathf.Sin(3.78f * phase)) *
                    Pulse(t, 0.0026f, 0.039f);
                float chest = (low - air) * 1.17f * Pulse(t, 0.002f, 0.032f);
                float tail = (0.43f * (top - low) + 0.59f * (low - air)) * Pulse(t - 0.017f, 0.032f, 0.082f) *
                    (0.73f + 0.27f * Mathf.Cos(Tau * (16.7f + variant) * t));
                float reflection = (0.48f * top + 0.69f * (low - air)) *
                    (Pulse(t - (0.055f + 0.004f * variant), 0.004f, 0.021f) * 0.24f +
                    Pulse(t - 0.109f, 0.008f, 0.036f) * 0.14f);
                samples[i] = crack + punch + chest + tail + reflection;
            }
            return Clip("Dustline / impulse " + (variant + 1), samples, 0.69f);
        }

        public static AudioClip Reload()
        {
            var samples = new float[Rate * 188 / 100];
            uint seed = 0xd114e589u;
            float top = 0, low = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)Rate;
                float n = Noise(ref seed);
                top += 0.33f * (n - top);
                low += 0.07f * (n - low);
                float lift = Tick(t - 0.014f, 830, 0.027f, top) * 0.58f;
                float slide = (top - low) * Pulse(t - 0.082f, 0.023f, 0.085f) *
                    (0.72f + 0.28f * Mathf.Sin(Tau * (45 * t + 32 * t * t)));
                float seat = Tick(t - 0.782f, 330, 0.039f, top) * 0.79f + Tick(t - 0.835f, 680, 0.023f, top) * 0.37f;
                float release = (top - low) * Pulse(t - 1.324f, 0.018f, 0.065f) * 0.37f +
                    Tick(t - 1.622f, 590, 0.024f, top) * 0.65f + Tick(t - 1.684f, 1270, 0.037f, top) * 0.18f;
                samples[i] = lift + slide + seat + release;
            }
            return Clip("Dustline / reload sequence", samples, 0.58f);
        }

        public static AudioClip Dry()
        {
            var samples = new float[Rate * 115 / 1000];
            uint seed = 0x8a33b741u;
            float filtered = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)Rate;
                filtered += 0.39f * (Noise(ref seed) - filtered);
                samples[i] = Tick(t, 970, 0.012f, filtered) + Tick(t - 0.032f, 480, 0.016f, filtered) * 0.33f;
            }
            return Clip("Dustline / empty click", samples, 0.50f);
        }

        static float Noise(ref uint state)
        {
            state = unchecked(state * 1664525u + 1013904223u);
            return (state >> 8) * (2f / 16777216f) - 1;
        }

        static float Pulse(float t, float attack, float decay)
        {
            return t > 0 ? Mathf.Min(1, t / attack) * Mathf.Exp(-t / decay) : 0;
        }

        static float Tick(float t, float pitch, float decay, float filtered)
        {
            if (t <= 0) return 0;
            float phase = Tau * pitch * t;
            float tone = Mathf.Sin(phase) * 0.48f + Mathf.Sin(2.21f * phase) * 0.18f + Mathf.Sin(3.48f * phase) * 0.09f;
            return (tone + 0.66f * filtered) * Pulse(t, 0.00085f, decay);
        }

        static AudioClip Clip(string title, float[] samples, float peak)
        {
            int end = samples.Length - 1;
            float average = 0, measured = 0;
            for (int i = 0; i <= end; i++) average += samples[i];
            average /= samples.Length;
            for (int i = 0; i <= end; i++)
            {
                float fade = Mathf.Min(1, Mathf.Min((end - i) / (Rate * 0.018f), i / (Rate * 0.0003f)));
                samples[i] = (samples[i] - average) * fade;
                measured = Mathf.Max(measured, Mathf.Abs(samples[i]));
            }
            float gain = peak / Mathf.Max(0.001f, measured);
            for (int i = 0; i <= end; i++) samples[i] *= gain;
            var clip = AudioClip.Create(title, samples.Length, 1, Rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
