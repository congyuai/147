using System;
using System.IO;

namespace Dustline
{
    public sealed partial class GameSession
    {
#if UNITY_EDITOR || DUSTLINE_DEMO_GM
        const string ProfileFileName = "dustline-demo-profile-v1.json";

        internal bool ApplyDemoGrant(bool level, int amount, out string error)
        {
            error = null;
            if (Phase != SessionPhase.Menu || Profile == null || store == null)
            {
                error = "当前不能使用测试工具。";
                return false;
            }
            int limit = level ? 99 - PlayerLevel : int.MaxValue - Profile.Credits;
            if (amount < 1 || amount > limit)
            {
                error = limit > 0 ? "增量必须在 1 至 " + limit.ToString("N0") + " 之间。" : "已经达到上限。";
                return false;
            }

            var next = Profile.WithInventory(Stash, Loadout, Profile.Credits + (level ? 0 : amount));
            if (level) next.GmLevelBonus += amount;
            try { store.Save(next); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
            {
                error = "测试存档未保存，数值未改变。";
                return false;
            }
            Profile = next;
            return true;
        }
#else
        const string ProfileFileName = "dustline-profile-v1.json";
#endif
    }
}
