using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dustline
{
    public static class RaidFrameRate
    {
        public const int Uncapped = -1;
        public const int Default = 60;
        const string PreferenceKey = "Dustline.RaidFrameRate.v1";
        public static IReadOnlyList<int> Options { get; } = Array.AsReadOnly(new[] { 30, 60, 90, 120, Uncapped });

        public static int Normalize(int value) => value == 30 || value == 60 || value == 90 || value == 120 || value == Uncapped ? value : Default;

        public static int Target(int preference, bool mobile, double refreshRate)
        {
            int requested = Normalize(preference);
            if (!mobile) return requested;
            int display = refreshRate >= 1 && refreshRate <= int.MaxValue
                ? (int)Math.Round(refreshRate, MidpointRounding.AwayFromZero) : Default;
            // Unity's mobile value -1 targets 30 fps rather than the display maximum.
            return requested == Uncapped ? display : Math.Min(requested, display);
        }

        public static int Load() => Normalize(PlayerPrefs.GetInt(PreferenceKey, Default));

        public static void Save(int preference)
        {
            PlayerPrefs.SetInt(PreferenceKey, Normalize(preference));
            PlayerPrefs.Save();
        }

        public static void Apply(int preference)
        {
            bool mobile = Application.isMobilePlatform;
            if (!mobile && QualitySettings.vSyncCount != 0) QualitySettings.vSyncCount = 0;
            int target = Target(preference, mobile, mobile ? Screen.currentResolution.refreshRateRatio.value : 0);
            if (target != Application.targetFrameRate) Application.targetFrameRate = target;
        }
    }
}
