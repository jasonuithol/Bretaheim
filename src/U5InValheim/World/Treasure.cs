using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using U5InValheim.Data;
using U5InValheim.WorldGen.Core;
using UnityEngine;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// The original's loot in Britannia (Data/Loot.cs):
    /// - a dying monster or townsperson may leave a chest by its treasure value (apply_damage),
    ///   trapped or not; Valheim's own drops of Ultima creatures are gone;
    /// - a chest holds the original's rolls (fixed and random tables by its quality), made when it
    ///   appears; opening a trapped one springs the trap first (acid, poison, bomb or gas), an
    ///   empty one says "Chest empty!", and once opened it is gone when emptied;
    /// - the objects in the settlements' .NPC files lie where the original has them (the castle's
    ///   chests, the magic carpet, the sandalwood box, the crown and sceptre): E takes them;
    /// - the original's 113 hidden items are found by searching (E "Search") on their tile: unseen
    ///   in the settlements, under a small mound out in the land (the Glass Sword, the buried
    ///   moonstones, the mandrake and nightshade that can be picked at midnight).
    /// </summary>
    public static class Treasure
    {
        public const string ChestPrefab = "u5_chest", SpotPrefab = "u5_loot";
        public const string QualityKey = "u5_chest_quality", TrapKey = "u5_trap", OpenedKey = "u5_opened";
        public const string KindKey = "u5_loot_kind", ValueKey = "u5_loot_value", CountKey = "u5_loot_count",
                            HiddenKey = "u5_loot_hidden", IndexKey = "u5_loot_index", DayKey = "u5_loot_day", MoundKey = "u5_loot_mound",
                            CoverKey = "u5_loot_cover";
        /// <summary>A hidden spot under furniture (a wardrobe, a barrel) is searched through it: its box encloses the tile this high.</summary>
        public const float CoverHeight = 2.6f, CoverWidth = 1.98f;
        /// <summary>A loot spot that is no hidden-object entry (index key unset).</summary>
        const int NotHidden = -1;
        /// <summary>The reagent patches' spots (hidden-object index 200 + patch): mandrake, mandrake, nightshade.</summary>
        public const int ReagentPatchBase = 200, MoonstoneBase = 300;
        // DATA.OVL (DGROUP)
        const int SFound = 0x8b5c, STrapped = 0x8b7e, SChestEmpty = 0x8b88, PatchX = 0x3e66, PatchY = 0x3e6a, PatchReagent = 0x3e6e;
        static readonly System.Random random = new System.Random();

        static ItemMap Items => UltimaItems.Map;
        static List<HiddenObject> hidden;
        static List<HiddenObject> Hidden => hidden ?? (hidden = LootTables.Hidden(Ovl));
        static DataOvl Ovl => Plugin.U5.Ovl;

        public static void Register() => PrefabManager.OnVanillaPrefabsAvailable += Create;

        public static void Install(Harmony harmony)
        {
            var death = AccessTools.Method(typeof(Character), "OnDeath");
            if (death != null) harmony.Patch(death, prefix: new HarmonyMethod(typeof(Treasure), nameof(OnDeath_Prefix)));
            var open = AccessTools.Method(typeof(Container), nameof(Container.Interact));
            if (open != null) harmony.Patch(open, prefix: new HarmonyMethod(typeof(Treasure), nameof(ContainerInteract_Prefix)));
            if (death == null || open == null) Plugin.Log.LogWarning("Treasure: Character.OnDeath / Container.Interact not found");
        }

        static void Create()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Create;
            // the chest: a wild chest of Valheim's, empty of Valheim's loot
            var chest = PrefabManager.Instance.CreateClonedPrefab(ChestPrefab, "TreasureChest_meadows");
            if (chest != null)
            {
                var c = chest.GetComponent<Container>();
                if (c != null)
                {
                    c.m_name = "Chest";
                    c.m_defaultItems = new DropTable();
                    c.m_privacy = Container.PrivacySetting.Public;
                    c.m_autoDestroyEmpty = false;
                    c.m_width = Mathf.Max(c.m_width, 6);
                    c.m_height = Mathf.Max(c.m_height, 4);
                }
                chest.AddComponent<U5Chest>();
                PrefabManager.Instance.AddPrefab(new CustomPrefab(chest, false));
            }
            else Plugin.Log.LogError("Treasure: no TreasureChest_meadows to make a chest of");

            // a loot spot: a networked holder of one piece of loot, taken or searched with E
            var holder = new GameObject("U5InValheim_Loot");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            var spot = new GameObject(SpotPrefab);
            spot.transform.SetParent(holder.transform, false);
            spot.layer = LayerMask.NameToLayer("item");
            var nview = spot.AddComponent<ZNetView>();
            nview.m_persistent = true;
            var box = spot.AddComponent<BoxCollider>();
            box.size = new Vector3(1.4f, 0.6f, 1.4f);
            box.center = new Vector3(0, 0.3f, 0);
            spot.AddComponent<LootSpot>();
            PrefabManager.Instance.AddPrefab(new CustomPrefab(spot, false));
        }

        // ── giving ──────────────────────────────────────────────────────────────────────────────

        /// <summary>Loot into an inventory (in stacks it can hold); what does not fit is dropped at <paramref name="at"/>. Returns its name for messages.</summary>
        public static string Give(Inventory inv, Loot loot, Vector3 at)
        {
            var names = new List<string>();
            foreach (var (prefabName, count) in Items.Prefabs(loot))
            {
                var prefab = ObjectDB.instance?.GetItemPrefab(prefabName);
                var drop = prefab?.GetComponent<ItemDrop>();
                if (drop == null) { Plugin.Log.LogWarning($"Treasure: no item {prefabName}"); continue; }
                int stack = Mathf.Max(1, drop.m_itemData.m_shared.m_maxStackSize);
                for (int left = count; left > 0; left -= stack)
                {
                    int n = Mathf.Min(stack, left);
                    if (inv == null || !inv.AddItem(prefab, n))
                        ItemDrop.DropItem(drop.m_itemData, n, at + Vector3.up + Random.insideUnitSphere * 0.3f, Quaternion.identity);
                }
                string name = Localization.instance != null ? Localization.instance.Localize(drop.m_itemData.m_shared.m_name) : drop.m_itemData.m_shared.m_name;
                if (!prefabName.EndsWith("_Legs")) names.Add(count > 1 ? $"{count} {name}" : name);
            }
            return string.Join(", ", names);
        }

        // ── chests ──────────────────────────────────────────────────────────────────────────────

        /// <summary>A chest of this quality, filled now with the original's rolls (by whoever makes it).</summary>
        public static GameObject SpawnChest(Vector3 at, Quaternion rot, int quality, bool trapped)
        {
            var prefab = ZNetScene.instance?.GetPrefab(ChestPrefab);
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, at, rot);
            var zdo = go.GetComponent<ZNetView>()?.GetZDO();
            var container = go.GetComponent<Container>();
            if (zdo == null || container == null) return go;
            zdo.Set(QualityKey, quality);
            zdo.Set(TrapKey, trapped);
            foreach (var loot in LootTables.Chest(Ovl, quality, random)) Give(container.GetInventory(), loot, at);
            return go;
        }

        /// <summary>A chest holding just <paramref name="contents"/> (a dungeon hallway's chest: its own table).</summary>
        public static GameObject SpawnChest(Vector3 at, Quaternion rot, List<Loot> contents, bool trapped)
        {
            var prefab = ZNetScene.instance?.GetPrefab(ChestPrefab);
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, at, rot);
            var zdo = go.GetComponent<ZNetView>()?.GetZDO();
            var container = go.GetComponent<Container>();
            if (zdo == null || container == null) return go;
            zdo.Set(TrapKey, trapped);
            foreach (var loot in contents) Give(container.GetInventory(), loot, at);
            return go;
        }

        /// <summary>A piece of loot lying in the open for the taking (E), made now (a dungeon room's objects).</summary>
        public static GameObject SpawnLoot(Vector3 at, Loot loot) => SpawnSpot(at, loot, false, NotHidden, false, ZoneSystem.SpawnMode.Full, null);

        /// <summary>A dying Ultima creature or townsperson (on its owner) may leave a chest; Valheim's drops of Ultima creatures are not theirs.</summary>
        static void OnDeath_Prefix(Character __instance)
        {
            if (Britannia.Active == null || __instance is Player) return;
            var stats = UltimaCreatures.Of(__instance);
            var nview = __instance.GetComponent<ZNetView>();
            if (stats == null || nview == null || !nview.IsOwner()) return;
            var drops = __instance.GetComponent<CharacterDrop>();
            if (drops != null) drops.m_drops = new List<CharacterDrop.Drop>();
            // (what vanishes or turns into something else leaves nothing; nothing is left in water)
            if ((stats.Flags & 0x1001) != 0 || stats.Type == 0x1c || stats.Type == 0x1e || __instance.InWater()) return;
            var (chest, trapped) = LootTables.Drop(stats.Treasure, random);
            if (!chest) return;
            var at = __instance.transform.position;
            SpawnChest(at, Quaternion.Euler(0, random.Next(360), 0), stats.Treasure, trapped);
            Plugin.Log.LogInfo($"{__instance.m_name} left a chest (quality {stats.Treasure}{(trapped ? ", trapped" : "")}) at {at}");
        }

        /// <summary>Opening a chest the first time: its trap springs, an empty one says so and is gone.</summary>
        static bool ContainerInteract_Prefix(Container __instance, Humanoid character, bool hold)
        {
            if (hold || __instance.GetComponent<U5Chest>() == null || !(character is Player player)) return true;
            var nview = __instance.GetComponent<ZNetView>();
            var zdo = nview?.GetZDO();
            if (zdo == null || zdo.GetBool(OpenedKey)) return true;
            nview.ClaimOwnership();
            zdo.Set(OpenedKey, true);
            if (zdo.GetBool(TrapKey))
            {
                zdo.Set(TrapKey, false);
                Spring(player, __instance.transform.position);
            }
            if (__instance.GetInventory().NrOfItems() == 0)
            {
                player.Message(MessageHud.MessageType.Center, Ovl.CStr(SChestEmpty).Trim());
                nview.Destroy();
                return false;
            }
            return true;
        }

        /// <summary>trap_effect: acid (3 in 8: the opener, 1..30), poison (2 in 8: the opener), bomb (2 in 8: everyone near, 1..30 each), gas (1 in 8: everyone near poisoned).</summary>
        static void Spring(Player opener, Vector3 at)
        {
            int kind = Ovl.U8(Dg.TrapTypeTable + random.Next(8));
            int[] said = { Dg.StrAcid, Dg.StrPoison, Dg.StrBomb, Dg.StrGas };
            opener.Message(MessageHud.MessageType.Center, Ovl.CStr(STrapped).Trim() + " " + Ovl.CStr(said[kind & 3]).Trim());
            var near = new List<Player>();
            Player.GetPlayersInRange(at, 8f, near);
            if (!near.Contains(opener)) near.Add(opener);
            switch (kind)
            {
                case 0: Hurt(opener, at, random.Next(1, 31), false); break;
                case 1: Hurt(opener, at, 0, true); break;
                case 2: foreach (var p in near) Hurt(p, at, random.Next(1, 31), false); break;
                default: foreach (var p in near) Hurt(p, at, 0, true); break;
            }
        }

        static void Hurt(Player p, Vector3 at, int damage, bool poison)
        {
            var hit = new HitData { m_point = p.GetCenterPoint(), m_dir = (p.transform.position - at).normalized };
            hit.m_damage.m_damage = damage * Ultima.Scale;
            if (poison) hit.m_damage.m_poison = Ultima.Scale;
            p.Damage(hit);
        }

        // ── loot spots ──────────────────────────────────────────────────────────────────────────

        static GameObject SpawnSpot(Vector3 at, Loot loot, bool hidden, int index, bool mound, ZoneSystem.SpawnMode mode, List<GameObject> spawned, bool covered = false)
        {
            var prefab = ZNetScene.instance?.GetPrefab(SpotPrefab);
            if (prefab == null) return null;
            if (mode == ZoneSystem.SpawnMode.Ghost) ZNetView.StartGhostInit();
            var go = Object.Instantiate(prefab, at, Quaternion.Euler(0, random.Next(360), 0));
            var zdo = go.GetComponent<ZNetView>()?.GetZDO();
            if (zdo != null)
            {
                zdo.Set(KindKey, (int)loot.Kind);
                zdo.Set(ValueKey, loot.Value);
                zdo.Set(CountKey, loot.Count);
                zdo.Set(HiddenKey, hidden);
                zdo.Set(IndexKey, index);
                zdo.Set(MoundKey, mound);
                if (covered) zdo.Set(CoverKey, true);
                go.GetComponent<LootSpot>()?.Show();
            }
            if (mode == ZoneSystem.SpawnMode.Ghost)
            {
                spawned.Add(go);
                ZNetView.FinishGhostInit();
            }
            return go;
        }

        /// <summary>What lies in a settlement for the taking or the finding (in the zone being made).</summary>
        public static void SpawnSettlement(Settlements.Built b, Vector2s zone, ZoneSystem.SpawnMode mode, List<GameObject> spawned)
        {
            int loc = b.Location.Number;
            int made = 0, covered = 0;
            Vector3 World(int z, int x, int y)
            {
                var (lx, ly, lz) = b.Plan.TilePos(z, y * SettlementPlan.Size + x);
                return b.Origin + new Vector3(lx, ly + 0.05f, lz);
            }
            foreach (var npc in Plugin.U5.NpcsOf(b.Location))
            {
                if (!npc.Exists || npc.Slot == 0) continue;
                var loot = LootTables.SettlementObject(npc.Type);
                if (loot.Kind == LootKind.None) continue;
                var at = World(npc.Schedule.Z[0], npc.Schedule.X[0], npc.Schedule.Y[0]);
                if (ZoneSystem.GetZone(at) != zone) continue;
                if (loot.Kind == LootKind.Chest)
                {
                    if (mode == ZoneSystem.SpawnMode.Ghost) ZNetView.StartGhostInit();
                    var go = SpawnChest(at, Quaternion.Euler(0, 90 * random.Next(4), 0), loot.Value, false);
                    if (mode == ZoneSystem.SpawnMode.Ghost) { if (go != null) spawned.Add(go); ZNetView.FinishGhostInit(); }
                }
                else SpawnSpot(at, loot, false, NotHidden, false, mode, spawned);
                made++;
            }
            foreach (var h in Hidden)
            {
                if (h.Location != loc || h.Loot.Kind == LootKind.None) continue;
                var at = World(h.Z, h.X, h.Y);
                if (ZoneSystem.GetZone(at) != zone) continue;
                var (lx, ly, lz) = b.Plan.TilePos(h.Z, h.Y * SettlementPlan.Size + h.X);
                bool under = Covered(b.Plan, lx, ly, lz);
                SpawnSpot(at, h.Loot, true, h.Index, false, mode, spawned, under);
                made++;
                if (under) covered++;
            }
            if (made > 0) Plugin.Log.LogInfo($"{b.Location.Name}: {made} pieces of loot in zone {zone} ({covered} hidden under furniture)");
        }

        /// <summary>Whether furniture stands on the tile around (x, y, z): a piece (not a floor) whose pivot is inside it, above its floor.</summary>
        static bool Covered(SettlementPlan plan, float x, float y, float z)
        {
            float half = plan.Tile / 2 - 0.1f;
            foreach (var p in plan.Pieces)
                if (Mathf.Abs(p.X - x) < half && Mathf.Abs(p.Z - z) < half && p.Y > y - 0.1f && p.Y < y + 2f
                    && p.Prefab.IndexOf("floor", System.StringComparison.OrdinalIgnoreCase) < 0) return true;
            return false;
        }

        /// <summary>What is hidden out in Britannia (in the zone being made): the hidden objects on the surface, the buried moonstones, the reagent patches.</summary>
        public static void SpawnLand(Vector2s zone, ZoneSystem.SpawnMode mode, List<GameObject> spawned)
        {
            var shape = Britannia.Active;
            if (shape == null) return;
            Vector3 Ground(int x, int y)
            {
                shape.TileCentre(x, y, out float wx, out float wz);
                return new Vector3(wx, Britannia.SeaLevel + Mathf.Max(shape.Height(wx, wz), 0f), wz);
            }
            void Place(int x, int y, Loot loot, int index)
            {
                var at = Ground(x, y);
                if (ZoneSystem.GetZone(at) != zone) return;
                SpawnSpot(at, loot, true, index, true, mode, spawned);
                Plugin.Log.LogInfo($"Hidden in the land at tile ({x},{y}): {loot}");
            }
            // (z 0 is Britannia; the Underworld's are not made yet)
            foreach (var h in Hidden)
                if (h.Location == 0 && h.Z == 0 && h.Loot.Kind != LootKind.None) Place(h.X, h.Y, h.Loot, h.Index);
            var start = Plugin.U5.InitialGame;
            for (int i = 0; i < 8 && start != null; i++)
            {
                var m = start.Moonstone(i);
                if (m.buried && m.z == 0) Place(m.x, m.y, new Loot(LootKind.Moonstone, i), MoonstoneBase + i);
            }
            for (int i = 0; i < 4; i++)
            {
                int x = Ovl.U8(PatchX + i), y = Ovl.U8(PatchY + i);
                if (x == 0 && y == 0) continue;
                // (the count, 2..15 sprigs, is rolled when picked)
                Place(x, y, new Loot(LootKind.Item, Ovl.U8(PatchReagent + i)), ReagentPatchBase + i);
            }
        }

        // ── searching and taking ────────────────────────────────────────────────────────────────

        /// <summary>E on a spot: the loot, if it can be had now; the spot is gone unless it is found again.</summary>
        public static void Take(LootSpot spot, Player player)
        {
            var nview = spot.GetComponent<ZNetView>();
            var zdo = nview?.GetZDO();
            if (zdo == null) return;
            var loot = new Loot((LootKind)zdo.GetInt(KindKey), zdo.GetInt(ValueKey), zdo.GetInt(CountKey));
            int index = zdo.GetInt(IndexKey, NotHidden);
            bool again = false;
            int day = EnvMan.instance != null ? EnvMan.instance.GetDay() : 0;
            var inv = player.GetInventory();
            bool Has(string special) => inv.GetAllItems().Any(i => i.m_dropPrefab != null && i.m_dropPrefab.name == Items.Special(special)?.Prefab);
            switch (index)
            {
                case LootTables.BlackthornKeys:
                    // found while the party has no keys
                    again = true;
                    if (Has("Key")) { Nothing(player); return; }
                    break;
                case LootTables.MinocSkullKeys:
                    // once a day
                    again = true;
                    if (zdo.GetInt(DayKey, -1) == day) { Nothing(player); return; }
                    break;
                case LootTables.GlassSword:
                    // while none is owned
                    again = true;
                    if (inv.GetAllItems().Any(i => i.m_dropPrefab != null && Items.EquipmentOf(i.m_dropPrefab.name) == 39)) { Nothing(player); return; }
                    break;
            }
            if (index >= ReagentPatchBase && index < MoonstoneBase)
            {
                // a reagent patch (its value the reagent): at midnight, once a day, 2..15 sprigs
                again = true;
                if (TownNpc.Hour() != 0 || zdo.GetInt(DayKey, -1) == day) { Nothing(player); return; }
                var reagent = Items.Reagent(loot.Value);
                if (reagent != null)
                {
                    int n = random.Next(2, 16);
                    var prefab = ObjectDB.instance.GetItemPrefab(reagent.Prefab);
                    if (prefab != null && !inv.AddItem(prefab, n)) ItemDrop.DropItem(prefab.GetComponent<ItemDrop>().m_itemData, n, player.transform.position, Quaternion.identity);
                    Found(player, $"{n} {reagent.Name}");
                }
                nview.ClaimOwnership();
                zdo.Set(DayKey, day);
                return;
            }
            Found(player, Give(inv, loot, player.transform.position));
            nview.ClaimOwnership();
            if (again) zdo.Set(DayKey, day);
            else nview.Destroy();
        }

        static void Found(Player p, string what) => p.Message(MessageHud.MessageType.Center, Ovl.CStr(SFound).Trim() + " " + what);
        static void Nothing(Player p) => p.Message(MessageHud.MessageType.Center, "Nothing!");
    }

    /// <summary>A loot chest: once opened, it is gone when empty (on its owner).</summary>
    public class U5Chest : MonoBehaviour
    {
        ZNetView nview;
        Container container;

        void Awake()
        {
            nview = GetComponent<ZNetView>();
            container = GetComponent<Container>();
            InvokeRepeating(nameof(Check), 2f, 2f);
        }

        void Check()
        {
            if (nview == null || !nview.IsValid() || !nview.IsOwner() || container == null) return;
            if (nview.GetZDO().GetBool(Treasure.OpenedKey) && !container.IsInUse() && container.GetInventory().NrOfItems() == 0) nview.Destroy();
        }
    }

    /// <summary>
    /// A piece of loot on its tile: seen (the item lying there: E takes it), hidden in a
    /// settlement (unseen: E searches), or under a mound out in the land (E searches).
    /// </summary>
    public class LootSpot : MonoBehaviour, Hoverable, Interactable
    {
        ZNetView nview;
        bool shown;

        void Awake()
        {
            nview = GetComponent<ZNetView>();
            if (nview?.GetZDO() != null) Show();
        }

        /// <summary>Its look: the item lying there, a mound, or nothing (made once its loot is known).</summary>
        public void Show()
        {
            var zdo = nview?.GetZDO();
            if (shown || zdo == null || zdo.GetInt(Treasure.KindKey) == 0) return;
            shown = true;
            if (zdo.GetBool(Treasure.CoverKey) && GetComponent<BoxCollider>() is BoxCollider box)
            {
                box.size = new Vector3(Treasure.CoverWidth, Treasure.CoverHeight, Treasure.CoverWidth);
                box.center = new Vector3(0, Treasure.CoverHeight / 2, 0);
            }
            if (!Recolour.Draws) return;
            GameObject look = null;
            if (zdo.GetBool(Treasure.MoundKey)) look = Visual("mudpile", 0.35f);
            else if (!zdo.GetBool(Treasure.HiddenKey))
            {
                var prefabs = UltimaItems.Map.Prefabs(Loot());
                if (prefabs.Count > 0) look = Visual(prefabs[0].prefab, 1f);
            }
            if (look == null) return;
            look.transform.SetParent(transform, false);
            look.transform.localPosition = Vector3.zero;
        }

        Loot Loot()
        {
            var zdo = nview.GetZDO();
            return new Loot((LootKind)zdo.GetInt(Treasure.KindKey), zdo.GetInt(Treasure.ValueKey), zdo.GetInt(Treasure.CountKey));
        }

        /// <summary>A prefab's look only (no scripts, physics or colliders).</summary>
        static GameObject Visual(string prefabName, float scale)
        {
            var prefab = ObjectDB.instance?.GetItemPrefab(prefabName) ?? ZNetScene.instance?.GetPrefab(prefabName);
            if (prefab == null) return null;
            var holder = new GameObject("u5_loot_holder");
            holder.SetActive(false);
            var copy = Object.Instantiate(prefab, holder.transform);
            for (int pass = 0; pass < 4; pass++)
                foreach (var c in copy.GetComponentsInChildren<Component>(true))
                    if (c is MonoBehaviour || c is Rigidbody || c is Collider) Object.DestroyImmediate(c);
            copy.transform.SetParent(null, false);
            copy.transform.localScale *= scale;
            Object.Destroy(holder);
            copy.SetActive(true);
            return copy;
        }

        bool Hidden => nview?.GetZDO()?.GetBool(Treasure.HiddenKey) ?? true;

        public string GetHoverText()
        {
            if (nview?.GetZDO() == null) return "";
            if (Hidden) return Localization.instance.Localize("[<color=yellow><b>$KEY_Use</b></color>] Search");
            return Localization.instance.Localize($"{GetHoverName()}\n[<color=yellow><b>$KEY_Use</b></color>] Take");
        }

        public string GetHoverName()
        {
            if (nview?.GetZDO() == null || Hidden) return "";
            var prefabs = UltimaItems.Map.Prefabs(Loot());
            return prefabs.Count > 0 ? UltimaItems.Map.Get(prefabs[0].prefab)?.Name ?? prefabs[0].prefab : "";
        }

        public float GetHoverOffset() => 0f;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !(user is Player p) || p != Player.m_localPlayer || nview == null || !nview.IsValid()) return false;
            Treasure.Take(this, p);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    }
}
