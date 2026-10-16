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
Console.WriteLine(Dump.Run(data, argv.ToArray()));
return 0;
