using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Bretaheim.Data;
using Bretaheim.WorldGen.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Bretaheim.WorldGen
{
    /// <summary>
    /// Britannia's creatures fight on Ultima's numbers (MASTER_PLAN §5.4/§5.6): every monster and
    /// every townsperson has its creature type's stat line (DATA.OVL), its hit points are the
    /// type's and its blows the type's damage less a roll of the defender's armour (COMBAT.OVL
    /// roll_attack_damage), whatever body or weapon it has; all on one scale
    /// (<see cref="Ultima.Scale"/>). On the Britannia surface Valheim's hostile spawns and raids
    /// give way to the original's random encounters (<see cref="UltimaSpawner"/>); its harmless
    /// animals stay.
    /// </summary>
    public static class UltimaCreatures
    {
        /// <summary>ZDO keys: creature type + 1 (0 = not an Ultima creature), a wild encounter (despawns far from players), hit points set.</summary>
        public const string TypeKey = "u5_type", WildKey = "u5_wild";
        public const string MonsterGroup = "u5_monsters";

        static readonly Dictionary<Character, CreatureStats> stats = new Dictionary<Character, CreatureStats>();
        static readonly Dictionary<int, CreatureStats> byType = new Dictionary<int, CreatureStats>();
        static readonly System.Random random = new System.Random();
        static Dictionary<int, (string prefab, float scale)> bodies;

        public static CreatureStats Stats(int type)
        {
            if (!byType.TryGetValue(type, out var s)) byType[type] = s = CreatureStats.Of(Plugin.U5.Ovl, type);
            return s;
        }

        public static void Register(Character c, int type) { if (c != null) stats[c] = Stats(type); }
        public static void Unregister(Character c) { if (c != null) stats.Remove(c); }
        public static CreatureStats Of(Character c) => c != null && stats.TryGetValue(c, out var s) ? s : null;

        /// <summary>monsters.json: the Valheim body of each creature type (and its size).</summary>
        public static (string prefab, float scale)? Body(int type)
        {
            if (bodies == null)
            {
                bodies = new Dictionary<int, (string, float)>();
                try
                {
                    var root = Json.Obj(Json.Parse(File.ReadAllText(Path.Combine(Plugin.PluginDirectory, "mappings", "monsters.json"))));
                    foreach (var e in Json.Arr(root["creatures"]))
                    {
                        var o = Json.Obj(e);
                        var prefab = Json.Str(o, "prefab");
                        if (prefab != null) bodies[(int)Json.Num(o, "type")] = (prefab, (float)Json.Num(o, "scale", 1));
                    }
                }
                catch (Exception e) { Plugin.Log.LogError($"monsters.json: {e.Message}"); }
            }
            return bodies.TryGetValue(type, out var b) ? b : ((string, float)?)null;
        }

        /// <summary>The first creature type with this Valheim body, or −1.</summary>
        public static int TypeOfBody(string prefab)
        {
            Body(0);
            foreach (var kv in bodies.OrderBy(kv => kv.Key)) if (kv.Value.prefab == prefab) return kv.Key;
            // Valheim's own troll is the ettin it is made into
            if (prefab == "Troll") return TypeOfBody(Ettin.PrefabName);
            return -1;
        }

        public static void Install(Harmony harmony)
        {
            Patch(harmony, typeof(Character), "Awake", postfix: nameof(Awake_Postfix));
            Patch(harmony, typeof(Character), "OnDestroy", prefix: nameof(OnDestroy_Prefix));
            Patch(harmony, typeof(Character), "SetupMaxHealth", prefix: nameof(SetupMaxHealth_Prefix));
            Patch(harmony, typeof(Character), "RPC_Damage", prefix: nameof(RPC_Damage_Prefix));
            Patch(harmony, typeof(SpawnSystem), "Spawn", prefix: nameof(Spawn_Prefix));
            Patch(harmony, typeof(RandEventSystem), "UpdateRandomEvent", prefix: nameof(UpdateRandomEvent_Prefix));
        }

        static void Patch(Harmony h, Type t, string m, string prefix = null, string postfix = null)
        {
            var method = AccessTools.Method(t, m);
            if (method == null) { Plugin.Log.LogWarning($"Ultima creatures: {t.Name}.{m} not found"); return; }
            h.Patch(method, prefix: prefix != null ? new HarmonyMethod(typeof(UltimaCreatures), prefix) : null,
                postfix: postfix != null ? new HarmonyMethod(typeof(UltimaCreatures), postfix) : null);
        }

        /// <summary>An Ultima monster loaded on any peer: its type's stats, the monsters' group (they never fight each other), its size.</summary>
        static void Awake_Postfix(Character __instance)
        {
            var zdo = __instance.GetComponent<ZNetView>()?.GetZDO();
            if (zdo == null) return;
            int type = zdo.GetInt(TypeKey) - 1;
            if (type < 0)
            {
                // any other monster in Britannia (a dev command's, a Valheim spawner's) whose body
                // is an Ultima creature's is that creature: the first type with that body
                if (Britannia.Active == null || __instance is Player || __instance.GetComponent<TownNpc>() != null) return;
                type = TypeOfBody(Utils.GetPrefabName(__instance.gameObject.name));
                if (type < 0) return;
                if (zdo.IsOwner())
                {
                    zdo.Set(TypeKey, type + 1);
                    SetHitPoints(__instance, Stats(type));
                }
            }
            Register(__instance, type);
            __instance.m_group = MonsterGroup;
            var ai = __instance.GetComponent<MonsterAI>();
            if (ai != null)
            {
                // monster_morale: a hurt monster runs (a quarter of its hit points left); they come for the party
                ai.m_fleeIfLowHealth = 0.25f;
                ai.m_enableHuntPlayer = true;
                // Ultima's monsters go for people, not walls
                ai.m_attackPlayerObjects = false;
            }
            var body = Body(type);
            if (body != null) __instance.transform.localScale = Vector3.one * body.Value.scale;
            if (zdo.GetBool(WildKey) && __instance.GetComponent<WildEncounter>() == null) __instance.gameObject.AddComponent<WildEncounter>();
        }

        static void OnDestroy_Prefix(Character __instance) => Unregister(__instance);

        /// <summary>An Ultima creature's hit points are its type's at any Valheim level (Valheim sets them up on load and on SetLevel).</summary>
        static bool SetupMaxHealth_Prefix(Character __instance)
        {
            int type = __instance.GetComponent<ZNetView>()?.GetZDO()?.GetInt(TypeKey) - 1 ?? -1;
            if (type < 0) return true;
            SetHitPoints(__instance, Stats(type));
            return false;
        }

        /// <summary>A creature of an Ultima type in its Valheim body, with the type's hit points (a wild one despawns far from players).</summary>
        public static Character Spawn(int type, Vector3 at, bool wild, string bodyPrefab = null)
        {
            var body = Body(type);
            var prefab = bodyPrefab != null ? ZNetScene.instance.GetPrefab(bodyPrefab) : body != null ? ZNetScene.instance.GetPrefab(body.Value.prefab) : null;
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, at, Quaternion.Euler(0, random.Next(360), 0));
            var zdo = go.GetComponent<ZNetView>()?.GetZDO();
            var c = go.GetComponent<Character>();
            if (zdo == null || c == null) { Object.Destroy(go); return null; }
            zdo.Set(TypeKey, type + 1);
            if (wild) zdo.Set(WildKey, true);
            // (Awake ran before the keys were set: do now what it would have done)
            Awake_Postfix(c);
            c.SetLevel(1);
            SetHitPoints(c, Stats(type));
            return c;
        }

        /// <summary>Ultima hit points, as many as the type has (on the scale), set once by whoever made it.</summary>
        public static void SetHitPoints(Character c, CreatureStats s)
        {
            float hp = s.HitPoints * Ultima.Scale;
            if (c.GetMaxHealth() == hp) return;
            c.SetMaxHealth(hp);
            c.SetHealth(hp);
        }

        /// <summary>
        /// A blow on the defender's owner, before Valheim's resistances and armour, between any two
        /// of Ultima's (creatures, townsfolk, players in Britannia): the attacker's damage (a
        /// creature's type's, a player's roll of their weapon's attack value) less a roll of the
        /// defender's armour (a type's, a player's worn defence); the undead take half (COMBAT.OVL
        /// roll_attack_damage, apply_damage). Delivered untyped, so Valheim's resistances and armour
        /// add nothing. A player hit by Valheim's own animals, or hitting them, stays Valheim's.
        /// </summary>
        static void RPC_Damage_Prefix(Character __instance, HitData hit)
        {
            var defender = Of(__instance);
            var attackerBody = hit.GetAttacker();
            var attacker = Of(attackerBody);
            var player = __instance as Player;
            var striker = attackerBody as Player;
            // Ultima numbers on both sides: Ultima creatures, and players in Britannia (their
            // weapon's attack value, their worn armour); Valheim's own animals stay Valheim's
            bool ultimaDefender = defender != null || (player != null && Britannia.Active != null);
            bool ultimaAttacker = attacker != null || (striker != null && Britannia.Active != null);
            if (!ultimaDefender || !ultimaAttacker) return;
            // a ballista's bolt is the cannon's, whoever fired it (Ballistas.BoltDamage); a spell's is the spell's (SpellEffects)
            if (hit.m_hitType == HitData.HitType.Turret || hit.m_skill == SpellEffects.SpellSkill) return;
            float poison = hit.m_damage.m_poison;
            int damage = attacker != null ? attacker.Damage : AvatarStats.WeaponAttack(striker);
            int armour = defender != null ? defender.Armour : AvatarStats.Defence(player);
            float blow = Ultima.Damage(damage, armour, defender?.Undead ?? false, false, random) * Ultima.Scale;
            // untyped: past Valheim's resistances and armour (the Ultima armour is in the blow)
            hit.m_damage = new HitData.DamageTypes();
            hit.m_damage.m_damage = blow;
            // a poisonous monster's bite poisons
            if (attacker != null && attacker.Poisonous && blow > 0) hit.m_damage.m_poison = Mathf.Max(poison, Ultima.Scale);
        }

        /// <summary>On the Britannia surface only Valheim's harmless animals spawn; the monsters are Ultima's.</summary>
        static bool Spawn_Prefix(SpawnSystem.SpawnData critter)
        {
            if (Britannia.Active == null || critter?.m_prefab == null) return true;
            var c = critter.m_prefab.GetComponent<Character>();
            return c == null || c.m_faction == Character.Faction.AnimalsVeg;
        }

        /// <summary>No raids in Britannia.</summary>
        static bool UpdateRandomEvent_Prefix() => Britannia.Active == null;
    }

    /// <summary>A random encounter: gone when no player has been near for a while (the original clears monsters that drift out of the party's window).</summary>
    public class WildEncounter : MonoBehaviour
    {
        const float Range = 220f, After = 20f;
        ZNetView nview;
        float alone;

        void Awake() => nview = GetComponent<ZNetView>();

        void Update()
        {
            if (nview == null || nview.GetZDO() == null || !nview.IsOwner()) return;
            alone = Player.IsPlayerInRange(transform.position, Range) ? 0 : alone + Time.deltaTime;
            if (alone > After) ZNetScene.instance.Destroy(gameObject);
        }
    }

    /// <summary>
    /// The original's random encounters around the local player (MAINOUT.OVL
    /// update_overworld_monsters): one overworld turn for every tile travelled (32 m), or every
    /// few seconds standing; a roll of 1..30 under the terrain's rate brings a monster of the original's tables
    /// for the terrain where it appears, a group of 1..its group size, out of sight (scaled down
    /// from the original's 7+ tiles to stay in the loaded world), hunting the nearest player as the
    /// original's close on the party. None in or by the settlements.
    /// </summary>
    public class UltimaSpawner : MonoBehaviour
    {
        const float TurnEvery = 8f, NearFrom = 50f, NearTo = 90f, CapRange = 150f;
        const int Cap = 8;
        float next;
        Vector3? lastTurnAt;
        readonly System.Random random = new System.Random();

        void Update()
        {
            var me = Player.m_localPlayer;
            if (Britannia.Active == null || me == null || me.IsDead() || ZoneSystem.instance == null || ZNetScene.instance == null) return;
            var at = me.transform.position;
            // a turn for every tile travelled (the original's turn is a step), or every few seconds standing
            float tile = Britannia.ShapeAt(at).TileMetres;
            bool moved = lastTurnAt.HasValue && new Vector2(at.x - lastTurnAt.Value.x, at.z - lastTurnAt.Value.z).magnitude >= tile;
            if (!moved && Time.time < next) return;
            next = Time.time + TurnEvery;
            lastTurnAt = at;
            var shape = Britannia.ShapeAt(at);
            if (InTown(shape, at.x, at.z)) return;
            if (!Encounters.Roll(shape.TileAt(at.x, at.z), TownNpc.Hour(), random, shape == Britannia.Under)) return;
            Encounter(at, me.GetPlayerName(), random);
        }

        /// <summary>One encounter near a point (the turn's roll already made): what came, or null.</summary>
        public static string Encounter(Vector3 at, string who, System.Random random) => Encounter(at, who, random, NearFrom, NearTo);

        /// <summary>One encounter between these distances from a point (an ambush comes close).</summary>
        public static string Encounter(Vector3 at, string who, System.Random random, float nearFrom, float nearTo)
        {
            var shape = Britannia.ShapeAt(at);
            bool under = shape == Britannia.Under;
            int near = 0;
            foreach (var c in Character.GetAllCharacters())
                if (c.GetComponent<WildEncounter>() != null && Vector3.Distance(c.transform.position, at) < CapRange) near++;
            if (near >= Cap) return null;
            for (int tries = 0; tries < 16; tries++)
            {
                float angle = (float)(random.NextDouble() * Math.PI * 2), dist = nearFrom + (float)random.NextDouble() * (nearTo - nearFrom);
                var p = at + new Vector3(Mathf.Cos(angle) * dist, 0, Mathf.Sin(angle) * dist);
                if (InTown(shape, p.x, p.z) || !ZoneSystem.instance.IsZoneLoaded(p)) continue;
                byte tile = shape.TileAt(p.x, p.z);
                int monster = Encounters.Choose(Plugin.U5.Ovl, tile, random, under);
                if (monster == Encounters.Whirlpool)
                {
                    // (a whirlpool on the deep sea: Whirlpool drifts it and takes ships to the Underworld)
                    if (ZoneSystem.instance.GetGroundHeight(p) > Britannia.SeaLevel - 4f || Whirlpool.Spawn(p) == null) continue;
                    string w = $"a whirlpool on tile {tile:x2}, {dist:0} m from {who}";
                    Plugin.Log.LogInfo("Encounter: " + w);
                    return w;
                }
                // a pirate ship (never on the 0x60..0x6f water tiles: spawn_random_monster)
                if (monster == Encounters.PirateShip)
                {
                    var pirates = (tile & 0xf0) == 0x60 ? null : Pirates.Spawn(p, random);
                    if (pirates == null) continue;
                    Plugin.Log.LogInfo($"Encounter: {pirates} on tile {tile:x2}, {dist:0} m from {who}");
                    return pirates;
                }
                int type = monster == 0 ? -1 : Encounters.TypeOf(monster);
                var body = type < 0 ? null : UltimaCreatures.Body(type);
                var prefab = body != null ? ZNetScene.instance.GetPrefab(body.Value.prefab) : null;
                if (prefab == null) continue;
                bool sea = tile < 4 || (tile > 0x5f && tile < 0x70);
                float ground = ZoneSystem.instance.GetGroundHeight(p);
                float water = Britannia.SeaLevel;
                if (!sea && ground < water + 0.3f) continue;
                p.y = sea ? water - 1f : ground + 0.2f;
                int count = Encounters.Count(UltimaCreatures.Stats(type), random);
                for (int i = 0; i < count; i++)
                {
                    var q = p + new Vector3((float)random.NextDouble() * 4 - 2, 0, (float)random.NextDouble() * 4 - 2);
                    if (!sea) q.y = ZoneSystem.instance.GetGroundHeight(q) + 0.2f;
                    // the original's monsters close on the party (monster_turn): Valheim's raid hunting
                    UltimaCreatures.Spawn(type, q, true)?.GetBaseAI()?.SetHuntPlayer(true);
                }
                var said = $"{count} {Plugin.U5.Ovl.CreatureName(type)} ({body.Value.prefab}) on tile {tile:x2}, {dist:0} m from {who}";
                Plugin.Log.LogInfo("Encounter: " + said);
                return said;
            }
            return null;
        }

        static bool InTown(BritanniaShape shape, float x, float z)
        {
            foreach (var site in shape.Sites)
                if (Mathf.Max(Mathf.Abs(x - site.CentreX), Mathf.Abs(z - site.CentreZ)) < site.Half + site.Blend + 20f) return true;
            return false;
        }
    }
}
