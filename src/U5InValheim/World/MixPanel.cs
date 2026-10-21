using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Jotunn.Managers;
using U5InValheim.Data;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// Mixing spells (CMDS.OVL cmd_mix_reagents; Action mappings "Mix Spell"): M by a burning
    /// fire opens this panel, no kettle. "For what spell?": the circle's six spells (those whose
    /// reagents are carried shown so); choosing one marks its recipe (DATA.OVL) among the eight
    /// reagents, which may be changed; "How much?"; Mix spends one of each marked reagent per
    /// mixture and, when the marks are the spell's recipe, gives that many of the spell ("Done!",
    /// at most 99); any other mixture blows up as a trapped chest does (trap_effect).
    /// </summary>
    public static class MixPanel
    {
        static GameObject panel;
        static Text title, info, countText;
        static readonly List<GameObject> spellButtons = new List<GameObject>(), circleButtons = new List<GameObject>(), reagentButtons = new List<GameObject>();
        static int circle = 1, spell = -1, count = 1;
        static byte marks;
        static int openedFrame;

        public static bool IsOpen => panel != null && panel.activeSelf;

        public static void Install(Harmony harmony)
        {
            var update = AccessTools.Method(typeof(Menu), "Update");
            if (update != null) harmony.Patch(update, prefix: new HarmonyMethod(typeof(MixPanel), nameof(MenuUpdate_Prefix)));
        }

        /// <summary>Escape closes the panel, not the game menu behind it.</summary>
        static bool MenuUpdate_Prefix()
        {
            if (!IsOpen) return true;
            if (ZInput.GetKeyDown(KeyCode.Escape) && Time.frameCount != openedFrame) Close();
            return false;
        }

        static DataOvl Ovl => Plugin.U5.Ovl;
        static string S(int dg) => Ovl.CStr(dg).Replace("\n", " ").Trim();
        // DATA.OVL (DGROUP) CMDS mixing words
        const int SNoReagents = 0x8f98, SForWhat = 0x8fac, SMixing = 0x8ff0, SDone = 0x8ffc, SNothingToMix = 0x9004, SInsufficient = 0x8f7e, SHowMuch = 0x8f72;

        static int Held(Player me, int reagent)
        {
            var def = UltimaItems.Map.Reagent(reagent);
            var prefab = def != null ? ObjectDB.instance?.GetItemPrefab(def.Prefab) : null;
            var name = prefab?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name;
            return name != null ? me.GetInventory().CountItems(name) : 0;
        }

        public static void Open(Player me)
        {
            if (Enumerable.Range(0, Magic.Reagents).All(r => Held(me, r) == 0))
            {
                me.Message(MessageHud.MessageType.Center, S(SNoReagents));
                return;
            }
            if (!Build()) return;
            panel.SetActive(true);
            openedFrame = Time.frameCount;
            GUIManager.BlockInput(true);
            info.text = S(SForWhat);
            Refresh();
        }

        public static void Close()
        {
            if (!IsOpen) return;
            panel.SetActive(false);
            GUIManager.BlockInput(false);
        }

        static Player Me => Player.m_localPlayer;

        static void Refresh()
        {
            var me = Me;
            if (me == null || panel == null) return;
            var spells = Spells.Table;
            for (int i = 0; i < circleButtons.Count; i++)
                circleButtons[i].GetComponentInChildren<Text>().text = (i + 1 == circle ? "<color=orange>" : "") + Roman(i + 1) + (i + 1 == circle ? "</color>" : "");
            for (int i = 0; i < spellButtons.Count; i++)
            {
                var sp = spells[(circle - 1) * 6 + i];
                bool can = Magic.MostToMix(sp.Recipe, r => Held(me, r)) > 0;
                string name = sp.Words;
                if (sp.Index == spell) name = "<color=orange>" + name + "</color>";
                else if (!can) name = "<color=#9a9a9a>" + name + "</color>";
                spellButtons[i].GetComponentInChildren<Text>().text = name;
            }
            for (int r = 0; r < reagentButtons.Count; r++)
            {
                bool marked = (marks & (0x80 >> r)) != 0;
                reagentButtons[r].GetComponentInChildren<Text>().text = (marked ? "<color=orange>[x] " : "[  ] ") + Ovl.ReagentName(r) + " (" + Held(me, r) + ")" + (marked ? "</color>" : "");
            }
            int most = Magic.MostToMix(marks, r => Held(me, r));
            count = Mathf.Clamp(count, 1, Math.Max(1, most));
            countText.text = S(SHowMuch) + " " + count;
            title.text = spell >= 0 ? "Mix " + spells[spell].Words : "Mix";
        }

        static void ChooseSpell(int s)
        {
            spell = s;
            marks = Spells.Table[s].Recipe;
            count = 1;
            var held = Spells.Table[s].Reagents.Select(r => Ovl.ReagentName(r));
            info.text = Spells.Table[s].Words + ": " + string.Join(", ", held) + ". " + Mixed(s);
            Refresh();
        }

        static string Mixed(int s)
        {
            var me = Me;
            var def = UltimaItems.Map.Inventory(ItemMap.SpellBase + s);
            var prefab = def != null ? ObjectDB.instance?.GetItemPrefab(def.Prefab) : null;
            var name = prefab?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name;
            int n = me != null && name != null ? me.GetInventory().CountItems(name) : 0;
            return n > 0 ? $"(Thou hast {n} mixed.)" : "";
        }

        /// <summary>Mix: the marked reagents spent (one each per mixture), the spell gained or the mixture's blast.</summary>
        static void Mix()
        {
            var me = Me;
            if (me == null) return;
            if (spell < 0) { info.text = S(SForWhat); return; }
            if (marks == 0) { info.text = S(SNothingToMix); return; }
            int most = Magic.MostToMix(marks, r => Held(me, r));
            if (most < count) { info.text = S(SInsufficient); return; }
            if (!Spells.NearFire(me.transform.position)) { Close(); return; }
            foreach (int r in Magic.ReagentsOf(marks))
            {
                var def = UltimaItems.Map.Reagent(r);
                var name = ObjectDB.instance.GetItemPrefab(def.Prefab).GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
                me.GetInventory().RemoveItem(name, count);
            }
            var sp = Spells.Table[spell];
            if (!Magic.Mixes(sp, marks))
            {
                Plugin.Log.LogInfo($"{me.GetPlayerName()} mixes {sp.Words} wrongly (0x{marks:x2}, not 0x{sp.Recipe:x2})");
                Close();
                me.Message(MessageHud.MessageType.Center, S(SMixing));
                Treasure.Spring(me, me.transform.position, chest: false);
                return;
            }
            var spellDef = UltimaItems.Map.Inventory(ItemMap.SpellBase + spell);
            var prefab = spellDef != null ? ObjectDB.instance.GetItemPrefab(spellDef.Prefab) : null;
            if (prefab != null)
            {
                var shared = prefab.GetComponent<ItemDrop>().m_itemData.m_shared;
                int room = Math.Max(0, Magic.MostMixed - me.GetInventory().CountItems(shared.m_name));
                int n = Math.Min(count, room);
                if (n > 0 && !me.GetInventory().AddItem(prefab, n)) Treasure.DropOnGround(prefab, n, me.transform.position + Vector3.up);
            }
            SpellStatus.Fx("vfx_bowl_AddItem", me.transform.position, 3f);
            info.text = S(SMixing) + " " + S(SDone) + " " + Mixed(spell);
            Plugin.Log.LogInfo($"{me.GetPlayerName()} mixes {count} {sp.Words}");
            Refresh();
        }

        /// <summary>The panel's choices made by name (the console's u5 mix, for tests): the spell (0..47) with its recipe, how many, then Mix.</summary>
        public static bool Choose(int s, int n)
        {
            if (!IsOpen || s < 0 || s >= Spells.Table.Length) return false;
            ChooseSpell(s);
            count = Math.Max(1, n);
            Mix();
            return true;
        }

        static string Roman(int n) => new[] { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII" }[n];

        static bool Build()
        {
            if (panel != null) return true;
            var gui = GUIManager.CustomGUIFront;
            if (gui == null) return false;
            var g = GUIManager.Instance;
            panel = g.CreateWoodpanel(gui.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -40), 760, 560, false);
            title = g.CreateText("Mix", panel.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -30), g.AveriaSerifBold, 26, g.ValheimOrange, true, Color.black, 700, 40, false).GetComponent<Text>();
            title.alignment = TextAnchor.MiddleCenter;
            // the circles
            for (int c = 1; c <= 8; c++)
            {
                int cc = c;
                var b = g.CreateButton(Roman(c), panel.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2((c - 4.5f) * 82, -80), 76, 32);
                b.GetComponent<Button>().onClick.AddListener(() => { circle = cc; Refresh(); });
                circleButtons.Add(b);
            }
            // the circle's six spells
            for (int i = 0; i < 6; i++)
            {
                int ii = i;
                var b = g.CreateButton("", panel.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2((i % 3 - 1) * 232, -130 - (i / 3) * 42), 222, 36);
                b.GetComponent<Button>().onClick.AddListener(() => ChooseSpell((circle - 1) * 6 + ii));
                spellButtons.Add(b);
            }
            // the eight reagents (marked or not)
            for (int r = 0; r < Magic.Reagents; r++)
            {
                int rr = r;
                var b = g.CreateButton("", panel.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2((r % 2 - 0.5f) * 340, -240 - (r / 2) * 40), 330, 34);
                b.GetComponent<Button>().onClick.AddListener(() => { marks ^= (byte)(0x80 >> rr); Refresh(); });
                reagentButtons.Add(b);
            }
            info = g.CreateText("", panel.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 150), g.AveriaSerifBold, 17, Color.white, true, Color.black, 700, 60, false).GetComponent<Text>();
            info.alignment = TextAnchor.MiddleCenter;
            countText = g.CreateText("", panel.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-170, 90), g.AveriaSerifBold, 18, g.ValheimOrange, true, Color.black, 180, 34, false).GetComponent<Text>();
            countText.alignment = TextAnchor.MiddleRight;
            var less = g.CreateButton("−", panel.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-50, 90), 40, 34);
            less.GetComponent<Button>().onClick.AddListener(() => { count--; Refresh(); });
            var more = g.CreateButton("+", panel.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 90), 40, 34);
            more.GetComponent<Button>().onClick.AddListener(() => { count++; Refresh(); });
            var all = g.CreateButton("All", panel.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(60, 90), 60, 34);
            all.GetComponent<Button>().onClick.AddListener(() => { count = 99; Refresh(); });
            var mix = g.CreateButton("Mix", panel.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-90, 35), 160, 38);
            mix.GetComponent<Button>().onClick.AddListener(Mix);
            var close = g.CreateButton("Close", panel.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(90, 35), 160, 38);
            close.GetComponent<Button>().onClick.AddListener(Close);
            panel.SetActive(false);
            return true;
        }
    }
}
