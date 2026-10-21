using System;
using System.Collections.Generic;
using System.Linq;
using Bretaheim.Data;
using Xunit;

namespace Bretaheim.Tests
{
    /// <summary>The original's magic (Data/Magic.cs) against DATA.OVL and CAST.OVL's rules.</summary>
    public class MagicTests
    {
        static Spell[] Spells => Magic.Read(Env.Data.Ovl);

        [U5Fact]
        public void RecipesAreTheBookOfLores()
        {
            var s = Spells;
            var names = Enumerable.Range(0, 8).Select(r => Env.Data.Ovl.ReagentName(r)).ToArray();
            string Recipe(int spell) => string.Join(", ", s[spell].Reagents.Select(r => names[r]));
            Assert.Equal("In Lor", s[Magic.InLor].Words);
            Assert.Equal("Sulfur Ash", Recipe(Magic.InLor));
            Assert.Equal("Sulfur Ash, Blk. Pearl", Recipe(Magic.GravPor));
            Assert.Equal("Ginseng, Sp. Silk", Recipe(Magic.Mani));
            Assert.Equal("Ginseng, Garlic", Recipe(Magic.AnNox));
            Assert.Equal("In Vas Por Ylem", s[Magic.InVasPorYlem].Words);
            Assert.Equal("Sulfur Ash, Blood Moss, Mandrake", Recipe(Magic.InVasPorYlem));
            Assert.Equal("Sulfur Ash, Ginseng, Garlic, Sp. Silk, Blood Moss, Mandrake", Recipe(Magic.InManiCorp));
            // every spell needs something; some share a recipe (the spell is named before mixing)
            Assert.All(s, sp => Assert.NotEqual(0, sp.Recipe));
            Assert.Equal(s[Magic.AnZu].Recipe, s[Magic.AnNox].Recipe);
        }

        [U5Fact]
        public void PlacesAreTheOriginals()
        {
            var s = Spells;
            // light anywhere but in a fight; healing anywhere; the missiles in a fight only;
            // the wind and In Wis outdoors only; the ladders in dungeons only
            Assert.Equal(SpellPlace.Overworld | SpellPlace.Town | SpellPlace.Dungeon, s[Magic.InLor].Places);
            Assert.Equal(SpellPlace.Combat | SpellPlace.Dungeon | SpellPlace.Town | SpellPlace.Overworld, s[Magic.Mani].Places);
            Assert.Equal(SpellPlace.Combat, s[Magic.GravPor].Places);
            Assert.Equal(SpellPlace.Overworld, s[Magic.RelHur].Places);
            Assert.Equal(SpellPlace.Overworld, s[Magic.InWis].Places);
            Assert.Equal(SpellPlace.Dungeon, s[Magic.UusPor].Places);
            Assert.Equal(SpellPlace.Overworld | SpellPlace.Combat, s[Magic.InPor].Places);
        }

        [U5Fact]
        public void TheCastsChecksComeInTheOriginalsOrder()
        {
            var s = Spells;
            var vasFlam = s[Magic.VasFlam]; // circle 3, combat only
            Assert.Equal(3, vasFlam.Circle);
            Assert.Equal(CastCheck.Absorbed, Magic.Check(vasFlam, SpellPlace.Combat, Magic.Stonegate, true, 1, 30, 8));
            Assert.Equal(CastCheck.Absorbed, Magic.Check(vasFlam, SpellPlace.Combat, Magic.BlackthornsPalace, false, 1, 30, 8));
            Assert.Equal(CastCheck.Ok, Magic.Check(vasFlam, SpellPlace.Combat, Magic.BlackthornsPalace, true, 1, 30, 8));
            Assert.Equal(CastCheck.NotHere, Magic.Check(vasFlam, SpellPlace.Town, 2, false, 1, 30, 8));
            Assert.Equal(CastCheck.NoneMixed, Magic.Check(vasFlam, SpellPlace.Combat, 0, false, 0, 30, 8));
            Assert.Equal(CastCheck.ManaTooLow, Magic.Check(vasFlam, SpellPlace.Combat, 0, false, 1, 2, 8));
            Assert.Equal(CastCheck.LevelTooLow, Magic.Check(vasFlam, SpellPlace.Combat, 0, false, 1, 3, 2));
            Assert.Equal(CastCheck.Ok, Magic.Check(vasFlam, SpellPlace.Combat, 0, false, 1, 3, 3));
        }

        [Fact]
        public void MixingSpendsOnePerMixtureOfEachChosenReagent()
        {
            var held = new Dictionary<int, int> { [0] = 5, [5] = 2, [1] = 0 };
            int Held(int r) => held.TryGetValue(r, out int n) ? n : 0;
            Assert.Equal(5, Magic.MostToMix(Magic.MaskOf(new[] { 0 }), Held));
            Assert.Equal(2, Magic.MostToMix(Magic.MaskOf(new[] { 0, 5 }), Held));
            Assert.Equal(0, Magic.MostToMix(Magic.MaskOf(new[] { 0, 1 }), Held));
            Assert.Equal(0, Magic.MostToMix(0, Held));
            Assert.Equal(0x84, Magic.MaskOf(new[] { 0, 5 }));
            Assert.Equal(new List<int> { 0, 5 }, Magic.ReagentsOf(0x84));
            var grav = new Spell { Index = Magic.GravPor, Recipe = 0x84 };
            Assert.True(Magic.Mixes(grav, 0x84));
            Assert.False(Magic.Mixes(grav, 0x80));
            Assert.False(Magic.Mixes(grav, 0));
        }

        [Fact]
        public void ManaIsIntelligenceAndANewAvatarHasItAll()
        {
            var a = Avatar.Load(new Dictionary<string, string> { ["u5_avatar_int"] = "22" });
            Assert.Equal(22, Magic.MaxMana(a));
            Assert.Equal(22, a.Mana);
            var spent = Avatar.Load(new Dictionary<string, string> { ["u5_avatar_int"] = "22", ["u5_avatar_mp"] = "3" });
            Assert.Equal(3, spent.Mana);
        }

        [Fact]
        public void EvasionIsTheIntelligenceRoll()
        {
            // equal intelligence: rolls 1..14 evade (under 15), 15..30 do not
            Assert.True(Magic.Evades(20, 20, 14));
            Assert.False(Magic.Evades(20, 20, 15));
            // a dull creature against a sage hardly ever evades
            Assert.Equal(0, Enumerable.Range(1, 30).Count(r => Magic.Evades(30, 2, r)));
        }

        [Fact]
        public void PotionsSurpriseOneTimeInEight()
        {
            var rnd = new Random(5);
            int n = 16000, same = 0, orange = 0;
            for (int i = 0; i < n; i++)
            {
                int p = Magic.Drunk(Magic.Yellow, rnd);
                if (p == Magic.Yellow) same++;
                if (p == Magic.Orange) orange++;
            }
            // 14/16 plus 1/16 × 1/8 still yellow; orange 1/16 plus 1/128
            Assert.InRange(same / (double)n, 0.87, 0.89);
            Assert.InRange(orange / (double)n, 0.06, 0.08);
        }

        [Fact]
        public void TimesAreTheOriginals()
        {
            Assert.Equal(20, Magic.StatusTurns(Magic.InSanct, false));
            Assert.Equal(100, Magic.StatusTurns(Magic.InSanct, true));
            Assert.Equal(30, Magic.StatusTurns(Magic.RelTym, false));
            Assert.Equal(10, Magic.StatusTurns(Magic.AnTym, false));
            Assert.Equal(255, Magic.LightMinutes(Magic.VasLor, false));
            Assert.Equal(240, Magic.LightMinutes(Magic.VasLor, true));
            // Valheim's 30-minute day: In Lor's 100 minutes are a little over two minutes
            Assert.Equal(125.0, (double)Magic.MinutesToSeconds(100, 1800f), 3);
        }

        [Fact]
        public void InWisSpeaksInRuneLetters()
        {
            // Britain's gate, tile (81, 106): y 0x6a → "G'K", x 0x51 → "F'B"
            Assert.Equal("G'K\", F'B\"", Magic.RunePosition(81, 106));
        }

        [U5Fact]
        public void MissilesHitForTheirWeaponsAttack()
        {
            var ovl = Env.Data.Ovl;
            Assert.Equal(16, ovl.ItemAttack(Magic.MissileWeapon(Magic.GravPor)));
            Assert.Equal(30, ovl.ItemAttack(Magic.MissileWeapon(Magic.VasFlam)));
            Assert.Equal(99, ovl.ItemAttack(Magic.MissileWeapon(Magic.XenCorp)));
            Assert.Equal(21, ovl.ItemAttack(Magic.MissileWeapon(Magic.InFlamGrav)));
        }

        [Fact]
        public void SummonsAreTheOriginals()
        {
            var counts = Enumerable.Range(0, 16).GroupBy(Magic.SummonedAnimal).ToDictionary(g => g.Key, g => g.Count());
            Assert.Equal(6, counts[0x14]);
            Assert.Equal(5, counts[0x16]);
            Assert.Equal(3, counts[0x15]);
            Assert.Equal(2, counts[0x22]);
            Assert.Equal(50, Magic.ExperienceAfterResurrection(100, 50));
            Assert.Equal(100, Magic.ExperienceAfterResurrection(100, 99));
        }
    }
}
