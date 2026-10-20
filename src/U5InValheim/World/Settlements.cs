using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using U5InValheim.WorldGen.Core;
using UnityEngine;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// Phase 2: the settlements built from their maps (SettlementPlanner). When a Britannia world
    /// activates, the configured settlements are planned, their squares flattened and painted
    /// (BritanniaShape sites) and their pieces bucketed by zone. When a zone is generated
    /// (ZoneSystem.PlaceLocations, Full on the owner or Ghost ahead of players) its pieces are
    /// spawned, so they are saved with the world exactly once.
    /// </summary>
    public static class Settlements
    {
        public class Built
        {
            public Data.Location Location;
            public SettlementPlan Plan;
            public SettlementSite Site;
            /// <summary>World position of the settlement's local origin (centre, ground level).</summary>
            public Vector3 Origin;
            /// <summary>Who of its townsfolk (moved by this peer) stands where.</summary>
            public readonly TownCrowd Crowd = new TownCrowd();
            TownWatch watch;
            /// <summary>The town's alarm (monsters in town).</summary>
            public TownWatch Watch => watch ?? (watch = new TownWatch(this));
        }

        public const string FixedKey = "u5_fixed";
        public const string ClimbKey = "u5_climb";
        public const string ClimbVerbKey = "u5_climb_verb";
        public const string ScaleKey = "u5_scale";
        public const string LockKey = "u5_lock";
        public const string LookKey = "u5_look";

        public static List<Built> All { get; private set; } = new List<Built>();
        static Dictionary<Vector2s, List<(Built settlement, Placement piece)>> byZone = new Dictionary<Vector2s, List<(Built, Placement)>>();
        /// <summary>Shell pieces of settlements built as combined structures, by (location, zone).</summary>
        static Dictionary<(int, Vector2s), List<Placement>> shells = new Dictionary<(int, Vector2s), List<Placement>>();
        static Type clearAreaType;
        static TownTiles tiles;
        /// <summary>town_tiles.json (once the settlements are planned).</summary>
        public static TownTiles Tiles => tiles;
        static PieceGeometry geo;

        public static void Install(Harmony harmony)
        {
            clearAreaType = AccessTools.Inner(typeof(ZoneSystem), "ClearArea");
            var place = AccessTools.Method(typeof(ZoneSystem), "PlaceLocations");
            if (place == null || clearAreaType == null)
            {
                Plugin.Log.LogError("Settlements not installed: ZoneSystem.PlaceLocations/ClearArea not found");
                return;
            }
            harmony.Patch(place, postfix: new HarmonyMethod(typeof(Settlements), nameof(PlaceLocations_Postfix)));
        }

        /// <summary>How far past a settlement's slopes the wild's trees and rocks are kept away (m).</summary>
        const float ClearMargin = 20f;

        /// <summary>Room left between two settlement squares for their slopes.</summary>
        const float SiteGap = 16f;

        /// <summary>Plans the settlements of the world being activated and gives the shape their sites.</summary>
        public static void Prepare(BritanniaShape shape, List<Poi> pois)
        {
            All = new List<Built>();
            byZone = new Dictionary<Vector2s, List<(Built, Placement)>>();
            shells = new Dictionary<(int, Vector2s), List<Placement>>();
            bool combineAll = Plugin.CombinedShells.Value.Trim() == "*";
            var combined = combineAll ? new HashSet<int>() : new HashSet<int>(Plugin.CombinedShells.Value.Split(',')
                .Select(t => t.Trim()).Where(t => t.Length > 0).Select(int.Parse));
            var wanted = new HashSet<int>(Plugin.BuildSettlements.Value.Split(',')
                .Select(t => t.Trim()).Where(t => t.Length > 0).Select(int.Parse));
            if (wanted.Count == 0) { shape.SetSites(new SettlementSite[0]); return; }

            string dir = Path.Combine(Plugin.PluginDirectory, "mappings");
            tiles = tiles ?? TownTiles.Parse(File.ReadAllText(Path.Combine(dir, "town_tiles.json")));
            geo = geo ?? PieceGeometry.Parse(File.ReadAllText(Path.Combine(dir, "piece_geometry.json")));
            var planner = new SettlementPlanner(Plugin.U5, tiles, geo);
            var sites = new List<SettlementSite>();
            // in order of location number (Britain and the castle before the villages crowding them),
            // moved apart so each square has room for its slope back to the land
            var chosen = pois.Where(p => p.Location > 0 && !p.Underworld && wanted.Contains(p.Location) && Plugin.U5.Location(p.Location).IsSettlement)
                .OrderBy(p => p.Location).ToList();
            var squares = chosen.Select(p =>
            {
                var l = Plugin.U5.Location(p.Location);
                shape.TileCentre(l.X, l.Y, out float x, out float z);
                return (x, z, half: new SettlementPlan().HalfSize);
            }).ToList();
            SiteLayout.Separate(squares, SiteGap, out var centres);
            for (int k = 0; k < chosen.Count; k++)
            {
                var poi = chosen[k];
                var loc = Plugin.U5.Location(poi.Location);
                var (cx, cz) = centres[k];
                if (cx != squares[k].x || cz != squares[k].z)
                    Plugin.Log.LogInfo($"{loc.Name} moved ({cx - squares[k].x:0}, {cz - squares[k].z:0}) m to make room");
                var plan = planner.Plan(loc, poi.Kind, shape.Biome(cx, cz));
                var site = new SettlementSite
                {
                    Location = loc.Number, CentreX = cx, CentreZ = cz, Half = plan.HalfSize, Tile = plan.Tile,
                    Base = SiteBase(shape, cx, cz, plan, tiles), Paint = plan.GroundPaint, Water = plan.Water,
                    Basement = plan.HasBasement ? plan.Basement : null, Depth = plan.BasementDepth,
                };
                sites.Add(site);
                var built = new Built { Location = loc, Plan = plan, Site = site, Origin = new Vector3(cx, Britannia.SeaLevel + site.Base, cz) };
                All.Add(built);
                foreach (var piece in plan.Pieces)
                {
                    var zone = ZoneSystem.GetZone(built.Origin + new Vector3(piece.X, 0, piece.Z));
                    if ((combineAll || combined.Contains(loc.Number)) && Structures.IsShell(piece))
                    {
                        if (!shells.TryGetValue((loc.Number, zone), out var shell))
                        {
                            shells[(loc.Number, zone)] = shell = new List<Placement>();
                            // the structure object itself is spawned with the zone's other pieces
                            if (!byZone.TryGetValue(zone, out var l)) byZone[zone] = l = new List<(Built, Placement)>();
                            l.Add((built, new Placement { Prefab = Structures.PrefabName, X = float.NaN }));
                        }
                        shell.Add(piece);
                        continue;
                    }
                    if (!byZone.TryGetValue(zone, out var list)) byZone[zone] = list = new List<(Built, Placement)>();
                    list.Add((built, piece));
                }
                Plugin.Log.LogInfo($"Planned {loc.Name}: {plan.Pieces.Count} pieces on ground {site.Base:0.0} m");
            }
            shape.SetSites(sites);
        }

        /// <summary>
        /// Settlement ground: the average natural height of its square; with water tiles, 1.5 m (or
        /// its water_ground), so they hold the sea-level water; with a basement, high enough to
        /// keep the sea out of it.
        /// </summary>
        static float SiteBase(BritanniaShape shape, float cx, float cz, SettlementPlan plan, TownTiles tiles)
        {
            double sum = 0;
            int n = 0;
            for (float dz = -plan.HalfSize; dz <= plan.HalfSize; dz += 8)
            for (float dx = -plan.HalfSize; dx <= plan.HalfSize; dx += 8)
            {
                sum += shape.NaturalHeight(cx + dx, cz + dz);
                n++;
            }
            float avg = (float)(sum / n);
            float ground = plan.HasWater ? (tiles.WaterGround.TryGetValue(plan.Location.Number, out float g) ? g : 1.5f) : Mathf.Clamp(avg, 1.5f, 60f);
            // a basement's floor stays a metre above the sea (below it, the sea would flood in)
            return plan.HasBasement ? Mathf.Max(ground, plan.BasementDepth + 1) : ground;
        }

        public static bool IsBuilt(int location) => All.Any(b => b.Location.Number == location);
        public static Built Get(int location) => All.FirstOrDefault(b => b.Location.Number == location);
        public static bool ShellPieces(int location, Vector2s zone, out List<Placement> pieces) => shells.TryGetValue((location, zone), out pieces);

        /// <summary>
        /// World spawn point: inside Iolo's hut, or (setting SpawnLocation) just outside another
        /// built settlement, on land, at the middle of the side of its square nearest a road.
        /// False when that place is not built.
        /// </summary>
        public static bool TrySpawnPoint(out Vector3 pos)
        {
            pos = default;
            int at = Plugin.SpawnLocation.Value;
            var site = All.FirstOrDefault(b => b.Location.Number == at);
            if (site == null) return false;
            if (site.Plan.HasSpawn)
            {
                pos = site.Origin + new Vector3(site.Plan.SpawnX, 0.3f, site.Plan.SpawnZ);
                return true;
            }
            var shape = Britannia.Active;
            float d = site.Site.Half + site.Site.Blend * 0.6f;
            var best = new[] { (0f, -d), (0f, d), (d, 0f), (-d, 0f) }
                .Select(o => new Vector3(site.Site.CentreX + o.Item1, 0, site.Site.CentreZ + o.Item2))
                .Where(p => shape.Height(p.x, p.z) > 1f)
                .OrderBy(p => shape.PathDistance(p.x, p.z))
                .DefaultIfEmpty(new Vector3(site.Site.CentreX, 0, site.Site.CentreZ - d)).First();
            pos = new Vector3(best.x, Britannia.SeaLevel + Mathf.Max(shape.Height(best.x, best.z), 0f) + 0.3f, best.z);
            return true;
        }

        // ── spawning ──────────────────────────────────────────────────────────────────────────

        static void PlaceLocations_Postfix(Vector2s zoneID, IList clearAreas, ZoneSystem.SpawnMode mode, List<GameObject> spawnedObjects)
        {
            if (Britannia.Active == null || (mode != ZoneSystem.SpawnMode.Full && mode != ZoneSystem.SpawnMode.Ghost)) return;

            // keep the wild vegetation out of every built settlement touching this zone: its square, its
            // slopes and a margin, so no big tree or boulder standing outside reaches in (a beech's
            // crown or a boulder is up to ~17 m across from where it stands)
            var zonePos = ZoneSystem.GetZonePos(zoneID);
            foreach (var b in All)
            {
                float clear = b.Site.Half + b.Site.Blend + ClearMargin;
                if (Mathf.Abs(zonePos.x - b.Origin.x) < clear + 32f && Mathf.Abs(zonePos.z - b.Origin.z) < clear + 32f)
                    clearAreas.Add(Activator.CreateInstance(clearAreaType, b.Origin, clear));
            }

            // the people live in the zone of their settlement's middle
            foreach (var b in All)
                if (ZoneSystem.GetZone(b.Origin) == zoneID) TownsfolkSpawner.Spawn(b, mode, spawnedObjects);

            if (!byZone.TryGetValue(zoneID, out var pieces)) return;
            int spawned = 0, missing = 0;
            foreach (var (b, p) in pieces)
            {
                var prefab = ZNetScene.instance.GetPrefab(p.Prefab);
                if (prefab == null) { missing++; continue; }
                bool structure = p.Prefab == Structures.PrefabName;
                // a structure sits at its zone's centre (its ZDO must belong to the zone); its
                // children are placed at their own world positions when it is rebuilt
                var pos = structure ? new Vector3(zonePos.x, b.Origin.y, zonePos.z) : b.Origin + new Vector3(p.X, p.Y, p.Z);
                if (mode == ZoneSystem.SpawnMode.Ghost) ZNetView.StartGhostInit();
                var rot = Quaternion.Euler(0, p.Yaw, 0);
                if (p.Turned) rot *= Quaternion.Euler(p.Tilt, 0, 0) * Quaternion.Euler(0, p.Spin, 0) * Quaternion.Euler(0, 0, p.Roll);
                var go = UnityEngine.Object.Instantiate(prefab, pos, rot);
                var scale = p.Scaled ? new Vector3(p.SX, p.SY, p.SZ) : Vector3.zero;
                if (p.Scaled) go.transform.localScale = scale;
                var zdo = go.GetComponent<ZNetView>()?.GetZDO();
                if (zdo != null && structure)
                {
                    zdo.Set(Structures.LocationKey, b.Location.Number);
                    zdo.Set(Structures.ZoneXKey, (int)zoneID.x);
                    zdo.Set(Structures.ZoneYKey, (int)zoneID.y);
                }
                else if (zdo != null)
                {
                    zdo.Set(FixedKey, true);
                    if (p.Scaled) zdo.Set(ScaleKey, scale);
                    if (p.Lock != null) zdo.Set(LockKey, p.Lock);
                    if (p.Look != null) { zdo.Set(LookKey, p.Look); Looks.Apply(go, p.Look); }
                    if (p.StandItem != null) zdo.Set(ZDOVars.s_item, p.StandItem.GetStableHashCode());
                    if (p.Secret != 0) zdo.Set(TownDoors.SecretKey, p.Secret);
                    if (p.Hidden != 0) zdo.Set(TownDoors.HiddenKey, p.Hidden);
                    if (p.Text != null) zdo.Set(ZDOVars.s_text, p.Text);
                    if (p.Items != null) zdo.Set(ZDOVars.s_items, ContainerBytes(p.Items));
                    if (p.Climb)
                    {
                        zdo.Set(ClimbKey, b.Origin + new Vector3(p.ClimbX, p.ClimbY, p.ClimbZ));
                        zdo.Set(ClimbVerbKey, p.ClimbVerb);
                    }
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
            Plugin.Log.LogInfo($"Zone {zoneID}: spawned {spawned} settlement pieces{(missing > 0 ? $", {missing} with unknown prefabs" : "")}");
        }

        /// <summary>A container's saved inventory holding one of each item.</summary>
        static byte[] ContainerBytes(string[] items)
        {
            var inv = new Inventory("u5", null, 6, 4);
            foreach (var name in items)
            {
                var item = ObjectDB.instance?.GetItemPrefab(name);
                if (item != null) inv.AddItem(item, 1);
            }
            var pkg = new ZPackage();
            inv.Save(pkg);
            return pkg.GetArray();
        }
    }
}
