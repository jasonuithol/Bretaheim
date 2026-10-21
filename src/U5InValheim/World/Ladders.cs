using Jotunn.Entities;
using Jotunn.Managers;
using U5InValheim.WorldGen.Core;
using UnityEngine;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// The settlements' ladders baked into one piece each (SettlementPlanner.LadderUp/LadderDown):
    /// a networked object holding visual copies of its rails and rungs (their meshes and colliders,
    /// no scripts), instead of a piece per rail and rung. Its climb (ClimbPoint, from the ZDO's
    /// climb target) is added when it starts; its look ("passable") comes back as any piece's does.
    /// </summary>
    public static class Ladders
    {
        public static void Register() => PrefabManager.OnVanillaPrefabsAvailable += Create;

        static void Create()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Create;
            var holder = new GameObject("U5InValheim_Ladders");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            foreach (bool up in new[] { true, false })
            {
                var prefab = new GameObject(up ? SettlementPlanner.LadderUp : SettlementPlanner.LadderDown);
                prefab.transform.SetParent(holder.transform, false);
                var nview = prefab.AddComponent<ZNetView>();
                nview.m_persistent = true;
                prefab.AddComponent<U5Ladder>();
                foreach (var (name, x, y, sx, sy, sz) in SettlementPlanner.LadderParts(up))
                {
                    var part = Visual(name);
                    if (part == null) { Plugin.Log.LogError($"Ladders: no {name}"); continue; }
                    part.transform.SetParent(prefab.transform, false);
                    part.transform.localPosition = new Vector3(x, y, 0);
                    part.transform.localScale = new Vector3(sx, sy, sz);
                }
                PrefabManager.Instance.AddPrefab(new CustomPrefab(prefab, false));
            }
        }

        /// <summary>A vanilla piece's pristine look and shape only (its "new" state; no scripts or physics body).</summary>
        internal static GameObject Visual(string prefabName)
        {
            var prefab = PrefabManager.Instance.GetPrefab(prefabName);
            if (prefab == null) return null;
            var holder = new GameObject("u5_ladder_holder");
            holder.SetActive(false);
            var copy = Object.Instantiate(prefab, holder.transform);
            var wnt = copy.GetComponent<WearNTear>();
            if (wnt != null)
            {
                foreach (var state in new[] { wnt.m_worn, wnt.m_broken, wnt.m_wet })
                    if (state != null && state != wnt.m_new) Object.DestroyImmediate(state);
                if (wnt.m_new != null) wnt.m_new.SetActive(true);
            }
            for (int pass = 0; pass < 6; pass++)
            {
                bool any = false;
                foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null) continue;
                    try { Object.DestroyImmediate(mb); any = true; } catch (System.Exception) { }
                }
                if (!any) break;
            }
            foreach (var body in copy.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(body);
            copy.transform.SetParent(null, false);
            Object.Destroy(holder);
            copy.SetActive(true);
            return copy;
        }
    }

    /// <summary>A baked ladder: climbed (E) to the floor its ZDO names.</summary>
    public class U5Ladder : MonoBehaviour
    {
        // Start, not Awake: a freshly spawned ladder gets its climb target after Awake.
        void Start()
        {
            var zdo = GetComponent<ZNetView>()?.GetZDO();
            if (zdo == null || GetComponent<ClimbPoint>() != null) return;
            var target = zdo.GetVec3(Settlements.ClimbKey, Vector3.zero);
            if (target == Vector3.zero) return;
            var climb = gameObject.AddComponent<ClimbPoint>();
            climb.Target = target;
            climb.Verb = zdo.GetString(Settlements.ClimbVerbKey, "Climb");
        }
    }
}
