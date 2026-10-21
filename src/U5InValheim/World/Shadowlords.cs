using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using U5InValheim.Data;
using U5InValheim.WorldGen.Core;
using UnityEngine;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// The three Shadowlords (MASTER_PLAN §5.8, Phase 7): Faulinei (Falsehood), Astaroth (Hatred)
    /// and Nosfentor (Cowardice). Where they are is the calendar's (Core/ShadowlordRoutes: a new
    /// town 1..8 every midnight), so every peer knows without asking. In a town they appear as the
    /// original's spawn_shadowlord has it (a Shadow Lord, the Wraith of monsters.json, at x 15 and
    /// the location's row of DATA.OVL 0x13a5, on the ground floor, AI 6: it goes for players who
    /// come within three tiles), and the town feels it:
    /// - entering: "An air of falsehood doth surround thee..." (in Stonegate one line for each
    ///   Shadowlord still about);
    /// - Falsehood: after every payment in a shop, inn or healer 1..64 gold more is gone
    ///   (shadowlord_cheat_gold), and after a conversation "Something was stolen!"
    ///   (shadowlord_steals_item);
    /// - Hatred: half the townsfolk (npc_affected_by_shadowlord) come at the players and curse them
    ///   ("Begone, vermin!": 1..8 to everyone near), then run off; Cowardice: they hide at home and
    ///   say only "Don't hurt me! Please go away!" (TownNpc.Sway).
    /// Talking to a Shadowlord (E) draws its "Begone, vermin!". Slain in a fight, one is gone from
    /// that town until its next move (it is not destroyed: the original only removes its object).
    /// Its doom: yell its name in the chat in the Lycaeum, Empath Abbey or Serpent's Hold and it
    /// appears two tiles north of you; then, standing at the Flame of Truth, Love or Courage (the
    /// tile south of it, DATA.OVL 0x4882..), use its Shard (Falsehood, Hatred, Cowardice) with the
    /// Shadowlord beside you: it is cast into the flame and the Shadowlord is destroyed for good
    /// (global key u5_shadowlord_doomed&lt;n&gt;; CAST use_gem_shard).
    /// </summary>
    public static class Shadowlords
    {
        public const string IndexKey = "u5_shadowlord", SummonedKey = "u5_shadowlord_summoned", DoomKey = "u5_shadowlord_doomed", GoneKey = "u5_shadowlord_gone";
        /// <summary>The Shadow Lord creature type (DATA.OVL monster 47).</summary>
        public const int CreatureType = 47;
        /// <summary>Stonegate, their lair.</summary>
        public const int Stonegate = 29;
        // DATA.OVL (DGROUP)
        const int SAirOf = 0x27b8, SSurround = 0x27c4, SBegone = 0x278a, SStolen = 0x94dc, SDontHurt = 0x9176,
                  SGemShard = 0x4794, SCast = 0x47fe, SDoom = 0x483b, SWrought = 0x4874, SNoEffect = 0x47f0, SAppears = 0x4425, SYellNoEffect = 0x4418,
                  ShardX = 0x4882, ShardY = 0x4886, ShardLoc = 0x488a, ShardZ = 0x488e, SpawnY = 0x13a5;
        static readonly int[] ShardNames = { 0x47cc, 0x47d9, 0x47e3 }, FlameNames = { 0x4822, 0x482a, 0x4831 }, DoomNames = { 0x4858, 0x4861, 0x486a };
        static readonly string[] Shards = { "Shard of Falsehood", "Shard of Hatred", "Shard of Cowardice" };
        static readonly System.Random random = new System.Random();

        static DataOvl Ovl => Plugin.U5.Ovl;

        // ── where they are ──────────────────────────────────────────────────────────────────

        static ShadowlordRoutes routes;
        static int routesSeed;

        public static ShadowlordRoutes Routes
        {
            get
            {
                if (routes == null || routesSeed != Britannia.Seed)
                {
                    var g = Plugin.U5.InitialGame;
                    routes = new ShadowlordRoutes(Britannia.Seed, Enumerable.Range(0, ShadowlordRoutes.Count).Select(g.ShadowlordLocation).ToArray());
                    routesSeed = Britannia.Seed;
                }
                return routes;
            }
        }

        public static string Name(int i) => ShadowlordRoutes.Names[i];
        /// <summary>Destroyed for good (its shard cast into its flame).</summary>
        public static bool Doomed(int i) => WorldKeys.Get(DoomKey + i) != null;
        /// <summary>Slain today (gone from its town until midnight).</summary>
        static bool GoneToday(int i) => WorldKeys.Get(GoneKey + i) == Calendar.WorldDay.ToString();

        static readonly int[] towns = new int[ShadowlordRoutes.Count];
        static float townsAt = -1;
        static long townsDay = -1;

        /// <summary>The town (1..8) Shadowlord <paramref name="i"/> is in now, or 0 (in none, slain today, or destroyed).</summary>
        public static int TownOf(int i)
        {
            long day = Calendar.WorldDay;
            if (Time.time - townsAt > 0.5f || day != townsDay || townsAt < 0)
            {
                townsAt = Time.time;
                townsDay = day;
                for (int k = 0; k < towns.Length; k++) towns[k] = Doomed(k) || GoneToday(k) ? 0 : Routes.TownOf(k, day);
            }
            return towns[i];
        }

        /// <summary>Which Shadowlord is in a location now, or −1.</summary>
        public static int In(int location)
        {
            if (location < 1 || location > ShadowlordRoutes.Towns || Britannia.Active == null) return -1;
            for (int i = 0; i < ShadowlordRoutes.Count; i++) if (TownOf(i) == location) return i;
            return -1;
        }

        /// <summary>"An air of hatred doth surround thee..."</summary>
        public static string Air(int i) => (Ovl.CStr(SAirOf) + Ovl.PtrStr(Dg.ShadowlordNamePtrs, i) + Ovl.CStr(SSurround)).Replace("\n", " ").Trim();

        /// <summary>One line per Shadowlord: where it is today.</summary>
        public static string Describe()
        {
            var lines = new List<string>();
            for (int i = 0; i < ShadowlordRoutes.Count; i++)
            {
                int t = TownOf(i);
                string where = Doomed(i) ? "destroyed" : GoneToday(i) ? "driven off until midnight" : t > 0 ? $"in {Plugin.U5.Location(t).Name}" : "in no town yet";
                lines.Add($"{Name(i)} ({ShadowlordRoutes.Airs[i]}): {where}");
            }
            return string.Join("\n", lines);
        }

        // ── their air on the townsfolk ──────────────────────────────────────────────────────

        /// <summary>The air on a townsperson now: 1 Hatred, 2 Cowardice, 0 none (npc_affected_by_shadowlord).</summary>
        public static int Sway(int location, int slot, byte[] times)
        {
            int sl = In(location);
            if (sl != 1 && sl != 2) return 0;
            var npcs = Plugin.U5.NpcsOf(Plugin.U5.Location(location));
            return Routes.Affected(Calendar.WorldDay, location, slot, times, npcs[4].Type) ? sl : 0;
        }

        /// <summary>The player a hateful townsperson goes for: the nearest in their town.</summary>
        public static Player HateTarget(TownNpc t)
        {
            var b = t.Home;
            if (b == null) return null;
            Player best = null;
            float bestD = 30f;
            foreach (var p in Player.GetAllPlayers())
            {
                if (p == null || p.IsDead() || !InTown(b, p.transform.position)) continue;
                float d = Vector3.Distance(p.transform.position, t.transform.position);
                if (d < bestD) { best = p; bestD = d; }
            }
            return best;
        }

        /// <summary>TOWN begone_vermin: the words over the speaker's head, and a blow of 1..8 to everyone within a tile (damage_whole_party).</summary>
        public static void Begone(GameObject speaker, Vector3 at)
        {
            Chat.instance?.SetNpcText(speaker, Vector3.up * 2.1f, 20f, 5f, "", Ovl.CStr(SBegone).Replace("\n", " ").Trim(), false);
            var near = new List<Player>();
            Player.GetPlayersInRange(at, Plugin.TileMetres.Value, near);
            foreach (var p in near)
            {
                var hit = new HitData { m_point = p.GetCenterPoint(), m_dir = (p.transform.position - speaker.transform.position).normalized };
                hit.m_damage.m_damage = random.Next(1, 9) * Ultima.Scale;
                p.Damage(hit);
            }
        }

        public static string DontHurtMe => Ovl.CStr(SDontHurt).Replace("\n", " ").Trim();

        // ── Falsehood ───────────────────────────────────────────────────────────────────────

        /// <summary>SHOPPES shadowlord_cheat_gold: after a payment with Falsehood in town, 1..64 gold more is gone.</summary>
        public static void Cheat(Player me, int location)
        {
            if (me == null || In(location) != 0) return;
            int n = Mathf.Min(random.Next(1, 65), me.GetInventory().CountItems("$item_coins"));
            if (n <= 0) return;
            me.GetInventory().RemoveItem("$item_coins", n);
            Plugin.Log.LogInfo($"Falsehood in {Plugin.U5.Location(location).Name}: {n} more gold gone from {me.GetPlayerName()}");
        }

        /// <summary>
        /// TALK shadowlord_steals_item: after a conversation with Falsehood in town, "Something was
        /// stolen!": one key, gem or torch (one of those carried), else the last-numbered arms or
        /// armour carried, else a potion, else a scroll, else 1..15 gold.
        /// </summary>
        public static void AfterConversation(int location)
        {
            var me = Player.m_localPlayer;
            if (me == null || In(location) != 0) return;
            var inv = me.GetInventory();
            var map = UltimaItems.Map;
            me.Message(MessageHud.MessageType.Center, Ovl.CStr(SStolen).Trim());
            ItemDrop.ItemData Carried(string prefab) => prefab == null ? null : inv.GetAllItems().FirstOrDefault(i => !i.m_equipped && i.m_dropPrefab != null && i.m_dropPrefab.name == prefab);
            var small = new[] { "Key", "Gem", "Torch" }.Select(s => Carried(map?.Special(s)?.Prefab)).Where(i => i != null).ToList();
            ItemDrop.ItemData take = small.Count > 0 ? small[random.Next(small.Count)] : null;
            for (int id = 47; take == null && id >= 0; id--) take = Carried(map?.Inventory(id)?.Prefab);
            for (int k = 7; take == null && k >= 0; k--) take = Carried(map?.Inventory(ItemMap.PotionBase + k)?.Prefab);
            for (int k = 7; take == null && k >= 0; k--) take = Carried(map?.Inventory(ItemMap.ScrollBase + k)?.Prefab);
            string what;
            if (take != null)
            {
                what = take.m_shared.m_name;
                inv.RemoveItem(take, 1);
            }
            else
            {
                int n = Mathf.Min(random.Next(1, 16), inv.CountItems("$item_coins"));
                if (n > 0) inv.RemoveItem("$item_coins", n);
                what = $"{n} gold";
            }
            Plugin.Log.LogInfo($"Falsehood in {Plugin.U5.Location(location).Name}: {what} stolen from {me.GetPlayerName()}");
        }

        // ── in the towns ────────────────────────────────────────────────────────────────────

        public static bool InTown(Settlements.Built b, Vector3 at)
        {
            var local = at - b.Origin;
            return Mathf.Max(Mathf.Abs(local.x), Mathf.Abs(local.z)) <= b.Site.Half && local.y > -b.Plan.BasementDepth - 4f && local.y < 4f * b.Plan.Storey;
        }

        /// <summary>The settlement a position is in, or null.</summary>
        public static Settlements.Built TownAt(Vector3 at) => Settlements.All.FirstOrDefault(b => InTown(b, at));

        /// <summary>The Shadowlords loaded on this peer.</summary>
        public static IEnumerable<Shadowlord> Loaded => Shadowlord.All.Where(s => s != null && s.IsValid);

        /// <summary>spawn_shadowlord: where it stands in a town (x 15, the location's row, the ground floor).</summary>
        public static Vector3 SpawnPoint(Settlements.Built b)
        {
            int y = Ovl.U8(SpawnY + b.Location.Number);
            var (lx, ly, lz) = b.Plan.TilePos(0, y * SettlementPlan.Size + 15);
            return b.Origin + new Vector3(lx, ly + 0.3f, lz);
        }

        /// <summary>A Shadowlord's body (on this peer, which owns it).</summary>
        public static Character Spawn(int i, int location, Vector3 at, bool summoned)
        {
            var c = UltimaCreatures.Spawn(CreatureType, at, false);
            if (c == null) { Plugin.Log.LogWarning("Shadowlords: no body for the Shadow Lord (monsters.json type 47)"); return null; }
            var zdo = c.GetComponent<ZNetView>().GetZDO();
            zdo.Set(ResidentMonster.ResidentKey, location);
            zdo.Set(ResidentMonster.AiKey, 6);
            zdo.Set(IndexKey, i + 1);
            zdo.Set(SummonedKey, summoned);
            if (c.GetComponent<ResidentMonster>() == null) c.gameObject.AddComponent<ResidentMonster>();
            if (c.GetComponent<Shadowlord>() == null) c.gameObject.AddComponent<Shadowlord>();
            Plugin.Log.LogInfo($"Shadowlord {Name(i)} {(summoned ? "summoned" : "appears")} in {Plugin.U5.Location(location).Name} at {at}");
            return c;
        }

        /// <summary>The town's Shadowlord is there (made by the player who is in town, once its ground is loaded).</summary>
        public static void EnsureIn(Settlements.Built b)
        {
            int loc = b.Location.Number, sl = In(loc);
            if (sl < 0 || Loaded.Any(s => s.Index == sl && !s.Summoned)) return;
            var at = SpawnPoint(b);
            if (!ZNetScene.instance.IsAreaReady(at)) return;
            Spawn(sl, loc, at, false);
        }

        // ── their doom ──────────────────────────────────────────────────────────────────────

        public static void Install(Harmony harmony)
        {
            Patch(harmony, typeof(Character), "Awake", postfix: nameof(Awake_Postfix));
            Patch(harmony, typeof(Character), "OnDeath", prefix: nameof(OnDeath_Prefix));
            Patch(harmony, typeof(Chat), nameof(Chat.SendText), postfix: nameof(SendText_Postfix));
            Patch(harmony, typeof(Humanoid), nameof(Humanoid.UseItem), prefix: nameof(UseItem_Prefix));
        }

        static void Patch(Harmony h, System.Type t, string m, string prefix = null, string postfix = null)
        {
            var method = AccessTools.Method(t, m);
            if (method == null) { Plugin.Log.LogWarning($"Shadowlords: {t.Name}.{m} not found"); return; }
            h.Patch(method, prefix: prefix != null ? new HarmonyMethod(typeof(Shadowlords), prefix) : null,
                postfix: postfix != null ? new HarmonyMethod(typeof(Shadowlords), postfix) : null);
        }

        static void Awake_Postfix(Character __instance)
        {
            var zdo = __instance.GetComponent<ZNetView>()?.GetZDO();
            if (zdo == null || zdo.GetInt(IndexKey) == 0 || __instance.GetComponent<Shadowlord>() != null) return;
            __instance.gameObject.AddComponent<Shadowlord>();
        }

        /// <summary>Slain in a fight (on its owner): gone from its town until midnight.</summary>
        static void OnDeath_Prefix(Character __instance)
        {
            var s = __instance.GetComponent<Shadowlord>();
            if (s == null || s.Summoned || !__instance.GetComponent<ZNetView>().IsOwner()) return;
            WorldKeys.Set(GoneKey + s.Index, Calendar.WorldDay.ToString());
            Plugin.Log.LogInfo($"Shadowlord {Name(s.Index)} slain: gone until midnight");
        }

        /// <summary>CMDS yell_shadowlord_name: its name yelled in the Lycaeum, Empath Abbey or Serpent's Hold calls it two tiles north.</summary>
        static void SendText_Postfix(string text)
        {
            var me = Player.m_localPlayer;
            if (me == null || string.IsNullOrEmpty(text) || Britannia.Active == null) return;
            string said = Summon(me, text);
            if (said != null) me.Message(MessageHud.MessageType.Center, said);
        }

        /// <summary>What a yell does: null (nothing to do with them), "No effect!" or "A shadowlord appears".</summary>
        public static string Summon(Player me, string text)
        {
            int i = System.Array.FindIndex(ShadowlordRoutes.Names, n => text.IndexOf(n, System.StringComparison.OrdinalIgnoreCase) >= 0);
            if (i < 0) return null;
            var b = TownAt(me.transform.position);
            int loc = b?.Location.Number ?? 0;
            if (loc < 30 || loc > 32) return null;
            if (Doomed(i) || Loaded.Any(s => s.Summoned && InTown(b, s.transform.position))) return Ovl.CStr(SYellNoEffect).Trim();
            // two tiles north (the settlement's tiles are 2 m; north is +z)
            Spawn(i, loc, me.transform.position + Vector3.forward * 2f * b.Plan.Tile + Vector3.up * 0.3f, true);
            return Ovl.CStr(SAppears).Trim();
        }

        /// <summary>Using a shard (CAST use_gem_shard).</summary>
        static bool UseItem_Prefix(Humanoid __instance, Inventory inventory, ItemDrop.ItemData item)
        {
            if (!(__instance is Player p) || p != Player.m_localPlayer || item?.m_dropPrefab == null || UltimaItems.Map == null) return true;
            int shard = System.Array.FindIndex(Shards, s => UltimaItems.Map.Special(s)?.Prefab == item.m_dropPrefab.name);
            if (shard < 0) return true;
            UseShard(p, shard, inventory ?? p.GetInventory(), item);
            return false;
        }

        /// <summary>The flame a shard belongs to: its settlement and the tile south of the flame, in the world.</summary>
        public static Vector3? ShardSite(int shard)
        {
            var b = Settlements.Get(Ovl.U8(ShardLoc + shard));
            if (b == null) return null;
            int x = Ovl.U8(ShardX + shard), y = Ovl.U8(ShardY + shard), z = Ovl.I8(ShardZ + shard);
            var (lx, ly, lz) = b.Plan.TilePos(z, y * SettlementPlan.Size + x);
            return b.Origin + new Vector3(lx, ly, lz);
        }

        /// <summary>
        /// "Thou dost hold above thee the evil Shard of …": at its flame's tile it is cast into the
        /// Flame (the ground shakes); with its Shadowlord (the one called there) beside the flame,
        /// the Shadowlord is destroyed and the shard consumed. Anywhere else, "No effect!".
        /// </summary>
        public static string UseShard(Player me, int shard, Inventory inv, ItemDrop.ItemData item)
        {
            string said = (Ovl.CStr(SGemShard) + Ovl.CStr(ShardNames[shard])).Replace("\n", " ").Trim();
            var site = ShardSite(shard);
            var d = site.HasValue ? me.transform.position - site.Value : Vector3.one * 1000;
            if (!site.HasValue || new Vector2(d.x, d.z).magnitude > 2.5f || Mathf.Abs(d.y) > 2f)
            {
                said += " " + Ovl.CStr(SNoEffect).Trim();
                me.Message(MessageHud.MessageType.Center, said);
                return said;
            }
            said += " " + (Ovl.CStr(SCast) + Ovl.CStr(FlameNames[shard])).Replace("\n", " ").Trim();
            GameCamera.instance?.AddShake(me.transform.position, 30f, 2f, false);
            if (CastIntoFlame(shard, me.transform.position))
            {
                inv.RemoveItem(item, 1);
                said += " " + (Ovl.CStr(SDoom) + Ovl.CStr(DoomNames[shard]) + Ovl.CStr(SWrought)).Replace("\n", " ").Trim();
                Plugin.Log.LogInfo($"{me.GetPlayerName()}: the doom of the Shadowlord {Name(shard)} is wrought");
            }
            me.Message(MessageHud.MessageType.Center, said);
            return said;
        }

        /// <summary>The shard cast into its flame by someone at <paramref name="at"/>: its Shadowlord, called there and beside them, is destroyed for good.</summary>
        public static bool CastIntoFlame(int shard, Vector3 at)
        {
            var lord = Loaded.Where(s => s.Index == shard && s.Summoned && Vector3.Distance(s.transform.position, at) < 8f)
                .OrderBy(s => Vector3.Distance(s.transform.position, at)).FirstOrDefault();
            if (lord == null) return false;
            WorldKeys.Set(DoomKey + shard, Calendar.WorldDay.ToString());
            lord.Vanish();
            return true;
        }
    }

    /// <summary>
    /// A Shadowlord's body (any peer): its name, "Begone, vermin!" when spoken to (E); on its
    /// owner it leaves when the calendar takes it elsewhere (or it is destroyed), and of two of
    /// the same in one place (two players arriving at once) the later goes.
    /// </summary>
    public class Shadowlord : MonoBehaviour, Interactable
    {
        public static readonly List<Shadowlord> All = new List<Shadowlord>();

        ZNetView nview;
        Character body;
        public int Index { get; private set; } = -1;
        public bool Summoned { get; private set; }
        public int Location { get; private set; }
        public bool IsValid => nview != null && nview.IsValid() && Index >= 0;

        void Awake()
        {
            nview = GetComponent<ZNetView>();
            body = GetComponent<Character>();
            var zdo = nview?.GetZDO();
            if (zdo == null) return;
            Index = zdo.GetInt(Shadowlords.IndexKey) - 1;
            Summoned = zdo.GetBool(Shadowlords.SummonedKey);
            Location = zdo.GetInt(ResidentMonster.ResidentKey);
            if (body != null) body.m_name = Plugin.U5.Ovl.CreatureName(Shadowlords.CreatureType);
            All.Add(this);
            InvokeRepeating(nameof(Check), 2f, 2f);
        }

        void OnDestroy() => All.Remove(this);

        void Check()
        {
            if (!IsValid || !nview.IsOwner() || Index < 0) return;
            bool stays = Summoned ? !Shadowlords.Doomed(Index) : Shadowlords.TownOf(Index) == Location;
            // (of two the same here, the later-made one goes)
            var twin = Shadowlords.Loaded.FirstOrDefault(s => s != this && s.Index == Index && s.Summoned == Summoned && s.Location == Location);
            if (twin != null && Later(twin)) stays = false;
            if (!stays) Vanish();
        }

        bool Later(Shadowlord other)
        {
            ZDOID a = nview.GetZDO().m_uid, b = other.nview.GetZDO().m_uid;
            return a.UserID != b.UserID ? a.UserID > b.UserID : a.ID > b.ID;
        }

        /// <summary>Gone (to its next town, or destroyed), in its death's smoke.</summary>
        public void Vanish()
        {
            if (!IsValid) return;
            if (body != null) body.m_deathEffects.Create(transform.position, transform.rotation);
            Plugin.Log.LogInfo($"Shadowlord {Shadowlords.Name(Index)} leaves {Plugin.U5.Location(Location).Name}");
            nview.ClaimOwnership();
            ZNetScene.instance.Destroy(gameObject);
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !(user is Player p) || p != Player.m_localPlayer || !IsValid) return false;
            Shadowlords.Begone(gameObject, p.transform.position);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    }

    /// <summary>
    /// The local player's view of the Shadowlords: entering a town, the air there; in one of the
    /// eight towns, its Shadowlord made if it is not there yet (by whoever is in town).
    /// </summary>
    public class ShadowlordWatch : MonoBehaviour
    {
        int lastLoc;
        int lastPresent = -1;
        float enteredAt, nextAt;

        void Update()
        {
            if (Time.time < nextAt) return;
            nextAt = Time.time + 1f;
            var me = Player.m_localPlayer;
            if (me == null || Britannia.Active == null || Plugin.U5 == null || ZNetScene.instance == null) { lastLoc = 0; return; }
            var b = Shadowlords.TownAt(me.transform.position);
            int loc = b?.Location.Number ?? 0;
            int present = Shadowlords.In(loc);
            if (loc != lastLoc)
            {
                lastLoc = loc;
                enteredAt = Time.time;
                lastPresent = -1;
                if (loc == Shadowlords.Stonegate)
                {
                    var lines = Enumerable.Range(0, ShadowlordRoutes.Count).Reverse().Where(i => !Shadowlords.Doomed(i)).Select(Shadowlords.Air).ToList();
                    if (lines.Count > 0) me.Message(MessageHud.MessageType.Center, string.Join("\n", lines));
                }
            }
            if (present >= 0 && present != lastPresent) me.Message(MessageHud.MessageType.Center, Shadowlords.Air(present));
            lastPresent = present;
            if (b != null && present >= 0 && Time.time - enteredAt > 3f) Shadowlords.EnsureIn(b);
        }
    }
}
