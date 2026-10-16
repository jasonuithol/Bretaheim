using System;
using System.Collections.Generic;

namespace U5InValheim.Data
{
    /// <summary>High nibble of a DUNGEON.DAT cell (low nibble = variant, see <see cref="DungeonCell.Variant"/>).</summary>
    public enum DungeonCellKind
    {
        Hallway = 0x0, LadderUp = 0x1, LadderDown = 0x2, LadderUpDown = 0x3, Chest = 0x4, Fountain = 0x5,
        Trap = 0x6, OpenChest = 0x7, EnergyField = 0x8, Unused9 = 0x9, UnusedA = 0xa, Wall = 0xb,
        AltWall = 0xc, SecretDoor = 0xd, Door = 0xe, Room = 0xf,
    }

    public struct DungeonCell
    {
        public byte Raw;
        public DungeonCellKind Kind => (DungeonCellKind)(Raw >> 4);
        public int Variant => Raw & 0xf;
        /// <summary>For rooms (0xF_): the room number within this dungeon's room set.</summary>
        public int Room => Raw & 0xf;
        public bool CeilingHole => Kind == DungeonCellKind.Hallway && (Raw & 8) != 0;
        public override string ToString() => $"{Kind}:{Variant}";
    }

    /// <summary>DUNGEON.DAT: 8 dungeons × 8 levels × 8×8 cells, one byte each.</summary>
    public class DungeonMaps
    {
        public const int Dungeons = 8, Levels = 8, Size = 8;
        readonly byte[] data;

        public DungeonMaps(byte[] data)
        {
            if (data.Length != Dungeons * Levels * Size * Size)
                throw new U5FormatException($"DUNGEON.DAT has {data.Length} bytes, expected 4096");
            this.data = data;
        }

        /// <summary>Cell (x, y) of level 0..7 of dungeon 0..7 (Deceit, Despise, Destard, Wrong, Covetous, Shame, Hythloth, Doom).</summary>
        public DungeonCell Cell(int dungeon, int level, int x, int y)
            => new DungeonCell { Raw = data[((dungeon * Levels + level) * Size + (y & 7)) * Size + (x & 7)] };
    }

    /// <summary>
    /// One 11×11 combat map of BRIT.CBT / DUNGEON.CBT (352 bytes = 11 rows of 11 tiles + 21 bytes of
    /// per-row metadata). Metadata rows (wiki "*.CBT"): 0 new tile ids for triggers, 1..4 party
    /// start x[6]/y[6] when entering from east/west/south/north, 5 monster tiles[16], 6 monster
    /// x[16], 7 monster y[16], 8 trigger x[8]/y[8], 9 and 10 the two replacement-tile positions
    /// x[8]/y[8] per trigger.
    /// </summary>
    public class CombatMap
    {
        public const int Size = 11;
        public const int RowBytes = 32;
        public const int Bytes = Size * RowBytes;

        public readonly byte[,] Tiles = new byte[Size, Size]; // [y, x]
        readonly byte[] raw;

        public CombatMap(byte[] file, int index)
        {
            raw = new byte[Bytes];
            Buffer.BlockCopy(file, index * Bytes, raw, 0, Bytes);
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                Tiles[y, x] = raw[y * RowBytes + x];
        }

        byte Meta(int row, int i) => raw[row * RowBytes + Size + i];
        byte[] MetaRange(int row, int start, int count)
        {
            var r = new byte[count];
            for (int i = 0; i < count; i++) r[i] = Meta(row, start + i);
            return r;
        }

        public byte[] NewTileIds => MetaRange(0, 0, 16);
        /// <summary>dir: 0 east, 1 west, 2 south/below, 3 north/above.</summary>
        public byte[] PartyX(int dir) => MetaRange(1 + dir, 0, 6);
        public byte[] PartyY(int dir) => MetaRange(1 + dir, 6, 6);
        public byte[] MonsterTiles => MetaRange(5, 0, 16);
        public byte[] MonsterX => MetaRange(6, 0, 16);
        public byte[] MonsterY => MetaRange(7, 0, 16);
        public byte[] TriggerX => MetaRange(8, 0, 8);
        public byte[] TriggerY => MetaRange(8, 8, 8);
        public byte[] NewTile1X => MetaRange(9, 0, 8);
        public byte[] NewTile1Y => MetaRange(9, 8, 8);
        public byte[] NewTile2X => MetaRange(10, 0, 8);
        public byte[] NewTile2Y => MetaRange(10, 8, 8);

        public static List<CombatMap> ReadAll(byte[] file)
        {
            if (file.Length % Bytes != 0) throw new U5FormatException($".CBT size {file.Length} is not a multiple of {Bytes}");
            var r = new List<CombatMap>();
            for (int i = 0; i < file.Length / Bytes; i++) r.Add(new CombatMap(file, i));
            return r;
        }
    }

    public class Sign
    {
        /// <summary>Location number (0 = overworld/dungeon group), floor, x, y.</summary>
        public int Location, Z, X, Y;
        /// <summary>Text with the high bit stripped; lower-case letters are the sign-frame glyphs.</summary>
        public string Text;
    }

    /// <summary>SIGNS.DAT: 0x21 u16 offsets, then per group a run of (location, z, x, y, text, NUL).</summary>
    public static class SignsFile
    {
        public const int Groups = 0x21;

        public static List<Sign>[] Read(byte[] data)
        {
            var offsets = new int[Groups + 1];
            for (int i = 0; i < Groups; i++) offsets[i] = data[2 * i] | data[2 * i + 1] << 8;
            offsets[Groups] = data.Length;
            var r = new List<Sign>[Groups];
            for (int g = 0; g < Groups; g++)
            {
                r[g] = new List<Sign>();
                int end = NextOffset(offsets, g);
                for (int p = offsets[g]; p + 4 < end;)
                {
                    var s = new Sign { Location = data[p], Z = (sbyte)data[p + 1], X = data[p + 2], Y = data[p + 3] };
                    p += 4;
                    var sb = new System.Text.StringBuilder();
                    while (p < end && data[p] != 0) sb.Append((char)(data[p++] & 0x7f));
                    p++;
                    s.Text = sb.ToString();
                    r[g].Add(s);
                }
            }
            return r;
        }

        // Offsets are not guaranteed to be ascending: a group ends at the nearest later offset.
        static int NextOffset(int[] offsets, int g)
        {
            int best = offsets[Groups];
            for (int i = 0; i < Groups; i++)
                if (offsets[i] > offsets[g] && offsets[i] < best) best = offsets[i];
            return best;
        }
    }
}
