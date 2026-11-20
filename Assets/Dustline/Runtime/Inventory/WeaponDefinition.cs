using UnityEngine;

namespace Dustline
{
    [CreateAssetMenu(menuName = "Dustline/Weapon Definition")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        public string AmmoId;
        public int MagazineSize = 20;
        public float Damage = 24f;
        public float FireInterval = 0.13f;
        public float Range = 100f;
        public float ReloadSeconds = 2f;
        public float Recoil = 1.2f;
        public bool Automatic = true;
    }
}
