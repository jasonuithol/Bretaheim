using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace U5InValheim.DevTools
{
    /// <summary>
    /// Finds what makes loading pause: every frame longer than HitchMs is logged ("hitch") with the
    /// time spent in the timed sections since the frame before (Valheim's zone spawning, terrain
    /// meshes, vegetation and networked-object creation; our settlement pieces, townsfolk and
    /// structures, with what each structure built), the rest "other" (rendering, physics,
    /// everything untimed). Sections nest: SpawnZone includes the terrain, vegetation and the
    /// settlement pieces spawned in it; CreateDestroyObjects includes CreateObject.
    /// </summary>
    public class LoadProfiler : MonoBehaviour
    {
        const double HitchMs = 150;
        static readonly Dictionary<string, (double ms, int n)> frame = new Dictionary<string, (double, int)>();
        static int structureObjects, structureRenderers, structureColliders;
        readonly Stopwatch since = Stopwatch.StartNew();

        static readonly (System.Type type, string method)[] Timed =
        {
            (typeof(ZoneSystem), "SpawnZone"),
            (typeof(ZoneSystem), "PlaceVegetation"),
            (typeof(Heightmap), "Generate"),
            (typeof(HeightmapBuilder), "RequestTerrainSync"),
            (typeof(Heightmap), "ApplyModifiers"),
            (typeof(ZNetScene), "CreateDestroyObjects"),
            (typeof(ZNetScene), "CreateObject"),
            (typeof(WorldGen.Settlements), "PlaceLocations_Postfix"),
            (typeof(WorldGen.TownsfolkSpawner), "Spawn"),
            (typeof(WorldGen.U5Structure), "Build"),
            (typeof(WorldGen.Settlements), "Prepare"),
            (typeof(Minimap), "GenerateWorldMap"),
        };

        public static void Install(Harmony harmony)
        {
            var pre = new HarmonyMethod(typeof(LoadProfiler), nameof(Prefix));
            var post = new HarmonyMethod(typeof(LoadProfiler), nameof(Postfix));
            foreach (var (type, name) in Timed)
                foreach (var m in AccessTools.GetDeclaredMethods(type).Where(m => m.Name == name && !m.IsAbstract))
                    harmony.Patch(m, prefix: pre, postfix: post);
        }

        static void Prefix(out long __state) => __state = Stopwatch.GetTimestamp();

        static void Postfix(long __state, MethodBase __originalMethod, object __instance)
        {
            double ms = (Stopwatch.GetTimestamp() - __state) * 1000.0 / Stopwatch.Frequency;
            string key = __originalMethod.DeclaringType.Name + "." + __originalMethod.Name;
            frame.TryGetValue(key, out var v);
            frame[key] = (v.ms + ms, v.n + 1);
            // one call that long: what it ran on and who called it
            if (ms >= SlowCallMs)
            {
                string on = __instance is Component c && c != null ? $" on {c.name} at {c.transform.position}" + (c is Heightmap h ? $" (distant LOD {h.IsDistantLod})" : "") : "";
                if (__instance is HeightmapBuilder b) on = $" (afterwards {Traverse.Create(b).Field("m_toBuild").GetValue<System.Collections.ICollection>()?.Count} to build, {Traverse.Create(b).Field("m_ready").GetValue<System.Collections.ICollection>()?.Count} ready)";
                Plugin.Log.LogInfo($"slow call {key} {ms:0} ms{on}\n{new StackTrace(1, false)}");
            }
        }

        const double SlowCallMs = 500;

        /// <summary>What a structure just built (counted from its templates).</summary>
        public static void Built(int objects, int renderers, int colliders)
        {
            structureObjects += objects;
            structureRenderers += renderers;
            structureColliders += colliders;
        }

        void LateUpdate()
        {
            double ms = since.Elapsed.TotalMilliseconds;
            since.Restart();
            if (ms >= HitchMs)
            {
                double timed = frame.Where(kv => kv.Key != "ZNetScene.CreateObject" && kv.Key != "ZoneSystem.PlaceVegetation"
                                                 && kv.Key != "HeightmapBuilder.RequestTerrainSync" && kv.Key != "Heightmap.ApplyModifiers"
                                                 && kv.Key != "Heightmap.Generate" && kv.Key != "Settlements.PlaceLocations_Postfix"
                                                 && kv.Key != "TownsfolkSpawner.Spawn").Sum(kv => kv.Value.ms);
                string parts = string.Join(", ", frame.OrderByDescending(kv => kv.Value.ms).Select(kv => $"{kv.Key} {kv.Value.ms:0} ms ×{kv.Value.n}"));
                string built = structureObjects > 0 ? $"; structures built {structureObjects} objects, {structureRenderers} renderers, {structureColliders} colliders" : "";
                Plugin.Log.LogInfo($"hitch {ms:0} ms: {(parts.Length > 0 ? parts : "nothing timed")}{built}; other ≈ {System.Math.Max(0, ms - timed):0} ms");
            }
            frame.Clear();
            structureObjects = structureRenderers = structureColliders = 0;
        }
    }
}
