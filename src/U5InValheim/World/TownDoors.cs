using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// Locked doors and secret doors of the settlements (Action mappings: Jimmy; Constructing world
    /// assets 78). A locked door does not open for a player: "Locked! No keys!" (the original's Open
    /// and Jimmy answers; keys are not in the game yet); a magically locked one is "Magically
    /// sealed.". Townsfolk have the keys. A secret door is a wall with rune-marked bricks: hitting
    /// it takes the bricks away, dark souls style, and shows the door hidden behind them ("a
    /// hidden door!").
    /// </summary>
    public static class TownDoors
    {
        public const string SecretKey = "u5_secret", HiddenKey = "u5_hidden";
        // DATA.OVL (DGROUP) Jimmy/Open strings
        const int SHiddenDoor = 0x8a48, SNoKeys = 0x8a7c, SLocked = 0x8bd6;

        public static void Install(Harmony harmony)
        {
            var interact = AccessTools.Method(typeof(Door), "Interact");
            var hover = AccessTools.Method(typeof(Door), "GetHoverText");
            var damage = AccessTools.Method(typeof(WearNTear), "Damage");
            if (interact == null || hover == null || damage == null) { Plugin.Log.LogWarning("TownDoors: Door.Interact/GetHoverText or WearNTear.Damage not found"); return; }
            harmony.Patch(interact, prefix: new HarmonyMethod(typeof(TownDoors), nameof(Interact_Prefix)));
            harmony.Patch(hover, postfix: new HarmonyMethod(typeof(TownDoors), nameof(Hover_Postfix)));
            harmony.Patch(damage, prefix: new HarmonyMethod(typeof(TownDoors), nameof(Damage_Prefix)) { priority = Priority.First });
        }

        static string Lock(Door door) => door.GetComponent<U5Fixed>() != null ? door.GetComponent<ZNetView>()?.GetZDO()?.GetString(Settlements.LockKey) ?? "" : "";

        static bool Shut(Door door) => door.GetComponent<ZNetView>()?.GetZDO()?.GetInt(ZDOVars.s_state) == 0;

        public static string LockedText(string lockKind)
        {
            var ovl = Plugin.U5.Ovl;
            return lockKind == "magic" ? "Magically sealed." : ovl.CStr(SLocked).Trim() + " " + ovl.CStr(SNoKeys).Trim();
        }

        /// <summary>A shut locked door stays shut for players (an open one may be closed).</summary>
        static bool Interact_Prefix(Door __instance, Humanoid character, bool hold, ref bool __result)
        {
            // a portcullis is no door: nobody opens it
            if (__instance.GetComponent<Portcullis>() != null)
            {
                if (character is Player p && !hold) p.Message(MessageHud.MessageType.Center, "The portcullis is down.");
                __result = false;
                return false;
            }
            if (hold || !(character is Player)) return true;
            string kind = Lock(__instance);
            if (kind.Length == 0 || !Shut(__instance)) return true;
            __instance.m_lockedEffects?.Create(__instance.transform.position, __instance.transform.rotation);
            character.Message(MessageHud.MessageType.Center, LockedText(kind));
            __result = true;
            return false;
        }

        static void Hover_Postfix(Door __instance, ref string __result)
        {
            if (__instance.GetComponent<Portcullis>() != null) { __result = "Portcullis"; return; }
            string kind = Lock(__instance);
            if (kind.Length == 0 || !Shut(__instance)) return;
            __result = Localization.instance.Localize(__instance.m_name) + "\n(" + (kind == "magic" ? "magically sealed" : "locked") + ")";
        }

        /// <summary>A player's blow on a secret wall reveals its door.</summary>
        static void Damage_Prefix(WearNTear __instance, HitData hit)
        {
            if (__instance.GetComponent<U5Fixed>() == null) return;
            var zdo = __instance.GetComponent<ZNetView>()?.GetZDO();
            int group = zdo?.GetInt(SecretKey) ?? 0;
            if (group == 0 || !(hit?.GetAttacker() is Player player) || player != Player.m_localPlayer) return;
            Reveal(__instance.transform.position, group);
            player.Message(MessageHud.MessageType.Center, Plugin.U5.Ovl.CStr(SHiddenDoor).Trim());
        }

        /// <summary>Takes away the secret wall's bricks (group) near this point and shows its door.</summary>
        public static int Reveal(Vector3 near, int group)
        {
            var bricks = new List<WearNTear>();
            var doors = new List<WearNTear>();
            foreach (var w in Object.FindObjectsByType<WearNTear>(FindObjectsSortMode.None))
            {
                if (w == null || w.GetComponent<U5Fixed>() == null || Vector3.Distance(w.transform.position, near) > 4f) continue;
                var z = w.GetComponent<ZNetView>()?.GetZDO();
                if (z == null) continue;
                if (z.GetInt(SecretKey) == group) bricks.Add(w);
                else if (z.GetInt(HiddenKey) == group) doors.Add(w);
            }
            foreach (var w in bricks)
            {
                var nview = w.GetComponent<ZNetView>();
                w.m_destroyedEffect?.Create(w.transform.position, w.transform.rotation);
                nview.ClaimOwnership();
                ZNetScene.instance.Destroy(w.gameObject);
            }
            foreach (var d in doors)
            {
                var nview = d.GetComponent<ZNetView>();
                nview.ClaimOwnership();
                nview.GetZDO().Set(HiddenKey, 0);
                d.GetComponent<HiddenDoor>()?.Check();
            }
            Plugin.Log.LogInfo($"a hidden door: {bricks.Count} bricks taken away, {doors.Count} door shown");
            return bricks.Count;
        }
    }

    /// <summary>The door behind a secret wall: not seen or touched until the wall is hit (its ZDO's hidden group cleared).</summary>
    public class HiddenDoor : MonoBehaviour
    {
        ZNetView nview;
        Renderer[] renderers;
        Collider[] colliders;
        float checkAt;

        void Awake()
        {
            nview = GetComponent<ZNetView>();
            renderers = GetComponentsInChildren<Renderer>(true);
            colliders = GetComponentsInChildren<Collider>(true);
            Show(false);
        }

        void Update()
        {
            if (Time.time < checkAt) return;
            checkAt = Time.time + 0.5f;
            Check();
        }

        public void Check()
        {
            var zdo = nview != null ? nview.GetZDO() : null;
            if (zdo == null || zdo.GetInt(TownDoors.HiddenKey) != 0) return;
            Show(true);
            Destroy(this);
        }

        void Show(bool on)
        {
            foreach (var r in renderers) if (r != null) r.enabled = on;
            foreach (var c in colliders) if (c != null) c.enabled = on;
        }
    }
}
