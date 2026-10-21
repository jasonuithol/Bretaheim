using System;
using System.Linq;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace Bretaheim.WorldGen
{
    /// <summary>
    /// The settlements' cannons are Valheim ballistas (MASTER_PLAN §5.3 Fire). One that sees a
    /// monster is manned: a guard appears behind it and it shoots explosive bolts until the monster
    /// is dead or gone, then the guard is gone again. A player can fire one too (E): as the
    /// original's Fire command, that raises the town's alarm (CMDS cmd_fire, TOWN raise_alarm) and
    /// the guards come to arrest them (<see cref="Crime"/>).
    /// </summary>
    public static class Ballistas
    {
        public const string BoltName = "u5_explosive_bolt", ProjectileName = "u5_explosive_bolt_projectile";
        /// <summary>A bolt's blow (untyped, past armour and resistances): a cannonball removes nearly anything it hits.</summary>
        public const float BoltDamage = 40 * Data.Ultima.Scale;
        const float BlastRadius = 4f;

        /// <summary>Who fires the shot being made (a player at the ballista, else its crew).</summary>
        internal static Character firing;

        public static void Register() => PrefabManager.OnVanillaPrefabsAvailable += Create;

        /// <summary>The explosive bolt: the black-metal ballista bolt, bursting where it lands (the siege bomb's blast).</summary>
        static void Create()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Create;
            var proj = PrefabManager.Instance.CreateClonedPrefab(ProjectileName, "Turret_projectile");
            var bolt = PrefabManager.Instance.CreateClonedPrefab(BoltName, "TurretBolt");
            var blast = PrefabManager.Instance.GetPrefab("fx_siegebomb_explosion");
            if (proj == null || bolt == null)
            {
                Plugin.Log.LogError("Ballistas: no Turret_projectile/TurretBolt to make explosive bolts from");
                return;
            }
            var p = proj.GetComponent<Projectile>();
            p.m_aoe = BlastRadius;
            p.m_respawnItemOnHit = false;
            if (blast != null)
                p.m_hitEffects.m_effectPrefabs = (p.m_hitEffects.m_effectPrefabs ?? new EffectList.EffectData[0])
                    .Concat(new[] { new EffectList.EffectData { m_prefab = blast, m_enabled = true } }).ToArray();
            var shared = bolt.GetComponent<ItemDrop>().m_itemData.m_shared;
            shared.m_name = "Explosive bolt";
            shared.m_attack.m_attackProjectile = proj;
            shared.m_damages = new HitData.DamageTypes { m_damage = BoltDamage };
            ItemManager.Instance.AddItem(new CustomItem(bolt, false));
        }

        public static void Install(Harmony harmony)
        {
            Patch(harmony, "UpdateTarget", prefix: nameof(UpdateTarget_Prefix));
            Patch(harmony, "Interact", prefix: nameof(Interact_Prefix));
            Patch(harmony, "UseItem", prefix: nameof(UseItem_Prefix));
            Patch(harmony, "GetHoverText", prefix: nameof(GetHoverText_Prefix));
            Patch(harmony, "ShootProjectile", postfix: nameof(ShootProjectile_Postfix));
        }

        static void Patch(Harmony h, string m, string prefix = null, string postfix = null)
        {
            var method = AccessTools.Method(typeof(Turret), m);
            if (method == null) { Plugin.Log.LogWarning($"Ballistas: Turret.{m} not found"); return; }
            h.Patch(method, prefix: prefix != null ? new HarmonyMethod(typeof(Ballistas), prefix) : null,
                postfix: postfix != null ? new HarmonyMethod(typeof(Ballistas), postfix) : null);
        }

        /// <summary>A town ballista picks its own targets: monsters, and only while it is manned.</summary>
        static bool UpdateTarget_Prefix(Turret __instance)
        {
            var b = __instance.GetComponent<TownBallista>();
            if (b == null) return true;
            b.Aim();
            return false;
        }

        /// <summary>E on a town ballista fires it (and is a crime).</summary>
        static bool Interact_Prefix(Turret __instance, Humanoid character, bool hold, ref bool __result)
        {
            var b = __instance.GetComponent<TownBallista>();
            if (b == null) return true;
            __result = !hold && character is Player player && b.PlayerFires(player);
            return false;
        }

        /// <summary>A town ballista takes no ammunition from anyone: it has its own.</summary>
        static bool UseItem_Prefix(Turret __instance, ref bool __result)
        {
            if (__instance.GetComponent<TownBallista>() == null) return true;
            __result = false;
            return false;
        }

        static bool GetHoverText_Prefix(Turret __instance, ref string __result)
        {
            if (__instance.GetComponent<TownBallista>() == null) return true;
            __result = Localization.instance.Localize("Ballista\n[<color=yellow><b>$KEY_Use</b></color>] Fire");
            return false;
        }

        /// <summary>The bolt is the firer's: the crew's bolts spare townsfolk and players, a player's spare townsfolk.</summary>
        static void ShootProjectile_Postfix(Turret __instance)
        {
            var b = __instance.GetComponent<TownBallista>();
            if (b == null) return;
            var shot = Traverse.Create(__instance).Field("m_lastProjectile").GetValue<GameObject>();
            var proj = shot != null ? shot.GetComponent<Projectile>() : null;
            var owner = firing ?? b.CrewBody;
            if (proj != null && owner != null) Traverse.Create(proj).Field("m_owner").SetValue(owner);
        }
    }

    /// <summary>A settlement's ballista (on every peer; its owner aims, mans and fires it).</summary>
    public class TownBallista : MonoBehaviour
    {
        const float Range = 50f, Arc = 60f, ManFor = 15f, CheckEvery = 0.5f, CrewBehind = 1.6f;
        static readonly int SightMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");

        Turret turret;
        ZNetView nview;
        Settlements.Built town;
        TownNpc crew;
        Character target;
        float checkAt, seenAt = -100f;

        public Character CrewBody => crew != null ? crew.GetComponent<Character>() : null;
        bool Manned => crew != null && !crew.IsDead && Vector3.Distance(crew.transform.position, CrewSpot) < 2.5f;
        Vector3 CrewSpot => transform.position - transform.forward * CrewBehind + Vector3.up * 0.1f;

        void Awake()
        {
            turret = GetComponent<Turret>();
            nview = GetComponent<ZNetView>();
            // its own explosive bolts, never running out; a wide look over the walls
            var ammo = ZNetScene.instance?.GetPrefab(Ballistas.BoltName)?.GetComponent<ItemDrop>();
            if (ammo != null)
            {
                turret.m_defaultAmmo = ammo;
                // (the black-metal bolt's model shows loaded)
                var shown = turret.m_allowedAmmo.FirstOrDefault(a => a.m_ammo != null && a.m_ammo.name == "TurretBolt");
                if (shown.m_visual != null) turret.m_allowedAmmo.Add(new Turret.AmmoType { m_ammo = ammo, m_visual = shown.m_visual });
            }
            turret.m_maxAmmo = 0;
            turret.m_viewDistance = Range;
            turret.m_horizontalAngle = Arc;
            turret.m_targetPlayers = false;
            turret.m_targetTamed = false;
        }

        Settlements.Built Town
        {
            get
            {
                if (town != null) return town;
                var p = transform.position;
                return town = Settlements.All.FirstOrDefault(b => Mathf.Max(Mathf.Abs(p.x - b.Origin.x), Mathf.Abs(p.z - b.Origin.z)) < b.Site.Half + 1f);
            }
        }

        void Update()
        {
            if (nview == null || nview.GetZDO() == null || !nview.IsOwner() || Time.time < checkAt) return;
            checkAt = Time.time + CheckEvery;
            target = Spot();
            if (target != null)
            {
                seenAt = Time.time;
                if ((crew == null || crew.IsDead) && Town != null)
                {
                    crew = TownsfolkSpawner.SpawnCrew(Town, CrewSpot, transform.rotation.eulerAngles.y, this);
                    if (crew != null) Plugin.Log.LogInfo($"{Town.Location.Name}: a guard mans the ballista against {target.m_name}");
                }
            }
            else if (crew != null && Time.time - seenAt > ManFor) Dismiss();
        }

        void OnDestroy() => Dismiss();

        void Dismiss()
        {
            if (crew != null && crew.GetComponent<ZNetView>()?.GetZDO() != null && crew.IsOwned) ZNetScene.instance.Destroy(crew.gameObject);
            crew = null;
        }

        /// <summary>The nearest monster in its arc and range that it can see.</summary>
        Character Spot()
        {
            var eye = turret.m_eye != null ? turret.m_eye.transform.position : transform.position + Vector3.up * 1.5f;
            Character best = null;
            float bestD = Range;
            foreach (var c in Character.GetAllCharacters())
            {
                if (c == null || c.IsPlayer() || c.IsDead() || c.IsTamed() || c.GetComponent<TownNpc>() != null || c.GetComponent<CreatureTalker>() != null || ResidentMonster.Is(c)) continue;
                if (UltimaCreatures.Of(c) == null && (c.GetBaseAI() == null || c.GetFaction() == Character.Faction.AnimalsVeg || c.GetFaction() == Character.Faction.Players)) continue;
                var to = c.GetCenterPoint() - eye;
                float d = to.magnitude;
                if (d >= bestD) continue;
                var flat = new Vector3(to.x, 0, to.z);
                if (Vector3.Angle(transform.forward, flat) > Arc) continue;
                // (from its eye, so the merlons beside its firing port block the view; not its own frame)
                if (Blocked(eye, c.GetCenterPoint())) continue;
                best = c;
                bestD = d;
            }
            return best;
        }

        bool Blocked(Vector3 from, Vector3 to)
        {
            foreach (var h in Physics.RaycastAll(from, to - from, Vector3.Distance(from, to), SightMask))
                if (!h.collider.transform.IsChildOf(transform)) return true;
            return false;
        }

        /// <summary>Turret.UpdateTarget's place: the spotted monster while manned, else nothing.</summary>
        public void Aim()
        {
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;
            var want = Manned && target != null && !target.IsDead() ? target : null;
            var now = Traverse.Create(turret).Field("m_target").GetValue<Character>();
            if (want == now) return;
            nview.InvokeRPC(ZNetView.Everybody, "RPC_SetTarget", want != null ? want.GetZDOID() : ZDOID.None);
        }

        /// <summary>A player fires it (CMDS cmd_fire): a bolt where it points, and the town is alarmed.</summary>
        public bool PlayerFires(Player player)
        {
            if (turret.IsCoolingDown()) return false;
            nview.ClaimOwnership();
            Ballistas.firing = player;
            try { turret.ShootProjectile(); }
            finally { Ballistas.firing = null; }
            player.Message(MessageHud.MessageType.Center, Plugin.U5.Ovl.CStr(0x42f2).Trim());
            if (Town != null) Crime.Commit(player, Town);
            return true;
        }
    }
}
