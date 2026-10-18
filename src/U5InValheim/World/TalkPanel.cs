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
    /// Talking to townsfolk (MASTER_PLAN §5.3): E on a townsperson opens this panel (a log of the
    /// conversation, a line to type in, Name/Job/Bye) running the original conversation
    /// (<see cref="TalkSession"/>); what they say also shows as a speech bubble over them, the
    /// way Haldor and Hildir speak. Bye, Escape or walking away ends it.
    /// </summary>
    public static class TalkPanel
    {
        const int LogLines = 16;
        const float WalkAway = 6f;

        static GameObject panel;
        static Text title, log, prompt;
        static InputField field;
        static TownNpc npc;
        static TalkSession session;
        static readonly List<string> lines = new List<string>();
        static int openedFrame;

        public static bool IsOpen => panel != null && panel.activeSelf;
        public static TownNpc With => IsOpen ? npc : null;

        public static void Install(Harmony harmony)
        {
            // Escape closes the conversation, not the game menu behind it
            var update = AccessTools.Method(typeof(Menu), "Update");
            if (update != null) harmony.Patch(update, prefix: new HarmonyMethod(typeof(TalkPanel), nameof(MenuUpdate_Prefix)));
        }

        static bool MenuUpdate_Prefix() => !IsOpen;

        public static void Open(TownNpc who)
        {
            if (!Start(who, who.GetHoverName())) return;
            session = new TalkSession(who.Script, Plugin.U5.Ovl, new PlayerTalkWorld(who));
            Show(session.TakeOutput());
            SetButtons(("Name", () => Say("name")), ("Job", () => Say("job")),
                ("Bye", () => { if (session != null && !session.Over && !session.Prompt.StartsWith("You respond")) Say(TalkSession.Bye); else Close(); }));
            Focus();
            Refresh();
        }

        /// <summary>A shop visit (SHOPPES*.OVL): the keeper's greeting, then Buy (Valheim's trade window with the shop's stock), the healer's services, Bye (their farewell).</summary>
        public static void OpenShop(TownNpc who, ShopInfo shop)
        {
            if (!Start(who, shop.Name)) return;
            session = null;
            var shops = Plugin.U5.Shops;
            bought = false;
            Show(shops.Greeting(shop, TownNpc.Hour(), random));
            visit = shop;
            ShopButtons();
            if (who.ShopTrader() == null && shop.Trade != ShopTrade.Healer) Show("(This shop opens in a later version.)");
            Refresh();
        }

        /// <summary>The shop's choices: Buy, Sell (armourers), the healer's services, Bye.</summary>
        static void ShopButtons()
        {
            var who = npc;
            var shop = visit;
            var shops = Plugin.U5.Shops;
            var choices = new List<(string, Action)>();
            if (who.ShopTrader() != null) choices.Add(("Buy", () => { var trader = who.ShopTrader(); Close(farewell: false, keepTalking: true); StoreGui.instance?.Show(trader); }));
            if (shop.Trade == ShopTrade.Healer)
            {
                var (heal, cure) = shops.HealerPrices(shop);
                choices.Add(($"Heal ({heal} gp)", () => Heal(heal, false)));
                choices.Add(($"Cure ({cure} gp)", () => Heal(cure, true)));
            }
            if (shop.Trade == ShopTrade.Armoury) choices.Add(("Sell", Sell));
            choices.Add(("Bye", () => Close()));
            SetButtons(choices.ToArray());
        }

        // ── selling to an armourer ──────────────────────────────────────────────────────────

        public static int Intelligence => Player.m_localPlayer != null && Player.m_localPlayer.m_customData.TryGetValue("u5_int", out var v) && int.TryParse(v, out int i) ? i : Shops.DefaultIntelligence;

        /// <summary>"What dost thou wish to sell?" in the trade window (the arms carried, at the armourer's offers), or "Thou hast nothing to sell!".</summary>
        static void Sell()
        {
            var who = npc;
            var trader = who.SellTrader();
            if (!ShopTrading.FillSell(trader, visit, Intelligence))
            {
                Show(Plugin.U5.Shops.NoWares(visit, TownNpc.Hour()));
                return;
            }
            var prompt = LastQuote(Plugin.U5.Shops.SellPrompt(random));
            Close(farewell: false, keepTalking: true);
            if (prompt != null) Chat.instance?.SetNpcText(who.gameObject, Vector3.up * 2.1f, 20f, 6f, "", prompt, false);
            StoreGui.instance?.Show(trader);
        }

        static readonly System.Random random = new System.Random();
        static ShopInfo visit;
        static bool bought;

        /// <summary>The healer's cure (poison) or heal (to full health), paid in coins.</summary>
        static void Heal(int price, bool cure)
        {
            var me = Player.m_localPlayer;
            if (me == null) return;
            var inv = me.GetInventory();
            if (inv.CountItems("$item_coins") < price)
            {
                Show("\"" + Plugin.U5.Ovl.CStr(0x9328) + "\"");
                return;
            }
            if (price > 0) inv.RemoveItem("$item_coins", price);
            if (cure) me.GetSEMan().RemoveStatusEffect("Poison".GetStableHashCode(), true);
            else me.Heal(me.GetMaxHealth() - me.GetHealth(), true);
            bought = true;
            Show(cure ? "(Thou art cured.)" : "(Thou art healed.)");
        }

        static bool Start(TownNpc who, string heading)
        {
            if (IsOpen) Close(farewell: false);
            if (!Build()) return false;
            npc = who;
            visit = null;
            lines.Clear();
            title.text = heading;
            panel.SetActive(true);
            openedFrame = Time.frameCount;
            GUIManager.BlockInput(true);
            return true;
        }

        static void SetButtons(params (string text, Action click)[] choices)
        {
            foreach (var b in buttons) Object.Destroy(b);
            buttons.Clear();
            var g = GUIManager.Instance;
            float width = Math.Min(150, 620f / choices.Length - 10);
            for (int i = 0; i < choices.Length; i++)
            {
                var click = choices[i].click;
                var b = g.CreateButton(choices[i].text, panel.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2((i - (choices.Length - 1) / 2f) * (width + 10), 35), width, 36);
                b.GetComponent<Button>().onClick.AddListener(() => click());
                buttons.Add(b);
            }
        }

        static readonly List<GameObject> buttons = new List<GameObject>();

        public static void Close() => Close(farewell: true);

        static void Close(bool farewell, bool keepTalking = false)
        {
            if (panel == null || !panel.activeSelf) return;
            // leaving a shop: the keeper's farewell over their head
            if (farewell && visit != null && npc != null && Chat.instance != null)
            {
                var said = LastQuote(Plugin.U5.Shops.Farewell(visit, bought, TownNpc.Hour(), random));
                if (said != null) Chat.instance.SetNpcText(npc.gameObject, Vector3.up * 2.1f, 20f, 8f, "", said, false);
            }
            panel.SetActive(false);
            GUIManager.BlockInput(false);
            // (buying: they wait at the counter while the trade window is open)
            if (npc != null && !keepTalking) npc.EndTalk();
            npc = null;
            session = null;
            visit = null;
        }

        /// <summary>The player's line (typed, or a button).</summary>
        static void Say(string text)
        {
            if (session == null || session.Over) { Close(); return; }
            session.Respond(text);
            Show(session.TakeOutput());
            if (session.Over) { Close(); return; }
            field.text = "";
            Focus();
            Refresh();
        }

        static void Show(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            text = text.Replace("\r", "");
            while (text.Contains("\n\n\n")) text = text.Replace("\n\n\n", "\n\n");
            lines.AddRange(text.Split('\n'));
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0 && lines.Count > LogLines) lines.RemoveAt(lines.Count - 1);
            if (lines.Count > LogLines) lines.RemoveRange(0, lines.Count - LogLines);
            log.text = string.Join("\n", lines);
            // the newest words of the townsperson over their head
            var said = LastQuote(text);
            if (said != null && npc != null && Chat.instance != null)
                Chat.instance.SetNpcText(npc.gameObject, Vector3.up * 2.1f, 20f, 8f, "", said, false);
        }

        static string LastQuote(string text)
        {
            int end = text.LastIndexOf('"');
            if (end <= 0) return null;
            int start = text.LastIndexOf('"', end - 1);
            if (start < 0) return null;
            var s = text.Substring(start + 1, end - start - 1).Replace('\n', ' ').Trim();
            return s.Length == 0 ? null : s.Length > 160 ? s.Substring(0, 157) + "..." : s;
        }

        static void Refresh()
        {
            bool open = session != null && !session.Over;
            prompt.text = open ? session.Prompt.Replace("\n", " ").Replace(":", "").Trim() : "";
            field.gameObject.SetActive(open);
        }

        static void Focus()
        {
            if (!field.gameObject.activeInHierarchy) return;
            field.Select();
            field.ActivateInputField();
        }

        static bool Build()
        {
            if (panel != null) return true;
            var gui = GUIManager.CustomGUIFront;
            if (gui == null) return false;
            var g = GUIManager.Instance;
            panel = g.CreateWoodpanel(gui.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -60), 680, 520, false);
            title = g.CreateText("", panel.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -30), g.AveriaSerifBold, 26, g.ValheimOrange, true, Color.black, 620, 40, false).GetComponent<Text>();
            title.alignment = TextAnchor.MiddleCenter;
            log = g.CreateText("", panel.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -250), g.AveriaSerifBold, 17, Color.white, true, Color.black, 620, 380, false).GetComponent<Text>();
            log.alignment = TextAnchor.LowerLeft;
            log.verticalOverflow = VerticalWrapMode.Truncate;
            log.horizontalOverflow = HorizontalWrapMode.Wrap;
            prompt = g.CreateText("", panel.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(110, 85), g.AveriaSerifBold, 17, g.ValheimOrange, true, Color.black, 180, 30, false).GetComponent<Text>();
            prompt.alignment = TextAnchor.MiddleRight;
            var input = g.CreateInputField(panel.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(390, 85), InputField.ContentType.Standard, "type, then Enter", 18, 380, 34);
            field = input.GetComponent<InputField>();
            field.characterLimit = 30;
            field.onEndEdit.AddListener(text =>
            {
                if (ZInput.GetKeyDown(KeyCode.Return) || ZInput.GetKeyDown(KeyCode.KeypadEnter)) Say(text);
            });
            panel.AddComponent<TalkPanelWatch>();
            panel.SetActive(false);
            return true;
        }

        /// <summary>Escape, walking away or the townsperson going away ends the conversation.</summary>
        class TalkPanelWatch : MonoBehaviour
        {
            void Update()
            {
                if (ZInput.GetKeyDown(KeyCode.Escape)) { Close(); return; }
                // the E that opened the conversation is not the first letter typed
                if (Time.frameCount <= openedFrame + 2 && field.text.Length > 0) field.text = "";
                var player = Player.m_localPlayer;
                if (npc == null || player == null || player.IsDead() || Vector3.Distance(player.transform.position, npc.transform.position) > WalkAway) Close();
            }
        }
    }

    /// <summary>The conversation's view of the game: the local player (their name, met townsfolk and karma kept with the character, coins as gold).</summary>
    public class PlayerTalkWorld : ITalkWorld
    {
        const string KarmaKey = "u5_karma";
        readonly TownNpc npc;
        readonly System.Random random = new System.Random();

        public PlayerTalkWorld(TownNpc npc) { this.npc = npc; }

        static Player Me => Player.m_localPlayer;
        public string AvatarName => Me?.GetPlayerName() ?? "Avatar";
        public IEnumerable<string> PartyNames => Player.GetAllPlayers().Where(p => p != null && Vector3.Distance(p.transform.position, npc.transform.position) < 32f).Select(p => p.GetPlayerName());

        string MetKey => $"u5_met_{npc.Location}_{npc.Slot}";
        public bool Met
        {
            get => Me != null && Me.m_customData.ContainsKey(MetKey);
            set { if (Me != null && value) Me.m_customData[MetKey] = "1"; }
        }

        /// <summary>Karma (0..99), 50 for a new Avatar (until the virtue questions set it, Phase 5).</summary>
        public int Karma
        {
            get => Me != null && Me.m_customData.TryGetValue(KarmaKey, out var k) && int.TryParse(k, out int v) ? v : 50;
            set { if (Me != null) Me.m_customData[KarmaKey] = Mathf.Clamp(value, 0, 99).ToString(); }
        }

        public int Gold => Me?.GetInventory().CountItems("$item_coins") ?? 0;
        public void TakeGold(int amount) => Me?.GetInventory().RemoveItem("$item_coins", amount);

        public void Give(int item)
        {
            if (Me == null) return;
            // gold is coins; the other gifts arrive with the item set (Phase 5)
            if (item == 0x42)
            {
                var coins = ObjectDB.instance.GetItemPrefab("Coins");
                if (coins != null) Me.GetInventory().AddItem(coins, 1);
                return;
            }
            string name = item < 0x40 ? Plugin.U5.Ovl.InventoryName(item) : item.ToString("x2");
            Me.Message(MessageHud.MessageType.TopLeft, $"(Ultima gift not yet mapped: {name})");
        }

        /// <summary>A party is other players (MASTER_PLAN §1.6): townsfolk never join.</summary>
        public bool Join() => false;
        public void Alarm() => Me?.Message(MessageHud.MessageType.Center, "The guards are called!");
        public bool Coin() => random.Next(2) == 0;
    }
}
