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
