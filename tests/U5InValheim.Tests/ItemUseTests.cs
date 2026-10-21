using System.IO;
using System.Linq;
using U5InValheim.Data;
using Xunit;

namespace U5InValheim.Tests
{
    /// <summary>The Use command's items, Jimmy and the worn rings, as the original has them (CAST.OVL use_item_command, SJOG cmd_jimmy).</summary>
    public class ItemUseTests
    {
        [Fact]
        public void SextantPositionIsTwoLettersALatitudeAndALongitude()
        {
            // in_wis_print_position: 'A' + (y >> 4), 'A' + (y & 15); then the same of x
            Assert.Equal("A'A\", A'A\"", ItemUse.Position(0, 0));
            Assert.Equal("P'P\", P'P\"", ItemUse.Position(255, 255));
            // x 0x56, y 0x6b
            Assert.Equal("G'L\", F'G\"", ItemUse.Position(0x56, 0x6b));
        }

        [Fact]
        public void NightIsBeforeSixAndAfterEighteen()
        {
            Assert.True(ItemUse.Night(0));
            Assert.True(ItemUse.Night(5));
            Assert.False(ItemUse.Night(6));
            Assert.False(ItemUse.Night(18));
            Assert.True(ItemUse.Night(19));
            // the telescope itself shows the sky from 18
            Assert.True(ItemUse.TelescopeNight(18));
            Assert.False(ItemUse.TelescopeNight(17));
        }

        [Fact]
        public void ClockOfADayFraction()
        {
            Assert.Equal((0, 0), ItemUse.Clock(0));
            Assert.Equal((12, 0), ItemUse.Clock(0.5));
            Assert.Equal((18, 30), ItemUse.Clock((18 * 60 + 30) / 1440.0 + 1e-9));
            Assert.Equal((23, 59), ItemUse.Clock(0.99999));
        }

        [Fact]
        public void JimmyOpensBelowTheDexterity()
        {
            // a roll of 0..29 below the dexterity opens the lock
            Assert.True(ItemUse.JimmyOpens(15, 14));
            Assert.False(ItemUse.JimmyOpens(15, 15));
            Assert.Equal(15, Enumerable.Range(0, ItemUse.JimmyRoll).Count(r => ItemUse.JimmyOpens(15, r)));
            Assert.Equal(0.5, ItemUse.JimmyChance(15));
            Assert.Equal(1.0, ItemUse.JimmyChance(30));
            Assert.Equal(1.0, ItemUse.JimmyChance(99));
        }

        [Fact]
        public void RingRolls()
        {
            Assert.Equal(1, Enumerable.Range(0, ItemUse.RegenerationRoll).Count(ItemUse.Regenerates));
            Assert.Equal(1, Enumerable.Range(0, ItemUse.VanishRoll).Count(ItemUse.RingVanishes));
            Assert.True(ItemUse.MayVanish(ItemUse.Invisibility));
            Assert.True(ItemUse.MayVanish(ItemUse.Regeneration));
            Assert.False(ItemUse.MayVanish(ItemUse.Protection));
        }

        [Fact]
        public void LooksOfTheWornItems()
        {
            var (worn, party, radius) = ItemUse.ParseLooks(File.ReadAllText(Path.Combine(Env.RepoRoot, "assets/mappings/status.json")));
            Assert.Equal(1, radius);
            Assert.Equal("Cold", worn.Single(l => l.Item == ItemUse.Invisibility).StatusEffect);
            Assert.Equal("Staff_shield", worn.Single(l => l.Item == ItemUse.Regeneration).StatusEffect);
            var crown = party.Single(l => l.Name == "Crown of Lord British");
            Assert.Equal("magic", crown.Blocks);
            Assert.Equal(50, party.Single(l => l.Name == "Amulet of Lord British").Armour);
        }

        [U5Fact]
        public void WordsOfTheOriginal()
        {
            var ovl = Env.Data.Ovl;
            Assert.Equal("The pocket watch reads 8:05 AM.", ItemUse.Watch(ovl, 8, 5));
            Assert.Equal("The pocket watch reads 12:00 PM.", ItemUse.Watch(ovl, 12, 0));
            Assert.Equal("The pocket watch reads 12:30 AM.", ItemUse.Watch(ovl, 0, 30));
            Assert.Equal("Only outdoors!", ItemUse.Sextant(ovl, false, 22, 0, 0));
            Assert.Equal("Only at night!", ItemUse.Sextant(ovl, true, 12, 0, 0));
            Assert.Equal("Position: A'B\", C'D\"", ItemUse.Sextant(ovl, true, 22, 0x23, 0x01));
            Assert.Equal("Key broke!", ovl.CStr(ItemUse.SKeyBroke).Trim());
            Assert.Equal("A ring has vanished!", ovl.CStr(ItemUse.SRingVanished).Trim());
            Assert.True(ItemUse.PasswordMatches(ovl, "impera"));
            Assert.True(ItemUse.PasswordMatches(ovl, "IMPE"));
            Assert.False(ItemUse.PasswordMatches(ovl, "imp"));
            Assert.False(ItemUse.PasswordMatches(ovl, "pass"));
        }

        [U5Fact]
        public void NightSkyStepsBackADayAtATime()
        {
            var ovl = Env.Data.Ovl;
            var g = Env.Data.InitialGame;
            // the game begins on 5 April 139: every star at its starting orbit index (DATA.OVL 0x3750) + 1
            var first = ItemUse.NightSky(ovl, g.Year, g.Month, g.Day, new[] { 0, 0, 0 });
            Assert.Equal(new[] { 19, 3, 9, 16, 12, 7, 5, 3 }, first.Select(s => s.Column).ToArray());
            Assert.Equal(new[] { 18, 17, 15, 13, 11, 8, 5, 1 }, first.Select(s => s.Row).ToArray());
            Assert.DoesNotContain(first, s => s.Shadowlord);
            // a day later each steps back to its previous visible position: Moonglow's orbit shows 4, 11, 18
            var next = ItemUse.NightSky(ovl, 139, 4, 6, new[] { 2, 0, 0xff });
            Assert.Equal(12, next[0].Column);
            Assert.Equal(2, next[7].Column);
            Assert.True(next[1].Shadowlord);
            Assert.Equal(1, next.Count(s => s.Shadowlord));
            // and around a whole orbit after enough days
            Assert.All(ItemUse.NightSky(ovl, 141, 13, 28, null), s => Assert.InRange(s.Column, 1, ItemUse.SkyColumns));
        }
    }
}
