using U5InValheim.Data;

namespace U5InValheim.WorldGen.Core
{
    /// <summary>
    /// The cloth of a shop banner (Constructing world assets 240-249: "a banner mounted to a tall
    /// post with the likeness of the icon printed on it"): the guild icon straight out of TILES.16,
    /// doubled, on a field of the icon's own background colour, with a border in its main colour
    /// (the cloth itself ends in a point). Unity-free, so U5Dump can draw exactly what the game shows.
    /// </summary>
    public static class BannerArt
    {
        /// <summary>Texture size in pixels: the banner cloth is 1.18 m × 2.98 m.</summary>
        public const int Width = 40, Height = 100;
        const int Scale = 2, Border = 2, IconTop = 22;

        /// <summary>RGBA bytes, row by row from the top.</summary>
        public static byte[] Render(TileSet tiles, int tile)
        {
            var icon = tiles.Pixels(tile);
            int field = icon[0];
            // the border takes the icon's commonest colour other than its background
            var counts = new int[16];
            foreach (var c in icon) counts[c]++;
            int trim = -1;
            for (int c = 0; c < 16; c++)
                if (c != field && (trim < 0 || counts[c] > counts[trim])) trim = c;
            if (trim < 0) trim = field == 15 ? 0 : 15;

            var rgba = new byte[Width * Height * 4];
            int iconLeft = (Width - TileSet.Size * Scale) / 2;
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int colour = field;
                bool edge = x < Border || x >= Width - Border || y < Border || y >= Height - Border;
                int ix = (x - iconLeft) / Scale, iy = (y - IconTop) / Scale;
                if (x >= iconLeft && y >= IconTop && ix < TileSet.Size && iy < TileSet.Size) colour = icon[iy * TileSet.Size + ix];
                else if (edge) colour = trim;
                int rgb = TileSet.EgaPalette[colour], o = (y * Width + x) * 4;
                rgba[o] = (byte)(rgb >> 16);
                rgba[o + 1] = (byte)(rgb >> 8);
                rgba[o + 2] = (byte)rgb;
                rgba[o + 3] = 255;
            }
            return rgba;
        }
    }
}
