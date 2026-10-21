using System;
using System.IO;
using System.Linq;
using U5InValheim.Data;
using Xunit;

namespace U5InValheim.Tests
{
    /// <summary>The original's loot: chests, monster drops, hidden items, gifts, and the Valheim items each is.</summary>
    public class LootTests
    {
        static ItemMap Items => ItemMap.Parse(File.ReadAllText(Path.Combine(Env.RepoRoot, "assets/mappings/items.json")));

        [U5Fact]
        public void ChestsHoldTheOriginalsLoot()
        {
            var rnd = new Random(5);
            var kinds = new System.Collections.Generic.HashSet<LootKind>();
            int empty = 0, items = 0;
            for (int i = 0; i < 2000; i++)
            {
                var loot = LootTables.Chest(Env.Data.Ovl, 30, rnd);
                if (loot.Count == 0) empty++;
                foreach (var l in loot)
                {
                    kinds.Add(l.Kind);
                    Assert.True(l.Count > 0, l.ToString());
                    if (l.Kind == LootKind.Item)
                    {
                        items++;
                        // never the unique ones (minimum 255)
                        Assert.DoesNotContain(l.Value, new[] { 8, 15, 35, 39, 40, 41, 47 });
                    }
                    if (l.Kind == LootKind.Potion || l.Kind == LootKind.Scroll) Assert.InRange(l.Value, 0, 7);
                    if (l.Kind == LootKind.Gold) Assert.InRange(l.Count, 1, 255);
                }
            }
            // the fixed roll's eight kinds (a chest inside is opened too) and arms from the random table
            foreach (var k in new[] { LootKind.Gold, LootKind.Potion, LootKind.Scroll, LootKind.Keys, LootKind.Gems, LootKind.Torches, LootKind.Food, LootKind.Item })
                Assert.Contains(k, kinds);
            Assert.True(items > 1000, $"{items} items");
            // a quality-1 chest: little but gold
            var poor = Enumerable.Range(0, 500).SelectMany(_ => LootTables.Chest(Env.Data.Ovl, 1, rnd)).ToList();
            Assert.All(poor, l => Assert.True(l.Kind == LootKind.Gold || (l.Kind == LootKind.Item && l.Value >= 16), l.ToString()));
        }

        [Fact]
        public void MonstersLeaveChestsByTheirTreasure()
        {
            var rnd = new Random(1);
            Assert.All(Enumerable.Range(0, 200), _ => Assert.False(LootTables.Drop(0, rnd).chest));
            // a dragon (30) always leaves one, usually trapped; a rat (5) one time in six
            Assert.All(Enumerable.Range(0, 200), _ => Assert.True(LootTables.Drop(30, rnd).chest));
            int rats = Enumerable.Range(0, 6000).Count(_ => LootTables.Drop(5, rnd).chest);
            Assert.InRange(rats, 800, 1200);
        }

        [U5Fact]
        public void HiddenItemsAreTheOriginals()
        {
            var hidden = LootTables.Hidden(Env.Data.Ovl);
            Assert.Equal(113, hidden.Count);
            var glass = hidden[LootTables.GlassSword];
            Assert.Equal((0, 64, 80), (glass.Location, glass.X, glass.Y));
            Assert.Equal(new Loot(LootKind.Item, 39), glass.Loot);
            Assert.Equal(new Loot(LootKind.SkullKeys, 0, 5), hidden[LootTables.MinocSkullKeys].Loot);
            Assert.Equal(new Loot(LootKind.HmsCapePlans), hidden[12].Loot);
            // the Ankh in Cove (location 23)
            Assert.Contains(hidden, h => h.Location == 23 && h.Loot.Kind == LootKind.Item && h.Loot.Value == 47);
        }

        [Fact]
        public void EveryLootIsAValheimItem()
        {
            var items = Items;
            foreach (LootKind k in Enum.GetValues(typeof(LootKind)))
            {
                if (k == LootKind.None || k == LootKind.Chest) continue;
                var p = items.Prefabs(new Loot(k, 0, 3));
                Assert.True(p.Count > 0, k.ToString());
                Assert.All(p, x => Assert.Equal(3, x.count));
            }
            Assert.Equal(new[] { ("U5_RingMail", 1), ("U5_RingMail_Legs", 1) }, items.Prefabs(new Loot(LootKind.Item, 11)));
            Assert.Equal("U5_Moonstone4", items.Prefabs(new Loot(LootKind.Moonstone, 4))[0].prefab);
            Assert.Equal(("Coins", 7), items.Prefabs(new Loot(LootKind.Gold, 0, 7))[0]);
            Assert.Equal(new Loot(LootKind.Item, 28), LootTables.Gift(28));
            Assert.Equal(LootKind.Sextant, LootTables.Gift(0x48).Kind);
            Assert.Equal(new Loot(LootKind.Item, 27, 5), LootTables.Object(5, 27));
        }

        [U5Fact]
        public void TheHarpsichordsMelody()
        {
            var m = new Melody(Env.Data.Ovl);
            Assert.Equal("6789878767653", m.Tune);
            bool Play(string keys) { bool done = false; foreach (char c in keys) done = m.Play(c - '0'); return done; }
            Assert.True(Play("6789878767653"));
            // a slip partway, and the original's fallbacks: a wrong note starts again (or at 1 on the first note)
            Assert.False(Play("678912"));
            Assert.True(Play("6789878767653"));
            Assert.False(Play("67898787676"));
            Assert.True(Play("53"));
            // the scale rises from 1
            for (int n = 2; n <= 9; n++) Assert.True(Melody.Frequency(Env.Data.Ovl, n) > Melody.Frequency(Env.Data.Ovl, n - 1));
        }
    }
}
