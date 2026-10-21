using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using U5InValheim.Data;
using U5InValheim.WorldGen.Core;
using UnityEngine;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// The Underworld (MASTER_PLAN §5.1, Phase 7): UNDER.DAT as a second continent east of
    /// Britannia (<see cref="Continents"/>, terrain by <see cref="WorldGenPatches"/>, the world's
    /// edge pushed out by <see cref="WorldSize"/>), always dark (its own environment, chosen by
    /// position), and the original's ways between the two: a dungeon's bottom (down from level 7)
    /// and its Underworld entrance (into level 7), Doom's entrance down there, the falls that
    /// carry a boat over into the Underworld, the whirlpools of the deep sea
    /// (<see cref="Whirlpool"/>), and its earthquakes. A crossing with a ship moves the ship and
    /// everyone aboard (WorldWrap's passengers follow their ship's jump) once the zones there
    /// are made.
    /// </summary>
    public static class Underworld
    {
        public const string EnvName = "U5_Underworld";
        /// <summary>A dungeon entrance's ZDO bool: it is the dungeon's way in from the Underworld (always open).</summary>
        public const string BelowKey = "u5_dng_below";

        /// <summary>A ship or player is being carried between the continents (the edge wrap waits).</summary>
        public static bool Transferring
        {
            get => transferring && Time.time - transferSince < 60f; // (never stuck: a crossing broken off by leaving the world)
            private set { transferring = value; transferSince = Time.time; }
        }
        static bool transferring;
        static float transferSince;

        static DataOvl Ovl => Plugin.U5.Ovl;
        static Type clearAreaType;
        static readonly System.Random random = new System.Random();

        public static void Install(Harmony harmony)
        {
            Dungeons.UnderworldExit = ExitBelow;
            var env = AccessTools.Method(typeof(EnvMan), "GetEnvironmentOverride");
            if (env != null) harmony.Patch(env, postfix: new HarmonyMethod(typeof(Underworld), nameof(EnvironmentOverride_Postfix)));
            else Plugin.Log.LogWarning("Underworld: EnvMan.GetEnvironmentOverride not found (the Underworld has Valheim's weather)");
            clearAreaType = AccessTools.Inner(typeof(ZoneSystem), "ClearArea");
            var place = AccessTools.Method(typeof(ZoneSystem), "PlaceLocations");
            if (place != null && clearAreaType != null) harmony.Patch(place, postfix: new HarmonyMethod(typeof(Underworld), nameof(PlaceLocations_Postfix)));
            else Plugin.Log.LogError("Underworld: ZoneSystem.PlaceLocations/ClearArea not found; no ways in from below");
        }

        // ── where things are ─────────────────────────────────────────────────────────────────

        /// <summary>World position of the middle of tile (x, y) of the surface or the Underworld, on the ground (or the water).</summary>
        public static Vector3 Ground(int x, int y, bool under)
        {
            var p = Britannia.TileCentre(x, y, under);
            p.y = Mathf.Max(p.y, Britannia.SeaLevel);
            return p;
        }

        /// <summary>
        /// Doom's entrance in the Underworld (for the story: Lord British is rescued from Doom's
        /// bottom): the ground before it, or null outside a Britannia world. The entrance object is
        /// <c>DungeonEntrance.Get(UnderworldLinks.Doom)</c> while its zone is loaded; inside, Doom is
        /// built like any dungeon (Dungeons.World(7, level, u, v)).
        /// </summary>
        public static Vector3? DoomEntrance()
        {
            if (Britannia.Under == null) return null;
            var l = Plugin.U5.Location(DungeonInfo.Location(UnderworldLinks.Doom));
            return Ground(l.X, l.Y, true);
        }

        /// <summary>Where a dungeon's bottom leads (exit_dungeon down): the Underworld at the dungeon's own (x, y), before its entrance there.</summary>
        static Vector3? ExitBelow(int d)
        {
            if (Britannia.Under == null || d < 0 || d >= DungeonInfo.Count) return null;
            var l = Plugin.U5.Location(DungeonInfo.Location(d));
            var p = Ground(l.X, l.Y, true) + new Vector3(0, 0, -9f);
            p.y = Mathf.Max(Britannia.SeaLevel, Britannia.SeaLevel + Britannia.Under.Height(p.x, p.z)) + 1f;
            Plugin.Log.LogInfo($"Exit to the Underworld from {Dungeons.Name(d)}: {p}");
            Message(Player.m_localPlayer, Ovl.CStr(Dungeons.SExitTo).Trim() + " " + Ovl.CStr(Dungeons.SUnderworld).Trim());
            return p;
        }

        // ── the dark ─────────────────────────────────────────────────────────────────────────

        /// <summary>In the Underworld (not in a dungeon above it) the environment is always the Underworld's.</summary>
        static void EnvironmentOverride_Postfix(EnvMan __instance, ref string __result)
        {
            if (!string.IsNullOrEmpty(__result) || Britannia.Under == null) return;
            var p = Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : Utils.GetMainCamera()?.transform.position;
            if (p == null || p.Value.y > 1000f || !Britannia.InUnderworld(p.Value)) return;
            if (Environment(__instance) != null) __result = EnvName;
        }

        /// <summary>
        /// The Underworld's environment: always dark (EnvSetup.m_alwaysDark, so no sun and no day),
        /// a dim grey ambient light, a little dark fog, no rain or snow, little wind; made once
        /// from Valheim's Crypt.
        /// </summary>
        public static EnvSetup Environment(EnvMan env)
        {
            var have = env.m_environments.FirstOrDefault(e => e.m_name == EnvName);
            if (have != null) return have;
            var from = env.m_environments.FirstOrDefault(e => e.m_name == "Crypt") ?? env.m_environments.FirstOrDefault(e => e.m_name == "Clear") ?? env.m_environments.FirstOrDefault();
            if (from == null) return null;
            var s = from.Clone();
            s.m_name = EnvName;
            s.m_default = false;
            s.m_alwaysDark = true;
            s.m_isWet = s.m_isFreezing = s.m_isFreezingAtNight = s.m_isCold = s.m_isColdAtNight = false;
            s.m_snowBuildup = 0;
            var amb = new Color(0.16f, 0.15f, 0.18f);
            s.m_ambColorDay = s.m_ambColorNight = amb;
            var fog = new Color(0.04f, 0.04f, 0.05f);
            s.m_fogColorDay = s.m_fogColorNight = s.m_fogColorMorning = s.m_fogColorEvening = fog;
            s.m_fogColorSunDay = s.m_fogColorSunNight = s.m_fogColorSunMorning = s.m_fogColorSunEvening = fog;
            s.m_fogDensityDay = s.m_fogDensityNight = s.m_fogDensityMorning = s.m_fogDensityEvening = 0.012f;
            var sun = new Color(0.25f, 0.22f, 0.3f);
            s.m_sunColorDay = s.m_sunColorNight = s.m_sunColorMorning = s.m_sunColorEvening = sun;
            s.m_lightIntensityDay = s.m_lightIntensityNight = 0.15f;
            s.m_windMin = 0f;
            s.m_windMax = 0.2f;
            s.m_psystems = new GameObject[0];
            if (s.m_auroraColors == null) s.m_auroraColors = new Gradient();
            s.m_envObject = null;
            s.m_rainCloudAlpha = 0;
            s.m_cloudOpacityDay = s.m_cloudOpacityNight = s.m_cloudOpacityMorning = s.m_cloudOpacityEvening = 0;
            s.m_auroraIntensityDay = s.m_auroraIntensityNight = s.m_auroraIntensityMorning = s.m_auroraIntensityEvening = 0;
            env.AppendEnvironment(s);
            Plugin.Log.LogInfo($"Underworld: environment {EnvName} made from {from.m_name}");
            return s;
        }

        // ── the ways in from below ───────────────────────────────────────────────────────────

        /// <summary>
        /// With each Underworld zone: the entrances of the seven surface dungeons at their own (x, y)
        /// (UNDER.DAT has a cave, mine or dungeon tile there); Doom's, the Underworld's own dungeon,
        /// is placed by <see cref="Dungeons"/>.
        /// </summary>
        static void PlaceLocations_Postfix(Vector2s zoneID, IList clearAreas, ZoneSystem.SpawnMode mode, List<GameObject> spawnedObjects)
        {
            var under = Britannia.Under;
            if (under == null || Britannia.Active == null || (mode != ZoneSystem.SpawnMode.Full && mode != ZoneSystem.SpawnMode.Ghost)) return;
            for (int d = 0; d < DungeonInfo.Count; d++)
            {
                var l = Plugin.U5.Location(DungeonInfo.Location(d));
                if (l.InUnderworld) continue;
                under.TileCentre(l.X, l.Y, out float x, out float z);
                Dungeons.PlaceEntrance(zoneID, clearAreas, mode, spawnedObjects, d, under, new Vector2(x, z), below: true);
            }
        }

        /// <summary>
        /// enter_dungeon from the Underworld: level 7, cell (7, 7), facing west. Valheim's teleport
        /// brings the player (and loads the dungeon's surface entrance, which holds its state);
        /// the level is built and the player stood on its floor.
        /// </summary>
        public static void EnterFromBelow(Player player, int d)
        {
            if (player == null) return;
            Plugin.Instance.StartCoroutine(IntoDungeon(player, d));
        }

        static IEnumerator IntoDungeon(Player player, int d)
        {
            var (level, cx, cy, facing) = UnderworldLinks.DungeonArrival(d, fromUnderworld: true);
            var (u, v) = DungeonLevelPlan.CellCentre(cy * 8 + cx);
            var to = Dungeons.World(d, level, u, v, 0.5f);
            var rot = Quaternion.Euler(0, facing * 90f, 0);
            if (!player.TeleportTo(to, rot, true)) yield break;
            Message(player, Dungeons.Name(d));
            Plugin.Log.LogInfo($"{player.GetPlayerName()} enters {Dungeons.Name(d)} from the Underworld (level {level})");
            float t0 = Time.time;
            DungeonLevelPlan plan = null;
            while (player != null && Time.time - t0 < 60f)
            {
                if (!player.IsTeleporting() && (plan = DungeonRunner.EnsureBuilt(d, level)) != null) break;
                if (!player.IsTeleporting()) Dungeons.Move(player, to);
                yield return null;
            }
            if (player == null) yield break;
            if (plan == null) { Plugin.Log.LogWarning($"Underworld: {Dungeons.Name(d)} level {level} was not built"); yield break; }
            DungeonEntrance.Get(d)?.Prepare();
            var (su, sv) = Dungeons.Stand(plan, (int)u, (int)v, besides: true);
            Dungeons.Move(player, Dungeons.World(d, level, su, sv, 0.3f));
        }

        // ── crossings ────────────────────────────────────────────────────────────────────────

        public enum Way { Whirlpool, Falls }

        /// <summary>
        /// Carries the local player (and their ship, when they own the one they are on) to a
        /// tile: dragged down a whirlpool (spinning, sinking) or over the falls (tipping over the
        /// edge), then put there once its zones are made, the ship springing back up out of the
        /// water or dropping into the pool.
        /// </summary>
        public static void Carry(Player player, Ship ship, Vector3 centre, Vector3 dest, Way way)
        {
            if (Transferring || (player == null && ship == null)) return;
            Plugin.Instance.StartCoroutine(Carrying(player, ship, centre, dest, way));
        }

        static IEnumerator Carrying(Player player, Ship ship, Vector3 centre, Vector3 dest, Way way)
        {
            Transferring = true;
            if (player != null) GameCamera.instance?.AddShake(player.transform.position, 30f, way == Way.Whirlpool ? 3f : 2f, false);
            if (ship == null)
            {
                // a swimmer: Valheim's teleport (with its loading screen)
                yield return new WaitForSeconds(1f);
                if (player != null) player.TeleportTo(dest + Vector3.up * (way == Way.Falls ? 8f : 1f), player.transform.rotation, true);
                Transferring = false;
                yield break;
            }
            var body = ship.GetComponent<Rigidbody>();
            var playerBody = player != null ? player.GetComponent<Rigidbody>() : null;
            bool attached = player != null && player.IsAttached();
            if (body != null) body.isKinematic = true;
            Vector3 start = ship.transform.position, last = start;
            var startRot = ship.transform.rotation;
            float t0 = Time.time, spinUp = 3f;
            while (ship != null && Time.time - t0 < spinUp)
            {
                float t = (Time.time - t0) / spinUp;
                WorldWrap.Prewarm(dest);
                Vector3 p;
                Quaternion r;
                if (way == Way.Whirlpool)
                {
                    float turn = 400f * t * t;
                    var off = Quaternion.Euler(0, turn, 0) * (start - centre);
                    off.y = 0;
                    p = centre + off * (1 - 0.6f * t);
                    p.y = start.y - 6f * t * t;
                    r = Quaternion.Euler(0, turn, 0) * startRot * Quaternion.Euler(0, 0, 12f * t);
                }
                else
                {
                    p = start + ship.transform.forward * 12f * t;
                    p.y = start.y - 10f * t * t;
                    r = startRot * Quaternion.Euler(30f * t, 0, 0);
                }
                ship.transform.SetPositionAndRotation(p, r);
                if (body != null) body.position = p;
                if (!attached && player != null) WorldWrap.MoveBody(player.transform, playerBody, p - last);
                last = p;
                yield return null;
            }
            // the place below: made first (or the ship would land on nothing)
            while (ship != null && !WorldWrap.DestinationReady(dest) && Time.time - t0 < 20f)
            {
                WorldWrap.Prewarm(dest);
                yield return null;
            }
            if (ship == null) { Transferring = false; yield break; }
            float yaw = ship.transform.eulerAngles.y;
            var arrive = new Vector3(dest.x, way == Way.Whirlpool ? Britannia.SeaLevel - 4f : Britannia.SeaLevel + 10f, dest.z);
            var delta = arrive - ship.transform.position;
            ship.transform.rotation = Quaternion.Euler(0, yaw, 0);
            WorldWrap.MoveBody(ship.transform, body, delta);
            if (player != null) WorldWrap.MoveBody(player.transform, playerBody, delta);
            Plugin.Log.LogInfo($"Carried {(way == Way.Whirlpool ? "down a whirlpool" : "over the falls")}: ship and player by {delta} to {arrive}");
            // a frame for the passengers to see their ship jump, then it is free
            yield return null;
            while (ship != null && !WorldWrap.DestinationReady(arrive) && Time.time - t0 < 40f) yield return null;
            if (ship != null && body != null)
            {
                body.isKinematic = false;
                // (it springs back up out of the whirlpool)
                body.linearVelocity = way == Way.Whirlpool ? Vector3.up * 9f : Vector3.zero;
                VehicleDamage(ship);
            }
            Transferring = false;
        }

        /// <summary>vehicle_damage: a 1..30 roll off the hull's 99 (Valheim: that share of the ship's health).</summary>
        static void VehicleDamage(Ship ship)
        {
            var wnt = ship.GetComponent<WearNTear>();
            if (wnt == null) return;
            var hit = new HitData { m_point = ship.transform.position, m_dir = Vector3.down };
            hit.m_damage.m_blunt = wnt.m_health * random.Next(1, 31) / 99f;
            wnt.Damage(hit);
        }

        public static void Message(Player p, string text)
        {
            if (p != null && p == Player.m_localPlayer) p.Message(MessageHud.MessageType.Center, text);
        }

        // ── console ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// u5 under (devcommands): where the local player is (tile and continent); u5 under &lt;x&gt;
        /// &lt;y&gt; goes to that Underworld tile; u5 under surface &lt;x&gt; &lt;y&gt; to that surface tile;
        /// u5 under falls (to the river above the falls); u5 under whirlpool (one 40 m ahead);
        /// u5 under doom | ararat | &lt;dungeon&gt; (before that place down there).
        /// </summary>
        public static void Command(string[] args, Action<string> print)
        {
            var me = Player.m_localPlayer;
            if (me == null || Britannia.Under == null) { print("u5 under: only in a Britannia world"); return; }
            var (tx, ty, tz) = Britannia.Tile(me.transform.position);
            if (args.Length < 2) { print($"tile ({tx},{ty}) {(tz == 0 ? "Britannia" : "Underworld")}, world {me.transform.position}"); return; }
            if (!Terminal.m_cheat) { print("u5 under needs devcommands"); return; }
            string what = args[1].ToLowerInvariant();
            Vector3 to;
            if (what == "whirlpool")
            {
                var at = me.transform.position + Vector3.ProjectOnPlane(me.transform.forward, Vector3.up).normalized * 40f;
                print(Whirlpool.Spawn(at) != null ? $"a whirlpool at {at}" : "no whirlpool prefab");
                return;
            }
            if (what == "falls") to = Ground(UnderworldLinks.FallsX, UnderworldLinks.FallsY - 3, false);
            else if (what == "surface" && args.Length > 3 && int.TryParse(args[2], out int sx) && int.TryParse(args[3], out int sy)) to = Ground(sx, sy, false);
            else if (int.TryParse(args[1], out int ux) && args.Length > 2 && int.TryParse(args[2], out int uy)) to = Ground(ux, uy, true);
            else
            {
                var loc = Plugin.U5.FindLocation(args[1]);
                if (loc == null) { print("u5 under [<x> <y> | surface <x> <y> | falls | whirlpool | doom | ararat | <dungeon>]"); return; }
                to = Ground(loc.X, loc.Y, true) + new Vector3(0, 0, -24f);
                to.y = Britannia.SeaLevel + Mathf.Max(0, Britannia.Under.Height(to.x, to.z));
            }
            me.TeleportTo(to + Vector3.up * 1.5f, me.transform.rotation, true);
            print($"to {to}");
        }
    }

    /// <summary>
    /// The local player's turn in the Underworld and at its ways: earthquakes, the falls, a
    /// whirlpool meeting their ship.
    /// </summary>
    public class UnderworldWatch : MonoBehaviour
    {
        const float TurnEvery = 8f, FallsAgain = 15f;
        const byte Darkness = 0xff;
        float nextTurn, fallsAfter, sayAgain;
        /// <summary>The last place the local player stood outside the darkness.</summary>
        Vector3? lit;
        readonly System.Random random = new System.Random();

        static DataOvl Ovl => Plugin.U5.Ovl;

        void Update()
        {
            var me = Player.m_localPlayer;
            if (Britannia.Under == null || me == null || me.IsDead() || me.IsTeleporting() || Underworld.Transferring || ZoneSystem.instance == null) return;
            var at = me.transform.position;
            if (at.y > 1000f) return; // a dungeon or interior
            var ship = Ship.GetLocalShip();
            bool owner = ship != null && WorldWrap.IsOwner(ship);

            // a whirlpool meets my ship
            if (owner)
                foreach (var w in Whirlpool.All)
                {
                    if (w == null || !w.Valid) continue;
                    var d = w.transform.position - ship.transform.position;
                    d.y = 0;
                    if (d.magnitude > Whirlpool.Reach) continue;
                    Underworld.Message(me, Ovl.CStr(UnderworldLinks.SWhirlpool).Trim());
                    var centre = w.transform.position;
                    w.Vanish();
                    Plugin.Log.LogInfo($"{me.GetPlayerName()}'s ship meets a whirlpool at {centre}");
                    Underworld.Carry(me, ship, centre, Underworld.Ground(UnderworldLinks.WhirlpoolX, UnderworldLinks.WhirlpoolY, true), Underworld.Way.Whirlpool);
                    return;
                }

            // the falls: afloat on a waterfall tile, carried over it
            if (Time.time >= fallsAfter && (owner || (ship == null && me.IsSwimming())))
            {
                var mover = owner ? ship.transform.position : at;
                var (x, y, z) = Britannia.Tile(mover);
                var shape = Britannia.ShapeAt(mover);
                if (UnderworldLinks.IsWaterfall(shape.TileAt(mover.x, mover.z)))
                {
                    fallsAfter = Time.time + FallsAgain;
                    var (lx, ly, under) = UnderworldLinks.OverFalls(x, y - 1, z != 0);
                    string said = Ovl.CStr(UnderworldLinks.SFalls).Trim();
                    // every member whose dexterity is not above a 1..30 roll takes 1
                    if (AvatarStats.Mine.Dexterity <= random.Next(1, 31)) Dungeons.Hurt(me, 1);
                    if (under && z == 0) said += "\n" + Ovl.CStr(UnderworldLinks.SFallingInto).Trim();
                    Underworld.Message(me, said);
                    Plugin.Log.LogInfo($"{me.GetPlayerName()} goes over the falls at ({x},{y}) to ({lx},{ly}){(under && z == 0 ? " in the Underworld" : "")}");
                    Underworld.Carry(me, owner ? ship : null, mover, Underworld.Ground(lx, ly, under), Underworld.Way.Falls);
                    return;
                }
            }

            // the unending darkness round Doom (check_darkness_tile): only the Amulet of Lord British
            // lets one go on into it; without it the player is held where the light ends (one
            // brought into it by magic or a ladder may walk out)
            if (Britannia.InUnderworld(at) && Britannia.Under.TileAt(at.x, at.z) == Darkness && !WornEffects.Wears(me, WornEffects.AmuletPrefab))
            {
                if (lit.HasValue && Vector3.Distance(lit.Value, at) < 12f) Dungeons.Move(me, lit.Value);
                if (Time.time >= sayAgain) { Underworld.Message(me, "Unending darkness!\nThou canst see nothing."); sayAgain = Time.time + 4f; }
            }
            else lit = at;

            // an Underworld turn: 1 in 256, an earthquake
            if (Time.time < nextTurn) return;
            nextTurn = Time.time + TurnEvery;
            if (!Britannia.InUnderworld(at) || !UnderworldLinks.Earthquake(random)) return;
            Underworld.Message(me, Ovl.CStr(UnderworldLinks.SEarthquake).Trim());
            GameCamera.instance?.AddShake(at, 50f, 4f, false);
            Dungeons.Hurt(me, Dungeons.Roll8());
            Plugin.Log.LogInfo($"Earthquake under {me.GetPlayerName()}");
        }
    }
}
