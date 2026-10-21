using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using Bretaheim.Data;
using UnityEngine;

namespace Bretaheim.WorldGen
{
    /// <summary>
    /// What the rings, amulets and regalia do when worn (MASTER_PLAN §5.5), as the original has
    /// them, shown as Valheim status effects with the looks of assets/mappings/status.json (in
    /// place of the trinkets' own Valheim effects):
    /// Ring of Invisibility — no monster picks the wearer as its target (find_nearest_enemy);
    /// Ring of Regeneration — each 8 s turn a 1-in-8 roll heals 1 hit point; either ring vanishes
    /// on a 1-in-16 roll as a fight begins ("A ring has vanished!"); Ring of Protection and the
    /// Spiked Collar — their defence (2) only, counted with the armour; the Amulet of Turning and
    /// the Ankh — nothing (the original's turning roll is thrown away). Regalia reach every player
    /// within one tile (32 m) of the wearer: the Crown of Lord British — a blue barrier through
    /// which no magic attack passes (the original's Crown icon stops spell-casting monsters'
    /// missiles and special actions); the Amulet of Lord British — +50 armour and a silver Rested.
    /// The Black Badge is only worn (Blackthorn's guards, <see cref="BlackthornGuards"/>). Kept on
    /// the player's ZDO (<see cref="WornKey"/>) so every machine knows who wears what.
    /// </summary>
    public static class WornEffects
    {
        public const string WornKey = "u5_worn";
        public const int Invisible = 1, Crown = 2, Amulet = 4, Badge = 8;
        /// <summary>A turn: the encounter rate's 8 s (about a 32 m tile at a walk).</summary>
        public const float Turn = 8f;
        /// <summary>A fight begins when an Ultima monster is roused this near after this long without one.</summary>
        const float FightNear = 30f, FightGap = 60f;

        static readonly Dictionary<int, StatusEffect> worn = new Dictionary<int, StatusEffect>();
        static readonly Dictionary<string, StatusEffect> party = new Dictionary<string, StatusEffect>();
        static readonly Dictionary<string, ItemUse.Look> partyLooks = new Dictionary<string, ItemUse.Look>();
        static float radiusTiles = 1;
        static bool made;
        static readonly System.Random random = new System.Random();

        public static float Radius => radiusTiles * Plugin.TileMetres.Value;

        static string Prefab(string special) => UltimaItems.Map.Special(special)?.Prefab ?? "";
        public static string CrownPrefab => Prefab("Crown of Lord British");
        public static string AmuletPrefab => Prefab("Amulet of Lord British");
        public static string BadgePrefab => Prefab("Black Badge");
        public static string GrapplePrefab => Prefab("Grapple");

        public static void Install(Harmony harmony)
        {
            Patch(harmony, typeof(Character), "RPC_Damage", prefix: nameof(RPC_Damage_Prefix), first: true);
            Patch(harmony, typeof(Player), "GetBodyArmor", postfix: nameof(GetBodyArmor_Postfix));
            Patch(harmony, typeof(MonsterAI), "UpdateTarget", prefix: nameof(UpdateTarget_Prefix));
            Patch(harmony, typeof(Character), "GetSlideAngle", postfix: nameof(GetSlideAngle_Postfix));
        }

        static void Patch(Harmony h, System.Type t, string m, string prefix = null, string postfix = null, bool first = false)
        {
            var method = AccessTools.Method(t, m);
            if (method == null) { Plugin.Log.LogWarning($"Worn effects: {t.Name}.{m} not found"); return; }
            h.Patch(method, prefix: prefix != null ? new HarmonyMethod(typeof(WornEffects), prefix) { priority = first ? Priority.First : Priority.Normal } : null,
                postfix: postfix != null ? new HarmonyMethod(typeof(WornEffects), postfix) : null);
        }

        // ── the items ───────────────────────────────────────────────────────────────────────

        /// <summary>
        /// An Ultima item as it is made (UltimaItems): a ring or amulet wears its own status effect
        /// instead of the trinket's (no Valheim bonus, no adrenaline); the Amulet of Lord British is
        /// worn as the wishbone is (a utility item); the utility items say what using them does.
        /// </summary>
        public static void Prepare(ItemDef def, ItemDrop.ItemData.SharedData s)
        {
            MakeEffects();
            bool trinket = s.m_itemType == ItemDrop.ItemData.ItemType.Trinket;
            if (trinket || def.Effect != null || def.Use != null)
            {
                s.m_equipStatusEffect = null;
                s.m_setStatusEffect = null;
                s.m_fullAdrenalineSE = null;
                s.m_maxAdrenaline = 0;
            }
            if (def.Kind == ItemKind.Equipment && worn.TryGetValue(def.Id, out var se)) s.m_equipStatusEffect = se;
            switch (def.Name)
            {
                case "Amulet of Lord British":
                    s.m_itemType = ItemDrop.ItemData.ItemType.Utility;
                    s.m_maxStackSize = 1;
                    s.m_description = "Worn, it guards thee and all within a stone's throw: +50 armour.";
                    break;
                case "Crown of Lord British":
                    s.m_description = "Worn, no magic touches thee or any within a stone's throw.";
                    break;
                case "Sceptre of Lord British": s.m_description = "Use it (right-click) to dissolve a magical field."; break;
                case "Black Badge": s.m_description = "Worn, it marks thee a servant of Blackthorn."; break;
                case "Gem": s.m_description = "Use it (right-click) to view a map; the gem is spent."; break;
                case "Sextant": s.m_description = "Use it (right-click) at night out in the land for thy position."; break;
                case "Pocket Watch": s.m_description = "Use it (right-click) for the time."; break;
                case "Spyglass": s.m_description = "Use it (right-click) at night to see the stars."; break;
                case "Grapple": s.m_description = "Held, it lets thee klimb slopes too steep to walk."; break;
                case "Magic Carpet": s.m_description = "Use it (right-click) to lay it down and board it."; break;
                case "Key": s.m_description = "Jimmy a locked door with it (E at the door); it may break."; break;
                case "Skull Key": s.m_description = "Opens a magically sealed door (E at the door); it is spent."; break;
            }
            switch (def.Id >= 0 && def.Kind == ItemKind.Equipment ? def.Id : -1)
            {
                case ItemUse.Invisibility: s.m_description = (s.m_description + " Worn, no monster sees thee. It may vanish as a fight begins.").Trim(); break;
                case ItemUse.Regeneration: s.m_description = (s.m_description + " Worn, thy wounds slowly heal. It may vanish as a fight begins.").Trim(); break;
            }
        }

        /// <summary>The status effects of the worn items and the regalia, from status.json (once).</summary>
        static void MakeEffects()
        {
            if (made) return;
            made = true;
            List<ItemUse.Look> wornLooks, partyList;
            try { (wornLooks, partyList, radiusTiles) = ItemUse.ParseLooks(File.ReadAllText(Path.Combine(Plugin.PluginDirectory, "mappings", "status.json"))); }
            catch (System.Exception e) { Plugin.Log.LogError($"status.json: {e.Message}"); return; }
            foreach (var look in wornLooks)
            {
                if (look.StatusEffect == null) continue;
                var se = Make(look, 0f);
                if (se != null) worn[look.Item] = se;
            }
            foreach (var look in partyList)
            {
                partyLooks[look.Name] = look;
                var se = Make(look, 3f);
                if (se != null) party[look.Name] = se;
            }
            Plugin.Log.LogInfo($"Worn effects: {worn.Count} worn, {party.Count} party status effects (radius {radiusTiles} tile)");
        }

        /// <summary>Our status effect with a Valheim one's icon and visual effects (recoloured), and none of its numbers.</summary>
        static StatusEffect Make(ItemUse.Look look, float ttl)
        {
            var source = PrefabManager.Cache.GetPrefab<StatusEffect>(look.StatusEffect);
            if (source == null) { Plugin.Log.LogWarning($"Worn effects: no Valheim status effect {look.StatusEffect} for {look.Name}"); return null; }
            var se = ScriptableObject.CreateInstance<SE_Stats>();
            se.name = "U5_SE_" + ItemMap.PrefabFor(look.Name).Substring(3);
            se.m_name = look.Name;
            se.m_tooltip = look.Name;
            se.m_icon = source.m_icon;
            se.m_ttl = ttl;
            se.m_startMessage = "";
            se.m_stopMessage = "";
            Color tint = default;
            bool tinted = look.Tint != null && ColorUtility.TryParseHtmlString(look.Tint, out tint);
            se.m_startEffects = Effects(source.m_startEffects, tinted && Recolour.Draws ? tint : (Color?)null, se.name);
            se.m_stopEffects = new EffectList();
            ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(se, false));
            return se;
        }

        static EffectList Effects(EffectList source, Color? tint, string name)
        {
            var list = new EffectList();
            if (source?.m_effectPrefabs == null) return list;
            var copied = new List<EffectList.EffectData>();
            var clone = AccessTools.Method(typeof(object), "MemberwiseClone");
            foreach (var e in source.m_effectPrefabs)
            {
                if (e?.m_prefab == null) continue;
                var copy = (EffectList.EffectData)clone.Invoke(e, null);
                if (tint.HasValue)
                {
                    var prefab = PrefabManager.Instance.CreateClonedPrefab(name + "_" + e.m_prefab.name, e.m_prefab);
                    if (prefab != null)
                    {
                        Recolour.Model(prefab, tint.Value);
                        foreach (var light in prefab.GetComponentsInChildren<Light>(true)) light.color = tint.Value;
                        copy.m_prefab = prefab;
                    }
                }
                copied.Add(copy);
            }
            list.m_effectPrefabs = copied.ToArray();
            return list;
        }

        // ── who wears what ──────────────────────────────────────────────────────────────────

        public static int Bits(Player p) => p != null ? p.GetComponent<ZNetView>()?.GetZDO()?.GetInt(WornKey) ?? 0 : 0;

        public static bool Wears(Player p, string prefab)
            => p != null && !string.IsNullOrEmpty(prefab) && p.GetInventory().GetEquippedItems().Any(i => i.m_dropPrefab != null && i.m_dropPrefab.name == prefab);

        static ItemDrop.ItemData WornItem(Player p, int itemId)
        {
            var def = UltimaItems.Map.Inventory(itemId);
            return def == null ? null : p.GetInventory().GetEquippedItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == def.Prefab);
        }

        static bool Has(Player p, string regalia) => party.TryGetValue(regalia, out var se) && p.GetSEMan().HaveStatusEffect(se.NameHash());

        /// <summary>The Ultima defence the Amulet of Lord British adds to a player in its reach (+50 armour on Valheim's scale).</summary>
        public static int ExtraDefence(Player p)
            => Has(p, "Amulet of Lord British") && partyLooks.TryGetValue("Amulet of Lord British", out var l) ? Mathf.RoundToInt(l.Armour / Ultima.Scale) : 0;

        /// <summary>Holding the grapple (Klimb).</summary>
        public static bool Grappling { get; private set; }

        // ── patches ─────────────────────────────────────────────────────────────────────────

        /// <summary>A magic attack on a player under the Crown's barrier: a spell-casting monster's missile, or fire, frost, lightning or spirit from anything but a player. It does not pass.</summary>
        static bool RPC_Damage_Prefix(Character __instance, HitData hit)
        {
            if (!(__instance is Player p) || p != Player.m_localPlayer || hit == null || !Has(p, "Crown of Lord British")) return true;
            var attacker = hit.GetAttacker();
            if (attacker is Player) return true;
            var stats = UltimaCreatures.Of(attacker);
            var d = hit.m_damage;
            bool magic = (stats != null && (stats.Flags & 0x8000) != 0 && hit.m_ranged) || d.m_fire + d.m_frost + d.m_lightning + d.m_spirit > 0;
            if (!magic) return true;
            p.Message(MessageHud.MessageType.TopLeft, "The Crown's barrier turns the magic aside!");
            return false;
        }

        static void GetBodyArmor_Postfix(Player __instance, ref float __result)
        {
            if (Britannia.Active != null) __result += ExtraDefence(__instance) * Ultima.Scale;
        }

        /// <summary>A monster loses sight of a target wearing the Ring of Invisibility (and FindEnemy skips them: m_aiSkipTarget).</summary>
        static readonly AccessTools.FieldRef<MonsterAI, Character> target = AccessTools.FieldRefAccess<MonsterAI, Character>("m_targetCreature");
        static void UpdateTarget_Prefix(MonsterAI __instance)
        {
            var t = target(__instance);
            if (t != null && t.m_aiSkipTarget) target(__instance) = null;
        }

        /// <summary>Klimb: holding the grapple, the local player stands on slopes up to 80° (Valheim lets players slide from 38°).</summary>
        static void GetSlideAngle_Postfix(Character __instance, ref float __result)
        {
            if (Grappling && __instance == Player.m_localPlayer) __result = 80f;
        }

        const string AmuletLightName = "u5_amulet_light";

        /// <summary>A soft silver light at the wearer's shoulders while the Amulet of Lord British is worn.</summary>
        static void AmuletLight(Player p, bool on)
        {
            var t = p.transform.Find(AmuletLightName);
            if (on == (t != null)) return;
            if (!on) { Object.Destroy(t.gameObject); return; }
            var go = new GameObject(AmuletLightName);
            go.transform.SetParent(p.transform, false);
            go.transform.localPosition = new Vector3(0f, 1.6f, 0.3f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.86f, 0.9f, 1f);
            light.range = 16f;
            light.intensity = 1.6f;
            light.shadows = LightShadows.None;
        }

        // ── each moment ─────────────────────────────────────────────────────────────────────

        public class Tick : MonoBehaviour
        {
            float checkAt, turnAt, lastFight = -999f;

            void Update()
            {
                if (Time.time < checkAt) return;
                checkAt = Time.time + 0.5f;
                var me = Player.m_localPlayer;
                if (me == null || Britannia.Active == null) { Grappling = false; return; }
                var zdo = me.GetComponent<ZNetView>()?.GetZDO();
                if (zdo == null) return;

                int bits = (WornItem(me, ItemUse.Invisibility) != null ? Invisible : 0) | (Wears(me, CrownPrefab) ? Crown : 0)
                           | (Wears(me, AmuletPrefab) ? Amulet : 0) | (Wears(me, BadgePrefab) ? Badge : 0);
                if (me.IsDead()) bits = 0;
                if (zdo.GetInt(WornKey) != bits) zdo.Set(WornKey, bits);
                Grappling = !me.IsDead() && Wears(me, GrapplePrefab);

                // the invisible are no monster's target, whoever's machine runs the monster
                foreach (var p in Player.GetAllPlayers()) if (p != null) p.m_aiSkipTarget = (Bits(p) & Invisible) != 0;
                // the Amulet of Lord British shines about whoever wears it (seen by everyone)
                foreach (var p in Player.GetAllPlayers()) if (p != null) AmuletLight(p, (Bits(p) & Amulet) != 0 && !p.IsDead());

                // the regalia's reach
                if (!me.IsDead())
                    foreach (var (bit, name) in new[] { (Crown, "Crown of Lord British"), (Amulet, "Amulet of Lord British") })
                    {
                        if (!party.TryGetValue(name, out var se)) continue;
                        bool near = Player.GetAllPlayers().Any(p => p != null && !p.IsDead() && (Bits(p) & bit) != 0 && Vector3.Distance(p.transform.position, me.transform.position) <= Radius);
                        if (near) me.GetSEMan().AddStatusEffect(se, resetTime: true);
                    }

                if (me.IsDead()) return;
                // the Ring of Regeneration, each turn
                if (Time.time >= turnAt)
                {
                    turnAt = Time.time + Turn;
                    if (WornItem(me, ItemUse.Regeneration) != null && ItemUse.Regenerates(random.Next(ItemUse.RegenerationRoll)) && me.GetHealth() < me.GetMaxHealth())
                        me.Heal(Ultima.Scale, true);
                }
                // a fight begins: a worn Invisibility or Regeneration ring may vanish
                bool fight = Character.GetAllCharacters().Any(c => c != null && !c.IsDead() && UltimaCreatures.Of(c) != null && c.GetComponent<TownNpc>() == null
                                                                   && c.GetBaseAI() != null && c.GetBaseAI().IsAlerted() && Vector3.Distance(c.transform.position, me.transform.position) < FightNear);
                if (fight)
                {
                    if (Time.time - lastFight > FightGap) FightBegins(me);
                    lastFight = Time.time;
                }
            }

            static void FightBegins(Player me)
            {
                foreach (int id in new[] { ItemUse.Invisibility, ItemUse.Regeneration })
                {
                    var ring = WornItem(me, id);
                    if (ring == null || !ItemUse.RingVanishes(random.Next(ItemUse.VanishRoll))) continue;
                    // (one ring of a stack: the rest stay worn)
                    if (ring.m_stack <= 1) me.UnequipItem(ring, false);
                    me.GetInventory().RemoveItem(ring, 1);
                    me.Message(MessageHud.MessageType.Center, Plugin.U5.Ovl.CStr(ItemUse.SRingVanished).Trim());
                    Plugin.Log.LogInfo($"{me.GetPlayerName()}'s {ring.m_shared.m_name} vanished as a fight began");
                }
            }
        }
    }
}
