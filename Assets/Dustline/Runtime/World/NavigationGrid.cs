using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dustline
{
    public sealed class NavigationGrid
    {
        const float BodyRadius = .47f, FootClearance = .13f, TopClearance = 1.88f;
        readonly float linkRadius, groundHeight;
        readonly int obstacleMask;
        enum FloorKind { Flat, Port, Quarry }
        readonly FloorKind terrain;
        readonly HashSet<Collider> quarryFloors;
        readonly Bounds[] doors;
        readonly Dictionary<Vector2Int, List<int>> buckets = new Dictionary<Vector2Int, List<int>>();
        readonly Collider[] overlaps = new Collider[24];
        readonly RaycastHit[] hits = new RaycastHit[24];
        readonly List<Edge>[] edges;
        readonly float[] cost, goalDistance;
        readonly int[] previous;
        readonly byte[] state;
        readonly List<OpenEntry> open = new List<OpenEntry>();
        readonly List<Vector3> chain = new List<Vector3>();
        public IReadOnlyList<Vector3> Points { get; }

        readonly struct Edge
        {
            internal readonly int To;
            internal readonly float Length;
            internal readonly bool Door;
            internal Edge(int to, float length, bool door) { To = to; Length = length; Door = door; }
        }

        readonly struct OpenEntry
        {
            internal readonly int Node;
            internal readonly float Score;
            internal OpenEntry(int node, float score) { Node = node; Score = score; }
        }

        public NavigationGrid(IEnumerable<Vector3> waypoints, float linkRadius = 68f)
            : this(waypoints, linkRadius, Physics.DefaultRaycastLayers, 0f) { }

        public NavigationGrid(IEnumerable<Vector3> waypoints, float linkRadius, int obstacleMask, float groundHeight)
            : this(waypoints, linkRadius, obstacleMask, groundHeight, FloorKind.Flat, Array.Empty<HarborDoor>(), null) { }

        internal NavigationGrid(IEnumerable<Vector3> waypoints, float linkRadius, HarborDoor[] harborDoors)
            : this(waypoints, linkRadius, Physics.DefaultRaycastLayers, 0, FloorKind.Port, harborDoors, null) { }

        internal NavigationGrid(IEnumerable<Vector3> waypoints, float linkRadius, HashSet<Collider> quarryFloors)
            : this(waypoints, linkRadius, Physics.DefaultRaycastLayers, 0, FloorKind.Quarry, Array.Empty<HarborDoor>(), quarryFloors) { }

        NavigationGrid(IEnumerable<Vector3> waypoints, float linkRadius, int obstacleMask, float groundHeight, FloorKind terrain,
            HarborDoor[] harborDoors, HashSet<Collider> quarryFloors)
        {
            if (waypoints == null) throw new ArgumentNullException(nameof(waypoints));
            if (!(linkRadius > 0) || float.IsInfinity(linkRadius)) throw new ArgumentOutOfRangeException(nameof(linkRadius));
            this.linkRadius = linkRadius;
            this.obstacleMask = obstacleMask;
            this.groundHeight = groundHeight;
            this.terrain = terrain;
            this.quarryFloors = quarryFloors;
            doors = new Bounds[harborDoors.Length];
            for (int i = 0; i < doors.Length; i++) doors[i] = harborDoors[i].Opening;
            Physics.SyncTransforms();

            var points = new List<Vector3>();
            var used = new HashSet<Vector2Int>();
            foreach (var waypoint in waypoints)
            {
                if (!TryFloor(waypoint, out var point) || !IsClear(point, true)) continue;
                var key = new Vector2Int(Mathf.RoundToInt(point.x * 4), Mathf.RoundToInt(point.z * 4));
                if (!used.Add(key)) continue;
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
                    if (j <= i || !Sweep(point, points[j], true)) continue;
                    float length = Vector3.Distance(point, points[j]);
                    bool door = CrossesAny(point, points[j], doors);
                    edges[i].Add(new Edge(j, length, door));
                    edges[j].Add(new Edge(i, length, door));
                }
            }
            cost = new float[points.Count];
            previous = new int[points.Count];
            state = new byte[points.Count];
            goalDistance = new float[points.Count];
        }

        Vector2Int Cell(Vector3 point) => new Vector2Int(Mathf.FloorToInt(point.x / linkRadius), Mathf.FloorToInt(point.z / linkRadius));
        static Vector3 Lower(Vector3 p) => p + Vector3.up * (FootClearance + BodyRadius);
        static Vector3 Upper(Vector3 p) => p + Vector3.up * (TopClearance - BodyRadius);

        internal static float PlanarDistanceSquared(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x, z = a.z - b.z;
            return x * x + z * z;
        }

        bool SampleFloor(float x, float z, out float y)
        {
            if (terrain == FloorKind.Port) return PortTerrain.TryWalk(x, z, out y);
            if (terrain == FloorKind.Quarry) return QuarryTerrain.TryWalk(x, z, out y);
            y = groundHeight;
            return true;
        }

        internal bool TryFloor(Vector3 point, out Vector3 floor)
        {
            bool standable = SampleFloor(point.x, point.z, out float y);
            floor = new Vector3(point.x, y, point.z);
            return standable;
        }

        IEnumerable<int> CloseNodes(Vector3 point)
        {
            var cell = Cell(point);
            float radiusSq = linkRadius * linkRadius;
            for (int x = cell.x - 1; x <= cell.x + 1; x++)
                for (int z = cell.y - 1; z <= cell.y + 1; z++)
                    if (buckets.TryGetValue(new Vector2Int(x, z), out var indices))
                        foreach (int i in indices)
                            if (PlanarDistanceSquared(Points[i], point) <= radiusSq) yield return i;
        }

        internal List<Vector3> ReachablePoints(Vector3 origin)
        {
            var result = new List<Vector3>();
            if (!TryFloor(origin, out var floor) || !IsClear(floor, false)) return result;
            var reached = new bool[Points.Count];
            var frontier = new Queue<int>();
            foreach (int i in CloseNodes(floor))
            {
                if (!Sweep(floor, Points[i])) continue;
                reached[i] = true;
                frontier.Enqueue(i);
            }
            while (frontier.Count > 0)
            {
                int current = frontier.Dequeue();
                result.Add(Points[current]);
                foreach (var edge in edges[current])
                {
                    if (reached[edge.To] || edge.Door && !Sweep(Points[current], Points[edge.To])) continue;
                    reached[edge.To] = true;
                    frontier.Enqueue(edge.To);
                }
            }
            return result;
        }

        internal bool CanStandAt(Vector3 position) => TryFloor(position, out var p) && IsClear(p, false);

        bool IsClear(Vector3 p, bool ignoreDoorLeaf)
        {
            int count = Physics.OverlapCapsuleNonAlloc(Lower(p), Upper(p), BodyRadius, overlaps,
                obstacleMask, QueryTriggerInteraction.Ignore);
            if (count == overlaps.Length) return false;
            for (int i = 0; i < count; i++)
                if (Blocks(overlaps[i], ignoreDoorLeaf)) return false;
            return true;
        }

        bool Blocks(Collider collider, bool ignoreDoorLeaf)
        {
            if (collider is CharacterController) return false;
            if (quarryFloors != null && quarryFloors.Contains(collider)) return false;
            if (terrain == FloorKind.Port && collider.TryGetComponent<FootstepSurface>(out var surface) && surface.IsNavigationFloor) return false;
            if (ignoreDoorLeaf && doors.Length > 0)
            {
                var door = collider.GetComponentInParent<HarborDoor>();
                if (door != null && door.IsLeaf(collider)) return false;
            }
            return true;
        }

        bool Cast(Vector3 from, Vector3 to, bool ignoreDoorLeaf, bool avoidActors, CharacterController actor)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < .001f) return true;
            int count = Physics.CapsuleCastNonAlloc(Lower(from), Upper(from), BodyRadius, delta / distance,
                hits, distance, avoidActors ? Physics.AllLayers : obstacleMask, QueryTriggerInteraction.Ignore);
            if (count == hits.Length) return false;
            for (int i = 0; i < count; i++)
            {
                var collider = hits[i].collider;
                if (collider is CharacterController other)
                {
                    if (avoidActors && other != actor && Approaching(from, to, other.bounds.center)) return false;
                }
                else if (Blocks(collider, ignoreDoorLeaf)) return false;
            }
            return true;
        }

        bool TerrainTrace(Vector3 from, Vector3 to, out bool straight)
        {
            straight = true;
            if (terrain == FloorKind.Flat) return true;
            float horizontal = Mathf.Sqrt(PlanarDistanceSquared(from, to));
            int count = Mathf.Max(1, Mathf.CeilToInt(horizontal / 1.15f));
            float previousHeight = from.y;
            float walkGradient = terrain == FloorKind.Quarry ? QuarryTerrain.WalkGradient : PortTerrain.WalkGradient;
            float maxRise = .27f + horizontal / count * walkGradient;
            for (int i = 1; i <= count; i++)
            {
                float t = i / (float)count;
                Vector3 p = Vector3.LerpUnclamped(from, to, t);
                if (!SampleFloor(p.x, p.z, out float height) || Mathf.Abs(height - previousHeight) > maxRise) return false;
                if (Mathf.Abs(p.y - height) > .11f) straight = false;
                previousHeight = height;
            }
            return true;
        }

        bool Sweep(Vector3 from, Vector3 to, bool ignoreDoorLeaf = false, bool avoidActors = false, CharacterController actor = null)
        {
            if (!TerrainTrace(from, to, out bool straight)) return false;
            if (straight) return Cast(from, to, ignoreDoorLeaf, avoidActors, actor);
            int pieces = Mathf.CeilToInt(Mathf.Sqrt(PlanarDistanceSquared(from, to)) / 3.5f);
            Vector3 last = from;
            for (int i = 1; i <= pieces; i++)
            {
                if (!TryFloor(Vector3.LerpUnclamped(from, to, i / (float)pieces), out var next)
                    || !IsClear(next, ignoreDoorLeaf) || !Cast(last, next, ignoreDoorLeaf, avoidActors, actor)) return false;
                last = next;
            }
            return true;
        }

        public bool CanTraverse(Vector3 from, Vector3 to) =>
            TryFloor(from, out var a) && TryFloor(to, out var b) && IsClear(a, false) && IsClear(b, false) && Sweep(a, b);

        public bool TryLocalStep(Vector3 position, Vector3 waypoint, CharacterController actor, out Vector3 direction)
        {
            direction = Vector3.zero;
            Vector3 delta = waypoint - position;
            delta.y = 0;
            float distance = delta.magnitude;
            if (distance < .01f || !TryFloor(position, out var from) || Mathf.Abs(from.y - position.y) > .8f || !IsClear(from, false))
                return false;
            Vector3 heading = delta / distance;
            float ahead = Mathf.Min(1.2f, distance);
            if (TryHeading(from, heading, ahead, actor)) { direction = heading; return true; }

            // A local sidestep must still point toward the route; a dead end is left to the path search.
            float sign = (actor != null && (actor.GetInstanceID() & 1) != 0) ? -1 : 1;
            for (int i = 0; i < 4; i++)
            {
                float angle = (i < 2 ? 33 : 63) * (i % 2 == 0 ? sign : -sign);
                Vector3 side = Quaternion.Euler(0, angle, 0) * heading;
                if (TryHeading(from, side, ahead, actor)) { direction = side; return true; }
            }
            return false;
        }

        bool TryHeading(Vector3 from, Vector3 heading, float distance, CharacterController actor)
        {
            if (!TryFloor(from + heading * distance, out var to) || !Sweep(from, to, false, true, actor)) return false;
            int count = Physics.OverlapCapsuleNonAlloc(Lower(to), Upper(to), BodyRadius, overlaps,
                Physics.AllLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (overlaps[i] is CharacterController other)
                {
                    if (other != actor && Approaching(from, to, other.bounds.center)) return false;
                }
                else if (Blocks(overlaps[i], false)) return false;
            }
            return count < overlaps.Length;
        }

        static bool Approaching(Vector3 from, Vector3 to, Vector3 actorCenter)
        {
            return (to.x - from.x) * (actorCenter.x - from.x) + (to.z - from.z) * (actorCenter.z - from.z) > .01f;
        }

        static bool CrossesAny(Vector3 from, Vector3 to, IReadOnlyList<Bounds> samples)
        {
            for (int i = 0; i < samples.Count; i++)
            {
                Bounds b = samples[i];
                if (Mathf.Min(from.x, to.x) > b.max.x + BodyRadius || Mathf.Max(from.x, to.x) < b.min.x - BodyRadius
                    || Mathf.Min(from.z, to.z) > b.max.z + BodyRadius || Mathf.Max(from.z, to.z) < b.min.z - BodyRadius) continue;
                Vector2 delta = new Vector2(to.x - from.x, to.z - from.z);
                Vector2 toward = new Vector2(b.center.x - from.x, b.center.z - from.z);
                Vector2 nearest = delta * Mathf.Clamp01(Vector2.Dot(toward, delta) / Mathf.Max(.0001f, delta.sqrMagnitude));
                float extent = new Vector2(b.extents.x, b.extents.z).magnitude + BodyRadius;
                if ((toward - nearest).sqrMagnitude <= extent * extent) return true;
            }
            return false;
        }

        public List<Vector3> Path(Vector3 start, Vector3 end)
        {
            var result = new List<Vector3>();
            if (!TryFloor(start, out var from) || !TryFloor(end, out var to) || !IsClear(from, false)
                || !TryGoal(from, ref to) || PlanarDistanceSquared(from, to) < .0025f) return result;
            if (PlanarDistanceSquared(from, to) <= 95 * 95 && Sweep(from, to)) { result.Add(to); return result; }

            Array.Clear(state, 0, state.Length);
            for (int i = 0; i < goalDistance.Length; i++) goalDistance[i] = -1;
            open.Clear();
            foreach (int i in CloseNodes(from))
            {
                if (!IsClear(Points[i], false) || !Sweep(from, Points[i])) continue;
                previous[i] = -1;
                cost[i] = Vector3.Distance(from, Points[i]);
                state[i] = 1;
                Push(i, cost[i] + Vector3.Distance(Points[i], to));
            }
            if (open.Count == 0) return result;

            bool hasGoalLink = false;
            foreach (int i in CloseNodes(to))
                if (IsClear(Points[i], false) && Sweep(Points[i], to))
                { goalDistance[i] = Vector3.Distance(Points[i], to); hasGoalLink = true; }
            if (!hasGoalLink) return result;

            while (open.Count > 0)
            {
                int current = Pop();
                if (state[current] == 2) continue;
                if (goalDistance[current] >= 0)
                {
                    CopyPath(from, to, current, result);
                    break;
                }
                state[current] = 2;
                foreach (var edge in edges[current])
                {
                    int next = edge.To;
                    float nextCost = cost[current] + edge.Length;
                    if (state[next] == 2 || (state[next] == 1 && nextCost >= cost[next])) continue;
                    if (edge.Door && (!IsClear(Points[next], false) || !Sweep(Points[current], Points[next]))) continue;
                    previous[next] = current;
                    cost[next] = nextCost;
                    state[next] = 1;
                    Push(next, nextCost + Vector3.Distance(Points[next], to));
                }
            }
            return result;
        }

        bool TryGoal(Vector3 from, ref Vector3 to)
        {
            if (IsClear(to, false)) return true;
            Vector3 toward = from - to;
            toward.y = 0;
            if (toward.sqrMagnitude < .01f) return false;
            toward.Normalize();
            for (int radius = 1; radius <= 2; radius++)
                for (int i = 0; i < 8; i++)
                {
                    int turn = i % 2 == 0 ? -i / 2 : (i + 1) / 2;
                    Vector3 offset = Quaternion.Euler(0, turn * 45, 0) * toward * radius;
                    if (!TryFloor(to + offset, out var nearby) || !IsClear(nearby, false)) continue;
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
                    if (PlanarDistanceSquared(from, chain[shortcut]) <= 82 * 82 && Sweep(from, chain[shortcut]))
                    { next = shortcut; break; }
                if (PlanarDistanceSquared(chain[next], from) >= .0025f)
                {
                    from = chain[next];
                    result.Add(from);
                }
                i = next - 1;
            }
        }

        void Push(int node, float score)
        {
            int index = open.Count;
            open.Add(default);
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (open[parent].Score <= score) break;
                open[index] = open[parent];
                index = parent;
            }
            open[index] = new OpenEntry(node, score);
        }

        int Pop()
        {
            int best = open[0].Node;
            int count = open.Count - 1;
            var tail = open[count];
            open.RemoveAt(count);
            if (count == 0) return best;
            int index = 0;
            while (index * 2 + 1 < count)
            {
                int child = index * 2 + 1;
                if (child + 1 < count && open[child + 1].Score < open[child].Score) child++;
                if (open[child].Score >= tail.Score) break;
                open[index] = open[child];
                index = child;
            }
            open[index] = tail;
            return best;
        }
    }
}
