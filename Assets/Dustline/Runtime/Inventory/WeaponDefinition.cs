using UnityEngine;

namespace Dustline
{
    public enum WeaponForm { Rifle, Baton, Tangdao, ShortBlade, Pistol, Smg, Shotgun, Marksman, Sniper, Lmg }

    [CreateAssetMenu(menuName = "Dustline/Weapon Definition")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        public const string RifleId = "rifle";
        public const string BatonId = "baton";
        public WeaponForm Form;
        public bool AlternateFrame;
        public bool Melee;
        public string AmmoId;
        public AmmoFamily AmmoFamily;
        public string FrameInterface;
        public float MoveFactor = 1f;
        public int Rays = 1;
        public int MagazineSize = 20;
        public float Damage = 24f;
        public float FireInterval = 0.13f;
        public float Range = 100f;
        public float ReloadSeconds = 2f;
        public float Recoil = 1.2f;
        public float HipSpread = .012f;
        public float AimSpread = .002f;
        public bool Automatic = true;
    }
}
