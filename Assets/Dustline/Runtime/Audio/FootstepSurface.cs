using UnityEngine;

namespace Dustline
{
    public enum SurfaceKind { Concrete, Asphalt, Gravel, Metal, Wood, Grass, Dirt }

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
        internal bool UsesPortGround;
        internal bool IsNavigationFloor => UsesPortGround || IsBridgeDeck;
        internal bool IsBridgeDeck;
        static readonly RaycastHit[] hits = new RaycastHit[16];

        public static SurfaceKind Underfoot(Vector3 feet)
        {
            int count = Physics.RaycastNonAlloc(feet + Vector3.up * 0.22f, Vector3.down, hits, 0.73f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float distance = float.PositiveInfinity;
            Collider floor = null;
            Vector3 contact = feet;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (hit.distance >= distance || hit.normal.y < 0.5f || hit.collider is CharacterController) continue;
                distance = hit.distance;
                floor = hit.collider;
                contact = hit.point;
            }
            if (floor == null) return SurfaceKind.Concrete;
            var surface = floor.GetComponentInParent<FootstepSurface>();
            return surface == null ? SurfaceKind.Concrete : surface.UsesPortGround
                ? PortTerrain.Surface(contact.x, contact.z) : surface.Kind;
        }
    }
}
