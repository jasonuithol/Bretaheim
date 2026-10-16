using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using U5InValheim.WorldGen.Core;

/// <summary>Top-down drawing of a SettlementPlan: one panel per storey plus a roof panel.</summary>
static class TownPreview
{
    const int PxPerM = 8;

    public static void Render(SettlementPlan plan, PieceGeometry geo, string path)
    {
        int size = (int)(plan.HalfSize * 2 * PxPerM);
        int storeys = Math.Max(1, (int)Math.Ceiling(plan.Pieces.Where(p => !IsRoof(p.Prefab)).Select(p => p.Y).DefaultIfEmpty(0).Max() / plan.Storey + 0.01));
        int panels = storeys + 1;
        int W = panels * (size + 8), H = size;
        var img = new byte[W * H * 3];
        for (int panel = 0; panel < panels; panel++)
        {
            int ox = panel * (size + 8);
            // ground
            for (int cy = 0; cy < SettlementPlan.Size; cy++)
            for (int cx = 0; cx < SettlementPlan.Size; cx++)
            {
                int c = cy * SettlementPlan.Size + cx;
                var col = plan.Water[c] ? (40, 90, 170) : plan.GroundPaint[c] switch
                {
                    Paint.Paved => (150, 150, 150), Paint.Dirt => (140, 110, 70), Paint.Cultivated => (90, 70, 40), _ => (70, 120, 50)
                };
                if (panel > 0) col = (col.Item1 / 3, col.Item2 / 3, col.Item3 / 3);
                int s = (int)(plan.Tile * PxPerM);
                for (int y = 0; y < s; y++) for (int x = 0; x < s; x++) Put(img, W, H, ox + cx * s + x, cy * s + y, col);
            }
            foreach (var p in plan.Pieces)
            {
                bool roof = IsRoof(p.Prefab);
                bool mine = panel == storeys ? roof : !roof && (int)Math.Floor((p.Y + 0.6f) / plan.Storey) == panel;
                if (!mine || !geo.TryGet(p.Prefab, out var b)) continue;
                var col = Colour(p.Prefab);
                // rasterise the rotated footprint
                double r = p.Yaw * Math.PI / 180, cs = Math.Cos(r), sn = Math.Sin(r);
                for (float lx = b.MinX; lx <= b.MaxX; lx += 0.1f)
                for (float lz = b.MinZ; lz <= b.MaxZ; lz += 0.1f)
                {
                    double wx = p.X + lx * cs + lz * sn, wz = p.Z - lx * sn + lz * cs;
                    int px = ox + (int)((wx + plan.HalfSize) * PxPerM), py = (int)((plan.HalfSize - wz) * PxPerM);
                    Put(img, W, H, px, py, col);
                }
            }
        }
        using var f = File.Create(path);
        var header = System.Text.Encoding.ASCII.GetBytes($"P6\n{W} {H}\n255\n");
        f.Write(header, 0, header.Length);
        f.Write(img, 0, img.Length);
    }

    static bool IsRoof(string prefab) => prefab.Contains("_roof");

    static (int, int, int) Colour(string prefab)
    {
        if (prefab.Contains("door") || prefab.Contains("gate")) return (230, 200, 40);
        if (prefab.Contains("roof")) return (170, 60, 50);
        if (prefab.StartsWith("stone") || prefab.StartsWith("blackmarble")) return (200, 200, 210);
        if (prefab.Contains("wall") || prefab.Contains("pole")) return (120, 80, 40);
        if (prefab.Contains("floor")) return (180, 140, 90);
        if (prefab.StartsWith("Pickable")) return (230, 220, 90);
        if (prefab.Contains("Tree") || prefab.Contains("tree") || prefab.StartsWith("Beech") || prefab.StartsWith("Oak") || prefab.StartsWith("Birch")) return (20, 70, 20);
        if (prefab == "sign") return (255, 255, 255);
        if (prefab.Contains("fence")) return (160, 120, 60);
        return (230, 60, 200);
    }

    static void Put(byte[] img, int W, int H, int x, int y, (int, int, int) c)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return;
        int i = 3 * (y * W + x);
        img[i] = (byte)c.Item1; img[i + 1] = (byte)c.Item2; img[i + 2] = (byte)c.Item3;
    }
}
