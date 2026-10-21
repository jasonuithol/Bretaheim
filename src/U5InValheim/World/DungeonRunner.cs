using System;
using System.Collections.Generic;
using System.Linq;
using U5InValheim.Data;
using U5InValheim.WorldGen.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// Builds the dungeon levels near anyone in them (the local player; on a server every peer)
    /// and runs what happens to the local player inside (DUNGEON dungeon_turn_events): darkness,
    /// the level wrapping round (8 → 0), pits, bombs, energy fields, lava, rooms filling with
    /// their monsters and their triggers, rooms cleared or filling again. Levels are local
    /// objects, never saved: a mesh of floors, walls and ceilings (one tile 2 m, walls 6 m) plus
    /// furniture, ladders, grates, doors, fountains and fields as visual copies of Valheim pieces.
    /// </summary>
    public class DungeonRunner : MonoBehaviour
    {
        static DungeonRunner instance;
        readonly Dictionary<(int d, int level), DungeonLevelBuild> built = new Dictionary<(int, int), DungeonLevelBuild>();
        readonly Dictionary<(int, int), float> lastWanted = new Dictionary<(int, int), float>();
        readonly Dictionary<int, float> roomSpawned = new Dictionary<int, float>();
        float nextScan, nextRooms, nextHazard;
        int lastD = -1, lastLevel = -1, lastCell = -1;
        Vector3 lastSafe;
        bool forcedEnv;
        static readonly System.Random random = new System.Random();
        static DataOvl Ovl => Plugin.U5.Ovl;

        void Awake() => instance = this;

        /// <summary>The level built now (synchronously) for a player about to arrive on it; its plan.</summary>
        public static DungeonLevelPlan EnsureBuilt(int d, int level)
        {
            if (instance == null) return null;
            var e = DungeonEntrance.Get(d);
            if (e == null) { Plugin.Log.LogWarning($"Dungeon {d}: entrance not loaded"); return null; }
            instance.lastWanted[(d, level)] = Time.time;
            if (!instance.built.TryGetValue((d, level), out var b) || b.Revision != e.Revision)
            {
                b?.Destroy();
                instance.built[(d, level)] = b = new DungeonLevelBuild(d, level, e);
            }
            return b.Plan;
        }

        /// <summary>For the self-test: fill a room now / run the room bookkeeping now.</summary>
        public static void FillRoomNow(int d, int level, int cell)
        {
            var e = DungeonEntrance.Get(d);
            byte c = e.Cells[level * 64 + cell];
            instance.FillRoom(e, d, level, cell, c & 0xf);
        }

        public static void CheckRooms(int d)
        {
            var e = DungeonEntrance.Get(d);
            if (e != null) instance.Rooms(e, d);
        }

        public static DungeonLevelBuild Built(int d, int level) => instance != null && instance.built.TryGetValue((d, level), out var b) ? b : null;

        /// <summary>spawn_dungeon_monster: arriving on a level brings its wandering monster, if it has none about.</summary>
        public static void Arrived(int d, int level, Player player)
        {
            if (instance == null) return;
            int tag = d * 8 + level + 1;
            if (Character.GetAllCharacters().Any(c => !c.IsDead() && c.GetComponent<ZNetView>()?.GetZDO()?.GetInt(Dungeons.WanderTag) == tag)) return;
            if (!instance.built.TryGetValue((d, level), out var b)) return;
            var types = DungeonInfo.Wanderers(Ovl);
            int type = types[random.Next(types.Length)];
            Dungeons.Inside(player.transform.position, out _, out _, out double pu, out double pv);
            int here = DungeonLevelPlan.CellOf((int)Math.Round(pu), (int)Math.Round(pv), out _, out _);
            var halls = Enumerable.Range(0, 64).Where(i => (b.Plan.CellByIndex(i) & 0xf0) == 0
                && Math.Abs(i % 8 - here % 8) + Math.Abs(i / 8 - here / 8) >= 2).ToList();
            if (halls.Count == 0) return;
            int cell = halls[random.Next(halls.Count)];
            var (u, v) = DungeonLevelPlan.CellCentre(cell);
            var c = UltimaCreatures.Spawn(type, Dungeons.World(d, level, u, v, 0.5f), false);
            c?.GetComponent<ZNetView>()?.GetZDO()?.Set(Dungeons.WanderTag, tag);
        }

        void Update()
        {
            if (Britannia.Active == null || Plugin.U5 == null)
            {
                if (built.Count > 0) { foreach (var b in built.Values) b.Destroy(); built.Clear(); }
                return;
            }
            if (Time.time >= nextScan)
            {
                nextScan = Time.time + 0.5f;
                Scan();
            }
            var player = Player.m_localPlayer;
            if (player == null || player.IsDead() || player.IsTeleporting()) return;
            bool inside = Dungeons.Inside(player.transform.position, out int d, out int level, out double u, out double v);
            Darkness(inside);
            if (!inside) { lastD = -1; return; }
            var e = DungeonEntrance.Get(d);
            if (e == null || !built.ContainsKey((d, level))) return;
            if (Wrap(player, d, u, v)) return;
            int tu = (int)Math.Round(u), tv = (int)Math.Round(v);
            int cell = DungeonLevelPlan.CellOf(tu, tv, out int lx, out int ly);
            if (d != lastD || level != lastLevel)
            {
                if (lastD == d && lastLevel >= 0) Arrived(d, level, player);
                lastCell = -1;
            }
            bool entered = d != lastD || level != lastLevel || cell != lastCell;
            lastD = d; lastLevel = level; lastCell = cell;
            Hazards(player, e, d, level, cell, lx, ly, tu, tv, entered);
            if (Time.time >= nextRooms)
            {
                nextRooms = Time.time + 2f;
                Rooms(e, d);
            }
        }

        /// <summary>Which levels are wanted (anyone within a level of them) and built; changed ones rebuilt.</summary>
        void Scan()
        {
            var at = new List<Vector3>();
            if (Player.m_localPlayer != null) at.Add(Player.m_localPlayer.transform.position);
            if (ZNet.instance != null && ZNet.instance.IsServer())
                foreach (var peer in ZNet.instance.GetPeers()) at.Add(peer.m_refPos);
            foreach (var p in at)
            {
                if (!Dungeons.Inside(p, out int d, out int level, out _, out _)) continue;
                for (int k = Math.Max(0, level - 1); k <= Math.Min(DungeonInfo.Levels - 1, level + 1); k++) lastWanted[(d, k)] = Time.time;
            }
            bool one = false;
            foreach (var key in lastWanted.Keys.ToList())
            {
                var e = DungeonEntrance.Get(key.Item1);
                if (Time.time - lastWanted[key] > 20f || e == null)
                {
                    if (built.TryGetValue(key, out var old)) { old.Destroy(); built.Remove(key); }
                    lastWanted.Remove(key);
                    continue;
                }
                if (built.TryGetValue(key, out var b) && b.Revision == e.Revision) { b.Refresh(e); continue; }
                if (one) continue; // one level a scan, so building never stalls a frame twice over
                one = true;
                b?.Destroy();
                built[key] = new DungeonLevelBuild(key.Item1, key.Item2, e);
            }
        }

        /// <summary>Dungeons are dark: Valheim's crypt weather while inside.</summary>
        void Darkness(bool inside)
        {
            if (EnvMan.instance == null) return;
            if (inside && !forcedEnv)
            {
                EnvMan.instance.SetForceEnvironment(DarkEnv());
                forcedEnv = true;
            }
            else if (!inside && forcedEnv)
            {
                EnvMan.instance.SetForceEnvironment("");
                forcedEnv = false;
            }
        }

        static string darkEnv;
        static string DarkEnv()
        {
            if (darkEnv != null) return darkEnv;
            var names = EnvMan.instance.m_environments.Select(s => s.m_name).ToList();
            darkEnv = names.FirstOrDefault(n => n == "Crypt") ?? names.FirstOrDefault(n => n.Contains("Crypt")) ?? names.FirstOrDefault(n => n.Contains("Cave")) ?? "";
            Plugin.Log.LogInfo($"Dungeons: dark environment '{darkEnv}'");
            return darkEnv;
        }

        /// <summary>The level is a torus: past half a cell beyond an edge, the player is moved a level's width back.</summary>
        static bool Wrap(Player player, int d, double u, double v)
        {
            const double past = DungeonLevelPlan.CellTiles / 2.0;
            float w = DungeonLevelPlan.Tiles * DungeonLevelPlan.Tile;
            var shift = Vector3.zero;
            if (u < -past) shift.x = w;
            else if (u > DungeonLevelPlan.Tiles - 1 + past) shift.x = -w;
            if (v < -past) shift.z = -w;
            else if (v > DungeonLevelPlan.Tiles - 1 + past) shift.z = w;
            if (shift == Vector3.zero) return false;
            Dungeons.Move(player, player.transform.position + shift);
            return true;
        }

        // ── what the place does to the player ─────────────────────────────────────────────

        void Hazards(Player player, DungeonEntrance e, int d, int level, int cell, int lx, int ly, int tu, int tv, bool entered)
        {
            byte c = e.Cells[level * 64 + cell];
            int hi = c & 0xf0;
            var b = built[(d, level)];
            bool tick = Time.time >= nextHazard;
            if (tick) nextHazard = Time.time + 2f;

            if (hi == 0xf0 || hi == 0xa0)
            {
                if (entered)
                {
                    player.Message(MessageHud.MessageType.TopLeft, Ovl.CStr(Dungeons.SEnteringRoom).Trim());
                    if (hi == 0xf0 && e.RoomState(c & 0xf) == 0) FillRoom(e, d, level, cell, c & 0xf);
                }
                // check_room_trigger: stepping on a trigger changes its tiles (once)
                var map = Dungeons.Planner.Room(d, c & 0xf);
                foreach (var t in DungeonInfo.Triggers(map))
                    if (t.X == lx && t.Y == ly && !e.UsedTriggers().Contains((level, cell, t.Index)))
                    {
                        e.FireTrigger(level, cell, t);
                        Plugin.Log.LogInfo($"{Dungeons.Name(d)} level {level} room {c & 0xf}: trigger {t.Index} at ({t.X},{t.Y}) → {t.Tile:x2}");
                    }
                var cls = b.Plan.ClassAt(tu, tv);
                if (cls == DTile.Lava && tick) Fire(player);
                foreach (var f in b.Plan.Features)
                    if (f.Kind == DFeatureKind.Field && f.U == DungeonLevelPlan.Mod(tu, 88) && f.V == DungeonLevelPlan.Mod(tv, 88) && (entered || tick))
                        Field(player, e, level, cell, f.Value, false);
                lastSafe = player.transform.position;
                return;
            }
            if (hi == 0x80)
            {
                if (entered || tick) Field(player, e, level, cell, c, true);
                return;
            }
            lastSafe = player.transform.position;
            if (!entered) return;
            if (c == 0x61 || c == 0x69) Fall(player, e, d, level, cell, c);
            else if (c == 0x62 || c == 0x6a)
            {
                player.Message(MessageHud.MessageType.Center, Ovl.CStr(0x2dc7).Trim() + "\n" + Ovl.CStr(0x2dd3).Trim());
                Dungeons.Hurt(player, Dungeons.Roll8());
                e.SetCell(level, cell, (byte)(c & 8));
            }
        }

        static void Fire(Player player)
        {
            player.Message(MessageHud.MessageType.TopLeft, Ovl.CStr(0x2dbf).Trim());
            Dungeons.Hurt(player, Dungeons.Roll8(), fire: true);
        }

        /// <summary>sleep_field_effect, poison_field_effect, "Fire!!", electric_field_push.</summary>
        void Field(Player player, DungeonEntrance e, int level, int cell, byte value, bool hallway)
        {
            switch (DungeonInfo.Field(value))
            {
                case FieldKind.Sleep:
                    player.Message(MessageHud.MessageType.Center, Ovl.CStr(0x2d53).Trim());
                    // a dexterity of at most 1..30 falls asleep: knocked down
                    if (AvatarStats.Mine.Dexterity <= random.Next(1, 31)) player.Stagger(-player.transform.forward);
                    if (hallway) e.SetCell(level, cell, (byte)(value & 8));
                    break;
                case FieldKind.Poison:
                    player.Message(MessageHud.MessageType.Center, Ovl.CStr(0x2d61).Trim());
                    if (AvatarStats.Mine.Dexterity <= random.Next(1, 31)) Dungeons.Hurt(player, 0, poison: true);
                    break;
                case FieldKind.Fire:
                    Fire(player);
                    break;
                case FieldKind.Electric:
                    player.Message(MessageHud.MessageType.Center, Ovl.CStr(0x2ca8).Trim() + " " + Ovl.CStr(0x2caf).Trim());
                    Dungeons.Move(player, lastSafe);
                    Dungeons.Hurt(player, Dungeons.Roll8());
                    break;
            }
        }

        /// <summary>fall_through_pit: the hidden pit opens (a pit now), the player falls a level and lands hurt; the landing cell shows the hole above.</summary>
        void Fall(Player player, DungeonEntrance e, int d, int level, int cell, byte c)
        {
            player.Message(MessageHud.MessageType.Center, Ovl.CStr(0x2d6a).Trim() + "\n" + Ovl.CStr(0x2d75).Trim());
            var changes = new List<(int, byte)> { (level * 64 + cell, (byte)(c & 0xf8)) };
            if (level == DungeonInfo.Levels - 1)
            {
                e.SetCells(changes);
                var below = Dungeons.UnderworldExit?.Invoke(d);
                if (below != null) player.TeleportTo(below.Value, player.transform.rotation, true);
                return;
            }
            byte landing = e.Cells[(level + 1) * 64 + cell];
            if (landing < 0x90) changes.Add(((level + 1) * 64 + cell, (byte)(landing | 8)));
            e.SetCells(changes);
            var plan = EnsureBuilt(d, level + 1);
            if (plan == null) return;
            var p = player.transform.position;
            Dungeons.Inside(p, out _, out _, out double u, out double v);
            var (su, sv) = Dungeons.Stand(plan, (int)Math.Round(u), (int)Math.Round(v));
            var to = Dungeons.World(d, level + 1, su, sv, 1.5f);
            Dungeons.Move(player, to);
            player.Message(MessageHud.MessageType.Center, Ovl.CStr(0x2d81).Trim());
            Dungeons.Hurt(player, Dungeons.Roll8());
        }

        // ── rooms ────────────────────────────────────────────────────────────────────────────

        /// <summary>setup_room_combatants: the room's monsters (Ultima creatures of their types) and objects (chests, loot) where its map has them.</summary>
        void FillRoom(DungeonEntrance e, int d, int level, int cell, int room)
        {
            e.SetRoomState(room, 1);
            roomSpawned[d * 16 + room] = Time.time;
            var monsters = new List<RoomMonster>();
            var objects = new List<RoomObject>();
            DungeonInfo.RoomContents(Ovl, Dungeons.Planner.Room(d, room), level, random, monsters, objects);
            int ox = (cell % 8) * DungeonLevelPlan.CellTiles, oy = (cell / 8) * DungeonLevelPlan.CellTiles;
            int made = 0;
            foreach (var m in monsters)
            {
                var ch = UltimaCreatures.Spawn(m.Type, Dungeons.World(d, level, ox + m.X, oy + m.Y, 0.3f), false);
                if (ch == null) continue;
                ch.GetComponent<ZNetView>()?.GetZDO()?.Set(Dungeons.RoomTag, d * 16 + room + 1);
                made++;
            }
            foreach (var o in objects)
            {
                var at = Dungeons.World(d, level, ox + o.X, oy + o.Y, 0.05f);
                if (o.Loot.Kind == LootKind.Chest) Treasure.SpawnChest(at, Quaternion.Euler(0, random.Next(4) * 90, 0), o.Loot.Value, false);
                else if (o.Loot.Kind != LootKind.None) Treasure.SpawnLoot(at, o.Loot);
            }
            Plugin.Log.LogInfo($"{Dungeons.Name(d)} level {level} room {room}: {made}/{monsters.Count} monsters, {objects.Count(o => o.Loot.Kind != LootKind.None)} objects");
        }

        /// <summary>
        /// By whoever holds the dungeon's state: a room whose monsters are all dead is cleared (its
        /// cells become cleared rooms, 0xa_), or, if it is one that fills again, fills again once
        /// nobody is in it; a room left by everyone loses its triggers' changes.
        /// </summary>
        void Rooms(DungeonEntrance e, int d)
        {
            if (!e.IsOwner) return;
            var players = Player.GetAllPlayers().Where(p => !p.IsDead()).Select(p =>
            {
                bool inside = Dungeons.Inside(p.transform.position, out int pd, out int pl, out double u, out double v);
                int cell = inside ? DungeonLevelPlan.CellOf((int)Math.Round(u), (int)Math.Round(v), out _, out _) : -1;
                return (inside && pd == d, level: pl, cell);
            }).Where(x => x.Item1).ToList();
            var cells = e.Cells;
            for (int room = 0; room < 16; room++)
            {
                byte state = e.RoomState(room);
                int tag = d * 16 + room + 1;
                if (state == 1)
                {
                    if (roomSpawned.TryGetValue(tag - 1, out float when) && Time.time - when < 5f) continue;
                    bool alive = Character.GetAllCharacters().Any(c => !c.IsDead() && c.GetComponent<ZNetView>()?.GetZDO()?.GetInt(Dungeons.RoomTag) == tag);
                    if (alive) continue;
                    if (DungeonInfo.Persistent(Ovl, d, room)) e.SetRoomState(room, 3);
                    else
                    {
                        // mark_dungeon_room_cleared, apply_cleared_rooms_to_map: every cell of this room is a cleared room
                        e.SetRoomState(room, 2);
                        var changes = Enumerable.Range(0, 512).Where(i => cells[i] == 0xf0 + room).Select(i => (i, (byte)(0xa0 + room))).ToList();
                        if (changes.Count > 0) e.SetCells(changes);
                    }
                    Plugin.Log.LogInfo($"{Dungeons.Name(d)} room {room} cleared");
                }
                else if (state == 3)
                {
                    bool occupied = players.Any(p => (cells[p.level * 64 + p.cell] & 0xf) == room && (cells[p.level * 64 + p.cell] & 0xf0) == 0xf0);
                    if (!occupied) e.SetRoomState(room, 0);
                }
            }
            foreach (var (level, cell) in e.ChangedRooms().ToList())
                if (!players.Any(p => p.level == level && p.cell == cell)) e.ResetRoom(level, cell);
        }
    }

    /// <summary>One dungeon level as built on this peer.</summary>
    public class DungeonLevelBuild
    {
        public readonly int D, Level;
        public readonly int Revision;
        public readonly DungeonLevelPlan Plan;
        readonly GameObject root;
        readonly List<DungeonDoor> doors = new List<DungeonDoor>();

        const int Margin = DungeonLevelPlan.CellTiles;
        static readonly bool headless = SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;

        public DungeonLevelBuild(int d, int level, DungeonEntrance e)
        {
            D = d;
            Level = level;
            Revision = e.Revision;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var cells = e.Cells;
            var changes = e.Changes();
            Plan = Dungeons.Planner.Level(d, level, cells, changes);
            var above = level > 0 ? Dungeons.Planner.Level(d, level - 1, cells, changes) : null;
            var c = Dungeons.Centre(d) ?? Vector2.zero;
            root = new GameObject($"u5_dungeon_{Dungeons.Name(d)}_{level}");
            root.transform.position = new Vector3(c.x, Dungeons.LevelY(level), c.y);
            int tris = BuildMesh(above);
            int objects = BuildFeatures(e);
            Refresh(e);
            Plugin.Log.LogInfo($"Built {Dungeons.Name(d)} level {level} (rev {Revision}): {tris} triangles, {objects} objects in {sw.ElapsedMilliseconds} ms");
        }

        public void Destroy()
        {
            if (root != null) Object.Destroy(root);
        }

        /// <summary>Doors follow the dungeon's state.</summary>
        public void Refresh(DungeonEntrance e)
        {
            if (doors.Count == 0) return;
            var open = e.OpenDoors();
            foreach (var door in doors) if (door != null) door.Show(open.Contains((Level, door.U, door.V)));
        }

        // ── the mesh ─────────────────────────────────────────────────────────────────────────

        enum Sub { Stone, Earth, Planks, Water, Lava, Wall, Ceiling, Count }

        class MeshParts
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<int>[] T = Enumerable.Range(0, (int)Sub.Count).Select(_ => new List<int>()).ToArray();

            /// <summary>A quad a-b-c-d (in order round it) facing <paramref name="n"/>, its texture laid by world metres.</summary>
            public void Quad(Sub s, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
            {
                int i = V.Count;
                foreach (var p in new[] { a, b, c, d })
                {
                    V.Add(p);
                    N.Add(n);
                    UV.Add(Mathf.Abs(n.y) > 0.5f ? new Vector2(p.x / 2f, p.z / 2f) : new Vector2((Mathf.Abs(n.x) > 0.5f ? p.z : p.x) / 2f, p.y / 2f));
                }
                bool front = Vector3.Dot(Vector3.Cross(b - a, c - a), n) > 0;
                var t = T[(int)s];
                if (front) t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                else t.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            }
        }

        static Sub FloorOf(DTile c) => c == DTile.Earth ? Sub.Earth : c == DTile.Planks ? Sub.Planks : c == DTile.Water ? Sub.Water : c == DTile.Lava ? Sub.Lava : Sub.Stone;

        int BuildMesh(DungeonLevelPlan above)
        {
            const float T = DungeonLevelPlan.Tile, H = DungeonLevelPlan.Height, S = DungeonLevelPlan.Slab;
            var m = new MeshParts();
            int lo = -Margin, hi = DungeonLevelPlan.Tiles + Margin;
            bool Open(int u, int v) => u >= lo && v >= lo && u < hi && v < hi && Plan.ClassAt(u, v) != DTile.Solid;
            float X(int u) => (u - DungeonLevelPlan.Tiles / 2) * T;          // west edge of tile u
            float Z(int v) => (DungeonLevelPlan.Tiles / 2 - v) * T;          // north edge of tile v
            for (int v = lo; v < hi; v++)
            {
                // floors and ceilings in runs along the row
                int runStart = -1; Sub runSub = Sub.Stone;
                int ceilStart = -1;
                for (int u = lo; u <= hi; u++)
                {
                    bool open = u < hi && Open(u, v);
                    bool floor = open && !Plan.FloorHole(u, v);
                    var sub = floor ? FloorOf(Plan.ClassAt(u, v)) : Sub.Stone;
                    if (runStart >= 0 && (!floor || sub != runSub))
                    {
                        Flat(m, runSub, X(runStart), X(u), Z(v + 1), Z(v), 0, Vector3.up);
                        runStart = -1;
                    }
                    if (floor && runStart < 0) { runStart = u; runSub = sub; }
                    bool ceil = open && !Plan.CeilingHole(u, v);
                    if (ceilStart >= 0 && !ceil) { Flat(m, Sub.Ceiling, X(ceilStart), X(u), Z(v + 1), Z(v), H, Vector3.down); ceilStart = -1; }
                    if (ceil && ceilStart < 0) ceilStart = u;
                }
                for (int u = lo; u < hi; u++)
                {
                    if (!Open(u, v)) continue;
                    float x0 = X(u), x1 = X(u + 1), zn = Z(v), zs = Z(v + 1);
                    // walls toward solid neighbours
                    if (!Open(u, v - 1)) Wall(m, x0, zn, x1, zn, 0, H, Vector3.back);
                    if (!Open(u, v + 1)) Wall(m, x0, zs, x1, zs, 0, H, Vector3.forward);
                    if (!Open(u + 1, v)) Wall(m, x1, zs, x1, zn, 0, H, Vector3.left);
                    if (!Open(u - 1, v)) Wall(m, x0, zs, x0, zn, 0, H, Vector3.right);
                    // the shafts of holes through the stone below and above
                    if (Plan.FloorHole(u, v)) Shaft(m, x0, x1, zs, zn, -S, 0);
                    if (Plan.CeilingHole(u, v))
                    {
                        Shaft(m, x0, x1, zs, zn, H, H + S);
                        if (above == null || !above.FloorHole(u, v)) Flat(m, Sub.Ceiling, x0, x1, zs, zn, H + S - 0.02f, Vector3.down);
                    }
                }
            }
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(m.V);
            mesh.SetNormals(m.N);
            mesh.SetUVs(0, m.UV);
            mesh.subMeshCount = (int)Sub.Count;
            int tris = 0;
            for (int s = 0; s < (int)Sub.Count; s++) { mesh.SetTriangles(m.T[s], s); tris += m.T[s].Count / 3; }
            mesh.RecalculateBounds();
            var go = new GameObject("shell");
            go.layer = LayerMask.NameToLayer("static_solid");
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            if (!headless) go.AddComponent<MeshRenderer>().sharedMaterials = DungeonLooks.Materials(Plan.Style);
            return tris;
        }

        static void Flat(MeshParts m, Sub s, float x0, float x1, float z0, float z1, float y, Vector3 n)
            => m.Quad(s, new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0), n);

        static void Wall(MeshParts m, float xa, float za, float xb, float zb, float y0, float y1, Vector3 n)
            => m.Quad(Sub.Wall, new Vector3(xa, y0, za), new Vector3(xa, y1, za), new Vector3(xb, y1, zb), new Vector3(xb, y0, zb), n);

        static void Shaft(MeshParts m, float x0, float x1, float zs, float zn, float y0, float y1)
        {
            Wall(m, x0, zn, x1, zn, y0, y1, Vector3.back);
            Wall(m, x0, zs, x1, zs, y0, y1, Vector3.forward);
            Wall(m, x1, zs, x1, zn, y0, y1, Vector3.left);
            Wall(m, x0, zs, x0, zn, y0, y1, Vector3.right);
        }

        // ── what stands in it ────────────────────────────────────────────────────────────────

        Vector3 Local(int u, int v, float y = 0)
        {
            var (x, z) = DungeonLevelPlan.Local(u, v);
            return new Vector3(x, y, z);
        }

        GameObject Copy(string prefab, Vector3 local, Quaternion rot, Vector3 scale, Transform parent = null)
        {
            var t = Structures.Template(prefab);
            if (t == null) return null;
            var go = Object.Instantiate(t, parent ?? root.transform);
            go.transform.localPosition = local;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            go.SetActive(true);
            return go;
        }

        int BuildFeatures(DungeonEntrance e)
        {
            const float H = DungeonLevelPlan.Height, S = DungeonLevelPlan.Slab;
            int n = 0;
            foreach (var f in Plan.Features)
            {
                switch (f.Kind)
                {
                    case DFeatureKind.Prop:
                    {
                        var p = f.Piece;
                        var rot = Quaternion.Euler(0, p.Yaw, 0);
                        if (p.Turned) rot *= Quaternion.Euler(p.Tilt, 0, 0) * Quaternion.Euler(0, p.Spin, 0) * Quaternion.Euler(0, 0, p.Roll);
                        var go = Copy(p.Prefab, new Vector3(p.X, p.Y, p.Z), rot, p.Scaled ? new Vector3(p.SX, p.SY, p.SZ) : Vector3.one);
                        if (go == null) break;
                        if (p.Look != null) Looks.Apply(go, p.Look);
                        if (!headless && (p.Prefab.Contains("torch") || p.Prefab.Contains("brazier") || p.Prefab.Contains("fire_pit") || p.Prefab.Contains("hearth")))
                            DungeonLooks.Burn(go);
                        n++;
                        break;
                    }
                    case DFeatureKind.LadderUp:
                    {
                        var go = Ladder(Local(f.U, f.V), H + S + 0.1f);
                        Climb(go, f, -1, false, "Climb up");
                        n++;
                        break;
                    }
                    case DFeatureKind.LadderDown:
                    case DFeatureKind.Pit:
                    {
                        // an iron grate over the hole, climbed down (the original's Klimb down a pit too)
                        var go = Copy("iron_grate", Local(f.U, f.V, -0.1f) + new Vector3(0, 0, -1f / 3f), Quaternion.Euler(90, 0, 0), new Vector3(1, 2f / 3f, 1));
                        if (go == null) break;
                        Climb(go, f, 1, false, f.Kind == DFeatureKind.Pit ? "Climb down the pit" : "Climb down");
                        n++;
                        break;
                    }
                    case DFeatureKind.Grapple:
                    {
                        // a hole above: a knotted line hangs from it, climbed with a grapple
                        var go = Copy("wood_pole2", Local(f.U, f.V, H - 1.5f), Quaternion.identity, new Vector3(0.12f, 1.5f, 0.12f));
                        if (go == null) break;
                        Climb(go, f, -1, true, "Climb up (grapple)");
                        n++;
                        break;
                    }
                    case DFeatureKind.Fountain:
                    {
                        var go = new GameObject("fountain");
                        go.transform.SetParent(root.transform, false);
                        go.transform.localPosition = Local(f.U, f.V);
                        go.layer = LayerMask.NameToLayer("piece_nonsolid");
                        var box = go.AddComponent<BoxCollider>();
                        box.size = new Vector3(2.2f, 2f, 2.2f);
                        box.center = new Vector3(0, 1f, 0);
                        go.AddComponent<DungeonFountain>().Kind = f.Value & 0xf;
                        n++;
                        break;
                    }
                    case DFeatureKind.Door:
                    {
                        var hinge = new GameObject("door");
                        hinge.transform.SetParent(root.transform, false);
                        // the leaf lies across the way through, hinged on its west (or north) side
                        float yaw = f.Yaw == 90 ? 90 : 0;
                        var rot = Quaternion.Euler(0, yaw, 0);
                        hinge.transform.localPosition = Local(f.U, f.V) + rot * new Vector3(-1f, 0, 0);
                        hinge.transform.localRotation = rot;
                        var leaf = Copy("darkwood_gate", new Vector3(1f, 0, 0), Quaternion.identity, new Vector3(1f, 1.4f, 1f), hinge.transform);
                        var door = hinge.AddComponent<DungeonDoor>();
                        door.Init(D, Level, DungeonLevelPlan.Mod(f.U, 88), DungeonLevelPlan.Mod(f.V, 88), f.Lock, yaw);
                        doors.Add(door);
                        n++;
                        break;
                    }
                    case DFeatureKind.Secret:
                    {
                        // the wall face that hides the door: rune-marked stone, a blow shows the door
                        var rot = Quaternion.Euler(0, f.Yaw, 0);
                        var face = Local(f.U, f.V) + rot * new Vector3(0, 0, DungeonLevelPlan.Tile / 2);
                        var go = Copy("stone_wall_4x2", face + Vector3.up * (H / 2), rot, new Vector3(1.4f, H / 2f, 0.3f));
                        if (go == null) break;
                        Looks.Apply(go, "rune");
                        go.AddComponent<DungeonSecret>().Init(D, Level, f.Cell, f.Value);
                        n++;
                        break;
                    }
                    case DFeatureKind.Field:
                    {
                        if (headless) break;
                        bool hallway = (f.Value & 0xf0) == 0x80;
                        float size = hallway ? 14f : DungeonLevelPlan.Tile;
                        DungeonLooks.Field(root.transform, Local(f.U, f.V, H / 2), new Vector3(size, H, size), DungeonInfo.Field(f.Value));
                        n++;
                        break;
                    }
                }
            }
            return n;
        }

        /// <summary>A ladder of two rails and rungs every 0.4 m, walked through (as the towns' are), climbed with E.</summary>
        GameObject Ladder(Vector3 at, float height)
        {
            var go = new GameObject("ladder");
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = at;
            foreach (int side in new[] { -1, 1 })
                Copy("wood_pole2", new Vector3(side * 0.45f, height / 2, 0), Quaternion.identity, new Vector3(0.25f, height / 2, 0.25f), go.transform);
            for (float h = 0.3f; h < height - 0.2f; h += 0.4f)
                Copy("wood_beam_1", new Vector3(0, h, 0), Quaternion.identity, new Vector3(0.96f, 0.25f, 0.25f), go.transform);
            Looks.Apply(go, "passable");
            return go;
        }

        void Climb(GameObject go, DFeature f, int dir, bool grapple, string verb)
        {
            var c = go.AddComponent<DungeonClimb>();
            c.D = D; c.Level = Level; c.U = f.U; c.V = f.V; c.Dir = dir; c.Grapple = grapple; c.Verb = verb;
        }
    }

    /// <summary>Climbed with E: a ladder, a grate, a hole above.</summary>
    public class DungeonClimb : MonoBehaviour, Hoverable, Interactable
    {
        public int D, Level, U, V, Dir;
        public bool Grapple;
        public string Verb;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !(user is Player p) || p != Player.m_localPlayer) return false;
            Dungeons.Climb(p, D, Level, U, V, Dir, Grapple);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
        public string GetHoverText() => Localization.instance.Localize($"[<color=yellow><b>$KEY_Use</b></color>] {(Dir < 0 && Level == 0 && !Grapple ? "Leave " + Dungeons.Name(D) : Verb)}");
        public string GetHoverName() => Verb;
        public float GetHoverOffset() => 0.5f;
    }

    /// <summary>A fountain (look_in_dungeon): "Will you drink?" — 0 cures poison, 1 heals, 2 poisons, the rest taste bad (0..7 damage).</summary>
    public class DungeonFountain : MonoBehaviour, Hoverable, Interactable
    {
        public int Kind;
        static DataOvl Ovl => Plugin.U5.Ovl;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !(user is Player p) || p != Player.m_localPlayer) return false;
            string gulp = Ovl.CStr(0x7718).Trim();
            switch (Kind)
            {
                case 0:
                    p.GetSEMan().RemoveStatusEffect("Poison".GetStableHashCode());
                    p.Message(MessageHud.MessageType.Center, gulp + "\n" + Ovl.CStr(0x7726).Trim());
                    break;
                case 1:
                    p.Heal(p.GetMaxHealth());
                    p.Message(MessageHud.MessageType.Center, gulp + "\n" + Ovl.CStr(0x772e).Trim());
                    break;
                case 2:
                    Dungeons.Hurt(p, 0, poison: true);
                    p.Message(MessageHud.MessageType.Center, gulp + "\n" + Ovl.CStr(0x7738).Trim());
                    break;
                default:
                    int dmg = new System.Random().Next(0, 8);
                    if (dmg > 0) Dungeons.Hurt(p, dmg);
                    p.Message(MessageHud.MessageType.Center, gulp + "\n" + Ovl.CStr(0x7744).Trim());
                    break;
            }
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
        public string GetHoverText() => Localization.instance.Localize($"A fountain\n[<color=yellow><b>$KEY_Use</b></color>] Drink");
        public string GetHoverName() => "A fountain";
        public float GetHoverOffset() => 0.5f;
    }

    /// <summary>A heavy door: opened and shut with E for everyone (the dungeon's state); a locked one stays shut ("Locked! No keys!"), a portcullis for good.</summary>
    public class DungeonDoor : MonoBehaviour, Hoverable, Interactable
    {
        public int D, Level, U, V;
        string lockKind;
        bool open;

        float shutYaw;

        public void Init(int d, int level, int u, int v, string lockKind, float shutYaw)
        {
            D = d; Level = level; U = u; V = v;
            this.lockKind = lockKind;
            this.shutYaw = shutYaw;
        }

        /// <summary>Shut, the leaf lies across the way; open, it swings a quarter turn on its hinge.</summary>
        public void Show(bool isOpen)
        {
            open = isOpen;
            transform.localRotation = Quaternion.Euler(0, shutYaw - (open ? 90f : 0f), 0);
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !(user is Player p) || p != Player.m_localPlayer) return false;
            if (!open && lockKind != null)
            {
                p.Message(MessageHud.MessageType.Center, lockKind == "magic" ? "The portcullis is down." : TownDoors.LockedText(lockKind));
                return true;
            }
            DungeonEntrance.Get(D)?.SetDoor(Level, U, V, !open);
            Show(!open);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
        public string GetHoverText() => Localization.instance.Localize($"A heavy door\n[<color=yellow><b>$KEY_Use</b></color>] {(open ? "Close" : "Open")}");
        public string GetHoverName() => "A heavy door";
        public float GetHoverOffset() => 0.5f;
    }

    /// <summary>A secret door's face: a blow finds it ("A hidden door!") and the cell becomes a door for everyone.</summary>
    public class DungeonSecret : MonoBehaviour, IDestructible
    {
        int d, level, cell;
        byte value;

        public void Init(int d, int level, int cell, byte value)
        {
            this.d = d; this.level = level; this.cell = cell; this.value = value;
        }

        public void Damage(HitData hit)
        {
            if (!(hit.GetAttacker() is Player p) || p != Player.m_localPlayer) return;
            var e = DungeonEntrance.Get(d);
            if (e == null || (e.Cells[level * 64 + cell] & 0xf0) != 0xd0) return;
            p.Message(MessageHud.MessageType.Center, Plugin.U5.Ovl.CStr(0x88fe).Trim());
            // search_dungeon_cell: the cell becomes a door (its ceiling-hole bit kept)
            e.SetCell(level, cell, (byte)((value & 8) + 0xe0));
        }

        public DestructibleType GetDestructibleType() => DestructibleType.Default;
    }

    /// <summary>The dungeon's materials and light effects (only on machines that draw).</summary>
    public static class DungeonLooks
    {
        static readonly Dictionary<DungeonStyle, Material[]> materials = new Dictionary<DungeonStyle, Material[]>();
        static Material transparent;

        static Material From(string prefab, Color? tint = null)
        {
            var r = Structures.Template(prefab)?.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(x => x.sharedMaterial != null);
            if (r == null) return null;
            if (tint == null) return r.sharedMaterial;
            var m = new Material(r.sharedMaterial) { name = r.sharedMaterial.name + " (dungeon)" };
            if (m.HasProperty("_Color")) m.color = tint.Value;
            return m;
        }

        static Material Flat(Color c)
        {
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) return null;
            return new Material(shader) { color = c };
        }

        /// <summary>Stone floor, earth, planks, water, lava, walls, ceiling: crypts of dressed stone, caves and mines of darker rock.</summary>
        public static Material[] Materials(DungeonStyle style)
        {
            if (materials.TryGetValue(style, out var m)) return m;
            Color rock = style == DungeonStyle.Crypt ? Color.white : style == DungeonStyle.Mine ? new Color(0.75f, 0.68f, 0.6f) : new Color(0.62f, 0.58f, 0.55f);
            m = new[]
            {
                From("stone_floor_2x2", rock),
                From("stone_floor_2x2", new Color(0.55f, 0.45f, 0.33f)),
                From("wood_floor"),
                Flat(new Color(0.1f, 0.25f, 0.45f, 1f)),
                Flat(new Color(1f, 0.38f, 0.05f, 1f)),
                From("stone_wall_2x1", rock),
                From("stone_wall_2x1", rock * 0.7f),
            };
            materials[style] = m;
            return m;
        }

        /// <summary>A torch, brazier or fire: its fire objects lit and a warm light.</summary>
        public static void Burn(GameObject go)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t.name.IndexOf("enabled", StringComparison.OrdinalIgnoreCase) >= 0) t.gameObject.SetActive(true);
            if (go.GetComponentInChildren<Light>() != null) return;
            var l = new GameObject("light").AddComponent<Light>();
            l.transform.SetParent(go.transform, false);
            l.transform.localPosition = Vector3.up * 0.6f;
            l.type = LightType.Point;
            l.color = new Color(1f, 0.62f, 0.3f);
            l.range = 9f;
            l.intensity = 1.3f;
        }

        /// <summary>An energy field: a glowing haze of its colour (sleep blue, poison green, fire red, electric white) and its light.</summary>
        public static void Field(Transform parent, Vector3 at, Vector3 size, FieldKind kind)
        {
            Color c = kind == FieldKind.Sleep ? new Color(0.3f, 0.4f, 1f) : kind == FieldKind.Poison ? new Color(0.3f, 1f, 0.3f)
                : kind == FieldKind.Fire ? new Color(1f, 0.3f, 0.1f) : new Color(0.9f, 0.9f, 1f);
            var haze = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(haze.GetComponent<Collider>());
            haze.name = "field";
            haze.transform.SetParent(parent, false);
            haze.transform.localPosition = at;
            haze.transform.localScale = size;
            var mat = Flat(new Color(c.r, c.g, c.b, 0.16f));
            if (mat != null) haze.GetComponent<Renderer>().sharedMaterial = mat;
            var l = new GameObject("light").AddComponent<Light>();
            l.transform.SetParent(haze.transform, false);
            l.type = LightType.Point;
            l.color = c;
            l.range = Mathf.Max(size.x, 6f);
            l.intensity = 1.2f;
        }
    }
}
