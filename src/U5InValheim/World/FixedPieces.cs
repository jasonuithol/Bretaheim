using System;
using HarmonyLib;
using UnityEngine;

namespace U5InValheim.WorldGen
{
    /// <summary>Marks a settlement piece once it has loaded (cheap test for the damage patches).</summary>
    public class U5Fixed : MonoBehaviour { }

    /// <summary>
    /// Settlement pieces are part of the world, not player builds: they never wear, never lose
    /// support, take no damage and cannot be removed with the hammer. Stairs and ladders get their
    /// climb interaction. Pieces are recognised by the "u5_fixed" key on their ZDO.
    /// </summary>
    public static class FixedPieces
    {
        public static void Install(Harmony harmony)
        {
            try
            {
                Postfix(harmony, typeof(ZNetView), "Awake", nameof(ZNetView_Awake));
                Postfix(harmony, typeof(WearNTear), "Awake", nameof(WearNTear_Awake));
                Prefix(harmony, typeof(WearNTear), "Damage", nameof(Block));
                Prefix(harmony, typeof(WearNTear), "ApplyDamage", nameof(BlockApply));
                Prefix(harmony, typeof(WearNTear), "Remove", nameof(Block));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Fixed settlement pieces not installed: {e.Message}");
            }
        }

        static void Postfix(Harmony h, Type t, string m, string patch)
            => h.Patch(AccessTools.Method(t, m) ?? throw new MissingMethodException(t.Name, m), postfix: new HarmonyMethod(typeof(FixedPieces), patch));

        static void Prefix(Harmony h, Type t, string m, string patch)
            => h.Patch(AccessTools.Method(t, m) ?? throw new MissingMethodException(t.Name, m), prefix: new HarmonyMethod(typeof(FixedPieces), patch));

        /// <summary>Settlement pieces squeezed into their tile (or turned upside down) get their scale back on load.</summary>
        static void ZNetView_Awake(ZNetView __instance)
        {
            var zdo = __instance.GetZDO();
            if (zdo == null) return;
            var scale = zdo.GetVec3(Settlements.ScaleKey, Vector3.zero);
            if (scale != Vector3.zero) __instance.transform.localScale = scale;
        }

        static void WearNTear_Awake(WearNTear __instance)
        {
            var zdo = __instance.GetComponent<ZNetView>()?.GetZDO();
            if (zdo == null || !zdo.GetBool(Settlements.FixedKey)) return;
            Apply(__instance, zdo);
        }

        /// <summary>
        /// Makes a loaded settlement piece permanent. Called from WearNTear.Awake for pieces loaded
        /// from the world, and right after spawning for new ones — their Awake runs inside
        /// Instantiate, before the spawner has written the "u5_fixed" key.
        /// </summary>
        public static void Apply(WearNTear __instance, ZDO zdo)
        {
            if (__instance.GetComponent<U5Fixed>() != null) return;
            __instance.gameObject.AddComponent<U5Fixed>();
            __instance.m_noSupportWear = false;
            __instance.m_noRoofWear = false;
            var piece = __instance.GetComponent<Piece>();
            if (piece != null) piece.m_canBeRemoved = false;
            // town fires (torches, braziers, hearths, stoves) burn for ever, starting full
            var fire = __instance.GetComponent<Fireplace>();
            if (fire != null)
            {
                fire.m_infiniteFuel = true;
                if (zdo.IsOwner() && zdo.GetFloat(ZDOVars.s_fuel) < fire.m_maxFuel) zdo.Set(ZDOVars.s_fuel, fire.m_maxFuel);
            }
            var target = zdo.GetVec3(Settlements.ClimbKey, Vector3.zero);
            if (target != Vector3.zero)
            {
                var climb = __instance.gameObject.AddComponent<ClimbPoint>();
                climb.Target = target;
                climb.Verb = zdo.GetString(Settlements.ClimbVerbKey, "Climb");
            }
        }

        static bool Block(WearNTear __instance) => __instance.GetComponent<U5Fixed>() == null;

        static bool BlockApply(WearNTear __instance, ref bool __result)
        {
            if (__instance.GetComponent<U5Fixed>() == null) return true;
            __result = false;
            return false;
        }
    }

    /// <summary>Stairs/ladder of a settlement: interact to change floor, as the original does on the spot.</summary>
    public class ClimbPoint : MonoBehaviour, Interactable, Hoverable
    {
        public Vector3 Target;
        public string Verb = "Climb";

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !(user is Player player) || player != Player.m_localPlayer) return false;
            var body = player.GetComponent<Rigidbody>();
            player.transform.position = Target;
            if (body != null)
            {
                body.position = Target;
                body.linearVelocity = Vector3.zero;
            }
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
        public string GetHoverText() => Localization.instance.Localize($"[<color=yellow><b>$KEY_Use</b></color>] {Verb}");
        public string GetHoverName() => Verb;
        public float GetHoverOffset() => 0.5f;
    }
}
