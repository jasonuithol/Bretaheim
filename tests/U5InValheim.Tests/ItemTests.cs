using System.IO;
using System.Linq;
using U5InValheim.Data;
using Xunit;

namespace U5InValheim.Tests
{
    /// <summary>items.json as the catalogue of every Ultima item that can be carried.</summary>
    public class ItemTests
    {
        static ItemMap Items => ItemMap.Parse(File.ReadAllText(Path.Combine(Env.RepoRoot, "assets/mappings/items.json")));

        [Fact]
        public void EveryInventoryIdIsAnItem()
        {
            var items = Items;
            // the sling waits for its custom weapon
            var missing = Enumerable.Range(0, 112).Where(id => id != 17 && items.Inventory(id) == null).ToList();
            Assert.Empty(missing);
            Assert.All(Enumerable.Range(0, 8), r => Assert.NotNull(items.Reagent(r)));
            Assert.Equal(8, items.All.Count(d => d.Kind == ItemKind.Moonstone));
            foreach (var name in new[] { "Food", "Gold", "Torch", "Gem", "Key", "Skull Key", "Grapple", "Magic Carpet", "Spyglass", "Sextant", "Pocket Watch",
                                         "Black Badge", "Sandalwood Box", "HMS Cape Plans", "Shard of Falsehood", "Shard of Hatred", "Shard of Cowardice",
                                         "Crown of Lord British", "Sceptre of Lord British", "Amulet of Lord British" })
                Assert.True(items.Special(name) != null, name);
        }

        [Fact]
        public void OurItemsHaveTheirOwnPrefabs()
        {
            var items = Items;
            var ours = items.All.Where(d => d.IsOurs).Select(d => d.Prefab).ToList();
            Assert.Equal(ours.Count, ours.Distinct().Count());
            Assert.All(ours, p => Assert.StartsWith("U5_", p));
            // two Ultima items made from one Valheim item stay apart
            Assert.NotEqual(items.Special("Pocket Watch").Prefab, items.Special("Black Badge").Prefab);
            // gold is Valheim's own (torches are ours: stacks of 99)
            Assert.Equal("Coins", items.Special("Gold").Prefab);
        }

        [Fact]
        public void ArmourCountsOnceAndOldShopItemsStillCount()
        {
            var items = Items;
            var ring = items.Inventory(11);
            Assert.Equal("U5_RingMail", ring.Prefab);
            Assert.Equal(new[] { "U5_RingMail_Legs" }, ring.Extra);
            Assert.Equal(11, items.EquipmentOf("U5_RingMail"));
            Assert.Equal(-1, items.EquipmentOf("U5_RingMail_Legs"));
            Assert.Equal(11, items.EquipmentOf("ArmorBronzeChest"));
            Assert.Equal(-1, items.EquipmentOf("Wood"));
        }

        [U5Fact]
        public void SpellsAndScrollsAreNamedInFull()
        {
            var items = Items.Named(Env.Data.Ovl);
            Assert.Equal("In Lor", items.Inventory(48).Name);
            // cut at ten letters in DATA.OVL
            Assert.Equal("In Vas Por Ylem", items.Inventory(48 + 30).Name);
            Assert.Equal("Kal Xen Corp", items.Inventory(48 + 43).Name);
            Assert.Equal("Scroll of Vas Lor", items.Inventory(96).Name);
            Assert.Equal("Scroll of An Tym", items.Inventory(103).Name);
            Assert.All(items.All, d => Assert.False(string.IsNullOrEmpty(d.Name), d.Prefab));
        }
    }
}
