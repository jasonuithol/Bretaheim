using HarmonyLib;
using UnityEngine;

namespace Bretaheim.WorldGen
{
    /// <summary>
    /// What the scenes played in the world (ResurrectionStage, IntroStage) are made of: the local
    /// player's input held and the camera put where the scene wants it, the black void, figures
    /// (Valheim bodies with nothing but their looks and animator), lights and effects. All of it
    /// local to one machine: no network objects.
    /// </summary>
    public static class Stage
    {
        /// <summary>A scene is playing: the player's input is held and the camera is the scene's.</summary>
        public static bool Held;
        /// <summary>Where the camera is and what it looks at while <see cref="Held"/>.</summary>
        public static Vector3 CameraAt, LookAt;
        /// <summary>Its field of view while held (0: the game's own).</summary>
        public static float Fov;
        const string VoidEnv = "U5_Void";
        /// <summary>Where the player really is while a scene has them up in the void: saved as their logout point, so quitting mid-scene does not leave them in the sky.</summary>
        public static Vector3? SafeSpot;

        public static void Install(Harmony harmony)
        {
            var take = AccessTools.Method(typeof(PlayerController), "TakeInput");
            if (take != null) harmony.Patch(take, prefix: new HarmonyMethod(typeof(Stage), nameof(TakeInput_Prefix)));
            else Plugin.Log.LogWarning("Stage: PlayerController.TakeInput not found");
            var cam = AccessTools.Method(typeof(GameCamera), "LateUpdate");
            if (cam != null) harmony.Patch(cam, postfix: new HarmonyMethod(typeof(Stage), nameof(CameraLateUpdate_Postfix)));
            else Plugin.Log.LogWarning("Stage: GameCamera.LateUpdate not found");
            var logout = AccessTools.Method(typeof(PlayerProfile), nameof(PlayerProfile.SetLogoutPoint));
            if (logout != null) harmony.Patch(logout, prefix: new HarmonyMethod(typeof(Stage), nameof(SetLogoutPoint_Prefix)));
            else Plugin.Log.LogWarning("Stage: PlayerProfile.SetLogoutPoint not found");
        }

        static void SetLogoutPoint_Prefix(ref Vector3 point)
        {
            if (SafeSpot != null) point = SafeSpot.Value;
        }

        static float fovWas;

        static void CameraLateUpdate_Postfix(GameCamera __instance)
        {
            var cam = __instance.GetComponent<Camera>();
            if (!Held)
            {
                if (fovWas > 0 && cam != null) { cam.fieldOfView = fovWas; fovWas = 0; }
                return;
            }
            __instance.transform.position = CameraAt;
            if ((LookAt - CameraAt).sqrMagnitude > 0.0001f) __instance.transform.rotation = Quaternion.LookRotation(LookAt - CameraAt);
            if (Fov > 0 && cam != null)
            {
                if (fovWas == 0) fovWas = cam.fieldOfView;
                cam.fieldOfView = Fov;
            }
        }

        static bool TakeInput_Prefix(ref bool __result)
        {
            if (!Held) return true;
            __result = false;
            return false;
        }

        public static Quaternion Face(Vector3 from, Vector3 to)
        {
            var d = to - from;
            d.y = 0f;
            return d.sqrMagnitude > 0.001f ? Quaternion.LookRotation(d) : Quaternion.identity;
        }

        /// <summary>One of Valheim's effects (smoke, sparks …) played once at a place, with no network object.</summary>
        public static GameObject Effect(string prefab, Vector3 at, Transform parent = null)
        {
            var p = ZNetScene.instance?.GetPrefab(prefab);
            if (p == null) return null;
            ZNetView.m_forceDisableInit = true;
            try { return Object.Instantiate(p, at, Quaternion.identity, parent); }
            finally { ZNetView.m_forceDisableInit = false; }
        }

        /// <summary>Something to stand on: an unseen slab (the void shows nothing).</summary>
        public static GameObject Floor(Vector3 at, float size = 30f)
        {
            var go = new GameObject("u5_void_floor");
            go.layer = LayerMask.NameToLayer("static_solid");
            go.transform.position = at - Vector3.up * 0.5f;
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(size, 1f, size);
            return go;
        }

        /// <summary>A spotlight at <paramref name="from"/> turned on <paramref name="onto"/>.</summary>
        public static GameObject Spot(Vector3 from, Vector3 onto, Color colour, float intensity, float range, float angle = 45f, Transform parent = null)
        {
            var go = new GameObject("u5_void_spot");
            go.transform.SetParent(parent, false);
            go.transform.position = from;
            go.transform.rotation = Quaternion.LookRotation(onto - from);
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.spotAngle = angle;
            l.color = colour;
            l.intensity = intensity;
            l.range = range;
            return go;
        }

        public static GameObject Glow(Vector3 at, Color colour, float intensity, float range, Transform parent = null)
        {
            var go = new GameObject("u5_void_light");
            go.transform.SetParent(parent, false);
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
        public static GameObject Figure(Vector3 at, Quaternion facing, string[] items, Transform parent = null)
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
            try { go.transform.SetParent(parent, true); go.SetActive(true); }
            finally { ZNetView.m_forceDisableInit = false; }
            // dressed once awake (the setters ask its ZNetView, gone with the network held off: no ZDO, so local only)
            if (vis != null) Dress(vis, items);
            Object.Destroy(holder);
            return go;
        }

        /// <summary>Puts these items on a figure (helmet, chest, legs, cape, or in a hand).</summary>
        public static void Dress(VisEquipment vis, string[] items)
        {
            foreach (var item in items)
            {
                var drop = ObjectDB.instance?.GetItemPrefab(item)?.GetComponent<ItemDrop>();
                if (drop == null) continue;
                int h = item.GetStableHashCode();
                switch (drop.m_itemData.m_shared.m_itemType)
                {
                    case ItemDrop.ItemData.ItemType.Helmet: vis.SetHelmetItem(h); break;
                    case ItemDrop.ItemData.ItemType.Chest: vis.SetChestItem(h); break;
                    case ItemDrop.ItemData.ItemType.Legs: vis.SetLegItem(h); break;
                    case ItemDrop.ItemData.ItemType.Shoulder: vis.SetShoulderItem(h, 0, 1); break;
                    case ItemDrop.ItemData.ItemType.Shield: vis.SetLeftItem(h, 0, 1); break;
                    default: vis.SetRightItem(h, 1); break;
                }
            }
        }

        /// <summary>A figure given a player's own body: their model, skin, hair and beard.</summary>
        public static void Likeness(GameObject figure, Player like)
        {
            var vis = figure != null ? figure.GetComponent<VisEquipment>() : null;
            if (vis == null || like == null) return;
            var t = Traverse.Create(like);
            vis.SetModel(like.GetPlayerModel());
            vis.SetSkinColor(t.Field<Vector3>("m_skinColor").Value);
            vis.SetHairColor(like.GetHairColor());
            string hair = t.Field<string>("m_hairItem").Value, beard = t.Field<string>("m_beardItem").Value;
            if (!string.IsNullOrEmpty(hair)) vis.SetHairItem(hair.GetStableHashCode());
            if (!string.IsNullOrEmpty(beard)) vis.SetBeardItem(beard.GetStableHashCode());
        }

        /// <summary>A copy of a prefab with nothing but its body and animator (no behaviour, physics or network object), its animation at <paramref name="tempo"/>.</summary>
        public static GameObject Still(GameObject prefab, Vector3 at, Quaternion facing, float scale, float tempo = 1f, Transform parent = null)
        {
            var holder = new GameObject("u5_still_holder");
            holder.SetActive(false);
            var go = Object.Instantiate(prefab, at, facing, holder.transform);
            go.name = "u5_stage_" + prefab.name;
            // an item is a prop, not loot: none of the glints that rise from things dropped on the ground
            if (prefab.GetComponent<ItemDrop>() != null)
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                    if (ps.gameObject != go) ps.gameObject.SetActive(false);
                    else ps.GetComponent<ParticleSystemRenderer>().enabled = false;
            for (int pass = 0; pass < 4; pass++)
                foreach (var c in go.GetComponentsInChildren<Component>(true))
                    if (c != null && (c is Rigidbody || c is Collider || (c is MonoBehaviour && !(c is LODGroup))))
                        Object.DestroyImmediate(c);
            go.transform.localScale *= scale;
            if (tempo != 1f) foreach (var anim in go.GetComponentsInChildren<Animator>(true)) anim.speed = tempo;
            ZNetView.m_forceDisableInit = true;
            try { go.transform.SetParent(parent, true); go.SetActive(true); }
            finally { ZNetView.m_forceDisableInit = false; }
            Object.Destroy(holder);
            return go;
        }

        /// <summary>One of Valheim's emotes on a stage figure (kneel, challenge, point …).</summary>
        public static void Pose(GameObject figure, string emote)
        {
            var anim = figure != null ? figure.GetComponentInChildren<Animator>() : null;
            if (anim == null) return;
            anim.ResetTrigger("emote_stop");
            anim.SetTrigger("emote_" + emote);
        }

        // ── the void ────────────────────────────────────────────────────────────────────────

        static CameraClearFlags clearFlags;
        static Color background;
        static float farClip;
        static bool cameraHeld;

        /// <summary>The void: the black environment (or <paramref name="env"/>), and the camera clearing to <paramref name="sky"/> and drawing nothing past <paramref name="far"/> (no sky dome, no stars).</summary>
        public static void Darkness(bool on, Color? sky = null, float far = 60f, string env = null)
        {
            var envMan = EnvMan.instance;
            var cam = Utils.GetMainCamera();
            if (!on)
            {
                envMan?.SetForceEnvironment("");
                if (cameraHeld && cam != null) { cam.clearFlags = clearFlags; cam.backgroundColor = background; cam.farClipPlane = farClip; }
                cameraHeld = false;
                return;
            }
            if (envMan != null) envMan.SetForceEnvironment(env ?? (Void(envMan) != null ? VoidEnv : ""));
            if (cam != null)
            {
                if (!cameraHeld) { clearFlags = cam.clearFlags; background = cam.backgroundColor; farClip = cam.farClipPlane; }
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = sky ?? Color.black;
                cam.farClipPlane = far;
                cameraHeld = true;
            }
        }

        /// <summary>The peal of thunder: Valheim's thunder sound where the lightning would be.</summary>
        public static void Thunder(Vector3 at, float volume = 1f)
        {
            var fx = ZNetScene.instance?.GetPrefab("fx_Lightning");
            var sound = fx != null ? fx.GetComponentInChildren<AudioSource>(true) : null;
            if (sound?.clip != null) AudioSource.PlayClipAtPoint(sound.clip, at + Vector3.up * 5f, volume);
        }

        /// <summary>Black: no sun, sky or clouds to be seen through thick black fog; a faint ambient light so the figures show.</summary>
        public static EnvSetup Void(EnvMan env) => Environment(env, VoidEnv, new Color(0.14f, 0.14f, 0.16f), Color.black, 0.08f);

        /// <summary>A dark environment of our own (cloned from <paramref name="like"/>, else the Underworld's, keeping its rain, sounds and music): this ambient light, fog colour and density, no sun.</summary>
        public static EnvSetup Environment(EnvMan env, string name, Color ambient, Color fog, float density, string like = null)
        {
            foreach (var e in env.m_environments) if (e.m_name == name) return e;
            var from = like != null ? env.m_environments.Find(e => e.m_name == like) : null;
            from = from ?? Underworld.Environment(env);
            if (from == null) return null;
            var s = from.Clone();
            s.m_name = name;
            s.m_default = false;
            s.m_alwaysDark = true;
            s.m_ambColorDay = s.m_ambColorNight = ambient;
            s.m_fogColorDay = s.m_fogColorNight = s.m_fogColorMorning = s.m_fogColorEvening = fog;
            s.m_fogColorSunDay = s.m_fogColorSunNight = s.m_fogColorSunMorning = s.m_fogColorSunEvening = fog;
            s.m_fogDensityDay = s.m_fogDensityNight = s.m_fogDensityMorning = s.m_fogDensityEvening = density;
            var black = Color.black;
            s.m_sunColorDay = s.m_sunColorNight = s.m_sunColorMorning = s.m_sunColorEvening = black;
            s.m_lightIntensityDay = s.m_lightIntensityNight = 0f;
            s.m_windMin = s.m_windMax = 0f;
            env.AppendEnvironment(s);
            return s;
        }
    }
}
