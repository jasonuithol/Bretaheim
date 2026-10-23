using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Bretaheim.Data;

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
if (argv.Count > 0 && argv[0] == "intro")
{
    // intro <outdir>: the 21 story screens and the gypsy's screens as PPMs, their words printed
    string outDir = argv.Count > 1 ? argv[1] : "intro";
    Directory.CreateDirectory(outDir);
    var screens = Story.Load(dir, data.Ovl);
    for (int s = 0; s < screens.Count; s++)
    {
        screens[s].Picture.WritePpm(Path.Combine(outDir, $"story{s:00}.ppm"));
        screens[s].After?.WritePpm(Path.Combine(outDir, $"story{s:00}b.ppm"));
        Console.WriteLine($"[{s}] {screens[s].Text}\n");
    }
    var art = new VirtueCasting.Art(dir, data.Ovl);
    art.Opening(out string open).WritePpm(Path.Combine(outDir, "gypsy0.ppm"));
    Console.WriteLine($"[gypsy] {open}\n");
    var casting = new VirtueCasting(data.Ovl, data.InitialGame.Character(0), new Random(argv.Count > 2 ? int.Parse(argv[2]) : 1));
    for (int q = 1; !casting.Done; q++)
    {
        casting.Next();
        art.Question(casting, out string question).WritePpm(Path.Combine(outDir, $"gypsy{q}.ppm"));
        int card = casting.Answer('A');
        Console.WriteLine($"[{q}] {VirtueCasting.Names[casting.Asked > 0 ? card : 0]}: {question}\n");
    }
    art.Closing(out string end).WritePpm(Path.Combine(outDir, "gypsy8.ppm"));
    Console.WriteLine($"[end] {end}\nSTR {Math.Max(20, casting.Strength)} DEX {casting.Dexterity} INT {casting.Intelligence} MP {casting.Intelligence}");
    return 0;
}
if (argv.Count > 0 && argv[0] == "view")
{
    // view <outdir>: "Return to the View" played once, a PPM after every event but waits, the events printed
    string outDir = argv.Count > 1 ? argv[1] : "view";
    Directory.CreateDirectory(outDir);
    byte[] Read(string f) => File.ReadAllBytes(U5Data.FindFile(dir, f));
    var ibm = new FixedFont(Read("IBM.CH"));
    var view = new TheView(Read("MISCMAPS.DAT"), data.Ovl);
    if (argv.Count > 2 && argv[2] == "plan")
    {
        // view <outdir> plan: what each scene is built of on the stage (IntroStage)
        string maps = AppContext.BaseDirectory;
        var tiles = Bretaheim.WorldGen.Core.TownTiles.Parse(File.ReadAllText(Path.Combine(maps, "town_tiles.json")));
        var geo = Bretaheim.WorldGen.Core.PieceGeometry.Parse(File.ReadAllText(Path.Combine(maps, "piece_geometry.json")));
        var planner = new Bretaheim.WorldGen.Core.SettlementPlanner(data, tiles, geo);
        for (int sc = 0; sc < 4; sc++)
        {
            var raw = view.Stage(sc);
            for (int y = TheView.StageY - 8; y < TheView.StageY + 8; y++)
                Console.WriteLine(string.Join("", Enumerable.Range(0, 32).Select(x => raw[y * 32 + x] == TheView.Nothing ? " ." : $"{raw[y * 32 + x]:x2}"[0..2])));
            var plan = planner.PlanMap(raw, "dwelling", Bretaheim.WorldGen.Core.VBiome.Meadows, false);
            Console.WriteLine($"scene {sc} \"{view.Caption[sc]}\": {plan.Pieces.Count} pieces: " + string.Join(", ", plan.Pieces.GroupBy(p => p.Prefab).OrderByDescending(g => g.Count()).Select(g => $"{g.Key}×{g.Count()}")));
        }
        return 0;
    }
    int shot = 0, frames = 0;
    foreach (var ev in view.Run())
    {
        frames += ev.Frames;
        string what = ev.Kind == ViewEventKind.Scene ? $"\"{view.Caption[ev.Scene]}\"" : ev.Kind == ViewEventKind.Wait ? $"{ev.Frames}" : $"slot {ev.Slot} tile 0x{ev.Tile + (ev.Slot >= 0 ? 0x100 : 0):x} at {ev.X},{ev.Y}" + (ev.Kind == ViewEventKind.Move ? $" from {ev.FromX},{ev.FromY}" : "");
        if (ev.Kind != ViewEventKind.Wait) view.Render(data.Tiles, ibm).WritePpm(Path.Combine(outDir, $"{shot++:000}.ppm"));
        Console.WriteLine($"{frames * TheView.Tick,6:0.0}s {(ev.Kind == ViewEventKind.Wait ? "" : $"[{shot - 1:000}] ")}{ev.Kind} {what}");
    }
    return 0;
}
if (argv.Count > 0 && argv[0] == "scene")
{
    // scene blackthorn|endgame|resurrection <outdir> [answers…]: the cut-scene played with these answers (typed
    // lines for Blackthorn, y/n for Lord British; "party=N", "box", "ruined=0,1", "karma=N"), a PPM at every
    // stop (key, question, picture), the text printed
    string which = argv.Count > 1 ? argv[1] : "blackthorn", outDir = argv.Count > 2 ? argv[2] : "scene";
    Directory.CreateDirectory(outDir);
    var rest = argv.Skip(3).ToList();
    int party = rest.Where(a => a.StartsWith("party=")).Select(a => int.Parse(a.Substring(6))).DefaultIfEmpty(1).First();
    bool box = rest.Contains("box");
    var ruined = new bool[8];
    foreach (var a in rest.Where(a => a.StartsWith("ruined=")))
        foreach (var n in a.Substring(7).Split(',')) ruined[int.Parse(n)] = true;
    var answers = new Queue<string>(rest.Where(a => !a.Contains('=') && a != "box"));
    byte[] Read(string f) => File.ReadAllBytes(U5Data.FindFile(dir, f));
    var ibm = new FixedFont(Read("IBM.CH"));
    var names = new[] { "Avatar", "Shamino", "Iolo", "Dupre", "Jaana", "Geoffrey" }.Take(party).ToList();
    CutScene view;
    IEnumerable<Beat> beats;
    Action<string> answer;
    if (which == "blackthorn")
    {
        var s = new BlackthornScene(data.Ovl, Read("MISCMSG.DAT"), Read("MISCMAPS.DAT"), "Avatar", false, party, names.Count > 1 ? names[1] : "", ruined);
        s.OnBetray = v => Console.WriteLine($"** shrine {v} betrayed");
        s.OnExecute = sliced => Console.WriteLine($"** companion executed ({(sliced ? "sliced" : "merciful")})");
        view = s.View; beats = s.Run(); answer = a => s.Answer = a;
    }
    else if (which == "resurrection")
    {
        int karma = rest.Where(a => a.StartsWith("karma=")).Select(a => int.Parse(a.Substring(6))).DefaultIfEmpty(50).First();
        var s = new ResurrectionScene(data.Ovl, Read("KARMA.DAT"), karma);
        view = s.View; beats = s.Run(); answer = a => { };
    }
    else
    {
        var s = new EndgameScene(data.Ovl, Read("MISCMAPS.DAT"), Read("ENDMSG.DAT"), names, box, new Random(1));
        s.OnWon = () => Console.WriteLine("** won");
        s.Finale = () => EndStory.Finale(dir, data.Ovl, "Avatar", 140, 2, 11);
        view = s.View; beats = s.Run(); answer = a => s.Yes = a.StartsWith("y");
    }
    int shot = 0, endless = 0;
    foreach (var b in beats)
    {
        if (b.Endless)
        {
            // the certificate (the end), or the figures wandering for ever
            if (b.Kind == BeatKind.Picture) { b.Picture.WritePpm(Path.Combine(outDir, $"{shot++:00}.ppm")); Console.WriteLine(b.Words); break; }
            if (++endless > 30) break;
            continue;
        }
        if (b.Kind == BeatKind.Wait) continue;
        var pic = b.Kind == BeatKind.Picture ? b.Picture : view.Render(data.Tiles, ibm);
        pic.WritePpm(Path.Combine(outDir, $"{shot++:00}.ppm"));
        if (b.Words != null) Console.WriteLine($"[{shot - 1}] {b.Words}");
        if (b.Kind == BeatKind.Ask || b.Kind == BeatKind.YesNo) answer(answers.Count > 0 ? answers.Dequeue() : "");
    }
    view.Render(data.Tiles, ibm).WritePpm(Path.Combine(outDir, $"{shot++:00}.ppm"));
    Console.WriteLine(view.Text);
    return 0;
}
if (argv.Count > 0 && argv[0] == "preview")
{
    // preview <out.ppm> [pixels=1024] [extraTiles=24] [under]
    string outPath = argv.Count > 1 ? argv[1] : "preview.ppm";
    int px = argv.Count > 2 ? int.Parse(argv[2]) : 1024;
    int extra = argv.Count > 3 ? int.Parse(argv[3]) : 24;
    bool under = argv.Contains("under");
    string terrainJson = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "terrain.json"));
    // (the Underworld as built in game: its own terrain changes, east of Britannia, deep lakes; its places marked)
    var shape = under
        ? Bretaheim.WorldGen.Core.Continents.UnderworldShape(data.Underworld, Bretaheim.WorldGen.Core.TerrainTable.Parse(terrainJson, underworld: true), 1234)
        : new Bretaheim.WorldGen.Core.BritanniaShape(data.Britannia, Bretaheim.WorldGen.Core.TerrainTable.Parse(terrainJson), seed: 1234);
    Preview.Render(shape, Bretaheim.WorldGen.Core.Pois.Build(data), outPath, px, extra);
    Console.WriteLine($"wrote {outPath}");
    return 0;
}
if (argv.Count > 0 && argv[0] == "creatures")
{
    // creatures <out.ppm>: each creature type 16..47's four frames in TILES.16, a row each, at 4x
    string outPath = argv.Count > 1 ? argv[1] : "creatures.ppm";
    const int s = 4, t = Bretaheim.Data.TileSet.Size;
    int w = 4 * t * s, h = 32 * t * s;
    var img = new byte[w * h * 3];
    for (int type = 16; type < 48; type++)
    for (int f = 0; f < 4; f++)
    {
        var px = data.Tiles.Pixels(Bretaheim.Data.CreatureColours.FirstTile + 4 * type + f);
        for (int y = 0; y < t * s; y++)
        for (int x = 0; x < t * s; x++)
        {
            int rgb = Bretaheim.Data.TileSet.EgaPalette[px[(y / s) * t + x / s]];
            int dst = (((type - 16) * t * s + y) * w + f * t * s + x) * 3;
            img[dst] = (byte)(rgb >> 16); img[dst + 1] = (byte)(rgb >> 8); img[dst + 2] = (byte)rgb;
        }
    }
    using (var o = File.Create(outPath))
    {
        var head = System.Text.Encoding.ASCII.GetBytes($"P6\n{w} {h}\n255\n");
        o.Write(head, 0, head.Length);
        o.Write(img, 0, img.Length);
    }
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
    int bw = Bretaheim.WorldGen.Core.BannerArt.Width, bh = Bretaheim.WorldGen.Core.BannerArt.Height;
    int w = tiles.Count * (bw + gap) * s, h = bh * s;
    var img = new byte[w * h * 3];
    for (int k = 0; k < img.Length; k++) img[k] = 96;
    for (int n = 0; n < tiles.Count; n++)
    {
        var rgba = Bretaheim.WorldGen.Core.BannerArt.Render(data.Tiles, tiles[n]);
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
if (argv.Count > 0 && argv[0] == "bench")
{
    // bench [n=200000]: how fast the terrain builder's questions are answered (biome, height, paint
    // per sample), at random over the world and over a zone-mesh grid near Britain (no settlement sites)
    int n = argv.Count > 1 ? int.Parse(argv[1]) : 200000;
    var table = Bretaheim.WorldGen.Core.TerrainTable.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "terrain.json")));
    var sw0 = System.Diagnostics.Stopwatch.StartNew();
    var shape = new Bretaheim.WorldGen.Core.BritanniaShape(data.Britannia, table, seed: 1234);
    Console.WriteLine($"shape built in {sw0.ElapsedMilliseconds} ms; world {shape.WrapMetres:0} m");
    var rnd = new Random(1);
    var pts = Enumerable.Range(0, n).Select(_ => ((float)(rnd.NextDouble() - 0.5) * shape.WrapMetres, (float)(rnd.NextDouble() - 0.5) * shape.WrapMetres)).ToArray();
    var britain = data.Location(2); shape.TileCentre(britain.X, britain.Y, out float bx, out float bz);
    var grid = new System.Collections.Generic.List<(float, float)>();
    for (int zi = 0; grid.Count < n; zi++) { int zx = zi % 20, zz = zi / 20 % 20; for (int gz = 0; gz <= 64 && grid.Count < n; gz += 2) for (int gx = 0; gx <= 64 && grid.Count < n; gx += 2) grid.Add((bx + zx * 64 + gx - 640, bz + zz * 64 + gz - 640)); }
    foreach (var (label, set) in new (string, (float, float)[])[] { ("random", pts), ("near Britain", grid.ToArray()) })
    {
        double sum = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var (x, z) in set) sum += (int)shape.Biome(x, z);
        double b = sw.Elapsed.TotalMilliseconds * 1000 / set.Length; sw.Restart();
        foreach (var (x, z) in set) sum += shape.Height(x, z);
        double h = sw.Elapsed.TotalMilliseconds * 1000 / set.Length; sw.Restart();
        foreach (var (x, z) in set) sum += (int)shape.PaintAt(x, z);
        double pa = sw.Elapsed.TotalMilliseconds * 1000 / set.Length;
        Console.WriteLine($"{label,-13} biome {b:0.00} µs, height {h:0.00} µs, paint {pa:0.00} µs per sample ({set.Length} samples; {sum:0})");
    }
    return 0;
}
if (argv.Count > 0 && argv[0] == "sites")
{
    // sites <loc,loc,...>: where those settlements are built (moved apart as in game) and the land there
    var table = Bretaheim.WorldGen.Core.TerrainTable.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "terrain.json")));
    var shape = new Bretaheim.WorldGen.Core.BritanniaShape(data.Britannia, table, seed: 1234);
    var locs = (argv.Count > 1 ? argv[1] : "2,13,17").Split(',').Select(int.Parse).OrderBy(n => n).ToList();
    var squares = locs.Select(n => { var l = data.Location(n); shape.TileCentre(l.X, l.Y, out float x, out float z); return (x, z, half: 32f); }).ToList();
    Bretaheim.WorldGen.Core.SiteLayout.Separate(squares, 16, out var centres);
    for (int k = 0; k < locs.Count; k++)
    {
        var (x, z) = centres[k];
        float lo = float.MaxValue, hi = float.MinValue;
        for (float dz = -32; dz <= 32; dz += 8) for (float dx = -32; dx <= 32; dx += 8)
        { float h = shape.NaturalHeight(x + dx, z + dz); lo = Math.Min(lo, h); hi = Math.Max(hi, h); }
        Console.WriteLine($"{locs[k],3} {data.Location(locs[k]).Name,-22} tile ({squares[k].x:0}, {squares[k].z:0}) built at ({x:0}, {z:0}) moved ({x - squares[k].x:0}, {z - squares[k].z:0}) land {lo:0.0}..{hi:0.0} m");
    }
    return 0;
}
if (argv.Count > 0 && argv[0] == "dressing")
{
    // dressing: how many of each prefab dressing.json scatters over the whole overworld
    var table = Bretaheim.WorldGen.Core.TerrainTable.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "terrain.json")));
    var shape = new Bretaheim.WorldGen.Core.BritanniaShape(data.Britannia, table, seed: 1234);
    var d = Bretaheim.WorldGen.Core.Dressing.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "dressing.json")));
    var counts = new System.Collections.Generic.Dictionary<string, int>();
    int cells = (int)(shape.WrapMetres / Bretaheim.WorldGen.Core.Dressing.Cell) / 2;
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
    var table = Bretaheim.WorldGen.Core.TerrainTable.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "terrain.json")));
    var shape = new Bretaheim.WorldGen.Core.BritanniaShape(data.Britannia, table, seed: 1234);
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
if (argv.Count > 0 && argv[0] == "shops")
{
    // shops <location>: its shopkeepers as the game shows them (hours, greeting, stock, farewell)
    var loc = data.FindLocation(argv[1]);
    var rnd = new Random(1);
    foreach (var npc in data.NpcsOf(loc).Where(n => n.IsShopkeeper))
    {
        var s = data.Shops.Of(loc, npc);
        var open = Enumerable.Range(0, 24).Where(h => Bretaheim.Data.Shops.IsOpen(npc, h)).ToList();
        Console.WriteLine($"== {s.Trade} slot {s.Slot}: {s.Name}, kept by {s.Keeper}; open {(open.Count > 0 ? $"{open.First()}:00-{open.Last() + 1}:00" : "never")}");
        Console.WriteLine("closed: " + data.Shops.Closed(s));
        Console.WriteLine(data.Shops.Greeting(s, 10, rnd));
        foreach (var w in data.Shops.Stock(s)) Console.WriteLine($"  {w.Name} x{w.Quantity}: {w.Price} gp");
        if (s.Trade == Bretaheim.Data.ShopTrade.Armoury)
        {
            Console.WriteLine(data.Shops.SellPrompt(rnd));
            foreach (int id in new[] { 0x1a, 0x1b, 0x0f })
            {
                int offer = data.Shops.SellOffer(id, Bretaheim.Data.Shops.DefaultIntelligence);
                Console.WriteLine(offer > 0 && !Bretaheim.Data.Shops.IsAmmo(id) ? data.Shops.Offer(s, id, offer, 10, rnd) + " " + data.Shops.Sold(s, 10) : data.Shops.WillNotBuy(s, id, 10));
            }
        }
        if (s.Trade == Bretaheim.Data.ShopTrade.Healer) Console.WriteLine($"  heal {data.Shops.HealerPrices(s).heal} gp, cure {data.Shops.HealerPrices(s).cure} gp");
        if (s.Trade == Bretaheim.Data.ShopTrade.Inn)
        {
            int price = data.Shops.InnPrice(s, Bretaheim.Data.Shops.DefaultIntelligence);
            Console.WriteLine($"  rest {price} gp, {data.Shops.InnRoomCount(s)} rooms, bed {data.Shops.InnBed(s)}");
            Console.WriteLine(data.Shops.InnRoom(s, 21, price));
            Console.WriteLine(data.Shops.InnGoodNight(s, 21, false));
            Console.WriteLine(data.Shops.InnThrownOut(s, 21));
            Console.WriteLine(data.Shops.Zzz + " / " + data.Shops.MorningCry);
        }
        Console.WriteLine(data.Shops.Farewell(s, true, 10, rnd));
    }
    return 0;
}
if (argv.Count > 0 && argv[0] == "converse")
{
    // converse <location> <name> [line ...]: a conversation as the in-game panel shows it
    var loc = data.FindLocation(argv[1]);
    var npc = data.NpcsOf(loc).First(n => string.Equals(data.NpcName(loc, n), argv[2], StringComparison.OrdinalIgnoreCase));
    var t = new Bretaheim.Data.TalkSession(data.ScriptOf(loc, npc), data.Ovl, new DumpTalkWorld());
    Console.Write(t.TakeOutput());
    foreach (var line in argv.Skip(3))
    {
        if (t.Over) break;
        t.Respond(line);
        Console.Write(t.TakeOutput());
    }
    Console.WriteLine(t.Over ? "\n[over]" : $"\n[waiting: {t.Prompt.Replace("\n", " ")}]");
    return 0;
}
if (argv.Count > 0 && argv[0] == "census")
{
    // census <hex tile>[,<hex tile>…]: every settlement tile of those numbers (location, floor, x, y),
    // with the tiles round it (n e s w) and the same cell's tile on the floors above and below;
    // census unlisted: every tile town_tiles.json has no rule for, counted
    if (argv[1] == "unlisted")
    {
        var listed = System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "town_tiles.json")), "\"([0-9a-f]{2})\": \\{\"kind\"")
            .Select(m => Convert.ToByte(m.Groups[1].Value, 16)).ToHashSet();
        var counts = new System.Collections.Generic.Dictionary<byte, (int n, string where)>();
        foreach (var loc in data.Locations.Where(l => l.IsSettlement))
            for (int z = loc.MinZ; z <= loc.MaxZ; z++)
                foreach (var t in data.Maps.Floor(loc, z).Where(t => !listed.Contains(t)))
                    counts[t] = counts.TryGetValue(t, out var c) ? (c.n + 1, c.where.Contains(loc.Name) ? c.where : c.where + ", " + loc.Name) : (1, loc.Name);
        foreach (var kv in counts.OrderBy(k => k.Key)) Console.WriteLine($"{kv.Key:x2} {data.TileNames[kv.Key],-28} x{kv.Value.n}: {kv.Value.where}");
        return 0;
    }
    var want = argv[1].Split(',').Select(s => Convert.ToByte(s, 16)).ToHashSet();
    if (argv.Count > 2 && (argv[2] == "britannia" || argv[2] == "underworld"))
    {
        // census <tiles> britannia|underworld: those tiles on the overworld, with the 3×3 round each
        var w = argv[2] == "britannia" ? data.Britannia : data.Underworld;
        for (int y = 0; y < 256; y++)
            for (int x = 0; x < 256; x++)
                if (want.Contains(w[x, y]))
                    Console.WriteLine($"({x,3},{y,3}) {w[x, y]:x2}  " + string.Join(" | ", Enumerable.Range(-1, 3).Select(dy => string.Join(" ", Enumerable.Range(-1, 3).Select(dx => w[x + dx, y + dy].ToString("x2"))))));
        return 0;
    }
    foreach (var loc in data.Locations.Where(l => l.IsSettlement))
        for (int z = loc.MinZ; z <= loc.MaxZ; z++)
        {
            var m = data.Maps.Floor(loc, z);
            for (int c = 0; c < 32 * 32; c++)
            {
                if (!want.Contains(m[c])) continue;
                int x = c % 32, y = c / 32;
                string At(int xx, int yy) => xx < 0 || yy < 0 || xx > 31 || yy > 31 ? "--" : m[yy * 32 + xx].ToString("x2");
                string Floor(int zz) => zz < loc.MinZ || zz > loc.MaxZ ? "--" : data.Maps.Floor(loc, zz)[c].ToString("x2");
                Console.WriteLine($"{loc.Number,2} {loc.Name,-18} z{z,2} ({x,2},{y,2}) {m[c]:x2}  n {At(x, y - 1)} e {At(x + 1, y)} s {At(x, y + 1)} w {At(x - 1, y)}  up {Floor(z + 1)} down {Floor(z - 1)}");
            }
        }
    return 0;
}
if (argv.Count > 0 && argv[0] == "town")
{
    // town <location> <out.ppm>
    string maps = AppContext.BaseDirectory;
    var tiles = Bretaheim.WorldGen.Core.TownTiles.Parse(File.ReadAllText(Path.Combine(maps, "town_tiles.json")));
    var geo = Bretaheim.WorldGen.Core.PieceGeometry.Parse(File.ReadAllText(Path.Combine(maps, "piece_geometry.json")));
    var loc = data.FindLocation(argv[1]);
    var pois = Bretaheim.WorldGen.Core.Pois.Build(data);
    string kind = pois.First(p => p.Location == loc.Number).Kind;
    var plan = new Bretaheim.WorldGen.Core.SettlementPlanner(data, tiles, geo).Plan(loc, kind, Bretaheim.WorldGen.Core.VBiome.Meadows);
    string outPath = argv.Count > 2 ? argv[2] : "town.ppm";
    if (outPath == "probe")
    {
        Console.WriteLine(new Bretaheim.WorldGen.Core.SettlementPlanner(data, tiles, geo).Probe(loc, int.Parse(argv[3]), int.Parse(argv[4]), int.Parse(argv[5])));
        return 0;
    }
    if (outPath == "inn")
    {
        // town <location> inn: the inn's rooms (I) and the beds on that storey (b in them, B outside)
        foreach (var kv in plan.InnRooms)
        {
            Console.WriteLine($"inn rooms, floor {kv.Key}");
            var beds = new System.Collections.Generic.HashSet<int>(plan.Seats.Where(s => s.Key.z == kv.Key && s.Value.Bed).Select(s => s.Key.cell));
            for (int y = 0; y < 32; y++)
                Console.WriteLine(string.Concat(Enumerable.Range(0, 32).Select(x => { int c = y * 32 + x; return beds.Contains(c) ? (kv.Value[c] ? 'b' : 'B') : kv.Value[c] ? 'I' : '.'; })));
        }
        return 0;
    }
    if (outPath == "floors")
    {
        Console.Write(new Bretaheim.WorldGen.Core.SettlementPlanner(data, tiles, geo).DescribeFloors(loc));
        return 0;
    }
    if (outPath.EndsWith(".json"))
        File.WriteAllText(outPath, "[" + string.Join(",\n", plan.Pieces.Select(p =>
            System.FormattableString.Invariant($"{{\"p\":\"{p.Prefab}\",\"x\":{p.X},\"y\":{p.Y},\"z\":{p.Z},\"yaw\":{p.Yaw},\"s\":[{(p.Scaled ? p.SX : 1)},{(p.Scaled ? p.SY : 1)},{(p.Scaled ? p.SZ : 1)}]{(p.StandItem != null ? $",\"item\":\"{p.StandItem}\"" : "")}{(p.Look != null ? $",\"look\":\"{p.Look}\"" : "")}}}"))) + "]");
    else TownPreview.Render(plan, geo, outPath);
    Console.WriteLine($"{loc.Name} ({kind}): {plan.Pieces.Count} pieces, spawn {(plan.HasSpawn ? $"({plan.SpawnX}, {plan.SpawnZ})" : "none")}"
        + $"{(plan.Garden.Any(g => g) ? $", flower beds {plan.Garden.Count(g => g)} tiles" : "")}{(plan.HasWater ? $", water {plan.Water.Count(w => w)} tiles" : "")}");
    foreach (var g in plan.Pieces.GroupBy(p => p.Prefab).OrderByDescending(g => g.Count())) Console.Write($"{g.Key}x{g.Count()} ");
    Console.WriteLine();
    var noGeometry = plan.Pieces.Select(p => p.Prefab).Distinct().Where(p => !geo.Has(p)).ToList();
    if (noGeometry.Count > 0) Console.WriteLine("no geometry: " + string.Join(", ", noGeometry));
    return 0;
}
Console.WriteLine(Dump.Run(data, argv.ToArray()));
return 0;

class DumpTalkWorld : Bretaheim.Data.ITalkWorld
{
    public string AvatarName => "Avatar";
    public System.Collections.Generic.IEnumerable<string> PartyNames => new[] { "Avatar" };
    public bool Met { get; set; }
    public int Karma { get; set; } = 50;
    public int Gold { get; set; } = 100;
    public void TakeGold(int amount) { Gold -= amount; Console.Write($"[pays {amount}]"); }
    public void Give(int item) => Console.Write($"[gives 0x{item:x2}]");
    public bool Join() => false;
    public void Alarm() => Console.Write("[guards!]");
    public bool Coin() => true;
}
