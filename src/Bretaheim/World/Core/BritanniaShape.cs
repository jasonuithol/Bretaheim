using System;
using System.Collections.Generic;
using Bretaheim.Data;

namespace Bretaheim.WorldGen.Core
{
    /// <summary>
    /// The terrain of one 256×256 U5 map placed in Valheim: height (metres relative to sea level)
    /// and biome at any world position. Unity-free and read-only after construction, because
    /// Valheim samples it from its terrain worker threads.
    ///
    /// Coordinates: tile (tx, ty) has its north-west corner at world
    /// x = (tx − 128)·T + OriginX, z = (128 − ty)·T + OriginZ (U5 y grows south, Valheim z north).
    ///
    /// Everything is periodic over the 256-tile square (toroidal blurs, noise whose octaves repeat
    /// every 256 tiles), so the margin beyond each edge shows exactly the opposite edge and the
    /// wrap teleport (MASTER_PLAN §5.1) lands on identical ground. Past the margin the land fades
    /// into deep ocean.
    ///
    /// Height synthesis follows generate_terrain.py: per-class base elevation blurred at two
    /// scales, noise scaled by a blurred per-class amplitude, coasts forced crisp (land ≥ +0.8 m,
    /// sea ≤ −1 m), river channels carved, fine grain on land.
    /// </summary>
    public sealed class BritanniaShape
    {
        public const int MapTiles = 256;
        public const int SamplesPerTile = 4;
        const int N = MapTiles * SamplesPerTile;
        public const float DeepOcean = -70f;
        /// <summary>The forest factor over the sea (treeless): keeps the river lilies off the coasts.</summary>
        public const float SeaForestFactor = 10f;

        public float TileMetres { get; }
        /// <summary>The least depth of open water away from its shore (metres; 0: as the shore shelves). Set before the shape is used.</summary>
        public float MinWaterDepth { get; set; }
        public int MarginTiles { get; }
        public int FadeTiles { get; }
        public float OriginX { get; }
        public float OriginZ { get; }
        public WorldMap Map { get; }
        public TerrainTable Terrain { get; }

        readonly float[] baseH = new float[N * N];
        readonly float[] amp = new float[N * N];
        readonly float[] sea = new float[N * N];
        readonly float[] river = new float[N * N];
        readonly float[] riverWide = new float[N * N];
        /// <summary>1 where the coast's biome has beaches (Plains, Meadows, Ashlands), blurred so they fade out.</summary>
        readonly float[] beachBiome = new float[N * N];
        /// <summary>Signed metres from each sample to the coastline: positive inland, negative out to sea.</summary>
        readonly float[] coastDist = new float[N * N];
        /// <summary>Rivers blurred over about 40 m: how near the water a bank is.</summary>
        readonly float[] riverBank = new float[N * N];
        readonly float[] forest = new float[N * N];
        readonly VBiome[] landBiome = new VBiome[MapTiles * MapTiles];
        /// <summary>Smoothed path lines in tile coordinates, and per tile the ones that pass near it.</summary>
        readonly List<(double ax, double ay, double bx, double by)> pathSegments = new List<(double, double, double, double)>();
        readonly List<int>[] pathBuckets = new List<int>[MapTiles * MapTiles];
        /// <summary>Width of the overworld's dirt paths, metres.</summary>
        public const float PathWidth = 4f;
        static readonly int[] DirX = { 1, -1, 0, 0, 1, -1, 1, -1 }, DirY = { 0, 0, 1, -1, 1, 1, -1, -1 };
        readonly int seed;
        readonly Cliffs cliffs;

        public BritanniaShape(WorldMap map, TerrainTable terrain, int seed, float tileMetres = 32f,
                              int marginTiles = 16, int fadeTiles = 6, float originX = 0f, float originZ = 0f)
        {
            Map = map;
            Terrain = terrain;
            TileMetres = tileMetres;
            MarginTiles = marginTiles;
            FadeTiles = fadeTiles;
            OriginX = originX;
            OriginZ = originZ;
            this.seed = seed;
            cliffs = new Cliffs(map, terrain);

            var tBase = new float[N * N];
            var tAmp = new float[N * N];
            var tSea = new float[N * N];
            var tRiver = new float[N * N];
            var tMouth = new float[N * N];
            var tForest = new float[N * N];
            var mouth = RiverMouthCloseness();
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int tx = x / SamplesPerTile, ty = y / SamplesPerTile;
                var c = terrain[map[tx, ty]];
                int i = y * N + x;
                bool seaHalf = c.SeaHalf != null && InSeaHalf(c.SeaHalf, x % SamplesPerTile, y % SamplesPerTile);
                tBase[i] = seaHalf ? c.SeaBase : c.Base;
                tAmp[i] = seaHalf ? 0.3f : c.Noise;
                tSea[i] = c.Water == WaterKind.Sea || seaHalf ? 1f : 0f;
                tRiver[i] = c.Water == WaterKind.River ? 1f : 0f;
                tMouth[i] = tRiver[i] * mouth[ty * MapTiles + tx];
                tForest[i] = c.Forest;
            }
            // two-scale base: some local relief (σ ≈ ¾ tile) on long slopes toward the ranges (σ ≈ 2
            // tiles), so a lone mountain tile is a hill and only whole ranges climb high
            var fine = Blur(tBase, 3f);
            var coarse = Blur(tBase, 8f);
            for (int i = 0; i < baseH.Length; i++) baseH[i] = 0.2f * fine[i] + 0.8f * coarse[i];
            Array.Copy(Blur(tAmp, 1.6f), amp, amp.Length);
            Array.Copy(Blur(tSea, 0.8f), sea, sea.Length);
            CoastDistances();
            Array.Copy(Blur(tRiver, 0.8f), river, river.Length);
            Array.Copy(Blur(tMouth, 2.4f), riverWide, riverWide.Length);
            Array.Copy(Blur(tRiver, 3f), riverBank, riverBank.Length);
            Array.Copy(Blur(tForest, 2f), forest, forest.Length);

            ResolveLandBiomes();
            var tBeach = new float[N * N];
            for (int i = 0; i < tBeach.Length; i++)
            {
                var lb = landBiome[(i / N / SamplesPerTile) * MapTiles + (i % N) / SamplesPerTile];
                tBeach[i] = lb == VBiome.Plains || lb == VBiome.Meadows || lb == VBiome.AshLands ? 1f : 0f;
            }
            Array.Copy(Blur(tBeach, 2f), beachBiome, beachBiome.Length);
            LinkPaths();

            var tLava = new float[N * N];
            bool anyLava = false;
            for (int i = 0; i < tLava.Length; i++)
                if (terrain[map[(i % N) / SamplesPerTile, i / N / SamplesPerTile]].Water == WaterKind.Lava) { tLava[i] = 1f; anyLava = true; }
            if (anyLava) lava = Blur(tLava, 1.2f);
        }

        /// <summary>Lava tiles blurred over about 10 m (null when the map has none).</summary>
        readonly float[] lava;

        /// <summary>
        /// How much a position is molten lava (0..1): Valheim's lava is the Ashlands ground whose
        /// paint mask alpha is high (Heightmap.GetLava), so this is that alpha on the lava tiles.
        /// </summary>
        public float Lava(float wx, float wz)
        {
            if (lava == null) return 0f;
            ToTile(wx, wz, out double u, out double v);
            if (OutsideTiles(u, v) > 0) return 0f;
            return (float)SmoothStep((Sample(lava, u * SamplesPerTile - 0.5, v * SamplesPerTile - 0.5) - 0.25) / 0.5);
        }

        // ── coordinates ─────────────────────────────────────────────────────────────────────

        /// <summary>Continuous tile coordinates (u east, v south) of a world position.</summary>
        public void ToTile(float wx, float wz, out double u, out double v)
        {
            u = (wx - OriginX) / TileMetres + MapTiles / 2;
            v = MapTiles / 2 - (wz - OriginZ) / TileMetres;
        }

        /// <summary>World x/z of the centre of tile (tx, ty).</summary>
        public void TileCentre(double tx, double ty, out float wx, out float wz)
        {
            wx = (float)((tx + 0.5 - MapTiles / 2) * TileMetres + OriginX);
            wz = (float)((MapTiles / 2 - (ty + 0.5)) * TileMetres + OriginZ);
        }

        /// <summary>Metres one full wrap moves a traveller (256 tiles).</summary>
        public float WrapMetres => MapTiles * TileMetres;

        /// <summary>Tiles outside the 256×256 square (Chebyshev distance, 0 inside).</summary>
        public double OutsideTiles(double u, double v)
            => Math.Max(0, Math.Max(Math.Abs(u - MapTiles / 2.0) - MapTiles / 2.0, Math.Abs(v - MapTiles / 2.0) - MapTiles / 2.0));

        public byte TileAt(float wx, float wz)
        {
            ToTile(wx, wz, out double u, out double v);
            return Map[(int)Math.Floor(u), (int)Math.Floor(v)];
        }

        // ── sampling ────────────────────────────────────────────────────────────────────────

        // ── settlements ─────────────────────────────────────────────────────────────────────

        SettlementSite[] sites = new SettlementSite[0];

        /// <summary>Flattens and paints the ground of built settlements (set before the shape is used).</summary>
        public void SetSites(IEnumerable<SettlementSite> list) => sites = new List<SettlementSite>(list).ToArray();
        public IReadOnlyList<SettlementSite> Sites => sites;

        /// <summary>Ground height in metres relative to sea level (Valheim's absolute y = 30 + this).</summary>
        public float Height(float wx, float wz)
        {
            float h = NaturalHeight(wx, wz);
            foreach (var site in sites)
            {
                float d = Math.Max(Math.Abs(wx - site.CentreX), Math.Abs(wz - site.CentreZ)) - site.Half;
                if (d >= site.Blend) continue;
                if (d <= 0)
                {
                    int cell = site.Cell(wx, wz);
                    // water is at sea level: its bed 1.6 m under it, however high the ground
                    if (cell >= 0 && site.Water[cell]) return Math.Min(site.Base - 3.1f, -1.6f);
                    // the basement's rooms are dug a storey down; a point on the edge of a dug cell
                    // is dug too, so the cut's slope falls inside the wall beside it
                    if (site.Basement != null && site.BasementAt(wx, wz)) return site.Base - site.Depth;
                    return site.Base;
                }
                h = (float)Lerp(site.Base, h, SmoothStep(d / site.Blend));
            }
            return h;
        }

        /// <summary>Terrain paint: a settlement's streets and fields, the overworld's roads.</summary>
        public Paint PaintAt(float wx, float wz)
        {
            foreach (var site in sites)
            {
                int cell = site.Cell(wx, wz);
                if (cell >= 0) return site.Paint[cell];
            }
            ToTile(wx, wz, out double u, out double v);
            if (OutsideTiles(u, v) > 0) return Paint.None;
            return OnPath(u, v) ? Paint.Dirt : Paint.None;
        }

        /// <summary>On a dirt path: within the path's (varying) half width of its wandering centre line.</summary>
        bool OnPath(double u, double v) => PathQuery(u, v, out _, out _, out double half) < half;

        /// <summary>Metres from a world position to the nearest path's centre line (∞ when none passes near).</summary>
        public float PathDistance(float wx, float wz)
        {
            ToTile(wx, wz, out double u, out double v);
            return (float)(PathQuery(u, v, out _, out _, out _) * TileMetres);
        }

        /// <summary>Metres from a world position to the nearest path's edge (negative on the path).</summary>
        public float PathEdgeDistance(float wx, float wz)
        {
            ToTile(wx, wz, out double u, out double v);
            return (float)((PathQuery(u, v, out _, out _, out double half) - half) * TileMetres);
        }

        /// <summary>
        /// The path's wander (tile units) at a point: broad bends of ~5 m over ~60 m and a ~1.5 m
        /// wiggle over ~15 m. A path is drawn where the point moved by its wander lands on a line.
        /// </summary>
        void PathWander(double u, double v, out double du, out double dv)
        {
            double mu = u / MapTiles, mv = v / MapTiles;
            du = (12 * PeriodicFbm(mu, mv, 128, 2, 808) + 3.5 * PeriodicFbm(mu, mv, 512, 1, 1501)) / TileMetres;
            dv = (12 * PeriodicFbm(mu, mv, 128, 2, 909) + 3.5 * PeriodicFbm(mu, mv, 512, 1, 1601)) / TileMetres;
        }

        /// <summary>Half the path's width (tile units) at a point: 1.3-2.8 m, varying along the path.</summary>
        double PathHalfWidth(double u, double v)
            => Math.Max(1.3, Math.Min(2.8, 2.0 + 2.2 * PeriodicFbm(u / MapTiles, v / MapTiles, 256, 2, 1401))) / TileMetres;

        /// <summary>
        /// Distance (tile units) from (u, v) to the nearest path centre line, the real position of
        /// that centre point (cu, cv), and the path's half width there.
        /// </summary>
        double PathQuery(double u, double v, out double cu, out double cv, out double half)
        {
            half = PathHalfWidth(u, v);
            PathWander(u, v, out double du, out double dv);
            double wu = u + du, wv = v + dv;
            cu = u; cv = v;
            var bucket = pathBuckets[Wrap((int)Math.Floor(wv)) * MapTiles + Wrap((int)Math.Floor(wu))];
            if (bucket == null) return double.PositiveInfinity;
            // segments are stored near their own copy of the map; bring the point next to them
            double best = double.PositiveInfinity;
            foreach (int k in bucket)
            {
                var (ax, ay, bx, by) = pathSegments[k];
                double su = Math.Floor((wu - ax) / MapTiles + 0.5) * MapTiles, sv = Math.Floor((wv - ay) / MapTiles + 0.5) * MapTiles;
                double pu = wu - su, pv = wv - sv;
                double ex = bx - ax, ey = by - ay, len2 = ex * ex + ey * ey;
                double t = len2 > 0 ? Math.Max(0, Math.Min(1, ((pu - ax) * ex + (pv - ay) * ey) / len2)) : 0;
                double nx = ax + ex * t, ny = ay + ey * t, dx = pu - nx, dy = pv - ny, d2 = dx * dx + dy * dy;
                if (d2 >= best) continue;
                best = d2;
                cu = nx + su - du; cv = ny + sv - dv; // back from the wandered space (the wander barely changes over a few metres)
            }
            return Math.Sqrt(best);
        }

        /// <summary>
        /// Points every ~2 m along the paths drawn inside a world rectangle, with the path's half
        /// width there (metres): where the paths clear the vegetation.
        /// </summary>
        public IEnumerable<(float x, float z, float half)> PathPoints(float x0, float z0, float x1, float z1)
        {
            ToTile(x0, z1, out double u0, out double v0);
            ToTile(x1, z0, out double u1, out double v1);
            var seen = new HashSet<int>();
            for (int ty = (int)Math.Floor(v0) - 1; ty <= (int)Math.Floor(v1) + 1; ty++)
            for (int tx = (int)Math.Floor(u0) - 1; tx <= (int)Math.Floor(u1) + 1; tx++)
            {
                var bucket = pathBuckets[Wrap(ty) * MapTiles + Wrap(tx)];
                if (bucket == null) continue;
                foreach (int k in bucket)
                {
                    if (!seen.Add(k)) continue;
                    var (ax, ay, bx, by) = pathSegments[k];
                    // the copy of the segment nearest the rectangle
                    double su = Math.Floor((u0 - ax) / MapTiles + 0.5) * MapTiles, sv = Math.Floor((v0 - ay) / MapTiles + 0.5) * MapTiles;
                    double len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay)) * TileMetres;
                    int n = Math.Max(1, (int)Math.Ceiling(len / 2));
                    for (int i = 0; i <= n; i++)
                    {
                        double pu = ax + (bx - ax) * i / n + su, pv = ay + (by - ay) * i / n + sv;
                        PathWander(pu, pv, out double du, out double dv);
                        pu -= du; pv -= dv;
                        float x = (float)((pu - MapTiles / 2) * TileMetres + OriginX), z = (float)((MapTiles / 2 - pv) * TileMetres + OriginZ);
                        if (x < x0 - 4 || x > x1 + 4 || z < z0 - 4 || z > z1 + 4) continue;
                        yield return (x, z, (float)(PathHalfWidth(pu, pv) * TileMetres));
                    }
                }
            }
        }

        /// <summary>
        /// The overworld's paths: road and bridge tiles join their path neighbours and the places
        /// (towns, shrines …) they lead to (diagonally only where no straight step joins them). The
        /// network is split into chains between ends and junctions, and each chain of tile centres is
        /// smoothed (Chaikin) so U5's staircase diagonals become curves.
        /// </summary>
        void LinkPaths()
        {
            bool Path(int x, int y) { var n = Terrain[Map[Wrap(x), Wrap(y)]].Name; return n == "road" || n == "bridge"; }
            bool Place(int x, int y) => Terrain[Map[Wrap(x), Wrap(y)]].Poi != null;
            var links = new byte[MapTiles * MapTiles];
            for (int y = 0; y < MapTiles; y++)
            for (int x = 0; x < MapTiles; x++)
            {
                bool path = Path(x, y);
                if (!path && !Place(x, y)) continue;
                int l = 0;
                for (int k = 0; k < 8; k++)
                {
                    int nx = x + DirX[k], ny = y + DirY[k];
                    bool joins = path ? Path(nx, ny) || Place(nx, ny) : Path(nx, ny);
                    if (!joins) continue;
                    if (k >= 4 && (Path(x + DirX[k], y) || Path(x, y + DirY[k]) || (!path && (Place(x + DirX[k], y) || Place(x, y + DirY[k]))))) continue;
                    l |= 1 << k;
                }
                links[y * MapTiles + x] = (byte)l;
                if (path && l == 0) AddPath(new List<(double, double)> { (x + 0.5, y + 0.5) });
            }
            // keep links symmetric (a diagonal dropped on one side is dropped on the other)
            for (int i = 0; i < links.Length; i++)
                for (int k = 0; k < 8; k++)
                {
                    if ((links[i] & (1 << k)) == 0) continue;
                    int j = Wrap(i / MapTiles + DirY[k]) * MapTiles + Wrap(i % MapTiles + DirX[k]);
                    if ((links[j] & (1 << Opposite(k))) == 0) links[i] &= (byte)~(1 << k);
                }
            int Degree(int i) { int d = 0; for (int k = 0; k < 8; k++) if ((links[i] & (1 << k)) != 0) d++; return d; }
            var used = new HashSet<(int, int)>();
            void Walk(int from, int k0)
            {
                var pts = new List<(double, double)>();
                int i = from, k = k0;
                double px = i % MapTiles + 0.5, py = i / MapTiles + 0.5;
                pts.Add((px, py));
                while (true)
                {
                    used.Add((i, k));
                    int j = Wrap(i / MapTiles + DirY[k]) * MapTiles + Wrap(i % MapTiles + DirX[k]);
                    used.Add((j, Opposite(k)));
                    px += DirX[k]; py += DirY[k]; // unwrapped, so a chain crossing the edge stays continuous
                    pts.Add((px, py));
                    if (Degree(j) != 2 || j == from) break;
                    int next = -1;
                    for (int n = 0; n < 8; n++)
                        if ((links[j] & (1 << n)) != 0 && n != Opposite(k)) next = n;
                    if (next < 0 || used.Contains((j, next))) break;
                    i = j; k = next;
                }
                AddPath(pts);
            }
            for (int pass = 0; pass < 2; pass++) // ends and junctions first, then loops
                for (int i = 0; i < links.Length; i++)
                {
                    if (links[i] == 0 || (pass == 0 && Degree(i) == 2)) continue;
                    for (int k = 0; k < 8; k++)
                        if ((links[i] & (1 << k)) != 0 && !used.Contains((i, k))) Walk(i, k);
                }
        }

        static int Opposite(int k) => k < 4 ? k ^ 1 : 11 - k;

        /// <summary>Smooths a chain of tile centres (ends kept) and files its segments by the tiles they come near.</summary>
        void AddPath(List<(double x, double y)> pts)
        {
            for (int it = 0; it < 3 && pts.Count > 2; it++)
            {
                var sm = new List<(double, double)> { pts[0] };
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    var (ax, ay) = pts[i]; var (bx, by) = pts[i + 1];
                    if (i > 0) sm.Add((0.75 * ax + 0.25 * bx, 0.75 * ay + 0.25 * by));
                    if (i + 2 < pts.Count) sm.Add((0.25 * ax + 0.75 * bx, 0.25 * ay + 0.75 * by));
                }
                sm.Add(pts[pts.Count - 1]);
                pts = sm;
            }
            double pad = 24.0 / TileMetres; // the wander (up to ~8 m) plus ~12 m of verge, where edges are dressed
            for (int i = 0; i < Math.Max(1, pts.Count - 1); i++)
            {
                var (ax, ay) = pts[i]; var (bx, by) = pts[Math.Min(i + 1, pts.Count - 1)];
                int k = pathSegments.Count;
                pathSegments.Add((ax, ay, bx, by));
                for (int y = (int)Math.Floor(Math.Min(ay, by) - pad); y <= (int)Math.Floor(Math.Max(ay, by) + pad); y++)
                for (int x = (int)Math.Floor(Math.Min(ax, bx) - pad); x <= (int)Math.Floor(Math.Max(ax, bx) + pad); x++)
                {
                    int c = Wrap(y) * MapTiles + Wrap(x);
                    (pathBuckets[c] ?? (pathBuckets[c] = new List<int>())).Add(k);
                }
            }
        }

        /// <summary>Ground height without settlements.</summary>
        public float NaturalHeight(float wx, float wz)
        {
            float h = RawHeight(wx, wz);
            // paths are levelled across: the ground within and just beside a path takes the height
            // of the path's centre line (not over water: fords and bridges keep their river)
            if (h <= 0.3f) return h;
            ToTile(wx, wz, out double u, out double v);
            if (OutsideTiles(u, v) > 0) return h;
            double d = PathQuery(u, v, out double cu, out double cv, out double half);
            if (d >= half + 3 / TileMetres) return h;
            float hc = RawHeight((float)((cu - MapTiles / 2) * TileMetres + OriginX), (float)((MapTiles / 2 - cv) * TileMetres + OriginZ));
            if (hc <= 0.3f) return h;
            return (float)Lerp(h, hc, 1 - SmoothStep((d - half) * TileMetres / 3));
        }

        /// <summary>Ground height without settlements or path levelling.</summary>
        float RawHeight(float wx, float wz)
        {
            ToTile(wx, wz, out double u, out double v);
            double outside = OutsideTiles(u, v);
            if (outside >= MarginTiles + FadeTiles) return DeepOcean;

            double gx = u * SamplesPerTile - 0.5, gy = v * SamplesPerTile - 0.5;
            float b = Sample(baseH, gx, gy);
            float a = Sample(amp, gx, gy);
            float s = Sample(sea, gx, gy);
            float r = Sample(river, gx, gy);

            double mu = u / MapTiles, mv = v / MapTiles; // map units, noise periodic over 1.0
            double n = PeriodicFbm(mu, mv, 12, 6, 0);
            double ridged = 1 - Math.Abs(PeriodicFbm(mu, mv, 6, 3, 101));
            double h = b + a * (0.6 * n + 0.6 * (ridged - 0.5));

            float coastSigned = Sample(coastDist, gx, gy);
            float coast = Math.Abs(coastSigned);
            if (s >= 0.5f)
            {
                // the seabed shelves out from the shore and is ~10 m deep 30 m out, where Valheim's
                // waves reach full height (they grow with depth up to 10 m)
                h = Math.Min(-0.15, Math.Max(h, -0.15 - 0.2 * coast - 0.006 * coast * coast));
                // (the Underworld's lakes lie among its rock, which would hold them shallow: deep enough for a ship)
                if (MinWaterDepth > 0) h = Math.Min(h, -Math.Min(MinWaterDepth, 0.15 + 0.12 * coast));
            }
            else
            {
                h = Math.Max(h, 0.8);
                h += 0.6 * PeriodicFbm(mu, mv, 256, 2, 202);
                // Beaches on low Plains, Meadows and Ashlands coasts. Valheim's terrain shader draws
                // sand only within ~0.15 m of the water level (on flat ground), so a beach is a flat
                // sand strip that high, 0-20 m deep along the coast, then low dunes and a rise back
                // to the natural ground. Other coasts come down to just above the sand band; high
                // ground (mountain lakes, sea cliffs) keeps its rocky shore.
                double beach = BeachWidth(mu, mv, gx, gy);
                if (coast < beach + 60)
                {
                    double t = coast - beach, ceiling, sandy = SmoothStep(beach / 2.5);
                    double shore = Lerp(0.45, SandHeight, sandy);
                    if (t < 0) ceiling = SandHeight + 0.05 * PeriodicFbm(mu, mv, 512, 1, 1201);
                    else ceiling = shore + 0.05 * t + 0.006 * t * t + 1.5 * sandy * Ridges(mu, mv, 1101) * SmoothStep(t / 6) * (1 - SmoothStep((t - 20) / 15));
                    double w = (1 - SmoothStep((t - 35) / 25)) * (1 - SmoothStep((h - 5) / 10));
                    h = Lerp(h, Math.Min(h, ceiling), w);
                }
                // Every river has gentle banks: a narrow sandy edge at the water, a flat flood
                // plain about a metre up with low hummocks, then the ground rises back over ~40 m;
                // the bed shelves into a channel at least 2.8 m deep (boats need ~2 m). Rivers
                // widen toward the sea (a wider blur near the mouth).
                r = Math.Max(r, Sample(riverWide, gx, gy) * 1.6f);
                double rb = Sample(riverBank, gx, gy);
                if (rb > 0.05)
                {
                    double q = Math.Max(0, Math.Min(1, (0.42 - rb) / 0.37)); // 0 at the water's edge, 1 ~40 m back
                    double ceiling = SandHeight + 0.9 * SmoothStep((q - 0.06) / 0.08) + 11 * SmoothStep((q - 0.45) / 0.55)
                                     + 0.7 * Ridges(mu, mv, 1301) * SmoothStep((q - 0.15) / 0.1) * (1 - SmoothStep((q - 0.5) / 0.2));
                    h = Lerp(h, Math.Min(h, ceiling), SmoothStep((rb - 0.05) / 0.2));
                }
                if (r > 0.3f)
                {
                    double bed = Lerp(SandHeight, -2.8 - Math.Max(0, r - 0.75) * 2.0, SmoothStep((r - 0.3) / 0.45));
                    h = Math.Min(h, bed);
                }
                // the land stands higher behind a waterfall: a cliff across the river, the river's banks rising
                // as walls a few metres from its water (a gorge), the water itself left at the sea's level
                h += cliffs.Offset(u, v) * SmoothStep((h - 0.1) / 0.5);
            }

            if (outside > MarginTiles)
            {
                double t = SmoothStep((outside - MarginTiles) / FadeTiles);
                h = Lerp(h, DeepOcean, t);
            }
            return (float)h;
        }

        /// <summary>Height of beach sand above the water (the terrain shader's sand band is ~0.1-0.3 m).</summary>
        public const double SandHeight = 0.1;

        /// <summary>Metres of flat sand behind the waterline: 0-20 m along the coast, none off the beach biomes.</summary>
        double BeachWidth(double mu, double mv, double gx, double gy)
            => 20 * SmoothStep(PeriodicFbm(mu, mv, 48, 2, 1001) * 1.8 + 0.45) * Sample(beachBiome, gx, gy);

        /// <summary>Ridged noise 0..1 at ~20 m: dunes and hummocks.</summary>
        double Ridges(double mu, double mv, int salt) => Math.Max(0, 1 - Math.Abs(PeriodicFbm(mu, mv, 384, 2, salt) * 2.2));

        /// <summary>Metres inland from the coast (0 at sea), and how deep the beach there is.</summary>
        public float CoastDistance(float wx, float wz, out float beachWidth)
        {
            ToTile(wx, wz, out double u, out double v);
            double gx = u * SamplesPerTile - 0.5, gy = v * SamplesPerTile - 0.5;
            beachWidth = (float)BeachWidth(u / MapTiles, v / MapTiles, gx, gy);
            return Sample(sea, gx, gy) >= 0.5f || OutsideTiles(u, v) > 0 ? 0 : Math.Max(0, Sample(coastDist, gx, gy));
        }

        /// <summary>Approximate metres from a river's water's edge (0 in the water, ∞ more than ~40 m away).</summary>
        public float RiverDistance(float wx, float wz)
        {
            ToTile(wx, wz, out double u, out double v);
            if (OutsideTiles(u, v) > 0) return float.PositiveInfinity;
            double gx = u * SamplesPerTile - 0.5, gy = v * SamplesPerTile - 0.5;
            if (Sample(sea, gx, gy) >= 0.3f) return float.PositiveInfinity;
            double rb = Sample(riverBank, gx, gy);
            if (rb <= 0.05) return float.PositiveInfinity;
            return (float)(Math.Max(0, (0.42 - rb) / 0.37) * 40);
        }

        /// <summary>
        /// How much a position is river bank (0..1): dry ground within a few metres of the water's
        /// edge and up to ~25 m back from it, where reeds and shrubs grow.
        /// </summary>
        public float RiverBank(float wx, float wz)
        {
            ToTile(wx, wz, out double u, out double v);
            if (OutsideTiles(u, v) > 0) return 0;
            double gx = u * SamplesPerTile - 0.5, gy = v * SamplesPerTile - 0.5;
            if (Sample(sea, gx, gy) >= 0.3f) return 0;
            double rb = Sample(riverBank, gx, gy);
            return (float)(SmoothStep((rb - 0.12) / 0.15) * (1 - SmoothStep((rb - 0.42) / 0.06)));
        }

        /// <summary>Valheim biome at a world position.</summary>
        public VBiome Biome(float wx, float wz)
        {
            ToTile(wx, wz, out double u, out double v);
            double outside = OutsideTiles(u, v);
            if (outside >= MarginTiles + FadeTiles * 0.5) return VBiome.Ocean;
            if (Sample(sea, u * SamplesPerTile - 0.5, v * SamplesPerTile - 0.5) >= 0.5f) return VBiome.Ocean;

            // Warp the lookup so biome borders are not staircases of 32 m tiles: broad meanders
            // (~10 tiles) plus finer wobble (~1-3 tiles) that turns lone tiles into irregular patches.
            double mu = u / MapTiles, mv = v / MapTiles;
            double wu = u + 2.0 * PeriodicFbm(mu, mv, 24, 3, 303) + 1.3 * PeriodicFbm(mu, mv, 96, 2, 606);
            double wv = v + 2.0 * PeriodicFbm(mu, mv, 24, 3, 404) + 1.3 * PeriodicFbm(mu, mv, 96, 2, 707);
            return landBiome[Wrap((int)Math.Floor(wv)) * MapTiles + Wrap((int)Math.Floor(wu))];
        }

        /// <summary>
        /// Valheim's forest factor (lower = denser forest; the vegetation tables gate their trees on
        /// it): per tile class, blurred over about half a tile and varied a little by noise.
        /// </summary>
        public float ForestFactor(float wx, float wz)
        {
            ToTile(wx, wz, out double u, out double v);
            if (OutsideTiles(u, v) > 0 || Sample(sea, u * SamplesPerTile - 0.5, v * SamplesPerTile - 0.5) >= 0.3f) return SeaForestFactor;
            float f = Sample(forest, u * SamplesPerTile - 0.5, v * SamplesPerTile - 0.5);
            double mu = u / MapTiles, mv = v / MapTiles;
            return (float)(f * (1 + 0.25 * PeriodicFbm(mu, mv, 512, 2, 505)));
        }

        /// <summary>The land biome assigned to tile (tx, ty) (water and POI tiles take their surroundings').</summary>
        public VBiome LandBiomeOfTile(int tx, int ty) => landBiome[Wrap(ty) * MapTiles + Wrap(tx)];

        // ── internals ───────────────────────────────────────────────────────────────────────

        /// <summary>Sample (sx, sy) of a tile (0..SamplesPerTile−1, sy south) lies in its diagonal sea half.</summary>
        static bool InSeaHalf(string half, int sx, int sy)
        {
            int last = SamplesPerTile - 1;
            switch (half)
            {
                case "nw": return sx + sy < last;
                case "se": return sx + sy > last;
                case "ne": return sx > sy;
                case "sw": return sx < sy;
                default: return false;
            }
        }

        /// <summary>
        /// Signed distance in metres from every sample to the nearest sample on the other side of
        /// the coastline (sea = blurred sea ≥ ½), less half a sample, negative at sea, so it passes
        /// through 0 at the shore: a
        /// two-pass chamfer transform on the torus, repeated so it carries across the map edge.
        /// </summary>
        void CoastDistances()
        {
            float spacing = TileMetres / SamplesPerTile;
            const float Far = 1e6f;
            var land = new float[N * N];
            var water = new float[N * N];
            for (int i = 0; i < N * N; i++)
            {
                bool isSea = sea[i] >= 0.5f;
                land[i] = isSea ? 0 : Far;  // distance to the nearest sea sample, for land
                water[i] = isSea ? Far : 0; // distance to the nearest land sample, for sea
            }
            Chamfer(land, spacing);
            Chamfer(water, spacing);
            for (int i = 0; i < N * N; i++)
                coastDist[i] = sea[i] >= 0.5f ? -Math.Max(0, water[i] - spacing / 2) : Math.Max(0, land[i] - spacing / 2);
        }

        static void Chamfer(float[] d, float step)
        {
            float a = step, b = step * 1.4142f;
            for (int rep = 0; rep < 2; rep++)
            {
                for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x;
                    float v = d[i];
                    v = Math.Min(v, d[y * N + WrapN(x - 1)] + a);
                    v = Math.Min(v, d[WrapN(y - 1) * N + x] + a);
                    v = Math.Min(v, d[WrapN(y - 1) * N + WrapN(x - 1)] + b);
                    v = Math.Min(v, d[WrapN(y - 1) * N + WrapN(x + 1)] + b);
                    d[i] = v;
                }
                for (int y = N - 1; y >= 0; y--)
                for (int x = N - 1; x >= 0; x--)
                {
                    int i = y * N + x;
                    float v = d[i];
                    v = Math.Min(v, d[y * N + WrapN(x + 1)] + a);
                    v = Math.Min(v, d[WrapN(y + 1) * N + x] + a);
                    v = Math.Min(v, d[WrapN(y + 1) * N + WrapN(x + 1)] + b);
                    v = Math.Min(v, d[WrapN(y + 1) * N + WrapN(x - 1)] + b);
                    d[i] = v;
                }
            }
        }

        /// <summary>
        /// Per tile, how close its river is to the sea: 1 for river tiles touching the sea, falling
        /// to 0 ten river tiles upstream (BFS along the river network on the torus); 0 elsewhere.
        /// </summary>
        float[] RiverMouthCloseness()
        {
            const int Reach = 10;
            var dist = new int[MapTiles * MapTiles];
            var queue = new Queue<int>();
            bool River(int i) => Terrain[Map[i % MapTiles, i / MapTiles]].Water == WaterKind.River;
            bool Sea(int i) => Terrain[Map[i % MapTiles, i / MapTiles]].Water == WaterKind.Sea;
            int[] dx = { 1, -1, 0, 0 }, dy = { 0, 0, 1, -1 };
            for (int i = 0; i < dist.Length; i++)
            {
                dist[i] = -1;
                if (!River(i)) continue;
                int x = i % MapTiles, y = i / MapTiles;
                for (int k = 0; k < 4; k++)
                    if (Sea(Wrap(y + dy[k]) * MapTiles + Wrap(x + dx[k]))) { dist[i] = 0; queue.Enqueue(i); break; }
            }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % MapTiles, y = i / MapTiles;
                for (int k = 0; k < 4; k++)
                {
                    int j = Wrap(y + dy[k]) * MapTiles + Wrap(x + dx[k]);
                    if (dist[j] >= 0 || !River(j)) continue;
                    dist[j] = dist[i] + 1;
                    queue.Enqueue(j);
                }
            }
            var r = new float[dist.Length];
            for (int i = 0; i < r.Length; i++) r[i] = dist[i] < 0 ? 0 : Math.Max(0, 1 - dist[i] / (float)Reach);
            return r;
        }

        /// <summary>Tiles without a land biome (water, roads, places) take the nearest land tile's biome (BFS on the torus).</summary>
        void ResolveLandBiomes()
        {
            var queue = new Queue<int>();
            for (int i = 0; i < landBiome.Length; i++)
            {
                int x = i % MapTiles, y = i / MapTiles;
                var c = Terrain[Map[x, y]];
                landBiome[i] = c.Biome;
                foreach (var near in c.BiomeNear)
                    if (Math.Abs(x - near.x) <= near.r && Math.Abs(y - near.y) <= near.r) landBiome[i] = near.biome;
                if (landBiome[i] != VBiome.None) queue.Enqueue(i);
            }
            if (queue.Count == 0)
            {
                for (int i = 0; i < landBiome.Length; i++) landBiome[i] = VBiome.Meadows;
                return;
            }
            int[] dx = { 1, -1, 0, 0 }, dy = { 0, 0, 1, -1 };
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % MapTiles, y = i / MapTiles;
                for (int k = 0; k < 4; k++)
                {
                    int j = Wrap(y + dy[k]) * MapTiles + Wrap(x + dx[k]);
                    if (landBiome[j] != VBiome.None) continue;
                    landBiome[j] = landBiome[i];
                    queue.Enqueue(j);
                }
            }
        }

        /// <summary>Fractal noise periodic over one map (1.0 map units): octave o has cells·2^o lattice cells per map.</summary>
        double PeriodicFbm(double mu, double mv, int cells, int octaves, int salt)
        {
            double sum = 0, a = 1, total = 0;
            int c = cells;
            for (int o = 0; o < octaves; o++)
            {
                sum += a * PeriodicPerlin(mu * c, mv * c, c, salt + o * 31);
                total += a;
                a *= 0.5;
                c *= 2;
            }
            return sum / total;
        }

        double PeriodicPerlin(double x, double y, int period, int salt)
        {
            // Sample at the wrapped position; the lattice wraps with the period, so the noise is
            // continuous across the map edge.
            double px = x - Math.Floor(x / period) * period;
            double py = y - Math.Floor(y / period) * period;
            int xi = (int)Math.Floor(px), yi = (int)Math.Floor(py);
            double xf = px - xi, yf = py - yi;
            double g00 = Corner(xi, yi, xf, yf, period, salt);
            double g10 = Corner(xi + 1, yi, xf - 1, yf, period, salt);
            double g01 = Corner(xi, yi + 1, xf, yf - 1, period, salt);
            double g11 = Corner(xi + 1, yi + 1, xf - 1, yf - 1, period, salt);
            double fu = Fade(xf), fv = Fade(yf);
            return Lerp(Lerp(g00, g10, fu), Lerp(g01, g11, fu), fv) * 1.4;
        }

        double Corner(int ix, int iy, double dx, double dy, int period, int salt)
        {
            ix %= period; iy %= period;
            int g = (int)(Hash(ix, iy, salt ^ seed) & 15);
            return GradX[g] * dx + GradY[g] * dy;
        }

        // 16 unit gradients around the circle
        static readonly double[] GradX = new double[16], GradY = new double[16];
        static BritanniaShape()
        {
            for (int i = 0; i < 16; i++)
            {
                GradX[i] = Math.Cos(i * Math.PI / 8);
                GradY[i] = Math.Sin(i * Math.PI / 8);
            }
        }

        static uint Hash(int x, int y, int salt)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + salt * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                return h ^ (h >> 16);
            }
        }

        /// <summary>Bilinear sample of an N×N toroidal grid at grid coordinates.</summary>
        static float Sample(float[] g, double gx, double gy)
        {
            int x0 = (int)Math.Floor(gx), y0 = (int)Math.Floor(gy);
            float fx = (float)(gx - x0), fy = (float)(gy - y0);
            int x1 = WrapN(x0 + 1), y1 = WrapN(y0 + 1);
            x0 = WrapN(x0); y0 = WrapN(y0);
            float a = g[y0 * N + x0] + (g[y0 * N + x1] - g[y0 * N + x0]) * fx;
            float b = g[y1 * N + x0] + (g[y1 * N + x1] - g[y1 * N + x0]) * fx;
            return a + (b - a) * fy;
        }

        /// <summary>Approximate Gaussian blur (three box passes per axis) on the N×N torus; σ in samples.</summary>
        static float[] Blur(float[] src, float sigma)
        {
            int r = Math.Max(1, (int)Math.Round((Math.Sqrt(4 * sigma * sigma + 1) - 1) / 2));
            var a = (float[])src.Clone();
            var b = new float[a.Length];
            for (int pass = 0; pass < 3; pass++)
            {
                BoxH(a, b, r);
                BoxV(b, a, r);
            }
            return a;
        }

        static void BoxH(float[] src, float[] dst, int r)
        {
            float inv = 1f / (2 * r + 1);
            for (int y = 0; y < N; y++)
            {
                int row = y * N;
                float sum = 0;
                for (int k = -r; k <= r; k++) sum += src[row + WrapN(k)];
                for (int x = 0; x < N; x++)
                {
                    dst[row + x] = sum * inv;
                    sum += src[row + WrapN(x + r + 1)] - src[row + WrapN(x - r)];
                }
            }
        }

        static void BoxV(float[] src, float[] dst, int r)
        {
            float inv = 1f / (2 * r + 1);
            for (int x = 0; x < N; x++)
            {
                float sum = 0;
                for (int k = -r; k <= r; k++) sum += src[WrapN(k) * N + x];
                for (int y = 0; y < N; y++)
                {
                    dst[y * N + x] = sum * inv;
                    sum += src[WrapN(y + r + 1) * N + x] - src[WrapN(y - r) * N + x];
                }
            }
        }

        static int WrapN(int i) => ((i % N) + N) % N;
        static int Wrap(int i) => i & (MapTiles - 1);
        static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
        static double Lerp(double a, double b, double t) => a + (b - a) * t;
        static double SmoothStep(double t) { t = Math.Max(0, Math.Min(1, t)); return t * t * (3 - 2 * t); }
    }

    /// <summary>Where built settlements go.</summary>
    public static class SiteLayout
    {
        /// <summary>
        /// Moves settlement squares apart so that every two are at least <paramref name="gap"/>
        /// metres apart (room for their slopes back to the land). The overworld puts a settlement
        /// on one 32 m tile but its square is 64 m: Lord British's castle and the three
        /// Britannys stand on neighbouring tiles. Earlier squares stay put; a later one moves
        /// straight away from the one it crowds, along the axis they are furthest apart on.
        /// </summary>
        public static void Separate(IList<(float x, float z, float half)> squares, float gap, out (float x, float z)[] centres)
        {
            centres = new (float x, float z)[squares.Count];
            for (int i = 0; i < squares.Count; i++) centres[i] = (squares[i].x, squares[i].z);
            for (int i = 1; i < centres.Length; i++)
                for (int pass = 0; pass < 8; pass++)
                {
                    bool moved = false;
                    for (int j = 0; j < i; j++)
                    {
                        float dx = centres[i].x - centres[j].x, dz = centres[i].z - centres[j].z;
                        float need = squares[i].half + squares[j].half + gap;
                        if (Math.Abs(dx) >= need || Math.Abs(dz) >= need) continue;
                        if (Math.Abs(dx) >= Math.Abs(dz)) centres[i].x = centres[j].x + (dx < 0 ? -need : need);
                        else centres[i].z = centres[j].z + (dz < 0 ? -need : need);
                        moved = true;
                    }
                    if (!moved) break;
                }
        }
    }

    /// <summary>The flattened, painted square of a built settlement (32×32 town tiles).</summary>
    public class SettlementSite
    {
        public int Location;
        public float CentreX, CentreZ;
        /// <summary>Half the side of the square, metres.</summary>
        public float Half;
        public float Tile;
        /// <summary>Ground height of the settlement, metres above sea.</summary>
        public float Base;
        /// <summary>Width of the slope back to natural ground around the square.</summary>
        public float Blend = 14f;
        public Paint[] Paint;
        public bool[] Water;
        /// <summary>Cells dug out for the basement, or null; the basement floor is Depth under Base.</summary>
        public bool[] Basement;
        public float Depth = 5f;

        /// <summary>Within (or on the edge of) a basement cell.</summary>
        public bool BasementAt(float wx, float wz)
        {
            const float Edge = 0.02f;
            foreach (var (dx, dz) in new[] { (0f, 0f), (Edge, 0f), (-Edge, 0f), (0f, Edge), (0f, -Edge), (Edge, Edge), (-Edge, Edge), (Edge, -Edge), (-Edge, -Edge) })
            {
                int c = Cell(wx + dx, wz + dz);
                if (c >= 0 && Basement[c]) return true;
            }
            return false;
        }

        /// <summary>Cell index (row-major, y south) of a world position inside the square, else −1.</summary>
        public int Cell(float wx, float wz)
        {
            int x = (int)Math.Floor((wx - CentreX + Half) / Tile);
            int y = (int)Math.Floor((CentreZ + Half - wz) / Tile);
            int n = (int)Math.Round(2 * Half / Tile);
            return x < 0 || y < 0 || x >= n || y >= n ? -1 : y * n + x;
        }
    }
}
