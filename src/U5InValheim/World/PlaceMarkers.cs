using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using U5InValheim.WorldGen.Core;
using UnityEngine;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// Phase 1 placeholders for the places of Britannia: a sign with the name stands on each one
    /// (spawned once, when its zone is generated, so it is saved with the world) and every place
    /// gets a minimap pin. Phase 2 replaces the signs with the real towns.
    /// </summary>
    public static class PlaceMarkers
    {
        const float ClearRadius = 6f;
        static Dictionary<Vector2s, List<Poi>> byZone;
        static Type clearAreaType;

        public static void Install(Harmony harmony)
        {
            try
            {
                var place = AccessTools.Method(typeof(ZoneSystem), "PlaceLocations") ?? throw new MissingMethodException("ZoneSystem.PlaceLocations");
                clearAreaType = AccessTools.Inner(typeof(ZoneSystem), "ClearArea") ?? throw new MissingMemberException("ZoneSystem.ClearArea");
                harmony.Patch(place, postfix: new HarmonyMethod(typeof(PlaceMarkers), nameof(PlaceLocations_Postfix)));
                var spawned = AccessTools.Method(typeof(Player), "OnSpawned") ?? throw new MissingMethodException("Player.OnSpawned");
                harmony.Patch(spawned, postfix: new HarmonyMethod(typeof(PlaceMarkers), nameof(OnSpawned_Postfix)));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Place markers not installed: {e.Message}");
            }
        }

        static Dictionary<Vector2s, List<Poi>> ByZone()
        {
            if (byZone != null) return byZone;
            byZone = new Dictionary<Vector2s, List<Poi>>();
            foreach (var p in Britannia.Pois)
            {
                if (p.Underworld) continue;
                var zone = ZoneSystem.GetZone(Britannia.TileCentre(p.TileX, p.TileY));
                if (!byZone.TryGetValue(zone, out var list)) byZone[zone] = list = new List<Poi>();
                list.Add(p);
            }
            return byZone;
        }

        /// <summary>Called when a new world is activated.</summary>
        public static void Reset() => byZone = null;

        // Runs once per zone, when the zone is generated (Full on the owner, Ghost for zones
        // generated ahead of time). Clear areas added here keep trees off the sign.
        static void PlaceLocations_Postfix(Vector2s zoneID, IList clearAreas, ZoneSystem.SpawnMode mode, List<GameObject> spawnedObjects)
        {
            if (Britannia.Active == null || (mode != ZoneSystem.SpawnMode.Full && mode != ZoneSystem.SpawnMode.Ghost)) return;
            if (!ByZone().TryGetValue(zoneID, out var pois)) return;
            var prefab = ZNetScene.instance.GetPrefab("sign");
            if (prefab == null) return;

            foreach (var p in pois)
            {
                var pos = Britannia.TileCentre(p.TileX, p.TileY);
                clearAreas.Add(Activator.CreateInstance(clearAreaType, pos, ClearRadius));
                if (mode == ZoneSystem.SpawnMode.Ghost) ZNetView.StartGhostInit();
                var sign = UnityEngine.Object.Instantiate(prefab, pos + Vector3.up * 0.1f, Quaternion.identity);
                var zdo = sign.GetComponent<ZNetView>()?.GetZDO();
                zdo?.Set(ZDOVars.s_text, p.Name);
                if (mode == ZoneSystem.SpawnMode.Ghost)
                {
                    spawnedObjects.Add(sign);
                    ZNetView.FinishGhostInit();
                }
            }
        }

        static Minimap pinnedOn;

        static void OnSpawned_Postfix(Player __instance)
        {
            if (Britannia.Active == null || __instance != Player.m_localPlayer || Minimap.instance == null) return;
            if (pinnedOn == Minimap.instance) return;
            pinnedOn = Minimap.instance;
            foreach (var p in Britannia.Pois)
            {
                if (p.Underworld) continue;
                var type = p.Kind == "towne" || p.Kind == "castle" || p.Kind == "village" || p.Kind == "keep"
                    ? Minimap.PinType.Icon1 : Minimap.PinType.Icon3;
                Minimap.instance.AddPin(Britannia.TileCentre(p.TileX, p.TileY), type, p.Name, save: false, isChecked: false);
            }
        }
    }
}
