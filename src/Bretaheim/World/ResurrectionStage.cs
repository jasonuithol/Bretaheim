using System.Collections;
using System.Collections.Generic;
using Bretaheim.Data;
using UnityEngine;

namespace Bretaheim.WorldGen
{
    /// <summary>
    /// Lord British's raising (Data/ResurrectionScene) played in the world instead of on the
    /// Ultima screen: the risen player stands in a black void high above the throne room, the
    /// camera held above and back so every figure is in sight; the original's words come one by one; two guardian Valkyries hover
    /// where the sparkles leave them (the scene's tiles at either side below the sleeper; spotlit from
    /// the camera's side, with a glow of their own),
    /// lightning and the ground shaking for the thunder, Lord British's apparition (cyan, as his
    /// tile) above, his KARMA.DAT words over him until a key; "Strange words are intoned.",
    /// "Vertigo..." and the player is in the throne room. All of it is local to the risen
    /// player's machine (no networked objects), and it ends early, cleaning up, if they die or
    /// leave. Input is held while it plays (Space or E goes on from his words).
    /// </summary>
    public static class ResurrectionStage
    {
        /// <summary>Above the throne room, out of every other system's way (dungeons build at 5000 − 8·level, the Underworld's check stops at 1000).</summary>
        const float Height = 3000f;
        /// <summary>One of the original's map tiles, in metres on the stage.</summary>
        const float Tile = 1.6f;
                /// <summary>The scene's sparkles (tile 0x16): Odin's vanishing smoke.</summary>
        const string Sparkle = "vfx_odin_despawn";
        static readonly Color Apparition = new Color(0.35f, 0.9f, 0.95f);
        /// <summary>How big and how high the guardian Valkyries hover (Valheim's is made to carry a player).</summary>
        const float ValkyrieScale = 0.3f, FallenScale = 0.3f, ValkyrieHover = 1.6f;
        /// <summary>The guardians' wings beat at a tenth of Valheim's pace (the user's).</summary>
        const float GuardianTempo = 0.1f;
        /// <summary>Lord British's apparition is twice a man's height (the user's).</summary>
        const float LordBritishScale = 2f;
        /// <summary>The risen stand this far back from the stage's middle (towards the camera, away from Lord British).</summary>
        const float RisenBack = 1f;

        public static bool Playing { get; private set; }

        /// <summary>The camera: well above and back (south) of the stage, so the guardians, the risen and Lord British are all in sight.</summary>
        static readonly Vector3 CameraOffset = new Vector3(0f, 6.5f, -9f);

        /// <summary>Plays the scene for the local player, then puts them at <paramref name="home"/>.</summary>
        public static IEnumerator Play(Player me, int karma, Vector3 home)
        {
            var ovl = Plugin.U5.Ovl;
            var karmaDat = ScenePlayer.Read("KARMA.DAT");
            string S(int dg) => ovl.CStr(dg).Trim('\n', ' ');
            // each player their own spot, so two risen together do not lie on one another
            var stage = home + new Vector3((me.GetPlayerID() & 7) * 40f, Height, 0f);
            var built = new List<GameObject>();
            Playing = true;
            Plugin.Log.LogInfo($"Resurrection stage for {me.GetPlayerName()} at {stage}");
            try
            {
                Stage.Darkness(true);
                Stage.CameraAt = stage + CameraOffset;
                Stage.LookAt = stage + new Vector3(0f, 0.8f, 0.8f);
                Stage.Held = true;
                Stage.SafeSpot = home;
                built.Add(Stage.Floor(stage));
                var risen = stage + Vector3.back * RisenBack;
                // a player just spawned (raised after a death) may not teleport for 2 s (Player.m_teleportCooldown)
                for (float t = 0f; !me.TeleportTo(risen + Vector3.up * 0.1f, Quaternion.Euler(0f, 0f, 0f), false) && t < 5f; t += Time.deltaTime)
                {
                    yield return null;
                    if (!Here(me)) yield break;
                }
                yield return new WaitForSeconds(1f);
                if (!Here(me)) yield break;
                built.Add(Stage.Glow(risen + Vector3.up * 3f, new Color(0.7f, 0.7f, 0.8f), 1.2f, 8f));
                // the risen lit from the camera's side as the guardians are
                built.Add(Stage.Spot(risen + Vector3.up * 3.5f + CameraOffset.normalized * 3f, risen + Vector3.up, new Color(0.9f, 0.9f, 1f), 4f, 10f));

                foreach (var (dg, wait) in new[] { (ResurrectionScene.SDarkness, 3.5f), (ResurrectionScene.SRefuge, 2.5f), (ResurrectionScene.SNoEvil, 3.5f), (ResurrectionScene.SDisturbed, 3f), (ResurrectionScene.SShouts, 3.5f) })
                {
                    Say(me, S(dg));
                    yield return new WaitForSeconds(wait);
                    if (!Here(me)) yield break;
                }

                // the two guardians appear where the sparkles leave them: the original's (2, 7) and (8, 7), below and either side of the sleeper at (5, 5)
                foreach (int x in new[] { 2, 8 })
                {
                    var at = stage + new Vector3((x - 5) * Tile * 1.3f, 0f, -(7 - 5) * Tile * 0.5f);
                    Stage.Effect(Sparkle, at);
                    yield return new WaitForSeconds(0.6f);
                    // the guardians (the user's): on the left the Valkyrie that brings a newcomer to Valheim, on the right a Fallen Valkyrie, hovering
                    bool fallen = x > 5;
                    var guardian = fallen ? FallenValkyrie(at + Vector3.up * ValkyrieHover, Stage.Face(at, stage)) : Valkyrie(me, at + Vector3.up * ValkyrieHover, Stage.Face(at, stage));
                    if (guardian != null) built.Add(guardian);
                    // lit from the camera's side, the light turned on them (not on the camera), and a soft glow of their own
                    built.Add(Stage.Spot(at + Vector3.up * (ValkyrieHover + 2f) + CameraOffset.normalized * 2.5f, at + Vector3.up * ValkyrieHover, new Color(0.85f, 0.88f, 1f), 6f, 9f));
                    built.Add(Stage.Glow(at + Vector3.up * ValkyrieHover, fallen ? new Color(0.75f, 0.3f, 0.9f) : new Color(0.45f, 0.5f, 1f), 0.8f, 2.5f));
                    yield return new WaitForSeconds(0.8f);
                    if (!Here(me)) yield break;
                }

                Say(me, S(ResurrectionScene.SThunder));
                for (int i = 0; i < 2; i++)
                {
                    var flash = Stage.Glow(stage + new Vector3(0f, 8f, 3 * Tile), new Color(0.85f, 0.9f, 1f), 8f, 40f);
                    Stage.Thunder(stage);
                    GameCamera.instance?.AddShake(stage, 50f, 3f, false);
                    yield return new WaitForSeconds(0.15f);
                    Object.Destroy(flash);
                    yield return new WaitForSeconds(0.6f);
                }
                if (!Here(me)) yield break;

                // Lord British's apparition at (5, 2): three tiles north
                var lbAt = stage + new Vector3(0f, 0f, 3 * Tile);
                Stage.Effect(Sparkle, lbAt);
                yield return new WaitForSeconds(0.6f);
                var lb = Stage.Figure(lbAt, Stage.Face(lbAt, stage), new[] { "HelmetDverger", "ArmorMageChest", "ArmorMageLegs", "CapeLinen" });
                if (lb != null) lb.transform.localScale *= LordBritishScale;
                if (lb != null) { built.Add(lb); Stage.Pose(lb, "challenge"); }
                built.Add(Stage.Glow(lbAt + Vector3.up * 2.2f * LordBritishScale + Vector3.back * LordBritishScale, Apparition, 3f, 9f));
                yield return null;
                yield return null;
                if (lb != null && Recolour.Draws) Recolour.Model(lb, Apparition);
                yield return new WaitForSeconds(1.5f);
                if (!Here(me)) yield break;

                string words = karmaDat != null ? ResurrectionScene.KarmaText(ovl, karmaDat, karma) : "";
                Say(me, "");
                if (lb != null && Chat.instance != null) Chat.instance.SetNpcText(lb, Vector3.up * 2.4f * LordBritishScale, 40f, 60f, "Lord British", "\"" + words.Replace('\n', ' ') + "\"", true);
                else Say(me, "\"" + words + "\"");
                me.Message(MessageHud.MessageType.TopLeft, "Space or E: go on");
                float until = Time.time + 60f;
                yield return new WaitForSeconds(0.3f);
                while (Time.time < until && Here(me) && !ZInput.GetKeyDown(KeyCode.Space) && !ZInput.GetKeyDown(KeyCode.E) && !ZInput.GetButtonDown("Use") && !ZInput.GetButtonDown("JoyUse"))
                    yield return null;
                if (!Here(me)) yield break;
                if (lb != null) Chat.instance?.ClearNpcText(lb);

                Say(me, S(ResurrectionScene.SIntoned));
                if (lb != null) Stage.Pose(lb, "point");
                Stage.Effect(Sparkle, stage);
                yield return new WaitForSeconds(2.5f);
                Say(me, S(ResurrectionScene.SVertigo));
                GameCamera.instance?.AddShake(stage, 50f, 1.5f, true);
                yield return new WaitForSeconds(2f);
            }
            finally
            {
                foreach (var go in built) if (go != null) Object.Destroy(go);
                for (; valkyriesLoaded > 0; valkyriesLoaded--) me?.m_valkyrie.Release();
                Stage.Darkness(false);
                Playing = false;
                Stage.Held = false;
                Stage.SafeSpot = null;
                if (me != null && !me.IsDead() && me == Player.m_localPlayer)
                {
                    me.TeleportTo(home, me.transform.rotation, false);
                    Plugin.Log.LogInfo($"Resurrection stage over: {me.GetPlayerName()} in the throne room");
                }
            }
        }

        static bool Here(Player me) => me != null && me == Player.m_localPlayer && !me.IsDead();

        static void Say(Player me, string text) => MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, text);

        /// <summary>
        /// A guardian: Valheim's Valkyrie (the Player prefab's, which brings a newcomer), only its
        /// body and animator, no flight and no network object.
        /// </summary>
        static GameObject Valkyrie(Player me, Vector3 at, Quaternion facing)
        {
            // loaded for the scene (released when it ends: the copies draw from its assets)
            var reference = me.m_valkyrie;
            reference.Load();
            valkyriesLoaded++;
            {
                var prefab = reference.Asset;
                return prefab != null ? Stage.Still(prefab, at, facing, ValkyrieScale, GuardianTempo) : null;
            }
        }

        /// <summary>The other guardian: a Fallen Valkyrie (Ashlands), its body and animator only.</summary>
        static GameObject FallenValkyrie(Vector3 at, Quaternion facing)
        {
            var prefab = ZNetScene.instance?.GetPrefab("FallenValkyrie");
            return prefab != null ? Stage.Still(prefab, at, facing, FallenScale, GuardianTempo) : null;
        }

        static int valkyriesLoaded;
    }
}
