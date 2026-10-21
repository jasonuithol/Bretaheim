using System;
using System.IO;
using System.Linq;
using Xunit;
using Bretaheim.Data;

namespace Bretaheim.Tests
{
    public class ShopTests
    {
        static ItemMap Items => ItemMap.Parse(File.ReadAllText(Path.Combine(Env.RepoRoot, "assets/mappings/items.json")));

        [U5Fact]
        public void InnsAddTheIntelligenceAdjustment()
        {
            // SHOPPES3: price + price·(100 − 3·INT)/100 — half again at 0, the rate at 33, cheaper above
            var loc = Env.Data.Location(2);
            var keeper = Env.Data.NpcsOf(loc).First(n => n.IsShopkeeper && n.Trade == ShopTrade.Inn);
            var inn = Env.Data.Shops.Of(loc, keeper);
            int rate = Env.Data.Shops.InnPrice(inn, 33) ;
            Assert.True(rate > 0);
            Assert.Equal(rate + rate * 100 / 100, Env.Data.Shops.InnPrice(inn, 0));
            Assert.Equal(rate + rate * 55 / 100, Env.Data.Shops.InnPrice(inn, 15));
            Assert.True(Env.Data.Shops.InnPrice(inn, 30) < Env.Data.Shops.InnPrice(inn, 15));
        }

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
        public void ArmourersBuyArmsButNotAmmunition()
        {
            var loc = Env.Data.Location(2);
            var s = Env.Data.Shops.Of(loc, Env.Data.NpcsOf(loc).First(n => n.IsShopkeeper && n.Trade == ShopTrade.Armoury));
            int bow = Items.EquipmentOf("BowFineWood");
            Assert.True(bow >= 0, "the fine wood bow is the Ultima bow");
            // 15 × 75 × 3 / 100 + 1
            Assert.Equal(34, Env.Data.Shops.SellOffer(bow, 15));
            var offer = Env.Data.Shops.Offer(s, bow, 34, 10, new Random(3));
            Assert.Contains("34", offer);
            Assert.EndsWith("Deal?\"", offer);
            Assert.Contains("ammunition", Env.Data.Shops.WillNotBuy(s, 0x1b, 10));
            Assert.Contains("Gwenneth", Env.Data.Shops.Sold(s, 10));
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
                    // our own items, each made from a Valheim item
                    Assert.Contains(items.Get(m.Value.prefab)?.Base ?? m.Value.prefab, prefabs);
                    foreach (var x in m.Value.extra) Assert.Contains(items.Get(x).Base, prefabs);
                    wares++;
                }
            }
            Assert.True(wares > 40, $"{wares} wares");
        }
    }
}
