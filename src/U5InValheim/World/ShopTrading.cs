using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using U5InValheim.Data;
using UnityEngine;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// Buying from Britannia's shops through Valheim's trade window (StoreGui, Haldor's): the
    /// shop's Trader is never started, so its buy/sell reactions are ours (the keeper's thanks
    /// from SHOPPE.DAT over their head, the extra pieces of an armour set), and the window's
    /// "sell valuables" stays off (Ultima's shops buy arms, not amber; selling comes later).
    /// </summary>
    public static class ShopTrading
    {
        /// <summary>Marks a shop's Trader and says which shop it is.</summary>
        public class Visit : MonoBehaviour
        {
            public ShopInfo Shop;
        }

        static ItemMap items;
        static readonly System.Random random = new System.Random();

        public static ItemMap Items
        {
            get
            {
                if (items == null)
                {
                    try { items = ItemMap.Parse(File.ReadAllText(Path.Combine(Plugin.PluginDirectory, "mappings", "items.json"))); }
                    catch (Exception e) { Plugin.Log.LogError($"items.json: {e.Message}"); items = ItemMap.Parse("{\"equipment\":[],\"reagents\":[],\"special\":[]}"); }
                }
                return items;
            }
        }

        public static void Install(Harmony harmony)
        {
            Prefix(harmony, typeof(Trader), "OnBought", nameof(OnBought_Prefix));
            Prefix(harmony, typeof(Trader), "OnSold", nameof(OnSold_Prefix));
            Prefix(harmony, typeof(StoreGui), "GetSellableItem", nameof(GetSellableItem_Prefix));
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
                        ItemDrop.DropItem(prefab.GetComponent<ItemDrop>().m_itemData, 1, me.transform.position + me.transform.forward, Quaternion.identity);
                }
                break;
            }
            var thanks = Plugin.U5.Shops.Thanks(visit.Shop, TownNpc.Hour());
            var keeper = __instance.transform.parent != null ? __instance.transform.parent.gameObject : __instance.gameObject;
            if (thanks.Count > 0 && Chat.instance != null)
                Chat.instance.SetNpcText(keeper, Vector3.up * 2.1f, 20f, 6f, "", thanks[random.Next(thanks.Count)], false);
            return false;
        }

        /// <summary>Whether the trade window is open on this shop.</summary>
        public static bool IsTrading(Trader trader)
            => trader != null && StoreGui.IsVisible() && Traverse.Create(StoreGui.instance).Field("m_trader").GetValue<Trader>() == trader;

        static bool OnSold_Prefix(Trader __instance) => __instance.GetComponent<Visit>() == null;

        static bool GetSellableItem_Prefix(ref ItemDrop.ItemData __result)
        {
            var trader = Traverse.Create(StoreGui.instance).Field("m_trader").GetValue<Trader>();
            if (trader == null || trader.GetComponent<Visit>() == null) return true;
            __result = null;
            return false;
        }
    }
}
