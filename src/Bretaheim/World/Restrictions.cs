using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Bretaheim.Data;
using UnityEngine;

namespace Bretaheim.WorldGen
{
    /// <summary>
    /// What Valheim lets a player do that Britannia does not (MASTER_PLAN §5.12, "Restrictions and
    /// Etc.txt"), in Britannia worlds only. Banned everywhere, Iolo's hut included: felling trees,
    /// breaking rocks, raising or digging the ground, building crafting tables, crafting anything
    /// at all (the recipes are gone from the crafting window), brewing and cooking, planting food
    /// (garlic and ginseng may be planted: the turnip and carrot, whose harvest is the reagent).
    /// The hammer, hoe and cultivator (the tool chest in Iolo's hut) work only around the hut: a
    /// player carrying one cannot leave it, nor drop one elsewhere; building there is for the
    /// hut's upkeep and its garden.
    /// </summary>
    public static class Restrictions
    {
        /// <summary>The Valheim tools of Iolo's hut.</summary>
        public static readonly string[] Tools = { "Hammer", "Hoe", "Cultivator" };
        /// <summary>What the plantable reagents grow into, and the reagent harvested from them (items.json reagents "plantable").</summary>
        static readonly Dictionary<string, int> Reagents = new Dictionary<string, int> { { "Carrot", 1 }, { "Turnip", 2 } };
        /// <summary>Iolo's hut (location 13).</summary>
        public const int Hut = 13;
        const float Margin = 4f;

        static bool Active => Britannia.Active != null;
        static float sayAt;

        static void Say(Character who, string what)
        {
            if (who != Player.m_localPlayer || Time.time < sayAt) return;
            sayAt = Time.time + 1.5f;
            who.Message(MessageHud.MessageType.Center, what);
        }

        /// <summary>Whether a position is around Iolo's hut (its settlement's square and a margin); false when the hut is not built.</summary>
        public static bool AroundHut(Vector3 p)
        {
            var hut = Settlements.Get(Hut);
            return hut != null && Mathf.Max(Mathf.Abs(p.x - hut.Origin.x), Mathf.Abs(p.z - hut.Origin.z)) <= hut.Site.Half + Margin;
        }

        public static bool IsTool(ItemDrop.ItemData item) => item?.m_dropPrefab != null && Tools.Contains(item.m_dropPrefab.name);

        public static void Install(Harmony harmony)
        {
            Prefix(harmony, typeof(TreeBase), nameof(TreeBase.Damage), nameof(Chop_Prefix));
            Prefix(harmony, typeof(TreeLog), nameof(TreeLog.Damage), nameof(Chop_Prefix));
            Prefix(harmony, typeof(MineRock), nameof(MineRock.Damage), nameof(Mine_Prefix));
            Prefix(harmony, typeof(MineRock5), nameof(MineRock5.Damage), nameof(Mine_Prefix));
            Prefix(harmony, typeof(Destructible), nameof(Destructible.Damage), nameof(Destructible_Prefix));
            Prefix(harmony, typeof(TerrainOp), "Awake", nameof(TerrainOp_Prefix));
            Prefix(harmony, typeof(Player), nameof(Player.TryPlacePiece), nameof(TryPlacePiece_Prefix));
            Prefix(harmony, typeof(InventoryGui), "DoCrafting", nameof(DoCrafting_Prefix));
            Prefix(harmony, typeof(Fermenter), nameof(Fermenter.Interact), nameof(Brew_Prefix));
            Prefix(harmony, typeof(Fermenter), nameof(Fermenter.UseItem), nameof(BrewItem_Prefix));
            Prefix(harmony, typeof(Fermenter), nameof(Fermenter.GetHoverText), nameof(BarrelHover_Prefix));
            Prefix(harmony, typeof(Fermenter), nameof(Fermenter.GetHoverName), nameof(BarrelName_Prefix));
            Prefix(harmony, typeof(CookingStation), nameof(CookingStation.Interact), nameof(Brew_Prefix));
            Prefix(harmony, typeof(CookingStation), nameof(CookingStation.UseItem), nameof(BrewItem_Prefix));
            Prefix(harmony, typeof(Humanoid), nameof(Humanoid.DropItem), nameof(DropItem_Prefix));
            var recipes = AccessTools.Method(typeof(Player), nameof(Player.GetAvailableRecipes));
            if (recipes != null) harmony.Patch(recipes, postfix: new HarmonyMethod(typeof(Restrictions), nameof(GetAvailableRecipes_Postfix)));
            var pieces = AccessTools.Method(typeof(PieceTable), nameof(PieceTable.UpdateAvailable));
            if (pieces != null) harmony.Patch(pieces, postfix: new HarmonyMethod(typeof(Restrictions), nameof(UpdateAvailable_Postfix)));
            var pickable = AccessTools.Method(typeof(Pickable), "Awake");
            if (pickable != null) harmony.Patch(pickable, postfix: new HarmonyMethod(typeof(Restrictions), nameof(PickableAwake_Postfix)));
        }

        static void Prefix(Harmony h, System.Type t, string m, string prefix)
        {
            var method = AccessTools.Method(t, m);
            if (method == null) { Plugin.Log.LogWarning($"Restrictions: {t.Name}.{m} not found"); return; }
            h.Patch(method, prefix: new HarmonyMethod(typeof(Restrictions), prefix));
        }

        // ── the land: trees, rocks, ground ──────────────────────────────────────────────────

        static bool Chop_Prefix(HitData hit)
        {
            if (!Active || !(hit?.GetAttacker() is Player p)) return true;
            Say(p, "Thou shalt not fell the trees of Britannia.");
            return false;
        }

        static bool Mine_Prefix(HitData hit)
        {
            if (!Active || !(hit?.GetAttacker() is Player p)) return true;
            Say(p, "Thou canst not break the rocks of Britannia.");
            return false;
        }

        /// <summary>Stumps, saplings and small rocks; barrels, pots and the pushable things of the settlements still break.</summary>
        static bool Destructible_Prefix(Destructible __instance, HitData hit)
        {
            if (!Active || !(hit?.GetAttacker() is Player p)) return true;
            string name = __instance.name.ToLowerInvariant();
            if (__instance.GetDestructibleType() == DestructibleType.Tree) { Say(p, "Thou shalt not fell the trees of Britannia."); return false; }
            if (name.StartsWith("rock") || name.Contains("_rock") || __instance.GetComponent<MineRock>() != null) { Say(p, "Thou canst not break the rocks of Britannia."); return false; }
            return true;
        }

        static bool Reshapes(TerrainOp op) => op.m_settings.m_level || op.m_settings.m_raise || op.m_settings.m_smooth;

        /// <summary>No ground is raised, lowered, levelled or smoothed (hoe, pickaxe): the op vanishes unapplied. Paint (the cultivator, a path) is left to the placing rules.</summary>
        static bool TerrainOp_Prefix(TerrainOp __instance)
        {
            if (!Active || !Reshapes(__instance)) return true;
            var me = Player.m_localPlayer;
            if (me != null && Vector3.Distance(me.transform.position, __instance.transform.position) < 12f) Say(me, "The land of Britannia is not thine to reshape.");
            Object.Destroy(__instance.gameObject);
            return false;
        }

        // ── building and planting ───────────────────────────────────────────────────────────

        /// <summary>Why a piece is never placed in Britannia (null: it may be, around the hut).</summary>
        static string Banned(Piece piece)
        {
            if (piece.GetComponent<CraftingStation>() != null || piece.GetComponentInChildren<CraftingStation>() != null) return "No crafting table is built in Britannia.";
            var op = piece.GetComponent<TerrainOp>();
            if (op != null && Reshapes(op)) return "The land of Britannia is not thine to reshape.";
            var plant = piece.GetComponent<Plant>();
            if (plant != null && !PlantsReagent(plant)) return "Only garlic and ginseng are planted in Britannia.";
            return null;
        }

        /// <summary>Why a piece may not be placed at a position (null: it may).</summary>
        static string Forbidden(Piece piece, Vector3 at) => Banned(piece) ?? (AroundHut(at) ? null : "Thou mayest build only to keep Iolo's hut.");

        static bool PlantsReagent(Plant plant)
            => plant.m_grownPrefabs.Length > 0 && plant.m_grownPrefabs.All(g => g != null && g.GetComponent<Pickable>()?.m_itemPrefab != null && Reagents.ContainsKey(g.GetComponent<Pickable>().m_itemPrefab.name));

        static bool TryPlacePiece_Prefix(Player __instance, Piece piece, ref bool __result)
        {
            if (!Active || piece == null) return true;
            var ghost = Traverse.Create(__instance).Field("m_placementGhost").GetValue<GameObject>();
            var at = ghost != null ? ghost.transform.position : __instance.transform.position;
            string why = Forbidden(piece, at);
            if (why == null) return true;
            Say(__instance, why);
            __result = false;
            return false;
        }

        /// <summary>The banned pieces are not offered at all (crafting tables, food plants, reshaping the ground).</summary>
        static void UpdateAvailable_Postfix(PieceTable __instance)
        {
            if (!Active) return;
            bool Gone(Piece p) => p == null || Banned(p) != null;
            __instance.m_availablePieces.RemoveWhere(Gone);
            var byCategory = Traverse.Create(__instance).Field("m_availablePiecesByCategory").GetValue<List<List<Piece>>>();
            if (byCategory != null) foreach (var list in byCategory) list.RemoveAll(Gone);
        }

        /// <summary>A planted carrot is ginseng and a turnip garlic: their harvest is the reagent.</summary>
        static void PickableAwake_Postfix(Pickable __instance)
        {
            if (!Active || __instance.m_itemPrefab == null || !Reagents.TryGetValue(__instance.m_itemPrefab.name, out var reagent)) return;
            var def = UltimaItems.Map?.Reagent(reagent);
            var prefab = def != null ? ObjectDB.instance?.GetItemPrefab(def.Prefab) : null;
            if (prefab != null) __instance.m_itemPrefab = prefab;
        }

        // ── crafting, brewing, cooking ──────────────────────────────────────────────────────

        static bool DoCrafting_Prefix(Player player)
        {
            if (!Active) return true;
            Say(player, "Nothing is crafted in Britannia: arms, potions and torches are bought, found or won.");
            return false;
        }

        static void GetAvailableRecipes_Postfix(ref List<Recipe> available)
        {
            if (Active) available?.Clear();
        }

        /// <summary>A fermenter in Britannia is a barrel (tile 0xa6): searched, never brewing (Searching).</summary>
        static bool Brew_Prefix(Fermenter __instance, Humanoid user, bool hold, ref bool __result)
        {
            if (!Active) return true;
            if (!hold && user is Player p && p == Player.m_localPlayer)
                Searching.Search(__instance.transform.position, Searching.TileReach, Searching.SInTheBarrel, p);
            __result = true;
            return false;
        }

        static bool BrewItem_Prefix(ref bool __result)
        {
            if (!Active) return true;
            __result = false;
            return false;
        }

        static bool BarrelHover_Prefix(ref string __result)
        {
            if (!Active) return true;
            __result = Searching.SearchHover("Barrel");
            return false;
        }

        static bool BarrelName_Prefix(ref string __result)
        {
            if (!Active) return true;
            __result = "Barrel";
            return false;
        }

        // ── the tools stay at the hut ───────────────────────────────────────────────────────

        static bool DropItem_Prefix(Humanoid __instance, ItemDrop.ItemData item, ref bool __result)
        {
            if (!Active || !(__instance is Player p) || !IsTool(item) || AroundHut(p.transform.position)) return true;
            Say(p, "The tools belong in the chest of Iolo's hut.");
            __result = false;
            return false;
        }

        /// <summary>The local player carrying a tool of the hut cannot leave it: they are kept at the edge.</summary>
        public class ToolWatch : MonoBehaviour
        {
            Vector3 lastInside;
            bool haveInside;
            float checkAt;

            void Update()
            {
                if (Time.time < checkAt) return;
                checkAt = Time.time + 0.2f;
                var me = Player.m_localPlayer;
                if (me == null || !Active || me.IsDead() || me.IsTeleporting() || Settlements.Get(Hut) == null) { haveInside = false; return; }
                var pos = me.transform.position;
                if (AroundHut(pos)) { lastInside = pos; haveInside = true; return; }
                if (!haveInside || !me.GetInventory().GetAllItems().Any(IsTool)) return;
                // back to where they last stood around the hut
                var body = me.GetComponent<Rigidbody>();
                if (body != null) body.velocity = Vector3.zero;
                me.transform.position = lastInside;
                Say(me, "Put the tools away in the chest of Iolo's hut before thou leavest.");
            }
        }
    }
}
