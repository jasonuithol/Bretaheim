using HarmonyLib;
using Bretaheim.Data;
using UnityEngine;

namespace Bretaheim.WorldGen
{
    /// <summary>
    /// Barley rations (Data/Rations): the local character eats one barley from their inventory at
    /// 6, 12 and 18 o'clock (Britannia's calendar); with none, each hour is "Starving!" (1..8 hit points). Valheim's food does nothing in
    /// Britannia (hit points are the level's, AvatarStats): eating it is refused.
    /// </summary>
    public class RationsWatch : MonoBehaviour
    {
        static readonly System.Random random = new System.Random();
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
            if (showMessages) __instance.Message(MessageHud.MessageType.Center, "Such food is not eaten in Britannia: thou eatest a ration of barley at six, noon and six.");
            return false;
        }

        void Update()
        {
            if (Time.time < checkAt) return;
            checkAt = Time.time + 2f;
            var me = Player.m_localPlayer;
            if (me == null || me.IsDead() || Britannia.Active == null || !Calendar.Running) return;
            if (Intro.Showing || me.IsTeleporting()) return;
            int hour = Calendar.Hour;
            if (!Rations.NewHour(me.m_customData, Calendar.WorldDay * 24 + hour)) return;
            var inv = me.GetInventory();
            var barley = ObjectDB.instance?.GetItemPrefab(Rations.Item)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name;
            int left = barley != null ? inv.CountItems(barley) : 0;
            if (left == 0)
            {
                int hp = Rations.StarvingDamage(random);
                me.Message(MessageHud.MessageType.Center, Plugin.U5.Ovl.CStr(Dg.StrStarving).Trim());
                // (Ultima hit points on the creatures' scale; it can kill, as starving did)
                me.SetHealth(me.GetHealth() - hp * Ultima.Scale);
                Plugin.Log.LogInfo($"{me.GetPlayerName()} is starving: {hp} hit points, health {me.GetHealth():0}");
                return;
            }
            if (!Rations.Mealtime(hour)) return;
            inv.RemoveItem(barley, 1);
            me.Message(MessageHud.MessageType.TopLeft, $"Thou eatest a ration of barley ({left - 1} left).");
            Plugin.Log.LogInfo($"{me.GetPlayerName()} ate a ration ({left - 1} left)");
        }
    }
}
