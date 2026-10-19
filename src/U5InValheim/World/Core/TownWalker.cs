using System;
using System.Collections.Generic;
using U5InValheim.Data;

namespace U5InValheim.WorldGen.Core
{
    public enum Pose { Stand, Sit, Lie }

    /// <summary>
    /// A townsperson's day (NPC.OVL move_npcs, in real time): at each hour of their schedule they
    /// walk to that place (across storeys by the ladders, on the spot like a player climbing), and
    /// there stay put (AI 0) or wander near it (AI 1 within 3 tiles, AI 2 within 6). With no way
    /// there they simply appear there, as the original teleports NPCs it cannot path. Stopping on
    /// a chair they sit, on a bed they lie down; on a tile someone else stands on, they take the
    /// nearest free one (<see cref="TownCrowd"/>).
    /// Unity-free: the game moves the body to <see cref="X"/>/<see cref="Y"/>/<see cref="Z"/>.
    /// </summary>
    public class TownWalker
    {
        /// <summary>Walking pace, and the most they hurry to (a jog) so that no trip takes longer than TripTime seconds.</summary>
        public const float Speed = 1.6f, Hurry = 4f, TripTime = 50f;
        const int N = SettlementPlan.Size;

        readonly SettlementPlan plan;
        readonly NpcSchedule schedule;
        readonly TownCrowd crowd;
        uint rng;

        /// <summary>Storey and settlement-local position (metres; Y is the storey's floor).</summary>
        public int Floor { get; private set; }
        public float X { get; private set; }
        public float Y { get; private set; }
        public float Z { get; private set; }
        /// <summary>Which way they face (unit, in x/z): where they walk, and once there, <see cref="Settle"/>.</summary>
        public float HeadingX { get; private set; }
        public float HeadingZ { get; private set; } = 1;
        public bool Moving => path != null && path.Count > 0;
        /// <summary>How fast they are going (walking, or hurrying on a long trip or a chase).</summary>
        public float Pace => pace;
        /// <summary>Standing, sitting on a chair or lying in bed; resting, the body is at Rest* facing RestYaw.</summary>
        public Pose Pose { get; private set; }
        public float RestX { get; private set; }
        public float RestY { get; private set; }
        public float RestZ { get; private set; }
        public float RestYaw { get; private set; }

        List<(int z, int cell)> path;
        int goalPlace = -1;
        float wanderIn, pace = Speed;

        public TownWalker(SettlementPlan plan, NpcSchedule schedule, int floor, float x, float z, int seed, TownCrowd crowd = null)
        {
            this.plan = plan;
            this.schedule = schedule;
            this.crowd = crowd;
            Floor = floor;
            X = x;
            Z = z;
            Y = plan.LevelY(floor);
            rng = (uint)seed * 2654435761u + 1;
            wanderIn = 4 + seed % 7;
        }

        public int Cell => plan.CellAt(X, Z);

        int chaseFloor, chaseCell = -1;

        /// <summary>Chasing (a guard after a monster): running to that tile by the floor's paths, not the schedule.</summary>
        public bool Chasing => chaseCell >= 0;

        /// <summary>Runs after something on that tile (the path is found again when it moves).</summary>
        public void Chase(int floor, int cell)
        {
            if (Chasing && floor == chaseFloor && cell == chaseCell && Moving) return;
            Leave();
            chaseFloor = floor;
            chaseCell = cell;
            path = TownPath.Find(plan, Floor, Cell, floor, cell);
            pace = Hurry;
        }

        /// <summary>Hurried there and stopped (a chase or errand done).</summary>
        public bool Arrived => Chasing && !Moving;

        /// <summary>Taken where the body is (a guard closing in on a monster or fighting it, off the paths).</summary>
        public void Follow(float x, float z)
        {
            path = null;
            X = x;
            Z = z;
        }

        /// <summary>Stops where they are (close enough to strike).</summary>
        public void Halt() => path = null;

        /// <summary>Back to the day's schedule (from wherever the chase ended).</summary>
        public void StopChase()
        {
            if (!Chasing) return;
            chaseCell = -1;
            goalPlace = -1;
            path = null;
        }

        /// <summary>Advances dt seconds at the given hour of the day (0..23).</summary>
        public void Tick(float dt, int hour)
        {
            if (Chasing)
            {
                Walk(dt);
                return;
            }
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
                var way = Floor == z && TownPath.Walkable(plan, z, target) && !(crowd?.Taken(z, target, this) ?? false) ? TownPath.Find(plan, Floor, Cell, z, target) : null;
                if (way != null && way.TrueForAll(st => st.z == z && Math.Max(Math.Abs(st.cell % N - post % N), Math.Abs(st.cell / N - post / N)) <= reach))
                {
                    Leave();
                    path = way;
                    pace = Speed;
                }
            }
            Walk(dt);
            if (!Moving && !settled) Settle();
        }

        bool settled;

        /// <summary>
        /// Standing still they face the furniture beside them (a counter, table, anvil, stove), else
        /// the most open way (the longest line of floor), never a wall.
        /// </summary>
        void Settle()
        {
            int c = Cell, x = c % N, y = c / N;
            if (crowd != null && !crowd.TryTake(Floor, c, this))
            {
                int free = crowd.NearestFree(plan, Floor, c, this);
                var way = free >= 0 ? TownPath.Find(plan, Floor, c, Floor, free) : null;
                if (way != null)
                {
                    path = way;
                    pace = Speed;
                    return;
                }
            }
            settled = true;
            if (plan.Seats.TryGetValue((Floor, c), out var seat))
            {
                Pose = seat.Bed ? Pose.Lie : Pose.Sit;
                RestX = seat.X;
                RestY = seat.Y;
                RestZ = seat.Z;
                RestYaw = seat.Yaw;
                HeadingX = (float)Math.Sin(seat.Yaw * Math.PI / 180);
                HeadingZ = (float)Math.Cos(seat.Yaw * Math.PI / 180);
                return;
            }
            plan.Squeeze.TryGetValue(Floor, out var furniture);
            // east, west, north (map y − 1), south
            var dirs = new[] { (1, 0), (-1, 0), (0, -1), (0, 1) };
            int best = -1, bestRun = 0;
            for (int i = 0; i < 4; i++)
            {
                int nx = x + dirs[i].Item1, ny = y + dirs[i].Item2;
                if (nx < 0 || ny < 0 || nx >= N || ny >= N) continue;
                if (furniture != null && furniture[ny * N + nx]) { best = i; break; }
                int run = 0;
                while (run < 6 && nx >= 0 && ny >= 0 && nx < N && ny < N && TownPath.Walkable(plan, Floor, ny * N + nx))
                {
                    run++;
                    nx += dirs[i].Item1;
                    ny += dirs[i].Item2;
                }
                if (run > bestRun) { bestRun = run; best = i; }
            }
            if (best < 0) return;
            HeadingX = dirs[best].Item1;
            HeadingZ = -dirs[best].Item2;
        }

        /// <summary>The tile they face when standing still (for tests).</summary>
        public int Facing => plan.CellAt(X + HeadingX * plan.Tile, Z + HeadingZ * plan.Tile);

        /// <summary>Gets up and leaves their tile to others.</summary>
        void Leave()
        {
            settled = false;
            Pose = Pose.Stand;
            crowd?.Leave(this);
        }

        void Head(int z, int cell)
        {
            Leave();
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
