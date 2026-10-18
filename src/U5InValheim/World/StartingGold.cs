using System;
using HarmonyLib;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// A new Avatar arrives in Britannia with 150 gold (Valheim coins): once per character per
    /// Britannia world, remembered in the character's custom data.
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
            __instance.m_customData[key] = "1";
            __instance.Message(MessageHud.MessageType.TopLeft, $"{Amount} gold");
            Plugin.Log.LogInfo($"Starting gold: {Amount} coins for {__instance.GetPlayerName()}");
        }
    }
}
