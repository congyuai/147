using System;

namespace Dustline
{
    public enum SessionPhase { Menu, Raid, Results, Error, Loading }

    public sealed class ExtractionTimer
    {
        public float RequiredSeconds { get; }
        public float Elapsed { get; private set; }
        public float Remaining => Math.Max(0, RequiredSeconds - Elapsed);
        public bool Completed => Elapsed >= RequiredSeconds;

        public ExtractionTimer(float seconds)
        {
            if (seconds <= 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            RequiredSeconds = seconds;
        }

        public bool Step(bool inside, bool alive, float seconds)
        {
            if (!inside || !alive) { Elapsed = 0; return false; }
            Elapsed = Math.Min(RequiredSeconds, Elapsed + Math.Max(0, seconds));
            return Completed;
        }
    }

    public sealed class Magazine
    {
        public int Capacity { get; }
        public int Rounds { get; private set; }
        public bool Reloading { get; private set; }
        public float Remaining { get; private set; }

        public Magazine(int capacity, int rounds)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            Capacity = capacity;
            Rounds = Math.Max(0, Math.Min(rounds, capacity));
        }

        public bool Fire()
        {
            if (Reloading || Rounds <= 0) return false;
            Rounds--;
            return true;
        }

        public bool BeginReload(int reserve, float duration)
        {
            if (Reloading || Rounds == Capacity || reserve <= 0) return false;
            Reloading = true;
            Remaining = Math.Max(0, duration);
            return true;
        }

        public int Step(float delta, int reserve)
        {
            if (!Reloading) return 0;
            Remaining -= Math.Max(0, delta);
            if (Remaining > 0) return 0;
            Reloading = false;
            Remaining = 0;
            int taken = Math.Max(0, Math.Min(Capacity - Rounds, reserve));
            Rounds += taken;
            return taken;
        }
    }
}
