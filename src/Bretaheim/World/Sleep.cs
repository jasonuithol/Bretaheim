using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Bretaheim.WorldGen
{
    /// <summary>
    /// Sleeping in Britannia: in an inn's bed (Inns) or under a camp's lean-to (Camp). As in
    /// Valheim, the night passes only when every player is lying down; as in the original, the
    /// party may sleep by day too (eight hours pass). Sleeping at a camp may end in an ambush
    /// ("Ambushed!"): the time stops passing, everyone wakes, and the monsters come.
    /// </summary>
    public static class Sleep
    {
        const string WakeRpc = "U5_Wake";
        /// <summary>How long a day's sleep lasts (Britannia hours), and the chance a camp is ambushed.</summary>
        const float DayHours = 8f, AmbushChance = 0.25f, CampNear = 6f, AmbushFrom = 10f, AmbushTo = 18f;
        /// <summary>"Good morning" only on waking in the morning (these hours) after this much sleep.</summary>
        const int MorningFrom = 5, MorningTo = 11;
        const float GoodNight = 6f;
        // DATA.OVL (DGROUP)
        const int SZzz = 0x41d4, SAmbushed = 0x41e0;
        static readonly System.Random random = new System.Random();

        /// <summary>The hours a player sleeping at a camp asked for (on their ZDO; 0 elsewhere).</summary>
        const string HoursKey = "u5_sleep_hours";

        public static void Install(Harmony harmony)
        {
            var update = AccessTools.Method(typeof(Game), "UpdateSleeping");
            if (update != null) harmony.Patch(update, prefix: new HarmonyMethod(typeof(Sleep), nameof(UpdateSleeping_Prefix)), postfix: new HarmonyMethod(typeof(Sleep), nameof(UpdateSleeping_Postfix)));
            var start = AccessTools.Method(typeof(Game), "Start");
            if (start != null) harmony.Patch(start, postfix: new HarmonyMethod(typeof(Sleep), nameof(GameStart_Postfix)));
            var setSleeping = AccessTools.Method(typeof(Player), "SetSleeping");
            if (setSleeping != null) harmony.Patch(setSleeping, prefix: new HarmonyMethod(typeof(Sleep), nameof(SetSleeping_Prefix)));
        }

        static void GameStart_Postfix() => ZRoutedRpc.instance?.Register(WakeRpc, sender => Wake());

        /// <summary>Camping: how many hours this player will sleep (an inn's bed: until morning).</summary>
        public static void SetHours(Player p, int hours) => p?.GetComponent<ZNetView>()?.GetZDO()?.Set(HoursKey, hours);

        /// <summary>Lies the player down to sleep at this spot (a bed's or a lean-to's).</summary>
        public static void LieDown(Player p, Transform spot, GameObject bed)
        {
            // (only a camp asks for hours: anywhere else sleeps the night)
            if (bed == null || bed.GetComponent<LeanTo>() == null) SetHours(p, 0);
            p.AttachStart(spot, bed, hideWeapons: true, isBed: true, onShip: false, "attach_bed", new Vector3(0f, 0.5f, 0f));
            p.Message(MessageHud.MessageType.Center, Plugin.U5.Ovl.CStr(SZzz).Trim());
        }

        static bool wasSleeping;

        /// <summary>
        /// On the server, before Valheim's own check: when everyone is lying down and all of them at
        /// camps, the hours they asked for pass (the fewest asked, so nobody oversleeps), by day or night.
        /// </summary>
        static bool UpdateSleeping_Prefix(Game __instance)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || Britannia.Active == null || EnvMan.instance == null) return true;
            var t = Traverse.Create(__instance);
            if (t.Field("m_sleeping").GetValue<bool>() || EnvMan.instance.IsTimeSkipping()) return true;
            var sleepers = ZNet.instance.GetAllCharacterZDOS();
            if (sleepers.Count == 0 || !sleepers.All(z => z.GetBool(ZDOVars.s_inBed) && z.GetInt(HoursKey) > 0)) return true;
            if (ZNet.instance.GetTimeSeconds() - t.Field("m_lastSleepTime").GetValue<double>() < 10.0) return true;
            int hours = sleepers.Min(z => z.GetInt(HoursKey));
            Pass(EnvMan.instance, hours);
            t.Field("m_sleeping").SetValue(true);
            ZRoutedRpc.instance.InvokeRoutedRPC(0L, "SleepStart");
            Plugin.Log.LogInfo($"The camp sleeps {hours} hour{(hours == 1 ? "" : "s")}");
            return false;
        }

        /// <summary>Time passes this many Britannia hours (quickly, as sleeping does).</summary>
        static void Pass(EnvMan env, float hours)
        {
            double now = ZNet.instance.GetTimeSeconds(), to = now + env.m_dayLengthSec * hours / 24.0;
            var e = Traverse.Create(env);
            e.Field("m_skipTime").SetValue(true);
            e.Field("m_skipToTime").SetValue(to);
            e.Field("m_timeSkipSpeed").SetValue((to - now) / 12.0);
        }

        /// <summary>On the server, after Valheim's own check: sleeping by day, and the camp's ambush when sleep begins.</summary>
        static void UpdateSleeping_Postfix(Game __instance)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || Britannia.Active == null || EnvMan.instance == null) return;
            var t = Traverse.Create(__instance);
            bool sleeping = t.Field("m_sleeping").GetValue<bool>();
            var env = EnvMan.instance;
            if (!sleeping && !env.IsTimeSkipping() && !(EnvMan.IsAfternoon() || EnvMan.IsNight())
                && t.Method("EverybodyIsTryingToSleep").GetValue<bool>()
                && ZNet.instance.GetTimeSeconds() - t.Field("m_lastSleepTime").GetValue<double>() >= 10.0)
            {
                // by day, as the original allows: eight hours pass
                Pass(env, DayHours);
                t.Field("m_sleeping").SetValue(true);
                ZRoutedRpc.instance.InvokeRoutedRPC(0L, "SleepStart");
                sleeping = true;
                Plugin.Log.LogInfo("Everyone sleeps by day: eight hours pass");
            }
            if (sleeping && !wasSleeping) Ambush(env);
            wasSleeping = sleeping;
        }

        /// <summary>A party sleeping at a camp may be ambushed: the time stops, everyone wakes, the monsters come.</summary>
        static void Ambush(EnvMan env)
        {
            var zdoCampers = ZNet.instance.GetAllCharacterZDOS().Where(z => z.GetBool(ZDOVars.s_inBed) && Camp.Near(z.GetPosition(), CampNear)).ToList();
            if (zdoCampers.Count == 0 || random.NextDouble() >= AmbushChance) return;
            var at = zdoCampers[0].GetPosition();
            Traverse.Create(env).Field("m_skipTime").SetValue(false);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, WakeRpc);
            var what = UltimaSpawner.Encounter(at, "the camp", random, AmbushFrom, AmbushTo);
            Plugin.Log.LogInfo($"The camp is ambushed: {what ?? "nothing could come"}");
        }

        static double asleepAt = -1;
        static bool ambushed;

        /// <summary>
        /// Waking: "Good morning" (and Valheim's rested) only in the morning after a good night's
        /// sleep (six hours or more); an ambush wakes the sleeper with neither.
        /// </summary>
        static bool SetSleeping_Prefix(Player __instance, bool sleep)
        {
            if (Britannia.Active == null || __instance != Player.m_localPlayer || ZNet.instance == null || EnvMan.instance == null) return true;
            bool was = __instance.IsSleeping();
            if (sleep && !was) { asleepAt = ZNet.instance.GetTimeSeconds(); ambushed = false; return true; }
            if (sleep || !was) return true;
            double slept = asleepAt < 0 ? 0 : (ZNet.instance.GetTimeSeconds() - asleepAt) / EnvMan.instance.m_dayLengthSec * 24.0;
            int hour = TownNpc.Hour();
            bool goodMorning = !ambushed && slept >= GoodNight && hour >= MorningFrom && hour <= MorningTo;
            asleepAt = -1;
            if (goodMorning) return true;
            // up without "Good morning" (no Valheim Rested in Britannia: AvatarStats)
            Traverse.Create(__instance).Field("m_sleeping").SetValue(false);
            Traverse.Create(__instance).Field("m_wakeupTime").SetValue(ZNet.instance.GetTimeSeconds());
            ambushed = false;
            return false;
        }

        /// <summary>Everyone up: the local player gets out of bed.</summary>
        static void Wake()
        {
            var me = Player.m_localPlayer;
            if (me == null || !me.InBed()) return;
            ambushed = true;
            me.AttachStop();
            me.Message(MessageHud.MessageType.Center, Plugin.U5.Ovl.CStr(SAmbushed).Trim());
        }
    }
}
