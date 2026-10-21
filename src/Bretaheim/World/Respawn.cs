using HarmonyLib;
using Bretaheim.WorldGen.Core;
using UnityEngine;

namespace Bretaheim.WorldGen
{
    /// <summary>
    /// A player who has died comes back in the middle of Lord British's throne room (town_tiles.json
    /// "respawn"), as the original's Avatar wakes before Lord British, not at a bed or the world's
    /// start. Logging in still puts a player where they logged out.
    /// </summary>
    public static class Respawn
    {
        public static void Install(Harmony harmony)
        {
            var find = AccessTools.Method(typeof(Game), "FindSpawnPoint");
            if (find == null) { Plugin.Log.LogWarning("Respawn: Game.FindSpawnPoint not found"); return; }
            harmony.Patch(find, prefix: new HarmonyMethod(typeof(Respawn), nameof(FindSpawnPoint_Prefix)));
        }

        /// <summary>The throne room's middle (world), or null when its settlement is not built.</summary>
        public static Vector3? Point()
        {
            var r = Settlements.Tiles?.Respawn;
            if (r == null) return null;
            var town = Settlements.Get(r.Value.location);
            if (town == null) return null;
            var (x, y, z) = town.Plan.TilePos(r.Value.z, r.Value.y * SettlementPlan.Size + r.Value.x);
            return town.Origin + new Vector3(x, y + 0.3f, z);
        }

        static bool FindSpawnPoint_Prefix(Game __instance, ref Vector3 point, ref bool usedLogoutPoint, float dt, ref bool __result)
        {
            if (Britannia.Active == null) return true;
            var t = Traverse.Create(__instance);
            if (!t.Field("m_respawnAfterDeath").GetValue<bool>()) return true;
            var at = Point();
            if (at == null) return true;
            // as Valheim waits for a bed's area: the throne room loaded, then there
            float wait = t.Field("m_respawnWait").GetValue<float>() + dt;
            t.Field("m_respawnWait").SetValue(wait);
            usedLogoutPoint = false;
            ZNet.instance.SetReferencePosition(at.Value);
            if (wait > __instance.m_respawnLoadDuration && ZNetScene.instance.IsAreaReady(at.Value))
            {
                point = at.Value;
                __result = true;
                Plugin.Log.LogInfo($"Respawned in Lord British's throne room {at.Value}");
            }
            else
            {
                point = Vector3.zero;
                __result = false;
            }
            return false;
        }
    }
}
