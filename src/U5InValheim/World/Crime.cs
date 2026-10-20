using System;
using System.Collections;
using HarmonyLib;
using U5InValheim.WorldGen.Core;
using UnityEngine;

namespace U5InValheim.WorldGen
{
    /// <summary>
    /// Crime and arrest (TOWN raise_alarm, guard_arrest). A crime (firing a town's cannon) makes
    /// the player wanted in that settlement: its guards come for them, and the first to reach
    /// them says "Thou art under arrest! Wilt thou come quietly?". Yes: "The guard strikes thee
    /// unconscious!", and the player awakens in the jail of Yew at 8 o'clock. No: "Then defend
    /// thyself, rogue!", and the guards fight them (they may be fought back). Leaving the
    /// settlement, or dying, ends it. Kept on the player's ZDO (the settlement's number), so every
    /// peer's guards know.
    /// </summary>
    public static class Crime
    {
        /// <summary>How close a guard comes to make the arrest.</summary>
        public const float Reach = 2.5f;
        /// <summary>How long the guards fight a player who would not come quietly before one appears beside them and takes them anyway.</summary>
        public const float FightFor = 60f;
        const string WantedKey = "u5_wanted", HostileKey = "u5_hostile", JailRpc = "U5_Jail";
        /// <summary>The jail of Yew (guard_arrest: location 4, x 25, y 4, level 0).</summary>
        public const int JailLocation = 4, JailX = 25, JailY = 4;
        // DATA.OVL (DGROUP) strings of guard_arrest
        const int SArrest = 0x27e2, SComeQuietly = 0x27fe, SStruck = 0x281b, SAwaken = 0x2845, SDefend = 0x285e;

        static int Get(Player p, string key) => p != null ? p.GetComponent<ZNetView>()?.GetZDO()?.GetInt(key) ?? 0 : 0;

        static void Set(Player p, string key, int value)
        {
            var zdo = p != null ? p.GetComponent<ZNetView>()?.GetZDO() : null;
            if (zdo == null || p != Player.m_localPlayer || zdo.GetInt(key) == value) return;
            zdo.Set(key, value);
        }

        /// <summary>The settlement whose guards want to arrest this player (0: none).</summary>
        public static int WantedBy(Player p) => Get(p, WantedKey);
        /// <summary>The settlement whose guards fight this player (0: none).</summary>
        public static int HostileTo(Player p) => Get(p, HostileKey);

        /// <summary>A crime in a settlement (on the criminal's machine): its guards come to arrest them.</summary>
        public static void Commit(Player p, Settlements.Built town)
        {
            if (p != Player.m_localPlayer || HostileTo(p) == town.Location.Number) return;
            Set(p, WantedKey, town.Location.Number);
            Plugin.Log.LogInfo($"{town.Location.Name}: {p.GetPlayerName()} is wanted");
        }

        static void Clear(Player p)
        {
            Set(p, WantedKey, 0);
            Set(p, HostileKey, 0);
        }

        public static void Install(Harmony harmony)
        {
            var isEnemy = AccessTools.Method(typeof(BaseAI), "IsEnemy", new[] { typeof(Character), typeof(Character) });
            if (isEnemy != null) harmony.Patch(isEnemy, postfix: new HarmonyMethod(typeof(Crime), nameof(IsEnemy_Postfix)));
            else Plugin.Log.LogWarning("Crime: BaseAI.IsEnemy not found");
            var start = AccessTools.Method(typeof(Game), "Start");
            if (start != null) harmony.Patch(start, postfix: new HarmonyMethod(typeof(Crime), nameof(GameStart_Postfix)));
        }

        /// <summary>A player who would not come quietly and a guard of that settlement are enemies (their blows land both ways).</summary>
        static void IsEnemy_Postfix(Character a, Character b, ref bool __result)
        {
            if (__result || a == null || b == null) return;
            var (player, other) = a is Player pa ? (pa, b) : b is Player pb ? (pb, a) : (null, null);
            if (player == null) return;
            var npc = TownsfolkPatches.NpcOf(other);
            if (npc != null && npc.IsGuard && HostileTo(player) == npc.Location) __result = true;
        }

        static void GameStart_Postfix() => ZRoutedRpc.instance?.Register(JailRpc, sender => PassToEight());

        // ── the arrest ──────────────────────────────────────────────────────────────────────

        /// <summary>The guard's words (TalkPanel): "Thou art under arrest!" "Wilt thou come quietly?"</summary>
        public static string Challenge => Plugin.U5.Ovl.CStr(SArrest) + Plugin.U5.Ovl.CStr(SComeQuietly).TrimEnd(':', '\n');

        /// <summary>The answer to the guard.</summary>
        public static void Answer(bool quietly, TownNpc guard)
        {
            var me = Player.m_localPlayer;
            if (me == null) return;
            var ovl = Plugin.U5.Ovl;
            if (!quietly)
            {
                // raise_alarm: the guards attack
                Set(me, WantedKey, 0);
                Set(me, HostileKey, guard.Location);
                TalkPanel.Tell(ovl.CStr(SDefend));
                Plugin.Log.LogInfo($"{guard.Home?.Location.Name}: {me.GetPlayerName()} will not come quietly");
                return;
            }
            Clear(me);
            TalkPanel.Tell(ovl.CStr(SStruck));
            Plugin.Instance.StartCoroutine(Jail(me));
        }

        /// <summary>
        /// A minute of fighting and the rogue still stands: a guard appears beside them (as the
        /// ballistas' crews do), strikes them unconscious, and they wake in the jail of Yew.
        /// </summary>
        static IEnumerator Overpowered(Player me, Settlements.Built town)
        {
            Clear(me);
            var ovl = Plugin.U5.Ovl;
            var at = me.transform.position - me.transform.forward * 1.2f;
            var face = Quaternion.LookRotation(Vector3.ProjectOnPlane(me.transform.position - at, Vector3.up)).eulerAngles.y;
            var captor = TownsfolkSpawner.SpawnCrew(town, at, face, null);
            Plugin.Log.LogInfo($"{town.Location.Name}: a guard appears and takes {me.GetPlayerName()} after a minute's fight");
            me.Message(MessageHud.MessageType.Center, ovl.CStr(SArrest).Trim());
            yield return new WaitForSeconds(1.5f);
            me.Message(MessageHud.MessageType.Center, ovl.CStr(SStruck).Trim());
            yield return Jail(me);
            if (captor != null && captor.GetComponent<ZNetView>()?.GetZDO() != null && captor.IsOwned) ZNetScene.instance.Destroy(captor.gameObject);
        }

        /// <summary>Struck unconscious; awakening in Yew's jail at 8 o'clock.</summary>
        static IEnumerator Jail(Player me)
        {
            yield return new WaitForSeconds(2f);
            TalkPanel.Close();
            var yew = Settlements.Get(JailLocation);
            if (yew == null)
            {
                me.Message(MessageHud.MessageType.Center, "(Yew is not built in this world: the guards let thee go.)");
                yield break;
            }
            var (x, y, z) = yew.Plan.TilePos(0, JailY * SettlementPlan.Size + JailX);
            me.TeleportTo(yew.Origin + new Vector3(x, y + 0.2f, z), Quaternion.Euler(0, 180f, 0), true);
            if (ZNet.instance.IsServer()) PassToEight();
            else ZRoutedRpc.instance.InvokeRoutedRPC(ZNet.instance.GetServerPeer()?.m_uid ?? 0L, JailRpc);
            Plugin.Log.LogInfo($"{me.GetPlayerName()} is put in the jail of Yew");
            yield return new WaitForSeconds(4f);
            me.Message(MessageHud.MessageType.Center, Plugin.U5.Ovl.CStr(SAwaken).Trim());
        }

        /// <summary>Time passes until 8 o'clock (on the server, as sleeping does: quickly, not at once).</summary>
        static void PassToEight()
        {
            var env = EnvMan.instance;
            if (env == null || ZNet.instance == null) return;
            double now = ZNet.instance.GetTimeSeconds(), day = env.m_dayLengthSec;
            // Valheim's day runs faster at night: 8 o'clock (a third of Britannia's day) is this far into its clock
            double eight = 0.15 + (8 / 24.0 - 0.25) / 0.5 * 0.7;
            double to = Math.Floor(now / day) * day + eight * day;
            if (to <= now) to += day;
            var t = Traverse.Create(env);
            t.Field("m_skipTime").SetValue(true);
            t.Field("m_skipToTime").SetValue(to);
            t.Field("m_timeSkipSpeed").SetValue((to - now) / 12.0);
        }

        /// <summary>The local player's side: the guard's reach brings the question; leaving the settlement or dying ends it all.</summary>
        public class Watch : MonoBehaviour
        {
            float checkAt, hostileSince = -1f;
            bool taking;

            void Update()
            {
                if (Time.time < checkAt) return;
                checkAt = Time.time + 0.25f;
                var me = Player.m_localPlayer;
                if (me == null || Britannia.Active == null) return;
                int wanted = WantedBy(me), hostile = HostileTo(me);
                if (hostile == 0) hostileSince = -1f;
                else if (hostileSince < 0) hostileSince = Time.time;
                if (wanted == 0 && hostile == 0) return;
                var town = Settlements.Get(wanted != 0 ? wanted : hostile);
                if (me.IsDead() || town == null || OutOf(town, me.transform.position))
                {
                    Clear(me);
                    return;
                }
                if (hostile != 0 && Time.time - hostileSince >= FightFor && !taking)
                {
                    taking = true;
                    StartCoroutine(Take(me, town));
                    return;
                }
                if (wanted == 0 || TalkPanel.IsOpen) return;
                foreach (var c in Character.GetAllCharacters())
                {
                    var npc = TownsfolkPatches.NpcOf(c);
                    if (npc == null || !npc.IsGuard || npc.IsDead || npc.Location != wanted) continue;
                    if (Vector3.Distance(c.transform.position, me.transform.position) > Reach) continue;
                    TalkPanel.OpenArrest(npc);
                    return;
                }
            }

            IEnumerator Take(Player me, Settlements.Built town)
            {
                yield return Overpowered(me, town);
                taking = false;
            }

            static bool OutOf(Settlements.Built town, Vector3 p)
                => Mathf.Max(Mathf.Abs(p.x - town.Origin.x), Mathf.Abs(p.z - town.Origin.z)) > town.Site.Half + town.Site.Blend + 10f;
        }
    }
}
