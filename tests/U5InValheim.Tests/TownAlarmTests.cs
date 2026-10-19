using System;
using System.IO;
using System.Linq;
using Xunit;
using U5InValheim.WorldGen.Core;

namespace U5InValheim.Tests
{
    public class TownAlarmTests
    {
        static string Mapping(string f) => File.ReadAllText(Path.Combine(Env.RepoRoot, "assets/mappings", f));
        static readonly Lazy<SettlementPlan> britain = new Lazy<SettlementPlan>(() =>
            new SettlementPlanner(Env.Data, TownTiles.Parse(Mapping("town_tiles.json")), PieceGeometry.Parse(Mapping("piece_geometry.json"))).Plan(Env.Data.Location(2), "towne", VBiome.Meadows));

        [Fact]
        public void BigJobsNeedMoreGuards()
        {
            Assert.Equal(1, TownAlarm.GuardsNeeded(20, 4));    // a greyling
            Assert.Equal(2, TownAlarm.GuardsNeeded(600, 4));   // a troll
            Assert.Equal(4, TownAlarm.GuardsNeeded(5000, 4));  // never more than there are
            Assert.Equal(1, TownAlarm.GuardsNeeded(5000, 0));
        }

        [Fact]
        public void ReinforcementsComeAfterFiveMinutesOrWhenTheGuardsAreDown()
        {
            Assert.False(TownAlarm.ReinforcementsDue(60, 3, 4));
            Assert.True(TownAlarm.ReinforcementsDue(300, 3, 4));
            Assert.True(TownAlarm.ReinforcementsDue(10, 0, 4));
            Assert.False(TownAlarm.ReinforcementsDue(10, 0, 0)); // a town with no guards waits the five minutes
        }

        [U5Fact]
        public void TownsfolkHideAtHomeAndEveryoneCanGetThere()
        {
            var plan = britain.Value;
            var loc = Env.Data.Location(2);
            foreach (var npc in Env.Data.NpcsOf(loc).Where(n => n.Slot > 0 && n.Exists && n.CreatureType >= 0 && n.CreatureType != 12))
            {
                var (z, cell) = TownAlarm.Home(plan, npc.Schedule);
                for (int k = 0; k < 3; k++)
                {
                    var s = npc.Schedule;
                    Assert.NotNull(TownPath.Find(plan, s.Z[k], s.Y[k] * 32 + s.X[k], z, cell));
                }
            }
            // the innkeeper's home is a bed
            var justin = Env.Data.NpcsOf(loc).First(n => Env.Data.NpcName(loc, n) == "Justin");
            var home = TownAlarm.Home(plan, justin.Schedule);
            Assert.True(plan.Seats.TryGetValue(home, out var seat) && seat.Bed, "Justin hides in his bed");
        }

        [U5Fact]
        public void ReinforcementsArriveOnTheEdgeNearestTheFight()
        {
            var plan = britain.Value;
            int edge = TownAlarm.EdgeNear(plan, 0, -60); // south of town
            Assert.True(edge >= 0);
            Assert.True(edge / 32 >= 30, $"edge row {edge / 32}");
            Assert.True(TownPath.Walkable(plan, 0, edge));
        }
    }
}
