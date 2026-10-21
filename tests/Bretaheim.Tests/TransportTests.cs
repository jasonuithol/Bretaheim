using System;
using System.IO;
using System.Linq;
using Xunit;
using Bretaheim.Data;
using Bretaheim.WorldGen.Core;

namespace Bretaheim.Tests
{
    public class TransportTests
    {
        static ShopInfo Shop(int location, ShopTrade trade)
        {
            var loc = Env.Data.Location(location);
            var npc = Env.Data.NpcsOf(loc).First(n => n.Slot > 0 && n.IsShopkeeper && n.Trade == trade);
            return Env.Data.Shops.Of(loc, npc);
        }

        [Fact]
        public void TheIntelligenceAdjustmentIsTheOriginals()
        {
            // price + price·(100 − 3·INT)/100: dearer for the dull, never below the price under 34
            Assert.Equal(930, Shops.AdjustForIntelligence(600, 15));
            Assert.Equal(1182, Shops.AdjustForIntelligence(600, 1));
            Assert.Equal(600, Shops.AdjustForIntelligence(600, 33) - 6);
            // (above 33 the unsigned divide makes the original's garbage, kept)
            Assert.NotEqual(600 - 600 * 2 / 100, Shops.AdjustForIntelligence(600, 34));
        }

        [Fact]
        public void TheShipwrightSellsOneFrigateAndPutsSkiffsAboardIt()
        {
            int bought = 0;
            Assert.Equal(VesselSale.Frigate, Shops.Sell(bought, frigate: true));
            bought = Shops.After(bought, VesselSale.Frigate);
            Assert.Equal(2, Shops.SkiffsAboard(bought));
            Assert.Equal(VesselSale.DockFull, Shops.Sell(bought, frigate: true));
            Assert.Equal(VesselSale.SkiffAboard, Shops.Sell(bought, frigate: false));
            bought = Shops.After(bought, VesselSale.SkiffAboard);
            Assert.Equal(3, Shops.SkiffsAboard(bought));
            // a skiff alone: a second one is refused, unpaid
            int skiff = Shops.After(0, Shops.Sell(0, frigate: false));
            Assert.Equal(Shops.BoughtSkiff, skiff);
            Assert.Equal(VesselSale.SkiffRefused, Shops.Sell(skiff, frigate: false));
            Assert.False(Shops.Paid(VesselSale.SkiffRefused));
            Assert.Equal(VesselSale.DockFull, Shops.Sell(skiff, frigate: true));
        }

        [U5Fact]
        public void ShipwrightsAreInJhelomMinocEastBritannyAndBuccaneersDen()
        {
            var towns = Env.Data.Ovl.ShopTowns(ShopTrade.Shipwright);
            Assert.Equal(new[] { 3, 5, 21, 24 }, towns.ToArray());
            var s = Shop(3, ShopTrade.Shipwright);
            Assert.Equal(0, s.Slot);
            Assert.Equal((39, 221), Env.Data.Shops.Dock(s));
            Assert.Equal(930, Env.Data.Shops.FrigatePrice(s, 15));
            Assert.Equal(310, Env.Data.Shops.SkiffPrice(s, 15));
            var shops = Env.Data.Shops;
            Assert.Contains("Frigates", shops.ShipwrightMenu(s, 10));
            Assert.Contains("930 gold", shops.FrigateOffer(s, 10, 930));
            Assert.Contains("take it?", shops.FrigateOffer(s, 10, 930));
            Assert.Contains("310", shops.SkiffOffer(s, 10, 310));
            Assert.Contains("Sold!", shops.SoldAtDocks(s, 10));
            Assert.Contains("docks outside", shops.SoldAtDocks(s, 10));
            Assert.Contains("OUT!", shops.CheatMeOut(s, 10));
            Assert.Contains(s.Keeper, shops.CheatMeOut(s, 10));
            Assert.Contains("onboard", shops.SaleWords(s, VesselSale.SkiffAboard, 10));
            Assert.Contains("87,000", shops.SaleWords(s, VesselSale.DockFull, 10));
            Assert.EndsWith("sir?\"", shops.AnythingElse(s, 10));
            Assert.DoesNotContain("#", shops.Greeting(s, 10, new Random(1)));
        }

        [U5Fact]
        public void StablesAreInTrinsicNorthBritannyPawsAndTheLycaeum()
        {
            Assert.Equal(new[] { 6, 20, 22, 30 }, Env.Data.Ovl.ShopTowns(ShopTrade.Horses).ToArray());
            var s = Shop(6, ShopTrade.Horses);
            Assert.Equal(155, Env.Data.Shops.HorsePrice(s, 15));
            var offer = Env.Data.Shops.HorseOffer(s, 10, 155);
            Assert.Contains("155 gp", offer);
            Assert.Contains("Deal?", offer);
            Assert.Contains("feed it", Env.Data.Shops.CannotFeedIt(s, 10));
            Assert.Contains("GET THAT HORSE OUT OF HERE!", Env.Data.Shops.HorseOutOfHere);
        }

        [U5Fact]
        public void BritainsTavernServesMuttonAleRationsAndChat()
        {
            var s = Shop(2, ShopTrade.Tavern);
            var shops = Env.Data.Shops;
            Assert.Equal(0, shops.TavernStyle(s));
            Assert.Equal("Mutton", shops.OfferName(s, TavernOffer.Meal, 10));
            Assert.Equal("Ale", shops.OfferName(s, TavernOffer.Drink, 10));
            Assert.Equal("Rations", shops.OfferName(s, TavernOffer.Rations, 10));
            Assert.Equal("Chat", shops.OfferName(s, TavernOffer.Rumour, 10));
            Assert.Equal(4, shops.MealPrice(s));
            Assert.False(shops.HasWineList(s));
            Assert.Contains("Mutton", shops.TavernMenu(s, 10));
            Assert.Equal("\"That will be 4 gold for the  of ye, sir.", shops.ThatWillBeText(4, 1, false));
            Assert.Contains("three of ye, milady", shops.ThatWillBeText(12, 3, true));
            Assert.Contains("Beat it!", shops.CantPayText(s));
            Assert.Contains("enough", shops.EnoughText(s, false));
            Assert.Contains(shops.Stock(s), w => w.Item == Shops.Food);
        }

        [U5Fact]
        public void WineTavernsShowTheWineListAndSomeSellNoRations()
        {
            var shops = Env.Data.Shops;
            var towns = Env.Data.Ovl.ShopTowns(ShopTrade.Tavern);
            int wine = 0, none = 0;
            foreach (int t in towns)
            {
                var s = Shop(t, ShopTrade.Tavern);
                if (shops.HasWineList(s)) wine++;
                if (!shops.Offers(s, TavernOffer.Rations))
                {
                    none++;
                    Assert.DoesNotContain(shops.Stock(s), w => w.Item == Shops.Food);
                }
            }
            Assert.True(wine >= 1 && none >= wine, $"{wine} wine lists, {none} without rations");
            var list = shops.WineListEntries();
            Assert.Equal(("Rose", 18), list[0]);
            Assert.Equal(("Chablis", 98), list[5]);
            Assert.Contains("Claret", shops.WineListText(false));
        }

        [U5Fact]
        public void RumoursAreFoundByTheirFourLettersAtAWordsStart()
        {
            var shops = Env.Data.Shops;
            var r = shops.FindRumour("tell of honesty");
            Assert.NotNull(r);
            Assert.Equal("Malik", r.Person);
            Assert.Equal("Moonglow", r.Place);
            Assert.Equal(50, r.Price);
            Assert.Null(shops.FindRumour("phoney"));
            Assert.Null(shops.FindRumour(""));
            // (only 15 characters are read)
            Assert.Null(shops.FindRumour("aaaaaaaaaaaaaa honesty"));
            var crown = shops.FindRumour("CROWN");
            Assert.Equal("Terrance", crown.Person);
            Assert.Equal("Britain", crown.Place);
            var s = Shop(2, ShopTrade.Tavern);
            Assert.Contains("50 gold", shops.RumourPrice(s, 10, r));
            for (int seed = 0; seed < 8; seed++)
            {
                var answer = shops.RumourAnswer(s, 10, r, new Random(seed));
                Assert.Contains("Moonglow", answer);
                Assert.EndsWith("says " + s.Keeper + ".", answer);
            }
        }

        [U5Fact]
        public void EveryShipwrightHasABerthInDeepWater()
        {
            var terrain = TerrainTable.Parse(File.ReadAllText(Path.Combine(Env.RepoRoot, "assets/mappings/terrain.json")));
            var shape = new BritanniaShape(Env.Data.Britannia, terrain, seed: 1);
            foreach (int t in Env.Data.Ovl.ShopTowns(ShopTrade.Shipwright))
            {
                var s = Shop(t, ShopTrade.Shipwright);
                var loc = s.Location;
                var (dx, dy) = Env.Data.Shops.Dock(s);
                shape.TileCentre(loc.X, loc.Y, out float cx, out float cz);
                Assert.True(Docks.Berth(shape, dx, dy, loc.X, loc.Y, cx, cz, 46f, out float x, out float z), $"{loc.Name}: no berth");
                Assert.True(Docks.Floats(shape, x, z));
                shape.TileCentre(dx, dy, out float tx, out float tz);
                Assert.True(Math.Abs(x - tx) + Math.Abs(z - tz) < 2 * Docks.Search, $"{loc.Name}: berth far from the dock");
            }
        }
    }
}
