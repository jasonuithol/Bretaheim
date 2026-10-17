using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace U5InValheim.DevTools
{
    /// <summary>
    /// Development aid: when DumpPieceGeometry is on, writes the snap points and collider bounds of
    /// the building pieces the town builder uses to BepInEx/u5_piece_geometry.json once ZNetScene
    /// has loaded (works on a headless dedicated server). The builder's geometry table is derived
    /// from that file instead of guessed.
    /// </summary>
    public static class PieceGeometryDump
    {
        public static void Install(Harmony harmony)
        {
            var awake = AccessTools.Method(typeof(ZNetScene), "Awake");
            if (awake != null) harmony.Patch(awake, postfix: new HarmonyMethod(typeof(PieceGeometryDump), nameof(Awake_Postfix)));
        }

        static void Awake_Postfix(ZNetScene __instance)
        {
            if (Plugin.SelfTest.Value)
                WorldGen.Structures.SelfTest("stone_wall_2x1", "stone_fence", "darkwood_roof", "piece_grausten_roof_45", "piece_grausten_roof_45_corner",
                    "forge_ext1", "forge_ext2", "forge_ext3", "forge_ext4", "forge_ext5", "forge_ext6");
            var names = Plugin.DumpPieceGeometry.Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            if (names.Count == 0) return;
            if (names.Count == 1 && names[0] == "*")
                names = __instance.m_prefabs.Where(p => p != null && p.GetComponent<Piece>() != null).Select(p => p.name).ToList();

            var sb = new StringBuilder("{\n");
            bool first = true;
            foreach (var name in names)
            {
                var prefab = __instance.GetPrefab(name);
                if (prefab == null) continue;
                if (!first) sb.Append(",\n");
                first = false;
                sb.Append($"  \"{name}\": {{");
                var snaps = new List<string>();
                foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true))
                    if (t.CompareTag("snappoint")) snaps.Add(V(prefab.transform.InverseTransformPoint(t.position)));
                sb.Append($"\"snap\": [{string.Join(", ", snaps)}]");

                var b = LocalBounds(prefab);
                if (b.HasValue) sb.Append($", \"min\": {V(b.Value.min)}, \"max\": {V(b.Value.max)}");
                var piece = prefab.GetComponent<Piece>();
                if (piece != null) sb.Append($", \"category\": \"{piece.m_category}\"");
                var wnt = prefab.GetComponent<WearNTear>();
                if (wnt != null) sb.Append($", \"material\": \"{wnt.m_materialType}\"");
                sb.Append("}");
            }
            sb.Append("\n}\n");
            string path = Path.Combine(BepInEx.Paths.BepInExRootPath, "u5_piece_geometry.json");
            File.WriteAllText(path, sb.ToString());
            Plugin.Log.LogInfo($"Piece geometry of {names.Count} prefabs written to {path}");
        }

        /// <summary>Union of the prefab's non-trigger collider bounds in its own space.</summary>
        static Bounds? LocalBounds(GameObject prefab)
        {
            Bounds? r = null;
            foreach (var c in prefab.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger) continue;
                Bounds local;
                switch (c)
                {
                    case BoxCollider box: local = new Bounds(box.center, box.size); break;
                    case MeshCollider mesh when mesh.sharedMesh != null: local = mesh.sharedMesh.bounds; break;
                    case CapsuleCollider cap: local = new Bounds(cap.center, Vector3.one * cap.radius * 2 + Axis(cap.direction) * (cap.height - cap.radius * 2)); break;
                    case SphereCollider sph: local = new Bounds(sph.center, Vector3.one * sph.radius * 2); break;
                    default: continue;
                }
                // corners of the collider's local box through its transform into prefab space
                var m = prefab.transform.worldToLocalMatrix * c.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var p = m.MultiplyPoint3x4(new Vector3(
                        (i & 1) == 0 ? local.min.x : local.max.x,
                        (i & 2) == 0 ? local.min.y : local.max.y,
                        (i & 4) == 0 ? local.min.z : local.max.z));
                    if (r == null) r = new Bounds(p, Vector3.zero);
                    else { var rb = r.Value; rb.Encapsulate(p); r = rb; }
                }
            }
            return r;
        }

        static Vector3 Axis(int d) => d == 0 ? Vector3.right : d == 1 ? Vector3.up : Vector3.forward;

        static string V(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "[{0:0.###}, {1:0.###}, {2:0.###}]", v.x, v.y, v.z);
    }
}
