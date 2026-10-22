using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Jotunn.Managers;
using Bretaheim.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Bretaheim.WorldGen
{
    /// <summary>
    /// The pouch (MAPPING_DECISIONS 2026-10-22, <see cref="Pouch"/>): reagents and mixed spells are
    /// never inventory items. Whatever would put one into the player's inventory (a pickup, a
    /// chest, a shop, a gift, the starting kit, a reagent patch) puts it in the pouch instead (at
    /// most 99 of each: a full pouch refuses more), and an older character's are moved in on
    /// spawning. Beside the open inventory a panel shows the reagents and the spells held; a
    /// click on a spell readies it for the cast key (<see cref="Spells"/>).
    /// </summary>
    public static class PouchPanel
    {
        static GameObject panel;
        static RectTransform spellList;
        static Text readiedText;
        static readonly List<Text> reagentNames = new List<Text>(), reagentCounts = new List<Text>();
        static string shown;

        public static void Install(Harmony harmony)
        {
            var t = typeof(Inventory);
            var data = typeof(ItemDrop.ItemData);
            Prefix(harmony, AccessTools.Method(t, "AddItem", new[] { data }), nameof(AddItem_Prefix));
            Prefix(harmony, AccessTools.Method(t, "AddItem", new[] { data, typeof(Vector2i) }), nameof(AddItem_Prefix));
            Prefix(harmony, AccessTools.Method(t, "AddItem", new[] { data, typeof(int), typeof(int), typeof(int), typeof(bool) }), nameof(AddItemAt_Prefix));
            Prefix(harmony, AccessTools.Method(t, "CanAddItem", new[] { data, typeof(int) }), nameof(CanAddItem_Prefix));
            var update = AccessTools.Method(typeof(InventoryGui), "Update");
            if (update != null) harmony.Patch(update, postfix: new HarmonyMethod(typeof(PouchPanel), nameof(InventoryGuiUpdate_Postfix)));
        }

        static void Prefix(Harmony h, System.Reflection.MethodInfo m, string patch)
        {
            if (m == null) { Plugin.Log.LogWarning($"Pouch: an Inventory method for {patch} is not found"); return; }
            h.Patch(m, prefix: new HarmonyMethod(typeof(PouchPanel), patch));
        }

        // ── the pouch, in the character's custom data ────────────────────────────────────────

        public static Pouch Of(Player p) => Pouch.Load(p?.m_customData);

        public static void Save(Player p, Pouch pouch)
        {
            if (p == null) return;
            pouch.Save(p.m_customData);
        }

        static Dictionary<string, ItemDef> byName;

        /// <summary>The Ultima item an item is (by its prefab, or by its name when it has none set).</summary>
        static ItemDef DefOf(ItemDrop.ItemData item)
        {
            if (item == null) return null;
            if (item.m_dropPrefab != null) return UltimaItems.Map.Get(item.m_dropPrefab.name);
            if (byName == null)
            {
                byName = new Dictionary<string, ItemDef>();
                foreach (var d in UltimaItems.Map.All) if (Pouch.Holds(d) && d.Name != null) byName[d.Name] = d;
            }
            return item.m_shared?.m_name != null && byName.TryGetValue(item.m_shared.m_name, out var def) ? def : null;
        }

        /// <summary>
        /// A reagent or spell coming to the local player's inventory goes into the pouch: true
        /// (handled) with whether it was taken; a full pouch refuses it (it stays where it was).
        /// </summary>
        static bool Intercept(Inventory inv, ItemDrop.ItemData item, int amount, out bool taken)
        {
            taken = false;
            var me = Player.m_localPlayer;
            if (me == null || inv == null || inv != me.GetInventory()) return false;
            var def = DefOf(item);
            if (!Pouch.Holds(def)) return false;
            var pouch = Of(me);
            if (pouch.IsFull(def))
            {
                me.Message(MessageHud.MessageType.Center, $"Thy pouch holds no more {def.Name} ({Pouch.Most}).");
                return true;
            }
            amount = Mathf.Min(amount, item.m_stack);
            int added = pouch.Add(def, amount);
            Save(me, pouch);
            // (the rest over the cap is lost, as the original's)
            item.m_stack -= amount;
            taken = true;
            if (added < amount) me.Message(MessageHud.MessageType.TopLeft, $"Thy pouch holds no more {def.Name} ({Pouch.Most}).");
            return true;
        }

        static bool AddItem_Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
        {
            if (!Intercept(__instance, item, item != null ? item.m_stack : 0, out bool taken)) return true;
            __result = taken;
            return false;
        }

        /// <summary>Into a slot (a container's item dragged or taken all): loading a saved inventory is left alone (moved on spawning).</summary>
        static bool AddItemAt_Prefix(Inventory __instance, ItemDrop.ItemData item, int amount, bool skipValidPositionCheck, ref bool __result)
        {
            if (skipValidPositionCheck || !Intercept(__instance, item, amount, out bool taken)) return true;
            __result = taken;
            return false;
        }

        /// <summary>A reagent or spell always "fits" (the pouch takes it) unless the pouch is full of it: picking up is not refused for a full inventory.</summary>
        static bool CanAddItem_Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
        {
            var me = Player.m_localPlayer;
            if (me == null || __instance != me.GetInventory()) return true;
            var def = DefOf(item);
            if (!Pouch.Holds(def)) return true;
            __result = !Of(me).IsFull(def);
            return false;
        }

        /// <summary>An older character's reagents and spells (inventory items; a readied spell in the ammunition slot) moved into the pouch.</summary>
        public static void Migrate(Player p)
        {
            var inv = p.GetInventory();
            var held = inv.GetAllItems().Where(i => Pouch.Holds(DefOf(i))).ToList();
            if (held.Count == 0) return;
            var pouch = Of(p);
            foreach (var item in held)
            {
                var def = DefOf(item);
                if (item.m_equipped)
                {
                    if (def.Kind == ItemKind.Spell && pouch.Readied < 0) pouch.Readied = def.Spell;
                    p.UnequipItem(item, false);
                }
                int added = pouch.Add(def, item.m_stack);
                inv.RemoveItem(item);
                Plugin.Log.LogInfo($"Pouch: {item.m_stack} {def.Name} moved from the inventory ({added} kept)");
            }
            Save(p, pouch);
        }

        // ── the panel beside the inventory ──────────────────────────────────────────────────

        static void InventoryGuiUpdate_Postfix()
        {
            var me = Player.m_localPlayer;
            // in Britannia the pouch takes the crafting panel's place: spells are all there is to make
            var crafting = InventoryGui.instance != null ? InventoryGui.instance.m_crafting : null;
            if (crafting != null && crafting.gameObject.activeSelf == (Britannia.Active != null)) crafting.gameObject.SetActive(Britannia.Active == null);
            bool show = me != null && Britannia.Active != null && InventoryGui.IsVisible() && !StoreGui.IsVisible();
            if (!show)
            {
                if (panel != null && panel.activeSelf) panel.SetActive(false);
                return;
            }
            if (!Build()) return;
            if (!panel.activeSelf) { panel.SetActive(true); shown = null; }
            var pouch = Of(me);
            string now = pouch.ToString();
            if (now != shown) { shown = now; Refresh(pouch); }
        }

        /// <summary>A spell readied for the cast key (a click on it; the console's u5 pouch ready).</summary>
        public static bool Ready(Player me, int spell)
        {
            if (me == null || spell < 0 || spell >= Magic.Count) return false;
            var pouch = Of(me);
            pouch.Readied = spell;
            Save(me, pouch);
            Plugin.Log.LogInfo($"{me.GetPlayerName()} readies {Spells.Table[spell].Words} ({pouch.SpellCount(spell)} mixed)");
            return true;
        }

        static string Roman(int n) => new[] { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII" }[n];

        static void Refresh(Pouch pouch)
        {
            var ovl = Plugin.U5.Ovl;
            for (int r = 0; r < reagentNames.Count; r++)
            {
                reagentNames[r].text = ovl.ReagentName(r);
                reagentCounts[r].text = pouch.Reagents[r].ToString();
                var c = pouch.Reagents[r] > 0 ? Color.white : new Color(0.6f, 0.6f, 0.6f);
                reagentNames[r].color = reagentCounts[r].color = c;
            }
            foreach (Transform child in spellList) Object.Destroy(child.gameObject);
            var g = GUIManager.Instance;
            var held = pouch.HeldSpells().ToList();
            foreach (int s in held)
            {
                int ss = s;
                var sp = Spells.Table[s];
                string label = $"{sp.Words}  ×{pouch.Spells[s]}  ({Roman(sp.Circle)})";
                if (s == pouch.Readied) label = "<color=orange>" + label + "</color>";
                var b = g.CreateButton(label, spellList, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, 270, 30);
                b.AddComponent<LayoutElement>().preferredHeight = 30;
                b.GetComponent<Button>().onClick.AddListener(() => Ready(Player.m_localPlayer, ss));
            }
            if (held.Count == 0)
            {
                var none = g.CreateText("No spells mixed (M by a fire).", spellList, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, g.AveriaSerifBold, 15, new Color(0.7f, 0.7f, 0.7f), true, Color.black, 270, 30, false);
                none.AddComponent<LayoutElement>().preferredHeight = 30;
                none.GetComponent<Text>().alignment = TextAnchor.MiddleCenter;
            }
            readiedText.text = pouch.Readied >= 0
                ? $"Readied: {Spells.Table[pouch.Readied].Words} ×{pouch.SpellCount(pouch.Readied)} ({Spells.CastKey?.Value} casts)"
                : "Click a spell to ready it.";
        }

        const float Width = 310, Height = 640;

        static bool Build()
        {
            if (panel != null) return true;
            var gui = GUIManager.CustomGUIFront;
            if (gui == null) return false;
            var g = GUIManager.Instance;
            // where the crafting panel is (hidden in Britannia), in the inventory's own canvas; else in the middle at the top
            var crafting = InventoryGui.instance != null ? InventoryGui.instance.m_crafting : null;
            if (crafting != null)
            {
                panel = g.CreateWoodpanel(crafting.parent, crafting.anchorMin, crafting.anchorMax, Vector2.zero, Width, Height, false);
                var rt = (RectTransform)panel.transform;
                // its top where the crafting panel's top is
                rt.pivot = new Vector2(crafting.pivot.x, 1f);
                // (less its heading's height: the character's box above overlaps the crafting panel's top)
                rt.anchoredPosition = crafting.anchoredPosition + new Vector2(0, (1f - crafting.pivot.y) * crafting.rect.height - 45f);
                rt.SetSiblingIndex(crafting.GetSiblingIndex());
            }
            else panel = g.CreateWoodpanel(gui.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -Height / 2 - 24), Width, Height, false);
            panel.name = "u5_pouch";
            Text Label(string text, Vector2 at, int size, Color colour, float w, TextAnchor align)
            {
                var t = g.CreateText(text, panel.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), at, g.AveriaSerifBold, size, colour, true, Color.black, w, 26, false).GetComponent<Text>();
                t.alignment = align;
                return t;
            }
            Label("Pouch", new Vector2(0, -28), 24, g.ValheimOrange, Width - 30, TextAnchor.MiddleCenter);
            Label("Reagents", new Vector2(0, -60), 18, g.ValheimOrange, Width - 40, TextAnchor.MiddleLeft);
            for (int r = 0; r < Magic.Reagents; r++)
            {
                float y = -86 - r * 24;
                reagentNames.Add(Label("", new Vector2(-20, y), 16, Color.white, Width - 80, TextAnchor.MiddleLeft));
                reagentCounts.Add(Label("", new Vector2(Width / 2 - 50, y), 16, Color.white, 50, TextAnchor.MiddleRight));
            }
            Label("Spells", new Vector2(0, -290), 18, g.ValheimOrange, Width - 40, TextAnchor.MiddleLeft);
            // the spells held: a list scrolled with the mouse wheel
            var view = new GameObject("spells", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            var vrt = view.GetComponent<RectTransform>();
            vrt.SetParent(panel.transform, false);
            vrt.anchorMin = vrt.anchorMax = new Vector2(0.5f, 1f);
            vrt.pivot = new Vector2(0.5f, 1f);
            vrt.anchoredPosition = new Vector2(0, -306);
            vrt.sizeDelta = new Vector2(Width - 30, Height - 306 - 60);
            view.GetComponent<Image>().color = new Color(0, 0, 0, 0.25f);
            var content = new GameObject("list", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            spellList = content.GetComponent<RectTransform>();
            spellList.SetParent(vrt, false);
            spellList.anchorMin = new Vector2(0f, 1f);
            spellList.anchorMax = new Vector2(1f, 1f);
            spellList.pivot = new Vector2(0.5f, 1f);
            spellList.anchoredPosition = Vector2.zero;
            spellList.sizeDelta = Vector2.zero;
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 4;
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = view.GetComponent<ScrollRect>();
            scroll.content = spellList;
            scroll.viewport = vrt;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = false;
            scroll.scrollSensitivity = 34;
            readiedText = g.CreateText("", panel.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 30), g.AveriaSerifBold, 15, Color.white, true, Color.black, Width - 30, 44, false).GetComponent<Text>();
            readiedText.alignment = TextAnchor.MiddleCenter;
            panel.SetActive(false);
            return true;
        }

        /// <summary>The console's u5 pouch: what it holds (and, with devcommands, give or ready).</summary>
        public static string Command(string[] args)
        {
            var me = Player.m_localPlayer;
            if (me == null) return "u5 pouch: no player";
            var pouch = Of(me);
            if (args.Length >= 4 && args[1] == "give")
            {
                if (!Terminal.m_cheat) return "u5 pouch give needs devcommands";
                if (!int.TryParse(args[3], out int i) || !int.TryParse(args.Length > 4 ? args[4] : "1", out int n)) return "u5 pouch give reagent|spell <index> <n>";
                int added = args[2] == "reagent" && i >= 0 && i < Magic.Reagents ? pouch.Add(UltimaItems.Map.Reagent(i), n)
                          : args[2] == "spell" ? pouch.AddSpell(i, n) : -1;
                if (added < 0) return "u5 pouch give reagent <0-7> <n> | spell <0-47> <n>";
                Save(me, pouch);
                return $"{added} given: {pouch}";
            }
            if (args.Length >= 3 && args[1] == "ready" && int.TryParse(args[2], out int s))
                return Ready(me, s) ? $"readied {Spells.Table[s].Words}" : "u5 pouch ready <0-47>";
            var ovl = Plugin.U5.Ovl;
            var lines = new List<string> { "reagents: " + string.Join(", ", Enumerable.Range(0, Magic.Reagents).Select(r => $"{ovl.ReagentName(r)} {pouch.Reagents[r]}")) };
            lines.Add("spells: " + (pouch.HeldSpells().Any() ? string.Join(", ", pouch.HeldSpells().Select(x => $"{x} {Spells.Table[x].Words} {pouch.Spells[x]}")) : "none"));
            lines.Add("readied: " + (pouch.Readied >= 0 ? $"{pouch.Readied} {Spells.Table[pouch.Readied].Words}" : "none"));
            return string.Join("\n", lines);
        }
    }
}
