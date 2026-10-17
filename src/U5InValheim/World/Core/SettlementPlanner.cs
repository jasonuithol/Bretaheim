using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using U5InValheim.Data;

namespace U5InValheim.WorldGen.Core
{
    /// <summary>One piece to spawn: prefab, pivot position in settlement-local metres, yaw, extra data.</summary>
    public struct Placement
    {
        public string Prefab;
        /// <summary>Pivot position: x east, y up from the settlement's ground, z north.</summary>
        public float X, Y, Z;
        public float Yaw;
        /// <summary>Scale (0 = unscaled): pieces squeezed into the 2 m tile or turned upside down.</summary>
        public float SX, SY, SZ;
        public bool Scaled => SX != 0;
        /// <summary>Doors: "key" or "magic" when locked, else null.</summary>
        public string Lock;
        /// <summary>Sign text, or null.</summary>
        public string Text;
        /// <summary>Container contents as prefab names (one item each), or null.</summary>
        public string[] Items;
        /// <summary>Climb destination (settlement-local) for stairs and ladders.</summary>
        public bool Climb;
        public float ClimbX, ClimbY, ClimbZ;
        public string ClimbVerb;

        public override string ToString() => $"{Prefab} ({X:0.##}, {Y:0.##}, {Z:0.##}) yaw {Yaw}";
    }

    /// <summary>What a settlement's 32×32 maps turn into in Valheim.</summary>
    public class SettlementPlan
    {
        public const int Size = SettlementMaps.Size;
        public Data.Location Location;
        public string Kind;
        public float Tile = 2f, Storey = 3f;
        public readonly List<Placement> Pieces = new List<Placement>();
        /// <summary>Terrain paint of each ground cell (row-major, y south).</summary>
        public readonly Paint[] GroundPaint = new Paint[Size * Size];
        /// <summary>Ground cells that are water (carved below sea level).</summary>
        public readonly bool[] Water = new bool[Size * Size];
        /// <summary>Where players appear (Iolo's hut), settlement-local.</summary>
        public float SpawnX, SpawnZ;
        public bool HasSpawn;

        public float HalfSize => Size * Tile / 2;
        public float LocalX(double tx) => (float)((tx - (Size / 2 - 0.5)) * Tile);
        public float LocalZ(double ty) => (float)(((Size / 2 - 0.5) - ty) * Tile);
        public bool HasWater => Water.Any(w => w);
    }

    /// <summary>
    /// Turns a settlement's maps into a <see cref="SettlementPlan"/> (MASTER_PLAN §5.2, Phase 2;
    /// authority: Constructing world assets.txt, Solution A). One town tile is 2 m × 2 m × 3 m.
    ///
    /// Height zones: the ground zone (floor tops at ground level), above it the 3 m wall zone where
    /// walls, furniture and loose objects live, then per storey an inter-level wood floor whose top
    /// is the top of the wall zone below and another wall zone; the roof zone starts on top of the
    /// last wall zone.
    ///
    /// Per floor: a flood fill from the map border through non-barrier tiles (walls, windows,
    /// doors, battlements are barriers) separates outside from inside; inside tiles get floors,
    /// barriers next to them become building walls. A wall tile is a solid block filling its
    /// tile. Ground floors and streets are terrain paint (planks get wooden floors).
    /// </summary>
    public class SettlementPlanner
    {
        readonly U5Data data;
        readonly TownTiles tiles;
        readonly PieceGeometry geo;

        public SettlementPlanner(U5Data data, TownTiles tiles, PieceGeometry geo)
        {
            this.data = data;
            this.tiles = tiles;
            this.geo = geo;
        }

        const int N = SettlementPlan.Size;

        class Level
        {
            public int Z;
            public byte[] Raw;
            public TileRule[] Rule = new TileRule[N * N];
            public bool[] Barrier = new bool[N * N];
            public bool[] Exterior = new bool[N * N];
            public bool[] Interior = new bool[N * N];
            /// <summary>Barriers built on this floor.</summary>
            public bool[] Built = new bool[N * N];
            /// <summary>Built barriers that belong to buildings (not the settlement's outer wall).</summary>
            public bool[] Building = new bool[N * N];
            public bool[] Covered = new bool[N * N];
            /// <summary>One-tile gaps in a wall line that lead into a building: built as open arches.</summary>
            public bool[] Doorway = new bool[N * N];
        }

        public SettlementPlan Plan(Data.Location loc, string kind, VBiome biome)
        {
            var plan = new SettlementPlan { Location = loc, Kind = kind };
            string styleKey = kind;
            if (loc.Number == 18) styleKey = "evil"; // Blackthorn's palace
            if (!tiles.Settlements.TryGetValue(styleKey, out var materials)) materials = ("stone", "shingle");
            var style = tiles.Styles[materials.walls];

            // First read every floor on its own; then again from the ground up, where a tile with
            // rooms, walls or a roof on the floor above is indoors (U5 draws buildings that way:
            // shop fronts and taverns are often open to the street on their own floor).
            int z0 = Math.Max(0, loc.MinZ);
            var alone = new List<Level>();
            for (int z = z0; z <= loc.MaxZ; z++) alone.Add(Analyse(data.Maps.Floor(loc, z), z, null, null));
            var levels = new List<Level>();
            for (int z = z0; z <= loc.MaxZ; z++)
            {
                var up = z + 1 - z0 < alone.Count ? alone[z + 1 - z0] : null;
                bool[] hint = null;
                if (up != null)
                {
                    hint = new bool[N * N];
                    for (int i = 0; i < N * N; i++) hint[i] = up.Interior[i] || up.Rule[i].Kind == TileKind.Roof || (up.Barrier[i] && up.Building[i]);
                }
                levels.Add(Analyse(data.Maps.Floor(loc, z), z, levels.Count > 0 ? levels[levels.Count - 1] : null, hint));
            }

            var ctx = new Ctx(plan, geo);
            Ground(ctx, levels[0]);
            for (int i = 0; i < levels.Count; i++)
            {
                var lv = levels[i];
                Floors(ctx, lv, i > 0 ? levels[i - 1] : null, style);
                Walls(ctx, lv, style, tiles.Cobble);
                Battlements(ctx, lv, style);
                Furniture(ctx, lv, levels, biome, Cottages(lv));
            }
            Roofs(ctx, levels, tiles.Roofs[materials.roof], tiles.Roofs[tiles.CottageRoof]);
            Signs(ctx, loc, levels);
            Spawn(plan, levels[0]);
            if (loc.Number == 13) IolosHut(ctx, levels[0]);
            return plan;
        }

        // ── analysis ────────────────────────────────────────────────────────────────────────

        Level Analyse(byte[] raw, int z, Level below, bool[] indoorAbove)
        {
            var lv = new Level { Z = z, Raw = raw };
            for (int i = 0; i < N * N; i++)
            {
                lv.Rule[i] = tiles[raw[i]];
                lv.Barrier[i] = lv.Rule[i].IsBarrier;
            }

            // A one-tile gap between two wall tiles is a doorway (many U5 houses have no door
            // tile): closed for the flood fill so the house does not leak into the street. A gap
            // with no room on either side is an alley and is reopened.
            for (int i = 0; i < N * N; i++)
            {
                if (lv.Barrier[i]) continue;
                bool ew = EW(lv, i), ns = NS(lv, i);
                lv.Doorway[i] = ew != ns;
            }
            Flood(lv);
            bool reopened = false;
            for (int i = 0; i < N * N; i++)
            {
                if (!lv.Doorway[i]) continue;
                bool ew = EW(lv, i);
                int a = ew ? i - N : i - 1, b = ew ? i + N : i + 1;
                if (!Enclosed(lv, a) && !Enclosed(lv, b)) { lv.Doorway[i] = false; reopened = true; }
            }
            if (reopened) Flood(lv);

            for (int i = 0; i < N * N; i++)
            {
                // inside: enclosed and not open-air ground; plank floors are always a building
                // (Iolo's barn is open on one side)
                lv.Interior[i] = !lv.Barrier[i] && !lv.Doorway[i] && !lv.Rule[i].IsOutdoor && lv.Rule[i].Kind != TileKind.Roof
                                 && (!lv.Exterior[i] || lv.Rule[i].Paint == Paint.Planks || (indoorAbove != null && indoorAbove[i]));
                // an upper floor only exists above something built on the floor below
                if (below != null && lv.Interior[i] && !below.Covered[i] && !below.Built[i]) lv.Interior[i] = false;
            }
            for (int i = 0; i < N * N; i++)
            {
                if (!lv.Barrier[i]) continue;
                bool nextToInside = Neighbours8(i).Any(n => lv.Interior[n]);
                if (lv.Rule[i].Kind == TileKind.Battlement)
                {
                    // battlements are wall-walks on top of the walls of the floor below
                    lv.Built[i] = below != null && (below.Built[i] || below.Covered[i]);
                    continue;
                }
                if (z == 0) lv.Built[i] = true;
                else if (below == null) lv.Built[i] = nextToInside; // first pass: the floor on its own
                else lv.Built[i] = nextToInside && (below.Built[i] || below.Covered[i]);
                lv.Building[i] = lv.Built[i] && nextToInside;
            }
            for (int i = 0; i < N * N; i++)
            {
                if (lv.Doorway[i] && !Neighbours4(i).Any(n => lv.Interior[n])) lv.Doorway[i] = false;
                if (lv.Doorway[i] && below != null && !(below.Built[i] || below.Covered[i])) lv.Doorway[i] = false;
                lv.Covered[i] = lv.Interior[i] || lv.Building[i] || lv.Doorway[i];
            }
            return lv;
        }

        static bool EW(Level lv, int i) { int x = i % N; return x > 0 && x < N - 1 && lv.Barrier[i - 1] && lv.Barrier[i + 1]; }
        static bool NS(Level lv, int i) { int y = i / N; return y > 0 && y < N - 1 && lv.Barrier[i - N] && lv.Barrier[i + N]; }

        static bool Enclosed(Level lv, int c)
            => c >= 0 && c < N * N && !lv.Exterior[c] && !lv.Barrier[c] && !lv.Doorway[c] && !lv.Rule[c].IsOutdoor;

        /// <summary>Marks what is reachable from the map border without crossing barriers or doorways.</summary>
        static void Flood(Level lv)
        {
            Array.Clear(lv.Exterior, 0, lv.Exterior.Length);
            var q = new Queue<int>();
            bool Open(int c) => !lv.Barrier[c] && !lv.Doorway[c];
            for (int i = 0; i < N; i++)
                foreach (int c in new[] { i, (N - 1) * N + i, i * N, i * N + N - 1 })
                    if (Open(c) && !lv.Exterior[c]) { lv.Exterior[c] = true; q.Enqueue(c); }
            while (q.Count > 0)
            {
                int c = q.Dequeue();
                foreach (int n in Neighbours4(c))
                    if (Open(n) && !lv.Exterior[n]) { lv.Exterior[n] = true; q.Enqueue(n); }
            }
        }

        static IEnumerable<int> Neighbours4(int c)
        {
            int x = c % N, y = c / N;
            if (x > 0) yield return c - 1;
            if (x < N - 1) yield return c + 1;
            if (y > 0) yield return c - N;
            if (y < N - 1) yield return c + N;
        }

        static IEnumerable<int> Neighbours8(int c)
        {
            int x = c % N, y = c / N;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < N && ny < N) yield return ny * N + nx;
            }
        }

        // ── placement helpers ─────────────────────────────────────────────────────────────────

        class Ctx
        {
            public readonly SettlementPlan Plan;
            readonly PieceGeometry geo;
            public Ctx(SettlementPlan plan, PieceGeometry geo) { Plan = plan; this.geo = geo; }

            public float Height(string prefab) => geo[prefab].SizeY;
            public bool Known(string prefab) => prefab != null && geo.Has(prefab);

            /// <summary>
            /// Ground-floor cells covered by the footprint of a placed object other than the
            /// building shell (walls, floors, roofs): big props spill onto neighbouring tiles.
            /// </summary>
            public HashSet<int> Occupied()
            {
                var r = new HashSet<int>();
                foreach (var p in Plan.Pieces)
                {
                    if (p.Y > 1 || !geo.TryGet(p.Prefab, out var b)) continue;
                    if (p.Prefab.Contains("wall") || p.Prefab.Contains("floor") || p.Prefab.Contains("roof") || p.Prefab.Contains("door")
                        || p.Prefab == "stone_fence" || p.Prefab.StartsWith("wood_pole") || p.Prefab.StartsWith("wood_beam")) continue;
                    for (float lx = b.MinX + 0.2f; lx <= b.MaxX - 0.2f; lx += 0.25f)
                    for (float lz = b.MinZ + 0.2f; lz <= b.MaxZ - 0.2f; lz += 0.25f)
                    {
                        Rotate(lx, lz, p.Yaw, out float wx, out float wz);
                        int cx = (int)Math.Floor((p.X + wx + Plan.HalfSize) / Plan.Tile);
                        int cy = (int)Math.Floor((Plan.HalfSize - (p.Z + wz)) / Plan.Tile);
                        if (cx >= 0 && cy >= 0 && cx < N && cy < N) r.Add(cy * N + cx);
                    }
                }
                return r;
            }

            /// <summary>The prefab's bounds centre relative to its pivot, scaled and turned by yaw.</summary>
            void Offset(PieceGeometry.Box b, float yaw, float sx, float sy, float sz, out float ox, out float oy, out float oz)
            {
                Rotate((b.MinX + b.MaxX) / 2 * sx, (b.MinZ + b.MaxZ) / 2 * sz, yaw, out ox, out oz);
                oy = (b.MinY + b.MaxY) / 2 * sy;
            }

            /// <summary>Places a piece so that the centre of its (scaled) collider bounds is at (x, y, z).</summary>
            public void Centre(string prefab, float x, float y, float z, float yaw, float sx = 1, float sy = 1, float sz = 1)
            {
                if (!geo.TryGet(prefab, out var b)) { Add(prefab, x, y, z, yaw, sx, sy, sz); return; }
                Offset(b, yaw, sx, sy, sz, out float ox, out float oy, out float oz);
                Add(prefab, x - ox, y - oy, z - oz, yaw, sx, sy, sz);
            }

            /// <summary>
            /// Places a piece standing on y with its footprint centred on (x, z). Furniture stands on
            /// its pivot, as Valheim's own placement does (colliders often reach below the visible
            /// base: the iron fire pit's by 0.73 m); poles and pillars, whose pivot is their middle,
            /// stand on the bottom of their bounds.
            /// </summary>
            public void Stand(string prefab, float x, float y, float z, float yaw, float sx = 1, float sy = 1, float sz = 1)
            {
                if (!geo.TryGet(prefab, out var b)) { Add(prefab, x, y, z, yaw, sx, sy, sz); return; }
                Offset(b, yaw, sx, sy, sz, out float ox, out _, out float oz);
                bool centredPivot = prefab.Contains("pole") || prefab.Contains("pillar") || prefab.Contains("column") || prefab == "wood_stack";
                Add(prefab, x - ox, centredPivot ? y - b.MinY * sy : y, z - oz, yaw, sx, sy, sz);
            }

            /// <summary>Places a piece so that the top of its bounds is at y (floors).</summary>
            public void Top(string prefab, float x, float y, float z, float yaw, float sx = 1, float sy = 1, float sz = 1)
            {
                var b = geo[prefab];
                Offset(b, yaw, sx, sy, sz, out float ox, out _, out float oz);
                Add(prefab, x - ox, y - b.MaxY * sy, z - oz, yaw, sx, sy, sz);
            }

            public void Pivot(string prefab, float x, float y, float z, float yaw, float sx = 1, float sy = 1, float sz = 1)
                => Add(prefab, x, y, z, yaw, sx, sy, sz);

            public void Add(string prefab, float x, float y, float z, float yaw, float sx = 1, float sy = 1, float sz = 1, string text = null, string[] items = null)
            {
                var pl = new Placement { Prefab = prefab, X = x, Y = y, Z = z, Yaw = yaw, Text = text, Items = items };
                if (sx != 1 || sy != 1 || sz != 1) { pl.SX = sx; pl.SY = sy; pl.SZ = sz; }
                Plan.Pieces.Add(pl);
            }

            /// <summary>Sets a field of the piece added last.</summary>
            public void Last(Func<Placement, Placement> change)
            {
                int i = Plan.Pieces.Count - 1;
                Plan.Pieces[i] = change(Plan.Pieces[i]);
            }

            /// <summary>The pieces of a composite prop, turned by yaw around the tile centre (x, z), on floor y.</summary>
            public void Parts(Part[] parts, float x, float y, float z, float yaw)
            {
                foreach (var part in parts)
                {
                    Rotate(part.X, part.Z, yaw, out float px, out float pz);
                    float py = y + part.Y, pyaw = part.Yaw + yaw;
                    switch (part.Place)
                    {
                        case PartPlace.Stand: Stand(part.Prefab, x + px, py, z + pz, pyaw, part.SX, part.SY, part.SZ); break;
                        case PartPlace.Top: Top(part.Prefab, x + px, py, z + pz, pyaw, part.SX, part.SY, part.SZ); break;
                        default: Centre(part.Prefab, x + px, py, z + pz, pyaw, part.SX, part.SY, part.SZ); break;
                    }
                }
            }

            /// <summary>Unity's yaw (degrees, clockwise seen from above): local (x, z) → world.</summary>
            public static void Rotate(float x, float z, float yaw, out float wx, out float wz)
            {
                double r = yaw * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
                wx = (float)(x * c + z * s);
                wz = (float)(-x * s + z * c);
            }
        }

        static float CellX(SettlementPlan p, int c) => p.LocalX(c % N);
        static float CellZ(SettlementPlan p, int c) => p.LocalZ(c / N);

        // ── ground, floors ────────────────────────────────────────────────────────────────────

        void Ground(Ctx ctx, Level g)
        {
            var p = ctx.Plan;
            for (int c = 0; c < N * N; c++)
            {
                var r = g.Rule[c];
                if (r.Kind == TileKind.Water) { p.Water[c] = true; continue; }
                if (r.Paint == Paint.Planks)
                {
                    ctx.Top("wood_floor", CellX(p, c), 0.05f, CellZ(p, c), 0);
                    continue;
                }
                if (g.Barrier[c] || g.Doorway[c]) p.GroundPaint[c] = g.Building[c] || g.Doorway[c] ? Paint.Paved : Paint.None;
                else if (g.Interior[c]) p.GroundPaint[c] = r.Paint == Paint.None ? Paint.Paved : r.Paint; // floor continues under furniture
                else p.GroundPaint[c] = r.Paint;
            }
        }

        /// <summary>A solid wall block (a wall tile built on this floor).</summary>
        static bool Solid(Level lv, int c) => lv != null && lv.Built[c] && lv.Rule[c].Kind == TileKind.Wall;

        /// <summary>
        /// Inter-level floors (wood: stone is impractical up there): one 2 m piece per tile whose
        /// top is the top of the wall zone below. Rooms, doorways, openings and wall-walks get one
        /// unless they stand on a solid wall block of the floor below.
        /// </summary>
        void Floors(Ctx ctx, Level lv, Level below, TownStyle style)
        {
            if (lv.Z == 0 || below == null) return;
            var p = ctx.Plan;
            float y = p.Storey * lv.Z;
            for (int c = 0; c < N * N; c++)
            {
                var k = lv.Rule[c].Kind;
                bool floored = lv.Interior[c] || lv.Doorway[c] || (lv.Built[c] && (lv.Rule[c].IsOpening || k == TileKind.Battlement));
                if (floored && !Solid(below, c)) ctx.Top(style.Floor, CellX(p, c), y, CellZ(p, c), 0);
            }
        }

        // ── walls ─────────────────────────────────────────────────────────────────────────────

        /// <summary>Tiles a wall run continues through: built walls, openings and doorways.</summary>
        static bool WallLike(Level lv, int x, int y)
        {
            if (x < 0 || y < 0 || x >= N || y >= N) return false;
            int c = y * N + x;
            return (lv.Built[c] && lv.Rule[c].Kind != TileKind.Battlement) || lv.Doorway[c];
        }

        /// <summary>A wall tile runs east–west when it continues that way (or nowhere north–south).</summary>
        static bool AlongX(Level lv, int c)
        {
            int x = c % N, y = c / N;
            return WallLike(lv, x - 1, y) || WallLike(lv, x + 1, y) || !(WallLike(lv, x, y - 1) || WallLike(lv, x, y + 1));
        }

        /// <summary>
        /// Wall tiles are solid 2 m × 2 m × 3 m blocks (the wall zone). Stone walls are three courses
        /// of 2×1×1 bricks laid along their run, each course 1 m out of phase with the one below;
        /// 1×1×1 bricks fill the odd ends, so neighbouring tiles of a run share bricks. Cobblestone
        /// walls are a wooden frame filled with stone fence; windows a sill and arches with the
        /// opening between; arrow slits leave a cross-shaped gap in the middle course; doors stand
        /// in the middle of their tile, 3 m tall, or 2 m with an open viewport above; archways and
        /// doorways are open below a course of bricks.
        /// </summary>
        void Walls(Ctx ctx, Level lv, TownStyle style, CobbleStyle cobble)
        {
            var p = ctx.Plan;
            float y0 = p.Storey * lv.Z;
            const int G = N * 2; // 1 m grid
            var along = new bool[N * N];
            for (int c = 0; c < N * N; c++) along[c] = AlongX(lv, c);

            // courses of bricks
            for (int course = 0; course < 3; course++)
            {
                bool Brick(int c)
                {
                    if (lv.Doorway[c]) return course == 2;
                    if (!lv.Built[c]) return false;
                    var r = lv.Rule[c];
                    if (r.Kind == TileKind.Wall) return r.Build == WallBuild.Brick;
                    if (r.Kind == TileKind.Arch) return course == 2;
                    if (r.Kind == TileKind.Window && r.Narrow) return course != 1;
                    return false;
                }
                var used = new bool[G * G];
                bool Cell(int gx, int gy, bool alongX)
                {
                    if (gx < 0 || gy < 0 || gx >= G || gy >= G || used[gy * G + gx]) return false;
                    int c = (gy / 2) * N + gx / 2;
                    return Brick(c) && along[c] == alongX;
                }
                float y = y0 + course + 0.5f;
                for (int gy = 0; gy < G; gy++)
                for (int gx = 0; gx < G; gx++)
                {
                    int c = (gy / 2) * N + gx / 2;
                    if (used[gy * G + gx] || !Brick(c)) continue;
                    bool ax = along[c];
                    // a 2 m brick starts on cells of this course's phase when the next cell is free
                    int pos = ax ? gx : gy;
                    bool pair = (pos + course) % 2 == 0 && (ax ? Cell(gx + 1, gy, true) : Cell(gx, gy + 1, false));
                    used[gy * G + gx] = true;
                    if (pair)
                    {
                        if (ax) { used[gy * G + gx + 1] = true; ctx.Centre(style.Brick, gx + 1 - N, y, N - gy - 0.5f, 0); }
                        else { used[(gy + 1) * G + gx] = true; ctx.Centre(style.Brick, gx + 0.5f - N, y, N - gy - 1, 90); }
                    }
                    else ctx.Centre(style.BrickSmall, gx + 0.5f - N, y, N - gy - 0.5f, 0);
                }
            }

            var posts = new HashSet<(int, int)>();
            for (int c = 0; c < N * N; c++)
            {
                if (!lv.Built[c] || lv.Rule[c].Kind == TileKind.Battlement) continue;
                var r = lv.Rule[c];
                float cx = CellX(p, c), cz = CellZ(p, c), yaw = along[c] ? 0 : 90;
                switch (r.Kind)
                {
                    case TileKind.Wall when r.Build == WallBuild.Cobble:
                        CobbleWall(ctx, lv, cobble, c, along[c], y0, posts);
                        break;
                    case TileKind.Wall when r.Build == WallBuild.Shelf && r.Parts != null:
                        ctx.Parts(r.Parts, cx, y0, cz, yaw);
                        break;
                    case TileKind.Window when r.Narrow:
                        // the middle course: four short pillars around a cross-shaped slit
                        foreach (var (dx, dz) in new[] { (-1, -1), (-1, 1), (1, -1), (1, 1) })
                            ctx.Centre(style.BrickSmall, cx + dx * 0.625f, y0 + 1.5f, cz + dz * 0.625f, 0, 0.75f, 1, 0.75f);
                        break;
                    case TileKind.Window:
                        ctx.Centre(style.Sill, cx, y0 + 0.5f, cz, yaw);
                        for (int side = -1; side <= 1; side += 2)
                        {
                            Ctx.Rotate(0, side * 0.5f, yaw, out float ox, out float oz);
                            ctx.Centre(style.Arch, cx + ox, y0 + 2.5f, cz + oz, yaw);
                        }
                        break;
                    case TileKind.Door:
                        if (r.Gate && ctx.Known(style.Gate)) ctx.Centre(style.Gate, cx, y0 + 1.5f, cz, yaw);
                        else if (r.Viewport) ctx.Centre(style.Door, cx, y0 + 1, cz, yaw);
                        else ctx.Centre(style.Door, cx, y0 + 1.5f, cz, yaw, 1, 1.5f, 1);
                        if (r.Lock != null) ctx.Last(pl => { pl.Lock = r.Lock; return pl; });
                        break;
                }
            }
        }

        /// <summary>
        /// A cobblestone wall tile: two rows of stone fence, three high, in a frame of 3 m posts on
        /// the tile corners (shared along a run) with a beam along the top of each open face.
        /// </summary>
        static void CobbleWall(Ctx ctx, Level lv, CobbleStyle s, int c, bool alongX, float y0, HashSet<(int, int)> posts)
        {
            var p = ctx.Plan;
            float cx = CellX(p, c), cz = CellZ(p, c), yaw = alongX ? 0 : 90;
            for (int course = 0; course < 3; course++)
                for (int side = -1; side <= 1; side += 2)
                {
                    Ctx.Rotate(0, side * 0.5f, yaw, out float ox, out float oz);
                    ctx.Pivot(s.Fence, cx + ox, y0 + course, cz + oz, yaw);
                }
            foreach (var (dx, dz) in new[] { (-1, -1), (-1, 1), (1, -1), (1, 1) })
            {
                if (!posts.Add(((int)Math.Round(cx + dx), (int)Math.Round(cz + dz)))) continue;
                ctx.Stand(s.Post, cx + dx, y0, cz + dz, 0);
                ctx.Stand(s.PostShort, cx + dx, y0 + 2, cz + dz, 0);
            }
            int x = c % N, y = c / N;
            foreach (var (nx, ny, dx, dz, byaw) in new[] { (x, y - 1, 0f, 1f, 0f), (x, y + 1, 0f, -1f, 0f), (x + 1, y, 1f, 0f, 90f), (x - 1, y, -1f, 0f, 90f) })
                if (!WallLike(lv, nx, ny))
                    ctx.Centre(s.Beam, cx + dx, y0 + 2.8f, cz + dz, byaw);
        }

        /// <summary>
        /// Parapets on the wall-walks: a 1 m course of bricks flush with the outer face of the
        /// walk, and (unless the tile has none) a 1×1×1 merlon on the middle of each outer side.
        /// The outer sides are the open sides facing away from the settlement's centre, limited to
        /// the tile's own direction (N–S walks have their parapet east or west).
        /// </summary>
        void Battlements(Ctx ctx, Level lv, TownStyle style)
        {
            var p = ctx.Plan;
            float y = p.Storey * lv.Z;
            for (int c = 0; c < N * N; c++)
            {
                if (lv.Rule[c].Kind != TileKind.Battlement || !lv.Built[c]) continue;
                int x = c % N, ty = c / N, raw = lv.Raw[c];
                bool nsWalk = raw == 0x50 || raw == 0x56, ewWalk = raw == 0x51 || raw == 0x57;
                float cx = CellX(p, c), cz = CellZ(p, c);
                var sides = new List<(float dx, float dz)>();
                foreach (var (nx, ny, dx, dz) in new[] { (x, ty - 1, 0f, 1f), (x, ty + 1, 0f, -1f), (x + 1, ty, 1f, 0f), (x - 1, ty, -1f, 0f) })
                {
                    if (nsWalk && dx == 0 || ewWalk && dz == 0) continue;
                    bool open = nx < 0 || ny < 0 || nx >= N || ny >= N
                                || (!lv.Built[ny * N + nx] && !lv.Covered[ny * N + nx] && !lv.Doorway[ny * N + nx]);
                    if (open) sides.Add((dx, dz));
                }
                var outward = sides.Where(s => s.dx * cx + s.dz * cz > 0).ToList();
                if (outward.Count > 0) sides = outward;
                // the parapet's 1 m cells along the outer sides, then bricks over them
                var cells = new HashSet<(int, int)>();
                foreach (var (dx, dz) in sides)
                    for (int a = -1; a <= 1; a += 2)
                        cells.Add(dx != 0 ? ((int)dx, a) : (a, (int)dz));
                var done = new HashSet<(int, int)>();
                foreach (var (dx, dz) in sides)
                {
                    var (a, b) = dx != 0 ? (((int)dx, -1), ((int)dx, 1)) : ((-1, (int)dz), (1, (int)dz));
                    if (done.Contains(a) || done.Contains(b)) continue;
                    done.Add(a); done.Add(b);
                    ctx.Centre(style.Brick, cx + dx * 0.5f, y + 0.5f, cz + dz * 0.5f, dx != 0 ? 90 : 0);
                    if (lv.Rule[c].Merlons) ctx.Centre(style.Merlon, cx + dx * 0.5f, y + 1.5f, cz + dz * 0.5f, 0);
                }
                foreach (var (qx, qz) in cells)
                    if (!done.Contains((qx, qz))) ctx.Centre(style.BrickSmall, cx + qx * 0.5f, y + 0.5f, cz + qz * 0.5f, 0);
            }
        }

        // ── roofs ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The roof zone starts on top of the last wall zone of each building: anything covered on
        /// one floor and not built on the floor above gets a hip roof whose eaves sit on the outer
        /// wall tops. Cobblestone cottages get thatch, other buildings their settlement's roof.
        /// </summary>
        void Roofs(Ctx ctx, List<Level> levels, RoofSet roof, RoofSet cottage)
        {
            for (int i = 0; i < levels.Count; i++)
            {
                var lv = levels[i];
                var above = i + 1 < levels.Count ? levels[i + 1] : null;
                var cells = new bool[N * N];
                for (int c = 0; c < N * N; c++)
                    cells[c] = lv.Covered[c] && !(above != null && (above.Covered[c] || above.Built[c]));
                float baseY = ctx.Plan.Storey * (lv.Z + 1);
                foreach (var building in RoofFootprints(cells))
                    HipRoof(ctx, IsCottage(lv, building) ? cottage : roof, building, baseY);
            }
        }

        /// <summary>A building most of whose walls are cobblestone.</summary>
        static bool IsCottage(Level lv, bool[] cells)
        {
            int walls = 0, cobble = 0;
            for (int c = 0; c < N * N; c++)
            {
                if (!cells[c] || !Solid(lv, c)) continue;
                walls++;
                if (lv.Rule[c].Build == WallBuild.Cobble) cobble++;
            }
            return walls > 0 && cobble * 2 > walls;
        }

        /// <summary>
        /// The roof outline of each building (4-connected group of roof cells): its bounding
        /// rectangle when the building nearly fills it — stepped diagonal walls like Iolo's octagonal
        /// hut would otherwise give a sawtooth of corner pieces — else its own (rectilinear) shape.
        /// </summary>
        static IEnumerable<bool[]> RoofFootprints(bool[] cells)
        {
            var seen = new bool[N * N];
            for (int s0 = 0; s0 < N * N; s0++)
            {
                if (!cells[s0] || seen[s0]) continue;
                var group = new List<int>();
                var q = new Queue<int>();
                q.Enqueue(s0); seen[s0] = true;
                while (q.Count > 0)
                {
                    int c = q.Dequeue();
                    group.Add(c);
                    foreach (int n in Neighbours4(c))
                        if (cells[n] && !seen[n]) { seen[n] = true; q.Enqueue(n); }
                }
                int x0 = group.Min(c => c % N), x1 = group.Max(c => c % N), y0 = group.Min(c => c / N), y1 = group.Max(c => c / N);
                var outline = new bool[N * N];
                bool box = group.Count >= 0.8 * (x1 - x0 + 1) * (y1 - y0 + 1);
                if (box)
                    for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                        outline[y * N + x] = true;
                else
                    foreach (int c in group) outline[c] = true;
                yield return outline;
            }
        }

        /// <summary>
        /// One continuous hip roof over any footprint. Each roof cell gets a ring number: how many
        /// cells it lies inside the footprint (Chebyshev distance to the outside). Ring k's low edge
        /// is k rises above the wall tops, and each cell's piece follows from which neighbours are lower:
        ///   one side lower → straight slope facing it;
        ///   two adjacent sides → outer corner (hip);
        ///   only a diagonal → inner corner (valley: L-shapes and stepped walls);
        ///   opposite sides (a one-cell crest) or more → ridge along the crest.
        /// Thatch and shingle (26°, 1 m rise per 2 m cell) have a piece for each; their slope is low
        /// along local +z, the ocorner low along +x and +z, the icorner low only at the (+x, +z)
        /// corner, the ridge cap runs along local x, all with the pivot on the low edge. Grausten
        /// (45°, 2 m rise, pivot at mid-height) has only slopes and two triangles: a valley is the
        /// two triangles, a hip the same two upside down (a triangle with its low edge and apex
        /// swapped), a crest two half-size slopes.
        /// </summary>
        static void HipRoof(Ctx ctx, RoofSet s, bool[] cells, float baseY)
        {
            var p = ctx.Plan;
            var ring = new int[N * N];
            var q = new Queue<int>();
            for (int c = 0; c < N * N; c++)
            {
                ring[c] = -1;
                if (!cells[c]) continue;
                bool edge = false;
                int x = c % N, y = c / N;
                for (int dy = -1; dy <= 1 && !edge; dy++)
                for (int dx = -1; dx <= 1 && !edge; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    edge = nx < 0 || ny < 0 || nx >= N || ny >= N || !cells[ny * N + nx];
                }
                if (edge) { ring[c] = 0; q.Enqueue(c); }
            }
            while (q.Count > 0)
            {
                int c = q.Dequeue();
                foreach (int n in Neighbours8(c))
                    if (cells[n] && ring[n] < 0) { ring[n] = ring[c] + 1; q.Enqueue(n); }
            }

            for (int c = 0; c < N * N; c++)
            {
                if (!cells[c]) continue;
                int k = ring[c], x = c % N, y = c / N;
                int R(int nx, int ny) => nx < 0 || ny < 0 || nx >= N || ny >= N ? -1 : ring[ny * N + nx];
                bool n = R(x, y - 1) < k, e = R(x + 1, y) < k, so = R(x, y + 1) < k, w = R(x - 1, y) < k;
                int lower = (n ? 1 : 0) + (e ? 1 : 0) + (so ? 1 : 0) + (w ? 1 : 0);
                float px = CellX(p, c), pz = CellZ(p, c), low = baseY + k * s.Rise;

                if (s.Triangles)
                {
                    float py = low + s.Rise / 2;
                    if (lower == 0)
                    {
                        float yaw = R(x + 1, y - 1) < k ? 0 : R(x + 1, y + 1) < k ? 90 : R(x - 1, y + 1) < k ? 180 : 270;
                        Triangle(ctx, s.Corner, px, py, pz, yaw, false);
                        Triangle(ctx, s.Corner2, px, py, pz, yaw + 90, false);
                    }
                    else if (lower == 1)
                        ctx.Pivot(s.Slope, px, py, pz, n ? 0 : e ? 90 : so ? 180 : 270);
                    else if (lower == 2 && !(n && so) && !(e && w))
                    {
                        float yaw = n && e ? 0 : e && so ? 90 : so && w ? 180 : 270;
                        Triangle(ctx, s.Corner, px, py, pz, yaw + 180, true);
                        Triangle(ctx, s.Corner2, px, py, pz, yaw + 270, true);
                    }
                    else
                    {
                        // a crest: two half-size slopes meeting 1 m above the neighbours' tops
                        float yaw = n && so ? 0 : 90;
                        for (int side = 0; side < 2; side++)
                        {
                            float sy = yaw + side * 180;
                            Ctx.Rotate(0, 0.5f, sy, out float ox, out float oz);
                            ctx.Pivot(s.Slope, px + ox, low + s.Rise / 4, pz + oz, sy, 1, 0.5f, 0.5f);
                        }
                    }
                    continue;
                }

                if (lower == 0)
                {
                    float yaw = R(x + 1, y - 1) < k ? 0 : R(x + 1, y + 1) < k ? 90 : R(x - 1, y + 1) < k ? 180 : 270;
                    ctx.Pivot(ctx.Known(s.ICorner) ? s.ICorner : s.Slope, px, low, pz, yaw);
                }
                else if (lower == 1)
                    ctx.Pivot(s.Slope, px, low, pz, n ? 0 : e ? 90 : so ? 180 : 270);
                else if (lower == 2 && !(n && so) && !(e && w))
                    ctx.Pivot(ctx.Known(s.OCorner) ? s.OCorner : s.Slope, px, low, pz, n && e ? 0 : e && so ? 90 : so && w ? 180 : 270);
                else if (ctx.Known(s.Top))
                    ctx.Pivot(s.Top, px, low, pz, n && so ? 0 : 90); // ridge across the lower sides
            }
        }

        /// <summary>
        /// A grausten roof triangle covering half a cell. The pieces' cell is centred 0.5 m along
        /// their local +z from the pivot; upright, the high edge is the cell's local south side and
        /// the low point its +x (corner) or −x (corner2) north corner. Upside down (y scale −1) the
        /// south side becomes the low edge and that corner the apex.
        /// </summary>
        static void Triangle(Ctx ctx, string prefab, float x, float y, float z, float yaw, bool flipped)
        {
            Ctx.Rotate(0, 0.5f, yaw, out float ox, out float oz);
            ctx.Pivot(prefab, x - ox, y, z - oz, yaw % 360, 1, flipped ? -1 : 1, 1);
        }

        // ── furniture, plants, fences, climbing ──────────────────────────────────────────────

        void Furniture(Ctx ctx, Level lv, List<Level> levels, VBiome biome, bool[] cottage)
        {
            var p = ctx.Plan;
            float y = p.Storey * lv.Z;
            var done = new bool[N * N];
            for (int c = 0; c < N * N; c++)
            {
                if (done[c] || lv.Barrier[c]) continue;
                if (lv.Z > 0 && !lv.Interior[c]) continue; // upper maps repeat the outdoors
                var r = lv.Rule[c];
                int tx = c % N, ty = c / N;
                float x = CellX(p, c), z = CellZ(p, c);
                switch (r.Kind)
                {
                    case TileKind.Prop:
                        if (r.Parts != null) ctx.Parts(r.Parts, x, y, z, r.Yaw);
                        else if (r.Choices != null) ctx.Stand(r.Choices[Hash(tx, ty, 8) % r.Choices.Length], x, y, z, Hash(tx, ty, 9) % 4 * 90);
                        else if (r.Prefab != null && r.WallSide is float side)
                        {
                            // mounted on the wall on that side, facing into the room (checked in game:
                            // the sconce's back is its local −x, and the tile's side is the far one)
                            Ctx.Rotate(0, -0.85f, side, out float ox, out float oz);
                            ctx.Centre(r.Prefab, x + ox, y + 1.8f, z + oz, side + 270);
                        }
                        else if (r.Prefab != null) ctx.Stand(r.Prefab, x, y, z, r.Yaw);
                        break;
                    case TileKind.Chair:
                        ctx.Stand("piece_chair", x, y, z, r.Yaw);
                        break;
                    case TileKind.BedHead:
                    {
                        // cottages have plain wooden beds, everyone else the fancy one
                        string bed = cottage[c] ? "bed" : "piece_bed02";
                        int foot = Neighbours4(c).Where(n => lv.Rule[n].Kind == TileKind.BedFoot && !done[n]).DefaultIfEmpty(-1).First();
                        if (foot < 0) { ctx.Stand(bed, x, y, z, 0); break; }
                        done[foot] = true;
                        float bx = (x + CellX(p, foot)) / 2, bz = (z + CellZ(p, foot)) / 2;
                        bool horizontal = foot / N == c / N;
                        float yaw = horizontal ? (foot > c ? 270 : 90) : (foot > c ? 0 : 180);
                        ctx.Stand(bed, bx, y, bz, yaw);
                        break;
                    }
                    case TileKind.Table:
                    {
                        int x1 = tx;
                        while (x1 + 1 < N && lv.Rule[ty * N + x1 + 1].Kind == TileKind.Table) x1++;
                        int len = x1 - tx + 1;
                        for (int i = tx; i <= x1; i++) done[ty * N + i] = true;
                        float mx = (p.LocalX(tx) + p.LocalX(x1)) / 2;
                        if (len >= 3) ctx.Stand("piece_table_oak", mx, y, z, 0);
                        else if (len == 2) ctx.Stand("piece_table", mx, y, z, 0);
                        else ctx.Stand("piece_table_round", mx, y, z, 0);
                        break;
                    }
                    case TileKind.Crop:
                        foreach (var (ox, oz) in new[] { (-0.5f, -0.5f), (0.5f, 0.5f), (-0.5f, 0.5f), (0.5f, -0.5f) })
                            ctx.Stand(r.Prefab ?? "Pickable_Barley", x + ox, y, z + oz, Hash(tx, ty, 7) % 360);
                        break;
                    case TileKind.Tree:
                        if (lv.Z == 0 && Hash(tx, ty, 1) % 100 < 45)
                            ctx.Add(TreeFor(biome, Hash(tx, ty, 2)), x + Jitter(tx, ty, 3), 0, z + Jitter(tx, ty, 4), Hash(tx, ty, 5) % 360);
                        break;
                    case TileKind.FruitTree:
                        if (lv.Z == 0) ctx.Add("Beech_small1", x, 0, z, Hash(tx, ty, 6) % 360);
                        break;
                    case TileKind.FenceEW:
                        ctx.Stand("wood_fence", x, y, z, 0);
                        break;
                    case TileKind.FenceNS:
                        ctx.Stand("wood_fence", x, y, z, 90);
                        break;
                    case TileKind.LadderUp:
                    case TileKind.LadderDown:
                    case TileKind.Stairs:
                        ClimbPoint(ctx, lv, levels, c, r.Kind, x, y, z);
                        break;
                }
            }
        }

        /// <summary>Cells of buildings most of whose walls are cobblestone (cottages), per <see cref="IsCottage"/>.</summary>
        static bool[] Cottages(Level lv)
        {
            var r = new bool[N * N];
            var seen = new bool[N * N];
            for (int s0 = 0; s0 < N * N; s0++)
            {
                if (!lv.Covered[s0] || seen[s0]) continue;
                var group = new bool[N * N];
                var list = new List<int>();
                var q = new Queue<int>();
                q.Enqueue(s0); seen[s0] = true;
                while (q.Count > 0)
                {
                    int c = q.Dequeue();
                    group[c] = true;
                    list.Add(c);
                    foreach (int n in Neighbours4(c))
                        if (lv.Covered[n] && !seen[n]) { seen[n] = true; q.Enqueue(n); }
                }
                if (IsCottage(lv, group)) foreach (int c in list) r[c] = true;
            }
            return r;
        }

        /// <summary>
        /// Stairs and ladders become a ladder you use to change floor (like the original, which
        /// changes floor on the spot): the destination is the same tile one floor up or down.
        /// </summary>
        void ClimbPoint(Ctx ctx, Level lv, List<Level> levels, int c, TileKind kind, float x, float y, float z)
        {
            int idx = levels.IndexOf(lv);
            bool up = kind == TileKind.LadderUp || (kind == TileKind.Stairs && idx + 1 < levels.Count &&
                      (levels[idx + 1].Rule[c].Kind == TileKind.Stairs || levels[idx + 1].Interior[c]));
            int target = idx + (up ? 1 : -1);
            ctx.Stand("wood_pole2", x, y, z, 0);
            if (target < 0 || target >= levels.Count) return; // basements come later (interiors)
            var p = ctx.Plan;
            int last = p.Pieces.Count - 1;
            var pl = p.Pieces[last];
            pl.Climb = true;
            pl.ClimbX = x;
            pl.ClimbZ = z;
            pl.ClimbY = p.Storey * levels[target].Z + 0.2f;
            pl.ClimbVerb = up ? "Climb up" : "Climb down";
            // stand the arrival a step off the ladder, on a floor tile of the target level
            int land = Neighbours4(c).Where(n => levels[target].Interior[n]).DefaultIfEmpty(-1).First();
            if (land >= 0) { pl.ClimbX = CellX(p, land); pl.ClimbZ = CellZ(p, land); }
            p.Pieces[last] = pl;
        }

        static string TreeFor(VBiome biome, int h)
        {
            switch (biome)
            {
                case VBiome.BlackForest: return h % 3 == 0 ? "Pinetree_01" : "FirTree";
                case VBiome.Mountain: return "FirTree";
                case VBiome.Plains: return h % 2 == 0 ? "Oak1" : "Birch1";
                default: return h % 3 == 0 ? "Oak1" : "Beech1";
            }
        }

        static int Hash(int x, int y, int salt)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + salt * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                return (int)((h ^ (h >> 16)) & 0x7fffffff);
            }
        }

        static float Jitter(int x, int y, int salt) => (Hash(x, y, salt) % 1000) / 1000f - 0.5f;

        // ── signs, spawn, Iolo's hut ──────────────────────────────────────────────────────────

        void Signs(Ctx ctx, Data.Location loc, List<Level> levels)
        {
            var p = ctx.Plan;
            foreach (var group in data.Signs)
                foreach (var sign in group)
                {
                    if (sign.Location != loc.Number) continue;
                    var lv = levels.FirstOrDefault(l => l.Z == sign.Z);
                    if (lv == null) continue;
                    int c = sign.Y * N + sign.X;
                    if (c < 0 || c >= N * N) continue;
                    string text = SignText(sign.Text);
                    if (text.Length == 0) continue;
                    float x = CellX(p, c), z = CellZ(p, c), yaw = 180;
                    // signs hang on walls: stand it on the open side, facing out
                    foreach (var (n, dx, dz, yw) in new[] { (c + N, 0f, -1f, 180f), (c - N, 0f, 1f, 0f), (c + 1, 1f, 0f, 90f), (c - 1, -1f, 0f, 270f) })
                    {
                        if (n < 0 || n >= N * N || lv.Barrier[n]) continue;
                        if (!lv.Barrier[c]) break;
                        x += dx * 1.05f; z += dz * 1.05f; yaw = yw;
                        break;
                    }
                    ctx.Add("sign", x, p.Storey * lv.Z + 1.6f, z, yaw, text: text);
                }
        }

        /// <summary>SIGNS.DAT text without the frame glyphs (lower-case letters) and with '@' as space.</summary>
        public static string SignText(string raw)
        {
            var lines = raw.Contains('\n') ? raw.Split('\n') : Chunk(raw, 16);
            var sb = new StringBuilder();
            foreach (var line in lines)
            {
                var clean = new string(line.Select(ch => ch == '@' ? ' ' : char.IsLower(ch) || ch == '&' ? ' ' : ch).ToArray()).Trim();
                clean = string.Join(" ", clean.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
                if (!clean.Any(char.IsLetterOrDigit)) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(clean);
            }
            return sb.ToString();
        }

        static string[] Chunk(string s, int n)
        {
            var r = new List<string>();
            for (int i = 0; i < s.Length; i += n) r.Add(s.Substring(i, Math.Min(n, s.Length - i)));
            return r.ToArray();
        }

        /// <summary>The spawn point is the middle of the largest room on the ground floor.</summary>
        void Spawn(SettlementPlan p, Level g)
        {
            var region = LargestInterior(g);
            if (region.Count == 0) return;
            double mx = region.Average(c => c % N), my = region.Average(c => c / N);
            int best = region.OrderBy(c => g.Rule[c].Kind == TileKind.Ground ? 0 : 1)
                             .ThenBy(c => Math.Pow(c % N - mx, 2) + Math.Pow(c / N - my, 2)).First();
            p.SpawnX = CellX(p, best);
            p.SpawnZ = CellZ(p, best);
            p.HasSpawn = true;
        }

        static List<int> LargestInterior(Level g)
        {
            var seen = new bool[N * N];
            var best = new List<int>();
            for (int s = 0; s < N * N; s++)
            {
                if (!g.Interior[s] || seen[s]) continue;
                var region = new List<int>();
                var q = new Queue<int>();
                q.Enqueue(s); seen[s] = true;
                while (q.Count > 0)
                {
                    int c = q.Dequeue();
                    region.Add(c);
                    foreach (int n in Neighbours4(c))
                        if (g.Interior[n] && !seen[n]) { seen[n] = true; q.Enqueue(n); }
                }
                if (region.Count > best.Count) best = region;
            }
            return best;
        }

        /// <summary>
        /// Iolo's hut (MASTER_PLAN §5.12): the chest with the only Valheim tools in Britannia,
        /// inside the hut, and the one exposed workbench beside the garden.
        /// </summary>
        void IolosHut(Ctx ctx, Level g)
        {
            var p = ctx.Plan;
            var hut = LargestInterior(g);
            var taken = ctx.Occupied();
            bool Free(int c) => g.Rule[c].Kind == TileKind.Ground && g.Rule[c].Paint != Paint.Cultivated && !taken.Contains(c);
            int spawnCell = hut.OrderBy(c => Math.Abs(CellX(p, c) - p.SpawnX) + Math.Abs(CellZ(p, c) - p.SpawnZ)).First();
            // tool chest: a free floor tile against a wall, not where players appear
            int chest = hut.Where(c => c != spawnCell && Free(c) && Neighbours4(c).Any(n => g.Barrier[n] && g.Rule[n].Kind == TileKind.Wall))
                           .OrderBy(c => c).DefaultIfEmpty(-1).First();
            if (chest >= 0)
            {
                float yaw = 0;
                foreach (var (n, yw) in new[] { (chest - N, 180f), (chest + N, 0f), (chest - 1, 90f), (chest + 1, 270f) })
                    if (g.Barrier[n]) { yaw = yw; break; }
                ctx.Stand("piece_chest_wood", CellX(p, chest), 0, CellZ(p, chest), yaw);
                var pl = p.Pieces[p.Pieces.Count - 1];
                pl.Items = new[] { "Hammer", "Hoe", "Cultivator" };
                p.Pieces[p.Pieces.Count - 1] = pl;
            }
            // workbench: the free outdoor tile nearest the crops that is clear of the fence
            var crops = Enumerable.Range(0, N * N).Where(c => g.Rule[c].Kind == TileKind.Crop || g.Rule[c].Paint == Paint.Cultivated).ToList();
            if (crops.Count == 0) return;
            double mx = crops.Average(c => c % N), my = crops.Average(c => c / N);
            int bench = Enumerable.Range(0, N * N)
                .Where(c => g.Exterior[c] && Free(c) && Neighbours8(c).All(n => Free(n) || g.Rule[n].Kind == TileKind.FenceEW || g.Rule[n].Kind == TileKind.FenceNS)
                            && Neighbours8(c).All(n => g.Rule[n].Kind != TileKind.Tree))
                .OrderBy(c => Math.Pow(c % N - mx, 2) + Math.Pow(c / N - my, 2))
                .DefaultIfEmpty(-1).First();
            if (bench >= 0) ctx.Stand("piece_workbench", CellX(p, bench), 0, CellZ(p, bench), 270);
        }
    }
}
