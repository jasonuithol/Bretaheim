using System.Linq;
using HarmonyLib;
using Jotunn.Managers;
using U5InValheim.Data;
using UnityEngine;
using UnityEngine.UI;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// Use item and View gem (Action mappings; MASTER_PLAN §5.5, §5.11): right-clicking an
    /// Ultima item in the inventory (or its hotbar key) does what the original's Use does, in its
    /// words (CAST.OVL use_item_command). The gem opens the Valheim map and is spent (the View
    /// command); in Britannia the map key no longer opens it, gems are how maps are seen. The
    /// sextant gives the position at night out in the land; the pocket watch the time; the
    /// spyglass the night sky with its eight town-stars and the Shadowlords' crescents (a panel
    /// drawn as the original's telescope draws it); the magic carpet is laid and boarded
    /// (<see cref="MagicCarpet"/>); the sceptre dissolves fields. The badge, the crown and the
    /// amulet answer as they are put on and taken off (their effects: <see cref="WornEffects"/>).
    /// </summary>
    public static class UseItems
    {
        static bool viewingGem;

        /// <summary>The Britannian date the night sky is drawn for (year, month 1..13, day 1..28): the calendar's when it has one; until then INIT.GAM's first day plus Valheim's days.</summary>
        public static System.Func<(int year, int month, int day)> Date = () => { var n = Calendar.Now; return (n.Year, n.Month, n.Day); };
        /// <summary>The towns (1..8) the three Shadowlords are at (0 none, 0xff destroyed): INIT.GAM's until the Shadowlords move.</summary>
        public static System.Func<int[]> ShadowlordTowns = () => Enumerable.Range(0, 3).Select(i => Shadowlords.Doomed(i) ? 0xff : Shadowlords.TownOf(i)).ToArray();

        public static void Install(Harmony harmony)
        {
            var use = AccessTools.Method(typeof(Humanoid), "UseItem", new[] { typeof(Inventory), typeof(ItemDrop.ItemData), typeof(bool) });
            if (use != null) harmony.Patch(use, prefix: new HarmonyMethod(typeof(UseItems), nameof(UseItem_Prefix)), postfix: new HarmonyMethod(typeof(UseItems), nameof(UseItem_Postfix)));
            else Plugin.Log.LogWarning("Use items: Humanoid.UseItem not found");
            var mode = AccessTools.Method(typeof(Minimap), "SetMapMode");
            if (mode != null) harmony.Patch(mode, prefix: new HarmonyMethod(typeof(UseItems), nameof(SetMapMode_Prefix)));
            else Plugin.Log.LogWarning("Use items: Minimap.SetMapMode not found");
            // Escape closes the night sky, not the game menu behind it
            var menu = AccessTools.Method(typeof(Menu), "Update");
            if (menu != null) harmony.Patch(menu, prefix: new HarmonyMethod(typeof(UseItems), nameof(MenuUpdate_Prefix)));
        }

        static bool MenuUpdate_Prefix() => !SkyPanel.IsOpen;

        static Player Me => Player.m_localPlayer;
        static DataOvl Ovl => Plugin.U5.Ovl;
        static string Say(int dg) => Ovl.CStr(dg).Replace("\n", " ").Trim();

        public static void Tell(Player p, string text)
        {
            text = text.Trim();
            p.Message(MessageHud.MessageType.Center, text);
            Plugin.Log.LogInfo($"Use: {text.Replace("\n", " / ")}");
        }

        /// <summary>The local player's use of one of our items; false when it is handled here (not equipped or eaten as Valheim would).</summary>
        static bool UseItem_Prefix(Humanoid __instance, Inventory inventory, ItemDrop.ItemData item, ref bool __state)
        {
            __state = item != null && item.m_equipped;
            if (!(__instance is Player p) || p != Me || item?.m_dropPrefab == null || Britannia.Active == null) return true;
            var def = UltimaItems.Map.Get(item.m_dropPrefab.name);
            if (def == null) return true;
            inventory = inventory ?? p.GetInventory();
            switch (def.Name)
            {
                case "Gem": ViewGem(p, inventory, item); return false;
                case "Sextant": Sextant(p); return false;
                case "Pocket Watch": Watch(p); return false;
                case "Spyglass": Spyglass(p); return false;
                case "Magic Carpet": Tell(p, Say(ItemUse.SCarpet) + "\n" + MagicCarpet.Lay(p, inventory, item)); return false;
                case "Skull Key": Tell(p, Say(ItemUse.SSkullKey) + "\nIt opens a magically sealed door: press E at one."); return false;
                case "Key": Tell(p, "Jimmy a locked door: press E at it."); return false;
                default: return true;
            }
        }

        /// <summary>Putting on or taking off the badge, the crown, the amulet; taking up the sceptre.</summary>
        static void UseItem_Postfix(Humanoid __instance, ItemDrop.ItemData item, bool __state)
        {
            if (!(__instance is Player p) || p != Me || item?.m_dropPrefab == null || Britannia.Active == null || item.m_equipped == __state) return;
            var def = UltimaItems.Map.Get(item.m_dropPrefab.name);
            if (def == null) return;
            bool on = item.m_equipped;
            switch (def.Name)
            {
                case "Black Badge": Tell(p, Say(ItemUse.SBadge) + "\n" + Say(on ? ItemUse.SBadgeWorn : ItemUse.SRemoved)); break;
                case "Crown of Lord British": Tell(p, Say(ItemUse.SCrown) + "\n" + (on ? Say(ItemUse.SDonCrown) + " " + Say(ItemUse.SOfLordBritish) : Say(ItemUse.SRemoved))); break;
                case "Amulet of Lord British": Tell(p, Say(ItemUse.SAmulet) + "\n" + (on ? Say(ItemUse.SWearingAmulet) + " " + Say(ItemUse.SOfLordBritish) : Say(ItemUse.SRemoved))); break;
                case "Sceptre of Lord British": if (on) Sceptre(p); break;
            }
        }

        // ── where the player is ─────────────────────────────────────────────────────────────

        /// <summary>Within a settlement's square (the original's towns, castles and keeps).</summary>
        public static bool InSettlement(Vector3 at)
            => Settlements.All.Any(b => Mathf.Max(Mathf.Abs(at.x - b.Origin.x), Mathf.Abs(at.z - b.Origin.z)) < b.Site.Half + b.Site.Blend);

        /// <summary>Below ground: Valheim builds dungeons and interiors far above the land.</summary>
        public static bool Underground(Vector3 at) => at.y > 1000f;

        /// <summary>The overworld tile (0..255) under a position.</summary>
        public static (int x, int y) Tile(Vector3 at)
        {
            Britannia.Active.ToTile(at.x, at.z, out double u, out double v);
            return ((int)System.Math.Floor(u) & 0xff, (int)System.Math.Floor(v) & 0xff);
        }

        /// <summary>Britannia's clock (Calendar: one Valheim day is one Ultima day).</summary>
        public static (int hour, int minute) Clock() => (Calendar.Hour, Calendar.Minute);

        // ── the items ───────────────────────────────────────────────────────────────────────

        /// <summary>View a gem: one gem is spent and the map shows (the View command).</summary>
        static void ViewGem(Player p, Inventory inv, ItemDrop.ItemData gem)
        {
            if (Minimap.instance == null) return;
            inv.RemoveItem(gem, 1);
            Tell(p, Say(ItemUse.SViewGem));
            InventoryGui.instance?.Hide();
            viewingGem = true;
            try { Minimap.instance.SetMapMode(Minimap.MapMode.Large); }
            finally { viewingGem = false; }
        }

        /// <summary>In Britannia the map opens only through a gem.</summary>
        static bool SetMapMode_Prefix(Minimap.MapMode mode)
        {
            if (Britannia.Active == null || mode != Minimap.MapMode.Large || viewingGem) return true;
            // (M by a burning fire mixes spells instead: Spells)
            if (Me != null && ZInput.GetButtonDown("Map") && Spells.NearFire(Me.transform.position)) return false;
            Me?.Message(MessageHud.MessageType.Center, "Thou canst view a map only through a gem (right-click one).");
            return false;
        }

        static void Sextant(Player p)
        {
            var at = p.transform.position;
            var (hour, _) = Clock();
            var (x, y) = Tile(at);
            Tell(p, Say(ItemUse.SSextant) + "\n" + ItemUse.Sextant(Ovl, !InSettlement(at) && !Underground(at), hour, x, y));
        }

        static void Watch(Player p)
        {
            var (hour, minute) = Clock();
            Tell(p, ItemUse.Watch(Ovl, hour, minute).Replace("\n", " "));
        }

        static void Spyglass(Player p)
        {
            var (hour, _) = Clock();
            if (Underground(p.transform.position)) { Tell(p, Say(ItemUse.SSpyglass) + "\n" + Say(ItemUse.SNotHere)); return; }
            if (!ItemUse.Night(hour)) { Tell(p, Say(ItemUse.SSpyglass) + "\n" + Say(ItemUse.SNoStars)); return; }
            var (year, month, day) = Date();
            var stars = ItemUse.NightSky(Ovl, year, month, day, ShadowlordTowns());
            Plugin.Log.LogInfo($"Spyglass: the sky of {day}-{month}-{year}: {string.Join("; ", stars)}");
            InventoryGui.instance?.Hide();
            SkyPanel.Show(stars, Say(ItemUse.SLooking) + " " + Say(ItemUse.SNightSky));
        }

        /// <summary>The sceptre (use_item_command 0x14): every magical field within a tile dissolves; with none, "No effect!".</summary>
        static void Sceptre(Player p)
        {
            int dissolved = 0;
            float reach = Plugin.TileMetres.Value * 1.5f;
            foreach (var nview in Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None))
            {
                if (nview == null || nview.GetZDO() == null || !nview.gameObject.name.StartsWith(FieldPrefix)) continue;
                if (Vector3.Distance(nview.transform.position, p.transform.position) > reach) continue;
                nview.ClaimOwnership();
                ZNetScene.instance.Destroy(nview.gameObject);
                dissolved++;
            }
            Tell(p, Say(ItemUse.SSceptre) + "\n" + Say(ItemUse.SWieldingSceptre) + " " + Say(ItemUse.SOfLordBritish) + "\n" + Say(dissolved > 0 ? ItemUse.SFieldDissolved : ItemUse.SNoEffect));
        }

        /// <summary>Magical fields (poison, sleep, fire, energy) are prefabs whose names begin so; the sceptre dissolves them.</summary>
        public const string FieldPrefix = "u5_field";
    }

    /// <summary>
    /// The spyglass's night sky (LOOKOBJ look_telescope): 80 random stars, the eight town-stars
    /// where their orbits put them on this date, a crescent beside the town-star of each town a
    /// Shadowlord haunts; drawn pixel for pixel as the original draws it (its 192-pixel view,
    /// scaled up), until a key or a click.
    /// </summary>
    public static class SkyPanel
    {
        const int Size = 192, Scale = 3;
        static GameObject panel;
        static RawImage image;
        static Text caption;
        static readonly System.Random random = new System.Random();

        public static bool IsOpen => panel != null && panel.activeSelf;

        public static void Show(System.Collections.Generic.List<ItemUse.Star> stars, string words)
        {
            if (!Build()) return;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[Size * Size];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, 255);
            void Plot(int y, int x, Color32 c) { if (x >= 0 && x < Size && y >= 0 && y < Size) px[(Size - 1 - y) * Size + x] = c; }
            var star = new Color32(170, 170, 200, 255);
            var planet = new Color32(255, 255, 255, 255);
            var lord = new Color32(220, 40, 40, 255);
            for (int n = 0; n < 80; n++) Plot(random.Next(9, 0xad), random.Next(9, 0xb7), star);
            foreach (var s in stars)
            {
                // draw_sky_planet(row, col)
                int y0 = s.Row * 8, x0 = s.Column * 8;
                Plot(y0 + 8, x0 + 6, planet);
                for (int dy = 0; dy < 3; dy++)
                    for (int dx = 0; dx < 3; dx++)
                        if (x0 + dx < 0xb1) Plot(y0 + dy + 7, x0 + dx + 7, planet);
                if (x0 < 0xae) Plot(y0 + 8, x0 + 10, planet);
                if (!s.Shadowlord) continue;
                // draw_sky_crescent(row, col − 1): eight short lines in an arc
                int x = (s.Column - 1) * 8, y = y0;
                void Line(int ya, int yb, int lx) { for (int ly = Mathf.Min(ya, yb); ly <= Mathf.Max(ya, yb); ly++) Plot(ly, lx, lord); }
                if (2 < x) { Line(y + 12, y + 10, x + 5); Line(y + 12, y + 10, x + 6); Line(y + 12, y + 8, x + 7); }
                if (x < 0xb0) Line(y + 12, y + 8, x + 8);
                if (x < 0xaf) Line(y + 10, y + 6, x + 9);
                if (x < 0xae) Line(y + 10, y + 6, x + 10);
                if (x < 0xad) Line(y + 8, y + 5, x + 11);
                if (x < 0xac) Line(y + 7, y + 5, x + 12);
            }
            tex.SetPixels32(px);
            tex.Apply(false);
            if (image.texture != null) Object.Destroy(image.texture);
            image.texture = tex;
            caption.text = words;
            panel.SetActive(true);
            GUIManager.BlockInput(true);
        }

        public static void Close()
        {
            if (!IsOpen) return;
            panel.SetActive(false);
            GUIManager.BlockInput(false);
        }

        static bool Build()
        {
            if (panel != null) return true;
            var gui = GUIManager.CustomGUIFront;
            if (gui == null) return false;
            var g = GUIManager.Instance;
            int side = Size * Scale;
            panel = g.CreateWoodpanel(gui.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, side + 60, side + 110, false);
            caption = g.CreateText("", panel.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -30), g.AveriaSerifBold, 22, g.ValheimOrange, true, Color.black, side, 40, false).GetComponent<Text>();
            caption.alignment = TextAnchor.MiddleCenter;
            var go = new GameObject("sky", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(panel.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(side, side);
            rt.anchoredPosition = new Vector2(0, -20);
            image = go.GetComponent<RawImage>();
            panel.AddComponent<Watch>();
            panel.SetActive(false);
            return true;
        }

        /// <summary>Escape, E, Space, Enter or a click closes the sky.</summary>
        class Watch : MonoBehaviour
        {
            float openedAt;
            void OnEnable() => openedAt = Time.time;
            void Update()
            {
                if (Time.time - openedAt < 0.3f) return;
                if (ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetKeyDown(KeyCode.E) || ZInput.GetKeyDown(KeyCode.Space) || ZInput.GetKeyDown(KeyCode.Return)
                    || ZInput.GetMouseButtonDown(0) || ZInput.GetMouseButtonDown(1) || Player.m_localPlayer == null || Player.m_localPlayer.IsDead()) Close();
            }
        }
    }
}
