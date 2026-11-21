using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dustline
{
    public enum MeleeSound { Swing, Contact, Solid }

    public sealed class RaidEnvironmentAudio : MonoBehaviour
    {
        const int Rate = 22050;
        const int Variants = 4;
        const float Tau = 2 * Mathf.PI;
        readonly System.Random variation = new System.Random();
        readonly List<AudioClip> owned = new List<AudioClip>();
        const int SurfaceCount = (int)SurfaceKind.Dirt + 1;
        readonly AudioClip[,] footsteps = new AudioClip[SurfaceCount, Variants];
        readonly int[] previousVariant = new int[SurfaceCount];
        readonly AudioSource[] voices = new AudioSource[6];
        readonly LoopSource[] loops = new LoopSource[4];
        readonly AudioClip[] melee = new AudioClip[3];
        AudioSource localStep, meleeVoice;
        GameSession session;

        readonly struct LoopSource
        {
            internal readonly AudioSource Source;
            internal readonly float Range, Gain;
            internal LoopSource(AudioSource source, float range, float gain)
            { Source = source; Range = range; Gain = gain; }
        }

        internal void Initialize(GameSession owner)
        {
            session = owner;
            localStep = Source("Player footsteps", Vector3.zero, false, 1);
            localStep.priority = 68;
            meleeVoice = Source("Melee foley", Vector3.zero, false, 1);
            meleeVoice.priority = 72;
            for (int i = 0; i < melee.Length; i++) melee[i] = CreateMelee((MeleeSound)i);
            for (int i = 0; i < voices.Length; i++) voices[i] = Source("Nearby footsteps", Vector3.zero, true, 29);
            for (int surface = 0; surface < footsteps.GetLength(0); surface++)
            {
                previousVariant[surface] = -1;
                for (int take = 0; take < Variants; take++)
                    footsteps[surface, take] = CreateStep((SurfaceKind)surface, take);
            }

            loops[0] = Loop("Harbor wind", Vector3.zero, 0, 0.17f, 0);
            loops[1] = Loop("Workshop ventilation", PortLayout.Districts[2].Center + Vector3.up * 3, 105, 0.29f, 1);
            loops[2] = Loop("Freight yard rumble", PortLayout.Districts[3].Center + Vector3.up * 2, 143, 0.30f, 2);
            loops[3] = Loop("Quay waves", new Vector3(PortLayout.Shore, PortTerrain.SeaY, PortLayout.Districts[4].Center.z), 153, 0.32f, 3);
        }

        AudioSource Source(string title, Vector3 position, bool spatial, float range)
        {
            var node = new GameObject(title);
            node.transform.SetParent(transform, false);
            node.transform.position = position;
            var source = node.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = spatial ? 1 : 0;
            source.dopplerLevel = 0;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = spatial ? Mathf.Min(range * 0.2f, 24) : 1;
            source.maxDistance = range;
            source.priority = 168;
            return source;
        }

        LoopSource Loop(string title, Vector3 position, float range, float gain, int kind)
        {
            var source = Source(title, position, range > 0, Mathf.Max(range, 1));
            source.clip = CreateAmbience(title, kind);
            source.loop = true;
            source.priority = 190;
            source.volume = 0;
            source.Play();
            return new LoopSource(source, range, gain);
        }

        void Update()
        {
            if (session == null) return;
            bool active = session.Phase == SessionPhase.Raid && session.Player != null;
            Vector3 origin = active ? session.Player.transform.position : Vector3.zero;
            float blend = 1 - Mathf.Exp(-2.6f * Time.unscaledDeltaTime);
            for (int i = 0; i < loops.Length; i++)
            {
                var layer = loops[i];
                float gain = active ? layer.Gain : 0;
                if (active && layer.Range > 0)
                {
                    float distance = Vector3.Distance(origin, layer.Source.transform.position);
                    gain *= 1 - Mathf.SmoothStep(0, 1, (distance - layer.Range * 0.54f) / (layer.Range * 0.46f));
                }
                layer.Source.volume = Mathf.Lerp(layer.Source.volume, gain, blend);
            }
        }

        internal void PlayFootstep(Vector3 feet, float strength, bool local)
        {
            if (!local && (session.Player == null || (feet - session.Player.transform.position).sqrMagnitude > 29 * 29)) return;
            var source = localStep;
            if (!local)
            {
                source = null;
                foreach (var voice in voices)
                    if (!voice.isPlaying) { source = voice; break; }
                if (source == null) return;
                source.transform.position = feet + Vector3.up * 0.1f;
            }
            int surface = (int)FootstepSurface.Underfoot(feet);
            int previous = previousVariant[surface];
            int take = variation.Next(previous < 0 ? Variants : Variants - 1);
            if (previous >= 0 && take >= previous) take++;
            previousVariant[surface] = take;
            source.pitch = 0.91f + (float)variation.NextDouble() * 0.18f;
            source.PlayOneShot(footsteps[surface, take], (local ? 0.55f : 0.66f) * Mathf.Clamp01(strength));
        }

        public void PlayMelee(Vector3 position, MeleeSound type)
        {
            meleeVoice.transform.position = position;
            meleeVoice.pitch = 0.92f + 0.16f * (float)variation.NextDouble();
            meleeVoice.PlayOneShot(melee[(int)type], type == MeleeSound.Swing ? 0.45f : 0.55f);
        }

        AudioClip CreateMelee(MeleeSound type)
        {
            var samples = new float[(int)(Rate * 0.24f)];
            var random = new System.Random(146 + (int)type * 317);
            float filtered = 0, low = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)Rate;
                float grain = Signed(random);
                filtered += (grain - filtered) * 0.20f;
                low += (grain - low) * 0.052f;
                float wind = (filtered - low) * 1.15f * Pulse(t, 0.031f, 0.06f);
                float contact = 0;
                if (type != MeleeSound.Swing)
                {
                    float delay = t - 0.018f;
                    float body = Mathf.Sin(Tau * (type == MeleeSound.Contact ? 104 : 403) * delay);
                    contact = (0.48f * body + 0.6f * (type == MeleeSound.Contact ? filtered : grain)) * Pulse(delay, 0.003f, 0.041f);
                }
                samples[i] = SoftLimit(wind + contact) * Mathf.Clamp01((samples.Length - 1 - i) / (Rate * 0.035f));
            }
            return Clip("Melee " + type, samples);
        }

        AudioClip CreateStep(SurfaceKind surface, int take)
        {
            var rng = new System.Random(191 + 1451 * (int)surface + 227 * take);
            int length = (int)(Rate * (surface == SurfaceKind.Metal ? 0.34f : 0.28f));
            var data = new float[length];
            var chipTimes = new float[8];
            for (int i = 0; i < chipTimes.Length; i++) chipTimes[i] = 0.014f + i * 0.009f + 0.016f * Signed(rng);
            float low = 0, mid = 0, previous = 0;
            float shift = 1 + 0.12f * Signed(rng);
            for (int i = 0; i < length; i++)
            {
                float t = (float)i / Rate;
                float white = Signed(rng);
                low += (white - low) * 0.042f;
                mid += (white - mid) * 0.29f;
                float grain = white - mid;
                float scratch = white - previous;
                previous = white;
                float heel = Pulse(t, 0.003f, 0.031f);
                float toe = Pulse(t - 0.064f, 0.006f, 0.031f);
                float body = Mathf.Sin(Tau * shift * 96 * t) * heel;
                float sample;
                switch (surface)
                {
                    case SurfaceKind.Asphalt:
                        sample = 0.32f * body + 0.58f * mid * (heel + 0.61f * toe)
                            + 0.28f * grain * Pulse(t - .054f, .011f, .048f);
                        break;
                    case SurfaceKind.Gravel:
                        float chips = 0;
                        foreach (var time in chipTimes) chips += Pulse(t - time, 0.002f, 0.008f);
                        sample = 0.18f * body + 0.36f * mid * (heel + toe) + 0.26f * scratch * chips;
                        break;
                    case SurfaceKind.Metal:
                        float ring = Mathf.Sin(Tau * shift * 580 * t) + 0.58f * Mathf.Sin(Tau * shift * 957 * t)
                            + 0.31f * Mathf.Sin(Tau * shift * 1703 * t);
                        sample = 0.17f * body + 0.21f * ring * Pulse(t, 0.002f, 0.053f)
                            + 0.29f * grain * (heel + 0.4f * toe);
                        break;
                    case SurfaceKind.Wood:
                        float knock = Mathf.Sin(Tau * shift * 173 * t) + 0.48f * Mathf.Sin(Tau * shift * 289 * t);
                        sample = 0.31f * knock * heel + 0.52f * low * (heel + toe) + 0.1f * grain * toe;
                        break;
                    case SurfaceKind.Grass:
                        float sweep = Pulse(t - .012f, .018f, .055f) + .64f * Pulse(t - .079f, .012f, .053f);
                        float blades = Pulse(t - chipTimes[0], .002f, .009f) + Pulse(t - chipTimes[4], .002f, .012f);
                        sample = .24f * body + (mid - low) * (.66f * sweep) + grain * (.33f * sweep + .18f * blades);
                        break;
                    case SurfaceKind.Dirt:
                        float press = Pulse(t, .006f, .044f) + .64f * Pulse(t - .062f, .009f, .038f);
                        float thud = Mathf.Sin(Tau * shift * 72 * t) * press;
                        sample = .39f * thud + .61f * low * press + .42f * (mid - low) * Pulse(t - .033f, .013f, .034f);
                        break;
                    default:
                        sample = 0.30f * body + 0.45f * mid * (heel + 0.45f * toe) + 0.45f * grain * Pulse(t, 0.001f, 0.015f);
                        break;
                }
                float tail = Mathf.Clamp01((length - 1 - i) / (Rate * 0.024f));
                data[i] = SoftLimit(sample) * tail * 0.83f;
            }
            return Clip(surface + " step " + take, data);
        }

        AudioClip CreateAmbience(string title, int kind)
        {
            const int seconds = 8;
            int length = Rate * seconds;
            var data = new float[length];
            var rng = new System.Random(4141 + 781 * kind);
            var low = NoiseRing(319, rng);
            var mid = NoiseRing(4607, rng);
            var air = NoiseRing(26724, rng);
            for (int i = 0; i < length; i++)
            {
                float phase = (float)i / length;
                float wave = Tau * phase;
                float soft = RingSample(low, phase), detail = RingSample(mid, phase), hiss = RingSample(air, phase);
                float body = 0.67f + 0.19f * Mathf.Sin(wave * 2) + 0.11f * Mathf.Sin(wave * 5 + 0.8f);
                float sample;
                if (kind == 1)
                {
                    float fan = Mathf.Sin(wave * 416) * 0.42f + Mathf.Sin(wave * 832) * 0.14f + Mathf.Sin(wave * 1251) * 0.042f;
                    sample = (fan * (0.64f + 0.13f * Mathf.Sin(wave * 19)) + 0.21f * soft + 0.13f * detail) * 0.66f;
                }
                else if (kind == 2)
                {
                    float rhythm = Mathf.Max(0, Mathf.Sin(wave * 23 + 0.5f));
                    sample = body * (soft * 0.36f + 0.085f * Mathf.Sin(wave * 601))
                        + 0.09f * rhythm * rhythm * (detail + 0.4f * Mathf.Sin(wave * 1121));
                }
                else if (kind == 3)
                {
                    float swell = Mathf.Clamp01(0.5f + 0.41f * Mathf.Sin(wave * 4 + 0.8f * Mathf.Sin(wave * 2)));
                    sample = 0.21f * soft + swell * swell * (0.31f * detail + 0.19f * hiss);
                }
                else sample = body * (0.32f * soft + 0.18f * detail + 0.06f * hiss);
                data[i] = SoftLimit(sample);
            }
            return Clip(title, data);
        }

        AudioClip Clip(string title, float[] samples)
        {
            var clip = AudioClip.Create(title, samples.Length, 1, Rate, false);
            clip.SetData(samples, 0);
            owned.Add(clip);
            return clip;
        }

        static float Signed(System.Random rng) => (float)(rng.NextDouble() * 2 - 1);
        static float Pulse(float time, float attack, float decay) => time <= 0 ? 0 : Mathf.Clamp01(time / attack) * Mathf.Exp(-time / decay);
        static float SoftLimit(float value) => value / (1 + Mathf.Abs(value));
        static float[] NoiseRing(int count, System.Random rng)
        {
            var ring = new float[count];
            for (int i = 0; i < count; i++) ring[i] = Signed(rng);
            return ring;
        }

        static float RingSample(float[] ring, float phase)
        {
            float offset = phase * ring.Length;
            int index = (int)offset;
            float f = offset - index;
            return Mathf.Lerp(ring[index], ring[(index + 1) % ring.Length], f * f * (3 - 2 * f));
        }

        void OnDestroy()
        {
            foreach (var clip in owned) Destroy(clip);
        }
    }
}
