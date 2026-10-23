using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Bretaheim.Data;

namespace Bretaheim.Tests
{
    /// <summary>Karma, barley rations, the introduction and the gypsy's virtue questions (MASTER_PLAN §5.9, §5.13).</summary>
    public class KarmaFoodIntroTests
    {
        [Fact]
        public void TheAnkhCountsTenMoreKarmaWhileWorn()
        {
            Assert.Equal(60, Karma.WithAnkh(50, true));
            Assert.Equal(50, Karma.WithAnkh(50, false));
            Assert.Equal(99, Karma.WithAnkh(95, true));
        }

        [Fact]
        public void KarmaStartsAtFiftyAndStaysWithinNoughtToNinetyNine()
        {
            var data = new Dictionary<string, string>();
            Assert.Equal(50, Karma.Get(data));
            Assert.Equal(45, Karma.Add(data, Karma.AttackTownsperson));
            Assert.Equal("45", data[Karma.Key]);
            Assert.Equal(0, Karma.Add(data, -200));
            Assert.Equal(99, Karma.Add(data, 500));
            // the conversation's key is the same (TALK 0x89/0x8a through PlayerTalkWorld)
            data["u5_karma"] = "120";
            Assert.Equal(99, Karma.Get(data));
        }

        [Fact]
        public void KarmaDeedsAreTheOriginals()
        {
            Assert.Equal(-5, Karma.AttackTownsperson);
            Assert.Equal(-5, Karma.Murder);
            Assert.Equal(-2, Karma.OpenTownChest);
            Assert.Equal(-1, Karma.PickCrops);
            Assert.True(Karma.IsSettlement(2) && Karma.IsSettlement(0x20));
            Assert.False(Karma.IsSettlement(0) || Karma.IsSettlement(0x21));
            // a beggar: +1, +2 more for the last coin, at most once every 100 turns
            Assert.Equal(1, Karma.ForCharity(1000, -1, 125, 10));
            Assert.Equal(3, Karma.ForCharity(1000, -1, 125, 0));
            Assert.Equal(0, Karma.ForCharity(1000, 900, 125, 10));
            Assert.Equal(1, Karma.ForCharity(1000, 875, 125, 10));
            Assert.True(Karma.IsBeggar(new SettlementNpc { Type = 0x6d }));
            Assert.False(Karma.IsBeggar(new SettlementNpc { Type = 0x70 }));
        }

        [Fact]
        public void ARationIsEatenOnceADay()
        {
            var data = new Dictionary<string, string>();
            Assert.False(Rations.NewHour(data, 30));   // new to the world: starts counting
            Assert.False(Rations.NewHour(data, 30));
            Assert.True(Rations.NewHour(data, 31));
            Assert.False(Rations.NewHour(data, 31));
            Assert.True(Rations.NewHour(data, 50));    // hours passed at once: one change
            Assert.False(Rations.NewHour(data, 50));
            Assert.False(Rations.NewHour(data, 20));   // another world's hours
            Assert.True(Rations.NewHour(data, 21));
            Assert.True(Rations.Mealtime(6) && Rations.Mealtime(12) && Rations.Mealtime(18));
            Assert.False(Rations.Mealtime(0) || Rations.Mealtime(7));
            var rnd = new System.Random(1);
            for (int k = 0; k < 200; k++) Assert.InRange(Rations.StarvingDamage(rnd), 1, 8);
        }

        [U5Fact]
        public void TheGypsyAsksSevenQuestionsAndMakesTheAvatar()
        {
            var ovl = Env.Data.Ovl;
            var start = Env.Data.InitialGame.Character(0);
            Assert.Equal((15, 15, 15), (start.Str, start.Dex, start.Int));
            for (int seed = 0; seed < 50; seed++)
            {
                var c = new VirtueCasting(ovl, start, new Random(seed));
                var rejected = new HashSet<int>();
                var chosen = new List<int>();
                for (int q = 0; q < VirtueCasting.Questions; q++)
                {
                    c.Next();
                    Assert.NotEqual(c.First, c.Second);
                    Assert.DoesNotContain(c.First, rejected);
                    Assert.DoesNotContain(c.Second, rejected);
                    Assert.True(c.QuestionOffset > 0);
                    int lower = c.Left, higher = c.Right;
                    int pick = c.Answer(q % 2 == 0 ? 'A' : 'B');
                    Assert.Equal(q % 2 == 0 ? lower : higher, pick);
                    rejected.Add(pick == lower ? higher : lower);
                    chosen.Add(pick);
                }
                Assert.True(c.Done);
                // one card is never rejected: the last chosen
                Assert.Equal(7, rejected.Count);
                int I = 15, D = 15, S = 15;
                foreach (int k in chosen) { I += ovl.U8(VirtueCasting.IntBonus + k); D += ovl.U8(VirtueCasting.DexBonus + k); S += ovl.U8(VirtueCasting.StrBonus + k); }
                Assert.Equal((I, D, S), (c.Intelligence, c.Dexterity, c.Strength));
                var a = new Avatar { Made = false };
                c.Apply(a);
                Assert.Equal((I, I, D, Math.Max(20, S)), (a.Intelligence, a.Mana, a.Dexterity, a.Strength));
            }
            // the cards: Honesty +2 intelligence, Valor +2 strength, Humility nothing
            Assert.Equal(2, ovl.U8(VirtueCasting.IntBonus + 0));
            Assert.Equal(2, ovl.U8(VirtueCasting.StrBonus + 2));
            Assert.Equal(0, ovl.U8(VirtueCasting.IntBonus + 7) + ovl.U8(VirtueCasting.DexBonus + 7) + ovl.U8(VirtueCasting.StrBonus + 7));
        }

        [U5Fact]
        public void TheGypsysWordsAndPictures()
        {
            var art = new VirtueCasting.Art(Env.U5Dir, Env.Data.Ovl);
            var open = art.Opening(out string intro);
            Assert.Contains("gypsy", intro);
            Assert.Contains(open.Pixels, p => p == 15); // the words, in white
            var c = new VirtueCasting(Env.Data.Ovl, Env.Data.InitialGame.Character(0), new Random(1));
            c.Next();
            art.Question(c, out string q);
            Assert.Contains("A)", q);
            Assert.Contains("B)", q);
            art.Closing(out string end);
            Assert.Contains("Thy path is chosen", end);
        }

        [U5Fact]
        public void TheIntroductionHasTwentyOneScreens()
        {
            Assert.True(Story.Available(Env.U5Dir));
            var screens = Story.Load(Env.U5Dir, Env.Data.Ovl);
            Assert.Equal(21, screens.Count);
            Assert.All(screens, s => Assert.False(string.IsNullOrWhiteSpace(s.Text)));
            Assert.All(screens, s => Assert.True(s.Picture.Pixels.Count(p => p != 0) > 2000));
            Assert.Contains("shimmering blue door", screens[6].Text);
            Assert.NotNull(screens[1].After);
            // the words are drawn in white
            Assert.Contains(screens[2].Picture.Pixels, p => p == 15);
        }

        [Fact]
        public void IolosLutePlaysGreensleevesThroughInWholeBars()
        {
            // a beat's pickup and sixteen bars of three
            Assert.Equal(49 * Lute.Beat, Lute.Length, 0.001f);
            var samples = Lute.Play();
            Assert.True(samples.Length >= (Lute.Length + 2f) * Lute.Rate);
            Assert.InRange(samples.Max(Math.Abs), 0.79f, 0.8001f);
            // heard all through: no second without a string sounding
            for (int s = 0; s < (int)Lute.Length; s++)
                Assert.True(samples.Skip(s * Lute.Rate).Take(Lute.Rate).Max(Math.Abs) > 0.05f, $"silent at {s} s");
            Assert.Equal(samples, Lute.Play());
        }

        [Fact]
        public void AParagraphIsReadInPagesBetweenSentences()
        {
            var text = "One two three. \"Four five six!\" Seven eight nine ten. Eleven.\nA new line here.";
            Assert.Equal(new[] { "One two three. \"Four five six!\"", "Seven eight nine ten. Eleven.", "A new line here." }, Story.Pages(text, 6));
            // a sentence longer than a page is a page of its own; nothing lost or added
            var pages = Story.Pages(text, 2);
            Assert.Equal(Story.Words(text), pages.Sum(Story.Words));
            Assert.Contains("Seven eight nine ten.", pages);
            Assert.Single(Story.Pages("Mr. Smith"));
        }

        [U5Fact]
        public void TheIntroductionsLongParagraphsTurnInPages()
        {
            foreach (var screen in Story.Load(Env.U5Dir, Env.Data.Ovl))
            {
                var pages = Story.Pages(screen.Text);
                Assert.Equal(Story.Words(screen.Text), pages.Sum(Story.Words));
                Assert.All(pages, page => Assert.True(Story.Words(page) <= 70, page));
            }
        }
    }
}
