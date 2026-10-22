using System.Linq;

namespace Bretaheim.Data
{
    /// <summary>
    /// The colour a creature is drawn in: its four frames in TILES.16 (tile 0x140 + 4 × type,
    /// Creatures.TypeOf), counted per EGA colour. Black is the background; the dark and light EGA
    /// colours of one hue (blue 1/9, green 2/10, cyan 3/11, red 4/12, magenta 5/13, brown and
    /// yellow 6/14, the greys and white 7/8/15) count together, and the most drawn hue wins, as
    /// the mean of its pixels. The shark swims in drawn water (dark blue), which is not the shark.
    /// </summary>
    public static class CreatureColours
    {
        public const int FirstTile = 0x140, Frames = 4, Shark = 19;

        static readonly int[][] Hues = { new[] { 1, 9 }, new[] { 2, 10 }, new[] { 3, 11 }, new[] { 4, 12 }, new[] { 5, 13 }, new[] { 6, 14 }, new[] { 7, 8, 15 } };

        /// <summary>How many pixels of each EGA colour (0..15) the type's four frames have.</summary>
        public static int[] Histogram(TileSet tiles, int type)
        {
            var count = new int[16];
            for (int f = 0; f < Frames; f++)
                foreach (var p in tiles.Pixels(FirstTile + Frames * type + f)) count[p]++;
            return count;
        }

        /// <summary>The creature's colour (0xRRGGBB), or null when its frames are all background.</summary>
        public static int? Dominant(TileSet tiles, int type)
        {
            var count = Histogram(tiles, type);
            count[0] = 0;
            if (type == Shark) count[1] = count[9] = 0;
            var hue = Hues.OrderByDescending(h => h.Sum(c => count[c])).First();
            int n = hue.Sum(c => count[c]);
            if (n == 0) return null;
            double r = 0, g = 0, b = 0;
            foreach (int c in hue)
            {
                int rgb = TileSet.EgaPalette[c];
                r += (rgb >> 16 & 255) * count[c]; g += (rgb >> 8 & 255) * count[c]; b += (rgb & 255) * count[c];
            }
            return (int)System.Math.Round(r / n) << 16 | (int)System.Math.Round(g / n) << 8 | (int)System.Math.Round(b / n);
        }
    }
}
