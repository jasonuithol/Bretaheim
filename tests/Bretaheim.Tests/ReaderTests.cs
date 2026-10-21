using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Bretaheim.Data;

namespace Bretaheim.Tests
{
    public class LzwTests
    {
        [U5Fact]
        public void EveryLzwFileMatchesTheRustPort()
        {
            var rows = Env.Golden("rustref.tsv").Where(r => r[0] == "lzw").ToList();
            Assert.NotEmpty(rows);
            foreach (var r in rows)
            {
                var output = Lzw.DecompressFile(File.ReadAllBytes(U5Data.FindFile(Env.U5Dir, r[1])));
                Assert.Equal(int.Parse(r[2]), output.Length);
                Assert.Equal(r[3], Env.Fnv1a64(output).ToString("x16"));
            }
        }

        [Fact]
        public void RoundTripOfAHandMadeStream()
        {
            // codes 'A' 'B' 258('AB') END, 9 bits each, LSB first
            int[] codes = { 'A', 'B', 258, 257 };
            var bytes = new List<byte>();
            ulong acc = 0; int n = 0;
            foreach (int c in codes) { acc |= (ulong)c << n; n += 9; while (n >= 8) { bytes.Add((byte)acc); acc >>= 8; n -= 8; } }
            if (n > 0) bytes.Add((byte)acc);
            Assert.Equal("ABAB", new string(Lzw.Decompress(bytes.ToArray()).Select(b => (char)b).ToArray()));
        }
    }

    public class WorldTests
    {
        [U5Fact]
        public void BritanniaMatchesGenerateTerrain()
        {
            var row = Env.Golden("pyref.tsv").Single(r => r[0] == "brit");
            var tiles = Env.Data.Britannia.Tiles;
            Assert.Equal(int.Parse(row[2]), tiles.Length);
            Assert.Equal(row[3], Env.Fnv1a64(tiles).ToString("x16"));
        }

        [U5Fact]
        public void LocationCoordinatesMatchGenerateTerrain()
        {
            foreach (var r in Env.Golden("pyref.tsv").Where(r => r[0] == "location"))
            {
                var loc = Env.Data.Location(int.Parse(r[1]));
                Assert.Equal((int.Parse(r[2]), int.Parse(r[3])), (loc.X, loc.Y));
            }
        }

        [U5Fact]
        public void TheMapWraps()
        {
            var w = Env.Data.Britannia;
            Assert.Equal(w[0, 0], w[256, -256]);
            Assert.Equal(w[255, 3], w[-1, 259]);
        }

        [U5Fact]
        public void SettlementsSitOnTheirOverworldTile()
        {
            // Every town/dwelling/castle/keep stands on a settlement-type tile (hut, keep, village,
            // towne, castle, lighthouse, ...) or a castle-wall tile, in Britannia or the Underworld.
            var settlementTiles = new HashSet<int> { 0x10, 0x12, 0x13, 0x14, 0x15, 0x1b, 0x39, 0x3a, 0x3b, 0x3c, 0x3d, 0x3e, 0x3f };
            foreach (var loc in Env.Data.Locations.Where(l => l.IsSettlement))
            {
                var world = loc.InUnderworld ? Env.Data.Underworld : Env.Data.Britannia;
                int t = world[loc.X, loc.Y];
                Assert.True(settlementTiles.Contains(t), $"{loc} stands on tile 0x{t:x2}");
            }
            Assert.True(Env.Data.Location(25).InUnderworld); // Ararat
            Assert.False(Env.Data.Location(2).InUnderworld);
        }

        [U5Fact]
        public void TileNamesAreReadable()
        {
            Assert.Equal("deep water", Env.Data.TileNames[1]);
            Assert.Equal("a ladder", Env.Data.TileNames[SettlementMaps.TileLadderUp]);
        }
    }

    public class LocationTests
    {
        [U5Fact]
        public void FortyLocationsWithNames()
        {
            var locs = Env.Data.Locations;
            Assert.Equal(40, locs.Length);
            Assert.All(locs, l => Assert.False(l.Name.StartsWith("Location ")));
            Assert.Equal("Britain", Env.Data.Location(2).Name);
            Assert.Equal("Iolo's Hut", Env.Data.Location(13).Name);
            Assert.Equal("Doom", Env.Data.Location(40).Name);
        }

        [U5Fact]
        public void FloorsPartitionEachMapFile()
        {
            // Following the ladders must assign each of the 16 maps of a file to exactly one place.
            var d = Env.Data;
            for (int f = 0; f < 4; f++)
            {
                var used = new int[SettlementMaps.MapsPerFile];
                foreach (var l in d.Locations.Where(l => (int)l.File == f))
                    for (int z = l.MinZ; z <= l.MaxZ; z++) used[l.MapIndex(z)]++;
                Assert.All(used, c => Assert.Equal(1, c));
            }
        }

        [U5Fact]
        public void KnownFloorLayouts()
        {
            var d = Env.Data;
            Assert.Equal((-1, 3), (d.Location(17).MinZ, d.Location(17).MaxZ)); // Lord British's castle
            Assert.Equal((0, 0), (d.Location(13).MinZ, d.Location(13).MaxZ));  // Iolo's hut
            Assert.Equal((-1, 1), (d.Location(32).MinZ, d.Location(32).MaxZ)); // Serpent's Hold
        }
    }

    public class NpcTests
    {
        [U5Fact]
        public void EveryConversationNpcHasAScript()
        {
            var d = Env.Data;
            foreach (var loc in d.Locations.Where(l => l.IsSettlement))
                foreach (var n in d.NpcsOf(loc).Where(n => n.Slot > 0 && n.HasConversation))
                {
                    var s = d.ScriptOf(loc, n);
                    Assert.True(s != null, $"{loc.Name} slot {n.Slot} dialog {n.Dialog} has no .TLK entry");
                    Assert.False(string.IsNullOrWhiteSpace(d.NpcName(loc, n)));
                }
        }

        [U5Fact]
        public void EveryScriptIsUsedByAnNpc()
        {
            var d = Env.Data;
            for (int f = 0; f < 4; f++)
            {
                var used = new HashSet<int>(d.Locations.Where(l => (int)l.File == f)
                    .SelectMany(l => d.NpcsOf(l).Where(n => n.Slot > 0 && n.HasConversation).Select(n => (int)n.Dialog)));
                Assert.All(d.Talk[f].Npcs, n => Assert.Contains(n, used));
            }
        }

        [U5Fact]
        public void SchedulesStayOnTheMap()
        {
            var d = Env.Data;
            foreach (var loc in d.Locations.Where(l => l.IsSettlement))
                foreach (var n in d.NpcsOf(loc).Where(n => n.Slot > 0 && n.Exists))
                    for (int k = 0; k < 3; k++)
                    {
                        Assert.InRange(n.Schedule.X[k], 0, 31);
                        Assert.InRange(n.Schedule.Y[k], 0, 31);
                        Assert.InRange(n.Schedule.Z[k], loc.MinZ, loc.MaxZ);
                        Assert.All(n.Schedule.Times, t => Assert.InRange(t, 0, 23));
                    }
        }

        [Fact]
        public void ScheduleHourChoiceFollowsTheOriginal()
        {
            // Britain's Justin: hours 23, 9, 18, 19 -> places 0, 1, 2, 1
            var s = new NpcSchedule { Times = new byte[] { 23, 9, 18, 19 } };
            Assert.Equal(0, s.PlaceAt(23));
            Assert.Equal(0, s.PlaceAt(3));   // still the 23:00 entry after midnight
            Assert.Equal(1, s.PlaceAt(9));
            Assert.Equal(1, s.PlaceAt(17));
            Assert.Equal(2, s.PlaceAt(18));
            Assert.Equal(1, s.PlaceAt(19));  // the 4th hour sends the NPC back to place 1
            // equal hours: the earlier entry wins
            var t = new NpcSchedule { Times = new byte[] { 0, 0, 0, 0 } };
            Assert.Equal(0, t.PlaceAt(12));
        }

        [U5Fact]
        public void PeopleAreCreatureTypes()
        {
            var d = Env.Data;
            var britain = d.NpcsOf(d.Location(2));
            Assert.Equal("Villager", d.Ovl.CreatureName(britain[4].CreatureType)); // Justin the cook
            Assert.Equal("Child", d.Ovl.CreatureName(britain[5].CreatureType));    // Eb the busboy
        }
    }

    public class TlkTests
    {
        [U5Fact]
        public void AllScriptsParse()
        {
            var d = Env.Data;
            int scripts = 0;
            foreach (var f in d.Talk)
                foreach (var n in f.Npcs)
                {
                    var s = f[n];
                    Assert.NotEmpty(s.Name);
                    // Job may be empty: scripted NPCs (Blackthorn, Stillwelt, ...) jump from their
                    // description straight to label 1. They then must have labels.
                    if (s.Job.Length == 0) Assert.NotEmpty(s.Labels);
                    // Keywords are words of up to 7 letters ("an ylem"); the only exception is the
                    // shifted text of Mario (TOWNE.TLK 23), whose stray NUL turns answer text into keywords.
                    if (!(f == d.Talk[0] && n == 23))
                        Assert.All(s.Keywords, k => Assert.All(k.Keys, key => Assert.InRange(key.Length, 1, 7)));
                    scripts++;
                }
            Assert.Equal(135, scripts); // TOWNE + DWELLING + CASTLE + KEEP .TLK entries
        }

        [U5Fact]
        public void EveryGotoTargetsAnExistingLabel()
        {
            var d = Env.Data;
            foreach (var f in d.Talk)
                foreach (var n in f.Npcs)
                {
                    var s = f[n];
                    var labels = new HashSet<int>(s.Labels.Select(l => l.Number));
                    foreach (var text in AllTexts(s))
                        for (int i = 0; i < text.Length; i++)
                        {
                            byte b = text[i];
                            int argc = TlkOp.ArgCount(b);
                            if (b == TlkOp.IfMetGoto && i + 1 < text.Length && text[i + 1] != 0xff)
                                Assert.Contains(text[i + 1] - 0x90, labels);
                            else if (b > TlkOp.LabelMark && b < TlkOp.EndLabel)
                                Assert.Contains(b - 0x90, labels);
                            i += argc;
                        }
                }
        }

        static IEnumerable<byte[]> AllTexts(TlkScript s)
        {
            yield return s.Greeting; yield return s.Job; yield return s.Bye;
            foreach (var k in s.Keywords) yield return k.Answer;
            foreach (var l in s.Labels)
            {
                yield return l.Question;
                if (l.DefaultAnswer != null) yield return l.DefaultAnswer;
                foreach (var k in l.Keywords) yield return k.Answer;
            }
        }

        [U5Fact]
        public void JustinTheCook()
        {
            var d = Env.Data;
            var britain = d.Location(2);
            var justin = d.NpcsOf(britain)[4];
            var s = d.ScriptOf(britain, justin);
            Assert.Equal("Justin", d.NpcName(britain, justin));
            Assert.Equal("I am the cook, of course!", d.RenderTlk(s.Job).Substring(5));
            Assert.Contains(s.Keywords, k => k.Keys.SequenceEqual(new[] { "chef", "cook" }));
            var bite = s.Labels.Single(l => l.Number == 1);
            Assert.Equal("Wouldst thou like to try a bite?", d.RenderTlk(bite.Question));
            Assert.StartsWith("<give 0x41>", d.RenderTlk(bite.Keywords.Single(k => k.Keys[0] == "y").Answer));
        }

        [Fact]
        public void WordsGetSpacesAroundThem()
        {
            // 0x01 = a compressed word; text bytes are ASCII | 0x80
            byte[] b = { 'A' | 0x80, 0x01, 'B' | 0x80, TlkOp.NewLine, 'C' | 0x80 };
            Assert.Equal("A the B\nC", TlkText.Render(b, c => c == 1 ? "the" : null));
        }
    }

    public class DungeonTests
    {
        [U5Fact]
        public void EveryDungeonHasAWayIn()
        {
            // Level 0 is entered by a ladder up, or (Destard, Shame, Doom) in a room.
            var d = Env.Data;
            for (int dn = 0; dn < DungeonMaps.Dungeons; dn++)
            {
                var cells = Enumerable.Range(0, 64).Select(i => d.Dungeons.Cell(dn, 0, i % 8, i / 8).Kind).ToList();
                Assert.Contains(cells, k => k == DungeonCellKind.LadderUp || k == DungeonCellKind.LadderUpDown || k == DungeonCellKind.Room);
            }
        }

        [U5Fact]
        public void CombatMapCounts()
        {
            Assert.Equal(112, Env.Data.DungeonRooms.Count);
            Assert.Equal(16, Env.Data.BritCombatMaps.Count);
        }
    }

    public class SaveGameTests
    {
        [U5Fact]
        public void NewGameStartsInIolosHut()
        {
            var g = Env.Data.InitialGame;
            Assert.Equal(13, g.Location);
            Assert.Equal("Iolo's Hut", Env.Data.Location(g.Location).Name);
            Assert.Equal(3, g.PartySize);
            Assert.Equal("Shamino", g.Character(1).Name);
            Assert.Equal("Iolo", g.Character(2).Name);
            Assert.InRange(g.Karma, 0, 99);
        }
    }

    public class DumpTests
    {
        [U5Fact]
        public void EveryDumpRuns()
        {
            var d = Env.Data;
            string[][] cmds =
            {
                new[] { "locations" }, new[] { "map", "Britain" }, new[] { "map", "17", "-1" }, new[] { "npcs", "Britain" },
                new[] { "talk", "Britain", "4" }, new[] { "items" }, new[] { "monsters" }, new[] { "spells" },
                new[] { "reagents" }, new[] { "virtues" }, new[] { "words" }, new[] { "dungeon", "0", "0" },
                new[] { "room", "0" }, new[] { "world", "70", "100" }, new[] { "tiles" }, new[] { "signs" }, new[] { "init" },
            };
            foreach (var c in cmds)
            {
                var text = Dump.Run(d, c);
                Assert.False(text.StartsWith("error") || text.StartsWith("unknown"), $"{string.Join(" ", c)}: {text}");
            }
        }
    }
}
