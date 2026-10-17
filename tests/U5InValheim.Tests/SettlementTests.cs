using System;
using System.IO;
using System.Linq;
using Xunit;
using U5InValheim.WorldGen.Core;

namespace U5InValheim.Tests
{
    public class SettlementTests
    {
        static string Mapping(string f) => File.ReadAllText(Path.Combine(Env.RepoRoot, "assets/mappings", f));
        static readonly Lazy<PieceGeometry> geo = new Lazy<PieceGeometry>(() => PieceGeometry.Parse(Mapping("piece_geometry.json")));
        static readonly Lazy<SettlementPlanner> planner = new Lazy<SettlementPlanner>(() =>
            new SettlementPlanner(Env.Data, TownTiles.Parse(Mapping("town_tiles.json")), geo.Value));
        static readonly Lazy<System.Collections.Generic.HashSet<string>> prefabs = new Lazy<System.Collections.Generic.HashSet<string>>(() =>
            new System.Collections.Generic.HashSet<string>(File.ReadAllLines(Path.Combine(Env.RepoRoot, "tools/data/valheim_prefabs.txt")).Where(l => !l.StartsWith("#"))));

        static SettlementPlan Plan(int location, string kind) => planner.Value.Plan(Env.Data.Location(location), kind, VBiome.BlackForest);

        [U5Fact]
        public void IolosHutHasToolsWorkbenchAndSpawn()
        {
            var p = Plan(13, "dwelling");
            var chest = p.Pieces.Single(x => x.Items != null);
            Assert.Equal("piece_chest_wood", chest.Prefab);
            Assert.Equal(new[] { "Hammer", "Hoe", "Cultivator" }, chest.Items);
            Assert.Single(p.Pieces, x => x.Prefab == "piece_workbench");
            Assert.True(p.HasSpawn);
            // the spawn point is inside the hut: under its roof
            Assert.Contains(p.Pieces, x => x.Prefab == "wood_roof" && Math.Abs(x.X - p.SpawnX) < 6 && Math.Abs(x.Z - p.SpawnZ) < 6);
            Assert.True(p.Pieces.Count(x => x.Prefab == "Pickable_Barley") >= 40, "the barley plot");
        }

        [U5Theory]
        [InlineData(13, "dwelling")]
        [InlineData(2, "towne")]
        [InlineData(17, "castle")]
        public void PlansUseOnlyRealPrefabsWithinTheSquare(int location, string kind)
        {
            var p = Plan(location, kind);
            Assert.All(p.Pieces, x => Assert.Contains(x.Prefab, prefabs.Value));
            Assert.All(p.Pieces, x => Assert.InRange(x.X, -p.HalfSize - 2, p.HalfSize + 2));
            Assert.All(p.Pieces, x => Assert.InRange(x.Z, -p.HalfSize - 2, p.HalfSize + 2));
            Assert.Contains(p.Pieces, x => x.Prefab.Contains("door"));
        }

        [U5Fact]
        public void BritainHasRoofsUpperFloorsAndItsTownWall()
        {
            var p = Plan(2, "towne");
            Assert.True(p.Pieces.Count(x => x.Prefab == "darkwood_roof") > 100, "shingle roofs");
            Assert.Contains(p.Pieces, x => x.Prefab == "wood_roof"); // the cobblestone cottages' thatch
            Assert.True(p.Pieces.Count(x => x.Prefab == "wood_floor" && x.Y > 2.5f) > 100, "upper floors");
            Assert.True(p.Pieces.Count(x => x.Prefab.StartsWith("stone_wall")) > 1000, "solid stone walls");
            Assert.Contains(p.Pieces, x => x.Prefab == "sign" && x.Text.Contains("PRIVATE"));
            Assert.True(p.HasWater, "the moat around the plaza");
            Assert.True(p.Pieces.Count(x => x.Climb) >= 2, "ladders between floors");
        }

        [U5Fact]
        public void CastlesHaveGraustenRoofsAndParapets()
        {
            var hold = Plan(32, "castle");
            Assert.True(hold.Pieces.Count(x => x.Prefab == "piece_grausten_roof_45") > 50);
            // hips are triangles turned upside down
            Assert.Contains(hold.Pieces, x => x.Prefab.StartsWith("piece_grausten_roof_45_corner") && x.SY < 0);
            // Lord British's castle has parapets on its walls, but where its upper floor draws roof
            // tiles (0x27/0x28) there is a roof
            var p = Plan(17, "castle");
            Assert.True(p.Pieces.Count(x => x.Prefab.StartsWith("piece_grausten_roof")) > 200);
            Assert.True(p.Pieces.Count(x => x.Prefab == "stone_wall_1x1" && Math.Abs((x.Y % 4 + 4) % 4 - 1.5f) < 0.01f) > 20, "merlons");
            Assert.Contains(p.Pieces, x => x.Prefab == "wood_door" && x.Lock != null);
        }

        [U5Theory]
        [InlineData(13, "dwelling", 1300)]
        [InlineData(2, "towne", 6000)]
        [InlineData(17, "castle", 8000)]
        public void PieceBudget(int location, string kind, int maxPieces)
        {
            var p = Plan(location, kind);
            Assert.InRange(p.Pieces.Count, 1, maxPieces);
            // runs are laid in 2 m bricks; 1 m bricks only at run ends and every other course's start
            int big = p.Pieces.Count(x => x.Prefab == "stone_wall_2x1"), small = p.Pieces.Count(x => x.Prefab == "stone_wall_1x1" && !x.Scaled);
            Assert.InRange(small, 0, Math.Max(20, (big + small) * 3 / 10)); // lintels over openings are often single bricks
        }

        [U5Theory]
        [InlineData(2, "towne")]
        [InlineData(17, "castle")]
        public void BricksNeverOverlap(int location, string kind)
        {
            // every 1 m cell of every course is filled by at most one brick
            var p = Plan(location, kind);
            var cells = new System.Collections.Generic.HashSet<(int, int, int)>();
            foreach (var b in p.Pieces.Where(x => (x.Prefab == "stone_wall_2x1" || x.Prefab == "stone_wall_1x1") && !x.Scaled))
            {
                bool wide = b.Prefab == "stone_wall_2x1";
                int y = (int)Math.Floor(b.Y);
                foreach (var d in wide ? new[] { -0.5f, 0.5f } : new[] { 0f })
                {
                    float x = b.X + (b.Yaw == 0 ? d : 0), z = b.Z + (b.Yaw == 0 ? 0 : d);
                    Assert.True(cells.Add(((int)Math.Floor(x), y, (int)Math.Floor(z))), $"{b} overlaps another brick");
                }
            }
        }

        [U5Theory]
        [InlineData(2)]
        [InlineData(17)]
        [InlineData(18)]
        [InlineData(31)]
        public void UpperFloorsAreOnlyWhatTheirLaddersReach(int location)
        {
            // upper maps repeat the ground below them; only the walk from the ladders and stairs is real
            string floors = planner.Value.DescribeFloors(Env.Data.Location(location));
            Assert.DoesNotContain("whole map", floors);
            var upper = floors.Split("floor ").Skip(2).ToList();
            Assert.NotEmpty(upper);
            Assert.All(upper, f => Assert.Contains("o", f));
            // the copied streets of Britain's ground floor are not on its upper floor
            if (location == 2) Assert.True(upper[0].Count(ch => ch == 'o') < 400);
        }

        [Fact]
        public void SignTextDropsTheFrame()
        {
            Assert.Equal("PRIVATE\nCHAMBERS\nDO NOT ENTER",
                SettlementPlanner.SignText("abbbbbbbbbbbbbc\ng   PRIVATE   g\ng  CHAMBERS   g\ngDO@NOT@ENTER g\ndeeeeeeeeeeeeef"));
        }

        [U5Fact]
        public void SitesFlattenAndPaintTheirSquare()
        {
            var shape = new BritanniaShape(Env.Data.Britannia, TerrainTable.Parse(Mapping("terrain.json")), seed: 3);
            var p = Plan(2, "towne");
            var loc = Env.Data.Location(2);
            shape.TileCentre(loc.X, loc.Y, out float cx, out float cz);
            var site = new SettlementSite { Location = 2, CentreX = cx, CentreZ = cz, Half = p.HalfSize, Tile = p.Tile, Base = 1.5f, Paint = p.GroundPaint, Water = p.Water };
            shape.SetSites(new[] { site });
            int paved = 0;
            for (int i = 0; i < 32 * 32; i++)
            {
                float wx = cx - site.Half + (i % 32 + 0.5f) * 2, wz = cz + site.Half - (i / 32 + 0.5f) * 2;
                float h = shape.Height(wx, wz);
                if (p.Water[i]) Assert.True(h < 0, "water cells below sea level");
                else Assert.Equal(1.5f, h);
                Assert.Equal(p.GroundPaint[i], shape.PaintAt(wx, wz));
                if (p.GroundPaint[i] == Paint.Paved) paved++;
            }
            Assert.True(paved > 100);
        }
    }

    public sealed class U5TheoryAttribute : TheoryAttribute
    {
        public U5TheoryAttribute()
        {
            if (!Env.HaveU5) Skip = $"Ultima V files not found in '{Env.U5Dir}' (set U5_DIR)";
        }
    }
}
