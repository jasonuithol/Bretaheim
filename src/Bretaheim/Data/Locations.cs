using System;
using System.Collections.Generic;

namespace Bretaheim.Data
{
    /// <summary>Which settlement file a location's maps, NPCs and dialogue live in.</summary>
    public enum SettlementFile { Towne = 0, Dwelling = 1, Castle = 2, Keep = 3, Dungeon = 4 }

    /// <summary>
    /// One of the 40 places of DATA.OVL's location tables (location number 1..40): 1..8 TOWNE,
    /// 9..16 DWELLING, 17..24 CASTLE, 25..32 KEEP, 33..40 the dungeons.
    /// </summary>
    public class Location
    {
        public int Number { get; internal set; }
        public string Name { get; internal set; }
        public SettlementFile File { get; internal set; }
        /// <summary>Index 0..7 inside its file (= its slot in the .NPC / map start tables).</summary>
        public int Slot { get; internal set; }
        public int X { get; internal set; }
        public int Y { get; internal set; }
        /// <summary>Map index in the .DAT file for floor z = 0.</summary>
        public int GroundMap { get; internal set; }
        /// <summary>Lowest floor (−1 for a basement, else 0).</summary>
        public int MinZ { get; internal set; }
        /// <summary>Highest floor.</summary>
        public int MaxZ { get; internal set; }

        /// <summary>True for the places reached in the Underworld (Ararat); X/Y are then UNDER.DAT coordinates.</summary>
        public bool InUnderworld { get; internal set; }

        public bool IsSettlement => File != SettlementFile.Dungeon;
        public int FloorCount => IsSettlement ? MaxZ - MinZ + 1 : 0;
        public int MapIndex(int z) => GroundMap + z;

        public override string ToString() => $"{Number,2} {Name} ({File}#{Slot}) @({X},{Y})";
    }

    /// <summary>The 32×32 settlement maps of TOWNE/DWELLING/CASTLE/KEEP.DAT (16 maps × 1 KB each).</summary>
    public class SettlementMaps
    {
        public const int Size = 32;
        public const int MapsPerFile = 16;

        // Ladder tiles that lead to another floor (TOWN.OVL use_stairs / cmd_klimb_town):
        // 0x86 and 0xc9 go down, 0xc8 goes up.
        public const byte TileLadderUp = 0xc8;
        public const byte TileLadderDown = 0xc9;
        public const byte TileHatchDown = 0x86;

        readonly byte[][] files = new byte[4][];

        public SettlementMaps(Func<string, byte[]> read)
        {
            files[0] = read("TOWNE.DAT");
            files[1] = read("DWELLING.DAT");
            files[2] = read("CASTLE.DAT");
            files[3] = read("KEEP.DAT");
            for (int i = 0; i < 4; i++)
                if (files[i].Length != MapsPerFile * Size * Size)
                    throw new U5FormatException($"{(SettlementFile)i}.DAT has {files[i].Length} bytes, expected 16384");
        }

        /// <summary>Raw 32×32 map (row-major, y then x) of map <paramref name="index"/> in <paramref name="file"/>.</summary>
        public byte[] Map(SettlementFile file, int index)
        {
            if (index < 0 || index >= MapsPerFile) throw new ArgumentOutOfRangeException(nameof(index));
            var r = new byte[Size * Size];
            Buffer.BlockCopy(files[(int)file], index * Size * Size, r, 0, r.Length);
            return r;
        }

        public byte[] Floor(Location loc, int z) => Map(loc.File, loc.MapIndex(z));

        public static bool HasUp(byte[] map) => Array.IndexOf(map, TileLadderUp) >= 0;
        public static bool HasDown(byte[] map)
            => Array.IndexOf(map, TileLadderDown) >= 0 || Array.IndexOf(map, TileHatchDown) >= 0;
    }

    public static class LocationTable
    {
        public const int Count = 40;

        // g_location_name_ptrs has NULL for five locations; their names come from elsewhere in the
        // game (dwelling signs, castle conversations), so we name them here.
        static readonly Dictionary<int, string> MissingNames = new Dictionary<int, string>
        {
            { 14, "Spektran" }, { 15, "Sin'Vraal's Hut" }, { 16, "Grendel's Hut" },
            { 17, "Lord British's Castle" }, { 18, "Blackthorn's Palace" },
        };

        /// <summary>
        /// Builds the 40 locations. A settlement's floors are found by following its ladders from
        /// the ground map (start table 0x1e1a..): while a floor has a way down the map before it is
        /// the floor below; while it has a way up the map after it is the floor above. This yields
        /// e.g. Lord British's castle = maps 0..4 (z −1..3) and Serpent's Hold = maps 13..15.
        /// </summary>
        /// <summary>Overworld tiles that mark a place: hut .. lighthouse (0x10..0x1b) and castle walls (0x39..0x3f).</summary>
        public static bool IsPlaceTile(byte t) => (t >= 0x10 && t <= 0x1b) || (t >= 0x39 && t <= 0x3f);

        public static Location[] Build(DataOvl ovl, SettlementMaps maps, WorldMap britannia, WorldMap underworld)
        {
            var r = new Location[Count];
            for (int i = 0; i < Count; i++)
            {
                int number = i + 1;
                var file = (SettlementFile)(i / 8);
                string name = ovl.PtrStr(Dg.LocationNamePtrs, i);
                if (name == null) MissingNames.TryGetValue(number, out name);
                var loc = new Location
                {
                    Number = number,
                    Name = name == null ? $"Location {number}" : TitleCase(name),
                    File = file,
                    Slot = i % 8,
                    X = ovl.U8(Dg.LocationX + i),
                    Y = ovl.U8(Dg.LocationY + i),
                };
                // The tables hold only x/y; a place whose surface tile is plain terrain while the
                // Underworld has a place tile there lives below (Ararat).
                loc.InUnderworld = !IsPlaceTile(britannia[loc.X, loc.Y]) && IsPlaceTile(underworld[loc.X, loc.Y]);
                if (file != SettlementFile.Dungeon)
                {
                    loc.GroundMap = ovl.U8(Dg.MapFileStartTowne + i);
                    int lo = loc.GroundMap, hi = loc.GroundMap;
                    while (lo > 0 && SettlementMaps.HasDown(maps.Map(file, lo))) lo--;
                    while (hi < SettlementMaps.MapsPerFile - 1 && SettlementMaps.HasUp(maps.Map(file, hi))) hi++;
                    loc.MinZ = lo - loc.GroundMap;
                    loc.MaxZ = hi - loc.GroundMap;
                }
                r[i] = loc;
            }
            return r;
        }

        static string TitleCase(string s)
        {
            if (s.Length == 0 || HasLower(s)) return s;
            var chars = s.ToLowerInvariant().ToCharArray();
            bool start = true;
            for (int i = 0; i < chars.Length; i++)
            {
                if (start && char.IsLetter(chars[i])) chars[i] = char.ToUpperInvariant(chars[i]);
                start = chars[i] == ' ' || chars[i] == '-';
            }
            return new string(chars);
        }

        static bool HasLower(string s)
        {
            foreach (char c in s) if (char.IsLower(c)) return true;
            return false;
        }
    }
}
