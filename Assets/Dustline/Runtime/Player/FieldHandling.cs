using UnityEngine;

namespace Dustline
{
    public enum ShotPosture { Low, Standing, Walking, Running, Airborne }

    public static class FieldHandling
    {
        public static ShotPosture Posture(bool low, bool airborne, bool running, float planarSpeed)
            => airborne ? ShotPosture.Airborne : low ? ShotPosture.Low : running ? ShotPosture.Running
                : planarSpeed > .07f ? ShotPosture.Walking : ShotPosture.Standing;

        public static float Spread(float baseTangent, float handling, ShotPosture posture, float movement)
        {
            float ratio = Mathf.Clamp01(movement);
            float multiplier = posture switch
            {
                ShotPosture.Low => .68f,
                ShotPosture.Walking => 1.32f + .48f * ratio,
                ShotPosture.Running => 2.25f + .40f * ratio,
                ShotPosture.Airborne => 3.20f + .45f * ratio,
                _ => 1f
            };
            return baseTangent * handling * multiplier;
        }

        public static Vector2 ScreenRadius(float tangent, Rect pixels, Matrix4x4 projection)
            => new Vector2(pixels.width * Mathf.Abs(projection.m00), pixels.height * Mathf.Abs(projection.m11)) * (.5f * tangent);
    }

    public struct ViewRecoil
    {
        public Vector2 Offset { get; private set; }
        float recoverAt;
        int pattern;

        public void Add(float now, float interval, float impulse)
        {
            pattern = (pattern + 1) & 31;
            var angle = Offset;
            angle.x = Mathf.Min(13f, angle.x + impulse);
            angle.y = Mathf.Clamp(angle.y + Mathf.Sin(pattern * 1.73f) * impulse * .13f, -2f, 2f);
            Offset = angle;
            recoverAt = now + Mathf.Clamp(interval * 1.35f + .045f, .17f, .64f);
        }

        public void Step(float now, float delta)
        {
            float recovery = Mathf.Min(delta, now - recoverAt);
            if (recovery <= 0f) return;
            Offset *= Mathf.Exp(-6.5f * recovery);
            if (Offset.sqrMagnitude < .000001f)
            {
                Offset = Vector2.zero;
                pattern = 0;
            }
        }
    }
}
