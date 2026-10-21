using System.Collections.Generic;
using HarmonyLib;
using Bretaheim.Data;
using UnityEngine;

namespace Bretaheim.WorldGen
{
    /// <summary>
    /// Karma in Britannia (MASTER_PLAN §5.9): <see cref="Karma"/> over the character's custom data,
    /// changed where the original changes it and the deed exists here: striking a townsperson
    /// (once a day each; "Murdered!" when they lie asleep), opening a chest in a settlement, picking
    /// someone else's crops, paying a beggar (PlayerTalkWorld), the conversations' own karma
    /// (TALK 0x89/0x8a). A player's karma lives on their own machine: <see cref="Change"/> sends it there.
    /// </summary>
    public static class KarmaDeeds
    {
        const string KarmaRpc = "U5_Karma";
        /// <summary>The settlement whose crops are the players' own (Iolo's hut: its garden is theirs).</summary>
        public const int OwnGarden = 13;

        /// <summary>The townsfolk this player has struck today (one karma loss per person a day, as one fight in the original).</summary>
        static readonly HashSet<ZDOID> struck = new HashSet<ZDOID>();
        static int struckDay = -1;

        static bool Active => Britannia.Active != null;

        /// <summary>The local player's karma (0..99).</summary>
        public static int Mine => Player.m_localPlayer != null ? Karma.Get(Player.m_localPlayer.m_customData) : Karma.Start;

        public static void Install(Harmony harmony)
        {
            var start = AccessTools.Method(typeof(Game), "Start");
            if (start != null) harmony.Patch(start, postfix: new HarmonyMethod(typeof(KarmaDeeds), nameof(GameStart_Postfix)));
            var pick = AccessTools.Method(typeof(Pickable), nameof(Pickable.Interact));
            if (pick != null) harmony.Patch(pick, prefix: new HarmonyMethod(typeof(KarmaDeeds), nameof(PickableInteract_Prefix)));
            else Plugin.Log.LogWarning("Karma: Pickable.Interact not found");
        }

        static void GameStart_Postfix() => ZRoutedRpc.instance?.Register<int, string>(KarmaRpc, (sender, delta, why) => Apply(delta, why));

        /// <summary>
        /// A deed of <paramref name="player"/> worth <paramref name="delta"/> karma (the
        /// <see cref="Karma"/> constants), on whichever machine it is seen: applied on theirs.
        /// </summary>
        public static void Change(Player player, int delta, string why)
        {
            if (player == null || delta == 0 || !Active) return;
            if (player == Player.m_localPlayer) { Apply(delta, why); return; }
            long owner = player.GetZDOID().UserID;
            if (owner != 0) ZRoutedRpc.instance?.InvokeRoutedRPC(owner, KarmaRpc, delta, why ?? "");
        }

        static void Apply(int delta, string why)
        {
            var me = Player.m_localPlayer;
            if (me == null || !Active) return;
            int before = Karma.Get(me.m_customData), after = Karma.Add(me.m_customData, delta);
            Plugin.Log.LogInfo($"Karma {before} -> {after} ({why})");
            if (after != before) me.Message(MessageHud.MessageType.TopLeft, $"{(delta > 0 ? "+" : "")}{after - before} karma{(string.IsNullOrEmpty(why) ? "" : ": " + why)}");
        }

        /// <summary>The built settlement whose square a position is in, or null.</summary>
        public static Settlements.Built SettlementAt(Vector3 p)
        {
            foreach (var b in Settlements.All)
                if (Mathf.Abs(p.x - b.Origin.x) <= b.Site.Half && Mathf.Abs(p.z - b.Origin.z) <= b.Site.Half) return b;
            return null;
        }

        // ── the deeds ───────────────────────────────────────────────────────────────────────

        /// <summary>A player's blow on a townsperson (on the striker's machine): 5 the first time that day; 5 more when they were asleep in bed ("Murdered!").</summary>
        public static void Struck(Player p, TownNpc npc)
        {
            if (p != Player.m_localPlayer || npc == null || npc.IsDead) return;
            int day = EnvMan.instance != null ? EnvMan.instance.GetDay() : 0;
            if (day != struckDay) { struck.Clear(); struckDay = day; }
            var id = npc.GetComponent<ZNetView>()?.GetZDO()?.m_uid ?? ZDOID.None;
            if (!struck.Add(id)) return;
            Change(p, Karma.AttackTownsperson, $"thou hast attacked {npc.GetHoverName()}");
            if (npc.IsAsleep)
            {
                p.Message(MessageHud.MessageType.Center, Plugin.U5.Ovl.CStr(0x2718).Trim()); // "Murdered!"
                Change(p, Karma.Murder, "murder");
            }
        }

        /// <summary>A chest opened for the first time (Treasure): in a settlement, 2 karma (open_chest).</summary>
        public static void ChestOpened(Player p, Vector3 at)
        {
            var town = SettlementAt(at);
            if (town == null || !Karma.IsSettlement(town.Location.Number)) return;
            Change(p, Karma.OpenTownChest, $"a chest of {town.Location.Name}");
        }

        /// <summary>Picking crops in someone else's garden (get_item "Crops picked!"): 1 karma. Iolo's hut's garden is the players' own.</summary>
        static void PickableInteract_Prefix(Pickable __instance, Humanoid character, bool repeat)
        {
            if (!Active || repeat || !(character is Player p) || p != Player.m_localPlayer) return;
            var t = Traverse.Create(__instance);
            if (t.Field("m_picked").GetValue<bool>() || t.Field("m_pickedLocal").GetValue<bool>()) return;
            if (__instance.m_itemPrefab == null || __instance.m_itemPrefab.name != "Barley") return;
            var town = SettlementAt(__instance.transform.position);
            if (town == null || town.Location.Number == OwnGarden) return;
            Change(p, Karma.PickCrops, $"crops of {town.Location.Name}");
        }
    }
}
