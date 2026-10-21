using System.Collections.Generic;
using System.Linq;
using U5InValheim.Data;
using HarmonyLib;
using UnityEngine;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// Locked doors and secret doors of the settlements (Action mappings: Jimmy; Constructing world
    /// assets 78). A locked door does not open for a player without a key: "Locked! No keys!"
    /// (the original's Open and Jimmy answers). With keys, E jimmies it (SJOG cmd_jimmy): a roll of
    /// 0..29 below the player's dexterity opens it ("Unlocked!"), else a key is lost ("Key
    /// broke!"). A magically locked one is "Magically sealed." unless the player carries a skull
    /// key: E uses one up and it opens (In Ex Por, which never fails on a sealed door). An opened
    /// lock stays open. Portcullises open for nobody. Townsfolk have the keys. A secret door is a wall with rune-marked bricks: hitting
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

        static readonly System.Random random = new System.Random();

        static string KeyPrefab => UltimaItems.Map.Special("Key")?.Prefab;
        static string SkullKeyPrefab => UltimaItems.Map.Special("Skull Key")?.Prefab;

        static ItemDrop.ItemData Carried(Player p, string prefab)
            => prefab == null ? null : p.GetInventory().GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == prefab);

        static void Unlock(Door door)
        {
            var nview = door.GetComponent<ZNetView>();
            nview.ClaimOwnership();
            nview.GetZDO().Set(Settlements.LockKey, "");
        }

        /// <summary>
        /// A player at a shut locked door: jimmy it with a key, or open a sealed one with a skull
        /// key; true when it is now unlocked (then it opens), false with the answer shown.
        /// </summary>
        public static bool TryUnlock(Door door, Player p, string kind)
        {
            var ovl = Plugin.U5.Ovl;
            if (kind == "magic")
            {
                var skull = Carried(p, SkullKeyPrefab);
                if (skull == null) { p.Message(MessageHud.MessageType.Center, LockedText(kind)); return false; }
                p.GetInventory().RemoveItem(skull, 1);
                Unlock(door);
                p.Message(MessageHud.MessageType.Center, ovl.CStr(ItemUse.SSkullKey).Trim() + "\n" + ovl.CStr(ItemUse.SUnlocked).Trim());
                Plugin.Log.LogInfo($"{p.GetPlayerName()} opens a sealed door with a skull key");
                return true;
            }
            var key = Carried(p, KeyPrefab);
            if (key == null) { p.Message(MessageHud.MessageType.Center, LockedText(kind)); return false; }
            int dex = AvatarStats.Mine.Dexterity, roll = random.Next(ItemUse.JimmyRoll);
            if (ItemUse.JimmyOpens(dex, roll))
            {
                Unlock(door);
                p.Message(MessageHud.MessageType.Center, ovl.CStr(ItemUse.SUnlocked).Trim());
                Plugin.Log.LogInfo($"{p.GetPlayerName()} jimmies a lock (dexterity {dex}, roll {roll})");
                return true;
            }
            p.GetInventory().RemoveItem(key, 1);
            door.m_lockedEffects?.Create(door.transform.position, door.transform.rotation);
            p.Message(MessageHud.MessageType.Center, ovl.CStr(ItemUse.SKeyBroke).Trim());
            Plugin.Log.LogInfo($"{p.GetPlayerName()}'s key broke on a lock (dexterity {dex}, roll {roll})");
            return false;
        }

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
                if (character is Player pp && !hold) pp.Message(MessageHud.MessageType.Center, "The portcullis is down.");
                __result = false;
                return false;
            }
            if (hold || !(character is Player p)) return true;
            string kind = Lock(__instance);
            if (kind.Length == 0 || !Shut(__instance)) return true;
            // a key (or a skull key) in hand: the lock is tried, and an opened one lets the door open
            if (p == Player.m_localPlayer && TryUnlock(__instance, p, kind)) return true;
            if (Carried(p, kind == "magic" ? SkullKeyPrefab : KeyPrefab) == null)
                __instance.m_lockedEffects?.Create(__instance.transform.position, __instance.transform.rotation);
            __result = true;
            return false;
        }

        static void Hover_Postfix(Door __instance, ref string __result)
        {
            if (__instance.GetComponent<Portcullis>() != null) { __result = "Portcullis"; return; }
            string kind = Lock(__instance);
            if (kind.Length == 0 || !Shut(__instance)) return;
            __result = Localization.instance.Localize(__instance.m_name) + "\n(" + (kind == "magic" ? "magically sealed" : "locked") + ")";
            var me = Player.m_localPlayer;
            if (me != null && Carried(me, kind == "magic" ? SkullKeyPrefab : KeyPrefab) != null)
                __result += Localization.instance.Localize("\n[<color=yellow><b>$KEY_Use</b></color>] " + (kind == "magic" ? "Use Skull Key" : "Jimmy"));
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
