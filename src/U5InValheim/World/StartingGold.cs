using System;
using HarmonyLib;
using UnityEngine;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// A new Avatar arrives in Britannia with 150 gold (Valheim coins) and INIT.GAM's food as
    /// barley rations (63): once per character per Britannia world, remembered in the
    /// character's custom data.
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
            __instance.GetInventory().AddItem(coins, Amount);
            int food = Plugin.U5.InitialGame?.Food ?? 0;
            var barley = ObjectDB.instance.GetItemPrefab(Data.Rations.Item);
            var drop = barley?.GetComponent<ItemDrop>();
            if (drop != null)
                for (int left = food, stack = Mathf.Max(1, drop.m_itemData.m_shared.m_maxStackSize); left > 0; left -= stack)
                    __instance.GetInventory().AddItem(barley, Mathf.Min(stack, left));
            __instance.m_customData[key] = "1";
            __instance.Message(MessageHud.MessageType.TopLeft, $"{Amount} gold, {food} rations");
            Plugin.Log.LogInfo($"Starting gold: {Amount} coins and {food} rations for {__instance.GetPlayerName()}");
        }
    }
}
