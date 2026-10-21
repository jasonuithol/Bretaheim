using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Bretaheim.Data;
using Bretaheim.WorldGen.Core;
using UnityEngine;

namespace Bretaheim.WorldGen
{
    /// <summary>
    /// The moongates and moonstones (MASTER_PLAN §5.8; ROOT update_moongates, enter_moongate,
    /// moongate_travel; CAST bury_moonstone). The eight moonstones lie where INIT.GAM buried them
    /// (in open grassland; they are dug up as Treasure's mounds); where one is moved to is kept in
    /// the world (global key u5_moonstone&lt;n&gt;: "x,y,z[,wx,wz]" or "carried"). By night (20:00 to
    /// 4:59 Ultima time) a moongate rises out of the ground at every buried moonstone of Britannia
    /// over four seconds and sinks again at five; walking into a risen gate carries the player
    /// (Valheim's teleport, its loading screen) to moonstone n's site, n the phase of Felucca in
    /// the evening and of Trammel after midnight (Core/Moons). A dug-up moonstone's gate is gone,
    /// and a gate whose way leads to a carried stone does nothing. Using a moonstone (from the
    /// inventory) buries it where the player stands, on soft ground of the land, and a gate will
    /// rise there. The gates are drawn by each player's own machine (they are the calendar's, not
    /// objects of the world).
    /// </summary>
    public static class Moongates
    {
        public const string SiteKey = "u5_moonstone";
        // DATA.OVL "Moonstone ", "buried!", "cannot be buried here!"
        const int SMoonstone = 0x4768, SBuried = 0x4773, SCannot = 0x477c;

        /// <summary>Where moonstone <paramref name="i"/> is now: as moved in this world, else as INIT.GAM has it.</summary>
        public static MoonstoneSite Site(int i)
        {
            var kept = WorldKeys.Get(SiteKey + i);
            if (kept != null && MoonstoneSite.TryParse(kept, out var site)) return site;
            var m = Plugin.U5.InitialGame.Moonstone(i);
            return m.buried ? MoonstoneSite.At(m.x, m.y, m.z) : MoonstoneSite.InPack;
        }

        /// <summary>Whether moonstone <paramref name="i"/> still lies where INIT.GAM buried it (Treasure places its mound when the land is made).</summary>
        public static bool StillAt(int i, int x, int y, int z = 0)
        {
            var s = Site(i);
            return s.Buried && (s.Z == 0) == (z == 0) && s.X == x && s.Y == y && !s.WorldX.HasValue;
        }

        /// <summary>The spot on the ground of a site, on the surface or (z ≠ 0) in the Underworld, or null (carried).</summary>
        public static Vector3? Position(MoonstoneSite s)
        {
            var shape = Britannia.Active == null ? null : Britannia.Level(s.Z);
            if (shape == null || !s.Buried) return null;
            float wx, wz;
            if (s.WorldX.HasValue && s.WorldZ.HasValue) { wx = s.WorldX.Value; wz = s.WorldZ.Value; }
            else shape.TileCentre(s.X, s.Y, out wx, out wz);
            var at = new Vector3(wx, Britannia.SeaLevel + Mathf.Max(shape.Height(wx, wz), 0f), wz);
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(at, out float h)) at.y = h;
            return at;
        }

        /// <summary>Whether a gate stands at moonstone <paramref name="i"/>'s site now (it is buried and it is night).</summary>
        public static bool GateUpAt(int i) => Moons.GatesUp(Calendar.Hour) && Site(i).Buried;

        /// <summary>Moonstone <paramref name="i"/> was dug up (Treasure): its site is gone.</summary>
        public static void DugUp(int i)
        {
            WorldKeys.Set(SiteKey + i, MoonstoneSite.Carried);
            Plugin.Log.LogInfo($"Moonstone {i} dug up: its moongate is gone");
        }

        public static void Install(Harmony harmony)
        {
            SpellEffects.GateTravel = GateTravel;
            var use = AccessTools.Method(typeof(Humanoid), nameof(Humanoid.UseItem));
            if (use != null) harmony.Patch(use, prefix: new HarmonyMethod(typeof(Moongates), nameof(UseItem_Prefix)));
            else Plugin.Log.LogWarning("Moongates: Humanoid.UseItem not found (moonstones cannot be buried)");
        }

        /// <summary>
        /// Vas Rel Por (cast_vas_rel_por_gate_travel): "To phase:" 1..8, then the party is at that
        /// moonstone's burial place (moongate_travel; nothing when it is carried). Not aboard a ship.
        /// </summary>
        public static bool GateTravel(Player me, int phase)
        {
            if (me == null || me.IsAttachedToShip()) return false;
            if (phase >= 0) return TravelTo(me, phase);
            PhasePrompt.Ask(me);
            return true;
        }

        static bool TravelTo(Player me, int stone)
        {
            var site = Site(stone);
            var dest = site.Buried ? Position(site) : null;
            if (dest == null) { me.Message(MessageHud.MessageType.Center, "Failed!"); return false; }
            SpellStatus.Fx("vfx_odin_despawn", me.transform.position, 3f);
            me.TeleportTo(dest.Value + Vector3.back * 3f + Vector3.up * 0.5f, Quaternion.LookRotation(Vector3.back), true);
            Plugin.Log.LogInfo($"{me.GetPlayerName()} casts Vas Rel Por to moonstone {stone}");
            return true;
        }

        /// <summary>The spell's question, "To phase:" (the text box; answered 1..8).</summary>
        class PhasePrompt : TextReceiver
        {
            static readonly PhasePrompt one = new PhasePrompt();
            public static void Ask(Player me) => Plugin.Instance.StartCoroutine(Later());

            static System.Collections.IEnumerator Later()
            {
                // (asked while a key or a text box's Enter is still being handled)
                yield return null;
                TextInput.instance?.RequestText(one, Plugin.U5.Ovl.CStr(0x45e7).Trim().TrimEnd(':'), 1);
            }

            public string GetText() => "";

            public void SetText(string text)
            {
                var me = Player.m_localPlayer;
                if (me == null) return;
                if (text != null && text.Length == 1 && text[0] >= '1' && text[0] <= '8') TravelTo(me, text[0] - '1');
                else me.Message(MessageHud.MessageType.Center, "Failed!");
            }
        }

        /// <summary>Using a moonstone buries it (CAST bury_moonstone).</summary>
        static bool UseItem_Prefix(Humanoid __instance, Inventory inventory, ItemDrop.ItemData item)
        {
            if (!(__instance is Player p) || p != Player.m_localPlayer || item?.m_dropPrefab == null || UltimaItems.Map == null) return true;
            var def = UltimaItems.Map.Get(item.m_dropPrefab.name);
            if (def == null || def.Kind != ItemKind.Moonstone) return true;
            p.Message(MessageHud.MessageType.Center, Bury(p, def.Id, inventory ?? p.GetInventory(), item));
            return false;
        }

        /// <summary>Buries moonstone <paramref name="stone"/> where the player stands, if the ground there takes it; what is said.</summary>
        public static string Bury(Player p, int stone, Inventory inv, ItemDrop.ItemData item)
        {
            var ovl = Plugin.U5.Ovl;
            string cannot = (ovl.CStr(SMoonstone) + ovl.CStr(SCannot)).Trim();
            if (Britannia.Active == null) return cannot;
            var at = p.transform.position;
            var shape = Britannia.ShapeAt(at);
            int z = shape == Britannia.Active ? 0 : -1;
            shape.ToTile(at.x, at.z, out double u, out double v);
            int tx = ((int)System.Math.Floor(u) % 256 + 256) % 256, ty = ((int)System.Math.Floor(v) % 256 + 256) % 256;
            byte tile = shape.TileAt(at.x, at.z);
            bool inSettlement = shape.Sites.Any(s => Mathf.Max(Mathf.Abs(at.x - s.CentreX), Mathf.Abs(at.z - s.CentreZ)) <= s.Half + s.Blend);
            float ground = Britannia.SeaLevel + shape.Height(at.x, at.z);
            // (only into the land's soft ground: not in a settlement's square, in water, or below ground)
            if (!MoonstoneSite.CanBuryOn(tile) || inSettlement || p.InWater() || at.y < ground - 6f || shape.OutsideTiles(u, v) > 0)
                return cannot;
            var site = MoonstoneSite.At(tx, ty, z, at.x, at.z);
            WorldKeys.Set(SiteKey + stone, site.Format());
            inv.RemoveItem(item, 1);
            Treasure.BuryMoonstone(at, stone);
            Plugin.Log.LogInfo($"{p.GetPlayerName()} buried moonstone {stone} at tile ({tx},{ty}) {at}");
            return (ovl.CStr(SMoonstone) + ovl.CStr(SBuried)).Trim();
        }
    }

    /// <summary>
    /// The moongates near the local player: raised and sunk with the night (update_moongates'
    /// sixteen frames as four seconds), and walking into a risen one (enter_moongate). On a
    /// headless server there is nobody to carry and nothing to draw.
    /// </summary>
    public class MoongateKeeper : MonoBehaviour
    {
        const float Near = 300f, Reach = 1.4f, RiseSeconds = 4f, Sunk = 6f, ArriveSouth = 3f;

        class Gate
        {
            public int Stone;
            public Vector3 At;
            public GameObject Body;
            public float Height;
        }

        readonly Dictionary<int, Gate> gates = new Dictionary<int, Gate>();
        float scanAt;
        bool armed = true;
        int lastHour = -1;

        void Update()
        {
            var me = Player.m_localPlayer;
            if (me == null || Britannia.Active == null || Plugin.U5 == null || ZNetScene.instance == null)
            {
                Clear();
                return;
            }
            int hour = Calendar.Hour;
            bool up = Moons.GatesUp(hour);
            if (lastHour >= 0 && hour != lastHour && hour == 20) Dusk(me);
            lastHour = hour;
            if (Time.time >= scanAt)
            {
                scanAt = Time.time + 1f;
                Scan(me.transform.position);
            }
            Gate inside = null;
            foreach (var g in gates.Values)
            {
                g.Height = Mathf.MoveTowards(g.Height, up ? 1f : 0f, Time.deltaTime / RiseSeconds);
                if (g.Body != null)
                {
                    g.Body.SetActive(g.Height > 0);
                    g.Body.transform.position = g.At + Vector3.down * Sunk * (1f - g.Height);
                }
                var d = me.transform.position - g.At;
                if (Mathf.Abs(d.y) < 3f && new Vector2(d.x, d.z).magnitude < Reach) inside = g;
            }
            if (inside == null)
            {
                armed = true;
                return;
            }
            if (!armed || inside.Height < 1f || me.IsTeleporting() || me.IsDead()) return;
            armed = false;
            Enter(me, inside);
        }

        /// <summary>The gates of the buried moonstones within reach of the player (new ones made, far ones dropped).</summary>
        void Scan(Vector3 from)
        {
            for (int i = 0; i < 8; i++)
            {
                var at = Moongates.Position(Moongates.Site(i));
                bool want = at.HasValue && Vector2.Distance(new Vector2(at.Value.x, at.Value.z), new Vector2(from.x, from.z)) < Near;
                gates.TryGetValue(i, out var g);
                if (want && g != null && (g.At - at.Value).sqrMagnitude > 1f) { Drop(i); g = null; }
                if (want && g == null)
                {
                    gates[i] = new Gate { Stone = i, At = at.Value, Body = Recolour.Draws ? Build(at.Value) : null, Height = 0f };
                    if (Moons.GatesUp(Calendar.Hour)) gates[i].Height = 1f;
                }
                else if (!want && g != null) Drop(i);
            }
        }

        void Drop(int i)
        {
            if (gates.TryGetValue(i, out var g) && g.Body != null) Destroy(g.Body);
            gates.Remove(i);
        }

        void Clear()
        {
            foreach (var i in gates.Keys.ToList()) Drop(i);
            lastHour = -1;
        }

        void OnDestroy() => Clear();

        /// <summary>Dusk: the moons and where the gates lead tonight.</summary>
        static void Dusk(Player me)
        {
            var now = Calendar.Now;
            me.Message(MessageHud.MessageType.TopLeft, $"The moons rise: Trammel {Moons.PhaseName(Calendar.Moons.Trammel(now.Day))}, Felucca {Moons.PhaseName(Calendar.Moons.Felucca(now.Day))}");
        }

        /// <summary>enter_moongate: to the site of the moonstone the moons name (nothing when it is carried or is this one).</summary>
        void Enter(Player me, Gate from)
        {
            var now = Calendar.Now;
            int to = Calendar.Moons.Destination(now);
            if (to == Moons.Spirituality)
            {
                // the original's shrine of Spirituality (CAST2 visit_shrine) is not built yet
                Plugin.Log.LogInfo($"Moongate at {now}: the way to the Shrine of Spirituality (not built yet)");
                me.Message(MessageHud.MessageType.Center, "The gate shimmers toward the Shrine of Spirituality, but its way is closed.");
                return;
            }
            var dest = Moongates.Position(Moongates.Site(to));
            if (dest == null || to == from.Stone)
            {
                Plugin.Log.LogInfo($"Moongate {from.Stone} at {now}: leads to moonstone {to}, {(dest == null ? "not buried" : "this one")}: nothing happens");
                return;
            }
            // out in front of the gate (south), looking away from it
            var arrive = dest.Value + Vector3.back * ArriveSouth + Vector3.up * 0.5f;
            Plugin.Log.LogInfo($"{me.GetPlayerName()} steps into moongate {from.Stone} at {now}: to moonstone {to} at {dest.Value}");
            me.TeleportTo(arrive, Quaternion.LookRotation(Vector3.back), true);
        }

        /// <summary>
        /// A moongate: Valheim's stone portal with its swirl lit (as when a portal finds its
        /// target), stripped to its look (no piece, no collider, no network object), plus a
        /// cold blue light.
        /// </summary>
        static GameObject Build(Vector3 at)
        {
            var prefab = ZNetScene.instance.GetPrefab("portal_stone") ?? ZNetScene.instance.GetPrefab("portal_wood");
            if (prefab == null) return null;
            var holder = new GameObject("u5_moongate_holder");
            holder.SetActive(false);
            var copy = Instantiate(prefab, holder.transform);
            var portal = copy.GetComponent<TeleportWorld>();
            var swirl = portal != null ? portal.m_target_found : null;
            var model = portal != null ? portal.m_model : null;
            var glow = portal != null ? portal.m_colorTargetfound : Color.cyan;
            for (int pass = 0; pass < 4; pass++)
                foreach (var c in copy.GetComponentsInChildren<Component>(true))
                    if ((c is MonoBehaviour && !(c is EffectFade)) || c is Collider || c is Rigidbody) DestroyImmediate(c);
            copy.transform.SetParent(null, false);
            Destroy(holder);
            copy.name = "u5_moongate";
            copy.transform.position = at + Vector3.down * Sunk;
            copy.transform.rotation = Quaternion.identity;
            var light = new GameObject("u5_moongate_light").AddComponent<Light>();
            light.transform.SetParent(copy.transform, false);
            light.transform.localPosition = Vector3.up * 2f;
            light.type = LightType.Point;
            light.color = new Color(0.45f, 0.6f, 1f);
            light.range = 12f;
            light.intensity = 1.6f;
            copy.SetActive(true);
            if (swirl != null)
            {
                swirl.gameObject.SetActive(true);
                swirl.SetActive(true);
            }
            if (model != null) model.material.SetColor("_EmissionColor", glow);
            return copy;
        }
    }
}
