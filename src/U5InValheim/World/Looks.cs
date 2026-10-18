using System;
using System.Collections.Generic;
using U5InValheim.WorldGen.Core;
using UnityEngine;
using Object = UnityEngine.Object;

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
        static readonly Dictionary<Mesh, Mesh> unwrapped = new Dictionary<Mesh, Mesh>();
        static readonly Dictionary<(Material, int), Material> printed = new Dictionary<(Material, int), Material>();

        public static void Apply(GameObject go, string look)
        {
            if (go.GetComponent<U5Look>() != null) return;
            go.AddComponent<U5Look>();
            switch (look)
            {
                case "white_marble": Whiten(go.transform); break;
                case "fountain": Fountain(go); break;
                case "passable": Passable(go); break;
                default:
                    if (look.StartsWith("icon:")) Icon(go, Convert.ToInt32(look.Substring(5), 16));
                    else if (look.StartsWith("drawbridge:")) Drawbridge.Add(go, look.Substring(11));
                    break;
            }
        }

        /// <summary>
        /// Walked through, as ladders are in the original: its colliders move to Valheim's
        /// non-solid piece layer (torches', banners'), which players pass through but can still
        /// point at to use.
        /// </summary>
        static void Passable(GameObject go)
        {
            int layer = LayerMask.NameToLayer("piece_nonsolid");
            foreach (var col in go.GetComponentsInChildren<Collider>(true)) col.gameObject.layer = layer;
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

        /// <summary>
        /// A shop banner printed with a guild icon from the player's TILES.16 (BannerArt): the
        /// cloth gets its own UVs spanning the whole banner (flat in its local y–z plane, u along
        /// +z, v up) and a pixel-art texture; the rod and the wind sway stay Valheim's.
        /// </summary>
        static void Icon(GameObject go, int tile)
        {
            var tiles = Plugin.U5?.Tiles;
            if (tiles == null) return;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var r = mf.GetComponent<Renderer>();
                if (mf.sharedMesh == null || r == null || mf.name.IndexOf("beam", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                var cloth = Unwrapped(mf.sharedMesh);
                if (cloth == null) continue;
                mf.sharedMesh = cloth;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null) mats[i] = Printed(mats[i], tiles, tile);
                r.sharedMaterials = mats;
            }
        }

        /// <summary>A copy of a flat banner cloth with UVs from its own y–z extent, or null if it isn't one.</summary>
        static Mesh Unwrapped(Mesh mesh)
        {
            if (unwrapped.TryGetValue(mesh, out var done)) return done;
            var b = mesh.bounds;
            if (!mesh.isReadable || b.size.x > 0.05f || b.size.y < 0.5f || b.size.z < 0.3f) return unwrapped[mesh] = null;
            var copy = Object.Instantiate(mesh);
            var v = copy.vertices;
            var uv = new Vector2[v.Length];
            for (int i = 0; i < v.Length; i++)
                uv[i] = new Vector2((v[i].z - b.min.z) / b.size.z, (v[i].y - b.min.y) / b.size.y);
            copy.uv = uv;
            return unwrapped[mesh] = copy;
        }

        static Material Printed(Material cloth, Data.TileSet tiles, int tile)
        {
            if (printed.TryGetValue((cloth, tile), out var done)) return done;
            var rgba = BannerArt.Render(tiles, tile);
            var tex = new Texture2D(BannerArt.Width, BannerArt.Height, TextureFormat.RGBA32, false)
            {
                name = $"u5_banner_{tile:x2}", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
            };
            var px = new Color32[BannerArt.Width * BannerArt.Height];
            for (int y = 0; y < BannerArt.Height; y++)
            for (int x = 0; x < BannerArt.Width; x++)
            {
                int o = (y * BannerArt.Width + x) * 4; // BannerArt rows run from the top, Unity's from the bottom
                px[(BannerArt.Height - 1 - y) * BannerArt.Width + x] = new Color32(rgba[o], rgba[o + 1], rgba[o + 2], rgba[o + 3]);
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            // set whether or not the shader reports the property (a dedicated server's shaders are stubs)
            var m = new Material(cloth) { name = $"{cloth.name}_u5icon_{tile:x2}", mainTexture = tex };
            m.SetColor("_Color", Color.white);
            return printed[(cloth, tile)] = m;
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

    /// <summary>
    /// A drawbridge deck (look "drawbridge:dx,dz", the offset from the deck's middle to its
    /// hinge edge): raised at night, lowered in the morning, swinging about the hinge. Every
    /// peer works it out from the shared clock, so nothing is sent.
    /// </summary>
    public class Drawbridge : MonoBehaviour
    {
        const float Raised = 80f, DegreesPerSecond = 15f;
        Vector3 hinge, axis, homePos;
        Quaternion homeRot;
        float angle;
        bool ready;

        public static void Add(GameObject go, string offset)
        {
            var parts = offset.Split(',');
            if (parts.Length != 2) return;
            var d = new Vector3(float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), 0,
                                float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
            go.AddComponent<Drawbridge>().toHinge = d;
        }

        Vector3 toHinge;

        void Start()
        {
            homePos = transform.position;
            homeRot = transform.rotation;
            hinge = homePos + toHinge;
            // the deck's far side is opposite the hinge: turning about (deck → hinge) × up lifts it
            axis = Vector3.Cross(-toHinge, Vector3.up).normalized;
            if (axis.sqrMagnitude < 0.5f) { enabled = false; return; }
            angle = EnvMan.IsNight() ? Raised : 0; // already where it should be when it loads
            ready = true;
            Pose();
        }

        void Update()
        {
            if (!ready) return;
            float target = EnvMan.IsNight() ? Raised : 0;
            if (Mathf.Approximately(angle, target)) return;
            angle = Mathf.MoveTowards(angle, target, DegreesPerSecond * Time.deltaTime);
            Pose();
        }

        void Pose()
        {
            var turn = Quaternion.AngleAxis(angle, axis);
            transform.SetPositionAndRotation(hinge + turn * (homePos - hinge), turn * homeRot);
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
