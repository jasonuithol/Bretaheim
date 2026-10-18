using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using U5InValheim.WorldGen.Core;

namespace U5InValheim.Tests
{
    public class TownsfolkTests
    {
        readonly ITestOutputHelper output;
        public TownsfolkTests(ITestOutputHelper output) { this.output = output; }

        static string Mapping(string f) => File.ReadAllText(Path.Combine(Env.RepoRoot, "assets/mappings", f));
        static readonly Lazy<SettlementPlanner> planner = new Lazy<SettlementPlanner>(() =>
            new SettlementPlanner(Env.Data, TownTiles.Parse(Mapping("town_tiles.json")), PieceGeometry.Parse(Mapping("piece_geometry.json"))));

        [U5Theory]
        [InlineData(2, "towne")]
        [InlineData(13, "dwelling")]
        [InlineData(17, "castle")]
        [InlineData(19, "village")]
        [InlineData(20, "village")]
        [InlineData(21, "village")]
        public void EveryoneCanWalkTheirDay(int location, string kind)
        {
            var loc = Env.Data.Location(location);
            var plan = planner.Value.Plan(loc, kind, VBiome.Meadows);
            var stuck = new List<string>();
            foreach (var npc in Env.Data.NpcsOf(loc).Where(n => n.Slot > 0 && n.Exists))
            {
                var s = npc.Schedule;
                for (int a = 0; a < 3; a++)
                {
                    int b = (a + 1) % 3;
                    int ca = s.Y[a] * 32 + s.X[a], cb = s.Y[b] * 32 + s.X[b];
                    if (TownPath.Find(plan, s.Z[a], ca, s.Z[b], cb) == null)
                        stuck.Add($"{npc.Slot} {Townsfolk.Label(Env.Data, loc, npc)}: ({s.X[a]},{s.Y[a]},{s.Z[a]}) -> ({s.X[b]},{s.Y[b]},{s.Z[b]})");
                }
            }
            foreach (var l in stuck) output.WriteLine(l);
            Assert.True(stuck.Count == 0, string.Join("\n", stuck));
        }

        [U5Theory]
        [InlineData(2, "towne")]
        [InlineData(13, "dwelling")]
        [InlineData(17, "castle")]
        [InlineData(19, "village")]
        [InlineData(20, "village")]
        [InlineData(21, "village")]
        public void EveryoneLivesTheirDay(int location, string kind)
        {
            // a Valheim day is 30 minutes: an Ultima hour is 75 s
            const float Hour = 75f, Dt = 0.25f;
            var loc = Env.Data.Location(location);
            var plan = planner.Value.Plan(loc, kind, VBiome.Meadows);
            var wrong = new List<string>();
            foreach (var npc in Env.Data.NpcsOf(loc).Where(n => n.Slot > 0 && n.Exists && n.CreatureType >= 0))
            {
                var s = npc.Schedule;
                int p0 = s.PlaceAt(0);
                var (x0, _, z0) = plan.TilePos(s.Z[p0], s.Y[p0] * 32 + s.X[p0]);
                var w = new TownWalker(plan, s, s.Z[p0], x0, z0, npc.Slot);
                string who = $"{npc.Slot} {Townsfolk.Label(Env.Data, loc, npc)}";
                for (int hour = 0; hour < 24; hour++)
                {
                    for (float t = 0; t < Hour; t += Dt)
                    {
                        w.Tick(Dt, hour);
                        int c = w.Cell;
                        bool onPost = Enumerable.Range(0, 3).Any(k => s.Z[k] == w.Floor && s.Y[k] * 32 + s.X[k] == c);
                        if (!onPost && !TownPath.Walkable(plan, w.Floor, c) && !plan.Squeeze[w.Floor][c])
                        {
                            wrong.Add($"{who} hour {hour}: on ({c % 32},{c / 32},{w.Floor})");
                            break;
                        }
                    }
                    if (!w.Moving)
                    {
                        // standing still, nobody stares at a wall
                        int f = w.Facing, here = w.Cell;
                        bool open = TownPath.Walkable(plan, w.Floor, f) || plan.Squeeze[w.Floor][f];
                        bool anyOpen = new[] { here - 1, here + 1, here - 32, here + 32 }.Any(n => n >= 0 && n < 1024 && Math.Abs(n % 32 - here % 32) <= 1 && (TownPath.Walkable(plan, w.Floor, n) || plan.Squeeze[w.Floor][n]));
                        if (!open && anyOpen) wrong.Add($"{who} hour {hour}: at ({here % 32},{here / 32},{w.Floor}) faces ({f % 32},{f / 32})");
                    }
                    int place = s.PlaceAt(hour), post = s.Y[place] * 32 + s.X[place];
                    int reach = s.Ai[place] == 1 ? 3 : s.Ai[place] == 2 ? 6 : 0;
                    int cell = w.Cell;
                    if (w.Floor != s.Z[place] || Math.Max(Math.Abs(cell % 32 - post % 32), Math.Abs(cell / 32 - post / 32)) > reach)
                        wrong.Add($"{who} hour {hour}: at ({cell % 32},{cell / 32},{w.Floor}), post ({post % 32},{post / 32},{s.Z[place]}) ai {s.Ai[place]}");
                }
            }
            Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        }

        [U5Fact]
        public void LabelsAreNamesOrRoles()
        {
            var loc = Env.Data.Location(2);
            var labels = Env.Data.NpcsOf(loc).Where(n => n.Slot > 0 && n.Exists).Select(n => Townsfolk.Label(Env.Data, loc, n)).ToList();
            Assert.Contains("Justin", labels);
            Assert.Contains("Armourer", labels);
            Assert.Contains("Guard", labels);
        }
    }
}
