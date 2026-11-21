using System;
using UnityEngine;

namespace Dustline
{
    [Serializable]
    public sealed class BladeChoice
    {
        public string Id;
        public string DisplayName;
        public WeaponDefinition Weapon;
    }

    [CreateAssetMenu(menuName = "Dustline/Blade Catalog")]
    public class BladeCatalog : ScriptableObject
    {
        public const string DefaultId = "tangdao";
        public const string ShortBladeId = "short_blade";
        public BladeChoice[] Blades;

        public WeaponDefinition Resolve(string id)
        {
            WeaponDefinition fallback = null;
            if (Blades == null)
                return null;

            foreach (var choice in Blades)
            {
                if (choice == null || string.IsNullOrWhiteSpace(choice.Id) || choice.Weapon == null)
                    continue;
                if (string.Equals(choice.Id, id, StringComparison.Ordinal))
                    return choice.Weapon;
                if (choice.Id == DefaultId)
                    fallback = choice.Weapon;
            }
            return fallback;
        }
    }
}
