using System;

namespace Dustline
{
    internal sealed class RaidSite
    {
        internal readonly string Name, Description, DisabledReason;
        internal readonly string[] Tasks;
        internal readonly RaidDifficulty[] Difficulties;
        internal readonly string LocalTime;

        internal RaidSite(string name, string description, RaidDifficulty[] difficulties, string localTime, string disabledReason, params string[] tasks)
        {
            Name = name; Description = description; Difficulties = difficulties; LocalTime = localTime; DisabledReason = disabledReason; Tasks = tasks;
        }

        internal bool Open => Difficulties.Length != 0;
    }

    internal readonly struct RaidDifficulty
    {
        internal readonly string Name;
        internal readonly int RequiredValue, RequiredLevel, Seconds;
        internal readonly float Threat;

        internal RaidDifficulty(string name, int requiredValue, int requiredLevel, int seconds, float threat)
        { Name = name; RequiredValue = requiredValue; RequiredLevel = requiredLevel; Seconds = seconds; Threat = threat; }
    }

    internal static class Deployment
    {
        internal const int Harbor = 0, Quarry = 1;
        internal static readonly RaidSite[] Sites =
        {
            new RaidSite("北岬货运港", "沿河道和堆场搜索物资。海事检修站由首领及精英守卫。",
                new[] { new RaidDifficulty("常规", 0, 1, 900, 1f), new RaidDifficulty("高风险", 50000, 2, 720, 1.25f) }, "16:20", "",
                "搜刮｜海事检修站", "撤离｜铁路西闸或东泊位"),
            new RaidSite("石脊采石场", "穿过破碎站、石料台和输送廊。沿两端的旧道撤离。",
                new[] { new RaidDifficulty("常规", 0, 1, 720, 1f), new RaidDifficulty("高风险", 75000, 2, 600, 1.35f) }, "", "",
                "搜刮｜破碎站与值班物资", "撤离｜采场出口"),
            new RaidSite("灰湾水闸", "", Array.Empty<RaidDifficulty>(), "", "该地点尚未开放。")
        };

        internal static bool TryDifficulty(int site, int tier, out RaidDifficulty difficulty)
        {
            difficulty = default;
            if (site < 0 || site >= Sites.Length || tier < 0 || tier >= Sites[site].Difficulties.Length) return false;
            difficulty = Sites[site].Difficulties[tier];
            return true;
        }

        internal static int Level(ProfileData profile) => 1 + Math.Min(98, profile.Extractions);

        internal static string EntryError(int site, int tier, long value, int level)
        {
            if (!TryDifficulty(site, tier, out var difficulty)) return "该地点或难度尚未开放。";
            if (level < difficulty.RequiredLevel) return "当前等级不足。";
            if (value < difficulty.RequiredValue) return "当前装备价值不足。";
            return null;
        }
    }
}
