using System;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using Bretaheim.WorldGen.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Bretaheim.WorldGen
{
    /// <summary>
    /// The settlements' stairs (SettlementPlanner.Stairs): a spiral stair baked into one piece per
    /// turn (u5_stairs_360/450/540/630): a newel post and its steps (visual copies of wood_pole_log_4
    /// and wood_floor_1x1, their own colliders off) and one smooth helicoid collider through the
    /// steps' middles, walked on. Players do not slide on it (Valheim slides a player off anything
    /// steeper than 38°, and the stair is steeper near its post); E takes the player up or down
    /// at once, as the original changes floor on the spot.
    /// </summary>
    public static class Stairs
    {
        static AccessTools.FieldRef<Character, Collider> lastGround;
        static AccessTools.FieldRef<Character, float> slippage;

        public static void Register() => PrefabManager.OnVanillaPrefabsAvailable += Create;

        public static void Install(Harmony harmony)
        {
            try
            {
                lastGround = AccessTools.FieldRefAccess<Character, Collider>("m_lastGroundCollider");
                slippage = AccessTools.FieldRefAccess<Character, float>("m_slippage");
                var slide = AccessTools.Method(typeof(Character), "ApplySlide") ?? throw new MissingMethodException("Character", "ApplySlide");
                harmony.Patch(slide, prefix: new HarmonyMethod(typeof(Stairs), nameof(ApplySlide_Prefix)));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Stairs: players will slide on them ({e.Message})");
            }
        }

        /// <summary>No sliding while standing on a settlement stair.</summary>
        static bool ApplySlide_Prefix(Character __instance)
        {
            var ground = lastGround(__instance);
            if (ground == null || ground.GetComponent<StairRamp>() == null) return true;
            slippage(__instance) = 0f;
            return false;
        }

        static void Create()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Create;
            var holder = new GameObject("Bretaheim_Stairs");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            int pieceLayer = LayerMask.NameToLayer("piece");
            foreach (int turn in SettlementPlanner.StairTurns)
            {
                var prefab = new GameObject(SettlementPlanner.StairsPrefab(turn));
                prefab.transform.SetParent(holder.transform, false);
                var nview = prefab.AddComponent<ZNetView>();
                nview.m_persistent = true;
                prefab.AddComponent<U5Stairs>().Turn = turn;
                bool post = true;
                foreach (var (name, x, y, z, yaw, sx, sy, sz) in SettlementPlanner.StairParts(turn))
                {
                    var part = Ladders.Visual(name);
                    if (part == null) { Plugin.Log.LogError($"Stairs: no {name}"); continue; }
                    // (the steps are walked on the ramp; the post stays solid)
                    if (!post) foreach (var col in part.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);
                    post = false;
                    part.transform.SetParent(prefab.transform, false);
                    part.transform.localPosition = new Vector3(x, y, z);
                    part.transform.localRotation = Quaternion.Euler(0, yaw, 0);
                    part.transform.localScale = new Vector3(sx, sy, sz);
                }
                var (v, t) = SettlementPlanner.StairRamp(turn);
                var verts = new Vector3[v.Length / 3];
                for (int i = 0; i < verts.Length; i++) verts[i] = new Vector3(v[3 * i], v[3 * i + 1], v[3 * i + 2]);
                var mesh = new Mesh { name = $"u5_stairs_ramp_{turn}", vertices = verts, triangles = t };
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                var ramp = new GameObject("ramp");
                ramp.layer = pieceLayer;
                ramp.transform.SetParent(prefab.transform, false);
                ramp.AddComponent<MeshCollider>().sharedMesh = mesh;
                ramp.AddComponent<StairRamp>();
                PrefabManager.Instance.AddPrefab(new CustomPrefab(prefab, false));
            }
        }
    }

    /// <summary>Marks a stair's walking surface (players do not slide on it).</summary>
    public class StairRamp : MonoBehaviour { }

    /// <summary>A baked spiral stair: E climbs to its top (left by its exit side) or back down to its foot.</summary>
    public class U5Stairs : MonoBehaviour, Hoverable, Interactable
    {
        public int Turn = 540;
        /// <summary>How far from the post the landings are: 0.6 m into the tile beside.</summary>
        const float Landing = 1.6f;

        bool Below(Player p) => transform.InverseTransformPoint(p.transform.position).y < SettlementPlanner.StairRise / 2;

        public string GetHoverText()
        {
            var me = Player.m_localPlayer;
            if (me == null) return "";
            return Localization.instance.Localize($"Stairs\n[<color=yellow><b>$KEY_Use</b></color>] {(Below(me) ? "Climb up" : "Climb down")}");
        }

        public string GetHoverName() => "Stairs";
        public float GetHoverOffset() => 0.5f;
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !(user is Player player) || player != Player.m_localPlayer) return false;
            Vector3 local;
            if (Below(player))
            {
                double a = Turn * Math.PI / 180;
                local = new Vector3((float)(Landing * Math.Sin(a)), SettlementPlanner.StairRise + 0.2f, (float)(Landing * Math.Cos(a)));
            }
            else local = new Vector3(0, 0.2f, Landing);
            var target = transform.TransformPoint(local);
            var body = player.GetComponent<Rigidbody>();
            player.transform.position = target;
            if (body != null)
            {
                body.position = target;
                body.linearVelocity = Vector3.zero;
            }
            return true;
        }
    }
}
