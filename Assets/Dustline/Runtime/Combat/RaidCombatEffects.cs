using UnityEngine;

namespace Dustline
{
    public sealed class RaidCombatEffects : MonoBehaviour
    {
        const int LocalVoices = 2;
        const int RemoteVoices = 8;
        const float HearingRange = 56;
        const float MuzzleRange = 43;
        const float ImpactRange = 50;
        const float LocalImpactRange = 72;
        const int LocalBursts = 6;
        const int RemoteBursts = 22;
        readonly Burst[] bursts = new Burst[LocalBursts + RemoteBursts];
        readonly AudioSource[] shots = new AudioSource[LocalVoices + RemoteVoices];
        readonly AudioClip[] shotClips = new AudioClip[4];
        AudioSource foley;
        AudioClip reloadClip, dryClip;
        GameObject presentation;
        CombatFxBatch batch;
        Camera eye;
        int localVoice, localBurst, remoteBurst, remoteTracer, lastClip = -1;
        float visualsUntil;
        uint variation = 0x157e9b31;
        bool initialized;

        public bool TracersEnabled { get; set; } = true;

        struct Burst
        {
            public Vector3 Start, End, Axis, Normal;
            public Transform FlashAnchor;
            public float Born, Until, Seed, Length;
            public bool Flash, Impact, Actor, Trace, Local;
        }

        public void Initialize()
        {
            if (initialized) return;
            presentation = new GameObject("Combat presentation") { layer = 2 };
            presentation.transform.SetParent(transform, false);
            batch = new CombatFxBatch(presentation, bursts.Length);
            for (int i = 0; i < shotClips.Length; i++) shotClips[i] = CombatFoley.Shot(i);
            reloadClip = CombatFoley.Reload();
            dryClip = CombatFoley.Dry();
            for (int i = 0; i < shots.Length; i++)
                shots[i] = CreateVoice(i < LocalVoices ? "Local impulse" : "Distant impulse", i >= LocalVoices);
            foley = CreateVoice("Local handling", false);
            initialized = true;
        }

        public void Shot(Vector3 muzzle, Vector3 end, Vector3 normal, bool hit, bool actorHit, bool local)
            => EnqueueShot(muzzle, end, normal, hit, actorHit, local, null);

        internal void Shot(Transform muzzle, Vector3 end, Vector3 normal, bool hit, bool actorHit)
            => EnqueueShot(muzzle.position, end, normal, hit, actorHit, true, muzzle);

        void EnqueueShot(Vector3 muzzle, Vector3 end, Vector3 normal, bool hit, bool actorHit, bool local, Transform anchor)
        {
            if (!initialized || !isActiveAndEnabled) return;
            var observer = Observer();
            float sourceDistance = observer != null ? (muzzle - observer.position).sqrMagnitude : float.PositiveInfinity;
            if (local || sourceDistance < HearingRange * HearingRange)
                PlayShot(muzzle, local, observer, sourceDistance);
            if (observer == null) return;

            float impactReach = local ? LocalImpactRange : ImpactRange;
            bool showMuzzle = local || sourceDistance <= MuzzleRange * MuzzleRange;
            bool showImpact = hit && (end - observer.position).sqrMagnitude <= impactReach * impactReach;
            if (!showMuzzle && !showImpact) return;
            Vector3 delta = end - muzzle;
            float length = delta.magnitude;
            bool nearSide = local && Vector3.Dot(delta, observer.forward) < 0.035f;
            Vector3 axis = length > 0.001f && !nearSide ? delta / length : observer.forward;
            float now = Time.time;
            bool trace = showMuzzle && !nearSide && length > 0.38f && TracersEnabled && (local || remoteTracer++ % 3 == 0);
            int index = local ? localBurst++ : LocalBursts + remoteBurst++;
            if (localBurst == LocalBursts) localBurst = 0;
            if (remoteBurst == RemoteBursts) remoteBurst = 0;
            if (remoteTracer == 3) remoteTracer = 0;
            bursts[index] = new Burst {
                Start = muzzle, End = end, Axis = axis, Length = length, FlashAnchor = anchor,
                Normal = normal.sqrMagnitude > 0.01f ? normal.normalized : -axis,
                Born = now, Until = now + (showMuzzle ? (local ? 0.41f : 0.32f) : actorHit ? 0.14f : 0.29f),
                Seed = Next(), Flash = showMuzzle, Impact = showImpact, Actor = actorHit, Trace = trace, Local = local
            };
            visualsUntil = Mathf.Max(visualsUntil, bursts[index].Until);
        }

        public void Reload(Vector3 position) => Reload(position, CombatFoley.ReloadReference);

        internal void Reload(Vector3 position, float seconds)
        {
            if (!initialized || !isActiveAndEnabled) return;
            float pitch = CombatFoley.ReloadReference / Mathf.Clamp(seconds, 0.8f, 3.3f);
            Play(foley, reloadClip, position, 0.25f, pitch);
        }

        public void DryFire(Vector3 position)
        {
            if (initialized && isActiveAndEnabled) Play(foley, dryClip, position, 0.23f, Mathf.Lerp(0.97f, 1.06f, Next()));
        }

        internal void CancelReload()
        {
            if (foley != null && foley.clip == reloadClip) foley.Stop();
        }

        Transform Observer()
        {
            if (eye == null || !eye.isActiveAndEnabled) eye = Camera.main;
            return eye != null ? eye.transform : null;
        }

        AudioSource CreateVoice(string label, bool spatial)
        {
            var node = new GameObject(label, typeof(AudioSource)) { layer = 2 };
            node.transform.SetParent(presentation.transform, false);
            var voice = node.GetComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = spatial ? 1 : 0;
            voice.dopplerLevel = 0;
            voice.rolloffMode = AudioRolloffMode.Linear;
            voice.minDistance = 3;
            voice.maxDistance = HearingRange;
            voice.priority = spatial ? 152 : 48;
            return voice;
        }

        void PlayShot(Vector3 position, bool local, Transform observer, float distance)
        {
            AudioSource voice = local ? shots[localVoice++] : RemoteVoice(observer.position, distance);
            if (localVoice == LocalVoices) localVoice = 0;
            if (voice == null) return;
            lastClip = (lastClip + 1 + (int)(Next() * (shotClips.Length - 1))) % shotClips.Length;
            Play(voice, shotClips[lastClip], position, local ? 0.31f : 0.061f, Mathf.Lerp(0.95f, 1.06f, Next()));
        }

        AudioSource RemoteVoice(Vector3 listener, float distance)
        {
            AudioSource candidate = null;
            float farthest = distance;
            for (int i = LocalVoices; i < shots.Length; i++)
            {
                var voice = shots[i];
                if (!voice.isPlaying) return voice;
                float other = (voice.transform.position - listener).sqrMagnitude;
                if (other > farthest) { candidate = voice; farthest = other; }
            }
            return candidate;
        }

        static void Play(AudioSource voice, AudioClip clip, Vector3 position, float volume, float pitch)
        {
            voice.Stop();
            voice.transform.position = position;
            voice.clip = clip;
            voice.volume = volume;
            voice.pitch = pitch;
            voice.Play();
        }

        float Next()
        {
            variation = unchecked(variation * 1664525u + 1013904223u);
            return (variation >> 8) * (1f / 16777216f);
        }

        void LateUpdate()
        {
            if (!initialized) return;
            float now = Time.time;
            if (now >= visualsUntil) { batch.Hide(); return; }
            var observer = Observer();
            if (observer == null) { batch.Hide(); return; }
            batch.Begin(observer);
            for (int i = bursts.Length - 1; i >= 0; i--)
            {
                ref var shot = ref bursts[i];
                if (shot.Until <= now) continue;
                float age = now - shot.Born;
                if (shot.Flash) DrawMuzzle(in shot, age);
                if (shot.Trace && TracersEnabled) DrawTracer(in shot, age);
                if (shot.Impact) DrawImpact(in shot, age);
            }
            batch.Upload();
        }

        void DrawMuzzle(in Burst shot, float age)
        {
            Vector3 side = Vector3.Cross(shot.Axis, Vector3.up);
            side = side.sqrMagnitude > 0.0001f ? side.normalized : Vector3.right;
            Vector3 up = Vector3.Cross(side, shot.Axis);
            float smokeLife = shot.Local ? 0.41f : 0.3f;
            float progress = age / smokeLife;
            if (progress < 1)
            {
                float opacity = (1 - progress) * Mathf.SmoothStep(0, 1, progress * 10 + 0.16f);
                float drift = (shot.Seed - 0.5f) * 0.17f;
                Vector3 center = shot.Start + shot.Axis * (0.055f + progress * 0.28f) + Vector3.up * (progress * 0.13f) + side * (drift * progress);
                batch.Billboard(center, 0.044f + 0.096f * progress, shot.Seed * 6 + progress, CombatFxBatch.Ink.Vapor, 0.53f * opacity);
                if (shot.Local)
                    batch.Billboard(center + shot.Axis * (0.03f + 0.09f * progress), 0.032f + 0.053f * progress,
                        -shot.Seed * 5 - progress, CombatFxBatch.Ink.Vapor, 0.34f * opacity);
            }
            const float flashLife = 0.058f;
            if (age >= flashLife) return;
            Vector3 start = shot.Start, axis = shot.Axis;
            if (shot.FlashAnchor != null)
            {
                if (!shot.FlashAnchor.gameObject.activeInHierarchy) return;
                start = shot.FlashAnchor.position;
                axis = shot.FlashAnchor.forward;
                side = shot.FlashAnchor.right;
                up = shot.FlashAnchor.up;
            }
            float fade = 1 - age / flashLife;
            float width = Mathf.Lerp(0.049f, 0.078f, shot.Seed) * (0.55f + 0.45f * fade);
            float length = (0.15f + shot.Seed * 0.09f) * (1.16f - 0.4f * fade);
            Vector3 rear = start - axis * 0.028f;
            Vector3 tip = start + axis * length;
            batch.Billboard(start + axis * 0.043f, width * 1.7f, 0, CombatFxBatch.Ink.Glow, 0.7f * fade);
            for (int i = 0; i < 3; i++)
            {
                float phase = shot.Seed * 6.28f + i * Mathf.PI / 3;
                Vector3 blade = (side * Mathf.Cos(phase) + up * Mathf.Sin(phase)) * width;
                batch.Quad(rear - blade * 0.66f, rear + blade * 0.66f, tip + blade, tip - blade,
                    CombatFxBatch.Ink.Tongue, fade);
            }
            Vector3 core = start + axis * 0.033f;
            side *= width * 0.32f;
            up *= width * 0.32f;
            Vector3 front = start + axis * (length * 0.71f);
            batch.Ridge(rear, front, core + side, core + up, CombatFxBatch.Ink.Core, fade);
            batch.Ridge(rear, front, core + up, core - side, CombatFxBatch.Ink.CoreEdge, fade);
            batch.Ridge(rear, front, core - side, core - up, CombatFxBatch.Ink.Core, fade);
            batch.Ridge(rear, front, core - up, core + side, CombatFxBatch.Ink.CoreEdge, fade);
        }

        void DrawTracer(in Burst shot, float age)
        {
            float path = Mathf.Min(shot.Length, 64);
            float duration = Mathf.Lerp(0.05f, 0.112f, path / 64);
            if (age >= duration) return;
            float t = age / duration;
            float head = Mathf.Min(path, 0.39f + path * (0.09f + t * 0.91f));
            float tail = Mathf.Max(0.09f, head - Mathf.Min(path * 0.70f, shot.Local ? 8.5f : 5.7f));
            if (head - tail < 0.03f) return;
            Vector3 from = shot.Start + shot.Axis * tail, to = shot.Start + shot.Axis * head;
            float glow = (1 - t) * (shot.Local ? 0.85f : 0.58f);
            batch.Ribbon(from, to, 0.036f, CombatFxBatch.Ink.Glow, glow * 0.62f);
            batch.Ribbon(from, to, 0.013f, CombatFxBatch.Ink.Streak, glow);
        }

        void DrawImpact(in Burst shot, float age)
        {
            float life = shot.Actor ? 0.145f : 0.29f;
            if (age >= life) return;
            float progress = age / life;
            Vector3 point = shot.End + shot.Normal * 0.016f;
            Vector3 side = Vector3.Cross(shot.Normal, Mathf.Abs(shot.Normal.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
            Vector3 tangent = Vector3.Cross(shot.Normal, side);
            if (!shot.Actor)
            {
                batch.Billboard(point + shot.Normal * (0.055f + 0.21f * progress), 0.051f + 0.23f * progress,
                    4 * shot.Seed + progress, CombatFxBatch.Ink.Dust, 0.73f * (1 - progress));
                batch.Billboard(point + shot.Normal * (0.027f + 0.13f * progress) + side * (0.09f * progress),
                    0.04f + 0.13f * progress, -4 * shot.Seed - progress, CombatFxBatch.Ink.Dust, 0.44f * (1 - progress));
            }
            else
            {
                batch.Billboard(point + shot.Normal * (0.025f + 0.065f * progress), 0.032f + 0.05f * progress,
                    shot.Seed * 6, CombatFxBatch.Ink.Signal, 0.51f * (1 - progress));
            }
            float sparkAge = age / (shot.Actor ? 0.10f : 0.17f);
            if (sparkAge >= 1) return;
            int count = shot.Actor ? 2 : 4;
            for (int i = 0; i < count; i++)
            {
                float phi = shot.Seed * 6.28f + i * 2.39f;
                Vector3 ray = shot.Normal * (0.48f + 0.12f * i) + side * Mathf.Cos(phi) * 0.83f + tangent * Mathf.Sin(phi) * 0.83f;
                float travel = (shot.Actor ? 0.13f : 0.43f) * (0.16f + 0.84f * sparkAge);
                Vector3 front = point + ray * travel - Vector3.up * (0.064f * sparkAge * sparkAge);
                batch.Ribbon(front - ray * (0.025f + (1 - sparkAge) * 0.07f), front,
                    shot.Actor ? 0.011f : 0.019f, shot.Actor ? CombatFxBatch.Ink.Signal : CombatFxBatch.Ink.Streak,
                    (1 - sparkAge) * (shot.Actor ? 0.63f : 0.93f));
            }
        }

        void OnDisable()
        {
            if (batch != null) batch.Hide();
            visualsUntil = 0;
            System.Array.Clear(bursts, 0, bursts.Length);
            foreach (var source in shots) if (source != null) source.Stop();
            if (foley != null) foley.Stop();
        }

        void OnDestroy()
        {
            if (batch != null) batch.Release();
            foreach (var clip in shotClips) if (clip != null) Destroy(clip);
            if (reloadClip != null) Destroy(reloadClip);
            if (dryClip != null) Destroy(dryClip);
            if (presentation != null) Destroy(presentation);
        }
    }
}
