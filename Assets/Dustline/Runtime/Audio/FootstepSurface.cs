using UnityEngine;

namespace Dustline
{
    public enum SurfaceKind { Concrete, Asphalt, Gravel, Metal, Wood }

    public struct StepCadence
    {
        float travel;

        public bool Advance(float horizontalDistance, bool grounded, float length)
        {
            if (!grounded) { travel = 0; return false; }
            if (horizontalDistance <= 0.0001f) return false;
            travel += horizontalDistance;
            if (travel < length) return false;
            travel %= length;
            return true;
        }
    }

    public sealed class FootstepSurface : MonoBehaviour
    {
        public SurfaceKind Kind = SurfaceKind.Concrete;
        static readonly RaycastHit[] hits = new RaycastHit[12];

        public static SurfaceKind Underfoot(Vector3 feet)
        {
            int count = Physics.RaycastNonAlloc(feet + Vector3.up * 0.22f, Vector3.down, hits, 0.73f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float distance = float.PositiveInfinity;
            Collider floor = null;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (hit.distance >= distance || hit.normal.y < 0.5f || hit.collider is CharacterController) continue;
                distance = hit.distance;
                floor = hit.collider;
            }
            if (floor == null) return SurfaceKind.Concrete;
            var surface = floor.GetComponentInParent<FootstepSurface>();
            return surface != null ? surface.Kind : SurfaceKind.Concrete;
        }
    }
}
