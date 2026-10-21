using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using Bretaheim.Data;
using Bretaheim.WorldGen;
using Bretaheim.WorldGen.Core;
using UnityEngine;

namespace Bretaheim.DevTools
{
    /// <summary>
    /// U5_SELFTEST=transport on a dedicated server: a frigate and a skiff delivered to each
    /// shipwright's dock (afloat, upright, the frigate's skiffs lowered and its crew taking to one
    /// when it sinks), a pirate ship off Jhelom with its crew (aboard, then dead, the ship
    /// captured), and a horse led out by Trinsic's stables (tame, saddled, twice a player's pace).
    /// Logged as "transport self-test".
    /// </summary>
    public static class TransportSelfTest
    {
        static void Log(string s) => Plugin.Log.LogInfo("transport self-test " + s);

        static string Ship(GameObject go)
        {
            if (go == null) return "GONE";
            var zdo = go.GetComponent<ZNetView>()?.GetZDO();
            float water = ZoneSystem.instance.m_waterLevel;
            var wnt = go.GetComponent<WearNTear>();
            return $"{Utils.GetPrefabName(go.name)} at ({go.transform.position.x:0}, {go.transform.position.z:0}) y {go.transform.position.y - water:0.00} from the sea, upright {go.transform.up.y:0.00}, " +
                   $"health {zdo?.GetFloat(ZDOVars.s_health, wnt?.m_health ?? 0):0}/{wnt?.m_health:0}, owner \"{zdo?.GetString(Vessels.OwnerNameKey)}\", skiffs {zdo?.GetInt(Vessels.SkiffsKey)}, pirate {zdo?.GetBool(Vessels.PirateKey)}";
        }

        static int KarvesNear(Vector3 at, float r) => UnityEngine.Object.FindObjectsByType<Ship>(FindObjectsSortMode.None)
            .Count(s => Utils.GetPrefabName(s.gameObject.name) == Vessels.SkiffPrefab && Vector3.Distance(s.transform.position, at) < r);

        public static IEnumerator Run(Func<Vector3, IEnumerator> generate)
        {
            var shops = Plugin.U5.Shops;
            var shape = Britannia.Active;
            GameObject firstFrigate = null;
            foreach (int town in Plugin.U5.Ovl.ShopTowns(ShopTrade.Shipwright))
            {
                var b = Settlements.Get(town);
                if (b == null) { Log($"shipwright {town}: not built"); continue; }
                var keeper = Plugin.U5.NpcsOf(b.Location).FirstOrDefault(n => n.Slot > 0 && n.IsShopkeeper && n.Trade == ShopTrade.Shipwright);
                var shop = shops.Of(b.Location, keeper);
                var (dx, dy) = shops.Dock(shop);
                bool found = Docks.Berth(shape, dx, dy, b.Location.X, b.Location.Y, b.Site.CentreX, b.Site.CentreZ, b.Site.Half + b.Site.Blend, out float x, out float z);
                Log($"{b.Location.Name}: {shop.Name} ({shop.Keeper}), frigate {shops.FrigatePrice(shop, 15)} gp, skiff {shops.SkiffPrice(shop, 15)} gp, dock ({dx},{dy}), berth {(found ? "" : "NOT FOUND, ")}({x:0}, {z:0}) {Vector3.Distance(new Vector3(x, 0, z), new Vector3(b.Site.CentreX, 0, b.Site.CentreZ)):0} m from the town's centre");
                if (!found) continue;
                var berth = new Vector3(x, 0, z);
                Log($"{b.Location.Name} berth before its zones: water ready {Vessels.WaterReady(berth)}");
                for (int i = 0; i < 20 && !Vessels.WaterReady(berth); i++) yield return generate(berth);
                var volumes = UnityEngine.Object.FindObjectsByType<WaterVolume>(FindObjectsSortMode.None)
                    .Where(w => Vector3.Distance(new Vector3(w.transform.position.x, 0, w.transform.position.z), berth) < 100).ToList();
                Log($"{b.Location.Name} berth zone {ZoneSystem.GetZone(berth)} loaded {ZoneSystem.instance.IsZoneLoaded(berth)}; water volumes within 100 m: {volumes.Count}");
                WaterVolume wv = null;
                Log($"{b.Location.Name} berth: water ready {Vessels.WaterReady(berth)}, ground {ZoneSystem.instance.GetGroundHeight(berth) - ZoneSystem.instance.m_waterLevel:0.0} m, Britannia {shape.Height(x, z):0.0} m, water surface {Floating.GetWaterLevel(berth + Vector3.up * (ZoneSystem.instance.m_waterLevel - 1), ref wv) - ZoneSystem.instance.m_waterLevel:0.00}");
                var frigate = Vessels.Spawn(true, berth, 0, 1, "self-test", Shops.SkiffsAboard(Shops.BoughtFrigate));
                var skiff = Vessels.Spawn(false, new Vector3(x + 12, 0, z), 90, 1, "self-test", 0);
                // (a playerless server keeps no zones: made again round the dock while we wait)
                for (int i = 0; i < 4; i++) yield return generate(berth);
                Log($"{b.Location.Name} frigate after 15 s: {Ship(frigate)}");
                Log($"{b.Location.Name} skiff after 15 s: {Ship(skiff)}");
                if (firstFrigate == null) firstFrigate = frigate;
                else if (frigate != null) ZNetScene.instance.Destroy(frigate);
                if (skiff != null) ZNetScene.instance.Destroy(skiff);
            }

            // a skiff lowered from the frigate, then the frigate sunk: its crew take to the other
            if (firstFrigate != null)
            {
                var at = firstFrigate.transform.position;
                int before = KarvesNear(at, 30);
                Log($"before lowering a skiff: {Ship(firstFrigate)}");
                firstFrigate.GetComponent<ZNetView>().InvokeRPC("u5_LowerSkiff", 1L, "self-test");
                yield return generate(at);
                Log($"lowered a skiff: {Ship(firstFrigate)}; skiffs near {before} -> {KarvesNear(at, 30)}");
                firstFrigate.GetComponent<ZNetView>().InvokeRPC("u5_AddSkiff", 1);
                yield return new WaitForSeconds(1f);
                Log($"a skiff put aboard: {Ship(firstFrigate)}");
                before = KarvesNear(at, 30);
                Traverse.Create(firstFrigate.GetComponent<WearNTear>()).Method("Destroy", new Type[] { typeof(HitData), typeof(bool) }).GetValue(null, false);
                yield return generate(at);
                Log($"sunk: {(firstFrigate == null ? "gone" : "STILL AFLOAT")}; skiffs near {before} -> {KarvesNear(at, 30)}");
            }

            // pirates off Jhelom
            var jhelom = Settlements.Get(3);
            if (jhelom != null)
            {
                Vector3 sea = Vector3.zero;
                bool found = false;
                for (float r = 120; r < 400 && !found; r += 20)
                    for (int i = 0; i < 16 && !found; i++)
                    {
                        float a = i * Mathf.PI / 8;
                        var p = new Vector3(jhelom.Site.CentreX + Mathf.Cos(a) * r, 0, jhelom.Site.CentreZ + Mathf.Sin(a) * r);
                        if (Docks.Floats(shape, p.x, p.z)) { sea = p; found = true; }
                    }
                if (!found) Log("pirates: no sea near Jhelom");
                else
                {
                    yield return generate(sea);
                    var said = Pirates.Spawn(sea, new System.Random(3));
                    var ship = UnityEngine.Object.FindObjectsByType<PirateShip>(FindObjectsSortMode.None).OrderBy(s => Vector3.Distance(s.transform.position, sea)).FirstOrDefault();
                    var shipGo = ship != null ? ship.gameObject : null;
                    Log($"pirates: {said ?? "none"}, water ready there {Vessels.WaterReady(sea)}");
                    for (int i = 0; i < 3; i++) yield return generate(sea);
                    if (ship == null) Log("pirates: NO PIRATE SHIP");
                    else
                    {
                        var zdo = ship.GetComponent<ZNetView>().GetZDO();
                        var crew = Pirates.Crew(zdo, ship.transform.position, 60f);
                        Log($"pirates after 10 s: {Ship(ship.gameObject)}; crew {crew.Count}: " + string.Join("; ", crew.Select(c =>
                        {
                            var local = ship.transform.InverseTransformPoint(c.transform.position);
                            var st = UltimaCreatures.Of(c);
                            return $"{Utils.GetPrefabName(c.gameObject.name)} {(st != null ? Plugin.U5.Ovl.CreatureName(st.Type) : "not Ultima")} hp {c.GetHealth():0} at deck ({local.x:0.0}, {local.y:0.0}, {local.z:0.0}) {(c.IsSwimming() ? "SWIMMING" : "")}";
                        })));
                        foreach (var c in crew) c.SetHealth(0);
                        yield return new WaitForSeconds(3f);
                        Log($"pirates: crew left {Pirates.Crew(zdo, ship.transform.position, 60f).Count}");
                        ship.Capture(1, "self-test");
                        yield return new WaitForSeconds(1f);
                        Log($"pirates captured: {Ship(shipGo)}, still a pirate ship {shipGo != null && shipGo.GetComponent<PirateShip>() != null}");
                    }
                }
            }

            // a horse at Trinsic's stables
            var trinsic = Settlements.Get(6) ?? Settlements.Get(2);
            yield return generate(trinsic.Origin);
            var keeperAt = trinsic.Origin;
            var spotFound = Horses.FreeSpot(keeperAt, out var spot);
            Log($"horse: free spot from {trinsic.Location.Name}'s centre {(spotFound ? $"{Vector3.Distance(spot, keeperAt):0.0} m away" : "NONE")}");
            var horse = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab(Horses.PrefabName), spotFound ? spot : keeperAt + Vector3.up * 2, Quaternion.identity);
            yield return new WaitForSeconds(3f);
            var hc = horse.GetComponent<Character>();
            var player = ZNetScene.instance.GetPrefab("Player").GetComponent<Player>();
            Log($"horse: {hc.m_name} tamed {hc.IsTamed()}, saddle {horse.GetComponent<ZNetView>().GetZDO().GetBool(ZDOVars.s_haveSaddleHash)} (shown {horse.GetComponent<Tameable>()?.m_saddle?.gameObject.activeSelf}), " +
                $"speeds walk {hc.m_walkSpeed:0.0}/jog {hc.m_speed:0.0}/run {hc.m_runSpeed:0.0} against a player's {player.m_walkSpeed:0.0}/{player.m_speed:0.0}/{player.m_runSpeed:0.0}, ultima {UltimaCreatures.Of(hc) != null}, faction {hc.m_faction}");
            ZNetScene.instance.Destroy(horse);
        }
    }
}
