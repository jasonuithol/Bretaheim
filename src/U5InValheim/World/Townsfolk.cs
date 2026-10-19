using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jotunn.Entities;
using Jotunn.Managers;
using U5InValheim.Data;
using U5InValheim.WorldGen.Core;
using UnityEngine;
using Object = UnityEngine.Object;
using Pose = U5InValheim.WorldGen.Core.Pose;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// The people of the built settlements (MASTER_PLAN §5.3, Phase 3): one townsperson per
    /// .NPC slot, spawned with the settlement's home zone and kept in the world like its pieces.
    /// A townsperson is Valheim's player body without the player (like Haldor and Hildir, not a
    /// creature): dressed from npcs.json through VisEquipment, moved by <see cref="TownNpc"/>.
    /// </summary>
    public static class TownsfolkSpawner
    {
        public const string PrefabName = "u5_townsperson";
        public const string LocKey = "u5_npc_loc", SlotKey = "u5_npc_slot", FloorKey = "u5_npc_z", PoseKey = "u5_npc_pose", SwingKey = "u5_npc_swing",
            DeadKey = "u5_npc_dead", TempKey = "u5_npc_temp", FromKey = "u5_npc_from", FromSlotKey = "u5_npc_from_slot";

        static Dictionary<int, PersonLook> looks;

        public static void Register() => PrefabManager.OnVanillaPrefabsAvailable += Create;

        /// <summary>
        /// The townsperson prefab: Valheim's player body made a real creature (a Humanoid with
        /// the Player's own settings, in the players' faction), so monsters see, chase and fight
        /// townsfolk, gear protects and arms them, and they walk under physics.
        /// </summary>
        static void Create()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Create;
            var prefab = PrefabManager.Instance.CreateClonedPrefab(PrefabName, "Player");
            var player = prefab != null ? prefab.GetComponent<Player>() : null;
            if (player == null)
            {
                Plugin.Log.LogError("Townsfolk: Player prefab not found");
                return;
            }
            // the Player's settings (serialized fields) on a plain Humanoid; its runtime state is made in Awake
            var human = prefab.AddComponent<Humanoid>();
            foreach (var type in new[] { typeof(Character), typeof(Humanoid) })
                foreach (var f in HarmonyLib.AccessTools.GetDeclaredFields(type))
                    if (!f.IsStatic && !f.IsInitOnly && !f.IsNotSerialized && (f.IsPublic || f.GetCustomAttributes(typeof(SerializeField), false).Length > 0))
                        f.SetValue(human, f.GetValue(player));
            foreach (var type in new[] { typeof(PlayerController), typeof(Talker), typeof(Player), typeof(Skills) })
                foreach (var c in prefab.GetComponentsInChildren(type, true))
                    Object.DestroyImmediate(c);
            human.m_name = "Townsperson";
            human.m_group = "u5_townsfolk";
            human.m_faction = Character.Faction.Players;
            human.m_health = 8 * Ultima.Scale; // (a villager's; each gets their type's when dressed)
            // a little quicker than their schedule walks and hurries, so the body keeps up with it
            human.m_walkSpeed = TownWalker.Speed + 0.4f;
            human.m_runSpeed = TownWalker.Hurry + 0.5f;
            human.m_defaultItems = new GameObject[0];
            human.m_randomWeapon = new GameObject[0];
            human.m_randomArmor = new GameObject[0];
            human.m_randomShield = new GameObject[0];
            human.m_randomSets = new Humanoid.ItemSet[0];
            var nview = prefab.GetComponent<ZNetView>();
            nview.m_persistent = true;
            var vis = prefab.GetComponent<VisEquipment>();
            if (vis != null) vis.m_playerComponent = null;
            // on the creatures' layer (the hover ray and the monsters' eyes see it; the player layer is not one)
            prefab.layer = LayerMask.NameToLayer("character");
            prefab.AddComponent<TownNpc>();
            PrefabManager.Instance.AddPrefab(new CustomPrefab(prefab, false));
            Plugin.Log.LogInfo($"Townsfolk prefab {PrefabName}: {string.Join(",", prefab.GetComponents<Component>().Select(c => c.GetType().Name))}");
        }

        public static PersonLook LookOf(int creatureType)
        {
            if (looks == null)
            {
                try { looks = Townsfolk.ParseLooks(File.ReadAllText(Path.Combine(Plugin.PluginDirectory, "mappings", "npcs.json"))); }
                catch (Exception e) { Plugin.Log.LogError($"npcs.json: {e.Message}"); looks = new Dictionary<int, PersonLook>(); }
            }
            return looks.TryGetValue(creatureType, out var l) ? l : new PersonLook { Type = creatureType };
        }

        /// <summary>The people of a settlement whose home zone is being generated, each at their place for this hour.</summary>
        public static void Spawn(Settlements.Built b, ZoneSystem.SpawnMode mode, List<GameObject> spawnedObjects)
        {
            var prefab = ZNetScene.instance.GetPrefab(PrefabName);
            if (prefab == null) return;
            int hour = TownNpc.Hour(), n = 0;
            foreach (var npc in Plugin.U5.NpcsOf(b.Location))
            {
                if (npc.Slot == 0 || !npc.Exists || npc.CreatureType < 0 || npc.CreatureType == 3) continue;
                var s = npc.Schedule;
                int place = s.PlaceAt(hour), z = s.Z[place], cell = s.Y[place] * SettlementPlan.Size + s.X[place];
                var (x, y, lz) = b.Plan.TilePos(z, cell);
                if (mode == ZoneSystem.SpawnMode.Ghost) ZNetView.StartGhostInit();
                var go = Object.Instantiate(prefab, b.Origin + new Vector3(x, y + 0.05f, lz), Quaternion.Euler(0, (npc.Slot * 47) % 360, 0));
                var zdo = go.GetComponent<ZNetView>().GetZDO();
                zdo.Set(LocKey, b.Location.Number);
                zdo.Set(SlotKey, npc.Slot);
                zdo.Set(FloorKey, z);
                var look = LookOf(npc.CreatureType);
                if (look.Scale != 1f)
                {
                    var scale = Vector3.one * look.Scale;
                    go.transform.localScale = scale;
                    zdo.Set(Settlements.ScaleKey, scale);
                }
                Dress(zdo, b.Location, npc);
                go.GetComponent<TownNpc>().Init();
                if (mode == ZoneSystem.SpawnMode.Ghost)
                {
                    spawnedObjects.Add(go);
                    ZNetView.FinishGhostInit();
                }
                n++;
            }
            Plugin.Log.LogInfo($"{b.Location.Name}: {n} townsfolk");
        }

        /// <summary>Temporary guards running in from the town's edge (TownWatch): the look of the town's guards, gone when the fight is over.</summary>
        /// <summary>A guard of a settlement, or null for a town without (North Britanny).</summary>
        public static SettlementNpc GuardOf(Data.Location loc) => Plugin.U5.NpcsOf(loc).FirstOrDefault(n => n.Slot > 0 && n.Exists && (n.IsGuard || n.CreatureType == 12));

        /// <summary>Temporary guards come in at a town's edge: its own, or (a town without guards) those of the settlement that sent them.</summary>
        public static void SpawnReinforcements(Settlements.Built b, int edgeCell, int count, Settlements.Built from = null)
        {
            var prefab = ZNetScene.instance.GetPrefab(PrefabName);
            from = from ?? b;
            var guard = GuardOf(from.Location);
            // (they belong to the town they defend: any of its people's slots carries them; they are labelled Guard)
            var anyone = Plugin.U5.NpcsOf(b.Location).FirstOrDefault(n => n.Slot > 0 && n.Exists);
            if (prefab == null || guard == null || anyone == null) return;
            var (x, y, z) = b.Plan.TilePos(0, edgeCell);
            for (int i = 0; i < count; i++)
            {
                var go = Object.Instantiate(prefab, b.Origin + new Vector3(x + (i % 3 - 1) * 0.8f, y + 0.05f, z + (i / 3) * 0.8f), Quaternion.identity);
                var zdo = go.GetComponent<ZNetView>().GetZDO();
                zdo.Set(LocKey, b.Location.Number);
                zdo.Set(SlotKey, from == b ? guard.Slot : anyone.Slot);
                zdo.Set(FloorKey, 0);
                zdo.Set(TempKey, true);
                zdo.Set(FromKey, from.Location.Number);
                zdo.Set(FromSlotKey, guard.Slot);
                Dress(zdo, from.Location, guard);
                var npc = go.GetComponent<TownNpc>();
                npc.Init();
                npc.Duty = Duty.Fight;
            }
        }

        static readonly Vector3[] Skins = { new Vector3(1f, 1f, 1f), new Vector3(0.9f, 0.82f, 0.75f), new Vector3(0.75f, 0.62f, 0.5f), new Vector3(0.55f, 0.42f, 0.32f), new Vector3(0.38f, 0.28f, 0.2f) };
        static readonly Vector3[] Hairs = { new Vector3(0.15f, 0.1f, 0.07f), new Vector3(0.35f, 0.22f, 0.12f), new Vector3(0.6f, 0.45f, 0.25f), new Vector3(0.85f, 0.7f, 0.45f), new Vector3(0.5f, 0.2f, 0.1f), new Vector3(0.6f, 0.6f, 0.6f), new Vector3(0.05f, 0.05f, 0.05f) };

        static uint Seed(Data.Location loc, SettlementNpc npc) => (uint)(loc.Number * 7919 + npc.Slot * 104729) * 2654435761u;
        static int Pick(uint h, int count, int salt) => (int)(((h ^ (uint)salt * 0x9e3779b9u) >> 8) % (uint)count);

        /// <summary>Body and colouring (ZDO, as VisEquipment reads them), fixed per person; their clothes are <see cref="Outfit"/>.</summary>
        static void Dress(ZDO zdo, Data.Location loc, SettlementNpc npc)
        {
            uint h = Seed(loc, npc);
            int model = Pick(h, 2, 1);
            zdo.Set(ZDOVars.s_modelIndex, model);
            zdo.Set(ZDOVars.s_skinColor, Skins[Pick(h, Skins.Length, 2)]);
            zdo.Set(ZDOVars.s_hairColor, Hairs[Pick(h, Hairs.Length, 3)]);
            zdo.Set(ZDOVars.s_hairItem, ("Hair" + (1 + Pick(h, 20, 4))).GetStableHashCode());
            if (model == 0 && Pick(h, 2, 5) == 0) zdo.Set(ZDOVars.s_beardItem, ("Beard" + (1 + Pick(h, 20, 6))).GetStableHashCode());
            else
            {
                // (the body may have grown a random beard as it was made)
                zdo.Set(ZDOVars.s_beardItem, 0);
                zdo.Set(ZDOVars.s_noBeard, true);
            }
        }

        /// <summary>What a person wears and carries (npcs.json), fixed per person: equipped for real, so armour protects and weapons strike.</summary>
        public static List<string> Outfit(Data.Location loc, SettlementNpc npc)
        {
            var look = LookOf(npc.CreatureType);
            uint h = Seed(loc, npc);
            int model = Pick(h, 2, 1);
            var items = new List<string>();
            // other bodies (children: Hildir) are not walkers: the same body, small, in Hildir's clothes
            var outfit = look.Base == "human" ? look.Outfit : new[] { "hildir:work-clothes" };
            foreach (var entry in outfit)
                switch (entry)
                {
                    case "hildir:work-clothes":
                        items.Add((model == 1 ? "ArmorDress" : "ArmorTunic") + (1 + Pick(h, 10, 7)));
                        if (Pick(h, 3, 8) == 0) items.Add("HelmetHat" + (1 + Pick(h, 10, 9)));
                        break;
                    case "hildir:beaded-tunic-red": items.Add("ArmorTunic" + (1 + Pick(h, 10, 10))); break;
                    default: items.Add(entry); break;
                }
            return items;
        }
    }

    /// <summary>
    /// A townsperson in the world, a real creature (Humanoid): the owner of its ZDO runs its day
    /// (<see cref="TownWalker"/>, a point running ahead along their paths that the body walks after
    /// under physics, opening the doors in its way and closing them behind), sits or lies it down
    /// (Valheim's attach_chair / attach_bed, as for a player), and in a fight swings its weapon for
    /// real. Monsters see and fight townsfolk; a townsperson who falls is gone until the next
    /// morning; players cannot hurt them (crimes come with karma).
    /// </summary>
    public class TownNpc : MonoBehaviour, Hoverable, Interactable
    {
        /// <summary>How much higher a smaller body sits, per unit of scale lost (a 0.7 child sank about 0.5 m).</summary>
        const float ChildSeatLift = 1.6f;
        const float Near = 0.3f, CatchUp = 4f, StuckFor = 2f, DoorReach = 2f, DoorBehind = 3f;
        static readonly int AttachChair = ZSyncAnimation.GetHash("attach_chair"), AttachBed = ZSyncAnimation.GetHash("attach_bed");

        ZNetView nview;
        Humanoid human;
        Rigidbody body;
        RigidbodyConstraints freeConstraints;
        ZSyncAnimation zanim;
        Settlements.Built home;
        SettlementNpc npc;
        TownWalker walker;
        string label = "";
        bool ready;

        /// <summary>The hour of Britannia's day (Valheim's day as 24 Ultima hours).</summary>
        public static int Hour() => EnvMan.instance != null ? Mathf.FloorToInt(EnvMan.instance.GetDayFraction() * 24f) % 24 : 12;

        void Awake()
        {
            nview = GetComponent<ZNetView>();
            human = GetComponent<Humanoid>();
            body = GetComponent<Rigidbody>();
            freeConstraints = body.constraints;
            zanim = GetComponent<ZSyncAnimation>();
            TownsfolkPatches.Register(human, this);
            if (nview.GetZDO() != null && nview.GetZDO().GetInt(TownsfolkSpawner.LocKey) != 0) Init();
        }

        /// <summary>Reads who this is from the ZDO (also right after spawning, whose Awake ran before the keys were set).</summary>
        public void Init()
        {
            var zdo = nview.GetZDO();
            if (zdo == null || ready) return;
            home = Settlements.Get(zdo.GetInt(TownsfolkSpawner.LocKey));
            int slot = zdo.GetInt(TownsfolkSpawner.SlotKey);
            if (home == null || slot <= 0 || slot >= NpcFile.NpcsPerSettlement) return;
            home.Watch.Join(this);
            npc = Plugin.U5.NpcsOf(home.Location)[slot];
            label = zdo.GetBool(TownsfolkSpawner.TempKey) ? "Guard" : Townsfolk.Label(Plugin.U5, home.Location, npc);
            if (human != null) human.m_name = label;
            // they fight on their creature type's stats (a guard's, for reinforcements)
            UltimaCreatures.Register(human, CreatureType);
            ready = true;
        }

        public int Location => home?.Location.Number ?? 0;
        public int Slot => npc?.Slot ?? 0;
        /// <summary>Their conversation (.TLK), or null for shopkeepers, guards and the silent.</summary>
        public TlkScript Script => npc != null ? Plugin.U5.ScriptOf(home.Location, npc) : null;
        bool talking;
        bool Asleep => nview.GetZDO()?.GetInt(TownsfolkSpawner.PoseKey) == (int)Pose.Lie;

        // ── body: gear and health (on the owner) ────────────────────────────────────────────

        bool equipped;

        /// <summary>Their creature type (DATA.OVL stat line): the .NPC type's, a guard's for reinforcements.</summary>
        public int CreatureType => IsReinforcement ? 12 : Math.Max(0, npc?.CreatureType ?? 4);

        /// <summary>
        /// Their clothes and arms, equipped for real (a creature's inventory is not saved: whoever
        /// moves them dresses them; what they hold is only how they look and swing: their blows
        /// are their type's), and their type's hit points.
        /// </summary>
        void Outfit()
        {
            if (equipped || human == null) return;
            equipped = true;
            var (lookLoc, lookNpc) = LookSource();
            var inv = human.GetInventory();
            foreach (var name in TownsfolkSpawner.Outfit(lookLoc, lookNpc))
            {
                if (ObjectDB.instance.GetItemPrefab(name) == null) { Plugin.Log.LogWarning($"Townsfolk: no item {name}"); continue; }
                var item = inv.AddItem(name, 1, 1, 0, 0L, "", false);
                if (item != null) human.EquipItem(item, false);
            }
            UltimaCreatures.SetHitPoints(human, UltimaCreatures.Stats(CreatureType));
        }

        // ── the town's alarm (TownWatch) ────────────────────────────────────────────────────

        /// <summary>What they are doing about a monster in town (set by the town's watch).</summary>
        public Duty Duty
        {
            get => duty;
            set
            {
                if (duty == value) return;
                duty = value;
                if (walker != null && (value == Duty.None || value == Duty.Fight)) walker.StopChase();
            }
        }
        Duty duty;

        public bool IsOwned => ready && nview.GetZDO() != null && nview.IsOwner();
        public bool IsGuard => npc != null && (npc.IsGuard || npc.CreatureType == 12 || IsReinforcement);
        public bool IsReinforcement => nview.GetZDO()?.GetBool(TownsfolkSpawner.TempKey) ?? false;
        public bool IsDead => (nview.GetZDO()?.GetInt(TownsfolkSpawner.DeadKey) ?? 0) > 0;
        public int Floor => walker?.Floor ?? nview.GetZDO()?.GetInt(TownsfolkSpawner.FloorKey) ?? 0;
        public int Cell => walker?.Cell ?? 0;
        /// <summary>Gone home to hide (and arrived).</summary>
        public bool Hidden => duty == Duty.Hide && walker != null && walker.Arrived;
        public float Health => human != null ? human.GetHealth() : 0f;
        public float MaxHealth => human != null ? human.GetMaxHealth() : 1f;

        /// <summary>Hurrying to a tile (out of town for help).</summary>
        public void RunTo(int z, int cell) => walker?.Chase(z, cell);
        public bool ArrivedAt(int cell) => walker != null && walker.Arrived && walker.Cell == cell;

        /// <summary>Hurrying to another townsperson (with news, or to fetch them).</summary>
        public void RunTo(TownNpc other)
        {
            if (walker == null || other.walker == null) return;
            walker.Chase(other.Floor, other.Cell);
        }

        /// <summary>Whole again (after a fight).</summary>
        public void Heal() { if (human != null) human.SetHealth(human.GetMaxHealth()); }

        /// <summary>Damage outside a blow (tests): falls at 0 like any other.</summary>
        public void Hurt(float damage)
        {
            if (human == null || IsDead) return;
            human.SetHealth(Mathf.Max(0, human.GetHealth() - damage));
            if (human.GetHealth() <= 0) Fall();
        }

        /// <summary>
        /// At 0 health a townsperson falls (Valheim's death ragdoll) and is gone from sight until
        /// the next morning, when they are back, whole; reinforcements are gone for good.
        /// </summary>
        public void Fall()
        {
            var zdo = nview.GetZDO();
            if (zdo == null || IsDead) return;
            Plugin.Log.LogInfo($"{home.Location.Name}: {label} has fallen");
            foreach (var go in human.m_deathEffects.Create(transform.position, transform.rotation, transform))
            {
                var ragdoll = go.GetComponent<Ragdoll>();
                if (ragdoll != null) HarmonyLib.Traverse.Create(human).Method("OnRagdollCreated", ragdoll).GetValue();
            }
            if (IsReinforcement) { ZNetScene.instance.Destroy(gameObject); return; }
            zdo.Set(TownsfolkSpawner.DeadKey, (EnvMan.instance?.GetDay() ?? 0) + 1);
            duty = Duty.None;
            if (walker != null) home.Crowd.Leave(walker);
            walker = null;
        }

        // ── talking ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Talking (TALK.OVL talk_to_npc): townsfolk with a conversation open the panel; shopkeepers
        /// their shop while it is open, else "come see me when it's open"; the rest give no
        /// response. Nobody talks in their sleep.
        /// </summary>
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !ready || user != Player.m_localPlayer || Asleep || IsDead) return false;
            var me = Player.m_localPlayer;
            ShopInfo shop = npc.IsShopkeeper ? Plugin.U5.Shops.Of(home.Location, npc) : null;
            if (shop != null && !Shops.IsOpen(npc, Hour()))
            {
                me.Message(MessageHud.MessageType.Center, Plugin.U5.Shops.Closed(shop));
                return true;
            }
            if (shop == null && Script == null)
            {
                me.Message(MessageHud.MessageType.Center, "No response.");
                return true;
            }
            // the one talking moves them (they stop and turn to you)
            nview.ClaimOwnership();
            talking = true;
            if (shop != null) TalkPanel.OpenShop(this, shop);
            else TalkPanel.Open(this);
            return true;
        }

        Trader trader, sellTrader;

        /// <summary>The armourer's buying side for the trade window (filled with the player's arms when opened).</summary>
        public Trader SellTrader()
        {
            if (sellTrader != null) return sellTrader;
            var go = new GameObject("u5_shop_sell");
            go.transform.SetParent(transform, false);
            sellTrader = go.AddComponent<Trader>();
            sellTrader.enabled = false;
            sellTrader.m_name = label;
            return sellTrader;
        }

        /// <summary>
        /// The shop's stock for Valheim's trade window (a disabled Trader on a child, so the
        /// townsperson stays the thing you point at), or null for shops that sell nothing yet.
        /// </summary>
        public Trader ShopTrader()
        {
            if (trader != null) return trader;
            var shop = npc != null && npc.IsShopkeeper ? Plugin.U5.Shops.Of(home.Location, npc) : null;
            if (shop == null) return null;
            var items = new List<Trader.TradeItem>();
            foreach (var ware in Plugin.U5.Shops.Stock(shop))
            {
                var map = ShopTrading.Items.Of(ware.Item);
                var drop = map != null ? ObjectDB.instance?.GetItemPrefab(map.Value.prefab)?.GetComponent<ItemDrop>() : null;
                if (drop == null) continue;
                // (the trade window expects the empty strings and effect list a serialized trader has, not nulls)
                items.Add(new Trader.TradeItem
                {
                    m_prefab = drop, m_stack = ware.Quantity, m_price = ware.Price, m_name = ware.Name,
                    m_tooltip = "", m_requiredGlobalKey = "", m_buyKey = "", m_incrementKey = "", m_buyPlayerEffects = new EffectList(),
                });
            }
            if (items.Count == 0) return null;
            var go = new GameObject("u5_shop");
            go.transform.SetParent(transform, false);
            trader = go.AddComponent<Trader>();
            trader.enabled = false;
            trader.m_name = shop.Keeper;
            trader.m_items = items;
            go.AddComponent<ShopTrading.Visit>().Shop = shop;
            return trader;
        }

        public void EndTalk() => talking = false;
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        // ── living their day ────────────────────────────────────────────────────────────────

        void Update()
        {
            if (!ready || nview.GetZDO() == null) return;
            if (talking && TalkPanel.With != this && !ShopTrading.IsTrading(trader) && !ShopTrading.IsTrading(sellTrader)) talking = false;
            if (nview.IsOwner()) Live(Time.deltaTime);
            else if (walker != null)
            {
                // whoever owns it next picks up from where it stands (and dresses them again)
                home.Crowd.Leave(walker);
                walker = null;
                equipped = false;
            }
            // (gone this frame: a reinforcement leaving or fallen)
            if (nview.GetZDO() == null) return;
            ShowBody(!IsDead);
        }

        void Live(float dt)
        {
            var zdo = nview.GetZDO();
            Outfit();
            if (walker == null)
            {
                var local = transform.position - home.Origin;
                walker = new TownWalker(home.Plan, npc.Schedule, zdo.GetInt(TownsfolkSpawner.FloorKey), local.x, local.z, home.Location.Number * 32 + npc.Slot, home.Crowd);
                bodyFloor = walker.Floor;
            }
            home.Watch.Tick();
            if (IsDead)
            {
                Still();
                // fallen: back, whole, the next morning
                var env = EnvMan.instance;
                if (env != null && env.GetDay() >= zdo.GetInt(TownsfolkSpawner.DeadKey) && Hour() >= 6)
                {
                    zdo.Set(TownsfolkSpawner.DeadKey, 0);
                    Heal();
                    walker = null;
                }
                return;
            }
            if (IsReinforcement && duty == Duty.None) duty = Duty.Leave;
            if (talking)
            {
                // stopped, turned to the one they talk to (in a chair or bed they stay put)
                human.SetMoveDir(Vector3.zero);
                var me = Player.m_localPlayer;
                if (walker.Pose == Pose.Stand && me != null)
                {
                    var to = me.transform.position - transform.position;
                    to.y = 0;
                    if (to.sqrMagnitude > 0.01f) human.SetLookDir(to.normalized);
                }
                return;
            }
            int floor = walker.Floor;
            facing = false;
            steering = false;
            if (duty != Duty.None) OnDuty();
            if (nview.GetZDO() == null) return; // (left town)
            walker.Tick(dt, Hour());
            if (walker.Floor != floor) zdo.Set(TownsfolkSpawner.FloorKey, walker.Floor);
            if (zdo.GetInt(TownsfolkSpawner.PoseKey) != (int)walker.Pose) zdo.Set(TownsfolkSpawner.PoseKey, (int)walker.Pose);
            Drive(dt);
        }

        // ── the body follows the schedule ───────────────────────────────────────────────────

        int bodyFloor;
        float behind;
        bool facing, steering;
        readonly List<Door> opened = new List<Door>();
        float doorsAt;

        /// <summary>
        /// The body walks (or runs) after the walker's point; ladders and anything left far behind
        /// (a table in the way, a stuck door) are a step on the spot. In a chair or bed it rests on
        /// the seat, still. Doors in the way open, and close again behind.
        /// </summary>
        void Drive(float dt)
        {
            zanim.SetBool(AttachChair, walker.Pose == Pose.Sit);
            zanim.SetBool(AttachBed, walker.Pose == Pose.Lie);
            if (walker.Pose != Pose.Stand)
            {
                // on the chair or bed, turned as it is (as Valheim seats a player); Valheim's sitting
                // pose is made for a full-size body: a child would sink into the seat
                float lift = walker.Pose == Pose.Sit ? (1f - transform.localScale.y) * ChildSeatLift : 0f;
                Hold(true);
                Place(home.Origin + new Vector3(walker.RestX, walker.RestY + lift, walker.RestZ), Quaternion.Euler(0, walker.RestYaw, 0));
                human.SetMoveDir(Vector3.zero);
                return;
            }
            Hold(false);
            if (steering) { behind = 0; return; }
            var target = home.Origin + new Vector3(walker.X, walker.Y, walker.Z);
            if (walker.Floor != bodyFloor || Mathf.Abs(target.y - transform.position.y) > 2.5f)
            {
                // a ladder: on the other storey at once, as when a player climbs
                bodyFloor = walker.Floor;
                Place(target, transform.rotation);
            }
            var to = target - transform.position;
            to.y = 0;
            float d = to.magnitude;
            behind = d > CatchUp || (d > 1f && body.linearVelocity.magnitude < 0.2f) ? behind + dt : 0;
            if (behind > StuckFor)
            {
                behind = 0;
                Place(target, transform.rotation);
                d = 0;
            }
            if (d > Near)
            {
                var dir = to / d;
                human.SetMoveDir(dir);
                human.SetRun(walker.Pace > TownWalker.Speed + 0.5f);
                if (!facing) human.SetLookDir(dir);
                Doors(dir);
            }
            else
            {
                human.SetMoveDir(Vector3.zero);
                if (!facing) human.SetLookDir(new Vector3(walker.HeadingX, 0, walker.HeadingZ));
                Doors(Vector3.zero);
            }
        }

        void Still()
        {
            human.SetMoveDir(Vector3.zero);
            Hold(true);
        }

        /// <summary>
        /// Held still (seated, lying, fallen) by freezing the body rather than making it kinematic:
        /// Valheim's movement keeps setting a character's velocity, which a kinematic body refuses
        /// with a warning each frame.
        /// </summary>
        void Hold(bool held)
        {
            var want = held ? RigidbodyConstraints.FreezeAll : freeConstraints;
            if (body.constraints == want) return;
            body.constraints = want;
            body.linearVelocity = Vector3.zero;
        }

        void Place(Vector3 at, Quaternion rot)
        {
            transform.SetPositionAndRotation(at, rot);
            body.position = at;
            body.rotation = rot;
            body.linearVelocity = Vector3.zero;
        }

        static readonly Collider[] nearby = new Collider[16];

        /// <summary>A closed door just ahead opens; one left a few steps behind closes again.</summary>
        void Doors(Vector3 dir)
        {
            if (Time.time < doorsAt) return;
            doorsAt = Time.time + 0.25f;
            for (int i = opened.Count - 1; i >= 0; i--)
            {
                var door = opened[i];
                if (door == null) { opened.RemoveAt(i); continue; }
                if (Vector3.Distance(door.transform.position, transform.position) < DoorBehind) continue;
                if (DoorOpen(door)) door.Interact(human, false, false);
                opened.RemoveAt(i);
            }
            if (dir == Vector3.zero) return;
            int n = Physics.OverlapSphereNonAlloc(transform.position + dir * 1f, DoorReach * 0.6f, nearby, LayerMask.GetMask("piece", "piece_nonsolid", "Default", "static_solid"));
            for (int i = 0; i < n; i++)
            {
                var door = nearby[i] != null ? nearby[i].GetComponentInParent<Door>() : null;
                if (door == null || opened.Contains(door) || DoorOpen(door)) continue;
                if (Vector3.Dot(door.transform.position - transform.position, dir) <= 0) continue;
                door.Interact(human, false, false);
                opened.Add(door);
            }
        }

        static bool DoorOpen(Door door) => door.GetComponent<ZNetView>()?.GetZDO()?.GetInt(ZDOVars.s_state) != 0;

        // ── on duty in the alarm ────────────────────────────────────────────────────────────

        Character foe;
        int leaveBy = -1;

        /// <summary>
        /// On duty in the town's alarm: messengers and recruiting guards are moved by the watch
        /// (RunTo), the hiding run home and stay, fighters fight, reinforcements leave by the edge
        /// they came in at and are gone.
        /// </summary>
        void OnDuty()
        {
            switch (duty)
            {
                case Duty.Hide:
                    if (!walker.Chasing)
                    {
                        var (hz, hc) = TownAlarm.Home(home.Plan, npc.Schedule);
                        walker.Chase(hz, hc);
                    }
                    break;
                case Duty.Fight:
                    Guard();
                    break;
                case Duty.Leave:
                    // out by the nearest edge (chosen once: two as near would have them dither)
                    if (leaveBy < 0)
                    {
                        var local = transform.position - home.Origin;
                        leaveBy = TownAlarm.EdgeNear(home.Plan, local.x, local.z);
                    }
                    if (leaveBy < 0 || ArrivedAt(leaveBy)) { ZNetScene.instance.Destroy(gameObject); return; }
                    walker.Chase(0, leaveBy);
                    break;
            }
        }

        const float ThrowFrom = 5f, ThrowTo = 20f, ThrowEvery = 6f;
        float throwAt;

        /// <summary>
        /// A fighting guard runs the nearest monster in town down along the streets (down the
        /// ladders if need be) and, in reach, turns on it and attacks with their weapon (Valheim's
        /// own attack: its animation, its hit, its damage). A guard with a spear in sight of a
        /// monster further off throws it first (the spear's secondary attack), then draws another.
        /// </summary>
        void Guard()
        {
            foe = home.Watch.Target(transform.position);
            if (foe == null)
            {
                walker.Halt();
                return;
            }
            Rearm();
            var to = foe.transform.position - transform.position;
            to.y = 0;
            // in reach as Valheim's creatures judge it: the weapon's AI range plus both bodies
            var held = human.GetCurrentWeapon()?.m_shared;
            float reach = Mathf.Max(held?.m_aiAttackRange ?? 2f, held?.m_attack.m_attackRange ?? 1.5f) + human.GetRadius() + foe.GetRadius();
            bool seen = InSight(foe);
            if (to.magnitude >= ThrowFrom && to.magnitude <= ThrowTo && Time.time >= throwAt && CanThrow() && seen)
            {
                throwAt = Time.time + ThrowEvery;
                Steer(Vector3.zero);
                human.SetLookDir((foe.GetCenterPoint() - human.GetEyePoint()).normalized);
                human.StartAttack(foe, true);
                return;
            }
            if (to.magnitude > reach)
            {
                // the last stretch straight at it, when nothing is in the way; else along the streets
                if (to.magnitude < 6f && seen)
                {
                    Steer(to.normalized);
                    human.SetLookDir(to.normalized);
                    return;
                }
                var local = foe.transform.position - home.Origin;
                walker.Chase(0, home.Plan.CellAt(local.x, local.z));
                return;
            }
            Steer(Vector3.zero);
            human.SetLookDir(to.normalized);
            human.StartAttack(foe, false);
        }

        /// <summary>Off the paths: the body is moved directly (closing in, or standing to fight) and the walker kept where it is.</summary>
        void Steer(Vector3 dir)
        {
            steering = true;
            facing = true;
            var local = transform.position - home.Origin;
            walker.Chase(walker.Floor, walker.Cell);
            walker.Follow(local.x, local.z);
            human.SetMoveDir(dir);
            human.SetRun(dir != Vector3.zero);
            Doors(dir);
        }

        /// <summary>Their weapon throws (a spear's secondary attack is its throw).</summary>
        bool CanThrow()
        {
            var weapon = human.GetCurrentWeapon();
            return weapon != null && weapon.m_shared.m_skillType == Skills.SkillType.Spears && weapon.HaveSecondaryAttack();
        }

        static readonly int SightMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");

        bool InSight(Character c) => !Physics.Linecast(human.GetEyePoint(), c.GetCenterPoint(), SightMask);

        /// <summary>A thrown weapon is gone: they draw another of their outfit's (guards never run out).</summary>
        void Rearm()
        {
            if (human.GetCurrentWeapon() != null && human.GetCurrentWeapon() != human.m_unarmedWeapon?.m_itemData) return;
            var npcFrom = LookSource();
            foreach (var name in TownsfolkSpawner.Outfit(npcFrom.loc, npcFrom.npc))
            {
                var drop = ObjectDB.instance.GetItemPrefab(name)?.GetComponent<ItemDrop>();
                if (drop == null || !drop.m_itemData.IsWeapon()) continue;
                var item = human.GetInventory().AddItem(name, 1, 1, 0, 0L, "", false);
                if (item != null) human.EquipItem(item, false);
                break;
            }
        }

        (Data.Location loc, SettlementNpc npc) LookSource()
        {
            var zdo = nview.GetZDO();
            int from = zdo.GetInt(TownsfolkSpawner.FromKey);
            if (from <= 0) return (home.Location, npc);
            var loc = Plugin.U5.Location(from);
            return (loc, Plugin.U5.NpcsOf(loc)[zdo.GetInt(TownsfolkSpawner.FromSlotKey)]);
        }

        // ── every peer ──────────────────────────────────────────────────────────────────────

        Renderer[] bodies;
        Collider[] solids;
        bool shown = true;

        /// <summary>A fallen townsperson is gone from sight (on every peer) until they come back.</summary>
        void ShowBody(bool show)
        {
            if (show == shown) return;
            shown = show;
            bodies = bodies ?? GetComponentsInChildren<Renderer>(true);
            solids = solids ?? GetComponentsInChildren<Collider>(true);
            foreach (var r in bodies) if (r != null) r.enabled = show;
            foreach (var c in solids) if (c != null) c.enabled = show;
        }

        void OnDestroy()
        {
            TownsfolkPatches.Unregister(human);
            UltimaCreatures.Unregister(human);
            if (walker != null) home?.Crowd.Leave(walker);
            home?.Watch.Part(this);
        }

        public string GetHoverText()
        {
            // a hurt townsperson shows how hurt
            string name = Health < MaxHealth ? $"{label} ({Mathf.CeilToInt(Health)}/{MaxHealth:0})" : label;
            return Asleep ? $"{name} (asleep)" : Localization.instance.Localize($"{name}\n[<color=yellow><b>$KEY_Use</b></color>] Talk");
        }
        public string GetHoverName() => label;
        public float GetHoverOffset() => 0f;
    }
}
