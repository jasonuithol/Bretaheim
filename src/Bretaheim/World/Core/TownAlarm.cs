using System;
using Bretaheim.Data;

namespace Bretaheim.WorldGen.Core
{
    /// <summary>
    /// The rules of a town under attack (Unity-free): how many guards a fight needs, where a
    /// townsperson hides, when reinforcements come.
    /// </summary>
    public static class TownAlarm
    {
        /// <summary>One guard per this much monster health (30 Ultima hit points on the scale: a troll is a one-guard job, a dragon three or four).</summary>
        public const float HealthPerGuard = 30 * Ultima.Scale;
        /// <summary>Reinforcements come after a fight this long (seconds), or when no guard is left standing.</summary>
        public const float ReinforceAfter = 300f;
        /// <summary>The alarm ends this long (seconds) after the last monster in town.</summary>
        public const float QuietFor = 5f;

        /// <summary>Guards a fight needs (at least one, at most all there are).</summary>
        public static int GuardsNeeded(float monsterHealth, int guards)
            => Math.Max(1, Math.Min(Math.Max(guards, 1), (int)Math.Ceiling(monsterHealth / HealthPerGuard)));

        /// <summary>Where a townsperson hides: the place of their schedule that is a bed (home), else their first place.</summary>
        public static (int z, int cell) Home(SettlementPlan plan, NpcSchedule s)
        {
            for (int k = 0; k < 3; k++)
            {
                int cell = s.Y[k] * SettlementPlan.Size + s.X[k];
                if (plan.Seats.TryGetValue((s.Z[k], cell), out var seat) && seat.Bed) return (s.Z[k], cell);
            }
            return (s.Z[0], s.Y[0] * SettlementPlan.Size + s.X[0]);
        }

        public static bool ReinforcementsDue(float fighting, int guardsStanding, int guards)
            => fighting >= ReinforceAfter || (guards > 0 && guardsStanding == 0);

        /// <summary>The walkable ground tile on the town's edge nearest a point (where reinforcements arrive), or −1.</summary>
        public static int EdgeNear(SettlementPlan plan, float x, float z)
        {
            const int N = SettlementPlan.Size;
            int best = -1;
            float bestD = float.MaxValue;
            for (int c = 0; c < N * N; c++)
            {
                int tx = c % N, ty = c / N;
                if (!TownPath.Walkable(plan, 0, c)) continue;
                int edge = Math.Min(Math.Min(tx, ty), Math.Min(N - 1 - tx, N - 1 - ty));
                if (edge > 1) continue;
                var (cx, _, cz) = plan.TilePos(0, c);
                float d = (cx - x) * (cx - x) + (cz - z) * (cz - z);
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }
    }
}
