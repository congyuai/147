using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dustline
{
    public sealed class NavigationGrid
    {
        const float BodyRadius = 0.48f;
        const float FootClearance = 0.13f;
        const float TopClearance = 1.83f;
        readonly float linkRadius, groundHeight;
        readonly int obstacleMask;
        readonly Dictionary<Vector2Int, List<int>> buckets = new Dictionary<Vector2Int, List<int>>();
        readonly Collider[] overlaps = new Collider[24];
        readonly RaycastHit[] hits = new RaycastHit[24];
        readonly List<Edge>[] edges;
        readonly float[] cost;
        readonly int[] previous;
        readonly byte[] state;
        readonly bool[] goalLinks;
        readonly List<int> open = new List<int>();
        readonly List<Vector3> chain = new List<Vector3>();
        public IReadOnlyList<Vector3> Points { get; }

        readonly struct Edge
        {
            internal readonly int To;
            internal readonly float Length;
            internal Edge(int to, float length) { To = to; Length = length; }
        }

        public NavigationGrid(IEnumerable<Vector3> waypoints, float linkRadius = 68f)
            : this(waypoints, linkRadius, Physics.DefaultRaycastLayers, 0f)
        {
        }

        public NavigationGrid(IEnumerable<Vector3> waypoints, float linkRadius, int obstacleMask, float groundHeight)
        {
            if (waypoints == null) throw new ArgumentNullException(nameof(waypoints));
            if (!(linkRadius > 0) || float.IsInfinity(linkRadius)) throw new ArgumentOutOfRangeException(nameof(linkRadius));
            this.linkRadius = linkRadius;
            this.obstacleMask = obstacleMask;
            this.groundHeight = groundHeight;
            Physics.SyncTransforms();

            var points = new List<Vector3>();
            var used = new HashSet<Vector2Int>();
            foreach (var waypoint in waypoints)
            {
                var point = OnFloor(waypoint);
                var key = new Vector2Int(Mathf.RoundToInt(point.x * 4), Mathf.RoundToInt(point.z * 4));
                if (!used.Add(key) || !CanStandAt(point)) continue;
                var cell = Cell(point);
                if (!buckets.TryGetValue(cell, out var indices)) buckets.Add(cell, indices = new List<int>());
                indices.Add(points.Count);
                points.Add(point);
            }
            Points = points.AsReadOnly();
            edges = new List<Edge>[points.Count];
            for (int i = 0; i < edges.Length; i++) edges[i] = new List<Edge>();
            for (int i = 0; i < points.Count; i++)
            {
                var point = points[i];
                foreach (int j in CloseNodes(point))
                {
                    if (j <= i) continue;
                    float length = Vector3.Distance(point, points[j]);
                    if (!Sweep(point, points[j])) continue;
                    edges[i].Add(new Edge(j, length));
                    edges[j].Add(new Edge(i, length));
                }
            }
            cost = new float[points.Count];
            previous = new int[points.Count];
            state = new byte[points.Count];
            goalLinks = new bool[points.Count];
        }

        Vector2Int Cell(Vector3 point) => new Vector2Int(Mathf.FloorToInt(point.x / linkRadius), Mathf.FloorToInt(point.z / linkRadius));
        Vector3 OnFloor(Vector3 point) => new Vector3(point.x, groundHeight, point.z);
        static Vector3 Lower(Vector3 p) => p + Vector3.up * (FootClearance + BodyRadius);
        static Vector3 Upper(Vector3 p) => p + Vector3.up * (TopClearance - BodyRadius);

        IEnumerable<int> CloseNodes(Vector3 point)
        {
            var cell = Cell(point);
            float radiusSq = linkRadius * linkRadius;
            for (int x = cell.x - 1; x <= cell.x + 1; x++)
                for (int z = cell.y - 1; z <= cell.y + 1; z++)
                    if (buckets.TryGetValue(new Vector2Int(x, z), out var indices))
                        foreach (int i in indices)
                            if ((Points[i] - point).sqrMagnitude <= radiusSq) yield return i;
        }

        internal bool CanStandAt(Vector3 position)
        {
            Vector3 p = OnFloor(position);
            int count = Physics.OverlapCapsuleNonAlloc(Lower(p), Upper(p), BodyRadius, overlaps,
                obstacleMask, QueryTriggerInteraction.Ignore);
            if (count == overlaps.Length) return false;
            for (int i = 0; i < count; i++)
                if (Blocks(overlaps[i])) return false;
            return true;
        }

        static bool Blocks(Collider collider) => !(collider is CharacterController);

        bool Sweep(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.001f) return true;
            int count = Physics.CapsuleCastNonAlloc(Lower(from), Upper(from), BodyRadius, delta / distance,
                hits, distance, obstacleMask, QueryTriggerInteraction.Ignore);
            if (count == hits.Length) return false;
            for (int i = 0; i < count; i++)
                if (Blocks(hits[i].collider)) return false;
            return true;
        }

        public List<Vector3> Path(Vector3 start, Vector3 end)
        {
            var result = new List<Vector3>();
            Vector3 from = OnFloor(start), to = OnFloor(end);
            if (!CanStandAt(from) || !TryGoal(from, ref to) || (to - from).sqrMagnitude < 0.0025f) return result;
            if (Sweep(from, to)) { result.Add(to); return result; }

            Array.Clear(state, 0, state.Length);
            Array.Clear(goalLinks, 0, goalLinks.Length);
            open.Clear();
            foreach (int i in CloseNodes(from))
            {
                if (!Sweep(from, Points[i])) continue;
                previous[i] = -1;
                cost[i] = Vector3.Distance(from, Points[i]);
                state[i] = 1;
                open.Add(i);
            }
            if (open.Count == 0) return result;

            bool hasGoalLink = false;
            foreach (int i in CloseNodes(to))
                if (Sweep(Points[i], to)) { goalLinks[i] = true; hasGoalLink = true; }
            if (!hasGoalLink) return result;

            while (open.Count > 0)
            {
                int best = 0;
                float score = float.PositiveInfinity;
                for (int i = 0; i < open.Count; i++)
                {
                    int candidate = open[i];
                    float f = cost[candidate] + Vector3.Distance(Points[candidate], to);
                    if (f < score) { score = f; best = i; }
                }
                int current = open[best];
                if (goalLinks[current])
                {
                    CopyPath(from, to, current, result);
                    break;
                }
                open[best] = open[open.Count - 1];
                open.RemoveAt(open.Count - 1);
                state[current] = 2;
                foreach (var edge in edges[current])
                {
                    int next = edge.To;
                    float nextCost = cost[current] + edge.Length;
                    if (state[next] == 2 || (state[next] == 1 && nextCost >= cost[next])) continue;
                    previous[next] = current;
                    cost[next] = nextCost;
                    if (state[next] == 0) { open.Add(next); state[next] = 1; }
                }
            }
            return result;
        }

        bool TryGoal(Vector3 from, ref Vector3 to)
        {
            if (CanStandAt(to)) return true;
            Vector3 towardStart = (from - to).normalized;
            if (towardStart.sqrMagnitude < 0.1f) return false;
            for (int radius = 1; radius <= 4; radius++)
                for (int i = 0; i < 8; i++)
                {
                    int turn = i % 2 == 0 ? -i / 2 : (i + 1) / 2;
                    Vector3 nearby = to + Quaternion.Euler(0, turn * 45, 0) * towardStart * radius;
                    if (!CanStandAt(nearby)) continue;
                    to = nearby;
                    return true;
                }
            return false;
        }

        void CopyPath(Vector3 from, Vector3 to, int last, List<Vector3> result)
        {
            chain.Clear();
            chain.Add(to);
            for (int i = last; i >= 0; i = previous[i]) chain.Add(Points[i]);
            for (int i = chain.Count - 1; i >= 0;)
            {
                int next = i;
                for (int shortcut = Mathf.Max(0, i - 3); shortcut < i; shortcut++)
                    if (Sweep(from, chain[shortcut])) { next = shortcut; break; }
                if ((chain[next] - from).sqrMagnitude >= 0.0025f)
                {
                    from = chain[next];
                    result.Add(from);
                }
                i = next - 1;
            }
        }
    }
}
