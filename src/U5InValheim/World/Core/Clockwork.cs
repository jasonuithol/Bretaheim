using System;
using System.Collections.Generic;
using System.Globalization;
using U5InValheim.Data;

namespace U5InValheim.WorldGen.Core
{
    /// <summary>
    /// A moment of Britannia's calendar (ROOT pass_time): 60 minutes, 24 hours, 28-day months,
    /// 13 months a year. Written as the original's status line has it, month-day-year.
    /// </summary>
    public readonly struct UltimaDate : IEquatable<UltimaDate>
    {
        public const int DaysPerMonth = 28, MonthsPerYear = 13, DaysPerYear = DaysPerMonth * MonthsPerYear, MinutesPerDay = 24 * 60;

        public readonly int Year, Month, Day, Hour, Minute;

        public UltimaDate(int year, int month, int day, int hour = 0, int minute = 0)
        {
            Year = year; Month = month; Day = day; Hour = hour; Minute = minute;
        }

        /// <summary>Days since month 1, day 1 of year 0 (to count days between dates).</summary>
        public long DayNumber => ((long)Year * MonthsPerYear + (Month - 1)) * DaysPerMonth + (Day - 1);

        public static UltimaDate FromDayNumber(long n, int hour, int minute)
        {
            long months = n / DaysPerMonth;
            return new UltimaDate((int)(months / MonthsPerYear), (int)(months % MonthsPerYear) + 1, (int)(n % DaysPerMonth) + 1, hour, minute);
        }

        /// <summary>The same time of day, so many days on (pass_time's roll-overs).</summary>
        public UltimaDate AddDays(long days) => FromDayNumber(DayNumber + days, Hour, Minute);

        public UltimaDate WithTime(int hour, int minute) => new UltimaDate(Year, Month, Day, hour, minute);

        /// <summary>"4-5-139".</summary>
        public string DateText => $"{Month}-{Day}-{Year}";
        /// <summary>"8:35".</summary>
        public string TimeText => $"{Hour}:{Minute:00}";
        public override string ToString() => $"{DateText} {TimeText}";

        public bool Equals(UltimaDate o) => Year == o.Year && Month == o.Month && Day == o.Day && Hour == o.Hour && Minute == o.Minute;
        public override bool Equals(object obj) => obj is UltimaDate d && Equals(d);
        public override int GetHashCode() => (int)DayNumber * MinutesPerDay + Hour * 60 + Minute;
    }

    /// <summary>
    /// Britannia's clock on Valheim's: one Valheim day is one Ultima day (its 24 hours spread over
    /// the Valheim day, midnight at its start), and the world's first Valheim day is INIT.GAM's
    /// date. Sleeping, the inn and the jail pass Ultima time by passing Valheim time.
    /// </summary>
    public static class UltimaCalendar
    {
        /// <summary>Valheim's day number on which a new world begins (ZNet's clock starts at 2040 s, day 1 of 1800 s days).</summary>
        public const int FirstDay = 1;

        /// <summary>The Valheim day (0-based, as EnvMan.GetDay) of a world time.</summary>
        public static long DayOf(double seconds, double dayLength) => (long)Math.Floor(seconds / dayLength);

        /// <summary>Days since the world began (0 on its first day; never negative).</summary>
        public static long WorldDay(double seconds, double dayLength) => Math.Max(0, DayOf(seconds, dayLength) - FirstDay);

        /// <summary>The Ultima date and time at a world time, from the starting date.</summary>
        public static UltimaDate At(UltimaDate start, double seconds, double dayLength)
        {
            long day = DayOf(seconds, dayLength);
            double into = seconds - day * dayLength;
            int minutes = Math.Min(UltimaDate.MinutesPerDay - 1, Math.Max(0, (int)Math.Floor(into * UltimaDate.MinutesPerDay / dayLength + 1e-9)));
            return start.AddDays(Math.Max(0, day - FirstDay)).WithTime(minutes / 60, minutes % 60);
        }

        /// <summary>The world time at which an Ultima hour next begins (for skipping to it).</summary>
        public static double NextHour(double seconds, double dayLength, int hour)
        {
            long day = DayOf(seconds, dayLength);
            double at = day * dayLength + hour * dayLength / 24;
            return at > seconds ? at : at + dayLength;
        }
    }

    /// <summary>
    /// Trammel and Felucca (DATA.OVL g_moon_phase_table: per day of the month the two phase glyphs
    /// '0'..'7'; draw_sky_strip, update_moongates, enter_moongate). By night (from 20:00 to 4:59)
    /// a moongate stands at every buried moonstone; stepping in carries the traveller to the site
    /// of moonstone n, n the phase of Trammel before noon and of Felucca after (so Felucca's in the
    /// evening, Trammel's after midnight); in the first ten minutes after midnight the gate leads
    /// to the Shrine of Spirituality instead.
    /// </summary>
    public sealed class Moons
    {
        public const int Phases = 8;
        /// <summary><see cref="Destination"/> for the Shrine of Spirituality.</summary>
        public const int Spirituality = -1;
        static readonly string[] names = { "new", "waxing crescent", "first quarter", "waxing gibbous", "full", "waning gibbous", "last quarter", "waning crescent" };

        readonly byte[] table;

        /// <summary>The 28 days' (Trammel, Felucca) glyph pairs.</summary>
        public Moons(byte[] glyphs)
        {
            if (glyphs.Length < UltimaDate.DaysPerMonth * 2) throw new ArgumentException("the moon table has 28 days of two glyphs");
            table = glyphs;
        }

        public static Moons Of(DataOvl ovl) => new Moons(ovl.Bytes(Dg.MoonPhases, UltimaDate.DaysPerMonth * 2));

        static int Glyph(byte b) => (b - '0') & 7;
        public int Trammel(int day) => Glyph(table[(Math.Max(1, Math.Min(UltimaDate.DaysPerMonth, day)) - 1) * 2]);
        public int Felucca(int day) => Glyph(table[(Math.Max(1, Math.Min(UltimaDate.DaysPerMonth, day)) - 1) * 2 + 1]);

        /// <summary>update_moongates: the gates grow by night and shrink by day (5 ≤ hour &lt; 20).</summary>
        public static bool GatesUp(int hour) => hour < 5 || hour >= 20;

        /// <summary>enter_moongate: the moonstone whose site the gate leads to now, or <see cref="Spirituality"/>.</summary>
        public int Destination(UltimaDate now)
        {
            if (now.Hour == 0 && now.Minute < 10) return Spirituality;
            return now.Hour < 12 ? Trammel(now.Day) : Felucca(now.Day);
        }

        public static string PhaseName(int phase) => names[phase & 7];

        /// <summary>"Trammel waxing gibbous, Felucca full" (and where a gate leads now).</summary>
        public string Describe(UltimaDate now)
        {
            string s = $"Trammel {PhaseName(Trammel(now.Day))} ({Trammel(now.Day)}), Felucca {PhaseName(Felucca(now.Day))} ({Felucca(now.Day)})";
            if (!GatesUp(now.Hour)) return s + "; the moongates are down";
            int to = Destination(now);
            return s + (to == Spirituality ? "; the moongates lead to the Shrine of Spirituality" : $"; the moongates lead to moonstone {to}");
        }
    }

    /// <summary>
    /// Where one of the eight moonstones is (INIT.GAM / SAVED.GAM 0x28a..0x2a9: x, y, location
    /// 0 buried or 0xff carried, z): buried at a tile of Britannia (z 0) or the Underworld (z −1),
    /// or carried. One the players buried also keeps the exact spot (Valheim metres).
    /// </summary>
    public struct MoonstoneSite : IEquatable<MoonstoneSite>
    {
        public const string Carried = "carried";
        public bool Buried;
        public int X, Y, Z;
        public float? WorldX, WorldZ;

        public static MoonstoneSite At(int x, int y, int z = 0, float? wx = null, float? wz = null)
            => new MoonstoneSite { Buried = true, X = x, Y = y, Z = z, WorldX = wx, WorldZ = wz };
        public static MoonstoneSite InPack => new MoonstoneSite();

        /// <summary>The text kept in the world (a global key's value).</summary>
        public string Format()
        {
            if (!Buried) return Carried;
            var c = CultureInfo.InvariantCulture;
            string s = $"{X},{Y},{Z}";
            if (WorldX.HasValue && WorldZ.HasValue) s += "," + WorldX.Value.ToString("0.##", c) + "," + WorldZ.Value.ToString("0.##", c);
            return s;
        }

        public static bool TryParse(string text, out MoonstoneSite site)
        {
            site = InPack;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            if (text == Carried) return true;
            var p = text.Split(',');
            if (p.Length != 3 && p.Length != 5) return false;
            var c = CultureInfo.InvariantCulture;
            if (!int.TryParse(p[0], NumberStyles.Integer, c, out int x) || !int.TryParse(p[1], NumberStyles.Integer, c, out int y) || !int.TryParse(p[2], NumberStyles.Integer, c, out int z)) return false;
            site = At(x, y, z);
            if (p.Length == 5)
            {
                if (!float.TryParse(p[3], NumberStyles.Float, c, out float wx) || !float.TryParse(p[4], NumberStyles.Float, c, out float wz)) return false;
                site.WorldX = wx;
                site.WorldZ = wz;
            }
            return true;
        }

        /// <summary>CAST bury_moonstone: only into soft ground (swamp, grass, brush, forest, hills: tiles 4..10; furrows 0x2c/0x2d).</summary>
        public static bool CanBuryOn(byte tile) => tile == 0x2c || tile == 0x2d || (tile > 3 && tile < 0xb);

        public bool Equals(MoonstoneSite o) => Buried == o.Buried && (!Buried || (X == o.X && Y == o.Y && Z == o.Z && WorldX == o.WorldX && WorldZ == o.WorldZ));
        public override bool Equals(object obj) => obj is MoonstoneSite s && Equals(s);
        public override int GetHashCode() => Buried ? (X * 256 + Y) * 4 + Z : -1;
        public override string ToString() => Buried ? $"buried at ({X},{Y}){(Z != 0 ? " below" : "")}" : "carried";
    }

    /// <summary>
    /// The Shadowlords' travels (pass_time): at every midnight each Shadowlord still in Britannia
    /// (location below 0x80) moves to a town 1..8 (Moonglow, Britain, Jhelom, Yew, Minoc, Trinsic,
    /// Skara Brae, New Magincia) where no Shadowlord is. INIT.GAM starts all three at 0 (in no town)
    /// until the first midnight. The original's random draws are a draw seeded by the world and the
    /// day here, so every peer knows where they are without asking; the original also keeps them
    /// out of the party's town at midnight, which has no meaning with many players and is left out.
    /// A destroyed Shadowlord still draws its town (unseen), so the others' travels do not change
    /// when it falls.
    /// </summary>
    public sealed class ShadowlordRoutes
    {
        public const int Count = 3, Towns = 8;
        public static readonly string[] Names = { "Faulinei", "Astaroth", "Nosfentor" };
        /// <summary>Falsehood, Hatred, Cowardice (DATA.OVL "An air of …").</summary>
        public static readonly string[] Airs = { "falsehood", "hatred", "cowardice" };

        readonly ulong seed;
        readonly int[] start;
        long cachedDay = -1;
        int[] cached;

        public ShadowlordRoutes(int worldSeed, IReadOnlyList<int> startLocations)
        {
            seed = (ulong)(uint)worldSeed * 0x9E3779B97F4A7C15UL ^ 0x5348414457UL;
            start = new int[Count];
            for (int i = 0; i < Count; i++) start[i] = i < startLocations.Count ? startLocations[i] : 0;
        }

        /// <summary>Each Shadowlord's location on day <paramref name="day"/> of the world (0 = the first: INIT.GAM's).</summary>
        public int[] On(long day)
        {
            if (day < 0) day = 0;
            if (cached == null || day < cachedDay)
            {
                cached = (int[])start.Clone();
                cachedDay = 0;
            }
            while (cachedDay < day)
            {
                cachedDay++;
                Midnight(cached, new Draw(seed ^ (ulong)cachedDay * 0xD1B54A32D192ED03UL));
            }
            return (int[])cached.Clone();
        }

        /// <summary>The town a Shadowlord is in on a day (1..8), or 0 (none, or not yet in a town).</summary>
        public int TownOf(int shadowlord, long day)
        {
            int loc = On(day)[shadowlord];
            return loc >= 1 && loc <= Towns ? loc : 0;
        }

        /// <summary>Which Shadowlord is in a location on a day, or −1.</summary>
        public int In(int location, long day)
        {
            var at = On(day);
            for (int i = 0; i < Count; i++) if (at[i] == location) return i;
            return -1;
        }

        static void Midnight(int[] locs, Draw draw)
        {
            for (int sl = 0; sl < Count; sl++)
            {
                if (locs[sl] >= 0x80) continue;
                int town;
                do
                {
                    town = 1 + draw.Next(Towns);
                    for (int j = 0; j < Count; j++) if (locs[j] == town) town = 0;
                } while (town == 0);
                locs[sl] = town;
            }
        }

        /// <summary>
        /// TOWN npc_affected_by_shadowlord: a townsperson with a schedule (any of its four time bytes
        /// set) is touched by Hatred or Cowardice one time in two; the original reads the type of the
        /// settlement's NPC 4 instead of the person's own (kept: outside 0x40..0x73 nobody is touched).
        /// The coin is the day's and the person's, the same on every peer.
        /// </summary>
        public bool Affected(long day, int location, int slot, byte[] times, byte npc4Type)
        {
            bool scheduled = false;
            foreach (var t in times) if (t != 0) scheduled = true;
            if (!scheduled || npc4Type < 0x40 || npc4Type > 0x73) return false;
            var d = new Draw(seed ^ ((ulong)day << 16) ^ ((ulong)location << 8) ^ (ulong)slot ^ 0xA5A5A5A5UL);
            return d.Next(2) == 0;
        }

        /// <summary>splitmix64.</summary>
        struct Draw
        {
            ulong s;
            public Draw(ulong seed) { s = seed; }
            public int Next(int n)
            {
                s += 0x9E3779B97F4A7C15UL;
                ulong z = s;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                z ^= z >> 31;
                return (int)(z % (ulong)n);
            }
        }
    }
}
