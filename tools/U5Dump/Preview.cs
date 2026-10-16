using System;
using System.IO;
using U5InValheim.Data;
using U5InValheim.WorldGen.Core;

/// <summary>Renders a BritanniaShape to a PPM image: biome colours, hill shading, POI dots.</summary>
static class Preview
{
    public static void Render(BritanniaShape shape, System.Collections.Generic.List<Poi> pois, string path, int px, int extraTiles)
    {
        float t = shape.TileMetres;
        float half = (BritanniaShape.MapTiles / 2 + extraTiles) * t;
        float step = 2 * half / px;
        var h = new float[px * px];
        var b = new VBiome[px * px];
        for (int y = 0; y < px; y++)
        for (int x = 0; x < px; x++)
        {
            float wx = -half + (x + 0.5f) * step, wz = half - (y + 0.5f) * step;
            h[y * px + x] = shape.Height(wx, wz);
            b[y * px + x] = shape.Biome(wx, wz);
        }
        var img = new byte[px * px * 3];
        for (int y = 0; y < px; y++)
        for (int x = 0; x < px; x++)
        {
            int i = y * px + x;
            var (r, g, bl) = Colour(b[i], h[i]);
            float dx = h[y * px + Math.Min(px - 1, x + 1)] - h[i];
            float dy = h[Math.Min(px - 1, y + 1) * px + x] - h[i];
            float shade = h[i] < 0 ? 1f : Math.Clamp(1f + (-dx - dy) / step * 0.9f, 0.45f, 1.35f);
            img[3 * i] = (byte)Math.Clamp(r * shade, 0, 255);
            img[3 * i + 1] = (byte)Math.Clamp(g * shade, 0, 255);
            img[3 * i + 2] = (byte)Math.Clamp(bl * shade, 0, 255);
        }
        foreach (var p in pois)
        {
            if (p.Underworld) continue;
            shape.TileCentre(p.TileX, p.TileY, out float wx, out float wz);
            int cx = (int)((wx + half) / step), cy = (int)((half - wz) / step);
            for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if (x < 0 || y < 0 || x >= px || y >= px) continue;
                int i = 3 * (y * px + x);
                img[i] = 255; img[i + 1] = p.Kind == "moongate" ? (byte)80 : (byte)0; img[i + 2] = p.Kind == "shrine" ? (byte)255 : (byte)0;
            }
        }
        // the 256x256 square's border, so the wrap margin is visible
        for (int k = 0; k < px; k++)
            foreach (var edge in new[] { -128f * t, 128f * t })
            {
                int e = (int)((edge + half) / step);
                if (e >= 0 && e < px)
                {
                    Mark(img, px, e, k);
                    Mark(img, px, k, e);
                }
            }
        using var f = File.Create(path);
        var header = System.Text.Encoding.ASCII.GetBytes($"P6\n{px} {px}\n255\n");
        f.Write(header, 0, header.Length);
        f.Write(img, 0, img.Length);
    }

    static void Mark(byte[] img, int px, int x, int y)
    {
        if ((x + y) % 6 > 2) return;
        int i = 3 * (y * px + x);
        img[i] = 255; img[i + 1] = 255; img[i + 2] = 255;
    }

    static (float, float, float) Colour(VBiome b, float h)
    {
        if (b == VBiome.Ocean || h < 0)
        {
            float d = Math.Clamp(-h / 70f, 0, 1);
            return (40 - 30 * d, 110 - 80 * d, 190 - 100 * d);
        }
        switch (b)
        {
            case VBiome.Meadows: return (120, 175, 70);
            case VBiome.BlackForest: return (35, 90, 45);
            case VBiome.Swamp: return (85, 85, 50);
            case VBiome.Plains: return (190, 180, 90);
            case VBiome.Mountain: return (225, 225, 235);
            case VBiome.Mistlands: return (120, 100, 140);
            case VBiome.AshLands: return (150, 60, 40);
            default: return (255, 0, 255);
        }
    }
}
