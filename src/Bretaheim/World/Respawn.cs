using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Bretaheim.Data;
using Bretaheim.WorldGen.Core;
using UnityEngine;
using Avatar = Bretaheim.Data.Avatar;

namespace Bretaheim.WorldGen
{
    /// <summary>
    /// A player who has died comes back in the middle of Lord British's throne room (town_tiles.json
    /// "respawn"), as the original's Avatar wakes before Lord British, not at a bed or the world's
    /// start. Logging in still puts a player where they logged out.
    /// Dying is the original's (Data/Resurrection): nothing carried is lost (no tombstone, no
    /// death marker), but Lord British's raising costs experience by karma (the level that is
    /// left), fills hit points and mana, lifts karma to 75, ends spells, gives 63 rations to one
    /// who has none and lets time pass to 6 o'clock; his scene plays on waking.
    /// </summary>
    public static class Respawn
    {
        /// <summary>Character custom data: died in Britannia, to be raised on spawning (kept if they log out dead).</summary>
        public const string RaiseKey = "u5_raise";
        const string WakeRpc = "U5_WakeAtSix";

        public static void Install(Harmony harmony)
        {
            var find = AccessTools.Method(typeof(Game), "FindSpawnPoint");
            if (find == null) { Plugin.Log.LogWarning("Respawn: Game.FindSpawnPoint not found"); return; }
            harmony.Patch(find, prefix: new HarmonyMethod(typeof(Respawn), nameof(FindSpawnPoint_Prefix)));
            Patch(harmony, typeof(Player), "CreateTombStone", prefix: nameof(CreateTombStone_Prefix));
            Patch(harmony, typeof(Player), "OnDeath", prefix: nameof(OnDeath_Prefix), postfix: nameof(OnDeath_Postfix));
            Patch(harmony, typeof(Player), "OnSpawned", postfix: nameof(OnSpawned_Postfix));
            Patch(harmony, typeof(Game), "Start", postfix: nameof(GameStart_Postfix));
        }

        static void Patch(Harmony h, System.Type t, string m, string prefix = null, string postfix = null)
        {
            var method = AccessTools.Method(t, m);
            if (method == null) { Plugin.Log.LogWarning($"Respawn: {t.Name}.{m} not found"); return; }
            h.Patch(method, prefix: prefix != null ? new HarmonyMethod(typeof(Respawn), prefix) : null,
                postfix: postfix != null ? new HarmonyMethod(typeof(Respawn), postfix) : null);
        }

        static void GameStart_Postfix() => ZRoutedRpc.instance?.Register(WakeRpc, sender => WakeAtSix());

        // ── dying ───────────────────────────────────────────────────────────────────────────

        /// <summary>Everything carried stays carried (and worn stays worn): no tombstone in Britannia.</summary>
        static bool CreateTombStone_Prefix() => Britannia.Active == null;

        static void OnDeath_Prefix(Player __instance)
        {
            if (Britannia.Active != null && __instance == Player.m_localPlayer) __instance.m_customData[RaiseKey] = "1";
        }

        /// <summary>Nothing lies where they fell: Valheim's death marker goes.</summary>
        static void OnDeath_Postfix(Player __instance)
        {
            if (Britannia.Active == null || __instance != Player.m_localPlayer || Minimap.instance == null) return;
            var pins = Traverse.Create(Minimap.instance).Field("m_pins").GetValue<List<Minimap.PinData>>();
            var pin = pins?.FindLast(x => x.m_type == Minimap.PinType.Death && Vector3.Distance(x.m_pos, __instance.transform.position) < 1f);
            if (pin != null) Minimap.instance.RemovePin(pin);
        }

        static void OnSpawned_Postfix(Player __instance)
        {
            if (Britannia.Active == null || __instance != Player.m_localPlayer || !__instance.m_customData.Remove(RaiseKey)) return;
            Raise(__instance);
        }

        /// <summary>Lord British raises the player (resurrection_scene): the numbers at once, then his scene.</summary>
        public static void Raise(Player me)
        {
            int karma = Karma.Get(me.m_customData);
            var a = Avatar.Load(me.m_customData);
            int exp = a.Experience, level = a.Level;
            Resurrection.Raise(a, karma);
            a.Save(me.m_customData);
            me.SetMaxHealth(a.MaxHitPoints * Ultima.Scale, true);
            me.SetHealth(a.MaxHitPoints * Ultima.Scale);
            KarmaDeeds.Change(me, Resurrection.KarmaAfter(karma) - karma, "raised by Lord British");
            // spells end (Valheim took the status effects at death; invisibility is also in the ZDO)
            SpellStatus.Take(me, SpellStatus.Invisible);
            var barley = ObjectDB.instance?.GetItemPrefab(Rations.Item);
            string barleyName = barley?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name;
            if (barley != null && me.GetInventory().CountItems(barleyName) == 0) me.GetInventory().AddItem(barley, Resurrection.Food);
            if (Calendar.Hour != Resurrection.WakeHour)
            {
                if (ZNet.instance.IsServer()) WakeAtSix();
                else ZRoutedRpc.instance.InvokeRoutedRPC(ZNet.instance.GetServerPeer()?.m_uid ?? 0L, WakeRpc);
            }
            Plugin.Log.LogInfo($"{me.GetPlayerName()} raised by Lord British: karma {karma}, experience {exp} -> {a.Experience}, level {level} -> {a.Level}");
            Plugin.Instance.StartCoroutine(Scene(me, karma));
        }

        static IEnumerator Scene(Player me, int karma)
        {
            var karmaDat = ScenePlayer.Read("KARMA.DAT");
            if (karmaDat == null || ScenePlayer.Read("IBM.CH") == null || ScenePlayer.Playing) yield break;
            yield return new WaitForSeconds(0.5f);
            var scene = new ResurrectionScene(Plugin.U5.Ovl, karmaDat, karma);
            yield return ScenePlayer.Play(scene.Run(), scene.View, "Lord British", null, null);
        }

        /// <summary>Time passes until 6 o'clock (on the server, quickly, as sleeping does).</summary>
        static void WakeAtSix()
        {
            var env = EnvMan.instance;
            if (env == null || ZNet.instance == null || env.IsTimeSkipping()) return;
            double now = ZNet.instance.GetTimeSeconds(), to = UltimaCalendar.NextHour(now, env.m_dayLengthSec, Resurrection.WakeHour);
            var t = Traverse.Create(env);
            t.Field("m_skipTime").SetValue(true);
            t.Field("m_skipToTime").SetValue(to);
            t.Field("m_timeSkipSpeed").SetValue((to - now) / 12.0);
            Plugin.Log.LogInfo($"Time passes to {Resurrection.WakeHour} o'clock");
        }

        /// <summary>The throne room's middle (world), or null when its settlement is not built.</summary>
        public static Vector3? Point()
        {
            var r = Settlements.Tiles?.Respawn;
            if (r == null) return null;
            var town = Settlements.Get(r.Value.location);
            if (town == null) return null;
            var (x, y, z) = town.Plan.TilePos(r.Value.z, r.Value.y * SettlementPlan.Size + r.Value.x);
            return town.Origin + new Vector3(x, y + 0.3f, z);
        }

        static bool FindSpawnPoint_Prefix(Game __instance, ref Vector3 point, ref bool usedLogoutPoint, float dt, ref bool __result)
        {
            if (Britannia.Active == null) return true;
            var t = Traverse.Create(__instance);
            if (!t.Field("m_respawnAfterDeath").GetValue<bool>()) return true;
            var at = Point();
            if (at == null) return true;
            // as Valheim waits for a bed's area: the throne room loaded, then there
            float wait = t.Field("m_respawnWait").GetValue<float>() + dt;
            t.Field("m_respawnWait").SetValue(wait);
            usedLogoutPoint = false;
            ZNet.instance.SetReferencePosition(at.Value);
            if (wait > __instance.m_respawnLoadDuration && ZNetScene.instance.IsAreaReady(at.Value))
            {
                point = at.Value;
                __result = true;
                Plugin.Log.LogInfo($"Respawned in Lord British's throne room {at.Value}");
            }
            else
            {
                point = Vector3.zero;
                __result = false;
            }
            return false;
        }
    }
}
