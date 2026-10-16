using HarmonyLib;
using UnityEngine;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// Phase 1 prototype of the toroidal world (MASTER_PLAN §5.1). The terrain beyond each edge
    /// repeats the opposite edge for <see cref="Core.BritanniaShape.MarginTiles"/> tiles; halfway
    /// into that margin a traveller is moved one full map width back, onto identical ground.
    ///
    /// On foot: the local player is shifted with the engine's own teleport (no fade), which holds
    /// them until the destination area has loaded. By ship: the ship's owner moves the ship; every
    /// player aboard sees the ship jump and follows it by the same amount.
    /// </summary>
    public class WorldWrap : MonoBehaviour
    {
        Ship lastShip;
        Vector3 lastShipPos;

        void Update()
        {
            var shape = Britannia.Active;
            var player = Player.m_localPlayer;
            if (shape == null || player == null || player.IsTeleporting() || player.IsDead()) return;

            var ship = Ship.GetLocalShip();
            if (ship != null)
            {
                WrapShip(shape, ship);
                FollowShip(player, ship);
                return;
            }
            lastShip = null;
            var delta = WrapDelta(shape, player.transform.position);
            if (delta != Vector3.zero) Shift(player, delta);
        }

        /// <summary>Offset that brings a position back into the square, or zero while it is inside the inner half of the margin.</summary>
        static Vector3 WrapDelta(Core.BritanniaShape shape, Vector3 p)
        {
            shape.ToTile(p.x, p.z, out double u, out double v);
            float wrap = shape.WrapMetres, trigger = shape.MarginTiles / 2f;
            var d = Vector3.zero;
            if (u > Core.BritanniaShape.MapTiles + trigger) d.x = -wrap;
            else if (u < -trigger) d.x = wrap;
            if (v > Core.BritanniaShape.MapTiles + trigger) d.z = wrap;   // v grows south, z north
            else if (v < -trigger) d.z = -wrap;
            return d;
        }

        static void WrapShip(Core.BritanniaShape shape, Ship ship)
        {
            var nview = ship.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;
            var delta = WrapDelta(shape, ship.transform.position);
            if (delta == Vector3.zero) return;
            var body = ship.GetComponent<Rigidbody>();
            ship.transform.position += delta;
            if (body != null) body.position += delta;
            Plugin.Log.LogInfo($"Wrapped ship by {delta}");
        }

        void FollowShip(Player player, Ship ship)
        {
            var pos = ship.transform.position;
            if (ship == lastShip)
            {
                var jump = pos - lastShipPos;
                float half = Britannia.Active.WrapMetres / 2;
                if (Mathf.Abs(jump.x) > half || Mathf.Abs(jump.z) > half)
                    Shift(player, new Vector3(Mathf.Round(jump.x / Britannia.Active.WrapMetres) * Britannia.Active.WrapMetres, 0,
                                              Mathf.Round(jump.z / Britannia.Active.WrapMetres) * Britannia.Active.WrapMetres));
            }
            lastShip = ship;
            lastShipPos = pos;
        }

        /// <summary>
        /// Moves the player with Player.UpdateTeleport's machinery but past its 2 s fade: the
        /// player is placed at once and held until ZNetScene reports the area ready and a floor is
        /// found (non-distant: no loading screen).
        /// </summary>
        static void Shift(Player player, Vector3 delta)
        {
            var t = Traverse.Create(player);
            var from = player.transform.position;
            t.Field("m_teleporting").SetValue(true);
            t.Field("m_distantTeleport").SetValue(false);
            t.Field("m_teleportTimer").SetValue(2.01f);
            t.Field("m_teleportCooldown").SetValue(0f);
            t.Field("m_teleportFromPos").SetValue(from);
            t.Field("m_teleportFromRot").SetValue(player.transform.rotation);
            t.Field("m_teleportTargetPos").SetValue(from + delta);
            t.Field("m_teleportTargetRot").SetValue(player.transform.rotation);
            Plugin.Log.LogInfo($"Wrapped player from {from} by {delta}");
        }
    }
}
