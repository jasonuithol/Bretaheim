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
        static readonly Vector3 HeadOffset = new Vector3(0.75f, -0.05f, 0f);
        const float HeadScale = 1.1f;

        public static void Register() => PrefabManager.OnVanillaPrefabsAvailable += Create;

        static void Create()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Create;
            var prefab = PrefabManager.Instance.CreateClonedPrefab(PrefabName, "Troll");
            var character = prefab != null ? prefab.GetComponent<Character>() : null;
            if (character == null) { Plugin.Log.LogError("Ettin: no Troll prefab to make it from"); return; }
            character.m_name = "Ettin";
            var head = prefab.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Head");
            var trophy = PrefabManager.Instance.GetPrefab("TrophyForestTroll");
            if (head != null && trophy != null)
            {
                var second = Visual(trophy);
                second.name = "u5_second_head";
                var root = prefab.transform;
                second.transform.SetParent(head, false);
                // placed in the troll's frame (the head bone's own axes are the rig's), facing forward
                second.transform.position = head.position + root.TransformVector(HeadOffset);
                second.transform.rotation = root.rotation;
                second.transform.localScale = Vector3.one * HeadScale;
                second.transform.localScale = new Vector3(HeadScale / head.lossyScale.x, HeadScale / head.lossyScale.y, HeadScale / head.lossyScale.z);
                if (Plugin.SelfTest.Value)
                    Plugin.Log.LogInfo($"Ettin: second head at {root.InverseTransformPoint(second.transform.position)} (the troll's head {root.InverseTransformPoint(head.position)}, head scale {head.lossyScale}), {second.GetComponentsInChildren<Renderer>(true).Length} renderers");
            }
            else Plugin.Log.LogWarning($"Ettin: {(head == null ? "no Head bone" : "no TrophyForestTroll")}: one head only");
            prefab.AddComponent<EttinLook>();
            PrefabManager.Instance.AddPrefab(new CustomPrefab(prefab, false));
        }

        /// <summary>A prefab's look only: no scripts, physics or colliders.</summary>
        static GameObject Visual(GameObject prefab)
        {
            var holder = new GameObject("u5_ettin_holder");
            holder.SetActive(false);
            var copy = Object.Instantiate(prefab, holder.transform);
            for (int pass = 0; pass < 4; pass++)
                foreach (var c in copy.GetComponentsInChildren<Component>(true))
                    if (c is MonoBehaviour || c is Rigidbody || c is Collider) Object.DestroyImmediate(c);
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
