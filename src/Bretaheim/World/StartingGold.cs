using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Bretaheim.Data;
using UnityEngine;

namespace Bretaheim.WorldGen
{
    /// <summary>
    /// A new Avatar arrives in Britannia with what INIT.GAM gives the original's party: the
    /// Avatar's own arms and armour, worn (party record 0's equipment: Chain Coif, Chain Mail,
    /// Long Sword and the Ankh), the party's mixed spells, scrolls and potions (not the spare arms
    /// and armour its companions used: there are none), its
    /// reagents, keys, gems and torches, 150 gold (Valheim coins) and its food as barley rations
    /// (63): once per character per Britannia world, remembered in the character's custom data.
    /// The spells and reagents land in the pouch (<see cref="PouchPanel"/> takes them from the
    /// inventory's AddItem).
    /// </summary>
    public static class StartingGold
    {
        public const int Amount = 150;

        public static void Install(Harmony harmony)
        {
            var spawned = AccessTools.Method(typeof(Player), "OnSpawned");
            if (spawned == null) { Plugin.Log.LogWarning("Starting gold: Player.OnSpawned not found"); return; }
            harmony.Patch(spawned, postfix: new HarmonyMethod(typeof(StartingGold), nameof(OnSpawned_Postfix)));
        }

        static void OnSpawned_Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer || Britannia.Active == null || ZNet.instance == null) return;
            string key = $"u5_start_gold_{ZNet.instance.GetWorldUID()}";
            if (__instance.m_customData.ContainsKey(key)) return;
            var coins = ObjectDB.instance?.GetItemPrefab("Coins");
            if (coins == null) return;
            var start = Plugin.U5.InitialGame;
            var inv = __instance.GetInventory();
            inv.AddItem(coins, start?.Gold > 0 ? start.Gold : Amount);
            int food = start?.Food ?? 0;
            var barley = ObjectDB.instance.GetItemPrefab(Data.Rations.Item);
            var drop = barley?.GetComponent<ItemDrop>();
            if (drop != null)
                for (int left = food, stack = Mathf.Max(1, drop.m_itemData.m_shared.m_maxStackSize); left > 0; left -= stack)
                    inv.AddItem(barley, Mathf.Min(stack, left));
            if (start != null) Kit(__instance, start);            __instance.m_customData[key] = "1";
            __instance.Message(MessageHud.MessageType.TopLeft, $"{start?.Gold ?? Amount} gold, {food} rations, the Avatar's arms and the party's goods");
            Plugin.Log.LogInfo($"Starting kit: {start?.Gold ?? Amount} coins and {food} rations for {__instance.GetPlayerName()}");
        }

        /// <summary>The Avatar's equipment (worn) and the party's goods, from INIT.GAM.</summary>
        static void Kit(Player p, SaveGame start)
        {
            var inv = p.GetInventory();
            var at = p.transform.position;
            var worn = start.Character(0).Equipment.Where(id => id != 0xff).Select(id => (int)id).ToList();
            foreach (int id in worn) Treasure.Give(inv, new Loot(LootKind.Item, id), at);
            // the party's mixed spells, scrolls and potions (its spare arms and armour were the companions': none here)
            for (int id = ItemMap.SpellBase; id < SaveGame.ItemCount; id++)
                if (start.ItemQuantity(id) > 0) Treasure.Give(inv, new Loot(LootKind.Item, id, start.ItemQuantity(id)), at);
            for (int r = 0; r < SaveGame.ReagentCount; r++)
            {
                var def = UltimaItems.Map.Reagent(r);
                var prefab = def != null ? ObjectDB.instance.GetItemPrefab(def.Prefab) : null;
                if (prefab != null && start.Reagent(r) > 0) inv.AddItem(prefab, start.Reagent(r));
            }
            if (start.Keys > 0) Treasure.Give(inv, new Loot(LootKind.Keys, 0, start.Keys), at);
            if (start.Gems > 0) Treasure.Give(inv, new Loot(LootKind.Gems, 0, start.Gems), at);
            if (start.Torches > 0) Treasure.Give(inv, new Loot(LootKind.Torches, 0, start.Torches), at);
            // worn: each worn piece (an armour with its leggings)
            var wear = new List<string>();
            foreach (int id in worn)
            {
                var def = UltimaItems.Map.Inventory(id);
                if (def == null) continue;
                wear.Add(def.Prefab);
                wear.AddRange(def.Extra);
            }
            foreach (var prefab in wear)
            {
                var item = inv.GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == prefab && !i.m_equipped);
                if (item != null) p.EquipItem(item, false);
            }
            Plugin.Log.LogInfo($"Starting kit: wearing {string.Join(", ", wear)}");
        }
    }
}
