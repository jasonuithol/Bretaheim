using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Bretaheim.WorldGen.Core;
using UnityEngine;

namespace Bretaheim.WorldGen
{
    /// <summary>
    /// The overworld's wooden bridges (<see cref="BridgePlanner"/>), spawned with the zone their
    /// tile's middle is in, as settlement pieces are: fixed (no wear, damage or removal), the wild's
    /// trees and rocks kept off them.
    /// </summary>
    public static class Bridges
    {
        static Type clearAreaType;
        static BritanniaShape planned;
        static List<(Vector3 centre, List<Placement> pieces)> bridges = new List<(Vector3, List<Placement>)>();

        public static void Install(Harmony harmony)
        {
            try
            {
                clearAreaType = AccessTools.Inner(typeof(ZoneSystem), "ClearArea") ?? throw new MissingMemberException("ZoneSystem", "ClearArea");
                harmony.Patch(AccessTools.Method(typeof(ZoneSystem), "PlaceLocations") ?? throw new MissingMethodException("ZoneSystem", "PlaceLocations"),
                    postfix: new HarmonyMethod(typeof(Bridges), nameof(PlaceLocations_Postfix)));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Bridges not installed: {e.Message}");
            }
        }

        /// <summary>The bridges of the active Britannia (planned once per world).</summary>
        public static List<(Vector3 centre, List<Placement> pieces)> All()
        {
            var shape = Britannia.Active;
            if (shape == null) return new List<(Vector3, List<Placement>)>();
            if (planned == shape) return bridges;
            planned = shape;
            bridges = BridgePlanner.Bridges(shape).Select(b =>
            {
                shape.TileCentre(b.x, b.y, out float x, out float z);
                return (new Vector3(x, Britannia.SeaLevel, z), BridgePlanner.Plan(shape, b.x, b.y, b.eastWest));
            }).ToList();
            Plugin.Log.LogInfo($"Bridges: {bridges.Count} ({bridges.Sum(b => b.pieces.Count)} pieces)");
            return bridges;
        }

        static void PlaceLocations_Postfix(Vector2s zoneID, System.Collections.IList clearAreas, ZoneSystem.SpawnMode mode, List<GameObject> spawnedObjects)
        {
            if (Britannia.Active == null || (mode != ZoneSystem.SpawnMode.Full && mode != ZoneSystem.SpawnMode.Ghost)) return;
            foreach (var (centre, pieces) in All())
            {
                if (ZoneSystem.GetZone(centre) != zoneID) continue;
                float reach = pieces.Max(p => Mathf.Max(Mathf.Abs(p.X - centre.x), Mathf.Abs(p.Z - centre.z))) + 4f;
                clearAreas.Add(Activator.CreateInstance(clearAreaType, centre, reach));
                int spawned = 0;
                foreach (var p in pieces)
                {
                    var prefab = ZNetScene.instance.GetPrefab(p.Prefab);
                    if (prefab == null) continue;
                    if (mode == ZoneSystem.SpawnMode.Ghost) ZNetView.StartGhostInit();
                    var rot = Quaternion.Euler(0, p.Yaw, 0);
                    if (p.Turned) rot *= Quaternion.Euler(p.Tilt, 0, 0);
                    var go = UnityEngine.Object.Instantiate(prefab, new Vector3(p.X, Britannia.SeaLevel + p.Y, p.Z), rot);
                    var scale = p.Scaled ? new Vector3(p.SX, p.SY, p.SZ) : Vector3.zero;
                    if (p.Scaled) go.transform.localScale = scale;
                    var zdo = go.GetComponent<ZNetView>()?.GetZDO();
                    if (zdo != null)
                    {
                        zdo.Set(Settlements.FixedKey, true);
                        if (p.Scaled) zdo.Set(Settlements.ScaleKey, scale);
                        var wnt = go.GetComponent<WearNTear>();
                        if (wnt != null) FixedPieces.Apply(wnt, zdo);
                    }
                    if (mode == ZoneSystem.SpawnMode.Ghost)
                    {
                        spawnedObjects.Add(go);
                        ZNetView.FinishGhostInit();
                    }
                    spawned++;
                }
                Plugin.Log.LogInfo($"Zone {zoneID}: a bridge of {spawned} pieces at {centre}");
            }
        }
    }
}
