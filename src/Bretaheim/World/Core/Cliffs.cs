using System;
using System.Collections.Generic;
using Bretaheim.Data;

namespace Bretaheim.WorldGen.Core
{
    /// <summary>
    /// Cliffs at the waterfalls (MASTER_PLAN §5.1, Constructing world assets.txt "A Note On
    /// Elevation": "if you need to place cliffs anywhere, try using the location of these waterfalls
    /// (tiles 212 - 215)"). Valheim has one water level, so a river cannot fall; the land does
    /// instead: upstream of each waterfall tile the ground stands <see cref="Rise"/> metres higher,
    /// a cliff face across the river at the waterfall and the high ground falling back over a few
    /// tiles upstream and to the sides. The river's channel, carved after this, cuts a gorge through
    /// it. Downstream is the waterfall's river (or sea) neighbour nearest the sea along the rivers.
    /// Unity-free, periodic like the rest of <see cref="BritanniaShape"/>.
    /// </summary>
    public sealed class Cliffs
    {
        /// <summary>How much higher the land stands behind a waterfall (metres).</summary>
        public const float Rise = 14f;
        /// <summary>Across the river the high ground reaches this far (tiles) before falling away over <see cref="Fade"/>; upstream it holds <see cref="Back"/> tiles.</summary>
        public const double Side = 1.2, Back = 1.8, Fade = 1.5;
        /// <summary>The cliff face: the rise happens over this much of a tile (32 m: about 6 m).</summary>
        public const double Face = 0.2;

        const int T = BritanniaShape.MapTiles;
        /// <summary>Each waterfall: its tile centre (tile units) and the way downstream (a unit step, tile units).</summary>
        public readonly List<(double x, double y, double dx, double dy)> Falls = new List<(double, double, double, double)>();

        public Cliffs(WorldMap map, TerrainTable terrain)
        {
            bool Fall(int t) => t >= 0xd4 && t <= 0xd7;
            WaterKind Water(int x, int y) => terrain[map[x, y]].Water;
            // distance to the sea along the rivers (waterfalls and bridges are river too)
            var dist = new int[T * T];
            var queue = new Queue<int>();
            int[] ox = { 1, -1, 0, 0 }, oy = { 0, 0, 1, -1 };
            int W(int v) => ((v % T) + T) % T;
            bool River(int x, int y) => Water(x, y) == WaterKind.River || Fall(map[x, y]);
            for (int i = 0; i < dist.Length; i++)
            {
                dist[i] = -1;
                int x = i % T, y = i / T;
                if (!River(x, y)) continue;
                for (int k = 0; k < 4; k++)
                    if (Water(W(x + ox[k]), W(y + oy[k])) == WaterKind.Sea) { dist[i] = 0; queue.Enqueue(i); break; }
            }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue(), x = i % T, y = i / T;
                for (int k = 0; k < 4; k++)
                {
                    int nx = W(x + ox[k]), ny = W(y + oy[k]), j = ny * T + nx;
                    if (dist[j] >= 0 || !River(nx, ny)) continue;
                    dist[j] = dist[i] + 1;
                    queue.Enqueue(j);
                }
            }
            for (int y = 0; y < T; y++)
                for (int x = 0; x < T; x++)
                {
                    if (!Fall(map[x, y])) continue;
                    int best = -1, bestDist = int.MaxValue;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = W(x + ox[k]), ny = W(y + oy[k]);
                        // (the sea first, then the river tile nearest it; rivers that never reach the sea are no way down)
                        int d = Water(nx, ny) == WaterKind.Sea ? -1 : River(nx, ny) && dist[ny * T + nx] >= 0 ? dist[ny * T + nx] : int.MaxValue;
                        if (d < bestDist) { bestDist = d; best = k; }
                    }
                    if (best < 0) continue;
                    Falls.Add((x + 0.5, y + 0.5, ox[best], oy[best]));
                }
        }

        /// <summary>Metres added to the ground at continuous tile coordinates (u east, v south).</summary>
        public double Offset(double u, double v)
        {
            double sum = 0;
            foreach (var (fx, fy, dx, dy) in Falls)
            {
                double du = Wrap(u - fx), dv = Wrap(v - fy);
                if (Math.Abs(du) > Back + Fade + 1 || Math.Abs(dv) > Back + Fade + 1) continue;
                double up = -(du * dx + dv * dy), across = Math.Abs(du * dy - dv * dx);
                double s = SmoothStep(up / Face + 0.5) * (1 - SmoothStep((across - Side) / Fade)) * (1 - SmoothStep((up - Back) / Fade));
                sum = Math.Max(sum, s);
            }
            return Rise * sum;
        }

        static double Wrap(double d) => d - T * Math.Round(d / T);
        static double SmoothStep(double t) { t = Math.Max(0, Math.Min(1, t)); return t * t * (3 - 2 * t); }
    }
}
