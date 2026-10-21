using Bretaheim.WorldGen.Core;

namespace Bretaheim.WorldGen
{
    /// <summary>
    /// Britannia's date and time in the loaded world (MASTER_PLAN §5.8; arithmetic in
    /// Core/UltimaCalendar): one Valheim day is one Ultima day of 24 hours (75 s an hour at
    /// Valheim's 30-minute day, midnight at the start of the Valheim day), and the world's first
    /// day is INIT.GAM's date (4-5-139). It is read from Valheim's clock, which the server keeps
    /// and sends to every client and which sleeping, the inns and the jail move on, so every peer
    /// agrees without anything stored or sent.
    /// <code>
    ///   Calendar.Now          // UltimaDate: Year, Month (1..13), Day (1..28), Hour, Minute; DateText "4-5-139", TimeText "8:35"
    ///   Calendar.Hour         // 0..23 (the townsfolk's schedules, shop hours, the moongates)
    ///   Calendar.WorldDay     // days since the world began (0 on its first)
    ///   Calendar.Moons        // Trammel and Felucca (Core/Moons)
    /// </code>
    /// </summary>
    public static class Calendar
    {
        static UltimaDate? start;
        static Moons moons;

        /// <summary>INIT.GAM's date and time: the first day of every Britannia world.</summary>
        public static UltimaDate Start
        {
            get
            {
                if (start == null)
                {
                    var g = Plugin.U5?.InitialGame;
                    start = g != null ? new UltimaDate(g.Year, g.Month, g.Day, g.Hour, g.Minute) : new UltimaDate(139, 4, 5, 8, 35);
                }
                return start.Value;
            }
        }

        /// <summary>Whether a world's clock is running (else <see cref="Now"/> is noon of the first day).</summary>
        public static bool Running => ZNet.instance != null && EnvMan.instance != null;

        /// <summary>Valheim's world time (s) and day length (s).</summary>
        public static double Seconds => ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0;
        public static double DayLength => EnvMan.instance != null && EnvMan.instance.m_dayLengthSec > 0 ? EnvMan.instance.m_dayLengthSec : 1800;

        /// <summary>The Ultima date and time now.</summary>
        public static UltimaDate Now => Running ? UltimaCalendar.At(Start, Seconds, DayLength) : Start.WithTime(12, 0);

        /// <summary>The Ultima hour now (0..23).</summary>
        public static int Hour => Now.Hour;
        public static int Minute => Now.Minute;

        /// <summary>Days since the world began (0 on its first day).</summary>
        public static long WorldDay => Running ? UltimaCalendar.WorldDay(Seconds, DayLength) : 0;

        /// <summary>Trammel and Felucca (DATA.OVL's phase table).</summary>
        public static Moons Moons => moons ?? (moons = Moons.Of(Plugin.U5.Ovl));

        /// <summary>Valheim seconds until the next start of an Ultima hour (for "skiptime").</summary>
        public static double SecondsUntil(int hour) => UltimaCalendar.NextHour(Seconds, DayLength, hour) - Seconds;

        /// <summary>"It is 8:35 on 4-5-139; Trammel …, Felucca …".</summary>
        public static string Describe()
        {
            var now = Now;
            return $"It is {now.TimeText} on {now.DateText} (day {WorldDay + 1} of this world). {Moons.Describe(now)}.";
        }
    }

    /// <summary>World state kept as Valheim global keys ("name value"): saved with the world, set through the server, sent to every client.</summary>
    public static class WorldKeys
    {
        public static string Get(string name)
            => ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(name, out string value) ? value : null;

        public static void Set(string name, string value)
        {
            if (ZoneSystem.instance == null) return;
            ZoneSystem.instance.SetGlobalKey(string.IsNullOrEmpty(value) ? name : name + " " + value);
        }
    }

    /// <summary>
    /// "u5 time" (the date, the moons, the Shadowlords), "u5 moons" (and the gates: where each
    /// moonstone is and where the gates lead now), "u5 dusk" / "u5 dawn" / "u5 midnight" (with
    /// devcommands, on the server or host: the clock moved on to 20:00, 5:00 or 0:00; elsewhere the
    /// "skiptime" to type).
    /// </summary>
    public static class ClockCommands
    {
        public static bool Run(string[] args, System.Action<string> print)
        {
            if (args.Length == 0) return false;
            switch (args[0])
            {
                case "time":
                case "date":
                    print(Calendar.Describe());
                    if (Britannia.Active != null) print(Shadowlords.Describe());
                    return true;
                case "moons":
                case "gates":
                    print(Calendar.Describe());
                    if (Britannia.Active == null) return true;
                    var me = Player.m_localPlayer;
                    for (int i = 0; i < 8; i++)
                    {
                        var site = Moongates.Site(i);
                        var at = Moongates.Position(site);
                        string far = at.HasValue && me != null ? $", {UnityEngine.Vector3.Distance(at.Value, me.transform.position):0} m away" : "";
                        print($"moonstone {i}: {site}{(at.HasValue ? $" at ({at.Value.x:0}, {at.Value.z:0})" : "")}{far}");
                    }
                    return true;
                case "dusk": return Skip(20, print);
                case "dawn": return Skip(5, print);
                case "midnight": return Skip(0, print);
            }
            return false;
        }

        static bool Skip(int hour, System.Action<string> print)
        {
            if (!Calendar.Running) { print("no world is loaded"); return true; }
            double s = System.Math.Ceiling(Calendar.SecondsUntil(hour)) + 1;
            if (!Terminal.m_cheat || !ZNet.instance.IsServer())
            {
                print($"{hour}:00 is {s:0} s away: type \"skiptime {s:0}\" (devcommands; an admin on a dedicated server)");
                return true;
            }
            ZNet.instance.SetNetTime(ZNet.instance.GetTimeSeconds() + s);
            print($"skipped {s:0} s: {Calendar.Describe()}");
            return true;
        }
    }
}
