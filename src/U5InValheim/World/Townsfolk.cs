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
        public const string LocKey = "u5_npc_loc", SlotKey = "u5_npc_slot", FloorKey = "u5_npc_z";

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
    /// every peer animates it from how it moves.
    /// </summary>
    public class TownNpc : MonoBehaviour, Hoverable
    {
        const float TurnSpeed = 360f;
        static readonly int ForwardSpeed = ZSyncAnimation.GetHash("forward_speed"), OnGround = ZSyncAnimation.GetHash("onGround"), InWater = ZSyncAnimation.GetHash("inWater");

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
            npc = Plugin.U5.NpcsOf(home.Location)[slot];
            label = Townsfolk.Label(Plugin.U5, home.Location, npc);
            ready = true;
        }

        void Update()
        {
            if (!ready || nview.GetZDO() == null) return;
            if (nview.IsOwner()) Live(Time.deltaTime);
            else walker = null; // whoever owns it next picks up from where it stands
            Animate(Time.deltaTime);
        }

        void Live(float dt)
        {
            var zdo = nview.GetZDO();
            if (walker == null)
            {
                var local = transform.position - home.Origin;
                walker = new TownWalker(home.Plan, npc.Schedule, zdo.GetInt(TownsfolkSpawner.FloorKey), local.x, local.z, home.Location.Number * 32 + npc.Slot);
            }
            int floor = walker.Floor;
            walker.Tick(dt, Hour());
            if (walker.Floor != floor) zdo.Set(TownsfolkSpawner.FloorKey, walker.Floor);
            transform.position = home.Origin + new Vector3(walker.X, walker.Y, walker.Z);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(new Vector3(walker.HeadingX, 0, walker.HeadingZ)), TurnSpeed * dt);
        }

        void Animate(float dt)
        {
            if (animator == null || dt <= 0) return;
            var pos = transform.position;
            var moved = pos - lastPos;
            moved.y = 0;
            lastPos = pos;
            // teleports (ladders) are not walking
            float now = moved.magnitude / dt;
            speed = Mathf.Lerp(speed, now > 6f ? 0 : now, Mathf.Clamp01(dt * 8f));
            animator.SetFloat(ForwardSpeed, speed);
            animator.SetBool(OnGround, true);
            animator.SetBool(InWater, false);
        }

        public string GetHoverText() => label;
        public string GetHoverName() => label;
        public float GetHoverOffset() => 0f;
    }
}
