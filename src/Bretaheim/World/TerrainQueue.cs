using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace Bretaheim.WorldGen
{
    /// <summary>
    /// Valheim's terrain builder thread works through one queue and keeps only the last 16 meshes it
    /// built. Moving quickly over new land queues more zones than that, so a zone found ready can lose
    /// its build to the next one finished, before the zone is made; the game then waits for it at the
    /// back of the whole queue (RequestTerrainSync spins until done): the 0.7–3 s freezes seen when
    /// arriving. A build the game is waiting on goes to the front of the queue instead, and the
    /// builder keeps 64 finished builds, so far fewer are thrown away and built again.
    /// </summary>
    public static class TerrainQueue
    {
        const int KeepReady = 64;

        public static void Install(Harmony harmony)
        {
            var sync = AccessTools.Method(typeof(HeightmapBuilder), nameof(HeightmapBuilder.RequestTerrainSync));
            var thread = AccessTools.Method(typeof(HeightmapBuilder), "BuildThread");
            if (sync == null || thread == null) { Plugin.Log.LogWarning("TerrainQueue: HeightmapBuilder has changed; left as it is"); return; }
            harmony.Patch(sync, prefix: new HarmonyMethod(typeof(TerrainQueue), nameof(RequestTerrainSync_Prefix)));
            harmony.Patch(thread, transpiler: new HarmonyMethod(typeof(TerrainQueue), nameof(BuildThread_Transpiler)));
        }

        /// <summary>Not built yet: queued (or moved) next after the build in progress.</summary>
        static void RequestTerrainSync_Prefix(HeightmapBuilder __instance, Vector3 center, int width, float scale, bool distantLod, WorldGenerator worldGen)
        {
            var t = Traverse.Create(__instance);
            object gate = t.Field("m_lock").GetValue();
            var ready = t.Field("m_ready").GetValue<List<HeightmapBuilder.HMBuildData>>();
            var toBuild = t.Field("m_toBuild").GetValue<List<HeightmapBuilder.HMBuildData>>();
            if (gate == null || ready == null || toBuild == null) return;
            lock (gate)
            {
                foreach (var r in ready)
                    if (r.IsEqual(center, width, scale, distantLod, worldGen)) return;
                // (the first one may be building now: it stays first)
                int at = toBuild.FindIndex(b => b.IsEqual(center, width, scale, distantLod, worldGen));
                if (at >= 0 && at <= 1) return;
                var data = at >= 0 ? toBuild[at] : new HeightmapBuilder.HMBuildData(center, width, scale, distantLod, worldGen);
                if (at >= 0) toBuild.RemoveAt(at);
                toBuild.Insert(Mathf.Min(1, toBuild.Count), data);
            }
        }

        /// <summary>The builder keeps KeepReady finished builds instead of 16.</summary>
        static IEnumerable<CodeInstruction> BuildThread_Transpiler(IEnumerable<CodeInstruction> code)
        {
            int changed = 0;
            foreach (var ins in code)
            {
                if ((ins.opcode == OpCodes.Ldc_I4_S || ins.opcode == OpCodes.Ldc_I4) && System.Convert.ToInt32(ins.operand) == 16)
                {
                    ins.opcode = OpCodes.Ldc_I4;
                    ins.operand = KeepReady;
                    changed++;
                }
                yield return ins;
            }
            if (changed != 1) Plugin.Log.LogWarning($"TerrainQueue: expected one ready-queue limit in BuildThread, changed {changed}");
        }
    }
}
