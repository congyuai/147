namespace Dustline
{
    public enum BotRank { Regular, Elite, Boss }

    internal readonly struct BotTuning
    {
        public readonly float Health, PatrolSpeed, SearchSpeed, SightRange, HearingRange;
        public readonly float Damage, ShotInterval, ReactionSeconds, Spread, ReloadSeconds;

        BotTuning(float health, float patrolSpeed, float searchSpeed, float sightRange, float hearingRange,
            float damage, float shotInterval, float reactionSeconds, float spread, float reloadSeconds)
        {
            Health = health;
            PatrolSpeed = patrolSpeed;
            SearchSpeed = searchSpeed;
            SightRange = sightRange;
            HearingRange = hearingRange;
            Damage = damage;
            ShotInterval = shotInterval;
            ReactionSeconds = reactionSeconds;
            Spread = spread;
            ReloadSeconds = reloadSeconds;
        }

        public static BotTuning For(BotRank rank) => rank switch
        {
            BotRank.Elite => new BotTuning(105f, 2.15f, 2.95f, 32f, 34f, 10f, 0.68f, 0.80f, 0.82f, 2.8f),
            BotRank.Boss => new BotTuning(160f, 2.0f, 2.8f, 36f, 38f, 14f, 0.56f, 0.76f, 0.67f, 2.65f),
            _ => new BotTuning(65f, 1.8f, 2.45f, 25f, 23f, 6f, 0.92f, 1.08f, 1.16f, 3.2f)
        };
    }
}
