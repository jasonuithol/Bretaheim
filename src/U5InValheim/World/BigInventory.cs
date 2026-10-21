using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Jotunn.Managers;
using U5InValheim.Data;
using UnityEngine;
using UnityEngine.UI;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// The player's inventory in Britannia (MAPPING_DECISIONS 2026-10-22, <see cref="InventorySize"/>):
    /// 8 × 14 slots, the window showing six rows and scrolling the rest (mouse wheel); the hotbar
    /// is still the first row. The size is never less than the last row holding something, so
    /// Valheim (which drops what lies past its rows on spawning) never throws an item out, in
    /// Britannia or after it. A stack of Ultima torches burns out one torch at a time.
    /// </summary>
    public static class BigInventory
    {
        public static void Install(Harmony harmony)
        {
            var spawned = AccessTools.Method(typeof(Player), "OnSpawned");
            // (first: the starting kit, given on the same spawn, lands in the bigger inventory)
            if (spawned != null) harmony.Patch(spawned, postfix: new HarmonyMethod(typeof(BigInventory), nameof(OnSpawned_Postfix)) { priority = Priority.First });
            var size = AccessTools.Method(typeof(Player), "SetInventorySize");
            if (size != null) harmony.Patch(size, postfix: new HarmonyMethod(typeof(BigInventory), nameof(SetInventorySize_Postfix)));
            var invalid = AccessTools.Method(typeof(Humanoid), "DropInvalidItems");
            if (invalid != null) harmony.Patch(invalid, prefix: new HarmonyMethod(typeof(BigInventory), nameof(DropInvalidItems_Prefix)));
            var drain = AccessTools.Method(typeof(Humanoid), "DrainEquipedItemDurability");
            if (drain != null) harmony.Patch(drain, prefix: new HarmonyMethod(typeof(BigInventory), nameof(Drain_Prefix)));
            var empty = AccessTools.Method(typeof(Inventory), "FindEmptySlot");
            if (empty != null) harmony.Patch(empty, prefix: new HarmonyMethod(typeof(BigInventory), nameof(FindEmptySlot_Prefix)));
            var show = AccessTools.Method(typeof(InventoryGui), "Show");
            if (show != null) harmony.Patch(show, postfix: new HarmonyMethod(typeof(BigInventory), nameof(Show_Postfix)));
            var grid = AccessTools.Method(typeof(InventoryGrid), nameof(InventoryGrid.UpdateInventory));
            if (grid != null) harmony.Patch(grid, postfix: new HarmonyMethod(typeof(BigInventory), nameof(UpdateInventory_Postfix)));
            if (spawned == null || size == null || invalid == null || drain == null || show == null || grid == null)
                Plugin.Log.LogWarning("Inventory: Player.OnSpawned / SetInventorySize, Humanoid.DropInvalidItems / DrainEquipedItemDurability, InventoryGui.Show or InventoryGrid.UpdateInventory not found");
        }

        static void OnSpawned_Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return;
            Apply(__instance);
            PouchPanel.Migrate(__instance);
        }

        static void SetInventorySize_Postfix(Player __instance)
        {
            if (__instance == Player.m_localPlayer) Apply(__instance);
        }

        /// <summary>Valheim's rows for this character (4, or what was bought from Haldor).</summary>
        static int VanillaRows(Player p)
            => p.TryGetUniqueKeyValue(Player.InventoryRowsKey, out var v) && int.TryParse(v, out int r) ? Mathf.Clamp(r, 1, 9) : InventorySize.VanillaRows;

        /// <summary>The inventory sized for the world (and its contents), the window sized to show part of it.</summary>
        public static void Apply(Player p)
        {
            var inv = p.GetInventory();
            int vanilla = VanillaRows(p);
            int rows = InventorySize.RowsFor(Britannia.Active != null, vanilla, inv.GetAllItems().Select(i => i.m_gridPos.y));
            if (rows != inv.GetHeight())
            {
                inv.SetHeight(rows);
                Plugin.Log.LogInfo($"Inventory: {inv.GetWidth()} × {rows} slots{(Britannia.Active != null ? " (Britannia)" : "")}");
            }
            var gui = InventoryGui.instance;
            if (gui == null) return;
            visible = InventorySize.VisibleFor(rows, vanilla);
            gui.SetInventorySize(visible);
        }

        /// <summary>Nothing lies past the rows (an older or bigger inventory loaded): the rows grow to it, so Valheim drops nothing.</summary>
        static void DropInvalidItems_Prefix(Humanoid __instance)
        {
            if (!(__instance is Player p)) return;
            var inv = p.GetInventory();
            int need = inv.GetAllItems().Select(i => i.m_gridPos.y + 1).DefaultIfEmpty(0).Max();
            if (need > inv.GetHeight()) inv.SetHeight(need);
        }

        /// <summary>A burnt-out torch from a stack of Ultima torches: one is gone, the next is lit (Valheim's takes the whole stack).</summary>
        static bool Drain_Prefix(Humanoid __instance, ItemDrop.ItemData item, float dt)
        {
            if (item == null || item.m_stack <= 1 || item.m_dropPrefab == null || UltimaItems.Map.Get(item.m_dropPrefab.name) == null) return true;
            if (item.m_durability - item.m_shared.m_durabilityDrain * dt * Game.m_durabilityRate > 0f) return true;
            item.m_stack--;
            item.m_durability = item.GetMaxDurability();
            __instance.Message(MessageHud.MessageType.TopLeft, Localization.instance.Localize("$msg_broke", item.m_shared.m_name), 0, item.GetIcon());
            __instance.GetInventory().m_onChanged?.Invoke();
            return false;
        }

        /// <summary>
        /// Valheim puts what is not a weapon or tool in the lowest free row: in a scrolled
        /// inventory that is out of sight, so such things go to the first free slot under the
        /// hotbar instead (the hotbar row last).
        /// </summary>
        static bool FindEmptySlot_Prefix(Inventory __instance, bool topFirst, ref Vector2i __result)
        {
            var me = Player.m_localPlayer;
            if (topFirst || me == null || __instance != me.GetInventory() || __instance.GetHeight() <= InventorySize.Visible) return true;
            int w = __instance.GetWidth(), h = __instance.GetHeight();
            for (int i = 1; i <= h; i++)
            {
                int y = i % h;
                for (int x = 0; x < w; x++)
                    if (__instance.GetItemAt(x, y) == null) { __result = new Vector2i(x, y); return false; }
            }
            __result = new Vector2i(-1, -1);
            return false;
        }

        static void Show_Postfix(InventoryGui __instance)
        {
            var me = Player.m_localPlayer;
            if (me != null) Apply(me);
        }

        // ── the window: a few rows at a time, scrolled with the mouse wheel ───────────────────

        static int visible = InventorySize.VanillaRows, first;
        static Text rowsText;
        static readonly AccessTools.FieldRef<InventoryGrid, List<InventoryElement>> Elements = AccessTools.FieldRefAccess<InventoryGrid, List<InventoryElement>>("m_elements");
        static readonly AccessTools.FieldRef<InventoryGrid, Vector2i> Selected = AccessTools.FieldRefAccess<InventoryGrid, Vector2i>("m_selected");
        static readonly Dictionary<InventoryElement, Vector2> placed = new Dictionary<InventoryElement, Vector2>();

        /// <summary>Scrolls the inventory window to show this row first (the console's u5 scroll, for tests).</summary>
        public static void ScrollTo(int row) => first = row;

        /// <summary>
        /// The player's grid shows rows [first, first + visible): the grid root moved up by the
        /// rows scrolled past, the other rows' slots hidden (and moved aside, so the pointer never
        /// finds them); the mouse wheel over the grid (or the gamepad's selection) scrolls.
        /// </summary>
        static void UpdateInventory_Postfix(InventoryGrid __instance, Inventory inventory, Player player)
        {
            var gui = InventoryGui.instance;
            if (gui == null || __instance != gui.m_playerGrid || player == null || inventory == null) return;
            int rows = inventory.GetHeight();
            int shown = Mathf.Min(visible, rows);
            var view = __instance.transform as RectTransform;
            float space = __instance.m_elementSpace;
            if (rows > shown)
            {
                Vector3 pointer = ZInput.pointerPosition;
                if (view.rect.Contains(view.InverseTransformPoint(pointer)))
                {
                    float wheel = ZInput.GetMouseScrollWheel();
                    if (wheel < 0f) first++;
                    else if (wheel > 0f) first--;
                }
                // (the gamepad's selection kept in sight)
                if (ZInput.IsGamepadActive())
                {
                    int sel = Selected(__instance).y;
                    if (sel < first) first = sel;
                    else if (sel >= first + shown) first = sel - shown + 1;
                }
            }
            first = Mathf.Clamp(first, 0, rows - shown);
            var root = __instance.m_gridRoot;
            root.anchoredPosition = new Vector2(root.anchoredPosition.x, first * space);
            foreach (var e in Elements(__instance))
            {
                if (e == null) continue;
                var rt = e.transform as RectTransform;
                if (!placed.TryGetValue(e, out var at)) placed[e] = at = rt.anchoredPosition;
                bool inSight = e.Position.y >= first && e.Position.y < first + shown;
                if (e.gameObject.activeSelf != inSight) e.gameObject.SetActive(inSight);
                rt.anchoredPosition = inSight ? at : at + new Vector2(100000f, 0f);
            }
            if (rows > shown && rowsText == null)
            {
                var g = GUIManager.Instance;
                rowsText = g.CreateText("", gui.m_player, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-120, -14), g.AveriaSerifBold, 15,
                    g.ValheimOrange, true, Color.black, 240, 24, false).GetComponent<Text>();
                rowsText.alignment = TextAnchor.MiddleRight;
            }
            if (rowsText == null) return;
            rowsText.gameObject.SetActive(rows > shown);
            rowsText.text = $"rows {first + 1}–{first + shown} of {rows} (mouse wheel)";
        }
    }
}
