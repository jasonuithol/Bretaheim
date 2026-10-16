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
        public float Tile = 2f, Storey = 4f;
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
    /// Turns a settlement's maps into a <see cref="SettlementPlan"/> (MASTER_PLAN §5.2, Phase 2).
    /// One town tile is 2 m (the 64 m square fits between Britain's neighbours), a storey 4 m.
    ///
    /// Per floor: a flood fill from the map border through non-barrier tiles (walls, windows,
    /// doors, battlements are barriers) separates outside from inside; inside tiles get floors,
    /// barriers next to them become building walls. Walls run along the centre of their tiles:
    /// a full 2 m piece on straight runs, 1 m stubs toward each neighbour at corners and junctions,
    /// doors and windows in their run. Ground floors and streets are terrain paint (planks get
    /// wooden floors). Anything covered on one floor but not on the floor above gets a roof there:
    /// gabled (wood) over rectangles cut from the covered area, or flat walkable stone.
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
            if (!tiles.Settlements.TryGetValue(styleKey, out var styles)) styles = ("wood", "stone");
            var house = tiles.Styles[styles.houses];
            var outer = tiles.Styles[styles.perimeter];

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
            Ground(ctx, levels[0], house);
            foreach (var lv in levels)
            {
                Floors(ctx, lv, house);
                Walls(ctx, lv, house, outer);
                Battlements(ctx, lv, outer);
                Furniture(ctx, lv, levels, biome);
            }
            Roofs(ctx, levels, house);
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
            public float Width(string prefab) => geo[prefab].SizeX;
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
                    if (p.Prefab.Contains("wall") || p.Prefab.Contains("floor") || p.Prefab.Contains("roof") || p.Prefab.Contains("door")) continue;
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

            /// <summary>Places a piece so that the centre of its collider bounds is at (x, y, z).</summary>
            public void Centre(string prefab, float x, float y, float z, float yaw)
            {
                var b = geo[prefab];
                Rotate((b.MinX + b.MaxX) / 2, (b.MinZ + b.MaxZ) / 2, yaw, out float ox, out float oz);
                Add(prefab, x - ox, y - (b.MinY + b.MaxY) / 2, z - oz, yaw);
            }

            /// <summary>
            /// Places a piece standing on y with its footprint centred on (x, z). Furniture stands on
            /// its pivot, as Valheim's own placement does (colliders often reach below the visible
            /// base: the iron fire pit's by 0.73 m); poles and pillars, whose pivot is their middle,
            /// stand on the bottom of their bounds.
            /// </summary>
            public void Stand(string prefab, float x, float y, float z, float yaw)
            {
                if (!geo.TryGet(prefab, out var b)) { Add(prefab, x, y, z, yaw); return; }
                Rotate((b.MinX + b.MaxX) / 2, (b.MinZ + b.MaxZ) / 2, yaw, out float ox, out float oz);
                bool centredPivot = prefab.Contains("pole") || prefab.Contains("pillar") || prefab == "wood_stack";
                Add(prefab, x - ox, centredPivot ? y - b.MinY : y, z - oz, yaw);
            }

            /// <summary>Places a piece so that the top of its bounds is at y (floors).</summary>
            public void Top(string prefab, float x, float y, float z, float yaw)
            {
                var b = geo[prefab];
                Rotate((b.MinX + b.MaxX) / 2, (b.MinZ + b.MaxZ) / 2, yaw, out float ox, out float oz);
                Add(prefab, x - ox, y - b.MaxY, z - oz, yaw);
            }

            public void Pivot(string prefab, float x, float y, float z, float yaw) => Add(prefab, x, y, z, yaw);

            public void Add(string prefab, float x, float y, float z, float yaw, string text = null, string[] items = null)
                => Plan.Pieces.Add(new Placement { Prefab = prefab, X = x, Y = y, Z = z, Yaw = yaw, Text = text, Items = items });

            /// <summary>Unity's yaw (degrees, clockwise seen from above): local (x, z) → world.</summary>
            public static void Rotate(float x, float z, float yaw, out float wx, out float wz)
            {
                double r = yaw * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
                wx = (float)(x * c + z * s);
                wz = (float)(-x * s + z * c);
            }

            /// <summary>Fills a wall of the given width from y0 to y1 with the style's pieces.</summary>
            public void WallColumn(TownStyle style, bool full, float x, float z, float yaw, float y0, float y1)
            {
                string piece = full ? style.Wall : style.WallStub;
                float h = Height(piece);
                float y = y0;
                while (y + h <= y1 + 0.01f) { Centre(piece, x, y + h / 2, z, yaw); y += h; }
                if (full && y1 - y >= 0.99f && Known(style.WallHalf))
                {
                    float hh = Height(style.WallHalf);
                    while (y + hh <= y1 + 0.01f) { Centre(style.WallHalf, x, y + hh / 2, z, yaw); y += hh; }
                }
            }
        }

        static float CellX(SettlementPlan p, int c) => p.LocalX(c % N);
        static float CellZ(SettlementPlan p, int c) => p.LocalZ(c / N);

        // ── ground, floors ────────────────────────────────────────────────────────────────────

        void Ground(Ctx ctx, Level g, TownStyle house)
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

        /// <summary>
        /// Upper floors. A U5 wall fills its whole tile but ours runs along the tile's centre line,
        /// so the room-side half of every wall tile gets 1 m floor pieces too (otherwise a 1 m gap
        /// runs along every wall). A quarter of a wall tile is on the room side when the tile next
        /// to it in that direction — beside, above/below or diagonal — is a room.
        /// </summary>
        void Floors(Ctx ctx, Level lv, TownStyle house)
        {
            if (lv.Z == 0) return;
            var p = ctx.Plan;
            float y = p.Storey * lv.Z;
            bool In(int x, int yy) => x >= 0 && yy >= 0 && x < N && yy < N && lv.Interior[yy * N + x];
            for (int c = 0; c < N * N; c++)
            {
                if (lv.Interior[c] || (lv.Rule[c].Kind == TileKind.Battlement && lv.Built[c]))
                {
                    ctx.Top(house.Floor, CellX(p, c), y, CellZ(p, c), 0);
                    continue;
                }
                if (!(lv.Built[c] || lv.Doorway[c]) || !ctx.Known(house.FloorSmall)) continue;
                int x = c % N, cy = c / N;
                for (int sy = -1; sy <= 1; sy += 2)
                for (int sx = -1; sx <= 1; sx += 2)
                    if (In(x + sx, cy) || In(x, cy + sy) || In(x + sx, cy + sy))
                        ctx.Top(house.FloorSmall, CellX(p, c) + sx * 0.5f, y, CellZ(p, c) - sy * 0.5f, 0);
            }
        }

        // ── walls ─────────────────────────────────────────────────────────────────────────────

        void Walls(Ctx ctx, Level lv, TownStyle house, TownStyle outer)
        {
            var p = ctx.Plan;
            float y0 = p.Storey * lv.Z, y1 = y0 + p.Storey;
            var arch = new TileRule { Kind = TileKind.Door, Open = true };
            for (int c = 0; c < N * N; c++)
            {
                if (lv.Doorway[c])
                {
                    Opening(ctx, house, arch, CellX(p, c), CellZ(p, c), EW(lv, c) ? 0 : 90, y0, y1);
                    continue;
                }
                if (!lv.Built[c] || lv.Rule[c].Kind == TileKind.Battlement) continue;
                var style = lv.Building[c] ? house : outer;
                int x = c % N, y = c / N;
                bool B(int nx, int ny) => nx >= 0 && ny >= 0 && nx < N && ny < N
                                          && ((lv.Built[ny * N + nx] && lv.Rule[ny * N + nx].Kind != TileKind.Battlement) || lv.Doorway[ny * N + nx]);
                bool e = B(x + 1, y), w = B(x - 1, y), n = B(x, y - 1), s = B(x, y + 1);
                float cx = CellX(p, c), cz = CellZ(p, c);
                var rule = lv.Rule[c];

                if (rule.IsOpening)
                {
                    // the opening lies in its wall run; a free-standing one spans across its open sides
                    bool alongX = e || w || (!n && !s && y > 0 && y < N - 1 && !lv.Barrier[c - N] && !lv.Barrier[c + N]);
                    float yaw = alongX ? 0 : 90;
                    Opening(ctx, style, rule, cx, cz, yaw, y0, y1);
                    continue;
                }

                if (e && w && !n && !s) { ctx.WallColumn(style, true, cx, cz, 0, y0, y1); continue; }
                if (n && s && !e && !w) { ctx.WallColumn(style, true, cx, cz, 90, y0, y1); continue; }
                bool any = false;
                if (e) { ctx.WallColumn(style, false, cx + 0.5f, cz, 0, y0, y1); any = true; }
                if (w) { ctx.WallColumn(style, false, cx - 0.5f, cz, 0, y0, y1); any = true; }
                if (n) { ctx.WallColumn(style, false, cx, cz + 0.5f, 90, y0, y1); any = true; }
                if (s) { ctx.WallColumn(style, false, cx, cz - 0.5f, 90, y0, y1); any = true; }
                if (!any)
                {
                    float h = ctx.Height(style.Pole);
                    for (float yy = y0; yy + h <= y1 + 0.01f; yy += h) ctx.Centre(style.Pole, cx, yy + h / 2, cz, 0);
                }
            }
        }

        static void Opening(Ctx ctx, TownStyle style, TileRule rule, float x, float z, float yaw, float y0, float y1)
        {
            if (rule.Kind == TileKind.Door)
            {
                float top = y0 + 2;
                if (rule.Gate && ctx.Known("wood_gate")) { ctx.Centre("wood_gate", x, y0 + 1.5f, z, yaw); top = y0 + 3; }
                else if (!rule.Open) ctx.Centre(style.Door, x, y0 + 1, z, yaw);
                ctx.WallColumn(style, true, x, z, yaw, top, y1);
                return;
            }
            // window: sill to 1 m, opening 1–2 m, wall above
            ctx.WallColumn(style, true, x, z, yaw, y0, y0 + 1);
            ctx.WallColumn(style, true, x, z, yaw, y0 + 2, y1);
        }

        void Battlements(Ctx ctx, Level lv, TownStyle outer)
        {
            if (outer.Merlon == null || !ctx.Known(outer.Merlon)) return;
            float y = ctx.Plan.Storey * lv.Z;
            for (int c = 0; c < N * N; c++)
            {
                if (lv.Rule[c].Kind != TileKind.Battlement || !lv.Built[c]) continue;
                // merlons along the outer face: cells with an outside neighbour, every other one
                bool edge = Neighbours4(c).Any(n => lv.Exterior[n]);
                if (!edge || ((c % N) + (c / N)) % 2 != 0) continue;
                ctx.Centre(outer.Merlon, CellX(ctx.Plan, c), y + ctx.Height(outer.Merlon) / 2, CellZ(ctx.Plan, c), 0);
            }
        }

        // ── roofs ─────────────────────────────────────────────────────────────────────────────

        void Roofs(Ctx ctx, List<Level> levels, TownStyle house)
        {
            for (int i = 0; i < levels.Count; i++)
            {
                var lv = levels[i];
                var above = i + 1 < levels.Count ? levels[i + 1] : null;
                var cells = new bool[N * N];
                for (int c = 0; c < N * N; c++)
                    cells[c] = lv.Covered[c] && !(above != null && (above.Covered[c] || above.Built[c]));
                float baseY = ctx.Plan.Storey * (lv.Z + 1);
                if (house.Gabled)
                    foreach (var building in RoofFootprints(cells)) HipRoof(ctx, house, building, baseY);
                else
                    for (int c = 0; c < N * N; c++)
                        if (cells[c]) ctx.Top(house.Floor, CellX(ctx.Plan, c), baseY, CellZ(ctx.Plan, c), 0);
            }
        }

        /// <summary>
        /// One continuous hip roof over any footprint. Each roof cell gets a ring number: how many
        /// cells it lies inside the footprint (Chebyshev distance to the outside). Ring k is 1 m
        /// higher than ring k−1 — exactly the rise of Valheim's 26° roof pieces over a 2 m cell —
        /// and each cell's piece follows from which neighbours are lower:
        ///   one side lower → straight slope (wood_roof) facing it;
        ///   two adjacent sides → outer corner (wood_roof_ocorner);
        ///   only a diagonal → inner corner (wood_roof_icorner, L-shapes and stepped walls);
        ///   opposite sides (a one-cell crest) or more → ridge cap (wood_roof_top) along the crest.
        /// Piece conventions (from their snap points; pivot at the cell centre, y = low edge):
        /// wood_roof is low along local +z; ocorner low along +x and +z; icorner low only at the
        /// (+x, +z) corner; roof_top's ridge runs along local x. The roof sits 0.5 m low so the
        /// slope meets the wall tops on the wall centre lines; the eaves overhang by 1 m.
        /// </summary>
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

        static void HipRoof(Ctx ctx, TownStyle s, bool[] cells, float baseY)
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

            string ocorner = s.Roof + "_ocorner", icorner = s.Roof + "_icorner";
            for (int c = 0; c < N * N; c++)
            {
                if (!cells[c]) continue;
                int k = ring[c], x = c % N, y = c / N;
                int R(int nx, int ny) => nx < 0 || ny < 0 || nx >= N || ny >= N ? -1 : ring[ny * N + nx];
                bool n = R(x, y - 1) < k, e = R(x + 1, y) < k, so = R(x, y + 1) < k, w = R(x - 1, y) < k;
                int lower = (n ? 1 : 0) + (e ? 1 : 0) + (so ? 1 : 0) + (w ? 1 : 0);
                float px = CellX(p, c), pz = CellZ(p, c), py = baseY + k - 0.5f;

                if (lower == 0)
                {
                    // concave corner: the lower cell is diagonal
                    float yaw = R(x + 1, y - 1) < k ? 0 : R(x + 1, y + 1) < k ? 90 : R(x - 1, y + 1) < k ? 180 : 270;
                    ctx.Pivot(ctx.Known(icorner) ? icorner : s.Roof, px, py, pz, yaw);
                }
                else if (lower == 1)
                    ctx.Pivot(s.Roof, px, py, pz, n ? 0 : e ? 90 : so ? 180 : 270);
                else if (lower == 2 && !(n && so) && !(e && w))
                    ctx.Pivot(ctx.Known(ocorner) ? ocorner : s.Roof, px, py, pz, n && e ? 0 : e && so ? 90 : so && w ? 180 : 270);
                else if (ctx.Known(s.RoofTop))
                    ctx.Pivot(s.RoofTop, px, py, pz, n && so ? 0 : 90); // ridge across the lower sides
            }
        }

        // ── furniture, plants, fences, climbing ──────────────────────────────────────────────

        void Furniture(Ctx ctx, Level lv, List<Level> levels, VBiome biome)
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
                        if (r.Prefab != null) ctx.Stand(r.Prefab, x, y, z, r.Yaw);
                        break;
                    case TileKind.Chair:
                        ctx.Stand("piece_chair", x, y, z, r.Yaw);
                        break;
                    case TileKind.BedHead:
                    {
                        int foot = Neighbours4(c).Where(n => lv.Rule[n].Kind == TileKind.BedFoot && !done[n]).DefaultIfEmpty(-1).First();
                        if (foot < 0) { ctx.Stand("bed", x, y, z, 0); break; }
                        done[foot] = true;
                        float bx = (x + CellX(p, foot)) / 2, bz = (z + CellZ(p, foot)) / 2;
                        bool horizontal = foot / N == c / N;
                        float yaw = horizontal ? (foot > c ? 270 : 90) : (foot > c ? 0 : 180);
                        ctx.Stand("bed", bx, y, bz, yaw);
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
