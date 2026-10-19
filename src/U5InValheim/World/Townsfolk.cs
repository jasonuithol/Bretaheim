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
            HealthKey = "u5_npc_hp", DeadKey = "u5_npc_dead", TempKey = "u5_npc_temp";

        static Dictionary<int, PersonLook> looks;

        public static void Register() => PrefabManager.OnVanillaPrefabsAvailable += Create;

        static void Create()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Create;
            var prefab = PrefabManager.Instance.CreateClonedPrefab(PrefabName, "Player");
            if (prefab == null)
            {
                Plugin.Log.LogError("Townsfolk: Player prefab not found");
                return;
            }
            // everything that needs a real player or a Character goes
            foreach (var type in new[] { typeof(PlayerController), typeof(Talker), typeof(Skills), typeof(FootStep), typeof(Player), typeof(ZSyncAnimation), typeof(CharacterAnimEvent) })
                foreach (var c in prefab.GetComponentsInChildren(type, true))
                    Object.DestroyImmediate(c);
            var nview = prefab.GetComponent<ZNetView>();
            nview.m_persistent = true;
            var body = prefab.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.isKinematic = true;
                body.useGravity = false;
            }
            var vis = prefab.GetComponent<VisEquipment>();
            if (vis != null) vis.m_playerComponent = null;
            // on a layer the hover ray sees (the player layer is not one)
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
                var go = Object.Instantiate(prefab, b.Origin + new Vector3(x, y, lz), Quaternion.Euler(0, (npc.Slot * 47) % 360, 0));
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
        public static void SpawnReinforcements(Settlements.Built b, int edgeCell, int count)
        {
            var prefab = ZNetScene.instance.GetPrefab(PrefabName);
            var guard = Plugin.U5.NpcsOf(b.Location).FirstOrDefault(n => n.Slot > 0 && n.Exists && (n.IsGuard || n.CreatureType == 12));
            if (prefab == null || guard == null) return;
            var (x, y, z) = b.Plan.TilePos(0, edgeCell);
            for (int i = 0; i < count; i++)
            {
                var go = Object.Instantiate(prefab, b.Origin + new Vector3(x + (i % 3 - 1) * 0.8f, y, z + (i / 3) * 0.8f), Quaternion.identity);
                var zdo = go.GetComponent<ZNetView>().GetZDO();
                zdo.Set(LocKey, b.Location.Number);
                zdo.Set(SlotKey, guard.Slot);
                zdo.Set(FloorKey, 0);
                zdo.Set(TempKey, true);
                Dress(zdo, b.Location, guard);
                var npc = go.GetComponent<TownNpc>();
                npc.Init();
                npc.Duty = Duty.Fight;
            }
        }

        static readonly Vector3[] Skins = { new Vector3(1f, 1f, 1f), new Vector3(0.9f, 0.82f, 0.75f), new Vector3(0.75f, 0.62f, 0.5f), new Vector3(0.55f, 0.42f, 0.32f), new Vector3(0.38f, 0.28f, 0.2f) };
        static readonly Vector3[] Hairs = { new Vector3(0.15f, 0.1f, 0.07f), new Vector3(0.35f, 0.22f, 0.12f), new Vector3(0.6f, 0.45f, 0.25f), new Vector3(0.85f, 0.7f, 0.45f), new Vector3(0.5f, 0.2f, 0.1f), new Vector3(0.6f, 0.6f, 0.6f), new Vector3(0.05f, 0.05f, 0.05f) };

        /// <summary>Body, colouring and clothes (npcs.json), fixed per person.</summary>
        static void Dress(ZDO zdo, Data.Location loc, SettlementNpc npc)
        {
            var look = LookOf(npc.CreatureType);
            uint h = (uint)(loc.Number * 7919 + npc.Slot * 104729) * 2654435761u;
            int Pick(int count, int salt) => (int)(((h ^ (uint)salt * 0x9e3779b9u) >> 8) % (uint)count);
            int model = Pick(2, 1);
            zdo.Set(ZDOVars.s_modelIndex, model);
            zdo.Set(ZDOVars.s_skinColor, Skins[Pick(Skins.Length, 2)]);
            zdo.Set(ZDOVars.s_hairColor, Hairs[Pick(Hairs.Length, 3)]);
            zdo.Set(ZDOVars.s_hairItem, ("Hair" + (1 + Pick(20, 4))).GetStableHashCode());
            if (model == 0 && Pick(2, 5) == 0) zdo.Set(ZDOVars.s_beardItem, ("Beard" + (1 + Pick(20, 6))).GetStableHashCode());
            else
            {
                // (the body may have grown a random beard as it was made)
                zdo.Set(ZDOVars.s_beardItem, 0);
                zdo.Set(ZDOVars.s_noBeard, true);
            }
            // other bodies (children: Hildir) are not walkers: the same body, small, in Hildir's clothes
            var outfit = look.Base == "human" ? look.Outfit : new[] { "hildir:work-clothes" };
            foreach (var entry in outfit)
            {
                string item = entry;
                switch (entry)
                {
                    case "hildir:work-clothes":
                        item = (model == 1 ? "ArmorDress" : "ArmorTunic") + (1 + Pick(10, 7));
                        if (Pick(3, 8) == 0) Wear(zdo, "HelmetHat" + (1 + Pick(10, 9)));
                        break;
                    case "hildir:beaded-tunic-red": item = "ArmorTunic" + (1 + Pick(10, 10)); break;
                }
                Wear(zdo, item);
            }
        }

        static void Wear(ZDO zdo, string item)
        {
            var drop = ObjectDB.instance?.GetItemPrefab(item)?.GetComponent<ItemDrop>();
            if (drop == null) { Plugin.Log.LogWarning($"Townsfolk: no item {item}"); return; }
            int hash = item.GetStableHashCode();
            switch (drop.m_itemData.m_shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.Chest: zdo.Set(ZDOVars.s_chestItem, hash); break;
                case ItemDrop.ItemData.ItemType.Legs: zdo.Set(ZDOVars.s_legItem, hash); break;
                case ItemDrop.ItemData.ItemType.Helmet: zdo.Set(ZDOVars.s_helmetItem, hash); break;
                case ItemDrop.ItemData.ItemType.Shoulder: zdo.Set(ZDOVars.s_shoulderItem, hash); break;
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.Shield: zdo.Set(ZDOVars.s_leftItem, hash); break;
                default: zdo.Set(ZDOVars.s_rightItem, hash); break;
            }
        }
    }

    /// <summary>
    /// A townsperson in the world: the owner of its ZDO runs its day (<see cref="TownWalker"/>);
    /// every peer animates it from how it moves, and sits or lies it down (Valheim's
    /// attach_chair / attach_bed, as for a player) as the owner says.
    /// </summary>
    public class TownNpc : MonoBehaviour, Hoverable, Interactable
    {
        const float TurnSpeed = 360f;
        /// <summary>How much higher a smaller body sits, per unit of scale lost (a 0.7 child sank about 0.5 m).</summary>
        const float ChildSeatLift = 1.6f;
        static readonly int ForwardSpeed = ZSyncAnimation.GetHash("forward_speed"), OnGround = ZSyncAnimation.GetHash("onGround"), InWater = ZSyncAnimation.GetHash("inWater"),
            AttachChair = ZSyncAnimation.GetHash("attach_chair"), AttachBed = ZSyncAnimation.GetHash("attach_bed");

        ZNetView nview;
        Animator animator;
        Settlements.Built home;
        SettlementNpc npc;
        TownWalker walker;
        string label = "";
        bool ready;
        Vector3 lastPos;
        float speed;

        /// <summary>The hour of Britannia's day (Valheim's day as 24 Ultima hours).</summary>
        public static int Hour() => EnvMan.instance != null ? Mathf.FloorToInt(EnvMan.instance.GetDayFraction() * 24f) % 24 : 12;

        void Awake()
        {
            nview = GetComponent<ZNetView>();
            animator = GetComponentInChildren<Animator>();
            if (animator != null) animator.applyRootMotion = false;
            lastPos = transform.position;
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
            label = Townsfolk.Label(Plugin.U5, home.Location, npc);
            swingsSeen = zdo.GetInt(TownsfolkSpawner.SwingKey);
            ready = true;
        }

        public int Location => home?.Location.Number ?? 0;
        public int Slot => npc?.Slot ?? 0;
        /// <summary>Their conversation (.TLK), or null for shopkeepers, guards and the silent.</summary>
        public TlkScript Script => npc != null ? Plugin.U5.ScriptOf(home.Location, npc) : null;
        bool talking;
        bool Asleep => nview.GetZDO()?.GetInt(TownsfolkSpawner.PoseKey) == (int)Pose.Lie;

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
        public bool IsGuard => npc != null && (npc.IsGuard || npc.CreatureType == 12);
        public bool IsReinforcement => nview.GetZDO()?.GetBool(TownsfolkSpawner.TempKey) ?? false;
        public bool IsDead => (nview.GetZDO()?.GetInt(TownsfolkSpawner.DeadKey) ?? 0) > 0;
        public int Floor => walker?.Floor ?? nview.GetZDO()?.GetInt(TownsfolkSpawner.FloorKey) ?? 0;
        public int Cell => walker?.Cell ?? 0;
        /// <summary>Gone home to hide (and arrived).</summary>
        public bool Hidden => duty == Duty.Hide && walker != null && walker.Arrived;
        public float Health => nview.GetZDO()?.GetFloat(TownsfolkSpawner.HealthKey, TownAlarm.GuardHealth) ?? TownAlarm.GuardHealth;

        /// <summary>Hurrying to another townsperson (with news, or to fetch them).</summary>
        public void RunTo(TownNpc other)
        {
            if (walker == null || other.walker == null) return;
            walker.Chase(other.Floor, other.Cell);
        }

        /// <summary>Whole again (after a fight).</summary>
        public void Heal() => nview.GetZDO()?.Set(TownsfolkSpawner.HealthKey, TownAlarm.GuardHealth);

        /// <summary>A monster's blow: a guard falls at 0 and lies dead until the next morning (reinforcements are gone for good).</summary>
        public void Hurt(float damage)
        {
            var zdo = nview.GetZDO();
            if (zdo == null || IsDead) return;
            float hp = Health - damage;
            zdo.Set(TownsfolkSpawner.HealthKey, Mathf.Max(0, hp));
            if (hp > 0) return;
            Plugin.Log.LogInfo($"{home.Location.Name}: a guard has fallen");
            if (IsReinforcement) { ZNetScene.instance.Destroy(gameObject); return; }
            zdo.Set(TownsfolkSpawner.DeadKey, (EnvMan.instance?.GetDay() ?? 0) + 1);
            duty = Duty.None;
            home.Crowd.Leave(walker);
        }

        /// <summary>
        /// Talking (TALK.OVL talk_to_npc): townsfolk with a conversation open the panel; shopkeepers
        /// their shop while it is open, else "come see me when it's open"; the rest give no
        /// response. Nobody talks in their sleep.
        /// </summary>
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !ready || user != Player.m_localPlayer || Asleep) return false;
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

        // ── guards ──────────────────────────────────────────────────────────────────────────

        const float Reach = 2.2f, SwingEvery = 1.4f, GuardStrength = 1.5f;
        Character foe;
        float swingAt;
        int swingsSeen;

        /// <summary>
        /// On duty in the town's alarm: messengers and recruiting guards are moved by the watch
        /// (RunTo), the hiding run home and stay, fighters fight, reinforcements leave by the edge
        /// they came in at and are gone.
        /// </summary>
        void OnDuty(float dt)
        {
            var zdo = nview.GetZDO();
            int floor = walker.Floor;
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
                    var local = transform.position - home.Origin;
                    int edge = TownAlarm.EdgeNear(home.Plan, local.x, local.z);
                    if (edge < 0 || (walker.Arrived && walker.Cell == edge)) { ZNetScene.instance.Destroy(gameObject); return; }
                    walker.Chase(0, edge);
                    break;
            }
            walker.Tick(dt, Hour());
            if (walker.Floor != floor) zdo.Set(TownsfolkSpawner.FloorKey, walker.Floor);
            if (zdo.GetInt(TownsfolkSpawner.PoseKey) != (int)walker.Pose) zdo.Set(TownsfolkSpawner.PoseKey, (int)walker.Pose);
            transform.position = home.Origin + new Vector3(walker.X, walker.Y, walker.Z);
            if (duty != Duty.Fight || walker.Moving)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(new Vector3(walker.HeadingX, 0, walker.HeadingZ)), TurnSpeed * dt);
        }

        /// <summary>
        /// A fighting guard runs the nearest monster in town down along the streets (down the
        /// ladders if need be) and strikes it with their weapon, its own attack, half again as hard
        /// (Britannia's guards are formidable).
        /// </summary>
        void Guard()
        {
            foe = home.Watch.Target(transform.position);
            if (foe == null)
            {
                walker.Halt();
                return;
            }
            var to = foe.transform.position - transform.position;
            to.y = 0;
            if (to.magnitude > Reach)
            {
                var local = foe.transform.position - home.Origin;
                walker.Chase(0, home.Plan.CellAt(local.x, local.z));
                return;
            }
            walker.Chase(0, walker.Cell);
            walker.Halt();
            transform.rotation = Quaternion.LookRotation(to);
            if (Time.time >= swingAt)
            {
                swingAt = Time.time + SwingEvery;
                Strike(foe, to.normalized);
            }
        }

        void Strike(Character target, Vector3 dir)
        {
            var weapon = ObjectDB.instance?.GetItemPrefab(nview.GetZDO().GetInt(ZDOVars.s_rightItem))?.GetComponent<ItemDrop>()?.m_itemData.m_shared;
            var hit = new HitData();
            if (weapon != null)
            {
                hit.m_damage = weapon.m_damages;
                hit.m_damage.Modify(GuardStrength);
                hit.m_pushForce = weapon.m_attackForce;
            }
            else hit.m_damage.m_blunt = 20f;
            hit.m_point = target.GetCenterPoint();
            hit.m_dir = dir;
            target.Damage(hit);
            // every peer plays the thrust
            var zdo = nview.GetZDO();
            zdo.Set(TownsfolkSpawner.SwingKey, zdo.GetInt(TownsfolkSpawner.SwingKey) + 1);
        }

        /// <summary>The attack animation of what they hold (a guard's spear thrust).</summary>
        string SwingAnimation()
        {
            var weapon = ObjectDB.instance?.GetItemPrefab(nview.GetZDO().GetInt(ZDOVars.s_rightItem))?.GetComponent<ItemDrop>()?.m_itemData.m_shared;
            return weapon?.m_attack?.m_attackAnimation;
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

        void Update()
        {
            if (!ready || nview.GetZDO() == null) return;
            if (talking && TalkPanel.With != this && !ShopTrading.IsTrading(trader) && !ShopTrading.IsTrading(sellTrader)) talking = false;
            if (nview.IsOwner()) Live(Time.deltaTime);
            else if (walker != null)
            {
                // whoever owns it next picks up from where it stands
                home.Crowd.Leave(walker);
                walker = null;
            }
            Animate(Time.deltaTime);
        }

        void Live(float dt)
        {
            var zdo = nview.GetZDO();
            if (walker == null)
            {
                var local = transform.position - home.Origin;
                walker = new TownWalker(home.Plan, npc.Schedule, zdo.GetInt(TownsfolkSpawner.FloorKey), local.x, local.z, home.Location.Number * 32 + npc.Slot, home.Crowd);
            }
            home.Watch.Tick();
            if (IsDead)
            {
                // fallen: back at their post, whole, the next morning
                var env = EnvMan.instance;
                if (env != null && env.GetDay() >= zdo.GetInt(TownsfolkSpawner.DeadKey) && Hour() >= 6)
                {
                    zdo.Set(TownsfolkSpawner.DeadKey, 0);
                    zdo.Set(TownsfolkSpawner.HealthKey, TownAlarm.GuardHealth);
                    walker = null;
                }
                return;
            }
            if (IsReinforcement && duty == Duty.None) duty = Duty.Leave;
            if (duty != Duty.None) { OnDuty(dt); return; }
            if (talking)
            {
                // stopped, turned to the one they talk to (in a chair or bed they stay put)
                var me = Player.m_localPlayer;
                if (walker.Pose == Pose.Stand && me != null)
                {
                    var to = me.transform.position - transform.position;
                    to.y = 0;
                    if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), TurnSpeed * dt);
                }
                return;
            }
            int floor = walker.Floor;
            walker.Tick(dt, Hour());
            if (walker.Floor != floor) zdo.Set(TownsfolkSpawner.FloorKey, walker.Floor);
            if (zdo.GetInt(TownsfolkSpawner.PoseKey) != (int)walker.Pose) zdo.Set(TownsfolkSpawner.PoseKey, (int)walker.Pose);
            if (walker.Pose != Pose.Stand)
            {
                // on the chair or bed, turned as it is (as Valheim seats a player)
                // Valheim's sitting pose is made for a full-size body: a child would sink into the seat
                float lift = walker.Pose == Pose.Sit ? (1f - transform.localScale.y) * ChildSeatLift : 0f;
                transform.position = home.Origin + new Vector3(walker.RestX, walker.RestY + lift, walker.RestZ);
                transform.rotation = Quaternion.Euler(0, walker.RestYaw, 0);
                return;
            }
            transform.position = home.Origin + new Vector3(walker.X, walker.Y, walker.Z);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(new Vector3(walker.HeadingX, 0, walker.HeadingZ)), TurnSpeed * dt);
        }

        Renderer[] bodies;
        Collider[] solids;
        bool shown = true;

        /// <summary>A fallen guard is gone from sight (on every peer) until they come back.</summary>
        void ShowBody(bool show)
        {
            if (show == shown) return;
            shown = show;
            bodies = bodies ?? GetComponentsInChildren<Renderer>(true);
            solids = solids ?? GetComponentsInChildren<Collider>(true);
            foreach (var r in bodies) if (r != null) r.enabled = show;
            foreach (var c in solids) if (c != null) c.enabled = show;
        }

        void Animate(float dt)
        {
            ShowBody(!IsDead);
            if (animator == null || dt <= 0) return;
            var pos = transform.position;
            var moved = pos - lastPos;
            moved.y = 0;
            lastPos = pos;
            // teleports (ladders) are not walking
            float now = moved.magnitude / dt;
            int pose = nview.GetZDO().GetInt(TownsfolkSpawner.PoseKey);
            // getting into or out of a chair or bed is not walking
            speed = pose != 0 ? 0 : Mathf.Lerp(speed, now > 6f ? 0 : now, Mathf.Clamp01(dt * 8f));
            int swings = nview.GetZDO().GetInt(TownsfolkSpawner.SwingKey);
            if (swings != swingsSeen)
            {
                swingsSeen = swings;
                var anim = SwingAnimation();
                if (!string.IsNullOrEmpty(anim)) animator.SetTrigger(anim);
            }
            animator.SetBool(AttachChair, pose == (int)Pose.Sit);
            animator.SetBool(AttachBed, pose == (int)Pose.Lie);
            animator.SetFloat(ForwardSpeed, speed);
            animator.SetBool(OnGround, true);
            animator.SetBool(InWater, false);
        }

        void OnDestroy()
        {
            if (walker != null) home?.Crowd.Leave(walker);
            home?.Watch.Part(this);
        }

        public string GetHoverText()
        {
            // a hurt guard shows how hurt
            string name = IsGuard && Health < TownAlarm.GuardHealth ? $"{label} ({Mathf.CeilToInt(Health)}/{TownAlarm.GuardHealth:0})" : label;
            return Asleep ? $"{name} (asleep)" : Localization.instance.Localize($"{name}\n[<color=yellow><b>$KEY_Use</b></color>] Talk");
        }
        public string GetHoverName() => label;
        public float GetHoverOffset() => 0f;
    }
}
