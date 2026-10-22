using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
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
        const string VoidEnv = "U5_Void";
        /// <summary>The scene's sparkles (tile 0x16): Odin's vanishing smoke.</summary>
        const string Sparkle = "vfx_odin_despawn";
        static readonly Color Apparition = new Color(0.35f, 0.9f, 0.95f);
        /// <summary>How big and how high the guardian Valkyries hover (Valheim's is made to carry a player).</summary>
        const float ValkyrieScale = 0.3f, FallenScale = 0.3f, ValkyrieHover = 1.6f;

        public static bool Playing { get; private set; }

        public static void Install(Harmony harmony)
        {
            var take = AccessTools.Method(typeof(PlayerController), "TakeInput");
            if (take != null) harmony.Patch(take, prefix: new HarmonyMethod(typeof(ResurrectionStage), nameof(TakeInput_Prefix)));
            else Plugin.Log.LogWarning("Resurrection stage: PlayerController.TakeInput not found");
            var cam = AccessTools.Method(typeof(GameCamera), "LateUpdate");
            if (cam != null) harmony.Patch(cam, postfix: new HarmonyMethod(typeof(ResurrectionStage), nameof(CameraLateUpdate_Postfix)));
            else Plugin.Log.LogWarning("Resurrection stage: GameCamera.LateUpdate not found");
        }

        /// <summary>The stage's middle (the risen player) while the scene plays.</summary>
        static Vector3? focus;
        /// <summary>The camera: well above and back (south) of the stage, so the guardians, the risen and Lord British are all in sight.</summary>
        static readonly Vector3 CameraOffset = new Vector3(0f, 6.5f, -9f);

        static void CameraLateUpdate_Postfix(GameCamera __instance)
        {
            if (!Playing || focus == null) return;
            var at = focus.Value + CameraOffset;
            __instance.transform.position = at;
            __instance.transform.rotation = Quaternion.LookRotation(focus.Value + new Vector3(0f, 0.8f, 0.8f) - at);
        }

        static bool TakeInput_Prefix(ref bool __result)
        {
            if (!Playing) return true;
            __result = false;
            return false;
        }

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
                Darkness(true);
                focus = stage;
                built.Add(Floor(stage));
                me.TeleportTo(stage + Vector3.up * 0.1f, Quaternion.Euler(0f, 0f, 0f), false);
                yield return new WaitForSeconds(1f);
                if (!Here(me)) yield break;
                built.Add(Glow(stage + Vector3.up * 3f, new Color(0.7f, 0.7f, 0.8f), 1.2f, 8f));
                // the risen lit from the camera's side as the guardians are
                built.Add(Spot(stage + Vector3.up * 3.5f + CameraOffset.normalized * 3f, stage + Vector3.up, new Color(0.9f, 0.9f, 1f), 4f, 10f));

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
                    Effect(Sparkle, at);
                    yield return new WaitForSeconds(0.6f);
                    // the guardians (the user's): on the left the Valkyrie that brings a newcomer to Valheim, on the right a Fallen Valkyrie, hovering
                    bool fallen = x > 5;
                    var guardian = fallen ? FallenValkyrie(at + Vector3.up * ValkyrieHover, Face(at, stage)) : Valkyrie(me, at + Vector3.up * ValkyrieHover, Face(at, stage));
                    if (guardian != null) built.Add(guardian);
                    // lit from the camera's side, the light turned on them (not on the camera), and a soft glow of their own
                    built.Add(Spot(at + Vector3.up * (ValkyrieHover + 2f) + CameraOffset.normalized * 2.5f, at + Vector3.up * ValkyrieHover, new Color(0.85f, 0.88f, 1f), 6f, 9f));
                    built.Add(Glow(at + Vector3.up * ValkyrieHover, fallen ? new Color(0.75f, 0.3f, 0.9f) : new Color(0.45f, 0.5f, 1f), 0.8f, 2.5f));
                    yield return new WaitForSeconds(0.8f);
                    if (!Here(me)) yield break;
                }

                Say(me, S(ResurrectionScene.SThunder));
                for (int i = 0; i < 2; i++)
                {
                    var flash = Glow(stage + new Vector3(0f, 8f, 3 * Tile), new Color(0.85f, 0.9f, 1f), 8f, 40f);
                    Thunder(stage);
                    GameCamera.instance?.AddShake(stage, 50f, 3f, false);
                    yield return new WaitForSeconds(0.15f);
                    Object.Destroy(flash);
                    yield return new WaitForSeconds(0.6f);
                }
                if (!Here(me)) yield break;

                // Lord British's apparition at (5, 2): three tiles north
                var lbAt = stage + new Vector3(0f, 0f, 3 * Tile);
                Effect(Sparkle, lbAt);
                yield return new WaitForSeconds(0.6f);
                var lb = Figure(lbAt, Face(lbAt, stage), new[] { "HelmetDverger", "ArmorMageChest", "ArmorMageLegs", "CapeLinen" });
                if (lb != null) { built.Add(lb); Pose(lb, "challenge"); }
                built.Add(Glow(lbAt + Vector3.up * 2.2f + Vector3.back, Apparition, 3f, 8f));
                yield return null;
                yield return null;
                if (lb != null && Recolour.Draws) Recolour.Model(lb, Apparition);
                yield return new WaitForSeconds(1.5f);
                if (!Here(me)) yield break;

                string words = karmaDat != null ? ResurrectionScene.KarmaText(ovl, karmaDat, karma) : "";
                Say(me, "");
                if (lb != null && Chat.instance != null) Chat.instance.SetNpcText(lb, Vector3.up * 2.4f, 40f, 60f, "Lord British", "\"" + words.Replace('\n', ' ') + "\"", true);
                else Say(me, "\"" + words + "\"");
                me.Message(MessageHud.MessageType.TopLeft, "Space or E: go on");
                float until = Time.time + 60f;
                yield return new WaitForSeconds(0.3f);
                while (Time.time < until && Here(me) && !ZInput.GetKeyDown(KeyCode.Space) && !ZInput.GetKeyDown(KeyCode.E) && !ZInput.GetButtonDown("Use") && !ZInput.GetButtonDown("JoyUse"))
                    yield return null;
                if (!Here(me)) yield break;
                if (lb != null) Chat.instance?.ClearNpcText(lb);

                Say(me, S(ResurrectionScene.SIntoned));
                if (lb != null) Pose(lb, "point");
                Effect(Sparkle, stage);
                yield return new WaitForSeconds(2.5f);
                Say(me, S(ResurrectionScene.SVertigo));
                GameCamera.instance?.AddShake(stage, 50f, 1.5f, true);
                yield return new WaitForSeconds(2f);
            }
            finally
            {
                foreach (var go in built) if (go != null) Object.Destroy(go);
                for (; valkyriesLoaded > 0; valkyriesLoaded--) me?.m_valkyrie.Release();
                Darkness(false);
                Playing = false;
                focus = null;
                if (me != null && !me.IsDead() && me == Player.m_localPlayer)
                {
                    me.TeleportTo(home, me.transform.rotation, false);
                    Plugin.Log.LogInfo($"Resurrection stage over: {me.GetPlayerName()} in the throne room");
                }
            }
        }

        static bool Here(Player me) => me != null && me == Player.m_localPlayer && !me.IsDead();

        static void Say(Player me, string text) => MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, text);

        static Quaternion Face(Vector3 from, Vector3 to)
        {
            var d = to - from;
            d.y = 0f;
            return d.sqrMagnitude > 0.001f ? Quaternion.LookRotation(d) : Quaternion.identity;
        }

        static void Effect(string prefab, Vector3 at)
        {
            var p = ZNetScene.instance?.GetPrefab(prefab);
            if (p == null) return;
            ZNetView.m_forceDisableInit = true;
            try { Object.Instantiate(p, at, Quaternion.identity); }
            finally { ZNetView.m_forceDisableInit = false; }
        }

        /// <summary>Something to stand on: an unseen slab (the void shows nothing).</summary>
        static GameObject Floor(Vector3 at)
        {
            var go = new GameObject("u5_void_floor");
            go.layer = LayerMask.NameToLayer("static_solid");
            go.transform.position = at - Vector3.up * 0.5f;
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(30f, 1f, 30f);
            return go;
        }

        /// <summary>A spotlight at <paramref name="from"/> turned on <paramref name="onto"/>.</summary>
        static GameObject Spot(Vector3 from, Vector3 onto, Color colour, float intensity, float range)
        {
            var go = new GameObject("u5_void_spot");
            go.transform.position = from;
            go.transform.rotation = Quaternion.LookRotation(onto - from);
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.spotAngle = 45f;
            l.color = colour;
            l.intensity = intensity;
            l.range = range;
            return go;
        }

        static GameObject Glow(Vector3 at, Color colour, float intensity, float range)
        {
            var go = new GameObject("u5_void_light");
            go.transform.position = at;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = colour;
            l.intensity = intensity;
            l.range = range;
            return go;
        }

        /// <summary>
        /// A dressed figure on the stage: Valheim's Player body (its VisEquipment and animator only,
        /// no character, no network object) wearing these items.
        /// </summary>
        static GameObject Figure(Vector3 at, Quaternion facing, string[] items)
        {
            var prefab = ZNetScene.instance?.GetPrefab("Player");
            if (prefab == null) return null;
            var holder = new GameObject("u5_figure_holder");
            holder.SetActive(false);
            var go = Object.Instantiate(prefab, at, facing, holder.transform);
            go.name = "u5_stage_figure";
            for (int pass = 0; pass < 4; pass++)
                foreach (var c in go.GetComponentsInChildren<Component>(true))
                    if (c != null && (c is Rigidbody || c is Collider || (c is MonoBehaviour && !(c is VisEquipment) && !(c is ZNetView) && !(c is LODGroup))))
                        Object.DestroyImmediate(c);
            var vis = go.GetComponent<VisEquipment>();
            if (vis != null) vis.m_isPlayer = false;
            // out of the holder with the network held off: its ZNetView removes itself, the body draws
            ZNetView.m_forceDisableInit = true;
            try { go.transform.SetParent(null, true); go.SetActive(true); }
            finally { ZNetView.m_forceDisableInit = false; }
            // dressed once awake (the setters ask its ZNetView, gone with the network held off: no ZDO, so local only)
            if (vis != null)
            {
                foreach (var item in items)
                {
                    var drop = ObjectDB.instance?.GetItemPrefab(item)?.GetComponent<ItemDrop>();
                    if (drop == null) continue;
                    switch (drop.m_itemData.m_shared.m_itemType)
                    {
                        case ItemDrop.ItemData.ItemType.Helmet: vis.SetHelmetItem(item.GetStableHashCode()); break;
                        case ItemDrop.ItemData.ItemType.Chest: vis.SetChestItem(item.GetStableHashCode()); break;
                        case ItemDrop.ItemData.ItemType.Legs: vis.SetLegItem(item.GetStableHashCode()); break;
                        case ItemDrop.ItemData.ItemType.Shoulder: vis.SetShoulderItem(item.GetStableHashCode(), 0, 1); break;
                    }
                }
            }
            Object.Destroy(holder);
            return go;
        }

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
                return prefab != null ? Still(prefab, at, facing, ValkyrieScale) : null;
            }
        }

        /// <summary>The other guardian: a Fallen Valkyrie (Ashlands), its body and animator only.</summary>
        static GameObject FallenValkyrie(Vector3 at, Quaternion facing)
        {
            var prefab = ZNetScene.instance?.GetPrefab("FallenValkyrie");
            return prefab != null ? Still(prefab, at, facing, FallenScale) : null;
        }

        /// <summary>A copy of a prefab with nothing but its body and animator (no behaviour, physics or network object).</summary>
        static GameObject Still(GameObject prefab, Vector3 at, Quaternion facing, float scale)
        {
            var holder = new GameObject("u5_still_holder");
            holder.SetActive(false);
            var go = Object.Instantiate(prefab, at, facing, holder.transform);
            go.name = "u5_stage_" + prefab.name;
            for (int pass = 0; pass < 4; pass++)
                foreach (var c in go.GetComponentsInChildren<Component>(true))
                    if (c != null && (c is Rigidbody || c is Collider || (c is MonoBehaviour && !(c is LODGroup))))
                        Object.DestroyImmediate(c);
            go.transform.localScale *= scale;
            go.transform.SetParent(null, true);
            go.SetActive(true);
            Object.Destroy(holder);
            return go;
        }

        static int valkyriesLoaded;

        /// <summary>One of Valheim's emotes on a stage figure (kneel, challenge, point …).</summary>
        static void Pose(GameObject figure, string emote)
        {
            var anim = figure.GetComponentInChildren<Animator>();
            if (anim == null) return;
            anim.ResetTrigger("emote_stop");
            anim.SetTrigger("emote_" + emote);
        }

        // ── the void ────────────────────────────────────────────────────────────────────────

        static CameraClearFlags clearFlags;
        static Color background;
        static float farClip;
        static bool cameraHeld;

        /// <summary>The void: the black environment, and the camera clearing to black and drawing nothing past the stage (no sky dome, no stars).</summary>
        static void Darkness(bool on)
        {
            var env = EnvMan.instance;
            var cam = Utils.GetMainCamera();
            if (!on)
            {
                env?.SetForceEnvironment("");
                if (cameraHeld && cam != null) { cam.clearFlags = clearFlags; cam.backgroundColor = background; cam.farClipPlane = farClip; }
                cameraHeld = false;
                return;
            }
            if (env != null && Void(env) != null) env.SetForceEnvironment(VoidEnv);
            if (cam != null && !cameraHeld)
            {
                clearFlags = cam.clearFlags; background = cam.backgroundColor; farClip = cam.farClipPlane;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.farClipPlane = 60f;
                cameraHeld = true;
            }
        }

        /// <summary>The peal of thunder: Valheim's thunder sound where the lightning would be.</summary>
        static void Thunder(Vector3 at)
        {
            var fx = ZNetScene.instance?.GetPrefab("fx_Lightning");
            var sound = fx != null ? fx.GetComponentInChildren<AudioSource>(true) : null;
            if (sound?.clip != null) AudioSource.PlayClipAtPoint(sound.clip, at + Vector3.up * 5f, 1f);
        }

        /// <summary>Black: no sun, sky or clouds to be seen through thick black fog; a faint ambient light so the figures show.</summary>
        static EnvSetup Void(EnvMan env)
        {
            foreach (var e in env.m_environments) if (e.m_name == VoidEnv) return e;
            var from = Underworld.Environment(env);
            if (from == null) return null;
            var s = from.Clone();
            s.m_name = VoidEnv;
            s.m_default = false;
            s.m_alwaysDark = true;
            var amb = new Color(0.14f, 0.14f, 0.16f);
            s.m_ambColorDay = s.m_ambColorNight = amb;
            var black = Color.black;
            s.m_fogColorDay = s.m_fogColorNight = s.m_fogColorMorning = s.m_fogColorEvening = black;
            s.m_fogColorSunDay = s.m_fogColorSunNight = s.m_fogColorSunMorning = s.m_fogColorSunEvening = black;
            s.m_fogDensityDay = s.m_fogDensityNight = s.m_fogDensityMorning = s.m_fogDensityEvening = 0.08f;
            s.m_sunColorDay = s.m_sunColorNight = s.m_sunColorMorning = s.m_sunColorEvening = black;
            s.m_lightIntensityDay = s.m_lightIntensityNight = 0f;
            s.m_windMin = s.m_windMax = 0f;
            env.AppendEnvironment(s);
            return s;
        }
    }
}
