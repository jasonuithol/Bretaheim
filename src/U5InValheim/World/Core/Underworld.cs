using System;
using System.Collections.Generic;
using U5InValheim.Data;

namespace U5InValheim.WorldGen.Core
{
    /// <summary>
    /// Britannia and the Underworld side by side (MASTER_PLAN §5.1, "a full second continent"):
    /// both are 256×256 tori built by <see cref="BritanniaShape"/>, Britannia centred on the world's
    /// centre (where it has always been, so older worlds keep their land) and the Underworld
    /// <see cref="UnderworldOffsetTiles"/> tiles east of it. Their margins and fades just meet at
    /// the midline (each reaches 128 + 16 + 6 = 150 tiles from its centre), so neither shows the
    /// other. Tile coordinates are the original's on both: a link between them keeps (x, y).
    /// </summary>
    public static class Continents
    {
        /// <summary>From Britannia's centre to the Underworld's, in tiles (9.6 km at 32 m).</summary>
        public const int UnderworldOffsetTiles = 300;

        public static float UnderworldOriginX(float tileMetres) => UnderworldOffsetTiles * tileMetres;

        /// <summary>A world position belongs to the Underworld east of the midline between the two.</summary>
        public static bool InUnderworld(float wx, float tileMetres) => wx > UnderworldOriginX(tileMetres) / 2;

        /// <summary>Metres from the world's centre to the farthest ground of a continent (its margin and fade included).</summary>
        public static float Reach(BritanniaShape shape)
        {
            double half = (BritanniaShape.MapTiles / 2.0 + shape.MarginTiles + shape.FadeTiles) * shape.TileMetres;
            double x = Math.Abs(shape.OriginX) + half, z = Math.Abs(shape.OriginZ) + half;
            return (float)Math.Sqrt(x * x + z * z);
        }

        /// <summary>
        /// The Underworld's shape: UNDER.DAT with terrain.json's "underworld" changes, its own noise
        /// (seed varied), east of Britannia, its lakes (among rock that would hold them shallow)
        /// at least 3.5 m deep away from their shores, so a ship floats where a whirlpool or the
        /// falls bring it.
        /// </summary>
        public static BritanniaShape UnderworldShape(WorldMap under, TerrainTable underTerrain, int seed, float tileMetres = 32f)
            => new BritanniaShape(under, underTerrain, seed ^ 0x5eed, tileMetres, originX: UnderworldOriginX(tileMetres)) { MinWaterDepth = 3.5f };

        /// <summary>Valheim's own world edge (WorldGenerator.waterEdge): the ocean ends there.</summary>
        public const float ValheimEdge = 10500f;
    }

    /// <summary>
    /// The original's ways between the surface and the Underworld, and what happens down there
    /// (MAINOUT monster_contact, OUTSUBS go_over_falls, DUNGEON exit_dungeon / TOWN enter_dungeon,
    /// MAINOUT underworld_earthquake_check, OUTSUBS place_shards_and_amulet).
    /// </summary>
    public static class UnderworldLinks
    {
        /// <summary>Where a whirlpool drops a vessel: the Underworld's (0x22, 0x12), whatever the sea it was met on.</summary>
        public const int WhirlpoolX = 0x22, WhirlpoolY = 0x12;
        /// <summary>The falls that lead down: a party carried over a waterfall onto (0x36, 0x8a) of the surface lands there in the Underworld.</summary>
        public const int FallsX = 0x36, FallsY = 0x8a;
        /// <summary>The Amulet of Lord British lies at (0x69, 0xe1) of the Underworld until found.</summary>
        public const int AmuletX = 0x69, AmuletY = 0xe1;
        // DATA.OVL: the three shards' Underworld positions and their Shadowlords (0xf0..0xf2)
        const int ShardXs = 0x3a06, ShardYs = 0x3a0a;
        /// <summary>DATA.OVL strings: "F-A-L-L-S!!!", "Falling into underworld!!", "WHIRLPOOL!", "EARTHQUAKE!"</summary>
        public const int SFalls = 0x39b5, SFallingInto = 0x39c3, SWhirlpool = 0x6b04, SEarthquake = 0x2b1d;
        /// <summary>Doom, the Underworld's own dungeon (entered at level 0 from the Underworld).</summary>
        public const int Doom = 7;

        /// <summary>A waterfall tile (0xd4..0xd7).</summary>
        public static bool IsWaterfall(byte tile) => (tile & 0xfc) == 0xd4;

        /// <summary>
        /// go_over_falls: the party is carried two tiles south of where it was (the tile north of the
        /// waterfall); landing on the surface's (0x36, 0x8a) it is in the Underworld.
        /// </summary>
        public static (int x, int y, bool under) OverFalls(int x, int y, bool under)
        {
            int ly = (y + 2) & 0xff;
            return (x & 0xff, ly, under || ((x & 0xff) == FallsX && ly == FallsY));
        }

        /// <summary>underworld_earthquake_check: each Underworld turn, 1 in 256 (a roll of 0..255 equal to 0x69).</summary>
        public static bool Earthquake(Random rnd) => rnd.Next(0, 256) == 0x69;

        /// <summary>
        /// enter_dungeon: from the surface (and always into Doom) level 0, cell (1, 1), facing east;
        /// from the Underworld level 7, cell (7, 7), facing west. Facing 0..3 is north, east, south, west.
        /// </summary>
        public static (int level, int cellX, int cellY, int facing) DungeonArrival(int dungeon, bool fromUnderworld)
            => !fromUnderworld || dungeon == Doom ? (0, 1, 1, 1) : (DungeonInfo.Levels - 1, 7, 7, 3);

        /// <summary>
        /// exit_dungeon: up from level 0 to the surface (Doom's level 0 to its Underworld entrance:
        /// the original would put the party on the surface sea over Doom), down from level 7 to the
        /// Underworld, both at the dungeon's own (x, y).
        /// </summary>
        public static bool ExitsToUnderworld(int dungeon, int level, int dir)
            => dir > 0 ? level == DungeonInfo.Levels - 1 : level == 0 && dungeon == Doom;

        /// <summary>
        /// place_shards_and_amulet: what lies in the Underworld until taken: the Amulet of Lord
        /// British (unless found), and each Shadowlord's shard while it is not had and its
        /// Shadowlord is not destroyed.
        /// </summary>
        public static List<(int x, int y, Loot loot)> Objects(DataOvl ovl, bool amuletFound, Func<int, bool> shardWanted)
        {
            var r = new List<(int, int, Loot)>();
            if (!amuletFound) r.Add((AmuletX, AmuletY, new Loot(LootKind.AmuletOfLordBritish)));
            for (int i = 0; i < 3; i++)
                if (shardWanted(i)) r.Add((ovl.U8(ShardXs + i), ovl.U8(ShardYs + i), new Loot(LootKind.Shard, i)));
            return r;
        }
    }
}
