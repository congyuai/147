using UnityEngine;

namespace Dustline
{
    public enum HarborWaterMode { Dry, Wading, Swimming, Submerged }

    public struct HarborSwimming
    {
        public HarborWaterMode Mode { get; private set; }
        public bool IsWading => Mode == HarborWaterMode.Wading;
        public bool IsSwimming => Mode == HarborWaterMode.Swimming || IsSubmerged;
        public bool IsSubmerged => Mode == HarborWaterMode.Submerged;

        public void Update(bool harbor, Vector3 feet, float height, bool grounded)
        {
            // The bare grade describes the water column; a deck over it is dry at foot height.
            float immersion = PortTerrain.SeaY - feet.y;
            if (!harbor || immersion <= .035f || !PortTerrain.TryWaterDepth(feet.x, feet.z, out float depth))
            {
                Mode = HarborWaterMode.Dry;
                return;
            }

            // A lower exit threshold keeps contact with a rising riverbed from flickering between modes.
            float swimDepth = height * (IsSwimming ? .42f : grounded ? .60f : .54f);
            if (Mathf.Min(depth, immersion) < swimDepth)
            {
                Mode = HarborWaterMode.Wading;
                return;
            }

            float eyeDepth = immersion - (height - .2f);
            Mode = eyeDepth > (IsSubmerged ? -.015f : .035f) ? HarborWaterMode.Submerged : HarborWaterMode.Swimming;
        }

        public float VerticalVelocity(Vector3 feet, float capsuleHeight, float dt, bool up, bool down, float prior)
        {
            if (!IsSwimming) return prior;

            float bottom = PortTerrain.Ground(feet.x, feet.z);
            float surfaceDraft = capsuleHeight * (up ? .62f : .70f);
            float target = down && !up ? bottom : Mathf.Max(bottom, PortTerrain.SeaY - surfaceDraft);
            float goal = Mathf.Clamp((target - feet.y) * 5f, -2.35f, up ? 3.4f : 2.65f);
            return Mathf.MoveTowards(Mathf.Clamp(prior, -3.6f, 3.6f), goal, 12f * dt);
        }
    }
}
