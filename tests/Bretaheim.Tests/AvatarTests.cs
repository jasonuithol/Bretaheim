using System;
using System.Collections.Generic;
using Xunit;
using Bretaheim.Data;

namespace Bretaheim.Tests
{
    public class AvatarTests
    {
        [Fact]
        public void LevelsComeAtEachDoublingOfAHundred()
        {
            Assert.Equal(1, Avatar.LevelFor(0));
            Assert.Equal(1, Avatar.LevelFor(99));
            Assert.Equal(2, Avatar.LevelFor(100));
            Assert.Equal(3, Avatar.LevelFor(200));
            Assert.Equal(3, Avatar.LevelFor(399));
            Assert.Equal(4, Avatar.LevelFor(400));
            Assert.Equal(8, Avatar.LevelFor(6400));
        }

        [Fact]
        public void TheApparitionRaisesTheLevelAndOneStat()
        {
            var a = new Avatar { Experience = 250 };
            Assert.Equal(30, a.MaxHitPoints);
            int which = a.LevelUp(new Random(3));
            Assert.InRange(which, 0, 2);
            Assert.Equal(3, a.Level);
            Assert.Equal(90, a.MaxHitPoints);
            Assert.Equal(46, a.Strength + a.Dexterity + a.Intelligence);
            Assert.Equal(-1, a.LevelUp(new Random(3))); // nothing more until more experience
        }

        [Fact]
        public void TheAvatarIsKeptWithTheCharacter()
        {
            var data = new Dictionary<string, string>();
            new Avatar { Strength = 20, Level = 4, Experience = 512 }.Save(data);
            var b = Avatar.Load(data);
            Assert.Equal((20, 15, 4, 512), (b.Strength, b.Dexterity, b.Level, b.Experience));
            // a new character's Avatar is unmade, every stat 0, until the virtue questions
            var unmade = Avatar.Load(new Dictionary<string, string>());
            Assert.False(unmade.Made);
            Assert.Equal((0, 0, 0, 0), (unmade.Strength, unmade.Dexterity, unmade.Intelligence, unmade.Mana));
            var again = new Dictionary<string, string>();
            unmade.Save(again);
            Assert.False(Avatar.Load(again).Made);
            // saved before the questions existed: made
            Assert.True(Avatar.Load(new Dictionary<string, string> { { "u5_avatar_str", "18" } }).Made);
        }

        [U5Fact]
        public void KillsAndArmourAsTheOriginalCountsThem()
        {
            Assert.Equal(4, Avatar.ExperienceFor(CreatureStats.Of(Env.Data.Ovl, 41)));   // a troll: 15 / 4 + 1
            Assert.Equal(25, Avatar.ExperienceFor(CreatureStats.Of(Env.Data.Ovl, 39)));  // a dragon: 99 / 4 + 1
            // leather helm + leather armour + small shield
            int worn = Avatar.Defence(Env.Data.Ovl, new[] { 0, 10, 4 });
            Assert.Equal(Env.Data.Ovl.ItemDefense(0) + Env.Data.Ovl.ItemDefense(10) + Env.Data.Ovl.ItemDefense(4), worn);
            Assert.True(worn > 0);
            var words = Avatar.LevelUpWords(Env.Data.Ovl, "Iolo", 3, 0);
            Assert.Contains("Hail, Iolo!", words);
            Assert.Contains("level 3", words);
            Assert.Contains("stronger!", words);
        }
    }
}
