using System;

namespace U5InValheim.Data
{
    /// <summary>
    /// A 256×256 overworld (BRIT.DAT) or underworld (UNDER.DAT): 16×16 chunks of 16×16 tiles, west
    /// to east then north to south. BRIT.DAT omits all-water chunks; DATA.OVL's chunk map
    /// (0x3876) gives the stored chunk index per grid cell, 0xff = all tile 1 (deep water).
    /// Both maps wrap in x and y.
    /// </summary>
    public class WorldMap
    {
        public const int Size = 256;
        public const int ChunkSize = 16;
        public const byte TileDeepWater = 1;

        readonly byte[] tiles = new byte[Size * Size];

        WorldMap() { }

        public byte this[int x, int y] => tiles[(Wrap(y) * Size) + Wrap(x)];

        public byte[] Tiles => (byte[])tiles.Clone();

        static int Wrap(int v) => v & (Size - 1);

        public static WorldMap FromBrit(byte[] britDat, byte[] chunkMap)
        {
            if (chunkMap.Length != 256) throw new ArgumentException("chunk map must be 256 bytes");
            int stored = britDat.Length / (ChunkSize * ChunkSize);
            var m = new WorldMap();
            for (int gy = 0; gy < 16; gy++)
            for (int gx = 0; gx < 16; gx++)
            {
                int cid = chunkMap[gy * 16 + gx];
                for (int ty = 0; ty < ChunkSize; ty++)
                for (int tx = 0; tx < ChunkSize; tx++)
                {
                    byte t = cid == 0xff || cid >= stored
                        ? TileDeepWater
                        : britDat[cid * 256 + ty * ChunkSize + tx];
                    m.tiles[(gy * ChunkSize + ty) * Size + gx * ChunkSize + tx] = t;
                }
            }
            return m;
        }

        public static WorldMap FromUnder(byte[] underDat)
        {
            if (underDat.Length != Size * Size) throw new U5FormatException($"UNDER.DAT has {underDat.Length} bytes, expected 65536");
            var m = new WorldMap();
            for (int gy = 0; gy < 16; gy++)
            for (int gx = 0; gx < 16; gx++)
            {
                int cid = gy * 16 + gx;
                for (int ty = 0; ty < ChunkSize; ty++)
                for (int tx = 0; tx < ChunkSize; tx++)
                    m.tiles[(gy * ChunkSize + ty) * Size + gx * ChunkSize + tx] = underDat[cid * 256 + ty * ChunkSize + tx];
            }
            return m;
        }
    }

    /// <summary>LOOK2.DAT: the "look" description of each of the 0x200 tiles.</summary>
    public class TileNames
    {
        public const int Count = 0x200;
        readonly string[] names = new string[Count];

        public TileNames(byte[] look2)
        {
            for (int t = 0; t < Count; t++)
            {
                int o = look2[2 * t] | look2[2 * t + 1] << 8;
                var sb = new System.Text.StringBuilder();
                while (o < look2.Length && look2[o] != 0) sb.Append((char)(look2[o++] & 0x7f));
                names[t] = sb.ToString();
            }
        }

        public string this[int tile] => names[tile];
    }
}
