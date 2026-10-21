using System.Collections.Generic;

namespace Bretaheim.WorldGen.Core
{
    /// <summary>
    /// Townsfolk paths (NPC.OVL's BFS, across storeys): breadth-first over the walkable tiles of
    /// each storey and the ladders between them. The goal itself may be a tile nobody walks
    /// through (a bed, a shop counter's stool behind a table). When the floor offers no clear way,
    /// they squeeze past furniture and plants (never through walls or water).
    /// </summary>
    public static class TownPath
    {
        const int N = SettlementPlan.Size;

        /// <summary>The steps (storey, cell) after the start up to and including the goal, or null when there is no way.</summary>
        public static List<(int z, int cell)> Find(SettlementPlan plan, int z0, int c0, int z1, int c1)
            => Search(plan, z0, c0, z1, c1, false) ?? Search(plan, z0, c0, z1, c1, true);

        static List<(int z, int cell)> Search(SettlementPlan plan, int z0, int c0, int z1, int c1, bool squeeze)
        {
            var start = (z0, c0);
            var goal = (z1, c1);
            if (start == goal) return new List<(int, int)>();
            var from = new Dictionary<(int, int), (int, int)> { [start] = start };
            var queue = new Queue<(int z, int cell)>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var at = queue.Dequeue();
                foreach (var next in Steps(plan, at.z, at.cell))
                {
                    if (from.ContainsKey(next)) continue;
                    bool isGoal = next == goal;
                    if (!isGoal && !Walkable(plan, next.z, next.cell) && !(squeeze && plan.Squeeze.TryGetValue(next.z, out var sq) && sq[next.cell])) continue;
                    from[next] = at;
                    if (isGoal)
                    {
                        var path = new List<(int, int)>();
                        for (var s = next; s != start; s = from[s]) path.Add(s);
                        path.Reverse();
                        return path;
                    }
                    queue.Enqueue(next);
                }
            }
            return null;
        }

        public static bool Walkable(SettlementPlan plan, int z, int cell)
            => cell >= 0 && cell < N * N && plan.Walk.TryGetValue(z, out var w) && w[cell];

        static IEnumerable<(int z, int cell)> Steps(SettlementPlan plan, int z, int c)
        {
            int x = c % N, y = c / N;
            if (x > 0) yield return (z, c - 1);
            if (x < N - 1) yield return (z, c + 1);
            if (y > 0) yield return (z, c - N);
            if (y < N - 1) yield return (z, c + N);
            foreach (var l in plan.Links)
                if (l.Z == z && l.Cell == c) yield return (l.ToZ, c);
        }
    }
}
