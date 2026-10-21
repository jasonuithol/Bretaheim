using System.Collections.Generic;
using System.IO;
using System.Linq;
using U5InValheim.Data;
using Xunit;

namespace U5InValheim.Tests
{
    /// <summary>The inventory overhaul: the pouch of reagents and spells, the bigger inventory, stacks.</summary>
    public class InventoryTests
    {
        static ItemMap Items => ItemMap.Parse(File.ReadAllText(Path.Combine(Env.RepoRoot, "assets/mappings/items.json")));

        [Fact]
        public void ThePouchHoldsReagentsAndSpellsOnly()
        {
            var items = Items;
            Assert.True(Pouch.Holds(items.Reagent(0)));
            Assert.True(Pouch.Holds(items.Inventory(ItemMap.SpellBase + 5)));
            Assert.False(Pouch.Holds(items.Inventory(ItemMap.ScrollBase)));
            Assert.False(Pouch.Holds(items.Inventory(ItemMap.PotionBase)));
            Assert.False(Pouch.Holds(items.Inventory(1)));
            Assert.False(Pouch.Holds(items.Special("Key")));
            Assert.False(Pouch.Holds(null));
        }

        [Fact]
        public void CountsAreCappedAt99()
        {
            var items = Items;
            var ash = items.Reagent(0);
            var p = new Pouch();
            Assert.Equal(60, p.Add(ash, 60));
            Assert.Equal(39, p.Add(ash, 60));
            Assert.Equal(99, p.Count(ash));
            Assert.True(p.IsFull(ash));
            Assert.Equal(0, p.Add(ash, 1));
            Assert.False(p.Take(ash, 100));
            Assert.True(p.Take(ash, 9));
            Assert.Equal(90, p.Count(ash));
            Assert.Equal(99, p.AddSpell(Magic.InLor, 120));
            Assert.Equal(0, p.AddSpell(Magic.InLor, 1));
            Assert.True(p.TakeSpell(Magic.InLor));
            Assert.Equal(98, p.SpellCount(Magic.InLor));
            // a scroll is not the pouch's
            Assert.Equal(0, p.Add(items.Inventory(ItemMap.ScrollBase), 3));
        }

        [Fact]
        public void ThePouchIsKeptInTheCharactersData()
        {
            var items = Items;
            var p = new Pouch { Readied = Magic.Mani };
            p.Add(items.Reagent(7), 5);
            p.AddSpell(Magic.Mani, 3);
            p.AddSpell(Magic.AnTym, 1);
            var data = new Dictionary<string, string> { { "u5_karma", "50" } };
            p.Save(data);
            Assert.Equal("5", data["u5_pouch_reagent_7"]);
            Assert.Equal("3", data["u5_pouch_spell_04"]);
            Assert.Equal("1", data["u5_pouch_spell_47"]);
            Assert.Equal("4", data["u5_readied_spell"]);
            Assert.False(data.ContainsKey("u5_pouch_reagent_0"));
            var back = Pouch.Load(data);
            Assert.Equal(5, back.Reagents[7]);
            Assert.Equal(new[] { Magic.Mani, Magic.AnTym }, back.HeldSpells().ToArray());
            Assert.Equal(Magic.Mani, back.Readied);
            // spent to nothing: its key goes
            back.TakeSpell(Magic.AnTym);
            back.Readied = -1;
            back.Save(data);
            Assert.False(data.ContainsKey("u5_pouch_spell_47"));
            Assert.False(data.ContainsKey("u5_readied_spell"));
            Assert.Equal("50", data["u5_karma"]);
            // nonsense is ignored, over the cap is the cap
            var odd = Pouch.Load(new Dictionary<string, string> { { "u5_pouch_reagent_1", "x" }, { "u5_pouch_spell_00", "500" }, { "u5_readied_spell", "77" } });
            Assert.Equal(0, odd.Reagents[1]);
            Assert.Equal(99, odd.Spells[0]);
            Assert.Equal(-1, odd.Readied);
        }

        [Fact]
        public void AnOlderCharactersItemsMoveIntoThePouch()
        {
            var items = Items;
            var p = new Pouch();
            var carried = new List<(string, int)>
            {
                (items.Reagent(2).Prefab, 30), (items.Reagent(2).Prefab, 80),
                (items.Inventory(ItemMap.SpellBase + 9).Prefab, 4),
                (items.Inventory(ItemMap.ScrollBase).Prefab, 2), ("Coins", 150),
            };
            var moved = p.Absorb(items, carried);
            Assert.Equal(3, moved.Count);
            Assert.Equal(99, p.Reagents[2]);
            Assert.Equal(69, moved[1].taken);
            Assert.Equal(4, p.Spells[9]);
        }

        [Fact]
        public void TheInventoryGrowsInBritanniaAndNeverDropsItems()
        {
            Assert.Equal(14, InventorySize.RowsFor(true, 4, new[] { 0, 1 }));
            Assert.Equal(4, InventorySize.RowsFor(false, 4, new[] { 0, 3 }));
            // back in a Valheim world with items in row 10: the rows stay
            Assert.Equal(11, InventorySize.RowsFor(false, 4, new[] { 0, 10 }));
            // Haldor's bought rows count
            Assert.Equal(6, InventorySize.RowsFor(false, 6, new int[0]));
            Assert.Equal(6, InventorySize.VisibleFor(14, 4));
            Assert.Equal(4, InventorySize.VisibleFor(4, 4));
            Assert.Equal(9, InventorySize.VisibleFor(14, 9));
            // a slot for each kind of carried Ultima item that is not a reagent or a spell
            var items = Items;
            int kinds = items.All.Count(d => d.IsOurs && !Pouch.Holds(d));
            Assert.True(kinds <= InventorySize.Columns * InventorySize.Rows, $"{kinds} kinds");
        }

        [Fact]
        public void TorchesAreOurs()
        {
            var torch = Items.Special("Torch");
            Assert.Equal("U5_Torch", torch.Prefab);
            Assert.Equal("Torch", torch.Base);
            Assert.Equal("U5_Torch", Items.Of(Shops.Torch).Value.prefab);
        }
    }
}
