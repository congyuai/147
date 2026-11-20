using System;
using UnityEngine;

namespace Dustline
{
    public static class CombatNoise
    {
        public static event Action<Vector3, float, Health> Heard;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Heard = null; }

        public static void Emit(Vector3 position, float radius, Health source)
        {
            Heard?.Invoke(position, radius, source);
        }
    }
}
