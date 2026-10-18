using System;
using System.IO;
using System.Linq;
using U5InValheim.Data;

// Usage: dotnet run --project tools/U5Dump -- [--u5 <dir>] <what> [args]   (see Dump.Usage)
// The Ultima V directory defaults to $U5_DIR, then ~/Projects/Ultima 5.
var argv = args.ToList();
string dir = Environment.GetEnvironmentVariable("U5_DIR")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Projects", "Ultima 5");
int i = argv.IndexOf("--u5");
if (i >= 0 && i + 1 < argv.Count) { dir = argv[i + 1]; argv.RemoveRange(i, 2); }

var missing = U5Data.Validate(dir);
if (missing.Count > 0)
{
    Console.Error.WriteLine($"Ultima V directory '{dir}' is missing: {string.Join(", ", missing)}");
    return 1;
}
var data = U5Data.Load(dir);
if (argv.Count > 0 && argv[0] == "preview")
{
    // preview <out.ppm> [pixels=1024] [extraTiles=24] [under]
    string outPath = argv.Count > 1 ? argv[1] : "preview.ppm";
    int px = argv.Count > 2 ? int.Parse(argv[2]) : 1024;
    int extra = argv.Count > 3 ? int.Parse(argv[3]) : 24;
    bool under = argv.Contains("under");
    string terrainJson = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "terrain.json"));
    var shape = new U5InValheim.WorldGen.Core.BritanniaShape(under ? data.Underworld : data.Britannia,
        U5InValheim.WorldGen.Core.TerrainTable.Parse(terrainJson), seed: 1234);
    Preview.Render(shape, under ? new() : U5InValheim.WorldGen.Core.Pois.Build(data), outPath, px, extra);
    Console.WriteLine($"wrote {outPath}");
    return 0;
}
if (argv.Count > 0 && argv[0] == "banners")
{
    // banners <out.ppm> [tile ...]: the shop banner cloths (default: the guild icons 0xf0-0xf9 but 0xf8), side by side, doubled
    string outPath = argv.Count > 1 ? argv[1] : "banners.ppm";
    var tiles = argv.Count > 2 ? argv.Skip(2).Select(t => Convert.ToInt32(t, 16)).ToList()
        : Enumerable.Range(0xf0, 10).Where(t => t != 0xf8).ToList();
    const int s = 2, gap = 4;
    int bw = U5InValheim.WorldGen.Core.BannerArt.Width, bh = U5InValheim.WorldGen.Core.BannerArt.Height;
    int w = tiles.Count * (bw + gap) * s, h = bh * s;
    var img = new byte[w * h * 3];
    for (int k = 0; k < img.Length; k++) img[k] = 96;
    for (int n = 0; n < tiles.Count; n++)
    {
        var rgba = U5InValheim.WorldGen.Core.BannerArt.Render(data.Tiles, tiles[n]);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < bw * s; x++)
        {
            int src = ((y / s) * bw + x / s) * 4;
            int dst = (y * w + n * (bw + gap) * s + x) * 3;
            img[dst] = rgba[src]; img[dst + 1] = rgba[src + 1]; img[dst + 2] = rgba[src + 2];
        }
    }
    using (var f = File.Create(outPath))
    {
        var header = System.Text.Encoding.ASCII.GetBytes($"P6\n{w} {h}\n255\n");
        f.Write(header, 0, header.Length);
        f.Write(img, 0, img.Length);
    }
    Console.WriteLine($"wrote {outPath}: {string.Join(" ", tiles.Select(t => $"{t:x2} {data.TileNames[t]}"))}");
    return 0;
}
if (argv.Count > 0 && argv[0] == "dressing")
{
    // dressing: how many of each prefab dressing.json scatters over the whole overworld
    var table = U5InValheim.WorldGen.Core.TerrainTable.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "terrain.json")));
    var shape = new U5InValheim.WorldGen.Core.BritanniaShape(data.Britannia, table, seed: 1234);
    var d = U5InValheim.WorldGen.Core.Dressing.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "dressing.json")));
    var counts = new System.Collections.Generic.Dictionary<string, int>();
    int cells = (int)(shape.WrapMetres / U5InValheim.WorldGen.Core.Dressing.Cell) / 2;
    for (int cz = -cells; cz < cells; cz++)
    for (int cx = -cells; cx < cells; cx++)
    {
        var name = d.Pick(shape, cx, cz, out _, out _, out _, out _);
        if (name != null) counts[name] = counts.TryGetValue(name, out int n) ? n + 1 : 1;
    }
    foreach (var (name, n) in counts.OrderByDescending(kv => kv.Value)) Console.WriteLine($"{name,-20} {n,7}");
    Console.WriteLine($"total {counts.Values.Sum()} over {shape.WrapMetres / 1000:0.0} km square");
    return 0;
}
if (argv.Count > 0 && argv[0] == "slopes")
{
    // slopes [step=4]: slope angle percentiles per terrain class over the whole overworld
    float step = argv.Count > 1 ? float.Parse(argv[1]) : 4f;
    var table = U5InValheim.WorldGen.Core.TerrainTable.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "terrain.json")));
    var shape = new U5InValheim.WorldGen.Core.BritanniaShape(data.Britannia, table, seed: 1234);
    var byClass = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<float>>();
    float half = shape.WrapMetres / 2;
    for (float z = -half; z < half; z += step)
    for (float x = -half; x < half; x += step)
    {
        float h = shape.NaturalHeight(x, z), hx = shape.NaturalHeight(x + step, z), hz = shape.NaturalHeight(x, z + step);
        float g = MathF.Sqrt((hx - h) * (hx - h) + (hz - h) * (hz - h)) / step;
        string name = table[shape.TileAt(x, z)].Name;
        if (!byClass.TryGetValue(name, out var l)) byClass[name] = l = new();
        l.Add(MathF.Atan(g) * 180 / MathF.PI);
    }
    Console.WriteLine("class               n      p50   p90   p99   max   >35°   >45°");
    foreach (var (name, l) in byClass.OrderByDescending(kv => kv.Value.Count))
    {
        l.Sort();
        float P(double q) => l[(int)Math.Min(l.Count - 1, q * l.Count)];
        Console.WriteLine($"{name,-16} {l.Count,8} {P(.5),5:0.0} {P(.9),5:0.0} {P(.99),5:0.0} {l[^1],5:0.0} {l.Count(a => a > 35) * 100.0 / l.Count,5:0.0}% {l.Count(a => a > 45) * 100.0 / l.Count,5:0.0}%");
    }
    return 0;
}
if (argv.Count > 0 && argv[0] == "town")
{
    // town <location> <out.ppm>
    string maps = AppContext.BaseDirectory;
    var tiles = U5InValheim.WorldGen.Core.TownTiles.Parse(File.ReadAllText(Path.Combine(maps, "town_tiles.json")));
    var geo = U5InValheim.WorldGen.Core.PieceGeometry.Parse(File.ReadAllText(Path.Combine(maps, "piece_geometry.json")));
    var loc = data.FindLocation(argv[1]);
    var pois = U5InValheim.WorldGen.Core.Pois.Build(data);
    string kind = pois.First(p => p.Location == loc.Number).Kind;
    var plan = new U5InValheim.WorldGen.Core.SettlementPlanner(data, tiles, geo).Plan(loc, kind, U5InValheim.WorldGen.Core.VBiome.Meadows);
    string outPath = argv.Count > 2 ? argv[2] : "town.ppm";
    if (outPath == "probe")
    {
        Console.WriteLine(new U5InValheim.WorldGen.Core.SettlementPlanner(data, tiles, geo).Probe(loc, int.Parse(argv[3]), int.Parse(argv[4]), int.Parse(argv[5])));
        return 0;
    }
    if (outPath == "floors")
    {
        Console.Write(new U5InValheim.WorldGen.Core.SettlementPlanner(data, tiles, geo).DescribeFloors(loc));
        return 0;
    }
    if (outPath.EndsWith(".json"))
        File.WriteAllText(outPath, "[" + string.Join(",\n", plan.Pieces.Select(p =>
            System.FormattableString.Invariant($"{{\"p\":\"{p.Prefab}\",\"x\":{p.X},\"y\":{p.Y},\"z\":{p.Z},\"yaw\":{p.Yaw},\"s\":[{(p.Scaled ? p.SX : 1)},{(p.Scaled ? p.SY : 1)},{(p.Scaled ? p.SZ : 1)}]}}"))) + "]");
    else TownPreview.Render(plan, geo, outPath);
    Console.WriteLine($"{loc.Name} ({kind}): {plan.Pieces.Count} pieces, spawn {(plan.HasSpawn ? $"({plan.SpawnX}, {plan.SpawnZ})" : "none")}");
    foreach (var g in plan.Pieces.GroupBy(p => p.Prefab).OrderByDescending(g => g.Count())) Console.Write($"{g.Key}x{g.Count()} ");
    Console.WriteLine();
    var noGeometry = plan.Pieces.Select(p => p.Prefab).Distinct().Where(p => !geo.Has(p)).ToList();
    if (noGeometry.Count > 0) Console.WriteLine("no geometry: " + string.Join(", ", noGeometry));
    return 0;
}
Console.WriteLine(Dump.Run(data, argv.ToArray()));
return 0;
