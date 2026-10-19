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

        public static void Install(Harmony harmony)
        {
            Patch(harmony, typeof(Character), "CheckDeath", prefix: nameof(CheckDeath_Prefix));
            Patch(harmony, typeof(Character), "IsDead", postfix: nameof(IsDead_Postfix));
            Patch(harmony, typeof(Character), "Damage", prefix: nameof(Damage_Prefix));
            Patch(harmony, typeof(EnemyHud), "TestShow", prefix: nameof(TestShow_Prefix));
            Patch(harmony, typeof(Projectile), "Setup", postfix: nameof(ProjectileSetup_Postfix));
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
            if (hit.GetAttacker() is Player) return false;
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

        static bool TestShow_Prefix(Character c, ref bool __result)
        {
            var npc = Of(c);
            if (npc == null || (!npc.IsDead && npc.Health < npc.MaxHealth)) return true;
            __result = false;
            return false;
        }
    }
}
