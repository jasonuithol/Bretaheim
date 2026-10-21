using System;
using System.Collections.Generic;

namespace Bretaheim.WorldGen.Core
{
    /// <summary>
    /// Who stands where in a settlement: one townsperson per tile once they stop (the original
    /// never puts two NPCs on one tile), so two guards sent to the same door stand side by side.
    /// </summary>
    public class TownCrowd
    {
        const int N = SettlementPlan.Size;
        readonly Dictionary<(int, int), object> at = new Dictionary<(int, int), object>();
        readonly Dictionary<object, (int, int)> of = new Dictionary<object, (int, int)>();

        public bool Taken(int z, int cell, object who) => at.TryGetValue((z, cell), out var o) && o != who;

        /// <summary>Takes the tile unless someone else stands there (leaving any tile held before).</summary>
        public bool TryTake(int z, int cell, object who)
        {
            if (Taken(z, cell, who)) return false;
            Leave(who);
            at[(z, cell)] = who;
            of[who] = (z, cell);
            return true;
        }

        public void Leave(object who)
        {
            if (!of.TryGetValue(who, out var k)) return;
            at.Remove(k);
            of.Remove(who);
        }

        /// <summary>The nearest free floor tile within reach of a cell on its storey, or −1.</summary>
        public int NearestFree(SettlementPlan plan, int z, int cell, object who, int reach = 3)
        {
            var seen = new HashSet<int> { cell };
            var queue = new Queue<int>();
            queue.Enqueue(cell);
            while (queue.Count > 0)
            {
                int c = queue.Dequeue(), x = c % N, y = c / N;
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = x + dx, ny = y + dy, n = ny * N + nx;
                    if (nx < 0 || ny < 0 || nx >= N || ny >= N || !seen.Add(n)) continue;
                    if (Math.Max(Math.Abs(nx - cell % N), Math.Abs(ny - cell / N)) > reach || !TownPath.Walkable(plan, z, n)) continue;
                    if (!Taken(z, n, who)) return n;
                    queue.Enqueue(n);
                }
            }
            return -1;
        }
    }
}
