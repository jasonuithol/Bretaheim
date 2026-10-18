using System;
using System.Collections.Generic;
using U5InValheim.Data;

namespace U5InValheim.WorldGen.Core
{
    /// <summary>
    /// A townsperson's day (NPC.OVL move_npcs, in real time): at each hour of their schedule they
    /// walk to that place (across storeys by the ladders, on the spot like a player climbing), and
    /// there stay put (AI 0) or wander near it (AI 1 within 3 tiles, AI 2 within 6). With no way
    /// there they simply appear there, as the original teleports NPCs it cannot path.
    /// Unity-free: the game moves the body to <see cref="X"/>/<see cref="Y"/>/<see cref="Z"/>.
    /// </summary>
    public class TownWalker
    {
        /// <summary>Walking pace, and the most they hurry to (a jog) so that no trip takes longer than TripTime seconds.</summary>
        public const float Speed = 1.6f, Hurry = 4f, TripTime = 50f;
        const int N = SettlementPlan.Size;

        readonly SettlementPlan plan;
        readonly NpcSchedule schedule;
        uint rng;

        /// <summary>Storey and settlement-local position (metres; Y is the storey's floor).</summary>
        public int Floor { get; private set; }
        public float X { get; private set; }
        public float Y { get; private set; }
        public float Z { get; private set; }
        /// <summary>Direction of the last step (unit, in x/z), for facing.</summary>
        public float HeadingX { get; private set; }
        public float HeadingZ { get; private set; } = 1;
        public bool Moving => path != null && path.Count > 0;

        List<(int z, int cell)> path;
        int goalPlace = -1;
        float wanderIn, pace = Speed;

        public TownWalker(SettlementPlan plan, NpcSchedule schedule, int floor, float x, float z, int seed)
        {
            this.plan = plan;
            this.schedule = schedule;
            Floor = floor;
            X = x;
            Z = z;
            Y = plan.LevelY(floor);
            rng = (uint)seed * 2654435761u + 1;
            wanderIn = 4 + seed % 7;
        }

        public int Cell => plan.CellAt(X, Z);

        /// <summary>Advances dt seconds at the given hour of the day (0..23).</summary>
        public void Tick(float dt, int hour)
        {
            int place = schedule.PlaceAt(hour);
            int post = schedule.Y[place] * N + schedule.X[place];
            int ai = schedule.Ai[place];
            if (place != goalPlace)
            {
                goalPlace = place;
                Head(schedule.Z[place], post);
            }
            else if (!Moving && (ai == 1 || ai == 2) && (wanderIn -= dt) <= 0)
            {
                wanderIn = 6 + Next() % 10;
                int reach = ai == 1 ? 3 : 6;
                int tx = Clamp(post % N + (int)(Next() % (2 * reach + 1)) - reach);
                int ty = Clamp(post / N + (int)(Next() % (2 * reach + 1)) - reach);
                int target = ty * N + tx, z = schedule.Z[place];
                // the whole way stays near the post, on its storey (the original wanders a step at a time)
                var way = Floor == z && TownPath.Walkable(plan, z, target) ? TownPath.Find(plan, Floor, Cell, z, target) : null;
                if (way != null && way.TrueForAll(st => st.z == z && Math.Max(Math.Abs(st.cell % N - post % N), Math.Abs(st.cell / N - post / N)) <= reach))
                {
                    path = way;
                    pace = Speed;
                }
            }
            Walk(dt);
        }

        void Head(int z, int cell)
        {
            path = TownPath.Find(plan, Floor, Cell, z, cell);
            if (path == null) Place(z, cell);
            pace = path == null ? Speed : Math.Max(Speed, Math.Min(Hurry, path.Count * plan.Tile / TripTime));
        }

        void Walk(float dt)
        {
            float step = pace * dt;
            while (Moving && step > 0)
            {
                var (z, cell) = path[0];
                if (z != Floor)
                {
                    Place(z, cell);
                    path.RemoveAt(0);
                    continue;
                }
                var (tx, _, tz) = plan.TilePos(z, cell);
                float dx = tx - X, dz = tz - Z, d = (float)Math.Sqrt(dx * dx + dz * dz);
                if (d <= step)
                {
                    X = tx;
                    Z = tz;
                    step -= d;
                    path.RemoveAt(0);
                }
                else
                {
                    X += dx / d * step;
                    Z += dz / d * step;
                    step = 0;
                }
                if (d > 0.001f) { HeadingX = dx / d; HeadingZ = dz / d; }
            }
        }

        void Place(int z, int cell)
        {
            var (x, y, lz) = plan.TilePos(z, cell);
            Floor = z;
            X = x;
            Y = y;
            Z = lz;
        }

        static int Clamp(int v) => Math.Max(0, Math.Min(N - 1, v));

        uint Next()
        {
            rng ^= rng << 13;
            rng ^= rng >> 17;
            rng ^= rng << 5;
            return rng;
        }
    }
}
