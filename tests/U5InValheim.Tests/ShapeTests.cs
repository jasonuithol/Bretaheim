using System;
using System.IO;
using System.Linq;
using Xunit;
using U5InValheim.Data;
using U5InValheim.WorldGen.Core;

namespace U5InValheim.Tests
{
    public class ShapeTests
    {
        static readonly Lazy<TerrainTable> terrain = new Lazy<TerrainTable>(() =>
            TerrainTable.Parse(File.ReadAllText(Path.Combine(Env.RepoRoot, "assets/mappings/terrain.json"))));
        static readonly Lazy<BritanniaShape> shape = new Lazy<BritanniaShape>(() =>
            new BritanniaShape(Env.Data.Britannia, terrain.Value, seed: 42));

        /// <summary>Tiles whose 5×5 neighbourhood is all the same terrain class (borders may be smoothed).</summary>
        static bool Uniform(WorldMap m, TerrainTable t, int x, int y)
        {
            var c = t[m[x, y]];
            for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
                if (t[m[x + dx, y + dy]] != c) return false;
            return true;
        }

        [U5Fact]
        public void TileCentresKeepTheirTerrain()
        {
            var s = shape.Value; var m = Env.Data.Britannia; var t = terrain.Value;
            int checkedTiles = 0;
            for (int y = 0; y < 256; y++)
            for (int x = 0; x < 256; x++)
            {
                if (!Uniform(m, t, x, y)) continue;
                var c = t[m[x, y]];
                s.TileCentre(x, y, out float wx, out float wz);
                float h = s.Height(wx, wz);
                var b = s.Biome(wx, wz);
                if (c.Water == WaterKind.Sea)
                {
                    Assert.True(h < 0, $"{c.Name} tile ({x},{y}) at {h} m");
                    Assert.Equal(VBiome.Ocean, b);
                }
                else if (c.Biome != VBiome.None)
                {
                    Assert.True(h > 0, $"{c.Name} tile ({x},{y}) at {h} m");
                    Assert.Equal(c.Biome, b);
                }
                checkedTiles++;
            }
            Assert.True(checkedTiles > 20000, $"only {checkedTiles} uniform tiles");
        }

        [U5Fact]
        public void RiversAreBoatDeepAndKeepTheirBanksBiome()
        {
            var s = shape.Value; var m = Env.Data.Britannia; var t = terrain.Value;
            for (int y = 0; y < 256; y++)
            for (int x = 0; x < 256; x++)
            {
                if (t[m[x, y]].Water != WaterKind.River) continue;
                // river tiles bordered by river on both sides along some axis are mid-channel
                bool mid = (t[m[x - 1, y]].Water == WaterKind.River && t[m[x + 1, y]].Water == WaterKind.River)
                        || (t[m[x, y - 1]].Water == WaterKind.River && t[m[x, y + 1]].Water == WaterKind.River);
                if (!mid) continue;
                s.TileCentre(x, y, out float wx, out float wz);
                if (s.Biome(wx, wz) == VBiome.Ocean) continue; // river mouths
                Assert.True(s.Height(wx, wz) <= -2.4f, $"river ({x},{y}) only {s.Height(wx, wz)} m deep");
            }
        }

        [U5Fact]
        public void TheMarginRepeatsTheOppositeEdge()
        {
            var s = shape.Value;
            float wrap = s.WrapMetres;
            var rnd = new Random(7);
            for (int i = 0; i < 2000; i++)
            {
                // a point within the margin east of the square and its twin inside, near the west edge
                float wx = 128 * s.TileMetres + (float)rnd.NextDouble() * (s.MarginTiles - 1) * s.TileMetres;
                float wz = (float)(rnd.NextDouble() - 0.5) * 250 * s.TileMetres;
                Assert.Equal(s.Height(wx - wrap, wz), s.Height(wx, wz), 0.01f);
                Assert.Equal(s.Biome(wx - wrap, wz), s.Biome(wx, wz));
                // and north/south
                Assert.Equal(s.Height(wz, wx - wrap), s.Height(wz, wx), 0.01f);
            }
        }

        [U5Fact]
        public void BeyondTheMarginIsDeepOcean()
        {
            var s = shape.Value;
            float far = (128 + s.MarginTiles + s.FadeTiles + 1) * s.TileMetres;
            Assert.Equal(BritanniaShape.DeepOcean, s.Height(far, 0));
            Assert.Equal(VBiome.Ocean, s.Biome(0, -far));
            Assert.Equal(BritanniaShape.DeepOcean, s.Height(9000, 9000));
        }

        [U5Fact]
        public void SameSeedSameWorld()
        {
            var a = new BritanniaShape(Env.Data.Britannia, terrain.Value, seed: 5);
            var b = new BritanniaShape(Env.Data.Britannia, terrain.Value, seed: 5);
            var c = new BritanniaShape(Env.Data.Britannia, terrain.Value, seed: 6);
            float diff = 0;
            for (int i = 0; i < 200; i++)
            {
                float wx = -4000 + i * 40, wz = 1000 - i * 17;
                Assert.Equal(a.Height(wx, wz), b.Height(wx, wz));
                diff += Math.Abs(a.Height(wx, wz) - c.Height(wx, wz));
            }
            Assert.True(diff > 1, "the seed should vary the detail");
        }

        [U5Fact]
        public void BritainIsDryLand()
        {
            var s = shape.Value;
            var britain = Env.Data.Location(2);
            s.TileCentre(britain.X, britain.Y, out float wx, out float wz);
            Assert.True(s.Height(wx, wz) > 0.5f);
            Assert.NotEqual(VBiome.Ocean, s.Biome(wx, wz));
        }

        [U5Fact]
        public void PoisCoverPlacesShrinesAndMoongates()
        {
            var pois = Pois.Build(Env.Data);
            Assert.Equal(40, pois.Count(p => p.Location > 0));
            Assert.Equal(7, pois.Count(p => p.Kind == "shrine")); // Spirituality is off the map
            Assert.Equal(8, pois.Count(p => p.Kind == "moongate"));
            Assert.Single(pois, p => p.Kind == "codex");
            Assert.Equal("towne", pois.Single(p => p.Name == "Britain").Kind);
            // every shrine stands on a shrine tile
            Assert.All(pois.Where(p => p.Kind == "shrine"), p => Assert.Equal(0x19, Env.Data.Britannia[p.TileX, p.TileY]));
        }

        [Fact]
        public void JsonReader()
        {
            var o = Json.Obj(Json.Parse("{\"a\": [1, 2.5, -3e2], \"b\": {\"c\": \"x\\\"y\"}, \"d\": true, \"e\": null}"));
            Assert.Equal(new object[] { 1.0, 2.5, -300.0 }, Json.Arr(o["a"]).ToArray());
            Assert.Equal("x\"y", Json.Str(Json.Obj(o["b"]), "c"));
            Assert.True(Json.Bool(o, "d"));
            Assert.Null(o["e"]);
        }
    }
}
