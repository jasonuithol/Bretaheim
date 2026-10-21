using System;
using System.Collections.Generic;
using Bretaheim.WorldGen.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Bretaheim.WorldGen
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
                case "rune": Rune(go); break;
                case "bare_stand": BareStand(go); break;
                case "see_through": SeeThrough(go); break;
                case "magic_lock": MagicLock(go); break;
                case "bloody": Bloody(go); break;
                case "portcullis": go.AddComponent<Portcullis>(); break;
                case "harpsichord": go.AddComponent<Harpsichord>(); break;
                case "tune": go.AddComponent<TuneWall>(); break;
                case "ghost": foreach (var col in go.GetComponentsInChildren<Collider>(true)) col.enabled = false; break;
                default:
                    if (Furnishings.Apply(go, look)) break;
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

        static Material blood;

        /// <summary>
        /// A torture table splattered with blood: flat stains (a texture of dark red blots and
        /// droplets drawn here) lying on its top.
        /// </summary>
        static void Bloody(GameObject go)
        {
            if (blood == null)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader == null) return;
                blood = new Material(shader) { name = "u5_blood", mainTexture = BloodTexture() };
            }
            const float Top = 0.82f;
            foreach (var (x, z, size, turn) in new[] { (-0.35f, 0.05f, 0.75f, 20f), (0.35f, -0.22f, 0.45f, 140f), (0.8f, 0.3f, 0.3f, 260f), (-0.95f, -0.3f, 0.25f, 75f) })
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.DestroyImmediate(quad.GetComponent<Collider>());
                quad.name = "u5_blood";
                quad.transform.SetParent(go.transform, false);
                quad.transform.localPosition = new Vector3(x, Top, z);
                quad.transform.localRotation = Quaternion.Euler(90, turn, 0);
                quad.transform.localScale = new Vector3(size, size, 1);
                var r = quad.GetComponent<MeshRenderer>();
                r.sharedMaterial = blood;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        /// <summary>Dark red blots with ragged edges and a spray of droplets, on clear.</summary>
        static Texture2D BloodTexture()
        {
            const int S = 128;
            var px = new Color32[S * S];
            var rng = new System.Random(1349);
            var blots = new List<(float x, float y, float r)> { (0.5f, 0.5f, 0.26f) };
            for (int i = 0; i < 7; i++) blots.Add((0.5f + (float)(rng.NextDouble() - 0.5) * 0.4f, 0.5f + (float)(rng.NextDouble() - 0.5) * 0.4f, 0.08f + (float)rng.NextDouble() * 0.1f));
            for (int i = 0; i < 26; i++)
            {
                double a = rng.NextDouble() * Math.PI * 2, d = 0.3 + rng.NextDouble() * 0.17;
                blots.Add((0.5f + (float)(Math.Cos(a) * d), 0.5f + (float)(Math.Sin(a) * d), 0.008f + (float)rng.NextDouble() * 0.022f));
            }
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S, v = (y + 0.5f) / S, best = 0;
                    foreach (var b in blots)
                    {
                        float dx = u - b.x, dy = v - b.y;
                        // a ragged rim: the radius wobbles with the angle
                        float wob = 1 + 0.18f * Mathf.Sin(Mathf.Atan2(dy, dx) * 7 + b.x * 40);
                        best = Mathf.Max(best, 1 - Mathf.Sqrt(dx * dx + dy * dy) / (b.r * wob));
                    }
                    byte alpha = (byte)(best <= 0 ? 0 : Mathf.Clamp(best * 6f, 0, 1) * 215);
                    byte red = (byte)(70 + 40 * Mathf.Clamp01(1 - best));
                    px[y * S + x] = new Color32(red, 4, 6, alpha);
                }
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { name = "u5_blood", wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        static readonly Dictionary<Material, Material> glowing = new Dictionary<Material, Material>();
        /// <summary>The magic lock's glow: cyan (a blue read as Valheim's building-plan blue).</summary>
        static readonly Color MagicBlue = new Color(0.15f, 0.95f, 1f);

        /// <summary>
        /// A magically locked door (MASTER_PLAN §5.2: "magically locked doors glow blue"): its own
        /// materials lit a soft cyan, and a cyan light before it that slowly breathes.
        /// </summary>
        static void MagicLock(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r.GetType().Name == "ParticleSystemRenderer") continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    if (!glowing.TryGetValue(m, out var copy))
                    {
                        copy = new Material(m) { name = m.name + " (magic lock)" };
                        if (copy.HasProperty("_EmissionColor"))
                        {
                            copy.EnableKeyword("_EMISSION");
                            copy.SetColor("_EmissionColor", MagicBlue * 0.25f);
                        }
                        // (the wood keeps its own colour, barely touched)
                        if (copy.HasProperty("_Color")) copy.SetColor("_Color", copy.GetColor("_Color") * new Color(0.92f, 1f, 1f));
                        glowing[m] = copy;
                    }
                    mats[i] = copy;
                }
                r.sharedMaterials = mats;
            }
            var glow = new GameObject("u5_magic_glow");
            glow.transform.SetParent(go.transform, false);
            // (in the doorway's middle, a metre and a half up: the 3 m leaf is hinged at its pivot, a
            // metre along it; the 2 m viewport door's pivot is its middle, a metre up)
            glow.transform.localPosition = Utils.GetPrefabName(go).StartsWith("wood_door") ? new Vector3(0f, 0.5f, 0f) : new Vector3(-1f, 1.5f, 0f);
            var light = glow.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = MagicBlue;
            light.range = 5f;
            light.intensity = 1.2f;
            light.shadows = LightShadows.None;
            glow.AddComponent<Breathe>();
        }

        static readonly Dictionary<Material, Material> faded = new Dictionary<Material, Material>();
        const float SeeThroughAlpha = 0.45f;

        /// <summary>
        /// A floor a ladder passes through: its own material faded to semi-transparent (the
        /// piece shader's standard blend settings, as Unity's Fade mode); the piece stays solid.
        /// </summary>
        static void SeeThrough(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || !m.HasProperty("_SrcBlend")) continue;
                    if (!faded.TryGetValue(m, out var copy))
                    {
                        copy = new Material(m) { name = m.name + " (see-through)" };
                        copy.SetFloat("_Mode", 2f);
                        copy.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        copy.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        copy.SetInt("_ZWrite", 0);
                        copy.DisableKeyword("_ALPHATEST_ON");
                        copy.EnableKeyword("_ALPHABLEND_ON");
                        copy.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                        copy.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                        if (copy.HasProperty("_Color")) { var c = copy.GetColor("_Color"); c.a = SeeThroughAlpha; copy.SetColor("_Color", c); }
                        faded[m] = copy;
                    }
                    mats[i] = copy;
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        /// <summary>An item stand that shows only its item (laid on a table): its own disc is hidden.</summary>
        static void BareStand(GameObject go)
        {
            var attach = go.GetComponent<ItemStand>()?.m_attachOther;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                if (attach == null || !r.transform.IsChildOf(attach)) r.enabled = false;
        }

        static readonly Color RuneTint = new Color(0.88f, 0.9f, 0.96f), RuneGlow = new Color(0.012f, 0.02f, 0.05f);
        static readonly Dictionary<Material, Material> runed = new Dictionary<Material, Material>();

        /// <summary>
        /// The clue on a secret wall (Constructing world assets 78): close up (within a few metres)
        /// its middle bricks are a faintly colder grey with the barest glow, as if a rune were cut
        /// into them; from further off they are plain stone.
        /// </summary>
        static void Rune(GameObject go)
        {
            var near = go.AddComponent<RuneNear>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var plain = r.sharedMaterials;
                var marked = new Material[plain.Length];
                for (int i = 0; i < plain.Length; i++)
                {
                    var m = plain[i];
                    if (m == null) continue;
                    if (!runed.TryGetValue(m, out var copy))
                    {
                        copy = new Material(m) { name = m.name + " (rune)" };
                        if (copy.HasProperty("_Color")) copy.SetColor("_Color", copy.GetColor("_Color") * RuneTint);
                        if (copy.HasProperty("_EmissionColor"))
                        {
                            copy.EnableKeyword("_EMISSION");
                            copy.SetColor("_EmissionColor", RuneGlow);
                        }
                        runed[m] = copy;
                    }
                    marked[i] = copy;
                }
                near.Add(r, plain, marked);
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

    /// <summary>
    /// A magic portcullis (look "portcullis", one of the two stacked iron grates under a gate's
    /// arch): down at night, the drawbridges' hours; in the morning it slides up and vanishes, and
    /// at dusk it appears above the gate and slides down. Every peer works it out from the shared
    /// clock. It is not a door anyone can open.
    /// </summary>
    public class Portcullis : MonoBehaviour
    {
        /// <summary>How far it rises (m) before it is gone, and how fast it moves.</summary>
        const float Lift = 3f, Speed = 1f;
        Vector3 down;
        float lift;
        bool ready, shown = true;
        Renderer[] renderers;
        Collider[] colliders;
        Rigidbody body;

        void Start()
        {
            down = transform.position;
            // (the grate's physics body puts it back where the body is, unless the body moves too)
            body = GetComponent<Rigidbody>();
            renderers = GetComponentsInChildren<Renderer>(true);
            colliders = GetComponentsInChildren<Collider>(true);
            lift = EnvMan.IsNight() ? 0 : Lift; // already where it should be when it loads
            ready = true;
            Pose();
        }

        void Update()
        {
            if (!ready) return;
            float target = EnvMan.IsNight() ? 0 : Lift;
            if (Mathf.Approximately(lift, target)) return;
            lift = Mathf.MoveTowards(lift, target, Speed * Time.deltaTime);
            Pose();
        }

        void Pose()
        {
            transform.position = down + Vector3.up * lift;
            if (body != null) body.position = transform.position;
            // (fully up it is gone; it bars the way only while mostly down)
            bool show = lift < Lift - 0.01f;
            if (show != shown)
            {
                shown = show;
                foreach (var r in renderers) if (r != null) r.enabled = show;
            }
            bool bar = lift < 1f;
            foreach (var c in colliders) if (c != null && c.enabled != bar) c.enabled = bar;
        }

        /// <summary>Down (barring the gate) now.</summary>
        public bool IsDown => lift < 1f;
    }

    /// <summary>A light that slowly breathes (a magically locked door's glow).</summary>
    public class Breathe : MonoBehaviour
    {
        Light light;
        float phase, base_;

        void Awake()
        {
            light = GetComponent<Light>();
            base_ = light != null ? light.intensity : 1f;
            phase = UnityEngine.Random.value * 6f;
        }

        void Update()
        {
            if (light != null) light.intensity = base_ * (0.65f + 0.35f * Mathf.Sin(Time.time * 1.3f + phase));
        }
    }

    /// <summary>A secret wall's marked brick: the mark shows only to someone within a few metres of it.</summary>
    public class RuneNear : MonoBehaviour
    {
        const float Within = 5f, CheckEvery = 0.3f;
        readonly List<(Renderer r, Material[] plain, Material[] marked)> parts = new List<(Renderer, Material[], Material[])>();
        bool showing;
        float checkAt;

        public void Add(Renderer r, Material[] plain, Material[] marked) => parts.Add((r, plain, marked));

        void Update()
        {
            if (Time.time < checkAt) return;
            checkAt = Time.time + CheckEvery + UnityEngine.Random.value * 0.1f;
            var me = Player.m_localPlayer;
            bool near = me != null && Vector3.Distance(me.transform.position, transform.position) < Within;
            if (near == showing) return;
            showing = near;
            foreach (var (r, plain, marked) in parts)
                if (r != null) r.sharedMaterials = near ? marked : plain;
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
            // look_fountain: "a gurgling fountain!" … "Refreshing..." (no effect in the original; here it refreshes the stamina)
            player.AddStamina(player.GetMaxStamina());
            player.Message(MessageHud.MessageType.Center, Furnishings.Say(Data.FurnishingRules.SRefreshing));
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
        public string GetHoverText() => Localization.instance.Localize(Furnishings.Say(Data.FurnishingRules.SFountain) + "\n[<color=yellow><b>$KEY_Use</b></color>] Drink");
        public string GetHoverName() => "Fountain";
        public float GetHoverOffset() => 1f;
    }
}
