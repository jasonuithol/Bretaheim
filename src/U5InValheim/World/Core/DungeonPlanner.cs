using System;
using System.Collections.Generic;
using System.Text;
using U5InValheim.Data;

namespace U5InValheim.WorldGen.Core
{
    /// <summary>DUNGEON dungeon_main's tile sets: crypt (Deceit, Wrong, Covetous), mine (Shame, Hythloth), cave (the rest).</summary>
    public enum DungeonStyle { Cave = 1, Mine = 2, Crypt = 3 }

    /// <summary>What a tile of an assembled dungeon level is built as.</summary>
    public enum DTile : byte { Solid, Floor, Earth, Planks, Water, Lava, Door, Ladder }

    public enum DFeatureKind
    {
        /// <summary>A ladder rising into the ceiling (hallway 0x1_/0x3_, room tile 0xc8).</summary>
        LadderUp,
        /// <summary>An iron grate over a hole in the floor, climbed down (hallway 0x2_/0x3_, room tile 0xc9).</summary>
        LadderDown,
        /// <summary>A hallway chest (0x4_ closed, 0x7_ open): low 3 bits trapped.</summary>
        Chest,
        /// <summary>A fountain to drink from (0x5_: 0 cure, 1 heal, 2 poison, else bad taste).</summary>
        Fountain,
        /// <summary>An energy field filling a hallway cell (0x8_), or a field object in a room.</summary>
        Field,
        /// <summary>A pit (0x60): an iron grate over the hole, climbed down (cmd_klimb_dungeon).</summary>
        Pit,
        /// <summary>A hidden pit (0x61/0x69) or bomb trap (0x62/0x6a) under plain floor.</summary>
        Trap,
        /// <summary>A door leaf across the doorway tile (hallway door cells, room doors).</summary>
        Door,
        /// <summary>A wall face hiding a door (secret door cell 0xd_), revealed by a blow.</summary>
        Secret,
        /// <summary>A piece of furniture (Piece), a light, or a decoration.</summary>
        Prop,
        /// <summary>A hole in the ceiling (the ceiling-hole bit): climbed up with a grapple (cmd_klimb_dungeon).</summary>
        Grapple,
    }

    public struct DFeature
    {
        public DFeatureKind Kind;
        /// <summary>Tile of the level grid (0..87) where it stands.</summary>
        public int U, V;
        /// <summary>Cell index (y·8 + x) it belongs to.</summary>
        public int Cell;
        /// <summary>The cell byte, or the room tile, it comes from.</summary>
        public byte Value;
        /// <summary>Yaw (n 0, e 90, s 180, w 270): the way through a door (its leaf lies across it); the way a secret wall faces.</summary>
        public float Yaw;
        /// <summary>Doors: "key", "magic" (a portcullis), or null.</summary>
        public string Lock;
        /// <summary>Props: the piece placed, in level-local metres.</summary>
        public Placement Piece;

        public override string ToString() => $"{Kind} {Value:x2} at ({U},{V}) cell {Cell}";
    }

    public struct RoomMonster { public int Type, X, Y; }
    public struct RoomObject { public Loot Loot; public int Tile, X, Y; }
    public struct RoomTrigger { public int Index, X, Y, X1, Y1, X2, Y2; public byte Tile; }

    /// <summary>
    /// The original's dungeon facts (DUNGEON.OVL, DNGLOOK.OVL, CMDS.OVL yell_word_of_power): which
    /// room set a dungeon uses, its style, its word of power, the rooms that always fill again.
    /// Dungeon d (0..7) is location 0x21 + d: Deceit, Despise, Destard, Wrong, Covetous, Shame,
    /// Hythloth, Doom.
    /// </summary>
    public static class DungeonInfo
    {
        public const int Count = 8, Levels = 8, FirstLocation = 33;
        /// <summary>Where the party arrives from the surface (enter_dungeon): level 0, cell (1, 1), facing east.</summary>
        public const int StartLevel = 0, StartX = 1, StartY = 1;
        // DATA.OVL (DGROUP)
        const int PersistentRooms = 0x383a, PersistentCount = 0x3840, QtyRange = 0x383f, QtyBase = 0x384d,
                  RandomRoomTypes = 0x385e, WanderingTypes = 0x173c, GroupSize = 0x13c2;

        public static int Location(int dungeon) => FirstLocation + dungeon;

        /// <summary>
        /// The DUNGEON.CBT room set (16 rooms each) of a dungeon: location − 0x21 for Deceit but
        /// location − 0x22 for the others (enter_dungeon_room), so Deceit and Despise share set 0
        /// and the file's 7 sets serve 8 dungeons. Reproduced, not fixed.
        /// </summary>
        public static int RoomSet(int dungeon) => dungeon == 0 ? 0 : dungeon - 1;

        public static int RoomIndex(int dungeon, int room) => RoomSet(dungeon) * 16 + (room & 15);

        public static DungeonStyle Style(int dungeon)
        {
            int d = dungeon + 1;
            return d == 1 || d == 4 || d == 5 ? DungeonStyle.Crypt : d == 6 || d == 7 ? DungeonStyle.Mine : DungeonStyle.Cave;
        }

        public static byte WallTile(DungeonStyle s) => s == DungeonStyle.Crypt ? (byte)0x4f : (byte)0x4d;
        public static byte FloorTile(DungeonStyle s) => s == DungeonStyle.Crypt ? (byte)0x45 : (byte)0x05;

        /// <summary>A room that fills again every time (mark_dungeon_room_cleared's list: Wrong 1, 6, 11, 12; Covetous 0, 11).</summary>
        public static bool Persistent(DataOvl ovl, int dungeon, int room)
        {
            int key = ((Location(dungeon) & 0xf) << 4) + room;
            int n = ovl.U8(PersistentCount);
            for (int i = 0; i < n; i++) if (ovl.U8(PersistentRooms + i) == key) return true;
            return false;
        }

        /// <summary>The dungeon whose word of power the text holds (case-insensitive, anywhere in it; the first in order), or −1.</summary>
        public static int WordIn(DataOvl ovl, string text)
        {
            if (string.IsNullOrEmpty(text)) return -1;
            var words = ovl.WordsOfPower;
            for (int i = 0; i < Count; i++)
                if (text.IndexOf(words[i], StringComparison.OrdinalIgnoreCase) >= 0) return i;
            return -1;
        }

        /// <summary>The map tile of the dungeon's entrance once open (0x16 cave, 0x17 mine, 0x18 dungeon); sealed it is 0xdf.</summary>
        public static byte OpenTile(DataOvl ovl, int dungeon) => ovl.U8(Dg.DungeonOpenTiles + dungeon);

        /// <summary>The wandering monsters' types (spawn_dungeon_monster): rat, bat, spider, ghost, slime, gremlin, gazer, reaper.</summary>
        public static byte[] Wanderers(DataOvl ovl) => ovl.Bytes(WanderingTypes, 8);

        /// <summary>How many of a type come in a group (monster stats +5).</summary>
        public static int GroupOf(DataOvl ovl, int type) => ovl.I8(GroupSize + type * 8);

        /// <summary>
        /// A room's monsters and objects (setup_room_combatants mode 3): tiles below 0x40, 0xb4..0xb7
        /// and 0xe8..0xeb are objects (a chest of quality level·3 + 7, gold 1..level·10 + 10, the
        /// others their DATA.OVL quantity), tiles 0xec..0xef one of four random types, the rest the
        /// monster type (tile − 0x40) / 4.
        /// </summary>
        public static void RoomContents(DataOvl ovl, CombatMap map, int level, Random rnd, List<RoomMonster> monsters, List<RoomObject> objects)
        {
            var random = new int[4];
            for (int i = 0; i < 4; i++) random[i] = ovl.U8(RandomRoomTypes + rnd.Next(0, 8));
            byte[] tiles = map.MonsterTiles, xs = map.MonsterX, ys = map.MonsterY;
            for (int k = 0; k < 16; k++)
            {
                int tile = tiles[k];
                if (tile == 0) continue;
                if (tile < 0x40 || (tile & 0xfc) == 0xb4 || (tile & 0xfc) == 0xe8)
                {
                    int qty;
                    if (tile == 1) qty = level * 3 + 7;
                    else if (tile == 2) qty = rnd.Next(1, level * 10 + 11);
                    else if (tile <= 15) qty = (sbyte)ovl.U8(QtyBase + tile) + rnd.Next(0, Math.Max(1, (int)ovl.U8(QtyRange + tile)));
                    else qty = 0;
                    objects.Add(new RoomObject { Tile = tile, X = xs[k], Y = ys[k], Loot = LootTables.Object(tile, qty) });
                    continue;
                }
                int type = (tile & 0xfc) == 0xec ? random[tile & 3] : (tile - 0x40) >> 2;
                monsters.Add(new RoomMonster { Type = type, X = xs[k], Y = ys[k] });
            }
        }

        /// <summary>A room's triggers (check_room_trigger): stepping on (X, Y) puts Tile at (X1, Y1) and (X2, Y2); (0, 0) is none.</summary>
        public static List<RoomTrigger> Triggers(CombatMap map)
        {
            var r = new List<RoomTrigger>();
            byte[] tx = map.TriggerX, ty = map.TriggerY, ids = map.NewTileIds, x1 = map.NewTile1X, y1 = map.NewTile1Y, x2 = map.NewTile2X, y2 = map.NewTile2Y;
            for (int i = 0; i < 8; i++)
            {
                if (tx[i] == 0 && ty[i] == 0) continue;
                r.Add(new RoomTrigger { Index = i, X = tx[i], Y = ty[i], Tile = ids[i], X1 = x1[i], Y1 = y1[i], X2 = x2[i], Y2 = y2[i] });
            }
            return r;
        }
    }

    /// <summary>Which room tiles are what, for building (DUNGEON.CBT uses the overworld/town tile set).</summary>
    public static class DungeonTiles
    {
        public static DTile Class(byte t)
        {
            switch (t)
            {
                case 0xff: case 0x42: case 0x4b: case 0x4d: case 0x4e: case 0x4f: case 0x5a: case 0xfe:
                case 0x0b: case 0x0c: case 0x0d: case 0x0f:
                    return DTile.Solid;
                case 0x8f: return DTile.Lava;
                case 0x1d: case 0x40: case 0x48: case 0x49: case 0x6a: case 0x6b: return DTile.Planks;
                case 0x01: case 0x02: case 0x03: case 0xd4: return DTile.Water;
                case 0xb8: case 0xb9: case 0xba: case 0xbb: case 0x97: case 0x98: case 0x99: return DTile.Door;
                case 0xc8: case 0xc9: return DTile.Ladder;
                case 0x04: case 0x05: case 0x06: case 0x07: case 0x08: case 0x09: case 0x0a: case 0x1e: case 0x1f:
                    return DTile.Earth;
            }
            if (t >= 0x50 && t <= 0x57) return DTile.Solid;   // crenellations
            if (t >= 0x70 && t <= 0x7f) return DTile.Solid;   // strange walls
            if (t >= 0x60 && t <= 0x6f) return DTile.Water;   // rivers
            if (t >= 0xe4 && t <= 0xe7) return DTile.Water;
            if (t >= 0x20 && t <= 0x37) return DTile.Earth;   // roads, grass
            return DTile.Floor;
        }

        public static bool Open(DTile c) => c != DTile.Solid;

        /// <summary>A door tile's lock: 0xb9/0xbb "key", the portcullis (0x99) "magic" (nobody opens it), else none.</summary>
        public static string Lock(byte t) => t == 0xb9 || t == 0xbb ? "key" : t == 0x99 ? "magic" : null;

        public static char Glyph(DTile c) => c switch
        {
            DTile.Solid => '#', DTile.Floor => '.', DTile.Earth => ',', DTile.Planks => '=', DTile.Water => '~',
            DTile.Lava => '^', DTile.Door => '+', DTile.Ladder => 'H', _ => '?',
        };
    }

    /// <summary>
    /// One level of a dungeon assembled into a grid of tiles (MASTER_PLAN §5.7): each of the 8×8
    /// cells is an 11×11 combat map, as the original draws it when you fight there: a room's map
    /// from DUNGEON.CBT, any other cell built by DNGLOOK build_dungeon_combat_walls (a ring of wall
    /// with a 7-wide gap toward open cells, a 5-wide doorway toward doors and rooms, the outer edge
    /// closed toward walls; its ladder, chest or fountain in the middle). The level wraps (8 → 0),
    /// so tiles outside 0..87 repeat the other side.
    /// </summary>
    public class DungeonLevelPlan
    {
        public const int CellTiles = 11, Cells = 8, Tiles = Cells * CellTiles, Centre = 5;
        /// <summary>Metres per tile, wall height, the stone between two levels.</summary>
        public const float Tile = 2f, Height = 6f, Slab = 2f;
        public const float Spacing = Height + Slab;

        public readonly int Dungeon, Level;
        public readonly DungeonStyle Style;
        readonly byte[] cells = new byte[64];
        readonly byte[][] cellTiles = new byte[64][];
        public readonly List<DFeature> Features = new List<DFeature>();

        public byte Cell(int cx, int cy) => cells[(cy & 7) * 8 + (cx & 7)];
        public byte CellByIndex(int i) => cells[i];
        public byte[] CellTileMap(int i) => cellTiles[i];

        public static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
        public static int Mod(int a, int b) => ((a % b) + b) % b;

        /// <summary>The cell a tile of the grid (any integer, wrapped) is in, and the tile's place in it.</summary>
        public static int CellOf(int u, int v, out int lx, out int ly)
        {
            int cx = FloorDiv(u, CellTiles), cy = FloorDiv(v, CellTiles);
            lx = u - cx * CellTiles;
            ly = v - cy * CellTiles;
            return Mod(cy, Cells) * Cells + Mod(cx, Cells);
        }

        public byte TileAt(int u, int v)
        {
            int c = CellOf(u, v, out int lx, out int ly);
            return cellTiles[c][ly * CellTiles + lx];
        }

        public DTile ClassAt(int u, int v) => DungeonTiles.Class(TileAt(u, v));

        /// <summary>Level-local metres (x east, z north) of the middle of tile (u, v); the level's middle is (0, 0).</summary>
        public static (float x, float z) Local(double u, double v) => ((float)((u + 0.5 - Tiles / 2.0) * Tile), (float)((Tiles / 2.0 - v - 0.5) * Tile));

        /// <summary>The tile (fractional) at level-local metres.</summary>
        public static (double u, double v) TileAtLocal(float x, float z) => (x / Tile + Tiles / 2.0 - 0.5, Tiles / 2.0 - z / Tile - 0.5);

        /// <summary>Tile of a cell's middle.</summary>
        public static (int u, int v) CellCentre(int cell) => ((cell % 8) * CellTiles + Centre, (cell / 8) * CellTiles + Centre);

        readonly HashSet<(int, int)> floorHoles = new HashSet<(int, int)>(), ceilingHoles = new HashSet<(int, int)>();

        /// <summary>A hole in the floor (a ladder's grate or a pit) at grid tile (u, v), wrapped.</summary>
        public bool FloorHole(int u, int v) => floorHoles.Contains((Mod(u, Tiles), Mod(v, Tiles)));
        /// <summary>A hole in the ceiling (a ladder going up, a pit fallen through, the ceiling-hole bit).</summary>
        public bool CeilingHole(int u, int v) => ceilingHoles.Contains((Mod(u, Tiles), Mod(v, Tiles)));

        public DungeonLevelPlan(int dungeon, int level, DungeonStyle style)
        {
            Dungeon = dungeon;
            Level = level;
            Style = style;
        }

        internal void Set(byte[] levelCells)
        {
            Buffer.BlockCopy(levelCells, 0, cells, 0, 64);
        }

        internal void SetTiles(int cell, byte[] tiles) => cellTiles[cell] = tiles;
        internal void HoleBelow(int u, int v) => floorHoles.Add((u, v));
        internal void HoleAbove(int u, int v) => ceilingHoles.Add((u, v));

        /// <summary>The level as text: one character per tile (# solid, . floor, , earth, = planks, ~ water, ^ lava, + door, H ladder; o a hole in the floor).</summary>
        public string Draw()
        {
            var sb = new StringBuilder();
            for (int v = 0; v < Tiles; v++)
            {
                for (int u = 0; u < Tiles; u++)
                    sb.Append(FloorHole(u, v) ? 'o' : DungeonTiles.Glyph(ClassAt(u, v)));
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }

    /// <summary>Builds <see cref="DungeonLevelPlan"/>s from DUNGEON.DAT cells (the dungeon's current state) and DUNGEON.CBT.</summary>
    public class DungeonPlanner
    {
        readonly U5Data data;
        readonly TownTiles tiles;
        readonly PieceGeometry geo;

        /// <summary><paramref name="tiles"/>/<paramref name="geo"/> (town_tiles.json, piece_geometry.json) give room furniture; without them none is placed.</summary>
        public DungeonPlanner(U5Data data, TownTiles tiles = null, PieceGeometry geo = null)
        {
            this.data = data;
            this.tiles = tiles;
            this.geo = geo;
        }

        /// <summary>The dungeon's cells as DUNGEON.DAT has them (8 levels × 64).</summary>
        public byte[] InitialCells(int dungeon)
        {
            var r = new byte[DungeonMaps.Levels * 64];
            for (int z = 0; z < DungeonMaps.Levels; z++)
            for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                r[z * 64 + y * 8 + x] = data.Dungeons.Cell(dungeon, z, x, y).Raw;
            return r;
        }

        public CombatMap Room(int dungeon, int room) => data.DungeonRooms[DungeonInfo.RoomIndex(dungeon, room)];

        static readonly byte[] CentreTiles = { 0x00, 0xc8, 0xc9, 0xc8, 0xdc, 0xd8, 0x00, 0x00 };

        /// <summary>
        /// Plans level <paramref name="level"/> from the dungeon's <paramref name="cells"/> (512
        /// bytes), with room tiles changed by triggers in <paramref name="changed"/> ((level, cell,
        /// x, y) → tile).
        /// </summary>
        public DungeonLevelPlan Level(int dungeon, int level, byte[] cells, IReadOnlyDictionary<(int, int, int, int), byte> changed = null)
        {
            var style = DungeonInfo.Style(dungeon);
            var plan = new DungeonLevelPlan(dungeon, level, style);
            var lv = new byte[64];
            Buffer.BlockCopy(cells, level * 64, lv, 0, 64);
            plan.Set(lv);
            byte wall = DungeonInfo.WallTile(style), floor = DungeonInfo.FloorTile(style);
            for (int i = 0; i < 64; i++)
            {
                int cx = i % 8, cy = i / 8;
                byte c = lv[i];
                int hi = c & 0xf0;
                byte[] t;
                if (hi == 0xf0 || hi == 0xa0)
                {
                    var map = Room(dungeon, c & 0xf);
                    t = new byte[121];
                    for (int y = 0; y < 11; y++)
                    for (int x = 0; x < 11; x++)
                        t[y * 11 + x] = changed != null && changed.TryGetValue((level, i, x, y), out byte nt) ? nt : map.Tiles[y, x];
                }
                else if (hi == 0xb0 || hi == 0xc0 || hi == 0xd0)
                {
                    t = new byte[121];
                    for (int k = 0; k < 121; k++) t[k] = 0xff;
                }
                else t = Hallway(c, d => (byte)(plan.Cell(cx + Dx[d], cy + Dy[d]) & 0xf0), wall, floor, out _);
                plan.SetTiles(i, t);
            }
            for (int i = 0; i < 64; i++) Features(plan, i, cells);
            return plan;
        }

        // directions as the original numbers them: 0 north, 1 east, 2 south, 3 west
        public static readonly int[] Dx = { 0, 1, 0, -1 }, Dy = { -1, 0, 1, 0 };
        public static readonly float[] Yaw = { 0, 90, 180, 270 };

        static bool IsWall(int hi) => hi == 0xb0 || hi == 0xc0 || hi == 0xd0;

        /// <summary>
        /// build_dungeon_combat_walls on a floor of <paramref name="floor"/>: rows and columns 1 and 9
        /// wall, the corners black (0xff), the cell's object in the middle, then each side by its
        /// neighbour (combat_map_side_from_neighbour). A door cell gets its doorway: pillars (and here
        /// a wall with a door in its middle) across the passage. <paramref name="doorLine"/> is the
        /// side the door's wall runs along (0 = across north-south, i.e. an east-west passage), or −1.
        /// </summary>
        public static byte[] Hallway(byte own, Func<int, byte> neighbourHi, byte wall, byte floor, out int doorLine)
        {
            var t = new byte[121];
            for (int k = 0; k < 121; k++) t[k] = floor;
            for (int i = 0; i < 11; i++) { t[1 * 11 + i] = wall; t[9 * 11 + i] = wall; t[i * 11 + 1] = wall; t[i * 11 + 9] = wall; }
            t[0] = t[10] = t[110] = t[120] = 0xff;
            int ownHi = own & 0xf0;
            if (ownHi != 0 && ownHi < 0x80 && CentreTiles[ownHi >> 4] != 0) t[5 * 11 + 5] = CentreTiles[ownHi >> 4];
            for (int dir = 0; dir < 4; dir++)
            {
                int nb = neighbourHi(dir);
                if (nb < 0xa0) Opening(t, dir, 2, 7, floor);
                if (IsWall(nb)) WallSide(t, dir, ownHi, wall);
                else Opening(t, dir, 3, 5, floor);
            }
            doorLine = -1;
            if (ownHi == 0xe0)
            {
                // the original's pillars: a north wall → an east-west passage (pillars at (5,2), (5,8)); a west wall → north-south.
                // Between them a wall with the door in its middle (5,5).
                if (IsWall(neighbourHi(0))) doorLine = 0;
                else if (IsWall(neighbourHi(3))) doorLine = 3;
                if (doorLine == 0) for (int y = 2; y <= 8; y++) t[y * 11 + 5] = y == 5 ? (byte)0xb8 : wall;
                else if (doorLine == 3) for (int x = 2; x <= 8; x++) t[5 * 11 + x] = x == 5 ? (byte)0xb8 : wall;
            }
            return t;
        }

        static void Opening(byte[] t, int dir, int from, int count, byte floor)
        {
            for (int k = 0; k < count; k++)
            {
                int a = from + k;
                switch (dir)
                {
                    case 0: t[1 * 11 + a] = floor; break;
                    case 1: t[a * 11 + 9] = floor; break;
                    case 2: t[9 * 11 + a] = floor; break;
                    default: t[a * 11 + 1] = floor; break;
                }
            }
        }

        static void WallSide(byte[] t, int dir, int ownHi, byte wall)
        {
            for (int a = 0; a < 11; a++)
            {
                switch (dir)
                {
                    case 0: t[a] = 0xff; break;
                    case 1: t[a * 11 + 10] = 0xff; break;
                    case 2: t[10 * 11 + a] = 0xff; break;
                    default: t[a * 11] = 0xff; break;
                }
            }
            if (dir == 0 && ownHi == 0xe0) { t[8 * 11 + 5] = wall; t[2 * 11 + 5] = wall; }
            if (dir == 3 && ownHi == 0xe0) { t[5 * 11 + 8] = wall; t[5 * 11 + 2] = wall; }
        }

        void Features(DungeonLevelPlan plan, int i, byte[] allCells)
        {
            int cx = i % 8, cy = i / 8;
            byte c = plan.CellByIndex(i);
            int hi = c & 0xf0;
            var (mu, mv) = DungeonLevelPlan.CellCentre(i);
            void Add(DFeatureKind k, int u, int v, byte value, float yaw = 0, string lockKind = null)
                => plan.Features.Add(new DFeature { Kind = k, U = u, V = v, Cell = i, Value = value, Yaw = yaw, Lock = lockKind });

            // the ceiling-hole bit of open cells: a hole in the middle of the ceiling (a pit fallen through from above)
            if (hi < 0x90 && (c & 8) != 0)
            {
                plan.HoleAbove(mu, mv);
                if (hi != 0x10 && hi != 0x30) Add(DFeatureKind.Grapple, mu, mv, c);
            }
            switch (hi)
            {
                case 0x10:
                    Add(DFeatureKind.LadderUp, mu, mv, c);
                    plan.HoleAbove(mu, mv);
                    break;
                case 0x20:
                    Add(DFeatureKind.LadderDown, mu, mv, c);
                    plan.HoleBelow(mu, mv);
                    break;
                case 0x30:
                    // both ways: the ladder up in the middle, the grate down beside it (south)
                    Add(DFeatureKind.LadderUp, mu, mv, c);
                    plan.HoleAbove(mu, mv);
                    Add(DFeatureKind.LadderDown, mu, mv + 1, c);
                    plan.HoleBelow(mu, mv + 1);
                    break;
                case 0x40: case 0x70: Add(DFeatureKind.Chest, mu, mv, c); break;
                case 0x50:
                    Add(DFeatureKind.Fountain, mu, mv, c);
                    Props(plan, NewCtx(), i, 0xd8, mu, mv);
                    break;
                case 0x60:
                    if (c == 0x60 || c == 0x68) { Add(DFeatureKind.Pit, mu, mv, c); plan.HoleBelow(mu, mv); }
                    else Add(DFeatureKind.Trap, mu, mv, c);
                    break;
                case 0x80: Add(DFeatureKind.Field, mu, mv, c); break;
                case 0xd0:
                    // the secret door shows as wall to each open neighbour
                    for (int d = 0; d < 4; d++)
                    {
                        int nb = plan.Cell(cx + DungeonPlanner.Dx[d], cy + DungeonPlanner.Dy[d]) & 0xf0;
                        if (IsWall(nb)) continue;
                        // the face of the neighbour's wall ring (its row/column 1 or 9) facing into the neighbour
                        Add(DFeatureKind.Secret, mu + Dx[d] * 7, mv + Dy[d] * 7, c, Yaw[d]);
                    }
                    break;
                case 0xe0:
                {
                    Hallway(c, d => (byte)(plan.Cell(cx + Dx[d], cy + Dy[d]) & 0xf0), 0, 0, out int line);
                    if (line >= 0) Add(DFeatureKind.Door, mu, mv, 0xb8, line == 0 ? 90 : 0);
                    break;
                }
                case 0xa0: case 0xf0:
                    RoomFeatures(plan, i);
                    break;
            }
        }

        void RoomFeatures(DungeonLevelPlan plan, int i)
        {
            var t = plan.CellTileMap(i);
            int ox = (i % 8) * DungeonLevelPlan.CellTiles, oy = (i / 8) * DungeonLevelPlan.CellTiles;
            var ctx = NewCtx();
            for (int y = 0; y < 11; y++)
            for (int x = 0; x < 11; x++)
            {
                byte tile = t[y * 11 + x];
                int u = ox + x, v = oy + y;
                var cls = DungeonTiles.Class(tile);
                if (tile == 0xc8) { plan.Features.Add(new DFeature { Kind = DFeatureKind.LadderUp, U = u, V = v, Cell = i, Value = tile }); plan.HoleAbove(u, v); continue; }
                // a room's floor grate (0x86) is a way down too (combat Klimb in a room: exit 6)
                if (tile == 0xc9 || tile == 0x86) { plan.Features.Add(new DFeature { Kind = DFeatureKind.LadderDown, U = u, V = v, Cell = i, Value = tile }); plan.HoleBelow(u, v); continue; }
                if (cls == DTile.Door)
                {
                    // the leaf runs across the way through: between the open sides
                    bool ew = Open(t, x - 1, y) && Open(t, x + 1, y);
                    plan.Features.Add(new DFeature { Kind = DFeatureKind.Door, U = u, V = v, Cell = i, Value = tile, Yaw = ew ? 90 : 0, Lock = DungeonTiles.Lock(tile) });
                    continue;
                }
                if (cls != DTile.Solid) Props(plan, ctx, i, tile, u, v);
            }
        }

        SettlementPlanner.Ctx NewCtx() => tiles != null && geo != null ? new SettlementPlanner.Ctx(new SettlementPlan(), geo) : null;

        /// <summary>The furniture of a room tile, as the settlements build it (town_tiles.json): props, tables, chairs, beds, wall torches.</summary>
        void Props(DungeonLevelPlan plan, SettlementPlanner.Ctx ctx, int i, byte tile, int u, int v)
        {
            if (ctx == null) return;
            var rule = tiles[tile];
            int before = ctx.Plan.Pieces.Count;
            var (lx, lz) = DungeonLevelPlan.Local(u, v);
            switch (rule.Kind)
            {
                case TileKind.Prop:
                    if (rule.WallSide.HasValue)
                    {
                        // a wall torch hangs on the wall beside its tile, facing out
                        double r = rule.WallSide.Value * Math.PI / 180;
                        float px = lx + (float)Math.Sin(r) * 0.85f, pz = lz + (float)Math.Cos(r) * 0.85f;
                        if (rule.Prefab != null && ctx.Known(rule.Prefab)) ctx.Centre(rule.Prefab, px, 1.8f, pz, rule.WallSide.Value + 180);
                    }
                    else if (rule.Parts != null) ctx.Parts(rule.Parts, lx, 0, lz, rule.Yaw);
                    else if (rule.Prefab != null && ctx.Known(rule.Prefab)) ctx.Stand(rule.Prefab, lx, 0, lz, rule.Yaw);
                    break;
                case TileKind.Table: ctx.Stand("piece_table", lx, 0, lz, 0); break;
                case TileKind.Chair: ctx.Stand("piece_chair", lx, 0, lz, rule.Yaw); break;
                case TileKind.BedHead: ctx.Stand("bed", lx, 0, lz, rule.Yaw); break;
            }
            for (int p = before; p < ctx.Plan.Pieces.Count; p++)
                plan.Features.Add(new DFeature { Kind = DFeatureKind.Prop, U = u, V = v, Cell = i, Value = tile, Piece = ctx.Plan.Pieces[p] });
        }

        static bool Open(byte[] t, int x, int y) => x >= 0 && y >= 0 && x < 11 && y < 11 && DungeonTiles.Open(DungeonTiles.Class(t[y * 11 + x]));

        /// <summary>A dungeon level and its rooms as text (U5Dump "dungeon &lt;name&gt; &lt;level&gt; plan").</summary>
        public string Describe(int dungeon, int level, byte[] cells = null)
        {
            cells = cells ?? InitialCells(dungeon);
            var plan = Level(dungeon, level, cells);
            var sb = new StringBuilder($"{data.Location(DungeonInfo.Location(dungeon)).Name} level {level} ({plan.Style}, room set {DungeonInfo.RoomSet(dungeon)})\n");
            sb.Append(plan.Draw());
            var counts = new Dictionary<DFeatureKind, int>();
            foreach (var f in plan.Features) counts[f.Kind] = counts.TryGetValue(f.Kind, out int n) ? n + 1 : 1;
            foreach (var kv in counts) sb.Append($"{kv.Key} {kv.Value}  ");
            sb.Append('\n');
            for (int i = 0; i < 64; i++)
            {
                byte c = plan.CellByIndex(i);
                if ((c & 0xf0) != 0xf0 && (c & 0xf0) != 0xa0) continue;
                int room = c & 0xf;
                var monsters = new List<RoomMonster>();
                var objects = new List<RoomObject>();
                DungeonInfo.RoomContents(data.Ovl, Room(dungeon, room), level, new Random(1), monsters, objects);
                sb.Append($"room {room} at ({i % 8},{i / 8}){(DungeonInfo.Persistent(data.Ovl, dungeon, room) ? " (fills again)" : "")}: ");
                foreach (var m in monsters) sb.Append($"{data.Ovl.CreatureName(m.Type)}@{m.X},{m.Y} ");
                foreach (var o in objects) sb.Append($"[{o.Loot}]@{o.X},{o.Y} ");
                int tr = DungeonInfo.Triggers(Room(dungeon, room)).Count;
                if (tr > 0) sb.Append($"{tr} triggers");
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
