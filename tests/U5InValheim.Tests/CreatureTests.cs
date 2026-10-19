using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;
using U5InValheim.Data;

namespace U5InValheim.Tests
{
    public class CreatureTests
    {
        [U5Fact]
        public void GuardsAndTrollsAsTheOriginalHasThem()
        {
            var guard = CreatureStats.Of(Env.Data.Ovl, 12);
            Assert.Equal((30, 6, 99), (guard.Damage, guard.Armour, guard.HitPoints));
            var troll = CreatureStats.Of(Env.Data.Ovl, 41);
            Assert.Equal((15, 4, 15), (troll.Damage, troll.Armour, troll.HitPoints));
            var villager = CreatureStats.Of(Env.Data.Ovl, 4);
            Assert.Equal((6, 0, 8), (villager.Damage, villager.Armour, villager.HitPoints));
            Assert.True(CreatureStats.Of(Env.Data.Ovl, 33).Undead, "skeletons are undead");
        }

        [Fact]
        public void DamageIsTheAttackersLessTheArmour()
        {
            var rnd = new Random(1);
            for (int i = 0; i < 200; i++)
            {
                int d = Ultima.Damage(30, 6, false, false, rnd);
                Assert.InRange(d, 24, 29);
            }
            Assert.Equal(15, Ultima.Damage(15, 0, false, false, rnd));
            Assert.Equal(7, Ultima.Damage(15, 0, true, false, rnd)); // the undead take half
            Assert.Equal(0, Ultima.Damage(1, 0, true, false, rnd)); // 1/2
            Assert.Equal(99, Ultima.Damage(99, 30, false, false, rnd));
        }

        [Fact]
        public void EncountersFollowTheTerrainAndTheHour()
        {
            Assert.Equal(0, Encounters.Rate(0x21, 12)); // a road
            Assert.Equal(1, Encounters.Rate(5, 12));    // grass
            Assert.Equal(2, Encounters.Rate(10, 12));   // forest
            Assert.Equal(2, Encounters.Rate(4, 12));    // swamp
            Assert.Equal(5, Encounters.Rate(10, 3));    // forest in the small hours
            Assert.Equal(1, Encounters.Rate(5, 22));    // the original's night test never holds before midnight
        }

        [U5Fact]
        public void EveryEncounterHasAValheimBody()
        {
            var mapped = JsonDocument.Parse(File.ReadAllText(Path.Combine(Env.RepoRoot, "assets/mappings/monsters.json")))
                .RootElement.GetProperty("creatures").EnumerateArray()
                .Where(e => e.TryGetProperty("prefab", out var p) && p.ValueKind == JsonValueKind.String).Select(e => e.GetProperty("type").GetInt32()).ToHashSet();
            var rnd = new Random(7);
            var seen = new Dictionary<int, int>();
            foreach (byte tile in new byte[] { 1, 2, 5, 6, 7, 8, 9, 10, 11, 4 })
                for (int i = 0; i < 3000; i++)
                {
                    int m = Encounters.Choose(Env.Data.Ovl, tile, rnd);
                    if (m == 0) continue;
                    int type = Encounters.TypeOf(m);
                    if (type < 0) continue; // ships and whirlpools come later
                    seen[type] = seen.TryGetValue(type, out int n) ? n + 1 : 1;
                    Assert.True(mapped.Contains(type), $"type {type} ({Env.Data.Ovl.CreatureName(type)}) from tile {m:x2} has no Valheim body");
                }
            // the land is mostly orcs and snakes and rats; trolls and dragons are rare
            Assert.True(seen[32] > seen[41], "more orcs than trolls");
            Assert.Contains(39, seen.Keys); // a dragon now and then
            Assert.Contains(40, seen.Keys); // sand traps in the desert
            Assert.Contains(18, seen.Keys); // sea serpents at sea
        }
    }
}
