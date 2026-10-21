using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// Horses (the transport mapping: a Lox for now, a hornless Eikthyr later): bought at the
    /// stables (SHOPPES horse_shop), led out tame and saddled beside the buyer, ridden with
    /// Valheim's saddle controls (E mounts, the usual keys dismount). On horseback the party
    /// moves twice for the monsters' once (MAINOUT update_overworld_monsters skips every other
    /// turn), so a horse goes twice a player's pace; it will not enter swamp (can_enter_tile,
    /// class 3). Merchants other than the stables send riders away.
    /// </summary>
    public static class Horses
    {
        public const string PrefabName = "u5_horse";
        const byte Swamp = 0x04, NoHorses = 0x8f;
        const float Search = 40f;

        public static void Register() => PrefabManager.OnVanillaPrefabsAvailable += Create;

        static void Create()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Create;
            var prefab = PrefabManager.Instance.CreateClonedPrefab(PrefabName, "Lox");
            var c = prefab != null ? prefab.GetComponent<Character>() : null;
            var tame = prefab != null ? prefab.GetComponent<Tameable>() : null;
            if (c == null || tame == null) { Plugin.Log.LogError("Horses: no Lox to make a horse of"); return; }
            c.m_name = "Horse";
            tame.m_startsTamed = true;
            tame.m_randomStartingName.Clear();
            // a horse is bought, not bred
            foreach (var p in prefab.GetComponents<Procreation>()) Object.DestroyImmediate(p);
            // twice a player's pace (walking, jogging, running)
            var player = PrefabManager.Instance.GetPrefab("Player")?.GetComponent<Player>();
            if (player != null)
            {
                c.m_walkSpeed = 2 * player.m_walkSpeed;
                c.m_speed = 2 * player.m_speed;
                c.m_runSpeed = 2 * player.m_runSpeed;
            }
            prefab.AddComponent<HorseTack>();
            PrefabManager.Instance.AddPrefab(new CustomPrefab(prefab, false));
        }

        public static void Install(Harmony harmony)
        {
            var ride = AccessTools.Method(typeof(Sadle), "UpdateRiding");
            if (ride != null) harmony.Patch(ride, prefix: new HarmonyMethod(typeof(Horses), nameof(UpdateRiding_Prefix)));
            else Plugin.Log.LogWarning("Horses: Sadle.UpdateRiding not found");
        }

        public static bool Riding(Player p) => p != null && p.IsRiding();

        /// <summary>A horse led out beside the buyer, on open ground (not in a building, not in water); false when there is no room ("The stables are closed.").</summary>
        public static bool Buy(Player buyer, out string where)
        {
            where = "";
            var prefab = ZNetScene.instance?.GetPrefab(PrefabName);
            if (prefab == null || !FreeSpot(buyer.transform.position, out var at)) return false;
            var go = Object.Instantiate(prefab, at, Quaternion.Euler(0, buyer.transform.eulerAngles.y, 0));
            var zdo = go.GetComponent<ZNetView>()?.GetZDO();
            if (zdo != null) zdo.Set(ZDOVars.s_tamedName, buyer.GetPlayerName() + "'s horse");
            float d = Vector3.Distance(buyer.transform.position, at);
            where = d < 4 ? "beside thee" : $"outside, {d:0} m away";
            Plugin.Log.LogInfo($"Horses: {buyer.GetPlayerName()} bought a horse, waiting at {at}");
            return true;
        }

        /// <summary>The nearest spot within <see cref="Search"/> m with room for a horse: no piece within its body, dry ground.</summary>
        public static bool FreeSpot(Vector3 from, out Vector3 at)
        {
            float water = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : Britannia.SeaLevel;
            for (float r = 2f; r <= Search; r += 2f)
                for (int i = 0, n = Mathf.Max(8, (int)(r * 3)); i < n; i++)
                {
                    float a = i * Mathf.PI * 2 / n;
                    var p = from + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                    if (ZoneSystem.instance == null || !ZoneSystem.instance.GetGroundHeight(p, out float ground)) continue;
                    if (ground < water + 0.3f) continue;
                    p.y = ground;
                    if (Physics.CheckCapsule(p + Vector3.up * 0.8f, p + Vector3.up * 2.6f, 1.4f, Mask, QueryTriggerInteraction.Ignore)) continue;
                    at = p + Vector3.up * 0.2f;
                    return true;
                }
            at = from;
            return false;
        }

        static int mask;
        static int Mask => mask != 0 ? mask : (mask = LayerMask.GetMask("Default", "static_solid", "piece", "Default_small", "vehicle"));

        /// <summary>Into swamp a horse will not go (nor onto tile 0x8f): it stops at the edge.</summary>
        static bool UpdateRiding_Prefix(Sadle __instance, ref bool __result)
        {
            var shape = Britannia.Active;
            var c = __instance.GetComponentInParent<Character>();
            if (shape == null || c == null || Utils.GetPrefabName(c.gameObject.name) != PrefabName) return true;
            var dir = Traverse.Create(__instance).Field("m_controlDir").GetValue<Vector3>();
            if (dir.sqrMagnitude < 0.01f) return true;
            var ahead = c.transform.position + dir.normalized * 2.5f;
            byte tile = shape.TileAt(ahead.x, ahead.z);
            if (tile != Swamp && tile != NoHorses) return true;
            c.GetComponent<MonsterAI>()?.StopMoving();
            __result = false;
            return false;
        }

        // ── drunk (SHOPPES2 order_drink: g_drunk_moves) ─────────────────────────────────────

        static int drunk;

        /// <summary>Drunk for this many moves: the drinker staggers now and then.</summary>
        public static void Drunk(int moves) => drunk = moves;

        public static int DrunkMoves => drunk;

        /// <summary>One move (a few metres walked) while drunk: a stagger one time in three.</summary>
        public static void DrunkStep(Player me, System.Random rnd)
        {
            if (drunk <= 0 || me == null) return;
            drunk--;
            if (rnd.Next(3) == 0)
            {
                float a = (float)(rnd.NextDouble() * Mathf.PI * 2);
                me.Stagger(new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)));
            }
            if (drunk == 0) me.Message(MessageHud.MessageType.TopLeft, "Thou art sober again.");
        }
    }

    /// <summary>A bought horse comes saddled (the saddle is set by whoever owns it, once).</summary>
    public class HorseTack : MonoBehaviour
    {
        void Start()
        {
            var nview = GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid() || !nview.IsOwner() || nview.GetZDO().GetBool(ZDOVars.s_haveSaddleHash)) return;
            nview.GetZDO().Set(ZDOVars.s_haveSaddleHash, true);
            nview.InvokeRPC(ZNetView.Everybody, "SetSaddle", true);
        }
    }

    /// <summary>Per-player upkeep of the transport: bought vessels put in the water once their dock is loaded, a shipwright visit ended on leaving the town, a drunk staggering as they walk.</summary>
    public class TransportWatch : MonoBehaviour
    {
        const float MoveMetres = 3f;
        readonly System.Random random = new System.Random();
        Vector3 last;
        float walked, checkAt;

        void Update()
        {
            var me = Player.m_localPlayer;
            if (me == null || me.IsDead()) return;
            var at = me.transform.position;
            if (Horses.DrunkMoves > 0)
            {
                walked += Vector3.Distance(new Vector3(at.x, 0, at.z), new Vector3(last.x, 0, last.z));
                if (walked > 50f) walked = 0; // (a teleport, not a walk)
                while (walked >= MoveMetres) { walked -= MoveMetres; Horses.DrunkStep(me, random); }
            }
            last = at;
            if (Time.time < checkAt) return;
            checkAt = Time.time + 2f;
            Vessels.Forget(at);
            if (Britannia.Active != null) Vessels.DeliverDue(me);
        }
    }
}
