using System.Collections.Generic;
using UnityEngine;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// Settlement pieces whose look differs from the vanilla piece (the "look" of a part in
    /// town_tiles.json, synced as the "u5_look" ZDO key): applied when the piece is spawned and
    /// whenever it loads.
    /// </summary>
    public static class Looks
    {
        static readonly Color MarbleWhite = new Color(0.93f, 0.92f, 0.88f);
        static readonly Dictionary<Material, Material> whitened = new Dictionary<Material, Material>();
        static readonly Dictionary<Mesh, Mesh> trimmed = new Dictionary<Mesh, Mesh>();

        public static void Apply(GameObject go, string look)
        {
            if (go.GetComponent<U5Look>() != null) return;
            go.AddComponent<U5Look>();
            switch (look)
            {
                case "white_marble": Whiten(go.transform); break;
                case "fountain": Fountain(go); break;
            }
        }

        /// <summary>
        /// A water fountain (Constructing world assets 216-219): the hot tub without its stove,
        /// white marble, always full, and drunk from instead of fuelled.
        /// </summary>
        static void Fountain(GameObject go)
        {
            foreach (var smelter in go.GetComponents<Smelter>()) Object.DestroyImmediate(smelter);
            void Off(string path) { var t = go.transform.Find(path); if (t != null) t.gameObject.SetActive(false); }
            Off("add_fuel");
            Off("SitPoints");
            // the stove's colliders, fire and steam
            Off("colliders (1)/bottom (2)");
            Off("colliders (1)/bottom (3)");
            Off("colliders (1)/bottom (4)");
            Off("_Enabled/fire");
            Off("_Enabled/vfx_bath_mist");
            Off("_Enabled/vfx_planes");
            Off("_Enabled/WarmAndCozy");
            var enabled = go.transform.Find("_Enabled");
            if (enabled != null) enabled.gameObject.SetActive(true);
            var model = go.transform.Find("New");
            if (model != null)
            {
                // only the high LOD mesh is readable: the low LOD shows the trimmed high one too
                var high = model.Find("high")?.GetComponent<MeshFilter>();
                var mesh = high != null && high.sharedMesh != null ? WithoutStove(high.sharedMesh) : null;
                if (mesh != null && mesh != high.sharedMesh)
                    foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true)) mf.sharedMesh = mesh;
                Whiten(model);
            }
            go.AddComponent<FountainDrink>();
        }

        /// <summary>
        /// The hot tub's mesh without its stove (unchanged if the mesh can't be read): the stove,
        /// set into the tub's −z side, is pressed flat into the rim line, and its chimney (all
        /// that rises above the rim) is dropped. Checked offline on the mesh exported by the self-test.
        /// </summary>
        static Mesh WithoutStove(Mesh mesh)
        {
            if (trimmed.TryGetValue(mesh, out var done)) return done;
            if (!mesh.isReadable) return trimmed[mesh] = mesh;
            const float RimZ = -1.6f, RimTop = 1.25f;
            var copy = Object.Instantiate(mesh);
            var v = copy.vertices;
            for (int i = 0; i < v.Length; i++)
                if (v[i].z < RimZ) v[i].z = RimZ;
            copy.vertices = v;
            for (int s = 0; s < copy.subMeshCount; s++)
            {
                var tris = copy.GetTriangles(s);
                var keep = new List<int>(tris.Length);
                for (int i = 0; i < tris.Length; i += 3)
                {
                    float y = (v[tris[i]].y + v[tris[i + 1]].y + v[tris[i + 2]].y) / 3;
                    if (y > RimTop) continue;
                    keep.Add(tris[i]); keep.Add(tris[i + 1]); keep.Add(tris[i + 2]);
                }
                copy.SetTriangles(keep, s);
            }
            copy.RecalculateBounds();
            return trimmed[mesh] = copy;
        }

        static void Whiten(Transform root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || mats[i].shader.name == "Custom/Water") continue;
                    if (!whitened.TryGetValue(mats[i], out var white))
                    {
                        white = new Material(mats[i]) { name = mats[i].name + "_u5white" };
                        if (white.HasProperty("_MainTex")) white.SetTexture("_MainTex", Texture2D.whiteTexture);
                        if (white.HasProperty("_Color")) white.SetColor("_Color", MarbleWhite);
                        whitened[mats[i]] = white;
                    }
                    mats[i] = white;
                }
                r.sharedMaterials = mats;
            }
        }
    }

    /// <summary>Marks a piece whose look has been changed.</summary>
    public class U5Look : MonoBehaviour { }

    /// <summary>Town fountains: interact to drink.</summary>
    public class FountainDrink : MonoBehaviour, Interactable, Hoverable
    {
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !(user is Player player) || player != Player.m_localPlayer) return false;
            player.AddStamina(player.GetMaxStamina());
            player.Message(MessageHud.MessageType.Center, "The water is cool and clear.");
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
        public string GetHoverText() => Localization.instance.Localize("Fountain\n[<color=yellow><b>$KEY_Use</b></color>] Drink");
        public string GetHoverName() => "Fountain";
        public float GetHoverOffset() => 1f;
    }
}
