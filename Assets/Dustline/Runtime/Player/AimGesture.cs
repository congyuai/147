namespace Dustline
{
    public enum AimMode { Hold, Toggle, Hybrid }
    public enum GyroMode { Off, Always, AimOnly }

    public struct AimGesture
    {
        public const double TapSeconds = 0.23;
        bool held, latched;
        double started;

        public bool Active(AimMode mode) => held && mode != AimMode.Toggle || latched && mode != AimMode.Hold;

        public void Set(bool down, AimMode mode, double now)
        {
            if (down == held) return;
            held = down;
            if (down)
            {
                started = now;
                if (mode == AimMode.Toggle) latched = !latched;
            }
            else if (mode == AimMode.Hybrid && now - started < TapSeconds) latched = !latched;
        }
    }
}
