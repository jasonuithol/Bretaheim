using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using U5InValheim.WorldGen.Core;
using UnityEngine;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// Life along Britannia's rivers: water lilies in the shallows (a copy of Valheim's lily
    /// clutter kept to rivers by the forest factor, which is treeless over the sea) and shrubs and
    /// the odd berry bush on the banks, spawned with each zone like the settlements.
    /// </summary>
    public static class RiverBanks
    {
        const string LilyClutterName = "u5_river_lilies";
        const float Step = 3f, Density = 0.09f;
        static readonly Dictionary<VBiome, string[]> Shrubs = new Dictionary<VBiome, string[]>
        {
            { VBiome.Meadows, new[] { "Bush01", "Bush01", "shrub_2", "shrub_2", "RaspberryBush" } },
            { VBiome.BlackForest, new[] { "Bush01", "shrub_2", "Bush02_en", "BlueberryBush" } },
            { VBiome.Swamp, new[] { "shrub_2", "Bush02_en" } },
            { VBiome.Plains, new[] { "shrub_2_heath", "Bush01_heath", "CloudberryBush" } },
        };

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

        static void PlaceLocations_Postfix(Vector2s zoneID, ZoneSystem.SpawnMode mode, List<GameObject> spawnedObjects)
        {
            var shape = Britannia.Active;
            if (shape == null || (mode != ZoneSystem.SpawnMode.Full && mode != ZoneSystem.SpawnMode.Ghost)) return;
            var centre = ZoneSystem.GetZonePos(zoneID);
            float half = ZoneSystem.c_ZoneSize / 2f;
            int spawned = 0;
            for (float dz = -half; dz < half; dz += Step)
            for (float dx = -half; dx < half; dx += Step)
            {
                uint h = Hash(Mathf.RoundToInt(centre.x + dx), Mathf.RoundToInt(centre.z + dz));
                float x = centre.x + dx + (h & 0xff) / 255f * Step, z = centre.z + dz + ((h >> 8) & 0xff) / 255f * Step;
                float bank = shape.RiverBank(x, z);
                if (bank <= 0 || ((h >> 16) & 0xffff) / 65535f > bank * Density) continue;
                if (InSettlement(shape, x, z)) continue;
                float height = shape.Height(x, z);
                if (height < 0.3f) continue;
                if (!Shrubs.TryGetValue(shape.Biome(x, z), out var choices)) continue;
                var prefab = ZNetScene.instance.GetPrefab(choices[(h >> 3) % (uint)choices.Length]);
                if (prefab == null) continue;
                if (mode == ZoneSystem.SpawnMode.Ghost) ZNetView.StartGhostInit();
                var go = UnityEngine.Object.Instantiate(prefab, new Vector3(x, Britannia.SeaLevel + height, z), Quaternion.Euler(0, h % 360, 0));
                if (mode == ZoneSystem.SpawnMode.Ghost)
                {
                    spawnedObjects.Add(go);
                    ZNetView.FinishGhostInit();
                }
                spawned++;
            }
            if (spawned > 0) Plugin.Log.LogDebug($"Zone {zoneID}: {spawned} river bank shrubs");
        }

        static bool InSettlement(BritanniaShape shape, float x, float z)
        {
            foreach (var site in shape.Sites)
                if (Math.Max(Math.Abs(x - site.CentreX), Math.Abs(z - site.CentreZ)) < site.Half + site.Blend) return true;
            return false;
        }

        static uint Hash(int x, int z)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093 ^ z * 19349663 ^ 0x5bd1e995);
                h = (h ^ (h >> 13)) * 1274126177u;
                return h ^ (h >> 16);
            }
        }
    }
}
