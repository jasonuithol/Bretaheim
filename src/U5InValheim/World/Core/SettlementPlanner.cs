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
        /// <summary>A changed look applied when the piece loads ("white_marble", "fountain"), or null.</summary>
        public string Look;
        /// <summary>Item stands: the item (prefab name) shown on it, or null.</summary>
        public string StandItem;

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
        /// <summary>Cells carved out for the basement's rooms (its floor a storey under the ground), or none.</summary>
        public readonly bool[] Basement = new bool[Size * Size];
        public bool HasBasement => Basement.Any(b => b);
        /// <summary>
        /// The basement's ceiling: the ground floor's stone slab (stone_floor_2x2, 1 m thick, its top
        /// at street level). The basement sits that much lower, so it has a full storey under it.
        /// </summary>
        public const float Slab = 1f;
        /// <summary>How deep the basement's rooms are dug: its floor is this far under the ground.</summary>
        public float BasementDepth => Storey + Slab;
        /// <summary>Floor height of storey z (0 = the ground) relative to the ground.</summary>
        public float LevelY(int z) => z < 0 ? Storey * z - Slab : Storey * z;
        /// <summary>Small water features planted as flower beds instead.</summary>
        public readonly bool[] Garden = new bool[Size * Size];
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
            /// <summary>Upper floors: the tiles a player can walk to from the ladders and stairs (all floored), else null.</summary>
            public bool[] Walked;
            /// <summary>River water inside a building: a trough, floored round its quenching buckets.</summary>
            public bool[] Trough = new bool[N * N];
            /// <summary>Drawbridge decks (ground floor): each tile's deck piece (middle, scale along x and z) and the offset from its middle to the hinge.</summary>
            public bool[] Drawbridge = new bool[N * N];
            public (float x, float z, float sx, float sz, float hx, float hz)[] Deck = new (float, float, float, float, float, float)[N * N];
        }

        /// <summary>A tile that is not on this floor: open air.</summary>
        static readonly TileRule Nothing = new TileRule();

        /// <summary>Location number of the settlement being planned.</summary>
        int planning;

        public SettlementPlan Plan(Data.Location loc, string kind, VBiome biome)
        {
            planning = loc.Number;
            var plan = new SettlementPlan { Location = loc, Kind = kind };
            string styleKey = kind;
            if (loc.Number == 18) styleKey = "evil"; // Blackthorn's palace
            if (!tiles.Settlements.TryGetValue(styleKey, out var materials)) materials = ("stone", "shingle");
            var style = tiles.Styles[materials.walls];

            var levels = Levels(loc);

            var ctx = new Ctx(plan, geo);
            var ground = levels.First(l => l.Z == 0);
            Ground(ctx, ground);
            Basement(ctx, levels, style);
            for (int i = 0; i < levels.Count; i++)
            {
                var lv = levels[i];
                if (lv.Z > 0) Floors(ctx, lv, levels[i - 1], style);
                Walls(ctx, lv, style, tiles.Cobble);
                Battlements(ctx, lv, style);
                Furniture(ctx, lv, levels, biome, Cottages(lv));
                HallwayEnds(ctx, lv);
            }
            Roofs(ctx, levels, tiles.Roofs[materials.roof], tiles.Roofs[tiles.CottageRoof]);
            Signs(ctx, loc, levels);
            ArmouryWall(ctx, loc, levels);
            Spawn(plan, ground);
            if (loc.Number == 13) IolosHut(ctx, ground);
            return plan;
        }

        /// <summary>The analysed floors of a location, ground floor first.</summary>
        List<Level> Levels(Data.Location loc)
        {
            // Upper maps repeat the ground below them (so a player looking out of an upper window
            // saw something): only what can be walked to from the ladders and stairs, and the walls
            // around that walk, is really on an upper floor. First read the ground on its own and
            // each upper floor's walk on top of the floor below; then again from the ground up,
            // where a tile with rooms, walls or a roof on the floor above is indoors (U5 draws
            // buildings that way: shop fronts and taverns are often open to the street on their
            // own floor).
            int z0 = Math.Max(0, loc.MinZ);
            var raws = new List<byte[]>();
            for (int z = z0; z <= loc.MaxZ; z++) raws.Add(data.Maps.Floor(loc, z));
            var alone = new List<Level>();
            var walks = new List<bool[]>();
            for (int i = 0; i < raws.Count; i++)
            {
                var walked = i == 0 ? null : Walk(raws[i], raws[i - 1], alone[i - 1]);
                walks.Add(walked);
                alone.Add(Analyse(raws[i], z0 + i, null, null, walked));
            }
            var levels = new List<Level>();
            for (int i = 0; i < raws.Count; i++)
            {
                bool[] hint = null;
                if (i + 1 < raws.Count)
                {
                    var up = alone[i + 1];
                    hint = new bool[N * N];
                    for (int c = 0; c < N * N; c++)
                        hint[c] = up.Interior[c] || (up.Walked != null && up.Walked[c]) || tiles[raws[i + 1][c]].Kind == TileKind.Roof
                                  || (up.Barrier[c] && up.Building[c]);
                }
                // walk again on the final floor below, which knows the shop fronts under upper rooms
                var walked = i == 0 ? null : Walk(raws[i], raws[i - 1], levels[i - 1]);
                levels.Add(Analyse(raws[i], z0 + i, i > 0 ? levels[i - 1] : null, hint, walked));
            }

            // the basement (floor −1): what is walked to down the ground floor's ladders and stairs
            if (loc.MinZ < 0 && z0 == 0)
            {
                var raw = data.Maps.Floor(loc, -1);
                var walked = WalkDown(raw, raws[0], levels[0]);
                if (walked != null)
                {
                    var basement = Analyse(raw, -1, null, null, walked);
                    // all of it is indoors, however big (the castle's cellars run under the whole keep)
                    for (int c = 0; c < N * N; c++)
                    {
                        basement.Interior[c] = walked[c] && !basement.Barrier[c] && !basement.Doorway[c];
                        basement.Covered[c] = basement.Interior[c] || basement.Doorway[c] || basement.Built[c];
                    }
                    levels.Insert(0, basement);
                }
            }
            return levels;
        }

        /// <summary>
        /// Text plan of each floor for checking (U5Dump town … floors): # built wall, + opening,
        /// o walked or inside, : covered, ' ' not on this floor.
        /// </summary>
        public string Probe(Data.Location loc, int z, int x, int y)
        {
            var lv = Levels(loc).First(l => l.Z == z);
            int c = y * N + x;
            return $"raw {lv.Raw[c]:x2} kind {lv.Rule[c].Kind} barrier {lv.Barrier[c]} doorway {lv.Doorway[c]} exterior {lv.Exterior[c]} interior {lv.Interior[c]} built {lv.Built[c]} covered {lv.Covered[c]} building {Buildings(lv)[c]}";
        }

        public string DescribeFloors(Data.Location loc)
        {
            var sb = new StringBuilder();
            foreach (var lv in Levels(loc))
            {
                sb.Append($"floor {lv.Z}{(lv.Z > 0 && lv.Walked == null ? " (no ladder or stairs arrive: whole map)" : "")}\n");
                for (int y = 0; y < N; y++)
                {
                    for (int x = 0; x < N; x++)
                    {
                        int c = y * N + x;
                        sb.Append(lv.Built[c] ? (lv.Rule[c].IsOpening ? '+' : '#')
                                  : (lv.Walked != null ? lv.Walked[c] : lv.Interior[c]) ? 'o' : lv.Covered[c] ? ':'
                                  : lv.Rule[c].Kind == TileKind.Water ? '~' : lv.Walked == null ? '.' : ' ');
                    }
                    sb.Append('\n');
                }
            }
            return sb.ToString();
        }

        // ── analysis ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The tiles of an upper floor a player can walk to: from the ladders and stairs that arrive
        /// from the floor below, through floor tiles, doors, secret doors and archways (not plants, water or
        /// roofs), standing on a wall, a covered or a walked tile of the floor below (so the copies of the
        /// streets are never walked onto). Returns null (everything is on the floor) when nothing
        /// arrives from below.
        /// </summary>
        bool[] Walk(byte[] raw, byte[] belowRaw, Level below)
        {
            // a real ladder up or stairs on the floor below (not one of its own copies of the
            // ground) arrives on this tile, unless a wall stands here
            bool Arrives(int c)
            {
                var kb = tiles[belowRaw[c]].Kind;
                bool real = below.Walked == null || below.Walked[c];
                return real && (kb == TileKind.LadderUp || kb == TileKind.Stairs) && !tiles[raw[c]].IsBarrier;
            }
            bool Passable(int c)
            {
                var r = tiles[raw[c]];
                // grass and dirt count (rooftop terraces draw their floor as grass); plants and water do not
                if ((r.IsBarrier && r.Kind != TileKind.Door && r.Kind != TileKind.Arch && !r.Secret) || (r.IsOutdoor && r.Kind != TileKind.Ground) || r.Kind == TileKind.Roof) return false;
                var rb = below.Rule[c];
                // water inside a building (Britain's smithy has the river running through it) carries the floor too
                return below.Built[c] || below.Covered[c] || (below.Walked != null && below.Walked[c])
                       || (!rb.IsBarrier && !rb.IsOutdoor && rb.Kind != TileKind.Roof)
                       || (rb.Kind == TileKind.Water && !below.Exterior[c]);
            }
            var walked = new bool[N * N];
            var q = new Queue<int>();
            for (int c = 0; c < N * N; c++)
                if (Arrives(c)) { walked[c] = true; q.Enqueue(c); }
            if (q.Count == 0) return null;
            while (q.Count > 0)
            {
                int c = q.Dequeue();
                foreach (int n in Neighbours4(c))
                    if (!walked[n] && Passable(n)) { walked[n] = true; q.Enqueue(n); }
            }
            return walked;
        }

        /// <summary>
        /// The tiles of a basement a player can walk to: from the ladders up and stairs under the
        /// ground floor's ladders down and stairs, through floor tiles, doors, secret doors and
        /// archways (not the open ground the maps draw around the cellars). Null when nothing
        /// leads down.
        /// </summary>
        bool[] WalkDown(byte[] raw, byte[] aboveRaw, Level above)
        {
            bool Arrives(int c)
            {
                var ka = tiles[aboveRaw[c]].Kind;
                var k = tiles[raw[c]].Kind;
                return (ka == TileKind.LadderDown || ka == TileKind.Stairs) && (k == TileKind.LadderUp || k == TileKind.Stairs);
            }
            bool Passable(int c)
            {
                var r = tiles[raw[c]];
                return !(r.IsBarrier && r.Kind != TileKind.Door && r.Kind != TileKind.Arch && !r.Secret) && !r.IsOutdoor && r.Kind != TileKind.Roof;
            }
            var walked = new bool[N * N];
            var q = new Queue<int>();
            for (int c = 0; c < N * N; c++)
                if (Arrives(c)) { walked[c] = true; q.Enqueue(c); }
            if (q.Count == 0) return null;
            while (q.Count > 0)
            {
                int c = q.Dequeue();
                foreach (int n in Neighbours4(c))
                    if (!walked[n] && Passable(n)) { walked[n] = true; q.Enqueue(n); }
            }
            return walked;
        }

        Level Analyse(byte[] raw, int z, Level below, bool[] indoorAbove, bool[] walked = null)
        {
            var lv = new Level { Z = z, Raw = raw, Walked = walked };
            for (int i = 0; i < N * N; i++)
            {
                // on a walked floor only the walk and the walls bounding it are there
                bool present = walked == null || walked[i] || (tiles[raw[i]].IsBarrier && Neighbours8(i).Any(n => walked[n]));
                lv.Rule[i] = present ? tiles[raw[i]] : Nothing;
                lv.Barrier[i] = lv.Rule[i].IsBarrier;
            }

            // A gap of up to three tiles between two wall tiles is a doorway (many U5 houses have
            // no door tile; shop fronts are wide open): closed for the flood fill so the house
            // does not leak into the street. A gap with no room on either side is an alley and is
            // reopened.
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

            if (z == 0 && walked == null) Drawbridges(lv);
            var building = Buildings(lv);
            for (int i = 0; i < N * N; i++)
            {
                lv.Trough[i] = building[i] && lv.Rule[i].River;
                // inside: a building's floor; plank floors are always a building (Iolo's barn is
                // open on one side), and so is anything under a room of the floor above
                lv.Interior[i] = !lv.Drawbridge[i] && (lv.Trough[i] || (!lv.Barrier[i] && !lv.Doorway[i] && !lv.Rule[i].IsOutdoor && lv.Rule[i].Kind != TileKind.Roof
                                 && (building[i] || lv.Rule[i].Paint == Paint.Planks || (indoorAbove != null && indoorAbove[i]))));
                // an upper floor only exists above something built on the floor below
                if (below != null && lv.Interior[i] && !below.Covered[i] && !below.Built[i]) lv.Interior[i] = false;
                if (walked != null && !walked[i]) lv.Interior[i] = false;
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
                // ground walls all stand; upper ones bound the walk (or, without one, a room)
                bool bounds = walked != null || nextToInside;
                if (z == 0) lv.Built[i] = true;
                else if (below == null) lv.Built[i] = bounds; // first pass: the floor on its own
                else lv.Built[i] = bounds && (below.Built[i] || below.Covered[i]);
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

        /// <summary>
        /// The tiles of buildings: bounded walkable areas entered by a door, archway, doorway gap,
        /// ladder or stairs. An area (4-connected tiles that are not walls or doorways) is a
        /// building when it does not reach the map border, holds no open-air ground, plants or
        /// water (a town inside its walls, a garden inside its fence, a gazebo by its pond are
        /// not buildings), is not larger than a hall can be, and has a way in.
        /// </summary>
        static bool[] Buildings(Level lv)
        {
            const int MaxTiles = 160;
            var r = new bool[N * N];
            var seen = new bool[N * N];
            bool Open(int c) => !lv.Barrier[c] && !lv.Doorway[c];
            for (int s0 = 0; s0 < N * N; s0++)
            {
                if (seen[s0] || !Open(s0)) continue;
                var area = new List<int>();
                var q = new Queue<int>();
                q.Enqueue(s0); seen[s0] = true;
                bool border = false, outdoor = false, entrance = false;
                while (q.Count > 0)
                {
                    int c = q.Dequeue();
                    area.Add(c);
                    int x = c % N, y = c / N;
                    if (x == 0 || y == 0 || x == N - 1 || y == N - 1) border = true;
                    var k = lv.Rule[c].Kind;
                    // river water indoors is a trough (Britain's smithy), not the open air
                    if (lv.Rule[c].IsOutdoor && lv.Rule[c].Paint != Paint.Planks && !lv.Rule[c].River) outdoor = true;
                    if (k == TileKind.LadderUp || k == TileKind.LadderDown || k == TileKind.Stairs) entrance = true;
                    foreach (int n in Neighbours4(c))
                    {
                        var kn = lv.Rule[n].Kind;
                        if (lv.Doorway[n] || kn == TileKind.Door || kn == TileKind.Arch || lv.Rule[n].Secret) entrance = true;
                        if (Open(n) && !seen[n]) { seen[n] = true; q.Enqueue(n); }
                    }
                }
                if (!border && !outdoor && entrance && area.Count <= MaxTiles)
                    foreach (int c in area) r[c] = true;
            }
            return r;
        }

        /// <summary>A gap in a wall running east–west (a doorway passed north–south).</summary>
        static bool EW(Level lv, int i) => Gap(lv, i, 1, out _, out _);
        /// <summary>A gap in a wall running north–south.</summary>
        static bool NS(Level lv, int i) => Gap(lv, i, N, out _, out _);

        /// <summary>Widest gap in a wall taken for a doorway (shop fronts, stables and the pub are 2–3 tiles open).</summary>
        const int MaxGap = 3;

        /// <summary>
        /// Tile i is in a gap of a wall running along step (1: east–west, N: north–south): a run
        /// of 1..MaxGap open tiles [first, last] with a barrier at each end. A gap wider than one
        /// tile must be open on both sides along its whole width (a passage through the wall,
        /// not a nook beside it), and the wall must carry on straight past one of its ends.
        /// </summary>
        static bool Gap(Level lv, int i, int step, out int first, out int last)
        {
            first = last = i;
            if (lv.Barrier[i]) return false;
            bool Same(int a, int b) => step == 1 ? a / N == b / N : true;
            bool Inside(int c, int from) => c >= 0 && c < N * N && Same(c, from);
            while (Inside(first - step, first) && !lv.Barrier[first - step] && last - first < MaxGap * step) first -= step;
            while (Inside(last + step, last) && !lv.Barrier[last + step] && last - first < MaxGap * step) last += step;
            if (!Inside(first - step, first) || !Inside(last + step, last) || !lv.Barrier[first - step] || !lv.Barrier[last + step]) return false;
            if (first == last) return true;
            int side = step == 1 ? N : 1;
            bool Open(int c, int from) => c >= 0 && c < N * N && (side == N || c / N == from / N) && !lv.Barrier[c];
            // the wall carries on straight past at least one end, open on both sides there (a room
            // the width of the gap, between two walls, is not a gap: its ends are cross walls)
            bool Continues(int end, int dir) => Inside(end + dir, end) && lv.Barrier[end + dir] && Open(end - side, end) && Open(end + side, end);
            if (!Continues(first - step, -step) && !Continues(last + step, step)) return false;
            for (int c = first; c <= last; c += step)
            {
                int a = c - side, b = c + side;
                if (a < 0 || b >= N * N || (side == 1 && (a / N != c / N || b / N != c / N))) return false;
                if (lv.Barrier[a] || lv.Barrier[b]) return false;
            }
            return true;
        }

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
                        case PartPlace.Pivot: Pivot(part.Prefab, x + px, py, z + pz, pyaw, part.SX, part.SY, part.SZ); break;
                        default: Centre(part.Prefab, x + px, py, z + pz, pyaw, part.SX, part.SY, part.SZ); break;
                    }
                    if (part.Look != null) Last(pl => { pl.Look = part.Look; return pl; });
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
            var garden = Array.IndexOf(tiles.Garden.Locations, p.Location.Number) >= 0 ? Gardens(g) : new bool[N * N];
            for (int c = 0; c < N * N; c++)
            {
                var r = g.Rule[c];
                if (g.Trough[c]) { p.GroundPaint[c] = Paint.Paved; continue; }
                if (garden[c]) { p.Garden[c] = true; Plant(ctx, c); continue; }
                if (r.Kind == TileKind.Water) { p.Water[c] = true; continue; }
                if (g.Drawbridge[c])
                {
                    // the river runs on under the deck, which is lowered by day and raised at night
                    p.Water[c] = true;
                    var (dx, dz, sx, sz, hx, hz) = g.Deck[c];
                    ctx.Top("wood_floor", dx, 0.05f, dz, 0, sx, 1, sz);
                    string look = FormattableString.Invariant($"drawbridge:{hx:0.##},{hz:0.##}");
                    ctx.Last(pl => { pl.Look = look; return pl; });
                    continue;
                }
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
        /// Drawbridges (Constructing world assets 72, 73): a line of bridge tiles along a row with
        /// water at both ends is crossed north–south (along a column, east–west); the lines that
        /// touch make one bridge, hinged on its edge facing the middle of the map (the town or
        /// castle side, or as town_tiles.json "drawbridge_hinge" says), so the whole deck swings
        /// up about that edge. The deck is 2 m longer than its tiles: 1.5 m onto the hinge's bank, 0.5 m onto the other.
        /// </summary>
        void Drawbridges(Level lv)
        {
            bool Water(int c) => lv.Rule[c].Kind == TileKind.Water;
            bool Plank(int c) => lv.Rule[c].Drawbridge;
            var across = new int[N * N]; // 1: crossed north–south, 2: east–west
            for (int dir = 0; dir < 2; dir++)
            {
                int step = dir == 0 ? 1 : N;
                for (int c = 0; c < N * N; c++)
                {
                    if (!Plank(c)) continue;
                    int prev = c - step;
                    bool rowStart = dir == 0 ? c % N == 0 : c < N;
                    if (!rowStart && Plank(prev)) continue; // not the start of a line
                    int end = c;
                    while ((dir == 0 ? (end + 1) % N != 0 : end + N < N * N) && Plank(end + step)) end += step;
                    bool before = !rowStart && Water(prev);
                    bool after = (dir == 0 ? (end + 1) % N != 0 : end + N < N * N) && Water(end + step);
                    if (!before || !after) continue;
                    for (int t = c; t <= end; t += step) across[t] = dir == 0 ? 1 : 2;
                }
            }
            var seen = new bool[N * N];
            for (int s0 = 0; s0 < N * N; s0++)
            {
                if (seen[s0] || across[s0] == 0) continue;
                var deck = new List<int>();
                var q = new Queue<int>();
                q.Enqueue(s0); seen[s0] = true;
                while (q.Count > 0)
                {
                    int c = q.Dequeue();
                    deck.Add(c);
                    foreach (int n in Neighbours4(c))
                        if (!seen[n] && across[n] == across[s0]) { seen[n] = true; q.Enqueue(n); }
                }
                // the deck runs across the water (z for a north–south crossing, x for east–west), 2 m
                // longer than its tiles to reach over the banks, hinged on one end: the side
                // facing the middle of the map (the town or castle side) unless the settlement says
                bool ns = across[s0] == 1;
                var plan = new SettlementPlan();
                float A(int c) => ns ? CellZ(plan, c) : CellX(plan, c);
                float lo = deck.Min(A) - 1, hi = deck.Max(A) + 1, length = hi - lo;
                int count = (int)Math.Round(length / 2);
                bool hingeHigh = (lo + hi) / 2 < 0; // the middle of the map is north (east) of the deck
                if (tiles.DrawbridgeHinge.TryGetValue(planning, out string side))
                    hingeHigh = side == "n" || side == "e";
                // half a metre onto the far bank, a metre and a half onto the hinge's bank
                const float FarBank = 0.5f, HingeBank = 1.5f;
                float start = lo - (hingeHigh ? FarBank : HingeBank), end = hi + (hingeHigh ? HingeBank : FarBank);
                float stretch = (end - start) / length, hinge = hingeHigh ? end : start;
                foreach (int c in deck)
                {
                    int i = (int)Math.Round((A(c) - (lo + 1)) / 2);
                    float centre = start + (i + 0.5f) * (end - start) / count;
                    lv.Drawbridge[c] = true;
                    lv.Deck[c] = ns ? (CellX(plan, c), centre, 1f, stretch, 0f, hinge - centre)
                                    : (centre, CellZ(plan, c), stretch, 1f, hinge - centre, 0f);
                }
            }
        }

        /// <summary>
        /// The small water features of the ground floor (in the settlements listed for it) (a body of water of at most Garden.MaxTiles
        /// tiles clear of the map's edge, troughs aside): planted as flower beds, not dug as ponds.
        /// </summary>
        bool[] Gardens(Level g)
        {
            var r = new bool[N * N];
            var seen = new bool[N * N];
            bool Water(int c) => g.Rule[c].Kind == TileKind.Water && !g.Trough[c];
            for (int s0 = 0; s0 < N * N; s0++)
            {
                if (seen[s0] || !Water(s0)) continue;
                var body = new List<int>();
                var q = new Queue<int>();
                q.Enqueue(s0); seen[s0] = true;
                bool edge = false;
                while (q.Count > 0)
                {
                    int c = q.Dequeue();
                    body.Add(c);
                    int x = c % N, y = c / N;
                    if (x == 0 || y == 0 || x == N - 1 || y == N - 1) edge = true;
                    foreach (int n in Neighbours8(c))
                        if (!seen[n] && Water(n)) { seen[n] = true; q.Enqueue(n); }
                }
                if (!edge && body.Count <= tiles.Garden.MaxTiles)
                    foreach (int c in body) r[c] = true;
            }
            return r;
        }

        /// <summary>A flower bed tile: bushes and flowers scattered over it (picked by a hash of the tile).</summary>
        void Plant(Ctx ctx, int c)
        {
            var gd = tiles.Garden;
            var p = ctx.Plan;
            int tx = c % N, ty = c / N;
            float x = CellX(p, c), z = CellZ(p, c);
            void Scatter(string[] list, int count, int salt)
            {
                if (list.Length == 0) return;
                for (int k = 0; k < count; k++)
                {
                    float ox = (Hash(tx, ty, salt + 3 * k) % 1000 / 1000f - 0.5f) * 1.7f;
                    float oz = (Hash(tx, ty, salt + 3 * k + 1) % 1000 / 1000f - 0.5f) * 1.7f;
                    ctx.Stand(list[Hash(tx, ty, salt + 3 * k + 2) % list.Length], x + ox, 0, z + oz, Hash(tx, ty, salt + k) % 360);
                }
            }
            Scatter(gd.Bushes, gd.BushesPerTile, 300);
            Scatter(gd.Flowers, gd.FlowersPerTile, 400);
        }

        /// <summary>A solid wall block (a wall tile built on this floor).</summary>
        static bool Solid(Level lv, int c) => lv != null && lv.Built[c] && lv.Rule[c].Kind == TileKind.Wall;

        /// <summary>
        /// Inter-level floors (wood: stone is impractical up there): one 2 m piece per tile whose
        /// top is the top of the wall zone below. Rooms, doorways, openings and wall-walks get one
        /// unless they stand on a solid wall block of the floor below.
        /// </summary>
        /// <summary>
        /// The basement: its rooms are carved out of the ground a storey deep (the terrain follows
        /// Plan.Basement), floored with paving, and roofed at street level with the ground floor's
        /// stone, on which whatever stands above them stands. Its walls fill the rest of the
        /// storey and hide the cut's slopes.
        /// </summary>
        void Basement(Ctx ctx, List<Level> levels, TownStyle style)
        {
            var b = levels.FirstOrDefault(l => l.Z < 0);
            if (b == null) return;
            var p = ctx.Plan;
            for (int c = 0; c < N * N; c++)
            {
                // the rooms, and the doors, arches and secret walls walked through between them
                var k = b.Rule[c].Kind;
                bool passage = b.Walked != null && b.Walked[c] && (k == TileKind.Door || k == TileKind.Arch || b.Rule[c].Secret);
                if (!passage && (!(b.Interior[c] || b.Doorway[c]) || b.Barrier[c])) continue;
                p.Basement[c] = true;
                p.GroundPaint[c] = Paint.Paved;
                p.Water[c] = false;
                ctx.Top(style.Sill, CellX(p, c), 0, CellZ(p, c), 0);
            }
        }

        void Floors(Ctx ctx, Level lv, Level below, TownStyle style)
        {
            if (lv.Z == 0 || below == null) return;
            var p = ctx.Plan;
            float y = p.LevelY(lv.Z);
            for (int c = 0; c < N * N; c++)
            {
                var k = lv.Rule[c].Kind;
                bool floored = lv.Interior[c] || lv.Doorway[c] || (lv.Walked != null && lv.Walked[c])
                               || (lv.Built[c] && (lv.Rule[c].IsOpening || k == TileKind.Battlement));
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

        /// <summary>
        /// A doorway between storey-high walls (not parapets: a wall-walk between two parapets is
        /// open to the sky and gets no lintel).
        /// </summary>
        static bool TallFlanks(Level lv, int c)
        {
            bool Tall(int n) => lv.Built[n] && lv.Rule[n].Kind != TileKind.Battlement;
            if (Gap(lv, c, 1, out int first, out int last)) return Tall(first - 1) && Tall(last + 1);
            if (Gap(lv, c, N, out first, out last)) return Tall(first - N) && Tall(last + N);
            return Tall(c - N) && Tall(c + N);
        }

        /// <summary>A wall tile runs east–west when it continues that way (or nowhere north–south).</summary>
        static bool AlongX(Level lv, int c)
        {
            int x = c % N, y = c / N;
            return WallLike(lv, x - 1, y) || WallLike(lv, x + 1, y) || !(WallLike(lv, x, y - 1) || WallLike(lv, x, y + 1));
        }

        /// <summary>
        /// Wall tiles are solid 2 m × 2 m × 4 m blocks (the wall zone, one storey). Stone walls are
        /// four courses of 2×1×1 bricks laid along their run, each course 1 m out of phase with the
        /// one below; 1×1×1 bricks fill the odd ends, so neighbouring tiles of a run share bricks.
        /// Cobblestone walls are a wooden frame filled with stone fence; windows a sill and arches
        /// with the opening between (each arch piece with a twin turned 180°, so it arches from
        /// both sides); arrow slits leave a cross-shaped gap in the second course; doors are 3 m
        /// leaves (see Door); archways and doorways are open 3 m high. Openings have a lintel
        /// course of bricks on top.
        /// </summary>
        void Walls(Ctx ctx, Level lv, TownStyle style, CobbleStyle cobble)
        {
            var p = ctx.Plan;
            float y0 = p.LevelY(lv.Z);
            const int G = N * 2; // 1 m grid
            var along = new bool[N * N];
            for (int c = 0; c < N * N; c++) along[c] = AlongX(lv, c);

            // courses of bricks
            int courses = (int)Math.Round(p.Storey), lintel = courses - 1;
            for (int course = 0; course < courses; course++)
            {
                bool Brick(int c)
                {
                    if (lv.Doorway[c]) return course == lintel && TallFlanks(lv, c);
                    if (!lv.Built[c]) return false;
                    var r = lv.Rule[c];
                    if (r.Kind == TileKind.Wall) return r.Build == WallBuild.Brick;
                    if (r.Kind == TileKind.Window && r.Narrow) return course != 1;
                    return r.IsOpening && course == lintel;
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
                        // the arch piece curves down on one side only: a twin turned 180° in the same
                        // place makes a full arch across the 2 m opening
                        for (int side = -1; side <= 1; side += 2)
                        {
                            Ctx.Rotate(0, side * 0.5f, yaw, out float ox, out float oz);
                            ctx.Centre(style.Arch, cx + ox, y0 + 2.5f, cz + oz, yaw);
                            ctx.Centre(style.Arch, cx + ox, y0 + 2.5f, cz + oz, (yaw + 180) % 360);
                        }
                        break;
                    case TileKind.Door:
                        if (r.Gate && ctx.Known(style.Gate)) ctx.Centre(style.Gate, cx, y0 + 1.5f, cz, yaw);
                        else Door(ctx, lv, style, c, along[c], y0);
                        if (r.Lock != null) ctx.Last(pl => { pl.Lock = r.Lock; return pl; });
                        break;
                }
            }
        }

        /// <summary>
        /// A door: one 2 m × 3 m leaf filling its tile, its pivot the hinge on one side of the tile
        /// (a viewport door too: Valheim's 2 m × 2 m door is never used). A double door (two door
        /// tiles side by side in the wall) is two frameless leaves (the doors with a frame of their
        /// own would put a post in the middle), each hinged on the side away from the other so the
        /// pair opens from the middle.
        /// </summary>
        static void Door(Ctx ctx, Level lv, TownStyle style, int c, bool alongX, float y0)
        {
            var p = ctx.Plan;
            float cx = CellX(p, c), cz = CellZ(p, c), yaw = alongX ? 0 : 90;
            int x = c % N;
            int partner = alongX ? (x + 1 < N && IsDoor(lv, c + 1) ? c + 1 : x > 0 && IsDoor(lv, c - 1) ? c - 1 : -1)
                                 : (c + N < N * N && IsDoor(lv, c + N) ? c + N : c >= N && IsDoor(lv, c - N) ? c - N : -1);
            // both doors hinge on their local +x side: turn it away from the other leaf
            Ctx.Rotate(1, 0, yaw, out float ux, out float uz);
            if (partner >= 0 && (CellX(p, partner) - cx) * ux + (CellZ(p, partner) - cz) * uz > 0)
            {
                yaw += 180;
                ux = -ux; uz = -uz;
            }
            yaw %= 360;
            if (partner >= 0 && ctx.Known(style.DoubleDoor))
            {
                // the gate's pivot is the middle of its tile, 1 m up
                ctx.Pivot(style.DoubleDoor, cx, y0 + 1, cz, yaw);
                return;
            }
            // the single door's leaf reaches from its pivot (the hinge) along its local −x
            ctx.Pivot(style.Door, cx + ux * p.Tile / 2, y0, cz + uz * p.Tile / 2, yaw);
        }

        static bool IsDoor(Level lv, int c) => lv.Built[c] && lv.Rule[c].Kind == TileKind.Door && !lv.Rule[c].Gate;

        /// <summary>
        /// A cobblestone wall tile: two rows of stone fence, a storey high, in a frame of posts on
        /// the tile corners (shared along a run) with a beam along the top of each open face.
        /// </summary>
        static void CobbleWall(Ctx ctx, Level lv, CobbleStyle s, int c, bool alongX, float y0, HashSet<(int, int)> posts)
        {
            var p = ctx.Plan;
            float cx = CellX(p, c), cz = CellZ(p, c), yaw = alongX ? 0 : 90;
            float storey = p.Storey;
            for (int course = 0; course < storey; course++)
                for (int side = -1; side <= 1; side += 2)
                {
                    Ctx.Rotate(0, side * 0.5f, yaw, out float ox, out float oz);
                    ctx.Pivot(s.Fence, cx + ox, y0 + course, cz + oz, yaw);
                }
            foreach (var (dx, dz) in new[] { (-1, -1), (-1, 1), (1, -1), (1, 1) })
            {
                if (!posts.Add(((int)Math.Round(cx + dx), (int)Math.Round(cz + dz)))) continue;
                // 2 m posts, topped up with a 1 m one on an odd storey height
                float py = y0;
                for (; py + 2 <= y0 + storey; py += 2) ctx.Stand(s.Post, cx + dx, py, cz + dz, 0);
                if (py < y0 + storey) ctx.Stand(s.PostShort, cx + dx, py, cz + dz, 0);
            }
            int x = c % N, y = c / N;
            foreach (var (nx, ny, dx, dz, byaw) in new[] { (x, y - 1, 0f, 1f, 0f), (x, y + 1, 0f, -1f, 0f), (x + 1, y, 1f, 0f, 90f), (x - 1, y, -1f, 0f, 90f) })
                if (!WallLike(lv, nx, ny))
                    ctx.Centre(s.Beam, cx + dx, y0 + storey - 0.2f, cz + dz, byaw);
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
            float y = p.LevelY(lv.Z);
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
        /// A building with parapets on its top is a flat roof (a wall-walk): it gets no roof, except
        /// over the tiles the floor above draws as roof.
        /// </summary>
        void Roofs(Ctx ctx, List<Level> levels, RoofSet roof, RoofSet cottage)
        {
            for (int i = 0; i < levels.Count; i++)
            {
                var lv = levels[i];
                if (lv.Z < 0) continue; // the basement's roof is the ground floor
                var above = i + 1 < levels.Count ? levels[i + 1] : null;
                bool Parapet(int c) => above != null && above.Built[c] && above.Rule[c].Kind == TileKind.Battlement;
                var cells = new bool[N * N];
                for (int c = 0; c < N * N; c++)
                    cells[c] = lv.Covered[c] && !(above != null && (above.Covered[c] || above.Built[c] || (above.Walked != null && above.Walked[c])));
                float baseY = ctx.Plan.Storey * (lv.Z + 1);
                // a roof tile drawn on the floor above always gets a roof, parapets or not
                bool RoofTile(int c) => above != null && tiles[above.Raw[c]].Kind == TileKind.Roof;
                foreach (var building in RoofFootprints(cells))
                {
                    if (Enumerable.Range(0, N * N).Any(c => building[c] && (Parapet(c) || Neighbours8(c).Any(Parapet))))
                    {
                        var roofed = new bool[N * N];
                        for (int c = 0; c < N * N; c++) roofed[c] = building[c] && RoofTile(c);
                        foreach (var part in RoofFootprints(roofed))
                            HipRoof(ctx, IsCottage(lv, part) ? cottage : roof, part, baseY);
                        continue;
                    }
                    HipRoof(ctx, IsCottage(lv, building) ? cottage : roof, building, baseY);
                }
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
            float y = p.LevelY(lv.Z);
            var done = new bool[N * N];
            var beds = Beds(lv);
            var footlockers = Footlockers(lv, beds);
            for (int c = 0; c < N * N; c++)
            {
                if (done[c] || lv.Barrier[c]) continue;
                if (lv.Z != 0 && !(lv.Walked != null ? lv.Walked[c] : lv.Interior[c])) continue; // upper maps repeat the ground below
                var r = lv.Rule[c];
                int tx = c % N, ty = c / N;
                float x = CellX(p, c), z = CellZ(p, c);
                if (lv.Trough[c])
                {
                    if (r.Prefab != null) ctx.Stand(r.Prefab, x, y, z, Hash(tx, ty, 13) % 4 * 90);
                    continue;
                }
                switch (r.Kind)
                {
                    case TileKind.Prop:
                        if (r.SignPost == "post" && UnderBanner(lv, c)) break;
                        if (r.Parts != null) ctx.Parts(r.Parts, x, y, z, r.Yaw);
                        else if (r.Choices != null) ctx.Stand(r.Choices[Hash(tx, ty, 8) % r.Choices.Length], x, y, z, Hash(tx, ty, 9) % 4 * 90);
                        else if (r.Prefab != null && r.WallSide is float side)
                        {
                            // mounted on the wall on that side, facing into the room (checked in game:
                            // the sconce's back is its local −x, and the tile's side is the far one)
                            Ctx.Rotate(0, -0.85f, side, out float ox, out float oz);
                            ctx.Centre(r.Prefab, x + ox, y + 1.8f, z + oz, side + 270);
                        }
                        else if (r.Prefab != null) ctx.Stand(r.Prefab, x, y, z, footlockers.TryGetValue(c, out float lockerYaw) ? lockerYaw : r.Yaw);
                        break;
                    case TileKind.Sign:
                        ShopSign(ctx, lv, c, y);
                        break;
                    case TileKind.Chair:
                        ctx.Stand("piece_chair", x, y, z, r.Yaw);
                        break;
                    case TileKind.BedHead:
                    {
                        // cottages have plain wooden beds, everyone else the fancy one
                        string bed = cottage[c] ? "bed" : "piece_bed02";
                        var b = beds.FirstOrDefault(t => t.head == c || t.foot == c);
                        if (b.head == b.foot) { ctx.Stand(bed, x, y, z, 0); break; }
                        int other = b.head == c ? b.foot : b.head;
                        done[other] = true;
                        float hx = CellX(p, b.head), hz = CellZ(p, b.head), fx = CellX(p, b.foot), fz = CellZ(p, b.foot);
                        // both beds' headboards are at their local −z
                        float yaw = (float)(Math.Atan2(-(hx - fx), -(hz - fz)) * 180 / Math.PI + 360) % 360;
                        ctx.Stand(bed, (hx + fx) / 2, y, (hz + fz) / 2, yaw);
                        break;
                    }
                    case TileKind.Table:
                    {
                        int x1 = tx;
                        while (x1 + 1 < N && lv.Rule[ty * N + x1 + 1].Kind == TileKind.Table) x1++;
                        int len = x1 - tx + 1;
                        for (int i = tx; i <= x1; i++) done[ty * N + i] = true;
                        float mx = (p.LocalX(tx) + p.LocalX(x1)) / 2;
                        string table = len >= 2 ? "piece_table_oak" : "piece_table_round";
                        if (len >= 2 && geo.TryGet(table, out var ob))
                        {
                            // the run is laid with oak tables end to end (as many as come nearest their
                            // own 6.45 m), stretched or squeezed along their length to fill it exactly
                            float run = len * p.Tile, own = ob.MaxX - ob.MinX;
                            int k = Math.Max(1, (int)Math.Round(run / own));
                            float each = run / k;
                            for (int i = 0; i < k; i++)
                                ctx.Stand(table, p.LocalX(tx) - p.Tile / 2 + each * (i + 0.5f), y, z, 0, each / own, 1, 1);
                        }
                        else ctx.Stand(table, mx, y, z, 0);
                        // candelabra tiles (0xbe): a cluster of candles on the table top over the tile
                        float top = y + (geo.TryGet(table, out var tb) ? tb.MaxY : 0.8f);
                        for (int i = tx; i <= x1; i++)
                        {
                            var tr = lv.Rule[ty * N + i];
                            if (tr.Candles <= 0 || tr.Prefab == null) continue;
                            for (int k = 0; k < tr.Candles; k++)
                            {
                                double a = 2 * Math.PI * k / tr.Candles + Hash(i, ty, 11) % 60 * Math.PI / 180;
                                float rad = tr.Candles == 1 ? 0 : 0.22f;
                                ctx.Stand(tr.Prefab, p.LocalX(i) + rad * (float)Math.Cos(a), top, z + rad * (float)Math.Sin(a), Hash(i, ty, 12 + k) % 360);
                            }
                        }
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
            if (target < 0 || target >= levels.Count) return;
            var p = ctx.Plan;
            int last = p.Pieces.Count - 1;
            var pl = p.Pieces[last];
            pl.Climb = true;
            pl.ClimbX = x;
            pl.ClimbZ = z;
            pl.ClimbY = p.LevelY(levels[target].Z) + 0.2f;
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

        // ── armourers' display walls ─────────────────────────────────────────────────────────

        /// <summary>
        /// The armourer's wall: item stands along the wall behind where the armourer works by day
        /// (away from the counter they stand at), showing the families of what the shop sells
        /// (DATA.OVL), in proportion, each family cycling through its Valheim models.
        /// </summary>
        void ArmouryWall(Ctx ctx, Data.Location loc, List<Level> levels)
        {
            var disp = tiles.Armoury;
            if (disp.Stand == null || disp.Rows.Length == 0) return;
            var stock = data.Ovl.ArmouryStock(loc.Number);
            var npc = data.NpcsOf(loc).FirstOrDefault(n => n.Exists && n.IsShopkeeper && n.Trade == ShopTrade.Armoury);
            if (stock.Count == 0 || npc == null) return;
            const int place = 1; // the day's place (shop hours: from the 2nd hour, and back after the 3rd)
            var lv = levels.FirstOrDefault(l => l.Z == npc.Schedule.Z[place]);
            if (lv == null) return;
            int m = npc.Schedule.Y[place] * N + npc.Schedule.X[place];

            // away from the counter (a table tile beside them), else towards the nearest wall
            var dirs = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
            int Step(int c, (int dx, int dy) d)
            {
                int x = c % N + d.dx, y = c / N + d.dy;
                return x < 0 || y < 0 || x >= N || y >= N ? -1 : y * N + x;
            }
            int WallBehind((int, int) d)
            {
                int c = m;
                for (int i = 0; i < 6; i++)
                {
                    int n = Step(c, d);
                    if (n < 0) return -1;
                    if (lv.Barrier[n]) return Solid(lv, n) ? n : -1;
                    c = n;
                }
                return -1;
            }
            (int dx, int dy) back = (0, 0);
            int wall = -1;
            foreach (var d in dirs)
            {
                int counter = Step(m, (-d.Item1, -d.Item2));
                if (counter >= 0 && lv.Rule[counter].Kind == TileKind.Table && (wall = WallBehind(d)) >= 0) { back = d; break; }
            }
            if (wall < 0)
                foreach (var d in dirs.OrderBy(d => { int c = m, k = 0; while ((c = Step(c, d)) >= 0 && !lv.Barrier[c]) k++; return k; }))
                    if ((wall = WallBehind(d)) >= 0) { back = d; break; }
            if (wall < 0) return;

            // the run of wall facing the room, Reach tiles either side
            var run = new List<int> { wall };
            foreach (int sgn in new[] { -1, 1 })
            {
                var along = (back.dy * sgn, back.dx * sgn);
                int c = wall;
                for (int i = 0; i < disp.Reach; i++)
                {
                    int n = Step(c, along);
                    int front = n < 0 ? -1 : Step(n, (-back.dx, -back.dy));
                    if (n < 0 || front < 0 || !Solid(lv, n) || lv.Barrier[front]) break;
                    if (sgn < 0) run.Insert(0, n); else run.Add(n);
                    c = n;
                }
            }

            // what is shown: the families sold, in proportion
            var weights = disp.Families.Select(f => (f.models, w: stock.Count(id => f.u5.Contains(id)))).Where(f => f.w > 0).ToList();
            if (weights.Count == 0) return;
            // the rows are staggered (the upper ones between the tiles of the lower) so long items don't overlap
            int Spots(int row) => row % 2 == 0 ? run.Count : run.Count - 1;
            int slots = Enumerable.Range(0, disp.Rows.Length).Sum(Spots), total = weights.Sum(f => f.w);
            var items = new List<string>();
            int salt = loc.Number * 31;
            for (int f = 0; f < weights.Count; f++)
            {
                int n = f == weights.Count - 1 ? slots - items.Count : Math.Max(1, (int)Math.Round((double)slots * weights[f].w / total));
                var models = weights[f].models;
                for (int k = 0; k < n && items.Count < slots; k++) items.Add(models[(k + Hash(salt, f, 21)) % models.Length]);
            }
            // spread the families over the wall rather than in blocks
            items = items.Select((it, i) => (it, key: Hash(salt, i, 22))).OrderBy(t => t.key).Select(t => t.it).ToList();

            var p = ctx.Plan;
            float side = back.dx > 0 ? 90 : back.dx < 0 ? 270 : back.dy > 0 ? 180 : 0; // yaw of the wall's direction
            float y0 = p.LevelY(lv.Z);
            int slot = 0;
            for (int row = 0; row < disp.Rows.Length; row++)
                for (int i = 0; i < Spots(row); i++)
                {
                    if (slot >= items.Count) break;
                    // in front of the wall: the tile before it, its far edge (the stand's front is its local +z)
                    float x = 0, z = 0;
                    foreach (int c in row % 2 == 0 ? new[] { run[i] } : new[] { run[i], run[i + 1] })
                    {
                        int front = Step(c, (-back.dx, -back.dy));
                        x += CellX(p, front) + back.dx * 0.94f;
                        z += CellZ(p, front) - back.dy * 0.94f;
                    }
                    int n = row % 2 == 0 ? 1 : 2;
                    ctx.Pivot(disp.Stand, x / n, y0 + disp.Rows[row], z / n, side + 180);
                    string item = items[slot++];
                    ctx.Last(pl => { pl.StandItem = item; return pl; });
                }
        }

        // ── shop signs ───────────────────────────────────────────────────────────────────────

        static bool SignTop(Level lv, int c) => c >= 0 && c < N * N && lv.Rule[c].SignPost == "top";
        static bool SignFoot(Level lv, int c) => c >= 0 && c < N * N && lv.Rule[c].SignPost == "post";

        /// <summary>Signpost tops beside a guild icon, each standing on its post: the sides (−1 west, +1 east) the banner hangs from.</summary>
        static IEnumerable<int> SignArms(Level lv, int icon)
        {
            int tx = icon % N, ty = icon / N;
            if (ty + 1 >= N) yield break;
            if (tx > 0 && SignTop(lv, icon - 1) && SignFoot(lv, icon - 1 + N)) yield return -1;
            if (tx + 1 < N && SignTop(lv, icon + 1) && SignFoot(lv, icon + 1 + N)) yield return 1;
        }

        /// <summary>A post foot under a guild icon that hangs between posts: the banner hangs there instead.</summary>
        static bool UnderBanner(Level lv, int c) => c >= N && lv.Rule[c - N].Kind == TileKind.Sign && SignArms(lv, c - N).Any();

        /// <summary>
        /// A guild icon (Constructing world assets 224-249): a banner printed with the icon's
        /// TILES.16 picture (look "icon:&lt;tile&gt;"). The maps draw a signpost side-on: its foot
        /// (0xe0) on the ground, its top (0xe1/0xe2) a tile up the screen and the icon beside the
        /// top. In Valheim the posts stand at their feet and the banner hangs at the same height
        /// between or beside them, from beams off the post tops, facing south as the map is seen.
        /// An icon over a lone post hangs on the post's face; one with no post at all hangs on a
        /// wall next to it (an indoor shop) or, failing that, on a post of its own.
        /// </summary>
        void ShopSign(Ctx ctx, Level lv, int c, float y)
        {
            var p = ctx.Plan;
            var r = lv.Rule[c];
            string look = $"icon:{lv.Raw[c]:x2}";
            const float Rod = 3.9f; // the banner's rod, just under the 4 m post tops
            float x = CellX(p, c), z = CellZ(p, c);
            var arms = SignArms(lv, c).ToList();
            if (arms.Count > 0)
            {
                float bz = CellZ(p, c + N);
                foreach (int side in arms) ctx.Centre("wood_beam", x + side * p.Tile / 2, y + Rod + 0.05f, bz, 0);
                ctx.Pivot(r.Prefab, x, y + Rod, bz, 90);
            }
            else if (c + N < N * N && SignFoot(lv, c + N))
                ctx.Pivot(r.Prefab, x, y + Rod, CellZ(p, c + N) - 0.3f, 90);
            else
            {
                // a wall beside it (north first, as the map shows the icon above the wall), else its own post
                int wall = new[] { c - N, c + 1, c + N, c - 1 }.Where(n => n >= 0 && n < N * N && Math.Abs(n % N - c % N) <= 1 && Solid(lv, n))
                    .DefaultIfEmpty(-1).First();
                if (wall >= 0)
                {
                    int sx = wall % N - c % N, sy = wall / N - c / N;
                    float side = sx > 0 ? 90 : sx < 0 ? 270 : sy > 0 ? 180 : 0;
                    ctx.Pivot(r.Prefab, x + sx * 0.88f, y + p.Storey - 0.2f, z - sy * 0.88f, side + 90);
                }
                else
                {
                    ctx.Stand("wood_pole2", x, y, z, 0);
                    ctx.Stand("wood_pole2", x, y + 2, z, 0);
                    ctx.Pivot(r.Prefab, x, y + Rod, z - 0.3f, 90);
                }
            }
            ctx.Last(pl => { pl.Look = look; return pl; });
        }

        /// <summary>
        /// The beds of a floor as (head, foot) tiles. The maps draw a bed's head tile (0xab) and
        /// foot tile (0xac) the same way round wherever it stands, so the head goes to the end
        /// nearer a wall (along the bed); on a tie it stays where the map draws it. A lone head
        /// tile is (c, c).
        /// </summary>
        List<(int head, int foot)> Beds(Level lv)
        {
            var beds = new List<(int, int)>();
            var used = new bool[N * N];
            int ToWall(int c, int step)
            {
                for (int k = 0; k < N; k++)
                {
                    int x = c % N + (step == 1 ? k + 1 : step == -1 ? -k - 1 : 0), y = c / N + (step == N ? k + 1 : step == -N ? -k - 1 : 0);
                    if (x < 0 || y < 0 || x >= N || y >= N || lv.Barrier[y * N + x]) return k;
                }
                return N;
            }
            for (int c = 0; c < N * N; c++)
            {
                if (lv.Rule[c].Kind != TileKind.BedHead || lv.Barrier[c] || used[c]) continue;
                if (lv.Z != 0 && !(lv.Walked != null ? lv.Walked[c] : lv.Interior[c])) continue;
                int foot = Neighbours4(c).Where(n => lv.Rule[n].Kind == TileKind.BedFoot && !used[n]).DefaultIfEmpty(-1).First();
                used[c] = true;
                if (foot < 0) { beds.Add((c, c)); continue; }
                used[foot] = true;
                int step = foot - c;
                beds.Add(ToWall(foot, step) < ToWall(c, -step) ? (foot, c) : (c, foot));
            }
            return beds;
        }

        /// <summary>Footlockers: chests beside the foot of a bed, turned to face away from it (a chest's front is its local +z).</summary>
        Dictionary<int, float> Footlockers(Level lv, List<(int head, int foot)> beds)
        {
            var r = new Dictionary<int, float>();
            foreach (var (head, foot) in beds)
            {
                if (head == foot) continue;
                foreach (int n in Neighbours4(foot))
                {
                    if (n == head || lv.Rule[n].Prefab == null || !lv.Rule[n].Prefab.Contains("chest") || r.ContainsKey(n)) continue;
                    int dx = n % N - foot % N, dy = n / N - foot / N; // tiles, y south
                    r[n] = (float)((Math.Atan2(dx, -dy) * 180 / Math.PI + 360) % 360);
                }
            }
            return r;
        }

        /// <summary>
        /// Randotron: the dead end of a one-tile-wide hallway may get a banner or a mounted animal
        /// head on its end wall (picked per settlement, floor and tile).
        /// </summary>
        void HallwayEnds(Ctx ctx, Level lv)
        {
            var rt = tiles.Randotron;
            if (rt.HallwayEnd <= 0 || rt.Banners.Length + rt.Trophies.Length == 0) return;
            var p = ctx.Plan;
            float y = p.LevelY(lv.Z);
            int salt = 1000 + p.Location.Number * 37 + lv.Z * 7;
            // a one-tile hallway is all "doorway" tiles (gaps between two walls)
            bool Floor(int c) => !lv.Barrier[c] && (lv.Walked != null ? lv.Walked[c] : lv.Interior[c] || lv.Doorway[c]);
            for (int c = 0; c < N * N; c++)
            {
                int tx = c % N, ty = c / N;
                if (tx == 0 || ty == 0 || tx == N - 1 || ty == N - 1) continue;
                var k = lv.Rule[c].Kind;
                // an empty floor tile, or the stairs or ladder a hallway so often ends in
                if (!Floor(c) || (k != TileKind.Ground && k != TileKind.Stairs && k != TileKind.LadderUp && k != TileKind.LadderDown)) continue;
                // walls on three sides, the hallway on the fourth
                int open = -1, walls = 0;
                foreach (int n in Neighbours4(c))
                    if (Solid(lv, n)) walls++;
                    else open = n;
                if (walls != 3 || open < 0 || !Floor(open)) continue;
                int sx = c % N - open % N, sy = c / N - open / N; // towards the end wall, in tiles (y south)
                bool corridor = sx != 0 ? lv.Barrier[open - N] && lv.Barrier[open + N] : lv.Barrier[open - 1] && lv.Barrier[open + 1];
                if (!corridor || Hash(tx, ty, salt) % 1000 >= rt.HallwayEnd * 1000) continue;
                float side = sx > 0 ? 90 : sx < 0 ? 270 : sy > 0 ? 180 : 0; // yaw of the end wall's direction
                float dx = sx, dz = -sy, x = CellX(p, c), z = CellZ(p, c);
                int pick = Hash(tx, ty, salt + 1) % (rt.Banners.Length + rt.Trophies.Length);
                if (pick < rt.Banners.Length)
                    // the cloth hangs ~2.9 m under its rod, in the rod's local y–z plane
                    ctx.Pivot(rt.Banners[pick], x + dx * 0.88f, y + p.Storey - 0.2f, z + dz * 0.88f, side + 90);
                else if (rt.TrophyStand != null)
                {
                    // the stand's front (where the head goes) is its local +z
                    ctx.Pivot(rt.TrophyStand, x + dx * 0.94f, y + 2.1f, z + dz * 0.94f, side + 180);
                    string trophy = rt.Trophies[pick - rt.Banners.Length];
                    ctx.Last(pl => { pl.StandItem = trophy; return pl; });
                }
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
                    ctx.Add("sign", x, p.LevelY(lv.Z) + 1.6f, z, yaw, text: text);
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
