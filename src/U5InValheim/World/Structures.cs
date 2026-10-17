using System;
using System.Collections.Generic;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// Combined building shells (walls, floors, roofs) of a settlement: one networked
    /// "u5_structure" object per settlement and zone instead of hundreds of pieces. Its ZDO only
    /// says which settlement and zone it is; on load it rebuilds the shell from the settlement's
    /// plan (deterministic, every peer has it) using visual copies of the vanilla pieces: their
    /// meshes, LODs and colliders (on the piece layer, so shelter and roof checks still see them)
    /// without ZNetView, WearNTear or Piece. Nothing in a shell can be damaged, worn or removed.
    /// </summary>
    public static class Structures
    {
        public const string PrefabName = "u5_structure";
        public const string LocationKey = "u5_loc", ZoneXKey = "u5_zx", ZoneYKey = "u5_zy";

        static GameObject templateRoot;
        static readonly Dictionary<string, GameObject> templates = new Dictionary<string, GameObject>();

        /// <summary>Registers the u5_structure prefab (a ZNetView and the rebuilding component).</summary>
        public static void Register()
        {
            var holder = new GameObject("U5InValheim_PrefabHolder");
            holder.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(holder);
            var prefab = new GameObject(PrefabName);
            prefab.transform.SetParent(holder.transform, false);
            var nview = prefab.AddComponent<ZNetView>();
            nview.m_persistent = true;
            prefab.AddComponent<U5Structure>();
            PrefabManager.Instance.AddPrefab(new CustomPrefab(prefab, false));
        }

        /// <summary>Development self-test: builds templates and logs what each keeps.</summary>
        public static void SelfTest(params string[] names)
        {
            foreach (var n in names)
            {
                var t = Template(n);
                if (t == null) { Plugin.Log.LogWarning($"template {n}: no prefab"); continue; }
                var kinds = new SortedSet<string>();
                foreach (var c in t.GetComponentsInChildren<Component>(true)) kinds.Add(c.GetType().Name);
                int colliders = t.GetComponentsInChildren<Collider>(true).Length, renderers = t.GetComponentsInChildren<Renderer>(true).Length;
                int readable = 0, meshes = 0;
                foreach (var mf in t.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null) { meshes++; if (mf.sharedMesh.isReadable) readable++; }
                var piece = ZNetScene.instance?.GetPrefab(n)?.GetComponent<Piece>();
                Plugin.Log.LogInfo($"template {n} ({piece?.m_name}): {renderers} renderers, {colliders} colliders, {readable}/{meshes} readable meshes, components {string.Join(",", kinds)}");
            }
        }

        /// <summary>Building shell pieces go into structures; doors, furniture, plants and climb points stay pieces.</summary>
        public static bool IsShell(Core.Placement p)
        {
            if (p.Climb || p.Text != null || p.Items != null || p.Look != null) return false;
            string n = p.Prefab;
            if (n.Contains("door") || n.Contains("gate")) return false;
            // lights keep their scripts (piece_walltorch, piece_brazierfloor01, the lantern pole would match below)
            if (n.Contains("torch") || n.Contains("brazier") || n.Contains("lantern")) return false;
            return n.Contains("wall") || n.Contains("floor") || n.Contains("roof") || n.Contains("pole") || n.Contains("pillar")
                   || n.Contains("fence") || n.Contains("beam") || n.Contains("arch") || n.StartsWith("blackmarble_");
        }

        /// <summary>
        /// A visual copy of a vanilla piece: instantiated under an inactive root (so none of its
        /// components wake up and no ZDO is made), then stripped of every script and rigidbody,
        /// keeping only its pristine look (WearNTear's "new" state).
        /// </summary>
        public static GameObject Template(string prefabName)
        {
            if (templates.TryGetValue(prefabName, out var t)) return t;
            var prefab = ZNetScene.instance?.GetPrefab(prefabName);
            if (prefab == null) return templates[prefabName] = null;
            if (templateRoot == null)
            {
                templateRoot = new GameObject("U5InValheim_Templates");
                templateRoot.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(templateRoot);
            }
            var copy = UnityEngine.Object.Instantiate(prefab, templateRoot.transform);
            copy.name = prefabName;
            var wnt = copy.GetComponent<WearNTear>();
            if (wnt != null)
            {
                foreach (var state in new[] { wnt.m_worn, wnt.m_broken, wnt.m_wet })
                    if (state != null && state != wnt.m_new) UnityEngine.Object.DestroyImmediate(state);
                if (wnt.m_new != null) wnt.m_new.SetActive(true);
            }
            // scripts may require one another: remove them over a few passes
            for (int pass = 0; pass < 6; pass++)
            {
                bool any = false;
                foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null) continue;
                    try { UnityEngine.Object.DestroyImmediate(mb); any = true; } catch (Exception) { }
                }
                if (!any) break;
            }
            foreach (var body in copy.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(body);
            templates[prefabName] = copy;
            return copy;
        }
    }

    /// <summary>The component of u5_structure: rebuilds its shell when it starts.</summary>
    public class U5Structure : MonoBehaviour
    {
        ZNetView nview;
        bool built;

        void Awake() => nview = GetComponent<ZNetView>();

        // Start, not Awake: a freshly spawned structure gets its location/zone keys after Awake.
        void Start() => Build();

        void Build()
        {
            if (built || nview == null || !nview.IsValid()) return;
            var zdo = nview.GetZDO();
            int loc = zdo.GetInt(Structures.LocationKey);
            var zone = new Vector2s(zdo.GetInt(Structures.ZoneXKey), zdo.GetInt(Structures.ZoneYKey));
            var settlement = Settlements.Get(loc);
            if (settlement == null || !Settlements.ShellPieces(loc, zone, out var pieces))
            {
                Plugin.Log.LogWarning($"Structure for location {loc} zone {zone} has no plan (settlement not built in this world's config?)");
                return;
            }
            built = true;
            int missing = 0;
            foreach (var p in pieces)
            {
                var template = Structures.Template(p.Prefab);
                if (template == null) { missing++; continue; }
                var pos = settlement.Origin + new Vector3(p.X, p.Y, p.Z);
                var go = Instantiate(template, pos, Quaternion.Euler(0, p.Yaw, 0), transform);
                if (p.Scaled) go.transform.localScale = new Vector3(p.SX, p.SY, p.SZ);
                go.SetActive(true);
            }
            if (missing > 0) Plugin.Log.LogWarning($"Structure {loc}/{zone}: {missing} pieces with unknown prefabs");
        }
    }
}
