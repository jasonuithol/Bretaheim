using System.Collections;
using System.Linq;
using U5InValheim.WorldGen;
using U5InValheim.WorldGen.Core;
using UnityEngine;

namespace U5InValheim.DevTools
{
    /// <summary>
    /// Development self-test on a dedicated server (which generates no zones until a player
    /// joins): spawns Britain's townsfolk once the world is up, logs who they are and what they
    /// wear, and deletes them again (with no player near, the server unloads them at once; how
    /// they walk is tested offline, TownsfolkTests).
    /// </summary>
    public class TownsfolkSelfTest : MonoBehaviour
    {
        static bool holding;

        void Awake()
        {
            var remove = HarmonyLib.AccessTools.Method(typeof(ZNetScene), "RemoveObjects");
            if (remove != null) new HarmonyLib.Harmony(Plugin.PluginGUID + ".selftest").Patch(remove, prefix: new HarmonyLib.HarmonyMethod(typeof(TownsfolkSelfTest), nameof(Hold)));
        }

        static bool Hold() => !holding;

        IEnumerator Start()
        {
            while (ZNet.instance == null || !ZNet.instance.IsDedicated() || ZNetScene.instance == null || Settlements.Get(2) == null || EnvMan.instance == null)
                yield return new WaitForSeconds(1f);
            yield return new WaitForSeconds(5f);
            var b = Settlements.Get(2);
            TownsfolkSpawner.Spawn(b, ZoneSystem.SpawnMode.Full, null);
            var folk = FindObjectsByType<TownNpc>(FindObjectsSortMode.None).OrderBy(n => n.GetHoverName()).ToList();
            var zdos = folk.Select(n => n.GetComponent<ZNetView>().GetZDO()).ToList();
            Plugin.Log.LogInfo($"townsfolk self-test hour {TownNpc.Hour()}: {folk.Count} people: " +
                string.Join("; ", folk.Select(n => { var p = n.transform.position - b.Origin; return $"{n.GetHoverName()} ({p.x:0.0},{p.y:0.0},{p.z:0.0})"; })));
            foreach (var n in folk.Where(f => f.GetHoverName() == "Eb" || f.GetHoverName() == "Guard" || f.GetHoverName() == "Annon").Take(3))
            {
                var zdo = n.GetComponent<ZNetView>().GetZDO();
                Plugin.Log.LogInfo($"  {n.GetHoverName()}: model {zdo.GetInt(ZDOVars.s_modelIndex)} chest {Name(zdo.GetInt(ZDOVars.s_chestItem))} legs {Name(zdo.GetInt(ZDOVars.s_legItem))} helmet {Name(zdo.GetInt(ZDOVars.s_helmetItem))} right {Name(zdo.GetInt(ZDOVars.s_rightItem))} left {Name(zdo.GetInt(ZDOVars.s_leftItem))} hair {Name(zdo.GetInt(ZDOVars.s_hairItem))} beard {Name(zdo.GetInt(ZDOVars.s_beardItem))} scale {n.transform.localScale.x:0.00}" +
                    $" layer {LayerMask.LayerToName(n.gameObject.layer)} components {string.Join(",", n.GetComponents<Component>().Select(c => c.GetType().Name))}");
            }
            // Iolo's Bows' stock as the trade window gets it
            var keeper = folk.FirstOrDefault(f => f.GetHoverName() == "Armourer");
            var trader = keeper?.ShopTrader();
            Plugin.Log.LogInfo($"townsfolk self-test shop: {(trader == null ? "none" : string.Join("; ", trader.m_items.Select(t => $"{t.m_prefab?.name} x{t.m_stack} {t.m_price} gp tooltip {(t.m_tooltip == null ? "NULL" : "ok")} effects {(t.m_buyPlayerEffects == null ? "NULL" : "ok")}")))}");
            // the town's alarm: a monster on the street beside a villager, away from the guards
            holding = true; // (a headless server unloads everything with no player near: not during the test)
            yield return Alarm(b, folk, "Greyling", 12, false, zdos);
            yield return Alarm(b, folk, "Troll", 40, false, zdos);
            yield return Alarm(b, folk, "Greyling", 25, true, zdos);
            // North Britanny has no guards: its villager runs to Lord British's castle for help
            var north = Settlements.Get(20);
            if (north != null)
            {
                TownsfolkSpawner.Spawn(north, ZoneSystem.SpawnMode.Full, null);
                var northFolk = FindObjectsByType<TownNpc>(FindObjectsSortMode.None).Where(n => n.Location == 20).ToList();
                zdos.AddRange(northFolk.Select(n => n.GetComponent<ZNetView>().GetZDO()));
                yield return Alarm(north, northFolk, "Greyling", 40, false, zdos);
            }
            holding = false;
            yield return null;
            foreach (var zdo in zdos) if (zdo != null && zdo.IsValid()) ZDOMan.instance.DestroyZDO(zdo);
            Plugin.Log.LogInfo($"townsfolk self-test: removed {zdos.Count} again");
        }


        /// <summary>A monster on the street beside a villager (away from the guards), and what the town does, second by second.</summary>
        IEnumerator Alarm(Settlements.Built b, System.Collections.Generic.List<TownNpc> folk, string monster, int seconds, bool guardsDown, System.Collections.Generic.List<ZDO> zdos)
        {
            var prefab = ZNetScene.instance.GetPrefab(monster);
            var guards = folk.Where(f => f != null && f.IsGuard).ToList();
            var villager = folk.Where(f => f != null && !f.IsGuard && f.Floor == 0)
                .OrderByDescending(f => guards.Count == 0 ? 0 : guards.Min(g => Vector3.Distance(g.transform.position, f.transform.position))).FirstOrDefault();
            if (prefab == null || villager == null) { Plugin.Log.LogWarning($"townsfolk self-test alarm: no {monster} or villager in {b.Location.Name}"); yield break; }
            var vl = villager.transform.position - b.Origin;
            int vc = b.Plan.CellAt(vl.x, vl.z);
            int spot = Enumerable.Range(0, 1024).Where(c => b.Plan.Walk[0][c] && Mathf.Max(Mathf.Abs(c % 32 - vc % 32), Mathf.Abs(c / 32 - vc / 32)) is int d && d >= 2 && d <= 3).FirstOrDefault();
            var (sx, _, sz) = b.Plan.TilePos(0, spot);
            var m = Instantiate(prefab, b.Origin + new Vector3(sx, 0.2f, sz), Quaternion.identity).GetComponent<Character>();
            zdos.Add(m.GetComponent<ZNetView>().GetZDO());
            // no ground is generated here with no player near: keep it from falling through the world
            var body = m.GetComponent<Rigidbody>();
            if (body != null) { body.isKinematic = true; body.useGravity = false; }
            // all the guards down: reinforcements should come
            if (guardsDown) foreach (var g in guards) g.Hurt(100000f);
            float t0 = Time.time;
            m.m_onDeath += () => Plugin.Log.LogInfo($"townsfolk self-test alarm: the {monster} died at {Time.time - t0:0.0}s");
            Plugin.Log.LogInfo($"townsfolk self-test alarm: a {monster} ({m.GetMaxHealth():0} hp) beside {villager.GetHoverName()} in {b.Location.Name}");
            for (int i = 0; i < seconds; i++)
            {
                yield return new WaitForSeconds(1f);
                var all = FindObjectsByType<TownNpc>(FindObjectsSortMode.None).Where(f => f.Location == b.Location.Number).ToList();
                var duties = all.GroupBy(f => f.IsReinforcement ? "Reinforcement " + f.Duty : f.Duty.ToString()).Select(g => $"{g.Key} {g.Count()}");
                var hurt = all.Where(f => f.IsGuard && f.Health < TownAlarm.GuardHealth).Select(f => $"{f.Health:0}");
                Plugin.Log.LogInfo($"townsfolk self-test alarm t={i + 1}s: {string.Join(", ", duties)}; {monster} {(m != null ? $"{m.GetHealth():0} hp" : "gone")}{(hurt.Any() ? $"; guards hurt: {string.Join(" ", hurt)}" : "")}");
                if (m == null && all.All(f => f.Duty == Duty.None)) break;
            }
        }

        static string Name(int hash) => hash == 0 ? "-" : ObjectDB.instance?.GetItemPrefab(hash)?.name ?? ZNetScene.instance?.GetPrefab(hash)?.name ?? hash.ToString();
    }
}
