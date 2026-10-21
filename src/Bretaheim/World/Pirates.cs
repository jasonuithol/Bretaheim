using System.Collections.Generic;
using System.Linq;
using Bretaheim.Data;
using Bretaheim.WorldGen.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Bretaheim.WorldGen
{
    /// <summary>
    /// Pirate ships (MASTER_PLAN §5.4; MAINOUT choose_spawn_monster tile 0x2c, ROOT run_encounter,
    /// SJOG end_combat): a random encounter at sea is sometimes a longship crewed by pirates, a
    /// crew of the original's size (1..9, creature type 1's stats) that sails at the nearest
    /// player and fights from its deck (no cannons: §5.11). With every one of its crew dead and a
    /// player near, the ship is captured: it becomes theirs, its hull mended (99).
    /// </summary>
    public static class Pirates
    {
        public const string CrewOfKey = "u5_crew_of";
        /// <summary>The creature type pirates fight as (run_encounter: a ship's slot fights type 1).</summary>
        public const int CrewType = 1;
        /// <summary>Fuling bodies for the crew (monsters.json "vehicles": Fulings; human pirates are a later body).</summary>
        static readonly string[] CrewBodies = { "GoblinArcher", "Goblin" };

        /// <summary>A pirate ship and its crew at a point at sea; what came, or null.</summary>
        public static string Spawn(Vector3 at, System.Random random)
        {
            var shape = Britannia.Active;
            if (shape == null || !Docks.Floats(shape, at.x, at.z)) return null;
            var go = Vessels.Spawn(true, at, random.Next(360), 0, "", 0);
            if (go == null) return null;
            var nview = go.GetComponent<ZNetView>();
            nview.GetZDO().Set(Vessels.PirateKey, true);
            go.GetComponent<VesselState>()?.Refresh();
            int count = Encounters.Count(UltimaCreatures.Stats(CrewType), random);
            long id = ShipId(nview.GetZDO());
            int n = 0;
            for (int i = 0; i < count; i++)
            {
                // along the deck, two abreast
                var local = new Vector3((i % 2 == 0 ? -1f : 1f) * 1.2f, 1.6f, -5f + (i / 2) * 2.5f);
                var c = UltimaCreatures.Spawn(CrewType, go.transform.TransformPoint(local), true, CrewBodies[random.Next(CrewBodies.Length)]);
                if (c == null) continue;
                c.GetComponent<ZNetView>().GetZDO().Set(CrewOfKey, id);
                n++;
            }
            nview.GetZDO().Set("u5_crew", n);
            Plugin.Log.LogInfo($"Pirates: a pirate ship with {n} aboard at ({at.x:0}, {at.z:0})");
            return $"a pirate ship with {n} pirates";
        }

        public static long ShipId(ZDO zdo) => zdo == null ? 0 : (long)zdo.m_uid.ID ^ zdo.m_uid.UserID << 20;

        /// <summary>The living crew of a ship, near it.</summary>
        public static List<Character> Crew(ZDO ship, Vector3 at, float range)
        {
            long id = ShipId(ship);
            return Character.GetAllCharacters().Where(c => c != null && !c.IsDead() && Vector3.Distance(c.transform.position, at) < range
                && (c.GetComponent<ZNetView>()?.GetZDO()?.GetLong(CrewOfKey) ?? 0) == id).ToList();
        }
    }

    /// <summary>A pirate-held ship: sails at the nearest player, is captured when its crew are dead, or sails off (gone) with nobody near.</summary>
    public class PirateShip : MonoBehaviour
    {
        const float Hunt = 160f, Keep = 18f, Speed = 4.5f, Turn = 25f, CaptureRange = 40f, Gone = 260f, GoneAfter = 30f;
        ZNetView nview;
        Rigidbody body;
        float alone, check;
        bool crewSeen;

        void Awake()
        {
            nview = GetComponent<ZNetView>();
            body = GetComponent<Rigidbody>();
        }

        /// <summary>Called after the ship's own physics step (its owner's): toward the nearest player, over water only.</summary>
        public void Sail(float dt)
        {
            if (nview == null || !nview.IsValid() || !nview.IsOwner() || body == null || !nview.GetZDO().GetBool(Vessels.PirateKey)) return;
            var target = Player.GetClosestPlayer(transform.position, Hunt);
            if (target == null) return;
            var to = target.transform.position - transform.position;
            to.y = 0;
            float dist = to.magnitude;
            if (dist < 0.1f) return;
            var dir = to / dist;
            var rot = Quaternion.RotateTowards(body.rotation, Quaternion.LookRotation(dir, Vector3.up), Turn * dt);
            body.MoveRotation(Quaternion.Euler(body.rotation.eulerAngles.x, rot.eulerAngles.y, body.rotation.eulerAngles.z));
            var shape = Britannia.Active;
            var ahead = transform.position + transform.forward * 14f;
            bool water = shape == null || shape.Height(ahead.x, ahead.z) < -Docks.Depth;
            var v = body.linearVelocity;
            var want = dist > Keep && water ? transform.forward * Speed : Vector3.zero;
            body.linearVelocity = new Vector3(want.x, v.y, want.z);
        }

        /// <summary>The ship is theirs: no longer the pirates', its hull mended (99).</summary>
        public void Capture(long player, string name)
        {
            var zdo = nview.GetZDO();
            zdo.Set(Vessels.PirateKey, false);
            zdo.Set(Vessels.OwnerKey, player);
            zdo.Set(Vessels.OwnerNameKey, name);
            var wnt = GetComponent<WearNTear>();
            if (wnt != null) zdo.Set(ZDOVars.s_health, wnt.m_health * Shops.NewHull / 99f);
            MessageHud.instance?.MessageAll(MessageHud.MessageType.Center, "The pirate ship is captured!");
            Plugin.Log.LogInfo($"Pirates: {name} captured a pirate ship at {transform.position}");
            GetComponent<VesselState>()?.Refresh();
        }

        void Update()
        {
            if (nview == null || !nview.IsValid() || !nview.IsOwner() || Time.time < check) return;
            check = Time.time + 2f;
            var zdo = nview.GetZDO();
            // (captured while another peer owned it)
            if (!zdo.GetBool(Vessels.PirateKey)) { Destroy(this); return; }
            var crew = Pirates.Crew(zdo, transform.position, CaptureRange);
            if (crew.Count > 0) crewSeen = true;
            var near = Player.GetClosestPlayer(transform.position, CaptureRange);
            // every pirate dead (SJOG end_combat: g_no_more_enemies on a ship's slot): the ship is theirs
            if (crew.Count == 0 && (crewSeen || zdo.GetInt("u5_crew") == 0) && near != null)
            {
                Capture(near.GetPlayerID(), near.GetPlayerName());
                return;
            }
            // a random encounter: gone when nobody has been near for a while
            alone = Player.IsPlayerInRange(transform.position, Gone) ? 0 : alone + 2f;
            if (alone > GoneAfter) ZNetScene.instance.Destroy(gameObject);
        }
    }
}
