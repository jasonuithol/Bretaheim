using System;
using System.Collections;
using System.Linq;
using Bretaheim.Data;
using Bretaheim.WorldGen;
using Bretaheim.WorldGen.Core;
using UnityEngine;

namespace Bretaheim.DevTools
{
    /// <summary>
    /// U5_SELFTEST=under on a dedicated server: the Underworld's zones are made past Valheim's
    /// world edge (its far east too), the ground there is the Underworld's, the sea there is not
    /// dropped at the edge, the entrances (Doom's, and the way in from below to Destard) and Ararat
    /// are built, the Amulet and the shards lie there, a dungeon's bottom leads there, its
    /// encounters come from its tables, its environment is made, a whirlpool drifts, and a ship
    /// dragged down a whirlpool floats again at (0x22, 0x12) of the Underworld. Each check logs
    /// "underworld self-test: …".
    /// </summary>
    public static class UnderworldSelfTest
    {
        static void Log(string s) => Plugin.Log.LogInfo("underworld self-test: " + s);

        public static IEnumerator Run(Func<Vector3, IEnumerator> generate)
        {
            var under = Britannia.Under;
            if (under == null) { Log("FAIL no Underworld shape"); yield break; }
            Log($"Underworld origin x {under.OriginX:0}, reach {Continents.Reach(under):0} m (Valheim's edge {Continents.ValheimEdge:0})");

            // the places down there, made as a player arriving would make them
            var places = new (string name, int x, int y)[]
            {
                ("whirlpool arrival", UnderworldLinks.WhirlpoolX, UnderworldLinks.WhirlpoolY),
                ("falls landing", UnderworldLinks.FallsX, UnderworldLinks.FallsY),
                ("Doom", 128, 128),
                ("Ararat", 49, 58),
                ("Destard from below", 72, 168),
                ("the Amulet", UnderworldLinks.AmuletX, UnderworldLinks.AmuletY),
                ("the far east edge", 250, 128),
            };
            foreach (var (name, x, y) in places)
            {
                var at = Underworld.Ground(x, y, true);
                yield return generate(at);
                if (!ZoneSystem.instance.IsZoneLoaded(at)) yield return generate(at);
                float ground = ZoneSystem.instance.GetGroundHeight(at);
                Log($"{name} ({x},{y}) at {at}: shape {Britannia.SeaLevel + under.Height(at.x, at.z):0.0}, ground {ground:0.0}, biome {under.Biome(at.x, at.z)}, " +
                    $"zone loaded {ZoneSystem.instance.IsZoneLoaded(at)}, {Utils.LengthXZ(at):0} m from the centre" +
                    (Mathf.Abs(ground - (Britannia.SeaLevel + under.Height(at.x, at.z))) < 1.5f ? "" : " - FAIL the ground is not the Underworld's"));
            }

            // the sea past Valheim's edge stays at sea level: an Underworld lake more than 10.5 km out
            var far = FarLake();
            if (far != null)
            {
                yield return generate(far.Value);
                var water = UnityEngine.Object.FindObjectsByType<WaterVolume>(FindObjectsSortMode.None).OrderBy(w => (w.transform.position - far.Value).sqrMagnitude).FirstOrDefault();
                if (water != null)
                {
                    var p = new Vector3(far.Value.x, 30f, far.Value.z);
                    float surface = water.GetWaterSurface(p);
                    Log($"water surface at {p} ({Utils.LengthXZ(p):0} m out, volume at {water.transform.position}): {surface:0.0}, ground {ZoneSystem.instance.GetGroundHeight(p):0.0}" + (surface > 25f ? "" : " - FAIL dropped at the world's edge"));
                }
                else Log("FAIL no water volume by the far lake");
            }
            else Log("no lake past 10.5 km");

            // the entrances
            var doom = DungeonEntrance.Get(UnderworldLinks.Doom);
            Log(doom == null ? "FAIL Doom's entrance not made" : $"Doom's entrance at {doom.transform.position} (Underworld {Britannia.InUnderworld(doom.transform.position)}), open {doom.Open}");
            var below = DungeonEntrance.All.Where(e => e != null && e.Valid && e.Below).ToList();
            Log($"ways in from below: {string.Join(", ", below.Select(e => $"{Dungeons.Name(e.D)} at {e.transform.position}"))}" + (below.Any(e => e.D == 2) ? "" : " - FAIL none to Destard"));
            var exit = Dungeons.UnderworldExit?.Invoke(2);
            Log(exit == null ? "FAIL no way down from Destard" : $"Destard's bottom leads to {exit.Value} (Underworld {Britannia.InUnderworld(exit.Value)})");

            // Ararat
            var ararat = Settlements.Get(25);
            if (ararat == null) Log("FAIL Ararat not planned (BuildSettlements has no 25?)");
            else
            {
                int pieces = UnityEngine.Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None)
                    .Count(v => v != null && v.GetZDO() != null && Vector3.Distance(v.transform.position, ararat.Origin) < ararat.Site.Half + 4f);
                Log($"Ararat at {ararat.Origin} (Underworld {Britannia.InUnderworld(ararat.Origin)}): {ararat.Plan.Pieces.Count} pieces planned, {pieces} objects there");
            }

            // what lies there
            var spots = UnityEngine.Object.FindObjectsByType<LootSpot>(FindObjectsSortMode.None).Where(s => Britannia.InUnderworld(s.transform.position)).ToList();
            Log($"{spots.Count} loot spots in the Underworld: {string.Join(", ", spots.Take(8).Select(s => $"{s.GetHoverName()} at {s.transform.position:F0}"))}");

            // its environment
            if (EnvMan.instance != null)
            {
                var env = Underworld.Environment(EnvMan.instance);
                Log(env == null ? "FAIL no environment" : $"environment {env.m_name}, always dark {env.m_alwaysDark}");
            }

            // its encounters
            var rnd = new System.Random(5);
            var meet = Underworld.Ground(24, 85, true); // the grass and swamp of the west
            yield return generate(meet);
            for (int i = 0; i < 10; i++)
            {
                var said = UltimaSpawner.Encounter(meet, "the self-test", rnd);
                if (said != null) Log("encounter: " + said);
            }
            foreach (var c in Character.GetAllCharacters().Where(c => c.GetComponent<WildEncounter>() != null).ToList()) c.SetHealth(0);

            // a whirlpool on Britannia's deep sea drifts
            var sea = FindDeepSea();
            if (sea == null) { Log("FAIL no deep sea found"); yield break; }
            yield return generate(sea.Value);
            var pool = Whirlpool.Spawn(sea.Value + new Vector3(60, 0, 0));
            var p0 = pool != null ? pool.transform.position : Vector3.zero;
            yield return new WaitForSeconds(18f);
            Log(pool == null ? "FAIL no whirlpool" : $"a whirlpool drifted {Vector3.Distance(p0, pool.transform.position):0} m in 18 s");

            // a ship dragged down it springs up in the Underworld
            var karve = ZNetScene.instance.GetPrefab("Karve");
            if (karve == null) { Log("FAIL no Karve"); yield break; }
            var ship = UnityEngine.Object.Instantiate(karve, sea.Value + Vector3.up * 0.5f, Quaternion.identity).GetComponent<Ship>();
            yield return new WaitForSeconds(3f);
            Log($"a karve on the sea at {ship.transform.position}");
            Underworld.Carry(null, ship, ship.transform.position + new Vector3(4, 0, 0), Underworld.Ground(UnderworldLinks.WhirlpoolX, UnderworldLinks.WhirlpoolY, true), Underworld.Way.Whirlpool);
            yield return new WaitForSeconds(30f);
            if (ship == null) { Log("FAIL the karve is gone"); yield break; }
            var sp = ship.transform.position;
            var (tx, ty, tz) = Britannia.Tile(sp);
            Log($"the karve is at {sp} tile ({tx},{ty}) {(tz == 0 ? "Britannia" : "Underworld")}, y {sp.y:0.0}, upright {Vector3.Dot(ship.transform.up, Vector3.up):0.00}, " +
                $"health {ship.GetComponent<WearNTear>()?.GetHealthPercentage():0.00}" + (tz != 0 && Mathf.Abs(sp.y - Britannia.SeaLevel) < 3f ? " - floating" : " - FAIL"));
            Log("done");
        }

        /// <summary>The middle of an Underworld lake (3×3 water tiles) more than 10.6 km from the world's centre.</summary>
        static Vector3? FarLake()
        {
            var u = Britannia.Under;
            var map = Plugin.U5.Underworld;
            for (int y = 0; y < 256; y++)
            for (int x = 160; x < 256; x++)
            {
                bool lake = true;
                for (int k = -1; k <= 1 && lake; k++) for (int j = -1; j <= 1 && lake; j++) lake = map[x + j, y + k] == 2 || map[x + j, y + k] == 1;
                if (!lake) continue;
                u.TileCentre(x, y, out float wx, out float wz);
                if (Mathf.Sqrt(wx * wx + wz * wz) > 10600f) return new Vector3(wx, Britannia.SeaLevel, wz);
            }
            return null;
        }

        /// <summary>A point of Britannia's deep sea (tile 1, deep enough) near the spawn.</summary>
        static Vector3? FindDeepSea()
        {
            var shape = Britannia.Active;
            var map = Plugin.U5.Britannia;
            for (int r = 0; r < 128; r++)
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                int x = 0x60 + dx, y = 0x60 + dy;
                bool deep = true;
                for (int k = -2; k <= 2 && deep; k++) for (int j = -2; j <= 2 && deep; j++) deep = map[x + j, y + k] == 1;
                if (!deep) continue;
                shape.TileCentre(x & 0xff, y & 0xff, out float wx, out float wz);
                return new Vector3(wx, Britannia.SeaLevel, wz);
            }
            return null;
        }
    }
}
