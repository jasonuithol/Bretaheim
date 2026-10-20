using System.Collections.Generic;
using System.Linq;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// The ettin (monsters.json type 35): Valheim's troll with a second head on its shoulders (a
    /// frost troll's trophy head beside its own) and red skin. Its own prefab, "Ettin", so the
    /// `spawn ettin` dev command finds it.
    /// </summary>
    public static class Ettin
    {
        public const string PrefabName = "Ettin";
        /// <summary>
        /// Where the second head sits from the first, in the troll's own frame (x right, y up, z
        /// forward), its size, its turn outward and how far it looks up (tried in game: at 2.4 it was
        /// three times the first head and hung its face).
        /// </summary>
        static readonly Vector3 HeadOffset = new Vector3(1.0f, -0.05f, 0.1f);
        const float HeadScale = 0.8f, HeadTurn = 15f, HeadLookUp = 40f;

        public static void Register() => PrefabManager.OnVanillaPrefabsAvailable += Create;

        static void Create()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Create;
            var prefab = PrefabManager.Instance.CreateClonedPrefab(PrefabName, "Troll");
            var character = prefab != null ? prefab.GetComponent<Character>() : null;
            if (character == null) { Plugin.Log.LogError("Ettin: no Troll prefab to make it from"); return; }
            character.m_name = "Ettin";
            prefab.AddComponent<EttinLook>();
            // its body when it falls: the troll's ragdoll, made over the same way (else a blue, one-headed corpse)
            foreach (var effect in character.m_deathEffects.m_effectPrefabs ?? new EffectList.EffectData[0])
            {
                if (effect?.m_prefab == null || effect.m_prefab.GetComponent<Ragdoll>() == null) continue;
                var ragdoll = PrefabManager.Instance.CreateClonedPrefab(PrefabName + "_ragdoll", effect.m_prefab);
                ragdoll.AddComponent<EttinLook>();
                PrefabManager.Instance.AddPrefab(new CustomPrefab(ragdoll, false));
                effect.m_prefab = ragdoll;
            }
            PrefabManager.Instance.AddPrefab(new CustomPrefab(prefab, false));
        }

        /// <summary>
        /// The second head, made as each ettin (or its corpse) appears: the frost troll's trophy made as an item stand
        /// makes it (its attach object, live, with its own scripts) on the body's head bone, beside the
        /// first; only its colliders and physics go. A copy made at the menu comes without a mesh.
        /// </summary>
        public static Transform AddHead(GameObject body)
        {
            var head = body.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Head");
            // (the frost troll's: Valheim's forest troll trophy has no mesh or materials on its head)
            var trophy = ObjectDB.instance?.GetItemPrefab("TrophyFrostTroll");
            var attach = trophy != null ? ItemStand.GetAttachPrefab(trophy) : null;
            if (head == null || attach == null) { Plugin.Log.LogWarning($"Ettin: {(head == null ? $"no Head bone in {body.name}" : "no trophy to make a head of")}: one head only"); return null; }
            var root = body.transform;
            var second = Object.Instantiate(ItemStand.GetAttachGameObject(attach), head);
            second.name = "u5_second_head";
            foreach (var c in second.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
            foreach (var rb in second.GetComponentsInChildren<Rigidbody>(true)) Object.Destroy(rb);
            // placed in the body's frame (the head bone's own axes are the rig's), facing forward
            second.transform.position = head.position + root.TransformVector(HeadOffset);
            second.transform.rotation = root.rotation * Quaternion.Euler(-HeadLookUp, HeadTurn, 0);
            var ls = head.lossyScale;
            second.transform.localScale = new Vector3(HeadScale / ls.x, HeadScale / ls.y, HeadScale / ls.z);
            return second.transform;
        }
    }

    /// <summary>The ettin's red skin: its troll textures hue-turned from blue to red (made once, on machines that draw).</summary>
    public class EttinLook : MonoBehaviour
    {
        static readonly Dictionary<Material, Material> reddened = new Dictionary<Material, Material>();
        /// <summary>Hue turn: the troll's blue (about 0.6) to red.</summary>
        const float HueTurn = 0.4f;

        static bool reported;

        static bool Draws => SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;

        void Start()
        {
            var second = GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "u5_second_head") ?? Ettin.AddHead(gameObject);
            if (!Draws) return;
            if (second != null) foreach (var r in second.GetComponentsInChildren<Renderer>(true)) Redden(r);
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

        void Awake()
        {
            if (!Draws) return;
            foreach (var r in GetComponentsInChildren<Renderer>(true)) Redden(r);
        }

        static void Redden(Renderer r)
        {
            if (r.GetType().Name == "ParticleSystemRenderer") return;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] != null && !mats[i].name.EndsWith("(ettin)")) mats[i] = Red(mats[i]);
            r.sharedMaterials = mats;
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
