using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace U5InValheim.WorldGen
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
        const float DayHours = 8f, AmbushChance = 0.25f, CampNear = 6f;
        // DATA.OVL (DGROUP)
        const int SZzz = 0x41d4, SAmbushed = 0x41e0;
        static readonly System.Random random = new System.Random();

        public static void Install(Harmony harmony)
        {
            var update = AccessTools.Method(typeof(Game), "UpdateSleeping");
            if (update != null) harmony.Patch(update, postfix: new HarmonyMethod(typeof(Sleep), nameof(UpdateSleeping_Postfix)));
            var start = AccessTools.Method(typeof(Game), "Start");
            if (start != null) harmony.Patch(start, postfix: new HarmonyMethod(typeof(Sleep), nameof(GameStart_Postfix)));
        }

        static void GameStart_Postfix() => ZRoutedRpc.instance?.Register(WakeRpc, sender => Wake());

        /// <summary>Lies the player down to sleep at this spot (a bed's or a lean-to's).</summary>
        public static void LieDown(Player p, Transform spot, GameObject bed)
        {
            p.AttachStart(spot, bed, hideWeapons: true, isBed: true, onShip: false, "attach_bed", new Vector3(0f, 0.5f, 0f));
            p.Message(MessageHud.MessageType.Center, Plugin.U5.Ovl.CStr(SZzz).Trim());
        }

        static bool wasSleeping;

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
                double now = ZNet.instance.GetTimeSeconds(), to = now + env.m_dayLengthSec * DayHours / 24.0;
                var e = Traverse.Create(env);
                e.Field("m_skipTime").SetValue(true);
                e.Field("m_skipToTime").SetValue(to);
                e.Field("m_timeSkipSpeed").SetValue((to - now) / 12.0);
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
            var what = UltimaSpawner.Encounter(at, "the camp", random);
            Plugin.Log.LogInfo($"The camp is ambushed: {what ?? "nothing could come"}");
        }

        /// <summary>Everyone up: the local player gets out of bed.</summary>
        static void Wake()
        {
            var me = Player.m_localPlayer;
            if (me == null || !me.InBed()) return;
            me.AttachStop();
            me.Message(MessageHud.MessageType.Center, Plugin.U5.Ovl.CStr(SAmbushed).Trim());
        }
    }
}
