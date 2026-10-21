using U5InValheim.Data;
using Xunit;

namespace U5InValheim.Tests
{
    /// <summary>The one-off furnishings answer as the original's Look does (LOOKOBJ.OVL).</summary>
    public class FurnishingTests
    {
        [U5Fact]
        public void TheWellGrantsAHorseOnlyInPawsAndEmpathAbbey()
        {
            var ovl = Env.Data.Ovl;
            Assert.Equal("Corvette", ovl.CStr(FurnishingRules.HorseWords[0]).Trim());
            Assert.Equal("Horse", ovl.CStr(FurnishingRules.HorseWords[5]).Trim());
            Assert.Equal(FurnishingRules.WishResult.Horse, FurnishingRules.Wish(ovl, "a horse", FurnishingRules.Paws));
            Assert.Equal(FurnishingRules.WishResult.Horse, FurnishingRules.Wish(ovl, "PORSCHE", FurnishingRules.EmpathAbbey));
            Assert.Equal(FurnishingRules.WishResult.NoEffect, FurnishingRules.Wish(ovl, "a horse", 2));
            Assert.Equal(FurnishingRules.WishResult.NoEffect, FurnishingRules.Wish(ovl, "riches", FurnishingRules.Paws));
            Assert.Equal(FurnishingRules.WishResult.Nothing, FurnishingRules.Wish(ovl, "  ", FurnishingRules.Paws));
            // (the original reads twelve characters: a horse past them is not heard)
            Assert.Equal(FurnishingRules.WishResult.NoEffect, FurnishingRules.Wish(ovl, "I wish for a horse", FurnishingRules.Paws));
            Assert.StartsWith("Poof", FurnishingRules.WishWords(ovl, FurnishingRules.WishResult.Horse));
            Assert.StartsWith("No effect", FurnishingRules.WishWords(ovl, FurnishingRules.WishResult.NoEffect));
            Assert.StartsWith("a well", ovl.CStr(FurnishingRules.SWell).Trim());
        }

        [U5Fact]
        public void TheClockShowsTwelveHourTime()
        {
            var d = Env.Data;
            Assert.Equal("a grandfather clock, showing: 3:05 PM.", FurnishingRules.ClockText(d.Ovl, d.TileNames, FurnishingRules.ClockA, 15, 5));
            Assert.Equal("a grandfather clock, showing: 12:00 AM.", FurnishingRules.ClockText(d.Ovl, d.TileNames, FurnishingRules.ClockB, 0, 0));
            Assert.Equal("a grandfather clock, showing: 12:30 PM.", FurnishingRules.ClockText(d.Ovl, d.TileNames, FurnishingRules.ClockB, 12, 30));
        }

        [U5Fact]
        public void TheTelescopeSphereAndFountainWords()
        {
            var ovl = Env.Data.Ovl;
            Assert.Equal("the sun!", ovl.CStr(FurnishingRules.SSun).Trim());
            Assert.Equal("the night sky!", ovl.CStr(FurnishingRules.SNightSky).Trim());
            Assert.Equal("Death vision!", ovl.CStr(FurnishingRules.SDeathVision).Trim());
            Assert.Equal("Strange vision!", ovl.CStr(FurnishingRules.SStrangeVision).Trim());
            Assert.Equal("a gurgling fountain!", ovl.CStr(FurnishingRules.SFountain).Trim());
            Assert.Equal("Refreshing...", ovl.CStr(FurnishingRules.SRefreshing).Trim());
            Assert.Equal("Broken!", ovl.CStr(FurnishingRules.SBroken).Trim());
            // rand(1..30) at or above the intelligence is death
            Assert.True(FurnishingRules.StrangeVision(15, 14));
            Assert.False(FurnishingRules.StrangeVision(15, 15));
        }

        [Fact]
        public void GravesGiveUpDirtBodiesAndSometimesTreasure()
        {
            Assert.Equal(FurnishingRules.Dig.Dirt, FurnishingRules.DigUp(0));
            Assert.Equal(FurnishingRules.Dig.Body, FurnishingRules.DigUp(40));
            Assert.Equal(FurnishingRules.Dig.Risen, FurnishingRules.DigUp(80));
            Assert.Equal(FurnishingRules.Dig.Treasure, FurnishingRules.DigUp(99));
        }
    }
}
