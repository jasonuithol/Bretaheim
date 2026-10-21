using Xunit;
using Bretaheim.Data;

namespace Bretaheim.Tests
{
    /// <summary>The shrines of virtue and the Codex (cast2.rs shrine_quest_dialogue, codex_shrine_dialogue).</summary>
    public class ShrineTests
    {
        static ShrineData Data => ShrineData.Load(Env.U5Dir, Env.Data.Ovl);

        [U5Fact]
        public void TheShrinesAndTheirWords()
        {
            var d = Data;
            Assert.Equal(new[] { "Honesty", "Compassion", "Valour", "Justice", "Sacrifice", "Honor", "Spirituality", "Humility" }, d.Virtues);
            Assert.Equal(new[] { "Ahm", "Mu", "Ra", "Beh", "Cah", "Summ", "Om", "Lum" }, d.MantraWords);
            Assert.Equal((233, 66), (d.X[0], d.Y[0]));
            Assert.Equal(-1, d.At(0, 0));        // Spirituality: only by moongate
            Assert.Equal(7, d.At(231, 216));
            Assert.Equal("...and thou dost kneel before the Altar.", d.Msg(ShrineData.SKneel).Trim());
            Assert.Equal("the failing of Dishonesty!", d.Quest(0));
            Assert.StartsWith("A dishonest life", d.CodexWords(0));
            Assert.Equal("Mantra:", d.Ovl(ShrineData.SMantra).Trim());
            Assert.Equal("ALAKAZAM", d.Ovl(ShrineData.SAlakazam));
            Assert.Contains("VERAMOCOR", ShrineData.Runes(d.Msg(ShrineData.FinalPages[2])));
            Assert.Contains("THE GATE TO THE", ShrineData.Runes(d.Msg(ShrineData.FinalPages[1])));
            Assert.Equal(8, d.Words.Length);
            Assert.All(d.Words, w => Assert.False(string.IsNullOrEmpty(w)));
            // the shrine's map: its altar (a hot brazier) in the middle; the Codex on its map
            Assert.Equal(0xb2, d.MapTile(false, 5, 5));
            Assert.Equal(0x41, d.MapTile(true, 5, 2));
        }

        [U5Fact]
        public void MeditationOrdainsRewardsAndAsksOfferings()
        {
            var d = Data;
            Assert.True(ShrineRules.Focused(d, 0, "honesty", new[] { "ahm", "AHM", "ahm ahm" }));
            Assert.False(ShrineRules.Focused(d, 0, "honesty", new[] { "ahm", "mu", "ahm" }));
            Assert.False(ShrineRules.Focused(d, 0, "valour", new[] { "ahm", "ahm", "ahm" }));
            Assert.True(ShrineRules.Restores(d, 2, "Valour", new[] { "ra", "ra", "ra" }));
            Assert.False(ShrineRules.Restores(d, 2, "valo", new[] { "ra", "ra", "ra" }));

            int quests = 0, done = 0;
            Assert.Equal(ShrineOutcome.Ordained, ShrineRules.Meditate(0, ref quests, done));
            Assert.Equal(1, quests);
            Assert.True(ShrineRules.MayPass(quests));
            Assert.Equal(0, ShrineRules.ReadCodex(quests, ref done, out bool final));
            Assert.False(final);
            Assert.Equal(1, done);
            Assert.Equal(ShrineOutcome.WellDone, ShrineRules.Meditate(0, ref quests, done));
            Assert.Equal(0, quests);
            Assert.Equal(ShrineOutcome.Offering, ShrineRules.Meditate(0, ref quests, done));
            Assert.False(ShrineRules.MayPass(quests));
            Assert.Equal(-1, ShrineRules.ReadCodex(quests, ref done, out _));

            int karma = 50;
            var a = new Avatar { Strength = 20, Dexterity = 30, Intelligence = 15 };
            var rose = ShrineRules.Reward(d, 3, ref karma, a);   // Justice: dexterity and intelligence
            Assert.Equal((false, true, true), rose);
            Assert.Equal((53, 20, 30, 16), (karma, a.Strength, a.Dexterity, a.Intelligence));
            ShrineRules.Reward(d, 7, ref karma, a);              // Humility: no stat, karma +6
            Assert.Equal(59, karma);
            Assert.False(ShrineRules.Offer(3, 299, ref karma));
            Assert.True(ShrineRules.Offer(3, 300, ref karma));
            Assert.Equal(62, karma);

            // all eight done: the last pages
            int q = 0x80, all = 0x7f;
            Assert.Equal(7, ShrineRules.ReadCodex(q, ref all, out final));
            Assert.True(final);
        }
    }
}
