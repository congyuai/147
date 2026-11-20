using System.Collections.Generic;
using UnityEngine;

namespace Dustline
{
    public sealed class NavigationGrid
    {
        const int Size = 47;
        const float Spacing = 2;
        readonly bool[] blocked = new bool[Size * Size];
        readonly bool[,] edges = new bool[Size * Size, 4];
        static readonly int[] dx = { 1, -1, 0, 0 };
        static readonly int[] dz = { 0, 0, 1, -1 };

        public NavigationGrid()
        {
            Physics.SyncTransforms();
            for (int i = 0; i < blocked.Length; i++)
                blocked[i] = Physics.CheckBox(Position(i) + Vector3.up, new Vector3(0.48f, 0.7f, 0.48f), Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < blocked.Length; i++)
                for (int direction = 0; direction < 4; direction++)
                {
                    var step = new Vector3(dx[direction], 0, dz[direction]);
                    edges[i, direction] = !Physics.BoxCast(Position(i) + Vector3.up, new Vector3(0.4f, 0.7f, 0.4f),
                        step, Quaternion.identity, Spacing, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                }
        }
        Vector3 Position(int i) => new Vector3((i % Size - Size / 2) * Spacing, 0.05f, (i / Size - Size / 2) * Spacing);
        int Nearest(Vector3 point)
        {
            int best = -1;
            float distance = float.PositiveInfinity;
            for (int i = 0; i < blocked.Length; i++)
            {
                if (blocked[i]) continue;
                float candidate = (point - Position(i)).sqrMagnitude;
                if (candidate < distance) { best = i; distance = candidate; }
            }
            return best;
        }
        int Heuristic(int a, int b) => Mathf.Abs(a % Size - b % Size) + Mathf.Abs(a / Size - b / Size);

        public List<Vector3> Path(Vector3 start, Vector3 end)
        {
            int from = Nearest(start), to = Nearest(end);
            var result = new List<Vector3>();
            if (from < 0 || to < 0) return result;
            var open = new List<int> { from };
            var closed = new bool[blocked.Length];
            var cost = new int[blocked.Length];
            var previous = new int[blocked.Length];
            for (int i = 0; i < cost.Length; i++) { cost[i] = int.MaxValue; previous[i] = -1; }
            cost[from] = 0;
            while (open.Count > 0)
            {
                int bestIndex = 0;
                for (int i = 1; i < open.Count; i++)
                    if (cost[open[i]] + Heuristic(open[i], to) < cost[open[bestIndex]] + Heuristic(open[bestIndex], to)) bestIndex = i;
                int current = open[bestIndex];
                open.RemoveAt(bestIndex);
                if (current == to)
                {
                    for (int n = to; n != -1; n = previous[n]) result.Add(Position(n));
                    result.Reverse();
                    return result;
                }
                closed[current] = true;
                for (int direction = 0; direction < 4; direction++)
                {
                    int x = current % Size + dx[direction], z = current / Size + dz[direction];
                    if (x < 0 || z < 0 || x >= Size || z >= Size) continue;
                    int next = x + z * Size;
                    if (blocked[next] || !edges[current, direction] || closed[next] || cost[current] + 1 >= cost[next]) continue;
                    previous[next] = current;
                    cost[next] = cost[current] + 1;
                    if (!open.Contains(next)) open.Add(next);
                }
            }
            return result;
        }
    }
}
