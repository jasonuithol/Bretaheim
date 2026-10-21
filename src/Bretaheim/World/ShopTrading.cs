using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Bretaheim.Data;
using UnityEngine;

namespace Bretaheim.WorldGen
{
    /// <summary>
    /// Buying from Britannia's shops through Valheim's trade window (StoreGui, Haldor's): the
    /// shop's Trader is never started, so its buy/sell reactions are ours (the keeper's thanks
    /// from SHOPPE.DAT over their head, the extra pieces of an armour set), and the window's
    /// "sell valuables" stays off (Ultima's shops buy arms, not amber). Selling to an armourer
    /// is the same window turned round: a second Trader lists the arms the player carries at
    /// the armourer's offers, and its Buy button (labelled Sell) sells them.
    /// </summary>
    public static class ShopTrading
    {
        /// <summary>Marks a shop's Trader and says which shop it is.</summary>
        public class Visit : MonoBehaviour
        {
            public ShopInfo Shop;
        }

        /// <summary>Marks an armourer's "sell" Trader: which carried item each listed entry is.</summary>
        public class SellVisit : MonoBehaviour
        {
            public ShopInfo Shop;
            public readonly Dictionary<Trader.TradeItem, ItemDrop.ItemData> Items = new Dictionary<Trader.TradeItem, ItemDrop.ItemData>();
        }

        static string buyLabel;
        static readonly System.Random random = new System.Random();

        public static ItemMap Items => UltimaItems.Map;

        public static void Install(Harmony harmony)
        {
            Prefix(harmony, typeof(Trader), "OnBought", nameof(OnBought_Prefix));
            Prefix(harmony, typeof(Trader), "OnSold", nameof(OnSold_Prefix));
            Prefix(harmony, typeof(StoreGui), "GetSellableItem", nameof(GetSellableItem_Prefix));
            Prefix(harmony, typeof(StoreGui), "BuySelectedItem", nameof(BuySelectedItem_Prefix));
            Prefix(harmony, typeof(StoreGui), "CanAfford", nameof(CanAfford_Prefix));
            var show = AccessTools.Method(typeof(StoreGui), "Show");
            if (show != null) harmony.Patch(show, postfix: new HarmonyMethod(typeof(ShopTrading), nameof(Show_Postfix)));
        }

        static void Prefix(Harmony h, Type t, string m, string patch)
        {
            var method = AccessTools.Method(t, m);
            if (method == null) { Plugin.Log.LogWarning($"Shops: {t.Name}.{m} not found"); return; }
            h.Patch(method, prefix: new HarmonyMethod(typeof(ShopTrading), patch));
        }

        static bool OnBought_Prefix(Trader __instance, Trader.TradeItem item)
        {
            var visit = __instance.GetComponent<Visit>();
            if (visit == null) return true;
            // an armour set comes whole (items.json "extra")
            var me = Player.m_localPlayer;
            foreach (var ware in Plugin.U5.Shops.Stock(visit.Shop))
            {
                var map = Items.Of(ware.Item);
                if (map == null || map.Value.prefab != item.m_prefab.name) continue;
                foreach (var extra in map.Value.extra)
                {
                    var prefab = ObjectDB.instance.GetItemPrefab(extra);
                    if (prefab != null && me != null && !me.GetInventory().AddItem(prefab, 1))
                        Treasure.DropOnGround(prefab, 1, me.transform.position + me.transform.forward);
                }
                break;
            }
            var thanks = Plugin.U5.Shops.Thanks(visit.Shop, TownNpc.Hour());
            var keeper = __instance.transform.parent != null ? __instance.transform.parent.gameObject : __instance.gameObject;
            if (thanks.Count > 0 && Chat.instance != null)
                Chat.instance.SetNpcText(keeper, Vector3.up * 2.1f, 20f, 6f, "", thanks[random.Next(thanks.Count)], false);
            Shadowlords.Cheat(me, visit.Shop.Location.Number);
            return false;
        }

        static Trader Open => StoreGui.instance != null ? Traverse.Create(StoreGui.instance).Field("m_trader").GetValue<Trader>() : null;

        /// <summary>
        /// The armourer's offers for what the player carries (arms and armour not worn or held;
        /// no ammunition), in the trade window's terms; false when there is nothing to sell.
        /// </summary>
        public static bool FillSell(Trader trader, ShopInfo shop, int intelligence)
        {
            var visit = trader.GetComponent<SellVisit>() ?? trader.gameObject.AddComponent<SellVisit>();
            visit.Shop = shop;
            visit.Items.Clear();
            trader.m_items = new List<Trader.TradeItem>();
            var me = Player.m_localPlayer;
            if (me == null) return false;
            foreach (var item in me.GetInventory().GetAllItems())
            {
                if (item.m_equipped || item.m_dropPrefab == null) continue;
                int id = Items.EquipmentOf(item.m_dropPrefab.name);
                int offer = id < 0 || Shops.IsAmmo(id) ? 0 : Plugin.U5.Shops.SellOffer(id, intelligence);
                var drop = item.m_dropPrefab.GetComponent<ItemDrop>();
                if (offer == 0 || drop == null) continue;
                var entry = new Trader.TradeItem
                {
                    m_prefab = drop, m_stack = 1, m_price = offer, m_name = item.m_shared.m_name,
                    m_tooltip = "", m_requiredGlobalKey = "", m_buyKey = "", m_incrementKey = "", m_buyPlayerEffects = new EffectList(),
                };
                trader.m_items.Add(entry);
                visit.Items[entry] = item;
            }
            return trader.m_items.Count > 0;
        }

        /// <summary>Selling: the item goes, the coins come, the armourer's offer and "Done!" over their head.</summary>
        static bool BuySelectedItem_Prefix(StoreGui __instance)
        {
            var trader = Open;
            var visit = trader != null ? trader.GetComponent<SellVisit>() : null;
            if (visit == null) return true;
            var selected = Traverse.Create(__instance).Field("m_selectedItem").GetValue<Trader.TradeItem>();
            var me = Player.m_localPlayer;
            if (selected == null || me == null || !visit.Items.TryGetValue(selected, out var item) || !me.GetInventory().ContainsItem(item)) return false;
            // (one of a stack at a time, at the offer for one)
            me.GetInventory().RemoveItem(item, 1);
            var coins = ObjectDB.instance.GetItemPrefab("Coins");
            if (coins != null) me.GetInventory().AddItem(coins, selected.m_price);
            int hour = TownNpc.Hour();
            int id = Items.EquipmentOf(item.m_dropPrefab.name);
            var shops = Plugin.U5.Shops;
            string said = shops.Offer(visit.Shop, id, selected.m_price, hour, random).Replace(" Deal?", "").Trim('"') + " " + shops.Sold(visit.Shop, hour).Split('\n')[0].Trim('"');
            var keeper = trader.transform.parent != null ? trader.transform.parent.gameObject : trader.gameObject;
            Chat.instance?.SetNpcText(keeper, Vector3.up * 2.1f, 20f, 8f, "", said, false);
            me.Message(MessageHud.MessageType.TopLeft, $"{Localization.instance.Localize(item.m_shared.m_name)}: +{selected.m_price} gold");
            if (!FillSell(trader, visit.Shop, TalkPanel.Intelligence)) { __instance.Hide(); return false; }
            Traverse.Create(__instance).Method("FillList").GetValue();
            return false;
        }

        /// <summary>Selling costs nothing: every offer can be taken.</summary>
        static bool CanAfford_Prefix(ref bool __result)
        {
            var trader = Open;
            if (trader == null || trader.GetComponent<SellVisit>() == null) return true;
            __result = true;
            return false;
        }

        /// <summary>Our shops: the Buy button says Sell when selling, and Valheim's sell-valuables button is hidden.</summary>
        static void Show_Postfix(StoreGui __instance, Trader trader)
        {
            if (trader == null || __instance.m_buyButton == null) return;
            var label = __instance.m_buyButton.GetComponentInChildren<TMPro.TMP_Text>(true);
            if (label != null)
            {
                buyLabel = buyLabel ?? label.text;
                label.text = trader.GetComponent<SellVisit>() != null ? "Sell" : buyLabel;
            }
            bool ours = trader.GetComponent<Visit>() != null || trader.GetComponent<SellVisit>() != null;
            if (__instance.m_sellButton != null) __instance.m_sellButton.gameObject.SetActive(!ours);
        }

        /// <summary>Whether the trade window is open on this shop.</summary>
        public static bool IsTrading(Trader trader)
            => trader != null && StoreGui.IsVisible() && Traverse.Create(StoreGui.instance).Field("m_trader").GetValue<Trader>() == trader;

        static bool OnSold_Prefix(Trader __instance) => __instance.GetComponent<Visit>() == null;

        static bool GetSellableItem_Prefix(ref ItemDrop.ItemData __result)
        {
            var trader = Open;
            if (trader == null || (trader.GetComponent<Visit>() == null && trader.GetComponent<SellVisit>() == null)) return true;
            __result = null;
            return false;
        }
    }
}
