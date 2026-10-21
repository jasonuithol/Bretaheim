using System;
using System.IO;
using System.Linq;
using Xunit;
using Bretaheim.Data;
using Bretaheim.WorldGen.Core;

namespace Bretaheim.Tests
{
    public class UnderworldTests
    {
        static string TerrainJson => File.ReadAllText(Path.Combine(Env.RepoRoot, "assets/mappings/terrain.json"));
        static readonly Lazy<TerrainTable> under = new Lazy<TerrainTable>(() => TerrainTable.Parse(TerrainJson, underworld: true));
        static readonly Lazy<BritanniaShape> britannia = new Lazy<BritanniaShape>(() =>
            new BritanniaShape(Env.Data.Britannia, TerrainTable.Parse(TerrainJson), seed: 42));
        static readonly Lazy<BritanniaShape> below = new Lazy<BritanniaShape>(() =>
            Continents.UnderworldShape(Env.Data.Underworld, under.Value, 42));

        static bool Uniform(WorldMap m, TerrainTable t, int x, int y)
        {
            var c = t[m[x, y]];
            for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
                if (t[m[x + dx, y + dy]] != c) return false;
            return true;
        }

        [Fact]
        public void TheUnderworldsRockIsNotSnow()
        {
            var surface = TerrainTable.Parse(TerrainJson);
            Assert.Equal(VBiome.Mountain, surface[12].Biome);
            Assert.Equal(VBiome.Mistlands, under.Value[12].Biome);
            Assert.Equal(VBiome.Mistlands, under.Value[13].Biome);
            Assert.Equal(3f, under.Value[12].Forest);
            // everything else as on the surface, heights included
            Assert.Equal(surface[12].Base, under.Value[12].Base);
            Assert.Equal(surface[5].Biome, under.Value[5].Biome);
            Assert.Equal(WaterKind.Lava, under.Value[0x8f].Water);
        }

        [U5Fact]
        public void DoomStandsOnTheDarkFloorLevelWithTheLava()
        {
            Assert.False(under.Value[0xff].Impassable);
            Assert.True(TerrainTable.Parse(TerrainJson)[0xff].Impassable); // (the surface's table is unchanged)
            var s = below.Value;
            s.TileCentre(128, 128, out float x, out float z);
            Assert.InRange(s.Height(x, z), 20f, 45f);
            s.TileCentre(128, 124, out x, out z); // the darkness north of it
            Assert.InRange(s.Height(x, z), 20f, 50f);
        }

        [Fact]
        public void ContinentsLieSideBySide()
        {
            float t = 32f;
            Assert.Equal(9600f, Continents.UnderworldOriginX(t));
            Assert.False(Continents.InUnderworld(4799f, t));
            Assert.True(Continents.InUnderworld(4801f, t));
        }

        [U5Fact]
        public void TheContinentsMeetOnlyInDeepOcean()
        {
            var b = britannia.Value; var u = below.Value;
            // each reaches 128 + 16 + 6 tiles from its centre: they meet at the midline, deep ocean both sides
            for (float z = -6000; z <= 6000; z += 500)
            {
                Assert.Equal(BritanniaShape.DeepOcean, b.Height(4800f, z));
                Assert.Equal(BritanniaShape.DeepOcean, u.Height(4800f, z));
                Assert.Equal(VBiome.Ocean, u.Biome(4800f, z));
            }
            float reach = Continents.Reach(u);
            Assert.InRange(reach, Continents.ValheimEdge, 16384f); // past Valheim's edge, inside ZDOMan's 512 sectors of 64 m
            Assert.True(Continents.Reach(b) < Continents.ValheimEdge);
        }

        [U5Fact]
        public void UnderworldTilesKeepTheirTerrain()
        {
            var s = below.Value; var m = Env.Data.Underworld; var t = under.Value;
            int rock = 0, water = 0, land = 0;
            for (int y = 0; y < 256; y += 3)
            for (int x = 0; x < 256; x += 3)
            {
                if (!Uniform(m, t, x, y)) continue;
                var c = t[m[x, y]];
                s.TileCentre(x, y, out float wx, out float wz);
                Assert.True(Continents.InUnderworld(wx, 32f), $"tile ({x},{y}) is not east of the midline");
                if (c.Water == WaterKind.Sea) { Assert.True(s.Height(wx, wz) < 0, $"water ({x},{y})"); Assert.Equal(VBiome.Ocean, s.Biome(wx, wz)); water++; }
                else if (c.Name == "mountains") { Assert.True(s.Height(wx, wz) > 20, $"rock ({x},{y}) {s.Height(wx, wz)}"); rock++; }
                else if (c.Biome != VBiome.None) { Assert.True(s.Height(wx, wz) > 0, $"land ({x},{y})"); land++; }
            }
            Assert.True(rock > 500 && water > 20 && land > 50, $"rock {rock}, water {water}, land {land}");
        }

        [U5Fact]
        public void TheUnderworldWrapsLikeBritannia()
        {
            var s = below.Value;
            float w = s.WrapMetres;
            for (int k = 0; k < 40; k++)
            {
                // a point in the west margin shows the east edge
                s.TileCentre(-3 + k * 0.1, 7 + k * 6, out float x, out float z);
                Assert.Equal((double)s.Height(x + w, z), (double)s.Height(x, z), 2);
                Assert.Equal(s.Biome(x + w, z), s.Biome(x, z));
            }
        }

        [U5Fact]
        public void LavaBurnsOnTheLavaTiles()
        {
            var s = below.Value; var m = Env.Data.Underworld;
            // the lava tiles are thin streams: most of their middles are molten Ashlands ground
            int n = 0, molten = 0, ash = 0;
            for (int y = 0; y < 256; y++)
            for (int x = 0; x < 256; x++)
            {
                if (m[x, y] != 0x8f) continue;
                s.TileCentre(x, y, out float wx, out float wz);
                n++;
                if (s.Lava(wx, wz) > 0.5f) molten++;
                if (s.Biome(wx, wz) == VBiome.AshLands) ash++;
            }
            Assert.True(molten > n * 0.6 && ash > n * 0.6, $"{n} lava tiles, {molten} molten, {ash} Ashlands");
            s.TileCentre(49, 58, out float ax, out float az); // Ararat, by its river
            Assert.Equal(0f, s.Lava(ax, az));
            Assert.Equal(0f, britannia.Value.Lava(0, 0));
            Assert.True(n > 0);
        }

        [Fact]
        public void TheFallsLeadDownOnlyAtTheirTile()
        {
            Assert.Equal((0x36, 0x8a, true), UnderworldLinks.OverFalls(0x36, 0x88, false));
            Assert.Equal((57, 162, false), UnderworldLinks.OverFalls(57, 160, false));
            Assert.Equal((192, 18, true), UnderworldLinks.OverFalls(192, 16, true));
            Assert.Equal((5, 1, false), UnderworldLinks.OverFalls(5, 255, false));
            Assert.True(UnderworldLinks.IsWaterfall(0xd4) && UnderworldLinks.IsWaterfall(0xd7) && !UnderworldLinks.IsWaterfall(0xd8));
        }

        [U5Fact]
        public void TheWaysDownLandOnWater()
        {
            var t = under.Value;
            // the falls: the surface has the waterfall above the pool; the Underworld has water there
            Assert.True(UnderworldLinks.IsWaterfall(Env.Data.Britannia[0x36, 0x89]));
            Assert.Equal(WaterKind.Sea, t[Env.Data.Underworld[UnderworldLinks.FallsX, UnderworldLinks.FallsY]].Water);
            Assert.Equal(WaterKind.Sea, t[Env.Data.Underworld[UnderworldLinks.WhirlpoolX, UnderworldLinks.WhirlpoolY]].Water);
            var s = below.Value;
            s.TileCentre(UnderworldLinks.WhirlpoolX, UnderworldLinks.WhirlpoolY, out float wx, out float wz);
            Assert.True(s.Height(wx, wz) < -2f, $"a ship needs water under it: {s.Height(wx, wz)}");
        }

        [U5Fact]
        public void EveryDungeonHasAWayInFromBelow()
        {
            for (int d = 0; d < DungeonInfo.Count; d++)
            {
                var l = Env.Data.Location(DungeonInfo.Location(d));
                byte tile = Env.Data.Underworld[l.X, l.Y];
                Assert.True(tile >= 0x16 && tile <= 0x18, $"{l.Name}: UNDER.DAT ({l.X},{l.Y}) is {tile:x2}");
                Assert.Equal(d == UnderworldLinks.Doom, l.InUnderworld);
            }
            Assert.Equal((7, 7, 7, 3), UnderworldLinks.DungeonArrival(2, true));
            Assert.Equal((0, 1, 1, 1), UnderworldLinks.DungeonArrival(2, false));
            Assert.Equal((0, 1, 1, 1), UnderworldLinks.DungeonArrival(UnderworldLinks.Doom, true));
            Assert.True(UnderworldLinks.ExitsToUnderworld(3, 7, 1));
            Assert.False(UnderworldLinks.ExitsToUnderworld(3, 0, -1));
            Assert.True(UnderworldLinks.ExitsToUnderworld(UnderworldLinks.Doom, 0, -1));
        }

        [U5Fact]
        public void TheAmuletAndTheShardsLieInTheUnderworld()
        {
            var all = UnderworldLinks.Objects(Env.Data.Ovl, false, _ => true);
            Assert.Equal((0x69, 0xe1), (all[0].x, all[0].y));
            Assert.Equal(LootKind.AmuletOfLordBritish, all[0].loot.Kind);
            Assert.Equal(new[] { (192, 80, 0), (130, 65, 1), (176, 184, 2) }, all.Skip(1).Select(o => (o.x, o.y, o.loot.Value)).ToArray());
            Assert.All(all.Skip(1), o => Assert.Equal(LootKind.Shard, o.loot.Kind));
            Assert.Equal(2, UnderworldLinks.Objects(Env.Data.Ovl, true, s => s != 1).Count);
            // the Underworld's hidden objects (z ≠ 0) are on its land
            var hidden = LootTables.Hidden(Env.Data.Ovl).Where(h => h.Location == 0 && h.Z != 0).ToList();
            Assert.NotEmpty(hidden);
            Assert.All(hidden, h => Assert.NotEqual(WaterKind.Sea, under.Value[Env.Data.Underworld[h.X, h.Y]].Water));
        }

        [U5Fact]
        public void UnderworldEncountersUseItsOwnTables()
        {
            var ovl = Env.Data.Ovl;
            Assert.Equal(3, Encounters.Rate(0x21, 12, underworld: true)); // even on a road, at noon
            var rnd = new Random(7);
            var land = new System.Collections.Generic.HashSet<int>();
            var water = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < 4000; i++)
            {
                int m = Encounters.Choose(ovl, 5, rnd, underworld: true);
                if (m != 0) land.Add(m);
                int w = Encounters.Choose(ovl, 1, rnd, underworld: true);
                if (w != 0) water.Add(w);
                Assert.Equal(Encounters.UnderSwamp, Encounters.Choose(ovl, 4, rnd, underworld: true));
            }
            Assert.Equal(new[] { 0x84, 0x88 }, water.OrderBy(x => x));
            Assert.Equal(new[] { 0x90, 0x94, 0x98, 0xd8, 0xdc, 0xf0, 0xf4 }, land.OrderBy(x => x));
            Assert.DoesNotContain(Encounters.Whirlpool, water);
            // the surface's deep sea brings whirlpools
            int pools = Enumerable.Range(0, 20000).Count(_ => Encounters.Choose(ovl, 1, rnd) == Encounters.Whirlpool);
            Assert.InRange(pools, 20000 / 4 / 8 / 2, 20000 / 4 / 8 * 2);
        }

        [Fact]
        public void EarthquakesAreRare()
        {
            var rnd = new Random(3);
            int n = Enumerable.Range(0, 256 * 400).Count(_ => UnderworldLinks.Earthquake(rnd));
            Assert.InRange(n, 300, 520);
        }
    }
}
