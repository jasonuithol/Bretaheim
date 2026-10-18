using System;
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

        /// <summary>Development self-test: a prefab's children, renderers and materials, and its fire settings.</summary>
        static void LogHierarchy(GameObject prefab)
        {
            if (prefab == null) return;
            var sb = new StringBuilder($"hierarchy {prefab.name}:");
            foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true))
            {
                var path = t == prefab.transform ? "." : t.name;
                for (var up = t.parent; up != null && up != prefab.transform; up = up.parent) path = up.name + "/" + path;
                var r = t.GetComponent<Renderer>();
                sb.Append($"\n  {path} active={t.gameObject.activeSelf} pos={t.localPosition} scale={t.localScale}");
                if (r != null) sb.Append($" renderer[{string.Join(",", r.sharedMaterials.Select(m => m == null ? "null" : $"{m.name}/{m.shader?.name}"))}]");
                var mf = t.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) sb.Append($" mesh {mf.sharedMesh.name} readable={mf.sharedMesh.isReadable} bounds={mf.sharedMesh.bounds.min}..{mf.sharedMesh.bounds.max}");
                foreach (var c in t.GetComponents<Component>()) if (!(c is Transform) && !(c is Renderer) && !(c is MeshFilter)) sb.Append($" {c.GetType().Name}");
            }
            // readable meshes as OBJ (offline checks of mesh edits, e.g. the fountain's stove cut)
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || !mf.sharedMesh.isReadable || mf.name != "high") continue;
                var obj = new StringBuilder();
                foreach (var v in mf.sharedMesh.vertices) obj.Append(FormattableString.Invariant($"v {v.x} {v.y} {v.z}\n"));
                var tris = mf.sharedMesh.triangles;
                for (int i = 0; i < tris.Length; i += 3) obj.Append($"f {tris[i] + 1} {tris[i + 1] + 1} {tris[i + 2] + 1}\n");
                File.WriteAllText(Path.Combine(BepInEx.Paths.BepInExRootPath, $"{prefab.name}_{mf.transform.parent?.name}_{mf.name}.obj"), obj.ToString());
            }
            var fire = prefab.GetComponent<Fireplace>();
            if (fire != null)
                sb.Append($"\n  fire: enabled={fire.m_enabledObject?.name} disableCover={fire.m_disableCoverCheck} coverOffset={fire.m_coverCheckOffset} terrainOffset={fire.m_checkTerrainOffset} smoke={fire.m_smokeSpawner != null} secPerFuel={fire.m_secPerFuel}");
            Plugin.Log.LogInfo(sb.ToString());
        }

        /// <summary>Development self-test: a settlement look applied to an offline copy of a prefab.</summary>
        static void TestLook(GameObject prefab, string look)
        {
            if (prefab == null) return;
            ZNetView.m_forceDisableInit = true;
            var go = UnityEngine.Object.Instantiate(prefab);
            ZNetView.m_forceDisableInit = false;
            try
            {
                WorldGen.Looks.Apply(go, look);
                var sb = new StringBuilder($"look {look} on {prefab.name}:");
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null && mf.GetComponent<Renderer>()?.enabled == true && mf.gameObject.activeInHierarchy)
                    {
                        var mat = mf.GetComponent<Renderer>().sharedMaterial;
                        sb.Append($" {mf.name}={mf.sharedMesh.triangles.Length / 3}tris z>={mf.sharedMesh.bounds.min.z:0.00} y<={mf.sharedMesh.bounds.max.y:0.00} mat={mat?.name}");
                        if (mat != null) sb.Append($" tex[{string.Join(",", mat.GetTexturePropertyNames().Select(t => $"{t}={mat.GetTexture(t)?.name}"))}]");
                    }
                sb.Append($" smelter={go.GetComponent<Smelter>() != null} drink={go.GetComponent<WorldGen.FountainDrink>() != null}");
                Plugin.Log.LogInfo(sb.ToString());
            }
            catch (Exception e) { Plugin.Log.LogError($"look {look} on {prefab.name}: {e}"); }
            UnityEngine.Object.Destroy(go);
        }

        static void Awake_Postfix(ZNetScene __instance)
        {
            if (Plugin.SelfTest.Value)
                WorldGen.Structures.SelfTest("stone_wall_2x1", "stone_fence", "darkwood_roof", "piece_grausten_roof_45", "piece_grausten_roof_45_corner",
                    "forge_ext1", "forge_ext2", "forge_ext3", "forge_ext4", "forge_ext5", "forge_ext6", "piece_bathtub");
            if (Plugin.SelfTest.Value)
            {
                foreach (var n in new[] { "piece_bathtub", "piece_walltorch", "SwampTree1", "itemstand", "piece_banner01", "Candle_resin", "wood_gate", "ashwood_door", "forge_ext1" })
                    LogHierarchy(__instance.GetPrefab(n));
                foreach (var n in new[] { "forge_ext1", "forge_ext2", "forge_ext3", "forge_ext4", "forge_ext5", "forge_ext6", "piece_brazierfloor01", "piece_brazierfloor02", "piece_brazierceiling01", "itemstand", "itemstandh" })
                {
                    var piece = __instance.GetPrefab(n)?.GetComponent<Piece>();
                    var stand = __instance.GetPrefab(n)?.GetComponent<ItemStand>();
                    Plugin.Log.LogInfo($"piece name {n}: {piece?.m_name} = {(piece != null ? Localization.instance?.Localize(piece.m_name) : null)}"
                        + (stand != null ? $" stand types {string.Join(",", stand.m_supportedTypes)} orientation {stand.m_orientationType}" : ""));
                }
                TestLook(__instance.GetPrefab("piece_bathtub"), "fountain");
                TestLook(__instance.GetPrefab("blackmarble_column_2"), "white_marble");
                TestLook(__instance.GetPrefab("piece_banner01"), "icon:f6");
                var cloth = __instance.GetPrefab("piece_banner01")?.transform.Find("default")?.GetComponent<MeshFilter>()?.sharedMesh;
                if (cloth != null && cloth.isReadable)
                {
                    var v = cloth.vertices; var uv = cloth.uv;
                    var line = new StringBuilder($"banner cloth uv: {v.Length} verts, colours {cloth.colors.Length}, uv2 {cloth.uv2.Length}:");
                    for (int i = 0; i < v.Length; i += Math.Max(1, v.Length / 12)) line.Append($" ({v[i].z:0.00},{v[i].y:0.00})->({uv[i].x:0.00},{uv[i].y:0.00})");
                    Plugin.Log.LogInfo(line.ToString());
                }
            }
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
