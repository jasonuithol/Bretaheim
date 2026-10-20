using System.Collections.Generic;
using System.Linq;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// The ettin (monsters.json type 35): Valheim's troll with a second head on its shoulders (a
    /// forest troll's trophy head beside its own) and red skin. Its own prefab, "Ettin", so the
    /// `spawn ettin` dev command finds it.
    /// </summary>
    public static class Ettin
    {
        public const string PrefabName = "Ettin";
        /// <summary>Where the second head sits from the first, in the troll's own frame (x right, y up, z forward), and its size.</summary>
        /// <summary>(The troll's head is about 1.3 m across; the trophy head about 0.6 m: twice its size, clear of the first.)</summary>
        static readonly Vector3 HeadOffset = new Vector3(1.45f, -0.15f, 0.05f);
        const float HeadScale = 2.4f, HeadTurn = 15f;

        public static void Register() => PrefabManager.OnVanillaPrefabsAvailable += Create;

        static void Create()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Create;
            var prefab = PrefabManager.Instance.CreateClonedPrefab(PrefabName, "Troll");
            var character = prefab != null ? prefab.GetComponent<Character>() : null;
            if (character == null) { Plugin.Log.LogError("Ettin: no Troll prefab to make it from"); return; }
            character.m_name = "Ettin";
            // (the game's own trophy: a lookup by name can find an empty stand-in of the same name)
            var trophy = ZNetScene.instance?.GetPrefab("TrophyForestTroll") ?? ObjectDB.instance?.GetItemPrefab("TrophyForestTroll") ?? PrefabManager.Instance.GetPrefab("TrophyForestTroll");
            foreach (var mf in trophy != null ? trophy.GetComponentsInChildren<MeshFilter>(true) : new MeshFilter[0])
                Plugin.Log.LogInfo($"Ettin: the trophy's {mf.name}: mesh {(mf.sharedMesh != null ? $"{mf.sharedMesh.name} ({mf.sharedMesh.vertexCount} vertices, {mf.sharedMesh.bounds.size})" : "none")}, material {mf.GetComponent<Renderer>()?.sharedMaterial?.name ?? "none"}");
            AddHead(prefab, trophy);
            prefab.AddComponent<EttinLook>();
            // its body when it falls: the troll's ragdoll, made over the same way (else a blue, one-headed corpse)
            foreach (var effect in character.m_deathEffects.m_effectPrefabs ?? new EffectList.EffectData[0])
            {
                if (effect?.m_prefab == null || effect.m_prefab.GetComponent<Ragdoll>() == null) continue;
                var ragdoll = PrefabManager.Instance.CreateClonedPrefab(PrefabName + "_ragdoll", effect.m_prefab);
                AddHead(ragdoll, trophy);
                ragdoll.AddComponent<EttinLook>();
                PrefabManager.Instance.AddPrefab(new CustomPrefab(ragdoll, false));
                effect.m_prefab = ragdoll;
            }
            PrefabManager.Instance.AddPrefab(new CustomPrefab(prefab, false));
        }

        /// <summary>The second head: the trophy's look on the body's head bone, beside the first.</summary>
        static void AddHead(GameObject body, GameObject trophy)
        {
            var heads = body.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Head").ToList();
            var head = heads.FirstOrDefault();
            if (head == null || trophy == null) { Plugin.Log.LogWarning($"Ettin: {(head == null ? $"no Head bone in {body.name}" : "no TrophyForestTroll")}: one head only"); return; }
            var second = Visual(trophy);
            second.name = "u5_second_head";
            var root = body.transform;
            second.transform.SetParent(head, false);
            // placed in the troll's frame (the head bone's own axes are the rig's), facing forward
            second.transform.position = head.position + root.TransformVector(HeadOffset);
            second.transform.rotation = root.rotation * Quaternion.Euler(0, HeadTurn, 0);
            second.transform.localScale = new Vector3(HeadScale / head.lossyScale.x, HeadScale / head.lossyScale.y, HeadScale / head.lossyScale.z);
            Plugin.Log.LogInfo($"Ettin: second head on {body.name}'s {HeadPath(head, root)} ({heads.Count} called Head) at {root.InverseTransformPoint(second.transform.position)}, its head bone at {root.InverseTransformPoint(head.position)}; {second.GetComponentsInChildren<Renderer>(true).Length} renderers, layer {LayerMask.LayerToName(second.layer)}");
        }

        static string HeadPath(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (; t != null && t != root; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>A prefab's look only: no scripts, physics or colliders.</summary>
        static GameObject Visual(GameObject prefab)
        {
            var holder = new GameObject("u5_ettin_holder");
            holder.SetActive(false);
            var copy = Object.Instantiate(prefab, holder.transform);
            for (int pass = 0; pass < 4; pass++)
                foreach (var c in copy.GetComponentsInChildren<Component>(true))
                    if (c is MonoBehaviour || c is Rigidbody || c is Collider || c.GetType().Name.StartsWith("ParticleSystem")) Object.DestroyImmediate(c);
            copy.transform.SetParent(null, false);
            Object.Destroy(holder);
            copy.SetActive(true);
            return copy;
        }
    }

    /// <summary>The ettin's red skin: its troll textures hue-turned from blue to red (made once, on machines that draw).</summary>
    public class EttinLook : MonoBehaviour
    {
        static readonly Dictionary<Material, Material> reddened = new Dictionary<Material, Material>();
        /// <summary>Hue turn: the troll's blue (about 0.6) to red.</summary>
        const float HueTurn = 0.4f;

        static bool reported;

        void Start()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var second = GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "u5_second_head");
            Mend(second);
            if (reported) return;
            reported = true;
            if (second == null) { Plugin.Log.LogWarning($"Ettin: {name} has no second head"); return; }
            foreach (var r in second.GetComponentsInChildren<Renderer>(true))
            {
                var mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
                Plugin.Log.LogInfo($"Ettin: drawn second head {r.name}: active {r.gameObject.activeInHierarchy}, enabled {r.enabled}, mesh {(mesh != null ? $"{mesh.name} {mesh.vertexCount} vertices, bounds {mesh.bounds.size}" : "none")}, world size {r.bounds.size}, at {transform.InverseTransformPoint(r.bounds.center)}, lossy scale {r.transform.lossyScale}, shader {r.sharedMaterial?.shader?.name}, layer {LayerMask.LayerToName(r.gameObject.layer)}");
            }
            foreach (var lod in second.GetComponentsInChildren<LODGroup>(true))
                Plugin.Log.LogInfo($"Ettin: second head LOD group {lod.name}: size {lod.size}, lods {string.Join(", ", lod.GetLODs().Select(l => $"{l.screenRelativeTransitionHeight:0.000}: {l.renderers.Length}"))}");
        }

        /// <summary>A second head that came without its mesh takes the trophy's, as the running game has it.</summary>
        static void Mend(Transform second)
        {
            if (second == null) return;
            var trophy = ZNetScene.instance?.GetPrefab("TrophyForestTroll");
            if (trophy == null) return;
            var source = trophy.GetComponentsInChildren<MeshFilter>(true);
            foreach (var mf in second.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh != null) continue;
                var from = source.FirstOrDefault(s => s.name == mf.name && s.sharedMesh != null) ?? source.FirstOrDefault(s => s.sharedMesh != null);
                if (from == null) continue;
                mf.sharedMesh = from.sharedMesh;
                var r = mf.GetComponent<Renderer>();
                var mats = from.GetComponent<Renderer>()?.sharedMaterials;
                if (r != null && mats != null) r.sharedMaterials = mats.Select(m => m != null ? Red(m) : null).ToArray();
                Plugin.Log.LogInfo($"Ettin: second head mended with the trophy's {from.sharedMesh.name}");
            }
        }

        void Awake()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r.GetType().Name == "ParticleSystemRenderer") continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null) mats[i] = Red(mats[i]);
                r.sharedMaterials = mats;
            }
        }

        static Material Red(Material m)
        {
            if (reddened.TryGetValue(m, out var done)) return done;
            var copy = new Material(m) { name = m.name + " (ettin)" };
            if (m.HasProperty("_MainTex") && m.mainTexture is Texture2D tex)
            {
                var red = HueTurned(tex);
                if (red != null) copy.mainTexture = red;
            }
            return reddened[m] = copy;
        }

        static Texture2D HueTurned(Texture2D tex)
        {
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var was = RenderTexture.active;
            try
            {
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, true) { name = tex.name + " (ettin)" };
                copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                var px = copy.GetPixels32();
                for (int i = 0; i < px.Length; i++)
                {
                    Color.RGBToHSV(px[i], out float h, out float s, out float v);
                    var c = Color.HSVToRGB((h + HueTurn) % 1f, Mathf.Min(1f, s * 1.15f), v);
                    px[i] = new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), px[i].a);
                }
                copy.SetPixels32(px);
                copy.Apply(true);
                return copy;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"Ettin: could not redden {tex.name}: {e.Message}");
                return null;
            }
            finally
            {
                RenderTexture.active = was;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
