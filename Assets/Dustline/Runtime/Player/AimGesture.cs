namespace Dustline
{
    public enum AimMode { Hold, Toggle, Hybrid }
    public enum GyroMode { Off, Always, AimOnly }

    public enum LeanMode { Toggle, Hybrid }

    public struct LeanGesture
    {
        int heldSide, latchedSide;
        double started;

        public int Active(LeanMode mode) => mode == LeanMode.Hybrid && heldSide != 0 ? heldSide : latchedSide;

        public void Set(int side, bool down, LeanMode mode, double now)
        {
            if (down)
            {
                if (heldSide == side) return;
                heldSide = side;
                started = now;
                if (mode == LeanMode.Toggle) latchedSide = latchedSide == side ? 0 : side;
            }
            else if (heldSide == side)
            {
                heldSide = 0;
                if (mode == LeanMode.Hybrid)
                    latchedSide = now - started < AimGesture.TapSeconds && latchedSide != side ? side : 0;
            }
        }

        public void Cancel(int side)
        {
            if (heldSide == side) heldSide = 0;
            if (latchedSide == side) latchedSide = 0;
        }
    }

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
