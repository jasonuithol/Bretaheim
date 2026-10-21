using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Bretaheim.Data;
using Bretaheim.WorldGen.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Bretaheim.WorldGen
{
    /// <summary>
    /// Britannia's vessels (MASTER_PLAN §5.3, the transport mapping): a frigate is Valheim's
    /// longship, a skiff the Karve, both sailed with Valheim's own controls (the rudder: no
    /// yelling for sails). A shipwright delivers what was bought to the town's dock
    /// (<see cref="Docks"/>; SHOPPES2 shipwright_deliver, MAINOUT run_overworld) with its buyer's
    /// name on it; a frigate carries skiffs (two when new, one more for each bought with it),
    /// lowered beside it at the rudder (Shift+E), and its crew take to one when it sinks
    /// ("Abandon ship!", vehicle_damage). The HMS Cape plans, carried aboard, make a ship twice
    /// as quick under sail (read_overworld_command: a minute a step instead of two).
    /// </summary>
    public static class Vessels
    {
        public const string OwnerKey = "u5_vessel_owner", OwnerNameKey = "u5_vessel_owner_name", SkiffsKey = "u5_skiffs", PirateKey = "u5_pirate";
        public const string FrigatePrefab = "VikingShip", SkiffPrefab = "Karve";
        /// <summary>The sail's force with the HMS Cape plans aboard.</summary>
        public const float CapeSail = 2f;
        /// <summary>How far beside a frigate a lowered skiff is put.</summary>
        public const float SkiffOffset = 7f;

        // the vessel bought on this visit to a shipwright (g_bought_vessel), until the buyer leaves the town
        static int bought, boughtAt;
        static ZDOID boughtFrigate = ZDOID.None;

        public static void Install(Harmony harmony)
        {
            Patch(harmony, typeof(Ship), "Awake", postfix: nameof(ShipAwake_Postfix));
            Patch(harmony, typeof(Ship), "GetSailForce", postfix: nameof(GetSailForce_Postfix));
            Patch(harmony, typeof(Ship), "CustomFixedUpdate", postfix: nameof(CustomFixedUpdate_Postfix));
            Patch(harmony, typeof(ShipControlls), "Interact", prefix: nameof(Interact_Prefix));
            Patch(harmony, typeof(ShipControlls), "GetHoverText", postfix: nameof(GetHoverText_Postfix));
        }

        static void Patch(Harmony h, Type t, string m, string prefix = null, string postfix = null)
        {
            var method = AccessTools.Method(t, m);
            if (method == null) { Plugin.Log.LogWarning($"Vessels: {t.Name}.{m} not found"); return; }
            h.Patch(method, prefix: prefix != null ? new HarmonyMethod(typeof(Vessels), prefix) : null,
                postfix: postfix != null ? new HarmonyMethod(typeof(Vessels), postfix) : null);
        }

        // ── buying ──────────────────────────────────────────────────────────────────────────

        /// <summary>The bought-vessel byte for a visit to this settlement's shipwright (0 on another settlement's, or after leaving).</summary>
        public static int BoughtHere(int location) => boughtAt == location ? bought : 0;

        public static void Sold(int location, VesselSale sale)
        {
            if (boughtAt != location) bought = 0;
            boughtAt = location;
            bought = Shops.After(bought, sale);
        }

        /// <summary>A sale that could not be delivered after all: the visit's vessel as it was.</summary>
        public static void Unsold(int location, int before)
        {
            boughtAt = location;
            bought = before;
        }

        /// <summary>The visit is over once the buyer is away from the town (the original creates the vessel on leaving it).</summary>
        public static void Forget(Vector3 at)
        {
            if (bought == 0) return;
            var b = Settlements.Get(boughtAt);
            if (b != null && Mathf.Max(Mathf.Abs(at.x - b.Site.CentreX), Mathf.Abs(at.z - b.Site.CentreZ)) < b.Site.Half + b.Site.Blend + 60f) return;
            bought = 0;
            boughtFrigate = ZDOID.None;
        }

        /// <summary>
        /// A frigate (with its two skiffs) or a skiff for the town's dock, the buyer's: put in the
        /// water when the dock's zones are loaded (now, or as the buyer comes near: the original
        /// makes it on leaving the town), kept with the character until then.
        /// </summary>
        public static bool Deliver(Player buyer, ShopInfo shop, bool frigate, out string why)
        {
            why = "";
            var b = Settlements.Get(shop.Location.Number);
            var shape = Britannia.Active;
            if (b == null || shape == null || ZNetScene.instance == null) { why = "There is no dock here."; return false; }
            var (dx, dy) = Plugin.U5.Shops.Dock(shop);
            if (!Docks.Berth(shape, dx, dy, shop.Location.X, shop.Location.Y, b.Site.CentreX, b.Site.CentreZ, b.Site.Half + b.Site.Blend, out float x, out float z))
            {
                why = "The dock is silted up: no water deep enough for a ship.";
                Plugin.Log.LogWarning($"Vessels: no berth near {shop.Location.Name}'s dock ({dx},{dy})");
                return false;
            }
            var due = Due(buyer);
            due.Add(new DueVessel
            {
                Location = shop.Location.Number, Frigate = frigate, Skiffs = frigate ? Shops.SkiffsAboard(Shops.BoughtFrigate) : 0,
                X = x, Z = z, Yaw = Mathf.Atan2(x - b.Site.CentreX, z - b.Site.CentreZ) * Mathf.Rad2Deg,
            });
            SetDue(buyer, due);
            Plugin.Log.LogInfo($"Vessels: {buyer.GetPlayerName()} bought a {(frigate ? "frigate" : "skiff")} at {shop.Location.Name}, for the dock ({dx},{dy}) at ({x:0}, {z:0})");
            DeliverDue(buyer);
            return true;
        }

        /// <summary>A vessel bought and not yet in the water.</summary>
        public class DueVessel
        {
            public int Location, Skiffs;
            public bool Frigate;
            public float X, Z, Yaw;
        }

        const string DueKey = "u5_vessels_due";
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        public static List<DueVessel> Due(Player p)
        {
            var r = new List<DueVessel>();
            if (p == null || !p.m_customData.TryGetValue(DueKey, out var text)) return r;
            foreach (var entry in text.Split(';'))
            {
                var f = entry.Split('|');
                if (f.Length != 6) continue;
                r.Add(new DueVessel
                {
                    Location = int.Parse(f[0], Inv), Frigate = f[1] == "1", Skiffs = int.Parse(f[2], Inv),
                    X = float.Parse(f[3], Inv), Z = float.Parse(f[4], Inv), Yaw = float.Parse(f[5], Inv),
                });
            }
            return r;
        }

        static void SetDue(Player p, List<DueVessel> due)
        {
            if (due.Count == 0) { p.m_customData.Remove(DueKey); return; }
            p.m_customData[DueKey] = string.Join(";", due.Select(d => string.Join("|", d.Location.ToString(Inv), d.Frigate ? "1" : "0", d.Skiffs.ToString(Inv),
                d.X.ToString("0.0", Inv), d.Z.ToString("0.0", Inv), d.Yaw.ToString("0.0", Inv))));
        }

        /// <summary>Whether the water round a point is there to float a ship (its zone and the ones a ship's length away loaded).</summary>
        public static bool WaterReady(Vector3 at)
        {
            if (ZoneSystem.instance == null) return false;
            at.y = ZoneSystem.instance.m_waterLevel - 1f;
            // (GetLiquidLevel also sets up the water layer mask that GetWaterLevel, which ships use,
            // leaves unset on a peer where nothing Floating has woken yet: a headless server's ships sank)
            foreach (var (ox, oz) in new[] { (0f, 0f), (16f, 0f), (-16f, 0f), (0f, 16f), (0f, -16f) })
            {
                var p = at + new Vector3(ox, 0, oz);
                if (!ZoneSystem.instance.IsZoneLoaded(p) || Floating.GetLiquidLevel(p, 1f, LiquidType.Water) < -1000f) return false;
            }
            return true;
        }

        /// <summary>The vessels due to this player whose docks are loaded, put in the water.</summary>
        public static void DeliverDue(Player p)
        {
            var due = Due(p);
            if (due.Count == 0) return;
            var left = new List<DueVessel>();
            foreach (var d in due)
            {
                var at = new Vector3(d.X, 0, d.Z);
                if (!WaterReady(at)) { left.Add(d); continue; }
                var go = Spawn(d.Frigate, at, d.Yaw, p.GetPlayerID(), p.GetPlayerName(), d.Skiffs);
                if (go == null) { left.Add(d); continue; }
                if (d.Frigate && boughtAt == d.Location && bought >= 0x80) boughtFrigate = go.GetComponent<ZNetView>().GetZDO().m_uid;
                p.Message(MessageHud.MessageType.TopLeft, $"Thy {(d.Frigate ? "frigate" : "skiff")} lies at the dock.");
                Plugin.Log.LogInfo($"Vessels: {p.GetPlayerName()}'s {(d.Frigate ? "frigate" : "skiff")} put in the water at ({d.X:0}, {d.Z:0})");
            }
            SetDue(p, left);
        }

        /// <summary>A vessel afloat at a point (y is the sea's), facing <paramref name="yaw"/>, belonging to someone (0: nobody).</summary>
        public static GameObject Spawn(bool frigate, Vector3 at, float yaw, long owner, string ownerName, int skiffs)
        {
            var prefab = ZNetScene.instance?.GetPrefab(frigate ? FrigatePrefab : SkiffPrefab);
            if (prefab == null) return null;
            at.y = (ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : Britannia.SeaLevel) - 0.2f;
            Floating.GetLiquidLevel(at); // (sets up the water mask ships float by: see WaterReady)
            var go = Object.Instantiate(prefab, at, Quaternion.Euler(0, yaw, 0));
            var zdo = go.GetComponent<ZNetView>()?.GetZDO();
            if (zdo == null) return go;
            zdo.Set(OwnerKey, owner);
            zdo.Set(OwnerNameKey, ownerName ?? "");
            if (frigate) zdo.Set(SkiffsKey, skiffs);
            go.GetComponent<VesselState>()?.Refresh();
            return go;
        }

        /// <summary>A skiff bought after a frigate on the same visit: "It will be placed onboard thy frigate!"</summary>
        public static void AddSkiffAboard(Player buyer, int location)
        {
            if (boughtAt != location) return;
            // not in the water yet: one more aboard when it is
            var due = Due(buyer);
            var waiting = due.LastOrDefault(d => d.Frigate && d.Location == location);
            if (waiting != null) { waiting.Skiffs++; SetDue(buyer, due); return; }
            if (boughtFrigate == ZDOID.None) return;
            var ship = ZNetScene.instance?.FindInstance(boughtFrigate);
            ship?.GetComponent<ZNetView>()?.InvokeRPC("u5_AddSkiff", 1);
        }

        // ── aboard ──────────────────────────────────────────────────────────────────────────

        static ZDO Zdo(Component c) => c != null ? c.GetComponent<ZNetView>()?.GetZDO() : null;
        public static bool IsPirate(Ship ship) => Zdo(ship)?.GetBool(PirateKey) ?? false;
        public static int Skiffs(Ship ship) => Zdo(ship)?.GetInt(SkiffsKey) ?? 0;
        public static bool IsFrigate(Ship ship) => ship != null && Utils.GetPrefabName(ship.gameObject.name) == FrigatePrefab;

        static void ShipAwake_Postfix(Ship __instance)
        {
            if (__instance.GetComponent<VesselState>() == null) __instance.gameObject.AddComponent<VesselState>();
        }

        /// <summary>The HMS Cape plans carried by someone aboard (on the ship's owner, who sails it): twice the sail.</summary>
        static void GetSailForce_Postfix(Ship __instance, ref Vector3 __result)
        {
            var me = Player.m_localPlayer;
            if (me != null && Britannia.Active != null && __instance.IsPlayerInBoat(me) && HasCapePlans(me)) __result *= CapeSail;
        }

        public static bool HasCapePlans(Player p)
        {
            var prefab = UltimaItems.Map?.Special("HMS Cape Plans")?.Prefab;
            if (prefab == null || p == null) return false;
            foreach (var item in p.GetInventory().GetAllItems())
                if (item.m_dropPrefab != null && item.m_dropPrefab.name == prefab) return true;
            return false;
        }

        /// <summary>A pirate ship sails itself (its owner moves it): <see cref="PirateShip"/>.</summary>
        static void CustomFixedUpdate_Postfix(Ship __instance, float fixedDeltaTime)
        {
            var pirate = __instance.GetComponent<PirateShip>();
            if (pirate != null) pirate.Sail(fixedDeltaTime);
        }

        /// <summary>Shift+E at a frigate's rudder lowers a skiff; a pirate ship's rudder is the pirates'.</summary>
        static bool Interact_Prefix(ShipControlls __instance, Humanoid character, bool repeat, bool alt, ref bool __result)
        {
            if (repeat || !(character is Player p) || __instance.m_ship == null) return true;
            if (IsPirate(__instance.m_ship))
            {
                p.Message(MessageHud.MessageType.Center, "The pirates hold the helm!");
                __result = false;
                return false;
            }
            if (!alt || Skiffs(__instance.m_ship) <= 0) return true;
            __instance.m_ship.GetComponent<ZNetView>().InvokeRPC("u5_LowerSkiff", p.GetPlayerID(), p.GetPlayerName());
            p.Message(MessageHud.MessageType.Center, "A skiff is lowered.");
            __result = true;
            return false;
        }

        static void GetHoverText_Postfix(ShipControlls __instance, ref string __result)
        {
            var ship = __instance.m_ship;
            var zdo = Zdo(ship);
            if (zdo == null || Britannia.Active == null) return;
            if (zdo.GetBool(PirateKey)) { __result = "Pirate ship"; return; }
            string kind = IsFrigate(ship) ? "frigate" : "skiff";
            string owner = zdo.GetString(OwnerNameKey);
            if (owner.Length > 0) __result += $"\n({owner}'s {kind})";
            int skiffs = zdo.GetInt(SkiffsKey);
            if (skiffs > 0) __result += Localization.instance.Localize($"\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] Lower a skiff ({skiffs} aboard)");
            if (HasCapePlans(Player.m_localPlayer)) __result += "\n(The HMS Cape's plans: she flies before the wind)";
        }
    }

    /// <summary>A vessel's skiffs: lowered at the owner's (RPC), added by a shipwright, and taken to when it sinks.</summary>
    public class VesselState : MonoBehaviour
    {
        ZNetView nview;
        Ship ship;

        void Awake()
        {
            nview = GetComponent<ZNetView>();
            ship = GetComponent<Ship>();
            if (nview == null || nview.GetZDO() == null) return;
            nview.Register<long, string>("u5_LowerSkiff", RPC_LowerSkiff);
            nview.Register<int>("u5_AddSkiff", RPC_AddSkiff);
            var wnt = GetComponent<WearNTear>();
            if (wnt != null) wnt.m_onDestroyed += OnSunk;
            Refresh();
        }

        /// <summary>A pirate-held ship sails itself.</summary>
        public void Refresh()
        {
            if (nview == null || nview.GetZDO() == null) return;
            bool pirate = nview.GetZDO().GetBool(Vessels.PirateKey);
            var p = GetComponent<PirateShip>();
            if (pirate && p == null) gameObject.AddComponent<PirateShip>();
            if (!pirate && p != null) Destroy(p);
        }

        void RPC_AddSkiff(long sender, int n)
        {
            if (!nview.IsOwner()) return;
            nview.GetZDO().Set(Vessels.SkiffsKey, nview.GetZDO().GetInt(Vessels.SkiffsKey) + n);
        }

        void RPC_LowerSkiff(long sender, long player, string name)
        {
            if (!nview.IsOwner()) return;
            int n = nview.GetZDO().GetInt(Vessels.SkiffsKey);
            if (n <= 0) return;
            nview.GetZDO().Set(Vessels.SkiffsKey, n - 1);
            var side = transform.position + transform.right * Vessels.SkiffOffset;
            Vessels.Spawn(false, side, transform.eulerAngles.y, player, name, 0);
            Plugin.Log.LogInfo($"Vessels: {name} lowered a skiff from a frigate ({n - 1} left aboard)");
        }

        /// <summary>vehicle_damage: "Ship sunk!" with a skiff aboard is "Abandon ship!" into it.</summary>
        void OnSunk()
        {
            if (nview == null || !nview.IsValid() || !nview.IsOwner() || ship == null) return;
            int n = nview.GetZDO().GetInt(Vessels.SkiffsKey);
            if (n <= 0 || nview.GetZDO().GetBool(Vessels.PirateKey)) return;
            Vessels.Spawn(false, transform.position, transform.eulerAngles.y, nview.GetZDO().GetLong(Vessels.OwnerKey), nview.GetZDO().GetString(Vessels.OwnerNameKey), 0);
            MessageHud.instance?.MessageAll(MessageHud.MessageType.Center, "Ship sunk! Abandon ship!");
            Plugin.Log.LogInfo("Vessels: a frigate sank; its crew took to a skiff");
        }
    }
}
