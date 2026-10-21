using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bretaheim.Data;
using Bretaheim.WorldGen.Core;
using Xunit;

namespace Bretaheim.Tests
{
    /// <summary>The dungeons as built from DUNGEON.DAT and DUNGEON.CBT (MASTER_PLAN §5.7).</summary>
    public class DungeonBuildTests
    {
        static readonly Lazy<DungeonPlanner> planner = new Lazy<DungeonPlanner>(() => new DungeonPlanner(Env.Data,
            TownTiles.Parse(File.ReadAllText(Path.Combine(Env.RepoRoot, "assets/mappings/town_tiles.json"))),
            PieceGeometry.Parse(File.ReadAllText(Path.Combine(Env.RepoRoot, "assets/mappings/piece_geometry.json")))));

        [Fact]
        public void RoomSetsFollowTheOriginalsIndexQuirk()
        {
            // enter_dungeon_room: location − 0x21 for Deceit, location − 0x22 after it
            Assert.Equal(0, DungeonInfo.RoomSet(0));
            Assert.Equal(0, DungeonInfo.RoomSet(1));
            Assert.Equal(1, DungeonInfo.RoomSet(2));
            Assert.Equal(6, DungeonInfo.RoomSet(7));
            Assert.Equal(16 * 6 + 15, DungeonInfo.RoomIndex(7, 15));
            Assert.Equal(DungeonStyle.Crypt, DungeonInfo.Style(0));
            Assert.Equal(DungeonStyle.Cave, DungeonInfo.Style(1));
            Assert.Equal(DungeonStyle.Mine, DungeonInfo.Style(5));
            Assert.Equal(DungeonStyle.Cave, DungeonInfo.Style(7));
        }

        [U5Fact]
        public void TheFileHoldsSevenRoomSets() => Assert.Equal(7 * 16, Env.Data.DungeonRooms.Count);

        [U5Fact]
        public void PersistentRoomsAreWrongsAndCovetouss()
        {
            var ovl = Env.Data.Ovl;
            var found = new List<(int, int)>();
            for (int d = 0; d < 8; d++)
            for (int r = 0; r < 16; r++)
                if (DungeonInfo.Persistent(ovl, d, r)) found.Add((d, r));
            Assert.Equal(new[] { (3, 1), (3, 6), (3, 11), (3, 12), (4, 0), (4, 11) }, found.OrderBy(x => x).ToArray());
        }

        [U5Fact]
        public void WordsOfPowerOpenTheirDungeon()
        {
            var ovl = Env.Data.Ovl;
            Assert.Equal(0, DungeonInfo.WordIn(ovl, "fallax"));
            Assert.Equal(2, DungeonInfo.WordIn(ovl, "I shout INOPIA!"));
            Assert.Equal(7, DungeonInfo.WordIn(ovl, "Veramocor"));
            Assert.Equal(-1, DungeonInfo.WordIn(ovl, "open sesame"));
            // the entrances open as caves, mines and dungeons; every dungeon starts sealed (INIT.GAM)
            Assert.Equal(new byte[] { 0x18, 0x16, 0x16, 0x18, 0x18, 0x17, 0x17 }, Enumerable.Range(0, 7).Select(d => DungeonInfo.OpenTile(ovl, d)).ToArray());
            for (int d = 0; d < 8; d++) Assert.False(Env.Data.InitialGame.DungeonOpen(d));
            // and their entrances on the map are those tiles
            for (int d = 0; d < 7; d++)
            {
                var l = Env.Data.Location(DungeonInfo.Location(d));
                Assert.Equal(DungeonInfo.OpenTile(ovl, d), Env.Data.Britannia[l.X, l.Y]);
            }
        }

        [U5Fact]
        public void HallwaysAreTheOriginalsCombatMaps()
        {
            // a hallway with open cells north and south and walls east and west: a 7-wide way through
            byte[] t = DungeonPlanner.Hallway(0x00, d => d == 0 || d == 2 ? (byte)0x00 : (byte)0xb0, 0x4f, 0x45, out int line);
            Assert.Equal(-1, line);
            string Row(int y) => string.Concat(Enumerable.Range(0, 11).Select(x => t[y * 11 + x] == 0xff ? '#' : t[y * 11 + x] == 0x4f ? 'w' : '.'));
            Assert.Equal("#w.......w#", Row(0));
            Assert.Equal("#w.......w#", Row(1));
            Assert.Equal("#w.......w#", Row(5));
            Assert.Equal("#w.......w#", Row(10));
            // a ladder up in the middle of its cell
            t = DungeonPlanner.Hallway(0x10, d => 0xb0, 0x4f, 0x45, out _);
            Assert.Equal(0xc8, t[5 * 11 + 5]);
            Assert.Equal("###########", Row(0));
            Assert.Equal("#wwwwwwwww#", Row(1));
            Assert.Equal("#w...c...w#".Replace('c', '.'), Row(4));
            // a door between walls north and south: the passage runs east-west, the door in a wall across it
            t = DungeonPlanner.Hallway(0xe0, d => d == 0 || d == 2 ? (byte)0xb0 : (byte)0x00, 0x4f, 0x45, out line);
            Assert.Equal(0, line);
            Assert.Equal(0xb8, t[5 * 11 + 5]);
            Assert.Equal(0x4f, t[2 * 11 + 5]);
            Assert.Equal(0x4f, t[8 * 11 + 5]);
            Assert.Equal(0x45, t[5 * 11 + 0]);
        }

        [U5Fact]
        public void EveryLevelIsBuilt()
        {
            int ladders = 0, chests = 0, rooms = 0;
            for (int d = 0; d < 8; d++)
            {
                var cells = planner.Value.InitialCells(d);
                for (int z = 0; z < 8; z++)
                {
                    var plan = planner.Value.Level(d, z, cells);
                    ladders += plan.Features.Count(f => f.Kind == DFeatureKind.LadderUp || f.Kind == DFeatureKind.LadderDown);
                    chests += plan.Features.Count(f => f.Kind == DFeatureKind.Chest);
                    for (int i = 0; i < 64; i++) if ((plan.CellByIndex(i) & 0xf0) == 0xf0) rooms++;
                    // the level wraps: a tile one level-width away is the same tile
                    for (int k = 0; k < 50; k++)
                    {
                        int u = (k * 37) % 88, v = (k * 53) % 88;
                        Assert.Equal(plan.TileAt(u, v), plan.TileAt(u + 88, v - 88));
                    }
                }
            }
            Assert.True(ladders > 100, $"{ladders} ladders");
            Assert.Equal(3, chests); // DUNGEON.DAT has three hallway chests
            Assert.True(rooms > 50, $"{rooms} room cells");
        }

        [U5Fact]
        public void TheStartIsOpenFloor()
        {
            // enter_dungeon puts the party at (1, 1) of level 0: open ground in every dungeon
            for (int d = 0; d < 8; d++)
            {
                var plan = planner.Value.Level(d, 0, planner.Value.InitialCells(d));
                var (u, v) = DungeonLevelPlan.CellCentre(DungeonInfo.StartY * 8 + DungeonInfo.StartX);
                Assert.True(DungeonTiles.Open(plan.ClassAt(u, v + 1)), $"dungeon {d}");
            }
            // Deceit is left by the ladder up at its start; Destard has none on level 0, only the hole above its start (a grapple)
            var deceit = planner.Value.Level(0, 0, planner.Value.InitialCells(0));
            Assert.Contains(deceit.Features, f => f.Kind == DFeatureKind.LadderUp && f.Cell == 9);
            var destard = planner.Value.Level(2, 0, planner.Value.InitialCells(2));
            Assert.DoesNotContain(destard.Features, f => f.Kind == DFeatureKind.LadderUp);
            Assert.Contains(destard.Features, f => f.Kind == DFeatureKind.Grapple && f.Cell == 9);
            Assert.True(destard.CeilingHole(16, 16));
        }

        [U5Fact]
        public void RoomsHaveTheirMonstersObjectsAndFurniture()
        {
            var ovl = Env.Data.Ovl;
            // Destard's room 0 (set 1): orcs and a troll round a camp fire, a chest of quality 7 on level 0
            var monsters = new List<RoomMonster>();
            var objects = new List<RoomObject>();
            DungeonInfo.RoomContents(ovl, planner.Value.Room(2, 0), 0, new Random(1), monsters, objects);
            Assert.Equal(new[] { "Orc", "Troll", "Orc", "Orc", "Giant Rat" }, monsters.Select(m => ovl.CreatureName(m.Type)).ToArray());
            Assert.Contains(objects, o => o.Loot.Kind == LootKind.Chest && o.Loot.Value == 7);
            Assert.Contains(objects, o => o.Loot.Kind == LootKind.Gold && o.Loot.Count >= 1 && o.Loot.Count <= 10);
            // its floor grate leads down; its spit is furniture
            var plan = planner.Value.Level(2, 0, planner.Value.InitialCells(2));
            var roomCell = 1 * 8 + 3;
            Assert.Contains(plan.Features, f => f.Cell == roomCell && f.Kind == DFeatureKind.LadderDown && f.Value == 0x86);
            Assert.Contains(plan.Features, f => f.Cell == roomCell && f.Kind == DFeatureKind.Prop && f.Piece.Prefab == "fire_pit");
            // random monsters (0xec..0xef) are among the room table's types
            int randomTypes = 0;
            for (int r = 0; r < 112; r++)
            {
                monsters.Clear();
                objects.Clear();
                DungeonInfo.RoomContents(ovl, Env.Data.DungeonRooms[r], 3, new Random(r), monsters, objects);
                foreach (var m in monsters) Assert.InRange(m.Type, 0, 47);
                randomTypes += monsters.Count(m => new[] { 20, 21, 22, 34, 33, 24, 31 }.Contains(m.Type));
            }
            Assert.True(randomTypes > 0);
        }

        [U5Fact]
        public void TriggersChangeRoomTiles()
        {
            int withTriggers = 0;
            foreach (var map in Env.Data.DungeonRooms)
            {
                var tr = DungeonInfo.Triggers(map);
                if (tr.Count > 0) withTriggers++;
                foreach (var t in tr) { Assert.InRange(t.X, 0, 10); Assert.InRange(t.Y, 0, 10); }
            }
            Assert.True(withTriggers > 20, $"{withTriggers} rooms with triggers");
            // a change applies to the level's tiles
            int d = 0, z = 0;
            var cells = planner.Value.InitialCells(d);
            int cell = Enumerable.Range(0, 512).First(i => (cells[i] & 0xf0) == 0xf0);
            z = cell / 64;
            int ci = cell % 64;
            var changed = new Dictionary<(int, int, int, int), byte> { [(z, ci, 5, 5)] = 0x8f };
            var plan = planner.Value.Level(d, z, cells, changed);
            var (u, v) = DungeonLevelPlan.CellCentre(ci);
            Assert.Equal(DTile.Lava, plan.ClassAt(u, v));
        }

        [U5Fact]
        public void DungeonChestsHoldTheDungeonTable()
        {
            var rnd = new Random(3);
            var kinds = new HashSet<LootKind>();
            for (int i = 0; i < 500; i++)
                foreach (var l in LootTables.DungeonChest(Env.Data.Ovl, 7, rnd))
                {
                    kinds.Add(l.Kind);
                    if (l.Kind == LootKind.Gold) Assert.InRange(l.Count, 1, 56);
                    if (l.Kind == LootKind.Potion || l.Kind == LootKind.Scroll) Assert.InRange(l.Value, 0, 7);
                }
            foreach (var k in new[] { LootKind.Food, LootKind.Gold, LootKind.Keys, LootKind.Gems, LootKind.Torches, LootKind.Potion, LootKind.Scroll })
                Assert.Contains(k, kinds);
        }

        [Fact]
        public void LocalCoordinatesRoundTrip()
        {
            var (x, z) = DungeonLevelPlan.Local(0, 0);
            Assert.Equal(-87f, x);
            Assert.Equal(87f, z);
            var (u, v) = DungeonLevelPlan.TileAtLocal(x, z);
            Assert.Equal(0, u, 3);
            Assert.Equal(0, v, 3);
            Assert.Equal(1, DungeonLevelPlan.CellOf(12, 5, out int lx, out int ly));
            Assert.Equal((1, 5), (lx, ly));
            Assert.Equal(63, DungeonLevelPlan.CellOf(-1, -1, out lx, out ly));
            Assert.Equal((10, 10), (lx, ly));
        }
    }
}
