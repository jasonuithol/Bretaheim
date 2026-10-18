using System;

namespace U5InValheim.Data
{
    /// <summary>
    /// TILES.16: the 512 16×16 EGA tiles the game draws its maps with (Ultima5Revolutions
    /// platform/video.rs draw_tile): LZW, then 128 bytes per tile, 8 bytes per row, two pixels per
    /// byte (high nibble first), each a colour of the standard EGA palette.
    /// </summary>
    public class TileSet
    {
        public const int Count = 512, Size = 16, BytesPerTile = Size * Size / 2;

        /// <summary>The standard EGA 16-colour palette (0xRRGGBB).</summary>
        public static readonly int[] EgaPalette =
        {
            0x000000, 0x0000AA, 0x00AA00, 0x00AAAA, 0xAA0000, 0xAA00AA, 0xAA5500, 0xAAAAAA,
            0x555555, 0x5555FF, 0x55FF55, 0x55FFFF, 0xFF5555, 0xFF55FF, 0xFFFF55, 0xFFFFFF,
        };

        readonly byte[] data;

        public TileSet(byte[] raw)
        {
            data = Lzw.DecompressFile(raw);
            if (data.Length < Count * BytesPerTile)
                throw new U5FormatException($"TILES.16 is {data.Length} bytes, expected {Count * BytesPerTile}");
        }

        /// <summary>The EGA colour index (0..15) of pixel (x, y) of a tile; y counts down from the top.</summary>
        public int Pixel(int tile, int x, int y)
        {
            if ((uint)tile >= Count || (uint)x >= Size || (uint)y >= Size) throw new ArgumentOutOfRangeException();
            byte b = data[tile * BytesPerTile + y * (Size / 2) + x / 2];
            return x % 2 == 0 ? b >> 4 : b & 15;
        }

        /// <summary>A tile's 256 colour indices, row by row from the top.</summary>
        public byte[] Pixels(int tile)
        {
            var px = new byte[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                px[y * Size + x] = (byte)Pixel(tile, x, y);
            return px;
        }
    }
}
