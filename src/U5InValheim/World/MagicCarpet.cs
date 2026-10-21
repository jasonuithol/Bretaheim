using System.Linq;
using Jotunn.Entities;
using Jotunn.Managers;
using U5InValheim.Data;
using UnityEngine;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// The magic carpet (Use item, Board; use_item_command 0x10): used from the inventory it is
    /// laid down under the player, who boards it ("Boarded!"), on foot only ("X-it ship first!",
    /// "Only on foot!"), not underground or on a mountain ("Not here!"). The rider sits on it and
    /// steers it as a mount (Valheim's doodad controls, as the Lox's saddle): forward, back and
    /// sideways along where they look, run to go faster; it glides half a metre over land and
    /// water alike, never onto the mountains (as the original's carpet), and stops at walls. E
    /// leaves it where it is (X-it); anyone may board it then, and Shift+E rolls it up into the
    /// inventory again. Moved by its rider's machine (it takes the carpet's ZDO), synced to the rest.
    /// </summary>
    public class MagicCarpet : MonoBehaviour, Interactable, Hoverable, IDoodadController
    {
        public const string PrefabName = "u5_magic_carpet";
        const string RiderKey = "u5_rider";
        const float Hover = 0.5f, WaterHover = 0.8f, Walk = 7f, Run = 12f, Climb = 6f, TurnRate = 120f;
        const byte Mountains = 0x0c;

        ZNetView nview;
        Transform seat;
        Vector3 moveDir, lookDir;
        bool run;

        public static void Register() => PrefabManager.OnVanillaPrefabsAvailable += Create;

        static void Create()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Create;
            var prefab = PrefabManager.Instance.CreateClonedPrefab(PrefabName, "jute_carpet");
            if (prefab == null) { Plugin.Log.LogError("Magic carpet: no jute_carpet to make it of"); return; }
            // a vehicle, not a building piece: no hammer, no wear, no support
            foreach (var c in prefab.GetComponents<WearNTear>()) DestroyImmediate(c);
            foreach (var c in prefab.GetComponents<Piece>()) DestroyImmediate(c);
            var sync = prefab.GetComponent<ZSyncTransform>() ?? prefab.AddComponent<ZSyncTransform>();
            sync.m_syncPosition = true;
            sync.m_syncRotation = true;
            var seat = new GameObject("seat");
            seat.transform.SetParent(prefab.transform, false);
            seat.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            prefab.AddComponent<MagicCarpet>();
            PrefabManager.Instance.AddPrefab(new CustomPrefab(prefab, false));
        }

        void Awake()
        {
            nview = GetComponent<ZNetView>();
            seat = transform.Find("seat") ?? transform;
        }

        static string ItemPrefab => UltimaItems.Map.Special("Magic Carpet")?.Prefab;
        static string Say(int dg) => Plugin.U5.Ovl.CStr(dg).Replace("\n", " ").Trim();

        /// <summary>Use item: the carpet laid under the player, who boards it; the words to show.</summary>
        public static string Lay(Player p, Inventory inv, ItemDrop.ItemData item)
        {
            var at = p.transform.position;
            if (p.IsAttachedToShip() || p.GetControlledShip() != null) return Say(ItemUse.SXitShipFirst);
            if (p.IsAttached() || p.IsRiding()) return Say(ItemUse.SOnlyOnFoot);
            var shape = Britannia.Active;
            if (shape == null || UseItems.Underground(at) || shape.TileAt(at.x, at.z) == Mountains) return Say(ItemUse.SCarpetNotHere);
            var prefab = ZNetScene.instance?.GetPrefab(PrefabName);
            if (prefab == null) return Say(ItemUse.SCarpetNotHere);
            inv.RemoveItem(item, 1);
            InventoryGui.instance?.Hide();
            var go = Instantiate(prefab, new Vector3(at.x, Height(at), at.z), Quaternion.Euler(0, p.transform.eulerAngles.y, 0));
            go.GetComponent<MagicCarpet>().Board(p);
            Plugin.Log.LogInfo($"{p.GetPlayerName()} lays a magic carpet at {at}");
            return Say(ItemUse.SBoarded);
        }

        /// <summary>The carpet's height over a point: half a metre over the ground, or over the sea.</summary>
        static float Height(Vector3 at)
        {
            float ground = ZoneSystem.instance != null ? ZoneSystem.instance.GetGroundHeight(at) : at.y;
            float water = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : Britannia.SeaLevel;
            return ground > water ? ground + Hover : water + WaterHover;
        }

        long Rider => nview != null && nview.GetZDO() != null ? nview.GetZDO().GetLong(RiderKey) : 0L;

        bool Ridden => Rider != 0 && Player.GetAllPlayers().Any(p => p != null && p.GetPlayerID() == Rider && !p.IsDead());

        bool MineToRide(Player p) => p != null && p.GetDoodadController() as MagicCarpet == this;

        void Board(Player p)
        {
            if (p.IsAttached()) return;
            nview.ClaimOwnership();
            nview.GetZDO().Set(RiderKey, p.GetPlayerID());
            p.AttachStart(seat, gameObject, false, false, false, "attach_chair", new Vector3(0f, 0.6f, 1.6f));
            p.StartDoodadControl(this);
            moveDir = Vector3.zero;
            lookDir = transform.forward;
        }

        void RollUp(Player p)
        {
            var item = ObjectDB.instance?.GetItemPrefab(ItemPrefab);
            if (item == null || !p.GetInventory().CanAddItem(item, 1)) { p.Message(MessageHud.MessageType.Center, "Thou canst not carry any more!"); return; }
            p.GetInventory().AddItem(item, 1);
            nview.ClaimOwnership();
            ZNetScene.instance.Destroy(gameObject);
            p.Message(MessageHud.MessageType.Center, "Thou dost roll up the magic carpet.");
        }

        // ── Interactable, Hoverable ─────────────────────────────────────────────────────────

        public bool Interact(Humanoid user, bool repeat, bool alt)
        {
            if (repeat || !(user is Player p) || p != Player.m_localPlayer || nview == null || !nview.IsValid()) return false;
            if (MineToRide(p)) { p.StopDoodadControl(); return true; }
            if (Ridden) { p.Message(MessageHud.MessageType.Center, "Someone rides it."); return false; }
            if (alt) { RollUp(p); return true; }
            if (p.IsAttached() || p.IsRiding()) { p.Message(MessageHud.MessageType.Center, Say(ItemUse.SOnlyOnFoot)); return false; }
            Board(p);
            p.Message(MessageHud.MessageType.Center, Say(ItemUse.SBoarded));
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public string GetHoverName() => "Magic Carpet";
        public float GetHoverOffset() => 0.3f;

        public string GetHoverText()
        {
            var me = Player.m_localPlayer;
            if (MineToRide(me)) return Localization.instance.Localize("Magic Carpet\n[<color=yellow><b>$KEY_Use</b></color>] X-it");
            if (Ridden) return "Magic Carpet";
            return Localization.instance.Localize("Magic Carpet\n[<color=yellow><b>$KEY_Use</b></color>] Board\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] Roll it up");
        }

        // ── IDoodadController ───────────────────────────────────────────────────────────────

        public void OnUseStop(Player player)
        {
            player.AttachStop();
            if (nview != null && nview.IsValid() && nview.IsOwner()) nview.GetZDO().Set(RiderKey, 0L);
            moveDir = Vector3.zero;
        }

        public void ApplyControlls(Vector3 moveDir, Vector3 lookDir, bool run, bool autoRun, bool block)
        {
            this.moveDir = autoRun ? new Vector3(moveDir.x, 0, 1) : moveDir;
            this.lookDir = lookDir;
            this.run = run;
        }

        public Component GetControlledComponent() => this;
        public Vector3 GetPosition() => transform.position;
        public bool IsValid() => this != null && nview != null && nview.IsValid();

        // ── flying ──────────────────────────────────────────────────────────────────────────

        void Update()
        {
            var me = Player.m_localPlayer;
            if (me == null || nview == null || !nview.IsValid() || !nview.IsOwner()) return;
            if (Rider != me.GetPlayerID()) return;
            if (!MineToRide(me) || me.IsDead())
            {
                // left by dying or by being drawn away: the carpet is free again
                nview.GetZDO().Set(RiderKey, 0L);
                return;
            }
            float dt = Time.deltaTime;
            var heading = Vector3.ProjectOnPlane(lookDir, Vector3.up);
            if (heading.sqrMagnitude < 0.01f) heading = transform.forward;
            heading.Normalize();
            var right = Vector3.Cross(Vector3.up, heading);
            var velocity = heading * Mathf.Clamp(moveDir.z, -1f, 1f) + right * Mathf.Clamp(moveDir.x, -1f, 1f);
            if (velocity.sqrMagnitude > 1f) velocity.Normalize();
            velocity *= run ? Run : Walk;
            var pos = transform.position;
            if (velocity.sqrMagnitude > 0.01f)
            {
                var next = pos + velocity * dt;
                if (!Blocked(pos, velocity, me)) pos = next;
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(heading), TurnRate * dt);
            }
            pos.y = Mathf.MoveTowards(pos.y, Height(pos), Climb * dt);
            transform.position = pos;
        }

        /// <summary>A wall, a rock or a mountain tile ahead.</summary>
        bool Blocked(Vector3 pos, Vector3 velocity, Player rider)
        {
            var dir = velocity.normalized;
            var ahead = pos + dir * 1.5f;
            if (Britannia.Active != null && Britannia.Active.TileAt(ahead.x, ahead.z) == Mountains) return true;
            float dist = velocity.magnitude * Time.deltaTime + 1.2f;
            foreach (var hit in Physics.SphereCastAll(pos + Vector3.up * 0.9f, 0.5f, dir, dist, Mask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider == null || hit.collider.transform.IsChildOf(transform) || hit.collider.transform.IsChildOf(rider.transform)) continue;
                if (hit.collider.GetComponentInParent<Character>() != null) continue;
                return true;
            }
            return false;
        }

        static int mask;
        static int Mask => mask != 0 ? mask : (mask = LayerMask.GetMask("Default", "static_solid", "piece", "Default_small"));
    }
}
