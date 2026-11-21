using UnityEngine;

namespace Dustline
{
    public sealed class HarborDoor : MonoBehaviour
    {
        readonly Collider[] occupants = new Collider[16];
        BoxCollider leaf;
        GameObject rolledLeaf;
        Bounds opening;
        public bool IsOpen { get; private set; }
        internal Bounds Opening => opening;

        internal void Initialize(BoxCollider panel, GameObject rolled, bool open)
        {
            leaf = panel;
            rolledLeaf = rolled;
            Vector3 extents = leaf.size * .5f;
            Vector3 a = leaf.transform.TransformVector(new Vector3(extents.x, 0, 0));
            Vector3 b = leaf.transform.TransformVector(new Vector3(0, extents.y, 0));
            Vector3 c = leaf.transform.TransformVector(new Vector3(0, 0, extents.z));
            static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            opening = new Bounds(leaf.transform.TransformPoint(leaf.center), 2 * (Abs(a) + Abs(b) + Abs(c)));
            SetOpen(open);
        }

        internal bool IsLeaf(Collider candidate) => candidate == leaf;

        public bool TryToggle()
        {
            if (leaf == null) return false;
            if (IsOpen)
            {
                Physics.SyncTransforms();
                Vector3 size = Vector3.Scale(leaf.size, leaf.transform.lossyScale) * .5f + new Vector3(.055f, .015f, .095f);
                int count = Physics.OverlapBoxNonAlloc(leaf.transform.TransformPoint(leaf.center), size, occupants,
                    leaf.transform.rotation, Physics.AllLayers, QueryTriggerInteraction.Ignore);
                if (count == occupants.Length) return false;
                for (int i = 0; i < count; i++)
                {
                    Collider occupant = occupants[i];
                    if (occupant is CharacterController || occupant.attachedRigidbody != null ||
                        occupant.GetComponentInParent<Health>() != null || occupant.GetComponentInParent<LootContainer>() != null) return false;
                }
            }
            SetOpen(!IsOpen);
            Physics.SyncTransforms();
            return true;
        }

        void SetOpen(bool open)
        {
            IsOpen = open;
            leaf.gameObject.SetActive(!open);
            rolledLeaf.SetActive(open);
        }
    }
}
