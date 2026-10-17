using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using U5InValheim.WorldGen.Core;
using UnityEngine;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// Life along Britannia's edges: water lilies in the river shallows (a copy of Valheim's lily
    /// clutter kept to rivers by the forest factor, which is treeless over the sea), and the bushes,
    /// rocks and trees of dressing.json along coasts, river banks and paths, spawned with each
    /// zone like the settlements.
    /// </summary>
    public static class RiverBanks
    {
        const string LilyClutterName = "u5_river_lilies";

        public static void Install(Harmony harmony)
        {
            try
            {
                harmony.Patch(AccessTools.Method(typeof(ClutterSystem), "Awake") ?? throw new MissingMethodException("ClutterSystem", "Awake"),
                    postfix: new HarmonyMethod(typeof(RiverBanks), nameof(ClutterAwake_Postfix)));
                harmony.Patch(AccessTools.Method(typeof(ZoneSystem), "PlaceLocations") ?? throw new MissingMethodException("ZoneSystem", "PlaceLocations"),
                    postfix: new HarmonyMethod(typeof(RiverBanks), nameof(PlaceLocations_Postfix)));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"River banks not installed: {e.Message}");
            }
        }

        /// <summary>Adds the river lilies to the clutter (enabled only while a Britannia world is loaded).</summary>
        static void ClutterAwake_Postfix(ClutterSystem __instance)
        {
            if (__instance.m_clutter.Any(c => c.m_name == LilyClutterName)) return;
            var lilies = __instance.m_clutter.FirstOrDefault(c => c.m_prefab != null && c.m_prefab.name.IndexOf("waterlil", StringComparison.OrdinalIgnoreCase) >= 0);
            if (lilies == null)
            {
                Plugin.Log.LogWarning("River lilies: Valheim's water lily clutter not found");
                return;
            }
            var river = new ClutterSystem.Clutter();
            foreach (var f in AccessTools.GetDeclaredFields(typeof(ClutterSystem.Clutter)))
                if (!f.IsStatic && f.FieldType != typeof(List<GameObject>)) f.SetValue(river, f.GetValue(lilies));
            river.m_name = LilyClutterName;
            river.m_enabled = Britannia.Active != null;
            river.m_biome = (Heightmap.Biome)0x1ff;
            river.m_minAlt = -1.6f;
            river.m_maxAlt = -0.25f;
            river.m_snapToWater = true;
            river.m_minOceanDepth = river.m_maxOceanDepth = 0;
            river.m_minVegetation = river.m_maxVegetation = 0;
            river.m_onCleared = river.m_onUncleared = true;
            river.m_maxTilt = 35;
            river.m_inForest = true;
            river.m_forestTresholdMin = 0;
            river.m_forestTresholdMax = BritanniaShape.SeaForestFactor - 1;
            __instance.m_clutter.Add(river);
            Plugin.Log.LogInfo($"River lilies added (copy of {lilies.m_name})");
        }

        /// <summary>Keeps the river lilies to Britannia worlds (called when a world activates).</summary>
        public static void WorldChanged()
        {
            var clutter = ClutterSystem.instance?.m_clutter.FirstOrDefault(c => c.m_name == LilyClutterName);
            if (clutter != null) clutter.m_enabled = Britannia.Active != null;
        }

        static Dressing dressing;

        /// <summary>Dresses the coasts, river banks and paths of a zone being generated (dressing.json).</summary>
        static void PlaceLocations_Postfix(Vector2s zoneID, ZoneSystem.SpawnMode mode, List<GameObject> spawnedObjects)
        {
            var shape = Britannia.Active;
            if (shape == null || (mode != ZoneSystem.SpawnMode.Full && mode != ZoneSystem.SpawnMode.Ghost)) return;
            if (dressing == null)
            {
                try { dressing = Dressing.Parse(File.ReadAllText(Path.Combine(Plugin.PluginDirectory, "mappings", "dressing.json"))); }
                catch (Exception e) { Plugin.Log.LogError($"dressing.json: {e.Message}"); dressing = new Dressing(); }
            }
            var centre = ZoneSystem.GetZonePos(zoneID);
            float half = ZoneSystem.c_ZoneSize / 2f;
            float x0 = centre.x - half, z0 = centre.z - half, x1 = centre.x + half, z1 = centre.z + half;
            int spawned = 0;
            // a cell belongs to the zone its (jittered) point falls in, so nothing is placed twice
            for (int cz = Mathf.FloorToInt(z0 / Dressing.Cell) - 1; cz <= Mathf.FloorToInt(z1 / Dressing.Cell); cz++)
            for (int cx = Mathf.FloorToInt(x0 / Dressing.Cell) - 1; cx <= Mathf.FloorToInt(x1 / Dressing.Cell); cx++)
            {
                var name = dressing.Pick(shape, cx, cz, out float x, out float z, out float height, out float yaw);
                if (name == null || x < x0 || x >= x1 || z < z0 || z >= z1 || InSettlement(shape, x, z)) continue;
                var prefab = ZNetScene.instance.GetPrefab(name);
                if (prefab == null) continue;
                if (mode == ZoneSystem.SpawnMode.Ghost) ZNetView.StartGhostInit();
                var go = UnityEngine.Object.Instantiate(prefab, new Vector3(x, Britannia.SeaLevel + height, z), Quaternion.Euler(0, yaw, 0));
                if (mode == ZoneSystem.SpawnMode.Ghost)
                {
                    spawnedObjects.Add(go);
                    ZNetView.FinishGhostInit();
                }
                spawned++;
            }
            if (spawned > 0) Plugin.Log.LogDebug($"Zone {zoneID}: {spawned} edge plants and rocks");
        }

        static bool InSettlement(BritanniaShape shape, float x, float z)
        {
            foreach (var site in shape.Sites)
                if (Math.Max(Math.Abs(x - site.CentreX), Math.Abs(z - site.CentreZ)) < site.Half + site.Blend) return true;
            return false;
        }
    }
}
