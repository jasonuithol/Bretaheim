using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using Bretaheim.Data;
using Bretaheim.WorldGen.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Bretaheim.WorldGen
{
    /// <summary>
    /// The eight dungeons (MASTER_PLAN §5.7, Phase 6). Each entrance on the overworld is a stone
    /// door in the mountainside (u5_dungeon_entrance, made with its zone), sealed until its word
    /// of power is said in chat within a tile (32 m) of it (CMDS yell_word_of_power; all eight are
    /// sealed in INIT.GAM). Inside, the dungeon is built where Valheim builds its own interiors:
    /// high above the entrance (level 0 at y = 5000, each level 8 m below the last), from
    /// DUNGEON.DAT and DUNGEON.CBT (<see cref="DungeonPlanner"/>), by every peer near it
    /// (<see cref="DungeonRunner"/>). The dungeon's changing state (sealed, cells, rooms cleared,
    /// room tiles changed, doors open) lives on the entrance's ZDO.
    /// </summary>
    public static class Dungeons
    {
        public const string EntrancePrefab = "u5_dungeon_entrance";
        public const string DungeonKey = "u5_dng", OpenKey = "u5_dng_open", CellsKey = "u5_dng_cells", RoomsKey = "u5_dng_rooms",
                            TilesKey = "u5_dng_tiles", UsedKey = "u5_dng_used", DoorsKey = "u5_dng_doors", RevKey = "u5_dng_rev",
                            InitKey = "u5_dng_init", RoomTag = "u5_dng_room", WanderTag = "u5_dng_wander";
        public const float BaseY = 5000f;
        /// <summary>How near the entrance a word of power must be said (one Ultima tile).</summary>
        public const float YellRange = 32f;
        // DATA.OVL (DGROUP) strings
        public const int SUttered = 0x44d7, SNoEffect = 0x44f4, SUp = 0x6c74, SDown = 0x6c6c, SExitTo = 0x6c84, SBritannia = 0x6c9c,
                         SUnderworld = 0x6c8e, SWithWhat = 0x6cd6, SEnteringRoom = 0x2c58;

        static DungeonPlanner planner;
        static readonly System.Random random = new System.Random();
        static Type clearAreaType;

        /// <summary>
        /// Where a dungeon's bottom leads (Phase 7, the Underworld): given the dungeon, the place to
        /// arrive. Until the Underworld is built it is null and the way down says so.
        /// </summary>
        public static Func<int, Vector3?> UnderworldExit;

        static DataOvl Ovl => Plugin.U5.Ovl;

        public static DungeonPlanner Planner
        {
            get
            {
                if (planner != null) return planner;
                string dir = Path.Combine(Plugin.PluginDirectory, "mappings");
                var tiles = Settlements.Tiles ?? TownTiles.Parse(File.ReadAllText(Path.Combine(dir, "town_tiles.json")));
                var geo = PieceGeometry.Parse(File.ReadAllText(Path.Combine(dir, "piece_geometry.json")));
                return planner = new DungeonPlanner(Plugin.U5, tiles, geo);
            }
        }

        public static string Name(int d) => Plugin.U5.Location(DungeonInfo.Location(d)).Name;

        public static void Register() => PrefabManager.OnVanillaPrefabsAvailable += Create;

        static void Create()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Create;
            var holder = new GameObject("Bretaheim_Dungeons");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            var prefab = new GameObject(EntrancePrefab);
            prefab.transform.SetParent(holder.transform, false);
            prefab.layer = LayerMask.NameToLayer("piece");
            var nview = prefab.AddComponent<ZNetView>();
            nview.m_persistent = true;
            var box = prefab.AddComponent<BoxCollider>();
            box.size = new Vector3(5f, 4f, 1f);
            box.center = new Vector3(0, 2f, 0.4f);
            prefab.AddComponent<DungeonEntrance>();
            PrefabManager.Instance.AddPrefab(new CustomPrefab(prefab, false));
        }

        public static void Install(Harmony harmony)
        {
            // the spells that know dungeons: where they may be cast, Uus Por and Des Por (a level up or down, as a ladder does)
            SpellEffects.InDungeon = p => Inside(p, out _, out _, out _, out _);
            Endgame.DoomMirror = DoomMirror;
            SpellEffects.DungeonLadder = (player, dir) =>
            {
                if (!Inside(player.transform.position, out int d, out int level, out double u, out double v)) return false;
                Climb(player, d, level, (int)u, (int)v, dir, grapple: false);
                return true;
            };
            clearAreaType = AccessTools.Inner(typeof(ZoneSystem), "ClearArea");
            var place = AccessTools.Method(typeof(ZoneSystem), "PlaceLocations");
            if (place != null && clearAreaType != null) harmony.Patch(place, postfix: new HarmonyMethod(typeof(Dungeons), nameof(PlaceLocations_Postfix)));
            else Plugin.Log.LogError("Dungeons: ZoneSystem.PlaceLocations/ClearArea not found; no entrances");
            var send = AccessTools.Method(typeof(Chat), nameof(Chat.SendText));
            if (send != null) harmony.Patch(send, prefix: new HarmonyMethod(typeof(Dungeons), nameof(SendText_Prefix)));
            else Plugin.Log.LogWarning("Dungeons: Chat.SendText not found; words of power are not heard");
        }

        // ── where things are ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// World x, z of the middle of dungeon d (the middle of its entrance tile: on the surface,
        /// or for Doom in the Underworld), or null outside a Britannia world.
        /// </summary>
        /// <summary>The mirror Lord British is behind (ENDGAME): tile (5,1) of Doom's room 15 (DUNGEON.CBT 111), wherever Doom's map has that room.</summary>
        static Vector3? doomMirror;
        static bool doomMirrorFound;

        static Vector3? DoomMirror()
        {
            if (Britannia.Active == null || Plugin.U5 == null || Centre(UnderworldLinks.Doom) == null) return null;
            if (doomMirrorFound) return doomMirror;
            doomMirrorFound = true;
            const int room = 15, mirrorX = 5, mirrorY = 1;
            for (int level = 0; level < DungeonInfo.Levels; level++)
            for (int cell = 0; cell < 64; cell++)
            {
                var c = Plugin.U5.Dungeons.Cell(UnderworldLinks.Doom, level, cell % 8, cell / 8);
                int hi = c.Raw & 0xf0;
                if ((hi != 0xf0 && hi != 0xa0) || c.Room != room) continue;
                doomMirror = World(UnderworldLinks.Doom, level, (cell % 8) * DungeonLevelPlan.CellTiles + mirrorX, (cell / 8) * DungeonLevelPlan.CellTiles + mirrorY);
                Plugin.Log.LogInfo($"Doom's mirror: level {level}, cell {cell}, at {doomMirror}");
                return doomMirror;
            }
            Plugin.Log.LogWarning("Doom's mirror: no room 15 in Doom's map");
            return null;
        }

        public static Vector2? Centre(int d)
        {
            if (Britannia.Active == null || d < 0 || d >= DungeonInfo.Count) return null;
            var l = Plugin.U5.Location(DungeonInfo.Location(d));
            var shape = l.InUnderworld ? Britannia.Under : Britannia.Active;
            if (shape == null) return null;
            shape.TileCentre(l.X, l.Y, out float x, out float z);
            return new Vector2(x, z);
        }

        public static float LevelY(int level) => BaseY - level * DungeonLevelPlan.Spacing;

        /// <summary>World position of grid tile (u, v) of a dungeon level, <paramref name="up"/> metres above its floor.</summary>
        public static Vector3 World(int d, int level, double u, double v, float up = 0)
        {
            var c = Centre(d) ?? Vector2.zero;
            var (x, z) = DungeonLevelPlan.Local(u, v);
            return new Vector3(c.x + x, LevelY(level) + up, c.y + z);
        }

        /// <summary>Whether a position is inside a dungeon (its margin included), and where.</summary>
        public static bool Inside(Vector3 p, out int d, out int level, out double u, out double v)
        {
            d = level = -1;
            u = v = 0;
            if (p.y < LevelY(DungeonInfo.Levels - 1) - DungeonLevelPlan.Slab - 2 || p.y > BaseY + DungeonLevelPlan.Height + 2) return false;
            float half = (DungeonLevelPlan.Tiles / 2f + DungeonLevelPlan.CellTiles + 2) * DungeonLevelPlan.Tile;
            for (int i = 0; i < DungeonInfo.Count; i++)
            {
                var c = Centre(i);
                if (c == null || Mathf.Abs(p.x - c.Value.x) > half || Mathf.Abs(p.z - c.Value.y) > half) continue;
                d = i;
                level = Mathf.Clamp(Mathf.CeilToInt((BaseY - p.y - 0.01f) / DungeonLevelPlan.Spacing), 0, DungeonInfo.Levels - 1);
                (u, v) = DungeonLevelPlan.TileAtLocal(p.x - c.Value.x, p.z - c.Value.y);
                return true;
            }
            return false;
        }

        /// <summary>The open floor tile nearest (u, v) in its cell (not a hole, a ladder, a door or a wall), or (u, v).</summary>
        public static (int u, int v) Stand(DungeonLevelPlan plan, int u, int v, bool besides = false)
        {
            int cell = DungeonLevelPlan.CellOf(u, v, out _, out _);
            (int, int) best = (u, v);
            int bestD = int.MaxValue;
            for (int dv = -10; dv <= 10; dv++)
            for (int du = -10; du <= 10; du++)
            {
                int a = u + du, b = v + dv;
                if (DungeonLevelPlan.CellOf(a, b, out _, out _) != cell) continue;
                if (besides && du == 0 && dv == 0) continue;
                var c = plan.ClassAt(a, b);
                if (c == DTile.Solid || c == DTile.Door || c == DTile.Ladder || c == DTile.Lava || plan.FloorHole(a, b)) continue;
                int dist = du * du + dv * dv;
                if (dist < bestD) { bestD = dist; best = (a, b); }
            }
            return best;
        }

        // ── the entrances ────────────────────────────────────────────────────────────────────

        static void PlaceLocations_Postfix(Vector2s zoneID, IList clearAreas, ZoneSystem.SpawnMode mode, List<GameObject> spawnedObjects)
        {
            if (Britannia.Active == null || (mode != ZoneSystem.SpawnMode.Full && mode != ZoneSystem.SpawnMode.Ghost)) return;
            for (int d = 0; d < DungeonInfo.Count; d++)
            {
                var c = Centre(d);
                if (c == null) continue;
                var shape = Plugin.U5.Location(DungeonInfo.Location(d)).InUnderworld ? Britannia.Under : Britannia.Active;
                PlaceEntrance(zoneID, clearAreas, mode, spawnedObjects, d, shape, c.Value, below: false);
            }
        }

        /// <summary>
        /// Dungeon d's entrance at (x, z) of a continent, if that is in the zone being made: its
        /// door facing down the slope. <paramref name="below"/>: the way in from the Underworld to
        /// one of the surface dungeons (Underworld.EnterFromBelow; always open); the dungeon's state
        /// stays on its surface entrance.
        /// </summary>
        public static void PlaceEntrance(Vector2s zoneID, IList clearAreas, ZoneSystem.SpawnMode mode, List<GameObject> spawnedObjects,
                                         int d, BritanniaShape shape, Vector2 c, bool below)
        {
            var pos = new Vector3(c.x, Britannia.SeaLevel + shape.Height(c.x, c.y), c.y);
            if (ZoneSystem.GetZone(pos) != zoneID) return;
            clearAreas.Add(Activator.CreateInstance(clearAreaType, pos, 14f));
            // the door faces down the slope, its back to the mountain
            float bestYaw = 0, lowest = float.MaxValue;
            for (int k = 0; k < 8; k++)
            {
                float yaw = k * 45f, r = yaw * Mathf.Deg2Rad;
                float h = shape.Height(pos.x + Mathf.Sin(r) * 12f, pos.z + Mathf.Cos(r) * 12f);
                if (h < lowest) { lowest = h; bestYaw = yaw; }
            }
            var prefab = ZNetScene.instance?.GetPrefab(EntrancePrefab);
            if (prefab == null) { Plugin.Log.LogError("Dungeons: no entrance prefab"); return; }
            if (mode == ZoneSystem.SpawnMode.Ghost) ZNetView.StartGhostInit();
            var go = Object.Instantiate(prefab, pos, Quaternion.Euler(0, bestYaw, 0));
            var zdo = go.GetComponent<ZNetView>()?.GetZDO();
            if (zdo != null)
            {
                zdo.Set(DungeonKey, d + 1);
                if (below) zdo.Set(Underworld.BelowKey, true);
                else zdo.Set(OpenKey, Plugin.U5.InitialGame.DungeonOpen(d));
            }
            if (mode == ZoneSystem.SpawnMode.Ghost)
            {
                spawnedObjects.Add(go);
                ZNetView.FinishGhostInit();
            }
            Plugin.Log.LogInfo($"Dungeon entrance of {Name(d)}{(below ? " (from the Underworld)" : "")} placed at {pos} facing {bestYaw}°");
        }

        // ── words of power ───────────────────────────────────────────────────────────────────

        static void SendText_Prefix(Talker.Type type, string text)
        {
            try { Yell(Player.m_localPlayer, text); }
            catch (Exception e) { Plugin.Log.LogError($"Dungeons: yell: {e}"); }
        }

        /// <summary>
        /// yell_word_of_power: a word of power anywhere in what was said is "uttered" (the earth
        /// shakes); said within a tile of its own dungeon's entrance it opens it (or seals it again);
        /// elsewhere it has no effect.
        /// </summary>
        public static void Yell(Player player, string text)
        {
            if (player == null || Britannia.Active == null || Plugin.U5 == null) return;
            int d = DungeonInfo.WordIn(Ovl, text);
            if (d < 0) return;
            string said = Ovl.CStr(SUttered).Trim();
            GameCamera.instance?.AddShake(player.transform.position, 30f, 2f, false);
            var at = player.transform.position;
            var near = player.InInterior() ? null : DungeonEntrance.All
                .Where(e => e != null && e.Valid && Horizontal(e.transform.position, at) <= YellRange)
                .OrderBy(e => Horizontal(e.transform.position, at)).FirstOrDefault();
            if (near == null)
            {
                player.Message(MessageHud.MessageType.Center, said + "\n" + Ovl.CStr(SNoEffect).Trim());
                return;
            }
            player.Message(MessageHud.MessageType.Center, said);
            if (near.D != d) return;
            near.SetOpen(!near.Open);
            Plugin.Log.LogInfo($"{player.GetPlayerName()} said {Ovl.WordsOfPower[d]}: {Name(d)} is {(near.Open ? "open" : "sealed")}");
        }

        static float Horizontal(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        // ── going in and out ─────────────────────────────────────────────────────────────────

        /// <summary>enter_dungeon: from the surface to level 0, cell (1, 1), facing east.</summary>
        public static void Enter(Player player, DungeonEntrance e)
        {
            int d = e.D;
            var plan = DungeonRunner.EnsureBuilt(d, DungeonInfo.StartLevel);
            if (plan == null) return;
            e.Prepare();
            var (cu, cv) = DungeonLevelPlan.CellCentre(DungeonInfo.StartY * 8 + DungeonInfo.StartX);
            var (u, v) = Stand(plan, cu, cv, besides: true);
            var to = World(d, DungeonInfo.StartLevel, u, v, 0.3f);
            // (Valheim refuses a teleport within 2 s of the last one)
            if (!player.TeleportTo(to, Quaternion.Euler(0, 90, 0), false)) return;
            player.Message(MessageHud.MessageType.Center, Name(d));
            Plugin.Log.LogInfo($"{player.GetPlayerName()} enters {Name(d)} at {to}");
        }

        /// <summary>exit_dungeon: back out at the entrance.</summary>
        public static void Exit(Player player, int d)
        {
            var e = DungeonEntrance.Get(d);
            Vector3 to;
            Quaternion rot;
            if (e != null) { to = e.Front; rot = e.transform.rotation; }
            else
            {
                var c = Centre(d) ?? Vector2.zero;
                to = new Vector3(c.x, Britannia.SeaLevel + Britannia.ShapeAt(c.x, c.y).Height(c.x, c.y) + 0.5f, c.y);
                rot = player.transform.rotation;
            }
            player.TeleportTo(to, rot, false);
            // (Doom's way up leads to its entrance in the Underworld)
            bool under = Plugin.U5.Location(DungeonInfo.Location(d)).InUnderworld;
            player.Message(MessageHud.MessageType.Center, Ovl.CStr(SExitTo).Trim() + " " + Ovl.CStr(under ? SUnderworld : SBritannia).Trim());
        }

        /// <summary>
        /// Klimb in a dungeon (cmd_klimb_dungeon): a ladder or grate (or, with a grapple, a hole in the
        /// ceiling) to the level above or below, standing where the original puts the party (the same
        /// place on the other level); up from level 0 leaves the dungeon, down from level 7 leads to the
        /// Underworld.
        /// </summary>
        public static void Climb(Player player, int d, int level, int u, int v, int dir, bool grapple)
        {
            if (grapple && !HasGrapple(player))
            {
                player.Message(MessageHud.MessageType.Center, Ovl.CStr(SWithWhat).Replace("Klimb-", "").Trim());
                return;
            }
            player.Message(MessageHud.MessageType.Center, Ovl.CStr(dir < 0 ? SUp : SDown).Trim());
            if (dir < 0 && level == 0) { Exit(player, d); return; }
            if (dir > 0 && level == DungeonInfo.Levels - 1)
            {
                var below = UnderworldExit?.Invoke(d);
                if (below == null) { player.Message(MessageHud.MessageType.Center, Ovl.CStr(SExitTo).Trim() + " " + Ovl.CStr(SUnderworld).Trim() + "\n(not yet)"); return; }
                player.TeleportTo(below.Value, player.transform.rotation, true);
                return;
            }
            int to = level + dir;
            var plan = DungeonRunner.EnsureBuilt(d, to);
            if (plan == null) return;
            var (su, sv) = Stand(plan, u, v, besides: true);
            Move(player, World(d, to, su, sv, 0.2f));
        }

        public static void Move(Player player, Vector3 to)
        {
            var body = player.GetComponent<Rigidbody>();
            player.transform.position = to;
            if (body != null)
            {
                body.position = to;
                body.linearVelocity = Vector3.zero;
            }
        }

        static bool HasGrapple(Player player)
        {
            var inv = player.GetInventory();
            if (inv == null) return false;
            foreach (var (prefab, _) in UltimaItems.Map.Prefabs(new Loot(LootKind.Grapple)))
                if (inv.GetAllItems().Any(i => i.m_dropPrefab != null && i.m_dropPrefab.name == prefab)) return true;
            return false;
        }

        /// <summary>damage_whole_party for one player: 1..8 (on the Ultima scale), poison too if asked.</summary>
        public static void Hurt(Player p, int damage, bool poison = false, bool fire = false)
        {
            var hit = new HitData { m_point = p.GetCenterPoint(), m_dir = Vector3.down };
            if (fire) hit.m_damage.m_fire = damage * Ultima.Scale;
            else hit.m_damage.m_damage = damage * Ultima.Scale;
            if (poison) hit.m_damage.m_poison = Ultima.Scale;
            p.Damage(hit);
        }

        public static int Roll8() => random.Next(1, 9);
    }

    /// <summary>A dungeon's entrance on the overworld; its ZDO holds the dungeon's state.</summary>
    public class DungeonEntrance : MonoBehaviour, Hoverable, Interactable
    {
        static readonly List<DungeonEntrance> all = new List<DungeonEntrance>();
        public static IEnumerable<DungeonEntrance> All => all;
        /// <summary>Dungeon d's entrance that holds its state (not a way in from the Underworld), when loaded.</summary>
        public static DungeonEntrance Get(int d) => all.FirstOrDefault(e => e != null && e.D == d && e.Valid && !e.Below);

        ZNetView nview;
        GameObject slab;
        bool built;
        float nextLook;
        public int D { get; private set; } = -1;
        public bool Valid => nview != null && nview.IsValid();
        ZDO Zdo => nview.GetZDO();

        void Awake() => nview = GetComponent<ZNetView>();

        // Start, not Awake: a freshly spawned entrance gets its keys after Awake.
        void Start()
        {
            if (!Valid) return;
            D = Zdo.GetInt(Dungeons.DungeonKey) - 1;
            if (D < 0) return;
            all.Add(this);
            Build();
        }

        void OnDestroy() => all.Remove(this);

        void Update()
        {
            if (!built || Time.time < nextLook) return;
            nextLook = Time.time + 0.5f;
            if (slab != null && Valid) slab.SetActive(!Open);
        }

        /// <summary>The way in: two pillars and a lintel round a pair of doors set into a block of stone (iron grates for the crypts).</summary>
        void Build()
        {
            built = true;
            var style = DungeonInfo.Style(D);
            void Part(string prefab, Vector3 at, Vector3 scale, float yaw = 0)
            {
                var t = Structures.Template(prefab);
                if (t == null) return;
                var go = Instantiate(t, transform);
                go.transform.localPosition = at + Vector3.down * 0.3f;
                go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
                go.transform.localScale = scale;
                go.SetActive(true);
            }
            Part("stone_wall_4x2", new Vector3(0, 2.5f, -2.3f), new Vector3(1.7f, 2.6f, 3.6f));
            Part("stone_pillar", new Vector3(-2.6f, 2f, 0.2f), new Vector3(1.2f, 2f, 1.2f));
            Part("stone_pillar", new Vector3(2.6f, 2f, 0.2f), new Vector3(1.2f, 2f, 1.2f));
            Part("stone_wall_4x2", new Vector3(0, 4.5f, 0.2f), new Vector3(1.6f, 0.5f, 1.2f));
            if (style == DungeonStyle.Crypt)
            {
                Part("iron_grate", new Vector3(-1, 1.33f, 0), new Vector3(1, 1.33f, 1));
                Part("iron_grate", new Vector3(1, 1.33f, 0), new Vector3(1, 1.33f, 1));
            }
            else
            {
                Part("darkwood_gate", new Vector3(-1, 0, 0), Vector3.one);
                Part("darkwood_gate", new Vector3(1, 0, 0), Vector3.one, 180);
            }
            var slabT = Structures.Template("stone_wall_4x2");
            if (slabT != null)
            {
                slab = Instantiate(slabT, transform);
                slab.transform.localPosition = new Vector3(0, 1.9f, 0.6f);
                slab.transform.localScale = new Vector3(1.15f, 2.1f, 0.4f);
                slab.SetActive(!Open);
            }
        }

        public bool Open => Valid && (Below || Zdo.GetBool(Dungeons.OpenKey));

        /// <summary>The way in from the Underworld to a surface dungeon (UNDER.DAT's entrance tile): never sealed, it leads to level 7.</summary>
        public bool Below => Valid && Zdo.GetBool(Underworld.BelowKey);

        public void SetOpen(bool open) => Write(z => z.Set(Dungeons.OpenKey, open), false);

        public Vector3 Front => transform.position + transform.forward * 3.5f + Vector3.up * 0.6f;

        // ── the dungeon's state ──────────────────────────────────────────────────────────────

        byte[] cells;
        int cellsRev = -1;

        /// <summary>Geometry revision: changes whenever cells or room tiles change (the levels are rebuilt).</summary>
        public int Revision => Valid ? Zdo.GetInt(Dungeons.RevKey) : 0;

        /// <summary>The dungeon's 512 cells as they are now (DUNGEON.DAT until something changes).</summary>
        public byte[] Cells
        {
            get
            {
                int rev = Revision;
                if (cells == null || rev != cellsRev)
                {
                    cells = (Valid ? Zdo.GetByteArray(Dungeons.CellsKey) : null) ?? Dungeons.Planner.InitialCells(D);
                    cellsRev = rev;
                }
                return cells;
            }
        }

        void Write(Action<ZDO> change, bool geometry)
        {
            if (!Valid) return;
            nview.ClaimOwnership();
            change(Zdo);
            if (geometry) Zdo.Set(Dungeons.RevKey, Zdo.GetInt(Dungeons.RevKey) + 1);
        }

        public bool IsOwner => Valid && nview.IsOwner();

        public void SetCell(int level, int cell, byte value)
        {
            var c = (byte[])Cells.Clone();
            if (c[level * 64 + cell] == value) return;
            c[level * 64 + cell] = value;
            Write(z => z.Set(Dungeons.CellsKey, c), true);
        }

        /// <summary>Several cells at once: (index into the 512, value).</summary>
        public void SetCells(IEnumerable<(int index, byte value)> changes)
        {
            var c = (byte[])Cells.Clone();
            foreach (var (i, value) in changes) c[i] = value;
            Write(z => z.Set(Dungeons.CellsKey, c), true);
        }

        /// <summary>Room 0..15: 0 waiting, 1 its monsters are out, 2 cleared, 3 cleared but fills again once everyone has left it.</summary>
        public byte RoomState(int room)
        {
            var r = Valid ? Zdo.GetByteArray(Dungeons.RoomsKey) : null;
            return r != null && r.Length == 16 ? r[room] : (byte)0;
        }

        public void SetRoomState(int room, byte state)
        {
            var r = (Valid ? Zdo.GetByteArray(Dungeons.RoomsKey) : null) ?? new byte[16];
            r = (byte[])r.Clone();
            r[room] = state;
            Write(z => z.Set(Dungeons.RoomsKey, r), false);
        }

        /// <summary>Room tiles changed by triggers: (level, cell, x, y) → tile.</summary>
        public Dictionary<(int, int, int, int), byte> Changes()
        {
            var r = new Dictionary<(int, int, int, int), byte>();
            var b = Valid ? Zdo.GetByteArray(Dungeons.TilesKey) : null;
            if (b != null) for (int i = 0; i + 4 < b.Length; i += 5) r[(b[i], b[i + 1], b[i + 2], b[i + 3])] = b[i + 4];
            return r;
        }

        public HashSet<(int, int, int)> UsedTriggers()
        {
            var r = new HashSet<(int, int, int)>();
            var b = Valid ? Zdo.GetByteArray(Dungeons.UsedKey) : null;
            if (b != null) for (int i = 0; i + 2 < b.Length; i += 3) r.Add((b[i], b[i + 1], b[i + 2]));
            return r;
        }

        /// <summary>check_room_trigger: the trigger is used up and its two tiles change.</summary>
        public void FireTrigger(int level, int cell, RoomTrigger t)
        {
            var used = UsedTriggers();
            if (!used.Add((level, cell, t.Index))) return;
            var changes = Changes();
            if (t.X1 < 11 && t.Y1 < 11) changes[(level, cell, t.X1, t.Y1)] = t.Tile;
            if (t.X2 < 11 && t.Y2 < 11) changes[(level, cell, t.X2, t.Y2)] = t.Tile;
            SaveTriggers(used, changes);
        }

        /// <summary>A room left by everyone is as the file has it again (the original reloads it from DUNGEON.CBT on every entry).</summary>
        public void ResetRoom(int level, int cell)
        {
            var used = UsedTriggers();
            var changes = Changes();
            int n = used.RemoveWhere(u => u.Item1 == level && u.Item2 == cell);
            foreach (var k in changes.Keys.Where(k => k.Item1 == level && k.Item2 == cell).ToList()) { changes.Remove(k); n++; }
            if (n > 0) SaveTriggers(used, changes);
        }

        public IEnumerable<(int level, int cell)> ChangedRooms()
            => UsedTriggers().Select(u => (u.Item1, u.Item2)).Concat(Changes().Keys.Select(k => (k.Item1, k.Item2))).Distinct();

        void SaveTriggers(HashSet<(int, int, int)> used, Dictionary<(int, int, int, int), byte> changes)
        {
            var u = used.SelectMany(x => new[] { (byte)x.Item1, (byte)x.Item2, (byte)x.Item3 }).ToArray();
            var c = changes.SelectMany(kv => new[] { (byte)kv.Key.Item1, (byte)kv.Key.Item2, (byte)kv.Key.Item3, (byte)kv.Key.Item4, kv.Value }).ToArray();
            Write(z => { z.Set(Dungeons.UsedKey, u); z.Set(Dungeons.TilesKey, c); }, true);
        }

        public HashSet<(int, int, int)> OpenDoors()
        {
            var r = new HashSet<(int, int, int)>();
            var b = Valid ? Zdo.GetByteArray(Dungeons.DoorsKey) : null;
            if (b != null) for (int i = 0; i + 2 < b.Length; i += 3) r.Add((b[i], b[i + 1], b[i + 2]));
            return r;
        }

        public void SetDoor(int level, int u, int v, bool open)
        {
            var doors = OpenDoors();
            if (open ? !doors.Add((level, u, v)) : !doors.Remove((level, u, v))) return;
            var b = doors.SelectMany(x => new[] { (byte)x.Item1, (byte)x.Item2, (byte)x.Item3 }).ToArray();
            Write(z => z.Set(Dungeons.DoorsKey, b), false);
        }

        /// <summary>The first time anyone goes in: the hallways' chests are put out, filled from the dungeon chest table by level.</summary>
        public void Prepare()
        {
            if (!Valid || Zdo.GetBool(Dungeons.InitKey)) return;
            Write(z => z.Set(Dungeons.InitKey, true), false);
            var rnd = new System.Random();
            int n = 0;
            for (int level = 0; level < DungeonInfo.Levels; level++)
            {
                var plan = Dungeons.Planner.Level(D, level, Cells);
                foreach (var f in plan.Features.Where(f => f.Kind == DFeatureKind.Chest))
                {
                    var at = Dungeons.World(D, level, f.U, f.V, 0.05f);
                    Treasure.SpawnChest(at, Quaternion.Euler(0, rnd.Next(4) * 90, 0), LootTables.DungeonChest(Plugin.U5.Ovl, level, rnd), (f.Value & 7) != 0);
                    n++;
                }
            }
            Plugin.Log.LogInfo($"{Dungeons.Name(D)}: {n} chests put out");
        }

        public string GetHoverText()
        {
            if (D < 0) return "";
            string name = Dungeons.Name(D);
            return Open ? Localization.instance.Localize($"{name}\n[<color=yellow><b>$KEY_Use</b></color>] Enter") : $"{name}\n{TownDoors.LockedText("magic")}";
        }

        public string GetHoverName() => D >= 0 ? Dungeons.Name(D) : "";
        public float GetHoverOffset() => 0.5f;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !(user is Player player) || player != Player.m_localPlayer || D < 0) return false;
            if (!Open)
            {
                player.Message(MessageHud.MessageType.Center, TownDoors.LockedText("magic"));
                return true;
            }
            // on foot only: no horse, carpet or boat goes through a dungeon's door (so none reaches the Underworld that way)
            if (player.IsRiding() || player.IsAttached())
            {
                player.Message(MessageHud.MessageType.Center, Plugin.U5.Ovl.CStr(ItemUse.SOnlyOnFoot).Trim());
                return true;
            }
            if (Below) Underworld.EnterFromBelow(player, D);
            else Dungeons.Enter(player, this);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    }
}
