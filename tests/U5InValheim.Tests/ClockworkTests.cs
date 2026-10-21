using System.Collections.Generic;
using System.Linq;
using U5InValheim.Data;
using U5InValheim.WorldGen.Core;
using Xunit;

namespace U5InValheim.Tests
{
    /// <summary>Britannia's clock, its moons and moonstones, and the Shadowlords' travels (World/Core/Clockwork.cs).</summary>
    public class ClockworkTests
    {
        const double Day = 1800;
        static readonly UltimaDate Start = new UltimaDate(139, 4, 5, 8, 35);

        [Fact]
        public void TheCalendarRollsOverAsPassTimeDoes()
        {
            Assert.Equal(new UltimaDate(139, 4, 6, 8, 35), Start.AddDays(1));
            // 28-day months
            Assert.Equal(new UltimaDate(139, 5, 1, 8, 35), Start.AddDays(24));
            // 13 months a year
            Assert.Equal(new UltimaDate(140, 1, 5, 8, 35), Start.AddDays(10 * 28));
            Assert.Equal(new UltimaDate(140, 4, 5, 8, 35), Start.AddDays(364));
            Assert.Equal(Start, Start.AddDays(1000).AddDays(-1000));
            Assert.Equal("4-5-139 8:35", Start.ToString());
        }

        [Fact]
        public void OneValheimDayIsOneUltimaDay()
        {
            // a new world's clock starts at 2040 s: day 1, 3:12, the starting date
            var first = UltimaCalendar.At(Start, 2040, Day);
            Assert.Equal(new UltimaDate(139, 4, 5, 3, 12), first);
            Assert.Equal(0, UltimaCalendar.WorldDay(2040, Day));
            // 75 s an hour, midnight at the start of the Valheim day
            Assert.Equal(new UltimaDate(139, 4, 5, 20, 0), UltimaCalendar.At(Start, Day + 20 * 75, Day));
            Assert.Equal(new UltimaDate(139, 4, 6, 0, 0), UltimaCalendar.At(Start, 2 * Day, Day));
            Assert.Equal(new UltimaDate(139, 4, 5, 23, 59), UltimaCalendar.At(Start, 2 * Day - 0.01, Day));
            Assert.Equal(new UltimaDate(139, 5, 2, 12, 0), UltimaCalendar.At(Start, 26.5 * Day, Day));
            // (a clock before day 1 is still the first day)
            Assert.Equal(Start.WithTime(12, 0), UltimaCalendar.At(Start, 0.5 * Day, Day));
            // the next 20:00 (dusk for the moongates)
            Assert.Equal(Day + 20 * 75, UltimaCalendar.NextHour(Day + 100, Day, 20));
            Assert.Equal(2 * Day + 20 * 75, UltimaCalendar.NextHour(Day + 21 * 75, Day, 20));
        }

        [U5Fact]
        public void TheMoonsAreTheOriginalsTable()
        {
            var moons = Moons.Of(Env.Data.Ovl);
            // day 1 both new; Felucca turns every day, Trammel every three or four
            Assert.Equal((0, 0), (moons.Trammel(1), moons.Felucca(1)));
            Assert.Equal((1, 0), (moons.Trammel(2), moons.Felucca(2)));
            Assert.Equal((2, 3), (moons.Trammel(5), moons.Felucca(5)));
            Assert.Equal((7, 0), (moons.Trammel(28), moons.Felucca(28)));
            for (int d = 1; d <= 28; d++)
            {
                Assert.InRange(moons.Trammel(d), 0, 7);
                Assert.InRange(moons.Felucca(d), 0, 7);
            }
            Assert.Equal(8, Enumerable.Range(1, 28).Select(moons.Trammel).Distinct().Count());
        }

        [U5Fact]
        public void TheGatesLeadByTheMoons()
        {
            var moons = Moons.Of(Env.Data.Ovl);
            Assert.True(Moons.GatesUp(20));
            Assert.True(Moons.GatesUp(4));
            Assert.False(Moons.GatesUp(5));
            Assert.False(Moons.GatesUp(19));
            // after noon (the evening) Felucca's phase, before (after midnight) Trammel's
            Assert.Equal(3, moons.Destination(new UltimaDate(139, 4, 5, 21, 0)));
            Assert.Equal(2, moons.Destination(new UltimaDate(139, 4, 5, 3, 0)));
            // the first ten minutes after midnight: the Shrine of Spirituality
            Assert.Equal(Moons.Spirituality, moons.Destination(new UltimaDate(139, 4, 5, 0, 9)));
            Assert.Equal(moons.Trammel(5), moons.Destination(new UltimaDate(139, 4, 5, 0, 10)));
            Assert.Contains("Felucca", moons.Describe(new UltimaDate(139, 4, 5, 21, 0)));
        }

        [U5Fact]
        public void TheMoonstonesStartBuriedInGrassland()
        {
            var init = Env.Data.InitialGame;
            var sites = new HashSet<(int, int)>();
            for (int i = 0; i < 8; i++)
            {
                var m = init.Moonstone(i);
                Assert.True(m.buried);
                Assert.Equal(0, m.z);
                // in open grassland (not at the 0xa4 signposts)
                Assert.Equal(5, Env.Data.Britannia[m.x, m.y]);
                sites.Add((m.x, m.y));
            }
            Assert.Equal(8, sites.Count);
            Assert.Equal((139, 4, 5), (init.Year, init.Month, init.Day));
            // the Shadowlords start in no town
            Assert.Equal(new[] { 0, 0, 0 }, Enumerable.Range(0, 3).Select(init.ShadowlordLocation).ToArray());
        }

        [Fact]
        public void MoonstoneSitesAreKeptAsText()
        {
            foreach (var s in new[] { MoonstoneSite.At(224, 133), MoonstoneSite.At(12, 250, -1), MoonstoneSite.At(3, 4, 0, 123.5f, -4021.25f), MoonstoneSite.InPack })
            {
                Assert.True(MoonstoneSite.TryParse(s.Format(), out var back), s.Format());
                Assert.Equal(s, back);
            }
            Assert.Equal("carried", MoonstoneSite.InPack.Format());
            Assert.False(MoonstoneSite.TryParse("1,2", out _));
            Assert.False(MoonstoneSite.TryParse("", out _));
            // soft ground only
            Assert.True(MoonstoneSite.CanBuryOn(5));
            Assert.True(MoonstoneSite.CanBuryOn(0x2d));
            Assert.False(MoonstoneSite.CanBuryOn(1));
            Assert.False(MoonstoneSite.CanBuryOn(0x0c));
        }

        [Fact]
        public void TheShadowlordsMoveEveryMidnight()
        {
            var routes = new ShadowlordRoutes(12345, new[] { 0, 0, 0 });
            Assert.Equal(new[] { 0, 0, 0 }, routes.On(0));
            Assert.Equal(0, routes.TownOf(1, 0));
            var visits = new int[9];
            int[] before = routes.On(0);
            for (long d = 1; d <= 600; d++)
            {
                var at = routes.On(d);
                Assert.Equal(3, at.Distinct().Count());
                for (int i = 0; i < 3; i++)
                {
                    Assert.InRange(at[i], 1, 8);
                    // never staying, never where another still is (those after it move later)
                    Assert.NotEqual(before[i], at[i]);
                    for (int j = i + 1; j < 3; j++) Assert.NotEqual(before[j], at[i]);
                    visits[at[i]]++;
                }
                before = at;
            }
            // every town is visited
            Assert.All(Enumerable.Range(1, 8), t => Assert.True(visits[t] > 100, $"town {t}: {visits[t]}"));
            // the same on every peer and whatever was asked before
            var other = new ShadowlordRoutes(12345, new[] { 0, 0, 0 });
            Assert.Equal(routes.On(37), other.On(37));
            Assert.Equal(routes.On(5), other.On(5));
            Assert.NotEqual(Enumerable.Range(1, 30).Select(d => new ShadowlordRoutes(1, new[] { 0, 0, 0 }).On(d)[0]),
                            Enumerable.Range(1, 30).Select(d => new ShadowlordRoutes(2, new[] { 0, 0, 0 }).On(d)[0]));
            // who is where
            var day9 = routes.On(9);
            Assert.Equal(1, routes.In(day9[1], 9));
            Assert.Equal(-1, routes.In(Enumerable.Range(1, 8).First(t => !day9.Contains(t)), 9));
            // a destroyed one (0xff) stays out of the towns
            var two = new ShadowlordRoutes(7, new[] { 0, 0xff, 0 });
            Assert.All(Enumerable.Range(1, 50), d => Assert.Equal(0xff, two.On(d)[1]));
        }

        [Fact]
        public void HatredAndCowardiceTouchHalfTheTownsfolk()
        {
            var routes = new ShadowlordRoutes(99, new[] { 0, 0, 0 });
            var times = new byte[] { 8, 12, 20, 0 };
            int touched = Enumerable.Range(0, 1000).Count(i => routes.Affected(i / 32, 2, i % 32, times, 0x48));
            Assert.InRange(touched, 400, 600);
            // nobody without a schedule; nobody when the settlement's fifth NPC is no person (the original's slip, kept)
            Assert.Equal(0, Enumerable.Range(0, 100).Count(i => routes.Affected(i, 2, 5, new byte[4], 0x48)));
            Assert.Equal(0, Enumerable.Range(0, 100).Count(i => routes.Affected(i, 2, 5, times, 0x80)));
            Assert.Equal(routes.Affected(3, 2, 7, times, 0x48), new ShadowlordRoutes(99, new[] { 0, 0, 0 }).Affected(3, 2, 7, times, 0x48));
        }
    }
}
