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
            Assert.Contains(p.Pieces, x => x.Prefab == "ashwood_door" && x.Lock != null);
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

        [U5Fact]
        public void WallWalksBetweenParapetsAreOpenToTheSky()
        {
            // CASTLE.DAT: floor 2's wall-walks (0x45, black marble) run between parapets; nothing
            // may be built over them (they once got a doorway lintel a storey up)
            var loc = Env.Data.Location(17);
            var raw = Env.Data.Maps.Floor(loc, 2);
            var p = Plan(17, "castle");
            int walks = 0;
            for (int c = 0; c < 32 * 32; c++)
            {
                if (raw[c] != 0x45) continue;
                walks++;
                float x = p.LocalX(c % 32), z = p.LocalZ(c / 32);
                Assert.DoesNotContain(p.Pieces, pc => pc.Y > 2 * p.Storey + 0.5f && Math.Abs(pc.X - x) < 0.9f && Math.Abs(pc.Z - z) < 0.9f
                                                      && !pc.Prefab.StartsWith("piece_grausten"));
            }
            Assert.True(walks > 100, $"{walks} wall-walk tiles");
        }

        [U5Theory]
        [InlineData(2, "towne", 3)]   // Britain: two posts each side of the street, one between two posts
        [InlineData(4, "towne", 4)]   // Yew
        [InlineData(30, "keep", 7)]   // the Lycaeum: indoor shops on three floors, hung on their walls
        public void ShopSignsHangTheirGuildIcon(int location, string kind, int icons)
        {
            var p = Plan(location, kind);
            var banners = p.Pieces.Where(x => x.Look != null && x.Look.StartsWith("icon:")).ToList();
            Assert.Equal(icons, banners.Count);
            foreach (var b in banners)
            {
                int tile = Convert.ToInt32(b.Look.Substring(5), 16);
                Assert.InRange(tile, 0xf0, 0xf9);
                Assert.NotEqual(0xf8, tile);
                Assert.Equal("piece_banner01", b.Prefab);
                // the cloth hangs ~2.9 m under its rod: clear of the floor it hangs over
                float floor = (float)Math.Floor(b.Y / p.Storey + 0.01) * p.Storey;
                Assert.InRange(b.Y - floor, 3.5f, 4f);
                // nothing stands where it hangs (a post under a banner between two posts is left out)
                Assert.DoesNotContain(p.Pieces, x => x.Prefab.StartsWith("wood_pole") && Math.Abs(x.X - b.X) < 0.5f && Math.Abs(x.Z - b.Z) < 0.2f);
            }
        }

        [U5Fact]
        public void BannersPrintTheGuildIconsFromTilesDotSixteen()
        {
            foreach (int tile in new[] { 0xf0, 0xf1, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7, 0xf9 })
            {
                var rgba = BannerArt.Render(Env.Data.Tiles, tile);
                Assert.Equal(BannerArt.Width * BannerArt.Height * 4, rgba.Length);
                // every pixel of the icon is on the banner, doubled
                int left = (BannerArt.Width - 32) / 2, top = 22, same = 0;
                for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    int rgb = Data.TileSet.EgaPalette[Env.Data.Tiles.Pixel(tile, x, y)];
                    int o = ((top + 2 * y + 1) * BannerArt.Width + left + 2 * x + 1) * 4;
                    if (rgba[o] == (byte)(rgb >> 16) && rgba[o + 1] == (byte)(rgb >> 8) && rgba[o + 2] == (byte)rgb) same++;
                }
                Assert.Equal(256, same);
                // the icons are pictures, not blank tiles
                Assert.True(Env.Data.Tiles.Pixels(tile).Distinct().Count() >= 3, $"tile {tile:x2}");
            }
        }

        [U5Fact]
        public void RiverWaterIndoorsIsATroughOutdoorsShallowForLilies()
        {
            var tt = TownTiles.Parse(Mapping("town_tiles.json"));
            int troughs = 0, shallow = 0;
            foreach (int location in new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10 })
            {
                var loc = Env.Data.Location(location);
                var raw = Env.Data.Maps.Floor(loc, Math.Max(0, loc.MinZ));
                var p = Plan(location, "towne");
                                for (int c = 0; c < 32 * 32; c++)
                {
                    if (!tt[raw[c]].River) continue;
                    float x = p.LocalX(c % 32), z = p.LocalZ(c / 32);
                    bool bucket = p.Pieces.Any(pc => pc.Prefab == "forge_ext5" && Math.Abs(pc.X - x) < 0.9f && Math.Abs(pc.Z - z) < 0.9f);
                    if (p.Water[c])
                    {
                        Assert.True(p.Shallow[c]);
                        Assert.False(bucket);
                        shallow++;
                    }
                    else
                    {
                        Assert.True(bucket, $"{loc.Name} ({c % 32}, {c / 32}): a quenching bucket in the trough");
                        troughs++;
                    }
                }
                if (location == 2)
                    // Britain's smithy: three trough tiles inside, floored and roofed like the rest of it
                    Assert.Equal(3, p.Pieces.Count(pc => pc.Prefab == "forge_ext5"));
            }
            Assert.True(troughs >= 3 && shallow > 20, $"{troughs} troughs, {shallow} shallow river tiles");
            // a pond for ducks: its water within a metre of the ground, deep enough for the lilies (0.25..1.6 m)
            Assert.InRange(BritanniaShape.PondBed, -1.6f, -0.25f);
            Assert.InRange(BritanniaShape.WaterSiteBase, 0.35f, 1f);
        }

        [U5Fact]
        public void DoorsAreTallLeavesAndDoubleDoorsOpenFromTheMiddle()
        {
            var p = Plan(2, "towne");
            Assert.DoesNotContain(p.Pieces, pc => pc.Prefab == "wood_door");
            Assert.Contains(p.Pieces, pc => pc.Prefab == "ashwood_door");
            // Britain's smithy: the double door (0xba 0xba) at (3, 25) and (4, 25) is two frameless
            // leaves, each hinged (its local +x) on its outer edge
            float z = p.LocalZ(25);
            var leaves = p.Pieces.Where(pc => (pc.Prefab.Contains("door") || pc.Prefab.Contains("gate")) && Math.Abs(pc.Z - z) < 1.1f && pc.Y < 2
                                              && pc.X > p.LocalX(2) && pc.X < p.LocalX(5)).OrderBy(pc => pc.X).ToList();
            Assert.Equal(2, leaves.Count);
            Assert.All(leaves, pc => Assert.Equal("wood_gate", pc.Prefab));
            Assert.Equal(p.LocalX(3), leaves[0].X, 0.01f);
            Assert.Equal(180f, leaves[0].Yaw, 0.1f);
            Assert.Equal(p.LocalX(4), leaves[1].X, 0.01f);
            Assert.Equal(0f, leaves[1].Yaw, 0.1f);
        }

        [U5Theory]
        [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(24)] [InlineData(26)] [InlineData(32)]
        public void ArmourersDisplayTheirWaresBehindTheCounter(int location)
        {
            var p = Plan(location, "towne");
            var shown = p.Pieces.Where(pc => pc.Prefab == "itemstand" && pc.StandItem != null && !pc.StandItem.StartsWith("Trophy")).ToList();
            Assert.True(shown.Count >= 5, $"{shown.Count} items on the wall");
            // one wall: all at the same distance along it from the room
            Assert.True(shown.Select(pc => pc.Yaw % 360).Distinct().Count() == 1);
            if (location == 2)
            {
                // Iolo's Bows: a wall of different bows behind the counter (the north wall of row 17)
                Assert.True(shown.Count(pc => pc.StandItem.StartsWith("Bow") || pc.StandItem.StartsWith("Crossbow")) >= 8);
                Assert.True(shown.Select(pc => pc.StandItem).Distinct().Count() >= 8);
                Assert.All(shown, pc => Assert.Equal(p.LocalZ(18) + 0.94f, pc.Z, 0.01f));
                // staggered: the upper row hangs between the lower row's items
                var low = shown.Where(pc => pc.Y < 2).Select(pc => pc.X).OrderBy(x => x).ToList();
                var high = shown.Where(pc => pc.Y > 2).Select(pc => pc.X).OrderBy(x => x).ToList();
                Assert.Equal(low.Count - 1, high.Count);
                for (int i = 0; i < high.Count; i++) Assert.Equal((low[i] + low[i + 1]) / 2, high[i], 0.01f);
            }
        }

        [U5Fact]
        public void SmithiesHaveBellowsAndRaisedBraziers()
        {
            var p = Plan(2, "towne");
            // the bellows (0xfc) blow into the brazier west of them
            Assert.Contains(p.Pieces, pc => pc.Prefab == "forge_ext1" && pc.Yaw == 180);
            Assert.DoesNotContain(p.Pieces, pc => pc.Prefab == "forge_ext6");
            Assert.All(p.Pieces.Where(pc => pc.Prefab == "piece_brazierfloor01"), pc => Assert.True(pc.Y % p.Storey >= 0.9f));
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
