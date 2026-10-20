using System;
using System.Collections.Generic;
using HarmonyLib;
using U5InValheim.WorldGen.Core;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// Townsfolk are real creatures, with a few differences from Valheim's: they fall rather than
    /// die (back the next morning, TownNpc.Fall), count as dead to monsters meanwhile, cannot be
    /// hurt by players (crimes come with karma), and show a health bar only when hurt.
    /// </summary>
    public static class TownsfolkPatches
    {
        /// <summary>The townsfolk loaded here, by their creature (Valheim asks every creature IsDead many times a frame).</summary>
        static readonly Dictionary<Character, TownNpc> folk = new Dictionary<Character, TownNpc>();

        public static void Register(Character c, TownNpc npc) { if (c != null) folk[c] = npc; }
        public static void Unregister(Character c) { if (c != null) folk.Remove(c); }
        static TownNpc Of(Character c) => c != null && folk.TryGetValue(c, out var npc) ? npc : null;
        /// <summary>The townsperson of a creature, or null.</summary>
        public static TownNpc NpcOf(Character c) => Of(c);

        public static void Install(Harmony harmony)
        {
            Patch(harmony, typeof(Character), "CheckDeath", prefix: nameof(CheckDeath_Prefix));
            Patch(harmony, typeof(Character), "IsDead", postfix: nameof(IsDead_Postfix));
            Patch(harmony, typeof(Character), "Damage", prefix: nameof(Damage_Prefix));
            Patch(harmony, typeof(EnemyHud), "TestShow", prefix: nameof(TestShow_Prefix));
            Patch(harmony, typeof(Projectile), "Setup", postfix: nameof(ProjectileSetup_Postfix));
            Patch(harmony, typeof(Character), "GetHoverText", prefix: nameof(GetHoverText_Prefix));
            Patch(harmony, typeof(Character), "GetHoverName", prefix: nameof(GetHoverName_Prefix));
            Patch(harmony, typeof(Player), "FindHoverObject", postfix: nameof(FindHoverObject_Postfix));
        }

        /// <summary>How far away a townsperson you look straight at can be talked to (through a cell door's grate to the back of the cell).</summary>
        const float TalkReach = 9f;

        /// <summary>A townsperson in plain view a little beyond Valheim's 5 m reach can still be talked to.</summary>
        static void FindHoverObject_Postfix(Player __instance, ref UnityEngine.GameObject hover, Character hoverCreature)
        {
            if (hover == null && Of(hoverCreature) != null)
            {
                if (UnityEngine.Vector3.Distance(__instance.GetEyePoint(), hoverCreature.GetCenterPoint()) <= TalkReach) hover = hoverCreature.gameObject;
                return;
            }
            // a locked door with a viewport (a cell door): you talk through its grate to whoever
            // stands behind it where you look
            var door = hover != null ? hover.GetComponentInParent<Door>() : null;
            if (door == null || door.GetComponent<U5Fixed>() == null || !Utils.GetPrefabName(door.gameObject).StartsWith("wood_door")) return;
            var zdo = door.GetComponent<ZNetView>()?.GetZDO();
            if (zdo == null || zdo.GetString(Settlements.LockKey).Length == 0) return;
            var cam = GameCamera.instance != null ? GameCamera.instance.transform : null;
            if (cam == null) return;
            var eye = __instance.GetEyePoint();
            float side = UnityEngine.Vector3.Dot(eye - door.transform.position, door.transform.forward);
            Character best = null;
            float bestAngle = 25f;
            foreach (var kv in folk)
            {
                var c = kv.Key;
                if (c == null || kv.Value.IsDead || UnityEngine.Vector3.Distance(eye, c.GetCenterPoint()) > TalkReach) continue;
                // (on the far side of the door)
                if (UnityEngine.Vector3.Dot(c.transform.position - door.transform.position, door.transform.forward) * side >= 0) continue;
                float angle = UnityEngine.Vector3.Angle(cam.forward, c.GetCenterPoint() - cam.position);
                if (angle < bestAngle) { bestAngle = angle; best = c; }
            }
            if (best != null) hover = best.gameObject;
        }

        static void Patch(Harmony h, Type t, string m, string prefix = null, string postfix = null)
        {
            var method = AccessTools.Method(t, m);
            if (method == null) { Plugin.Log.LogWarning($"Townsfolk: {t.Name}.{m} not found"); return; }
            h.Patch(method, prefix: prefix != null ? new HarmonyMethod(typeof(TownsfolkPatches), prefix) : null,
                postfix: postfix != null ? new HarmonyMethod(typeof(TownsfolkPatches), postfix) : null);
        }

        static bool CheckDeath_Prefix(Character __instance)
        {
            var npc = Of(__instance);
            if (npc == null) return true;
            if (!npc.IsDead && __instance.GetHealth() <= 0f) npc.Fall();
            return false;
        }

        static void IsDead_Postfix(Character __instance, ref bool __result)
        {
            if (!__result && Of(__instance) is TownNpc npc && npc.IsDead) __result = true;
        }

        static bool Damage_Prefix(Character __instance, HitData hit)
        {
            var npc = Of(__instance);
            if (npc == null) return true;
            if (hit.GetAttacker() is Player p)
            {
                // a player's ballista bolts spare the townsfolk
                if (hit.m_hitType == HitData.HitType.Turret) return false;
                // attacking a townsperson is a crime: the guards come to arrest them (fighting the
                // guards they would not come quietly with is the fight itself); a villager cries
                // for the guards and runs
                if (!(npc.IsGuard && Crime.HostileTo(p) == npc.Location) && p == Player.m_localPlayer && npc.Home != null)
                {
                    Crime.Commit(p, npc.Home);
                    if (!npc.IsGuard) npc.Frighten();
                }
            }
            if (Plugin.SelfTest.Value) Plugin.Log.LogInfo($"townsfolk self-test blow: {npc.GetHoverName()} hit by {hit.GetAttacker()?.m_name ?? "?"} for {hit.GetTotalDamage():0.0} (health {__instance.GetHealth():0})");
            return true;
        }

        /// <summary>A guard's thrown spear does not land as loot (they draw another; the town is not littered with free spears).</summary>
        static void ProjectileSetup_Postfix(Projectile __instance, Character owner)
        {
            if (Of(owner) == null) return;
            __instance.m_respawnItemOnHit = false;
            __instance.m_spawnItem = null;
            if (Plugin.SelfTest.Value) Plugin.Log.LogInfo($"townsfolk self-test throw: {Of(owner).GetHoverName()} throws ({__instance.name})");
        }

        /// <summary>The hover finds the creature before the townsperson on the same body: it answers with the townsperson's name and Talk.</summary>
        static bool GetHoverText_Prefix(Character __instance, ref string __result)
        {
            var npc = Of(__instance);
            if (npc == null) return true;
            __result = npc.GetHoverText();
            return false;
        }

        static bool GetHoverName_Prefix(Character __instance, ref string __result)
        {
            var npc = Of(__instance);
            if (npc == null) return true;
            __result = npc.GetHoverName();
            return false;
        }

        static bool TestShow_Prefix(Character c, ref bool __result)
        {
            var npc = Of(c);
            if (npc == null || (!npc.IsDead && npc.Health < npc.MaxHealth)) return true;
            __result = false;
            return false;
        }
    }
}
