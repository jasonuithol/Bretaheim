using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using Bretaheim.Data;
using Bretaheim.WorldGen.Core;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace Bretaheim.WorldGen
{
    /// <summary>
    /// The settlements' one-off furnishings that do something when looked at (Constructing world
    /// assets.txt, Furniture; LOOKOBJ.OVL): the telescope shows the night sky (or the sun, which
    /// hurts), the wishing well takes a coin and hears a wish, the grandfather clocks show
    /// Britannia's time, the graves are read and their earth dug, the mirrors reflect and break,
    /// the crystal sphere gives a vision, and fences are climbed over. Each is a town_tiles.json
    /// "look" (<see cref="Looks"/>) or, for the grave's earth, a prefab of its own.
    /// </summary>
    public static class Furnishings
    {
        static DataOvl Ovl => Plugin.U5.Ovl;
        static readonly Random random = new Random();
        public static string Say(int dg) => Ovl.CStr(dg).Replace("\n", " ").Trim();

        public static void Register() => PrefabManager.OnVanillaPrefabsAvailable += Create;

        public static void Install(Harmony harmony)
        {
            var damage = AccessTools.Method(typeof(WearNTear), "Damage");
            if (damage != null) harmony.Patch(damage, prefix: new HarmonyMethod(typeof(Furnishings), nameof(Damage_Prefix)));
            else Plugin.Log.LogWarning("Furnishings: WearNTear.Damage not found (mirrors will not break)");
        }

        static void Create()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Create;
            var holder = new GameObject("Bretaheim_Furnishings");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            // the earth before a grave: a networked spot with a low mound, dug with E
            var plot = new GameObject(SettlementPlanner.GravePlot);
            plot.transform.SetParent(holder.transform, false);
            plot.layer = LayerMask.NameToLayer("item");
            var nview = plot.AddComponent<ZNetView>();
            nview.m_persistent = true;
            var box = plot.AddComponent<BoxCollider>();
            box.size = new Vector3(1.6f, 0.4f, 1.6f);
            box.center = new Vector3(0, 0.2f, 0);
            plot.AddComponent<GravePlot>();
            PrefabManager.Instance.AddPrefab(new CustomPrefab(plot, false));
        }

        /// <summary>A blow at a mirror breaks it (cmd_attack_town: "Broken!").</summary>
        static void Damage_Prefix(WearNTear __instance, HitData hit)
        {
            var mirror = __instance.GetComponent<MirrorGlass>();
            if (mirror == null) return;
            var attacker = hit?.GetAttacker() as Player;
            mirror.Break(attacker);
        }

        /// <summary>The settlement a position is in, or null.</summary>
        public static Settlements.Built SettlementAt(Vector3 at)
            => Settlements.All.FirstOrDefault(b => Mathf.Max(Mathf.Abs(at.x - b.Origin.x), Mathf.Abs(at.z - b.Origin.z)) < b.Site.Half + 1f);

        public static void Tell(Player p, string text)
        {
            text = text.Trim();
            p.Message(MessageHud.MessageType.Center, text);
            Plugin.Log.LogInfo($"Look: {text.Replace("\n", " / ")}");
        }

        static string Use(string verb) => Localization.instance.Localize($"[<color=yellow><b>$KEY_Use</b></color>] {verb}");

        // ── looks ───────────────────────────────────────────────────────────────────────────

        static readonly Dictionary<Material, Material> stained = new Dictionary<Material, Material>();
        static readonly Color Stain = new Color(0.32f, 0.24f, 0.18f);

        /// <summary>Wood stained a deep dark brown (the telescope).</summary>
        public static void DarkWood(GameObject go)
        {
            if (!Recolour.Draws) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r.GetType().Name == "ParticleSystemRenderer") continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || !m.HasProperty("_Color")) continue;
                    if (!stained.TryGetValue(m, out var copy))
                    {
                        copy = new Material(m) { name = m.name + " (stained)" };
                        copy.SetColor("_Color", copy.GetColor("_Color") * Stain);
                        stained[m] = copy;
                    }
                    mats[i] = copy;
                }
                r.sharedMaterials = mats;
            }
        }

        /// <summary>Looks of town_tiles.json handled here; false when the look is not one of them.</summary>
        public static bool Apply(GameObject go, string look)
        {
            switch (look)
            {
                case "dark_wood": DarkWood(go); return true;
                case "telescope": DarkWood(go); go.AddComponent<TelescopeView>(); return true;
                case "well": go.AddComponent<WishingWell>(); return true;
                case "clock": go.AddComponent<GrandfatherClock>(); return true;
                case "grave": go.AddComponent<GraveMarker>(); return true;
                case "mirror": go.AddComponent<MirrorGlass>(); return true;
                case "sphere": go.AddComponent<CrystalSphere>(); return true;
                default: return false;
            }
        }

        // ── the components ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// The telescope (0x59, look_telescope): by night the sky as the spyglass shows it ("the
        /// night sky!"), from 18 o'clock to 6; by day "the sun!", which hurts the looker (1).
        /// </summary>
        public class TelescopeView : MonoBehaviour, Hoverable, Interactable
        {
            public string GetHoverText() => "Telescope\n" + Use("Look through");
            public string GetHoverName() => "Telescope";
            public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
            public float GetHoverOffset() => 0.5f;

            public bool Interact(Humanoid user, bool hold, bool alt)
            {
                if (hold || !(user is Player p) || p != Player.m_localPlayer) return false;
                if (!ItemUse.TelescopeNight(Calendar.Hour))
                {
                    Tell(p, Say(FurnishingRules.SThouDostSee) + " " + Say(FurnishingRules.SSun));
                    Dungeons.Hurt(p, 1);
                    return true;
                }
                var (year, month, day) = UseItems.Date();
                var stars = ItemUse.NightSky(Ovl, year, month, day, UseItems.ShadowlordTowns());
                Plugin.Log.LogInfo($"Telescope: the sky of {day}-{month}-{year}: {string.Join("; ", stars)}");
                SkyPanel.Show(stars, Say(FurnishingRules.SNightSky));
                return true;
            }
        }


        /// <summary>
        /// The wishing well (0xa1, look_well): "a well. Drop a coin?" — with gold, E drops one and
        /// asks "Thy wish?"; a horse wished for (Corvette, Ferrari, Lamborghini, Lotus, Porsche,
        /// Horse) in Paws or Empath Abbey appears beside the well ("Poof!", a tame Lox); anything
        /// else has "No effect...", an empty wish is "Nothing".
        /// </summary>
        public class WishingWell : MonoBehaviour, Hoverable, Interactable, TextReceiver
        {
            static int Coins(Player p) => p?.GetInventory()?.CountItems("$item_coins") ?? 0;

            public string GetHoverText()
            {
                string name = Plugin.U5.TileNames[FurnishingRules.Well];
                return Coins(Player.m_localPlayer) > 0 ? name + "\n" + Use("Drop a coin") : name;
            }

            public string GetHoverName() => Plugin.U5.TileNames[FurnishingRules.Well];
            public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
            public float GetHoverOffset() => 0.5f;

            public bool Interact(Humanoid user, bool hold, bool alt)
            {
                if (hold || !(user is Player p) || p != Player.m_localPlayer) return false;
                if (Coins(p) == 0) { Tell(p, Plugin.U5.TileNames[FurnishingRules.Well]); return true; }
                p.GetInventory().RemoveItem("$item_coins", 1);
                Tell(p, Say(FurnishingRules.SYes));
                TextInput.instance?.RequestText(this, Say(FurnishingRules.SThyWish), 12);
                return true;
            }

            public string GetText() => "";

            public void SetText(string text)
            {
                var p = Player.m_localPlayer;
                if (p == null) return;
                int location = SettlementAt(transform.position)?.Location.Number ?? 0;
                var result = FurnishingRules.Wish(Ovl, text, location);
                Tell(p, FurnishingRules.WishWords(Ovl, result));
                if (result != FurnishingRules.WishResult.Horse) return;
                // a horse at x + 1 (the original's map cell east of the party): a tame Lox, the Horse of items.json
                var lox = ZNetScene.instance?.GetPrefab("Lox");
                if (lox == null) return;
                var at = transform.position + Vector3.right * 3f;
                var go = Object.Instantiate(lox, at, Quaternion.Euler(0, random.Next(360), 0));
                go.GetComponent<Character>()?.SetTamed(true);
                var sfx = ZNetScene.instance.GetPrefab("vfx_spawn");
                if (sfx != null) Object.Instantiate(sfx, at, Quaternion.identity);
                Plugin.Log.LogInfo($"Wishing well: a horse at {at}");
            }
        }

        /// <summary>
        /// A grandfather clock (0xfa/0xfb): a post with a face (two signs back to back) that shows
        /// Britannia's time; looked at, "a grandfather clock, showing: 3:05 PM." (look_terrain).
        /// Its face is ours to write: the sign's own script (an editable sign) is taken off.
        /// </summary>
        public class GrandfatherClock : MonoBehaviour, Hoverable, Interactable
        {
            TMPro.TMP_Text face;
            int shown = -1;
            float nextAt;

            void Start()
            {
                var sign = GetComponent<Sign>();
                if (sign != null)
                {
                    face = sign.m_textWidget;
                    Destroy(sign);
                }
            }

            void Update()
            {
                if (face == null || Time.time < nextAt) return;
                nextAt = Time.time + 1f;
                int now = Calendar.Hour * 60 + Calendar.Minute;
                if (now == shown) return;
                shown = now;
                int h = Calendar.Hour % 12;
                face.text = $"{(h == 0 ? 12 : h)}:{Calendar.Minute:00}";
            }

            string Text() => FurnishingRules.ClockText(Ovl, Plugin.U5.TileNames, FurnishingRules.ClockA, Calendar.Hour, Calendar.Minute);
            public string GetHoverText() => Text();
            public string GetHoverName() => "Clock";
            public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
            public float GetHoverOffset() => 0.5f;

            public bool Interact(Humanoid user, bool hold, bool alt)
            {
                if (hold || !(user is Player p) || p != Player.m_localPlayer) return false;
                Tell(p, Text());
                return true;
            }
        }

        /// <summary>A grave's marker (0x89/0x8a): its eulogy (SIGNS.DAT, the piece's text) read off it.</summary>
        public class GraveMarker : MonoBehaviour, Hoverable, Interactable
        {
            string Eulogy() => GetComponent<ZNetView>()?.GetZDO()?.GetString(ZDOVars.s_text) ?? "";

            public string GetHoverText()
            {
                string e = Eulogy();
                return e.Length > 0 ? $"\"{e.Replace("\n", " ")}\"\n" + Use("Read") : "A grave";
            }

            public string GetHoverName() => "Grave";
            public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
            public float GetHoverOffset() => 0.5f;

            public bool Interact(Humanoid user, bool hold, bool alt)
            {
                if (hold || !(user is Player p) || p != Player.m_localPlayer) return false;
                string e = Eulogy();
                Tell(p, e.Length > 0 ? e : "A grave, its words worn away.");
                return true;
            }
        }

        /// <summary>
        /// The earth before a grave (ours, Constructing world assets.txt): dug once (E): only
        /// earth, a body (bones), a body that rises (a skeleton), or now and then treasure.
        /// </summary>
        public class GravePlot : MonoBehaviour, Hoverable, Interactable
        {
            public const string DugKey = "u5_dug";
            ZNetView nview;
            GameObject mound;

            void Awake()
            {
                nview = GetComponent<ZNetView>();
                if (Recolour.Draws) mound = Visual("mudpile", 0.4f, transform);
            }

            bool Dug => nview?.GetZDO()?.GetBool(DugKey) ?? true;

            public string GetHoverText() => Dug ? "Disturbed earth" : "The earth of a grave\n" + Use("Dig");
            public string GetHoverName() => "Grave";
            public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
            public float GetHoverOffset() => 0.5f;

            public bool Interact(Humanoid user, bool hold, bool alt)
            {
                if (hold || !(user is Player p) || p != Player.m_localPlayer || nview == null || !nview.IsValid()) return false;
                if (Dug) { Tell(p, "This grave has been dug already."); return true; }
                nview.ClaimOwnership();
                nview.GetZDO().Set(DugKey, true);
                var at = transform.position;
                var dig = FurnishingRules.DigUp(random.Next(100));
                Plugin.Log.LogInfo($"Grave at {at}: {dig}");
                switch (dig)
                {
                    case FurnishingRules.Dig.Dirt:
                        Tell(p, "Naught but earth.");
                        break;
                    case FurnishingRules.Dig.Body:
                    case FurnishingRules.Dig.Risen:
                        Drop("BoneFragments", random.Next(2, 5), at);
                        if (dig == FurnishingRules.Dig.Body) { Tell(p, "Thou hast unearthed a body!"); break; }
                        Tell(p, "Thou hast unearthed a body!\nThe dead rise!");
                        var skeleton = ZNetScene.instance?.GetPrefab("Skeleton");
                        if (skeleton != null) Object.Instantiate(skeleton, at + Vector3.up * 0.3f, Quaternion.LookRotation(p.transform.position - at));
                        break;
                    default:
                        var found = new List<string>();
                        foreach (var loot in LootTables.Chest(Ovl, 8, random)) found.Add(Treasure.Give(p.GetInventory(), loot, at));
                        found.RemoveAll(s => s.Length == 0);
                        Tell(p, found.Count > 0 ? "Buried with the dead: " + string.Join(", ", found) + "!" : "Naught but earth.");
                        break;
                }
                return true;
            }

            static void Drop(string prefabName, int count, Vector3 at)
            {
                var drop = ObjectDB.instance?.GetItemPrefab(prefabName)?.GetComponent<ItemDrop>();
                if (drop != null) Treasure.DropOnGround(drop.gameObject, count, at + Vector3.up);
            }
        }

        /// <summary>
        /// The crystal sphere (0x29, cmd_look): a roll of 1..30 at or above the looker's
        /// intelligence is a "Death vision!" (1 damage); below it a "Strange vision!" and the
        /// gem's map of where they stand.
        /// </summary>
        public class CrystalSphere : MonoBehaviour, Hoverable, Interactable
        {
            public string GetHoverText() => Plugin.U5.TileNames[FurnishingRules.Sphere] + "\n" + Use("Gaze into");
            public string GetHoverName() => Plugin.U5.TileNames[FurnishingRules.Sphere];
            public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
            public float GetHoverOffset() => 0.5f;

            public bool Interact(Humanoid user, bool hold, bool alt)
            {
                if (hold || !(user is Player p) || p != Player.m_localPlayer) return false;
                int roll = random.Next(FurnishingRules.VisionRollMin, FurnishingRules.VisionRollMax + 1);
                if (!FurnishingRules.StrangeVision(AvatarStats.Mine.Intelligence, roll))
                {
                    Tell(p, Say(FurnishingRules.SDeathVision));
                    Dungeons.Hurt(p, 1);
                    return true;
                }
                Tell(p, Say(FurnishingRules.SStrangeVision));
                UseItems.ShowMap();
                return true;
            }
        }

        /// <summary>A visual copy of a prefab (no scripts, physics or colliders) under a parent.</summary>
        public static GameObject Visual(string prefabName, float scale, Transform parent)
        {
            var prefab = ZNetScene.instance?.GetPrefab(prefabName) ?? ObjectDB.instance?.GetItemPrefab(prefabName);
            if (prefab == null) return null;
            var holder = new GameObject("u5_visual_holder");
            holder.SetActive(false);
            var copy = Object.Instantiate(prefab, holder.transform);
            for (int pass = 0; pass < 4; pass++)
                foreach (var c in copy.GetComponentsInChildren<Component>(true))
                    if (c is MonoBehaviour || c is Rigidbody || c is Collider) Object.DestroyImmediate(c);
            copy.transform.SetParent(parent, false);
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localScale *= scale;
            Object.Destroy(holder);
            copy.SetActive(true);
            return copy;
        }
    }

    /// <summary>
    /// A standing mirror (0x9d): the crystal pane reflects what stands before it (a camera at the
    /// looker's eye mirrored through the pane, its view cut to the pane: the original draws the
    /// Avatar in it, tile 0x9e), drawn only on machines that draw and while a player is near and
    /// in front. A blow breaks it for good ("Broken!", tile 0x9f): the reflection is gone and the
    /// glass darkens.
    /// </summary>
    public class MirrorGlass : MonoBehaviour, Hoverable, Interactable
    {
        public const string BrokenKey = "u5_broken";
        const float Near = 9f;
        const int Width = 256, Height = 512;
        ZNetView nview;
        Camera eye;
        RenderTexture texture;
        GameObject glass;
        bool broken;
        float checkAt;
        // the pane in the piece's own space (crystal_wall_1x1: 1 m wide, x −0.49..0.51, y 0.01..1.01, the
        // piece stretched to 2 m tall), its reflecting face on the −z side (the room: mirrors stand at north walls)
        const float X0 = -0.49f, X1 = 0.51f, Y0 = 0.014f, Y1 = 1.014f, Face = -0.16f;

        void Awake() => nview = GetComponent<ZNetView>();

        void Start()
        {
            broken = nview?.GetZDO()?.GetBool(BrokenKey) ?? false;
            if (broken) ShowBroken();
            else if (Recolour.Draws) Build();
        }

        void OnDestroy()
        {
            if (texture != null) texture.Release();
            if (eye != null) Destroy(eye.gameObject);
        }

        void Build()
        {
            texture = new RenderTexture(Width, Height, 16) { name = "u5_mirror" };
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Texture");
            if (shader == null) return;
            glass = new GameObject("u5_mirror_glass");
            glass.transform.SetParent(transform, false);
            var mesh = new Mesh { name = "u5_mirror_quad" };
            mesh.vertices = new[] { new Vector3(X0, Y0, Face), new Vector3(X1, Y0, Face), new Vector3(X0, Y1, Face), new Vector3(X1, Y1, Face) };
            // seen from −z the quad's left is x1: the reflected view is laid mirror-wise (u 0 at x1)
            mesh.uv = new[] { new Vector2(1, 0), new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateNormals();
            glass.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = glass.AddComponent<MeshRenderer>();
            r.sharedMaterial = new Material(shader) { mainTexture = texture };
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var go = new GameObject("u5_mirror_eye");
            eye = go.AddComponent<Camera>();
            eye.enabled = false;
            eye.targetTexture = texture;
            go.AddComponent<MirrorEye>().Mirror = this;
            glass.SetActive(false);
        }

        void Update()
        {
            if (Time.time >= checkAt)
            {
                checkAt = Time.time + 1f;
                if (!broken && (nview?.GetZDO()?.GetBool(BrokenKey) ?? false)) { broken = true; ShowBroken(); }
            }
            if (eye == null || broken) return;
            var main = Utils.GetMainCamera();
            bool on = main != null && Vector3.Distance(main.transform.position, transform.position) < Near
                      && transform.InverseTransformPoint(main.transform.position).z < Face;
            if (on)
            {
                eye.CopyFrom(main);
                eye.targetTexture = texture;
                eye.depth = main.depth - 1;
            }
            eye.enabled = on;
            glass.SetActive(on);
        }

        /// <summary>Places the eye for this frame: the main camera's position mirrored through the pane, looking square at it, its view the pane exactly.</summary>
        public void Aim()
        {
            var main = Utils.GetMainCamera();
            if (main == null || eye == null) return;
            var t = transform;
            // the pane in world space (the piece is scaled: work in its own space, then out)
            Vector3 e = t.InverseTransformPoint(main.transform.position);
            var mirrored = new Vector3(e.x, e.y, 2 * Face - e.z);
            Vector3 worldEye = t.TransformPoint(mirrored);
            // the eye looks along the pane's normal into the room (−z), upright
            var forward = t.TransformDirection(Vector3.back).normalized;
            var up = t.TransformDirection(Vector3.up).normalized;
            eye.transform.SetPositionAndRotation(worldEye, Quaternion.LookRotation(forward, up));
            // the pane's corners as the eye sees them: its right is the piece's −x
            Vector3 c0 = eye.transform.InverseTransformPoint(t.TransformPoint(new Vector3(X1, Y0, Face)));
            Vector3 c1 = eye.transform.InverseTransformPoint(t.TransformPoint(new Vector3(X0, Y1, Face)));
            float d = c0.z;
            if (d < 0.02f) return;
            float near = d * 0.999f, far = main.farClipPlane;
            float k = near / d;
            eye.projectionMatrix = Matrix4x4.Frustum(Mathf.Min(c0.x, c1.x) * k, Mathf.Max(c0.x, c1.x) * k, Mathf.Min(c0.y, c1.y) * k, Mathf.Max(c0.y, c1.y) * k, near, far);
        }

        public void Break(Player by)
        {
            if (broken || nview?.GetZDO() == null) return;
            broken = true;
            nview.ClaimOwnership();
            nview.GetZDO().Set(BrokenKey, true);
            ShowBroken();
            var sfx = ZNetScene.instance?.GetPrefab("sfx_crystal_destroyed") ?? ZNetScene.instance?.GetPrefab("fx_crystal_destruction");
            if (sfx != null) Instantiate(sfx, transform.position + Vector3.up, Quaternion.identity);
            if (by != null && by == Player.m_localPlayer) Furnishings.Tell(by, Furnishings.Say(FurnishingRules.SBroken));
        }

        void ShowBroken()
        {
            if (eye != null) Destroy(eye.gameObject);
            if (glass != null) Destroy(glass);
            if (!Recolour.Draws) return;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.materials;
                foreach (var m in mats)
                    if (m.HasProperty("_Color")) m.SetColor("_Color", m.GetColor("_Color") * new Color(0.35f, 0.35f, 0.38f));
                r.materials = mats;
            }
        }

        string Name => Plugin.U5.TileNames[broken ? FurnishingRules.MirrorBroken : FurnishingRules.Mirror];
        public string GetHoverText() => Name;
        public string GetHoverName() => Name;
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
        public float GetHoverOffset() => 0.5f;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !(user is Player p) || p != Player.m_localPlayer) return false;
            // (standing before it, the original shows the Avatar in the glass: 0x9e)
            Furnishings.Tell(p, broken ? Name : Plugin.U5.TileNames[FurnishingRules.MirrorReflecting]);
            return true;
        }

        /// <summary>The mirror's camera: aimed just before it draws (after everything has moved this frame).</summary>
        class MirrorEye : MonoBehaviour
        {
            public MirrorGlass Mirror;
            void OnPreCull() => Mirror?.Aim();
        }
    }

    /// <summary>
    /// A fence (0x43, 0xca, 0xcb: Klimable fences, round poles) in a settlement's combined shell:
    /// E climbs over it to the other side.
    /// </summary>
    public class FenceKlimb : MonoBehaviour, Hoverable, Interactable
    {
        public string GetHoverText() => Localization.instance.Localize("Fence\n[<color=yellow><b>$KEY_Use</b></color>] Klimb over");
        public string GetHoverName() => "Fence";
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
        public float GetHoverOffset() => 0.5f;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !(user is Player p) || p != Player.m_localPlayer) return false;
            // wood_fence runs along its local x and is crossed along z (its middle is 0.15 m in)
            var t = transform;
            const float Middle = 0.15f, Beyond = 0.9f;
            float side = t.InverseTransformPoint(p.transform.position).z - Middle;
            var local = t.InverseTransformPoint(p.transform.position);
            var to = t.TransformPoint(new Vector3(Mathf.Clamp(local.x, -1f, 1f), 0, Middle - Mathf.Sign(side) * Beyond));
            to.y = Mathf.Max(to.y, p.transform.position.y) + 0.1f;
            var body = p.GetComponent<Rigidbody>();
            p.transform.position = to;
            if (body != null) { body.position = to; body.linearVelocity = Vector3.zero; }
            return true;
        }
    }
}
