using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Bretaheim.WorldGen
{
    /// <summary>
    /// The settlements' furniture keeps none of its Valheim uses (storage, adding fuel, crafting,
    /// hanging items): the original lets one only Search a tile. Wardrobes (dressers), wooden chests
    /// (trunks), pots, the hearth (fireplace) and braziers are searched (<see cref="Searching"/>),
    /// with the original's words for each; torches, candles, campfires, cauldrons and item stands
    /// show their name and do nothing. Iolo's hut keeps its tool chest (§5.12), and the
    /// loot chests (u5_chest, not settlement pieces) stay chests.
    /// </summary>
    public static class TownFurniture
    {
        const int SInTheDresser = 0x89dc, SInTheTrunk = 0x89ee, SInTheFireplace = 0x89fe, SInTheBrazier = 0x8a12, SPlain = 0x8a34;
        const int IolosHut = 13;

        public static void Install(Harmony harmony)
        {
            foreach (var t in new[] { typeof(Container), typeof(Fireplace), typeof(CraftingStation), typeof(ItemStand) })
            {
                Patch(harmony, t, "Interact", nameof(Interact_Prefix));
                Patch(harmony, t, "UseItem", nameof(UseItem_Prefix));
                Patch(harmony, t, "GetHoverText", nameof(Hover_Prefix));
            }
        }

        static void Patch(Harmony h, System.Type t, string m, string prefix)
        {
            var method = AccessTools.Method(t, m);
            if (method == null) { Plugin.Log.LogWarning($"Town furniture: {t.Name}.{m} not found"); return; }
            h.Patch(method, prefix: new HarmonyMethod(typeof(TownFurniture), prefix));
        }

        /// <summary>A settlement's furniture in Britannia (not Iolo's hut, whose tool chest is the players').</summary>
        static bool Town(Component c)
        {
            if (Britannia.Active == null || c == null || c.GetComponentInParent<U5Fixed>() == null) return false;
            var town = Shadowlords.TownAt(c.transform.position);
            return town == null || town.Location.Number != IolosHut;
        }

        /// <summary>The original's words for searching this furniture ("In the dresser t…"), or 0 when it is not searched.</summary>
        public static int Where(GameObject go)
        {
            switch (Utils.GetPrefabName(go))
            {
                case "piece_chest_warderobe": return SInTheDresser;
                case "piece_chest_wood": return SInTheTrunk;
                case "piece_pot1_red": case "piece_pot2": return SPlain;
                case "hearth": return SInTheFireplace;
                case "piece_brazierfloor01": return SInTheBrazier;
                default: return 0;
            }
        }

        static bool Interact_Prefix(Component __instance, Humanoid __0, bool __1, ref bool __result)
        {
            if (!Town(__instance)) return true;
            int where = Where(__instance.gameObject);
            if (where != 0 && !__1 && __0 is Player p && p == Player.m_localPlayer)
                Searching.Search(__instance.transform.position, Searching.TileReach, where, p);
            __result = where != 0;
            return false;
        }

        static bool UseItem_Prefix(Component __instance, ref bool __result)
        {
            if (!Town(__instance)) return true;
            __result = false;
            return false;
        }

        static bool Hover_Prefix(Component __instance, ref string __result)
        {
            if (!Town(__instance)) return true;
            var hover = __instance.GetComponent<Hoverable>();
            string name = Localization.instance.Localize(hover?.GetHoverName() ?? Utils.GetPrefabName(__instance.gameObject));
            __result = Where(__instance.gameObject) != 0 ? Searching.SearchHover(name) : name;
            return false;
        }

        /// <summary>Searchable town furniture within reach of a point: its words, or 0.</summary>
        public static int At(Vector3 at)
        {
            foreach (var c in Object.FindObjectsByType<U5Fixed>(FindObjectsSortMode.None))
            {
                if (c == null) continue;
                var d = c.transform.position - at;
                if (new Vector2(d.x, d.z).magnitude >= Searching.TileReach || Mathf.Abs(d.y) > 1.5f) continue;
                int where = Where(c.gameObject);
                if (where != 0) return where;
            }
            return 0;
        }
    }
}
