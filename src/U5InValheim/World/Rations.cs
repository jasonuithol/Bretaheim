using HarmonyLib;
using U5InValheim.Data;
using UnityEngine;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// Barley rations (Data/Rations): each new day the local character eats one barley from their
    /// inventory, or starves ("Starving!": 30 % of their health). Valheim's food does nothing in
    /// Britannia (hit points are the level's, AvatarStats): eating it is refused.
    /// </summary>
    public class RationsWatch : MonoBehaviour
    {
        float checkAt;

        public static void Install(Harmony harmony)
        {
            var canEat = AccessTools.Method(typeof(Player), nameof(Player.CanEat));
            if (canEat != null) harmony.Patch(canEat, prefix: new HarmonyMethod(typeof(RationsWatch), nameof(CanEat_Prefix)));
            else Plugin.Log.LogWarning("Rations: Player.CanEat not found");
        }

        /// <summary>Valheim's food is not eaten in Britannia (barley is carried, and eaten once a day).</summary>
        static bool CanEat_Prefix(Player __instance, ItemDrop.ItemData item, bool showMessages, ref bool __result)
        {
            if (Britannia.Active == null || item?.m_shared == null) return true;
            __result = false;
            if (showMessages) __instance.Message(MessageHud.MessageType.Center, "Such food is not eaten in Britannia: thou eatest a ration of barley each day.");
            return false;
        }

        void Update()
        {
            if (Time.time < checkAt) return;
            checkAt = Time.time + 2f;
            var me = Player.m_localPlayer;
            if (me == null || me.IsDead() || Britannia.Active == null || EnvMan.instance == null || ZNet.instance == null) return;
            if (Intro.Showing || me.IsTeleporting()) return;
            if (!Rations.Due(me.m_customData, EnvMan.instance.GetDay())) return;
            var inv = me.GetInventory();
            var barley = ObjectDB.instance?.GetItemPrefab(Rations.Item)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name;
            if (barley != null && inv.CountItems(barley) > 0)
            {
                inv.RemoveItem(barley, 1);
                me.Message(MessageHud.MessageType.TopLeft, $"Thou eatest a ration of barley ({inv.CountItems(barley)} left).");
                Plugin.Log.LogInfo($"{me.GetPlayerName()} ate a ration ({inv.CountItems(barley)} left)");
                return;
            }
            me.Message(MessageHud.MessageType.Center, Plugin.U5.Ovl.CStr(Dg.StrStarving).Trim());
            me.SetHealth(Rations.AfterStarving(me.GetHealth(), me.GetMaxHealth()));
            Plugin.Log.LogInfo($"{me.GetPlayerName()} is starving: health {me.GetHealth():0}");
        }
    }
}
