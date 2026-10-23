using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Bretaheim.Data;
using UnityEngine;

namespace Bretaheim.WorldGen
{
    /// <summary>
    /// The new-world introduction (MASTER_PLAN §5.13): instead of Valheim's text and the valkyrie,
    /// a character arriving in a Britannia world for the first time sees the original's "Ultima V
    /// Introduction" (Data/Story: the 21 screens drawn from the player's own STORYn.16, TEXT.16,
    /// STORY.DAT and the proportional font). Space goes on, Escape skips it all; then they stand in
    /// Iolo's hut. Seen once per character and world (custom data u5_intro_&lt;world&gt;).
    /// </summary>
    public static class Intro
    {
        const string SeenPrefix = "u5_intro_";
        static List<StoryScreen> screens;

        /// <summary>The introduction is on the screen (or played in the world).</summary>
        public static bool Showing => playing || IntroStage.Playing;
        static bool playing;

        static bool Active => Britannia.Active != null;

        public static void Install(Harmony harmony)
        {
            UltimaScreenPanel.Install(harmony);
            IntroStage.Install(harmony);
            var show = AccessTools.Method(typeof(Game), "ShowIntro");
            if (show != null) harmony.Patch(show, prefix: new HarmonyMethod(typeof(Intro), nameof(ShowIntro_Prefix)));
            var play = AccessTools.Method(typeof(CinematicsManager), nameof(CinematicsManager.Play), new[] { typeof(CinematicsManager.Settings), typeof(CinematicsManager.VideoCompleteAction) });
            if (play != null) harmony.Patch(play, prefix: new HarmonyMethod(typeof(Intro), nameof(Cinematic_Prefix)));
            var spawn = AccessTools.Method(typeof(Game), "SpawnPlayer");
            if (spawn != null) harmony.Patch(spawn, prefix: new HarmonyMethod(typeof(Intro), nameof(SpawnPlayer_Prefix)));
            var spawned = AccessTools.Method(typeof(Player), nameof(Player.OnSpawned));
            if (spawned != null) harmony.Patch(spawned, postfix: new HarmonyMethod(typeof(Intro), nameof(OnSpawned_Postfix)));
            if (show == null || spawn == null || spawned == null) Plugin.Log.LogWarning("Intro: Game.ShowIntro / SpawnPlayer / Player.OnSpawned not found");
        }

        /// <summary>No Valheim intro text in Britannia (ours comes when the player stands in the hut).</summary>
        static bool ShowIntro_Prefix(Game __instance)
        {
            if (!Active) return true;
            Traverse.Create(__instance).Field("m_inIntro").SetValue(false);
            return false;
        }

        /// <summary>Nor Valheim's intro film.</summary>
        static bool Cinematic_Prefix(CinematicsManager.Settings firstWithSetting, ref bool __result)
        {
            if (!Active || firstWithSetting != CinematicsManager.Settings.Intro) return true;
            __result = false;
            return false;
        }

        /// <summary>Nor the valkyrie: the Avatar arrives through the moongate, in Iolo's hut.</summary>
        static void SpawnPlayer_Prefix(ref bool spawnValkyrie)
        {
            if (Active) spawnValkyrie = false;
        }

        static string SeenKey => SeenPrefix + (ZNet.instance != null ? ZNet.instance.GetWorldUID() : 0);

        static void OnSpawned_Postfix(Player __instance)
        {
            if (!Active || __instance != Player.m_localPlayer || __instance.m_customData.ContainsKey(SeenKey)) return;
            Plugin.Instance.StartCoroutine(Play());
        }

        /// <summary>
        /// Plays the introduction (also for `u5 intro`): in the world (IntroStage, the View acted
        /// with the story's words), or the original's 21 screens when asked or when it cannot be staged.
        /// </summary>
        public static IEnumerator Play(bool screensOnly = false, int fromScene = 0)
        {
            if (playing || IntroStage.Playing) yield break;
            // (the world appears behind it first)
            yield return new WaitForSeconds(0.5f);
            if (!screensOnly && Player.m_localPlayer != null)
            {
                bool staged = false;
                yield return IntroStage.Play(Player.m_localPlayer, ok => staged = ok, fromScene);
                if (staged)
                {
                    MarkSeen();
                    Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "Iolo's hut");
                    yield break;
                }
            }
            if (screens == null)
            {
                try
                {
                    if (!Story.Available(Plugin.U5.Directory)) { Plugin.Log.LogWarning("Intro: STORY files not found; no introduction"); MarkSeen(); yield break; }
                    screens = Story.Load(Plugin.U5.Directory, Plugin.U5.Ovl);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Intro: could not read the story: {e.Message}");
                    MarkSeen();
                    yield break;
                }
            }
            playing = true;
            Plugin.Log.LogInfo("Intro: the Ultima V introduction");
            int at = 0;
            bool next = false, skip = false;
            void Show(Data.Screen s)
                => UltimaScreenPanel.Show(s, $"Ultima V — {at + 1} of {screens.Count}      Space: on      Escape: skip", () => next = true, () => skip = true);
            while (at < screens.Count && !skip)
            {
                Show(screens[at].Picture);
                // the extra picture comes a moment after (the original dissolves it in)
                float after = Time.time + 1.2f;
                bool turned = false;
                next = false;
                while (!next && !skip)
                {
                    if (!turned && screens[at].After != null && Time.time >= after) { turned = true; Show(screens[at].After); }
                    if (Player.m_localPlayer == null) { skip = true; break; }
                    yield return null;
                }
                at++;
            }
            UltimaScreenPanel.Close();
            playing = false;
            MarkSeen();
            Plugin.Log.LogInfo(skip ? "Intro: skipped" : "Intro: done");
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "Iolo's hut");
        }

        static void MarkSeen()
        {
            var me = Player.m_localPlayer;
            if (me != null) me.m_customData[SeenKey] = "1";
        }
    }
}
