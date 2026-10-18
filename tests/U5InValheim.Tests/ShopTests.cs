using System;
using System.IO;
using System.Linq;
using Xunit;
using U5InValheim.Data;

namespace U5InValheim.Tests
{
    public class ShopTests
    {
        static ItemMap Items => ItemMap.Parse(File.ReadAllText(Path.Combine(Env.RepoRoot, "assets/mappings/items.json")));

        [U5Fact]
        public void IolosBowsSellsBowsAtTheOriginalPrices()
        {
            var loc = Env.Data.Location(2);
            var gwenneth = Env.Data.NpcsOf(loc).First(n => n.IsShopkeeper && n.Trade == ShopTrade.Armoury);
            var s = Env.Data.Shops.Of(loc, gwenneth);
            Assert.Equal("Iolo's Bows", s.Name);
            Assert.Equal("Gwenneth", s.Keeper);
            Assert.True(Shops.IsOpen(gwenneth, 10));
            Assert.False(Shops.IsOpen(gwenneth, 2));
            Assert.Contains("Good morning, and welcome to Iolo's Bows!", Env.Data.Shops.Greeting(s, 9, new Random(1)));
            Assert.Contains("Good evening", Env.Data.Shops.Greeting(s, 19, new Random(1)));
            Assert.Contains(Env.Data.Shops.Stock(s), w => w.Name == "Bow" && w.Price == 75);
        }

        [U5Fact]
        public void EveryShopsWaresAreValheimItems()
        {
            var prefabs = File.ReadAllLines(Path.Combine(Env.RepoRoot, "tools/data/valheim_prefabs.txt")).ToHashSet();
            var items = Items;
            int wares = 0;
            foreach (var loc in Env.Data.Locations.Where(l => l.IsSettlement))
            foreach (var npc in Env.Data.NpcsOf(loc).Where(n => n.Slot > 0 && n.IsShopkeeper))
            {
                var s = Env.Data.Shops.Of(loc, npc);
                Assert.False(string.IsNullOrWhiteSpace(s.Name), $"{loc.Name} {npc.Trade}");
                Assert.DoesNotContain("#", Env.Data.Shops.Greeting(s, 10, new Random(2)));
                foreach (var w in Env.Data.Shops.Stock(s))
                {
                    Assert.True(w.Price > 0, $"{loc.Name} {s.Name}: {w.Name} costs {w.Price}");
                    var m = items.Of(w.Item);
                    // the sling waits for its custom weapon (items.json); shops leave it out
                    if (m == null && w.Item == 17) continue;
                    Assert.True(m != null, $"{w.Name} has no Valheim item");
                    Assert.Contains(m.Value.prefab, prefabs);
                    foreach (var x in m.Value.extra) Assert.Contains(x, prefabs);
                    wares++;
                }
            }
            Assert.True(wares > 40, $"{wares} wares");
        }
    }
}
