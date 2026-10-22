using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Bretaheim.Data;

namespace Bretaheim.Tests
{
    /// <summary>Blackthorn's interrogation, Lord British's rescue and the ending (blckthrn.rs, endgame.rs).</summary>
    public class StoryTests
    {
        static byte[] Read(string f) => File.ReadAllBytes(U5Data.FindFile(Env.U5Dir, f));

        /// <summary>Plays a scene: answers its questions in turn, stops at the end, at an endless beat, or after too many beats.</summary>
        static List<Beat> Play(IEnumerable<Beat> beats, Action<Beat, int> answer)
        {
            var seen = new List<Beat>();
            int asked = 0;
            foreach (var b in beats)
            {
                seen.Add(b);
                if (b.Endless || seen.Count > 20000) break;
                if (b.Kind == BeatKind.Ask || b.Kind == BeatKind.YesNo) answer(b, asked++);
            }
            return seen;
        }

        static BlackthornScene Blackthorn(int living, bool[] ruined = null)
            => new BlackthornScene(Env.Data.Ovl, Read("MISCMSG.DAT"), Read("MISCMAPS.DAT"), "Avatar", false, living, "Shamino", ruined ?? new bool[8]);

        [Fact]
        public void TheMantraAnywhereInTheFirstFourteenLetters()
        {
            Assert.True(BlackthornScene.ContainsMantra("ahm", "Ahm"));
            Assert.True(BlackthornScene.ContainsMantra("it is AHM!", "Ahm"));
            Assert.False(BlackthornScene.ContainsMantra("I will never tell AHM", "Ahm")); // past the fourteenth letter
            Assert.False(BlackthornScene.ContainsMantra("", "Ahm"));
            Assert.Equal(0, BlackthornScene.FirstUnbetrayed(new bool[8]));
            Assert.Equal(2, BlackthornScene.FirstUnbetrayed(new[] { true, true, false, false, false, false, false, false }));
            Assert.Equal(-1, BlackthornScene.FirstUnbetrayed(Enumerable.Repeat(true, 8).ToArray()));
        }

        [Fact]
        public void TheQuestIsReckonedFromTheFifthOfApril139()
        {
            Assert.Equal((0, 0, 0), Certificate.Elapsed(139, 4, 5));
            Assert.Equal((0, 11, 6), Certificate.Elapsed(140, 2, 11));
            Assert.Equal((0, 0, 27), Certificate.Elapsed(139, 5, 4));   // 28-day months
            Assert.Equal((1, 0, 0), Certificate.Elapsed(140, 4, 5));    // 13 months
        }

        [U5Fact]
        public void AloneTheMantraIsRewardedWithLife()
        {
            var s = Blackthorn(1);
            int betrayed = -1;
            s.OnBetray = v => betrayed = v;
            Play(s.Run(), (b, i) => s.Answer = "ahm");
            Assert.Equal(0, betrayed);
            Assert.True(s.Betrayed);
            Assert.False(s.CompanionKilled);
            Assert.Contains("What is the Mantra of the Mystic Shrine of Honesty?\"", s.View.Text);
            Assert.Contains("I sense Truth in thee", s.View.Text);
            Assert.Contains("Thou art subdued and blindfolded!", s.View.Text);
            Assert.Contains("GUARD! Release this goodman at once!\"", s.View.Text);
        }

        [U5Fact]
        public void AloneARefusalIsSentToTheDungeon()
        {
            var s = Blackthorn(1, new[] { true, false, false, false, false, false, false, false });
            var beats = Play(s.Run(), (b, i) => s.Answer = "never");
            Assert.Equal(1, s.Virtue);
            Assert.False(s.Betrayed);
            Assert.Contains("Mantra of the Mystic Shrine of Compassion?", s.View.Text);
            Assert.Contains("A child would catch thee in thy lies", s.View.Text);
            Assert.Single(beats, b => b.Kind == BeatKind.Ask);
        }

        [U5Fact]
        public void ACompanionPaysForFourRefusals()
        {
            var s = Blackthorn(2);
            bool? sliced = null;
            s.OnExecute = x => sliced = x;
            var beats = Play(s.Run(), (b, i) => s.Answer = "no");
            Assert.Equal(4, beats.Count(b => b.Kind == BeatKind.Ask));
            Assert.True(s.CompanionKilled);
            Assert.True(sliced);
            Assert.False(s.Betrayed);
            Assert.Contains("And then will Shamino die!\"", s.View.Text);
            Assert.Contains("Shamino is sliced in half!", s.View.Text);
            Assert.Contains("None call me unfair!", s.View.Text);
            // the two seats of a party of two, either side of Blackthorn
            Assert.Equal((3, 5), s.Seat(2, 0));
            Assert.Equal((7, 5), s.Seat(2, 1));
        }

        [U5Fact]
        public void TheMantraGivenStillCostsTheCompanion()
        {
            var s = Blackthorn(2);
            bool? sliced = null;
            s.OnExecute = x => sliced = x;
            var beats = Play(s.Run(), (b, i) => s.Answer = i == 0 ? "no" : "Ahm");
            Assert.Equal(2, beats.Count(b => b.Kind == BeatKind.Ask));
            Assert.True(s.Betrayed);
            Assert.False(sliced);
            Assert.Contains("merciful death", s.View.Text);
        }

        [U5Fact]
        public void WithEveryShrineBetrayedNothingIsAsked()
        {
            var s = Blackthorn(1, Enumerable.Repeat(true, 8).ToArray());
            var beats = Play(s.Run(), (b, i) => s.Answer = "ahm");
            Assert.Equal(-1, s.Virtue);
            Assert.DoesNotContain(beats, b => b.Kind == BeatKind.Ask);
            Assert.DoesNotContain("Blackthorn says", s.View.Text);
        }

        [U5Fact]
        public void TheCellIsDrawn()
        {
            var s = Blackthorn(2);
            Play(s.Run().TakeWhile(b => b.Kind != BeatKind.Ask), (b, i) => { });
            var screen = s.View.Render(Env.Data.Tiles, new FixedFont(Read("IBM.CH")));
            Assert.True(screen.Pixels.Count(p => p != 0) > 8000);
            Assert.Equal(BlackthornScene.BlackthornTile, s.View.Tile[8]);
            Assert.Equal(new[] { "\"What is the", "Mantra of the", "Mystic Shrine of", "Honesty?\"" }, s.View.Lines.Skip(s.View.Lines.Count - 7).Take(4));
        }

        EndgameScene Endgame(bool box, out List<Beat> finale)
        {
            var s = new EndgameScene(Env.Data.Ovl, Read("MISCMAPS.DAT"), Read("ENDMSG.DAT"), new[] { "Avatar", "Iolo" }, box, new Random(5));
            var f = finale = new List<Beat>();
            s.Finale = () => { var r = EndStory.Finale(Env.U5Dir, Env.Data.Ovl, "Avatar", 140, 2, 11).ToList(); f.AddRange(r); return r; };
            return s;
        }

        [U5Fact]
        public void WithTheBoxLordBritishLeadsThemHome()
        {
            var s = Endgame(true, out var finale);
            int won = 0;
            s.OnWon = () => won++;
            var beats = Play(s.Run(), (b, i) => s.Yes = true);
            Assert.True(s.Won);
            Assert.Equal(1, won);
            Assert.Contains("Didst thou bring my box?", s.View.Text);
            Assert.Contains("Orb of the Moons", s.View.Text);
            Assert.Equal(7, finale.Count);
            Assert.All(finale, b => Assert.Equal(BeatKind.Picture, b.Kind));
            Assert.Contains("Blackthorn steps through the gate", finale[EndStory.Screens - 1].Words);
            Assert.Contains("Avatar the Avatar", finale.Last().Words);
            Assert.True(beats.Last().Endless);
        }

        [U5Fact]
        public void WithoutItTheyPullUpAChair()
        {
            var s = Endgame(false, out var finale);
            var beats = Play(s.Run(), (b, i) => s.Yes = i > 0);
            Assert.False(s.Won);
            Assert.Empty(finale);
            Assert.Contains("The sandalwood box, from the secret passage in my chamber!", s.View.Text);
            Assert.Contains("pull up a chair", s.View.Text);
            Assert.True(beats.Last().Endless);
        }

        [U5Fact]
        public void TheCertificate()
        {
            var ovl = Env.Data.Ovl;
            Assert.Equal("Thirty-Nine", Certificate.Number(ovl, 39));
            Assert.Equal("Forty", Certificate.Number(ovl, 40));
            Assert.Equal("Fifth", Certificate.Ordinal(ovl, 5));
            Assert.Equal("Thirteenth", Certificate.Ordinal(ovl, 13));
            Assert.Equal("Twentieth", Certificate.Ordinal(ovl, 20));
            Assert.Equal("Twenty-Third", Certificate.Ordinal(ovl, 23));
            var text = Certificate.Plain(Certificate.Lines(ovl, "Dupre", 141, 7, 23));
            Assert.StartsWith("Be it known that on\nthe Twenty-Third Day of\nthe Seventh Month\nof the Year\nOne Hundred\nForty-One\n\nDupre the Avatar\n", text);
            Assert.Contains("THE QUEST OF THE AVATAR\nIS FOREVER\n", text);
            Assert.Contains("2 years, 3 months, 18 days\nto Lord British at Origin Systems!", text);
        }

        [Fact]
        public void LordBritishRaisesTheFallenAtACostOfExperienceByKarma()
        {
            var a = new Avatar { Experience = 1000, Level = 5, Intelligence = 22, Mana = 3, Strength = 20 };
            Resurrection.Raise(a, 50);
            Assert.Equal(500, a.Experience);
            Assert.Equal(4, a.Level);
            Assert.Equal(22, a.Mana);
            Assert.Equal(20, a.Strength);
            var saint = new Avatar { Experience = 1000, Level = 5 };
            Resurrection.Raise(saint, 98);
            Assert.Equal(1000, saint.Experience);
            Assert.Equal(5, saint.Level);
            Assert.Equal(75, Resurrection.KarmaAfter(40));
            Assert.Equal(90, Resurrection.KarmaAfter(90));
        }

        [U5Fact]
        public void TheResurrectionSceneSpeaksByKarma()
        {
            string Text(int karma)
            {
                var s = new ResurrectionScene(Env.Data.Ovl, Read("KARMA.DAT"), karma);
                var beats = Play(s.Run(), (b, i) => { });
                Assert.Single(beats, b => b.Kind == BeatKind.Key);
                return s.View.Text;
            }
            string low = Text(10), high = Text(99);
            Assert.Contains("An unending darkness engulfs thee...", low);
            Assert.Contains("FORTIS FORTUNA", low);
            Assert.Contains("Thou hast strayed far from the path of the Avatar", low);
            Assert.Contains("Well armed art thou to fight Death's embrace", high);
            Assert.Contains("Vertigo...", high);
        }
    }
}
