using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// The toroidal world (MASTER_PLAN §5.1). The terrain beyond each edge repeats the opposite
    /// edge for <see cref="Core.BritanniaShape.MarginTiles"/> tiles. Crossing it:
    ///
    /// 1. From <see cref="PrewarmTiles"/> past the edge, the destination's terrain/water zones are
    ///    created and kept alive (ZoneSystem only builds zones around the reference point, so
    ///    without this the traveller would arrive over nothing and fall).
    /// 2. From <see cref="TriggerTiles"/>, once the 3×3 zones at the destination are loaded, the
    ///    traveller is moved one map width in a single frame: on foot the player; at sea the ship's
    ///    owner moves the ship together with themselves, and every other player aboard follows when
    ///    they see their ship jump (ZSyncTransform snaps jumps over 5 m).
    /// 3. If the destination is still not ready <see cref="ForceTiles"/> past the edge, the move
    ///    happens anyway and the ship is held still (kinematic) until its new zones exist.
    /// </summary>
    public class WorldWrap : MonoBehaviour
    {
        const float PrewarmTiles = 1f, TriggerTiles = 8f, ForceTiles = 13f;

        static MethodInfo createLocalZones;
        Ship lastShip;
        Vector3 lastShipPos;
        Rigidbody heldShip;
        Vector3 heldAt;

        void Update()
        {
            var shape = Britannia.Active;
            var player = Player.m_localPlayer;
            ReleaseHeldShip();
            if (shape == null || player == null || player.IsDead() || ZoneSystem.instance == null) return;

            var ship = Ship.GetLocalShip();
            if (ship != null && FollowShipJump(player, ship)) return;

            bool owner = ship != null && IsOwner(ship);
            var mover = ship != null ? ship.transform.position : player.transform.position;
            var delta = WrapDelta(shape, mover, PrewarmTiles, out float beyond);
            if (delta == Vector3.zero) return;

            var dest = mover + delta;
            Prewarm(dest);
            if (beyond < TriggerTiles) return;
            bool ready = DestinationReady(dest);
            if (!ready && beyond < ForceTiles) return;
            if (ship != null && !owner) return; // passengers wait for the ship to jump

            if (ship != null)
            {
                MoveBody(ship.transform, ship.GetComponent<Rigidbody>(), delta);
                if (!ready) Hold(ship.GetComponent<Rigidbody>());
                lastShip = ship;
                lastShipPos = ship.transform.position;
            }
            MoveBody(player.transform, player.GetComponent<Rigidbody>(), delta);
            Plugin.Log.LogInfo($"Wrapped {(ship != null ? "ship and player" : "player")} by {delta} (destination {(ready ? "ready" : "not ready, holding")})");
        }

        /// <summary>
        /// Offset that brings a position back into the square once it is <paramref name="from"/>
        /// tiles past an edge; <paramref name="beyond"/> is how far past the edge it is.
        /// </summary>
        static Vector3 WrapDelta(Core.BritanniaShape shape, Vector3 p, float from, out float beyond)
        {
            shape.ToTile(p.x, p.z, out double u, out double v);
            float wrap = shape.WrapMetres;
            int n = Core.BritanniaShape.MapTiles;
            var d = Vector3.zero;
            float far = 0;
            double overU = u > n ? u - n : u < 0 ? -u : 0;
            double overV = v > n ? v - n : v < 0 ? -v : 0;
            if (overU >= from) { d.x = u > n ? -wrap : wrap; far = (float)overU; }
            if (overV >= from) { d.z = v > n ? wrap : -wrap; far = Mathf.Max(far, (float)overV); } // v grows south, z north
            beyond = far;
            return d;
        }

        /// <summary>A passenger whose ship was just moved one map width follows it.</summary>
        bool FollowShipJump(Player player, Ship ship)
        {
            var pos = ship.transform.position;
            bool jumped = false;
            if (ship == lastShip)
            {
                var jump = pos - lastShipPos;
                float wrap = Britannia.Active.WrapMetres;
                if (Mathf.Abs(jump.x) > wrap / 2 || Mathf.Abs(jump.z) > wrap / 2)
                {
                    var delta = new Vector3(Mathf.Round(jump.x / wrap) * wrap, 0, Mathf.Round(jump.z / wrap) * wrap);
                    if ((player.transform.position + delta - pos).sqrMagnitude < (player.transform.position - pos).sqrMagnitude)
                    {
                        MoveBody(player.transform, player.GetComponent<Rigidbody>(), delta);
                        Plugin.Log.LogInfo($"Followed my ship across the edge by {delta}");
                        jumped = true;
                    }
                }
            }
            lastShip = ship;
            lastShipPos = pos;
            return jumped;
        }

        static bool IsOwner(Ship ship)
        {
            var nview = ship.GetComponent<ZNetView>();
            return nview != null && nview.IsValid() && nview.IsOwner();
        }

        static void MoveBody(Transform t, Rigidbody body, Vector3 delta)
        {
            t.position += delta;
            if (body != null) body.position += delta;
        }

        /// <summary>Asks ZoneSystem to build the zones around <paramref name="dest"/> (one per call, like its own Update) and keeps built ones alive.</summary>
        static void Prewarm(Vector3 dest)
        {
            createLocalZones = createLocalZones ?? AccessTools.Method(typeof(ZoneSystem), "CreateLocalZones");
            createLocalZones?.Invoke(ZoneSystem.instance, new object[] { dest });
        }

        static bool DestinationReady(Vector3 dest)
        {
            var z = ZoneSystem.GetZone(dest);
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (!ZoneSystem.instance.IsZoneLoaded(new Vector2s(z.x + dx, z.y + dy))) return false;
            return true;
        }

        void Hold(Rigidbody body)
        {
            if (body == null) return;
            heldShip = body;
            heldAt = body.position;
            body.isKinematic = true;
        }

        void ReleaseHeldShip()
        {
            if (heldShip == null) return;
            if (ZoneSystem.instance != null && !DestinationReady(heldAt)) return;
            heldShip.isKinematic = false;
            heldShip = null;
        }
    }
}
